import { Component, OnInit, OnDestroy, NgZone } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { ActionSheetController } from '@ionic/angular';
import { ApiService } from '../../core/services/api.service';
import { Vehicle } from '../../core/models/types';
import {
  FrameMotionState, STATE_TEXT_ON_COLOR, VehicleStateStyle, frameState, stateMarkerHtml, stateStyle
} from '../../core/vehicle-state.util';
import * as L from 'leaflet';

export interface PlaybackPoint {
  lat: number;
  lng: number;
  speed: number;
  heading: number;
  ignition: boolean;
  time: Date;
  address?: string;
}

/**
 * État d'un point du trajet — la règle de la carte (vehicle-state.util), sans le test
 * de fraîcheur : un point d'hier n'est pas « déconnecté ». En route vert, ralenti orange,
 * contact coupé rouge (l'ancien replay peignait la conduite en BLEU et le contact
 * coupé en gris, qui veut dire « déconnecté » partout ailleurs).
 */
export function pointState(p: Pick<PlaybackPoint, 'speed' | 'ignition'>): FrameMotionState {
  return frameState({ speedKph: p.speed, ignitionOn: p.ignition });
}

export interface PlaybackStop {
  /** Premier point de l'arrêt. */
  index: number;
  durationMin: number;
  /** Contact coupé à un moment de l'arrêt = à l'arrêt (rouge), sinon au ralenti (orange). */
  state: 'idling' | 'parked';
}

/**
 * Arrêts d'au moins `minMinutes` : suite de points qui ne roulent pas (<= 3 km/h, même
 * seuil que le rapport d'activité de l'API). Un arrêt où le contact a été coupé est
 * « À l'arrêt » — même règle que l'API (HasIgnitionOff) ; moteur tournant tout du long,
 * c'est « Au ralenti ». L'ancien replay les marquait tous d'un point orange.
 */
export function findStops(points: PlaybackPoint[], minMinutes = 2): PlaybackStop[] {
  const stops: PlaybackStop[] = [];
  let stopStart: number | null = null;
  for (let i = 0; i < points.length; i++) {
    if (pointState(points[i]) !== 'moving') {
      if (stopStart === null) stopStart = i;
      continue;
    }
    if (stopStart !== null) {
      const durMin = (points[i].time.getTime() - points[stopStart].time.getTime()) / 60000;
      if (durMin >= minMinutes) {
        const stopped = points.slice(stopStart, i);
        stops.push({ index: stopStart, durationMin: durMin, state: stopped.some(p => !p.ignition) ? 'parked' : 'idling' });
      }
      stopStart = null;
    }
  }
  return stops;
}

/**
 * Marqueurs de DÉPART et d'ARRIVÉE : ni vert ni rouge. Une pastille verte au départ et
 * rouge à l'arrivée se lisaient « en route » / « à l'arrêt » ; ils portent désormais un
 * drapeau (bleu) et un drapeau à damier (sombre), couleurs qui ne sont celles d'aucun état.
 */
export const TRACK_START_COLOR = '#2563eb';
export const TRACK_END_COLOR = '#111827';
const TRACK_FLAG_STYLE = 'box-sizing:border-box;width:26px;height:26px;border-radius:50%;border:2px solid #fff;'
  + 'box-shadow:0 1px 4px rgba(0,0,0,0.35);display:flex;align-items:center;justify-content:center;';
export const TRACK_START_HTML = `<div class="track-flag track-start" style="${TRACK_FLAG_STYLE}background:${TRACK_START_COLOR}">`
  + '<svg width="14" height="14" viewBox="0 0 24 24" aria-hidden="true">'
  + '<rect x="4" y="2" width="2.5" height="20" rx="1" fill="#fff"/>'
  + '<path d="M6.5 3h13l-3.5 5 3.5 5h-13z" fill="#fff"/></svg></div>';
export const TRACK_END_HTML = `<div class="track-flag track-end" style="${TRACK_FLAG_STYLE}background:${TRACK_END_COLOR}">`
  + '<svg width="14" height="14" viewBox="0 0 24 24" aria-hidden="true">'
  + '<rect x="3" y="2" width="2.5" height="20" rx="1" fill="#fff"/>'
  + '<rect x="5.5" y="3" width="15" height="10" fill="#fff"/>'
  + `<path d="M5.5 3h3.75v3.33H5.5zM13 3h3.75v3.33H13zM9.25 6.33H13v3.34H9.25zM16.75 6.33h3.75v3.34h-3.75zM5.5 9.67h3.75V13H5.5zM13 9.67h3.75V13H13z" fill="${TRACK_END_COLOR}"/>`
  + '</svg></div>';

