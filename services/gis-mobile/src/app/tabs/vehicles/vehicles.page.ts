import { Component, OnInit, OnDestroy } from '@angular/core';
import { Router, ActivatedRoute } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { SignalRService, PositionUpdate } from '../../core/services/signalr.service';
import { Vehicle } from '../../core/models/types';
import { ModalController } from '@ionic/angular';

@Component({
  selector: 'app-vehicles',
  standalone: false,
  template: `
    <ion-header>
      <ion-toolbar color="primary">
        <ion-title>Véhicules</ion-title>
        <ion-buttons slot="end">
          <ion-button (click)="toggleFilter()">
            <ion-icon name="filter-outline" slot="icon-only"></ion-icon>
          </ion-button>
        </ion-buttons>
      </ion-toolbar>
      <ion-toolbar *ngIf="showSearch">
        <ion-searchbar
          placeholder="Rechercher un véhicule..."
          [(ngModel)]="searchTerm"
          (ionInput)="filterVehicles()"
          [debounce]="300"
        ></ion-searchbar>
      </ion-toolbar>
    </ion-header>

    <ion-content [fullscreen]="true">
      <ion-refresher slot="fixed" (ionRefresh)="onRefresh($event)">
        <ion-refresher-content pullingText="Tirer pour rafraîchir"></ion-refresher-content>
      </ion-refresher>

      <!-- Filter chips -->
      <div class="filter-chips" *ngIf="showFilter">
        <ion-chip [color]="activeFilter === 'all' ? 'primary' : 'medium'" (click)="setFilter('all')">
          <ion-label>Tous ({{ vehicles.length }})</ion-label>
        </ion-chip>
        <ion-chip [color]="activeFilter === 'moving' ? 'success' : 'medium'" (click)="setFilter('moving')">
          <ion-icon name="navigate"></ion-icon>
          <ion-label>En mouvement ({{ movingCount }})</ion-label>
        </ion-chip>
        <ion-chip [color]="activeFilter === 'stopped' ? 'warning' : 'medium'" (click)="setFilter('stopped')">
          <ion-icon name="stop-circle"></ion-icon>
          <ion-label>À l'arrêt ({{ stoppedCount }})</ion-label>
        </ion-chip>
        <ion-chip [color]="activeFilter === 'offline' ? 'danger' : 'medium'" (click)="setFilter('offline')">
          <ion-icon name="cloud-offline"></ion-icon>
          <ion-label>Hors ligne ({{ offlineCount }})</ion-label>
        </ion-chip>
      </div>

      <!-- Vehicle list -->
      <ion-list>
        <ion-item *ngFor="let v of filteredVehicles; trackBy: trackById" detail (click)="selectVehicle(v)">
          <div slot="start" class="vehicle-status-dot" [ngClass]="getStatusClass(v)"></div>
          <ion-label>
            <h2 class="vehicle-name">{{ v.name }}</h2>
            <p class="vehicle-info">
              <span>{{ v.plate }}</span>
              <span *ngIf="v.brand"> &middot; {{ v.brand }} {{ v.model }}</span>
            </p>
            <p class="vehicle-location" *ngIf="v.lastAddress">
              <ion-icon name="location-outline" class="inline-icon"></ion-icon>
              {{ v.lastAddress }}
            </p>
          </ion-label>
          <div slot="end" class="vehicle-speed" *ngIf="v.currentSpeed !== undefined && v.currentSpeed !== null">
            <span class="speed-value">{{ v.currentSpeed | number:'1.0-0' }}</span>
            <span class="speed-unit">km/h</span>
          </div>
        </ion-item>
      </ion-list>

      <div class="empty-state" *ngIf="filteredVehicles.length === 0 && !loading">
        <ion-icon name="car-sport-outline"></ion-icon>
        <p>Aucun véhicule trouvé</p>
      </div>

      <ion-spinner *ngIf="loading" name="crescent" class="center-spinner"></ion-spinner>

      <!-- Vehicle detail modal (bottom sheet style) -->
      <div class="detail-sheet" *ngIf="selectedVehicle" (click)="closeDetail($event)">
        <div class="detail-card" (click)="$event.stopPropagation()">
          <div class="sheet-handle"></div>

          <div class="detail-header">
            <div class="detail-status" [ngClass]="getStatusClass(selectedVehicle)">
              <ion-icon [name]="getStatusIcon(selectedVehicle)"></ion-icon>
            </div>
            <div class="detail-title">
              <h2>{{ selectedVehicle.name }}</h2>
              <p>{{ selectedVehicle.plate }} &middot; {{ selectedVehicle.brand }} {{ selectedVehicle.model }}</p>
            </div>
            <ion-button fill="clear" (click)="selectedVehicle = null">
              <ion-icon name="close" slot="icon-only"></ion-icon>
            </ion-button>
          </div>

          <div class="detail-grid">
            <div class="detail-item">
              <ion-icon name="speedometer-outline" color="primary"></ion-icon>
              <span class="detail-value">{{ selectedVehicle.currentSpeed || 0 }} km/h</span>
              <span class="detail-label">Vitesse</span>
            </div>
            <div class="detail-item">
              <ion-icon name="navigate-outline" color="success"></ion-icon>
              <span class="detail-value">{{ selectedVehicle.mileage | number:'1.0-0' }} km</span>
              <span class="detail-label">Kilométrage</span>
            </div>
            <div class="detail-item">
              <ion-icon name="flash-outline" [color]="selectedVehicle.ignitionOn ? 'success' : 'medium'"></ion-icon>
              <span class="detail-value">{{ selectedVehicle.ignitionOn ? 'ON' : 'OFF' }}</span>
              <span class="detail-label">Contact</span>
            </div>
            <div class="detail-item">
              <ion-icon name="car-outline" color="tertiary"></ion-icon>
              <span class="detail-value">{{ selectedVehicle.type }}</span>
              <span class="detail-label">Type</span>
            </div>
          </div>

          <div class="detail-driver" *ngIf="selectedVehicle.assignedDriverName">
            <ion-icon name="person-outline" color="primary"></ion-icon>
            <span>{{ selectedVehicle.assignedDriverName }}</span>
          </div>

          <div class="detail-actions">
            <ion-button expand="block" fill="outline" shape="round" (click)="locateOnMap(selectedVehicle)">
              <ion-icon name="map-outline" slot="start"></ion-icon>
              Localiser
            </ion-button>
            <ion-button expand="block" fill="solid" color="primary" shape="round" (click)="openPlayback(selectedVehicle)">
              <ion-icon name="play-circle-outline" slot="start"></ion-icon>
              Playback
            </ion-button>
          </div>
        </div>
      </div>
    </ion-content>
  `,
  styles: [`
    .filter-chips {
      display: flex; gap: 6px; padding: 8px 12px;
      overflow-x: auto; white-space: nowrap;
    }
    .filter-chips ion-chip { font-size: 12px; }
    .vehicle-status-dot {
      width: 12px; height: 12px; border-radius: 50%;
      margin-right: 4px; flex-shrink: 0;
    }
    .vehicle-status-dot.moving { background: #10b981; }
    .vehicle-status-dot.stopped { background: #f59e0b; }
    .vehicle-status-dot.offline { background: #ef4444; }
    .vehicle-status-dot.available { background: #6b7280; }
    .vehicle-name { font-weight: 600; font-size: 15px; }
    .vehicle-info { font-size: 13px; }
    .vehicle-location { font-size: 12px; color: var(--ion-color-medium); display: flex; align-items: center; gap: 2px; }
    .inline-icon { font-size: 12px; }
    .vehicle-speed { text-align: center; }
    .speed-value { display: block; font-size: 18px; font-weight: 700; color: var(--ion-color-primary); }
    .speed-unit { font-size: 10px; color: var(--ion-color-medium); }
    .empty-state { text-align: center; padding: 40px 20px; color: var(--ion-color-medium); }
    .empty-state ion-icon { font-size: 48px; display: block; margin: 0 auto 12px; }
    .center-spinner { display: block; margin: 40px auto; }
    .detail-sheet {
      position: fixed; top: 0; left: 0; right: 0; bottom: 0;
      z-index: 9999; background: rgba(0,0,0,0.4);
      display: flex; align-items: flex-end;
    }
    .detail-card {
      background: var(--ion-card-background, #fff);
      border-radius: 20px 20px 0 0;
      padding: 12px 20px 32px;
      width: 100%;
      max-height: 70vh; overflow-y: auto;
    }
    .sheet-handle {
      width: 40px; height: 4px; background: var(--ion-color-light-shade);
      border-radius: 2px; margin: 0 auto 16px;
    }
    .detail-header { display: flex; align-items: center; gap: 12px; margin-bottom: 20px; }
    .detail-status {
      width: 48px; height: 48px; border-radius: 50%;
      display: flex; align-items: center; justify-content: center;
    }
    .detail-status.moving { background: rgba(16,185,129,0.15); color: #10b981; }
    .detail-status.stopped { background: rgba(245,158,11,0.15); color: #f59e0b; }
    .detail-status.offline { background: rgba(239,68,68,0.15); color: #ef4444; }
    .detail-status.available { background: rgba(107,114,128,0.15); color: #6b7280; }
    .detail-status ion-icon { font-size: 24px; }
    .detail-title { flex: 1; }
    .detail-title h2 { margin: 0; font-size: 18px; font-weight: 700; }
    .detail-title p { margin: 2px 0 0; font-size: 13px; color: var(--ion-color-medium); }
    .detail-grid {
      display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-bottom: 16px;
    }
    .detail-item {
      background: var(--ion-color-light); border-radius: 12px;
      padding: 12px; text-align: center;
    }
    .detail-item ion-icon { font-size: 20px; display: block; margin: 0 auto 4px; }
    .detail-value { display: block; font-weight: 600; font-size: 14px; }
    .detail-label { display: block; font-size: 11px; color: var(--ion-color-medium); }
    .detail-driver {
      display: flex; align-items: center; gap: 8px;
      padding: 10px 14px; background: var(--ion-color-light);
      border-radius: 10px; margin-bottom: 16px; font-size: 14px;
    }
    .detail-actions { display: flex; gap: 8px; }
    .detail-actions ion-button { flex: 1; }
  `]
})
export class VehiclesPage implements OnInit, OnDestroy {
  vehicles: Vehicle[] = [];
  filteredVehicles: Vehicle[] = [];
  selectedVehicle: Vehicle | null = null;
  loading = true;
  searchTerm = '';
  showSearch = true;
  showFilter = false;
  activeFilter = 'all';
  movingCount = 0;
  stoppedCount = 0;
  offlineCount = 0;

