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
                  <span class="fuel-vehicle">{{ v.plate }}</span>
                  <span class="fuel-value">{{ v.consumption | number:'1.1-1' }} L/100 km</span>
                  <span class="fuel-trend" [class.up]="v.trend > 0" [class.down]="v.trend < 0">{{ v.trend > 0 ? '+' : '' }}{{ v.trend }}%</span>
                </div>
              </div>
            </div>
          </div>

          <!-- Card 3: Immobilisation des vehicules -->
          <div class="card card-immob">
            <div class="card-header"><span class="card-title">Immobilisation des vehicules</span></div>
            <div class="card-body">
              <div class="immob-list">
                <div class="immob-row" *ngFor="let v of immobilizedVehicles">
                  <span class="immob-dot" [class.warning]="v.days > 5"></span>
                  <span class="immob-plate">{{ v.plate }}</span>
                  <span class="immob-reason">{{ v.reason }}</span>
                  <span class="immob-days">{{ v.days }} jours</span>
                </div>
              </div>
            </div>
          </div>

          <!-- Card 4: Kilometrage global de la flotte -->
          <div class="card card-mileage">
            <div class="card-header"><span class="card-title">Kilometrage global de la flotte</span></div>
            <div class="card-body">
              <div class="mileage-total">
                <span class="mileage-big">{{ totalFleetMileage | number:'1.0-0' }} km</span>
                <span class="mileage-sub">{{ vehicles.length }} vehicules</span>
              </div>
              <div class="mileage-bars">
                <div class="mbar-row" *ngFor="let unit of topUnits">
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

          <!-- Card 4b: Kilometrage filtre -->
          <div class="card card-mileage-filtered">
            <div class="card-header"><span class="card-title">Kilometrage ({{ selectedPeriod === 'week' ? 'Semaine' : selectedPeriod === 'month' ? 'Mois' : 'Trimestre' }})</span></div>
            <div class="card-body">
              <div class="mileage-total">
                <span class="mileage-big">{{ filteredFleetMileage | number:'1.0-0' }} km</span>
                <span class="mileage-sub">Estimation periode</span>
              </div>
              <div class="mileage-bars">
                <div class="mbar-row" *ngFor="let unit of filteredTopUnits">
                  <span class="mbar-name">{{ unit.name }}</span>
                  <div class="mbar-track">
                    <div class="mbar-fill" [style.width.%]="(unit.mileage / filteredMaxMileage) * 100"
                      [style.background]="unit.color"></div>
                  </div>
                  <span class="mbar-km">{{ unit.mileage | number:'1.0-0' }} km</span>
                </div>
              </div>
            </div>
          </div>

          <!-- Card 5: Top scores de conduite -->
          <div class="card card-scores">
            <div class="card-header"><span class="card-title">Top scores de conduite</span></div>
            <div class="card-body">
              <div class="scores-grid">
                <div class="score-group" *ngFor="let group of drivingScores">
                  <div class="score-circle" [style.borderColor]="group.color">
                    <span class="score-val">{{ group.score }}</span>
                  </div>
                  <div class="score-vehicles">
                    <div class="score-v" *ngFor="let v of group.vehicles">{{ v }}</div>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- Card 6: Vehicules en bonne sante -->
          <div class="card card-health-good">
            <div class="card-header"><span class="card-title">Vehicules en bonne sante</span></div>
            <div class="card-body">
              <div class="health-list">
                <div class="health-row" *ngFor="let v of healthyVehicles">
                  <span class="health-plate">{{ v.plate }}</span>
                  <div class="health-bar-track">
                    <div class="health-bar-fill" [style.width.%]="v.score" [style.background]="getHealthColor(v.score)"></div>
                  </div>
                  <span class="health-icon good">&#x2713;</span>
                </div>
              </div>
            </div>
          </div>

          <!-- Card 7: Vehicules en mauvais etat -->
          <div class="card card-health-bad">
            <div class="card-header"><span class="card-title">Vehicules en mauvais etat</span></div>
            <div class="card-body">
              <div class="health-list">
                <div class="health-row" *ngFor="let v of unhealthyVehicles">
                  <span class="health-plate">{{ v.plate }}</span>
                  <span class="health-issue">{{ v.issue }}</span>
                  <span class="health-icon bad">&#9888;</span>
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

    /* Immobilization */
    .immob-list { display: flex; flex-direction: column; }
    .immob-row {
      display: flex; align-items: center; gap: 8px; padding: 8px 0;
      border-bottom: 1px solid #f1f5f9; font-size: 12px;
    }
    .immob-row:last-child { border-bottom: none; }
    .immob-dot {
      width: 8px; height: 8px; border-radius: 50%; background: #22c55e; flex-shrink: 0;
    }
    .immob-dot.warning { background: #ef4444; }
    .immob-plate { color: #334155; font-weight: 500; min-width: 80px; }
    .immob-reason { flex: 1; color: #94a3b8; }
    .immob-days { color: #1e293b; font-weight: 600; white-space: nowrap; }

    /* Mileage */
    .card-mileage { grid-column: 1 / 3; }
    .card-mileage-filtered { grid-column: 3; }
    .mileage-total { margin-bottom: 12px; }
    .mileage-big { font-size: 28px; font-weight: 700; color: #1e293b; }
    .mileage-sub { display: block; font-size: 12px; color: #94a3b8; margin-top: 2px; }
    .mileage-bars { display: flex; flex-direction: column; gap: 6px; }
    .mbar-row { display: flex; align-items: center; gap: 10px; }
    .mbar-name { font-size: 11px; color: #64748b; min-width: 100px; }
    .mbar-track { flex: 1; height: 14px; background: #f1f5f9; border-radius: 4px; overflow: hidden; }
    .mbar-fill { height: 100%; border-radius: 4px; transition: width 0.3s ease; }
    .mbar-km { font-size: 11px; color: #1e293b; font-weight: 600; min-width: 80px; text-align: right; }

    /* Scores */
    .scores-grid { display: flex; gap: 20px; flex-wrap: wrap; }
    .score-group { display: flex; flex-direction: column; align-items: center; gap: 8px; }
    .score-circle {
      width: 60px; height: 60px; border-radius: 50%; border: 4px solid;
      display: flex; align-items: center; justify-content: center;
    }
    .score-val { font-size: 18px; font-weight: 700; color: #1e293b; }
    .score-vehicles { display: flex; flex-direction: column; align-items: center; gap: 2px; }
    .score-v { font-size: 10px; color: #64748b; }

    /* Health cards */
    .health-list { display: flex; flex-direction: column; }
    .health-row {
      display: flex; align-items: center; gap: 8px; padding: 8px 0;
      border-bottom: 1px solid #f1f5f9; font-size: 12px;
    }
    .health-row:last-child { border-bottom: none; }
    .health-plate { color: #334155; font-weight: 500; min-width: 80px; }
    .health-bar-track { flex: 1; height: 8px; background: #f1f5f9; border-radius: 4px; overflow: hidden; }
    .health-bar-fill { height: 100%; border-radius: 4px; }
    .health-issue { flex: 1; color: #ef4444; font-size: 11px; }
    .health-icon { font-size: 14px; }
    .health-icon.good { color: #22c55e; }
    .health-icon.bad { color: #ef4444; }

    @media (max-width: 1100px) {
      .dashboard-grid { grid-template-columns: 1fr 1fr; }
      .card-expenses { grid-column: 1 / -1; grid-row: auto; }
      .card-mileage { grid-column: 1 / -1; }
      .card-mileage-filtered { grid-column: 1 / -1; }
    }
    @media (max-width: 700px) {
      .dashboard-grid { grid-template-columns: 1fr; }
      .card-expenses, .card-mileage, .card-mileage-filtered { grid-column: 1; }
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
    let km = rawKm;
    // GPS protocol artifacts: values > 10M are likely in meters
    if (km > 10_000_000) km = km / 1000;
    // Still too large? Probably millimeters or garbage data
    if (km > 2_000_000) km = km / 1000;
    // Cap at reasonable max for any vehicle (500,000 km)
    if (km > 500_000) return 0;
    return Math.round(km);
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

    // Driving scores (simulated)
    this.drivingScores = [
      { score: 92, color: '#22c55e', vehicles: sorted.slice(0, 2).map(v => v.plate || v.name) },
      { score: 78, color: '#f59e0b', vehicles: sorted.slice(2, 4).map(v => v.plate || v.name) },
      { score: 55, color: '#ef4444', vehicles: sorted.slice(4, 6).map(v => v.plate || v.name) }
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
