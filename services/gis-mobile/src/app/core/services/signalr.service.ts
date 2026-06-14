import { Injectable, OnDestroy } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject, BehaviorSubject, Subscription, timer } from 'rxjs';
import { AuthService } from './auth.service';
import { environment } from '../../../environments/environment';

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
  isMoving: boolean;
  recordedAt: string;
  timestamp: string;
  // Returned by the REST /gps/positions/latest payload; absent on the live
  // SignalR broadcast (hence optional) — used to enrich the detail sheet.
  address?: string;
  driverName?: string;
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

export interface SignalRNotification {
  id: number;
  type: string;
  title: string;
  message: string;
  priority: string;
  referenceType?: string;
  referenceId?: number;
  actionUrl?: string;
  metadata?: any;
  createdAt: string;
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
  public notification$ = new Subject<SignalRNotification>();
  public unreadCount$ = new BehaviorSubject<number>(0);
  public connectionState$ = this.connectionState.asObservable();

  /** Last error encountered (for on-device diagnostics) */
  public lastError: string | null = null;
  /** Last URL used for the connection (for on-device diagnostics) */
  public lastUrl: string | null = null;

  constructor(private authService: AuthService) {}

  ngOnDestroy(): void {
    this.cleanup();
  }

  private cleanup(): void {
    this.reconnectSubscription?.unsubscribe();
    this.positionUpdate$.complete();
    this.alert$.complete();
    this.geofenceEvent$.complete();
    this.notification$.complete();
    this.unreadCount$.complete();
    this.connectionState.complete();
  }

  async startConnection(): Promise<void> {
    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      return;
    }
    if (this.hubConnection?.state === signalR.HubConnectionState.Connecting) {
      return;
    }

    const token = this.authService.getToken();
    if (!token) {
      console.warn('SignalR: No authentication token available');
      this.lastError = 'No auth token available when starting connection';
      this.connectionState.next('Error');
      return;
    }

    this.connectionState.next('Connecting');
    this.lastUrl = this.authService.getSignalrUrl();
    this.lastError = null;

    try {
      this.hubConnection = new signalR.HubConnectionBuilder()
        .withUrl(this.authService.getSignalrUrl(), {
          accessTokenFactory: () => this.authService.getToken() || '',
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
      this.lastError = null;
      await this.resubscribeToVehicles();
    } catch (err: any) {
      console.error('SignalR connection failed:', err);
      this.lastError = err?.message || String(err);
      this.connectionState.next('Error');
      this.scheduleReconnect();
    }
  }

  private setupConnectionHandlers(): void {
    if (!this.hubConnection) return;

    this.hubConnection.onreconnecting(() => {
      this.connectionState.next('Reconnecting');
    });

    this.hubConnection.onreconnected(() => {
      this.connectionState.next('Connected');
      this.reconnectAttempts = 0;
      this.resubscribeToVehicles();
    });

    this.hubConnection.onclose((error) => {
      this.connectionState.next('Disconnected');
      if (error) {
        this.lastError = (error as any)?.message || String(error);
        this.scheduleReconnect();
      }
    });
  }

  private scheduleReconnect(): void {
    if (this.reconnectAttempts >= this.maxReconnectAttempts) return;

    this.reconnectSubscription?.unsubscribe();
    const delay = Math.min(5000 * Math.pow(2, this.reconnectAttempts), 60000);
    this.reconnectAttempts++;

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

    this.hubConnection.on('NewNotification', (notification: SignalRNotification) => {
      this.notification$.next(notification);
    });

    this.hubConnection.on('UnreadCountChanged', (data: { count: number }) => {
      this.unreadCount$.next(data.count);
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
    this.subscribedVehicles.add(vehicleId);
    if (!this.isConnected()) return false;
    try {
      await this.hubConnection!.invoke('SubscribeToVehicle', vehicleId);
      return true;
    } catch (err) {
      console.error(`Failed to subscribe to vehicle ${vehicleId}:`, err);
      return false;
    }
  }

  async unsubscribeFromVehicle(vehicleId: number): Promise<boolean> {
    this.subscribedVehicles.delete(vehicleId);
    if (!this.isConnected()) return false;
    try {
      await this.hubConnection!.invoke('UnsubscribeFromVehicle', vehicleId);
      return true;
    } catch (err) {
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
        console.warn('Error stopping SignalR:', err);
      }
      this.hubConnection = null;
    }
    this.connectionState.next('Disconnected');
  }

  isConnected(): boolean {
    return this.hubConnection?.state === signalR.HubConnectionState.Connected;
  }
}
