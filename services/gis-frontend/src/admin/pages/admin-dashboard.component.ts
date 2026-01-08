import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
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

    .stats-grid {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 20px;
    }

    .stat-card {
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      padding: 24px;
      display: flex;
      align-items: flex-start;
      gap: 16px;
      transition: all 0.3s;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
    }

    .stat-card:hover {
      transform: translateY(-2px);
      border-color: #cbd5e1;
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
    }

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
      background: rgba(0, 212, 170, 0.15);
      color: #00d4aa;
    }

    .stat-card.success .stat-icon {
      background: rgba(34, 197, 94, 0.15);
      color: #22c55e;
    }

    .stat-card.info .stat-icon {
      background: rgba(59, 130, 246, 0.15);
      color: #3b82f6;
    }

    .stat-card.warning .stat-icon {
      background: rgba(249, 115, 22, 0.15);
      color: #f97316;
    }

    .stat-content {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .stat-value {
      font-size: 28px;
      font-weight: 700;
      color: #1f2937;
    }

    .stat-label {
      font-size: 14px;
      color: #64748b;
    }

    .stat-change {
      font-size: 12px;
      color: #6b7280;
    }

    .stat-change.positive {
      color: #22c55e;
    }

    .dashboard-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      grid-template-rows: auto auto;
      gap: 20px;
    }

    .card {
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      padding: 24px;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
    }

    .card-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 20px;
    }

    .card-header h3 {
      font-size: 16px;
      font-weight: 600;
      color: #1f2937;
      margin: 0;
    }

    .period-badge {
      font-size: 12px;
      color: #64748b;
      padding: 4px 10px;
      background: #f1f5f9;
      border-radius: 12px;
    }

    .refresh-btn {
      width: 32px;
      height: 32px;
      border: none;
      background: #f1f5f9;
      border-radius: 8px;
      color: #64748b;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }

    .refresh-btn:hover {
      background: #e2e8f0;
      color: #1f2937;
    }

    .view-all {
      font-size: 13px;
      color: #00d4aa;
      text-decoration: none;
      transition: color 0.2s;
    }

    .view-all:hover {
      color: #00e6b8;
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
      color: #1f2937;
      font-weight: 500;
    }

    .feature-users {
      font-size: 12px;
      color: #64748b;
    }

    .feature-bar-container {
      height: 8px;
      background: #e2e8f0;
      border-radius: 4px;
      overflow: hidden;
    }

    .feature-bar {
      height: 100%;
      background: linear-gradient(90deg, #00d4aa 0%, #00a388 100%);
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
      color: #1f2937;
    }

    .trend {
      font-size: 12px;
      display: flex;
      align-items: center;
      gap: 2px;
      color: #6b7280;
    }

    .trend.positive {
      color: #22c55e;
    }

    .trend.negative {
      color: #ef4444;
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
      background: #22c55e;
      box-shadow: 0 0 8px rgba(34, 197, 94, 0.5);
    }

    .health-status.degraded .status-dot {
      background: #f97316;
      box-shadow: 0 0 8px rgba(249, 115, 22, 0.5);
    }

    .health-status.down .status-dot {
      background: #ef4444;
      box-shadow: 0 0 8px rgba(239, 68, 68, 0.5);
    }

    .health-info {
      flex: 1;
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .service-name {
      font-size: 14px;
      color: #1f2937;
      font-weight: 500;
    }

    .service-uptime {
      font-size: 12px;
      color: #64748b;
    }

    .response-time {
      font-size: 13px;
      color: #64748b;
      font-weight: 500;
    }

    .health-summary {
      display: flex;
      gap: 16px;
      padding-top: 12px;
      border-top: 1px solid #e2e8f0;
    }

    .summary-item {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 13px;
      color: #64748b;
    }

    .dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
    }

    .dot.healthy { background: #22c55e; }
    .dot.degraded { background: #f97316; }
    .dot.down { background: #ef4444; }

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
      border-bottom: 1px solid rgba(255, 255, 255, 0.05);
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
      background: rgba(255, 255, 255, 0.05);
      color: #8b98a5;
    }

    .activity-icon.login { background: rgba(34, 197, 94, 0.15); color: #22c55e; }
    .activity-icon.logout { background: rgba(239, 68, 68, 0.15); color: #ef4444; }
    .activity-icon.action { background: rgba(59, 130, 246, 0.15); color: #3b82f6; }

    .activity-content {
      flex: 1;
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .activity-user {
      font-size: 14px;
      color: #e7e9ea;
      font-weight: 500;
    }

    .activity-action {
      font-size: 12px;
      color: #8b98a5;
    }

    .activity-company {
      font-size: 11px;
      color: #6b7280;
    }

    .activity-time {
      font-size: 12px;
      color: #6b7280;
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
      background: rgba(255, 255, 255, 0.03);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 12px;
      color: #e7e9ea;
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.2s;
    }

    .action-btn:hover {
      background: rgba(255, 255, 255, 0.06);
      border-color: rgba(255, 255, 255, 0.15);
      transform: translateY(-2px);
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
      background: rgba(0, 212, 170, 0.15);
      color: #00d4aa;
    }

    .action-icon.create-estimate {
      background: rgba(59, 130, 246, 0.15);
      color: #3b82f6;
    }

    .action-icon.maintenance {
      background: rgba(249, 115, 22, 0.15);
      color: #f97316;
    }

    .action-icon.health-check {
      background: rgba(34, 197, 94, 0.15);
      color: #22c55e;
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
export class AdminDashboardComponent implements OnInit {
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
    this.adminService.getDashboardStats().subscribe(stats => {
      this.stats = stats;
    });

    this.adminService.getFeatureUsage().subscribe(features => {
      this.featureUsage = features;
      this.maxUsage = Math.max(...features.map(f => f.usageCount));
    });

    this.loadHealth();

    this.adminService.getActivityLogs(10).subscribe(logs => {
      this.recentActivity = logs;
    });
  }

  loadHealth() {
    this.adminService.getServiceHealth().subscribe(health => {
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
}
