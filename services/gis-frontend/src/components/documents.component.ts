import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AppLayoutComponent } from './shared/app-layout.component';
import { DocumentRenewalPopupComponent } from './shared/document-renewal-popup.component';
import { ApiService } from '../services/api.service';
import { trigger, transition, style, animate } from '@angular/animations';

export interface VehicleDocument {
  id?: number;
  vehicleId: number;
  vehicleName: string;
  vehiclePlate: string;
  type: 'insurance' | 'tax' | 'technical_inspection' | 'registration' | 'transport_permit';
  expiryDate: Date;
  documentNumber?: string;
  documentUrl?: string;
  lastRenewalDate?: Date;
  lastRenewalCost?: number;
  reminderDays: number;
  status: 'expired' | 'expiring_soon' | 'ok';
  daysUntilExpiry: number;
}

@Component({
  selector: 'app-documents',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent, DocumentRenewalPopupComponent],
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 }))
      ])
    ]),
    trigger('scaleIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'scale(0.95)' }),
        animate('200ms ease-out', style({ opacity: 1, transform: 'scale(1)' }))
      ])
    ])
  ],
  template: `
    <app-layout>
      <div class="documents-page">
        <!-- Filter Bar -->
        <div class="filter-bar">
          <div class="search-wrapper">
            <svg class="search-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/>
            </svg>
            <input type="text" class="search-input" placeholder="Rechercher un véhicule..." [(ngModel)]="searchQuery" (input)="filterDocuments()">
          </div>
          <select class="filter-select" [(ngModel)]="filterType" (change)="filterDocuments()">
            <option value="">Tous les types</option>
            <option value="insurance">Assurance</option>
            <option value="tax">Vignette</option>
            <option value="technical_inspection">Visite technique</option>
            <option value="registration">Carte grise</option>
            <option value="transport_permit">Autorisation transport</option>
          </select>
          <select class="filter-select" [(ngModel)]="filterStatus" (change)="filterDocuments()">
            <option value="">Tous les statuts</option>
            <option value="expired">Expirés</option>
            <option value="expiring_soon">Expire bientôt</option>
            <option value="ok">En règle</option>
          </select>
          <button class="btn-export" (click)="exportToExcel()">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/>
              <polyline points="7 10 12 15 17 10"/>
              <line x1="12" y1="15" x2="12" y2="3"/>
            </svg>
            Exporter
          </button>
        </div>

        <!-- Stats Bar -->
        <div class="stats-bar">
          <div class="stat-item" [class.alert]="expiredCount > 0" (click)="filterByStatus('expired')">
            <div class="stat-icon expired">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10"/>
                <line x1="15" y1="9" x2="9" y2="15"/>
                <line x1="9" y1="9" x2="15" y2="15"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ expiredCount }}</span>
              <span class="stat-label">Expirés</span>
            </div>
          </div>
          <div class="stat-item" [class.warning]="expiringSoonCount > 0" (click)="filterByStatus('expiring_soon')">
            <div class="stat-icon warning">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/>
                <line x1="12" y1="9" x2="12" y2="13"/>
                <line x1="12" y1="17" x2="12.01" y2="17"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ expiringSoonCount }}</span>
              <span class="stat-label">Expire < 30j</span>
            </div>
          </div>
          <div class="stat-item" (click)="filterByStatus('ok')">
            <div class="stat-icon ok">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                <polyline points="22 4 12 14.01 9 11.01"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ okCount }}</span>
              <span class="stat-label">En règle</span>
            </div>
          </div>
          <div class="stat-item" (click)="filterByStatus('')">
            <div class="stat-icon total">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                <polyline points="14 2 14 8 20 8"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ allDocuments.length }}</span>
              <span class="stat-label">Total</span>
            </div>
          </div>
        </div>

        <!-- Documents Table -->
        <div class="table-container" @fadeIn>
          <table class="documents-table">
            <thead>
              <tr>
                <th class="sortable" (click)="sortBy('vehicleName')">
                  Véhicule
                  <svg *ngIf="sortColumn === 'vehicleName'" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polyline [attr.points]="sortDirection === 'asc' ? '18 15 12 9 6 15' : '6 9 12 15 18 9'"/>
                  </svg>
                </th>
                <th>Type</th>
                <th class="sortable" (click)="sortBy('expiryDate')">
                  Date expiration
                  <svg *ngIf="sortColumn === 'expiryDate'" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polyline [attr.points]="sortDirection === 'asc' ? '18 15 12 9 6 15' : '6 9 12 15 18 9'"/>
                  </svg>
                </th>
                <th>Statut</th>
                <th>N° Document</th>
                <th>Dernier coût</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let doc of filteredDocuments" @scaleIn [class.expired]="doc.status === 'expired'" [class.warning]="doc.status === 'expiring_soon'">
                <td class="vehicle-cell">
                  <div class="vehicle-info">
                    <span class="vehicle-name">{{ doc.vehicleName }}</span>
                    <span class="vehicle-plate">{{ doc.vehiclePlate }}</span>
                  </div>
                </td>
                <td>
                  <span class="type-badge" [class]="doc.type">
                    {{ getTypeIcon(doc.type) }} {{ getTypeLabel(doc.type) }}
                  </span>
                </td>
                <td class="date-cell">
                  <span class="expiry-date">{{ formatDate(doc.expiryDate) }}</span>
                  <span class="days-info" [class]="doc.status">
                    {{ getDaysText(doc.daysUntilExpiry) }}
                  </span>
                </td>
                <td>
                  <span class="status-badge" [class]="doc.status">
                    <span class="status-dot"></span>
                    {{ getStatusLabel(doc.status) }}
                  </span>
                </td>
                <td class="doc-number">{{ doc.documentNumber || '-' }}</td>
                <td class="cost-cell">
                  <span *ngIf="doc.lastRenewalCost">{{ doc.lastRenewalCost | number:'1.0-0' }} DT</span>
                  <span *ngIf="!doc.lastRenewalCost" class="no-data">-</span>
                </td>
                <td class="actions-cell">
                  <button class="btn-action renew" (click)="openRenewalPopup(doc)" title="Renouveler">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <polyline points="1 4 1 10 7 10"/>
                      <path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10"/>
                    </svg>
                  </button>
                  <button class="btn-action view" *ngIf="doc.documentUrl" (click)="viewDocument(doc)" title="Voir document">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/>
                      <circle cx="12" cy="12" r="3"/>
                    </svg>
                  </button>
                  <button class="btn-action history" (click)="viewHistory(doc)" title="Historique">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <circle cx="12" cy="12" r="10"/>
                      <polyline points="12 6 12 12 16 14"/>
                    </svg>
                  </button>
                </td>
              </tr>
            </tbody>
          </table>

          <!-- Empty State -->
          <div class="empty-state" *ngIf="filteredDocuments.length === 0">
            <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
              <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
              <polyline points="14 2 14 8 20 8"/>
              <line x1="16" y1="13" x2="8" y2="13"/>
              <line x1="16" y1="17" x2="8" y2="17"/>
            </svg>
            <h3>Aucun document trouvé</h3>
            <p>Modifiez vos filtres ou ajoutez des échéances aux véhicules</p>
          </div>
        </div>
      </div>

      <!-- Renewal Popup -->
      <app-document-renewal-popup
        [isOpen]="isRenewalPopupOpen"
        [document]="selectedDocument"
        (closed)="closeRenewalPopup()"
        (saved)="onRenewalSaved($event)"
      />
    </app-layout>
  `,
  styles: [`
    /* ===== PAGE LAYOUT ===== */
    .documents-page {
      flex: 1;
      background: #f1f5f9;
      display: flex;
      flex-direction: column;
      min-height: calc(100vh - 42px);
    }

    /* ===== FILTER BAR ===== */
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

    .filter-select:focus {
      outline: none;
      border-color: #3b82f6;
    }

    .btn-export {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 6px 12px;
      background: #1e3a5f;
      color: white;
      border: none;
      border-radius: 3px;
      font-family: var(--font-family);
      font-size: 12px;
      font-weight: 500;
      cursor: pointer;
      margin-left: auto;
    }

    .btn-export:hover {
      background: #2d5a87;
    }

    /* ===== STATS BAR ===== */
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
      padding: 10px 16px;
      background: #f8fafc;
      border-radius: 8px;
      cursor: pointer;
      transition: all 0.2s;
      border: 2px solid transparent;
    }

    .stat-item:hover {
      background: #f1f5f9;
    }

    .stat-item.alert {
      background: #fef2f2;
      border-color: #fecaca;
    }

    .stat-item.warning {
      background: #fffbeb;
      border-color: #fde68a;
    }

    .stat-icon {
      width: 36px;
      height: 36px;
      border-radius: 8px;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .stat-icon.expired { background: #fee2e2; color: #dc2626; }
    .stat-icon.warning { background: #fef3c7; color: #d97706; }
    .stat-icon.ok { background: #dcfce7; color: #16a34a; }
    .stat-icon.total { background: #dbeafe; color: #2563eb; }

    .stat-content {
      display: flex;
      flex-direction: column;
    }

    .stat-value {
      font-size: 20px;
      font-weight: 700;
      color: #1e293b;
    }

    .stat-label {
      font-size: 11px;
      color: #64748b;
    }

    /* ===== TABLE ===== */
    .table-container {
      flex: 1;
      padding: 14px;
      overflow: auto;
    }

    .documents-table {
      width: 100%;
      border-collapse: collapse;
      background: white;
      border-radius: 8px;
      overflow: hidden;
      box-shadow: 0 1px 3px rgba(0,0,0,0.08);
    }

    .documents-table thead {
      background: #f8fafc;
    }

    .documents-table th {
      padding: 12px 14px;
      text-align: left;
      font-size: 11px;
      font-weight: 600;
      color: #64748b;
      text-transform: uppercase;
      border-bottom: 1px solid #e2e8f0;
    }

    .documents-table th.sortable {
      cursor: pointer;
      user-select: none;
    }

    .documents-table th.sortable:hover {
      color: #3b82f6;
    }

    .documents-table th svg {
      margin-left: 4px;
      vertical-align: middle;
    }

    .documents-table td {
      padding: 12px 14px;
      font-size: 13px;
      color: #1e293b;
      border-bottom: 1px solid #f1f5f9;
    }

    .documents-table tr:last-child td {
      border-bottom: none;
    }

    .documents-table tr.expired {
      background: #fef2f2;
    }

    .documents-table tr.warning {
      background: #fffbeb;
    }

    .documents-table tr:hover {
      background: #f8fafc;
    }

    .documents-table tr.expired:hover {
      background: #fee2e2;
    }

    .documents-table tr.warning:hover {
      background: #fef3c7;
    }

    /* Vehicle Cell */
    .vehicle-cell .vehicle-info {
      display: flex;
      flex-direction: column;
    }

    .vehicle-name {
      font-weight: 600;
      color: #1e293b;
    }

    .vehicle-plate {
      font-size: 11px;
      color: #64748b;
      font-family: monospace;
    }

    /* Type Badge */
    .type-badge {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 4px 10px;
      border-radius: 4px;
      font-size: 11px;
      font-weight: 500;
    }

    .type-badge.insurance { background: #dbeafe; color: #1e40af; }
    .type-badge.tax { background: #fef3c7; color: #92400e; }
    .type-badge.technical_inspection { background: #dcfce7; color: #166534; }
    .type-badge.registration { background: #f3e8ff; color: #7c3aed; }
    .type-badge.transport_permit { background: #fce7f3; color: #be185d; }

    /* Date Cell */
    .date-cell {
      display: flex;
      flex-direction: column;
    }

    .expiry-date {
      font-weight: 500;
    }

    .days-info {
      font-size: 11px;
      margin-top: 2px;
    }

    .days-info.expired { color: #dc2626; }
    .days-info.expiring_soon { color: #d97706; }
    .days-info.ok { color: #16a34a; }

    /* Status Badge */
    .status-badge {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 4px 10px;
      border-radius: 20px;
      font-size: 11px;
      font-weight: 500;
    }

    .status-badge.expired { background: #fee2e2; color: #dc2626; }
    .status-badge.expiring_soon { background: #fef3c7; color: #d97706; }
    .status-badge.ok { background: #dcfce7; color: #16a34a; }

    .status-dot {
      width: 6px;
      height: 6px;
      border-radius: 50%;
      background: currentColor;
    }

    /* Cost Cell */
    .cost-cell {
      font-weight: 600;
      color: #1e293b;
    }

    .no-data {
      color: #94a3b8;
    }

    .doc-number {
      font-family: monospace;
      font-size: 12px;
    }

    /* Actions */
    .actions-cell {
      display: flex;
      gap: 6px;
    }

    .btn-action {
      width: 28px;
      height: 28px;
      border-radius: 6px;
      border: 1px solid #e2e8f0;
      background: white;
      color: #64748b;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }

    .btn-action:hover {
      border-color: #cbd5e1;
      color: #1e293b;
    }

    .btn-action.renew:hover {
      background: #dbeafe;
      border-color: #3b82f6;
      color: #2563eb;
    }

    .btn-action.view:hover {
      background: #dcfce7;
      border-color: #22c55e;
      color: #16a34a;
    }

    .btn-action.history:hover {
      background: #f3e8ff;
      border-color: #a855f7;
      color: #7c3aed;
    }

    /* Empty State */
    .empty-state {
      text-align: center;
      padding: 60px 40px;
      color: #64748b;
    }

    .empty-state svg {
      margin-bottom: 16px;
      opacity: 0.5;
    }

    .empty-state h3 {
      margin: 0 0 8px;
      color: #1e293b;
    }

    .empty-state p {
      margin: 0;
      font-size: 14px;
    }

    /* ===== RESPONSIVE ===== */
    @media (max-width: 1024px) {
      .filter-bar {
        flex-wrap: wrap;
      }

      .search-wrapper {
        max-width: 100%;
        width: 100%;
      }

      .stats-bar {
        flex-wrap: wrap;
      }

      .stat-item {
        flex: 1;
        min-width: 120px;
      }
    }

    @media (max-width: 768px) {
      .documents-table {
        font-size: 12px;
      }

      .documents-table th,
      .documents-table td {
        padding: 8px 10px;
      }
    }
  `]
})
export class DocumentsComponent implements OnInit {
  allDocuments: VehicleDocument[] = [];
  filteredDocuments: VehicleDocument[] = [];

