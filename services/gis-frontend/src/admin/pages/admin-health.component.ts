import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService, ServiceHealth } from '../services/admin.service';

@Component({
  selector: 'admin-health',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent],
  template: `
    <admin-layout pageTitle="System Health">
      <div class="health-page">
        <div class="health-overview">
          <div class="overview-card" [class]="overallStatus">
            <div class="status-visual">
              <div class="status-ring">
                <svg viewBox="0 0 100 100">
                  <circle class="ring-bg" cx="50" cy="50" r="45"/>
                  <circle class="ring-progress" cx="50" cy="50" r="45" [style.stroke-dasharray]="uptimeCircle"/>
                </svg>
                <span class="uptime-value">{{ averageUptime.toFixed(1) }}%</span>
              </div>
            </div>
            <div class="overview-info">
              <h2>System Status: <span [class]="overallStatus">{{ overallStatus | titlecase }}</span></h2>
              <p>All services are being monitored in real-time</p>
              <div class="overview-stats">
                <div class="o-stat">
                  <span class="count healthy">{{ healthyCount }}</span>
                  <span class="label">Healthy</span>
                </div>
                <div class="o-stat">
                  <span class="count degraded">{{ degradedCount }}</span>
                  <span class="label">Degraded</span>
                </div>
                <div class="o-stat">
                  <span class="count down">{{ downCount }}</span>
                  <span class="label">Down</span>
                </div>
              </div>
            </div>
            <div class="refresh-info">
              <span>Last checked: {{ lastCheck }}</span>
              <button class="refresh-btn" (click)="loadHealth()">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <polyline points="23,4 23,10 17,10"/>
                  <polyline points="1,20 1,14 7,14"/>
                  <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"/>
                </svg>
                Refresh
              </button>
            </div>
          </div>
        </div>

        <div class="services-grid">
          <div class="service-card" *ngFor="let service of services" [class]="service.status">
            <div class="service-header">
              <div class="service-status">
                <span class="status-dot" [class]="service.status"></span>
                <span class="status-text">{{ service.status | titlecase }}</span>
              </div>
              <div class="service-icon">
                <svg *ngIf="service.name === 'GIS API'" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M18 20V10"/><path d="M12 20V4"/><path d="M6 20v-6"/>
                </svg>
                <svg *ngIf="service.name === 'GPS Ingest Service'" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="12" cy="12" r="10"/><polygon points="16.24,7.76 14.12,14.12 7.76,16.24 9.88,9.88"/>
                </svg>
                <svg *ngIf="service.name === 'PostgreSQL'" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <ellipse cx="12" cy="5" rx="9" ry="3"/><path d="M21 12c0 1.66-4 3-9 3s-9-1.34-9-3"/><path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5"/>
                </svg>
                <svg *ngIf="service.name === 'RabbitMQ'" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/><rect x="14" y="14" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/>
                </svg>
                <svg *ngIf="service.name === 'Frontend'" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <rect x="3" y="3" width="18" height="18" rx="2" ry="2"/><line x1="3" y1="9" x2="21" y2="9"/><line x1="9" y1="21" x2="9" y2="9"/>
                </svg>
              </div>
            </div>

            <h3 class="service-name">{{ service.name }}</h3>

            <div class="service-metrics">
              <div class="metric">
                <span class="metric-label">Response Time</span>
                <span class="metric-value" [class.good]="service.responseTime < 100" [class.warning]="service.responseTime >= 100 && service.responseTime < 500" [class.bad]="service.responseTime >= 500">
                  {{ service.responseTime }}ms
                </span>
              </div>
              <div class="metric">
                <span class="metric-label">Uptime</span>
                <span class="metric-value">{{ service.uptime }}%</span>
              </div>
            </div>

            <div class="uptime-bar">
              <div class="uptime-fill" [style.width.%]="service.uptime" [class]="service.status"></div>
            </div>

            <div class="service-details" *ngIf="service.details">
              <div class="detail-item" *ngFor="let item of getDetailsArray(service.details)">
                <span class="detail-key">{{ item.key }}</span>
                <span class="detail-value">{{ item.value }}</span>
              </div>
            </div>
          </div>
        </div>

        <div class="health-footer">
          <div class="auto-refresh">
            <label class="toggle-label">
              <input type="checkbox" [(ngModel)]="autoRefresh" (change)="toggleAutoRefresh()" />
              <span class="toggle-switch"></span>
              Auto-refresh every 30s
            </label>
          </div>
          <div class="legend">
            <span class="legend-item"><span class="dot healthy"></span> Healthy (&lt;100ms)</span>
            <span class="legend-item"><span class="dot degraded"></span> Degraded (100-500ms)</span>
            <span class="legend-item"><span class="dot down"></span> Down</span>
          </div>
        </div>
      </div>
    </admin-layout>
  `,
  styles: [`
    .health-page {
      display: flex;
      flex-direction: column;
      gap: 24px;
    }

    .health-overview {
      margin-bottom: 8px;
    }

    .overview-card {
      background: linear-gradient(135deg, rgba(26, 31, 46, 0.8) 0%, rgba(20, 24, 36, 0.9) 100%);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 20px;
      padding: 32px;
      display: flex;
      align-items: center;
      gap: 32px;
    }

    .overview-card.healthy {
      border-color: rgba(34, 197, 94, 0.3);
    }

    .overview-card.degraded {
      border-color: rgba(249, 115, 22, 0.3);
    }

    .overview-card.down {
      border-color: rgba(239, 68, 68, 0.3);
    }

    .status-visual {
      flex-shrink: 0;
    }

    .status-ring {
      width: 120px;
      height: 120px;
      position: relative;
    }

    .status-ring svg {
      transform: rotate(-90deg);
      width: 100%;
      height: 100%;
    }

    .ring-bg {
      fill: none;
      stroke: rgba(255, 255, 255, 0.1);
      stroke-width: 8;
    }

    .ring-progress {
      fill: none;
      stroke: #22c55e;
      stroke-width: 8;
      stroke-linecap: round;
      transition: stroke-dasharray 0.5s ease;
    }

    .overview-card.degraded .ring-progress { stroke: #f97316; }
    .overview-card.down .ring-progress { stroke: #ef4444; }

    .uptime-value {
      position: absolute;
      top: 50%;
      left: 50%;
      transform: translate(-50%, -50%);
      font-size: 24px;
      font-weight: 700;
      color: #e7e9ea;
    }

    .overview-info {
      flex: 1;
    }

    .overview-info h2 {
      margin: 0 0 8px 0;
      font-size: 22px;
      font-weight: 600;
      color: #e7e9ea;
    }

    .overview-info h2 span.healthy { color: #22c55e; }
    .overview-info h2 span.degraded { color: #f97316; }
    .overview-info h2 span.down { color: #ef4444; }

    .overview-info p {
      margin: 0 0 20px 0;
      font-size: 14px;
      color: #8b98a5;
    }

    .overview-stats {
      display: flex;
      gap: 32px;
    }

    .o-stat {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .o-stat .count {
      font-size: 28px;
      font-weight: 700;
    }

    .o-stat .count.healthy { color: #22c55e; }
    .o-stat .count.degraded { color: #f97316; }
    .o-stat .count.down { color: #ef4444; }

    .o-stat .label {
      font-size: 13px;
      color: #8b98a5;
    }

    .refresh-info {
      display: flex;
      flex-direction: column;
      align-items: flex-end;
      gap: 12px;
    }

    .refresh-info span {
      font-size: 12px;
      color: #6b7280;
    }

    .refresh-btn {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 18px;
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 10px;
      color: #e7e9ea;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.2s;
    }

    .refresh-btn:hover {
      background: rgba(255, 255, 255, 0.1);
    }

    .services-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
      gap: 20px;
    }

    .service-card {
      background: linear-gradient(135deg, rgba(26, 31, 46, 0.8) 0%, rgba(20, 24, 36, 0.9) 100%);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 16px;
      padding: 24px;
      transition: all 0.3s;
    }

    .service-card:hover {
      transform: translateY(-2px);
      border-color: rgba(255, 255, 255, 0.15);
    }

    .service-card.healthy { border-left: 3px solid #22c55e; }
    .service-card.degraded { border-left: 3px solid #f97316; }
    .service-card.down { border-left: 3px solid #ef4444; }

    .service-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 16px;
    }

    .service-status {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .status-dot {
      width: 10px;
      height: 10px;
      border-radius: 50%;
    }

    .status-dot.healthy {
      background: #22c55e;
      box-shadow: 0 0 10px rgba(34, 197, 94, 0.5);
    }

    .status-dot.degraded {
      background: #f97316;
      box-shadow: 0 0 10px rgba(249, 115, 22, 0.5);
    }

    .status-dot.down {
      background: #ef4444;
      box-shadow: 0 0 10px rgba(239, 68, 68, 0.5);
    }

    .status-text {
      font-size: 13px;
      font-weight: 500;
      color: #8b98a5;
    }

    .service-icon {
      width: 44px;
      height: 44px;
      background: rgba(255, 255, 255, 0.05);
      border-radius: 12px;
      display: flex;
      align-items: center;
      justify-content: center;
      color: #8b98a5;
    }

    .service-name {
      margin: 0 0 20px 0;
      font-size: 18px;
      font-weight: 600;
      color: #e7e9ea;
    }

    .service-metrics {
      display: flex;
      gap: 24px;
      margin-bottom: 16px;
    }

    .metric {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .metric-label {
      font-size: 12px;
      color: #6b7280;
    }

    .metric-value {
      font-size: 18px;
      font-weight: 600;
      color: #e7e9ea;
    }

    .metric-value.good { color: #22c55e; }
    .metric-value.warning { color: #f97316; }
    .metric-value.bad { color: #ef4444; }

    .uptime-bar {
      height: 6px;
      background: rgba(255, 255, 255, 0.1);
      border-radius: 3px;
      overflow: hidden;
      margin-bottom: 16px;
    }

    .uptime-fill {
      height: 100%;
      border-radius: 3px;
      transition: width 0.5s ease;
    }

    .uptime-fill.healthy { background: #22c55e; }
    .uptime-fill.degraded { background: #f97316; }
    .uptime-fill.down { background: #ef4444; }

    .service-details {
      padding-top: 16px;
      border-top: 1px solid rgba(255, 255, 255, 0.05);
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .detail-item {
      display: flex;
      justify-content: space-between;
      font-size: 13px;
    }

    .detail-key {
      color: #6b7280;
      text-transform: capitalize;
    }

    .detail-value {
      color: #e7e9ea;
      font-weight: 500;
    }

    .health-footer {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 20px 24px;
      background: linear-gradient(135deg, rgba(26, 31, 46, 0.6) 0%, rgba(20, 24, 36, 0.7) 100%);
      border: 1px solid rgba(255, 255, 255, 0.06);
      border-radius: 14px;
    }

    .toggle-label {
      display: flex;
      align-items: center;
      gap: 12px;
      font-size: 14px;
      color: #8b98a5;
      cursor: pointer;
    }

    .toggle-label input {
      display: none;
    }

    .toggle-switch {
      width: 44px;
      height: 24px;
      background: rgba(255, 255, 255, 0.1);
      border-radius: 12px;
      position: relative;
      transition: all 0.3s;
    }

    .toggle-switch::after {
      content: '';
      position: absolute;
      width: 18px;
      height: 18px;
      background: #8b98a5;
      border-radius: 50%;
      top: 3px;
      left: 3px;
      transition: all 0.3s;
    }

    .toggle-label input:checked + .toggle-switch {
      background: #00d4aa;
    }

    .toggle-label input:checked + .toggle-switch::after {
      left: 23px;
      background: #fff;
    }

    .legend {
      display: flex;
      gap: 20px;
    }

    .legend-item {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 13px;
      color: #8b98a5;
    }

    .legend .dot {
      width: 10px;
      height: 10px;
      border-radius: 50%;
    }

    .legend .dot.healthy { background: #22c55e; }
    .legend .dot.degraded { background: #f97316; }
    .legend .dot.down { background: #ef4444; }

    @media (max-width: 768px) {
      .overview-card {
        flex-direction: column;
        text-align: center;
      }
      .overview-stats {
        justify-content: center;
      }
      .refresh-info {
        align-items: center;
      }
      .health-footer {
        flex-direction: column;
        gap: 16px;
      }
    }
  `]
})
export class AdminHealthComponent implements OnInit, OnDestroy {
  services: ServiceHealth[] = [];
  autoRefresh = true;
  refreshInterval: any;
  lastCheck = '';

