import { Component, OnInit, OnDestroy } from '@angular/core';
import { Subscription } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { SignalRService, PositionUpdate } from '../../core/services/signalr.service';
import { AuthService } from '../../core/services/auth.service';
import { Router } from '@angular/router';

@Component({
  selector: 'app-dashboard',
  standalone: false,
  template: `
    <ion-header>
      <ion-toolbar color="primary">
        <ion-title>Tableau de bord</ion-title>
        <ion-buttons slot="end">
          <ion-button (click)="goToProfile()">
            <ion-icon name="person-circle-outline" slot="icon-only"></ion-icon>
          </ion-button>
        </ion-buttons>
      </ion-toolbar>
    </ion-header>

    <ion-content [fullscreen]="true">
      <ion-refresher slot="fixed" (ionRefresh)="onRefresh($event)">
        <ion-refresher-content pullingText="Tirer pour rafraîchir"></ion-refresher-content>
      </ion-refresher>

      <!-- Welcome card -->
      <div class="welcome-card">
        <h2>Bonjour, {{ userName }} 👋</h2>
        <p>{{ companyName }}</p>
        <div class="connection-badge" [class.connected]="isConnected" [class.disconnected]="!isConnected">
          <ion-icon [name]="isConnected ? 'wifi' : 'wifi-outline'"></ion-icon>
          {{ isConnected ? 'Temps réel actif' : 'Hors ligne' }}
        </div>
      </div>

      <!-- Stats Grid -->
      <div class="stats-grid">
        <div class="stat-card" (click)="goToTab('vehicles')">
          <div class="stat-icon" style="background: rgba(59,130,246,0.15);">
            <ion-icon name="car-sport" style="color: #3b82f6;"></ion-icon>
          </div>
          <div class="stat-value">{{ totalVehicles }}</div>
          <div class="stat-label">Véhicules</div>
        </div>

        <div class="stat-card" (click)="goToTab('monitoring')">
          <div class="stat-icon" style="background: rgba(16,185,129,0.15);">
            <ion-icon name="navigate" style="color: #10b981;"></ion-icon>
          </div>
          <div class="stat-value">{{ activeVehicles }}</div>
          <div class="stat-label">En mouvement</div>
        </div>

        <div class="stat-card">
          <div class="stat-icon" style="background: rgba(245,158,11,0.15);">
            <ion-icon name="build" style="color: #f59e0b;"></ion-icon>
          </div>
          <div class="stat-value">{{ inMaintenance }}</div>
          <div class="stat-label">En maintenance</div>
        </div>

        <div class="stat-card" (click)="goToTab('alerts')">
          <div class="stat-icon" style="background: rgba(239,68,68,0.15);">
            <ion-icon name="warning" style="color: #ef4444;"></ion-icon>
          </div>
          <div class="stat-value">{{ alertCount }}</div>
          <div class="stat-label">Alertes</div>
        </div>
      </div>

      <!-- Distance today -->
      <ion-card class="info-card">
        <ion-card-content>
          <div class="info-row">
            <div class="info-item">
              <ion-icon name="speedometer-outline" color="primary"></ion-icon>
              <div>
                <span class="info-value">{{ distanceToday | number:'1.0-0' }} km</span>
                <span class="info-label">Distance aujourd'hui</span>
              </div>
            </div>
            <div class="info-item">
              <ion-icon name="time-outline" color="tertiary"></ion-icon>
              <div>
                <span class="info-value">{{ stoppedVehicles }}</span>
                <span class="info-label">À l'arrêt</span>
              </div>
            </div>
          </div>
        </ion-card-content>
      </ion-card>

      <!-- Recent vehicles activity -->
      <div class="section-header">
        <h3>Activité récente</h3>
        <ion-button fill="clear" size="small" (click)="goToTab('monitoring')">
          Voir tout
          <ion-icon name="chevron-forward" slot="end"></ion-icon>
        </ion-button>
      </div>

      <ion-list *ngIf="recentPositions.length > 0">
        <ion-item *ngFor="let pos of recentPositions; trackBy: trackByVehicleId" detail (click)="goToVehicle(pos.vehicleId)">
          <div class="vehicle-avatar" slot="start" [class.moving]="pos.isMoving" [class.stopped]="!pos.isMoving">
            <ion-icon [name]="pos.isMoving ? 'navigate' : 'stop-circle'"></ion-icon>
          </div>
          <ion-label>
            <h2>{{ pos.vehicleName }}</h2>
            <p>{{ pos.plate }} &middot; {{ pos.speedKph | number:'1.0-0' }} km/h</p>
          </ion-label>
          <ion-note slot="end" color="medium">
            <ion-icon [name]="pos.ignitionOn ? 'flash' : 'flash-off'" [color]="pos.ignitionOn ? 'success' : 'medium'"></ion-icon>
          </ion-note>
        </ion-item>
      </ion-list>

      <div class="empty-state" *ngIf="recentPositions.length === 0 && !loading">
        <ion-icon name="analytics-outline"></ion-icon>
        <p>Aucune activité récente</p>
      </div>

      <ion-spinner *ngIf="loading" name="crescent" class="center-spinner"></ion-spinner>
    </ion-content>
  `,
  styles: [`
    .welcome-card {
      background: linear-gradient(135deg, var(--ion-color-primary), var(--ion-color-primary-shade));
      color: #fff;
      padding: 24px 20px 20px;
      margin: 0;
    }
    .welcome-card h2 { margin: 0 0 4px; font-size: 20px; font-weight: 700; }
    .welcome-card p { margin: 0; opacity: 0.8; font-size: 14px; }
    .connection-badge {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      margin-top: 12px;
      padding: 4px 12px;
      border-radius: 20px;
      font-size: 12px;
      font-weight: 500;
    }
    .connection-badge.connected { background: rgba(16,185,129,0.3); }
    .connection-badge.disconnected { background: rgba(239,68,68,0.3); }
    .stats-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
      padding: 16px;
    }
    .stat-card {
      background: var(--ion-card-background, #fff);
      border-radius: 16px;
      padding: 16px;
      box-shadow: 0 2px 8px rgba(0,0,0,0.06);
      cursor: pointer;
    }
    .stat-icon {
      width: 40px; height: 40px;
      border-radius: 12px;
      display: flex; align-items: center; justify-content: center;
      margin-bottom: 8px;
    }
    .stat-icon ion-icon { font-size: 20px; }
    .stat-value { font-size: 24px; font-weight: 700; color: var(--ion-text-color); }
    .stat-label { font-size: 12px; color: var(--ion-color-medium); margin-top: 2px; }
    .info-card { margin: 0 16px; border-radius: 16px; }
    .info-row { display: flex; gap: 20px; }
    .info-item {
      display: flex; align-items: center; gap: 10px; flex: 1;
    }
    .info-item ion-icon { font-size: 24px; }
    .info-value { display: block; font-weight: 600; font-size: 16px; }
    .info-label { display: block; font-size: 11px; color: var(--ion-color-medium); }
    .section-header {
      display: flex; justify-content: space-between; align-items: center;
      padding: 16px 16px 4px;
    }
    .section-header h3 { margin: 0; font-size: 16px; font-weight: 600; }
    .vehicle-avatar {
      width: 36px; height: 36px; border-radius: 50%;
      display: flex; align-items: center; justify-content: center;
    }
    .vehicle-avatar.moving { background: rgba(16,185,129,0.15); color: #10b981; }
    .vehicle-avatar.stopped { background: rgba(156,163,175,0.15); color: #9ca3af; }
    .vehicle-avatar ion-icon { font-size: 18px; }
    .empty-state {
      text-align: center; padding: 40px 20px; color: var(--ion-color-medium);
    }
    .empty-state ion-icon { font-size: 48px; display: block; margin: 0 auto 12px; }
    .center-spinner { display: block; margin: 40px auto; }
  `]
})
export class DashboardPage implements OnInit, OnDestroy {
  userName = '';
  companyName = '';
  isConnected = false;
  loading = true;