  searchQuery = '';
  filterType = '';
  filterStatus = '';
  sortColumn = 'expiryDate';
  sortDirection: 'asc' | 'desc' = 'asc';

  isRenewalPopupOpen = false;
  selectedDocument: VehicleDocument | null = null;

  constructor(private apiService: ApiService) {}

  ngOnInit(): void {
    this.loadDocuments();
  }

  loadDocuments(): void {
    // Load vehicles and build document list from expiry dates
    this.apiService.getVehicles().subscribe({
      next: (vehicles) => {
        this.allDocuments = [];
        const today = new Date();

        vehicles.forEach(vehicle => {
          // Insurance
          if (vehicle.insuranceExpiry) {
            this.allDocuments.push(this.createDocumentEntry(vehicle, 'insurance', new Date(vehicle.insuranceExpiry)));
          } else {
            // Add placeholder for vehicles without insurance date
            this.allDocuments.push(this.createDocumentEntry(vehicle, 'insurance', null));
          }

          // Technical Inspection
          if (vehicle.technicalInspectionExpiry) {
            this.allDocuments.push(this.createDocumentEntry(vehicle, 'technical_inspection', new Date(vehicle.technicalInspectionExpiry)));
          } else {
            this.allDocuments.push(this.createDocumentEntry(vehicle, 'technical_inspection', null));
          }

          // Tax (vignette) - using a computed date for demo, should come from vehicle
          // For now we'll skip if not available
        });

        this.filterDocuments();
      },
      error: (err) => console.error('Error loading vehicles:', err)
    });
  }

