import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../services/api.service';
import { MaintenanceRecord, Vehicle, Company } from '../models/types';
import { AppLayoutComponent } from './shared/app-layout.component';
import { MaintenancePopupComponent } from './shared/maintenance-popup.component';

@Component({
  selector: 'app-maintenance',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent, MaintenancePopupComponent],
  template: `
    <app-layout>
      <div class="maintenance-page">
        <div class="filter-bar">
          <div class="search-wrapper">
            <svg class="search-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/>
            </svg>
            <input type="text" class="search-input" placeholder="Rechercher..." [(ngModel)]="searchQuery" (input)="filterRecords()">
          </div>
          <select class="filter-select" [(ngModel)]="filterStatus" (change)="filterRecords()">
            <option value="">Tous les statuts</option>
            <option value="scheduled">Planifié</option>
            <option value="in_progress">En cours</option>
            <option value="completed">Terminé</option>
          </select>
          <select class="filter-select" [(ngModel)]="filterType" (change)="filterRecords()">
            <option value="">Tous les types</option>
            <option value="scheduled">Révision</option>
            <option value="repair">Réparation</option>
            <option value="oil_change">Vidange</option>
            <option value="tire_change">Pneumatiques</option>
            <option value="inspection">Contrôle technique</option>
          </select>
          <select class="filter-select" [(ngModel)]="filterVehicle" (change)="filterRecords()">
            <option value="">Tous les véhicules</option>
            @for (vehicle of vehicles; track vehicle.id) {
              <option [value]="vehicle.id">{{ vehicle.name }}</option>
            }
          </select>
          <button class="btn-add" (click)="openAddPopup()">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"/>
              <line x1="5" y1="12" x2="19" y2="12"/>
            </svg>
            Nouvelle maintenance
          </button>
        </div>

        <!-- Stats Bar -->
        <div class="stats-bar">
          <div class="stat-item">
            <div class="stat-icon scheduled">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="3" y="4" width="18" height="18" rx="2" ry="2"/>
                <line x1="16" y1="2" x2="16" y2="6"/>
                <line x1="8" y1="2" x2="8" y2="6"/>
                <line x1="3" y1="10" x2="21" y2="10"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ getScheduledCount() }}</span>
              <span class="stat-label">Planifiées</span>
            </div>
          </div>
          <div class="stat-item">
            <div class="stat-icon in-progress">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ getInProgressCount() }}</span>
              <span class="stat-label">En cours</span>
            </div>
          </div>
          <div class="stat-item">
            <div class="stat-icon completed">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                <polyline points="22 4 12 14.01 9 11.01"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ getCompletedCount() }}</span>
              <span class="stat-label">Terminées</span>
            </div>
          </div>
          <div class="stat-item cost">
            <div class="stat-icon cost">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="12" y1="1" x2="12" y2="23"/>
                <path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ getTotalCost() | number:'1.0-0' }} DH</span>
              <span class="stat-label">Coût total</span>
            </div>
          </div>
        </div>

        <!-- Records Table -->
        <div class="table-container">
          <table class="data-table">
            <thead>
              <tr>
                <th>Date</th>
                <th>Véhicule</th>
                <th>Type</th>
                <th>Description</th>
                <th>Kilométrage</th>
                <th>Prestataire</th>
                <th>Statut</th>
                <th>Coût</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              @for (record of records; track record.id) {
                <tr>
                  <td class="date-cell">{{ formatDate(record.date) }}</td>
                  <td>
                    <span class="vehicle-name">{{ getVehicleName(record.vehicleId) }}</span>
                  </td>
                  <td>
                    <span class="type-badge" [class]="record.type">
                      {{ getTypeLabel(record.type) }}
                    </span>
                  </td>
                  <td class="description-cell">{{ record.description }}</td>
                  <td>{{ record.mileageAtService | number:'1.0-0' }} km</td>
                  <td>{{ record.serviceProvider || '-' }}</td>
                  <td>
                    <span class="status-badge" [class]="record.status">
                      {{ getStatusLabel(record.status) }}
                    </span>
                  </td>
                  <td class="cost-cell">
                    <div class="cost-breakdown">
                      <span class="total-cost">{{ record.totalCost | number:'1.0-0' }} DH</span>
                      <span class="cost-detail">MO: {{ record.laborCost }} / Pièces: {{ record.partsCost }}</span>
                    </div>
                  </td>
                  <td>
                    <div class="action-buttons">
                      <button class="action-btn" title="Détails" (click)="viewDetails(record)">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/>
                          <circle cx="12" cy="12" r="3"/>
                        </svg>
                      </button>
                      <button class="action-btn" title="Modifier" (click)="editRecord(record)">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                          <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                        </svg>
                      </button>
                      <button class="action-btn danger" title="Supprimer" (click)="deleteRecord(record)">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <polyline points="3 6 5 6 21 6"/>
                          <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                        </svg>
                      </button>
                    </div>
                  </td>
                </tr>
              }
            </tbody>
          </table>

          @if (records.length === 0) {
            <div class="empty-state">
              <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>
              </svg>
              <p>Aucune maintenance enregistrée</p>
              <span>Ajoutez un nouvel enregistrement</span>
            </div>
          }
        </div>
      </div>

      <!-- Maintenance Popup -->
      <app-maintenance-popup
        [isOpen]="showPopup"
        [record]="selectedRecord"
        [vehicles]="vehicles"
        (closed)="closePopup()"
        (saved)="saveRecord($event)"
      ></app-maintenance-popup>
    </app-layout>
  `,
  styles: [`
    .maintenance-page {
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
      flex-wrap: wrap;
    }

    .search-wrapper {
      position: relative;
      flex: 1;
      max-width: 250px;
      min-width: 150px;
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
      flex-wrap: wrap;
    }

    .stat-item {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 8px 14px;
      background: #f8fafc;
      border-radius: 6px;
    }

    .stat-item.cost {
      margin-left: auto;
      background: #fef3c7;
    }

    .stat-icon {
      width: 32px;
      height: 32px;
      border-radius: 6px;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .stat-icon.scheduled { background: #dbeafe; color: #2563eb; }
    .stat-icon.in-progress { background: #fef3c7; color: #d97706; }
    .stat-icon.completed { background: #dcfce7; color: #16a34a; }
    .stat-icon.cost { background: #fde68a; color: #b45309; }

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

    .date-cell {
      white-space: nowrap;
      font-weight: 500;
    }

    .vehicle-name {
      font-weight: 500;
      color: #3b82f6;
    }

    .type-badge {
      display: inline-block;
      padding: 2px 8px;
      border-radius: 3px;
      font-size: 10px;
      font-weight: 500;
    }

    .type-badge.scheduled { background: #dbeafe; color: #1d4ed8; }
    .type-badge.repair { background: #fee2e2; color: #dc2626; }
    .type-badge.oil_change { background: #fef3c7; color: #b45309; }
    .type-badge.tire_change { background: #f3e8ff; color: #7c3aed; }
    .type-badge.inspection { background: #dcfce7; color: #16a34a; }
    .type-badge.other { background: #f1f5f9; color: #64748b; }

    .description-cell {
      max-width: 200px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .status-badge {
      display: inline-block;
      padding: 2px 8px;
      border-radius: 3px;
      font-size: 10px;
      font-weight: 500;
    }

    .status-badge.scheduled { background: #dbeafe; color: #2563eb; }
    .status-badge.in_progress { background: #fef3c7; color: #d97706; }
    .status-badge.completed { background: #dcfce7; color: #16a34a; }
    .status-badge.cancelled { background: #f1f5f9; color: #64748b; }

    .cost-cell {
      white-space: nowrap;
    }

    .cost-breakdown {
      display: flex;
      flex-direction: column;
    }

    .total-cost {
      font-weight: 600;
      color: #1e293b;
    }

    .cost-detail {
      font-size: 10px;
      color: #94a3b8;
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
export class MaintenanceComponent implements OnInit {
  records: MaintenanceRecord[] = [];
  allRecords: MaintenanceRecord[] = [];
  vehicles: Vehicle[] = [];
  company: Company | null = null;

  searchQuery = '';
  filterStatus = '';
  filterType = '';
  filterVehicle = '';

  constructor(
    private router: Router,
    private apiService: ApiService
  ) {}

  ngOnInit() {
    if (!this.apiService.isAuthenticated()) {
      this.router.navigate(['/login']);
      return;
    }

    this.loadData();
  }

  loadData() {
    this.apiService.getMaintenanceRecords().subscribe({
      next: (records) => {
        this.allRecords = records;
        this.records = [...this.allRecords];
      },
      error: (err) => console.error('Error loading maintenance:', err)
    });

    this.apiService.getVehicles().subscribe({
      next: (vehicles) => this.vehicles = vehicles,
      error: (err) => console.error('Error loading vehicles:', err)
    });
  }

  filterRecords() {
    this.records = this.allRecords.filter(r => {
      const matchesSearch = !this.searchQuery ||
        r.description.toLowerCase().includes(this.searchQuery.toLowerCase()) ||
        (r.serviceProvider || '').toLowerCase().includes(this.searchQuery.toLowerCase());
      const matchesStatus = !this.filterStatus || r.status === this.filterStatus;
      const matchesType = !this.filterType || r.type === this.filterType;
      const matchesVehicle = !this.filterVehicle || r.vehicleId === this.filterVehicle;
      return matchesSearch && matchesStatus && matchesType && matchesVehicle;
    });
  }

  getScheduledCount(): number {
    return this.allRecords.filter(r => r.status === 'scheduled').length;
  }

  getInProgressCount(): number {
    return this.allRecords.filter(r => r.status === 'in_progress').length;
  }

  getCompletedCount(): number {
    return this.allRecords.filter(r => r.status === 'completed').length;
  }

  getTotalCost(): number {
    return this.allRecords.reduce((sum, r) => sum + r.totalCost, 0);
  }

  getTypeLabel(type: string): string {
    const labels: any = {
      scheduled: 'Révision',
      repair: 'Réparation',
      oil_change: 'Vidange',
      tire_change: 'Pneumatiques',
      inspection: 'Contrôle technique',
      other: 'Autre'
    };
    return labels[type] || type;
  }

  getStatusLabel(status: string): string {
    const labels: any = {
      scheduled: 'Planifié',
      in_progress: 'En cours',
      completed: 'Terminé',
      cancelled: 'Annulé'
    };
    return labels[status] || status;
  }

  getVehicleName(vehicleId: string): string {
    const vehicle = this.vehicles.find(v => v.id === vehicleId);
    return vehicle ? vehicle.name : vehicleId;
  }

  formatDate(date: Date): string {
    return new Date(date).toLocaleDateString('fr-FR', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric'
    });
  }

  // Popup state
  showPopup = false;
  selectedRecord: MaintenanceRecord | null = null;

  openAddPopup() {
    this.selectedRecord = null;
    this.showPopup = true;
  }

  viewDetails(record: MaintenanceRecord) {
    this.selectedRecord = record;
    this.showPopup = true;
  }

  editRecord(record: MaintenanceRecord) {
    this.selectedRecord = record;
    this.showPopup = true;
  }

  closePopup() {
    this.showPopup = false;
    this.selectedRecord = null;
  }

  saveRecord(data: Partial<MaintenanceRecord>) {
    if (this.selectedRecord?.id) {
      // Update existing record
      this.apiService.updateMaintenanceRecord(parseInt(this.selectedRecord.id), data).subscribe({
        next: () => this.loadData(),
        error: (err) => console.error('Error updating record:', err)
      });
    } else {
      // Create new record
      this.apiService.createMaintenanceRecord(data).subscribe({
        next: () => this.loadData(),
        error: (err) => console.error('Error creating record:', err)
      });
    }

    this.closePopup();
  }

  deleteRecord(record: MaintenanceRecord) {
    if (confirm('Êtes-vous sûr de vouloir supprimer cet enregistrement ?')) {
      this.apiService.deleteMaintenanceRecord(parseInt(record.id)).subscribe({
        next: () => this.loadData(),
        error: (err) => console.error('Error deleting record:', err)
      });
    }
  }
}
