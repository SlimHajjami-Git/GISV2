import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MockDataService } from '../services/mock-data.service';
import { Vehicle, Company } from '../models/types';
import { AppLayoutComponent } from './shared/app-layout.component';
import { DateFilterBarComponent, CardComponent, LegendItemComponent } from './shared/ui';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent, DateFilterBarComponent, CardComponent, LegendItemComponent],
  template: `
    <app-layout>
      <div class="dashboard-container">
        <!-- Date Filter Bar -->
        <ui-date-filter-bar
          [selectedPeriod]="selectedPeriod"
          [fromDate]="fromDate"
          [toDate]="toDate"
          (periodChange)="onPeriodChange($event)"
          (dateRangeChange)="onDateRangeChange($event)"
          (applyFilter)="applyFilter()">
        </ui-date-filter-bar>

        <!-- Dashboard Grid -->
        <div class="dashboard-grid">
          <!-- Row 1 -->
          <!-- Motion State Card -->
          <div class="card">
            <div class="card-header">
              <span class="card-title">Motion state <span class="help">ⓘ</span></span>
              <span class="online-badge">ONLINE DATA</span>
            </div>
            <div class="card-body motion-content">
              <div class="pie-wrapper">
                <svg viewBox="0 0 100 100" class="pie-chart">
                  <circle cx="50" cy="50" r="45" fill="#84cc16"/>
                  <path [attr.d]="getSlice(0, motionData.stationary)" fill="#ef4444"/>
                  <path [attr.d]="getSlice(motionData.stationary, motionData.stationary + motionData.ignitionOn)" fill="#f97316"/>
                </svg>
                <div class="pie-center">{{ totalMotion }}</div>
              </div>
              <div class="motion-legend">
                <div class="legend-row"><span class="dot red"></span><span class="label">Stationary</span><span class="value">{{ motionData.stationary }}</span></div>
                <div class="legend-row"><span class="dot orange"></span><span class="label">Stationary with ignition on</span><span class="value">{{ motionData.ignitionOn }}</span></div>
                <div class="legend-row"><span class="dot green"></span><span class="label">Moving</span><span class="value">{{ motionData.moving }}</span></div>
                <div class="legend-row"><span class="dot lime"></span><span class="label">Moving with ignition on</span><span class="value">{{ motionData.movingIgnition }}</span></div>
                <div class="legend-row"><span class="dot blue"></span><span class="label">LBS detected data</span><span class="value">{{ motionData.lbs }}</span></div>
                <div class="legend-row"><span class="dot navy"></span><span class="label">Wi-Fi detected data</span><span class="value">{{ motionData.wifi }}</span></div>
                <div class="legend-row"><span class="dot gray"></span><span class="label">No actual state</span><span class="value">{{ motionData.noState }}</span></div>
                <div class="legend-row"><span class="dot lightgray"></span><span class="label">No coordinates</span><span class="value">{{ motionData.noCoords }}</span></div>
              </div>
            </div>
          </div>

          <!-- Consumed by FLS Chart -->
          <div class="card chart-card">
            <div class="card-header">
              <span class="card-title">Consumed by FLS <span class="help">ⓘ</span></span>
              <div class="chart-legend-header">
                <span class="legend-square green"></span>
                <span>Fuel truck</span>
                <span class="legend-total">118161</span>
              </div>
            </div>
            <div class="card-body">
              <div class="chart-wrapper">
                <div class="y-axis">
                  <span>1400</span>
                  <span>1050</span>
                  <span>700</span>
                  <span>350</span>
                  <span>0</span>
                </div>
                <div class="chart-area">
                  <svg class="line-svg" viewBox="0 0 500 200" preserveAspectRatio="none">
                    <!-- Grid lines -->
                    <line x1="0" y1="0" x2="500" y2="0" stroke="#e2e8f0" stroke-width="1"/>
                    <line x1="0" y1="50" x2="500" y2="50" stroke="#e2e8f0" stroke-width="1"/>
                    <line x1="0" y1="100" x2="500" y2="100" stroke="#e2e8f0" stroke-width="1"/>
                    <line x1="0" y1="150" x2="500" y2="150" stroke="#e2e8f0" stroke-width="1"/>
                    <line x1="0" y1="200" x2="500" y2="200" stroke="#e2e8f0" stroke-width="1"/>
                    <!-- Data line -->
                    <polyline fill="none" stroke="#22c55e" stroke-width="2"
                      points="0,150 60,140 120,130 180,120 240,50 300,20 360,30 420,80 480,170 500,160"/>
                  </svg>
                </div>
              </div>
              <div class="x-axis">
                <span>2025-01-03</span>
                <span>2025-01-05</span>
                <span>2025-01-07</span>
                <span>2025-01-09</span>
                <span>2025-01-11</span>
                <span>2025-01-13</span>
                <span>2025-01-15</span>
              </div>
            </div>
          </div>

          <!-- Row 2 -->
          <!-- Geofences Card -->
          <div class="card">
            <div class="card-header">
              <span class="card-title">Geofences with units</span>
              <span class="online-badge">ONLINE DATA</span>
            </div>
            <div class="card-body">
              <div class="geofence-list">
                <div class="geofence-row" *ngFor="let geo of geofences">
                  <span class="geo-icon" [style.color]="geo.color">●</span>
                  <span class="geo-name">{{ geo.name }}</span>
                  <span class="geo-count">{{ geo.count }}</span>
                </div>
              </div>
            </div>
          </div>

          <!-- Device Health Card -->
          <div class="card">
            <div class="card-header">
              <span class="card-title">Device health check status</span>
              <span class="online-badge">ONLINE DATA</span>
            </div>
            <div class="card-body health-content">
              <div class="health-pie-wrapper">
                <svg viewBox="0 0 100 100" class="health-pie">
                  <circle cx="50" cy="50" r="45" fill="#22c55e"/>
                  <path [attr.d]="getHealthSlice(0, healthData.attention)" fill="#eab308"/>
                  <path [attr.d]="getHealthSlice(healthData.attention, healthData.attention + healthData.unhealthy)" fill="#ef4444"/>
                </svg>
                <div class="health-center">{{ totalHealth }}</div>
              </div>
              <div class="health-legend">
                <div class="health-row"><span class="dot green"></span><span class="label">Healthy</span><span class="value">{{ healthData.healthy }}</span></div>
                <div class="health-row"><span class="dot yellow"></span><span class="label">Need attention</span><span class="value">{{ healthData.attention }}</span></div>
                <div class="health-row"><span class="dot red"></span><span class="label">Unhealthy</span><span class="value">{{ healthData.unhealthy }}</span></div>
              </div>
            </div>
          </div>

          <!-- Top Units by Mileage -->
          <div class="card mileage-card">
            <div class="card-header">
              <span class="card-title">Top units by mileage <span class="help">ⓘ</span></span>
            </div>
            <div class="card-body">
              <div class="mileage-scale">
                <span></span>
                <span>0 km</span>
                <span>8500 km</span>
                <span>17000 km</span>
                <span>25500 km</span>
                <span>34000 km</span>
              </div>
              <div class="mileage-list">
                <div class="mileage-row" *ngFor="let unit of topUnits">
                  <div class="unit-label">
                    <span class="unit-dot" [style.background]="unit.color"></span>
                    <span class="unit-name">{{ unit.name }}</span>
                    <span class="unit-km">{{ unit.mileage | number }} km</span>
                  </div>
                  <div class="bar-container">
                    <div class="bar" [style.width.%]="(unit.mileage / maxMileage) * 100" [style.background]="'#3b82f6'"></div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    .dashboard-container {
      flex: 1;
      background: #f1f5f9;
      min-height: calc(100vh - 42px);
      display: flex;
      flex-direction: column;
    }

    /* Dashboard Grid */
    .dashboard-grid {
      display: grid;
      grid-template-columns: 340px 1fr 1fr;
      grid-template-rows: auto 1fr;
      gap: 1px;
      background: #e2e8f0;
      flex: 1;
    }

    /* Cards */
    .card {
      background: white;
    }

    .card-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 10px 14px;
      border-bottom: 1px solid #f1f5f9;
    }

    .card-title {
      color: #1e293b;
      font-size: 13px;
      font-weight: 600;
    }

    .card-title .help {
      color: #94a3b8;
      font-size: 11px;
      margin-left: 4px;
    }

    .online-badge {
      color: #3b82f6;
      font-size: 9px;
      font-weight: 600;
      letter-spacing: 0.5px;
    }

    .card-body {
      padding: 14px;
    }

    /* Motion State */
    .motion-content {
      display: flex;
      gap: 16px;
      align-items: flex-start;
    }

    .pie-wrapper {
      position: relative;
      width: 110px;
      height: 110px;
      flex-shrink: 0;
    }

    .pie-chart {
      width: 100%;
      height: 100%;
      transform: rotate(-90deg);
    }

    .pie-center {
      position: absolute;
      top: 50%;
      left: 50%;
      transform: translate(-50%, -50%);
      font-size: 18px;
      font-weight: 700;
      color: #1e293b;
      background: white;
      width: 50px;
      height: 50px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .motion-legend {
      display: flex;
      flex-direction: column;
      gap: 3px;
      flex: 1;
    }

    .legend-row {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 11px;
    }

    .legend-row .label {
      flex: 1;
      color: #64748b;
    }

    .legend-row .value {
      color: #1e293b;
      font-weight: 600;
      min-width: 16px;
      text-align: right;
    }

    .dot {
      width: 8px;
      height: 8px;
      border-radius: 2px;
      flex-shrink: 0;
    }

    .dot.red { background: #ef4444; }
    .dot.orange { background: #f97316; }
    .dot.green { background: #22c55e; }
    .dot.lime { background: #84cc16; }
    .dot.blue { background: #3b82f6; }
    .dot.navy { background: #1e40af; }
    .dot.gray { background: #6b7280; }
    .dot.lightgray { background: #d1d5db; }
    .dot.yellow { background: #eab308; }

    /* Chart Card */
    .chart-card {
      grid-column: 2;
      grid-row: 1;
    }

    .chart-legend-header {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 11px;
      color: #64748b;
    }

    .legend-square {
      width: 12px;
      height: 12px;
      border-radius: 2px;
    }

    .legend-square.green {
      background: #22c55e;
    }

    .legend-total {
      color: #1e293b;
      font-weight: 600;
    }

    .chart-wrapper {
      display: flex;
      height: 180px;
    }

    .y-axis {
      display: flex;
      flex-direction: column;
      justify-content: space-between;
      padding-right: 8px;
      font-size: 10px;
      color: #94a3b8;
      text-align: right;
      width: 35px;
    }

    .chart-area {
      flex: 1;
      border-left: 1px solid #e2e8f0;
      border-bottom: 1px solid #e2e8f0;
    }

    .line-svg {
      width: 100%;
      height: 100%;
    }

    .x-axis {
      display: flex;
      justify-content: space-between;
      padding-top: 6px;
      padding-left: 40px;
      font-size: 9px;
      color: #94a3b8;
    }

    /* Geofences */
    .geofence-list {
      display: flex;
      flex-direction: column;
    }

    .geofence-row {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 6px 0;
      border-bottom: 1px solid #f1f5f9;
    }

    .geofence-row:last-child {
      border-bottom: none;
    }

    .geo-icon {
      font-size: 14px;
    }

    .geo-name {
      flex: 1;
      color: #64748b;
      font-size: 12px;
    }

    .geo-count {
      color: #1e293b;
      font-weight: 600;
      font-size: 12px;
    }

    /* Health */
    .health-content {
      display: flex;
      gap: 24px;
      align-items: center;
    }

    .health-pie-wrapper {
      position: relative;
      width: 100px;
      height: 100px;
      flex-shrink: 0;
    }

    .health-pie {
      width: 100%;
      height: 100%;
      transform: rotate(-90deg);
    }

    .health-center {
      position: absolute;
      top: 50%;
      left: 50%;
      transform: translate(-50%, -50%);
      font-size: 16px;
      font-weight: 700;
      color: #1e293b;
      background: white;
      width: 45px;
      height: 45px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .health-legend {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .health-row {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 12px;
    }

    .health-row .label {
      color: #64748b;
      min-width: 90px;
    }

    .health-row .value {
      color: #1e293b;
      font-weight: 600;
    }

    /* Mileage */
    .mileage-scale {
      display: grid;
      grid-template-columns: 140px repeat(5, 1fr);
      font-size: 9px;
      color: #94a3b8;
      padding-bottom: 6px;
      border-bottom: 1px solid #f1f5f9;
      margin-bottom: 8px;
    }

    .mileage-list {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .mileage-row {
      display: grid;
      grid-template-columns: 140px 1fr;
      align-items: center;
      gap: 8px;
    }

    .unit-label {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 11px;
    }

    .unit-dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
    }

    .unit-name {
      color: #64748b;
    }

    .unit-km {
      color: #94a3b8;
      font-size: 10px;
      margin-left: auto;
    }

    .bar-container {
      height: 18px;
      background: #f1f5f9;
      border-radius: 2px;
      overflow: hidden;
    }

    .bar {
      height: 100%;
      border-radius: 2px;
    }

    .mileage-card {
      grid-column: 2 / 4;
      grid-row: 2;
    }

    /* Responsive */
    @media (max-width: 1200px) {
      .dashboard-grid {
        grid-template-columns: 1fr 1fr;
      }

      .chart-card {
        grid-column: 1 / -1;
        grid-row: auto;
      }

      .mileage-card {
        grid-column: 1 / -1;
        grid-row: auto;
      }
    }

    @media (max-width: 768px) {
      .dashboard-grid {
        grid-template-columns: 1fr;
      }

      .filter-bar {
        flex-wrap: wrap;
      }

      .chart-card {
        grid-column: 1;
      }
    }
  `]
})
export class DashboardComponent implements OnInit {
  vehicles: Vehicle[] = [];
  company: Company | null = null;

