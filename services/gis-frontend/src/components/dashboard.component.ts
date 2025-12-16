import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MockDataService } from '../services/mock-data.service';
import { Vehicle, Employee, GPSAlert, Company } from '../models/types';
import { AppLayoutComponent } from './shared/app-layout.component';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, AppLayoutComponent],
  template: `
    <app-layout>
      <div class="page-container">
        <div class="dashboard-content">
        <div class="stats-grid">
          <div class="stat-card">
            <div class="stat-header">
              <div>
                <div class="stat-label">Total Vehicles</div>
                <div class="stat-value">{{ vehicles.length }}</div>
              </div>
              <div class="stat-icon vehicles">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M5 17h-2v-6l2-5h9l4 5v6h-2"/>
                  <circle cx="7" cy="17" r="2"/>
                  <circle cx="17" cy="17" r="2"/>
                </svg>
              </div>
            </div>
            <div class="stat-footer">
              <span class="stat-badge positive">{{ activeVehicles }} active</span>
            </div>
          </div>

          <div class="stat-card">
            <div class="stat-header">
              <div>
                <div class="stat-label">Online</div>
                <div class="stat-value">{{ vehiclesWithGPS }}</div>
              </div>
              <div class="stat-icon online">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>
                </svg>
              </div>
            </div>
            <div class="stat-footer">
              <span class="stat-info">{{ getPercentage(vehiclesWithGPS, vehicles.length) }}% of fleet</span>
            </div>
          </div>

          <div class="stat-card">
            <div class="stat-header">
              <div>
                <div class="stat-label">Active Employees</div>
                <div class="stat-value">{{ activeEmployees }}</div>
              </div>
              <div class="stat-icon employees">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/>
                  <circle cx="9" cy="7" r="4"/>
                </svg>
              </div>
            </div>
            <div class="stat-footer">
              <span class="stat-info">of {{ employees.length}} total</span>
            </div>
          </div>

          <div class="stat-card">
            <div class="stat-header">
              <div>
                <div class="stat-label">Alerts</div>
                <div class="stat-value">{{ alerts.length }}</div>
              </div>
              <div class="stat-icon alerts">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="12" cy="12" r="10"/>
                  <line x1="12" y1="8" x2="12" y2="12"/>
                  <line x1="12" y1="16" x2="12.01" y2="16"/>
                </svg>
              </div>
            </div>
            <div class="stat-footer">
              <span class="stat-badge" [class.negative]="alerts.length > 0">
                {{ alerts.length > 0 ? 'Requires attention' : 'No alerts' }}
              </span>
            </div>
          </div>
        </div>

        <div class="grid-2">
          <div class="card">
            <div class="card-header">
              <h2>Recent Vehicles</h2>
              <button class="btn-link" (click)="navigate('/vehicles')">
                View all
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="m9 18 6-6-6-6"/>
                </svg>
              </button>
            </div>
            <div class="card-body">
              <div class="list">
                <div class="list-item" *ngFor="let vehicle of vehicles.slice(0, 5)">
                  <div class="list-icon" [class.online]="vehicle.hasGPS">
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M5 17h-2v-6l2-5h9l4 5v6h-2"/>
                      <circle cx="7" cy="17" r="2"/>
                      <circle cx="17" cy="17" r="2"/>
                    </svg>
                  </div>
                  <div class="list-content">
                    <div class="list-title">{{ vehicle.brand }} {{ vehicle.model }}</div>
                    <div class="list-subtitle">{{ vehicle.plate }} • {{ vehicle.name }}</div>
                  </div>
                  <span class="badge" [class]="vehicle.status">
                    {{ getStatusLabel(vehicle.status) }}
                  </span>
                </div>
              </div>
            </div>
          </div>

          <div class="card">
            <div class="card-header">
              <h2>Recent Alerts</h2>
              <button class="btn-link" (click)="navigate('/monitoring')">
                View all
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="m9 18 6-6-6-6"/>
                </svg>
              </button>
            </div>
            <div class="card-body">
              <div class="list" *ngIf="alerts.length > 0">
                <div class="alert-item" *ngFor="let alert of alerts.slice(0, 5)">
                  <div class="alert-icon">
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <circle cx="12" cy="12" r="10"/>
                      <line x1="12" y1="8" x2="12" y2="12"/>
                      <line x1="12" y1="16" x2="12.01" y2="16"/>
                    </svg>
                  </div>
                  <div class="alert-content">
                    <div class="alert-message">{{ alert.message }}</div>
                    <div class="alert-time">{{ formatTime(alert.timestamp) }}</div>
                  </div>
                </div>
              </div>
              <div class="empty-state" *ngIf="alerts.length === 0">
                <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                  <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                  <polyline points="22 4 12 14.01 9 11.01"/>
                </svg>
                <p>No active alerts</p>
                <span>All your vehicles are operating normally</span>
              </div>
            </div>
          </div>
        </div>

        <div class="card">
          <div class="card-header">
            <h2>Vehicle Types</h2>
          </div>
          <div class="card-body">
            <div class="types-grid">
              <div class="type-card" *ngFor="let type of vehicleTypes">
                <div class="type-icon">{{ type.icon }}</div>
                <div class="type-info">
                  <div class="type-count">{{ type.count }}</div>
                  <div class="type-name">{{ type.name }}</div>
                </div>
              </div>
            </div>
          </div>
        </div>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    .page-container {
      flex: 1;
      background: var(--bg-page);
    }

    .dashboard-content {
      padding: 24px 32px;
      max-width: 1600px;
      margin: 0 auto;
    }

    .btn-primary {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 20px;
      background: var(--primary);
      color: white;
      border: none;
      border-radius: 8px;
      font-weight: 600;
      font-size: 14px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-primary:hover {
      background: var(--primary-dark);
      transform: translateY(-1px);
      box-shadow: var(--shadow-md);
    }

    .stats-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
      gap: 24px;
      margin-bottom: 32px;
    }

    .stat-card {
      background: var(--bg-card);
      border-radius: 12px;
      padding: 24px;
      border: 1px solid var(--border-color);
      transition: all 0.2s;
    }

    .stat-card:hover {
      border-color: var(--border-dark);
      transform: translateY(-2px);
      box-shadow: var(--shadow-md);
    }

    .stat-header {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      margin-bottom: 16px;
    }

    .stat-label {
      font-size: 13px;
      color: var(--text-muted);
      font-weight: 500;
      margin-bottom: 8px;
    }

    .stat-value {
      font-size: 36px;
      font-weight: 700;
      color: var(--text-primary);
      line-height: 1;
    }

    .stat-icon {
      width: 48px;
      height: 48px;
      border-radius: 10px;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .stat-icon.vehicles {
      background: rgba(59, 130, 246, 0.1);
      color: var(--primary);
    }

    .stat-icon.online {
      background: rgba(16, 185, 129, 0.1);
      color: var(--success);
    }

    .stat-icon.employees {
      background: rgba(6, 182, 212, 0.1);
      color: var(--info);
    }

    .stat-icon.alerts {
      background: rgba(239, 68, 68, 0.1);
      color: var(--danger);
    }

    .stat-footer {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .stat-badge {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 4px 10px;
      border-radius: 6px;
      font-size: 13px;
      font-weight: 500;
      background: var(--bg-secondary);
      color: var(--text-secondary);
    }

    .stat-badge.positive {
      background: rgba(16, 185, 129, 0.1);
      color: var(--success);
    }

    .stat-badge.negative {
      background: rgba(239, 68, 68, 0.1);
      color: var(--danger);
    }

    .stat-info {
      font-size: 13px;
      color: var(--text-muted);
    }

    .grid-2 {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 24px;
      margin-bottom: 24px;
    }

    .card {
      background: var(--bg-card);
      border-radius: 12px;
      border: 1px solid var(--border-color);
      overflow: hidden;
    }

    .card-header {
      padding: 20px 24px;
      border-bottom: 1px solid var(--border-color);
      display: flex;
      justify-content: space-between;
      align-items: center;
    }

    .card-header h2 {
      font-size: 18px;
      font-weight: 600;
      color: var(--text-primary);
      margin: 0;
    }

    .btn-link {
      display: flex;
      align-items: center;
      gap: 4px;
      color: var(--primary);
      background: none;
      border: none;
      font-weight: 600;
      font-size: 13px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-link:hover {
      color: var(--primary-dark);
      gap: 8px;
    }

    .card-body {
      padding: 20px 24px;
    }

    .list {
      display: flex;
      flex-direction: column;
      gap: 12px;
    }

    .list-item {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px;
      background: var(--bg-secondary);
      border-radius: 8px;
      transition: all 0.2s;
    }

    .list-item:hover {
      background: var(--bg-hover);
      transform: translateX(4px);
    }

    .list-icon {
      width: 40px;
      height: 40px;
      border-radius: 8px;
      background: var(--bg-page);
      display: flex;
      align-items: center;
      justify-content: center;
      color: var(--text-muted);
      flex-shrink: 0;
    }

    .list-icon.online {
      background: rgba(16, 185, 129, 0.1);
      color: var(--success);
    }

    .list-content {
      flex: 1;
      min-width: 0;
    }

    .list-title {
      font-weight: 600;
      color: var(--text-primary);
      font-size: 14px;
      margin-bottom: 2px;
    }

    .list-subtitle {
      font-size: 13px;
      color: var(--text-secondary);
    }

    .badge {
      padding: 6px 12px;
      border-radius: 6px;
      font-size: 12px;
      font-weight: 600;
      white-space: nowrap;
    }

    .badge.available {
      background: rgba(16, 185, 129, 0.1);
      color: var(--success);
    }

    .badge.in_use {
      background: rgba(59, 130, 246, 0.1);
      color: var(--primary);
    }

    .badge.maintenance {
      background: rgba(245, 158, 11, 0.1);
      color: var(--warning);
    }

    .alert-item {
      display: flex;
      gap: 12px;
      padding: 12px;
      background: rgba(239, 68, 68, 0.05);
      border-radius: 8px;
      border-left: 3px solid var(--danger);
    }

    .alert-icon {
      width: 40px;
      height: 40px;
      border-radius: 8px;
      background: rgba(239, 68, 68, 0.1);
      color: var(--danger);
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }

    .alert-content {
      flex: 1;
    }

    .alert-message {
      font-weight: 600;
      color: var(--text-primary);
      font-size: 14px;
      margin-bottom: 4px;
    }

    .alert-time {
      font-size: 12px;
      color: var(--text-muted);
    }

    .empty-state {
      text-align: center;
      padding: 48px 24px;
      color: var(--text-muted);
    }

    .empty-state svg {
      margin-bottom: 16px;
      opacity: 0.5;
    }

    .empty-state p {
      font-size: 15px;
      font-weight: 600;
      color: var(--text-secondary);
      margin: 0 0 4px;
    }

    .empty-state span {
      font-size: 13px;
    }

    .types-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
      gap: 16px;
    }

    .type-card {
      padding: 20px;
      background: var(--bg-secondary);
      border-radius: 10px;
      display: flex;
      align-items: center;
      gap: 16px;
      transition: all 0.2s;
    }

    .type-card:hover {
      background: var(--bg-hover);
      transform: translateY(-2px);
    }

    .type-icon {
      font-size: 36px;
    }

    .type-info {
      flex: 1;
    }

    .type-count {
      font-size: 28px;
      font-weight: 700;
      color: var(--text-primary);
      line-height: 1;
      margin-bottom: 4px;
    }

    .type-name {
      font-size: 13px;
      color: var(--text-secondary);
      font-weight: 500;
    }

    @media (max-width: 1200px) {
      .grid-2 {
        grid-template-columns: 1fr;
      }
    }

    @media (max-width: 768px) {
      .stats-grid {
        grid-template-columns: repeat(2, 1fr);
      }

      .page-container {
        padding: 20px;
      }
    }
  `]
})
export class DashboardComponent implements OnInit {
  vehicles: Vehicle[] = [];
  employees: Employee[] = [];
  alerts: GPSAlert[] = [];
  company: Company | null = null;
  vehicleTypes: any[] = [];

