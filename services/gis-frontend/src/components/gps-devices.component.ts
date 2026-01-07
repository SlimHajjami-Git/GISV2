import { Component, OnInit, NgZone, ChangeDetectorRef, ApplicationRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { ApiService } from '../services/api.service';
import { Vehicle, Company } from '../models/types';
import { AppLayoutComponent } from './shared/app-layout.component';

// View model for GPS device display
interface GPSDeviceView {
  vehicleId: string;
  vehicleName: string;
  vehiclePlate: string;
  imei: string;
  simNumber: string;
  simOperator: string;
  brand: string;
  model: string;
  installationDate?: Date;
  vehicleStatus: string;
  isOnline: boolean;
}

@Component({
  selector: 'app-gps-devices',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, AppLayoutComponent],
  template: `
    <app-layout>
      <div class="gps-devices-page">
        <div class="filter-bar">
          <div class="search-wrapper">
            <svg class="search-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/>
            </svg>
            <input type="text" class="search-input" placeholder="Rechercher par IMEI, modèle..." [(ngModel)]="searchQuery" (input)="filterDevices()">
          </div>
          <select class="filter-select" [(ngModel)]="filterStatus" (change)="filterDevices()">
            <option value="">Tous les statuts véhicule</option>
            <option value="in_use">En service</option>
            <option value="available">Disponible</option>
            <option value="maintenance">En maintenance</option>
          </select>
          <select class="filter-select" [(ngModel)]="filterOperator" (change)="filterDevices()">
            <option value="">Tous les opérateurs</option>
            <option value="ooredoo">Ooredoo</option>
            <option value="orange_tunisie">Orange Tunisie</option>
            <option value="tunisie_telecom">Tunisie Telecom</option>
          </select>
          <a routerLink="/vehicles" class="btn-add">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"/>
              <line x1="5" y1="12" x2="19" y2="12"/>
            </svg>
            Ajouter via Véhicule
          </a>
        </div>

        <!-- Stats Bar -->
        <div class="stats-bar">
          <div class="stat-item">
            <div class="stat-icon active">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="1" y="3" width="15" height="13" rx="2"/>
                <polygon points="16 8 20 8 23 11 23 16 16 16 16 8"/>
                <circle cx="5.5" cy="18.5" r="2.5"/>
                <circle cx="18.5" cy="18.5" r="2.5"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ getActiveCount() }}</span>
              <span class="stat-label">En service</span>
            </div>
          </div>
          <div class="stat-item">
            <div class="stat-icon available">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                <polyline points="22 4 12 14.01 9 11.01"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ getAvailableCount() }}</span>
              <span class="stat-label">Disponibles</span>
            </div>
          </div>
          <div class="stat-item">
            <div class="stat-icon danger">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ getMaintenanceCount() }}</span>
              <span class="stat-label">Maintenance</span>
            </div>
          </div>
          <div class="stat-item">
            <div class="stat-icon info">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z"/>
                <circle cx="12" cy="10" r="3"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ devices.length }}</span>
              <span class="stat-label">Total GPS</span>
            </div>
          </div>
        </div>

        <!-- Devices Table -->
        <div class="table-container">
          <table class="data-table">
            <thead>
              <tr>
                <th>Véhicule</th>
                <th>IMEI</th>
                <th>Appareil GPS</th>
                <th>N° SIM</th>
                <th>Opérateur</th>
                <th>Statut véhicule</th>
                <th>Date installation</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              @for (device of devices; track device.vehicleId) {
                <tr>
                  <td>
                    <div class="vehicle-info">
                      <span class="vehicle-name">{{ device.vehicleName }}</span>
                      <span class="vehicle-plate">{{ device.vehiclePlate }}</span>
                    </div>
                  </td>
                  <td class="imei-cell">{{ device.imei }}</td>
                  <td>{{ device.brand }} {{ device.model }}</td>
                  <td>{{ device.simNumber }}</td>
                  <td>
                    <span class="operator-badge" [class]="device.simOperator">
                      {{ getOperatorLabel(device.simOperator) }}
                    </span>
                  </td>
                  <td>
                    <span class="status-badge" [class]="device.vehicleStatus">
                      {{ getStatusLabel(device.vehicleStatus) }}
                    </span>
                  </td>
                  <td class="date-cell">{{ formatDate(device.installationDate) }}</td>
                  <td>
                    <div class="action-buttons">
                      <button class="action-btn" title="Voir véhicule" (click)="goToVehicle(device.vehicleId)">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/>
                          <circle cx="12" cy="12" r="3"/>
                        </svg>
                      </button>
                    </div>
                  </td>
                </tr>
              }
            </tbody>
          </table>
          
          @if (devices.length === 0) {
            <div class="empty-state">
              <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z"/>
                <circle cx="12" cy="10" r="3"/>
              </svg>
              <p>Aucun appareil GPS configuré</p>
              <span>Ajoutez un GPS via la page Véhicules</span>
            </div>
          }
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    .gps-devices-page {
      flex: 1;
      background: #f1f5f9;
      display: flex;
      flex-direction: column;
      min-height: calc(100vh - 42px);
    }

    .filter-bar {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 14px;
      background: white;
      border-bottom: 1px solid #e2e8f0;
    }

    .search-wrapper {
      position: relative;
      flex: 1;
      max-width: 300px;
    }

    .search-icon {
      position: absolute;
      left: 10px;
      top: 50%;
      transform: translateY(-50%);
      color: #94a3b8;
    }

    .search-input {
      width: 100%;
      padding: 6px 10px 6px 32px;
      font-family: var(--font-family);
      font-size: 12px;
      border: 1px solid #e2e8f0;
      border-radius: 3px;
      background: white;
      color: #1e293b;
    }

    .search-input:focus {
      outline: none;
      border-color: #3b82f6;
    }

    .filter-select {
      padding: 6px 10px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 3px;
      color: #1e293b;
      font-family: var(--font-family);
      font-size: 12px;
      cursor: pointer;
    }

    .btn-add {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 6px 12px;
      background: #3b82f6;
      color: white;
      border: none;
      border-radius: 3px;
      font-family: var(--font-family);
      font-size: 12px;
      font-weight: 500;
      cursor: pointer;
      margin-left: auto;
    }

    .btn-add:hover {
      background: #2563eb;
    }

    .stats-bar {
      display: flex;
      gap: 16px;
      padding: 12px 14px;
      background: white;
      border-bottom: 1px solid #e2e8f0;
    }

    .stat-item {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 8px 14px;
      background: #f8fafc;
      border-radius: 6px;
    }

    .stat-icon {
      width: 32px;
      height: 32px;
      border-radius: 6px;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .stat-icon.active { background: #dcfce7; color: #16a34a; }
    .stat-icon.warning { background: #fef3c7; color: #d97706; }
    .stat-icon.danger { background: #fee2e2; color: #dc2626; }
    .stat-icon.info { background: #dbeafe; color: #2563eb; }

    .stat-content {
      display: flex;
      flex-direction: column;
    }

    .stat-value {
      font-size: 16px;
      font-weight: 600;
      color: #1e293b;
    }

    .stat-label {
      font-size: 11px;
      color: #64748b;
    }

    .table-container {
      flex: 1;
      margin: 12px;
      background: white;
      border-radius: 6px;
      border: 1px solid #e2e8f0;
      overflow: auto;
    }

    .data-table {
      width: 100%;
      border-collapse: collapse;
      font-size: 12px;
    }

    .data-table th {
      padding: 10px 12px;
      text-align: left;
      background: #f8fafc;
      font-weight: 500;
      color: #64748b;
      border-bottom: 1px solid #e2e8f0;
      white-space: nowrap;
    }

    .data-table td {
      padding: 10px 12px;
      border-bottom: 1px solid #f1f5f9;
      color: #1e293b;
    }

    .data-table tr:hover {
      background: #f8fafc;
    }

    .imei-cell {
      font-family: 'Courier New', monospace;
      font-weight: 500;
    }

    .operator-badge {
      display: inline-block;
      padding: 2px 8px;
      border-radius: 3px;
      font-size: 10px;
      font-weight: 500;
    }

    .operator-badge.ooredoo { background: #fee2e2; color: #dc2626; }
    .operator-badge.orange_tunisie { background: #ffedd5; color: #c2410c; }
    .operator-badge.tunisie_telecom { background: #dbeafe; color: #1d4ed8; }
    .operator-badge.other { background: #f1f5f9; color: #64748b; }

    .status-badge {
      display: inline-block;
      padding: 2px 8px;
      border-radius: 3px;
      font-size: 10px;
      font-weight: 500;
    }

    .status-badge.active { background: #dcfce7; color: #16a34a; }
    .status-badge.inactive { background: #f1f5f9; color: #64748b; }
    .status-badge.maintenance { background: #fef3c7; color: #d97706; }
    .status-badge.unassigned { background: #fee2e2; color: #dc2626; }

    .vehicle-link {
      color: #3b82f6;
      font-weight: 500;
    }

    .no-vehicle {
      color: #94a3b8;
      font-style: italic;
    }

    .battery-indicator {
      display: flex;
      align-items: center;
      gap: 6px;
      width: 60px;
      height: 14px;
      background: #e2e8f0;
      border-radius: 2px;
      padding: 2px;
      position: relative;
    }

    .battery-fill {
      height: 100%;
      background: #22c55e;
      border-radius: 1px;
      transition: width 0.3s;
    }

    .battery-indicator.low .battery-fill {
      background: #ef4444;
    }

    .battery-indicator span {
      position: absolute;
      right: -30px;
      font-size: 10px;
      color: #64748b;
    }

    .signal-bars {
      display: flex;
      align-items: flex-end;
      gap: 2px;
      height: 14px;
    }

    .signal-bars .bar {
      width: 4px;
      background: #e2e8f0;
      border-radius: 1px;
    }

    .signal-bars .bar:nth-child(1) { height: 4px; }
    .signal-bars .bar:nth-child(2) { height: 6px; }
    .signal-bars .bar:nth-child(3) { height: 8px; }
    .signal-bars .bar:nth-child(4) { height: 10px; }
    .signal-bars .bar:nth-child(5) { height: 14px; }

    .signal-bars .bar.active {
      background: #22c55e;
    }

    .time-cell {
      font-size: 11px;
      color: #64748b;
    }

    .action-buttons {
      display: flex;
      gap: 6px;
    }

    .action-btn {
      padding: 4px;
      background: transparent;
      border: 1px solid #e2e8f0;
      border-radius: 3px;
      color: #64748b;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .action-btn:hover {
      background: #f8fafc;
      color: #1e293b;
    }

    .action-btn.danger:hover {
      background: #fee2e2;
      color: #dc2626;
      border-color: #fecaca;
    }

    .stat-icon.available { background: #dcfce7; color: #16a34a; }

    .vehicle-info {
      display: flex;
      flex-direction: column;
    }

    .vehicle-name {
      font-weight: 500;
      color: #1e293b;
    }

    .vehicle-plate {
      font-size: 10px;
      color: #64748b;
    }

    .date-cell {
      white-space: nowrap;
      color: #64748b;
    }

    .status-badge.in_use { background: #dbeafe; color: #2563eb; }
    .status-badge.available { background: #dcfce7; color: #16a34a; }
    .status-badge.maintenance { background: #fef3c7; color: #d97706; }

    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 60px 20px;
      color: #94a3b8;
    }

    .empty-state svg {
      margin-bottom: 16px;
      opacity: 0.5;
    }

    .empty-state p {
      font-size: 14px;
      font-weight: 500;
      color: #64748b;
      margin-bottom: 4px;
    }

    .empty-state span {
      font-size: 12px;
    }
  `]
})
export class GPSDevicesComponent implements OnInit {
  devices: GPSDeviceView[] = [];
  allDevices: GPSDeviceView[] = [];
  allVehicles: Vehicle[] = [];
  company: Company | null = null;

