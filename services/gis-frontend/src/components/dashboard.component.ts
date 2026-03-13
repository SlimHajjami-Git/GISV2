import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { ApiService } from '../services/api.service';
import { Vehicle, Company } from '../models/types';
import { AppLayoutComponent } from './shared/app-layout.component';
import { DateFilterBarComponent, CardComponent, LegendItemComponent } from './shared/ui';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent, DateFilterBarComponent, CardComponent, LegendItemComponent],
  template: `
    <app-layout>
      <div class="db">
        <ui-date-filter-bar
          [selectedPeriod]="selectedPeriod"
          [fromDate]="fromDate"
          [toDate]="toDate"
          (periodChange)="onPeriodChange($event)"
          (dateRangeChange)="onDateRangeChange($event)"
          (applyFilter)="applyFilter()">
        </ui-date-filter-bar>

        <!-- Row 1 -->
        <div class="row">
          <div class="panel w1">
            <h3>Etat des vehicules <span class="badge-live">TEMPS REEL</span></h3>
            <div class="motion-box">
              <div class="pie-wrap">
                <svg viewBox="0 0 100 100"><circle cx="50" cy="50" r="45" fill="#84cc16"/>
                  <path [attr.d]="getSlice(0, motionData.stationary)" fill="#ef4444"/>
                  <path [attr.d]="getSlice(motionData.stationary, motionData.stationary + motionData.ignitionOn)" fill="#f97316"/>
                </svg>
                <span class="pie-num">{{ totalMotion }}</span>
              </div>
              <div class="legend">
                <div><i class="c-red"></i>A l'arret <b>{{ motionData.stationary }}</b></div>
                <div><i class="c-orange"></i>Arret moteur allume <b>{{ motionData.ignitionOn }}</b></div>
                <div><i class="c-lime"></i>En mouvement <b>{{ motionData.movingIgnition }}</b></div>
                <div><i class="c-gray"></i>En maintenance <b>{{ motionData.noState }}</b></div>
                <div><i class="c-lgray"></i>Sans GPS <b>{{ motionData.noCoords }}</b></div>
              </div>
            </div>
          </div>

          <div class="panel w2">
            <h3>Consommation carburant flotte <span class="tag-green">{{ totalFuelConsumed | number:'1.0-0' }} L</span></h3>
            <div class="chart-box">
              <svg viewBox="0 0 500 160" preserveAspectRatio="none" class="chart-svg">
                <line *ngFor="let y of [0,40,80,120,160]" x1="0" [attr.y1]="y" x2="500" [attr.y2]="y" stroke="#f1f5f9" stroke-width="1"/>
                <polyline fill="none" stroke="#22c55e" stroke-width="2.5" stroke-linejoin="round" [attr.points]="fuelChartPoints"/>
              </svg>
              <div class="chart-labels"><span *ngFor="let l of fuelChartLabels">{{ l }}</span></div>
            </div>
          </div>
        </div>

        <!-- Row 2 -->
        <div class="row row3">
          <div class="panel">
            <h3>Depenses</h3>
            <div class="exp-box">
              <div class="pie-wrap pie-sm">
                <svg viewBox="0 0 100 100"><circle cx="50" cy="50" r="45" fill="#94a3b8"/>
                  <path [attr.d]="getExpenseSlice(0, fuelCost)" fill="#3b82f6"/>
                  <path [attr.d]="getExpenseSlice(fuelCost, fuelCost + maintenanceCost)" fill="#10b981"/>
                  <path [attr.d]="getExpenseSlice(fuelCost + maintenanceCost, fuelCost + maintenanceCost + repairCost)" fill="#f59e0b"/>
                </svg>
                <span class="pie-num sm">{{ totalCost | number:'1.0-0' }}</span>
              </div>
              <div class="legend">
                <div><i class="c-blue"></i>Carburant <b>{{ fuelCost | number:'1.0-0' }} DT</b></div>
                <div><i class="c-green"></i>Entretien <b>{{ maintenanceCost | number:'1.0-0' }} DT</b></div>
                <div><i class="c-yellow"></i>Reparation <b>{{ repairCost | number:'1.0-0' }} DT</b></div>
                <div><i class="c-lgray"></i>Autres <b>{{ otherCost | number:'1.0-0' }} DT</b></div>
              </div>
            </div>
          </div>

          <div class="panel">
            <h3>Geozones <span class="badge-live">TEMPS REEL</span></h3>
            <div class="geo-list" *ngIf="geofences.length > 0">
              <div *ngFor="let g of geofences" class="geo-row">
                <span class="geo-dot" [style.background]="g.color"></span>
                <span class="geo-name">{{ g.name }}</span>
                <b>{{ g.count }}</b>
              </div>
            </div>
            <div class="empty" *ngIf="geofences.length === 0">Aucune geozone</div>
          </div>

          <div class="panel">
            <h3>Sante vehicules <span class="badge-live">TEMPS REEL</span></h3>
            <div class="health-box">
              <div class="pie-wrap pie-sm">
                <svg viewBox="0 0 100 100"><circle cx="50" cy="50" r="45" fill="#22c55e"/>
                  <path [attr.d]="getHealthSlice(0, healthData.attention)" fill="#eab308"/>
                  <path [attr.d]="getHealthSlice(healthData.attention, healthData.attention + healthData.unhealthy)" fill="#ef4444"/>
                </svg>
                <span class="pie-num sm">{{ totalHealth }}</span>
              </div>
              <div class="legend">
                <div><i class="c-green"></i>En bon etat <b>{{ healthData.healthy }}</b></div>
                <div><i class="c-yellow"></i>Attention <b>{{ healthData.attention }}</b></div>
                <div><i class="c-red"></i>Mauvais etat <b>{{ healthData.unhealthy }}</b></div>
              </div>
            </div>
          </div>
        </div>

        <!-- Row 3 -->
        <div class="row">
          <div class="panel w1">
            <h3>Scores de conduite</h3>
            <div class="score-list" *ngIf="drivingScores.length > 0">
              <div *ngFor="let s of drivingScores; let i = index" class="score-row">
                <span class="score-rank">{{ i + 1 }}</span>
                <span class="score-plate">{{ s.plate }}</span>
                <div class="score-track"><div class="score-fill" [style.width.%]="s.score" [style.background]="getScoreColor(s.score)"></div></div>
                <b [style.color]="getScoreColor(s.score)">{{ s.score }}</b>
              </div>
            </div>
            <div class="empty" *ngIf="drivingScores.length === 0">Aucun score</div>
          </div>

          <div class="panel w2">
            <h3>Kilometrage par vehicule</h3>
            <div class="km-list">
              <div *ngFor="let u of topUnits" class="km-row">
                <span class="km-dot" [style.background]="u.color"></span>
                <span class="km-name">{{ u.name }}</span>
                <div class="km-track"><div class="km-fill" [style.width.%]="(u.mileage / maxMileage) * 100" [style.background]="u.color"></div></div>
                <span class="km-val">{{ u.mileage | number:'1.0-0' }} km</span>
              </div>
            </div>
          </div>
        </div>

        <!-- Row 4 -->
        <div class="row">
          <div class="panel w2">
            <h3>Consommation par vehicule <span class="tag-muted">(L/100 km)</span></h3>
            <div class="fv-grid" *ngIf="vehicleFuelStats.length > 0">
              <div *ngFor="let v of vehicleFuelStats" class="fv-row">
                <span class="fv-plate">{{ v.plate }}</span>
                <div class="fv-track"><div class="fv-fill" [style.width.%]="(v.consumption / maxFuelConsumption) * 100" [style.background]="getFuelColor(v.consumption)"></div></div>
                <b class="fv-val">{{ v.consumption | number:'1.1-1' }}</b>
                <span class="fv-extra">{{ v.totalLiters | number:'1.0-0' }} L</span>
                <span class="fv-extra">{{ v.totalKm | number:'1.0-0' }} km</span>
              </div>
            </div>
            <div class="empty" *ngIf="vehicleFuelStats.length === 0">Aucune donnee</div>
          </div>

          <div class="panel w1">
            <h3>Alertes recentes <span class="badge-alert">{{ alerts.length }}</span></h3>
            <div class="alert-list" *ngIf="alerts.length > 0">
              <div *ngFor="let a of alerts" class="alert-row">
                <span class="alert-icon" [class.alert-warn]="a.severity === 'warning'" [class.alert-danger]="a.severity === 'danger'" [class.alert-info]="a.severity === 'info'">&#9679;</span>
                <div class="alert-body">
                  <span class="alert-msg">{{ a.message }}</span>
                  <span class="alert-time">{{ a.time }}</span>
                </div>
              </div>
            </div>
            <div class="empty" *ngIf="alerts.length === 0">Aucune alerte</div>
          </div>
        </div>

        <!-- Row 5 -->
        <div class="row">
          <div class="panel w2">
            <h3>Derniers trajets</h3>
            <div class="trip-list" *ngIf="recentTrips.length > 0">
              <div *ngFor="let t of recentTrips" class="trip-row">
                <span class="trip-icon">&#9654;</span>
                <div class="trip-body">
                  <span class="trip-plate">{{ t.plate }}</span>
                  <span class="trip-detail">{{ t.distance }} km &middot; {{ t.duration }}</span>
                </div>
                <span class="trip-date">{{ t.date }}</span>
              </div>
            </div>
            <div class="empty" *ngIf="recentTrips.length === 0">Aucun trajet</div>
          </div>

          <div class="panel w1">
            <h3>Conducteurs <span class="tag-muted">{{ drivers.length }} actifs</span></h3>
            <div class="driver-list" *ngIf="drivers.length > 0">
              <div *ngFor="let d of drivers" class="driver-row">
                <span class="driver-avatar">{{ d.initials }}</span>
                <div class="driver-body">
                  <span class="driver-name">{{ d.name }}</span>
                  <span class="driver-vehicle">{{ d.vehicle || 'Non assigne' }}</span>
                </div>
                <span class="driver-status" [class.active]="d.active">{{ d.active ? 'Actif' : 'Inactif' }}</span>
              </div>
            </div>
            <div class="empty" *ngIf="drivers.length === 0">Aucun conducteur</div>
          </div>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    .db {
      flex: 1; background: #f1f5f9; padding: 0 16px 16px;
      overflow-y: auto; min-height: calc(100vh - 42px);
      display: flex; flex-direction: column;
    }

    .row {
      display: flex; gap: 8px; margin-top: 8px; flex: 1;
    }
    .row3 { }
    .panel {
      background: #fff; border-radius: 8px; padding: 12px 14px;
      box-shadow: 0 1px 2px rgba(0,0,0,.05); flex: 1; min-width: 0;
      display: flex; flex-direction: column;
    }
    .panel h3 + * { flex: 1; }
    .panel.w1 { flex: 1; max-width: 50%; }
    .panel.w2 { flex: 1.5; }
    .panel.w-full { flex: 1; }

    h3 {
      font-size: 13px; font-weight: 600; color: #1e293b;
      margin: 0 0 10px; display: flex; align-items: center; gap: 8px;
    }
    .badge-live {
      font-size: 9px; font-weight: 600; color: #3b82f6;
      letter-spacing: .5px; margin-left: auto;
    }
    .tag-green { font-size: 12px; color: #22c55e; font-weight: 700; margin-left: auto; }
    .tag-muted { font-size: 11px; color: #94a3b8; font-weight: 400; }
    .empty { color: #94a3b8; font-size: 12px; text-align: center; padding: 16px 0; }

    /* Pies */
    .pie-wrap {
      position: relative; width: 100px; height: 100px; flex-shrink: 0;
    }
    .pie-wrap svg { width: 100%; height: 100%; transform: rotate(-90deg); }
    .pie-num {
      position: absolute; top: 50%; left: 50%; transform: translate(-50%,-50%);
      font-size: 18px; font-weight: 700; color: #1e293b;
      background: #fff; width: 46px; height: 46px; border-radius: 50%;
      display: flex; align-items: center; justify-content: center;
    }
    .pie-sm { width: 80px; height: 80px; }
    .pie-num.sm { font-size: 13px; width: 36px; height: 36px; }

    /* Legends */
    .legend { display: flex; flex-direction: column; gap: 4px; font-size: 12px; color: #64748b; }
    .legend div { display: flex; align-items: center; gap: 6px; }
    .legend b { margin-left: auto; color: #1e293b; font-size: 12px; }
    .legend i {
      width: 8px; height: 8px; border-radius: 2px; flex-shrink: 0; display: inline-block;
    }
    .c-red { background: #ef4444; }
    .c-orange { background: #f97316; }
    .c-green { background: #22c55e; }
    .c-lime { background: #84cc16; }
    .c-blue { background: #3b82f6; }
    .c-yellow { background: #eab308; }
    .c-gray { background: #6b7280; }
    .c-lgray { background: #d1d5db; }

    /* Motion */
    .motion-box { display: flex; gap: 14px; align-items: flex-start; }

    /* Expenses */
    .exp-box { display: flex; gap: 12px; align-items: center; }

    /* Health */
    .health-box { display: flex; gap: 12px; align-items: center; }

    /* Chart */
    .chart-box { background: #f8fafc; border-radius: 6px; padding: 8px; }
    .chart-svg { width: 100%; height: 100px; display: block; background: #fff; border-radius: 4px; }
    .chart-labels {
      display: flex; justify-content: space-between;
      font-size: 10px; color: #94a3b8; margin-top: 4px;
    }

    /* Geozones */
    .geo-list { display: flex; flex-direction: column; gap: 2px; }
    .geo-row {
      display: flex; align-items: center; gap: 8px;
      padding: 5px 0; border-bottom: 1px solid #f8fafc; font-size: 12px; color: #64748b;
    }
    .geo-row:last-child { border-bottom: none; }
    .geo-dot { width: 10px; height: 10px; border-radius: 50%; flex-shrink: 0; }
    .geo-name { flex: 1; }
    .geo-row b { color: #1e293b; }

    /* Scores */
    .score-list { display: flex; flex-direction: column; gap: 3px; }
    .score-row { display: flex; align-items: center; gap: 8px; font-size: 12px; }
    .score-rank { color: #94a3b8; font-weight: 700; width: 18px; text-align: center; }
    .score-plate { color: #334155; font-weight: 500; width: 80px; }
    .score-track { flex: 1; height: 8px; background: #f1f5f9; border-radius: 4px; overflow: hidden; }
    .score-fill { height: 100%; border-radius: 4px; }
    .score-row b { width: 28px; text-align: right; font-size: 13px; }

    /* Kilometrage */
    .km-list { display: flex; flex-direction: column; gap: 3px; }
    .km-row { display: flex; align-items: center; gap: 8px; font-size: 12px; }
    .km-dot { width: 8px; height: 8px; border-radius: 50%; flex-shrink: 0; }
    .km-name { color: #64748b; width: 90px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .km-track { flex: 1; height: 14px; background: #f1f5f9; border-radius: 3px; overflow: hidden; }
    .km-fill { height: 100%; border-radius: 3px; }
    .km-val { color: #1e293b; font-weight: 600; font-size: 11px; min-width: 70px; text-align: right; }

    /* Fuel per vehicle */
    .fv-grid { display: flex; flex-direction: column; gap: 2px; }
    .fv-row { display: flex; align-items: center; gap: 8px; font-size: 12px; padding: 5px 0; border-bottom: 1px solid #f8fafc; }
    .fv-row:last-child { border-bottom: none; }
    .fv-plate { color: #334155; font-weight: 500; width: 80px; }
    .fv-track { flex: 1; height: 8px; background: #f1f5f9; border-radius: 4px; overflow: hidden; }
    .fv-fill { height: 100%; border-radius: 4px; }
    .fv-val { color: #1e293b; width: 32px; text-align: right; }
    .fv-extra { color: #94a3b8; font-size: 11px; width: 50px; text-align: right; }

    /* Alerts */
    .badge-alert {
      background: #fef2f2; color: #ef4444; font-size: 10px; font-weight: 700;
      padding: 2px 8px; border-radius: 10px; margin-left: auto;
    }
    .alert-list { display: flex; flex-direction: column; gap: 2px; }
    .alert-row { display: flex; align-items: flex-start; gap: 8px; padding: 6px 0; border-bottom: 1px solid #f8fafc; font-size: 12px; }
    .alert-row:last-child { border-bottom: none; }
    .alert-icon { font-size: 10px; margin-top: 2px; color: #94a3b8; }
    .alert-warn { color: #f59e0b; }
    .alert-danger { color: #ef4444; }
    .alert-info { color: #3b82f6; }
    .alert-body { flex: 1; display: flex; flex-direction: column; }
    .alert-msg { color: #334155; line-height: 1.3; }
    .alert-time { color: #94a3b8; font-size: 10px; margin-top: 1px; }

    /* Trips */
    .trip-list { display: flex; flex-direction: column; gap: 2px; }
    .trip-row { display: flex; align-items: center; gap: 10px; padding: 6px 0; border-bottom: 1px solid #f8fafc; font-size: 12px; }
    .trip-row:last-child { border-bottom: none; }
    .trip-icon { font-size: 10px; color: #3b82f6; }
    .trip-body { flex: 1; display: flex; flex-direction: column; }
    .trip-plate { color: #334155; font-weight: 500; }
    .trip-detail { color: #94a3b8; font-size: 11px; }
    .trip-date { color: #94a3b8; font-size: 10px; white-space: nowrap; }

    /* Drivers */
    .driver-list { display: flex; flex-direction: column; gap: 2px; }
    .driver-row { display: flex; align-items: center; gap: 10px; padding: 6px 0; border-bottom: 1px solid #f8fafc; font-size: 12px; }
    .driver-row:last-child { border-bottom: none; }
    .driver-avatar {
      width: 30px; height: 30px; border-radius: 50%; background: #e0e7ff; color: #4f46e5;
      display: flex; align-items: center; justify-content: center;
      font-size: 11px; font-weight: 700; flex-shrink: 0;
    }
    .driver-body { flex: 1; display: flex; flex-direction: column; }
    .driver-name { color: #334155; font-weight: 500; }
    .driver-vehicle { color: #94a3b8; font-size: 11px; }
    .driver-status {
      font-size: 10px; font-weight: 600; padding: 2px 8px; border-radius: 10px;
      background: #f1f5f9; color: #94a3b8;
    }
    .driver-status.active { background: #f0fdf4; color: #22c55e; }

    /* Responsive */
    @media (max-width: 1000px) {
      .row { flex-direction: column; }
      .panel.w1, .panel.w2 { flex: 1; max-width: 100%; }
    }
  `]
})
export class DashboardComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  vehicles: Vehicle[] = [];
  company: Company | null = null;
  selectedPeriod = 'week';
  fromDate = '';
  toDate = '';

  motionData = { stationary: 0, ignitionOn: 0, moving: 0, movingIgnition: 0, lbs: 0, wifi: 0, noState: 0, noCoords: 0 };
  healthData = { healthy: 0, attention: 0, unhealthy: 0 };
  geofences: { name: string; color: string; count: number }[] = [];
  topUnits: { name: string; color: string; mileage: number }[] = [];
  maxMileage = 1;
  totalFuelConsumed = 0;
  fuelChartPoints = '';
  fuelChartLabels: string[] = [];
  fuelCost = 0; maintenanceCost = 0; repairCost = 0; otherCost = 0; totalCost = 0;
  drivingScores: { plate: string; score: number }[] = [];
  vehicleFuelStats: { plate: string; consumption: number; totalLiters: number; totalKm: number }[] = [];
  maxFuelConsumption = 1;
  alerts: { message: string; severity: string; time: string }[] = [];
  recentTrips: { plate: string; distance: string; duration: string; date: string }[] = [];
  drivers: { name: string; initials: string; vehicle: string; active: boolean }[] = [];

  constructor(private router: Router, private apiService: ApiService, private cdr: ChangeDetectorRef) {}

  ngOnInit() {
    if (!this.apiService.isAuthenticated()) { this.router.navigate(['/login']); return; }
    const user = this.apiService.getCurrentUserSync();
    if (user) {
      this.company = { id: user.companyId.toString(), name: user.companyName, type: 'transport', subscriptionId: '1' } as Company;
    }
    const today = new Date();
    const weekAgo = new Date(today.getTime() - 7 * 24 * 60 * 60 * 1000);
    this.toDate = today.toISOString().split('T')[0];
    this.fromDate = weekAgo.toISOString().split('T')[0];
    this.fuelChartLabels = Array.from({length: 7}, (_, i) => {
      const d = new Date(today.getTime() - (6 - i) * 86400000);
      return d.toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit' });
    });
    this.loadVehicles();
    this.loadDashboardStats();
    this.loadGeofences();
    this.loadCostData();
    this.loadWidgetData();
    this.loadAlerts();
    this.loadTrips();
    this.loadDrivers();
  }

  loadVehicles() {
    this.apiService.getVehicles().pipe(takeUntil(this.destroy$)).subscribe({
      next: (vehicles) => {
        this.vehicles = vehicles.map(v => ({
          id: v.id?.toString() || '', companyId: v.companyId?.toString() || '',
          name: v.name, type: v.type, brand: v.brand, model: v.model,
          plate: v.plate, year: v.year, color: v.color,
          status: v.status as 'available' | 'in_use' | 'maintenance',
          hasGPS: v.hasGps, mileage: v.mileage
        })) as Vehicle[];
        this.buildMotionData();
        this.buildHealthData();
        this.buildTopUnits();
        this.loadFuelDataFromTrips();
        this.cdr.detectChanges();
      },
      error: (err) => console.error('Error loading vehicles:', err)
    });
  }

  loadDashboardStats() {
    this.apiService.getDashboardStats().pipe(takeUntil(this.destroy$)).subscribe({
      next: (stats) => {
        const v = stats?.Vehicles || stats?.vehicles;
        if (v) {
          this.motionData.movingIgnition = v.Online || v.online || 0;
          this.motionData.stationary = v.Offline || v.offline || 0;
          this.motionData.noCoords = (v.Total || v.total || 0) - (v.WithGps || v.withGps || 0);
        }
        const c = stats?.Costs || stats?.costs;
        if (c) this.totalFuelConsumed = c.FuelThisMonth || c.fuelThisMonth || 0;
        this.cdr.detectChanges();
      },
      error: (err) => console.error('Error loading stats:', err)
    });
  }

  loadGeofences() {
    this.apiService.getGeofences().pipe(takeUntil(this.destroy$)).subscribe({
      next: (geos: any[]) => {
        const colors = ['#22c55e','#3b82f6','#f97316','#06b6d4','#8b5cf6','#ec4899','#eab308','#14b8a6'];
        this.geofences = (geos || []).slice(0, 8).map((g: any, i: number) => ({
          name: g.name || g.Name || 'Geozone', color: colors[i % colors.length],
          count: g.assignedVehicleCount || g.vehicleCount || g.assignedVehicles?.length || 0
        }));
        this.cdr.detectChanges();
      },
      error: (err) => console.error('Error loading geofences:', err)
    });
  }

  loadCostData() {
    this.apiService.getDashboardCostSummary().pipe(takeUntil(this.destroy$)).subscribe({
      next: (s) => {
        this.fuelCost = s?.fuelCost || 0; this.maintenanceCost = s?.maintenanceCost || 0;
        this.repairCost = s?.repairCost || 0; this.otherCost = s?.otherCost || 0;
        this.totalCost = s?.totalCost || (this.fuelCost + this.maintenanceCost + this.repairCost + this.otherCost);
        this.cdr.detectChanges();
      },
      error: () => this.loadCostDataFallback()
    });
  }

  private loadCostDataFallback() {
    const now = new Date();
    this.apiService.getCosts({ startDate: new Date(now.getFullYear(), now.getMonth(), 1) }).pipe(takeUntil(this.destroy$)).subscribe({
      next: (costs) => {
        if (!Array.isArray(costs)) return;
        this.fuelCost = costs.filter((c: any) => c.type === 'fuel').reduce((s: number, c: any) => s + (c.amount || 0), 0);
        this.maintenanceCost = costs.filter((c: any) => c.type === 'maintenance').reduce((s: number, c: any) => s + (c.amount || 0), 0);
        this.repairCost = costs.filter((c: any) => c.type === 'insurance' || c.type === 'fine').reduce((s: number, c: any) => s + (c.amount || 0), 0);
        this.otherCost = costs.filter((c: any) => !['fuel','maintenance','insurance','fine'].includes(c.type)).reduce((s: number, c: any) => s + (c.amount || 0), 0);
        this.totalCost = this.fuelCost + this.maintenanceCost + this.repairCost + this.otherCost;
        this.cdr.detectChanges();
      }
    });
  }

  loadWidgetData() {
    this.apiService.getDashboardWidgetData(this.selectedPeriod).pipe(takeUntil(this.destroy$)).subscribe({
      next: (data) => {
        if (data.drivingScores?.length) {
          const flat: { plate: string; score: number }[] = [];
          data.drivingScores.forEach((g: any) => {
            if (g.vehicles) g.vehicles.forEach((v: string) => flat.push({ plate: v, score: g.score }));
            else if (g.plate) flat.push({ plate: g.plate, score: g.score });
          });
          this.drivingScores = flat.sort((a, b) => b.score - a.score);
        }
        if (data.topFuelConsumers?.length) {
          this.vehicleFuelStats = data.topFuelConsumers.map((v: any) => ({
            plate: v.plate, consumption: v.consumption || 0,
            totalLiters: v.totalLiters || Math.round(v.consumption * 10),
            totalKm: v.totalKm || Math.round((v.consumption * 10) / v.consumption * 100)
          }));
          this.maxFuelConsumption = Math.max(...this.vehicleFuelStats.map(v => v.consumption), 1);
        }
        this.cdr.detectChanges();
      },
      error: (err) => console.error('Error loading widget data:', err)
    });
  }

  buildMotionData() {
    const inUse = this.vehicles.filter(v => v.status === 'in_use').length;
    const maint = this.vehicles.filter(v => v.status === 'maintenance').length;
    const avail = this.vehicles.filter(v => v.status === 'available').length;
    const noGps = this.vehicles.length - this.vehicles.filter(v => v.hasGPS).length;
    this.motionData = { stationary: avail, ignitionOn: 0, moving: 0, movingIgnition: inUse, lbs: 0, wifi: 0, noState: maint, noCoords: noGps };
  }

  buildHealthData() {
    const m = this.vehicles.filter(v => v.status === 'maintenance').length;
    this.healthData = { healthy: this.vehicles.length - m, attention: Math.min(m, 1), unhealthy: Math.max(m - 1, 0) };
  }

  buildTopUnits() {
    const colors = ['#3b82f6','#22c55e','#f97316','#8b5cf6','#06b6d4','#ec4899','#eab308','#14b8a6'];
    const sorted = [...this.vehicles].filter(v => (v.mileage || 0) > 0).sort((a, b) => (b.mileage || 0) - (a.mileage || 0));
    this.topUnits = sorted.slice(0, 8).map((v, i) => ({ name: v.plate || v.name, color: colors[i % colors.length], mileage: Math.round(v.mileage || 0) }));
    this.maxMileage = Math.max(...this.topUnits.map(u => u.mileage), 1);
  }

  loadFuelDataFromTrips() {
    if (this.vehicleFuelStats.length > 0) return;
    const today = new Date();
    const weekAgo = new Date(today.getTime() - 7 * 86400000);
    this.apiService.getTrips({ startDate: weekAgo, endDate: today, limit: 500 }).pipe(takeUntil(this.destroy$)).subscribe({
      next: (trips: any[]) => {
        if (!trips || trips.length === 0) return;
        // Per-vehicle fuel stats
        const byVehicle: Record<string, { fuel: number; km: number }> = {};
        // Per-day fuel for chart
        const byDay: Record<string, number> = {};
        for (const t of trips) {
          const plate = t.vehicle?.plate || t.vehicle?.name || t.plate || 'Inconnu';
          const fuel = Number(t.fuelConsumedLiters || t.FuelConsumedLiters || 0);
          const km = Number(t.distanceKm || t.DistanceKm || 0);
          if (!byVehicle[plate]) byVehicle[plate] = { fuel: 0, km: 0 };
          byVehicle[plate].fuel += fuel;
          byVehicle[plate].km += km;
          const day = (t.startTime || t.StartTime || '').toString().split('T')[0];
          if (day) byDay[day] = (byDay[day] || 0) + fuel;
        }
        // Build per-vehicle stats
        this.vehicleFuelStats = Object.entries(byVehicle)
          .filter(([, v]) => v.km > 0)
          .map(([plate, v]) => ({
            plate, consumption: Math.round((v.fuel / v.km) * 1000) / 10,
            totalLiters: Math.round(v.fuel), totalKm: Math.round(v.km)
          }))
          .sort((a, b) => b.consumption - a.consumption)
          .slice(0, 10);
        this.maxFuelConsumption = Math.max(...this.vehicleFuelStats.map(v => v.consumption), 1);
        // Build fleet chart from daily fuel
        this.totalFuelConsumed = Object.values(byDay).reduce((s, v) => s + v, 0) || this.totalFuelConsumed;
        const dayKeys = Array.from({length: 7}, (_, i) => {
          const d = new Date(today.getTime() - (6 - i) * 86400000);
          return d.toISOString().split('T')[0];
        });
        const dailyValues = dayKeys.map(k => byDay[k] || 0);
        const maxVal = Math.max(...dailyValues, 1);
        this.fuelChartPoints = dailyValues.map((v, i) => {
          const x = (i / 6) * 500;
          const y = 155 - (v / maxVal) * 150;
          return `${x.toFixed(0)},${y.toFixed(0)}`;
        }).join(' ');
        this.cdr.detectChanges();
      },
      error: () => {}
    });
  }

  get totalMotion(): number {
    return this.motionData.stationary + this.motionData.ignitionOn + this.motionData.movingIgnition + this.motionData.noState + this.motionData.noCoords;
  }
  get totalHealth(): number {
    return this.healthData.healthy + this.healthData.attention + this.healthData.unhealthy;
  }

  getSlice(s: number, e: number): string { const t = this.totalMotion; return t === 0 ? '' : this.arc(50, 50, 45, (s/t)*360, (e/t)*360); }
  getHealthSlice(s: number, e: number): string { const t = this.totalHealth; return t === 0 ? '' : this.arc(50, 50, 45, (s/t)*360, (e/t)*360); }
  getExpenseSlice(s: number, e: number): string { return this.totalCost === 0 ? '' : this.arc(50, 50, 45, (s/this.totalCost)*360, (e/this.totalCost)*360); }

  arc(x: number, y: number, r: number, sa: number, ea: number): string {
    if (ea - sa >= 360) ea = sa + 359.99;
    if (ea <= sa) return '';
    const s = this.pol(x, y, r, ea), e = this.pol(x, y, r, sa);
    return `M ${x} ${y} L ${s.x} ${s.y} A ${r} ${r} 0 ${ea - sa > 180 ? 1 : 0} 0 ${e.x} ${e.y} Z`;
  }
  pol(cx: number, cy: number, r: number, a: number) {
    const rad = (a - 90) * Math.PI / 180;
    return { x: cx + r * Math.cos(rad), y: cy + r * Math.sin(rad) };
  }

  loadAlerts() {
    // Load from GpsAlerts
    this.apiService.getAlerts(undefined, undefined, 8).pipe(takeUntil(this.destroy$)).subscribe({
      next: (data: any[]) => {
        const mapped = (data || []).slice(0, 8).map((a: any) => ({
          message: a.message || a.Message || a.type || a.Type || 'Alerte',
          severity: (a.severity || a.Severity) === 'critical' ? 'danger' :
                   (a.severity || a.Severity) === 'warning' ? 'warning' : 'info',
          time: (a.createdAt || a.CreatedAt || a.timestamp || a.Timestamp) ?
            new Date(a.createdAt || a.CreatedAt || a.timestamp || a.Timestamp).toLocaleString('fr-FR', { day:'2-digit', month:'2-digit', hour:'2-digit', minute:'2-digit' }) : '',
          ts: new Date(a.createdAt || a.CreatedAt || a.timestamp || a.Timestamp || 0).getTime()
        }));
        this.mergeAlerts(mapped);
      },
      error: () => {}
    });
    // Load from Notifications
    this.apiService.getNotifications(undefined, undefined, 8).pipe(takeUntil(this.destroy$)).subscribe({
      next: (resp: any) => {
        const items = Array.isArray(resp) ? resp : (resp?.items || resp?.Items || []);
        const mapped = (items || []).slice(0, 8).map((n: any) => ({
          message: n.title || n.Title || n.message || n.Message || 'Notification',
          severity: (n.priority || n.Priority) === 'high' || (n.priority || n.Priority) === 'critical' ? 'danger' :
                   (n.priority || n.Priority) === 'medium' ? 'warning' : 'info',
          time: (n.createdAt || n.CreatedAt) ?
            new Date(n.createdAt || n.CreatedAt).toLocaleString('fr-FR', { day:'2-digit', month:'2-digit', hour:'2-digit', minute:'2-digit' }) : '',
          ts: new Date(n.createdAt || n.CreatedAt || 0).getTime()
        }));
        this.mergeAlerts(mapped);
      },
      error: () => {}
    });
  }

  private pendingAlerts: { message: string; severity: string; time: string; ts: number }[] = [];
  private mergeAlerts(items: { message: string; severity: string; time: string; ts: number }[]) {
    this.pendingAlerts = [...this.pendingAlerts, ...items];
    this.alerts = this.pendingAlerts
      .sort((a, b) => b.ts - a.ts)
      .slice(0, 8)
      .map(({ message, severity, time }) => ({ message, severity, time }));
    this.cdr.detectChanges();
  }

  loadTrips() {
    this.apiService.getTrips({ limit: 8 }).pipe(takeUntil(this.destroy$)).subscribe({
      next: (data: any[]) => {
        this.recentTrips = (data || []).slice(0, 8).map((t: any) => {
          const distKm = t.distanceKm || t.DistanceKm || t.distance || 0;
          const dist = Number(distKm).toFixed(1);
          const mins = t.durationMinutes || t.DurationMinutes || t.duration || 0;
          const dur = mins >= 60 ? `${Math.floor(mins/60)}h${(mins%60).toString().padStart(2,'0')}` : `${mins} min`;
          const plate = t.vehicle?.plate || t.vehicle?.Plate || t.vehicle?.name || t.vehicle?.Name
            || t.vehiclePlate || t.VehiclePlate || t.plate || t.Plate || 'Vehicule';
          const startTime = t.startTime || t.StartTime;
          return {
            plate,
            distance: dist,
            duration: dur,
            date: startTime ? new Date(startTime).toLocaleString('fr-FR', { day:'2-digit', month:'2-digit', hour:'2-digit', minute:'2-digit' }) : ''
          };
        });
        this.cdr.detectChanges();
      },
      error: (err) => console.error('Error loading trips:', err)
    });
  }

  loadDrivers() {
    this.apiService.getDrivers().pipe(takeUntil(this.destroy$)).subscribe({
      next: (data: any[]) => {
        this.drivers = (data || []).slice(0, 8).map((d: any) => {
          const name = d.fullName || d.name || `${d.firstName || ''} ${d.lastName || ''}`.trim() || 'Conducteur';
          const initials = name.split(' ').map((w: string) => w[0]?.toUpperCase()).join('').slice(0, 2);
          return {
            name,
            initials,
            vehicle: d.assignedVehiclePlate || d.vehiclePlate || '',
            active: d.isActive !== false
          };
        });
        this.cdr.detectChanges();
      },
      error: () => {}
    });
  }

  getScoreColor(s: number): string { return s >= 80 ? '#22c55e' : s >= 60 ? '#f59e0b' : '#ef4444'; }
  getFuelColor(c: number): string { return c <= 6 ? '#22c55e' : c <= 8 ? '#f59e0b' : '#ef4444'; }

  onPeriodChange(p: string) { this.selectedPeriod = p; }
  onDateRangeChange(r: { from: string; to: string }) { this.fromDate = r.from; this.toDate = r.to; }
  applyFilter() { this.loadVehicles(); this.loadDashboardStats(); this.loadWidgetData(); }
  ngOnDestroy() { this.destroy$.next(); this.destroy$.complete(); }
}