  selectedPeriod = 'week';
  fromDate = '';
  toDate = '';

  motionData = {
    stationary: 3,
    ignitionOn: 1,
    moving: 0,
    movingIgnition: 10,
    lbs: 0,
    wifi: 0,
    noState: 0,
    noCoords: 0
  };

  healthData = {
    healthy: 13,
    attention: 1,
    unhealthy: 0
  };

  geofences = [
    { name: 'Green geofence', color: '#22c55e', count: 2 },
    { name: 'Road', color: '#3b82f6', count: 2 },
    { name: 'Haltenhoffstraße', color: '#f97316', count: 1 },
    { name: 'New road', color: '#06b6d4', count: 1 },
    { name: 'Scheidestraße 3', color: '#8b5cf6', count: 1 },
    { name: 'Waetzener Landstraße 119', color: '#ec4899', count: 1 }
  ];

  topUnits = [
    { name: 'Fuel truck', color: '#f97316', mileage: 32050 },
    { name: 'Mercedes-Benz', color: '#22c55e', mileage: 18971 },
    { name: 'Mercedes-Benz', color: '#3b82f6', mileage: 10871 },
    { name: 'Mercedes-Benz', color: '#8b5cf6', mileage: 10935 },
    { name: 'Mercedes-Benz', color: '#06b6d4', mileage: 10935 },
    { name: 'Mercedes-Benz', color: '#ec4899', mileage: 10808 },
    { name: 'Mercedes-Benz', color: '#eab308', mileage: 10625 },
    { name: 'Citaro', color: '#14b8a6', mileage: 9133 }
  ];

