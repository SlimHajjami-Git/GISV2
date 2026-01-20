import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService, DailyActivityReport, ActivitySegment } from '../services/api.service';
import { AppLayoutComponent } from './shared/app-layout.component';

@Component({
  selector: 'app-daily-report',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent],
  template: `
    <app-layout>
      <div class="daily-report-page">
        <!-- Header -->
        <div class="page-header">
          <div class="header-content">
            <h1>📊 Rapport Journalier</h1>
            <p class="subtitle">Activité détaillée des véhicules sur 24 heures</p>
          </div>
        </div>

        <!-- Filters -->
        <div class="filters-bar">
          <div class="filter-group">
            <label>Date</label>
            <input type="date" [(ngModel)]="selectedDate" (change)="loadReports()" class="date-input">
          </div>
          <div class="filter-group">
            <label>Véhicule</label>
            <select [(ngModel)]="selectedVehicleId" (change)="loadReports()" class="select-input">
              <option value="">Tous les véhicules</option>
              @for (vehicle of vehicles; track vehicle.id) {
                <option [value]="vehicle.id">{{ vehicle.name }} {{ vehicle.plate ? '(' + vehicle.plate + ')' : '' }}</option>
              }
            </select>
          </div>
          <div class="filter-group">
            <label>Durée min. arrêt</label>
            <select [(ngModel)]="minStopDuration" (change)="loadReports()" class="select-input">
              <option [value]="60">1 minute</option>
              <option [value]="120">2 minutes</option>
              <option [value]="300">5 minutes</option>
              <option [value]="600">10 minutes</option>
            </select>
          </div>
          <button class="btn-refresh" (click)="loadReports()">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M23 4v6h-6M1 20v-6h6"/>
              <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"/>
            </svg>
            Actualiser
          </button>
        </div>

        <!-- Loading State -->
        @if (loading) {
          <div class="loading-container">
            <div class="spinner"></div>
            <p>Chargement des rapports...</p>
          </div>
        }

        <!-- Reports List -->
        @if (!loading && reports.length > 0) {
          <div class="reports-container">
            @for (report of reports; track report.vehicleId) {
              <div class="report-card" [class.no-activity]="!report.hasActivity">
                <!-- Vehicle Header -->
                <div class="report-header">
                  <div class="vehicle-info">
                    <div class="vehicle-icon">🚗</div>
                    <div class="vehicle-details">
                      <h2>{{ report.vehicleName }}</h2>
                      <span class="plate">{{ report.plate || 'Sans immatriculation' }}</span>
                      @if (report.driverName) {
                        <span class="driver">👤 {{ report.driverName }}</span>
                      }
                    </div>
                  </div>
                  <div class="report-date">
                    {{ formatDate(report.reportDate) }}
                  </div>
                </div>

                @if (report.hasActivity) {
                  <!-- Summary Cards -->
                  <div class="summary-grid">
                    <div class="summary-card driving">
                      <div class="summary-icon">🚗</div>
                      <div class="summary-content">
                        <span class="summary-value">{{ report.summary.totalDrivingFormatted }}</span>
                        <span class="summary-label">Conduite</span>
                      </div>
                    </div>
                    <div class="summary-card stopped">
                      <div class="summary-icon">🅿️</div>
                      <div class="summary-content">
                        <span class="summary-value">{{ report.summary.totalStoppedFormatted }}</span>
                        <span class="summary-label">Arrêts ({{ report.summary.stopCount }})</span>
                      </div>
                    </div>
                    <div class="summary-card distance">
                      <div class="summary-icon">📏</div>
                      <div class="summary-content">
                        <span class="summary-value">{{ report.summary.totalDistanceKm }} km</span>
                        <span class="summary-label">Distance</span>
                      </div>
                    </div>
                    <div class="summary-card speed">
                      <div class="summary-icon">⚡</div>
                      <div class="summary-content">
                        <span class="summary-value">{{ report.summary.maxSpeedKph }} km/h</span>
                        <span class="summary-label">Vitesse max</span>
                      </div>
                    </div>
                  </div>

                  <!-- First Start Info -->
                  @if (report.firstStart) {
                    <div class="first-start-info">
                      <div class="start-icon">🔑</div>
                      <div class="start-details">
                        <span class="start-label">Premier démarrage</span>
                        <span class="start-time">{{ formatTime(report.firstStart.timestamp) }}</span>
                        <span class="start-address">{{ report.firstStart.address || 'Adresse inconnue' }}</span>
                      </div>
                    </div>
                  }

                  <!-- Activity Timeline -->
                  <div class="activity-timeline">
                    <h3>📋 Chronologie des activités</h3>
                    <div class="timeline">
                      @for (activity of report.activities; track activity.sequenceNumber) {
                        <div class="timeline-item" [class.drive]="activity.type === 'drive'" [class.stop]="activity.type === 'stop'">
                          <div class="timeline-marker">
                            @if (activity.type === 'drive') {
                              <span class="marker-icon">🚗</span>
                            } @else {
                              <span class="marker-icon">🅿️</span>
                            }
                          </div>
                          <div class="timeline-content">
                            <div class="timeline-header">
                              <span class="activity-type">
                                {{ activity.type === 'drive' ? 'Trajet' : 'Arrêt' }} #{{ activity.sequenceNumber }}
                              </span>
                              <span class="activity-duration">{{ activity.durationFormatted }}</span>
                            </div>
                            <div class="timeline-times">
                              <span class="time-start">{{ formatTime(activity.startTime) }}</span>
                              @if (activity.endTime) {
                                <span class="time-arrow">→</span>
                                <span class="time-end">{{ formatTime(activity.endTime) }}</span>
                              }
                            </div>
                            @if (activity.type === 'drive') {
                              <div class="drive-stats">
                                <span class="stat">📏 {{ activity.distanceKm }} km</span>
                                <span class="stat">⌀ {{ activity.avgSpeedKph }} km/h</span>
                                <span class="stat">⚡ {{ activity.maxSpeedKph }} km/h max</span>
                              </div>
                              <div class="locations">
                                <div class="location from">
                                  <span class="location-label">De:</span>
                                  <span class="location-address">{{ activity.startLocation.address || 'Adresse inconnue' }}</span>
                                </div>
                                @if (activity.endLocation) {
                                  <div class="location to">
                                    <span class="location-label">À:</span>
                                    <span class="location-address">{{ activity.endLocation.address || 'Adresse inconnue' }}</span>
                                  </div>
                                }
                              </div>
                            } @else {
                              <div class="stop-location">
                                <span class="location-icon">📍</span>
                                <span class="location-address">{{ activity.startLocation.address || 'Adresse inconnue' }}</span>
                              </div>
                            }
                          </div>
                        </div>
                      }
                    </div>
                  </div>

                  <!-- Last Position Info -->
                  @if (report.lastPosition) {
                    <div class="last-position-info">
                      <div class="position-icon">{{ report.lastPosition.ignitionOn ? '🟢' : '🔴' }}</div>
                      <div class="position-details">
                        <span class="position-label">Dernière position</span>
                        <span class="position-time">{{ formatTime(report.lastPosition.timestamp) }}</span>
                        <span class="position-status">{{ report.lastPosition.ignitionOn ? 'Contact ON' : 'Contact OFF' }}</span>
                      </div>
                    </div>
                  }
                } @else {
                  <!-- No Activity -->
                  <div class="no-activity-message">
                    <div class="no-activity-icon">😴</div>
                    <p>Aucune activité enregistrée pour cette journée</p>
                  </div>
                }
              </div>
            }
          </div>
        }

        <!-- Empty State -->
        @if (!loading && reports.length === 0) {
          <div class="empty-state">
            <div class="empty-icon">📭</div>
            <h3>Aucun rapport disponible</h3>
            <p>Sélectionnez une date et un véhicule pour générer un rapport</p>
          </div>
        }
      </div>
    </app-layout>
  `,
  styles: [`
    .daily-report-page {
      padding: 24px;
      max-width: 1400px;
      margin: 0 auto;
    }

    .page-header {
      margin-bottom: 24px;
    }

    .header-content h1 {
      font-size: 28px;
      font-weight: 700;
      color: #1e293b;
      margin: 0 0 8px 0;
    }

    .subtitle {
      color: #64748b;
      margin: 0;
    }

    .filters-bar {
      display: flex;
      gap: 16px;
      align-items: flex-end;
      flex-wrap: wrap;
      background: white;
      padding: 16px 20px;
      border-radius: 12px;
      box-shadow: 0 1px 3px rgba(0,0,0,0.1);
      margin-bottom: 24px;
    }

    .filter-group {
      display: flex;
      flex-direction: column;
      gap: 6px;
    }

    .filter-group label {
      font-size: 12px;
      font-weight: 600;
      color: #64748b;
      text-transform: uppercase;
    }

    .date-input, .select-input {
      padding: 10px 14px;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      font-size: 14px;
      min-width: 180px;
      background: #f8fafc;
    }

    .date-input:focus, .select-input:focus {
      outline: none;
      border-color: #3b82f6;
      box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
    }

    .btn-refresh {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 20px;
      background: #3b82f6;
      color: white;
      border: none;
      border-radius: 8px;
      font-weight: 600;
      cursor: pointer;
      transition: background 0.2s;
    }

    .btn-refresh:hover {
      background: #2563eb;
    }

    .loading-container {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 60px;
      color: #64748b;
    }

    .spinner {
      width: 40px;
      height: 40px;
      border: 3px solid #e2e8f0;
      border-top-color: #3b82f6;
      border-radius: 50%;
      animation: spin 1s linear infinite;
      margin-bottom: 16px;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }

    .reports-container {
      display: flex;
      flex-direction: column;
      gap: 24px;
    }

    .report-card {
      background: white;
      border-radius: 16px;
      box-shadow: 0 1px 3px rgba(0,0,0,0.1);
      overflow: hidden;
    }

    .report-card.no-activity {
      opacity: 0.7;
    }

    .report-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 20px 24px;
      background: linear-gradient(135deg, #f8fafc 0%, #f1f5f9 100%);
      border-bottom: 1px solid #e2e8f0;
    }

    .vehicle-info {
      display: flex;
      align-items: center;
      gap: 16px;
    }

    .vehicle-icon {
      font-size: 32px;
    }

    .vehicle-details h2 {
      font-size: 20px;
      font-weight: 700;
      color: #1e293b;
      margin: 0 0 4px 0;
    }

    .plate {
      display: inline-block;
      background: #e2e8f0;
      padding: 2px 8px;
      border-radius: 4px;
      font-size: 12px;
      font-weight: 600;
      color: #475569;
      margin-right: 8px;
    }

    .driver {
      font-size: 13px;
      color: #64748b;
    }

    .report-date {
      font-size: 14px;
      font-weight: 600;
      color: #64748b;
      background: white;
      padding: 8px 16px;
      border-radius: 8px;
    }

    .summary-grid {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 16px;
      padding: 20px 24px;
      border-bottom: 1px solid #e2e8f0;
    }

    @media (max-width: 768px) {
      .summary-grid {
        grid-template-columns: repeat(2, 1fr);
      }
    }

    .summary-card {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 16px;
      border-radius: 12px;
      background: #f8fafc;
    }

    .summary-card.driving { background: #ecfdf5; }
    .summary-card.stopped { background: #fef3c7; }
    .summary-card.distance { background: #eff6ff; }
    .summary-card.speed { background: #fef2f2; }

    .summary-icon {
      font-size: 24px;
    }

    .summary-content {
      display: flex;
      flex-direction: column;
    }

    .summary-value {
      font-size: 18px;
      font-weight: 700;
      color: #1e293b;
    }

    .summary-label {
      font-size: 12px;
      color: #64748b;
    }

    .first-start-info, .last-position-info {
      display: flex;
      align-items: center;
      gap: 16px;
      padding: 16px 24px;
      background: #f0fdf4;
      border-bottom: 1px solid #e2e8f0;
    }

    .last-position-info {
      background: #f8fafc;
    }

    .start-icon, .position-icon {
      font-size: 24px;
    }

    .start-details, .position-details {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .start-label, .position-label {
      font-size: 12px;
      color: #64748b;
      text-transform: uppercase;
      font-weight: 600;
    }

    .start-time, .position-time {
      font-size: 16px;
      font-weight: 700;
      color: #1e293b;
    }

    .start-address, .position-status {
      font-size: 13px;
      color: #64748b;
    }

    .activity-timeline {
      padding: 20px 24px;
    }

    .activity-timeline h3 {
      font-size: 16px;
      font-weight: 600;
      color: #1e293b;
      margin: 0 0 16px 0;
    }

    .timeline {
      display: flex;
      flex-direction: column;
      gap: 0;
    }

    .timeline-item {
      display: flex;
      gap: 16px;
      padding: 16px 0;
      border-left: 2px solid #e2e8f0;
      margin-left: 16px;
      padding-left: 24px;
      position: relative;
    }

    .timeline-item.drive { border-left-color: #22c55e; }
    .timeline-item.stop { border-left-color: #f59e0b; }

    .timeline-marker {
      position: absolute;
      left: -18px;
      top: 16px;
      width: 32px;
      height: 32px;
      background: white;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      box-shadow: 0 1px 3px rgba(0,0,0,0.1);
    }

    .marker-icon {
      font-size: 16px;
    }

    .timeline-content {
      flex: 1;
      background: #f8fafc;
      padding: 16px;
      border-radius: 12px;
    }

    .timeline-item.drive .timeline-content { background: #f0fdf4; }
    .timeline-item.stop .timeline-content { background: #fffbeb; }

    .timeline-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 8px;
    }

    .activity-type {
      font-weight: 600;
      color: #1e293b;
    }

    .activity-duration {
      background: white;
      padding: 4px 10px;
      border-radius: 16px;
      font-size: 13px;
      font-weight: 600;
      color: #64748b;
    }

    .timeline-times {
      display: flex;
      align-items: center;
      gap: 8px;
      margin-bottom: 12px;
      font-size: 14px;
      color: #475569;
    }

    .time-arrow {
      color: #94a3b8;
    }

    .drive-stats {
      display: flex;
      gap: 16px;
      margin-bottom: 12px;
      flex-wrap: wrap;
    }

    .stat {
      font-size: 13px;
      color: #64748b;
    }

    .locations {
      display: flex;
      flex-direction: column;
      gap: 6px;
    }

    .location {
      display: flex;
      gap: 8px;
      font-size: 13px;
    }

    .location-label {
      color: #64748b;
      min-width: 24px;
    }

    .location-address {
      color: #475569;
    }

    .stop-location {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 13px;
      color: #475569;
    }

    .no-activity-message {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 40px;
      color: #64748b;
    }

    .no-activity-icon {
      font-size: 48px;
      margin-bottom: 12px;
    }

    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 80px;
      background: white;
      border-radius: 16px;
      color: #64748b;
    }

    .empty-icon {
      font-size: 64px;
      margin-bottom: 16px;
    }

    .empty-state h3 {
      margin: 0 0 8px 0;
      color: #1e293b;
    }

    .empty-state p {
      margin: 0;
    }
  `]
})
export class DailyReportComponent implements OnInit {
  reports: DailyActivityReport[] = [];
  vehicles: any[] = [];
  loading = false;

