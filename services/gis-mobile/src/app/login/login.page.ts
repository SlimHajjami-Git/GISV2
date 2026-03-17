import { Component } from '@angular/core';
import { Router } from '@angular/router';
import { LoadingController, ToastController, AlertController } from '@ionic/angular';
import { AuthService } from '../core/services/auth.service';

@Component({
  selector: 'app-login',
  standalone: false,
  template: `
    <ion-content class="login-content" [fullscreen]="true">
      <div class="login-container">
        <!-- Logo & Branding -->
        <div class="logo-section">
          <div class="logo-icon">
            <img src="assets/icon/calypso-logo.png" alt="Calypso" class="calypso-logo"
                 onerror="this.src='assets/icon/calypso-logo.svg'" />
          </div>
          <h1>Calypso</h1>
          <p class="subtitle">Gestion de Flotte Intelligente</p>
        </div>

        <!-- Login Form -->
        <div class="form-section">
          <ion-item lines="none" class="input-item">
            <ion-icon name="mail-outline" slot="start" color="medium"></ion-icon>
            <ion-input
              type="email"
              placeholder="Email"
              [(ngModel)]="email"
              (keyup.enter)="onLogin()"
              autocapitalize="off"
              autocomplete="email"
            ></ion-input>
          </ion-item>

          <ion-item lines="none" class="input-item">
            <ion-icon name="lock-closed-outline" slot="start" color="medium"></ion-icon>
            <ion-input
              [type]="showPassword ? 'text' : 'password'"
              placeholder="Mot de passe"
              [(ngModel)]="password"
              (keyup.enter)="onLogin()"
            ></ion-input>
            <ion-button fill="clear" slot="end" (click)="showPassword = !showPassword">
              <ion-icon [name]="showPassword ? 'eye-off-outline' : 'eye-outline'" color="medium"></ion-icon>
            </ion-button>
          </ion-item>

          <ion-button
            expand="block"
            (click)="onLogin()"
            [disabled]="!email || !password"
            class="login-btn"
            shape="round"
          >
            <ion-icon name="log-in-outline" slot="start"></ion-icon>
            Se connecter
          </ion-button>

          <div class="server-config" *ngIf="showServerConfig">
            <ion-item lines="none" class="input-item">
              <ion-icon name="server-outline" slot="start" color="medium"></ion-icon>
              <ion-input
                type="url"
                placeholder="URL du serveur (ex: http://192.168.1.10:5000)"
                [(ngModel)]="serverUrl"
              ></ion-input>
            </ion-item>
            <ion-button expand="block" fill="outline" size="small" (click)="saveServerUrl()" shape="round">
              Enregistrer
            </ion-button>
          </div>

          <ion-button fill="clear" size="small" (click)="showServerConfig = !showServerConfig" class="config-toggle">
            <ion-icon name="settings-outline" slot="start"></ion-icon>
            Configuration serveur
          </ion-button>
        </div>

        <!-- Footer -->
        <div class="footer-section">
          <p>Belive &copy; 2026</p>
        </div>
      </div>
    </ion-content>
  `,
  styles: [`
    .login-content {
      --background: linear-gradient(135deg, #1a1a2e 0%, #16213e 50%, #0f3460 100%);
    }
    .login-container {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      min-height: 100%;
      padding: 24px;
    }
    .logo-section {
      text-align: center;
      margin-bottom: 40px;
    }
    .calypso-logo {
      width: 120px;
      height: 120px;
      object-fit: contain;
    }
    .logo-section h1 {
      color: #fff;
      font-size: 28px;
      font-weight: 700;
      margin: 12px 0 4px;
    }
    .subtitle {
      color: rgba(255,255,255,0.6);
      font-size: 14px;
      margin: 0;
    }
    .form-section {
      width: 100%;
      max-width: 400px;
    }
    .input-item {
      --background: rgba(255,255,255,0.08);
      --border-radius: 12px;
      --color: #fff;
      --placeholder-color: rgba(255,255,255,0.4);
      margin-bottom: 12px;
      border-radius: 12px;
    }
    .input-item ion-icon[slot="start"] {
      margin-right: 8px;
    }
    .login-btn {
      margin-top: 16px;
      --border-radius: 12px;
      height: 48px;
      font-weight: 600;
    }
    .config-toggle {
      display: block;
      margin-top: 16px;
      --color: rgba(255,255,255,0.5);
      font-size: 12px;
    }
    .server-config {
      margin-top: 12px;
      padding: 12px;
      background: rgba(255,255,255,0.05);
      border-radius: 12px;
    }
    .server-config ion-button {
      margin-top: 8px;
    }
    .footer-section {
      margin-top: 40px;
      color: rgba(255,255,255,0.3);
      font-size: 12px;
    }
  `]
})
export class LoginPage {
  email = '';
  password = '';
  showPassword = false;
  showServerConfig = false;
  serverUrl = '';

  constructor(
    private authService: AuthService,
    private router: Router,
    private loadingCtrl: LoadingController,
    private toastCtrl: ToastController,
    private alertCtrl: AlertController
  ) {
    this.authService.getServerUrl().then(url => this.serverUrl = url);
  }

  async onLogin() {
    if (!this.email || !this.password) return;

    const loading = await this.loadingCtrl.create({
      message: 'Connexion...',
      spinner: 'crescent'
    });
    await loading.present();

    this.authService.login(this.email, this.password).subscribe({
      next: async (user) => {
        await loading.dismiss();
        if (user) {
          this.router.navigate(['/tabs/dashboard'], { replaceUrl: true });
        } else {
          const toast = await this.toastCtrl.create({
            message: 'Email ou mot de passe incorrect',
            duration: 3000,
            color: 'danger',
            position: 'top',
            icon: 'alert-circle-outline'
          });
          await toast.present();
        }
      },
      error: async (err) => {
        await loading.dismiss();
        const toast = await this.toastCtrl.create({
          message: 'Erreur de connexion au serveur',
          duration: 3000,
          color: 'danger',
          position: 'top',
          icon: 'cloud-offline-outline'
        });
        await toast.present();
      }
    });
  }

  async saveServerUrl() {
    if (this.serverUrl) {
      await this.authService.setServerUrl(this.serverUrl);
      const toast = await this.toastCtrl.create({
        message: `Serveur configuré: ${this.serverUrl}`,
        duration: 2000,
        color: 'success',
        position: 'top',
        icon: 'checkmark-circle-outline'
      });
      await toast.present();
      this.showServerConfig = false;
    }
  }
}
