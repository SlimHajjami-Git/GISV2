import { Component, OnInit, OnDestroy, NgZone, ChangeDetectorRef, ApplicationRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { ApiService } from '../services/api.service';
import { Vehicle, Company, MaintenanceRecord, VehicleCost } from '../models/types';
import { AppLayoutComponent } from './shared/app-layout.component';
import { DateFilterBarComponent, CardComponent, LegendItemComponent } from './shared/ui';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, AppLayoutComponent, DateFilterBarComponent, CardComponent, LegendItemComponent],
  template: `
    <app-layout>
      <div class="dashboard-container">
        <div class="dashboard-header">
          <h1 class="dashboard-title">Tableau de bord</h1>
          <div class="header-actions">
            <select class="period-select" [(ngModel)]="selectedPeriod" (change)="applyFilter()">
              <option value="week">Semaine</option>
              <option value="month">Mois</option>
              <option value="quarter">Trimestre</option>
            </select>
          </div>
        </div>

        <div class="dashboard-grid">
          <!-- Card 1: Depenses par categorie -->
          <div class="card card-expenses">
            <div class="card-header"><span class="card-title">Depenses par categorie</span></div>
            <div class="card-body">
              <div class="donut-section">
                <div class="donut-wrapper">
                  <svg viewBox="0 0 120 120" class="donut-svg">
                    <circle cx="60" cy="60" r="50" fill="none" stroke="#e2e8f0" stroke-width="18"/>
                    <circle cx="60" cy="60" r="50" fill="none" stroke="#3b82f6" stroke-width="18"
                      [attr.stroke-dasharray]="getFuelArc() + ' ' + (314 - getFuelArc())"
                      stroke-dashoffset="78.5" transform="rotate(-90 60 60)"/>
                    <circle cx="60" cy="60" r="50" fill="none" stroke="#10b981" stroke-width="18"
                      [attr.stroke-dasharray]="getMaintArc() + ' ' + (314 - getMaintArc())"
                      [attr.stroke-dashoffset]="78.5 - getFuelArc()" transform="rotate(-90 60 60)"/>
                    <circle cx="60" cy="60" r="50" fill="none" stroke="#f59e0b" stroke-width="18"
                      [attr.stroke-dasharray]="getRepairArc() + ' ' + (314 - getRepairArc())"
                      [attr.stroke-dashoffset]="78.5 - getFuelArc() - getMaintArc()" transform="rotate(-90 60 60)"/>
                  </svg>
                  <div class="donut-center">
                    <span class="donut-total">{{ totalMonthlyCost | number:'1.0-0' }}</span>
                    <span class="donut-pct">{{ getFuelPercentage() | number:'1.0-0' }}%</span>
                  </div>
                </div>
                <div class="donut-legend">
                  <div class="legend-item"><span class="ldot" style="background:#3b82f6"></span>Carburant <strong>{{ fuelCost | number:'1.0-0' }}</strong></div>
                  <div class="legend-item"><span class="ldot" style="background:#10b981"></span>Entretiens <strong>{{ maintenanceCost | number:'1.0-0' }}</strong></div>
                  <div class="legend-item"><span class="ldot" style="background:#f59e0b"></span>Reparations <strong>{{ repairCost | number:'1.0-0' }}</strong></div>
                  <div class="legend-item"><span class="ldot" style="background:#94a3b8"></span>Autres <strong>{{ otherCost | number:'1.0-0' }}</strong></div>
                </div>
              </div>
              <div class="expense-breakdown">
                <div class="expense-row"><span>Depenses</span><span class="trend up">+5%</span><span class="trend down">-8%</span></div>
                <div class="expense-row"><span>Carburant</span><span>{{ fuelCost | number:'1.0-0' }}</span><span class="pct">{{ getFuelPercentage() | number:'1.0-0' }}%</span></div>
                <div class="expense-row"><span>Entretiens</span><span>{{ maintenanceCost | number:'1.0-0' }}</span><span class="pct">{{ getMaintenancePercentage() | number:'1.0-0' }}%</span></div>
                <div class="expense-row"><span>Autres</span><span>{{ otherCost | number:'1.0-0' }}</span><span class="pct">{{ getOtherPercentage() | number:'1.0-0' }}%</span></div>
                <div class="expense-row total"><span>Total</span><span>{{ totalMonthlyCost | number:'1.0-0' }} DT</span><span class="trend down">-8%</span></div>
              </div>
            </div>
          </div>

          <!-- Card 2: Haute consommation carburant -->
          <div class="card card-fuel">
            <div class="card-header"><span class="card-title">Haute consommation de carburant</span><span class="card-unit">(L/100 km)</span></div>
            <div class="card-body">
              <div class="fuel-list">
                <div class="fuel-row" *ngFor="let v of topFuelConsumers">
                  <span class="fuel-icon">&#x1F69B;</span>
                  <span class="fuel-vehicle">{{ v.plate }}</span>
                  <span class="fuel-value">{{ v.consumption | number:'1.1-1' }} L/100 km</span>
                  <span class="fuel-trend" [class.up]="v.trend > 0" [class.down]="v.trend < 0">{{ v.trend > 0 ? '+' : '' }}{{ v.trend }}%</span>
                </div>
              </div>
            </div>
          </div>

          <!-- Card 3: Immobilisation des vehicules -->
          <div class="card card-immob">
            <div class="card-header"><span class="card-title">Immobilisation des vehicules</span><span class="card-menu">&#8943;</span></div>
            <div class="card-body">
              <div class="immob-subtitle">Arret prolonge (jours)</div>
              <div class="immob-chart">
                <svg viewBox="0 0 260 100" class="immob-line-svg" preserveAspectRatio="none">
                  <defs>
                    <linearGradient id="immobGrad" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="0%" stop-color="#22c55e" stop-opacity="0.25"/>
                      <stop offset="100%" stop-color="#22c55e" stop-opacity="0.02"/>
                    </linearGradient>
                  </defs>
                  <path [attr.d]="getImmobAreaPath()" fill="url(#immobGrad)" />
                  <polyline [attr.points]="getImmobLinePath()" fill="none" stroke="#22c55e" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"/>
                  <circle *ngFor="let pt of immobChartPoints; let i = index" [attr.cx]="pt.x" [attr.cy]="pt.y" r="3.5" fill="#fff" stroke="#22c55e" stroke-width="2"/>
                </svg>
                <div class="immob-chart-labels">
                  <span *ngFor="let lbl of immobChartLabels">{{ lbl }}</span>
                </div>
              </div>
              <div class="immob-list">
                <div class="immob-row" *ngFor="let v of immobilizedVehicles">
                  <span class="immob-dot" [style.background]="v.days > 3 ? '#f59e0b' : v.days > 1 ? '#3b82f6' : '#22c55e'"></span>
                  <span class="immob-plate">{{ v.plate }}</span>
                  <span class="immob-date">{{ v.reason }}</span>
                  <span class="immob-days">{{ v.days }} jours</span>
                </div>
              </div>
            </div>
          </div>

          <!-- Card 4: Kilometrage de la flotte (left, tall) -->
          <div class="card card-mileage">
            <div class="card-header">
              <span class="card-title">Kilometrage de la flotte</span>
              <select class="period-select-sm" [(ngModel)]="selectedPeriod" (change)="applyFilter()">
                <option value="week">Semaine</option>
                <option value="month">Mois</option>
                <option value="quarter">Trimestre</option>
              </select>
            </div>
            <div class="card-body">
              <div class="mileage-total">
                <span class="mileage-big">{{ totalFleetMileage | number:'1.0-0' }} km</span>
                <span class="mileage-trend up">+12%</span>
              </div>
              <div class="mileage-bars">
                <div class="mbar-row" *ngFor="let unit of topUnits">
                  <span class="mbar-icon">&#x1F69A;</span>
                  <span class="mbar-name">{{ unit.name }}</span>
                  <div class="mbar-track">
                    <div class="mbar-fill" [style.width.%]="(unit.mileage / maxMileage) * 100"
                      [style.background]="unit.color"></div>
                  </div>
                  <span class="mbar-km">{{ unit.mileage | number:'1.0-0' }} km</span>
                </div>
              </div>
            </div>
          </div>

          <!-- Card 5: Kilometrage de la flotte (center, stacked bars) -->
          <div class="card card-mileage-chart">
            <div class="card-header"><span class="card-title">Kilometrage de la flotte</span></div>
            <div class="card-body">
              <div class="mileage-chart-total">{{ totalFleetMileage | number:'1.0-0' }} km</div>
              <div class="stacked-bars">
                <div class="stacked-bar-row" *ngFor="let unit of filteredTopUnits">
                  <div class="stacked-bar" [style.width.%]="(unit.mileage / filteredMaxMileage) * 100"
                    [style.background]="unit.color"></div>
                  <span class="stacked-km">{{ unit.mileage | number:'1.0-0' }} km</span>
                </div>
              </div>
            </div>
          </div>

          <!-- Card 6: Vehicules en bonne sante -->
          <div class="card card-health-good">
            <div class="card-header"><span class="card-title">Vehicules en bonne sante</span><span class="card-menu">&#8943;</span></div>
            <div class="card-body">
              <div class="health-list">
                <div class="health-row" *ngFor="let v of healthyVehicles">
                  <span class="health-plate">{{ v.plate }}</span>
                  <div class="health-bar-track">
                    <div class="health-bar-fill" [style.width.%]="v.score" [style.background]="getHealthColor(v.score)"></div>
                  </div>
                  <span class="health-icon" [class.good]="v.score >= 70" [class.bad]="v.score < 70">{{ v.score >= 70 ? '&#x2713;' : '&#9888;' }}</span>
                </div>
              </div>
            </div>
          </div>

          <!-- Card 7: Top scores de conduite -->
          <div class="card card-scores">
            <div class="card-header">
              <span class="card-title">Top scores de conduite</span>
              <span class="scores-summary">{{ filteredFleetMileage | number:'1.0-0' }} km <span class="trend up">+25%</span></span>
            </div>
            <div class="card-body">
              <div class="scores-grid">
                <div class="score-group" *ngFor="let group of drivingScores">
                  <div class="score-circle" [style.borderColor]="group.color">
                    <span class="score-val">{{ group.score }}</span>
                  </div>
                  <div class="score-vehicles">
                    <div class="score-v" *ngFor="let v of group.vehicles">&#x1F69A; {{ v }}</div>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- Card 8: Vehicules en mauvais etat -->
          <div class="card card-health-bad">
            <div class="card-header"><span class="card-title">Vehicules en mauvais etat</span><span class="card-menu">&#8943;</span></div>
            <div class="card-body">
              <div class="health-bad-grid">
                <div class="health-bad-item" *ngFor="let v of unhealthyVehicles">
                  <span class="health-icon bad">&#9888;</span>
                  <span class="health-plate">{{ v.plate }}</span>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    :host { display: block; }
    .dashboard-container {
      flex: 1;
      background: #f1f5f9;
      min-height: calc(100vh - 42px);
      padding: 20px;
      overflow-y: auto;
    }
    .dashboard-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 20px;
    }
    .dashboard-title {
      font-size: 20px;
      font-weight: 700;
      color: #1e293b;
      margin: 0;
    }
    .period-select {
      padding: 6px 12px;
      border: 1px solid #cbd5e1;
      border-radius: 6px;
      background: #fff;
      font-size: 13px;
      color: #334155;
      cursor: pointer;
    }
    .dashboard-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 16px;
    }
    .card {
      background: #fff;
      border-radius: 10px;
      box-shadow: 0 1px 3px rgba(0,0,0,0.08);
      overflow: hidden;
    }
    .card-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 14px 16px 10px;
    }
    .card-title {
      font-size: 14px;
      font-weight: 600;
      color: #1e293b;
    }
    .card-unit {
      font-size: 11px;
      color: #94a3b8;
      margin-left: 6px;
    }
    .card-body {
      padding: 0 16px 16px;
    }

    /* Expenses donut */
    .card-expenses { grid-column: 1; grid-row: 1 / 3; }
    .card-fuel { grid-row: 1 / 3; display: flex; flex-direction: column; }
    .card-immob { grid-row: 1 / 3; display: flex; flex-direction: column; }
    .card-fuel .card-body, .card-immob .card-body { flex: 1; display: flex; flex-direction: column; }
    .card-fuel .fuel-list { flex: 1; display: flex; flex-direction: column; justify-content: space-around; }
    .donut-section { display: flex; gap: 16px; align-items: center; margin-bottom: 16px; }
    .donut-wrapper { position: relative; width: 130px; height: 130px; flex-shrink: 0; }
    .donut-svg { width: 100%; height: 100%; }
    .donut-center {
      position: absolute; top: 50%; left: 50%; transform: translate(-50%,-50%);
      text-align: center;
    }
    .donut-total { display: block; font-size: 18px; font-weight: 700; color: #1e293b; }
    .donut-pct { display: block; font-size: 11px; color: #64748b; }
    .donut-legend { display: flex; flex-direction: column; gap: 6px; }
    .legend-item { font-size: 12px; color: #64748b; display: flex; align-items: center; gap: 6px; }
    .legend-item strong { color: #1e293b; margin-left: auto; }
    .ldot { width: 10px; height: 10px; border-radius: 3px; flex-shrink: 0; }
    .expense-breakdown { border-top: 1px solid #e2e8f0; padding-top: 12px; }
    .expense-row {
      display: flex; justify-content: space-between; align-items: center;
      padding: 6px 0; font-size: 12px; color: #475569;
      border-bottom: 1px solid #f1f5f9;
    }
    .expense-row.total { font-weight: 700; color: #1e293b; border-bottom: none; }
    .expense-row .pct { color: #94a3b8; font-size: 11px; }
    .trend { font-size: 11px; font-weight: 600; padding: 2px 6px; border-radius: 4px; }
    .trend.up { color: #dc2626; background: #fef2f2; }
    .trend.down { color: #16a34a; background: #f0fdf4; }

    /* Fuel consumers */
    .fuel-icon { font-size: 14px; flex-shrink: 0; }
    .fuel-list { display: flex; flex-direction: column; }
    .fuel-row {
      display: flex; align-items: center; gap: 8px; padding: 8px 0;
      border-bottom: 1px solid #f1f5f9; font-size: 12px;
    }
    .fuel-row:last-child { border-bottom: none; }
    .fuel-vehicle { flex: 1; color: #334155; font-weight: 500; }
    .fuel-value { color: #1e293b; font-weight: 600; }
    .fuel-trend { font-size: 11px; font-weight: 600; min-width: 40px; text-align: right; }
    .fuel-trend.up { color: #dc2626; }
    .fuel-trend.down { color: #16a34a; }

    /* Immobilisation card */
    .card-menu { color: #94a3b8; cursor: pointer; font-size: 18px; letter-spacing: 2px; }
    .immob-subtitle { font-size: 11px; color: #94a3b8; margin-bottom: 8px; }
    .immob-chart { margin-bottom: 12px; }
    .immob-line-svg { width: 100%; height: 100px; }
    .immob-chart-labels { display: flex; justify-content: space-between; font-size: 10px; color: #94a3b8; margin-top: 4px; }
    .immob-list { display: flex; flex-direction: column; flex: 1; }
    .immob-row {
      display: flex; align-items: center; gap: 8px; padding: 6px 0;
      border-bottom: 1px solid #f1f5f9; font-size: 11px;
    }
    .immob-row:last-child { border-bottom: none; }
    .immob-dot { width: 8px; height: 8px; border-radius: 50%; flex-shrink: 0; }
    .immob-plate { color: #334155; font-weight: 600; min-width: 80px; }
    .immob-date { flex: 1; color: #94a3b8; font-size: 10px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .immob-days { color: #1e293b; font-weight: 600; white-space: nowrap; }

    /* Mileage (left, spans 2 rows) */
    .card-mileage { grid-column: 1; grid-row: 3 / 5; display: flex; flex-direction: column; }
    .card-mileage .card-body { flex: 1; display: flex; flex-direction: column; }
    .period-select-sm {
      padding: 4px 8px; border: 1px solid #cbd5e1; border-radius: 5px;
      background: #fff; font-size: 11px; color: #334155; cursor: pointer;
    }
    .mileage-total { margin-bottom: 12px; display: flex; align-items: baseline; gap: 8px; }
    .mileage-big { font-size: 28px; font-weight: 700; color: #1e293b; }
    .mileage-trend { font-size: 12px; font-weight: 600; }
    .mileage-trend.up { color: #16a34a; }
    .mileage-bars { display: flex; flex-direction: column; gap: 6px; flex: 1; }
    .mbar-row { display: flex; align-items: center; gap: 6px; }
    .mbar-icon { font-size: 12px; flex-shrink: 0; }
    .mbar-name { font-size: 11px; color: #64748b; min-width: 80px; }
    .mbar-track { flex: 1; height: 14px; background: #f1f5f9; border-radius: 4px; overflow: hidden; }
    .mbar-fill { height: 100%; border-radius: 4px; transition: width 0.3s ease; }
    .mbar-km { font-size: 11px; color: #1e293b; font-weight: 600; min-width: 70px; text-align: right; }

    /* Mileage chart (center-top) */
    .card-mileage-chart { grid-column: 2; grid-row: 3; }
    .mileage-chart-total { font-size: 24px; font-weight: 700; color: #1e293b; margin-bottom: 12px; }
    .stacked-bars { display: flex; flex-direction: column; gap: 5px; }
    .stacked-bar-row { display: flex; align-items: center; gap: 6px; }
    .stacked-bar { height: 16px; border-radius: 3px; min-width: 4px; }
    .stacked-km { font-size: 10px; color: #64748b; white-space: nowrap; }

    /* Health good (right-top) */
    .card-health-good { grid-column: 3; grid-row: 3; }

    /* Scores (center-bottom) */
    .card-scores { grid-column: 2; grid-row: 4; }
    .scores-summary { font-size: 11px; color: #64748b; }
    .scores-grid { display: flex; gap: 12px; flex-wrap: wrap; justify-content: center; }
    .score-group { display: flex; flex-direction: column; align-items: center; gap: 6px; flex: 1; min-width: 60px; }
    .score-circle {
      width: 54px; height: 54px; border-radius: 50%; border: 4px solid;
      display: flex; align-items: center; justify-content: center;
    }
    .score-val { font-size: 16px; font-weight: 700; color: #1e293b; }
    .score-vehicles { display: flex; flex-direction: column; align-items: center; gap: 2px; }
    .score-v { font-size: 9px; color: #64748b; }

    /* Health bad (right-bottom) */
    .card-health-bad { grid-column: 3; grid-row: 4; }
    .health-bad-grid {
      display: grid; grid-template-columns: 1fr 1fr; gap: 8px;
    }
    .health-bad-item {
      display: flex; align-items: center; gap: 4px; font-size: 11px;
    }

    /* Health shared */
    .health-list { display: flex; flex-direction: column; }
    .health-row {
      display: flex; align-items: center; gap: 8px; padding: 7px 0;
      border-bottom: 1px solid #f1f5f9; font-size: 12px;
    }
    .health-row:last-child { border-bottom: none; }
    .health-plate { color: #334155; font-weight: 500; min-width: 80px; font-size: 12px; }
    .health-bar-track { flex: 1; height: 8px; background: #f1f5f9; border-radius: 4px; overflow: hidden; }
    .health-bar-fill { height: 100%; border-radius: 4px; }
    .health-icon { font-size: 14px; }
    .health-icon.good { color: #22c55e; }
    .health-icon.bad { color: #ef4444; }

    @media (max-width: 1100px) {
      .dashboard-grid { grid-template-columns: 1fr 1fr; }
      .card-expenses, .card-mileage { grid-column: 1 / -1; grid-row: auto; }
      .card-fuel, .card-immob { grid-row: auto; }
      .card-mileage-chart, .card-scores { grid-column: auto; grid-row: auto; }
      .card-health-good, .card-health-bad { grid-column: auto; grid-row: auto; }
    }
    @media (max-width: 700px) {
      .dashboard-grid { grid-template-columns: 1fr; }
      .card-expenses, .card-mileage, .card-mileage-chart { grid-column: 1; grid-row: auto; }
      .card-fuel, .card-immob { grid-column: 1; grid-row: auto; }
      .dashboard-container { padding: 12px; }
    }
  `]
})
export class DashboardComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  vehicles: Vehicle[] = [];
  company: Company | null = null;

  selectedPeriod = 'month';
  fromDate = '';
  toDate = '';

  topUnits: { name: string; color: string; mileage: number }[] = [];
  maxMileage = 1;
  totalFleetMileage = 0;
  filteredTopUnits: { name: string; color: string; mileage: number }[] = [];
  filteredMaxMileage = 1;
  filteredFleetMileage = 0;
  maintenanceAlerts: any[] = [];

  // Cost data
  vehicleCosts: VehicleCost[] = [];
  totalMonthlyCost = 0;
  fuelCost = 0;
  maintenanceCost = 0;
  repairCost = 0;
  otherCost = 0;

  // New dashboard data
  topFuelConsumers: { plate: string; consumption: number; trend: number }[] = [];
  immobilizedVehicles: { plate: string; reason: string; days: number }[] = [];
  immobChartPoints: { x: number; y: number }[] = [];
  immobChartLabels: string[] = [];
  drivingScores: { score: number; color: string; vehicles: string[] }[] = [];
  healthyVehicles: { plate: string; score: number }[] = [];
  unhealthyVehicles: { plate: string; issue: string }[] = [];

  constructor(
    private router: Router,
    private apiService: ApiService,
    private ngZone: NgZone,
    private cdr: ChangeDetectorRef,
    private appRef: ApplicationRef
  ) {}

  ngOnInit() {
    if (!this.apiService.isAuthenticated()) {
      this.router.navigate(['/login']);
      return;
    }

    this.ngZone.run(() => {
      this.loadVehicles();
      this.loadCostData();
      this.loadMaintenanceAlerts();
    });

    const today = new Date();
    const monthAgo = new Date(today.getFullYear(), today.getMonth(), 1);
    this.toDate = today.toISOString().split('T')[0];
    this.fromDate = monthAgo.toISOString().split('T')[0];

    const user = this.apiService.getCurrentUserSync();
    if (user) {
      this.company = {
        id: user.companyId.toString(),
        name: user.companyName,
        type: 'transport',
        subscriptionId: '1'
      } as Company;
    }
  }

  loadVehicles() {
    this.apiService.getVehicles().pipe(takeUntil(this.destroy$)).subscribe({
      next: (vehicles) => {
        this.ngZone.run(() => {
          this.vehicles = vehicles.map(v => ({
            id: v.id?.toString() || '',
            companyId: v.companyId?.toString() || '',
            name: v.name,
            type: v.type,
            brand: v.brand,
            model: v.model,
            plate: v.plate,
            year: v.year,
            color: v.color,
            status: v.status as 'available' | 'in_use' | 'maintenance',
            hasGPS: v.hasGps,
            mileage: v.mileage
          })) as Vehicle[];
          this.buildDashboardData();
          this.cdr.detectChanges();
          this.appRef.tick();
        });
      },
      error: (err) => console.error('Error loading vehicles:', err)
    });
  }

  loadCostData() {
    const now = new Date();
    const startOfMonth = new Date(now.getFullYear(), now.getMonth(), 1);
    this.apiService.getCosts({ startDate: startOfMonth }).pipe(takeUntil(this.destroy$)).subscribe({
      next: (costs) => {
        this.ngZone.run(() => {
          this.vehicleCosts = costs.map(c => ({
            id: c.id?.toString() || '',
            vehicleId: c.vehicleId?.toString() || '',
            companyId: c.companyId?.toString() || '',
            type: c.type as 'fuel' | 'maintenance' | 'insurance' | 'tax' | 'toll' | 'parking' | 'fine' | 'other',
            description: c.description,
            amount: c.amount,
            date: new Date(c.date),
            mileage: c.mileage,
            receiptNumber: c.receiptNumber,
            receiptUrl: c.receiptUrl
          })) as VehicleCost[];

          this.fuelCost = this.vehicleCosts.filter(c => c.type === 'fuel').reduce((s, c) => s + c.amount, 0);
          this.maintenanceCost = this.vehicleCosts.filter(c => c.type === 'maintenance').reduce((s, c) => s + c.amount, 0);
          this.repairCost = this.vehicleCosts.filter(c => c.type === 'insurance' || c.type === 'fine').reduce((s, c) => s + c.amount, 0);
          this.otherCost = this.vehicleCosts.filter(c => !['fuel','maintenance','insurance','fine'].includes(c.type)).reduce((s, c) => s + c.amount, 0);
          this.totalMonthlyCost = this.fuelCost + this.maintenanceCost + this.repairCost + this.otherCost;
          this.cdr.detectChanges();
          this.appRef.tick();
        });
      },
      error: (err) => console.error('Error loading costs:', err)
    });
  }

  sanitizeMileage(rawKm: number): number {
    if (!rawKm || rawKm <= 0) return 0;
    return Math.round(rawKm);
  }

  buildDashboardData() {
    const colors = ['#3b82f6','#10b981','#f59e0b','#ef4444','#8b5cf6','#ec4899','#06b6d4','#f97316'];

    // Sanitize mileage for all vehicles
    const vehiclesWithKm = this.vehicles.map(v => ({
      ...v,
      mileageKm: this.sanitizeMileage(v.mileage || 0)
    }));

    // Top units by mileage (global)
    const sorted = [...vehiclesWithKm].sort((a, b) => b.mileageKm - a.mileageKm);
    this.topUnits = sorted.slice(0, 8).map((v, i) => ({
      name: v.plate || v.name,
      color: colors[i % colors.length],
      mileage: v.mileageKm
    }));
    this.maxMileage = Math.max(...this.topUnits.map(u => u.mileage), 1);
    this.totalFleetMileage = vehiclesWithKm.reduce((s, v) => s + v.mileageKm, 0);

    // Filtered mileage estimation based on period
    const divisor = this.selectedPeriod === 'week' ? 52 : this.selectedPeriod === 'month' ? 12 : 4;
    this.filteredTopUnits = sorted.slice(0, 8).map((v, i) => ({
      name: v.plate || v.name,
      color: colors[i % colors.length],
      mileage: Math.round(v.mileageKm / divisor)
    }));
    this.filteredMaxMileage = Math.max(...this.filteredTopUnits.map(u => u.mileage), 1);
    this.filteredFleetMileage = Math.round(this.totalFleetMileage / divisor);

    // Fuel consumers (simulated from vehicle data)
    this.topFuelConsumers = sorted.slice(0, 5).map(v => ({
      plate: v.plate || v.name,
      consumption: 8 + Math.random() * 12,
      trend: Math.round((Math.random() - 0.5) * 20)
    }));

    // Immobilized vehicles: from maintenance alerts + maintenance status vehicles
    this.buildImmobilizedVehicles(vehiclesWithKm);

    // Driving scores (simulated) - 4 groups matching reference
    this.drivingScores = [
      { score: 68, color: '#3b82f6', vehicles: sorted.slice(0, 2).map(v => v.plate || v.name) },
      { score: 63, color: '#10b981', vehicles: sorted.slice(2, 4).map(v => v.plate || v.name) },
      { score: 61, color: '#f59e0b', vehicles: sorted.slice(4, 6).map(v => v.plate || v.name) },
      { score: 57, color: '#ef4444', vehicles: sorted.slice(6, 8).map(v => v.plate || v.name) }
    ];

    // Vehicle health
    this.healthyVehicles = vehiclesWithKm
      .filter(v => v.status === 'available' || v.status === 'in_use')
      .slice(0, 5)
      .map(v => ({ plate: v.plate || v.name, score: 70 + Math.round(Math.random() * 30) }));

    this.unhealthyVehicles = vehiclesWithKm
      .filter(v => v.status === 'maintenance')
      .map(v => ({ plate: v.plate || v.name, issue: 'Maintenance requise' }));
  }

  buildImmobilizedVehicles(vehiclesWithKm: any[]) {
    const immob: { plate: string; reason: string; days: number }[] = [];
    const seen = new Set<string>();

    // From maintenance alerts (due/overdue/critical)
    if (this.maintenanceAlerts && this.maintenanceAlerts.length > 0) {
      for (const alert of this.maintenanceAlerts) {
        const plate = alert.vehiclePlate || alert.vehicleName || 'N/A';
        if (seen.has(plate)) continue;
        seen.add(plate);
        const status = (alert.status || '').toLowerCase();
        const reason = alert.templateName || alert.description || (status === 'overdue' ? 'Maintenance en retard' : status === 'critical' ? 'Maintenance critique' : 'Maintenance planifiee');
        const daysSince = alert.nextDueDate ? Math.max(0, Math.round((Date.now() - new Date(alert.nextDueDate).getTime()) / 86400000)) : 0;
        immob.push({ plate, reason, days: daysSince || 1 });
      }
    }

    // From vehicles with maintenance status
    for (const v of vehiclesWithKm) {
      const plate = v.plate || v.name;
      if (seen.has(plate)) continue;
      if (v.status === 'maintenance') {
        seen.add(plate);
        immob.push({ plate, reason: 'En maintenance', days: 1 });
      }
    }

    this.immobilizedVehicles = immob.sort((a, b) => b.days - a.days);
    this.buildImmobChart();
  }

  loadMaintenanceAlerts() {
    this.apiService.getMaintenanceAlerts().pipe(takeUntil(this.destroy$)).subscribe({
      next: (alerts) => {
        this.ngZone.run(() => {
          this.maintenanceAlerts = alerts || [];
          this.buildImmobilizedVehicles(this.vehicles.map(v => ({
            ...v,
            mileageKm: this.sanitizeMileage(v.mileage || 0)
          })));
          this.cdr.detectChanges();
        });
      },
      error: (err) => console.error('Error loading maintenance alerts:', err)
    });
  }

  // Donut arc helpers (circumference = 2 * PI * 50 = ~314)
  getFuelArc(): number {
    return this.totalMonthlyCost > 0 ? (this.fuelCost / this.totalMonthlyCost) * 314 : 0;
  }
  getMaintArc(): number {
    return this.totalMonthlyCost > 0 ? (this.maintenanceCost / this.totalMonthlyCost) * 314 : 0;
  }
  getRepairArc(): number {
    return this.totalMonthlyCost > 0 ? (this.repairCost / this.totalMonthlyCost) * 314 : 0;
  }

  getFuelPercentage(): number {
    return this.totalMonthlyCost > 0 ? (this.fuelCost / this.totalMonthlyCost) * 100 : 0;
  }
  getMaintenancePercentage(): number {
    return this.totalMonthlyCost > 0 ? (this.maintenanceCost / this.totalMonthlyCost) * 100 : 0;
  }
  getOtherPercentage(): number {
    return this.totalMonthlyCost > 0 ? (this.otherCost / this.totalMonthlyCost) * 100 : 0;
  }

  // Immobilisation line chart helpers
  getImmobLinePath(): string {
    return this.immobChartPoints.map(p => `${p.x},${p.y}`).join(' ');
  }
  getImmobAreaPath(): string {
    if (this.immobChartPoints.length < 2) return '';
    const pts = this.immobChartPoints;
    let d = `M ${pts[0].x},100 L ${pts[0].x},${pts[0].y}`;
    for (let i = 1; i < pts.length; i++) d += ` L ${pts[i].x},${pts[i].y}`;
    d += ` L ${pts[pts.length - 1].x},100 Z`;
    return d;
  }
  buildImmobChart() {
    const months = ['Jan', 'Fev', 'Mar', 'Avr', 'Mai', 'Jun', 'Jul', 'Aou', 'Sep', 'Oct', 'Nov', 'Dec'];
    const now = new Date();
    const labels: string[] = [];
    const values: number[] = [];
    for (let i = 5; i >= 0; i--) {
      const d = new Date(now.getFullYear(), now.getMonth() - i, 1);
      labels.push(months[d.getMonth()]);
      values.push(Math.max(0, this.immobilizedVehicles.length + Math.round((Math.random() - 0.4) * 3)));
    }
    this.immobChartLabels = labels;
    const maxVal = Math.max(...values, 1);
    const w = 260;
    const h = 90;
    const step = w / (values.length - 1);
    this.immobChartPoints = values.map((v, i) => ({
      x: Math.round(i * step),
      y: Math.round(h - (v / maxVal) * (h - 10))
    }));
  }

  getHealthColor(score: number): string {
    if (score >= 80) return '#22c55e';
    if (score >= 60) return '#f59e0b';
    return '#ef4444';
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }

  applyFilter() {
    this.loadVehicles();
    this.loadCostData();
  }
}
