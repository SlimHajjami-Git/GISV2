import { Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject, BehaviorSubject } from 'rxjs';

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

@Injectable({
  providedIn: 'root'
})
export class SignalRService {
  private hubConnection: signalR.HubConnection | null = null;
  private connectionState = new BehaviorSubject<string>('Disconnected');
  
  // Observables for real-time updates
  public positionUpdate$ = new Subject<PositionUpdate>();
  public alert$ = new Subject<GpsAlert>();
  public connectionState$ = this.connectionState.asObservable();

  constructor() {}

  async startConnection(): Promise<void> {
    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      console.log('SignalR already connected');
      return;
    }

    const token = localStorage.getItem('token');
    if (!token) {
      console.warn('No token found, cannot connect to SignalR');
      return;
    }

    // Build hub URL - use relative path for proxy
    const hubUrl = '/api/hubs/gps';

    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => token,
        skipNegotiation: true,
        transport: signalR.HttpTransportType.WebSockets
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Information)
      .build();

    // Register event handlers
    this.registerEventHandlers();

    // Connection state handlers
    this.hubConnection.onreconnecting(() => {
      console.log('SignalR reconnecting...');
      this.connectionState.next('Reconnecting');
    });

    this.hubConnection.onreconnected(() => {
      console.log('SignalR reconnected');
      this.connectionState.next('Connected');
    });

    this.hubConnection.onclose(() => {
      console.log('SignalR connection closed');
      this.connectionState.next('Disconnected');
    });

    try {
      await this.hubConnection.start();
      console.log('SignalR connected successfully');
      this.connectionState.next('Connected');
    } catch (err) {
      console.error('SignalR connection error:', err);
      this.connectionState.next('Error');
      // Retry after 5 seconds
      setTimeout(() => this.startConnection(), 5000);
    }
  }

  private registerEventHandlers(): void {
    if (!this.hubConnection) return;

    // Position updates from company group
    this.hubConnection.on('PositionUpdate', (position: PositionUpdate) => {
      console.log('Received position update:', position);
      this.positionUpdate$.next(position);
    });

    // Vehicle-specific position updates
    this.hubConnection.on('VehiclePosition', (position: PositionUpdate) => {
      console.log('Received vehicle position:', position);
      this.positionUpdate$.next(position);
    });

    // Alerts
    this.hubConnection.on('Alert', (alert: GpsAlert) => {
      console.log('Received alert:', alert);
      this.alert$.next(alert);
    });

    // Geofence events
    this.hubConnection.on('GeofenceEvent', (event: any) => {
      console.log('Received geofence event:', event);
    });
  }

  async subscribeToVehicle(vehicleId: number): Promise<void> {
    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      await this.hubConnection.invoke('SubscribeToVehicle', vehicleId);
      console.log(`Subscribed to vehicle ${vehicleId}`);
    }
  }

  async unsubscribeFromVehicle(vehicleId: number): Promise<void> {
    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      await this.hubConnection.invoke('UnsubscribeFromVehicle', vehicleId);
      console.log(`Unsubscribed from vehicle ${vehicleId}`);
    }
  }

  async stopConnection(): Promise<void> {
    if (this.hubConnection) {
      await this.hubConnection.stop();
      this.connectionState.next('Disconnected');
      console.log('SignalR disconnected');
    }
  }

  isConnected(): boolean {
    return this.hubConnection?.state === signalR.HubConnectionState.Connected;
  }
}
