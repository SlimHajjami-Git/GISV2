import { Component, OnInit, ChangeDetectorRef, NgZone } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService, DeviceEventDto, DeviceEventsResult } from '../services/api.service';
import { AppLayoutComponent } from './shared/app-layout.component';

@Component({
  selector: 'app-securite',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent],
  template: `
    <app-layout>
      <div class="securite-page">
        <!-- Header -->
        <div class="sec-header">
          <div class="header-left">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M13 2L3 14h9l-1 8 10-12h-9l1-8z"/>
            </svg>
            <h1>Securite</h1>
            <span class="event-count" *ngIf="totalCount > 0">{{ totalCount }}</span>
          </div>
        </div>

        <!-- Filters -->
        <div class="sec-filters">
          <button class="filter-btn" [class.active]="filterType === ''" (click)="filterType = ''; load()">Tous</button>
          <button class="filter-btn" [class.active]="filterType === 'restart'" (click)="filterType = 'restart'; load()">
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M23 4v6h-6"/><path d="M1 20v-6h6"/><path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"/></svg>
            Redemarrage
          </button>
          <button class="filter-btn" [class.active]="filterType === 'tamper_suspected'" (click)="filterType = 'tamper_suspected'; load()">
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
            Sabotage suspect
          </button>

          <select class="filter-select" [(ngModel)]="filterVehicleId" (change)="load()">
            <option [ngValue]="null">Tous les vehicules</option>
            @for (v of vehicles; track v.id) {
              <option [ngValue]="v.id">{{ v.name || v.plate }}</option>
            }
          </select>

          <div class="filter-dates">
            <input type="date" [(ngModel)]="filterFrom" (change)="load()">
            <span>-</span>
            <input type="date" [(ngModel)]="filterTo" (change)="load()">
          </div>

          <label class="toggle-small">
            <input type="checkbox" [(ngModel)]="showAcknowledged" (change)="load()">
            <span>Acquittes</span>
          </label>
        </div>

        <!-- Event List -->
        <div class="sec-list">
          @for (evt of events; track evt.id) {
            <div class="event-card" [class.acknowledged]="evt.acknowledged">
              <div class="event-icon" [class]="getEventClass(evt.eventType)">
                <svg *ngIf="evt.eventType === 'restart'" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M23 4v6h-6"/><path d="M1 20v-6h6"/><path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"/></svg>
                <svg *ngIf="evt.eventType === 'tamper_suspected'" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
                <svg *ngIf="evt.eventType !== 'restart' && evt.eventType !== 'tamper_suspected'" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M13 2L3 14h9l-1 8 10-12h-9l1-8z"/></svg>
              </div>
              <div class="event-content">
                <div class="event-title">
                  {{ evt.vehicleName || ('Device #' + evt.deviceId) }}
                  <span class="event-type-badge" [class]="evt.eventType">{{ getEventLabel(evt.eventType) }}</span>
                </div>
                <div class="event-details">
                  <span *ngIf="evt.offlineDurationSecs">Hors ligne : {{ formatDuration(evt.offlineDurationSecs) }}</span>
                  <span class="sep" *ngIf="evt.offlineDurationSecs">|</span>
                  <span>{{ formatDate(evt.eventAt) }}</span>
                </div>
                <div class="event-location" *ngIf="evt.lastKnownLat && evt.lastKnownLon">
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z"/><circle cx="12" cy="10" r="3"/></svg>
                  {{ evt.lastKnownLat.toFixed(5) }}, {{ evt.lastKnownLon.toFixed(5) }}
                </div>
              </div>
              <div class="event-actions">
                <button class="btn-ack" *ngIf="!evt.acknowledged" (click)="acknowledge(evt)">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="20 6 9 17 4 12"/></svg>
                  Acquitter
                </button>
                <span class="ack-badge" *ngIf="evt.acknowledged">
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="20 6 9 17 4 12"/></svg>
                  Acquitte
                </span>
              </div>
            </div>
          }

          @if (events.length === 0 && !loading) {
            <div class="empty-state">
              <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>
              </svg>
              <p>Aucun evenement de securite</p>
              <span>Les coupures d'alimentation et redemarrages apparaitront ici</span>
            </div>
          }

          @if (loading) {
            <div class="loading-state">
              <div class="spinner"></div>
              <span>Chargement...</span>
            </div>
          }

          <!-- Pagination -->
          <div class="pagination" *ngIf="totalCount > pageSize">
            <button class="page-btn" [disabled]="page <= 1" (click)="page = page - 1; load()">Precedent</button>
            <span class="page-info">{{ page }} / {{ totalPages }}</span>
            <button class="page-btn" [disabled]="page >= totalPages" (click)="page = page + 1; load()">Suivant</button>
          </div>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    .securite-page {
      flex: 1;
      background: #f1f5f9;
      display: flex;
      flex-direction: column;
      min-height: calc(100vh - 42px);
    }

    .sec-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 14px 20px;
      background: white;
      border-bottom: 1px solid #e2e8f0;
    }

    .header-left {
      display: flex;
      align-items: center;
      gap: 10px;
    }
    .header-left svg { color: #f59e0b; }
    .header-left h1 { margin: 0; font-size: 16px; font-weight: 600; color: #1e293b; }

    .event-count {
      background: #f59e0b;
      color: white;
      font-size: 11px;
      font-weight: 700;
      padding: 2px 7px;
      border-radius: 10px;
      min-width: 18px;
      text-align: center;
    }

    .sec-filters {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 10px 20px;
      background: white;
      border-bottom: 1px solid #e2e8f0;
      flex-wrap: wrap;
    }

    .filter-btn {
      display: flex;
      align-items: center;
      gap: 4px;
      padding: 6px 12px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      font-size: 11px;
      color: #64748b;
      cursor: pointer;
      font-weight: 500;
    }
    .filter-btn:hover { background: #f8fafc; }
    .filter-btn.active { background: #fef3c7; border-color: #f59e0b; color: #92400e; }

    .filter-select {
      padding: 6px 10px;
      border: 1px solid #e2e8f0;
      border-radius: 6px;
      font-size: 11px;
      color: #475569;
      background: white;
    }

    .filter-dates {
      display: flex;
      align-items: center;
      gap: 4px;
    }
    .filter-dates input {
      padding: 5px 8px;
      border: 1px solid #e2e8f0;
      border-radius: 6px;
      font-size: 11px;
      color: #475569;
    }
    .filter-dates span { color: #94a3b8; font-size: 11px; }

    .toggle-small {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 11px;
      color: #64748b;
      cursor: pointer;
      margin-left: auto;
    }
    .toggle-small input { width: 14px; height: 14px; }

    .sec-list {
      flex: 1;
      overflow-y: auto;
      padding: 8px 20px;
    }

    .event-card {
      display: flex;
      align-items: flex-start;
      gap: 12px;
      padding: 14px 16px;
      background: white;
      border-radius: 8px;
      margin-bottom: 6px;
      border: 1px solid #e2e8f0;
      transition: all 0.15s;
    }
    .event-card:hover { box-shadow: 0 2px 6px rgba(0,0,0,0.06); }
    .event-card.acknowledged { opacity: 0.6; }

    .event-icon {
      width: 40px;
      height: 40px;
      border-radius: 10px;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }
    .event-icon.restart { background: #fef3c7; color: #d97706; }
    .event-icon.tamper_suspected { background: #fee2e2; color: #dc2626; }
    .event-icon.gsm_reset { background: #e0e7ff; color: #4f46e5; }
    .event-icon.disconnect { background: #f1f5f9; color: #64748b; }

    .event-content { flex: 1; min-width: 0; }

    .event-title {
      font-size: 13px;
      font-weight: 600;
      color: #1e293b;
      display: flex;
      align-items: center;
      gap: 8px;
      margin-bottom: 4px;
    }

    .event-type-badge {
      font-size: 10px;
      padding: 2px 6px;
      border-radius: 4px;
      font-weight: 500;
    }
    .event-type-badge.restart { background: #fef3c7; color: #92400e; }
    .event-type-badge.tamper_suspected { background: #fee2e2; color: #991b1b; }
    .event-type-badge.gsm_reset { background: #e0e7ff; color: #3730a3; }
    .event-type-badge.disconnect { background: #f1f5f9; color: #475569; }

    .event-details {
      font-size: 12px;
      color: #64748b;
      margin-bottom: 4px;
      display: flex;
      align-items: center;
      gap: 6px;
    }
    .sep { color: #cbd5e1; }

    .event-location {
      font-size: 11px;
      color: #94a3b8;
      display: flex;
      align-items: center;
      gap: 4px;
    }

    .event-actions { flex-shrink: 0; }

    .btn-ack {
      display: flex;
      align-items: center;
      gap: 4px;
      padding: 6px 12px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 6px;
      font-size: 11px;
      color: #059669;
      cursor: pointer;
      font-weight: 500;
    }
    .btn-ack:hover { background: #ecfdf5; border-color: #6ee7b7; }

    .ack-badge {
      display: flex;
      align-items: center;
      gap: 4px;
      font-size: 11px;
      color: #94a3b8;
    }

    .empty-state {
      text-align: center;
      padding: 60px 20px;
      color: #94a3b8;
    }
    .empty-state svg { margin-bottom: 12px; opacity: 0.4; }
    .empty-state p { margin: 0 0 4px; font-weight: 500; font-size: 14px; color: #64748b; }
    .empty-state span { font-size: 12px; }

    .loading-state {
      text-align: center;
      padding: 40px;
      color: #94a3b8;
      font-size: 12px;
    }

    .spinner {
      width: 24px;
      height: 24px;
      border: 2px solid #e2e8f0;
      border-top-color: #3b82f6;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
      margin: 0 auto 8px;
    }
    @keyframes spin { to { transform: rotate(360deg); } }

    .pagination {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 12px;
      padding: 16px 0;
    }

    .page-btn {
      padding: 6px 14px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 6px;
      font-size: 12px;
      color: #3b82f6;
      cursor: pointer;
      font-weight: 500;
    }
    .page-btn:hover:not(:disabled) { background: #eff6ff; }
    .page-btn:disabled { opacity: 0.4; cursor: default; }

    .page-info { font-size: 12px; color: #64748b; }
  `]
})
export class SecuriteComponent implements OnInit {
  events: DeviceEventDto[] = [];
  vehicles: { id: number; name?: string; plate: string }[] = [];
  totalCount = 0;
  page = 1;
  pageSize = 20;
  loading = false;

