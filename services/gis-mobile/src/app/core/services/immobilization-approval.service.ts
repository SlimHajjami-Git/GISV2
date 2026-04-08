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
    const user = this.authService.getCurrentUserSync();
    if (!user || parseInt(user.id) !== 1) return;

    const meta = notification.metadata || {};
    const commandType = meta.commandType || 'STOP';
    const vehicleName = meta.vehicleName || 'Véhicule';
    const requestedByName = meta.requestedByName || 'Un utilisateur';
    const requestId = notification.id;
    const isStop = commandType === 'STOP';

    // Play alert sound
    this.playAlertSound();

    const alert = await this.alertCtrl.create({
      header: isStop ? 'DEMANDE D\'ARRÊT' : 'DEMANDE DE LIBÉRATION',
      message: isStop
        ? `<strong>${requestedByName}</strong> demande l'<strong style="color:#dc2626">ARRÊT</strong> du véhicule <strong>"${vehicleName}"</strong>`
        : `<strong>${requestedByName}</strong> demande la <strong style="color:#16a34a">LIBÉRATION</strong> du véhicule <strong>"${vehicleName}"</strong>`,
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

  private playAlertSound(): void {
    try {
      if (this.alertSound) {
        this.alertSound.currentTime = 0;
        this.alertSound.play().catch(() => {});
      }
    } catch (_) {}
  }
}
