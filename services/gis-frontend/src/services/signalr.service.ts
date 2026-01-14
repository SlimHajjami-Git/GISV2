import { Injectable, OnDestroy } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject, BehaviorSubject, Subscription, timer } from 'rxjs';

export interface PositionUpdate {
  deviceId: number;
  deviceUid: string;
  vehicleId: number;
  vehicleName: string;
  plate: string;
  latitude: number;
  longitude: number;
  speedKph: number;
  courseDeg: number;
  ignitionOn: boolean;
  recordedAt: string;
  timestamp: string;
}

export interface GpsAlert {
  deviceId: number;
  vehicleId: number;
  vehicleName: string;
  type: string;
  latitude: number;
  longitude: number;
  timestamp: string;
}

export interface GeofenceEvent {
  geofenceId: number;
  vehicleId: number;
  vehicleName: string;
  eventType: 'entry' | 'exit' | 'speed_violation';
  latitude: number;
  longitude: number;
  timestamp: string;
  speed?: number;
}

export type ConnectionState = 'Disconnected' | 'Connecting' | 'Connected' | 'Reconnecting' | 'Error';

@Injectable({
  providedIn: 'root'
})
export class SignalRService implements OnDestroy {
  private hubConnection: signalR.HubConnection | null = null;
  private connectionState = new BehaviorSubject<ConnectionState>('Disconnected');
  private reconnectAttempts = 0;
  private readonly maxReconnectAttempts = 10;
  private reconnectSubscription: Subscription | null = null;
  private subscribedVehicles = new Set<number>();

  public positionUpdate$ = new Subject<PositionUpdate>();
  public alert$ = new Subject<GpsAlert>();
  public geofenceEvent$ = new Subject<GeofenceEvent>();
  public connectionState$ = this.connectionState.asObservable();

  constructor() {}

  ngOnDestroy(): void {
    this.cleanup();
  }

  private cleanup(): void {
    this.reconnectSubscription?.unsubscribe();
    this.positionUpdate$.complete();
    this.alert$.complete();
    this.geofenceEvent$.complete();
    this.connectionState.complete();
  }

  async startConnection(): Promise<void> {
    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      return;
    }

    if (this.hubConnection?.state === signalR.HubConnectionState.Connecting) {
      return;
    }

    const token = localStorage.getItem('auth_token');
    if (!token) {
      console.warn('SignalR: No authentication token available');
      this.connectionState.next('Error');
      return;
    }

    this.connectionState.next('Connecting');
    const hubUrl = '/api/hubs/gps';

    try {
      this.hubConnection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl, {
          accessTokenFactory: () => localStorage.getItem('auth_token') || '',
          skipNegotiation: true,
          transport: signalR.HttpTransportType.WebSockets
        })
        .withAutomaticReconnect({
          nextRetryDelayInMilliseconds: (retryContext) => {
            if (retryContext.previousRetryCount >= this.maxReconnectAttempts) {
              return null;
            }
            const delays = [0, 1000, 2000, 5000, 10000, 15000, 30000];
            const index = Math.min(retryContext.previousRetryCount, delays.length - 1);
            return delays[index];
          }
        })
        .configureLogging(signalR.LogLevel.Warning)
        .build();

      this.setupConnectionHandlers();
      this.registerEventHandlers();

      await this.hubConnection.start();
      this.connectionState.next('Connected');
      this.reconnectAttempts = 0;

      await this.resubscribeToVehicles();
    } catch (err) {
      console.error('SignalR connection failed:', err);
      this.connectionState.next('Error');
      this.scheduleReconnect();
    }
  }

  private setupConnectionHandlers(): void {
    if (!this.hubConnection) return;

    this.hubConnection.onreconnecting((error) => {
      console.warn('SignalR reconnecting...', error?.message);
      this.connectionState.next('Reconnecting');
    });

    this.hubConnection.onreconnected((connectionId) => {
      console.log('SignalR reconnected:', connectionId);
      this.connectionState.next('Connected');
      this.reconnectAttempts = 0;
      this.resubscribeToVehicles();
    });

    this.hubConnection.onclose((error) => {
      console.warn('SignalR connection closed:', error?.message);
      this.connectionState.next('Disconnected');

      if (error) {
        this.scheduleReconnect();
      }
    });
  }

  private scheduleReconnect(): void {
    if (this.reconnectAttempts >= this.maxReconnectAttempts) {
      console.error('SignalR: Max reconnection attempts reached');
      return;
    }

    this.reconnectSubscription?.unsubscribe();

    const delay = Math.min(5000 * Math.pow(2, this.reconnectAttempts), 60000);
    this.reconnectAttempts++;

    console.log(`SignalR: Scheduling reconnect attempt ${this.reconnectAttempts} in ${delay}ms`);

    this.reconnectSubscription = timer(delay).subscribe(() => {
      this.startConnection();
    });
  }

  private registerEventHandlers(): void {
    if (!this.hubConnection) return;

    this.hubConnection.on('PositionUpdate', (position: PositionUpdate) => {
      this.positionUpdate$.next(position);
    });

    this.hubConnection.on('VehiclePosition', (position: PositionUpdate) => {
      this.positionUpdate$.next(position);
    });

    this.hubConnection.on('Alert', (alert: GpsAlert) => {
      this.alert$.next(alert);
    });

    this.hubConnection.on('GeofenceEvent', (event: GeofenceEvent) => {
      this.geofenceEvent$.next(event);
    });
  }

  private async resubscribeToVehicles(): Promise<void> {
    if (!this.isConnected()) return;

    for (const vehicleId of this.subscribedVehicles) {
      try {
        await this.hubConnection!.invoke('SubscribeToVehicle', vehicleId);
      } catch (err) {
        console.error(`Failed to resubscribe to vehicle ${vehicleId}:`, err);
      }
    }
  }

  async subscribeToVehicle(vehicleId: number): Promise<boolean> {
    if (!this.isConnected()) {
      this.subscribedVehicles.add(vehicleId);
      return false;
    }

    try {
      await this.hubConnection!.invoke('SubscribeToVehicle', vehicleId);
      this.subscribedVehicles.add(vehicleId);
      return true;
    } catch (err) {
      console.error(`Failed to subscribe to vehicle ${vehicleId}:`, err);
      return false;
    }
  }

  async unsubscribeFromVehicle(vehicleId: number): Promise<boolean> {
    this.subscribedVehicles.delete(vehicleId);

    if (!this.isConnected()) {
      return false;
    }

    try {
      await this.hubConnection!.invoke('UnsubscribeFromVehicle', vehicleId);
      return true;
    } catch (err) {
      console.error(`Failed to unsubscribe from vehicle ${vehicleId}:`, err);
      return false;
    }
  }

  async stopConnection(): Promise<void> {
    this.reconnectSubscription?.unsubscribe();
    this.subscribedVehicles.clear();

    if (this.hubConnection) {
      try {
        await this.hubConnection.stop();
      } catch (err) {
        console.warn('Error stopping SignalR connection:', err);
      }
      this.hubConnection = null;
    }

    this.connectionState.next('Disconnected');
  }

  isConnected(): boolean {
    return this.hubConnection?.state === signalR.HubConnectionState.Connected;
  }

  getConnectionState(): ConnectionState {
    return this.connectionState.value;
  }
}
