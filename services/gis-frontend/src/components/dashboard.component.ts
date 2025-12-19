import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
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

          <!-- Row 3: Maintenance & Costs -->
          <!-- Upcoming Maintenance Card -->
          <div class="card maintenance-card">
            <div class="card-header">
              <span class="card-title">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="vertical-align: middle; margin-right: 6px;">
                  <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>
                </svg>
                Maintenance
              </span>
              <a routerLink="/maintenance" class="view-all-link">Voir tout →</a>
            </div>
            <div class="card-body">
              <div class="maintenance-stats">
                <div class="maint-stat scheduled">
                  <span class="maint-count">{{ scheduledMaintCount }}</span>
                  <span class="maint-label">Planifiées</span>
                </div>
                <div class="maint-stat in-progress">
                  <span class="maint-count">{{ inProgressMaintCount }}</span>
                  <span class="maint-label">En cours</span>
                </div>
                <div class="maint-stat completed">
                  <span class="maint-count">{{ completedMaintCount }}</span>
                  <span class="maint-label">Terminées</span>
                </div>
              </div>
              <div class="upcoming-list">
                @for (m of upcomingMaintenance; track m.id) {
                  <div class="upcoming-item">
                    <div class="upcoming-icon" [class]="m.status">
                      <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>
                      </svg>
                    </div>
                    <div class="upcoming-info">
                      <span class="upcoming-title">{{ m.description }}</span>
                      <span class="upcoming-vehicle">{{ getVehicleName(m.vehicleId) }}</span>
                    </div>
                    <span class="upcoming-date">{{ formatShortDate(m.date) }}</span>
                  </div>
                }
              </div>
            </div>
          </div>

          <!-- Costs Summary Card -->
          <div class="card costs-card">
            <div class="card-header">
              <span class="card-title">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="vertical-align: middle; margin-right: 6px;">
                  <line x1="12" y1="1" x2="12" y2="23"/>
                  <path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>
                </svg>
                Dépenses du mois
              </span>
              <a routerLink="/reports" class="view-all-link">Voir rapport →</a>
            </div>
            <div class="card-body">
              <div class="cost-total">
                <span class="cost-amount">{{ totalMonthlyCost | number:'1.0-0' }} DT</span>
                <span class="cost-period">Ce mois</span>
              </div>
              <div class="cost-breakdown">
                <div class="cost-item fuel">
                  <div class="cost-bar" [style.width.%]="getFuelPercentage()"></div>
                  <span class="cost-type">Carburant</span>
                  <span class="cost-value">{{ fuelCost | number:'1.0-0' }} DT</span>
                </div>
                <div class="cost-item maintenance">
                  <div class="cost-bar" [style.width.%]="getMaintenancePercentage()"></div>
                  <span class="cost-type">Maintenance</span>
                  <span class="cost-value">{{ maintenanceCost | number:'1.0-0' }} DT</span>
                </div>
                <div class="cost-item other">
                  <div class="cost-bar" [style.width.%]="getOtherPercentage()"></div>
                  <span class="cost-type">Autres</span>
                  <span class="cost-value">{{ otherCost | number:'1.0-0' }} DT</span>
                </div>
              </div>
            </div>
          </div>

          <!-- Fleet Status Card -->
          <div class="card fleet-status-card">
            <div class="card-header">
              <span class="card-title">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="vertical-align: middle; margin-right: 6px;">
                  <rect x="1" y="3" width="15" height="13" rx="2"/>
                  <polygon points="16 8 20 8 23 11 23 16 16 16 16 8"/>
                  <circle cx="5.5" cy="18.5" r="2.5"/>
                  <circle cx="18.5" cy="18.5" r="2.5"/>
                </svg>
                État de la flotte
              </span>
              <a routerLink="/vehicles" class="view-all-link">Voir tout →</a>
            </div>
            <div class="card-body">
              <div class="fleet-stats">
                <div class="fleet-stat">
                  <div class="fleet-icon available">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                      <polyline points="22 4 12 14.01 9 11.01"/>
                    </svg>
                  </div>
                  <span class="fleet-count">{{ availableVehicles }}</span>
                  <span class="fleet-label">Disponibles</span>
                </div>
                <div class="fleet-stat">
                  <div class="fleet-icon in-use">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <rect x="1" y="3" width="15" height="13" rx="2"/>
                      <polygon points="16 8 20 8 23 11 23 16 16 16 16 8"/>
                      <circle cx="5.5" cy="18.5" r="2.5"/>
                      <circle cx="18.5" cy="18.5" r="2.5"/>
                    </svg>
                  </div>
                  <span class="fleet-count">{{ inUseVehicles }}</span>
                  <span class="fleet-label">En service</span>
                </div>
                <div class="fleet-stat">
                  <div class="fleet-icon maintenance">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>
                    </svg>
                  </div>
                  <span class="fleet-count">{{ maintenanceVehicles }}</span>
                  <span class="fleet-label">Maintenance</span>
                </div>
                <div class="fleet-stat">
                  <div class="fleet-icon gps">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z"/>
                      <circle cx="12" cy="10" r="3"/>
                    </svg>
                  </div>
                  <span class="fleet-count">{{ gpsEquippedVehicles }}</span>
                  <span class="fleet-label">Avec GPS</span>
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
      background: var(--bg-page);
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
      background: var(--border-color);
      flex: 1;
    }

    /* Cards */
    .card {
      background: var(--bg-card);
    }

    .card-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 10px 14px;
      border-bottom: 1px solid var(--border-color);
    }

    .card-title {
      color: var(--text-primary);
      font-size: 13px;
      font-weight: 600;
    }

    .card-title .help {
      color: var(--text-muted);
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
      color: var(--text-primary);
      background: var(--bg-card);
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
      color: var(--text-secondary);
    }

    .legend-row .value {
      color: var(--text-primary);
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
      color: var(--text-secondary);
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
      color: var(--text-primary);
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
      color: var(--text-muted);
      text-align: right;
      width: 35px;
    }

    .chart-area {
      flex: 1;
      border-left: 1px solid var(--border-color);
      border-bottom: 1px solid var(--border-color);
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
      color: var(--text-muted);
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
      border-bottom: 1px solid var(--border-color);
    }

    .geofence-row:last-child {
      border-bottom: none;
    }

    .geo-icon {
      font-size: 14px;
    }

    .geo-name {
      flex: 1;
      color: var(--text-secondary);
      font-size: 12px;
    }

    .geo-count {
      color: var(--text-primary);
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
      color: var(--text-primary);
      background: var(--bg-card);
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
      color: var(--text-secondary);
      min-width: 90px;
    }

    .health-row .value {
      color: var(--text-primary);
      font-weight: 600;
    }

    /* Mileage */
    .mileage-scale {
      display: grid;
      grid-template-columns: 140px repeat(5, 1fr);
      font-size: 9px;
      color: var(--text-muted);
      padding-bottom: 6px;
      border-bottom: 1px solid var(--border-color);
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
      color: var(--text-secondary);
    }

    .unit-km {
      color: var(--text-muted);
      font-size: 10px;
      margin-left: auto;
    }

    .bar-container {
      height: 18px;
      background: var(--bg-tertiary);
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

    /* View All Link */
    .view-all-link {
      font-size: 11px;
      color: #3b82f6;
      text-decoration: none;
      font-weight: 500;
    }

    .view-all-link:hover {
      text-decoration: underline;
    }

    /* Maintenance Card */
    .maintenance-card {
      grid-column: 1;
      grid-row: 3;
    }

    .maintenance-stats {
      display: flex;
      gap: 12px;
      padding-bottom: 12px;
      border-bottom: 1px solid var(--border-color);
      margin-bottom: 12px;
    }

    .maint-stat {
      flex: 1;
      text-align: center;
      padding: 8px;
      border-radius: 4px;
    }

    .maint-stat.scheduled { background: rgba(59, 130, 246, 0.1); }
    .maint-stat.in-progress { background: rgba(234, 179, 8, 0.1); }
    .maint-stat.completed { background: rgba(34, 197, 94, 0.1); }

    .maint-count {
      display: block;
      font-size: 18px;
      font-weight: 700;
      color: var(--text-primary);
    }

    .maint-label {
      font-size: 9px;
      color: var(--text-secondary);
      text-transform: uppercase;
    }

    .upcoming-list {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .upcoming-item {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 6px 0;
      border-bottom: 1px solid var(--border-color);
    }

    .upcoming-icon {
      width: 24px;
      height: 24px;
      border-radius: 4px;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .upcoming-icon.scheduled { background: #dbeafe; color: #2563eb; }
    .upcoming-icon.in_progress { background: #fef3c7; color: #d97706; }
    .upcoming-icon.completed { background: #dcfce7; color: #16a34a; }

    .upcoming-info {
      flex: 1;
      display: flex;
      flex-direction: column;
    }

    .upcoming-title {
      font-size: 11px;
      font-weight: 500;
      color: var(--text-primary);
    }

    .upcoming-vehicle {
      font-size: 10px;
      color: var(--text-muted);
    }

    .upcoming-date {
      font-size: 10px;
      color: var(--text-secondary);
      white-space: nowrap;
    }

    /* Costs Card */
    .costs-card {
      grid-column: 2;
      grid-row: 3;
    }

    .cost-total {
      text-align: center;
      padding: 12px;
      background: rgba(34, 197, 94, 0.1);
      border-radius: 6px;
      margin-bottom: 14px;
    }

    .cost-amount {
      display: block;
      font-size: 24px;
      font-weight: 700;
      color: #16a34a;
    }

    .cost-period {
      font-size: 10px;
      color: var(--text-secondary);
    }

    .cost-breakdown {
      display: flex;
      flex-direction: column;
      gap: 10px;
    }

    .cost-item {
      position: relative;
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 8px 10px;
      background: var(--bg-secondary);
      border-radius: 4px;
      overflow: hidden;
    }

    .cost-bar {
      position: absolute;
      left: 0;
      top: 0;
      height: 100%;
      opacity: 0.3;
    }

    .cost-item.fuel .cost-bar { background: #f97316; }
    .cost-item.maintenance .cost-bar { background: #3b82f6; }
    .cost-item.other .cost-bar { background: #64748b; }

    .cost-type {
      font-size: 11px;
      color: var(--text-secondary);
      position: relative;
    }

    .cost-value {
      font-size: 12px;
      font-weight: 600;
      color: var(--text-primary);
      position: relative;
    }

    /* Fleet Status Card */
    .fleet-status-card {
      grid-column: 3;
      grid-row: 3;
    }

    .fleet-stats {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 12px;
    }

    .fleet-stat {
      display: flex;
      flex-direction: column;
      align-items: center;
      padding: 14px;
      background: var(--bg-secondary);
      border-radius: 6px;
    }

    .fleet-icon {
      width: 36px;
      height: 36px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      margin-bottom: 8px;
    }

    .fleet-icon.available { background: #dcfce7; color: #16a34a; }
    .fleet-icon.in-use { background: #dbeafe; color: #2563eb; }
    .fleet-icon.maintenance { background: #fef3c7; color: #d97706; }
    .fleet-icon.gps { background: #f3e8ff; color: #9333ea; }

    .fleet-count {
      font-size: 20px;
      font-weight: 700;
      color: var(--text-primary);
    }

    .fleet-label {
      font-size: 10px;
      color: var(--text-secondary);
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

  // Maintenance data
  maintenanceRecords: MaintenanceRecord[] = [];
  upcomingMaintenance: MaintenanceRecord[] = [];
  scheduledMaintCount = 0;
  inProgressMaintCount = 0;
  completedMaintCount = 0;

  // Cost data
  vehicleCosts: VehicleCost[] = [];
  totalMonthlyCost = 0;
  fuelCost = 0;
  maintenanceCost = 0;
  otherCost = 0;

  // Fleet status
  availableVehicles = 0;
  inUseVehicles = 0;
  maintenanceVehicles = 0;
  gpsEquippedVehicles = 0;

  constructor(
    private router: Router,
    private apiService: ApiService
  ) {}

  ngOnInit() {
    if (!this.apiService.isAuthenticated()) {
      this.router.navigate(['/login']);
      return;
    }

    // Load data from API
    this.loadVehicles();
    this.loadMaintenanceData();
    this.loadCostData();

    const today = new Date();
    const weekAgo = new Date(today.getTime() - 7 * 24 * 60 * 60 * 1000);
    this.toDate = today.toISOString().split('T')[0];
    this.fromDate = weekAgo.toISOString().split('T')[0];
    
    // Set company from current user
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
    this.apiService.getVehicles().subscribe({
      next: (vehicles) => {
        this.vehicles = vehicles.map(v => ({
          id: v.id.toString(),
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
        this.calculateFleetStatus();
      },
      error: (err) => console.error('Error loading vehicles:', err)
    });
  }

  loadMaintenanceData() {
    this.apiService.getMaintenanceRecords().subscribe({
      next: (records) => {
        this.maintenanceRecords = records.map(m => ({
          id: m.id.toString(),
          vehicleId: m.vehicleId.toString(),
          companyId: m.companyId.toString(),
          type: m.type,
          description: m.description,
          mileageAtService: m.mileageAtService,
          date: new Date(m.date),
          nextServiceDate: m.nextServiceDate ? new Date(m.nextServiceDate) : undefined,
          nextServiceMileage: m.nextServiceMileage,
          status: m.status as 'scheduled' | 'in_progress' | 'completed',
          laborCost: m.laborCost,
          partsCost: m.partsCost,
          totalCost: m.totalCost,
          serviceProvider: m.serviceProvider,
          notes: m.notes,
          parts: m.parts || []
        })) as MaintenanceRecord[];
        
        this.scheduledMaintCount = this.maintenanceRecords.filter(m => m.status === 'scheduled').length;
        this.inProgressMaintCount = this.maintenanceRecords.filter(m => m.status === 'in_progress').length;
        this.completedMaintCount = this.maintenanceRecords.filter(m => m.status === 'completed').length;
        
        this.upcomingMaintenance = [...this.maintenanceRecords]
          .sort((a, b) => new Date(b.date).getTime() - new Date(a.date).getTime())
          .slice(0, 4);
      },
      error: (err) => console.error('Error loading maintenance:', err)
    });
  }

  loadCostData() {
    const now = new Date();
    const startOfMonth = new Date(now.getFullYear(), now.getMonth(), 1);
    
    this.apiService.getCosts({ startDate: startOfMonth }).subscribe({
      next: (costs) => {
        this.vehicleCosts = costs.map(c => ({
          id: c.id.toString(),
          vehicleId: c.vehicleId.toString(),
          companyId: c.companyId.toString(),
          type: c.type as 'fuel' | 'maintenance' | 'insurance' | 'tax' | 'toll' | 'parking' | 'fine' | 'other',
          description: c.description,
          amount: c.amount,
          date: new Date(c.date),
          mileage: c.mileage,
          receiptNumber: c.receiptNumber,
          receiptUrl: c.receiptUrl
        })) as VehicleCost[];
        
        this.fuelCost = this.vehicleCosts.filter(c => c.type === 'fuel').reduce((sum, c) => sum + c.amount, 0);
        this.maintenanceCost = this.vehicleCosts.filter(c => c.type === 'maintenance').reduce((sum, c) => sum + c.amount, 0);
        this.otherCost = this.vehicleCosts.filter(c => c.type !== 'fuel' && c.type !== 'maintenance').reduce((sum, c) => sum + c.amount, 0);
        this.totalMonthlyCost = this.fuelCost + this.maintenanceCost + this.otherCost;
      },
      error: (err) => console.error('Error loading costs:', err)
    });
  }

  calculateFleetStatus() {
    this.availableVehicles = this.vehicles.filter(v => v.status === 'available').length;
    this.inUseVehicles = this.vehicles.filter(v => v.status === 'in_use').length;
    this.maintenanceVehicles = this.vehicles.filter(v => v.status === 'maintenance').length;
    this.gpsEquippedVehicles = this.vehicles.filter(v => v.hasGPS).length;
  }

  getVehicleName(vehicleId: string): string {
    const vehicle = this.vehicles.find(v => v.id === vehicleId);
    return vehicle ? vehicle.name : vehicleId;
  }

  formatShortDate(date: Date): string {
    return new Date(date).toLocaleDateString('fr-FR', { day: '2-digit', month: 'short' });
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