  private createDocumentEntry(vehicle: any, type: 'insurance' | 'tax' | 'technical_inspection' | 'registration' | 'transport_permit', expiryDate: Date | null): VehicleDocument {
    const today = new Date();
    today.setHours(0, 0, 0, 0);

    let daysUntilExpiry = 999;
    let status: 'expired' | 'expiring_soon' | 'ok' = 'ok';

    if (expiryDate) {
      const expiry = new Date(expiryDate);
      expiry.setHours(0, 0, 0, 0);
      daysUntilExpiry = Math.ceil((expiry.getTime() - today.getTime()) / (1000 * 60 * 60 * 24));

      if (daysUntilExpiry < 0) {
        status = 'expired';
      } else if (daysUntilExpiry <= 30) {
        status = 'expiring_soon';
      } else {
        status = 'ok';
      }
    } else {
      status = 'expired'; // No date = expired/missing
      daysUntilExpiry = -999;
    }

    return {
      vehicleId: vehicle.id,
      vehicleName: vehicle.name || `${vehicle.brand} ${vehicle.model}`,
      vehiclePlate: vehicle.plate || 'N/A',
      type,
      expiryDate: expiryDate || new Date(),
      reminderDays: 30,
      status,
      daysUntilExpiry
    };
  }

  filterDocuments(): void {
    let docs = [...this.allDocuments];

    // Search filter
    if (this.searchQuery) {
      const query = this.searchQuery.toLowerCase();
      docs = docs.filter(d =>
        d.vehicleName.toLowerCase().includes(query) ||
        d.vehiclePlate.toLowerCase().includes(query)
      );
    }

    // Type filter
    if (this.filterType) {
      docs = docs.filter(d => d.type === this.filterType);
    }

    // Status filter
    if (this.filterStatus) {
      docs = docs.filter(d => d.status === this.filterStatus);
    }

    // Sort
    docs.sort((a, b) => {
      let comparison = 0;
      if (this.sortColumn === 'vehicleName') {
        comparison = a.vehicleName.localeCompare(b.vehicleName);
      } else if (this.sortColumn === 'expiryDate') {
        comparison = a.daysUntilExpiry - b.daysUntilExpiry;
      }
      return this.sortDirection === 'asc' ? comparison : -comparison;
    });

    this.filteredDocuments = docs;
  }

