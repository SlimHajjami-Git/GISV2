import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { ApiService } from '../services/api.service';
import { GPSLocation, GPSAlert, Vehicle, Company } from '../models/types';
import { AppLayoutComponent } from './shared/app-layout.component';
import { CardComponent } from './shared/ui';
import { interval, Subscription } from 'rxjs';

@Component({
  selector: 'app-gps',
  standalone: true,
  imports: [CommonModule, AppLayoutComponent, CardComponent],
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
      background: #f1f5f9;
      display: flex;
      flex-direction: column;
      min-height: calc(100vh - 42px);
    }

    .gps-layout {
      display: grid;
      grid-template-columns: 1fr 280px;
      gap: 1px;
      background: #e2e8f0;
      flex: 1;
    }

    .map-container {
      background: white;
      overflow: hidden;
      height: 100%;
    }

    .map-placeholder {
      width: 100%;
      height: 100%;
      background: linear-gradient(135deg, #3b82f6 0%, #1d4ed8 100%);
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
      font-size: 16px;
      font-weight: 600;
      margin: 0 0 6px;
    }

    .map-overlay p {
      font-size: 12px;
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
    .marker-name { font-weight: 600; color: #1e293b; font-size: 12px; }
    .marker-speed { font-size: 11px; color: #64748b; }

    .gps-sidebar {
      display: flex;
      flex-direction: column;
      background: white;
      overflow-y: auto;
    }

    .card {
      background: white;
      border-bottom: 1px solid #f1f5f9;
    }

    .card:last-child {
      border-bottom: none;
    }

    .card-header {
      padding: 10px 14px;
      border-bottom: 1px solid #f1f5f9;
    }

    .card-header h3 {
      font-size: 12px;
      font-weight: 600;
      color: #1e293b;
      margin: 0;
    }

    .vehicles-list {
      padding: 10px;
      display: flex;
      flex-direction: column;
      gap: 6px;
    }

    .vehicle-item {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 8px 10px;
      background: #f8fafc;
      border-radius: 3px;
      border: 1px solid #e2e8f0;
    }

    .vehicle-icon { font-size: 18px; }
    .vehicle-info { flex: 1; }
    .vehicle-name { font-weight: 600; color: #1e293b; font-size: 12px; }
    .vehicle-location { font-size: 11px; color: #64748b; }
    .vehicle-speed { text-align: center; }
    .speed-value { font-size: 14px; font-weight: 700; color: #3b82f6; }
    .speed-label { font-size: 9px; color: #94a3b8; }

    .alerts-list {
      padding: 10px;
      display: flex;
      flex-direction: column;
      gap: 6px;
    }

    .alert-item {
      display: flex;
      gap: 10px;
      padding: 8px 10px;
      background: rgba(239, 68, 68, 0.05);
      border-radius: 3px;
      border-left: 3px solid #ef4444;
    }

    .alert-icon { font-size: 16px; }
    .alert-content { flex: 1; }
    .alert-vehicle { font-weight: 600; color: #1e293b; font-size: 12px; }
    .alert-message { font-size: 11px; color: #64748b; margin: 2px 0; }
    .alert-time { font-size: 10px; color: #94a3b8; }
    .empty-state { text-align: center; padding: 20px; color: #94a3b8; font-size: 12px; }

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
export class GpsComponent implements OnInit, OnDestroy {
  locations: any[] = [];
  alerts: GPSAlert[] = [];
  gpsVehicles: Vehicle[] = [];
  vehicles: Vehicle[] = [];
  company: Company | null = null;
  private refreshSubscription?: Subscription;
  isLoading = true;
  lastUpdate: Date = new Date();

  constructor(
    private router: Router,
    private apiService: ApiService
  ) {}

  ngOnInit() {
    if (!this.apiService.isAuthenticated()) {
      this.router.navigate(['/login']);
      return;
    }

    this.loadData();
    
    // Auto-refresh every 10 seconds for real-time updates
    this.refreshSubscription = interval(10000).subscribe(() => {
      this.loadLocations();
    });
  }

  ngOnDestroy() {
    if (this.refreshSubscription) {
      this.refreshSubscription.unsubscribe();
    }
  }

  loadData() {
    this.isLoading = true;
    this.apiService.getVehicles().subscribe({
      next: (vehicles) => {
        this.vehicles = vehicles;
        this.gpsVehicles = this.vehicles.filter(v => v.hasGPS);
        this.loadLocations();
      },
      error: (err) => {
        console.error('Error loading vehicles:', err);
        this.isLoading = false;
      }
    });

    this.apiService.getAlerts(false).subscribe({
      next: (alerts) => this.alerts = alerts.filter(a => !a.resolved),
      error: (err) => console.error('Error loading alerts:', err)
    });
  }

  loadLocations() {
    this.apiService.getLatestPositions().subscribe({
      next: (positions) => {
        this.locations = positions.map(p => ({
          vehicleId: p.vehicleId,
          vehicleName: p.vehicleName,
          plate: p.plate,
          latitude: p.lastPosition?.latitude,
          longitude: p.lastPosition?.longitude,
          speed: p.lastPosition?.speedKph || 0,
          address: p.lastPosition?.address || 'Position GPS',
          ignitionOn: p.lastPosition?.ignitionOn,
          recordedAt: p.lastPosition?.recordedAt,
          isOnline: p.lastCommunication && 
            (new Date().getTime() - new Date(p.lastCommunication).getTime()) < 300000
        }));
        this.lastUpdate = new Date();
        this.isLoading = false;
      },
      error: (err) => {
        console.error('Error loading GPS positions:', err);
        this.isLoading = false;
      }
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
    this.apiService.logout();
    this.router.navigate(['/']);
  }
}
