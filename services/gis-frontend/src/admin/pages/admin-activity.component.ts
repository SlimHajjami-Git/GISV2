import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
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
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 10px;
      padding: 10px 14px;
      width: 240px;
    }

    .search-box svg { color: #8b98a5; }

    .search-box input {
      flex: 1;
      border: none;
      background: transparent;
      color: #e7e9ea;
      font-size: 14px;
      outline: none;
    }

    .filter-select, .date-filter {
      padding: 10px 14px;
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 10px;
      color: #e7e9ea;
      font-size: 14px;
      outline: none;
      cursor: pointer;
    }

    .header-actions {
      display: flex;
      gap: 10px;
    }

    .export-btn, .refresh-btn {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 16px;
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 10px;
      color: #e7e9ea;
      font-size: 14px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .export-btn:hover, .refresh-btn:hover {
      background: rgba(255, 255, 255, 0.1);
    }

    .refresh-btn {
      padding: 10px;
    }

    .activity-stats {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 16px;
    }

    .stat-card {
      background: linear-gradient(135deg, rgba(26, 31, 46, 0.8) 0%, rgba(20, 24, 36, 0.9) 100%);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 14px;
      padding: 20px;
      display: flex;
      align-items: center;
      gap: 16px;
    }

    .stat-icon {
      width: 48px;
      height: 48px;
      border-radius: 12px;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .stat-icon.login { background: rgba(34, 197, 94, 0.15); color: #22c55e; }
    .stat-icon.actions { background: rgba(59, 130, 246, 0.15); color: #3b82f6; }
    .stat-icon.users { background: rgba(0, 212, 170, 0.15); color: #00d4aa; }
    .stat-icon.companies { background: rgba(249, 115, 22, 0.15); color: #f97316; }

    .stat-content {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .stat-value {
      font-size: 24px;
      font-weight: 700;
      color: #e7e9ea;
    }

    .stat-label {
      font-size: 13px;
      color: #8b98a5;
    }

    .activity-table-container {
      background: linear-gradient(135deg, rgba(26, 31, 46, 0.8) 0%, rgba(20, 24, 36, 0.9) 100%);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 16px;
      overflow: hidden;
    }

    .activity-table {
      width: 100%;
      border-collapse: collapse;
    }

    .activity-table th {
      padding: 16px 20px;
      text-align: left;
      font-size: 12px;
      font-weight: 600;
      text-transform: uppercase;
      color: #8b98a5;
      background: rgba(255, 255, 255, 0.03);
      border-bottom: 1px solid rgba(255, 255, 255, 0.08);
    }

    .activity-table td {
      padding: 14px 20px;
      border-bottom: 1px solid rgba(255, 255, 255, 0.05);
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
      color: #e7e9ea;
    }

    .timestamp .time {
      font-size: 11px;
      color: #6b7280;
    }

    .user-cell {
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .user-avatar {
      width: 32px;
      height: 32px;
      background: linear-gradient(135deg, #3b82f6 0%, #2563eb 100%);
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
      color: #e7e9ea;
    }

    .company-badge {
      padding: 4px 10px;
      background: rgba(255, 255, 255, 0.05);
      border-radius: 6px;
      font-size: 12px;
      color: #8b98a5;
    }

    .action-badge {
      padding: 4px 10px;
      border-radius: 6px;
      font-size: 12px;
      font-weight: 500;
    }

    .action-badge.login { background: rgba(34, 197, 94, 0.15); color: #22c55e; }
    .action-badge.logout { background: rgba(239, 68, 68, 0.15); color: #ef4444; }
    .action-badge.view { background: rgba(59, 130, 246, 0.15); color: #3b82f6; }
    .action-badge.create { background: rgba(0, 212, 170, 0.15); color: #00d4aa; }
    .action-badge.update { background: rgba(249, 115, 22, 0.15); color: #f97316; }
    .action-badge.generate { background: rgba(168, 85, 247, 0.15); color: #a855f7; }

    .details {
      font-size: 13px;
      color: #8b98a5;
    }

    .ip-address {
      font-size: 12px;
      color: #6b7280;
      font-family: monospace;
    }

    .pagination {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 16px;
      padding: 16px;
      border-top: 1px solid rgba(255, 255, 255, 0.08);
    }

    .page-btn {
      width: 36px;
      height: 36px;
      border: none;
      background: rgba(255, 255, 255, 0.05);
      border-radius: 8px;
      color: #8b98a5;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }

    .page-btn:hover:not(:disabled) {
      background: rgba(255, 255, 255, 0.1);
      color: #e7e9ea;
    }

    .page-btn:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }

    .page-info {
      font-size: 14px;
      color: #8b98a5;
    }

    .live-feed {
      position: fixed;
      bottom: 24px;
      right: 24px;
      background: linear-gradient(135deg, #1a1f2e 0%, #141824 100%);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 14px;
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
      color: #e7e9ea;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
      width: 100%;
    }

    .live-indicator {
      width: 8px;
      height: 8px;
      background: #22c55e;
      border-radius: 50%;
      margin-left: auto;
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
      border-bottom: 1px solid rgba(255, 255, 255, 0.05);
    }

    .feed-item:last-child {
      border-bottom: none;
    }

    .feed-time {
      font-size: 11px;
      color: #6b7280;
    }

    .feed-user {
      font-size: 13px;
      color: #e7e9ea;
      font-weight: 500;
    }

    .feed-action {
      font-size: 12px;
      color: #8b98a5;
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
export class AdminActivityComponent implements OnInit {
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

    this.route.queryParams.subscribe(params => {
      if (params['userId']) {
        this.searchQuery = params['userId'];
        this.filterLogs();
      }
    });
  }

  loadLogs() {
    this.adminService.getActivityLogs(100).subscribe(logs => {
      this.logs = logs;
      this.recentLogs = logs.slice(0, 10);
      this.filterLogs();
    });
  }

  loadCompanies() {
    this.adminService.getClients().subscribe(clients => {
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
    if (action.startsWith('view')) return 'view';
    if (action.startsWith('create')) return 'create';
    if (action.startsWith('update')) return 'update';
    if (action.startsWith('generate')) return 'generate';
    return '';
  }

  formatAction(action: string): string {
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
}
