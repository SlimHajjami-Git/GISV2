import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService } from '../services/admin.service';

@Component({
  selector: 'admin-auto-recovery',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent],
  template: `
    <admin-layout pageTitle="Auto-Recovery Log">
      <div class="recovery-page">
        <div class="page-header">
          <div>
            <h2>Détections auto-recovery</h2>
            <p class="subtitle">Historique des commandes AJ+GO envoyées automatiquement quand le Rust détecte bit5=0 (immobilisation non demandée)</p>
          </div>
          <div class="header-actions">
            <select [(ngModel)]="selectedCompanyId" (ngModelChange)="load()">
              <option [ngValue]="null">Toutes les sociétés</option>
              <option *ngFor="let c of companies" [ngValue]="c.id">{{ c.name }}</option>
            </select>
            <select [(ngModel)]="limit" (ngModelChange)="load()">
              <option [ngValue]="50">50</option>
              <option [ngValue]="100">100</option>
              <option [ngValue]="500">500</option>
            </select>
            <button class="btn-refresh" (click)="load()">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 12a9 9 0 1 1-9-9c2.52 0 4.93 1 6.74 2.74L21 8"/><path d="M21 3v5h-5"/></svg>
              Actualiser
            </button>
          </div>
        </div>

        <div class="stats-bar" *ngIf="!loading && records.length > 0">
          <div class="stat-card">
            <span class="stat-value">{{ records.length }}</span>
            <span class="stat-label">Détections</span>
          </div>
          <div class="stat-card">
            <span class="stat-value">{{ uniqueDevices }}</span>
            <span class="stat-label">Véhicules concernés</span>
          </div>
          <div class="stat-card" *ngIf="lastDetection">
            <span class="stat-value">{{ lastDetection | date:'dd/MM HH:mm' }}</span>
            <span class="stat-label">Dernière détection</span>
          </div>
        </div>

        <div class="loading-state" *ngIf="loading">
          <div class="spinner"></div>
          <span>Chargement...</span>
        </div>

        <div class="table-container" *ngIf="!loading && records.length > 0">
          <table>
            <thead>
              <tr>
                <th>#</th>
                <th>Date détection</th>
                <th>MAT</th>
                <th>IMEI</th>
                <th>Véhicule</th>
                <th>Plaque</th>
                <th>Société</th>
                <th>Téléphone</th>
                <th>Type</th>
                <th>Statut</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let r of records; let i = index">
                <td class="mono">{{ r.id }}</td>
                <td>{{ r.createdAt | date:'dd/MM/yyyy HH:mm:ss' }}</td>
                <td class="mono">{{ r.deviceMat || '-' }}</td>
                <td class="mono">{{ r.deviceUid || '-' }}</td>
                <td>{{ r.vehicleName || '-' }}</td>
                <td>{{ r.vehiclePlate || '-' }}</td>
                <td>{{ r.companyName || '-' }}</td>
                <td class="mono">{{ r.driverPhone || '-' }}</td>
                <td><span class="type-badge" [ngClass]="getCommandClass(r.flagsHex)">{{ getCommandLabel(r.flagsHex) }}</span></td>
                <td>
                  <span class="status-badge" [ngClass]="r.status">{{ r.status }}</span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <div class="empty-state" *ngIf="!loading && records.length === 0">
          <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="#94a3b8" stroke-width="1.5">
            <path d="M9 12l2 2 4-4"/><circle cx="12" cy="12" r="10"/>
          </svg>
          <p>Aucune détection auto-recovery enregistrée</p>
        </div>
      </div>
    </admin-layout>
  `,
  styles: [`
    .recovery-page { padding: 24px; }

    .page-header {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      margin-bottom: 20px;
      gap: 16px;
      flex-wrap: wrap;
    }
    .page-header h2 { margin: 0; font-size: 20px; color: #1e293b; }
    .subtitle { margin: 4px 0 0; font-size: 13px; color: #64748b; }
    .header-actions { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
    .header-actions select {
      padding: 6px 10px;
      border: 1px solid #d1d5db;
      border-radius: 6px;
      font-size: 13px;
      background: #fff;
    }

    .btn-refresh {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 7px 14px;
      background: #3b82f6;
      color: #fff;
      border: none;
      border-radius: 6px;
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
    }
    .btn-refresh:hover { background: #2563eb; }

    .stats-bar {
      display: flex;
      gap: 16px;
      margin-bottom: 20px;
    }
    .stat-card {
      background: #fff;
      border: 1px solid #e5e7eb;
      border-radius: 8px;
      padding: 12px 20px;
      display: flex;
      flex-direction: column;
      gap: 2px;
    }
    .stat-value { font-size: 20px; font-weight: 700; color: #1e293b; }
    .stat-label { font-size: 12px; color: #64748b; }

    .loading-state {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 40px;
      justify-content: center;
      color: #64748b;
    }
    .spinner {
      width: 20px; height: 20px;
      border: 2px solid #e5e7eb;
      border-top-color: #3b82f6;
      border-radius: 50%;
      animation: spin 0.6s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }

    .table-container {
      background: #fff;
      border: 1px solid #e5e7eb;
      border-radius: 8px;
      overflow-x: auto;
    }
    table { width: 100%; border-collapse: collapse; font-size: 13px; }
    thead { background: #f8fafc; }
    th {
      padding: 10px 12px;
      text-align: left;
      font-weight: 600;
      color: #475569;
      border-bottom: 1px solid #e5e7eb;
      white-space: nowrap;
    }
    td {
      padding: 8px 12px;
      border-bottom: 1px solid #f1f5f9;
      color: #334155;
    }
    tr:hover td { background: #f8fafc; }
    .mono { font-family: 'SF Mono', Monaco, monospace; font-size: 12px; }
    .cmd { color: #059669; font-weight: 600; }

    .status-badge {
      display: inline-block;
      padding: 2px 8px;
      border-radius: 4px;
      font-size: 11px;
      font-weight: 600;
      text-transform: uppercase;
    }
    .status-badge.sent { background: #ecfdf5; color: #059669; }
    .status-badge.pending { background: #fef3c7; color: #d97706; }
    .status-badge.failed { background: #fef2f2; color: #dc2626; }

    .type-badge {
      display: inline-block;
      padding: 2px 8px;
      border-radius: 4px;
      font-size: 11px;
      font-weight: 700;
      font-family: 'SF Mono', Monaco, monospace;
    }
    .type-badge.GO_67 { background: #dbeafe; color: #1d4ed8; }
    .type-badge.GO_C3 { background: #e0e7ff; color: #4338ca; }

    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 12px;
      padding: 60px 20px;
      color: #94a3b8;
    }
    .empty-state p { font-size: 14px; }
  `]
})
export class AdminAutoRecoveryComponent implements OnInit {
  records: any[] = [];
  companies: any[] = [];
  loading = false;
  selectedCompanyId: number | null = null;
  limit = 100;

  get uniqueDevices(): number {
    return new Set(this.records.map(r => r.deviceId)).size;
  }

  get lastDetection(): Date | null {
    return this.records.length > 0 ? new Date(this.records[0].createdAt) : null;
  }

  constructor(private adminService: AdminService) {}

  ngOnInit() {
    this.loadCompanies();
    this.load();
  }

  loadCompanies() {
    this.adminService.getCompanies().subscribe({
      next: (data: any[]) => this.companies = data || [],
      error: () => {}
    });
  }

  load() {
    this.loading = true;
    this.adminService.getAutoRecoveryLog(this.limit, this.selectedCompanyId).subscribe({
      next: (data: any[]) => {
        this.records = data || [];
        this.loading = false;
      },
      error: () => {
        this.records = [];
        this.loading = false;
      }
    });
  }

  getCommandLabel(flagsHex: string): string {
    if (!flagsHex) return '-';
    return flagsHex.toUpperCase();
  }

  getCommandClass(flagsHex: string): string {
    if (!flagsHex) return '';
    const upper = flagsHex.toUpperCase();
    if (upper === 'C3') return 'GO_C3';
    if (upper === '67') return 'GO_67';
    return '';
  }
}