  maxMileage = 34000;

  constructor(
    private router: Router,
    private dataService: MockDataService
  ) {}

  ngOnInit() {
    if (!this.dataService.isAuthenticated()) {
      this.router.navigate(['/login']);
      return;
    }

    this.company = this.dataService.getCurrentCompany();

    if (this.company) {
      this.vehicles = this.dataService.getVehiclesByCompany(this.company.id);
    }

    const today = new Date();
    const weekAgo = new Date(today.getTime() - 7 * 24 * 60 * 60 * 1000);
    this.toDate = today.toISOString().split('T')[0];
    this.fromDate = weekAgo.toISOString().split('T')[0];
  }

  get totalMotion(): number {
    return this.motionData.stationary + this.motionData.ignitionOn + 
           this.motionData.moving + this.motionData.movingIgnition +
           this.motionData.lbs + this.motionData.wifi +
           this.motionData.noState + this.motionData.noCoords;
  }

  get totalHealth(): number {
    return this.healthData.healthy + this.healthData.attention + this.healthData.unhealthy;
  }

  getSlice(startPercent: number, endPercent: number): string {
    const total = this.totalMotion;
    if (total === 0) return '';
    
    const startAngle = (startPercent / total) * 360;
    const endAngle = (endPercent / total) * 360;
    
    return this.describeArc(50, 50, 45, startAngle, endAngle);
  }