  selectedDate = '';
  selectedVehicleId = '';
  minStopDuration = 120;

  constructor(
    private router: Router,
    private apiService: ApiService
  ) {}

  ngOnInit() {
    if (!this.apiService.isAuthenticated()) {
      this.router.navigate(['/login']);
      return;
    }

    // Set default date to today
    this.selectedDate = new Date().toISOString().split('T')[0];
    
    this.loadVehicles();
    this.loadReports();
  }

  loadVehicles() {
    this.apiService.getVehicles().subscribe({
      next: (vehicles) => this.vehicles = vehicles,
      error: (err) => console.error('Error loading vehicles:', err)
    });
  }

  loadReports() {
    this.loading = true;
    const date = this.selectedDate ? new Date(this.selectedDate) : undefined;
    const vehicleIds = this.selectedVehicleId ? [parseInt(this.selectedVehicleId)] : undefined;

    if (this.selectedVehicleId) {
      this.apiService.getDailyReport(
        parseInt(this.selectedVehicleId),
        date,
        this.minStopDuration
      ).subscribe({
        next: (report) => {
          this.reports = [report];
          this.loading = false;
        },
        error: (err) => {
          console.error('Error loading report:', err);
          this.loading = false;
        }
      });
    } else {
      this.apiService.getDailyReports(date, vehicleIds, this.minStopDuration).subscribe({
        next: (reports) => {
          this.reports = reports;
          this.loading = false;
        },
        error: (err) => {
          console.error('Error loading reports:', err);
          this.loading = false;
        }
      });
    }
  }

  formatDate(dateStr: string): string {
    const date = new Date(dateStr);
    return date.toLocaleDateString('fr-FR', {
      weekday: 'long',
      year: 'numeric',
      month: 'long',
      day: 'numeric'
    });
  }

  formatTime(dateStr: string): string {
    const date = new Date(dateStr);
    return date.toLocaleTimeString('fr-FR', {
      hour: '2-digit',
      minute: '2-digit'
    });
  }
}
