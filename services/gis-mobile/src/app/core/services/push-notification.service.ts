import { Injectable } from '@angular/core';
import { Platform } from '@ionic/angular';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';
import { ImmobilizationApprovalService } from './immobilization-approval.service';

/**
 * Push notifications are currently DISABLED on Android.
 *
 * `@capacitor/push-notifications`' `register()` throws a native
 * `IllegalStateException` ("Default FirebaseApp is not initialized")
 * when google-services.json / the Firebase Android SDK are missing,
 * and the exception escapes the Capacitor bridge as a FATAL EXCEPTION
 * that kills the whole process. A JS try/catch can't catch that.
 *
 * Real-time events work via SignalR while the app is open. FCM is
 * only needed for background / app-closed pushes, which we'll wire
 * up once Firebase is configured in android/app/build.gradle.
 *
 * This stub keeps the service class + callers compiling while the
 * native crash is neutralised.
 */
@Injectable({
  providedIn: 'root'
})
export class PushNotificationService {
  private initialized = false;

  constructor(
    private platform: Platform,
    private api: ApiService,
    private authService: AuthService,
    private immoApproval: ImmobilizationApprovalService
  ) {}

  async init(): Promise<void> {
    if (this.initialized) return;
    this.initialized = true;
    if (this.platform.is('capacitor')) {
      console.warn('Push notifications disabled (Firebase not configured)');
    }
  }

  async unregister(): Promise<void> {
    // No-op while push notifications are disabled
  }
}