  filterByStatus(status: string): void {
    this.filterStatus = status;
    this.filterDocuments();
  }

  sortBy(column: string): void {
    if (this.sortColumn === column) {
      this.sortDirection = this.sortDirection === 'asc' ? 'desc' : 'asc';
    } else {
      this.sortColumn = column;
      this.sortDirection = 'asc';
    }
    this.filterDocuments();
  }

  get expiredCount(): number {
    return this.allDocuments.filter(d => d.status === 'expired').length;
  }

  get expiringSoonCount(): number {
    return this.allDocuments.filter(d => d.status === 'expiring_soon').length;
  }

  get okCount(): number {
    return this.allDocuments.filter(d => d.status === 'ok').length;
  }

  getTypeLabel(type: string): string {
    const labels: { [key: string]: string } = {
      'insurance': 'Assurance',
      'tax': 'Vignette',
      'technical_inspection': 'Visite technique',
      'registration': 'Carte grise',
      'transport_permit': 'Autorisation'
    };
    return labels[type] || type;
  }

  getTypeIcon(type: string): string {
    const icons: { [key: string]: string } = {
      'insurance': '🛡️',
      'tax': '📋',
      'technical_inspection': '🔧',
      'registration': '📄',
      'transport_permit': '🚛'
    };
    return icons[type] || '📄';
  }

