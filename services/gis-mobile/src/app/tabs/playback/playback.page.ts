import { Component, OnInit, OnDestroy, NgZone } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { ActionSheetController } from '@ionic/angular';
import { ApiService } from '../../core/services/api.service';
import { Vehicle } from '../../core/models/types';
import * as L from 'leaflet';

interface PlaybackPoint {
  lat: number;
  lng: number;
  speed: number;
  heading: number;
  ignition: boolean;
  time: Date;
  address?: string;
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
        <ion-item lines="none" class="sel-item" (click)="showDatePicker = !showDatePicker">
          <ion-icon name="calendar-outline" slot="start" color="primary"></ion-icon>
          <ion-label>
            <p class="sel-hint">Date</p>
            <h3>{{ selectedDate | date:'dd/MM/yyyy' }}</h3>
          </ion-label>
          <ion-icon name="chevron-down-outline" slot="end" color="medium"></ion-icon>
        </ion-item>
        <ion-button fill="solid" color="primary" size="small" class="load-btn"
                    [disabled]="!selectedVehicleId || loadingTrack" (click)="loadTrack()">
          <ion-icon name="play-circle-outline" *ngIf="!loadingTrack"></ion-icon>
          <ion-spinner name="crescent" *ngIf="loadingTrack" style="width:18px;height:18px"></ion-spinner>
        </ion-button>
      </div>

      <ion-datetime
        *ngIf="showDatePicker"
        presentation="date"
        [(ngModel)]="datePickerValue"
        (ionChange)="onDatePicked()"
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
          <div class="info-item" [class.on]="currentPoint?.ignition" [class.off]="!currentPoint?.ignition">
            <ion-icon [name]="currentPoint?.ignition ? 'flash' : 'flash-off'" 
                      [color]="currentPoint?.ignition ? 'success' : 'danger'"></ion-icon>
            <span class="info-val">{{ currentPoint?.ignition ? 'ON' : 'OFF' }}</span>
          </div>
          <div class="info-item">
            <ion-icon name="time-outline" color="warning"></ion-icon>
            <span class="info-val">{{ currentPoint?.time | date:'HH:mm:ss' }}</span>
          </div>
        </div>
      </div>

      <!-- Stats bar -->
      <div class="stats-bar" *ngIf="points.length > 0">
        <div class="stat">
          <span class="stat-val">{{ totalDistance | number:'1.1-1' }}</span>
          <span class="stat-label">km</span>
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
    .info-row { display: flex; justify-content: space-around; }
    .info-item {
      display: flex; align-items: center; gap: 4px; font-size: 13px;
    }
    .info-item ion-icon { font-size: 16px; }
    .info-val { font-weight: 700; }
    .info-unit { font-size: 10px; color: var(--ion-color-medium); }
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

  // Date
  selectedDate = new Date();
  datePickerValue = new Date().toISOString();
  todayStr = new Date().toISOString();
  showDatePicker = false;

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
        this.selectedDate = new Date(params['date']);
        this.datePickerValue = this.selectedDate.toISOString();
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
        this.vehicles = v || [];
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

  onDatePicked() {
    this.selectedDate = new Date(this.datePickerValue);
    this.showDatePicker = false;
  }

