import { Injectable } from '@angular/core';
import { Platform } from '@ionic/angular';
import { PushNotifications, Token, PushNotificationSchema, ActionPerformed } from '@capacitor/push-notifications';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';
import { ImmobilizationApprovalService } from './immobilization-approval.service';

/**
 * FCM push notifications for the remote stop approval flow.
 *
 * Flow:
 * 1. On app startup (after login), request POST_NOTIFICATIONS permission
 * 2. Register with FCM → get device token
 * 3. POST token to /api/devicetokens so backend can target this device
 * 4. Handle incoming pushes in foreground (app open) and on tap (app background/killed)
 *
 * Pushes carrying `type=immobilization_request` are routed to
 * ImmobilizationApprovalService which renders the ACCEPTER/REFUSER alert.
 * This is what makes the flow work when the phone is locked or the app is killed.
 *
 * Real-time updates while the app is already open continue to arrive via SignalR;
 * FCM is the fallback when the SignalR connection isn't active.
 */
@Injectable({
  providedIn: 'root'
})
export class PushNotificationService {
  private initialized = false;
  private currentToken: string | null = null;

  constructor(
    private platform: Platform,
    private api: ApiService,
    private authService: AuthService,
    private immoApproval: ImmobilizationApprovalService
  ) {}

  async init(): Promise<void> {
    if (this.initialized) return;
    if (!this.platform.is('capacitor')) return; // Only on native

    this.initialized = true;

    // Request permission
    const perm = await PushNotifications.requestPermissions();
    if (perm.receive !== 'granted') {
      console.warn('Push notification permission denied');
      return;
    }

    // Listen for registration
    PushNotifications.addListener('registration', (token: Token) => {
      console.log('FCM token:', token.value);
      this.currentToken = token.value;
      this.registerTokenWithBackend(token.value);
    });

    PushNotifications.addListener('registrationError', (err) => {
      console.error('Push registration error:', err);
    });

    // Notification received while app is in foreground
    PushNotifications.addListener('pushNotificationReceived', (notification: PushNotificationSchema) => {
      console.log('Push received in foreground:', notification);
      // The ImmobilizationApprovalService handles real-time via SignalR,
      // but if this push arrives and SignalR missed it, handle it here
      const data = notification.data;
      if (data?.type === 'immobilization_request') {
        this.immoApproval.handlePushNotification(data);
      }
    });

    // User tapped on a notification (app was in background/closed)
    PushNotifications.addListener('pushNotificationActionPerformed', (action: ActionPerformed) => {
      console.log('Push action performed:', action);
      const data = action.notification.data;
      if (data?.type === 'immobilization_request') {
        // Small delay to let the app fully initialize
        setTimeout(() => {
          this.immoApproval.handlePushNotification(data);
        }, 500);
      }
    });

    // Register with FCM
    await PushNotifications.register();
  }

  private registerTokenWithBackend(token: string): void {
    const platform = this.platform.is('android') ? 'android' : 'ios';
    this.api.registerDeviceToken(token, platform).subscribe({
      next: () => console.log('Device token registered with backend'),
      error: (err) => console.error('Failed to register device token:', err)
    });
  }

  async unregister(): Promise<void> {
    if (this.currentToken) {
      this.api.unregisterDeviceToken(this.currentToken).subscribe();
      this.currentToken = null;
    }
  }
}