  searchQuery = '';
  filterStatus = '';
  filterOperator = '';

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
      this.loadData();
    });
  }

  loadData() {
    this.apiService.getVehicles().subscribe({
      next: (vehicles) => {
        this.ngZone.run(() => {
          this.allVehicles = vehicles;
          // Build GPS device views from vehicles that have GPS
          this.allDevices = this.allVehicles
            .filter(v => v.hasGPS && v.gpsImei)
            .map(v => ({
              vehicleId: v.id,
              vehicleName: v.name,
              vehiclePlate: v.plate,
              imei: v.gpsImei || '',
              simNumber: v.gpsSimNumber || '',
              simOperator: v.gpsSimOperator || '',
              brand: v.gpsBrand || '',
              model: v.gpsModel || '',
              installationDate: v.gpsInstallationDate,
              vehicleStatus: v.status,
              isOnline: v.isOnline || false
            }));
          this.devices = [...this.allDevices];
          this.cdr.detectChanges();
          this.appRef.tick();
        });
      },
      error: (err) => console.error('Error loading vehicles:', err)
    });
  }

  filterDevices() {
    this.devices = this.allDevices.filter(d => {
      const matchesSearch = !this.searchQuery ||
        d.imei.includes(this.searchQuery) ||
        d.model.toLowerCase().includes(this.searchQuery.toLowerCase()) ||
        d.brand.toLowerCase().includes(this.searchQuery.toLowerCase()) ||
        d.vehicleName.toLowerCase().includes(this.searchQuery.toLowerCase());
      const matchesStatus = !this.filterStatus || d.vehicleStatus === this.filterStatus;
      const matchesOperator = !this.filterOperator || d.simOperator === this.filterOperator;
      return matchesSearch && matchesStatus && matchesOperator;
    });
  }

  getActiveCount(): number {
    return this.allDevices.filter(d => d.vehicleStatus === 'in_use').length;
  }

  getAvailableCount(): number {
    return this.allDevices.filter(d => d.vehicleStatus === 'available').length;
  }

  getMaintenanceCount(): number {
    return this.allDevices.filter(d => d.vehicleStatus === 'maintenance').length;
  }

  getOperatorLabel(operator: string): string {
    const labels: any = {
      ooredoo: 'Ooredoo',
      orange_tunisie: 'Orange Tunisie',
      tunisie_telecom: 'Tunisie Telecom',
      other: 'Autre'
    };
    return labels[operator] || operator || '-';
  }

  getStatusLabel(status: string): string {
    const labels: any = {
      in_use: 'En service',
      available: 'Disponible',
      maintenance: 'Maintenance'
    };
    return labels[status] || status;
  }

  formatDate(date: Date | undefined): string {
    if (!date) return '-';
    return new Date(date).toLocaleDateString('fr-FR');
  }

  goToVehicle(vehicleId: string) {
    this.router.navigate(['/vehicles'], { queryParams: { id: vehicleId } });
  }
}
