import { Component, ElementRef, NgZone, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { ActionSheetController, AlertController, LoadingController, Platform, ToastController } from '@ionic/angular';
import { Subscription, firstValueFrom } from 'rxjs';
import * as L from 'leaflet';
import { ApiService } from '../../core/services/api.service';
import { KvStore } from '../../core/services/kv-store.service';
import { PushNotificationService } from '../../core/services/push-notification.service';
import { LocationConsentService } from '../../core/services/location-consent.service';
import { SENSOR_NOT_AUTHORIZED, TourTrackingService } from '../../core/services/tour-tracking.service';
import { DeclarationKind, DriverDeclarationsService, QueuedDeclaration } from '../../core/services/driver-declarations.service';
import {
  DRIVER_ERR_PENDING_STOPS, DRIVER_ERR_TOO_FAR, DriverTourDetail, DriverWaypoint
} from '../../core/models/driver-app.types';
import { decodePolyline6 } from '../../core/util/polyline';
import { stepActionFor } from '../../core/util/tour-steps';
import { TOUR_STATUS_LABELS } from '../tours/driver-tours.page';

const TOUR_CACHE_PREFIX = 'driver_tour_cache_';

@Component({
  selector: 'app-driver-tour-detail',
  standalone: false,
  template: `
    <ion-header>
      <ion-toolbar color="primary">
        <ion-buttons slot="start">
          <ion-back-button defaultHref="/driver/tours" text=""></ion-back-button>
        </ion-buttons>
        <ion-title>{{ tour?.name || 'Tournée' }}</ion-title>
        <ion-buttons slot="end" *ngIf="tour">
          <ion-badge class="status" [color]="statusOf(tour.status).color">{{ statusOf(tour.status).label }}</ion-badge>
        </ion-buttons>
      </ion-toolbar>
    </ion-header>

    <ion-content>
      <ion-refresher slot="fixed" (ionRefresh)="refresh($event)">
        <ion-refresher-content pullingText="Tirer pour actualiser" refreshingText="Actualisation…"></ion-refresher-content>
      </ion-refresher>

      <div #mapEl class="tour-map"></div>

      <div class="banner offline" *ngIf="fromCache">
        <ion-icon name="cloud-offline-outline"></ion-icon>
        <span>Hors ligne — dernière version connue de la tournée</span>
      </div>
      <div class="banner offline" *ngIf="queued.length > 0">
        <ion-icon name="time-outline"></ion-icon>
        <span>{{ queued.length }} déclaration(s) en attente de réseau : elles partiront toutes seules</span>
      </div>
      <div class="banner tracking" *ngIf="trackingHere && !sensorError">
        <ion-icon name="radio-outline"></ion-icon>
        <span>Suivi actif — votre position est transmise</span>
      </div>
      <div class="banner warn" *ngIf="trackingHere && sensorError" (click)="fixSensor()">
        <ion-icon name="location-outline"></ion-icon>
        <span>{{ sensorMessage }}</span>
      </div>

      <div class="state" *ngIf="loading && !tour"><ion-spinner name="crescent"></ion-spinner></div>
      <div class="state" *ngIf="!loading && error && !tour">
        <ion-icon name="alert-circle-outline" color="danger"></ion-icon>
        <p>{{ error }}</p>
        <ion-button fill="outline" (click)="load()">Réessayer</ion-button>
      </div>

      <ng-container *ngIf="tour">
        <div class="summary">
          <div><ion-icon name="time-outline"></ion-icon> Départ {{ tour.scheduledStartTime | date:'dd/MM HH:mm' }}</div>
          <div *ngIf="tour.vehicleName"><ion-icon name="car-outline"></ion-icon> {{ tour.vehicleName }} <small *ngIf="tour.vehiclePlate">({{ tour.vehiclePlate }})</small></div>
          <div *ngIf="tour.estimatedDistanceKm"><ion-icon name="speedometer-outline"></ion-icon> {{ tour.estimatedDistanceKm | number:'1.0-0' }} km
            <span *ngIf="tour.estimatedDurationMinutes"> · {{ durationLabel(tour.estimatedDurationMinutes) }}</span></div>
          <p class="notes" *ngIf="tour.notes || tour.description">{{ tour.notes || tour.description }}</p>
        </div>

        <div class="steps">
          <div class="step" *ngFor="let wp of tour.waypoints; let i = index; trackBy: trackById"
               [class.done]="wp.isCompleted" [class.skipped]="wp.waypointStatus === 'skipped'"
               [class.current]="actionFor(wp) !== null" [attr.id]="'step-' + wp.id">
            <div class="step-head">
              <div class="num" [class.origin]="wp.type === 'origin'" [class.dest]="wp.type === 'destination'">
                <ion-icon *ngIf="wp.isCompleted" name="checkmark"></ion-icon>
                <span *ngIf="!wp.isCompleted">{{ i + 1 }}</span>
              </div>
              <div class="step-text">
                <h3>{{ wp.name || wp.address || typeLabel(wp.type) }}</h3>
                <p *ngIf="wp.name && wp.address">{{ wp.address }}</p>
                <p class="times">
                  <span *ngIf="wp.estimatedArrivalTime">Prévu {{ wp.estimatedArrivalTime | date:'HH:mm' }}</span>
                  <span *ngIf="wp.actualArrivalTime"> · Arrivé {{ wp.actualArrivalTime | date:'HH:mm' }}</span>
                  <span *ngIf="wp.driverDepartedAt"> · Reparti {{ wp.driverDepartedAt | date:'HH:mm' }}</span>
                  <span *ngIf="wp.plannedPauseMinutes"> · Pause {{ wp.plannedPauseMinutes }} min</span>
                </p>
              </div>
              <ion-badge [color]="stepStatus(wp).color">{{ stepStatus(wp).label }}</ion-badge>
            </div>

            <div class="step-actions" *ngIf="actionFor(wp) as action">
              <ion-button *ngIf="action === 'depart_origin'" expand="block" size="large" color="success"
                          [disabled]="busy" (click)="depart(wp, true)">
                <ion-icon name="play" slot="start"></ion-icon> Je pars
              </ion-button>

              <ng-container *ngIf="action === 'arrive'">
                <ion-button expand="block" size="large" fill="outline" color="primary" (click)="navigateTo(wp)">
                  <ion-icon name="navigate" slot="start"></ion-icon> Naviguer
                </ion-button>
                <ion-button expand="block" size="large" color="primary" [disabled]="busy" (click)="arrive(wp)">
                  <ion-icon name="flag" slot="start"></ion-icon> Je suis arrivé
                </ion-button>
              </ng-container>

              <ion-button *ngIf="action === 'redepart'" expand="block" size="large" color="tertiary"
                          [disabled]="busy" (click)="depart(wp, false)">
                <ion-icon name="play-forward" slot="start"></ion-icon> Je repars
              </ion-button>
            </div>
          </div>
        </div>
      </ng-container>
    </ion-content>
  `,
  styles: [`
    .tour-map { width:100%; height:34vh; min-height:210px; z-index:1; background:#e5e7eb; }
    ion-badge.status { margin-right:10px; font-size:12px; padding:5px 8px; }
    .banner { display:flex; align-items:center; gap:10px; margin:10px 12px 0; padding:11px 14px; border-radius:12px; font-size:14px; font-weight:600; }
    .banner ion-icon { font-size:22px; flex-shrink:0; }
    .banner.tracking { background: rgba(16,185,129,.15); color:#047857; }
    .banner.offline { background: rgba(245,158,11,.15); color:#92400e; }
    .banner.warn { background: rgba(239,68,68,.13); color:#b91c1c; }
    .state { text-align:center; padding:40px 24px; color: var(--ion-color-medium); }
    .state ion-icon { font-size:52px; display:block; margin:0 auto 10px; }
    .summary { padding:12px 16px 4px; font-size:15px; display:grid; gap:6px; }
    .summary ion-icon { vertical-align:-3px; margin-right:4px; color: var(--ion-color-primary); font-size:18px; }
    .notes { margin:4px 0 0; color: var(--ion-color-medium); font-size:14px; white-space:pre-line; }
    .steps { padding:8px 12px 32px; }
    .step { background: var(--ion-card-background, #fff); border-radius:16px; padding:14px; margin:10px 0; box-shadow:0 1px 4px rgba(0,0,0,.08); border-left:6px solid transparent; }
    .step.current { border-left-color: var(--ion-color-primary); }
    .step.done { opacity:.8; }
    .step.skipped { opacity:.55; }
    .step-head { display:flex; align-items:flex-start; gap:12px; }
    .num { width:34px; height:34px; border-radius:50%; background: var(--ion-color-primary); color:#fff; display:flex; align-items:center; justify-content:center; font-weight:700; flex-shrink:0; }
    .num.origin { background:#10b981; } .num.dest { background:#ef4444; }
    .step.done .num { background:#64748b; }
    .step-text { flex:1; min-width:0; }
    .step-text h3 { margin:0; font-size:17px; font-weight:700; }
    .step-text p { margin:2px 0 0; font-size:13px; color: var(--ion-color-medium); }
    .step-text .times { color: var(--ion-text-color); font-size:14px; }
    .step-actions { margin-top:12px; display:grid; gap:8px; }
    .step-actions ion-button { --border-radius:14px; min-height:58px; font-size:18px; font-weight:700; margin:0; }
  `]
})
export class DriverTourDetailPage implements OnInit, OnDestroy {
  @ViewChild('mapEl', { static: true }) mapEl!: ElementRef<HTMLDivElement>;

  tourId = 0;
  tour: DriverTourDetail | null = null;
  loading = false;
  error: string | null = null;
  busy = false;
  fromCache = false;
  queued: QueuedDeclaration[] = [];
  trackingHere = false;
  sensorError: string | null = null;

  get sensorMessage(): string {
    return this.sensorError === SENSOR_NOT_AUTHORIZED
      ? 'Localisation non autorisée : touchez ici pour l\'activer'
      : 'Position du téléphone indisponible pour le moment';
  }

  private map: L.Map | null = null;
  private layers: L.LayerGroup | null = null;
  private subs = new Subscription();
  private openedSent = false;
  private fittedOnce = false;

  constructor(
    private route: ActivatedRoute,
    private api: ApiService,
    private store: KvStore,
    private declarations: DriverDeclarationsService,
    private tracking: TourTrackingService,
    private consent: LocationConsentService,
    private push: PushNotificationService,
    private platform: Platform,
    private alertCtrl: AlertController,
    private toastCtrl: ToastController,
    private loadingCtrl: LoadingController,
    private actionSheetCtrl: ActionSheetController,
    private zone: NgZone
  ) {}

  ngOnInit() {
    this.tourId = parseInt(this.route.snapshot.paramMap.get('id') || '0', 10);

    this.subs.add(this.tracking.state$.subscribe(s => { this.trackingHere = !!s && s.tourId === this.tourId; }));
    this.subs.add(this.tracking.sensorError$.subscribe(e => { this.sensorError = e; }));
    // Pas de SignalR pour un chauffeur : le push et le rejeu de la file sont les seuls signaux.
    this.subs.add(this.push.tourPush$.subscribe(ev => { if (ev.tourId === this.tourId) this.load(); }));
    this.subs.add(this.declarations.replayed$.subscribe(item => { if (item.tourId === this.tourId) this.load(); }));
    this.subs.add(this.declarations.dropped$.subscribe(({ item, message }) => {
      if (item.tourId !== this.tourId) return;
      this.toast(message || 'Une déclaration faite hors ligne a été refusée par le serveur. Refaites-la si besoin.', 'warning', 6000);
      this.load();
    }));
  }

  ngOnDestroy() {
    this.subs.unsubscribe();
    if (this.map) { this.map.remove(); this.map = null; }
  }

  ionViewWillEnter() {
    this.load();
  }

  // Même recette que tabs/monitoring et tabs/playback : Leaflet ne mesure bien son
  // conteneur qu'une fois la page Ionic réellement à l'écran.
  ionViewDidEnter() {
    if (!this.map) {
      setTimeout(() => this.initMap(), 200);
    } else {
      setTimeout(() => this.map?.invalidateSize(), 100);
    }
  }

  // ────────────────── Chargement ──────────────────

  async load() {
    if (!this.tourId) { this.error = 'Tournée introuvable.'; return; }
    this.loading = true;
    this.error = null;
    try {
      const tour = await firstValueFrom(this.api.getDriverTour(this.tourId));
      this.fromCache = false;
      await this.store.set(TOUR_CACHE_PREFIX + this.tourId, tour);
      await this.show(tour);
      this.markOpened(tour);
    } catch (err: any) {
      if (err?.status === 0) {
        const cached = await this.store.get<DriverTourDetail>(TOUR_CACHE_PREFIX + this.tourId);
        if (cached) {
          this.fromCache = true;
          await this.show(cached);
        } else {
          this.error = 'Pas de réseau, et cette tournée n\'a jamais été ouverte sur ce téléphone.';
        }
      } else if (err?.status === 404) {
        this.tour = null;
        this.error = 'Cette tournée n\'est plus disponible (retirée ou réaffectée).';
      } else {
        this.error = err?.error?.message || 'Impossible de charger la tournée.';
      }
    } finally {
      this.loading = false;
    }
  }

  async refresh(ev: any) {
    await this.declarations.replay();
    await this.load();
    ev?.target?.complete?.();
  }

  /** Affiche la tournée, avec par-dessus les gestes encore en file (pas encore connus du serveur). */
  private async show(tour: DriverTourDetail) {
    tour.waypoints = [...(tour.waypoints || [])].sort((a, b) => a.sequenceOrder - b.sequenceOrder);
    this.queued = await this.declarations.pendingFor(this.tourId);
    for (const q of this.queued) this.applyLocally(tour, q.waypointId, q.kind, q.body.clientTime || new Date().toISOString());
    this.tour = tour;
    this.drawMap();
    // Reprise : la tournée est en cours côté serveur mais le suivi ne tourne pas ici
    // (application réinstallée, tournée démarrée par le gestionnaire…).
    // Jamais au-delà des 12 h : le serveur répondrait tracking:false à chaque ouverture.
    const startedMs = tour.actualStartTime ? new Date(tour.actualStartTime).getTime() : NaN;
    const withinWindow = !isNaN(startedMs) && Date.now() - startedMs < TourTrackingService.MAX_DURATION_MS;
    if (!this.fromCache && this.queued.length === 0 && tour.status === 'in_progress' && tour.tracking
        && withinWindow && !this.tracking.isActive && await this.consent.hasAccepted()) {
      this.tracking.start(tour.id, 'full');
    }
  }

  /** POST …/opened à la première ouverture : le gestionnaire voit « Ouverte 07:12 ». */
  private markOpened(tour: DriverTourDetail) {
    if (this.openedSent || tour.openedAt) return;
    this.openedSent = true;
    this.api.markDriverTourOpened(tour.id).subscribe({
      error: () => { this.openedSent = false; }
    });
  }

  // ────────────────── Déclarations ──────────────────

  actionFor(wp: DriverWaypoint) {
    return this.tour ? stepActionFor(this.tour, wp) : null;
  }

  /** « Je pars » (startsTour = origine d'une tournée planifiée) ou « Je repars ». */
  async depart(wp: DriverWaypoint, startsTour: boolean) {
    if (this.busy) return;
    if (startsTour) {
      // Écran d'explication PUIS permission, avant que le suivi ne démarre. Un refus
      // n'empêche pas de partir : la tournée sera suivie par le boîtier seul.
      await this.consent.ensure();
    }
    await this.declare(wp, 'depart', { startsTour });
  }

  async arrive(wp: DriverWaypoint, confirmSkipPending = false) {
    if (this.busy) return;
    await this.declare(wp, 'arrive', { confirmSkipPending });
  }

  private async declare(wp: DriverWaypoint, kind: DeclarationKind, opts: { startsTour?: boolean; confirmSkipPending?: boolean }) {
    this.busy = true;
    const spinner = await this.loadingCtrl.create({ message: 'Envoi…', spinner: 'crescent' });
    await spinner.present();
    try {
      const outcome = await this.declarations.declare(this.tourId, wp.id, kind, {
        withPosition: true,
        startsTour: opts.startsTour,
        confirmSkipPending: opts.confirmSkipPending
      });
      await spinner.dismiss();

      if (outcome.sent) {
        const r = outcome.response;
        if (r.warning) this.toast(r.warning, 'warning', 5000);
        else if (r.tourStatus === 'completed') this.toast('Tournée terminée. Merci !', 'success');
        else this.toast(kind === 'arrive' ? 'Arrivée enregistrée' : 'Départ enregistré', 'success');
        // Le suivi vient peut-être de démarrer sans permission : le bandeau le dira.
        await this.load();
      } else {
        // Hors ligne : le geste est en file avec son heure ; l'écran avance quand même.
        if (this.tour) this.applyLocally(this.tour, wp.id, kind, new Date().toISOString());
        this.queued = await this.declarations.pendingFor(this.tourId);
        this.drawMap();
        this.toast('Pas de réseau : déclaration enregistrée, elle sera envoyée automatiquement.', 'warning', 5000);
      }
    } catch (err: any) {
      await spinner.dismiss();
      await this.handleDeclarationError(err, wp, kind);
    } finally {
      this.busy = false;
    }
  }

  private async handleDeclarationError(err: any, wp: DriverWaypoint, kind: DeclarationKind) {
    const code = err?.error?.code;
    if (err?.status === 409 && code === DRIVER_ERR_PENDING_STOPS) {
      const names: string[] = (err.error.pending || []).map((p: any) => p.name);
      const alert = await this.alertCtrl.create({
        header: 'Étapes non signalées',
        message: `${err.error.message || 'Des étapes n\'ont pas été signalées.'}` +
          (names.length ? `\n\n${names.map(n => '• ' + n).join('\n')}` : ''),
        cssClass: 'driver-alert-multiline',
        buttons: [
          { text: 'Annuler', role: 'cancel' },
          { text: 'Confirmer l\'arrivée', handler: () => { this.arrive(wp, true); } }
        ]
      });
      await alert.present();
      return;
    }
    if (err?.status === 409 && code === DRIVER_ERR_TOO_FAR) {
      const km = typeof err.error.distanceM === 'number' ? ` (${(err.error.distanceM / 1000).toFixed(1)} km)` : '';
      const alert = await this.alertCtrl.create({
        header: 'Trop loin de l\'étape' + km,
        message: err.error.message || 'Rapprochez-vous de l\'étape avant de signaler votre arrivée.',
        buttons: ['Compris']
      });
      await alert.present();
      return;
    }
    this.toast(err?.error?.message || (kind === 'arrive' ? 'Arrivée non enregistrée.' : 'Départ non enregistré.'), 'danger', 5000);
    if (err?.status === 400 || err?.status === 404) this.load();
  }

  /** Reflète un geste sur le modèle local (déclaration en file, serveur pas encore au courant). */
  private applyLocally(tour: DriverTourDetail, waypointId: number, kind: DeclarationKind, at: string) {
    const wp = tour.waypoints.find(w => w.id === waypointId);
    if (!wp) return;
    if (kind === 'depart') {
      if (tour.status === 'planned') {
        tour.status = 'in_progress';
        tour.actualStartTime = at;
        wp.isCompleted = true;
        wp.waypointStatus = 'completed';
        wp.actualArrivalTime = wp.actualArrivalTime || at;
      }
      wp.driverDepartedAt = at;
    } else {
      wp.isCompleted = true;
      wp.waypointStatus = 'completed';
      wp.actualArrivalTime = wp.actualArrivalTime || at;
      wp.driverArrivedAt = at;
      if (wp.type === 'destination') { tour.status = 'completed'; tour.actualEndTime = at; }
    }
  }

  /** Bandeau rouge touché : refaire le parcours explication → permission, puis relancer le capteur. */
  async fixSensor() {
    if (await this.consent.ensure()) await this.tracking.restartSensor();
  }

  // ────────────────── Navigation ──────────────────

  /**
   * Guidage vers l'étape. Sans plugin pour savoir quelles applications sont installées,
   * le chauffeur choisit : Google Maps (google.navigation:) ou Waze (lien universel).
   */
  async navigateTo(wp: DriverWaypoint) {
    const ll = `${wp.latitude},${wp.longitude}`;
    const google = this.platform.is('android')
      ? `google.navigation:q=${ll}`
      : `https://www.google.com/maps/dir/?api=1&destination=${ll}&travelmode=driving`;
    const waze = `https://waze.com/ul?ll=${ll}&navigate=yes`;
    const sheet = await this.actionSheetCtrl.create({
      header: `Naviguer vers ${wp.name || wp.address || 'l\'étape'}`,
      buttons: [
        { text: 'Google Maps', icon: 'map-outline', handler: () => { window.open(google, '_system'); } },
        { text: 'Waze', icon: 'navigate-circle-outline', handler: () => { window.open(waze, '_system'); } },
        { text: 'Annuler', role: 'cancel', icon: 'close' }
      ]
    });
    await sheet.present();
  }

  // ────────────────── Carte ──────────────────

  private initMap() {
    if (this.map || !this.mapEl?.nativeElement) return;
    this.map = L.map(this.mapEl.nativeElement, {
      center: [34.0, 9.0],
      zoom: 7,
      zoomControl: false,
      attributionControl: false
    });
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', { maxZoom: 19 }).addTo(this.map);
    this.layers = L.layerGroup().addTo(this.map);
    setTimeout(() => { this.map?.invalidateSize(); this.drawMap(); }, 150);
  }

  private drawMap() {
    if (!this.map || !this.layers || !this.tour) return;
    this.layers.clearLayers();
    const wps = this.tour.waypoints.filter(w => w.latitude && w.longitude);
    if (wps.length === 0) return;

    // Itinéraire calculé (Valhalla, polyline précision 6) ; à défaut, liaison droite
    // entre les étapes (vieilles tournées sans polyline, ou polyline corrompue).
    const route = decodePolyline6(this.tour.estimatedRoutePolyline);
    const straight = wps.map(w => [w.latitude, w.longitude] as [number, number]);
    const line = route.length > 1
      ? L.polyline(route, { color: '#1a56db', weight: 4, opacity: 0.8 })
      : L.polyline(straight, { color: '#1a56db', weight: 3, opacity: 0.7, dashArray: '6 6' });
    line.addTo(this.layers);

    const next = wps.find(w => !w.isCompleted && w.waypointStatus === 'pending');
    wps.forEach((w, i) => {
      const bg = w.isCompleted ? '#64748b'
        : w.type === 'origin' ? '#10b981'
        : w.type === 'destination' ? '#ef4444'
        : (next && next.id === w.id) ? '#f59e0b' : '#1a56db';
      const label = w.isCompleted ? '✓' : String(i + 1);
      const icon = L.divIcon({
        className: 'custom-marker',
        html: `<div style="width:28px;height:28px;border-radius:50%;background:${bg};border:3px solid #fff;box-shadow:0 1px 5px rgba(0,0,0,.35);color:#fff;font-weight:700;font-size:13px;display:flex;align-items:center;justify-content:center">${label}</div>`,
        iconSize: [28, 28],
        iconAnchor: [14, 14]
      });
      L.marker([w.latitude, w.longitude], { icon })
        .bindTooltip(w.name || w.address || this.typeLabel(w.type))
        .addTo(this.layers!);
    });

    if (!this.fittedOnce) {
      const bounds = L.latLngBounds(straight);
      if (route.length > 1) bounds.extend(line.getBounds());
      this.map.fitBounds(bounds, { padding: [30, 30], maxZoom: 16 });
      this.fittedOnce = true;
    }
  }

  // ────────────────── Libellés ──────────────────

  statusOf(status: string) {
    return TOUR_STATUS_LABELS[status] || { label: status, color: 'medium' };
  }

  stepStatus(wp: DriverWaypoint): { label: string; color: string } {
    if (this.queued.some(q => q.waypointId === wp.id)) return { label: 'En attente d\'envoi', color: 'warning' };
    if (wp.waypointStatus === 'skipped') return { label: 'Non visitée', color: 'medium' };
    if (wp.isCompleted) return { label: 'Atteinte', color: 'success' };
    return { label: 'À venir', color: 'light' };
  }

  typeLabel(type: string): string {
    return type === 'origin' ? 'Départ' : type === 'destination' ? 'Destination' : 'Arrêt';
  }

  durationLabel(minutes: number): string {
    const h = Math.floor(minutes / 60), m = Math.round(minutes % 60);
    return h > 0 ? `${h} h ${m.toString().padStart(2, '0')}` : `${m} min`;
  }

  trackById(_: number, wp: DriverWaypoint) {
    return wp.id;
  }

  private async toast(message: string, color: string, duration = 3000) {
    const t = await this.toastCtrl.create({ message, color, duration, position: 'top' });
    await t.present();
  }
}