  get healthyCount(): number { return this.services.filter(s => s.status === 'healthy').length; }
  get degradedCount(): number { return this.services.filter(s => s.status === 'degraded').length; }
  get downCount(): number { return this.services.filter(s => s.status === 'down').length; }

  get overallStatus(): string {
    if (this.downCount > 0) return 'down';
    if (this.degradedCount > 0) return 'degraded';
    return 'healthy';
  }

  get averageUptime(): number {
    if (this.services.length === 0) return 0;
    return this.services.reduce((sum, s) => sum + s.uptime, 0) / this.services.length;
  }

  get uptimeCircle(): string {
    const circumference = 2 * Math.PI * 45;
    const offset = circumference - (this.averageUptime / 100) * circumference;
    return `${circumference - offset} ${circumference}`;
  }

  constructor(
    private router: Router,
    private adminService: AdminService
  ) {}

  ngOnInit() {
    if (!this.adminService.isAuthenticated()) {
      this.router.navigate(['/admin/login']);
      return;
    }
    this.loadHealth();
    this.toggleAutoRefresh();
  }

  ngOnDestroy() {
    if (this.refreshInterval) {
      clearInterval(this.refreshInterval);
    }
  }

  loadHealth() {
    this.adminService.getServiceHealth().subscribe(services => {
      this.services = services;
      this.lastCheck = new Date().toLocaleTimeString();
    });
  }

  toggleAutoRefresh() {
    if (this.refreshInterval) {
      clearInterval(this.refreshInterval);
      this.refreshInterval = null;
    }
    if (this.autoRefresh) {
      this.refreshInterval = setInterval(() => this.loadHealth(), 30000);
    }
  }

  getDetailsArray(details: Record<string, any>): { key: string; value: any }[] {
    return Object.entries(details).map(([key, value]) => ({ key, value }));
  }
}
