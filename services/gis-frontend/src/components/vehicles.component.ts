import { Component, OnInit, NgZone, ChangeDetectorRef, ApplicationRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { trigger, transition, style, animate, state } from '@angular/animations';
import { ApiService } from '../services/api.service';
import { GeocodingService } from '../services/geocoding.service';
import { Vehicle, Company } from '../models/types';
import { AppLayoutComponent } from './shared/app-layout.component';
import { VehiclePopupComponent } from './shared/vehicle-popup.component';
import { VehicleCostsPopupComponent } from './shared/vehicle-costs-popup.component';

interface VehicleExtended extends Vehicle {
  fuelLevel?: number;
  nextMaintenanceKm?: number;
  lastPosition?: { lat: number; lng: number; address?: string };
  totalTrips?: number;
  monthlyDistance?: number;
  monthlyFuelCost?: number;
  insuranceExpiry?: Date;
}

interface VehicleExpense {
  id: string;
  date: Date;
  type: 'fuel' | 'maintenance' | 'insurance' | 'repair' | 'other';
  description: string;
  amount: number;
  mileage?: number;
}

interface VehicleTrip {
  id: string;
  startTime: Date;
  endTime: Date;
  distance: number;
  startAddress: string;
  endAddress: string;
}

@Component({
  selector: 'app-vehicles',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent, VehiclePopupComponent, VehicleCostsPopupComponent],
  animations: [
    trigger('slideIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateX(100%)' }),
        animate('300ms ease-out', style({ opacity: 1, transform: 'translateX(0)' }))
      ]),
      transition(':leave', [
        animate('300ms ease-in', style({ opacity: 0, transform: 'translateX(100%)' }))
      ])
    ]),
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 }))
      ])
    ]),
    trigger('scaleIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'scale(0.95)' }),
        animate('200ms ease-out', style({ opacity: 1, transform: 'scale(1)' }))
      ])
    ])
  ],
  template: `
    <app-layout>
      <div class="vehicles-page">
        <!-- LISTE DES VÉHICULES -->
        <div class="vehicles-list-view" @fadeIn>
          <!-- Filter Bar (identique à GPS) -->
          <div class="filter-bar">
            <div class="search-wrapper">
              <svg class="search-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/>
              </svg>
              <input type="text" class="search-input" placeholder="Rechercher par nom, marque, plaque..." [(ngModel)]="searchQuery" (input)="filterVehicles()">
            </div>
            <select class="filter-select" [(ngModel)]="filterStatus" (change)="filterVehicles()">
              <option value="">Tous les statuts</option>
              <option value="available">Disponible</option>
              <option value="in_use">En service</option>
              <option value="maintenance">En maintenance</option>
            </select>
            <select class="filter-select" [(ngModel)]="filterType" (change)="filterVehicles()">
              <option value="">Tous les types</option>
              <option value="camion">Camion</option>
              <option value="citadine">Citadine</option>
              <option value="suv">SUV</option>
              <option value="utilitaire">Utilitaire</option>
            </select>
            <button class="btn-add" (click)="openAddPopup()">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="12" y1="5" x2="12" y2="19"/>
                <line x1="5" y1="12" x2="19" y2="12"/>
              </svg>
              Nouveau véhicule
            </button>
          </div>

          <!-- Stats Bar (identique à GPS) -->
          <div class="stats-bar">
            <div class="stat-item">
              <div class="stat-icon info">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M7 17m-2 0a2 2 0 1 0 4 0a2 2 0 1 0 -4 0"/>
                  <path d="M17 17m-2 0a2 2 0 1 0 4 0a2 2 0 1 0 -4 0"/>
                  <path d="M5 17h-2v-6l2-5h9l4 5h1a2 2 0 0 1 2 2v4h-2m-4 0h-6m-6 -6h15m-6 0v-5"/>
                </svg>
              </div>
              <div class="stat-content">
                <span class="stat-value">{{ allVehicles.length }}</span>
                <span class="stat-label">Total</span>
              </div>
            </div>
            <div class="stat-item">
              <div class="stat-icon available">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                  <polyline points="22 4 12 14.01 9 11.01"/>
                </svg>
              </div>
              <div class="stat-content">
                <span class="stat-value">{{ getVehiclesByStatus('available').length }}</span>
                <span class="stat-label">Disponibles</span>
              </div>
            </div>
            <div class="stat-item">
              <div class="stat-icon active">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="12" cy="12" r="10"/>
                  <polyline points="12 6 12 12 16 14"/>
                </svg>
              </div>
              <div class="stat-content">
                <span class="stat-value">{{ getVehiclesByStatus('in_use').length }}</span>
                <span class="stat-label">En service</span>
              </div>
            </div>
            <div class="stat-item">
              <div class="stat-icon danger">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>
                </svg>
              </div>
              <div class="stat-content">
                <span class="stat-value">{{ getVehiclesByStatus('maintenance').length }}</span>
                <span class="stat-label">Maintenance</span>
              </div>
            </div>
          </div>

          <!-- Grille des véhicules -->
          <div class="grid-container">
            <div class="vehicles-grid">
            <div class="vehicle-card" *ngFor="let vehicle of filteredVehicles" @scaleIn (click)="openVehicleDetail(vehicle)">
              <!-- Header avec infos principales -->
              <div class="card-top" [class]="vehicle.status">
                <div class="card-top-left">
                  <div class="vehicle-type-icon" [class]="vehicle.type">
                    <span *ngIf="vehicle.type === 'camion'">🚛</span>
                    <span *ngIf="vehicle.type === 'citadine'">🚗</span>
                    <span *ngIf="vehicle.type === 'suv'">🚙</span>
                    <span *ngIf="vehicle.type === 'utilitaire'">🚐</span>
                    <span *ngIf="!vehicle.type || vehicle.type === 'other'">🚘</span>
                  </div>
                  <div class="card-top-info">
                    <span class="vehicle-brand">{{ vehicle.brand }}</span>
                    <span class="vehicle-year">{{ vehicle.year }}</span>
                  </div>
                </div>
                <div class="card-top-right">
                  <div class="status-badge-small" [class]="vehicle.status">
                    <span class="status-dot"></span>
                  </div>
                  <div class="gps-indicator" *ngIf="vehicle.hasGPS">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor">
                      <path d="M12 2C8.13 2 5 5.13 5 9c0 5.25 7 13 7 13s7-7.75 7-13c0-3.87-3.13-7-7-7z"/>
                    </svg>
                  </div>
                </div>
              </div>

              <!-- Contenu de la carte -->
              <div class="card-content">
                <div class="card-header">
                  <h3 class="vehicle-name">{{ vehicle.name }}</h3>
                  <span class="vehicle-plate">{{ vehicle.plate }}</span>
                </div>
                <p class="vehicle-model">{{ vehicle.brand }} {{ vehicle.model }} · {{ vehicle.year }}</p>

                <!-- Indicateurs visuels -->
                <div class="vehicle-indicators">
                  <!-- Jauge carburant -->
                  <div class="indicator fuel">
                    <div class="indicator-header">
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M3 22V8a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2v14"/>
                        <path d="M17 10h2a2 2 0 0 1 2 2v3"/>
                        <path d="M21 19a2 2 0 0 1-2 2h-2"/>
                        <path d="M3 14h14"/>
                      </svg>
                      <span>Carburant</span>
                    </div>
                    <div class="gauge-bar">
                      <div class="gauge-fill" [style.width.%]="vehicle.fuelLevel || 75" 
                           [class.low]="(vehicle.fuelLevel || 75) < 25"
                           [class.medium]="(vehicle.fuelLevel || 75) >= 25 && (vehicle.fuelLevel || 75) < 50">
                      </div>
                    </div>
                    <span class="indicator-value">{{ vehicle.fuelLevel || 75 }}%</span>
                  </div>

                  <!-- Kilométrage -->
                  <div class="indicator mileage">
                    <div class="indicator-header">
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <circle cx="12" cy="12" r="10"/>
                        <polyline points="12 6 12 12 16 14"/>
                      </svg>
                      <span>Kilométrage</span>
                    </div>
                    <span class="indicator-value large">{{ formatNumber(vehicle.mileage) }} km</span>
                  </div>
                </div>

                <!-- Prochaine maintenance -->
                <div class="maintenance-alert" *ngIf="isMaintenanceSoon(vehicle)">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/>
                    <line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/>
                  </svg>
                  <span>Maintenance dans {{ getMaintenanceRemaining(vehicle) }} km</span>
                </div>

                <!-- Alertes documents (assurance, visite technique) -->
                <div class="document-alerts" *ngIf="hasDocumentAlerts(vehicle)">
                  <div class="doc-alert" *ngIf="isInsuranceExpiring(vehicle)" [class]="getInsuranceStatus(vehicle)">
                    <span class="doc-icon">🛡️</span>
                    <span class="doc-text">{{ getInsuranceAlertText(vehicle) }}</span>
                  </div>
                  <div class="doc-alert" *ngIf="isTechnicalInspectionExpiring(vehicle)" [class]="getTechnicalStatus(vehicle)">
                    <span class="doc-icon">🔧</span>
                    <span class="doc-text">{{ getTechnicalAlertText(vehicle) }}</span>
                  </div>
                </div>

                <!-- Chauffeur assigné -->
                <div class="driver-info" *ngIf="vehicle.assignedDriverName">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/>
                    <circle cx="12" cy="7" r="4"/>
                  </svg>
                  <span>{{ vehicle.assignedDriverName }}</span>
                </div>
              </div>

              <!-- Footer avec actions -->
              <div class="card-footer">
                <button class="btn-action edit" (click)="openEditPopup(vehicle); $event.stopPropagation()">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                    <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                  </svg>
                </button>
                <button class="btn-action costs" (click)="openCostsPopup(vehicle); $event.stopPropagation()">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <line x1="12" y1="1" x2="12" y2="23"/>
                    <path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>
                  </svg>
                </button>
                <button class="btn-detail" (click)="openVehicleDetail(vehicle); $event.stopPropagation()">
                  Voir détails
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="m9 18 6-6-6-6"/>
                  </svg>
                </button>
              </div>
            </div>

            </div>
            <!-- État vide -->
            <div class="empty-state" *ngIf="filteredVehicles.length === 0">
              <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                <path d="M7 17m-2 0a2 2 0 1 0 4 0a2 2 0 1 0 -4 0"/>
                <path d="M17 17m-2 0a2 2 0 1 0 4 0a2 2 0 1 0 -4 0"/>
                <path d="M5 17h-2v-6l2-5h9l4 5h1a2 2 0 0 1 2 2v4h-2m-4 0h-6m-6 -6h15m-6 0v-5"/>
              </svg>
              <h3>Aucun véhicule trouvé</h3>
              <p>Modifiez vos filtres ou ajoutez un nouveau véhicule</p>
            </div>
          </div>
        </div>

      </div>

      <!-- PANNEAU DÉTAIL VÉHICULE (slide-in depuis la droite) -->
      <div class="detail-overlay" *ngIf="selectedDetailVehicle" @fadeIn (click)="closeVehicleDetail()">
        <div class="detail-panel" @slideIn (click)="$event.stopPropagation()">
          <!-- Header -->
          <div class="panel-header" [class]="selectedDetailVehicle.status">
            <div class="panel-header-content">
              <div class="vehicle-type-icon-large">
                <span *ngIf="selectedDetailVehicle.type === 'camion'">🚛</span>
                <span *ngIf="selectedDetailVehicle.type === 'citadine'">🚗</span>
                <span *ngIf="selectedDetailVehicle.type === 'suv'">🚙</span>
                <span *ngIf="selectedDetailVehicle.type === 'utilitaire'">🚐</span>
                <span *ngIf="!selectedDetailVehicle.type || selectedDetailVehicle.type === 'other'">🚘</span>
              </div>
              <div class="panel-header-info">
                <h2>{{ selectedDetailVehicle.name }}</h2>
                <p>{{ selectedDetailVehicle.brand }} {{ selectedDetailVehicle.model }} · {{ selectedDetailVehicle.year }}</p>
              </div>
            </div>
            <button class="btn-close-panel" (click)="closeVehicleDetail()">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="18" y1="6" x2="6" y2="18"/>
                <line x1="6" y1="6" x2="18" y2="18"/>
              </svg>
            </button>
          </div>

          <!-- Contenu scrollable -->
          <div class="panel-body">
            <!-- Statut et plaque -->
            <div class="info-section">
              <div class="status-plate-row">
                <div class="status-badge-large" [class]="selectedDetailVehicle.status">
                  <span class="status-dot"></span>
                  {{ getStatusLabel(selectedDetailVehicle.status) }}
                </div>
                <div class="plate-badge">{{ selectedDetailVehicle.plate }}</div>
              </div>
            </div>

            <!-- Spécifications -->
            <div class="info-section">
              <h3 class="section-title">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <rect x="3" y="3" width="18" height="18" rx="2" ry="2"/>
                  <line x1="3" y1="9" x2="21" y2="9"/>
                  <line x1="9" y1="21" x2="9" y2="9"/>
                </svg>
                Spécifications
              </h3>
              <div class="specs-grid">
                <div class="spec-item">
                  <span class="spec-label">Type</span>
                  <span class="spec-value">{{ getTypeLabel(selectedDetailVehicle.type) }}</span>
                </div>
                <div class="spec-item">
                  <span class="spec-label">Couleur</span>
                  <span class="spec-value">{{ selectedDetailVehicle.color || 'Non spécifié' }}</span>
                </div>
                <div class="spec-item">
                  <span class="spec-label">Kilométrage</span>
                  <span class="spec-value">{{ formatNumber(selectedDetailVehicle.mileage) }} km</span>
                </div>
                <div class="spec-item">
                  <span class="spec-label">Chauffeur</span>
                  <span class="spec-value">{{ selectedDetailVehicle.assignedDriverName || 'Non assigné' }}</span>
                </div>
              </div>
            </div>

            <!-- État du véhicule -->
            <div class="info-section">
              <h3 class="section-title">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M12 20V10"/><path d="M18 20V4"/><path d="M6 20v-4"/>
                </svg>
                État du véhicule
              </h3>
              <div class="gauge-item">
                <div class="gauge-header">
                  <span>Niveau carburant</span>
                  <span class="gauge-value">{{ selectedDetailVehicle.fuelLevel || 75 }}%</span>
                </div>
                <div class="gauge-bar-large">
                  <div class="gauge-fill" [style.width.%]="selectedDetailVehicle.fuelLevel || 75"
                       [class.low]="(selectedDetailVehicle.fuelLevel || 75) < 25"
                       [class.medium]="(selectedDetailVehicle.fuelLevel || 75) >= 25 && (selectedDetailVehicle.fuelLevel || 75) < 50">
                  </div>
                </div>
              </div>
              <div class="gauge-item">
                <div class="gauge-header">
                  <span>Prochaine maintenance</span>
                  <span class="gauge-value">{{ getMaintenanceRemaining(selectedDetailVehicle) }} km</span>
                </div>
                <div class="gauge-bar-large">
                  <div class="gauge-fill maintenance" [style.width.%]="getMaintenancePercent(selectedDetailVehicle)">
                  </div>
                </div>
              </div>
            </div>

            <!-- GPS Info -->
            <div class="info-section" *ngIf="selectedDetailVehicle.hasGPS">
              <h3 class="section-title">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0Z"/>
                  <circle cx="12" cy="10" r="3"/>
                </svg>
                Données GPS
              </h3>
              <div class="gps-stats-row">
                <div class="gps-stat-box">
                  <span class="gps-stat-value">{{ vehicleTrips.length }}</span>
                  <span class="gps-stat-label">Trajets ce mois</span>
                </div>
                <div class="gps-stat-box">
                  <span class="gps-stat-value">{{ calculateMonthlyDistance() }}</span>
                  <span class="gps-stat-label">km ce mois</span>
                </div>
              </div>
              <div class="current-location" *ngIf="currentLocation">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="12" cy="12" r="10"/>
                  <circle cx="12" cy="12" r="3"/>
                </svg>
                <span>{{ currentLocation }}</span>
              </div>
            </div>

            <!-- Dépenses -->
            <div class="info-section">
              <h3 class="section-title">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="12" y1="1" x2="12" y2="23"/>
                  <path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>
                </svg>
                Dépenses du mois
              </h3>
              <div class="expenses-list">
                <div class="expense-row">
                  <span class="expense-icon">⛽</span>
                  <span class="expense-label">Carburant</span>
                  <span class="expense-amount">{{ formatCurrency(monthlyExpenses.fuel) }}</span>
                </div>
                <div class="expense-row">
                  <span class="expense-icon">🔧</span>
                  <span class="expense-label">Maintenance</span>
                  <span class="expense-amount">{{ formatCurrency(monthlyExpenses.maintenance) }}</span>
                </div>
                <div class="expense-row">
                  <span class="expense-icon">📋</span>
                  <span class="expense-label">Assurance</span>
                  <span class="expense-amount">{{ formatCurrency(monthlyExpenses.insurance) }}</span>
                </div>
                <div class="expense-row">
                  <span class="expense-icon">🔩</span>
                  <span class="expense-label">Réparations</span>
                  <span class="expense-amount">{{ formatCurrency(monthlyExpenses.repair) }}</span>
                </div>
                <div class="expense-row total">
                  <span class="expense-icon">💰</span>
                  <span class="expense-label">Total</span>
                  <span class="expense-amount">{{ formatCurrency(getTotalMonthlyExpenses()) }}</span>
                </div>
              </div>
            </div>

            <!-- Trajets récents -->
            <div class="info-section" *ngIf="selectedDetailVehicle.hasGPS && vehicleTrips.length > 0">
              <h3 class="section-title">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <polyline points="22 12 18 12 15 21 9 3 6 12 2 12"/>
                </svg>
                Trajets récents
              </h3>
              <div class="trips-list">
                <div class="trip-item" *ngFor="let trip of vehicleTrips.slice(0, 5)">
                  <div class="trip-route">
                    <span class="trip-start">{{ trip.startAddress }}</span>
                    <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M5 12h14"/><path d="m12 5 7 7-7 7"/>
                    </svg>
                    <span class="trip-end">{{ trip.endAddress }}</span>
                  </div>
                  <div class="trip-meta">
                    <span>{{ formatDate(trip.startTime) }}</span>
                    <span class="trip-distance">{{ trip.distance }} km</span>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- Footer avec actions -->
          <div class="panel-footer">
            <button class="btn-action-footer edit" (click)="openEditPopup(selectedDetailVehicle)">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
              </svg>
              Modifier
            </button>
            <button class="btn-action-footer costs" (click)="openCostsPopup(selectedDetailVehicle)">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="12" y1="1" x2="12" y2="23"/>
                <path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>
              </svg>
              Dépenses
            </button>
          </div>
        </div>
      </div>

      <app-vehicle-popup
        [isOpen]="isPopupOpen"
        [vehicle]="selectedVehicle"
        (closed)="closePopup()"
        (saved)="saveVehicle($event)"
      />

      <app-vehicle-costs-popup
        [isOpen]="isCostsPopupOpen"
        [vehicle]="selectedVehicleForCosts"
        (closed)="closeCostsPopup()"
      />
    </app-layout>
  `,
  styles: [`
    /* ===== VEHICLES PAGE ===== */
    .vehicles-page {
      flex: 1;
      background: #f1f5f9;
      display: flex;
      flex-direction: column;
      min-height: calc(100vh - 42px);
    }

    .vehicles-page.detail-view {
      padding: 0;
      background: #f8fafc;
    }

    /* ===== FILTER BAR (identique GPS) ===== */
    .filter-bar {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 14px;
      background: white;
      border-bottom: 1px solid #e2e8f0;
    }

    .search-wrapper {
      position: relative;
      flex: 1;
      max-width: 300px;
    }

    .search-icon {
      position: absolute;
      left: 10px;
      top: 50%;
      transform: translateY(-50%);
      color: #94a3b8;
    }

    .search-input {
      width: 100%;
      padding: 6px 10px 6px 32px;
      font-family: var(--font-family);
      font-size: 12px;
      border: 1px solid #e2e8f0;
      border-radius: 3px;
      background: white;
      color: #1e293b;
    }

    .search-input:focus {
      outline: none;
      border-color: #3b82f6;
    }

    .filter-select {
      padding: 6px 10px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 3px;
      color: #1e293b;
      font-family: var(--font-family);
      font-size: 12px;
      cursor: pointer;
    }

    .filter-select:focus {
      outline: none;
      border-color: #3b82f6;
    }

    .btn-add {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 6px 12px;
      background: #3b82f6;
      color: white;
      border: none;
      border-radius: 3px;
      font-family: var(--font-family);
      font-size: 12px;
      font-weight: 500;
      cursor: pointer;
      margin-left: auto;
    }

    .btn-add:hover {
      background: #2563eb;
    }

    /* ===== STATS BAR (identique GPS) ===== */
    .stats-bar {
      display: flex;
      gap: 16px;
      padding: 12px 14px;
      background: white;
      border-bottom: 1px solid #e2e8f0;
    }

    .stat-item {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 8px 14px;
      background: #f8fafc;
      border-radius: 6px;
    }

    .stat-icon {
      width: 32px;
      height: 32px;
      border-radius: 6px;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .stat-icon.active { background: #dcfce7; color: #16a34a; }
    .stat-icon.available { background: #d1fae5; color: #059669; }
    .stat-icon.warning { background: #fef3c7; color: #d97706; }
    .stat-icon.danger { background: #fee2e2; color: #dc2626; }
    .stat-icon.info { background: #dbeafe; color: #2563eb; }

    .stat-content {
      display: flex;
      flex-direction: column;
    }

    .stat-value {
      font-size: 16px;
      font-weight: 600;
      color: #1e293b;
    }

    .stat-label {
      font-size: 11px;
      color: #64748b;
    }

    /* ===== GRID CONTAINER ===== */
    .grid-container {
      flex: 1;
      padding: 12px;
      overflow-y: auto;
    }

    /* ===== VEHICLES GRID ===== */
    .vehicles-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
      gap: 16px;
    }

    /* ===== VEHICLE CARD ===== */
    .vehicle-card {
      background: white;
      border-radius: 8px;
      overflow: hidden;
      box-shadow: 0 1px 3px rgba(0,0,0,0.08);
      transition: all 0.2s;
      cursor: pointer;
      border: 1px solid #e2e8f0;
    }

    .vehicle-card:hover {
      transform: translateY(-2px);
      box-shadow: 0 4px 12px rgba(0,0,0,0.1);
    }

    /* ===== CARD TOP (nouveau design) ===== */
    .card-top {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 12px 14px;
      border-bottom: 1px solid #e2e8f0;
    }

    .card-top.available { background: linear-gradient(135deg, #f0fdf4 0%, #dcfce7 100%); border-left: 3px solid #22c55e; }
    .card-top.in_use { background: linear-gradient(135deg, #eff6ff 0%, #dbeafe 100%); border-left: 3px solid #3b82f6; }
    .card-top.maintenance { background: linear-gradient(135deg, #fffbeb 0%, #fef3c7 100%); border-left: 3px solid #f59e0b; }

    .card-top-left {
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .vehicle-type-icon {
      font-size: 24px;
      line-height: 1;
    }

    .card-top-info {
      display: flex;
      flex-direction: column;
    }

    .vehicle-brand {
      font-size: 12px;
      font-weight: 600;
      color: #1e293b;
    }

    .vehicle-year {
      font-size: 11px;
      color: #64748b;
    }

    .card-top-right {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .status-badge-small {
      width: 10px;
      height: 10px;
      border-radius: 50%;
      position: relative;
    }

    .status-badge-small.available { background: #22c55e; }
    .status-badge-small.in_use { background: #3b82f6; }
    .status-badge-small.maintenance { background: #f59e0b; }

    .status-badge-small .status-dot {
      position: absolute;
      inset: 0;
      border-radius: 50%;
      animation: pulse-ring 2s infinite;
    }

    .status-badge-small.available .status-dot { background: rgba(34, 197, 94, 0.4); }
    .status-badge-small.in_use .status-dot { background: rgba(59, 130, 246, 0.4); }
    .status-badge-small.maintenance .status-dot { background: rgba(245, 158, 11, 0.4); }

    @keyframes pulse-ring {
      0% { transform: scale(1); opacity: 1; }
      50% { transform: scale(1.8); opacity: 0; }
      100% { transform: scale(1); opacity: 0; }
    }

    .gps-indicator {
      display: flex;
      align-items: center;
      color: #3b82f6;
    }

    .card-content {
      padding: 16px;
    }

    .vehicle-card .card-header {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      margin-bottom: 4px;
      padding: 0;
      border: none;
      background: none;
    }

    .vehicle-name {
      font-size: 16px;
      font-weight: 600;
      color: #1f2937;
      margin: 0;
    }

    .vehicle-plate {
      background: #f1f5f9;
      padding: 4px 10px;
      border-radius: 6px;
      font-size: 12px;
      font-weight: 600;
      color: #64748b;
      font-family: monospace;
    }

    .vehicle-model {
      color: #64748b;
      font-size: 13px;
      margin: 0 0 16px;
    }

    /* Indicators */
    .vehicle-indicators {
      display: flex;
      flex-direction: column;
      gap: 12px;
      margin-bottom: 12px;
    }

    .indicator {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .indicator-header {
      display: flex;
      align-items: center;
      gap: 6px;
      color: #64748b;
      font-size: 12px;
      min-width: 100px;
    }

    .gauge-bar {
      flex: 1;
      height: 6px;
      background: #e2e8f0;
      border-radius: 3px;
      overflow: hidden;
    }

    .gauge-fill {
      height: 100%;
      background: linear-gradient(90deg, #22c55e, #4ade80);
      border-radius: 3px;
      transition: width 0.5s ease;
    }

    .gauge-fill.low { background: linear-gradient(90deg, #ef4444, #f87171); }
    .gauge-fill.medium { background: linear-gradient(90deg, #f59e0b, #fbbf24); }

    .indicator-value {
      font-size: 12px;
      font-weight: 600;
      color: #1e293b;
      min-width: 50px;
      text-align: right;
    }

    .indicator-value.large {
      font-size: 14px;
    }

    .maintenance-alert {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 8px 12px;
      background: #fef3c7;
      border-radius: 8px;
      font-size: 11px;
      color: #92400e;
      margin-bottom: 12px;
    }

    /* Document Expiry Alerts */
    .document-alerts {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
      margin-bottom: 12px;
    }

    .doc-alert {
      display: flex;
      align-items: center;
      gap: 4px;
      padding: 4px 8px;
      border-radius: 4px;
      font-size: 10px;
      font-weight: 500;
    }

    .doc-alert.warning {
      background: #fef3c7;
      color: #92400e;
    }

    .doc-alert.critical {
      background: #fee2e2;
      color: #dc2626;
    }

    .doc-alert.expired {
      background: #dc2626;
      color: white;
    }

    .doc-icon {
      font-size: 11px;
    }

    .doc-text {
      white-space: nowrap;
    }

    .driver-info {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 12px;
      color: #64748b;
    }

    /* Card Footer */
    .card-footer {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 16px 20px;
      background: #f8fafc;
      border-top: 1px solid #e2e8f0;
    }

    .btn-action {
      width: 36px;
      height: 36px;
      border-radius: 8px;
      border: 1px solid #e2e8f0;
      background: white;
      color: #64748b;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }

    .btn-action:hover {
      border-color: #cbd5e1;
      color: #1e293b;
    }

    .btn-action.edit:hover { color: #3b82f6; border-color: #3b82f6; }
    .btn-action.costs:hover { color: #22c55e; border-color: #22c55e; }

    .btn-detail {
      margin-left: auto;
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 8px 16px;
      background: #1e3a5f;
      color: white;
      border: none;
      border-radius: 8px;
      font-size: 13px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-detail:hover {
      background: #2d5a87;
    }

    /* Empty State */
    .empty-state {
      text-align: center;
      padding: 80px 40px;
      color: #64748b;
    }

    .empty-state svg {
      margin-bottom: 16px;
      opacity: 0.5;
    }

    .empty-state h3 {
      margin: 0 0 8px;
      color: #1e293b;
    }

    .empty-state p {
      margin: 0;
      font-size: 14px;
    }

    /* ===== SLIDE-IN DETAIL PANEL ===== */
    .detail-overlay {
      position: fixed;
      inset: 0;
      background: rgba(0, 0, 0, 0.5);
      display: flex;
      justify-content: flex-end;
      z-index: 1000;
    }

    .detail-panel {
      width: 100%;
      max-width: 480px;
      height: 100vh;
      background: white;
      display: flex;
      flex-direction: column;
      box-shadow: -4px 0 20px rgba(0,0,0,0.15);
    }

    /* Panel Header */
    .panel-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 20px;
      color: white;
    }

    .panel-header.available { background: linear-gradient(135deg, #22c55e 0%, #16a34a 100%); }
    .panel-header.in_use { background: linear-gradient(135deg, #3b82f6 0%, #2563eb 100%); }
    .panel-header.maintenance { background: linear-gradient(135deg, #f59e0b 0%, #d97706 100%); }

    .panel-header-content {
      display: flex;
      align-items: center;
      gap: 14px;
    }

    .vehicle-type-icon-large {
      font-size: 36px;
      line-height: 1;
    }

    .panel-header-info h2 {
      margin: 0;
      font-size: 18px;
      font-weight: 600;
    }

    .panel-header-info p {
      margin: 4px 0 0;
      font-size: 13px;
      opacity: 0.9;
    }

    .btn-close-panel {
      background: rgba(255,255,255,0.2);
      border: none;
      width: 36px;
      height: 36px;
      border-radius: 8px;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      color: white;
      transition: background 0.2s;
    }

    .btn-close-panel:hover {
      background: rgba(255,255,255,0.3);
    }

    /* Panel Body */
    .panel-body {
      flex: 1;
      overflow-y: auto;
      padding: 20px;
    }

    .info-section {
      margin-bottom: 24px;
      padding-bottom: 20px;
      border-bottom: 1px solid #e2e8f0;
    }

    .info-section:last-child {
      border-bottom: none;
      margin-bottom: 0;
    }

    .info-section .section-title {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 13px;
      font-weight: 600;
      color: #1e293b;
      margin: 0 0 14px;
    }

    .info-section .section-title svg {
      color: #3b82f6;
    }

    /* Status & Plate Row */
    .status-plate-row {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .status-badge-large {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 6px 12px;
      border-radius: 20px;
      font-size: 12px;
      font-weight: 600;
    }

    .status-badge-large.available { background: #dcfce7; color: #16a34a; }
    .status-badge-large.in_use { background: #dbeafe; color: #2563eb; }
    .status-badge-large.maintenance { background: #fef3c7; color: #d97706; }

    .status-badge-large .status-dot {
      width: 6px;
      height: 6px;
      border-radius: 50%;
      background: currentColor;
    }

    .plate-badge {
      padding: 6px 12px;
      background: #f1f5f9;
      border-radius: 6px;
      font-size: 13px;
      font-weight: 600;
      font-family: monospace;
      color: #475569;
    }

    /* Specs Grid */
    .specs-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
    }

    .spec-item {
      background: #f8fafc;
      padding: 12px;
      border-radius: 8px;
    }

    .spec-label {
      display: block;
      font-size: 11px;
      color: #64748b;
      margin-bottom: 4px;
    }

    .spec-value {
      font-size: 13px;
      font-weight: 600;
      color: #1e293b;
    }

    /* Gauge Items */
    .gauge-item {
      margin-bottom: 16px;
    }

    .gauge-item:last-child {
      margin-bottom: 0;
    }

    .gauge-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 8px;
      font-size: 12px;
      color: #64748b;
    }

    .gauge-value {
      font-weight: 600;
      color: #1e293b;
    }

    .gauge-bar-large {
      height: 8px;
      background: #e2e8f0;
      border-radius: 4px;
      overflow: hidden;
    }

    .gauge-fill {
      height: 100%;
      background: linear-gradient(90deg, #22c55e, #4ade80);
      border-radius: 4px;
      transition: width 0.5s ease;
    }

    .gauge-fill.low { background: linear-gradient(90deg, #ef4444, #f87171); }
    .gauge-fill.medium { background: linear-gradient(90deg, #f59e0b, #fbbf24); }
    .gauge-fill.maintenance { background: linear-gradient(90deg, #3b82f6, #60a5fa); }

    /* GPS Stats */
    .gps-stats-row {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
      margin-bottom: 12px;
    }

    .gps-stat-box {
      background: #f0f9ff;
      padding: 14px;
      border-radius: 8px;
      text-align: center;
    }

    .gps-stat-value {
      display: block;
      font-size: 20px;
      font-weight: 700;
      color: #0369a1;
    }

    .gps-stat-label {
      font-size: 11px;
      color: #64748b;
    }

    .current-location {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 12px;
      background: #f8fafc;
      border-radius: 6px;
      font-size: 12px;
      color: #64748b;
    }

    .current-location svg {
      color: #22c55e;
    }

    /* Expenses List */
    .expenses-list {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .expense-row {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 10px 12px;
      background: #f8fafc;
      border-radius: 6px;
    }

    .expense-row.total {
      background: #1e3a5f;
      color: white;
    }

    .expense-row .expense-icon {
      font-size: 16px;
    }

    .expense-row .expense-label {
      flex: 1;
      font-size: 12px;
    }

    .expense-row .expense-amount {
      font-size: 13px;
      font-weight: 600;
    }

    /* Trips List */
    .trips-list {
      display: flex;
      flex-direction: column;
      gap: 10px;
    }

    .trip-item {
      padding: 12px;
      background: #f8fafc;
      border-radius: 8px;
    }

    .trip-route {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 12px;
      color: #1e293b;
      margin-bottom: 6px;
    }

    .trip-route svg {
      color: #94a3b8;
      flex-shrink: 0;
    }

    .trip-start, .trip-end {
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      max-width: 140px;
    }

    .trip-meta {
      display: flex;
      justify-content: space-between;
      font-size: 11px;
      color: #64748b;
    }

    .trip-distance {
      font-weight: 600;
      color: #3b82f6;
    }

    /* Panel Footer */
    .panel-footer {
      display: flex;
      gap: 12px;
      padding: 16px 20px;
      background: #f8fafc;
      border-top: 1px solid #e2e8f0;
    }

    .btn-action-footer {
      flex: 1;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 8px;
      padding: 12px;
      border-radius: 8px;
      font-size: 13px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-action-footer.edit {
      background: #3b82f6;
      color: white;
      border: none;
    }

    .btn-action-footer.edit:hover {
      background: #2563eb;
    }

    .btn-action-footer.costs {
      background: white;
      color: #1e293b;
      border: 1px solid #e2e8f0;
    }

    .btn-action-footer.costs:hover {
      background: #f1f5f9;
      border-color: #cbd5e1;
    }

    /* Responsive */
    @media (max-width: 500px) {
      .detail-panel {
        max-width: 100%;
      }
    }

    /* Legacy styles kept for compatibility */
    .hero-subtitle {
      color: #64748b;
      font-size: 16px;
      margin: 0 0 12px;
    }

    .hero-plate {
      display: inline-block;
      background: #1e293b;
      color: white;
      padding: 8px 16px;
      border-radius: 8px;
      font-family: monospace;
      font-size: 16px;
      font-weight: 600;
    }

    .status-badge {
      display: inline-flex;
      align-items: center;
      padding: 6px 14px;
      border-radius: 20px;
      font-size: 12px;
      font-weight: 600;
      text-transform: uppercase;
    }

    .status-badge.available { background: #dcfce7; color: #16a34a; }
    .status-badge.in_use { background: #dbeafe; color: #2563eb; }
    .status-badge.maintenance { background: #fef3c7; color: #d97706; }

    /* Specs Grid */
    .specs-grid {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 16px;
    }

    .spec-item {
      text-align: center;
      padding: 16px;
      background: #f8fafc;
      border-radius: 10px;
    }

    .spec-label {
      display: block;
      font-size: 11px;
      color: #64748b;
      text-transform: uppercase;
      margin-bottom: 4px;
    }

    .spec-value {
      font-size: 15px;
      font-weight: 600;
      color: #1e293b;
    }

    /* GPS Card */
    .gps-stats {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 16px;
      margin-bottom: 20px;
    }

    .gps-stat {
      text-align: center;
      padding: 16px;
      background: linear-gradient(135deg, #eff6ff, #dbeafe);
      border-radius: 12px;
    }

    .gps-stat-value {
      display: block;
      font-size: 24px;
      font-weight: 700;
      color: #1e3a5f;
    }

    .gps-stat-label {
      font-size: 11px;
      color: #64748b;
      text-transform: uppercase;
    }

    .current-location {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 12px 16px;
      background: #f0fdf4;
      border-radius: 8px;
      font-size: 13px;
      color: #16a34a;
      margin-bottom: 20px;
    }

    .recent-trips h4 {
      font-size: 14px;
      font-weight: 600;
      color: #1e293b;
      margin: 0 0 12px;
    }

    .trips-list {
      display: flex;
      flex-direction: column;
      gap: 12px;
    }

    .trip-item {
      padding: 12px;
      background: #f8fafc;
      border-radius: 8px;
    }

    .trip-route {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 13px;
      color: #1e293b;
      margin-bottom: 6px;
    }

    .trip-route svg { color: #94a3b8; }

    .trip-meta {
      display: flex;
      justify-content: space-between;
      font-size: 11px;
      color: #64748b;
    }

    .trip-distance {
      font-weight: 600;
      color: #3b82f6;
    }

    .no-trips {
      text-align: center;
      padding: 20px;
      color: #94a3b8;
      font-size: 13px;
    }

    /* Gauges */
    .gauge-item {
      margin-bottom: 20px;
    }

    .gauge-item:last-child {
      margin-bottom: 0;
    }

    .gauge-header {
      display: flex;
      justify-content: space-between;
      margin-bottom: 8px;
      font-size: 13px;
      color: #64748b;
    }

    .gauge-value {
      font-weight: 600;
      color: #1e293b;
    }

    .gauge-bar-large {
      height: 10px;
      background: #e2e8f0;
      border-radius: 5px;
      overflow: hidden;
    }

    .gauge-fill.maintenance {
      background: linear-gradient(90deg, #3b82f6, #60a5fa);
    }

    /* Expenses Quick */
    .expenses-summary {
      display: flex;
      flex-direction: column;
      gap: 12px;
    }

    .expense-type {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 12px;
      background: #f8fafc;
      border-radius: 8px;
    }

    .expense-icon {
      font-size: 18px;
    }

    .expense-label {
      flex: 1;
      font-size: 13px;
      color: #64748b;
    }

    .expense-amount {
      font-size: 14px;
      font-weight: 600;
      color: #1e293b;
    }

    .expense-total {
      display: flex;
      justify-content: space-between;
      padding: 12px;
      background: #1e3a5f;
      border-radius: 8px;
      color: white;
      font-weight: 600;
      margin-top: 4px;
    }

    /* Expenses Section */
    .expenses-section {
      margin: 0 40px 40px;
      background: white;
      border-radius: 16px;
      padding: 24px;
      box-shadow: 0 1px 3px rgba(0,0,0,0.08);
    }

    .section-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 20px;
    }

    .section-header h2 {
      display: flex;
      align-items: center;
      gap: 10px;
      font-size: 18px;
      font-weight: 600;
      color: #1e293b;
      margin: 0;
    }

    .period-filter {
      display: flex;
      gap: 8px;
    }

    .period-filter button {
      padding: 8px 16px;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      background: white;
      color: #64748b;
      font-size: 13px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .period-filter button:hover {
      border-color: #cbd5e1;
    }

    .period-filter button.active {
      background: #1e3a5f;
      border-color: #1e3a5f;
      color: white;
    }

    .expenses-table-wrapper {
      overflow-x: auto;
    }

    .expenses-table {
      width: 100%;
      border-collapse: collapse;
    }

    .expenses-table th,
    .expenses-table td {
      padding: 12px 16px;
      text-align: left;
      border-bottom: 1px solid #e2e8f0;
    }

    .expenses-table th {
      background: #f8fafc;
      font-size: 11px;
      font-weight: 600;
      color: #64748b;
      text-transform: uppercase;
    }

    .expenses-table td {
      font-size: 13px;
      color: #1e293b;
    }

    .expense-type-badge {
      display: inline-block;
      padding: 4px 10px;
      border-radius: 12px;
      font-size: 11px;
      font-weight: 600;
    }

    .expense-type-badge.fuel { background: #fef3c7; color: #d97706; }
    .expense-type-badge.maintenance { background: #dbeafe; color: #2563eb; }
    .expense-type-badge.insurance { background: #f3e8ff; color: #9333ea; }
    .expense-type-badge.repair { background: #fee2e2; color: #dc2626; }
    .expense-type-badge.other { background: #f1f5f9; color: #64748b; }

    .amount-cell {
      font-weight: 600;
      color: #1e293b;
    }

    .no-data {
      text-align: center;
      color: #94a3b8;
      padding: 40px !important;
    }

    /* Responsive */
    @media (max-width: 1200px) {
      .detail-content {
        grid-template-columns: 1fr;
      }
    }

    @media (max-width: 768px) {
      .page-header {
        padding: 24px 20px;
      }

      .header-content {
        flex-direction: column;
        gap: 20px;
        text-align: center;
      }

      .header-stats {
        justify-content: center;
      }

      .filters-section {
        flex-wrap: wrap;
        padding: 16px 20px;
      }

      .search-wrapper {
        width: 100%;
        max-width: none;
      }

      .filter-chips {
        width: 100%;
        overflow-x: auto;
      }

      .btn-add-vehicle {
        width: 100%;
        justify-content: center;
      }

      .vehicles-grid {
        grid-template-columns: 1fr;
        padding: 20px;
      }

      .vehicle-hero {
        flex-direction: column;
        align-items: center;
        text-align: center;
      }

      .specs-grid {
        grid-template-columns: repeat(2, 1fr);
      }

      .gps-stats {
        grid-template-columns: 1fr;
      }

      .expenses-section {
        margin: 0 20px 20px;
        padding: 16px;
      }
    }
  `]
})
export class VehiclesComponent implements OnInit {
  vehicles: VehicleExtended[] = [];
  allVehicles: VehicleExtended[] = [];
  filteredVehicles: VehicleExtended[] = [];
  company: Company | null = null;
  isPopupOpen = false;
  selectedVehicle: Vehicle | null = null;
  
