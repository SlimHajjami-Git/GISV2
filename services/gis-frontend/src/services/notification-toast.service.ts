import { Injectable } from '@angular/core';
import { Subscription } from 'rxjs';
import { ToastService } from './toast.service';
import { NotificationService } from './notification.service';
import { SignalRService, SignalRNotification, GeofenceEvent, GpsAlert } from './signalr.service';

/**
 * Bridge service that connects real-time SignalR events to toast notifications.
 * Handles: NewNotification, GeofenceEvent, Alert (driving behavior).
 * Initialized once at App root level.
 */
@Injectable({
  providedIn: 'root'
})
export class NotificationToastService {
  private subscriptions: Subscription[] = [];
  private initialized = false;

  constructor(
    private toast: ToastService,
    private notificationService: NotificationService,
    private signalR: SignalRService
  ) {}

  initialize(): void {
    if (this.initialized) return;
    this.initialized = true;

    // 1. Toast on every new SignalR notification (geofence, driving behavior, admin action, speed, maintenance)
    this.subscriptions.push(
      this.notificationService.newNotification$.subscribe(notification => {
        this.showNotificationToast(notification);
      })
    );

    // 2. Toast on geofence events (entry/exit) from SignalR direct broadcast
    this.subscriptions.push(
      this.signalR.geofenceEvent$.subscribe(event => {
        this.showGeofenceToast(event);
      })
    );

    // 3. Toast on GPS alerts (driving behavior, overspeed, SOS, etc.)
    this.subscriptions.push(
      this.signalR.alert$.subscribe(alert => {
        this.showAlertToast(alert);
      })
    );
  }

  destroy(): void {
    this.subscriptions.forEach(s => s.unsubscribe());
    this.subscriptions = [];
    this.initialized = false;
  }

  private showNotificationToast(notification: SignalRNotification): void {
    const type = this.mapNotificationType(notification.type, notification.priority);
    this.toast[type](notification.title, notification.message, this.getDuration(notification.priority));
  }

  private showGeofenceToast(event: GeofenceEvent): void {
    const vehicleName = event.vehicleName || `Véhicule #${event.vehicleId}`;
    const zoneName = event.geofenceName || `Zone #${event.geofenceId}`;
    const dateStr = event.timestamp
      ? new Date(event.timestamp).toLocaleString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' })
      : '';
    const dateSuffix = dateStr ? ` le ${dateStr}` : '';

    if (event.eventType === 'entry') {
      this.toast.info(
        `Géofence — ${vehicleName}`,
        `${vehicleName} est entré dans la zone "${zoneName}"${dateSuffix}`,
        6000
      );
    } else if (event.eventType === 'exit') {
      this.toast.warning(
        `Géofence — ${vehicleName}`,
        `${vehicleName} a quitté la zone "${zoneName}"${dateSuffix}`,
        6000
      );
    } else if (event.eventType === 'speed_violation') {
      this.toast.error(
        `Géofence — ${vehicleName}`,
        `${vehicleName} — ${event.speed ?? 0} km/h dans la zone "${zoneName}"${dateSuffix}`,
        8000
      );
    }
  }

  private showAlertToast(alert: GpsAlert): void {
    const vehicleName = alert.vehicleName || `Véhicule #${alert.vehicleId}`;
    switch (alert.type) {
      case 'overspeed':
        this.toast.error('Excès de vitesse', `${vehicleName} — vitesse excessive détectée`, 7000);
        break;
      case 'harsh_braking':
        this.toast.warning('Freinage brusque', `${vehicleName} — freinage brusque détecté`, 6000);
        break;
      case 'rapid_acceleration':
        this.toast.warning('Accélération rapide', `${vehicleName} — accélération rapide`, 5000);
        break;
      case 'sharp_turn':
        this.toast.warning('Virage brusque', `${vehicleName} — virage brusque détecté`, 5000);
        break;
      case 'sos':
        this.toast.error('Alerte SOS', `${vehicleName} — signal SOS reçu!`, 10000);
        break;
      case 'battery_low':
        this.toast.info('Batterie faible', `${vehicleName} — batterie GPS faible`, 5000);
        break;
      case 'jerk':
      case 'pothole':
        this.toast.info('Choc détecté', `${vehicleName} — choc/nid-de-poule détecté`, 5000);
        break;
      default:
        this.toast.info('Alerte véhicule', `${vehicleName} — ${alert.type}`, 5000);
        break;
    }
  }

  private mapNotificationType(type: string, priority: string): 'success' | 'error' | 'warning' | 'info' {
    // Map by notification type first
    if (type === 'driving_behavior' || type === 'speed_alert') return 'warning';
    if (type === 'geofence') return 'info';
    if (type === 'admin_action') return 'info';
    if (type === 'maintenance_due') return 'warning';

    // Fall back to priority
    if (priority === 'high' || priority === 'urgent') return 'error';
    if (priority === 'normal') return 'info';
    return 'info';
  }

  private getDuration(priority: string): number {
    if (priority === 'high' || priority === 'urgent') return 8000;
    if (priority === 'normal') return 6000;
    return 5000;
  }
}
