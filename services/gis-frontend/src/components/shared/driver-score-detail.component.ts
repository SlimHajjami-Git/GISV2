import { Component, Input, Output, EventEmitter, OnInit, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Employee, DriverScore, Vehicle, VehicleConsumptionData } from '../../models/types';
import { DateFilterBarComponent } from './ui/date-filter-bar.component';

@Component({
  selector: 'app-driver-score-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, DateFilterBarComponent],
  template: `
    <div class="overlay" *ngIf="isOpen" (click)="close()">
      <div class="detail-panel" (click)="$event.stopPropagation()">
        <!-- Header with gradient -->
        <div class="panel-header">
          <div class="header-bg"></div>
          <button class="close-btn" (click)="close()">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
              <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
            </svg>
          </button>
          <div class="header-content">
            <div class="avatar-wrapper">
              <div class="avatar" [style.border-color]="getScoreColor(driverScore?.overallScore || 0)">
                {{ getInitials() }}
              </div>
              <div class="score-badge" [style.background]="getScoreColor(driverScore?.overallScore || 0)">
                {{ driverScore?.overallScore || 0 }}
              </div>
            </div>
            <h2 class="driver-name">{{ employee?.name }}</h2>
            <div class="driver-meta">
              <span class="role-tag">{{ getRoleLabel(employee?.employeeRole) }}</span>
              <span class="status-tag" [class.active]="employee?.status === 'active'" [class.inactive]="employee?.status !== 'active'">
                <span class="dot"></span>{{ employee?.status === 'active' ? 'Actif' : 'Inactif' }}
              </span>
            </div>
          </div>
        </div>

        <!-- Quick Stats Bar -->
        <div class="quick-stats">
          <div class="qs-item">
            <span class="qs-value">{{ driverScore?.totalTrips || 0 }}</span>
            <span class="qs-label">Trajets</span>
          </div>
          <div class="qs-divider"></div>
          <div class="qs-item">
            <span class="qs-value">{{ driverScore?.totalKmDriven || 0 }}</span>
            <span class="qs-label">Km parcourus</span>
          </div>
          <div class="qs-divider"></div>
          <div class="qs-item">
            <span class="qs-value">{{ driverScore?.averageSpeed || 0 }}</span>
            <span class="qs-label">Km/h moy.</span>
          </div>
        </div>

        <!-- Date Filter -->
        <div class="date-filter-wrapper">
          <ui-date-filter-bar
            [selectedPeriod]="selectedPeriod"
            [fromDate]="fromDate"
            [toDate]="toDate"
            (periodChange)="onPeriodChange($event)"
            (dateRangeChange)="onDateRangeChange($event)">
          </ui-date-filter-bar>
        </div>

        <!-- Content -->
        <div class="panel-content">
          <!-- Info Cards Row -->
          <div class="info-cards">
            <div class="info-card">
              <div class="ic-icon email-icon">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/><polyline points="22,6 12,13 2,6"/></svg>
              </div>
              <div class="ic-content">
                <span class="ic-label">Email</span>
                <span class="ic-value">{{ employee?.email || '-' }}</span>
              </div>
            </div>
            <div class="info-card">
              <div class="ic-icon phone-icon">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.88.37 1.9.7 2.81 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.33 1.93.57 2.81.7A2 2 0 0 1 22 16.92z"/></svg>
              </div>
              <div class="ic-content">
                <span class="ic-label">Téléphone</span>
                <span class="ic-value">{{ employee?.phone || '-' }}</span>
              </div>
            </div>
            <div class="info-card">
              <div class="ic-icon date-icon">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="4" width="18" height="18" rx="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/></svg>
              </div>
              <div class="ic-content">
                <span class="ic-label">Embauché le</span>
                <span class="ic-value">{{ formatDate(employee?.hireDate) }}</span>
              </div>
            </div>
            <div class="info-card" *ngIf="employee?.cin">
              <div class="ic-icon cin-icon">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="2" y="5" width="20" height="14" rx="2"/><line x1="2" y1="10" x2="22" y2="10"/></svg>
              </div>
              <div class="ic-content">
                <span class="ic-label">CIN</span>
                <span class="ic-value">{{ employee?.cin }}</span>
              </div>
            </div>
          </div>

          <!-- Permit Section -->
          <div class="section" *ngIf="employee?.permitType">
            <h3 class="section-title">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="2" y="5" width="20" height="14" rx="2"/><line x1="2" y1="10" x2="22" y2="10"/></svg>
              Permis de conduire
            </h3>
            <div class="permit-card">
              <div class="permit-top">
                <div class="permit-type-badge">{{ employee?.permitType }}</div>
                <div class="permit-number" *ngIf="employee?.permitNumber">N° {{ employee?.permitNumber }}</div>
              </div>
              <div class="permit-bottom" *ngIf="employee?.permitExpiry">
                <div class="permit-expiry-info">
                  <span class="permit-expiry-label">Date d'expiration</span>
                  <span class="permit-expiry-date">{{ formatDate(employee?.permitExpiry) }}</span>
                </div>
                <div class="permit-status-badge" [class]="getPermitStatusClass()">
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg>
                  {{ getPermitDeadlineText() }}
                </div>
              </div>
            </div>
          </div>

          <!-- Assigned Vehicle Section -->
          <div class="section" *ngIf="assignedVehicle">
            <h3 class="section-title">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M5 17h-2v-6l2-5h9l4 5v6h-2"/><circle cx="7" cy="17" r="2"/><circle cx="17" cy="17" r="2"/></svg>
              Véhicule assigné
            </h3>
            <div class="vehicle-card">
              <div class="vc-icon">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M5 17h-2v-6l2-5h9l4 5v6h-2"/><circle cx="7" cy="17" r="2"/><circle cx="17" cy="17" r="2"/></svg>
              </div>
              <div class="vc-info">
                <span class="vc-name">{{ assignedVehicle.name }}</span>
                <span class="vc-brand">{{ assignedVehicle.brand }} {{ assignedVehicle.model }}</span>
              </div>
              <span class="vc-plate">{{ assignedVehicle.plate }}</span>
            </div>
          </div>

          <!-- Driving Score Section -->
          <div class="section">
            <h3 class="section-title">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 20V10"/><path d="M18 20V4"/><path d="M6 20v-4"/></svg>
              Score de conduite
            </h3>

            <!-- Behavior Metrics -->
            <div class="metrics-row">
              <div class="metric-tile braking">
                <div class="mt-header">
                  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
                  <span class="mt-count">{{ driverScore?.harshBrakingCount || 0 }}</span>
                </div>
                <span class="mt-label">Freinages brusques</span>
                <div class="mt-bar"><div class="mt-fill" [style.width.%]="getMetricPercent(driverScore?.harshBrakingCount || 0)"></div></div>
              </div>
              <div class="metric-tile acceleration">
                <div class="mt-header">
                  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="23 6 13.5 15.5 8.5 10.5 1 18"/><polyline points="17 6 23 6 23 12"/></svg>
                  <span class="mt-count">{{ driverScore?.harshAccelerationCount || 0 }}</span>
                </div>
                <span class="mt-label">Accélérations brusques</span>
                <div class="mt-bar"><div class="mt-fill" [style.width.%]="getMetricPercent(driverScore?.harshAccelerationCount || 0)"></div></div>
              </div>
              <div class="metric-tile speeding">
                <div class="mt-header">
                  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 2v4"/><path d="m16.2 7.8 2.9-2.9"/><path d="M18 12h4"/><path d="M2 12h4"/><path d="m4.9 4.9 2.9 2.9"/></svg>
                  <span class="mt-count">{{ driverScore?.speedingCount || 0 }}</span>
                </div>
                <span class="mt-label">Excès de vitesse</span>
                <div class="mt-bar"><div class="mt-fill" [style.width.%]="getMetricPercent(driverScore?.speedingCount || 0)"></div></div>
              </div>
            </div>
          </div>

          <!-- Consumption per Vehicle -->
          <div class="section" *ngIf="driverScore?.vehicleConsumption?.length">
            <h3 class="section-title">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 3v18h18"/><path d="M18.7 8l-5.1 5.2-2.8-2.7L7 14.3"/></svg>
              Consommation par véhicule
            </h3>
            <div class="consumption-table">
              <div class="ct-header">
                <span>Véhicule</span>
                <span>Km</span>
                <span>Conso.</span>
                <span>Réf.</span>
                <span>Diff.</span>
              </div>
              <div class="ct-row" *ngFor="let vc of driverScore?.vehicleConsumption">
                <div class="ct-vehicle">
                  <span class="ct-name">{{ vc.vehicleName }}</span>
                  <span class="ct-plate">{{ vc.vehiclePlate }}</span>
                </div>
                <span class="ct-km">{{ vc.kmDriven }}</span>
                <span class="ct-val">{{ vc.avgConsumption }}</span>
                <span class="ct-val">{{ vc.vehicleAvgConsumption }}</span>
                <span class="ct-diff" [class.positive]="vc.avgConsumption <= vc.vehicleAvgConsumption" [class.negative]="vc.avgConsumption > vc.vehicleAvgConsumption">
                  {{ getDiffLabel(vc.avgConsumption, vc.vehicleAvgConsumption) }}
                </span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .overlay {
      position: fixed; top: 0; left: 0; right: 0; bottom: 0;
      background: rgba(15, 23, 42, 0.6); backdrop-filter: blur(4px);
      display: flex; justify-content: flex-end; z-index: 1000;
      animation: fadeIn 0.2s ease;
    }
    @keyframes fadeIn { from { opacity: 0; } to { opacity: 1; } }

    .detail-panel {
      background: #fff; width: 520px; max-width: 100vw; height: 100vh;
      display: flex; flex-direction: column; animation: slideIn 0.3s ease;
      box-shadow: -8px 0 30px rgba(0,0,0,0.15);
    }
    @keyframes slideIn { from { transform: translateX(100%); } to { transform: translateX(0); } }

    /* Header */
    .panel-header {
      position: relative; padding: 0; overflow: hidden;
    }
    .header-bg {
      position: absolute; inset: 0;
      background: linear-gradient(135deg, #6366f1 0%, #8b5cf6 50%, #a78bfa 100%);
    }
    .close-btn {
      position: absolute; top: 12px; right: 12px; z-index: 2;
      width: 32px; height: 32px; border-radius: 8px;
      background: rgba(255,255,255,0.15); border: none;
      color: #fff; cursor: pointer; display: flex;
      align-items: center; justify-content: center;
      transition: background 0.15s;
    }
    .close-btn:hover { background: rgba(255,255,255,0.25); }

    .header-content {
      position: relative; z-index: 1; padding: 28px 24px 20px;
      display: flex; flex-direction: column; align-items: center; text-align: center;
    }
    .avatar-wrapper { position: relative; margin-bottom: 12px; }
    .avatar {
      width: 64px; height: 64px; border-radius: 50%;
      background: rgba(255,255,255,0.15); border: 3px solid;
      display: flex; align-items: center; justify-content: center;
      font-size: 22px; font-weight: 700; color: #fff;
      text-transform: uppercase;
    }
    .score-badge {
      position: absolute; bottom: -4px; right: -4px;
      width: 28px; height: 28px; border-radius: 50%;
      display: flex; align-items: center; justify-content: center;
      font-size: 11px; font-weight: 800; color: #fff;
      border: 2px solid #fff; box-shadow: 0 2px 6px rgba(0,0,0,0.2);
    }
    .driver-name { margin: 0; font-size: 18px; font-weight: 700; color: #fff; }
    .driver-meta { display: flex; align-items: center; gap: 8px; margin-top: 8px; }
    .role-tag {
      padding: 3px 10px; border-radius: 12px; font-size: 11px; font-weight: 600;
      background: rgba(255,255,255,0.2); color: #fff;
    }
    .status-tag {
      display: flex; align-items: center; gap: 4px;
      padding: 3px 10px; border-radius: 12px; font-size: 11px; font-weight: 600;
    }
    .status-tag.active { background: rgba(16,185,129,0.2); color: #a7f3d0; }
    .status-tag.inactive { background: rgba(239,68,68,0.2); color: #fecaca; }
    .status-tag .dot { width: 5px; height: 5px; border-radius: 50%; background: currentColor; }

    /* Quick Stats */
    .quick-stats {
      display: flex; align-items: center; justify-content: center;
      padding: 14px 24px; background: #f8fafc; border-bottom: 1px solid #e2e8f0;
    }
    .qs-item { display: flex; flex-direction: column; align-items: center; flex: 1; }
    .qs-value { font-size: 18px; font-weight: 800; color: #0f172a; }
    .qs-label { font-size: 10px; color: #94a3b8; text-transform: uppercase; letter-spacing: 0.3px; margin-top: 1px; }
    .qs-divider { width: 1px; height: 28px; background: #e2e8f0; }

    .date-filter-wrapper { border-bottom: 1px solid #e2e8f0; }
    .date-filter-wrapper :deep(.btn-apply) { display: none; }

    /* Content */
    .panel-content { padding: 20px 24px; overflow-y: auto; flex: 1; }

    /* Info Cards */
    .info-cards { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin-bottom: 20px; }
    .info-card {
      display: flex; align-items: center; gap: 10px;
      padding: 10px 12px; background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px;
    }
    .ic-icon {
      width: 32px; height: 32px; border-radius: 8px; flex-shrink: 0;
      display: flex; align-items: center; justify-content: center;
    }
    .ic-icon.email-icon { background: #ede9fe; color: #7c3aed; }
    .ic-icon.phone-icon { background: #dcfce7; color: #16a34a; }
    .ic-icon.date-icon { background: #dbeafe; color: #2563eb; }
    .ic-icon.cin-icon { background: #fef3c7; color: #d97706; }
    .ic-content { display: flex; flex-direction: column; min-width: 0; }
    .ic-label { font-size: 10px; color: #94a3b8; text-transform: uppercase; letter-spacing: 0.3px; }
    .ic-value { font-size: 12px; font-weight: 600; color: #0f172a; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }

    /* Sections */
    .section { margin-bottom: 20px; }
    .section-title {
      display: flex; align-items: center; gap: 8px;
      font-size: 13px; font-weight: 700; color: #0f172a;
      margin: 0 0 10px; padding-bottom: 8px; border-bottom: 1px solid #e2e8f0;
    }
    .section-title svg { color: #6366f1; }

    /* Permit Card */
    .permit-card {
      background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 10px; overflow: hidden;
    }
    .permit-top {
      display: flex; align-items: center; gap: 10px; padding: 12px 14px;
      border-bottom: 1px solid #e2e8f0;
    }
    .permit-type-badge {
      padding: 4px 12px; background: linear-gradient(135deg, #6366f1, #4f46e5);
      color: #fff; border-radius: 6px; font-size: 13px; font-weight: 800;
    }
    .permit-number { font-size: 12px; color: #64748b; }
    .permit-bottom { display: flex; align-items: center; justify-content: space-between; padding: 10px 14px; }
    .permit-expiry-info { display: flex; flex-direction: column; }
    .permit-expiry-label { font-size: 10px; color: #94a3b8; text-transform: uppercase; }
    .permit-expiry-date { font-size: 13px; font-weight: 700; color: #0f172a; }
    .permit-status-badge {
      display: flex; align-items: center; gap: 4px;
      padding: 4px 10px; border-radius: 6px; font-size: 11px; font-weight: 600;
    }
    .permit-status-badge.ok { background: #dcfce7; color: #16a34a; }
    .permit-status-badge.warning { background: #fef3c7; color: #d97706; }
    .permit-status-badge.critical { background: #fee2e2; color: #dc2626; animation: pulse 2s infinite; }
    .permit-status-badge.expired { background: #1e293b; color: #fff; }
    @keyframes pulse { 0%,100% { opacity:1; } 50% { opacity:0.7; } }

    /* Vehicle Card */
    .vehicle-card {
      display: flex; align-items: center; gap: 12px;
      padding: 12px 14px; background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 10px;
    }
    .vc-icon {
      width: 44px; height: 44px; border-radius: 10px;
      background: linear-gradient(135deg, #3b82f6, #1d4ed8);
      display: flex; align-items: center; justify-content: center; color: #fff; flex-shrink: 0;
    }
    .vc-info { flex: 1; display: flex; flex-direction: column; }
    .vc-name { font-size: 13px; font-weight: 600; color: #0f172a; }
    .vc-brand { font-size: 11px; color: #64748b; }
    .vc-plate {
      padding: 4px 10px; background: #1e293b; color: #fff; border-radius: 4px;
      font-size: 11px; font-weight: 700; letter-spacing: 0.5px; flex-shrink: 0;
    }

    /* Metrics */
    .metrics-row { display: flex; gap: 10px; }
    .metric-tile {
      flex: 1; padding: 14px; border-radius: 10px; border: 1px solid;
    }
    .metric-tile.braking { background: #fef3c7; border-color: #fde68a; }
    .metric-tile.braking svg { color: #d97706; }
    .metric-tile.acceleration { background: #fee2e2; border-color: #fecaca; }
    .metric-tile.acceleration svg { color: #dc2626; }
    .metric-tile.speeding { background: #dbeafe; border-color: #bfdbfe; }
    .metric-tile.speeding svg { color: #2563eb; }

    .mt-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 6px; }
    .mt-count { font-size: 22px; font-weight: 800; color: #0f172a; }
    .mt-label { font-size: 10px; color: #475569; text-transform: uppercase; letter-spacing: 0.3px; }
    .mt-bar { height: 4px; background: rgba(0,0,0,0.08); border-radius: 2px; margin-top: 8px; }
    .mt-fill { height: 100%; border-radius: 2px; background: currentColor; transition: width 0.3s; }
    .metric-tile.braking .mt-fill { background: #d97706; }
    .metric-tile.acceleration .mt-fill { background: #dc2626; }
    .metric-tile.speeding .mt-fill { background: #2563eb; }

    /* Consumption Table */
    .consumption-table { border: 1px solid #e2e8f0; border-radius: 10px; overflow: hidden; }
    .ct-header {
      display: grid; grid-template-columns: 2fr 1fr 1fr 1fr 1fr;
      padding: 8px 14px; background: #f8fafc;
      font-size: 10px; font-weight: 600; color: #64748b; text-transform: uppercase; letter-spacing: 0.3px;
    }
    .ct-row {
      display: grid; grid-template-columns: 2fr 1fr 1fr 1fr 1fr;
      padding: 10px 14px; border-top: 1px solid #f1f5f9; align-items: center; font-size: 12px;
    }
    .ct-row:hover { background: #f8fafc; }
    .ct-vehicle { display: flex; flex-direction: column; }
    .ct-name { font-weight: 600; color: #0f172a; }
    .ct-plate { font-size: 10px; color: #94a3b8; }
    .ct-km, .ct-val { color: #475569; }
    .ct-diff { font-weight: 700; padding: 2px 6px; border-radius: 4px; text-align: center; font-size: 11px; }
    .ct-diff.positive { background: #dcfce7; color: #16a34a; }
    .ct-diff.negative { background: #fee2e2; color: #dc2626; }

    @media (max-width: 768px) {
      .detail-panel { width: 100%; }
      .info-cards { grid-template-columns: 1fr; }
      .metrics-row { flex-direction: column; }
      .ct-header, .ct-row { grid-template-columns: 2fr 1fr 1fr; }
      .ct-header span:nth-child(4), .ct-header span:nth-child(5),
      .ct-row span:nth-child(4), .ct-row span:nth-child(5) { display: none; }
    }
  `]
})
export class DriverScoreDetailComponent implements OnInit, OnChanges {
  @Input() isOpen = false;
  @Input() employee: Employee | null = null;
  @Input() driverScore: DriverScore | null = null;
  @Input() assignedVehicle: Vehicle | null = null;
  @Output() closed = new EventEmitter<void>();
  @Output() dateFilterChanged = new EventEmitter<{ from: string; to: string }>();