  // Detail view
  selectedDetailVehicle: VehicleExtended | null = null;
  vehicleExpenses: VehicleExpense[] = [];
  vehicleTrips: VehicleTrip[] = [];
  currentLocation = '';
  expensesPeriod: 'month' | 'quarter' | 'year' = 'month';
  
  monthlyExpenses = {
    fuel: 0,
    maintenance: 0,
    insurance: 0,
    repair: 0
  };
  
  // Costs popup
  isCostsPopupOpen = false;
  selectedVehicleForCosts: Vehicle | null = null;

  // Filters
  searchQuery = '';
  filterStatus = '';
  filterType = '';

  constructor(
    private router: Router,
    private apiService: ApiService,
    private geocodingService: GeocodingService,
    private ngZone: NgZone,
    private cdr: ChangeDetectorRef,
    private appRef: ApplicationRef
  ) {}

  ngOnInit() {
    if (!this.apiService.isAuthenticated()) {
      this.router.navigate(['/login']);
      return;
    }

    const user = this.apiService.getCurrentUserSync();
    if (user) {
      this.company = {
        id: user.companyId.toString(),
        name: user.companyName,
        type: 'transport',
        subscriptionId: '1'
      } as Company;
    }
    
    this.ngZone.run(() => {
      this.loadVehicles();
    });
  }

