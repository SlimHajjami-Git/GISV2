import { Injectable, NgZone } from '@angular/core';
import { AlertController, ToastController } from '@ionic/angular';
import { Subscription } from 'rxjs';
import { SignalRService, SignalRNotification } from './signalr.service';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';

@Injectable({
  providedIn: 'root'
})
export class ImmobilizationApprovalService {
  private sub: Subscription | null = null;
  private initialized = false;
  private alertSound: HTMLAudioElement | null = null;

  constructor(
    private signalr: SignalRService,
    private api: ApiService,
    private authService: AuthService,
    private alertCtrl: AlertController,
    private toastCtrl: ToastController,
    private ngZone: NgZone
  ) {}

  init(): void {
    if (this.initialized) return;
    this.initialized = true;

    // Preload alert sound
    try {
      this.alertSound = new Audio('assets/sounds/alert.mp3');
      this.alertSound.load();
    } catch (_) {}

    this.sub = this.signalr.notification$.subscribe(notification => {
      this.ngZone.run(() => this.handleNotification(notification));
    });
  }

  destroy(): void {
    this.sub?.unsubscribe();
    this.sub = null;
    this.initialized = false;
  }

  private handleNotification(notification: SignalRNotification): void {
    if (notification.type === 'immobilization_request') {
      this.handleApprovalRequest(notification);
    } else if (notification.type === 'immobilization_response') {
      this.handleApprovalResponse(notification);
    }
  }

  private async handleApprovalRequest(notification: SignalRNotification): Promise<void> {
    // DO NOT re-check user.isCompanyAdmin here.
    //
    // The backend already gates this notification: `immobilization_request`
    // is only pushed to the SignalR group `user_{userId}` of users that pass
    // `Role.IsCompanyAdmin == true && Status == "active"` on the server
    // (see GpsController.ExecuteImmobilizationAsync → admins.Where(u => u.IsCompanyAdmin)).
    //
    // Re-checking here against the cached `AuthUser.isCompanyAdmin` produced
    // a silent-drop bug for any admin who was **promoted after their last login**:
    // their cached user object stayed at `isCompanyAdmin: false` until the next
    // token refresh cycle, so the SignalR notif arrived (visible in the bell),
    // the DB recorded `IsSent = true`, but the ACCEPTER / REFUSER popup never
    // showed. Observed in prod for fatma@gmail.com, sami@belive.tn, Nejem@belive.tn
    // on 2026-04-21 while admin@belive.tn (admin since creation) worked fine.
    //
    // Single source of truth = backend. If the notif got routed here, show it.
    const user = this.authService.getCurrentUserSync();
    if (!user) {
      console.warn('[ImmoApproval] No logged-in user at notif arrival — skipping');
      return;
    }
    console.log('[ImmoApproval] immobilization_request received', {
      notificationId: notification.id,
      vehicleName: notification.metadata?.vehicleName,
      requestedBy: notification.metadata?.requestedByName,
    });

    const meta = notification.metadata || {};
    const commandType = meta.commandType || 'STOP';
    const vehicleName = meta.vehicleName || 'Véhicule';
    const requestedByName = meta.requestedByName || 'Un utilisateur';
    const requestId = notification.id;
    const isStop = commandType === 'STOP';

    // Play alert sound
    this.playAlertSound();

    const alert = await this.alertCtrl.create({
      header: isStop ? '⛔ DEMANDE D\'ARRÊT' : '✅ DEMANDE DE LIBÉRATION',
      message: isStop
        ? `${requestedByName} demande l'ARRÊT du véhicule "${vehicleName}"`
        : `${requestedByName} demande la LIBÉRATION du véhicule "${vehicleName}"`,
      cssClass: 'immobilization-alert',
      backdropDismiss: false,
      buttons: [
        {
          text: 'Refuser',
          role: 'cancel',
          handler: () => { this.rejectRequest(requestId, vehicleName); }
        },
        {
          text: 'ACCEPTER',
          cssClass: 'alert-danger-btn',
          handler: () => { this.approveRequest(requestId, vehicleName); }
        }
      ]
    });
    await alert.present();
  }

  private async approveRequest(requestId: number, vehicleName: string): Promise<void> {
    this.api.approveImmobilization(requestId).subscribe({
      next: async () => {
        const toast = await this.toastCtrl.create({
          message: `Demande approuvée pour ${vehicleName}`,
          duration: 3000, color: 'success', position: 'top'
        });
        await toast.present();
      },
      error: async (err) => {
        const msg = err?.error?.message || 'Erreur lors de l\'approbation';
        const toast = await this.toastCtrl.create({
          message: msg, duration: 3000, color: 'danger', position: 'top'
        });
        await toast.present();
      }
    });
  }

  private async rejectRequest(requestId: number, vehicleName: string): Promise<void> {
    this.api.rejectImmobilization(requestId).subscribe({
      next: async () => {
        const toast = await this.toastCtrl.create({
          message: `Demande refusée pour ${vehicleName}`,
          duration: 3000, color: 'warning', position: 'top'
        });
        await toast.present();
      },
      error: async () => {
        const toast = await this.toastCtrl.create({
          message: 'Erreur lors du refus', duration: 3000, color: 'danger', position: 'top'
        });
        await toast.present();
      }
    });
  }

  private async handleApprovalResponse(notification: SignalRNotification): Promise<void> {
    const meta = notification.metadata || {};
    const status = meta.status;
    const vehicleName = meta.vehicleName || 'Véhicule';

    if (status === 'approved') {
      this.playAlertSound();
      const toast = await this.toastCtrl.create({
        message: `Votre demande pour "${vehicleName}" a été approuvée`,
        duration: 4000, color: 'success', position: 'top'
      });
      await toast.present();
    } else if (status === 'rejected') {
      const toast = await this.toastCtrl.create({
        message: `Votre demande pour "${vehicleName}" a été refusée`,
        duration: 4000, color: 'danger', position: 'top'
      });
      await toast.present();
    }
  }

  // Called from PushNotificationService when a push arrives (foreground or tap from background)
  handlePushNotification(data: any): void {
    this.ngZone.run(() => {
      const notification = {
        id: parseInt(data.notificationId || '0'),
        type: data.type,
        title: data.title || '',
        message: data.message || '',
        priority: data.priority || 'normal',
        metadata: data,
        createdAt: new Date().toISOString()
      };
      this.handleNotification(notification as any);
    });
  }

  private playAlertSound(): void {
    try {
      if (this.alertSound) {
        this.alertSound.currentTime = 0;
        this.alertSound.play().catch(() => {});
      }
    } catch (_) {}
  }
}
