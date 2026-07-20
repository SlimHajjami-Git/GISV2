import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService, ActivityLog, Client } from '../services/admin.service';

@Component({
  selector: 'admin-activity',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent],
  template: `
    <admin-layout pageTitle="Activity Logs">
      <div class="activity-page">
        <div class="page-header">
          <div class="filters">
            <div class="search-box">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
              </svg>
              <input type="text" [(ngModel)]="searchQuery" (input)="filterLogs()" placeholder="Search logs..." />
            </div>
            <select class="filter-select" [(ngModel)]="companyFilter" (change)="filterLogs()">
              <option value="all">All Companies</option>
              <option *ngFor="let company of companies" [value]="company.id">{{ company.name }}</option>
            </select>
            <select class="filter-select" [(ngModel)]="actionFilter" (change)="filterLogs()">
              <option value="all">All Actions</option>
              <option value="login">Login</option>
              <option value="logout">Logout</option>
              <option value="view_vehicle">View Vehicle</option>
              <option value="create_geofence">Create Geofence</option>
              <option value="generate_report">Generate Report</option>
              <option value="update_settings">Update Settings</option>
              <option value="add_maintenance">Add Maintenance</option>
            </select>
            <input type="date" class="date-filter" [(ngModel)]="dateFilter" (change)="filterLogs()" />
          </div>
          <div class="header-actions">
            <button class="export-btn" (click)="exportLogs()">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/>
                <polyline points="7,10 12,15 17,10"/>
                <line x1="12" y1="15" x2="12" y2="3"/>
              </svg>
              Export
            </button>
            <button class="refresh-btn" (click)="loadLogs()">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="23,4 23,10 17,10"/>
                <polyline points="1,20 1,14 7,14"/>
                <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"/>
              </svg>
            </button>
          </div>
        </div>

        <div class="activity-stats">
          <div class="stat-card">
            <div class="stat-icon login">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M15 3h4a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-4"/>
                <polyline points="10,17 15,12 10,7"/>
                <line x1="15" y1="12" x2="3" y2="12"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ loginCount }}</span>
              <span class="stat-label">Logins Today</span>
            </div>
          </div>
          <div class="stat-card">
            <div class="stat-icon actions">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="22,12 18,12 15,21 9,3 6,12 2,12"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ filteredLogs.length }}</span>
              <span class="stat-label">Total Actions</span>
            </div>
          </div>
          <div class="stat-card">
            <div class="stat-icon users">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/>
                <circle cx="9" cy="7" r="4"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ uniqueUsersCount }}</span>
              <span class="stat-label">Unique Users</span>
            </div>
          </div>
          <div class="stat-card">
            <div class="stat-icon companies">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="2" y="7" width="20" height="14" rx="2" ry="2"/>
                <path d="M16 21V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v16"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ uniqueCompaniesCount }}</span>
              <span class="stat-label">Active Companies</span>
            </div>
          </div>
        </div>

        <div class="activity-table-container">
          <table class="activity-table">
            <thead>
              <tr>
                <th>Timestamp</th>
                <th>User</th>
                <th>Company</th>
                <th>Action</th>
                <th>Details</th>
                <th>IP Address</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let log of paginatedLogs">
                <td>
                  <div class="timestamp">
                    <span class="date">{{ formatDate(log.timestamp) }}</span>
                    <span class="time">{{ formatTime(log.timestamp) }}</span>
                  </div>
                </td>
                <td>
                  <div class="user-cell">
                    <div class="user-avatar">{{ log.userName.charAt(0) }}</div>
                    <span class="user-name">{{ log.userName }}</span>
                  </div>
                </td>
                <td>
                  <span class="company-badge">{{ log.companyName }}</span>
                </td>
                <td>
                  <span class="action-badge" [class]="getActionClass(log.action)">
                    {{ formatAction(log.action) }}
                  </span>
                </td>
                <td>
                  <span class="details">{{ log.details }}</span>
                </td>
                <td>
                  <span class="ip-address">{{ log.ipAddress }}</span>
                </td>
              </tr>
            </tbody>
          </table>

          <div class="pagination" *ngIf="totalPages > 1">
            <button class="page-btn" [disabled]="currentPage === 1" (click)="goToPage(currentPage - 1)">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="15,18 9,12 15,6"/>
              </svg>
            </button>
            <span class="page-info">Page {{ currentPage }} of {{ totalPages }}</span>
            <button class="page-btn" [disabled]="currentPage === totalPages" (click)="goToPage(currentPage + 1)">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="9,18 15,12 9,6"/>
              </svg>
            </button>
          </div>
        </div>

        <div class="live-feed" [class.expanded]="showLiveFeed">
          <button class="feed-toggle" (click)="showLiveFeed = !showLiveFeed">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="12" cy="12" r="10"/>
              <circle cx="12" cy="12" r="3" fill="currentColor"/>
            </svg>
            Live Feed
            <span class="live-indicator"></span>
          </button>
          <div class="feed-content" *ngIf="showLiveFeed">
            <div class="feed-item" *ngFor="let log of recentLogs">
              <span class="feed-time">{{ getTimeAgo(log.timestamp) }}</span>
              <span class="feed-user">{{ log.userName }}</span>
              <span class="feed-action">{{ formatAction(log.action) }}</span>
            </div>
          </div>
        </div>
      </div>
    </admin-layout>
  `,
  styles: [`
    .activity-page {
      display: flex;
      flex-direction: column;
      gap: 24px;
      position: relative;
    }

    .page-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      flex-wrap: wrap;
      gap: 16px;
    }

    .filters {
      display: flex;
      align-items: center;
      gap: 12px;
      flex-wrap: wrap;
    }

    .search-box {
      display: flex;
      align-items: center;
      gap: 10px;
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      padding: 10px 14px;
      width: 240px;
      transition: border-color 0.2s, box-shadow 0.2s;
    }

    .search-box:focus-within {
      border-color: var(--adm-indigo);
      box-shadow: 0 0 0 3px rgba(79, 70, 229, 0.12);
    }

    .search-box svg { color: var(--adm-sub); }

    .search-box input {
      flex: 1;
      border: none;
      background: transparent;
      color: var(--adm-ink);
      font-size: 14px;
      outline: none;
    }

    .filter-select, .date-filter {
      padding: 10px 14px;
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      color: var(--adm-ink);
      font-size: 14px;
      outline: none;
      cursor: pointer;
      transition: border-color 0.2s, box-shadow 0.2s;
    }

    .filter-select:focus, .date-filter:focus {
      border-color: var(--adm-indigo);
      box-shadow: 0 0 0 3px rgba(79, 70, 229, 0.12);
    }

    .header-actions {
      display: flex;
      gap: 10px;
    }

    .export-btn {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 9px 18px;
      background: var(--adm-indigo);
      border: 1px solid var(--adm-indigo);
      border-radius: 10px;
      color: #fff;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .export-btn:hover {
      background: var(--adm-indigo-ink);
      border-color: var(--adm-indigo-ink);
    }

    .refresh-btn {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px;
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      color: var(--adm-ink);
      font-size: 14px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .refresh-btn:hover {
      border-color: var(--adm-indigo);
      color: var(--adm-indigo-ink);
    }

    .activity-stats {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 16px;
    }

    .stat-card {
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-left-width: 3px;
      border-radius: 16px;
      box-shadow: var(--adm-shadow);
      padding: 20px;
      display: flex;
      align-items: center;
      gap: 16px;
      animation: rise .35s ease backwards;
    }

    .stat-card:nth-child(1) { border-left-color: var(--adm-green); }
    .stat-card:nth-child(2) { border-left-color: var(--adm-indigo); }
    .stat-card:nth-child(3) { border-left-color: var(--adm-cyan); }
    .stat-card:nth-child(4) { border-left-color: var(--adm-amber); }

    .stat-icon {
      width: 48px;
      height: 48px;
      border-radius: 12px;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .stat-icon.login { background: rgba(5, 150, 105, 0.12); color: var(--adm-green); }
    .stat-icon.actions { background: rgba(79, 70, 229, 0.12); color: var(--adm-indigo); }
    .stat-icon.users { background: rgba(8, 145, 178, 0.12); color: var(--adm-cyan); }
    .stat-icon.companies { background: rgba(217, 119, 6, 0.12); color: var(--adm-amber); }

    .stat-content {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

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

    .activity-table-container {
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 16px;
      box-shadow: var(--adm-shadow);
      overflow: hidden;
      animation: rise .35s ease backwards;
    }

    .activity-table {
      width: 100%;
      border-collapse: collapse;
    }

    .activity-table th {
      padding: 14px 20px;
      text-align: left;
      font-size: 11px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: .06em;
      color: var(--adm-sub);
      background: #f8fafc;
      border-bottom: 1px solid var(--adm-border);
    }

    .activity-table td {
      padding: 14px 20px;
      font-size: 13px;
      border-bottom: 1px solid #eef2f7;
    }

    .activity-table tbody tr:hover td {
      background: #f8fafc;
    }

    .activity-table tr:last-child td {
      border-bottom: none;
    }

    .timestamp {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .timestamp .date {
      font-size: 13px;
      color: var(--adm-ink);
    }

    .timestamp .time {
      font-size: 11px;
      color: var(--adm-sub);
      font-variant-numeric: tabular-nums;
    }

    .user-cell {
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .user-avatar {
      width: 32px;
      height: 32px;
      background: linear-gradient(135deg, var(--adm-indigo) 0%, var(--adm-indigo-ink) 100%);
      border-radius: 8px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 600;
      font-size: 13px;
      color: #fff;
    }

    .user-name {
      font-size: 14px;
      color: var(--adm-ink);
    }

    .company-badge {
      padding: 3px 10px;
      background: rgba(100, 116, 139, 0.10);
      border-radius: 999px;
      font-size: 11px;
      font-weight: 700;
      color: var(--adm-slate-ink);
    }

    .action-badge {
      padding: 3px 10px;
      border-radius: 999px;
      font-size: 11px;
      font-weight: 700;
    }

    .action-badge.login { background: rgba(5, 150, 105, 0.10); color: var(--adm-green-ink); }
    .action-badge.logout { background: rgba(220, 38, 38, 0.10); color: var(--adm-red-ink); }
    .action-badge.view { background: rgba(8, 145, 178, 0.12); color: var(--adm-cyan-ink); }
    .action-badge.create { background: rgba(79, 70, 229, 0.12); color: var(--adm-indigo-ink); }
    .action-badge.update { background: rgba(217, 119, 6, 0.12); color: var(--adm-amber-ink); }
    .action-badge.generate { background: rgba(100, 116, 139, 0.12); color: var(--adm-slate-ink); }

    .details {
      font-size: 13px;
      color: var(--adm-sub);
    }

    .ip-address {
      font-size: 12px;
      color: var(--adm-sub);
      font-family: monospace;
      font-variant-numeric: tabular-nums;
    }

    .pagination {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 16px;
      padding: 16px;
      border-top: 1px solid var(--adm-border);
    }

    .page-btn {
      width: 36px;
      height: 36px;
      border: 1px solid var(--adm-border);
      background: var(--adm-card);
      border-radius: 10px;
      color: var(--adm-sub);
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }

    .page-btn:hover:not(:disabled) {
      border-color: var(--adm-indigo);
      color: var(--adm-indigo-ink);
    }

    .page-btn:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }

    .page-info {
      font-size: 13px;
      color: var(--adm-sub);
      font-variant-numeric: tabular-nums;
    }

    .live-feed {
      position: fixed;
      bottom: 24px;
      right: 24px;
      background: linear-gradient(160deg, var(--adm-carb1) 0%, var(--adm-carb2) 100%);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 16px;
      box-shadow: 0 24px 60px -24px rgba(2, 6, 23, 0.45);
      overflow: hidden;
      z-index: 50;
      transition: all 0.3s;
    }

    .live-feed.expanded {
      width: 320px;
    }

    .feed-toggle {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 12px 18px;
      background: none;
      border: none;
      color: #e2e8f0;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      width: 100%;
    }

    .live-indicator {
      width: 8px;
      height: 8px;
      background: var(--adm-green);
      border-radius: 50%;
      margin-left: auto;
      box-shadow: 0 0 8px rgba(5, 150, 105, 0.6);
      animation: pulse 2s infinite;
    }

    @keyframes pulse {
      0%, 100% { opacity: 1; transform: scale(1); }
      50% { opacity: 0.5; transform: scale(0.9); }
    }

    .feed-content {
      max-height: 300px;
      overflow-y: auto;
      border-top: 1px solid rgba(255, 255, 255, 0.08);
    }

    .feed-item {
      display: flex;
      flex-direction: column;
      gap: 4px;
      padding: 12px 18px;
      border-bottom: 1px solid rgba(255, 255, 255, 0.06);
    }

    .feed-item:last-child {
      border-bottom: none;
    }

    .feed-time {
      font-size: 11px;
      color: #94a3b8;
      font-variant-numeric: tabular-nums;
    }

    .feed-user {
      font-size: 13px;
      color: #e2e8f0;
      font-weight: 500;
    }

    .feed-action {
      font-size: 12px;
      color: #94a3b8;
    }

    @keyframes rise {
      from { opacity: 0; transform: translateY(8px); }
      to { opacity: 1; transform: none; }
    }

    @media (prefers-reduced-motion: reduce) {
      .stat-card, .activity-table-container { animation: none; }
      .live-indicator { animation: none; }
    }

    @media (max-width: 1200px) {
      .activity-stats {
        grid-template-columns: repeat(2, 1fr);
      }
    }

    @media (max-width: 768px) {
      .activity-stats {
        grid-template-columns: 1fr;
      }
    }
  `]
})
export class AdminActivityComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  logs: ActivityLog[] = [];
  filteredLogs: ActivityLog[] = [];
  recentLogs: ActivityLog[] = [];
  companies: Client[] = [];

  searchQuery = '';
  companyFilter = 'all';
  actionFilter = 'all';
  dateFilter = '';

  currentPage = 1;
  itemsPerPage = 15;

  showLiveFeed = false;

  get totalPages(): number {
    return Math.ceil(this.filteredLogs.length / this.itemsPerPage);
  }

  get paginatedLogs(): ActivityLog[] {
    const start = (this.currentPage - 1) * this.itemsPerPage;
    return this.filteredLogs.slice(start, start + this.itemsPerPage);
  }

  get loginCount(): number {
    const today = new Date().toDateString();
    return this.logs.filter(l => l.action === 'login' && new Date(l.timestamp).toDateString() === today).length;
  }

  get uniqueUsersCount(): number {
    return new Set(this.filteredLogs.map(l => l.userId)).size;
  }

  get uniqueCompaniesCount(): number {
    return new Set(this.filteredLogs.map(l => l.companyId)).size;
  }

  constructor(
    private router: Router,
    private route: ActivatedRoute,
    private adminService: AdminService
  ) {}

  ngOnInit() {
    if (!this.adminService.isAuthenticated()) {
      this.router.navigate(['/admin/login']);
      return;
    }
    this.loadLogs();
    this.loadCompanies();

    this.route.queryParams.pipe(takeUntil(this.destroy$)).subscribe(params => {
      if (params['userId']) {
        this.searchQuery = params['userId'];
        this.filterLogs();
      }
    });
  }

  loadLogs() {
    this.adminService.getActivityLogs(100).pipe(takeUntil(this.destroy$)).subscribe(logs => {
      this.logs = logs;
      this.recentLogs = logs.slice(0, 10);
      this.filterLogs();
    });
  }

  loadCompanies() {
    this.adminService.getClients().pipe(takeUntil(this.destroy$)).subscribe(clients => {
      this.companies = clients;
    });
  }

  filterLogs() {
    this.filteredLogs = this.logs.filter(log => {
      const matchesSearch = !this.searchQuery ||
        log.userName.toLowerCase().includes(this.searchQuery.toLowerCase()) ||
        log.action.toLowerCase().includes(this.searchQuery.toLowerCase());
      const matchesCompany = this.companyFilter === 'all' || log.companyId.toString() === this.companyFilter;
      const matchesAction = this.actionFilter === 'all' || log.action === this.actionFilter;
      const matchesDate = !this.dateFilter ||
        new Date(log.timestamp).toDateString() === new Date(this.dateFilter).toDateString();
      return matchesSearch && matchesCompany && matchesAction && matchesDate;
    });
    this.currentPage = 1;
  }

  goToPage(page: number) {
    this.currentPage = page;
  }

  exportLogs() {
    alert('Exporting activity logs...');
  }

  getActionClass(action: string): string {
    if (action === 'login') return 'login';
    if (action === 'logout') return 'logout';
    if (action === 'session') return 'view';
    if (action.startsWith('view')) return 'view';
    if (action.startsWith('create')) return 'create';
    if (action.startsWith('update')) return 'update';
    if (action.startsWith('generate')) return 'generate';
    return '';
  }

  formatAction(action: string): string {
    const labels: { [k: string]: string } = {
      login: 'Connexion',
      logout: 'Déconnexion',
      session: 'Reprise de session'
    };
    if (labels[action]) return labels[action];
    return action.split('_').map(w => w.charAt(0).toUpperCase() + w.slice(1)).join(' ');
  }

  formatDate(date: Date): string {
    return new Date(date).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
  }

  formatTime(date: Date): string {
    return new Date(date).toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' });
  }

  getTimeAgo(date: Date): string {
    const now = new Date();
    const diff = now.getTime() - new Date(date).getTime();
    const minutes = Math.floor(diff / 60000);
    if (minutes < 1) return 'Just now';
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours}h ago`;
    return `${Math.floor(hours / 24)}d ago`;
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