  loadVehicles() {
    this.apiService.getVehicles().subscribe({
      next: (vehicles) => {
        this.ngZone.run(() => {
          this.allVehicles = vehicles.map(v => ({
            id: v.id.toString(),
            companyId: v.companyId?.toString() || '',
            name: v.name,
            type: v.type,
            brand: v.brand,
            model: v.model,
            plate: v.plate,
            year: v.year,
            color: v.color,
            status: v.status as 'available' | 'in_use' | 'maintenance',
            hasGPS: v.hasGps,
            mileage: v.mileage,
            assignedDriverId: v.assignedDriverId?.toString(),
            assignedDriverName: v.assignedDriverName,
            fuelLevel: Math.floor(Math.random() * 60) + 40,
            nextMaintenanceKm: v.mileage + Math.floor(Math.random() * 5000) + 1000
          })) as VehicleExtended[];
          this.vehicles = [...this.allVehicles];
          this.filteredVehicles = [...this.allVehicles];
          this.cdr.detectChanges();
          this.appRef.tick();
        });
      },
      error: (err) => console.error('Error loading vehicles:', err)
    });
  }

  filterVehicles() {
    this.filteredVehicles = this.allVehicles.filter(v => {
      const matchesSearch = !this.searchQuery || 
        v.name.toLowerCase().includes(this.searchQuery.toLowerCase()) ||
        v.brand.toLowerCase().includes(this.searchQuery.toLowerCase()) ||
        v.plate.toLowerCase().includes(this.searchQuery.toLowerCase());
      const matchesStatus = !this.filterStatus || v.status === this.filterStatus;
      const matchesType = !this.filterType || v.type === this.filterType;
      return matchesSearch && matchesStatus && matchesType;
    });
  }

