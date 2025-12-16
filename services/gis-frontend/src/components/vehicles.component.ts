import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MockDataService } from '../services/mock-data.service';
import { Vehicle, Company } from '../models/types';
import { AppLayoutComponent } from './shared/app-layout.component';

@Component({
  selector: 'app-vehicles',
  standalone: true,
  imports: [CommonModule, AppLayoutComponent],
  template: `
    <app-layout>
      <div class="vehicles-page">
        <div class="filters-bar">
          <div class="search-box">
            <svg class="search-icon" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="11" cy="11" r="8"/>
              <path d="m21 21-4.35-4.35"/>
            </svg>
            <input type="text" placeholder="Rechercher un véhicule..." />
          </div>
          <select class="filter-select">
            <option value="">Tous les statuts</option>
            <option value="available">Disponibles</option>
            <option value="in_use">En service</option>
            <option value="maintenance">En maintenance</option>
          </select>
          <select class="filter-select">
            <option value="">Tous les types</option>
            <option value="camion">Camion</option>
            <option value="citadine">Citadine</option>
            <option value="suv">SUV</option>
            <option value="utilitaire">Utilitaire</option>
          </select>
        </div>

        <div class="vehicles-grid">
          <div class="vehicle-card" *ngFor="let vehicle of vehicles">
            <div class="vehicle-card-header">
              <div class="vehicle-icon-wrapper" [class]="vehicle.status">
                <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M5 17h-2v-6l2-5h9l4 5v6h-2"/>
                  <circle cx="7" cy="17" r="2"/>
                  <circle cx="17" cy="17" r="2"/>
                </svg>
              </div>
              <div class="vehicle-status-badge" [class]="vehicle.status">
                {{ getStatusLabel(vehicle.status) }}
              </div>
            </div>

            <div class="vehicle-card-body">
              <h3>{{ vehicle.name }}</h3>
              <p class="vehicle-subtitle">{{ vehicle.brand }} {{ vehicle.model }}</p>

              <div class="vehicle-details">
                <div class="detail-row">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <rect x="3" y="11" width="18" height="11" rx="2" ry="2"/>
                    <path d="M7 11V7a5 5 0 0 1 10 0v4"/>
                  </svg>
                  <span class="detail-label">Plaque:</span>
                  <span class="detail-value">{{ vehicle.plate }}</span>
                </div>

                <div class="detail-row">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <rect x="3" y="4" width="18" height="18" rx="2" ry="2"/>
                    <line x1="16" y1="2" x2="16" y2="6"/>
                    <line x1="8" y1="2" x2="8" y2="6"/>
                    <line x1="3" y1="10" x2="21" y2="10"/>
                  </svg>
                  <span class="detail-label">Année:</span>
                  <span class="detail-value">{{ vehicle.year }}</span>
                </div>

                <div class="detail-row">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <circle cx="12" cy="12" r="10"/>
                    <polyline points="12 6 12 12 16 14"/>
                  </svg>
                  <span class="detail-label">Type:</span>
                  <span class="detail-value">{{ getTypeLabel(vehicle.type) }}</span>
                </div>

                <div class="detail-row">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M12 2v4m0 12v4M4.93 4.93l2.83 2.83m8.48 8.48 2.83 2.83M2 12h4m12 0h4M4.93 19.07l2.83-2.83m8.48-8.48 2.83-2.83"/>
                  </svg>
                  <span class="detail-label">Kilométrage:</span>
                  <span class="detail-value">{{ vehicle.mileage.toLocaleString() }} km</span>
                </div>
              </div>

              <div class="vehicle-features">
                <span class="feature-badge gps" *ngIf="vehicle.hasGPS">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0Z"/>
                    <circle cx="12" cy="10" r="3"/>
                  </svg>
                  GPS actif
                </span>
                <span class="feature-badge driver" *ngIf="vehicle.assignedDriverId">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/>
                    <circle cx="12" cy="7" r="4"/>
                  </svg>
                  Chauffeur attribué
                </span>
              </div>
            </div>

            <div class="vehicle-card-footer">
              <button class="btn-secondary">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                  <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                </svg>
                Modifier
              </button>
              <button class="btn-link">
                Détails
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="m9 18 6-6-6-6"/>
                </svg>
              </button>
            </div>
          </div>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    /* ===== VEHICLES PAGE ===== */
    .vehicles-page {
      flex: 1;
      padding: 0;
      background: var(--bg-page);
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

    .filters-bar {
      display: flex;
      gap: 12px;
      padding: 24px 32px;
      background: var(--bg-card);
      border-bottom: 1px solid var(--border-color);
    }

    .search-box {
      flex: 1;
      position: relative;
    }

    .search-icon {
      position: absolute;
      left: 14px;
      top: 50%;
      transform: translateY(-50%);
      color: var(--text-muted);
      pointer-events: none;
    }

    .search-box input {
      width: 100%;
      padding: 10px 14px 10px 42px;
      background: var(--bg-secondary);
      border: 1px solid var(--border-color);
      border-radius: 8px;
      color: var(--text-primary);
      font-size: 14px;
      transition: all 0.2s;
    }

    .search-box input:focus {
      outline: none;
      border-color: var(--primary);
    }

    .search-box input::placeholder {
      color: var(--text-muted);
    }

    .filter-select {
      padding: 10px 14px;
      background: var(--bg-secondary);
      border: 1px solid var(--border-color);
      border-radius: 8px;
      color: var(--text-primary);
      font-size: 14px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .filter-select:focus {
      outline: none;
      border-color: var(--primary);
    }

    .filter-select option {
      background: var(--bg-secondary);
      color: var(--text-primary);
    }

    .vehicles-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
      gap: 20px;
      padding: 24px 32px;
    }

    .vehicle-card {
      background: var(--bg-secondary);
      border-radius: 12px;
      border: 1px solid var(--border-color);
      overflow: hidden;
      transition: all 0.2s;
    }

    .vehicle-card:hover {
      transform: translateY(-4px);
      box-shadow: var(--shadow-lg);
      border-color: var(--border-light);
    }

    .vehicle-card-header {
      padding: 20px;
      background: var(--bg-tertiary);
      display: flex;
      justify-content: space-between;
      align-items: center;
      border-bottom: 1px solid var(--border-color);
    }

    .vehicle-icon-wrapper {
      width: 56px;
      height: 56px;
      border-radius: 12px;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .vehicle-icon-wrapper.available {
      background: rgba(16, 185, 129, 0.1);
      color: var(--success);
    }

    .vehicle-icon-wrapper.in_use {
      background: rgba(14, 165, 233, 0.1);
      color: var(--primary);
    }

    .vehicle-icon-wrapper.maintenance {
      background: rgba(245, 158, 11, 0.1);
      color: var(--warning);
    }

    .vehicle-status-badge {
      padding: 6px 12px;
      border-radius: 6px;
      font-size: 12px;
      font-weight: 600;
    }

    .vehicle-status-badge.available {
      background: rgba(16, 185, 129, 0.1);
      color: var(--success);
    }

    .vehicle-status-badge.in_use {
      background: rgba(14, 165, 233, 0.1);
      color: var(--primary);
    }

    .vehicle-status-badge.maintenance {
      background: rgba(245, 158, 11, 0.1);
      color: var(--warning);
    }

    .vehicle-card-body {
      padding: 20px;
    }

    .vehicle-card-body h3 {
      font-size: 18px;
      font-weight: 600;
      color: var(--text-primary);
      margin: 0 0 4px;
    }

    .vehicle-subtitle {
      color: var(--text-muted);
      margin: 0 0 16px;
      font-size: 13px;
    }

    .vehicle-details {
      display: flex;
      flex-direction: column;
      gap: 10px;
      margin-bottom: 16px;
    }

    .detail-row {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 13px;
    }

    .detail-row svg {
      color: var(--text-muted);
      flex-shrink: 0;
    }

    .detail-label {
      color: var(--text-muted);
    }

    .detail-value {
      font-weight: 600;
      color: var(--text-primary);
      margin-left: auto;
    }

    .vehicle-features {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
    }

    .feature-badge {
      display: flex;
      align-items: center;
      gap: 4px;
      padding: 4px 10px;
      border-radius: 6px;
      font-size: 11px;
      font-weight: 600;
    }

    .feature-badge.gps {
      background: rgba(16, 185, 129, 0.1);
      color: var(--success);
    }

    .feature-badge.driver {
      background: rgba(14, 165, 233, 0.1);
      color: var(--primary);
    }

    .vehicle-card-footer {
      padding: 16px 20px;
      border-top: 1px solid var(--border-color);
      display: flex;
      gap: 12px;
      align-items: center;
    }

    .btn-secondary {
      flex: 1;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 6px;
      padding: 10px;
      background: var(--bg-tertiary);
      color: var(--text-primary);
      border: none;
      border-radius: 8px;
      font-weight: 600;
      font-size: 13px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-secondary:hover {
      background: var(--bg-hover);
    }

    .btn-link {
      display: flex;
      align-items: center;
      gap: 4px;
      padding: 10px 12px;
      color: var(--primary);
      background: none;
      border: none;
      font-weight: 600;
      font-size: 13px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-link:hover {
      color: var(--primary-light);
      gap: 6px;
    }

    @media (max-width: 768px) {
      .filters-bar {
        flex-wrap: wrap;
        padding: 16px 20px;
      }

      .search-box {
        width: 100%;
      }

      .vehicles-grid {
        grid-template-columns: 1fr;
        padding: 16px 20px;
      }
    }
  `]
})
export class VehiclesComponent implements OnInit {
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
    }
  }

  getStatusLabel(status: string): string {
    const labels: any = {
      available: 'Disponible',
      in_use: 'En service',
      maintenance: 'Maintenance'
    };
    return labels[status] || status;
  }

  getTypeLabel(type: string): string {
    const labels: any = {
      camion: 'Camion',
      citadine: 'Citadine',
      suv: 'SUV',
      utilitaire: 'Utilitaire',
      other: 'Autre'
    };
    return labels[type] || type;
  }

  navigate(path: string) {
    this.router.navigate([path]);
  }

  logout() {
    this.dataService.logout();
    this.router.navigate(['/']);
  }
}
