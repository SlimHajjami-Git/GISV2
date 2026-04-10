import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService } from '../services/admin.service';

@Component({
  selector: 'admin-commands',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent],
  template: `
    <admin-layout pageTitle="Commandes GPS">
      <div class="commands-page">
        <div class="page-header">
          <div>
            <h2>Commandes GPS</h2>
            <p class="subtitle">Envoyer des commandes directement aux boîtiers GPS sélectionnés</p>
          </div>
          <div class="header-actions">
            <select [(ngModel)]="selectedCompanyId" (ngModelChange)="loadDashboard()">
              <option [ngValue]="null">Toutes les sociétés</option>
              <option *ngFor="let c of companies" [ngValue]="c.id">{{ c.name }}</option>
            </select>
            <button class="btn-refresh" (click)="loadDashboard(); loadHistory()">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 12a9 9 0 1 1-9-9c2.52 0 4.93 1 6.74 2.74L21 8"/><path d="M21 3v5h-5"/></svg>
              Actualiser
            </button>
          </div>
        </div>

        <!-- Command input -->
        <div class="command-bar">
          <div class="command-input-group">
            <label>Commande :</label>
            <input type="text" [(ngModel)]="commandText" placeholder="Ex: AJ+GO#1311"
                   (keydown.enter)="sendCommand()" class="command-input" />
          </div>
          <button class="btn-send" [disabled]="selectedDeviceIds.size === 0 || !commandText.trim() || sending"
                  (click)="sendCommand()">
            <svg *ngIf="!sending" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polygon points="5,3 19,12 5,21 5,3"/></svg>
            <div *ngIf="sending" class="spinner-sm"></div>
            {{ sending ? 'Envoi...' : 'Envoyer' }} ({{ selectedDeviceIds.size }})
          </button>
        </div>

        <!-- Send result -->
        <div class="result-banner success" *ngIf="lastResult && !lastError">
          {{ lastResult }}
          <span *ngIf="autoRefreshCountdown > 0" class="refresh-info">Actualisation auto dans {{ autoRefreshCountdown }}s</span>
        </div>
        <div class="result-banner error" *ngIf="lastError">{{ lastError }}</div>

        <!-- Stats -->
        <div class="stats-bar" *ngIf="!loading && vehicles.length > 0">
          <div class="stat-card">
            <span class="stat-value">{{ selectedDeviceIds.size }}</span>
            <span class="stat-label">Sélectionnés</span>
          </div>
          <div class="stat-card">
            <span class="stat-value">{{ onlineCount }}</span>
            <span class="stat-label">En ligne</span>
          </div>
          <div class="stat-card">
            <span class="stat-value">{{ immobilizedCount }}</span>
            <span class="stat-label">Immobilisés</span>
          </div>
          <div class="stat-card">
            <span class="stat-value">{{ vehicles.length }}</span>
            <span class="stat-label">Total GPS</span>
          </div>
        </div>

        <!-- Loading -->
        <div class="loading-state" *ngIf="loading">
          <div class="spinner"></div>
          <span>Chargement...</span>
        </div>

        <!-- Vehicle table -->
        <div class="table-container" *ngIf="!loading && vehicles.length > 0">
          <table>
            <thead>
              <tr>
                <th class="check-col">
                  <input type="checkbox" [checked]="allSelected" (change)="toggleSelectAll()" />
                </th>
                <th>Véhicule</th>
                <th>Plaque</th>
                <th>IMEI</th>
                <th>MAT</th>
                <th>Société</th>
                <th>Statut</th>
                <th>Immobilisation</th>
                <th>Dernière commande</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let v of vehicles" [class.selected]="selectedDeviceIds.has(v.deviceId)"
                  (click)="toggleSelect(v.deviceId)">
                <td class="check-col">
                  <input type="checkbox" [checked]="selectedDeviceIds.has(v.deviceId)"
                         (click)="$event.stopPropagation()" (change)="toggleSelect(v.deviceId)" />
                </td>
                <td>{{ v.vehicleName }}</td>
                <td>{{ v.plate || '-' }}</td>
                <td class="mono">{{ v.imei || '-' }}</td>
                <td class="mono">{{ v.mat || '-' }}</td>
                <td>{{ v.companyName || '-' }}</td>
                <td>
                  <span class="online-badge" [class.online]="v.isOnline">
                    <span class="dot"></span>
                    {{ v.isOnline ? 'En ligne' : 'Hors ligne' }}
                  </span>
                </td>
                <td>
                  <span class="immo-badge" [class.active]="v.immobilizationActive">
                    {{ v.immobilizationActive ? 'ARRÊTÉ' : 'LIBRE' }}
                  </span>
                </td>
                <td>
                  <div *ngIf="v.lastCommand" class="last-cmd">
                    <span class="status-badge" [ngClass]="v.lastCommand.status">{{ v.lastCommand.status }}</span>
                    <span class="cmd-text mono">{{ v.lastCommand.commandText?.trim() }}</span>
                    <span class="cmd-time">{{ v.lastCommand.createdAt | date:'dd/MM HH:mm' }}</span>
                  </div>
                  <span *ngIf="!v.lastCommand" class="no-cmd">-</span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <div class="empty-state" *ngIf="!loading && vehicles.length === 0">
          <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="#94a3b8" stroke-width="1.5">
            <rect x="2" y="7" width="20" height="14" rx="2"/><path d="M16 3l-4 4-4-4"/>
          </svg>
          <p>Aucun véhicule avec GPS trouvé</p>
        </div>

        <!-- Command history -->
        <div class="history-section" *ngIf="commandHistory.length > 0">
          <h3>Historique des commandes</h3>
          <div class="table-container">
            <table>
              <thead>
                <tr>
                  <th>#</th>
                  <th>Date</th>
                  <th>Véhicule</th>
                  <th>IMEI</th>
                  <th>Commande</th>
                  <th>Statut</th>
                  <th>Envoyée</th>
                  <th>Source</th>
                  <th>Par</th>
                </tr>
              </thead>
              <tbody>
                <tr *ngFor="let h of commandHistory">
                  <td class="mono">{{ h.id }}</td>
                  <td>{{ h.createdAt | date:'dd/MM/yyyy HH:mm' }}</td>
                  <td>{{ h.vehicleName || '-' }}</td>
                  <td class="mono">{{ h.deviceUid || '-' }}</td>
                  <td class="mono cmd">{{ h.commandText?.trim() }}</td>
                  <td><span class="status-badge" [ngClass]="h.status">{{ h.status }}</span></td>
                  <td>{{ h.sentAt ? (h.sentAt | date:'HH:mm:ss') : '-' }}</td>
                  <td><span class="source-badge" [ngClass]="h.source">{{ h.source }}</span></td>
                  <td>{{ h.userName }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </admin-layout>
  `,
  styles: [`
    .commands-page { padding: 24px; }

    .page-header {
      display: flex; justify-content: space-between; align-items: flex-start;
      margin-bottom: 20px; gap: 16px; flex-wrap: wrap;
    }
    .page-header h2 { margin: 0; font-size: 20px; color: #1e293b; }
    .subtitle { margin: 4px 0 0; font-size: 13px; color: #64748b; }
    .header-actions { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
    .header-actions select {
      padding: 6px 10px; border: 1px solid #d1d5db; border-radius: 6px;
      font-size: 13px; background: #fff;
    }
    .btn-refresh {
      display: flex; align-items: center; gap: 6px; padding: 7px 14px;
      background: #3b82f6; color: #fff; border: none; border-radius: 6px;
      font-size: 13px; font-weight: 500; cursor: pointer;
    }
    .btn-refresh:hover { background: #2563eb; }

    /* Command bar */
    .command-bar {
      display: flex; gap: 12px; align-items: flex-end;
      margin-bottom: 16px; padding: 16px; background: #fff;
      border: 1px solid #e5e7eb; border-radius: 8px;
    }
    .command-input-group { flex: 1; display: flex; flex-direction: column; gap: 4px; }
    .command-input-group label { font-size: 12px; font-weight: 600; color: #475569; }
    .command-input {
      padding: 8px 12px; border: 1px solid #d1d5db; border-radius: 6px;
      font-size: 14px; font-family: 'SF Mono', Monaco, monospace;
    }
    .command-input:focus { outline: none; border-color: #3b82f6; box-shadow: 0 0 0 2px rgba(59,130,246,0.15); }
    .btn-send {
      display: flex; align-items: center; gap: 6px; padding: 8px 20px;
      background: #059669; color: #fff; border: none; border-radius: 6px;
      font-size: 14px; font-weight: 600; cursor: pointer; white-space: nowrap;
    }
    .btn-send:hover:not(:disabled) { background: #047857; }
    .btn-send:disabled { background: #94a3b8; cursor: not-allowed; }

    /* Result banner */
    .result-banner {
      padding: 10px 16px; border-radius: 6px; font-size: 13px;
      font-weight: 500; margin-bottom: 16px; display: flex;
      justify-content: space-between; align-items: center;
    }
    .result-banner.success { background: #ecfdf5; color: #059669; border: 1px solid #a7f3d0; }
    .result-banner.error { background: #fef2f2; color: #dc2626; border: 1px solid #fecaca; }
    .refresh-info { font-size: 11px; opacity: 0.7; }

    /* Stats */
    .stats-bar { display: flex; gap: 16px; margin-bottom: 20px; flex-wrap: wrap; }
    .stat-card {
      background: #fff; border: 1px solid #e5e7eb; border-radius: 8px;
      padding: 12px 20px; display: flex; flex-direction: column; gap: 2px;
    }
    .stat-value { font-size: 20px; font-weight: 700; color: #1e293b; }
    .stat-label { font-size: 12px; color: #64748b; }

    /* Loading */
    .loading-state {
      display: flex; align-items: center; gap: 10px; padding: 40px;
      justify-content: center; color: #64748b;
    }
    .spinner {
      width: 20px; height: 20px; border: 2px solid #e5e7eb;
      border-top-color: #3b82f6; border-radius: 50%;
      animation: spin 0.6s linear infinite;
    }
    .spinner-sm {
      width: 14px; height: 14px; border: 2px solid rgba(255,255,255,0.3);
      border-top-color: #fff; border-radius: 50%;
      animation: spin 0.6s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }

    /* Table */
    .table-container {
      background: #fff; border: 1px solid #e5e7eb; border-radius: 8px; overflow-x: auto;
    }
    table { width: 100%; border-collapse: collapse; font-size: 13px; }
    thead { background: #f8fafc; }
    th {
      padding: 10px 12px; text-align: left; font-weight: 600;
      color: #475569; border-bottom: 1px solid #e5e7eb; white-space: nowrap;
    }
    td { padding: 8px 12px; border-bottom: 1px solid #f1f5f9; color: #334155; }
    tr:hover td { background: #f8fafc; }
    tr.selected td { background: #eff6ff; }
    .check-col { width: 40px; text-align: center; }
    .mono { font-family: 'SF Mono', Monaco, monospace; font-size: 12px; }
    .cmd { color: #059669; font-weight: 600; }

    /* Badges */
    .status-badge {
      display: inline-block; padding: 2px 8px; border-radius: 4px;
      font-size: 11px; font-weight: 600; text-transform: uppercase;
    }
    .status-badge.sent { background: #ecfdf5; color: #059669; }
    .status-badge.pending { background: #fef3c7; color: #d97706; }
    .status-badge.failed { background: #fef2f2; color: #dc2626; }
    .status-badge.acknowledged { background: #dbeafe; color: #1d4ed8; }
    .status-badge.expired { background: #f1f5f9; color: #64748b; }

    .source-badge {
      display: inline-block; padding: 2px 8px; border-radius: 4px;
      font-size: 11px; font-weight: 500;
    }
    .source-badge.manual { background: #dbeafe; color: #1d4ed8; }
    .source-badge.auto_recovery { background: #fef3c7; color: #d97706; }

    .online-badge {
      display: inline-flex; align-items: center; gap: 5px; font-size: 12px; color: #94a3b8;
    }
    .online-badge.online { color: #059669; }
    .dot {
      width: 7px; height: 7px; border-radius: 50%; background: #d1d5db;
    }
    .online-badge.online .dot { background: #10b981; }

    .immo-badge {
      display: inline-block; padding: 2px 8px; border-radius: 4px;
      font-size: 11px; font-weight: 700;
      background: #ecfdf5; color: #059669;
    }
    .immo-badge.active { background: #fef2f2; color: #dc2626; }

    .last-cmd { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; }
    .cmd-text { font-size: 11px; max-width: 120px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .cmd-time { font-size: 11px; color: #94a3b8; }
    .no-cmd { color: #cbd5e1; }

    /* History */
    .history-section { margin-top: 32px; }
    .history-section h3 { font-size: 16px; color: #1e293b; margin: 0 0 12px; }

    /* Empty state */
    .empty-state {
      display: flex; flex-direction: column; align-items: center;
      gap: 12px; padding: 60px 20px; color: #94a3b8;
    }
    .empty-state p { font-size: 14px; }

    input[type="checkbox"] { cursor: pointer; width: 16px; height: 16px; }
    tr { cursor: pointer; }
  `]
})
export class AdminCommandsComponent implements OnInit, OnDestroy {
  vehicles: any[] = [];
  companies: any[] = [];
  commandHistory: any[] = [];
  selectedDeviceIds = new Set<number>();
  selectedCompanyId: number | null = null;
  commandText = '';
  loading = false;
  sending = false;
  lastResult = '';
  lastError = '';
  autoRefreshInterval: any = null;
  autoRefreshCountdown = 0;

