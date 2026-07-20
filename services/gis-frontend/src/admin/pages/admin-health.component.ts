import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
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
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-left-width: 3px;
      border-radius: 16px;
      box-shadow: var(--adm-shadow);
      padding: 32px;
      display: flex;
      align-items: center;
      gap: 32px;
      animation: rise .35s ease backwards;
    }

    .overview-card.healthy {
      border-left-color: var(--adm-green);
    }

    .overview-card.degraded {
      border-left-color: var(--adm-amber);
    }

    .overview-card.down {
      border-left-color: var(--adm-red);
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
      stroke: var(--adm-track);
      stroke-width: 8;
    }

    .ring-progress {
      fill: none;
      stroke: var(--adm-green);
      stroke-width: 8;
      stroke-linecap: round;
      transition: stroke-dasharray 0.5s ease;
    }

    .overview-card.degraded .ring-progress { stroke: var(--adm-amber); }
    .overview-card.down .ring-progress { stroke: var(--adm-red); }

    .uptime-value {
      position: absolute;
      top: 50%;
      left: 50%;
      transform: translate(-50%, -50%);
      font-size: 24px;
      font-weight: 800;
      font-variant-numeric: tabular-nums;
      color: var(--adm-ink);
    }

    .overview-info {
      flex: 1;
    }

    .overview-info h2 {
      margin: 0 0 8px 0;
      font-size: 22px;
      font-weight: 600;
      color: var(--adm-ink);
    }

    .overview-info h2 span.healthy { color: var(--adm-green-ink); }
    .overview-info h2 span.degraded { color: var(--adm-amber-ink); }
    .overview-info h2 span.down { color: var(--adm-red-ink); }

    .overview-info p {
      margin: 0 0 20px 0;
      font-size: 14px;
      color: var(--adm-sub);
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
      font-size: 26px;
      font-weight: 800;
      font-variant-numeric: tabular-nums;
    }

    .o-stat .count.healthy { color: var(--adm-green-ink); }
    .o-stat .count.degraded { color: var(--adm-amber-ink); }
    .o-stat .count.down { color: var(--adm-red-ink); }

    .o-stat .label {
      font-size: 11px;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: .06em;
      color: var(--adm-sub);
    }

    .refresh-info {
      display: flex;
      flex-direction: column;
      align-items: flex-end;
      gap: 12px;
    }

    .refresh-info span {
      font-size: 12px;
      color: var(--adm-sub);
    }

    .refresh-btn {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 9px 18px;
      background: var(--adm-indigo);
      border: none;
      border-radius: 10px;
      color: #fff;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .refresh-btn:hover {
      background: var(--adm-indigo-ink);
    }

    .services-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
      gap: 20px;
    }

    .service-card {
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 16px;
      box-shadow: var(--adm-shadow);
      padding: 24px;
      transition: transform 0.2s, box-shadow 0.2s;
      animation: rise .35s ease backwards;
    }

    .service-card:hover {
      transform: translateY(-1px);
      box-shadow: var(--adm-shadow-hover);
    }

    .service-card.healthy { border-left: 3px solid var(--adm-green); }
    .service-card.degraded { border-left: 3px solid var(--adm-amber); }
    .service-card.down { border-left: 3px solid var(--adm-red); }

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
      background: var(--adm-green);
      box-shadow: 0 0 8px rgba(5, 150, 105, 0.45);
    }

    .status-dot.degraded {
      background: var(--adm-amber);
      box-shadow: 0 0 8px rgba(217, 119, 6, 0.45);
    }

    .status-dot.down {
      background: var(--adm-red);
      box-shadow: 0 0 8px rgba(220, 38, 38, 0.45);
    }

    .status-text {
      font-size: 13px;
      font-weight: 500;
      color: var(--adm-sub);
    }

    .service-icon {
      width: 44px;
      height: 44px;
      background: var(--adm-track);
      border-radius: 12px;
      display: flex;
      align-items: center;
      justify-content: center;
      color: var(--adm-slate-ink);
    }

    .service-name {
      margin: 0 0 20px 0;
      font-size: 18px;
      font-weight: 600;
      color: var(--adm-ink);
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
      font-size: 11px;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: .06em;
      color: var(--adm-sub);
    }

    .metric-value {
      font-size: 18px;
      font-weight: 700;
      font-variant-numeric: tabular-nums;
      color: var(--adm-ink);
    }

    .metric-value.good { color: var(--adm-green-ink); }
    .metric-value.warning { color: var(--adm-amber-ink); }
    .metric-value.bad { color: var(--adm-red-ink); }

    .uptime-bar {
      height: 6px;
      background: var(--adm-track);
      border-radius: 3px;
      overflow: hidden;
      margin-bottom: 16px;
    }

    .uptime-fill {
      height: 100%;
      border-radius: 3px;
      transition: width 0.5s ease;
    }

    .uptime-fill.healthy { background: var(--adm-green); }
    .uptime-fill.degraded { background: var(--adm-amber); }
    .uptime-fill.down { background: var(--adm-red); }

    .service-details {
      padding-top: 16px;
      border-top: 1px solid var(--adm-border);
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
      color: var(--adm-sub);
      text-transform: capitalize;
    }

    .detail-value {
      color: var(--adm-ink);
      font-weight: 500;
      font-variant-numeric: tabular-nums;
    }

    .health-footer {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 20px 24px;
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 16px;
      box-shadow: var(--adm-shadow);
      animation: rise .35s ease backwards;
    }

    .toggle-label {
      display: flex;
      align-items: center;
      gap: 12px;
      font-size: 14px;
      color: var(--adm-sub);
      cursor: pointer;
    }

    .toggle-label input {
      display: none;
    }

    .toggle-switch {
      width: 44px;
      height: 24px;
      background: var(--adm-track);
      border: 1px solid var(--adm-border);
      border-radius: 12px;
      position: relative;
      transition: all 0.3s;
    }

    .toggle-switch::after {
      content: '';
      position: absolute;
      width: 18px;
      height: 18px;
      background: #fff;
      box-shadow: 0 1px 3px rgba(2, 6, 23, 0.25);
      border-radius: 50%;
      top: 2px;
      left: 3px;
      transition: all 0.3s;
    }

    .toggle-label input:checked + .toggle-switch {
      background: var(--adm-indigo);
      border-color: var(--adm-indigo);
    }

    .toggle-label input:checked + .toggle-switch::after {
      left: 22px;
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
      color: var(--adm-sub);
    }

    .legend .dot {
      width: 10px;
      height: 10px;
      border-radius: 50%;
    }

    .legend .dot.healthy { background: var(--adm-green); }
    .legend .dot.degraded { background: var(--adm-amber); }
    .legend .dot.down { background: var(--adm-red); }

    @keyframes rise {
      from { opacity: 0; transform: translateY(8px); }
      to { opacity: 1; transform: none; }
    }

    @media (prefers-reduced-motion: reduce) {
      .overview-card, .service-card, .health-footer { animation: none; }
    }

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
  private destroy$ = new Subject<void>();
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
    this.destroy$.next();
    this.destroy$.complete();
    if (this.refreshInterval) {
      clearInterval(this.refreshInterval);
    }
  }

  loadHealth() {
    this.adminService.getServiceHealth().pipe(takeUntil(this.destroy$)).subscribe(services => {
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
