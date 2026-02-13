import { Component, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { AlertController, ToastController } from '@ionic/angular';
import { AuthService, AuthUser } from '../../core/services/auth.service';
import { SignalRService } from '../../core/services/signalr.service';

@Component({
  selector: 'app-profile',
  standalone: false,
  template: `
    <ion-header>
      <ion-toolbar color="primary">
        <ion-buttons slot="start">
          <ion-back-button defaultHref="/tabs/dashboard"></ion-back-button>
        </ion-buttons>
        <ion-title>Mon Profil</ion-title>
      </ion-toolbar>
    </ion-header>

    <ion-content [fullscreen]="true">
      <!-- Profile header -->
      <div class="profile-header">
        <div class="avatar">
          <ion-icon name="person"></ion-icon>
        </div>
        <h2>{{ user?.name }}</h2>
        <p>{{ user?.email }}</p>
        <ion-chip color="primary" *ngIf="user?.isCompanyAdmin">
          <ion-icon name="shield-checkmark"></ion-icon>
          <ion-label>Admin</ion-label>
        </ion-chip>
      </div>

      <!-- Company info -->
      <ion-card class="info-card">
        <ion-card-header>
          <ion-card-subtitle>Entreprise</ion-card-subtitle>
        </ion-card-header>
        <ion-card-content>
          <div class="info-row">
            <ion-icon name="business-outline" color="primary"></ion-icon>
            <span>{{ user?.companyName || '-' }}</span>
          </div>
          <div class="info-row" *ngIf="user?.phone">
            <ion-icon name="call-outline" color="primary"></ion-icon>
            <span>{{ user?.phone }}</span>
          </div>
          <div class="info-row">
            <ion-icon name="ribbon-outline" color="primary"></ion-icon>
            <span>Rôle: {{ user?.roles?.[0] || '-' }}</span>
          </div>
        </ion-card-content>
      </ion-card>

      <!-- Connection status -->
      <ion-card class="info-card">
        <ion-card-header>
          <ion-card-subtitle>Connexion temps réel</ion-card-subtitle>
        </ion-card-header>
        <ion-card-content>
          <div class="info-row">
            <ion-icon [name]="isConnected ? 'wifi' : 'wifi-outline'" [color]="isConnected ? 'success' : 'danger'"></ion-icon>
            <span>{{ isConnected ? 'Connecté (SignalR)' : 'Déconnecté' }}</span>
          </div>
        </ion-card-content>
      </ion-card>

      <!-- Actions -->
      <ion-list class="actions-list">
        <ion-item button (click)="reconnectSignalR()" detail>
          <ion-icon name="reload-outline" slot="start" color="primary"></ion-icon>
          <ion-label>Reconnecter le temps réel</ion-label>
        </ion-item>
        <ion-item button (click)="showAbout()" detail>
          <ion-icon name="information-circle-outline" slot="start" color="primary"></ion-icon>
          <ion-label>À propos</ion-label>
        </ion-item>
      </ion-list>

      <!-- Logout -->
      <div class="logout-section">
        <ion-button expand="block" color="danger" fill="outline" shape="round" (click)="confirmLogout()">
          <ion-icon name="log-out-outline" slot="start"></ion-icon>
          Se déconnecter
        </ion-button>
      </div>

      <div class="version-text">
        GIS Fleet Mobile v1.0.0
      </div>
    </ion-content>
  `,
  styles: [`
    .profile-header {
      text-align: center;
      padding: 32px 20px 24px;
      background: linear-gradient(135deg, var(--ion-color-primary), var(--ion-color-primary-shade));
      color: #fff;
    }
    .avatar {
      width: 80px; height: 80px;
      border-radius: 50%;
      background: rgba(255,255,255,0.2);
      display: flex; align-items: center; justify-content: center;
      margin: 0 auto 12px;
    }
    .avatar ion-icon { font-size: 40px; }
    .profile-header h2 { margin: 0 0 4px; font-size: 20px; font-weight: 700; }
    .profile-header p { margin: 0 0 8px; opacity: 0.8; font-size: 14px; }
    .info-card { margin: 16px; border-radius: 16px; }
    .info-row {
      display: flex; align-items: center; gap: 10px;
      padding: 6px 0; font-size: 14px;
    }
    .info-row ion-icon { font-size: 18px; flex-shrink: 0; }
    .actions-list { margin: 16px; border-radius: 16px; overflow: hidden; }
    .logout-section { padding: 24px 16px 8px; }
    .version-text {
      text-align: center; padding: 16px;
      font-size: 12px; color: var(--ion-color-medium);
    }
  `]
})
export class ProfilePage implements OnInit {
  user: AuthUser | null = null;
  isConnected = false;

  constructor(
    private authService: AuthService,
    private signalr: SignalRService,
    private router: Router,
    private alertCtrl: AlertController,
    private toastCtrl: ToastController
  ) {}

  ngOnInit() {
    this.user = this.authService.getCurrentUserSync();
    this.signalr.connectionState$.subscribe(state => {
      this.isConnected = state === 'Connected';
    });
  }

  async reconnectSignalR() {
    await this.signalr.stopConnection();
    await this.signalr.startConnection();
    const toast = await this.toastCtrl.create({
      message: 'Reconnexion en cours...',
      duration: 2000,
      position: 'top',
      icon: 'reload-outline'
    });
    await toast.present();
  }

  async showAbout() {
    const alert = await this.alertCtrl.create({
      header: 'GIS Fleet Mobile',
      message: `
        <p><strong>Version:</strong> 1.0.0</p>
        <p><strong>Plateforme:</strong> Ionic + Angular + Capacitor</p>
        <p><strong>Backend:</strong> .NET 8 API</p>
        <p><strong>GPS:</strong> Temps réel via SignalR</p>
        <p>&copy; 2026 Belive</p>
      `,
      buttons: ['OK']
    });
    await alert.present();
  }

  async confirmLogout() {
    const alert = await this.alertCtrl.create({
      header: 'Déconnexion',
      message: 'Voulez-vous vraiment vous déconnecter ?',
      buttons: [
        { text: 'Annuler', role: 'cancel' },
        {
          text: 'Déconnecter',
          role: 'destructive',
          handler: () => this.doLogout()
        }
      ]
    });
    await alert.present();
  }

  async doLogout() {
    await this.signalr.stopConnection();
    await this.authService.logout();
    this.router.navigate(['/login'], { replaceUrl: true });
  }
}
