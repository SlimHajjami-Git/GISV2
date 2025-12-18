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
        <!-- Header -->
        <div class="panel-header">
          <div class="driver-info">
            <div class="avatar" [style.background]="getScoreColor(driverScore?.overallScore || 0)">
              {{ employee?.name?.charAt(0) || '?' }}
            </div>
            <div class="info">
              <div class="name-row">
                <h2>{{ employee?.name }}</h2>
                <button class="chat-btn" title="Chat instantané" (click)="openChat()">
                  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/>
                  </svg>
                </button>
              </div>
              <span class="role">{{ getRoleLabel(employee?.role) }}</span>
            </div>
          </div>
          <button class="close-btn" (click)="close()">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="18" y1="6" x2="6" y2="18"/>
              <line x1="6" y1="6" x2="18" y2="18"/>
            </svg>
          </button>
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
          <!-- Personal Info Section -->
          <div class="section">
            <h3 class="section-title">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/>
                <circle cx="12" cy="7" r="4"/>
              </svg>
              Informations Personnelles
            </h3>
            <div class="info-grid">
              <div class="info-item">
                <span class="label">Email</span>
                <span class="value">{{ employee?.email }}</span>
              </div>
              <div class="info-item">
                <span class="label">Téléphone</span>
                <span class="value">{{ employee?.phone }}</span>
              </div>
              <div class="info-item">
                <span class="label">Statut</span>
                <span class="value status" [class.active]="employee?.status === 'active'">
                  {{ employee?.status === 'active' ? 'Actif' : 'Inactif' }}
                </span>
              </div>
              <div class="info-item">
                <span class="label">Date d'embauche</span>
                <span class="value">{{ formatDate(employee?.hireDate) }}</span>
              </div>
            </div>
          </div>

          <!-- Assigned Vehicle Section -->
          <div class="section" *ngIf="assignedVehicle">
            <h3 class="section-title">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M5 17h-2v-6l2-5h9l4 5v6h-2"/>
                <circle cx="7" cy="17" r="2"/>
                <circle cx="17" cy="17" r="2"/>
              </svg>
              Véhicule Assigné
            </h3>
            <div class="vehicle-card">
              <div class="vehicle-icon">
                <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M5 17h-2v-6l2-5h9l4 5v6h-2"/>
                  <circle cx="7" cy="17" r="2"/>
                  <circle cx="17" cy="17" r="2"/>
                </svg>
              </div>
              <div class="vehicle-info">
                <h4>{{ assignedVehicle.name }}</h4>
                <p>{{ assignedVehicle.brand }} {{ assignedVehicle.model }}</p>
                <span class="plate">{{ assignedVehicle.plate }}</span>
              </div>
            </div>
          </div>

          <!-- Driving Score Section -->
          <div class="section">
            <h3 class="section-title">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M12 20V10"/>
                <path d="M18 20V4"/>
                <path d="M6 20v-4"/>
              </svg>
              Score de Conduite
            </h3>

            <!-- Overall Score -->
            <div class="score-overview">
              <div class="score-circle" [style.--score-color]="getScoreColor(driverScore?.overallScore || 0)">
                <div class="score-value">{{ driverScore?.overallScore || 0 }}</div>
                <div class="score-label">/ 100</div>
              </div>
              <div class="score-details">
                <div class="detail-item">
                  <span class="detail-label">Trajets effectués</span>
                  <span class="detail-value">{{ driverScore?.totalTrips || 0 }}</span>
                </div>
                <div class="detail-item">
                  <span class="detail-label">Distance totale</span>
                  <span class="detail-value">{{ driverScore?.totalKmDriven || 0 }} km</span>
                </div>
                <div class="detail-item">
                  <span class="detail-label">Vitesse moyenne</span>
                  <span class="detail-value">{{ driverScore?.averageSpeed || 0 }} km/h</span>
                </div>
              </div>
            </div>

            <!-- Behavior Metrics -->
            <div class="metrics-grid">
              <div class="metric-card warning">
                <div class="metric-icon">
                  <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <circle cx="12" cy="12" r="10"/>
                    <line x1="12" y1="8" x2="12" y2="12"/>
                    <line x1="12" y1="16" x2="12.01" y2="16"/>
                  </svg>
                </div>
                <div class="metric-value">{{ driverScore?.harshBrakingCount || 0 }}</div>
                <div class="metric-label">Freinages Brusques</div>
              </div>
              <div class="metric-card danger">
                <div class="metric-icon">
                  <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polyline points="23 6 13.5 15.5 8.5 10.5 1 18"/>
                    <polyline points="17 6 23 6 23 12"/>
                  </svg>
                </div>
                <div class="metric-value">{{ driverScore?.harshAccelerationCount || 0 }}</div>
                <div class="metric-label">Accélérations Brusques</div>
              </div>
              <div class="metric-card info">
                <div class="metric-icon">
                  <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M12 2v4"/>
                    <path d="m16.2 7.8 2.9-2.9"/>
                    <path d="M18 12h4"/>
                    <path d="m16.2 16.2 2.9 2.9"/>
                    <path d="M12 18v4"/>
                    <path d="m4.9 19.1 2.9-2.9"/>
                    <path d="M2 12h4"/>
                    <path d="m4.9 4.9 2.9 2.9"/>
                  </svg>
                </div>
                <div class="metric-value">{{ driverScore?.speedingCount || 0 }}</div>
                <div class="metric-label">Excès de Vitesse</div>
              </div>
            </div>
          </div>

          <!-- Consumption per Vehicle -->
          <div class="section" *ngIf="driverScore?.vehicleConsumption?.length">
            <h3 class="section-title">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M3 3v18h18"/>
                <path d="M18.7 8l-5.1 5.2-2.8-2.7L7 14.3"/>
              </svg>
              Consommation par Véhicule
            </h3>
            <div class="consumption-table">
              <div class="table-header">
                <span>Véhicule</span>
                <span>Km</span>
                <span>Conso. Conducteur</span>
                <span>Conso. Véhicule</span>
                <span>Différence</span>
              </div>
              <div class="table-row" *ngFor="let vc of driverScore?.vehicleConsumption">
                <div class="vehicle-cell">
                  <span class="vehicle-name">{{ vc.vehicleName }}</span>
                  <span class="vehicle-plate">{{ vc.vehiclePlate }}</span>
                </div>
                <span class="km-cell">{{ vc.kmDriven }}</span>
                <span class="consumption-cell">{{ vc.avgConsumption }} L/100km</span>
                <span class="consumption-cell">{{ vc.vehicleAvgConsumption }} L/100km</span>
                <span class="diff-cell" [class.positive]="vc.avgConsumption <= vc.vehicleAvgConsumption" [class.negative]="vc.avgConsumption > vc.vehicleAvgConsumption">
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
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(0, 0, 0, 0.5);
      display: flex;
      justify-content: center;
      align-items: center;
      z-index: 1000;
      animation: fadeIn 0.2s ease;
    }

    @keyframes fadeIn {
      from { opacity: 0; }
      to { opacity: 1; }
    }

    .detail-panel {
      background: white;
      border-radius: 12px;
      width: 90%;
      max-width: 800px;
      max-height: 90vh;
      overflow: hidden;
      display: flex;
      flex-direction: column;
      animation: slideUp 0.3s ease;
    }

    @keyframes slideUp {
      from { transform: translateY(20px); opacity: 0; }
      to { transform: translateY(0); opacity: 1; }
    }

    .panel-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 20px 24px;
      border-bottom: 1px solid #e2e8f0;
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
      color: white;
    }

    .driver-info {
      display: flex;
      align-items: center;
      gap: 16px;
    }

    .avatar {
      width: 56px;
      height: 56px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 24px;
      font-weight: 600;
      color: white;
      border: 3px solid rgba(255,255,255,0.3);
    }

    .name-row {
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .info h2 {
      margin: 0;
      font-size: 20px;
      font-weight: 600;
    }

    .chat-btn {
      background: rgba(255,255,255,0.2);
      border: none;
      border-radius: 50%;
      width: 32px;
      height: 32px;
      display: flex;
      align-items: center;
      justify-content: center;
      cursor: pointer;
      color: white;
      transition: all 0.2s;
    }

    .chat-btn:hover {
      background: rgba(255,255,255,0.3);
      transform: scale(1.1);
    }

    .date-filter-wrapper :deep(.btn-apply) {
      display: none;
    }

    .role {
      font-size: 14px;
      opacity: 0.9;
    }

    .close-btn {
      background: rgba(255,255,255,0.2);
      border: none;
      border-radius: 8px;
      padding: 8px;
      cursor: pointer;
      color: white;
      transition: background 0.2s;
    }

    .close-btn:hover {
      background: rgba(255,255,255,0.3);
    }

    .panel-content {
      padding: 24px;
      overflow-y: auto;
      flex: 1;
    }

    .section {
      margin-bottom: 28px;
    }

    .section-title {
      display: flex;
      align-items: center;
      gap: 10px;
      font-size: 16px;
      font-weight: 600;
      color: #1e293b;
      margin-bottom: 16px;
      padding-bottom: 8px;
      border-bottom: 2px solid #e2e8f0;
    }

    .section-title svg {
      color: #6366f1;
    }

    .info-grid {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 16px;
    }

    .info-item {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .info-item .label {
      font-size: 12px;
      color: #64748b;
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }

    .info-item .value {
      font-size: 14px;
      color: #1e293b;
      font-weight: 500;
    }

    .info-item .value.status.active {
      color: #10b981;
    }

    .vehicle-card {
      display: flex;
      align-items: center;
      gap: 16px;
      padding: 16px;
      background: #f8fafc;
      border-radius: 10px;
      border: 1px solid #e2e8f0;
    }

    .vehicle-icon {
      width: 56px;
      height: 56px;
      background: linear-gradient(135deg, #3b82f6 0%, #1d4ed8 100%);
      border-radius: 12px;
      display: flex;
      align-items: center;
      justify-content: center;
      color: white;
    }

    .vehicle-info h4 {
      margin: 0;
      font-size: 16px;
      font-weight: 600;
      color: #1e293b;
    }

    .vehicle-info p {
      margin: 4px 0;
      font-size: 14px;
      color: #64748b;
    }

    .plate {
      display: inline-block;
      padding: 4px 10px;
      background: #1e293b;
      color: white;
      font-size: 12px;
      font-weight: 600;
      border-radius: 4px;
      letter-spacing: 1px;
    }

    .score-overview {
      display: flex;
      align-items: center;
      gap: 32px;
      padding: 24px;
      background: linear-gradient(135deg, #f8fafc 0%, #f1f5f9 100%);
      border-radius: 12px;
      margin-bottom: 20px;
    }

    .score-circle {
      width: 120px;
      height: 120px;
      border-radius: 50%;
      background: conic-gradient(var(--score-color) calc(var(--score, 0) * 3.6deg), #e2e8f0 0deg);
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      position: relative;
    }

    .score-circle::before {
      content: '';
      position: absolute;
      width: 90px;
      height: 90px;
      background: white;
      border-radius: 50%;
    }

    .score-value {
      position: relative;
      font-size: 36px;
      font-weight: 700;
      color: #1e293b;
    }

    .score-label {
      position: relative;
      font-size: 14px;
      color: #64748b;
    }

    .score-details {
      flex: 1;
      display: flex;
      flex-direction: column;
      gap: 12px;
    }

    .score-details .detail-item {
      display: flex;
      justify-content: space-between;
      padding: 8px 0;
      border-bottom: 1px solid #e2e8f0;
    }

    .score-details .detail-label {
      color: #64748b;
      font-size: 14px;
    }

    .score-details .detail-value {
      font-weight: 600;
      color: #1e293b;
      font-size: 14px;
    }

    .metrics-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 16px;
    }

    .metric-card {
      padding: 20px;
      border-radius: 12px;
      text-align: center;
      transition: transform 0.2s;
    }

    .metric-card:hover {
      transform: translateY(-2px);
    }

    .metric-card.warning {
      background: linear-gradient(135deg, #fef3c7 0%, #fde68a 100%);
      border: 1px solid #f59e0b;
    }

    .metric-card.warning .metric-icon {
      color: #d97706;
    }

    .metric-card.danger {
      background: linear-gradient(135deg, #fee2e2 0%, #fecaca 100%);
      border: 1px solid #ef4444;
    }

    .metric-card.danger .metric-icon {
      color: #dc2626;
    }

    .metric-card.info {
      background: linear-gradient(135deg, #dbeafe 0%, #bfdbfe 100%);
      border: 1px solid #3b82f6;
    }

    .metric-card.info .metric-icon {
      color: #2563eb;
    }

    .metric-value {
      font-size: 32px;
      font-weight: 700;
      margin: 8px 0;
      color: #1e293b;
    }

    .metric-label {
      font-size: 12px;
      color: #64748b;
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }

    .consumption-table {
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      overflow: hidden;
    }

    .table-header {
      display: grid;
      grid-template-columns: 2fr 1fr 1.5fr 1.5fr 1fr;
      padding: 12px 16px;
      background: #f8fafc;
      font-size: 12px;
      font-weight: 600;
      color: #64748b;
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }

    .table-row {
      display: grid;
      grid-template-columns: 2fr 1fr 1.5fr 1.5fr 1fr;
      padding: 14px 16px;
      border-top: 1px solid #e2e8f0;
      align-items: center;
      font-size: 14px;
    }

    .table-row:hover {
      background: #f8fafc;
    }

    .vehicle-cell {
      display: flex;
      flex-direction: column;
    }

    .vehicle-name {
      font-weight: 500;
      color: #1e293b;
    }

    .vehicle-plate {
      font-size: 12px;
      color: #64748b;
    }

    .km-cell, .consumption-cell {
      color: #475569;
    }

    .diff-cell {
      font-weight: 600;
      padding: 4px 8px;
      border-radius: 4px;
      text-align: center;
    }

    .diff-cell.positive {
      background: #dcfce7;
      color: #16a34a;
    }

    .diff-cell.negative {
      background: #fee2e2;
      color: #dc2626;
    }

    @media (max-width: 768px) {
      .detail-panel {
        width: 100%;
        max-width: none;
        max-height: 100vh;
        border-radius: 0;
      }

      .info-grid {
        grid-template-columns: 1fr;
      }

      .metrics-grid {
        grid-template-columns: 1fr;
      }

      .score-overview {
        flex-direction: column;
        text-align: center;
      }

      .table-header, .table-row {
        grid-template-columns: 1.5fr 1fr 1fr;
      }

      .table-header span:nth-child(4),
      .table-header span:nth-child(5),
      .table-row .consumption-cell:last-of-type,
      .table-row .diff-cell {
        display: none;
      }
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

  openChat() {
    // TODO: Implement chat functionality
    alert(`Ouvrir le chat avec ${this.employee?.name}`);
  }
}