  constructor(
    private router: Router,
    private dataService: MockDataService
  ) {}

  ngOnInit() {
    if (!this.dataService.isAuthenticated()) {
      this.router.navigate(['/login']);
      return;
    }

    this.company = this.dataService.getCurrentCompany();

    if (this.company) {
      this.vehicles = this.dataService.getVehiclesByCompany(this.company.id);
      this.employees = this.dataService.getEmployeesByCompany(this.company.id);
    }

    this.dataService.getGPSAlerts().subscribe(alerts => {
      this.alerts = alerts.filter(a => !a.resolved);
    });

    this.calculateVehicleTypes();
  }

  get activeVehicles(): number {
    return this.vehicles.filter(v => v.status === 'in_use').length;
  }

  get vehiclesWithGPS(): number {
    return this.vehicles.filter(v => v.hasGPS).length;
  }

  get activeEmployees(): number {
    return this.employees.filter(e => e.status === 'active').length;
  }

  getPercentage(value: number, total: number): number {
    return total > 0 ? Math.round((value / total) * 100) : 0;
  }

  getStatusLabel(status: string): string {
    const labels: any = {
      available: 'Available',
      in_use: 'In use',
      maintenance: 'Maintenance'
    };
    return labels[status] || status;
  }

  formatTime(date: Date): string {
    const now = new Date();
    const diff = Math.floor((now.getTime() - new Date(date).getTime()) / 60000);

    if (diff < 60) return `${diff} min ago`;
    if (diff < 1440) return `${Math.floor(diff / 60)}h ago`;
    return `${Math.floor(diff / 1440)}d ago`;
  }

  calculateVehicleTypes() {
    const typeIcons: any = {
      camion: '🚛',
      citadine: '🚗',
      suv: '🚙',
      utilitaire: '🚐',
      other: '🚗'
    };

    const typeNames: any = {
      camion: 'Trucks',
      citadine: 'Cars',
      suv: 'SUVs',
      utilitaire: 'Vans',
      other: 'Others'
    };

    const counts: any = {};
    this.vehicles.forEach(v => {
      counts[v.type] = (counts[v.type] || 0) + 1;
    });

    this.vehicleTypes = Object.keys(counts).map(type => ({
      type,
      name: typeNames[type] || type,
      icon: typeIcons[type] || '🚗',
      count: counts[type]
    }));
  }

  navigate(path: string) {
    this.router.navigate([path]);
  }
}
