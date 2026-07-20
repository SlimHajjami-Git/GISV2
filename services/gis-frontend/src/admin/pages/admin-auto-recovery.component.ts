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
            <h2><span class="title-dash"></span>Détections auto-recovery</h2>
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
    .page-header h2 {
      margin: 0;
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 12px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: .08em;
      color: var(--adm-sub);
    }
    .title-dash {
      display: inline-block;
      width: 12px;
      height: 3px;
      border-radius: 2px;
      background: var(--adm-indigo);
    }
    .subtitle { margin: 6px 0 0; font-size: 13px; color: var(--adm-sub); }
    .header-actions { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
    .header-actions select {
      padding: 8px 12px;
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      font-size: 13px;
      color: var(--adm-ink);
      background: var(--adm-card);
      outline: none;
      transition: border-color 0.2s, box-shadow 0.2s;
    }
    .header-actions select:focus {
      border-color: var(--adm-indigo);
      box-shadow: 0 0 0 3px rgba(79, 70, 229, 0.12);
    }

    .btn-refresh {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 9px 18px;
      background: var(--adm-indigo);
      color: #fff;
      border: none;
      border-radius: 10px;
      font-size: 13px;
      font-weight: 600;
      cursor: pointer;
      transition: background 0.2s;
    }
    .btn-refresh:hover { background: var(--adm-indigo-ink); }

    .stats-bar {
      display: flex;
      gap: 16px;
      margin-bottom: 20px;
    }
    .stat-card {
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-left: 3px solid var(--adm-indigo);
      border-radius: 16px;
      box-shadow: var(--adm-shadow);
      padding: 14px 20px;
      display: flex;
      flex-direction: column;
      gap: 2px;
      animation: rise .35s ease backwards;
    }
    .stat-card:nth-child(2) { border-left-color: var(--adm-cyan); }
    .stat-card:nth-child(3) { border-left-color: var(--adm-slate); }
    .stat-value {
      font-size: 24px;
      font-weight: 800;
      font-variant-numeric: tabular-nums;
      color: var(--adm-ink);
    }
    .stat-label {
      font-size: 11px;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: .06em;
      color: var(--adm-sub);
    }

    .loading-state {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 40px;
      justify-content: center;
      color: var(--adm-sub);
    }
    .spinner {
      width: 20px; height: 20px;
      border: 2px solid var(--adm-track);
      border-top-color: var(--adm-indigo);
      border-radius: 50%;
      animation: spin 0.6s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }

    .table-container {
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 16px;
      box-shadow: var(--adm-shadow);
      overflow-x: auto;
      animation: rise .35s ease backwards;
    }
    table { width: 100%; border-collapse: collapse; font-size: 13px; }
    thead { background: #f8fafc; }
    th {
      padding: 10px 12px;
      text-align: left;
      font-size: 11px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: .06em;
      color: var(--adm-sub);
      border-bottom: 1px solid var(--adm-border);
      white-space: nowrap;
    }
    td {
      padding: 8px 12px;
      font-size: 13px;
      border-bottom: 1px solid #eef2f7;
      color: var(--adm-ink);
    }
    tbody tr:hover td { background: #f8fafc; }
    .mono {
      font-family: 'SF Mono', Monaco, monospace;
      font-size: 12px;
      font-variant-numeric: tabular-nums;
    }
    .cmd { color: var(--adm-green-ink); font-weight: 600; }

    .status-badge {
      display: inline-block;
      padding: 3px 10px;
      border-radius: 999px;
      font-size: 11px;
      font-weight: 700;
      text-transform: uppercase;
    }
    .status-badge.sent { background: rgba(5, 150, 105, 0.10); color: var(--adm-green-ink); }
    .status-badge.pending { background: rgba(217, 119, 6, 0.12); color: var(--adm-amber-ink); }
    .status-badge.failed { background: rgba(220, 38, 38, 0.10); color: var(--adm-red-ink); }

    .type-badge {
      display: inline-block;
      padding: 3px 10px;
      border-radius: 999px;
      font-size: 11px;
      font-weight: 700;
      font-family: 'SF Mono', Monaco, monospace;
    }
    .type-badge.GO_67 { background: rgba(8, 145, 178, 0.12); color: var(--adm-cyan-ink); }
    .type-badge.GO_C3 { background: rgba(79, 70, 229, 0.12); color: var(--adm-indigo-ink); }

    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 12px;
      padding: 60px 20px;
      color: var(--adm-sub);
    }
    .empty-state p { font-size: 14px; }

    @keyframes rise {
      from { opacity: 0; transform: translateY(8px); }
      to { opacity: 1; transform: none; }
    }

    @media (prefers-reduced-motion: reduce) {
      .stat-card, .table-container { animation: none; }
    }
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