/** Marqueur du véhicule rejoué : pastille de l'état du point, flèche orientée seulement en route. */
export function playbackMarkerHtml(p: PlaybackPoint): string {
  return stateMarkerHtml(pointState(p), { size: 28, rotateDeg: p.heading });
}

@Component({
  selector: 'app-playback',
  standalone: false,
  template: `
    <ion-header>
      <ion-toolbar color="primary">
        <ion-buttons slot="start">
          <ion-back-button defaultHref="/tabs/monitoring" text=""></ion-back-button>
        </ion-buttons>
        <ion-title>Trajet Playback</ion-title>
      </ion-toolbar>
    </ion-header>

    <ion-content [fullscreen]="true" class="playback-content">
      <!-- Vehicle & Date selectors -->
      <div class="selectors-bar">
        <ion-item lines="none" class="sel-item" (click)="pickVehicle()">
          <ion-icon name="car-sport-outline" slot="start" color="primary"></ion-icon>
          <ion-label>
            <p class="sel-hint">Véhicule</p>
            <h3>{{ selectedVehicleName || 'Choisir...' }}</h3>
          </ion-label>
          <ion-icon name="chevron-down-outline" slot="end" color="medium"></ion-icon>
        </ion-item>
        <ion-item lines="none" class="sel-item" (click)="activePickerField = activePickerField === 'start' ? null : 'start'">
          <ion-icon name="calendar-outline" slot="start" color="primary"></ion-icon>
          <ion-label>
            <p class="sel-hint">Début</p>
            <h3>{{ startDate | date:'dd/MM HH:mm' }}</h3>
          </ion-label>
        </ion-item>
        <ion-item lines="none" class="sel-item" (click)="activePickerField = activePickerField === 'end' ? null : 'end'">
          <ion-icon name="calendar-outline" slot="start" color="tertiary"></ion-icon>
          <ion-label>
            <p class="sel-hint">Fin</p>
            <h3>{{ endDate | date:'dd/MM HH:mm' }}</h3>
          </ion-label>
        </ion-item>
        <ion-button fill="solid" color="primary" size="small" class="load-btn"
                    [disabled]="!selectedVehicleId || loadingTrack" (click)="loadTrack()">
          <ion-icon name="play-circle-outline" *ngIf="!loadingTrack"></ion-icon>
          <ion-spinner name="crescent" *ngIf="loadingTrack" style="width:18px;height:18px"></ion-spinner>
        </ion-button>
      </div>

      <ion-datetime
        *ngIf="activePickerField === 'start'"
        presentation="date-time"
        hourCycle="h23"
        [(ngModel)]="startDateValue"
        (ionChange)="onStartDatePicked()"
        [max]="todayStr"
        class="date-picker-inline"
      ></ion-datetime>
      <ion-datetime
        *ngIf="activePickerField === 'end'"
        presentation="date-time"
        hourCycle="h23"
        [(ngModel)]="endDateValue"
        (ionChange)="onEndDatePicked()"
        [max]="todayStr"
        class="date-picker-inline"
      ></ion-datetime>

      <!-- Map -->
      <div id="playback-map" class="playback-map"></div>

      <!-- Info panel (current position info) -->
      <div class="info-panel" *ngIf="points.length > 0">
        <div class="info-row">
          <div class="info-item">
            <ion-icon name="speedometer-outline" color="primary"></ion-icon>
            <span class="info-val">{{ currentPoint?.speed || 0 | number:'1.0-0' }}</span>
            <span class="info-unit">km/h</span>
          </div>
          <div class="info-item">
            <ion-icon name="navigate-outline" color="tertiary"></ion-icon>
            <span class="info-val">{{ currentPoint?.heading || 0 | number:'1.0-0' }}°</span>
          </div>
          <!-- État du point courant, même pastille que la carte : couleur + icône + libellé. -->
          <div class="info-item state-item" *ngIf="currentStyle as st" [attr.data-state]="st.state">
            <span class="state-chip" [style.background]="st.color" [style.color]="textOnState">
              <ion-icon [name]="st.icon" aria-hidden="true"></ion-icon>
              {{ st.label }}
            </span>
          </div>
          <!-- Le contact reste affiché À CÔTÉ de l'état : « En route » ne dit rien du contact
               (véhicule remorqué ou poussé contact coupé). Couleur de l'application ou neutre,
               comme sur la carte et la fiche véhicule : plus le vert/rouge d'autrefois, qui
               contredisait le ralenti orange. -->
          <div class="info-item contact-item" [attr.data-ignition]="currentPoint?.ignition ? 'on' : 'off'">
            <ion-icon [name]="currentPoint?.ignition ? 'flash' : 'flash-off'"
                      [color]="currentPoint?.ignition ? 'primary' : 'medium'" aria-hidden="true"></ion-icon>
            <span class="info-val"><span class="sr-only">Contact </span>{{ currentPoint?.ignition ? 'ON' : 'OFF' }}</span>
          </div>
          <div class="info-item">
            <ion-icon name="time-outline" color="medium"></ion-icon>
            <span class="info-val">{{ currentPoint?.time | date:'HH:mm:ss' }}</span>
          </div>
        </div>
      </div>

      <!-- Stats bar -->
      <div class="stats-bar" *ngIf="points.length > 0">
        <div class="stat">
          <span class="stat-val">{{ totalDistance | number:'1.1-1' }}</span>
          <!-- « km » seul, sous un chiffre, se lisait comme un compteur : c'est la distance
               parcourue sur la seule tranche rejouée, mesurée point à point (computeStats). -->
          <span class="stat-label">km parcourus</span>
        </div>
        <div class="stat">
          <span class="stat-val">{{ maxSpeed | number:'1.0-0' }}</span>
          <span class="stat-label">km/h max</span>
        </div>
        <div class="stat">
          <span class="stat-val">{{ points.length }}</span>
          <span class="stat-label">points</span>
        </div>
        <div class="stat">
          <span class="stat-val">{{ duration }}</span>
          <span class="stat-label">durée</span>
        </div>
      </div>

      <!-- Playback controls -->
      <div class="controls" *ngIf="points.length > 0">
        <input type="range" class="timeline-slider" min="0" [max]="points.length - 1"
               [(ngModel)]="currentIndex" (input)="onSliderChange()" />
        <div class="time-labels">
          <span>{{ points[0]?.time | date:'HH:mm' }}</span>
          <span>{{ currentIndex + 1 }} / {{ points.length }}</span>
          <span>{{ points[points.length - 1]?.time | date:'HH:mm' }}</span>
        </div>
        <div class="control-buttons">
          <ion-button fill="clear" size="small" (click)="goToStart()">
            <ion-icon name="play-skip-back"></ion-icon>
          </ion-button>
          <ion-button fill="clear" size="small" (click)="stepBack()">
            <ion-icon name="play-back"></ion-icon>
          </ion-button>
          <ion-button [fill]="isPlaying ? 'solid' : 'outline'" color="primary" 
                      shape="round" (click)="togglePlay()">
            <ion-icon [name]="isPlaying ? 'pause' : 'play'" slot="icon-only"></ion-icon>
          </ion-button>
          <ion-button fill="clear" size="small" (click)="stepForward()">
            <ion-icon name="play-forward"></ion-icon>
          </ion-button>
          <ion-button fill="clear" size="small" (click)="goToEnd()">
            <ion-icon name="play-skip-forward"></ion-icon>
          </ion-button>
          <ion-button fill="clear" size="small" (click)="cycleSpeed()" class="speed-btn">
            <span class="speed-label">{{ playbackSpeed }}x</span>
          </ion-button>
        </div>
      </div>

      <!-- Empty state -->
      <div class="empty-state" *ngIf="!loadingTrack && points.length === 0 && trackLoaded">
        <ion-icon name="navigate-outline" color="medium"></ion-icon>
        <p>Aucun trajet trouvé pour cette date</p>
      </div>

      <div class="empty-state" *ngIf="!selectedVehicleId && !loadingTrack">
        <ion-icon name="car-sport-outline" color="medium"></ion-icon>
        <p>Sélectionnez un véhicule et une date</p>
      </div>
    </ion-content>
  `,
  styles: [`
    .playback-content { --background: #f5f5f5; }
    .selectors-bar {
      display: flex; align-items: center; gap: 4px;
      padding: 8px 10px; background: #fff;
      border-bottom: 1px solid rgba(0,0,0,0.06);
    }
    .sel-item {
      flex: 1; --background: var(--ion-color-light); --border-radius: 10px;
      --min-height: 44px; cursor: pointer; font-size: 13px; --padding-start: 8px;
    }
    .sel-item ion-icon[slot="start"] { font-size: 16px; margin-right: 4px; }
    .sel-item ion-icon[slot="end"] { font-size: 14px; }
    .sel-hint { font-size: 9px; color: var(--ion-color-medium); margin: 0; }
    .sel-item h3 { font-size: 12px; font-weight: 600; margin: 0; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .load-btn { --border-radius: 10px; height: 44px; min-width: 44px; }
    .date-picker-inline { margin: 0 auto; display: block; }
    .playback-map { width: 100%; height: 45vh; min-height: 250px; z-index: 1; }
    .info-panel {
      background: #fff; padding: 8px 12px;
      border-bottom: 1px solid rgba(0,0,0,0.06);
    }
    /* Cinq éléments (vitesse, cap, état, contact, heure) : retour à la ligne plutôt qu'un
       débordement sur un écran de 360 px. */
    .info-row { display: flex; flex-wrap: wrap; justify-content: space-around; gap: 4px 8px; }
    .sr-only {
      position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px;
      overflow: hidden; clip: rect(0, 0, 0, 0); white-space: nowrap; border: 0;
    }
    .info-item {
      display: flex; align-items: center; gap: 4px; font-size: 13px;
    }
    .info-item ion-icon { font-size: 16px; }
    .info-val { font-weight: 700; }
    .info-unit { font-size: 10px; color: var(--ion-color-medium); }
    .state-chip {
      display: inline-flex; align-items: center; gap: 4px;
      padding: 3px 8px; border-radius: 12px;
      font-size: 11px; font-weight: 700; white-space: nowrap;
    }
    .state-chip ion-icon { font-size: 13px; }
    .stats-bar {
      display: flex; justify-content: space-around; padding: 8px;
      background: #fff; border-bottom: 1px solid rgba(0,0,0,0.06);
    }
    .stat { text-align: center; }
    .stat-val { display: block; font-size: 15px; font-weight: 700; color: var(--ion-color-primary); }
    .stat-label { font-size: 9px; color: var(--ion-color-medium); }
    .controls {
      background: #fff; padding: 8px 16px 12px;
      border-top: 1px solid rgba(0,0,0,0.06);
    }
    .timeline-slider {
      width: 100%; height: 4px; -webkit-appearance: none; appearance: none;
      background: var(--ion-color-light-shade); border-radius: 2px; outline: none;
      margin: 4px 0;
    }
    .timeline-slider::-webkit-slider-thumb {
      -webkit-appearance: none; width: 16px; height: 16px;
      border-radius: 50%; background: var(--ion-color-primary); cursor: pointer;
    }
    .time-labels {
      display: flex; justify-content: space-between;
      font-size: 10px; color: var(--ion-color-medium); margin-bottom: 4px;
    }
    .control-buttons {
      display: flex; justify-content: center; align-items: center; gap: 2px;
    }
    .control-buttons ion-button { --padding-start: 8px; --padding-end: 8px; }
    .speed-btn { min-width: 44px; }
    .speed-label { font-size: 12px; font-weight: 700; color: var(--ion-color-primary); }
    .empty-state { text-align: center; padding: 60px 20px; }
    .empty-state ion-icon { font-size: 48px; display: block; margin: 0 auto 12px; }
    .empty-state p { font-size: 14px; color: var(--ion-color-medium); }
  `]
})
export class PlaybackPage implements OnInit, OnDestroy {
  // Vehicle selection
  vehicles: Vehicle[] = [];
  selectedVehicleId: number | null = null;
  selectedVehicleName = '';