  get onlineCount(): number {
    return this.vehicles.filter(v => v.isOnline).length;
  }

  get immobilizedCount(): number {
    return this.vehicles.filter(v => v.immobilizationActive).length;
  }

  get allSelected(): boolean {
    return this.vehicles.length > 0 && this.selectedDeviceIds.size === this.vehicles.length;
  }

  constructor(private adminService: AdminService, private cdr: ChangeDetectorRef) {}

  ngOnInit() {
    this.loadCompanies();
    this.loadDashboard();
    this.loadHistory();
  }

  ngOnDestroy() {
    this.clearAutoRefresh();
  }

  loadCompanies() {
    this.adminService.getCompanies().subscribe({
      next: (data: any[]) => this.companies = data || [],
      error: () => {}
    });
  }

  loadDashboard() {
    this.loading = true;
    this.adminService.getCommandDashboard(this.selectedCompanyId ?? undefined).subscribe({
      next: (data: any[]) => {
        this.vehicles = data || [];
        // Remove selections for devices no longer visible
        const visibleIds = new Set(this.vehicles.map(v => v.deviceId));
        for (const id of this.selectedDeviceIds) {
          if (!visibleIds.has(id)) this.selectedDeviceIds.delete(id);
        }
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.vehicles = [];
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  loadHistory() {
    this.adminService.getCommandHistory(100, this.selectedCompanyId).subscribe({
      next: (data: any[]) => {
        this.commandHistory = data || [];
        this.cdr.detectChanges();
      },
      error: () => {}
    });
  }

  toggleSelect(deviceId: number) {
    if (this.selectedDeviceIds.has(deviceId)) {
      this.selectedDeviceIds.delete(deviceId);
    } else {
      this.selectedDeviceIds.add(deviceId);
    }
  }

  toggleSelectAll() {
    if (this.allSelected) {
      this.selectedDeviceIds.clear();
    } else {
      this.vehicles.forEach(v => this.selectedDeviceIds.add(v.deviceId));
    }
  }

  sendCommand() {
    const text = this.commandText.trim();
    if (!text || this.selectedDeviceIds.size === 0 || this.sending) return;

    if (text.toUpperCase().includes('STOP')) {
      this.lastError = 'Les commandes STOP sont interdites';
      this.lastResult = '';
      return;
    }

    const count = this.selectedDeviceIds.size;
    if (!confirm(`Envoyer "${text}" à ${count} boîtier(s) ?`)) return;

    this.sending = true;
    this.lastResult = '';
    this.lastError = '';

    this.adminService.sendBulkCommand(Array.from(this.selectedDeviceIds), text).subscribe({
      next: (res: any) => {
        this.sending = false;
        this.lastResult = res.message || `${count} commande(s) envoyée(s)`;
        this.startAutoRefresh();
        this.loadHistory();
        this.cdr.detectChanges();
      },
      error: (err: any) => {
        this.sending = false;
        this.lastError = err.error?.message || 'Erreur lors de l\'envoi';
        this.cdr.detectChanges();
      }
    });
  }

  startAutoRefresh() {
    this.clearAutoRefresh();
    this.autoRefreshCountdown = 60;
    this.autoRefreshInterval = setInterval(() => {
      this.autoRefreshCountdown -= 5;
      if (this.autoRefreshCountdown <= 0) {
        this.clearAutoRefresh();
      } else {
        this.loadDashboard();
        this.loadHistory();
      }
      this.cdr.detectChanges();
    }, 5000);
  }

  clearAutoRefresh() {
    if (this.autoRefreshInterval) {
      clearInterval(this.autoRefreshInterval);
      this.autoRefreshInterval = null;
    }
    this.autoRefreshCountdown = 0;
  }
}
