import { Component, OnInit, OnDestroy, AfterViewInit } from '@angular/core';
import { Subscription } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { SignalRService, PositionUpdate } from '../../core/services/signalr.service';
import * as L from 'leaflet';

@Component({
  selector: 'app-monitoring',
  standalone: false,
  template: `
    <ion-header>
      <ion-toolbar color="primary">
        <ion-title>Monitoring</ion-title>
        <ion-buttons slot="end">
          <ion-button routerLink="/tabs/playback">
            <ion-icon name="play-circle-outline" slot="icon-only"></ion-icon>
          </ion-button>
          <ion-button (click)="centerOnAll()">
            <ion-icon name="locate-outline" slot="icon-only"></ion-icon>
          </ion-button>
        </ion-buttons>
      </ion-toolbar>
    </ion-header>

    <ion-content [fullscreen]="true" class="map-content">
      <!-- Map Container -->
      <div id="monitoring-map" class="map-container"></div>

      <!-- Floating vehicle count badge -->
      <div class="floating-badge">
        <ion-icon name="car-sport"></ion-icon>
        <span>{{ vehicleCount }} véhicules</span>
      </div>

      <!-- Bottom sheet: selected vehicle info -->
      <div class="vehicle-sheet" *ngIf="selectedVehicle" (click)="closeSheet()">
        <div class="sheet-handle"></div>
        <div class="sheet-content">
          <div class="sheet-header">
            <div class="sheet-avatar" [class.moving]="selectedVehicle.isMoving" [class.stopped]="!selectedVehicle.isMoving">
              <ion-icon [name]="selectedVehicle.isMoving ? 'navigate' : 'stop-circle'"></ion-icon>
            </div>
            <div>
              <h3>{{ selectedVehicle.vehicleName }}</h3>
              <p>{{ selectedVehicle.plate }}</p>
            </div>
          </div>
          <div class="sheet-stats">
            <div class="sheet-stat">
              <ion-icon name="speedometer-outline" color="primary"></ion-icon>
              <span>{{ selectedVehicle.speedKph | number:'1.0-0' }} km/h</span>
            </div>
            <div class="sheet-stat">
              <ion-icon [name]="selectedVehicle.ignitionOn ? 'flash' : 'flash-off'" [color]="selectedVehicle.ignitionOn ? 'success' : 'medium'"></ion-icon>
              <span>{{ selectedVehicle.ignitionOn ? 'Contact ON' : 'Contact OFF' }}</span>
            </div>
            <div class="sheet-stat">
              <ion-icon name="compass-outline" color="tertiary"></ion-icon>
              <span>{{ selectedVehicle.courseDeg | number:'1.0-0' }}°</span>
            </div>
          </div>
        </div>
      </div>
    </ion-content>
  `,
  styles: [`
    .map-content { position: relative; }
    .map-container {
      width: 100%; height: 100%;
      z-index: 1;
    }
    .floating-badge {
      position: absolute;
      top: 12px; left: 12px;
      z-index: 1000;
      background: var(--ion-card-background, #fff);
      border-radius: 20px;
      padding: 6px 14px;
      display: flex; align-items: center; gap: 6px;
      box-shadow: 0 2px 12px rgba(0,0,0,0.15);
      font-size: 13px; font-weight: 600;
    }
    .floating-badge ion-icon { color: var(--ion-color-primary); font-size: 16px; }
    .vehicle-sheet {
      position: absolute;
      bottom: 0; left: 0; right: 0;
      z-index: 1000;
      background: var(--ion-card-background, #fff);
      border-radius: 20px 20px 0 0;
      padding: 12px 20px 24px;
      box-shadow: 0 -4px 20px rgba(0,0,0,0.15);
    }
    .sheet-handle {
      width: 40px; height: 4px;
      background: var(--ion-color-light-shade);
      border-radius: 2px;
      margin: 0 auto 12px;
    }
    .sheet-header { display: flex; align-items: center; gap: 12px; margin-bottom: 12px; }
    .sheet-avatar {
      width: 44px; height: 44px; border-radius: 50%;
      display: flex; align-items: center; justify-content: center;
    }
    .sheet-avatar.moving { background: rgba(16,185,129,0.15); color: #10b981; }
    .sheet-avatar.stopped { background: rgba(156,163,175,0.15); color: #9ca3af; }
    .sheet-avatar ion-icon { font-size: 22px; }
    .sheet-header h3 { margin: 0; font-size: 16px; font-weight: 600; }
    .sheet-header p { margin: 2px 0 0; font-size: 13px; color: var(--ion-color-medium); }
    .sheet-stats { display: flex; gap: 16px; }
    .sheet-stat {
      display: flex; align-items: center; gap: 6px;
      font-size: 13px; font-weight: 500;
    }
  `]
})
export class MonitoringPage implements OnInit, OnDestroy, AfterViewInit {
  private map!: L.Map;
  private markers = new Map<number, L.Marker>();
  private subs: Subscription[] = [];
  vehicleCount = 0;
  selectedVehicle: PositionUpdate | null = null;

