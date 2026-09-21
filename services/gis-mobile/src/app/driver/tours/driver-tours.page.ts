import { Component, NgZone, OnDestroy, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { PushNotificationService } from '../../core/services/push-notification.service';
import { TourTrackingService, TrackingState } from '../../core/services/tour-tracking.service';
import { DriverDeclarationsService } from '../../core/services/driver-declarations.service';
import { DRIVER_ERR_NO_PROFILE, DriverTourSummary } from '../../core/models/driver-app.types';

type Scope = 'active' | 'history';

/** Libellés et couleurs des statuts de tournée, partagés par la liste et la fiche. */
export const TOUR_STATUS_LABELS: Record<string, { label: string; color: string }> = {
  planned: { label: 'Planifiée', color: 'medium' },
  in_progress: { label: 'En cours', color: 'success' },
  completed: { label: 'Terminée', color: 'primary' },
  cancelled: { label: 'Annulée', color: 'danger' }
};

@Component({
  selector: 'app-driver-tours',
  standalone: false,
  template: `
    <ion-header>
      <ion-toolbar color="primary">
        <ion-title>Mes tournées</ion-title>
        <ion-buttons slot="end">
          <ion-button (click)="openProfile()" aria-label="Mon profil">
            <ion-icon slot="icon-only" name="person-circle-outline"></ion-icon>
          </ion-button>
        </ion-buttons>
      </ion-toolbar>
      <ion-toolbar>
        <ion-segment [value]="scope" (ionChange)="onScopeChange($event)">
          <ion-segment-button value="active"><ion-label>En cours</ion-label></ion-segment-button>
          <ion-segment-button value="history"><ion-label>Historique</ion-label></ion-segment-button>
        </ion-segment>
      </ion-toolbar>
    </ion-header>

    <ion-content>
      <ion-refresher slot="fixed" (ionRefresh)="refresh($event)">
        <ion-refresher-content pullingText="Tirer pour actualiser" refreshingText="Actualisation…"></ion-refresher-content>
      </ion-refresher>

      <div class="banner tracking" *ngIf="tracking" (click)="openTour(tracking.tourId)">
        <ion-icon name="radio-outline"></ion-icon>
        <span>Suivi actif — votre position est transmise pendant la tournée</span>
        <ion-icon name="chevron-forward-outline"></ion-icon>
      </div>
      <div class="banner offline" *ngIf="pendingDeclarations > 0">
        <ion-icon name="cloud-offline-outline"></ion-icon>
        <span>{{ pendingDeclarations }} déclaration(s) en attente de réseau</span>
      </div>

      <div class="state" *ngIf="loading && tours.length === 0">
        <ion-spinner name="crescent"></ion-spinner>
      </div>

      <div class="state" *ngIf="!loading && error">
        <ion-icon name="alert-circle-outline" color="danger"></ion-icon>
        <p>{{ error }}</p>
        <ion-button fill="outline" size="default" (click)="load()">Réessayer</ion-button>
      </div>

      <div class="state" *ngIf="!loading && !error && tours.length === 0">
        <ion-icon name="map-outline" color="medium"></ion-icon>
        <p *ngIf="scope === 'active'">Aucune tournée en cours ou à venir.<br>Vous serez prévenu dès qu'une tournée vous sera envoyée.</p>
        <p *ngIf="scope === 'history'">Aucune tournée terminée ces 7 derniers jours.</p>
      </div>

      <ion-list lines="none" *ngIf="tours.length > 0">
        <ion-card *ngFor="let t of tours; trackBy: trackById" button (click)="openTour(t.id)" class="tour-card"
                  [class.live]="t.status === 'in_progress'">
          <ion-card-content>
            <div class="row top">
              <h2>{{ t.name }}</h2>
              <ion-badge [color]="statusOf(t.status).color">{{ statusOf(t.status).label }}</ion-badge>
            </div>
            <div class="row meta">
              <ion-icon name="time-outline"></ion-icon>
              <span>Départ {{ t.scheduledStartTime | date:'dd/MM HH:mm' }}</span>
            </div>
            <div class="row meta" *ngIf="t.vehicleName || t.vehiclePlate">
              <ion-icon name="car-outline"></ion-icon>
              <span>{{ t.vehicleName }} <small *ngIf="t.vehiclePlate">({{ t.vehiclePlate }})</small></span>
            </div>
            <div class="row meta" *ngIf="t.origin || t.destination">
              <ion-icon name="navigate-outline"></ion-icon>
              <span class="ellipsis">{{ t.origin || '?' }} → {{ t.destination || '?' }}</span>
            </div>
            <div class="progress">
              <ion-progress-bar [value]="progressOf(t)" [color]="t.status === 'in_progress' ? 'success' : 'medium'"></ion-progress-bar>
              <span>{{ t.completedCount }}/{{ t.waypointCount }} étapes</span>
              <span class="next" *ngIf="t.status === 'in_progress' && t.nextWaypointName">Prochaine : {{ t.nextWaypointName }}</span>
            </div>
          </ion-card-content>
        </ion-card>
      </ion-list>
    </ion-content>
  `,
  styles: [`
    .banner { display:flex; align-items:center; gap:10px; margin:12px 12px 0; padding:12px 14px; border-radius:12px; font-size:14px; font-weight:600; }
    .banner ion-icon { font-size:22px; flex-shrink:0; }
    .banner span { flex:1; }
    .banner.tracking { background: rgba(16,185,129,.15); color:#047857; }
    .banner.offline { background: rgba(245,158,11,.15); color:#92400e; }
    .state { text-align:center; padding:56px 24px; color: var(--ion-color-medium); }
    .state ion-icon { font-size:56px; display:block; margin:0 auto 12px; }
    .state p { font-size:16px; line-height:1.5; }
    .tour-card { margin:12px; border-radius:16px; }
    .tour-card.live { border-left: 6px solid var(--ion-color-success); }
    .row { display:flex; align-items:center; gap:8px; }
    .row.top { justify-content:space-between; margin-bottom:8px; }
    .row.top h2 { margin:0; font-size:19px; font-weight:700; color: var(--ion-text-color); }
    .row.meta { font-size:15px; color: var(--ion-text-color); margin:4px 0; }
    .row.meta ion-icon { font-size:18px; color: var(--ion-color-primary); flex-shrink:0; }
    .ellipsis { overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
    .progress { margin-top:10px; display:flex; flex-wrap:wrap; align-items:center; gap:6px 12px; font-size:13px; color: var(--ion-color-medium); }
    .progress ion-progress-bar { flex-basis:100%; height:6px; border-radius:3px; }
    .progress .next { font-weight:600; color: var(--ion-color-success-shade); }
  `]
})
export class DriverToursPage implements OnInit, OnDestroy {
  scope: Scope = 'active';
  tours: DriverTourSummary[] = [];
  loading = false;
  error: string | null = null;
  tracking: TrackingState | null = null;
  pendingDeclarations = 0;
  private subs = new Subscription();

  constructor(
    private api: ApiService,
    private router: Router,
    private push: PushNotificationService,
    private trackingService: TourTrackingService,
    private declarations: DriverDeclarationsService,
    private zone: NgZone
  ) {}

  ngOnInit() {
    this.subs.add(this.trackingService.state$.subscribe(s => this.zone.run(() => { this.tracking = s; })));
    this.subs.add(this.declarations.pendingCount$.subscribe(n => this.zone.run(() => { this.pendingDeclarations = n; })));
    // Sans SignalR, le push est la seule façon d'apprendre qu'une tournée est arrivée.
    this.subs.add(this.push.tourPush$.subscribe(() => this.load()));
    this.subs.add(this.declarations.replayed$.subscribe(() => this.load()));
    this.pendingDeclarations = this.declarations.pendingCount;
  }

  ngOnDestroy() {
    this.subs.unsubscribe();
  }

  ionViewWillEnter() {
    this.load();
  }

  onScopeChange(ev: any) {
    const v = ev?.detail?.value;
    this.scope = v === 'history' ? 'history' : 'active';
    this.tours = [];
    this.load();
  }

  load() {
    this.loading = true;
    this.error = null;
    const scope = this.scope;
    this.api.getDriverTours(scope).subscribe({
      next: (list) => {
        if (scope !== this.scope) return;
        this.loading = false;
        this.tours = Array.isArray(list) ? list : [];
      },
      error: (err) => {
        this.loading = false;
        this.error = this.describeError(err);
      }
    });
  }

  async refresh(ev: any) {
    await this.declarations.replay();
    this.load();
    setTimeout(() => ev?.target?.complete?.(), 400);
  }

  openTour(id: number) {
    this.router.navigate(['/driver/tours', id]);
  }

  openProfile() {
    this.router.navigate(['/driver/profile']);
  }

  statusOf(status: string) {
    return TOUR_STATUS_LABELS[status] || { label: status, color: 'medium' };
  }

  progressOf(t: DriverTourSummary): number {
    return t.waypointCount > 0 ? t.completedCount / t.waypointCount : 0;
  }

  trackById(_: number, t: DriverTourSummary) {
    return t.id;
  }

  private describeError(err: any): string {
    if (err?.status === 0) return 'Pas de réseau. Vos tournées s\'afficheront dès le retour de la connexion.';
    if (err?.status === 403 && err?.error?.code === DRIVER_ERR_NO_PROFILE) {
      return err.error.message || 'Aucune fiche chauffeur n\'est reliée à ce compte. Demandez à votre gestionnaire.';
    }
    return err?.error?.message || 'Impossible de charger vos tournées.';
  }
}