  totalVehicles = 0;
  activeVehicles = 0;
  inMaintenance = 0;
  alertCount = 0;
  distanceToday = 0;
  stoppedVehicles = 0;

  recentPositions: PositionUpdate[] = [];
  private subs: Subscription[] = [];

  constructor(
    private api: ApiService,
    private signalr: SignalRService,
    private authService: AuthService,
    private router: Router
  ) {}

  ngOnInit() {
    const user = this.authService.getCurrentUserSync();
    if (user) {
      this.userName = user.name?.split(' ')[0] || 'Utilisateur';
      this.companyName = user.companyName || '';
    }

    this.subs.push(
      this.signalr.connectionState$.subscribe(state => {
        this.isConnected = state === 'Connected';
      })
    );

    this.subs.push(
      this.signalr.positionUpdate$.subscribe(pos => {
        const idx = this.recentPositions.findIndex(p => p.vehicleId === pos.vehicleId);
        if (idx >= 0) {
          this.recentPositions[idx] = pos;
        } else {
          this.recentPositions.unshift(pos);
          if (this.recentPositions.length > 10) this.recentPositions.pop();
        }
        this.activeVehicles = this.recentPositions.filter(p => p.isMoving).length;
        this.stoppedVehicles = this.recentPositions.filter(p => !p.isMoving).length;
      })
    );

    this.subs.push(
      this.signalr.notification$.subscribe(() => {
        this.alertCount++;
      })
    );

    this.loadData();
    this.signalr.startConnection();
  }

  ngOnDestroy() {
    this.subs.forEach(s => s.unsubscribe());
  }

  loadData() {
    this.loading = true;

    this.api.getDashboardStats().subscribe({
      next: (data: any) => {
        this.totalVehicles = data.vehicles?.total || 0;
        this.activeVehicles = data.vehicles?.online || 0;
        this.inMaintenance = data.maintenance?.upcoming || 0;
        this.alertCount = data.alerts?.unresolved || 0;
        this.distanceToday = data.trips?.distanceToday || 0;
        this.stoppedVehicles = (data.vehicles?.withGps || 0) - (data.vehicles?.online || 0);
        this.loading = false;
      },
      error: () => {
        this.loading = false;
      }
    });

    this.api.getLastPositions().subscribe({
      next: (positions) => {
        this.recentPositions = (positions || [])
          .filter((p: any) => p.lastPosition)
          .slice(0, 10)
          .map((p: any) => ({
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
          }));
      }
    });
  }

  async onRefresh(event: any) {
    this.loadData();
    setTimeout(() => event.target.complete(), 1500);
  }

  goToProfile() {
    this.router.navigate(['/tabs/profile']);
  }

  goToTab(tab: string) {
    this.router.navigate([`/tabs/${tab}`]);
  }

  goToVehicle(vehicleId: number) {
    this.router.navigate(['/tabs/vehicles'], { queryParams: { id: vehicleId } });
  }

  trackByVehicleId(_: number, item: PositionUpdate) {
    return item.vehicleId;
  }
}
