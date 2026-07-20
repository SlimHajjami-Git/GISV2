import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService, DashboardStats, FeatureUsage, ServiceHealth, ActivityLog } from '../services/admin.service';

@Component({
  selector: 'admin-dashboard',
  standalone: true,
  imports: [CommonModule, AdminLayoutComponent],
  template: `
    <admin-layout pageTitle="Dashboard">
      <div class="dashboard">
        <div class="stats-grid">
          <div class="stat-card primary">
            <div class="stat-icon">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/>
                <circle cx="9" cy="7" r="4"/>
                <path d="M23 21v-2a4 4 0 0 0-3-3.87"/>
                <path d="M16 3.13a4 4 0 0 1 0 7.75"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ stats?.totalClients || 0 }}</span>
              <span class="stat-label">Total Clients</span>
              <span class="stat-change positive">+{{ stats?.newClientsThisMonth || 0 }} this month</span>
            </div>
          </div>

          <div class="stat-card success">
            <div class="stat-icon">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10"/>
                <circle cx="12" cy="12" r="3" fill="currentColor"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ stats?.usersOnline || 0 }}</span>
              <span class="stat-label">Users Online</span>
              <span class="stat-change">of {{ stats?.totalUsers || 0 }} total</span>
            </div>
          </div>

          <div class="stat-card info">
            <div class="stat-icon">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="1" y="3" width="15" height="13" rx="2"/>
                <path d="M16 8h4a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2h-7a2 2 0 0 1-2-2v-3"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ stats?.totalVehicles || 0 }}</span>
              <span class="stat-label">Total Vehicles</span>
              <span class="stat-change">{{ stats?.activeDevices || 0 }} active GPS</span>
            </div>
          </div>

          <div class="stat-card warning">
            <div class="stat-icon">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M22 12h-4l-3 9L9 3l-3 9H2"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ formatNumber(stats?.totalPositionsToday || 0) }}</span>
              <span class="stat-label">Positions Today</span>
              <span class="stat-change">{{ stats?.alertsToday || 0 }} alerts</span>
            </div>
          </div>
        </div>

        <div class="dashboard-grid">
          <div class="card feature-usage">
            <div class="card-header">
              <h3>Feature Usage</h3>
              <span class="period-badge">Last 7 days</span>
            </div>
            <div class="feature-list">
              <div class="feature-item" *ngFor="let feature of featureUsage">
                <div class="feature-info">
                  <span class="feature-name">{{ feature.feature }}</span>
                  <span class="feature-users">{{ feature.uniqueUsers }} users</span>
                </div>
                <div class="feature-bar-container">
                  <div class="feature-bar" [style.width.%]="getFeaturePercentage(feature.usageCount)"></div>
                </div>
                <div class="feature-stats">
                  <span class="usage-count">{{ formatNumber(feature.usageCount) }}</span>
                  <span class="trend" [class.positive]="feature.trend > 0" [class.negative]="feature.trend < 0">
                    <svg *ngIf="feature.trend > 0" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
                      <polyline points="18,15 12,9 6,15"/>
                    </svg>
                    <svg *ngIf="feature.trend < 0" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
                      <polyline points="6,9 12,15 18,9"/>
                    </svg>
                    {{ feature.trend > 0 ? '+' : '' }}{{ feature.trend }}%
                  </span>
                </div>
              </div>
            </div>
          </div>

          <div class="card service-health">
            <div class="card-header">
              <h3>Service Health</h3>
              <button class="refresh-btn" (click)="loadHealth()">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <polyline points="23,4 23,10 17,10"/>
                  <polyline points="1,20 1,14 7,14"/>
                  <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"/>
                </svg>
              </button>
            </div>
            <div class="health-list">
              <div class="health-item" *ngFor="let service of serviceHealth">
                <div class="health-status" [class]="service.status">
                  <span class="status-dot"></span>
                </div>
                <div class="health-info">
                  <span class="service-name">{{ service.name }}</span>
                  <span class="service-uptime">{{ service.uptime }}% uptime</span>
                </div>
                <div class="health-metrics">
                  <span class="response-time">{{ service.responseTime }}ms</span>
                </div>
              </div>
            </div>
            <div class="health-summary">
              <span class="summary-item">
                <span class="dot healthy"></span>
                {{ getHealthyCount() }} Healthy
              </span>
              <span class="summary-item">
                <span class="dot degraded"></span>
                {{ getDegradedCount() }} Degraded
              </span>
              <span class="summary-item">
                <span class="dot down"></span>
                {{ getDownCount() }} Down
              </span>
            </div>
          </div>

          <div class="card recent-activity">
            <div class="card-header">
              <h3>Recent Activity</h3>
              <a routerLink="/admin/activity" class="view-all">View All</a>
            </div>
            <div class="activity-list">
              <div class="activity-item" *ngFor="let log of recentActivity">
                <div class="activity-icon" [class]="getActivityIconClass(log.action)">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <circle cx="12" cy="12" r="10"/>
                    <polyline points="12,6 12,12 16,14"/>
                  </svg>
                </div>
                <div class="activity-content">
                  <span class="activity-user">{{ log.userName }}</span>
                  <span class="activity-action">{{ formatAction(log.action) }}</span>
                  <span class="activity-company">{{ log.companyName }}</span>
                </div>
                <span class="activity-time">{{ getTimeAgo(log.timestamp) }}</span>
              </div>
            </div>
          </div>

          <div class="card quick-actions">
            <div class="card-header">
              <h3>Quick Actions</h3>
            </div>
            <div class="actions-grid">
              <button class="action-btn" (click)="navigateTo('/admin/clients')">
                <div class="action-icon add-client">
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M16 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/>
                    <circle cx="8.5" cy="7" r="4"/>
                    <line x1="20" y1="8" x2="20" y2="14"/>
                    <line x1="23" y1="11" x2="17" y2="11"/>
                  </svg>
                </div>
                <span>Add Client</span>
              </button>
              <button class="action-btn" (click)="navigateTo('/admin/estimates')">
                <div class="action-icon create-estimate">
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                    <polyline points="14,2 14,8 20,8"/>
                    <line x1="12" y1="18" x2="12" y2="12"/>
                    <line x1="9" y1="15" x2="15" y2="15"/>
                  </svg>
                </div>
                <span>New Estimate</span>
              </button>
              <button class="action-btn" (click)="navigateTo('/admin/features')">
                <div class="action-icon maintenance">
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>
                  </svg>
                </div>
                <span>Maintenance Mode</span>
              </button>
              <button class="action-btn" (click)="navigateTo('/admin/health')">
                <div class="action-icon health-check">
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M22 12h-4l-3 9L9 3l-3 9H2"/>
                  </svg>
                </div>
                <span>Health Check</span>
              </button>
            </div>
          </div>
        </div>
      </div>
    </admin-layout>
  `,
  styles: [`
    .dashboard {
      display: flex;
      flex-direction: column;
      gap: 24px;
    }

    @keyframes rise {
      from { opacity: 0; transform: translateY(8px); }
      to { opacity: 1; transform: none; }
    }

    .stats-grid {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 20px;
    }

    .stat-card {
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-left: 3px solid var(--adm-indigo);
      border-radius: 16px;
      padding: 24px;
      display: flex;
      align-items: flex-start;
      gap: 16px;
      transition: box-shadow 0.2s, transform 0.2s;
      box-shadow: var(--adm-shadow);
      animation: rise 0.3s ease both;
    }

    .stat-card:hover {
      transform: translateY(-1px);
      box-shadow: var(--adm-shadow-hover);
    }

    .stat-card.primary { border-left-color: var(--adm-indigo); }
    .stat-card.success { border-left-color: var(--adm-green); }
    .stat-card.info { border-left-color: var(--adm-cyan); }
    .stat-card.warning { border-left-color: var(--adm-amber); }

    .stat-icon {
      width: 52px;
      height: 52px;
      border-radius: 14px;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }

    .stat-card.primary .stat-icon {
      background: rgba(79, 70, 229, 0.10);
      color: var(--adm-indigo);
    }

    .stat-card.success .stat-icon {
      background: rgba(5, 150, 105, 0.10);
      color: var(--adm-green);
    }

    .stat-card.info .stat-icon {
      background: rgba(8, 145, 178, 0.10);
      color: var(--adm-cyan);
    }

    .stat-card.warning .stat-icon {
      background: rgba(217, 119, 6, 0.10);
      color: var(--adm-amber);
    }

    .stat-content {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .stat-value {
      font-size: 26px;
      font-weight: 800;
      font-variant-numeric: tabular-nums;
      color: var(--adm-ink);
    }

    .stat-label {
      font-size: 11px;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: var(--adm-sub);
    }

    .stat-change {
      font-size: 12px;
      color: var(--adm-sub);
    }

    .stat-change.positive {
      color: var(--adm-green-ink);
    }

    .dashboard-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      grid-template-rows: auto auto;
      gap: 20px;
    }

    .card {
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 16px;
      padding: 24px;
      box-shadow: var(--adm-shadow);
      animation: rise 0.3s ease both;
    }

    .card-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 20px;
    }

    .card-header h3 {
      font-size: 12px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      color: var(--adm-sub);
      margin: 0;
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .card-header h3::before {
      content: '';
      display: inline-block;
      width: 3px;
      height: 12px;
      border-radius: 2px;
      background: var(--adm-indigo);
      flex-shrink: 0;
    }

    .period-badge {
      font-size: 11px;
      font-weight: 700;
      color: var(--adm-slate-ink);
      padding: 4px 10px;
      background: rgba(100, 116, 139, 0.10);
      border-radius: 999px;
    }

    .refresh-btn {
      width: 32px;
      height: 32px;
      border: 1px solid var(--adm-border);
      background: #ffffff;
      border-radius: 10px;
      color: var(--adm-sub);
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }

    .refresh-btn:hover {
      border-color: var(--adm-indigo);
      color: var(--adm-indigo);
    }

    .view-all {
      font-size: 13px;
      font-weight: 600;
      color: var(--adm-indigo);
      text-decoration: none;
      transition: color 0.2s;
    }

    .view-all:hover {
      color: var(--adm-indigo-ink);
    }

    .feature-list {
      display: flex;
      flex-direction: column;
      gap: 16px;
    }

    .feature-item {
      display: grid;
      grid-template-columns: 1fr 1.5fr auto;
      align-items: center;
      gap: 16px;
    }

    .feature-info {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .feature-name {
      font-size: 14px;
      color: var(--adm-ink);
      font-weight: 500;
    }

    .feature-users {
      font-size: 12px;
      color: var(--adm-sub);
    }

    .feature-bar-container {
      height: 8px;
      background: var(--adm-track);
      border-radius: 4px;
      overflow: hidden;
    }

    .feature-bar {
      height: 100%;
      background: var(--adm-indigo);
      border-radius: 4px;
      transition: width 0.5s ease;
    }

    .feature-stats {
      display: flex;
      flex-direction: column;
      align-items: flex-end;
      gap: 2px;
      min-width: 70px;
    }

    .usage-count {
      font-size: 14px;
      font-weight: 600;
      font-variant-numeric: tabular-nums;
      color: var(--adm-ink);
    }

    .trend {
      font-size: 12px;
      font-weight: 600;
      display: flex;
      align-items: center;
      gap: 2px;
      color: var(--adm-sub);
    }

    .trend.positive {
      color: var(--adm-green-ink);
    }

    .trend.negative {
      color: var(--adm-red-ink);
    }

    .health-list {
      display: flex;
      flex-direction: column;
      gap: 12px;
      margin-bottom: 16px;
    }

    .health-item {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px;
      background: #f8fafc;
      border-radius: 10px;
    }

    .health-status {
      width: 12px;
      height: 12px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .status-dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
    }

    .health-status.healthy .status-dot {
      background: var(--adm-green);
      box-shadow: 0 0 8px rgba(5, 150, 105, 0.45);
    }

    .health-status.degraded .status-dot {
      background: var(--adm-amber);
      box-shadow: 0 0 8px rgba(217, 119, 6, 0.45);
    }

    .health-status.down .status-dot {
      background: var(--adm-red);
      box-shadow: 0 0 8px rgba(220, 38, 38, 0.45);
    }

    .health-info {
      flex: 1;
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .service-name {
      font-size: 14px;
      color: var(--adm-ink);
      font-weight: 500;
    }

    .service-uptime {
      font-size: 12px;
      color: var(--adm-sub);
    }

    .response-time {
      font-size: 13px;
      color: var(--adm-sub);
      font-weight: 500;
      font-variant-numeric: tabular-nums;
    }

    .health-summary {
      display: flex;
      gap: 16px;
      padding-top: 12px;
      border-top: 1px solid var(--adm-border);
    }

    .summary-item {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 13px;
      color: var(--adm-sub);
    }

    .dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
    }

    .dot.healthy { background: var(--adm-green); }
    .dot.degraded { background: var(--adm-amber); }
    .dot.down { background: var(--adm-red); }

    .activity-list {
      display: flex;
      flex-direction: column;
      gap: 12px;
    }

    .activity-item {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 0;
      border-bottom: 1px solid #eef2f7;
    }

    .activity-item:hover {
      background: #f8fafc;
    }

    .activity-item:last-child {
      border-bottom: none;
    }

    .activity-icon {
      width: 36px;
      height: 36px;
      border-radius: 10px;
      display: flex;
      align-items: center;
      justify-content: center;
      background: var(--adm-track);
      color: var(--adm-slate);
      flex-shrink: 0;
    }

    .activity-icon.login { background: rgba(5, 150, 105, 0.10); color: var(--adm-green-ink); }
    .activity-icon.logout { background: rgba(220, 38, 38, 0.10); color: var(--adm-red-ink); }
    .activity-icon.action { background: rgba(8, 145, 178, 0.10); color: var(--adm-cyan-ink); }

    .activity-content {
      flex: 1;
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .activity-user {
      font-size: 14px;
      color: var(--adm-ink);
      font-weight: 500;
    }

    .activity-action {
      font-size: 12px;
      color: var(--adm-sub);
    }

    .activity-company {
      font-size: 11px;
      color: var(--adm-sub);
    }

    .activity-time {
      font-size: 12px;
      color: var(--adm-sub);
      font-variant-numeric: tabular-nums;
    }

    .actions-grid {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 12px;
    }

    .action-btn {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 12px;
      padding: 20px;
      background: #ffffff;
      border: 1px solid var(--adm-border);
      border-radius: 12px;
      color: var(--adm-ink);
      font-size: 13px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .action-btn:hover {
      border-color: var(--adm-indigo);
      box-shadow: var(--adm-shadow-hover);
      transform: translateY(-1px);
    }

    .action-icon {
      width: 48px;
      height: 48px;
      border-radius: 12px;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .action-icon.add-client {
      background: rgba(79, 70, 229, 0.10);
      color: var(--adm-indigo);
    }

    .action-icon.create-estimate {
      background: rgba(8, 145, 178, 0.10);
      color: var(--adm-cyan);
    }

    .action-icon.maintenance {
      background: rgba(217, 119, 6, 0.10);
      color: var(--adm-amber);
    }

    .action-icon.health-check {
      background: rgba(5, 150, 105, 0.10);
      color: var(--adm-green);
    }

    @media (prefers-reduced-motion: reduce) {
      .stat-card,
      .card {
        animation: none;
      }
      .stat-card:hover,
      .action-btn:hover {
        transform: none;
      }
    }

    @media (max-width: 1400px) {
      .stats-grid {
        grid-template-columns: repeat(2, 1fr);
      }
    }

    @media (max-width: 1200px) {
      .dashboard-grid {
        grid-template-columns: 1fr;
      }
    }

    @media (max-width: 768px) {
      .stats-grid {
        grid-template-columns: 1fr;
      }
      .feature-item {
        grid-template-columns: 1fr;
        gap: 8px;
      }
    }
  `]
})
export class AdminDashboardComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  stats: DashboardStats | null = null;
  featureUsage: FeatureUsage[] = [];
  serviceHealth: ServiceHealth[] = [];
  recentActivity: ActivityLog[] = [];
  maxUsage = 0;

  constructor(
    private router: Router,
    private adminService: AdminService
  ) {}

  ngOnInit() {
    if (!this.adminService.isAuthenticated()) {
      this.router.navigate(['/admin/login']);
      return;
    }

    this.loadDashboard();
  }

  loadDashboard() {
    this.adminService.getDashboardStats().pipe(takeUntil(this.destroy$)).subscribe(stats => {
      this.stats = stats;
    });

    this.adminService.getFeatureUsage().pipe(takeUntil(this.destroy$)).subscribe(features => {
      this.featureUsage = features;
      this.maxUsage = Math.max(...features.map(f => f.usageCount));
    });

    this.loadHealth();

    this.adminService.getActivityLogs(10).pipe(takeUntil(this.destroy$)).subscribe(logs => {
      this.recentActivity = logs;
    });
  }

  loadHealth() {
    this.adminService.getServiceHealth().pipe(takeUntil(this.destroy$)).subscribe(health => {
      this.serviceHealth = health;
    });
  }

  formatNumber(num: number): string {
    if (num >= 1000000) {
      return (num / 1000000).toFixed(1) + 'M';
    }
    if (num >= 1000) {
      return (num / 1000).toFixed(1) + 'K';
    }
    return num.toString();
  }

  getFeaturePercentage(count: number): number {
    return this.maxUsage > 0 ? (count / this.maxUsage) * 100 : 0;
  }

  getHealthyCount(): number {
    return this.serviceHealth.filter(s => s.status === 'healthy').length;
  }

  getDegradedCount(): number {
    return this.serviceHealth.filter(s => s.status === 'degraded').length;
  }

  getDownCount(): number {
    return this.serviceHealth.filter(s => s.status === 'down').length;
  }

  getActivityIconClass(action: string): string {
    if (action === 'login') return 'login';
    if (action === 'logout') return 'logout';
    return 'action';
  }

  formatAction(action: string): string {
    return action.split('_').map(w => w.charAt(0).toUpperCase() + w.slice(1)).join(' ');
  }

  getTimeAgo(date: Date): string {
    const now = new Date();
    const diff = now.getTime() - new Date(date).getTime();
    const minutes = Math.floor(diff / 60000);
    const hours = Math.floor(minutes / 60);
    const days = Math.floor(hours / 24);

    if (minutes < 1) return 'Just now';
    if (minutes < 60) return `${minutes}m ago`;
    if (hours < 24) return `${hours}h ago`;
    return `${days}d ago`;
  }

  navigateTo(path: string) {
    this.router.navigate([path]);
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