  // Date range
  startDate = new Date(new Date().setHours(0, 0, 0, 0));
  endDate = new Date(new Date().setHours(23, 59, 59, 0));
  startDateValue = this.startDate.toISOString();
  endDateValue = this.endDate.toISOString();
  todayStr = new Date().toISOString();
  activePickerField: 'start' | 'end' | null = null;

  // Track data
  points: PlaybackPoint[] = [];
  loadingTrack = false;
  trackLoaded = false;
  totalDistance = 0;
  maxSpeed = 0;
  duration = '';

  // Playback state
  currentIndex = 0;
  isPlaying = false;
  playbackSpeed = 1;
  private playInterval: any = null;
  private readonly speeds = [1, 2, 4, 8, 16];

  // Map
  private map: L.Map | null = null;
  private trackLine: L.Polyline | null = null;
  private progressLine: L.Polyline | null = null;
  private vehicleMarker: L.Marker | null = null;
  private startMarker: L.Marker | null = null;
  private endMarker: L.Marker | null = null;
  private stopMarkers: L.LayerGroup | null = null;

  get currentPoint(): PlaybackPoint | null {
    return this.points[this.currentIndex] || null;
  }

  /** Couleur, libellé et icône de l'état du point courant (vehicle-state.util). */
  get currentStyle(): VehicleStateStyle | null {
    const p = this.currentPoint;
    return p ? stateStyle(pointState(p)) : null;
  }