  getHealthSlice(startPercent: number, endPercent: number): string {
    const total = this.totalHealth;
    if (total === 0) return '';
    
    const startAngle = (startPercent / total) * 360;
    const endAngle = (endPercent / total) * 360;
    
    return this.describeArc(50, 50, 45, startAngle, endAngle);
  }

  describeArc(x: number, y: number, radius: number, startAngle: number, endAngle: number): string {
    if (endAngle - startAngle >= 360) {
      endAngle = startAngle + 359.99;
    }
    
    const start = this.polarToCartesian(x, y, radius, endAngle);
    const end = this.polarToCartesian(x, y, radius, startAngle);
    const largeArcFlag = endAngle - startAngle <= 180 ? "0" : "1";

    return [
      "M", x, y,
      "L", start.x, start.y,
      "A", radius, radius, 0, largeArcFlag, 0, end.x, end.y,
      "Z"
    ].join(" ");
  }

  polarToCartesian(centerX: number, centerY: number, radius: number, angleInDegrees: number) {
    const angleInRadians = (angleInDegrees - 90) * Math.PI / 180.0;
    return {
      x: centerX + (radius * Math.cos(angleInRadians)),
      y: centerY + (radius * Math.sin(angleInRadians))
    };
  }

  onPeriodChange(period: string) {
    this.selectedPeriod = period;
  }

  onDateRangeChange(range: { from: string; to: string }) {
    this.fromDate = range.from;
    this.toDate = range.to;
  }

  applyFilter() {
    console.log('Applying filter:', this.fromDate, 'to', this.toDate);
  }
}