  getStatusLabel(status: string): string {
    const labels: { [key: string]: string } = {
      'expired': 'Expiré',
      'expiring_soon': 'Expire bientôt',
      'ok': 'En règle'
    };
    return labels[status] || status;
  }

  getDaysText(days: number): string {
    if (days < -900) return 'Date non renseignée';
    if (days < 0) return `Expiré depuis ${Math.abs(days)} jour(s)`;
    if (days === 0) return 'Expire aujourd\'hui';
    if (days === 1) return 'Expire demain';
    return `Dans ${days} jours`;
  }

  formatDate(date: Date): string {
    if (!date) return '-';
    return new Date(date).toLocaleDateString('fr-FR', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric'
    });
  }

  openRenewalPopup(doc: VehicleDocument): void {
    this.selectedDocument = doc;
    this.isRenewalPopupOpen = true;
  }

  closeRenewalPopup(): void {
    this.isRenewalPopupOpen = false;
    this.selectedDocument = null;
  }

  onRenewalSaved(event: any): void {
    console.log('Renewal saved:', event);
    this.closeRenewalPopup();
    this.loadDocuments();
  }

  viewDocument(doc: VehicleDocument): void {
    if (doc.documentUrl) {
      window.open(doc.documentUrl, '_blank');
    }
  }

  viewHistory(doc: VehicleDocument): void {
    // TODO: Open history modal
    console.log('View history for:', doc);
  }

  exportToExcel(): void {
    // TODO: Implement Excel export
    console.log('Export to Excel');
  }
}
