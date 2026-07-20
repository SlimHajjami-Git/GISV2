import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, takeUntil, forkJoin } from 'rxjs';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService } from '../services/admin.service';

@Component({
  selector: 'admin-trames',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent],
  template: `
    <admin-layout pageTitle="Trames GPS">
      <div class="trames-page">
        <!-- Filters Bar -->
        <div class="filters-bar">
          <div class="filter-group">
            <label>Véhicule</label>
            <select [(ngModel)]="selectedVehicleId" (ngModelChange)="onVehicleChange()">
              <option [ngValue]="null">-- Sélectionner un véhicule --</option>
              <option *ngFor="let v of vehicles" [ngValue]="v.id">{{ v.plate || v.name }} {{ v.plate && v.name ? '(' + v.name + ')' : '' }}</option>
            </select>
          </div>
          <div class="filter-group">
            <label>Du</label>
            <input type="datetime-local" [(ngModel)]="fromDate">
          </div>
          <div class="filter-group">
            <label>Au</label>
            <input type="datetime-local" [(ngModel)]="toDate">
          </div>
          <div class="filter-group">
            <label>Max points</label>
            <select [(ngModel)]="maxPoints">
              <option [ngValue]="500">500</option>
              <option [ngValue]="1000">1 000</option>
              <option [ngValue]="3000">3 000</option>
              <option [ngValue]="5000">5 000</option>
              <option [ngValue]="10000">10 000</option>
              <option [ngValue]="50000">50 000 (brut)</option>
            </select>
          </div>
          <button class="btn-load" (click)="loadTrames()" [disabled]="!selectedVehicleId || loading">
            <svg *ngIf="!loading" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="1 4 1 10 7 10"/><path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10"/></svg>
            <span class="spinner" *ngIf="loading"></span>
            {{ loading ? 'Chargement...' : 'Charger' }}
          </button>
        </div>

        <!-- Stats -->
        <div class="stats-bar" *ngIf="trames.length > 0">
          <div class="stat"><strong>{{ trames.length }}</strong> trames</div>
          <div class="stat">Vitesse max: <strong>{{ statsMaxSpeed | number:'1.0-0' }} km/h</strong></div>
          <div class="stat">RPM max: <strong>{{ statsMaxRpm || '-' }}</strong></div>
          <div class="stat">Carburant: <strong>{{ statsFuelMin }}% → {{ statsFuelMax }}%</strong></div>
          <div class="stat">
            <input type="text" class="search-input" placeholder="Rechercher dans les trames..." [(ngModel)]="searchQuery" (ngModelChange)="filterTrames()">
          </div>
        </div>

        <!-- Table -->
        <div class="table-container" *ngIf="filteredTrames.length > 0">
          <table class="trames-table">
            <thead>
              <tr>
                <th class="sortable" (click)="sortBy('id')"># <span class="sort-icon">{{ getSortIcon('id') }}</span></th>
                <th class="sortable" (click)="sortBy('recordedAt')">Date/Heure <span class="sort-icon">{{ getSortIcon('recordedAt') }}</span></th>
                <th class="sortable" (click)="sortBy('latitude')">Latitude <span class="sort-icon">{{ getSortIcon('latitude') }}</span></th>
                <th class="sortable" (click)="sortBy('longitude')">Longitude <span class="sort-icon">{{ getSortIcon('longitude') }}</span></th>
                <th class="sortable" (click)="sortBy('speedKph')">Vitesse <span class="sort-icon">{{ getSortIcon('speedKph') }}</span></th>
                <th class="sortable" (click)="sortBy('courseDeg')">Cap <span class="sort-icon">{{ getSortIcon('courseDeg') }}</span></th>
                <th class="sortable" (click)="sortBy('ignitionOn')">Ignition <span class="sort-icon">{{ getSortIcon('ignitionOn') }}</span></th>
                <th class="sortable" (click)="sortBy('fuelRaw')">Carburant <span class="sort-icon">{{ getSortIcon('fuelRaw') }}</span></th>
                <th class="sortable" (click)="sortBy('rpm')">RPM <span class="sort-icon">{{ getSortIcon('rpm') }}</span></th>
                <th class="sortable" (click)="sortBy('odometerKm')">Odomètre <span class="sort-icon">{{ getSortIcon('odometerKm') }}</span></th>
                <th class="sortable" (click)="sortBy('temperatureC')">Temp. <span class="sort-icon">{{ getSortIcon('temperatureC') }}</span></th>
                <th class="sortable" (click)="sortBy('isRealTime')">Temps réel <span class="sort-icon">{{ getSortIcon('isRealTime') }}</span></th>
                <th class="sortable" (click)="sortBy('address')">Adresse <span class="sort-icon">{{ getSortIcon('address') }}</span></th>
                <th class="sortable" (click)="sortBy('createdAt')">Créé le <span class="sort-icon">{{ getSortIcon('createdAt') }}</span></th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let t of paginatedTrames; trackBy: trackById">
                <td class="id-cell">{{ t.id }}</td>
                <td class="time-cell">{{ formatDate(t.recordedAt) }}</td>
                <td class="coord-cell">{{ t.latitude | number:'1.6-6' }}</td>
                <td class="coord-cell">{{ t.longitude | number:'1.6-6' }}</td>
                <td class="speed-cell" [class.speed-high]="t.speedKph > 120" [class.speed-zero]="!t.speedKph || t.speedKph === 0">{{ t.speedKph != null ? (t.speedKph | number:'1.0-0') + ' km/h' : '-' }}</td>
                <td class="course-cell">{{ t.courseDeg != null ? (t.courseDeg | number:'1.0-0') + '°' : '-' }}</td>
                <td class="ignition-cell">
                  <span class="badge" [class.badge-on]="t.ignitionOn" [class.badge-off]="!t.ignitionOn">{{ t.ignitionOn ? 'ON' : 'OFF' }}</span>
                </td>
                <td class="fuel-cell">{{ t.fuelRaw != null ? t.fuelRaw + '%' : '-' }}</td>
                <td class="rpm-cell" [class.rpm-high]="t.rpm > 3500">{{ t.rpm || '-' }}</td>
                <td class="odo-cell">{{ t.odometerKm != null ? (t.odometerKm | number:'1.0-0') + ' km' : '-' }}</td>
                <td class="temp-cell">{{ t.temperatureC != null ? t.temperatureC + '°C' : '-' }}</td>
                <td class="rt-cell">
                  <span class="badge" [class.badge-rt]="t.isRealTime" [class.badge-stored]="!t.isRealTime">{{ t.isRealTime ? 'RT' : 'Stored' }}</span>
                </td>
                <td class="addr-cell" [title]="t.address || ''">{{ t.address || '-' }}</td>
                <td class="time-cell">{{ formatDate(t.createdAt) }}</td>
              </tr>
            </tbody>
          </table>
        </div>

        <!-- Pagination -->
        <div class="pagination-bar" *ngIf="filteredTrames.length > 0">
          <div class="page-info">
            Affichage {{ startItem }}-{{ endItem }} sur {{ filteredTrames.length }}
          </div>
          <div class="page-controls">
            <button (click)="goToPage(1)" [disabled]="currentPage === 1">«</button>
            <button (click)="goToPage(currentPage - 1)" [disabled]="currentPage === 1">‹</button>
            <span class="page-num">{{ currentPage }} / {{ totalPages }}</span>
            <button (click)="goToPage(currentPage + 1)" [disabled]="currentPage === totalPages">›</button>
            <button (click)="goToPage(totalPages)" [disabled]="currentPage === totalPages">»</button>
            <select [(ngModel)]="pageSize" (ngModelChange)="currentPage = 1">
              <option [ngValue]="50">50</option>
              <option [ngValue]="100">100</option>
              <option [ngValue]="200">200</option>
              <option [ngValue]="500">500</option>
            </select>
          </div>
        </div>

        <!-- Empty state -->
        <div class="empty-state" *ngIf="!loading && trames.length === 0 && selectedVehicleId">
          <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="#94a3b8" stroke-width="1.5">
            <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z"/><circle cx="12" cy="10" r="3"/>
          </svg>
          <p>Aucune trame GPS pour cette période</p>
        </div>

        <div class="empty-state" *ngIf="!selectedVehicleId && !loading">
          <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="#94a3b8" stroke-width="1.5">
            <rect x="1" y="3" width="15" height="13" rx="2"/><path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-5"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/>
          </svg>
          <p>Sélectionnez un véhicule pour afficher ses trames GPS</p>
        </div>
      </div>
    </admin-layout>
  `,
  styles: [`
    .trames-page {
      display: flex;
      flex-direction: column;
      gap: 16px;
      height: calc(100vh - 64px - 48px);
    }

    @keyframes rise {
      from { opacity: 0; transform: translateY(8px); }
      to { opacity: 1; transform: none; }
    }

    .filters-bar,
    .stats-bar,
    .table-container,
    .pagination-bar {
      animation: rise 0.25s ease both;
    }

    @media (prefers-reduced-motion: reduce) {
      .filters-bar,
      .stats-bar,
      .table-container,
      .pagination-bar {
        animation: none;
      }
    }

    .filters-bar {
      display: flex;
      align-items: flex-end;
      gap: 12px;
      padding: 16px;
      background: var(--adm-card);
      border-radius: 16px;
      border: 1px solid var(--adm-border);
      box-shadow: var(--adm-shadow);
      flex-wrap: wrap;
    }

    .filter-group {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .filter-group label {
      font-size: 11px;
      font-weight: 700;
      color: var(--adm-sub);
      text-transform: uppercase;
      letter-spacing: 0.06em;
    }

    .filter-group select,
    .filter-group input {
      padding: 8px 12px;
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      font-size: 13px;
      color: var(--adm-ink);
      background: #f8fafc;
      outline: none;
      transition: border-color 0.2s, box-shadow 0.2s;
    }

    .filter-group select:focus,
    .filter-group input:focus {
      border-color: var(--adm-indigo);
      box-shadow: 0 0 0 3px rgba(79, 70, 229, 0.12);
    }

    .btn-load {
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
      transition: all 0.2s;
      height: 38px;
    }

    .btn-load:hover:not(:disabled) {
      background: var(--adm-indigo-ink);
      transform: translateY(-1px);
      box-shadow: 0 4px 12px rgba(79, 70, 229, 0.3);
    }

    .btn-load:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }

    .spinner {
      width: 14px;
      height: 14px;
      border: 2px solid rgba(255,255,255,0.3);
      border-top-color: #fff;
      border-radius: 50%;
      animation: spin 0.6s linear infinite;
    }

    @keyframes spin { to { transform: rotate(360deg); } }

    .stats-bar {
      display: flex;
      align-items: center;
      gap: 16px;
      padding: 12px 16px;
      background: var(--adm-card);
      border-radius: 16px;
      border: 1px solid var(--adm-border);
      box-shadow: var(--adm-shadow);
      flex-wrap: wrap;
    }

    .stat {
      font-size: 12px;
      color: var(--adm-sub);
    }

    .stat:nth-child(-n+4) {
      border-left: 3px solid var(--adm-indigo);
      border-radius: 2px;
      padding-left: 10px;
    }

    .stat:nth-child(2) { border-left-color: var(--adm-cyan); }
    .stat:nth-child(3) { border-left-color: var(--adm-red); }
    .stat:nth-child(4) { border-left-color: var(--adm-amber); }

    .stat strong {
      color: var(--adm-ink);
      font-weight: 800;
      font-variant-numeric: tabular-nums;
    }

    .search-input {
      padding: 7px 12px;
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      font-size: 12px;
      width: 220px;
      outline: none;
      transition: border-color 0.2s, box-shadow 0.2s;
    }

    .search-input:focus {
      border-color: var(--adm-indigo);
      box-shadow: 0 0 0 3px rgba(79, 70, 229, 0.12);
    }

    .table-container {
      flex: 1;
      overflow: auto;
      background: var(--adm-card);
      border-radius: 16px;
      border: 1px solid var(--adm-border);
      box-shadow: var(--adm-shadow);
    }

    .trames-table {
      width: 100%;
      border-collapse: collapse;
      font-size: 13px;
    }

    .trames-table thead {
      position: sticky;
      top: 0;
      z-index: 10;
    }

    .trames-table th {
      padding: 10px 12px;
      text-align: left;
      font-size: 11px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: var(--adm-sub);
      background: #f8fafc;
      border-bottom: 1px solid var(--adm-border);
      white-space: nowrap;
      cursor: pointer;
      user-select: none;
      transition: background 0.15s;
    }

    .trames-table th:hover {
      background: var(--adm-track);
      color: var(--adm-ink);
    }

    .trames-table th .sort-icon {
      font-size: 9px;
      color: #94a3b8;
      margin-left: 2px;
    }

    .trames-table td {
      padding: 8px 12px;
      border-bottom: 1px solid #eef2f7;
      color: var(--adm-ink);
      white-space: nowrap;
      font-variant-numeric: tabular-nums;
    }

    .trames-table tbody tr:hover {
      background: #f8fafc;
    }

    .id-cell { color: #94a3b8; font-size: 11px; }
    .time-cell { font-family: monospace; font-size: 11px; }
    .coord-cell { font-family: monospace; font-size: 11px; color: var(--adm-indigo); }
    .speed-cell { font-weight: 600; }
    .speed-high { color: var(--adm-red); }
    .speed-zero { color: var(--adm-slate); }
    .course-cell { color: var(--adm-sub); }
    .fuel-cell { color: var(--adm-amber-ink); font-weight: 500; }
    .rpm-cell { color: var(--adm-indigo-ink); }
    .rpm-high { color: var(--adm-red); font-weight: 600; }
    .odo-cell { color: var(--adm-sub); font-size: 11px; }
    .temp-cell { color: var(--adm-cyan-ink); }
    .addr-cell {
      max-width: 250px;
      overflow: hidden;
      text-overflow: ellipsis;
      font-size: 11px;
      color: var(--adm-sub);
    }

    .badge {
      display: inline-block;
      padding: 2px 10px;
      border-radius: 999px;
      font-size: 11px;
      font-weight: 700;
      text-transform: uppercase;
    }

    .badge-on { background: rgba(5, 150, 105, 0.10); color: var(--adm-green-ink); }
    .badge-off { background: rgba(220, 38, 38, 0.10); color: var(--adm-red-ink); }
    .badge-rt { background: rgba(8, 145, 178, 0.10); color: var(--adm-cyan-ink); }
    .badge-stored { background: rgba(100, 116, 139, 0.10); color: var(--adm-slate-ink); }

    .pagination-bar {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 10px 16px;
      background: var(--adm-card);
      border-radius: 16px;
      border: 1px solid var(--adm-border);
      box-shadow: var(--adm-shadow);
    }

    .page-info {
      font-size: 12px;
      color: var(--adm-sub);
      font-variant-numeric: tabular-nums;
    }

    .page-controls {
      display: flex;
      align-items: center;
      gap: 6px;
    }

    .page-controls button {
      width: 32px;
      height: 32px;
      border: 1px solid var(--adm-border);
      background: #fff;
      border-radius: 8px;
      cursor: pointer;
      font-size: 14px;
      color: var(--adm-slate-ink);
      transition: all 0.15s;
    }

    .page-controls button:hover:not(:disabled) {
      background: #f8fafc;
      border-color: var(--adm-indigo);
      color: var(--adm-indigo-ink);
    }

    .page-controls button:disabled {
      opacity: 0.4;
      cursor: not-allowed;
    }

    .page-num {
      font-size: 13px;
      font-weight: 600;
      color: var(--adm-ink);
      padding: 0 8px;
      font-variant-numeric: tabular-nums;
    }

    .page-controls select {
      padding: 6px 8px;
      border: 1px solid var(--adm-border);
      border-radius: 8px;
      font-size: 12px;
      margin-left: 8px;
      outline: none;
      transition: border-color 0.2s, box-shadow 0.2s;
    }

    .page-controls select:focus {
      border-color: var(--adm-indigo);
      box-shadow: 0 0 0 3px rgba(79, 70, 229, 0.12);
    }

    .empty-state {
      flex: 1;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 12px;
      color: #94a3b8;
    }

    .empty-state p {
      font-size: 14px;
      margin: 0;
    }
  `]
})
export class AdminTramesComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();

  vehicles: any[] = [];
  selectedVehicleId: number | null = null;
  fromDate = '';
  toDate = '';
  maxPoints = 5000;
  loading = false;

  trames: any[] = [];
  filteredTrames: any[] = [];
  searchQuery = '';

  // Sorting
  sortColumn = '';
  sortDirection: 'asc' | 'desc' = 'asc';

  // Pagination
  currentPage = 1;
  pageSize = 100;

  // Stats
  statsMaxSpeed = 0;
  statsMaxRpm: number | null = null;
  statsFuelMin = 0;
  statsFuelMax = 0;

  constructor(private adminService: AdminService) {}

  ngOnInit() {
    // Set default dates: last 24h
    const now = new Date();
    const yesterday = new Date(now.getTime() - 24 * 60 * 60 * 1000);
    this.toDate = this.toLocalDatetime(now);
    this.fromDate = this.toLocalDatetime(yesterday);

    this.adminService.getVehicles().pipe(takeUntil(this.destroy$)).subscribe({
      next: (vehicles) => {
        this.vehicles = vehicles.sort((a: any, b: any) => (a.plate || a.name || '').localeCompare(b.plate || b.name || ''));
      }
    });
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }

  onVehicleChange() {
    this.trames = [];
    this.filteredTrames = [];
  }

  loadTrames() {
    if (!this.selectedVehicleId) return;
    this.loading = true;
    this.trames = [];
    this.filteredTrames = [];

    const from = this.fromDate ? new Date(this.fromDate) : undefined;
    const to = this.toDate ? new Date(this.toDate) : undefined;

    this.adminService.getVehicleHistory(this.selectedVehicleId, from, to, this.maxPoints)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (positions) => {
          this.trames = positions;
          this.computeStats();
          this.filterTrames();
          this.loading = false;
        },
        error: () => {
          this.loading = false;
        }
      });
  }

  computeStats() {
    this.statsMaxSpeed = 0;
    this.statsMaxRpm = null;
    this.statsFuelMin = 999;
    this.statsFuelMax = 0;
    for (const t of this.trames) {
      if (t.speedKph != null && t.speedKph > this.statsMaxSpeed) this.statsMaxSpeed = t.speedKph;
      if (t.rpm != null && (this.statsMaxRpm === null || t.rpm > this.statsMaxRpm)) this.statsMaxRpm = t.rpm;
      if (t.fuelRaw != null) {
        if (t.fuelRaw < this.statsFuelMin) this.statsFuelMin = t.fuelRaw;
        if (t.fuelRaw > this.statsFuelMax) this.statsFuelMax = t.fuelRaw;
      }
    }
    if (this.statsFuelMin === 999) this.statsFuelMin = 0;
  }

  filterTrames() {
    if (!this.searchQuery.trim()) {
      this.filteredTrames = [...this.trames];
    } else {
      const q = this.searchQuery.toLowerCase();
      this.filteredTrames = this.trames.filter((t: any) =>
        (t.address && t.address.toLowerCase().includes(q)) ||
        (t.id && t.id.toString().includes(q)) ||
        (t.speedKph != null && t.speedKph.toString().includes(q)) ||
        (t.rpm != null && t.rpm.toString().includes(q))
      );
    }
    this.currentPage = 1;
  }

  sortBy(column: string) {
    if (this.sortColumn === column) {
      this.sortDirection = this.sortDirection === 'asc' ? 'desc' : 'asc';
    } else {
      this.sortColumn = column;
      this.sortDirection = 'asc';
    }
    const dir = this.sortDirection === 'asc' ? 1 : -1;
    this.filteredTrames.sort((a: any, b: any) => {
      let valA = a[column];
      let valB = b[column];
      if (valA == null && valB == null) return 0;
      if (valA == null) return 1;
      if (valB == null) return -1;
      if (typeof valA === 'number' && typeof valB === 'number') return (valA - valB) * dir;
      if (typeof valA === 'boolean') return (valA === valB ? 0 : valA ? -1 : 1) * dir;
      return String(valA).localeCompare(String(valB), 'fr') * dir;
    });
    this.currentPage = 1;
  }

  getSortIcon(column: string): string {
    if (this.sortColumn !== column) return '\u21C5';
    return this.sortDirection === 'asc' ? '\u25B2' : '\u25BC';
  }

  get totalPages(): number {
    return Math.max(1, Math.ceil(this.filteredTrames.length / this.pageSize));
  }

  get startItem(): number {
    return (this.currentPage - 1) * this.pageSize + 1;
  }

  get endItem(): number {
    return Math.min(this.currentPage * this.pageSize, this.filteredTrames.length);
  }

  get paginatedTrames(): any[] {
    const start = (this.currentPage - 1) * this.pageSize;
    return this.filteredTrames.slice(start, start + this.pageSize);
  }

  goToPage(page: number) {
    if (page >= 1 && page <= this.totalPages) {
      this.currentPage = page;
    }
  }

  trackById(index: number, item: any) {
    return item.id;
  }

  formatDate(dateStr: string): string {
    if (!dateStr) return '-';
    const d = new Date(dateStr);
    return d.toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric' }) + ' ' +
           d.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
  }

  private toLocalDatetime(date: Date): string {
    const pad = (n: number) => n.toString().padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
  }
}