  loadTrack() {
    if (!this.selectedVehicleId || !this.map) return;

    this.stopPlayback();
    this.loadingTrack = true;
    this.trackLoaded = false;
    this.points = [];
    this.currentIndex = 0;
    this.clearMapLayers();

    const d = this.selectedDate;
    const from = new Date(d.getFullYear(), d.getMonth(), d.getDate(), 0, 0, 0).toISOString();
    const to = new Date(d.getFullYear(), d.getMonth(), d.getDate(), 23, 59, 59).toISOString();

    this.api.getVehicleHistory(this.selectedVehicleId.toString(), from, to, 10000).subscribe({
      next: (data) => {
        this.zone.run(() => {
          this.loadingTrack = false;
          this.trackLoaded = true;

          if (!data || data.length === 0) {
            this.points = [];
            return;
          }

          this.points = data.map((p: any) => ({
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

    // Full track (grey)
    this.trackLine = L.polyline(latlngs, {
      color: '#94a3b8',
      weight: 3,
      opacity: 0.6
    }).addTo(this.map);

    // Progress line (colored)
    this.progressLine = L.polyline([], {
      color: '#1a56db',
      weight: 4,
      opacity: 0.9
    }).addTo(this.map);

    // Start marker (green)
    const startIcon = L.divIcon({
      className: 'custom-marker',
      html: '<div style="width:14px;height:14px;border-radius:50%;background:#10b981;border:3px solid #fff;box-shadow:0 1px 4px rgba(0,0,0,0.3)"></div>',
      iconSize: [14, 14],
      iconAnchor: [7, 7]
    });
    this.startMarker = L.marker([this.points[0].lat, this.points[0].lng], { icon: startIcon })
      .bindTooltip('Départ ' + this.points[0].time.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' }))
      .addTo(this.map);

    // End marker (red)
    const endIcon = L.divIcon({
      className: 'custom-marker',
      html: '<div style="width:14px;height:14px;border-radius:50%;background:#ef4444;border:3px solid #fff;box-shadow:0 1px 4px rgba(0,0,0,0.3)"></div>',
      iconSize: [14, 14],
      iconAnchor: [7, 7]
    });
    const lastPt = this.points[this.points.length - 1];
    this.endMarker = L.marker([lastPt.lat, lastPt.lng], { icon: endIcon })
      .bindTooltip('Arrivée ' + lastPt.time.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' }))
      .addTo(this.map);

    // Stop markers (yellow dots where ignition off for >2 min)
    this.detectStops();

    // Vehicle marker (blue arrow)
    this.createVehicleMarker();

    // Fit bounds
    this.map.fitBounds(this.trackLine.getBounds(), { padding: [30, 30] });
  }

  private detectStops() {
    if (!this.stopMarkers || this.points.length < 3) return;

    const stopIcon = L.divIcon({
      className: 'custom-marker',
      html: '<div style="width:10px;height:10px;border-radius:50%;background:#f59e0b;border:2px solid #fff;box-shadow:0 1px 3px rgba(0,0,0,0.3)"></div>',
      iconSize: [10, 10],
      iconAnchor: [5, 5]
    });

    let stopStart: number | null = null;

    for (let i = 0; i < this.points.length; i++) {
      const p = this.points[i];
      if (p.speed < 2) {
        if (stopStart === null) stopStart = i;
      } else {
        if (stopStart !== null) {
          const durMin = (this.points[i].time.getTime() - this.points[stopStart].time.getTime()) / 60000;
          if (durMin >= 2) {
            const sp = this.points[stopStart];
            L.marker([sp.lat, sp.lng], { icon: stopIcon })
              .bindTooltip(`Arrêt ${Math.round(durMin)} min (${sp.time.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' })})`)
              .addTo(this.stopMarkers!);
          }
          stopStart = null;
        }
      }
    }
  }

  private createVehicleMarker() {
    if (!this.map || this.points.length === 0) return;

    const p = this.points[0];
    const icon = L.divIcon({
      className: 'vehicle-marker',
      html: `<div style="
        width:24px;height:24px;border-radius:50%;
        background:${p.ignition ? '#1a56db' : '#6b7280'};
        border:3px solid #fff;box-shadow:0 2px 6px rgba(0,0,0,0.3);
        display:flex;align-items:center;justify-content:center;
        transform:rotate(${p.heading}deg);
        transition:transform 0.3s ease;
      "><svg width="12" height="12" viewBox="0 0 24 24" fill="white"><path d="M12 2L4.5 20.29l.71.71L12 18l6.79 3 .71-.71z"/></svg></div>`,
      iconSize: [24, 24],
      iconAnchor: [12, 12]
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
        html: `<div style="
          width:24px;height:24px;border-radius:50%;
          background:${p.ignition ? (p.speed > 3 ? '#1a56db' : '#f59e0b') : '#6b7280'};
          border:3px solid #fff;box-shadow:0 2px 6px rgba(0,0,0,0.3);
          display:flex;align-items:center;justify-content:center;
          transform:rotate(${p.heading}deg);
        "><svg width="12" height="12" viewBox="0 0 24 24" fill="white"><path d="M12 2L4.5 20.29l.71.71L12 18l6.79 3 .71-.71z"/></svg></div>`,
        iconSize: [24, 24],
        iconAnchor: [12, 12]
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