  // Custom icons
  private movingIcon = L.divIcon({
    className: 'vehicle-marker moving-marker',
    html: '<div style="background:#10b981;width:28px;height:28px;border-radius:50%;display:flex;align-items:center;justify-content:center;border:3px solid #fff;box-shadow:0 2px 8px rgba(0,0,0,0.3);"><svg width="14" height="14" viewBox="0 0 24 24" fill="white"><path d="M12 2L4.5 20.29l.71.71L12 18l6.79 3 .71-.71z"/></svg></div>',
    iconSize: [28, 28],
    iconAnchor: [14, 14]
  });

  private stoppedIcon = L.divIcon({
    className: 'vehicle-marker stopped-marker',
    html: '<div style="background:#9ca3af;width:28px;height:28px;border-radius:50%;display:flex;align-items:center;justify-content:center;border:3px solid #fff;box-shadow:0 2px 8px rgba(0,0,0,0.3);"><svg width="12" height="12" viewBox="0 0 24 24" fill="white"><rect x="6" y="6" width="12" height="12" rx="2"/></svg></div>',
    iconSize: [28, 28],
    iconAnchor: [14, 14]
  });

  constructor(
    private api: ApiService,
    private signalr: SignalRService
  ) {}

  ngOnInit() {
    // Ensure SignalR is connected (may already be started by dashboard)
    this.signalr.startConnection();

    this.subs.push(
      this.signalr.positionUpdate$.subscribe(pos => {
        this.updateMarker(pos);
      })
    );
  }

  ngAfterViewInit() {
    setTimeout(() => this.initMap(), 300);
  }

  ngOnDestroy() {
    this.subs.forEach(s => s.unsubscribe());
    if (this.map) {
      this.map.remove();
    }
  }

  private initMap() {
    this.map = L.map('monitoring-map', {
      center: [34.0, 9.0], // Tunisia center
      zoom: 7,
      zoomControl: false,
      attributionControl: false
    });

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19
    }).addTo(this.map);

    // Load initial positions
    this.loadPositions();
  }

  private loadPositions() {
    this.api.getLastPositions().subscribe({
      next: (positions) => {
        (positions || []).filter((p: any) => p.lastPosition).forEach((p: any) => {
          const pos: PositionUpdate = {
            deviceId: p.deviceId || 0,
            deviceUid: p.deviceUid || '',
            vehicleId: p.vehicleId,
            vehicleName: p.vehicleName || 'Véhicule',
            plate: p.plate || '',
            latitude: p.lastPosition.latitude,
            longitude: p.lastPosition.longitude,
            speedKph: p.lastPosition.speedKph || 0,
            courseDeg: p.lastPosition.courseDeg || 0,
            ignitionOn: p.lastPosition.ignitionOn ?? false,
            isMoving: (p.lastPosition.speedKph || 0) > 3,
            recordedAt: p.lastPosition.recordedAt || '',
            timestamp: p.lastPosition.recordedAt || ''
          };
          this.updateMarker(pos);
        });
        this.vehicleCount = this.markers.size;
        this.centerOnAll();
      }
    });
  }

  private updateMarker(pos: PositionUpdate) {
    if (!this.map || !pos.latitude || !pos.longitude) return;

    const icon = pos.isMoving ? this.movingIcon : this.stoppedIcon;
    const latlng = L.latLng(pos.latitude, pos.longitude);

    if (this.markers.has(pos.vehicleId)) {
      const marker = this.markers.get(pos.vehicleId)!;
      marker.setLatLng(latlng);
      marker.setIcon(icon);
      (marker as any)._posData = pos;
    } else {
      const marker = L.marker(latlng, { icon })
        .addTo(this.map)
        .bindTooltip(pos.vehicleName, { direction: 'top', offset: [0, -16] });

      marker.on('click', () => {
        this.selectedVehicle = (marker as any)._posData || pos;
      });

      (marker as any)._posData = pos;
      this.markers.set(pos.vehicleId, marker);
    }
    this.vehicleCount = this.markers.size;
  }

  centerOnAll() {
    if (!this.map || this.markers.size === 0) return;
    const bounds = L.latLngBounds([]);
    this.markers.forEach(m => bounds.extend(m.getLatLng()));
    this.map.fitBounds(bounds, { padding: [40, 40], maxZoom: 14 });
  }

  closeSheet() {
    this.selectedVehicle = null;
  }
}