  /** Texte sur la couleur d'un état : lisible sur les quatre couleurs (contraste >= 4,5:1). */
  readonly textOnState = STATE_TEXT_ON_COLOR;

  constructor(
    private api: ApiService,
    private route: ActivatedRoute,
    private actionSheetCtrl: ActionSheetController,
    private zone: NgZone
  ) {}

  ngOnInit() {
    this.loadVehicles();
    // Check if vehicleId passed via query params
    this.route.queryParams.subscribe(params => {
      if (params['vehicleId']) {
        this.selectedVehicleId = +params['vehicleId'];
        if (params['vehicleName']) {
          this.selectedVehicleName = params['vehicleName'];
        }
      }
      if (params['date']) {
        const d = new Date(params['date']);
        this.startDate = new Date(d.setHours(0, 0, 0, 0));
        this.endDate = new Date(d.setHours(23, 59, 59, 0));
        this.startDateValue = this.startDate.toISOString();
        this.endDateValue = this.endDate.toISOString();
      }
      if (params['startDate']) {
        this.startDate = new Date(params['startDate']);
        this.startDateValue = this.startDate.toISOString();
      }
      if (params['endDate']) {
        this.endDate = new Date(params['endDate']);
        this.endDateValue = this.endDate.toISOString();
      }
    });
  }