  private subs: Subscription[] = [];
  private positionMap = new Map<number, PositionUpdate>();

  constructor(
    private api: ApiService,
    private signalr: SignalRService,
    private router: Router,
    private route: ActivatedRoute
  ) {}

  ngOnInit() {
    this.loadVehicles();

    // Update vehicles with real-time positions
    this.subs.push(
      this.signalr.positionUpdate$.subscribe(pos => {
        this.positionMap.set(pos.vehicleId, pos);
        const v = this.vehicles.find(v => v.id === pos.vehicleId.toString());
        if (v) {
          v.currentSpeed = pos.speedKph;
          v.currentLocation = { lat: pos.latitude, lng: pos.longitude };
          v.ignitionOn = pos.ignitionOn;
          v.isOnline = true;
          this.updateCounts();
        }
      })
    );
  }

  ngOnDestroy() {
    this.subs.forEach(s => s.unsubscribe());
  }

  loadVehicles() {
    this.loading = true;
    this.api.getVehicles().subscribe({
      next: (vehicles) => {
        this.vehicles = Array.isArray(vehicles) ? vehicles : [];
        this.updateCounts();
        this.filterVehicles();
        this.loading = false;
      },
      error: () => {
        this.loading = false;
      }
    });
  }

  updateCounts() {
    this.movingCount = this.vehicles.filter(v => v.isOnline && (v.currentSpeed || 0) > 3).length;
    this.stoppedCount = this.vehicles.filter(v => v.isOnline && (v.currentSpeed || 0) <= 3).length;
    this.offlineCount = this.vehicles.filter(v => !v.isOnline).length;
  }