  filterType = '';
  filterVehicleId: number | null = null;
  filterFrom = '';
  filterTo = '';
  showAcknowledged = false;

  get totalPages(): number {
    return Math.ceil(this.totalCount / this.pageSize) || 1;
  }

  constructor(private api: ApiService, private cdr: ChangeDetectorRef, private zone: NgZone) {}

  ngOnInit() {
    this.loadVehicles();
    this.load();
  }

  loadVehicles() {
    this.api.getVehicles().subscribe({
      next: (v: any[]) => this.vehicles = v.map(x => ({ id: x.id, name: x.name, plate: x.plate })),
      error: () => {}
    });
  }

  load() {
    this.loading = true;
    this.cdr.detectChanges();
    this.api.getDeviceEvents({
      eventType: this.filterType || undefined,
      vehicleId: this.filterVehicleId ?? undefined,
      from: this.filterFrom || undefined,
      to: this.filterTo || undefined,
      acknowledged: this.showAcknowledged ? undefined : false,
      page: this.page,
      pageSize: this.pageSize
    }).subscribe({
      next: (res) => {
        this.zone.run(() => {
          this.events = res.items;
          this.totalCount = res.totalCount;
          this.loading = false;
          this.cdr.detectChanges();
        });
      },
      error: () => {
        this.zone.run(() => {
          this.loading = false;
          this.cdr.detectChanges();
        });
      }
    });
  }

  acknowledge(evt: DeviceEventDto) {
    this.api.acknowledgeDeviceEvent(evt.id).subscribe({
      next: () => {
        evt.acknowledged = true;
      }
    });
  }

  getEventClass(type: string): string {
    return type || 'restart';
  }

  getEventLabel(type: string): string {
    const labels: Record<string, string> = {
      restart: 'Redemarrage',
      tamper_suspected: 'Sabotage suspect',
      gsm_reset: 'Reset GSM',
      disconnect: 'Deconnexion'
    };
    return labels[type] || type;
  }

  formatDuration(secs: number): string {
    const h = Math.floor(secs / 3600);
    const m = Math.floor((secs % 3600) / 60);
    return m > 0 ? `${h}h ${m}min` : `${h}h`;
  }

  formatDate(dateStr: string): string {
    const d = new Date(dateStr);
    return d.toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
  }
}