  setStatusFilter(status: string) {
    this.filterStatus = status;
    this.filterVehicles();
  }

  getVehiclesByStatus(status: string): Vehicle[] {
    return this.allVehicles.filter(v => v.status === status);
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

  formatNumber(num: number): string {
    return num?.toLocaleString('fr-FR') || '0';
  }

  formatCurrency(amount: number): string {
    return new Intl.NumberFormat('fr-TN', { style: 'currency', currency: 'TND' }).format(amount);
  }

  formatDate(date: Date | string): string {
    if (!date) return '-';
    const d = new Date(date);
    return d.toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric' });
  }

  isMaintenanceSoon(vehicle: any): boolean {
    const remaining = (vehicle.nextMaintenanceKm || 0) - vehicle.mileage;
    return remaining > 0 && remaining < 2000;
  }

  getMaintenanceRemaining(vehicle: any): string {
    const remaining = (vehicle.nextMaintenanceKm || vehicle.mileage + 5000) - vehicle.mileage;
    return this.formatNumber(Math.max(0, remaining));
  }

  getMaintenancePercent(vehicle: any): number {
    const interval = 10000;
    const remaining = (vehicle.nextMaintenanceKm || vehicle.mileage + 5000) - vehicle.mileage;
    return Math.min(100, Math.max(0, (remaining / interval) * 100));
  }

  // Detail View Methods
  openVehicleDetail(vehicle: Vehicle) {
    this.selectedDetailVehicle = vehicle as VehicleExtended;
    this.loadVehicleData(vehicle);
  }

  closeVehicleDetail() {
    this.selectedDetailVehicle = null;
    this.vehicleExpenses = [];
    this.vehicleTrips = [];
    this.currentLocation = '';
  }

  loadVehicleData(vehicle: Vehicle) {
    // Load mock data for demo
    this.vehicleExpenses = this.generateMockExpenses();
    this.vehicleTrips = this.generateMockTrips();
    this.calculateMonthlyExpenses();
    
    // Get current location if GPS enabled
    if (vehicle.hasGPS) {
      this.apiService.getVehicleHistory(parseInt(vehicle.id), new Date(Date.now() - 86400000), new Date(), 1).subscribe({
        next: (positions) => {
          if (positions.length > 0) {
            const pos = positions[0];
            if (pos.address) {
              this.currentLocation = pos.address;
            } else {
              this.geocodingService.reverseGeocode(pos.latitude, pos.longitude).subscribe({
                next: (address) => {
                  this.currentLocation = address || `${pos.latitude.toFixed(5)}, ${pos.longitude.toFixed(5)}`;
                  this.cdr.detectChanges();
                }
              });
            }
          }
        }
      });
    }
  }

  generateMockExpenses(): VehicleExpense[] {
    const types: ('fuel' | 'maintenance' | 'insurance' | 'repair')[] = ['fuel', 'maintenance', 'insurance', 'repair'];
    const expenses: VehicleExpense[] = [];
    for (let i = 0; i < 10; i++) {
      const type = types[Math.floor(Math.random() * types.length)];
      expenses.push({
        id: `exp-${i}`,
        date: new Date(Date.now() - Math.random() * 90 * 24 * 60 * 60 * 1000),
        type,
        description: this.getExpenseDescription(type),
        amount: Math.floor(Math.random() * 500) + 50,
        mileage: Math.floor(Math.random() * 10000) + 50000
      });
    }
    return expenses.sort((a, b) => new Date(b.date).getTime() - new Date(a.date).getTime());
  }

  getExpenseDescription(type: string): string {
    const descriptions: any = {
      fuel: 'Plein de carburant',
      maintenance: 'Vidange et filtres',
      insurance: 'Assurance véhicule',
      repair: 'Réparation freins'
    };
    return descriptions[type] || 'Dépense diverse';
  }

  generateMockTrips(): VehicleTrip[] {
    const trips: VehicleTrip[] = [];
    const cities = ['Tunis Centre', 'La Marsa', 'Sousse', 'Sfax', 'Hammamet', 'Bizerte'];
    for (let i = 0; i < 8; i++) {
      trips.push({
        id: `trip-${i}`,
        startTime: new Date(Date.now() - Math.random() * 30 * 24 * 60 * 60 * 1000),
        endTime: new Date(),
        distance: Math.floor(Math.random() * 200) + 10,
        startAddress: cities[Math.floor(Math.random() * cities.length)],
        endAddress: cities[Math.floor(Math.random() * cities.length)]
      });
    }
    return trips.sort((a, b) => new Date(b.startTime).getTime() - new Date(a.startTime).getTime());
  }

  calculateMonthlyExpenses() {
    const now = new Date();
    const monthStart = new Date(now.getFullYear(), now.getMonth(), 1);
    
    this.monthlyExpenses = { fuel: 0, maintenance: 0, insurance: 0, repair: 0 };
    
    this.vehicleExpenses
      .filter(e => new Date(e.date) >= monthStart)
      .forEach(e => {
        if (e.type in this.monthlyExpenses) {
          this.monthlyExpenses[e.type as keyof typeof this.monthlyExpenses] += e.amount;
        }
      });
  }

  calculateMonthlyDistance(): string {
    const total = this.vehicleTrips.reduce((sum, t) => sum + t.distance, 0);
    return this.formatNumber(total);
  }

  getTotalMonthlyExpenses(): number {
    return Object.values(this.monthlyExpenses).reduce((sum, val) => sum + val, 0);
  }

  setExpensesPeriod(period: 'month' | 'quarter' | 'year') {
    this.expensesPeriod = period;
  }

  getExpenseTypeLabel(type: string): string {
    const labels: any = {
      fuel: 'Carburant',
      maintenance: 'Maintenance',
      insurance: 'Assurance',
      repair: 'Réparation',
      other: 'Autre'
    };
    return labels[type] || type;
  }

  navigate(path: string) {
    this.router.navigate([path]);
  }

  logout() {
    this.apiService.logout();
    this.router.navigate(['/']);
  }

  openAddPopup() {
    this.selectedVehicle = null;
    this.isPopupOpen = true;
  }

  openEditPopup(vehicle: Vehicle) {
    this.selectedVehicle = vehicle;
    this.isPopupOpen = true;
  }

  closePopup() {
    this.isPopupOpen = false;
    this.selectedVehicle = null;
  }

  saveVehicle(vehicleData: Partial<Vehicle>) {
    console.log('Saving vehicle:', vehicleData);
    this.closePopup();
    this.loadVehicles();
  }

  openCostsPopup(vehicle: Vehicle) {
    this.selectedVehicleForCosts = vehicle;
    this.isCostsPopupOpen = true;
  }

  closeCostsPopup() {
    this.isCostsPopupOpen = false;
    this.selectedVehicleForCosts = null;
  }

  // Document Expiry Alert Methods
  hasDocumentAlerts(vehicle: any): boolean {
    return this.isInsuranceExpiring(vehicle) || this.isTechnicalInspectionExpiring(vehicle);
  }

  isInsuranceExpiring(vehicle: any): boolean {
    if (!vehicle.insuranceExpiry) return false;
    const days = this.getDaysUntilExpiry(vehicle.insuranceExpiry);
    return days <= 30;
  }

  isTechnicalInspectionExpiring(vehicle: any): boolean {
    if (!vehicle.technicalInspectionExpiry) return false;
    const days = this.getDaysUntilExpiry(vehicle.technicalInspectionExpiry);
    return days <= 30;
  }

  getDaysUntilExpiry(expiryDate: Date | string): number {
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    const expiry = new Date(expiryDate);
    expiry.setHours(0, 0, 0, 0);
    return Math.ceil((expiry.getTime() - today.getTime()) / (1000 * 60 * 60 * 24));
  }

  getInsuranceStatus(vehicle: any): string {
    if (!vehicle.insuranceExpiry) return 'expired';
    const days = this.getDaysUntilExpiry(vehicle.insuranceExpiry);
    if (days < 0) return 'expired';
    if (days <= 15) return 'critical';
    return 'warning';
  }

  getTechnicalStatus(vehicle: any): string {
    if (!vehicle.technicalInspectionExpiry) return 'expired';
    const days = this.getDaysUntilExpiry(vehicle.technicalInspectionExpiry);
    if (days < 0) return 'expired';
    if (days <= 15) return 'critical';
    return 'warning';
  }

  getInsuranceAlertText(vehicle: any): string {
    if (!vehicle.insuranceExpiry) return 'Assurance non renseignée';
    const days = this.getDaysUntilExpiry(vehicle.insuranceExpiry);
    if (days < 0) return `Assurance expirée`;
    if (days === 0) return `Assurance expire aujourd'hui`;
    return `Assurance: ${days}j`;
  }

  getTechnicalAlertText(vehicle: any): string {
    if (!vehicle.technicalInspectionExpiry) return 'Visite non renseignée';
    const days = this.getDaysUntilExpiry(vehicle.technicalInspectionExpiry);
    if (days < 0) return `Visite expirée`;
    if (days === 0) return `Visite expire aujourd'hui`;
    return `Visite: ${days}j`;
  }
}