  filterVehicles() {
    let result = [...this.vehicles];

    if (this.searchTerm) {
      const term = this.searchTerm.toLowerCase();
      result = result.filter(v =>
        v.name?.toLowerCase().includes(term) ||
        v.plate?.toLowerCase().includes(term) ||
        v.brand?.toLowerCase().includes(term)
      );
    }

    switch (this.activeFilter) {
      case 'moving':
        result = result.filter(v => v.isOnline && (v.currentSpeed || 0) > 3);
        break;
      case 'stopped':
        result = result.filter(v => v.isOnline && (v.currentSpeed || 0) <= 3);
        break;
      case 'offline':
        result = result.filter(v => !v.isOnline);
        break;
    }

    this.filteredVehicles = result;
  }

  setFilter(filter: string) {
    this.activeFilter = filter;
    this.filterVehicles();
  }

  toggleFilter() {
    this.showFilter = !this.showFilter;
  }

  selectVehicle(v: Vehicle) {
    this.selectedVehicle = v;
  }

  closeDetail(event: Event) {
    this.selectedVehicle = null;
  }

  locateOnMap(v: Vehicle) {
    this.selectedVehicle = null;
    if (v.currentLocation) {
      this.router.navigate(['/tabs/monitoring'], {
        queryParams: { lat: v.currentLocation.lat, lng: v.currentLocation.lng, vehicleId: v.id }
      });
    } else {
      this.router.navigate(['/tabs/monitoring']);
    }
  }

  openPlayback(v: Vehicle) {
    this.selectedVehicle = null;
    this.router.navigate(['/tabs/playback'], {
      queryParams: { vehicleId: v.id, vehicleName: v.name }
    });
  }

  async onRefresh(event: any) {
    this.loadVehicles();
    setTimeout(() => event.target.complete(), 1500);
  }

  getStatusClass(v: Vehicle): string {
    if (v.isOnline && (v.currentSpeed || 0) > 3) return 'moving';
    if (v.isOnline) return 'stopped';
    if (v.status === 'maintenance') return 'offline';
    return 'available';
  }

  getStatusIcon(v: Vehicle): string {
    if (v.isOnline && (v.currentSpeed || 0) > 3) return 'navigate';
    if (v.isOnline) return 'stop-circle';
    return 'cloud-offline';
  }

  trackById(_: number, v: Vehicle) {
    return v.id;
  }
}