  selectedPeriod = 'week';
  fromDate = '';
  toDate = '';

  ngOnInit() {
    this.initializeDates();
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['isOpen'] && this.isOpen) {
      this.initializeDates();
    }
  }

  initializeDates() {
    const today = new Date();
    const weekAgo = new Date(today.getTime() - 7 * 24 * 60 * 60 * 1000);
    this.fromDate = weekAgo.toISOString().split('T')[0];
    this.toDate = today.toISOString().split('T')[0];
  }

  close() {
    this.closed.emit();
  }

  onPeriodChange(period: string) {
    this.selectedPeriod = period;
  }

  onDateRangeChange(range: { from: string; to: string }) {
    this.fromDate = range.from;
    this.toDate = range.to;
  }

  applyFilter() {
    this.dateFilterChanged.emit({ from: this.fromDate, to: this.toDate });
  }

  getRoleLabel(role?: string): string {
    const labels: Record<string, string> = {
      driver: 'Chauffeur',
      accountant: 'Comptable',
      hr: 'Ressources Humaines',
      supervisor: 'Superviseur',
      other: 'Autre'
    };
    return labels[role || ''] || role || '';
  }

  formatDate(date?: Date): string {
    if (!date) return '-';
    return new Date(date).toLocaleDateString('fr-FR');
  }

  getScoreColor(score: number): string {
    if (score >= 80) return '#10b981';
    if (score >= 60) return '#f59e0b';
    return '#ef4444';
  }

  getDiffLabel(driverConsumption: number, vehicleConsumption: number): string {
    const diff = driverConsumption - vehicleConsumption;
    if (diff <= 0) {
      return `${Math.abs(diff).toFixed(1)} ↓`;
    }
    return `+${diff.toFixed(1)} ↑`;
  }

  getInitials(): string {
    const first = this.employee?.firstName || this.employee?.name?.split(' ')[0] || '?';
    const last = this.employee?.lastName || this.employee?.name?.split(' ')[1] || '';
    return (first.charAt(0) + last.charAt(0)).toUpperCase();
  }

  getMetricPercent(count: number): number {
    return Math.min(count * 5, 100);
  }

  getPermitStatusClass(): string {
    if (!this.employee?.permitExpiry) return 'ok';
    const today = new Date(); today.setHours(0, 0, 0, 0);
    const expiry = new Date(this.employee.permitExpiry); expiry.setHours(0, 0, 0, 0);
    const days = Math.ceil((expiry.getTime() - today.getTime()) / (1000 * 60 * 60 * 24));
    if (days < 0) return 'expired';
    if (days <= 15) return 'critical';
    if (days <= 30) return 'warning';
    return 'ok';
  }

  getPermitDeadlineText(): string {
    if (!this.employee?.permitExpiry) return '';
    const today = new Date(); today.setHours(0, 0, 0, 0);
    const expiry = new Date(this.employee.permitExpiry); expiry.setHours(0, 0, 0, 0);
    const days = Math.ceil((expiry.getTime() - today.getTime()) / (1000 * 60 * 60 * 24));
    if (days < 0) return 'Expiré';
    if (days === 0) return "Expire aujourd'hui";
    if (days <= 30) return `Expire dans ${days}j`;
    return `Valide (${days}j)`;
  }
}