  ngOnDestroy() {
    this.stopPlayback();
    if (this.map) {
      this.map.remove();
      this.map = null;
    }
  }

  ionViewDidEnter() {
    setTimeout(() => this.initMap(), 200);
  }

  private initMap() {
    if (this.map) {
      this.map.invalidateSize();
      return;
    }

    this.map = L.map('playback-map', {
      center: [34.0, 9.0],
      zoom: 7,
      zoomControl: false,
      attributionControl: false
    });

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19
    }).addTo(this.map);

    this.stopMarkers = L.layerGroup().addTo(this.map);

    // Auto-load if vehicle already selected
    if (this.selectedVehicleId) {
      this.loadTrack();
    }
  }

  loadVehicles() {
    this.api.getVehicles().subscribe({
      next: (v) => {
        this.vehicles = Array.isArray(v) ? v : [];
        // Set vehicle name if ID was passed via params
        if (this.selectedVehicleId && !this.selectedVehicleName) {
          const found = this.vehicles.find(x => parseInt(x.id) === this.selectedVehicleId);
          if (found) this.selectedVehicleName = found.name;
        }
      }
    });
  }

  async pickVehicle() {
    const buttons: any[] = this.vehicles.slice(0, 20).map(v => ({
      text: `${v.name} (${v.plate})`,
      handler: () => {
        this.selectedVehicleId = parseInt(v.id);
        this.selectedVehicleName = v.name;
      }
    }));
    buttons.push({ text: 'Annuler', role: 'cancel' });

    const sheet = await this.actionSheetCtrl.create({
      header: 'Choisir un véhicule',
      buttons
    });
    await sheet.present();
  }

  onStartDatePicked() {
    this.startDate = new Date(this.startDateValue);
    this.activePickerField = null;
  }

  onEndDatePicked() {
    this.endDate = new Date(this.endDateValue);
    this.activePickerField = null;
  }

  loadTrack() {
    if (!this.selectedVehicleId || !this.map) return;

    this.stopPlayback();
    this.loadingTrack = true;
    this.trackLoaded = false;
    this.points = [];
    this.currentIndex = 0;
    this.clearMapLayers();

    const from = this.startDate.toISOString();
    const to = this.endDate.toISOString();

    this.api.getVehicleHistory(this.selectedVehicleId.toString(), from, to, 2000).subscribe({
      next: (data: any) => {
        this.zone.run(() => {
          this.loadingTrack = false;
          this.trackLoaded = true;

          const positions = Array.isArray(data) ? data : (Array.isArray(data?.positions) ? data.positions : []);
          if (positions.length === 0) {
            this.points = [];
            return;
          }

          this.points = positions.map((p: any) => ({
            lat: p.latitude,
            lng: p.longitude,
            speed: p.speedKph || 0,
            heading: p.courseDeg || 0,
            ignition: p.ignitionOn ?? false,
            time: new Date(p.recordedAt),
            address: p.address
          }));

          this.computeStats();
          this.drawTrack();
          this.updatePosition(0);
        });
      },
      error: () => {
        this.zone.run(() => {
          this.loadingTrack = false;
          this.trackLoaded = true;
          this.points = [];
        });
      }
    });
  }

  private computeStats() {
    if (this.points.length < 2) return;

    // Max speed
    this.maxSpeed = Math.max(...this.points.map(p => p.speed));

    // Total distance (Haversine)
    let dist = 0;
    for (let i = 1; i < this.points.length; i++) {
      dist += this.haversine(
        this.points[i - 1].lat, this.points[i - 1].lng,
        this.points[i].lat, this.points[i].lng
      );
    }
    this.totalDistance = dist;

    // Duration
    const start = this.points[0].time;
    const end = this.points[this.points.length - 1].time;
    const mins = Math.round((end.getTime() - start.getTime()) / 60000);
    if (mins < 60) {
      this.duration = `${mins}m`;
    } else {
      const h = Math.floor(mins / 60);
      const m = mins % 60;
      this.duration = `${h}h${m.toString().padStart(2, '0')}`;
    }
  }

  private haversine(lat1: number, lon1: number, lat2: number, lon2: number): number {
    const R = 6371;
    const dLat = (lat2 - lat1) * Math.PI / 180;
    const dLon = (lon2 - lon1) * Math.PI / 180;
    const a = Math.sin(dLat / 2) ** 2 +
              Math.cos(lat1 * Math.PI / 180) * Math.cos(lat2 * Math.PI / 180) *
              Math.sin(dLon / 2) ** 2;
    return R * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
  }

  private drawTrack() {
    if (!this.map || this.points.length < 2) return;

    const latlngs: L.LatLngExpression[] = this.points.map(p => [p.lat, p.lng]);

    // Trajet complet en bleu pâle (le gris est réservé à « Déconnecté ») — smoothFactor for mobile perf
    this.trackLine = L.polyline(latlngs, {
      color: '#93c5fd',
      weight: 3,
      opacity: 0.8,
      smoothFactor: 1.5
    }).addTo(this.map);

    // Progress line (bleu foncé : le chemin parcouru, pas un état)
    this.progressLine = L.polyline([], {
      color: '#1a56db',
      weight: 4,
      opacity: 0.9
    }).addTo(this.map);

    // Départ : drapeau bleu (et non plus une pastille verte, lue « en route »)
    const startIcon = L.divIcon({
      className: 'custom-marker',
      html: TRACK_START_HTML,
      iconSize: [26, 26],
      iconAnchor: [13, 13]
    });
    this.startMarker = L.marker([this.points[0].lat, this.points[0].lng], { icon: startIcon })
      .bindTooltip('Départ ' + this.points[0].time.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' }))
      .addTo(this.map);

    // Arrivée : drapeau à damier sombre (et non plus une pastille rouge, lue « à l'arrêt »)
    const endIcon = L.divIcon({
      className: 'custom-marker',
      html: TRACK_END_HTML,
      iconSize: [26, 26],
      iconAnchor: [13, 13]
    });
    const lastPt = this.points[this.points.length - 1];
    this.endMarker = L.marker([lastPt.lat, lastPt.lng], { icon: endIcon })
      .bindTooltip('Arrivée ' + lastPt.time.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' }))
      .addTo(this.map);

    // Arrêts de plus de 2 min : rouge contact coupé, orange au ralenti
    this.detectStops();

    // Vehicle marker (pastille de l'état du point)
    this.createVehicleMarker();

    // Fit bounds
    this.map.fitBounds(this.trackLine.getBounds(), { padding: [30, 30] });
  }

  private detectStops() {
    if (!this.stopMarkers || this.points.length < 3) return;

    const icons = {
      idling: L.divIcon({ className: 'custom-marker', html: stateMarkerHtml('idling', { size: 18, border: 2 }), iconSize: [18, 18], iconAnchor: [9, 9] }),
      parked: L.divIcon({ className: 'custom-marker', html: stateMarkerHtml('parked', { size: 18, border: 2 }), iconSize: [18, 18], iconAnchor: [9, 9] })
    };

    for (const stop of findStops(this.points)) {
      const sp = this.points[stop.index];
      const at = sp.time.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
      L.marker([sp.lat, sp.lng], { icon: icons[stop.state] })
        .bindTooltip(`${stateStyle(stop.state).label} · ${Math.round(stop.durationMin)} min (${at})`)
        .addTo(this.stopMarkers);
    }
  }

  private createVehicleMarker() {
    if (!this.map || this.points.length === 0) return;

    const p = this.points[0];
    const icon = L.divIcon({
      className: 'vehicle-marker',
      html: playbackMarkerHtml(p),
      iconSize: [28, 28],
      iconAnchor: [14, 14]
    });

    this.vehicleMarker = L.marker([p.lat, p.lng], { icon, zIndexOffset: 1000 }).addTo(this.map);
  }

  private updatePosition(index: number) {
    if (index < 0 || index >= this.points.length) return;
    this.currentIndex = index;
    const p = this.points[index];

    // Update vehicle marker
    if (this.vehicleMarker && this.map) {
      this.vehicleMarker.setLatLng([p.lat, p.lng]);
      const icon = L.divIcon({
        className: 'vehicle-marker',
        html: playbackMarkerHtml(p),
        iconSize: [28, 28],
        iconAnchor: [14, 14]
      });
      this.vehicleMarker.setIcon(icon);
    }

    // Update progress line
    if (this.progressLine) {
      const progressLatLngs: L.LatLngExpression[] = this.points.slice(0, index + 1).map(pt => [pt.lat, pt.lng]);
      this.progressLine.setLatLngs(progressLatLngs);
    }

    // Keep vehicle in view during playback
    if (this.isPlaying && this.map) {
      if (!this.map.getBounds().contains([p.lat, p.lng])) {
        this.map.panTo([p.lat, p.lng], { animate: true, duration: 0.5 });
      }
    }
  }

  // ─── Playback Controls ─────────────────────────

  togglePlay() {
    if (this.isPlaying) {
      this.stopPlayback();
    } else {
      this.startPlayback();
    }
  }

  private startPlayback() {
    if (this.points.length === 0) return;
    if (this.currentIndex >= this.points.length - 1) {
      this.currentIndex = 0;
    }
    this.isPlaying = true;

    const baseInterval = 100; // ms between frames
    this.playInterval = setInterval(() => {
      this.zone.run(() => {
        if (this.currentIndex < this.points.length - 1) {
          this.updatePosition(this.currentIndex + 1);
        } else {
          this.stopPlayback();
        }
      });
    }, baseInterval / this.playbackSpeed);
  }

  private stopPlayback() {
    this.isPlaying = false;
    if (this.playInterval) {
      clearInterval(this.playInterval);
      this.playInterval = null;
    }
  }

  cycleSpeed() {
    const idx = this.speeds.indexOf(this.playbackSpeed);
    this.playbackSpeed = this.speeds[(idx + 1) % this.speeds.length];
    if (this.isPlaying) {
      this.stopPlayback();
      this.startPlayback();
    }
  }

  onSliderChange() {
    this.updatePosition(this.currentIndex);
  }

  stepForward() {
    if (this.currentIndex < this.points.length - 1) {
      this.updatePosition(this.currentIndex + 1);
    }
  }

  stepBack() {
    if (this.currentIndex > 0) {
      this.updatePosition(this.currentIndex - 1);
    }
  }

  goToStart() {
    this.stopPlayback();
    this.updatePosition(0);
  }

  goToEnd() {
    this.stopPlayback();
    this.updatePosition(this.points.length - 1);
  }

  private clearMapLayers() {
    if (this.trackLine) { this.trackLine.remove(); this.trackLine = null; }
    if (this.progressLine) { this.progressLine.remove(); this.progressLine = null; }
    if (this.vehicleMarker) { this.vehicleMarker.remove(); this.vehicleMarker = null; }
    if (this.startMarker) { this.startMarker.remove(); this.startMarker = null; }
    if (this.endMarker) { this.endMarker.remove(); this.endMarker = null; }
    if (this.stopMarkers) { this.stopMarkers.clearLayers(); }
  }
}
