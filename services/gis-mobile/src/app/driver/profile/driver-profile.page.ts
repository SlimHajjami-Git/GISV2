import { Component, NgZone, OnDestroy, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { AlertController } from '@ionic/angular';
import { Subscription } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { AuthService, AuthUser } from '../../core/services/auth.service';
import { PushNotificationService } from '../../core/services/push-notification.service';
import { TourTrackingService, TrackingState } from '../../core/services/tour-tracking.service';
import { DriverDeclarationsService } from '../../core/services/driver-declarations.service';
import { DriverMe } from '../../core/models/driver-app.types';
import { environment } from '../../../environments/environment';

export const APP_VERSION = '1.2.0';

@Component({
  selector: 'app-driver-profile',
  standalone: false,
  template: `
    <ion-header>
      <ion-toolbar color="primary">
        <ion-buttons slot="start">
          <ion-back-button defaultHref="/driver/tours" text=""></ion-back-button>
        </ion-buttons>
        <ion-title>Mon profil</ion-title>
      </ion-toolbar>
    </ion-header>

    <ion-content>
      <div class="head">
        <div class="avatar"><ion-icon name="person"></ion-icon></div>
        <h2>{{ me ? (me.firstName + ' ' + me.lastName) : user?.name }}</h2>
        <p>{{ user?.email }}</p>
        <ion-chip color="light"><ion-icon name="car-outline"></ion-icon><ion-label>Chauffeur</ion-label></ion-chip>
      </div>

      <ion-card class="card">
        <ion-card-content>
          <div class="line"><ion-icon name="business-outline" color="primary"></ion-icon><span>{{ me?.company?.name || user?.companyName || '-' }}</span></div>
          <div class="line">
            <ion-icon name="car-sport-outline" color="primary"></ion-icon>
            <span *ngIf="me?.assignedVehicle as v">{{ v.name }} <small>({{ v.plate }})</small>
              <ion-badge [color]="v.hasGps ? 'success' : 'medium'">{{ v.hasGps ? 'Boîtier GPS' : 'Sans boîtier' }}</ion-badge></span>
            <span *ngIf="me && !me.assignedVehicle">Aucun véhicule affecté</span>
            <span *ngIf="!me && !loadError">Chargement…</span>
            <span *ngIf="loadError" class="err">{{ loadError }}</span>
          </div>
        </ion-card-content>
      </ion-card>

      <ion-card class="card">
        <ion-card-content>
          <div class="line">
            <ion-icon [name]="tracking ? 'radio-outline' : 'radio-button-off-outline'" [color]="tracking ? 'success' : 'medium'"></ion-icon>
            <span *ngIf="tracking">Suivi actif (mode {{ tracking.mode === 'eco' ? 'économique' : 'complet' }}), {{ trackingService.pendingCount }} point(s) en attente</span>
            <span *ngIf="!tracking">Aucun suivi en cours</span>
          </div>
          <div class="line" *ngIf="pendingDeclarations > 0">
            <ion-icon name="cloud-offline-outline" color="warning"></ion-icon>
            <span>{{ pendingDeclarations }} déclaration(s) en attente de réseau</span>
          </div>
        </ion-card-content>
      </ion-card>

      <ion-list class="actions">
        <ion-item button detail (click)="openPrivacyPolicy()">
          <ion-icon name="shield-checkmark-outline" slot="start" color="primary"></ion-icon>
          <ion-label>Politique de confidentialité</ion-label>
        </ion-item>
      </ion-list>

      <div class="logout">
        <ion-button expand="block" color="danger" fill="outline" shape="round" size="large" (click)="confirmLogout()">
          <ion-icon name="log-out-outline" slot="start"></ion-icon>
          Se déconnecter
        </ion-button>
      </div>
      <div class="version">Calypso v{{ version }}</div>
    </ion-content>
  `,
  styles: [`
    .head { text-align:center; padding:28px 20px 20px; background: linear-gradient(135deg, var(--ion-color-primary), var(--ion-color-primary-shade)); color:#fff; }
    .avatar { width:76px; height:76px; border-radius:50%; background:rgba(255,255,255,.2); display:flex; align-items:center; justify-content:center; margin:0 auto 10px; }
    .avatar ion-icon { font-size:38px; }
    .head h2 { margin:0 0 4px; font-size:21px; font-weight:700; }
    .head p { margin:0 0 8px; opacity:.85; font-size:14px; }
    .card { margin:14px; border-radius:16px; }
    .line { display:flex; align-items:center; gap:10px; padding:7px 0; font-size:15px; }
    .line ion-icon { font-size:20px; flex-shrink:0; }
    .line ion-badge { margin-left:6px; vertical-align:middle; }
    .err { color: var(--ion-color-danger); }
    .actions { margin:14px; border-radius:16px; overflow:hidden; }
    .logout { padding:20px 16px 6px; }
    .version { text-align:center; padding:12px; font-size:12px; color: var(--ion-color-medium); }
  `]
})
export class DriverProfilePage implements OnInit, OnDestroy {
  user: AuthUser | null = null;
  me: DriverMe | null = null;
  loadError: string | null = null;
  tracking: TrackingState | null = null;
  pendingDeclarations = 0;
  version = APP_VERSION;
  private subs = new Subscription();

  constructor(
    private api: ApiService,
    private authService: AuthService,
    private push: PushNotificationService,
    public trackingService: TourTrackingService,
    private declarations: DriverDeclarationsService,
    private router: Router,
    private alertCtrl: AlertController,
    private zone: NgZone
  ) {}

  ngOnInit() {
    this.user = this.authService.getCurrentUserSync();
    this.subs.add(this.trackingService.state$.subscribe(s => this.zone.run(() => { this.tracking = s; })));
    this.subs.add(this.declarations.pendingCount$.subscribe(n => this.zone.run(() => { this.pendingDeclarations = n; })));
    this.pendingDeclarations = this.declarations.pendingCount;
    this.api.getDriverMe().subscribe({
      next: (me) => { this.me = me; },
      error: (err) => { this.loadError = err?.error?.message || (err?.status === 0 ? 'Hors ligne' : 'Profil indisponible'); }
    });
  }

  ngOnDestroy() {
    this.subs.unsubscribe();
  }

  openPrivacyPolicy() {
    const baseUrl = environment.apiUrl.replace(/\/api\/?$/, '');
    window.open(`${baseUrl}/politique-de-confidentialite`, '_system');
  }

  async confirmLogout() {
    const alert = await this.alertCtrl.create({
      header: 'Déconnexion',
      message: this.tracking
        ? 'Une tournée est en cours de suivi : la déconnexion arrête la transmission de votre position. Continuer ?'
        : 'Voulez-vous vraiment vous déconnecter ?',
      buttons: [
        { text: 'Annuler', role: 'cancel' },
        { text: 'Déconnecter', role: 'destructive', handler: () => { this.doLogout(); } }
      ]
    });
    await alert.present();
  }

  /**
   * Ordre important : dernier envoi des positions et désinscription du jeton FCM
   * pendant que le jeton d'accès existe encore, puis effacement de la session.
   */
  async doLogout() {
    await this.trackingService.stop({ flush: true });
    await this.push.unregister();
    await this.authService.logout();
    this.router.navigate(['/login'], { replaceUrl: true });
  }
}
