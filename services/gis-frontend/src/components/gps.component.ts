import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MockDataService } from '../services/mock-data.service';
import { GPSLocation, GPSAlert, Vehicle, Company } from '../models/types';
import { AppLayoutComponent } from './shared/app-layout.component';

@Component({
  selector: 'app-gps',
  standalone: true,
  imports: [CommonModule, AppLayoutComponent],
  template: `
    <app-layout>
      <div class="gps-page">
        <div class="gps-layout">
          <div class="map-container">
            <div class="map-placeholder">
              <div class="map-overlay">
                <h3>Carte interactive GPS</h3>
                <p>Visualisez la position de tous vos véhicules en temps réel</p>
                <div class="map-markers">
                  <div class="marker" *ngFor="let location of locations">
                    <div class="marker-icon">📍</div>
                    <div class="marker-info">
                      <div class="marker-name">{{ getVehicleName(location.vehicleId) }}</div>
                      <div class="marker-speed">{{ location.speed }} km/h</div>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <div class="gps-sidebar">
            <div class="card">
              <div class="card-header">
                <h3>Véhicules en ligne</h3>
              </div>
              <div class="vehicles-list">
                <div class="vehicle-item" *ngFor="let location of locations">
                  <div class="vehicle-icon">🚗</div>
                  <div class="vehicle-info">
                    <div class="vehicle-name">{{ getVehicleName(location.vehicleId) }}</div>
                    <div class="vehicle-location">{{ location.address }}</div>
                  </div>
                  <div class="vehicle-speed">
                    <div class="speed-value">{{ location.speed }}</div>
                    <div class="speed-label">km/h</div>
                  </div>
                </div>
              </div>
            </div>

            <div class="card">
              <div class="card-header">
                <h3>Alertes récentes</h3>
              </div>
              <div class="alerts-list">
                <div class="alert-item" *ngFor="let alert of alerts" [class]="alert.type">
                  <div class="alert-icon">⚠️</div>
                  <div class="alert-content">
                    <div class="alert-vehicle">{{ getVehicleName(alert.vehicleId) }}</div>
                    <div class="alert-message">{{ alert.message }}</div>
                    <div class="alert-time">{{ formatTime(alert.timestamp) }}</div>
                  </div>
                </div>
                <div class="empty-state" *ngIf="alerts.length === 0">
                  <p>Aucune alerte active</p>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    /* ===== GPS PAGE ===== */
    .gps-page {
      flex: 1;
      background: var(--bg-page);
    }

    .gps-layout {
      display: grid;
      grid-template-columns: 1fr 400px;
      gap: 24px;
      padding: 24px 32px;
    }
    .map-container {
      background: var(--bg-card);
      border-radius: 12px;
      overflow: hidden;
      box-shadow: var(--shadow-md);
      height: 600px;
      border: 1px solid var(--border-color);
    }

    .map-placeholder {
      width: 100%;
      height: 100%;
      background: linear-gradient(135deg, var(--primary) 0%, var(--primary-dark) 100%);
      position: relative;
    }

    .map-overlay {
      position: absolute;
      top: 50%;
      left: 50%;
      transform: translate(-50%, -50%);
      text-align: center;
      color: white;
      width: 100%;
    }

    .map-overlay h3 {
      font-size: 28px;
      font-weight: 700;
      margin: 0 0 8px;
    }

    .map-overlay p {
      font-size: 16px;
      margin: 0 0 24px;
      opacity: 0.9;
    }

    .map-markers {
      display: flex;
      justify-content: center;
      gap: 16px;
      flex-wrap: wrap;
      padding: 0 24px;
    }

    .marker {
      background: rgba(255, 255, 255, 0.95);
      border-radius: 12px;
      padding: 16px;
      display: flex;
      align-items: center;
      gap: 12px;
      min-width: 200px;
    }

    .marker-icon { font-size: 28px; }
    .marker-info { flex: 1; }
    .marker-name { font-weight: 600; color: var(--text-primary); }
    .marker-speed { font-size: 14px; color: var(--text-secondary); }

    .gps-sidebar {
      display: flex;
      flex-direction: column;
      gap: 24px;
    }

    .card {
      background: var(--bg-card);
      border-radius: 12px;
      border: 1px solid var(--border-color);
    }

    .card-header {
      padding: 20px;
      border-bottom: 1px solid var(--border-color);
    }

    .card-header h3 {
      font-size: 16px;
      font-weight: 600;
      color: var(--text-primary);
      margin: 0;
    }

    .vehicles-list {
      padding: 16px;
      display: flex;
      flex-direction: column;
      gap: 12px;
    }

    .vehicle-item {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px;
      background: var(--bg-secondary);
      border-radius: 8px;
    }

    .vehicle-icon { font-size: 24px; }
    .vehicle-info { flex: 1; }
    .vehicle-name { font-weight: 600; color: var(--text-primary); font-size: 14px; }
    .vehicle-location { font-size: 12px; color: var(--text-secondary); }
    .vehicle-speed { text-align: center; }
    .speed-value { font-size: 20px; font-weight: 700; color: var(--primary); }
    .speed-label { font-size: 10px; color: var(--text-muted); }

    .alerts-list {
      padding: 16px;
      display: flex;
      flex-direction: column;
      gap: 12px;
    }

    .alert-item {
      display: flex;
      gap: 12px;
      padding: 12px;
      background: rgba(239, 68, 68, 0.1);
      border-radius: 8px;
      border-left: 3px solid var(--danger);
    }

    .alert-icon { font-size: 20px; }
    .alert-content { flex: 1; }
    .alert-vehicle { font-weight: 600; color: var(--text-primary); font-size: 14px; }
    .alert-message { font-size: 12px; color: var(--text-secondary); margin: 4px 0; }
    .alert-time { font-size: 11px; color: var(--text-muted); }
    .empty-state { text-align: center; padding: 24px; color: var(--text-muted); }

    @media (max-width: 1024px) {
      .gps-layout {
        grid-template-columns: 1fr;
      }
    }

    @media (max-width: 768px) {
      .page-header {
        padding: 16px 20px;
      }

      .gps-layout {
        padding: 16px 20px;
      }
    }
  `]
})
export class GpsComponent implements OnInit {
  locations: GPSLocation[] = [];
  alerts: GPSAlert[] = [];
  gpsVehicles: Vehicle[] = [];
  vehicles: Vehicle[] = [];
  company: Company | null = null;

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
      this.gpsVehicles = this.vehicles.filter(v => v.hasGPS);
    }

    this.dataService.getGPSLocations().subscribe(locations => {
      this.locations = locations.filter(l =>
        this.gpsVehicles.some(v => v.id === l.vehicleId)
      );
    });

    this.dataService.getGPSAlerts().subscribe(alerts => {
      this.alerts = alerts.filter(a => !a.resolved);
    });
  }

  getVehicleName(vehicleId: string): string {
    const vehicle = this.vehicles.find(v => v.id === vehicleId);
    return vehicle ? vehicle.name : 'Véhicule inconnu';
  }

  formatTime(date: Date): string {
    const now = new Date();
    const diff = Math.floor((now.getTime() - new Date(date).getTime()) / 60000);
    if (diff < 60) return `Il y a ${diff} min`;
    if (diff < 1440) return `Il y a ${Math.floor(diff / 60)}h`;
    return `Il y a ${Math.floor(diff / 1440)}j`;
  }

  navigate(path: string) {
    this.router.navigate([path]);
  }

  logout() {
    this.dataService.logout();
    this.router.navigate(['/']);
  }
}
