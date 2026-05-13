import { Component, OnInit, OnDestroy, NgZone, ChangeDetectorRef, ApplicationRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { trigger, transition, style, animate, state } from '@angular/animations';
import { Subject, takeUntil } from 'rxjs';
import { ApiService, MaintenanceItemDto, DeclareFreeMaintenancesRequest } from '../services/api.service';
import { GeocodingService } from '../services/geocoding.service';
import { Vehicle, Company } from '../models/types';
import { AppLayoutComponent } from './shared/app-layout.component';
import { VehiclePopupComponent } from './shared/vehicle-popup.component';
import { VehicleCostsPopupComponent } from './shared/vehicle-costs-popup.component';
import { VehicleInfoComponent } from './vehicle-info.component';

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
  imports: [CommonModule, FormsModule, AppLayoutComponent, VehiclePopupComponent, VehicleCostsPopupComponent, VehicleInfoComponent],
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
        style({ opacity: 0 }),
        animate('150ms ease-out', style({ opacity: 1 }))
      ])
    ]),
    trigger('slideUp', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(24px) scale(0.96)' }),
        animate('280ms cubic-bezier(0.16, 1, 0.3, 1)',
          style({ opacity: 1, transform: 'translateY(0) scale(1)' }))
      ]),
      transition(':leave', [
        animate('180ms ease-in', style({ opacity: 0, transform: 'translateY(16px)' }))
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
            <button class="btn-add" *ngIf="isAdmin" (click)="openAddPopup()">
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

          <!-- Table des véhicules -->
          <div class="table-container">
            <table class="vehicles-table" *ngIf="filteredVehicles.length > 0">
              <thead>
                <tr>
                  <th class="col-vehicle">Véhicule</th>
                  <th class="col-plate">Plaque</th>
                  <th class="col-type">Type</th>
                  <th class="col-mileage">Kilométrage</th>
                  <th class="col-driver">Chauffeur</th>
                  <th class="col-status">Statut</th>
                  <th class="col-gps">GPS</th>
                  <th class="col-actions">Actions</th>
                </tr>
              </thead>
              <tbody>
                <tr class="vehicle-row" *ngFor="let vehicle of filteredVehicles" (click)="openVehicleDetail(vehicle)">
                  <td class="col-vehicle">
                    <div class="vehicle-cell">
                      <span class="vehicle-type-icon">
                        <span *ngIf="vehicle.type === 'camion'">🚛</span>
                        <span *ngIf="vehicle.type === 'citadine'">🚗</span>
                        <span *ngIf="vehicle.type === 'suv'">🚙</span>
                        <span *ngIf="vehicle.type === 'utilitaire'">🚐</span>
                        <span *ngIf="!vehicle.type || vehicle.type === 'other'">🚘</span>
                      </span>
                      <div class="vehicle-cell-info">
                        <span class="vehicle-name">{{ vehicle.name }}</span>
                        <span class="vehicle-model">{{ vehicle.brand }} {{ vehicle.model }}</span>
                      </div>
                    </div>
                  </td>
                  <td class="col-plate">
                    <span class="plate-badge">{{ vehicle.plate }}</span>
                  </td>
                  <td class="col-type">{{ getTypeLabel(vehicle.type) }}</td>
                  <td class="col-mileage">
                    <span class="mileage-value">{{ formatNumber(vehicle.mileage) }} km</span>
                  </td>
                  <td class="col-driver">
                    <span *ngIf="vehicle.assignedDriverName" class="driver-name">{{ vehicle.assignedDriverName }}</span>
                    <span *ngIf="!vehicle.assignedDriverName" class="no-driver">—</span>
                  </td>
                  <td class="col-status">
                    <span class="status-badge" [class]="vehicle.status">
                      <span class="status-dot"></span>
                      {{ getStatusLabel(vehicle.status) }}
                    </span>
                  </td>
                  <td class="col-gps">
                    <span class="gps-badge active" *ngIf="vehicle.hasGPS">
                      <svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor"><path d="M12 2C8.13 2 5 5.13 5 9c0 5.25 7 13 7 13s7-7.75 7-13c0-3.87-3.13-7-7-7z"/></svg>
                      GPS
                    </span>
                    <span class="gps-badge inactive" *ngIf="!vehicle.hasGPS">—</span>
                  </td>
                  <td class="col-actions">
                    <div class="actions-cell">
                      <button class="btn-table-action primary" (click)="goToVehicleInfo(vehicle); $event.stopPropagation()" title="Fiche véhicule">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <rect x="3" y="3" width="18" height="18" rx="2" ry="2"/>
                          <line x1="3" y1="9" x2="21" y2="9"/>
                          <line x1="9" y1="21" x2="9" y2="9"/>
                        </svg>
                        <span>Fiche</span>
                      </button>
                    </div>
                  </td>
                </tr>
              </tbody>
            </table>
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
                  <span class="spec-value">
                    {{ formatNumber(selectedDetailVehicle.mileage) }} km
                    <span class="mileage-source" *ngIf="selectedDetailVehicle.hasGPS" title="Données GPS disponibles">📡</span>
                  </span>
                </div>
                <div class="spec-item">
                  <span class="spec-label">Capacité réservoir</span>
                  <span class="spec-value">{{ selectedDetailVehicle.fuelTankCapacity ? selectedDetailVehicle.fuelTankCapacity + ' L' : 'Non spécifié' }}</span>
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

            <!-- Calypso 6 (P6): GPS Info section removed (was "Données GPS") -->

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

            <!-- Documents & Échéances -->
            <div class="info-section">
              <h3 class="section-title">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                  <polyline points="14 2 14 8 20 8"/>
                </svg>
                Documents & Echeances
              </h3>
              <div class="doc-alerts-list">
                <div class="doc-alert-row" [class]="getInsuranceStatus(selectedDetailVehicle)">
                  <span class="doc-icon">🛡️</span>
                  <div class="doc-info">
                    <span class="doc-type">Assurance</span>
                    <span class="doc-expiry">{{ selectedDetailVehicle.insuranceExpiry ? formatDate(selectedDetailVehicle.insuranceExpiry) : 'Non renseignee' }}</span>
                  </div>
                  <span class="doc-badge" [class]="getInsuranceStatus(selectedDetailVehicle)">{{ getInsuranceAlertText(selectedDetailVehicle) }}</span>
                </div>
                <div class="doc-alert-row" [class]="getTechnicalStatus(selectedDetailVehicle)">
                  <span class="doc-icon">🔧</span>
                  <div class="doc-info">
                    <span class="doc-type">Visite technique</span>
                    <span class="doc-expiry">{{ $any(selectedDetailVehicle).technicalInspectionExpiry ? formatDate($any(selectedDetailVehicle).technicalInspectionExpiry) : 'Non renseignee' }}</span>
                  </div>
                  <span class="doc-badge" [class]="getTechnicalStatus(selectedDetailVehicle)">{{ getTechnicalAlertText(selectedDetailVehicle) }}</span>
                </div>
              </div>
            </div>

            <!-- Calypso 6 (P6): Trajets récents section removed per PDF requirement -->

            <!-- Immobilisation: opérateur-set flag to mute every automatic
                 alert (accident, vitesse, batterie, geofence). Use case:
                 véhicule à l'atelier, parking longue durée, boîtier GPS
                 retiré, intervention prévue. La séparation de cette
                 fonctionnalité ici (et pas sur monitoring) évite que l'on
                 confonde avec une commande TCP envoyée au boîtier. -->
            <div class="info-section">
              <h3 class="section-title">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <rect x="3" y="11" width="18" height="11" rx="2" ry="2"/>
                  <path d="M7 11V7a5 5 0 0 1 10 0v4"/>
                </svg>
                Couper les alertes automatiques
              </h3>
              <div class="immobilization-banner" *ngIf="selectedDetailVehicle.isImmobilized">
                <strong>🔒 Véhicule immobilisé</strong>
                <div class="immobilization-reason" *ngIf="selectedDetailVehicle.immobilizationReason">
                  {{ selectedDetailVehicle.immobilizationReason }}
                </div>
                <div class="immobilization-since" *ngIf="selectedDetailVehicle.immobilizationStartedAt">
                  Depuis le {{ formatDate(selectedDetailVehicle.immobilizationStartedAt) }}
                </div>
                <button class="btn-immobilization-clear"
                        (click)="clearVehicleImmobilization(selectedDetailVehicle)">
                  Réactiver les alertes
                </button>
              </div>
              <div *ngIf="!selectedDetailVehicle.isImmobilized" class="immobilization-explain">
                <button class="btn-immobilization-set"
                        (click)="openVehicleImmobilizationDialog(selectedDetailVehicle)">
                  🔒 Immobiliser le véhicule
                </button>
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
            <button class="btn-action-footer info" (click)="goToVehicleInfo(selectedDetailVehicle)" style="background:#6366f1;color:white;">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/>
              </svg>
              Fiche véhicule
            </button>
            <button class="btn-action-footer costs" (click)="openCostsPopup(selectedDetailVehicle)">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="12" y1="1" x2="12" y2="23"/>
                <path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>
              </svg>
              Dépenses
            </button>
            <button class="btn-action-footer free-maint" (click)="openFreeMaintModal(selectedDetailVehicle)" title="Déclarer des entretiens offerts par le concessionnaire">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="20 12 20 22 4 22 4 12"/>
                <rect x="2" y="7" width="20" height="5"/>
                <line x1="12" y1="22" x2="12" y2="7"/>
                <path d="M12 7H7.5a2.5 2.5 0 0 1 0-5C11 2 12 7 12 7z"/>
                <path d="M12 7h4.5a2.5 2.5 0 0 0 0-5C13 2 12 7 12 7z"/>
              </svg>
              Entretiens offerts
            </button>
          </div>
        </div>
      </div>

      <!-- ============== MODAL ENTRETIENS OFFERTS ============== -->
      <div class="free-maint-overlay" *ngIf="isFreeMaintModalOpen" @fadeIn (click)="closeFreeMaintModal()">
        <div class="free-maint-modal" @slideUp (click)="$event.stopPropagation()">
          <!-- Decorative header with gift pattern -->
          <div class="fm-header">
            <div class="fm-header-pattern"></div>
            <button class="fm-close" (click)="closeFreeMaintModal()" aria-label="Fermer">×</button>
            <div class="fm-badge-wrapper">
              <div class="fm-gift-icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <polyline points="20 12 20 22 4 22 4 12"/>
                  <rect x="2" y="7" width="20" height="5"/>
                  <line x1="12" y1="22" x2="12" y2="7"/>
                  <path d="M12 7H7.5a2.5 2.5 0 0 1 0-5C11 2 12 7 12 7z"/>
                  <path d="M12 7h4.5a2.5 2.5 0 0 0 0-5C13 2 12 7 12 7z"/>
                </svg>
              </div>
            </div>
            <h2 class="fm-title">Entretiens offerts</h2>
            <p class="fm-subtitle" *ngIf="freeMaintVehicle">
              <span class="fm-vehicle-chip">
                <span class="fm-vehicle-icon">🚘</span>
                {{ freeMaintVehicle.name }} · {{ freeMaintVehicle.plate }}
              </span>
            </p>
          </div>

          <div class="fm-body">
            <div class="fm-intro">
              <p>Protégez vos collègues : déclarez les entretiens gratuits reçus à l'achat (concessionnaire, garantie constructeur, contrat de service). La prochaine fois que quelqu'un marque cet entretien comme fait, le système appliquera automatiquement le crédit.</p>
            </div>

            <form class="fm-form" (ngSubmit)="submitFreeMaintDeclaration()" #freeMaintForm="ngForm">
              <!-- Liste des entretiens actuellement déclarés -->
              <div class="fm-section" *ngIf="freeMaintExistingItems.length > 0">
                <h3 class="fm-section-title">
                  <span class="fm-section-num">A</span>
                  Entretiens déjà offerts
                </h3>
                <div class="fm-existing-list">
                  <div class="fm-existing-item" *ngFor="let item of freeMaintExistingItems">
                    <div class="fm-existing-main">
                      <div class="fm-existing-name">{{ item.templateName }}</div>
                      <div class="fm-existing-meta">
                        <span class="fm-counter">
                          <strong>{{ item.freeUsesRemaining }}</strong> / {{ item.freeUsesTotal }} restant(s)
                        </span>
                        <span class="fm-source" *ngIf="item.freeSource">· {{ item.freeSource }}</span>
                        <span class="fm-expiry" *ngIf="item.freeExpiryDate">
                          · expire le {{ item.freeExpiryDate | date: 'dd/MM/yyyy' }}
                        </span>
                      </div>
                    </div>
                    <button type="button" class="fm-existing-clear" (click)="clearFreeMaint(item)" title="Supprimer ce crédit">
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <polyline points="3 6 5 6 21 6"/>
                        <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                      </svg>
                    </button>
                  </div>
                </div>
              </div>

              <!-- Formulaire de déclaration -->
              <div class="fm-section">
                <h3 class="fm-section-title">
                  <span class="fm-section-num">{{ freeMaintExistingItems.length > 0 ? 'B' : 'A' }}</span>
                  Nouveau crédit
                </h3>

                <div class="fm-field">
                  <label class="fm-label">Type d'entretien</label>
                  <select class="fm-input" [(ngModel)]="freeMaintForm_templateId" name="templateId" required>
                    <option [ngValue]="null" disabled>— Choisir un modèle d'entretien —</option>
                    <option *ngFor="let t of freeMaintTemplates" [ngValue]="t.id">
                      {{ t.name }} <span *ngIf="t.category"> · {{ t.category }}</span>
                    </option>
                  </select>
                </div>

                <div class="fm-field-row">
                  <div class="fm-field">
                    <label class="fm-label">Nombre offert</label>
                    <div class="fm-stepper">
                      <button type="button" class="fm-step-btn" (click)="adjustFreeMaintCount(-1)">−</button>
                      <input type="number" class="fm-input fm-stepper-input" [(ngModel)]="freeMaintForm_count" name="count" min="1" max="99" required />
                      <button type="button" class="fm-step-btn" (click)="adjustFreeMaintCount(1)">+</button>
                    </div>
                  </div>

                  <div class="fm-field">
                    <label class="fm-label">Source <span class="fm-label-opt">(optionnel)</span></label>
                    <select class="fm-input" [(ngModel)]="freeMaintForm_source" name="source">
                      <option value="">— Choisir —</option>
                      <option value="Achat neuf">Achat neuf</option>
                      <option value="Garantie constructeur">Garantie constructeur</option>
                      <option value="Contrat concessionnaire">Contrat concessionnaire</option>
                      <option value="Promotion">Promotion</option>
                      <option value="Autre">Autre</option>
                    </select>
                  </div>
                </div>

                <div class="fm-field">
                  <label class="fm-label">Date limite <span class="fm-label-opt">(optionnel)</span></label>
                  <input type="date" class="fm-input" [(ngModel)]="freeMaintForm_expiry" name="expiry" />
                </div>

                <div class="fm-field">
                  <label class="fm-label">Note interne <span class="fm-label-opt">(optionnel)</span></label>
                  <textarea class="fm-input fm-textarea" [(ngModel)]="freeMaintForm_notes" name="notes" rows="2" placeholder="Ex: Offert à l'achat du véhicule le 12/03/2026 chez Peugeot Tunis"></textarea>
                </div>
              </div>

              <div class="fm-error" *ngIf="freeMaintError">{{ freeMaintError }}</div>

              <div class="fm-actions">
                <button type="button" class="fm-btn fm-btn-ghost" (click)="closeFreeMaintModal()">
                  Annuler
                </button>
                <button type="submit" class="fm-btn fm-btn-primary" [disabled]="!freeMaintForm_templateId || freeMaintForm_count < 1 || freeMaintSubmitting">
                  <svg *ngIf="!freeMaintSubmitting" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polyline points="20 6 9 17 4 12"/>
                  </svg>
                  <span *ngIf="freeMaintSubmitting">Enregistrement…</span>
                  <span *ngIf="!freeMaintSubmitting">Enregistrer le crédit</span>
                </button>
              </div>
            </form>
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

      <app-vehicle-info
        [isOpen]="isInfoPanelOpen"
        [vehicleId]="selectedInfoVehicleId"
        [vehicleName]="selectedInfoVehicleName"
        (closed)="closeInfoPanel()"
        (saved)="onInfoSaved()"
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

    /* ===== TABLE CONTAINER ===== */
    .table-container {
      flex: 1;
      padding: 16px 24px;
      overflow-x: auto;
    }

    .vehicles-table {
      width: 100%;
      border-collapse: separate;
      border-spacing: 0;
      background: #fff;
      border-radius: 10px;
      border: 1px solid #e2e8f0;
      overflow: hidden;
    }

    .vehicles-table thead {
      background: #f8fafc;
    }

    .vehicles-table th {
      padding: 10px 16px;
      font-size: 10px;
      font-weight: 600;
      color: #64748b;
      text-transform: uppercase;
      letter-spacing: 0.5px;
      text-align: left;
      border-bottom: 1px solid #e2e8f0;
      white-space: nowrap;
    }

    .vehicles-table td {
      padding: 12px 16px;
      font-size: 12px;
      color: #334155;
      border-bottom: 1px solid #f1f5f9;
      vertical-align: middle;
    }

    .vehicle-row {
      cursor: pointer;
      transition: background 0.15s;
    }

    .vehicle-row:hover {
      background: #f8fafc;
    }

    .vehicle-row:last-child td {
      border-bottom: none;
    }

    /* Vehicle cell (icon + name + model) */
    .vehicle-cell {
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .vehicle-type-icon {
      font-size: 20px;
      line-height: 1;
    }

    .vehicle-cell-info {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .vehicle-name {
      font-size: 13px;
      font-weight: 600;
      color: #1e293b;
    }

    .vehicle-model {
      font-size: 11px;
      color: #94a3b8;
    }

    /* Plate badge */
    .plate-badge {
      display: inline-block;
      background: #f1f5f9;
      padding: 4px 10px;
      border-radius: 4px;
      font-size: 12px;
      font-weight: 600;
      color: #475569;
      font-family: monospace;
    }

    /* Mileage */
    .mileage-value {
      font-weight: 600;
      color: #1e293b;
      font-size: 12px;
    }

    /* Driver */
    .driver-name {
      font-size: 12px;
      color: #334155;
    }

    .no-driver {
      color: #cbd5e1;
    }

    /* Status badge */
    .status-badge {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 4px 10px;
      border-radius: 20px;
      font-size: 11px;
      font-weight: 600;
      white-space: nowrap;
    }

    .status-badge .status-dot {
      width: 6px;
      height: 6px;
      border-radius: 50%;
      background: currentColor;
    }

    .status-badge.available { background: #dcfce7; color: #16a34a; }
    .status-badge.in_use { background: #dbeafe; color: #2563eb; }
    .status-badge.maintenance { background: #fef3c7; color: #d97706; }

    /* GPS badge */
    .gps-badge {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      font-size: 11px;
      font-weight: 500;
    }

    .gps-badge.active {
      color: #3b82f6;
    }

    .gps-badge.inactive {
      color: #cbd5e1;
    }

    /* Actions cell */
    .actions-cell {
      display: flex;
      align-items: center;
      gap: 6px;
    }

    .btn-table-action {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 5px 10px;
      border-radius: 4px;
      border: 1px solid #e2e8f0;
      background: white;
      color: #64748b;
      cursor: pointer;
      font-size: 11px;
      font-weight: 500;
      font-family: var(--font-family);
      transition: all 0.15s;
      white-space: nowrap;
    }

    .btn-table-action:hover {
      background: #f8fafc;
      border-color: #cbd5e1;
      color: #334155;
    }

    .btn-table-action.primary {
      background: #6366f1;
      border-color: #6366f1;
      color: white;
    }

    .btn-table-action.primary:hover {
      background: #4f46e5;
      border-color: #4f46e5;
    }

    /* Column widths */
    .col-vehicle { min-width: 200px; }
    .col-plate { min-width: 120px; }
    .col-type { min-width: 90px; }
    .col-mileage { min-width: 110px; }
    .col-driver { min-width: 140px; }
    .col-status { min-width: 110px; }
    .col-gps { min-width: 60px; }
    .col-actions { min-width: 120px; }

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
      top: 42px;
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(0, 0, 0, 0.5);
      display: flex;
      justify-content: flex-end;
      z-index: 1050;
    }

    .detail-panel {
      width: 100%;
      max-width: 480px;
      height: 100%;
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

    /* Document Alerts */
    .doc-alerts-list { display:flex; flex-direction:column; gap:6px; }
    .doc-alert-row { display:flex; align-items:center; gap:10px; padding:8px 10px; border-radius:6px; background:#f8fafc; }
    .doc-alert-row.expired { background:#fef2f2; }
    .doc-alert-row.critical { background:#fff7ed; }
    .doc-alert-row.warning { background:#fefce8; }
    .doc-icon { font-size:16px; }
    .doc-info { flex:1; display:flex; flex-direction:column; gap:1px; }
    .doc-type { font-size:12px; font-weight:600; color:#1e293b; }
    .doc-expiry { font-size:11px; color:#64748b; }
    .doc-badge { padding:2px 8px; border-radius:3px; font-size:10px; font-weight:600; white-space:nowrap; }
    .doc-badge.expired { background:#fecaca; color:#dc2626; }
    .doc-badge.critical { background:#fed7aa; color:#ea580c; }
    .doc-badge.warning { background:#fef08a; color:#a16207; }

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

    .btn-action-footer.free-maint {
      background: linear-gradient(135deg, #f59e0b 0%, #ea580c 100%);
      color: white;
      border: none;
      box-shadow: 0 2px 8px rgba(234, 88, 12, 0.25);
      position: relative;
      overflow: hidden;
    }

    .btn-action-footer.free-maint::before {
      content: '';
      position: absolute;
      inset: 0;
      background: linear-gradient(135deg, rgba(255,255,255,0.2), transparent);
      opacity: 0;
      transition: opacity 0.2s;
    }

    .btn-action-footer.free-maint:hover {
      transform: translateY(-1px);
      box-shadow: 0 4px 14px rgba(234, 88, 12, 0.35);
    }

    .btn-action-footer.free-maint:hover::before {
      opacity: 1;
    }

    /* ================ MODAL ENTRETIENS OFFERTS ================ */
    .free-maint-overlay {
      position: fixed;
      inset: 0;
      background: rgba(15, 23, 42, 0.62);
      backdrop-filter: blur(6px);
      z-index: 10000;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 20px;
    }

    .free-maint-modal {
      width: 100%;
      max-width: 560px;
      max-height: calc(100vh - 40px);
      background: white;
      border-radius: 20px;
      overflow: hidden;
      display: flex;
      flex-direction: column;
      box-shadow: 0 25px 70px -15px rgba(15, 23, 42, 0.5);
      font-family: system-ui, -apple-system, "Segoe UI", sans-serif;
    }

    .fm-header {
      position: relative;
      padding: 36px 28px 28px;
      background: linear-gradient(145deg, #fb923c 0%, #ea580c 60%, #c2410c 100%);
      color: white;
      overflow: hidden;
    }

    .fm-header-pattern {
      position: absolute;
      inset: 0;
      opacity: 0.18;
      background-image:
        radial-gradient(circle at 20% 30%, white 2px, transparent 2px),
        radial-gradient(circle at 80% 20%, white 1.5px, transparent 1.5px),
        radial-gradient(circle at 50% 80%, white 2.5px, transparent 2.5px),
        radial-gradient(circle at 15% 90%, white 1.5px, transparent 1.5px),
        radial-gradient(circle at 90% 65%, white 2px, transparent 2px);
      background-size: 90px 90px, 60px 60px, 120px 120px, 70px 70px, 100px 100px;
      pointer-events: none;
    }

    .fm-close {
      position: absolute;
      top: 14px;
      right: 14px;
      width: 36px;
      height: 36px;
      border-radius: 50%;
      border: none;
      background: rgba(255,255,255,0.2);
      color: white;
      font-size: 22px;
      line-height: 1;
      cursor: pointer;
      transition: all 0.2s;
      z-index: 2;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .fm-close:hover {
      background: rgba(255,255,255,0.32);
      transform: rotate(90deg);
    }

    .fm-badge-wrapper {
      position: relative;
      display: flex;
      justify-content: center;
      margin-bottom: 14px;
      z-index: 1;
    }

    .fm-gift-icon {
      width: 68px;
      height: 68px;
      border-radius: 20px;
      background: white;
      color: #ea580c;
      display: flex;
      align-items: center;
      justify-content: center;
      box-shadow: 0 12px 30px -8px rgba(0,0,0,0.35);
      animation: fm-wiggle 3s ease-in-out infinite;
    }

    .fm-gift-icon svg {
      width: 36px;
      height: 36px;
    }

    @keyframes fm-wiggle {
      0%, 92%, 100% { transform: rotate(0deg); }
      94% { transform: rotate(-6deg); }
      96% { transform: rotate(6deg); }
      98% { transform: rotate(-4deg); }
    }

    .fm-title {
      position: relative;
      margin: 0 0 6px;
      text-align: center;
      font-size: 24px;
      font-weight: 800;
      letter-spacing: -0.02em;
      z-index: 1;
    }

    .fm-subtitle {
      position: relative;
      margin: 0;
      text-align: center;
      font-size: 13px;
      z-index: 1;
    }

    .fm-vehicle-chip {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 6px 14px;
      background: rgba(255,255,255,0.22);
      border: 1px solid rgba(255,255,255,0.3);
      border-radius: 999px;
      backdrop-filter: blur(6px);
      font-weight: 600;
    }

    .fm-body {
      flex: 1;
      overflow-y: auto;
      padding: 24px 28px 28px;
    }

    .fm-intro {
      background: #fffbeb;
      border-left: 4px solid #f59e0b;
      border-radius: 10px;
      padding: 14px 16px;
      margin-bottom: 20px;
    }

    .fm-intro p {
      margin: 0;
      font-size: 13px;
      line-height: 1.55;
      color: #78350f;
    }

    .fm-section {
      margin-bottom: 22px;
    }

    .fm-section-title {
      display: flex;
      align-items: center;
      gap: 10px;
      margin: 0 0 14px;
      font-size: 14px;
      font-weight: 700;
      color: #0f172a;
      text-transform: uppercase;
      letter-spacing: 0.04em;
    }

    .fm-section-num {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 24px;
      height: 24px;
      border-radius: 50%;
      background: #1e293b;
      color: white;
      font-size: 12px;
      font-weight: 700;
    }

    .fm-existing-list {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .fm-existing-item {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 14px 16px;
      background: linear-gradient(135deg, #fffbeb 0%, #fef3c7 100%);
      border: 1px solid #fde68a;
      border-radius: 12px;
    }

    .fm-existing-main {
      flex: 1;
      min-width: 0;
    }

    .fm-existing-name {
      font-size: 14px;
      font-weight: 700;
      color: #0f172a;
      margin-bottom: 2px;
    }

    .fm-existing-meta {
      font-size: 12px;
      color: #78350f;
    }

    .fm-counter strong {
      color: #b45309;
      font-size: 13px;
    }

    .fm-existing-clear {
      width: 32px;
      height: 32px;
      border-radius: 8px;
      border: none;
      background: rgba(234, 88, 12, 0.1);
      color: #b45309;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.15s;
    }

    .fm-existing-clear:hover {
      background: #b45309;
      color: white;
    }

    .fm-field {
      display: flex;
      flex-direction: column;
      gap: 6px;
      margin-bottom: 14px;
    }

    .fm-field-row {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 14px;
    }

    .fm-label {
      font-size: 12px;
      font-weight: 600;
      color: #475569;
      text-transform: uppercase;
      letter-spacing: 0.04em;
    }

    .fm-label-opt {
      font-weight: 400;
      text-transform: none;
      letter-spacing: 0;
      color: #94a3b8;
    }

    .fm-input {
      width: 100%;
      padding: 11px 14px;
      border-radius: 10px;
      border: 1.5px solid #e2e8f0;
      background: white;
      font-size: 14px;
      color: #0f172a;
      transition: all 0.15s;
      font-family: inherit;
    }

    .fm-input:focus {
      outline: none;
      border-color: #ea580c;
      box-shadow: 0 0 0 3px rgba(234, 88, 12, 0.12);
    }

    .fm-textarea {
      resize: vertical;
      min-height: 60px;
    }

    .fm-stepper {
      display: flex;
      align-items: stretch;
      gap: 0;
      border-radius: 10px;
      overflow: hidden;
      border: 1.5px solid #e2e8f0;
      background: white;
    }

    .fm-stepper:focus-within {
      border-color: #ea580c;
      box-shadow: 0 0 0 3px rgba(234, 88, 12, 0.12);
    }

    .fm-step-btn {
      width: 42px;
      background: #f8fafc;
      border: none;
      color: #1e293b;
      font-size: 20px;
      font-weight: 700;
      cursor: pointer;
      transition: background 0.15s;
    }

    .fm-step-btn:hover {
      background: #fed7aa;
      color: #c2410c;
    }

    .fm-stepper-input {
      border: none;
      border-radius: 0;
      border-left: 1px solid #e2e8f0;
      border-right: 1px solid #e2e8f0;
      text-align: center;
      font-size: 16px;
      font-weight: 700;
      color: #ea580c;
    }

    .fm-stepper-input:focus {
      box-shadow: none;
    }

    .fm-error {
      background: #fef2f2;
      border: 1px solid #fecaca;
      color: #b91c1c;
      padding: 10px 14px;
      border-radius: 10px;
      font-size: 13px;
      margin-bottom: 14px;
    }

    .fm-actions {
      display: flex;
      gap: 10px;
      margin-top: 8px;
    }

    .fm-btn {
      flex: 1;
      padding: 13px 20px;
      border-radius: 11px;
      font-size: 14px;
      font-weight: 700;
      cursor: pointer;
      transition: all 0.18s;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 8px;
      font-family: inherit;
      border: none;
    }

    .fm-btn-ghost {
      background: #f1f5f9;
      color: #475569;
    }

    .fm-btn-ghost:hover {
      background: #e2e8f0;
      color: #0f172a;
    }

    .fm-btn-primary {
      background: linear-gradient(135deg, #f59e0b 0%, #ea580c 100%);
      color: white;
      box-shadow: 0 4px 14px -2px rgba(234, 88, 12, 0.45);
    }

    .fm-btn-primary:hover:not(:disabled) {
      transform: translateY(-1px);
      box-shadow: 0 6px 20px -4px rgba(234, 88, 12, 0.55);
    }

    .fm-btn-primary:disabled {
      opacity: 0.5;
      cursor: not-allowed;
      box-shadow: none;
    }

    @media (max-width: 600px) {
      .free-maint-modal {
        max-width: 100%;
      }
      .fm-field-row {
        grid-template-columns: 1fr;
      }
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

    .mileage-source {
      font-size: 12px;
      margin-left: 4px;
    }

    .btn-sync-mileage {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 8px 12px;
      background: linear-gradient(135deg, #3b82f6 0%, #2563eb 100%);
      color: white;
      border: none;
      border-radius: 6px;
      font-size: 12px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.2s;
      margin-top: 12px;
    }

    /* ── Immobilisation block in the vehicle detail panel ──────────────
       Distinct visual treatment (purple/indigo) so the operator can't
       confuse it with the "Modifier" / "Dépenses" footer buttons. */
    .immobilization-banner {
      background: rgba(99, 102, 241, 0.10);
      border-left: 3px solid #6366f1;
      border-radius: 6px;
      padding: 12px 14px;
      color: #4338ca;
      font-size: 13px;
    }
    .immobilization-banner strong {
      display: block;
      font-size: 14px;
      margin-bottom: 6px;
    }
    .immobilization-reason {
      font-style: italic;
      color: #6366f1;
      margin-bottom: 4px;
    }
    .immobilization-since {
      font-size: 11px;
      color: #818cf8;
      margin-bottom: 10px;
    }
    .immobilization-explain {
      /* No descriptive paragraph anymore — just wraps the toggle
         button so the section keeps a consistent layout when the
         vehicle is NOT immobilised. */
    }
    .btn-immobilization-set,
    .btn-immobilization-clear {
      display: block;
      width: 100%;
      padding: 8px 12px;
      font-size: 13px;
      font-weight: 600;
      border: 1px solid #6366f1;
      border-radius: 6px;
      cursor: pointer;
      transition: all 0.2s;
    }
    .btn-immobilization-set {
      background: white;
      color: #4338ca;
    }
    .btn-immobilization-set:hover {
      background: rgba(99, 102, 241, 0.08);
    }
    .btn-immobilization-clear {
      background: #4338ca;
      color: white;
    }
    .btn-immobilization-clear:hover {
      background: #3730a3;
    }

    .btn-sync-mileage:hover:not(:disabled) {
      box-shadow: 0 2px 8px rgba(59, 130, 246, 0.4);
    }

    .btn-sync-mileage:disabled {
      opacity: 0.6;
      cursor: not-allowed;
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

      .table-container {
        padding: 12px;
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
export class VehiclesComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
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
  isInfoPanelOpen = false;
  selectedInfoVehicleId: number | null = null;
  selectedInfoVehicleName: string = '';

  // Free maintenance modal
  isFreeMaintModalOpen = false;
  freeMaintVehicle: Vehicle | null = null;
  freeMaintTemplates: any[] = [];
  freeMaintExistingItems: MaintenanceItemDto[] = [];
  freeMaintForm_templateId: number | null = null;
  freeMaintForm_count = 2;
  freeMaintForm_source = 'Achat neuf';
  freeMaintForm_expiry = '';
  freeMaintForm_notes = '';
  freeMaintError: string | null = null;
  freeMaintSubmitting = false;

  // Filters
  searchQuery = '';
  filterStatus = '';
  filterType = '';
  
  // Sync mileage
  syncingMileage = false;
  isAdmin = false;

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
      this.isAdmin = user.isCompanyAdmin;
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
    this.apiService.getVehicles().pipe(takeUntil(this.destroy$)).subscribe({
      next: (vehicles) => {
        this.ngZone.run(() => {
          this.allVehicles = vehicles.map(v => ({
            id: v.id?.toString() || '',
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
            fuelTankCapacity: v.fuelTankCapacity,
            assignedDriverId: v.assignedDriverId?.toString(),
            assignedDriverName: v.assignedDriverName,
            fuelLevel: Math.floor(Math.random() * 60) + 40,
            nextMaintenanceKm: v.mileage + Math.floor(Math.random() * 5000) + 1000,
            // Immobilisation operator-set state (propagated to the detail
            // panel toggle that mutes auto-alerts for this vehicle).
            isImmobilized: !!(v as any).isImmobilized,
            immobilizationReason: (v as any).immobilizationReason ?? null,
            immobilizationStartedAt: (v as any).immobilizationStartedAt ?? null
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
    // Load vehicle details from API
    this.apiService.getVehicle(parseInt(vehicle.id)).pipe(takeUntil(this.destroy$)).subscribe({
      next: (details) => {
        if (details) {
          // Update selectedDetailVehicle with API data
          this.selectedDetailVehicle = {
            ...this.selectedDetailVehicle,
            ...details,
            fuelLevel: details.fuelLevel || 75,
            nextMaintenanceKm: details.nextMaintenanceKm || 5000,
            totalTrips: details.totalTrips || 0,
            monthlyDistance: details.monthlyDistance || 0,
            monthlyFuelCost: details.monthlyFuelCost || 0
          } as VehicleExtended;
          this.cdr.detectChanges();
        }
      },
      error: (err) => {
        console.error('Failed to load vehicle details:', err);
      }
    });

    // Load vehicle trips from API
    const startDate = new Date();
    startDate.setMonth(startDate.getMonth() - 1);
    this.apiService.getVehicleTrips(parseInt(vehicle.id), startDate, new Date()).pipe(takeUntil(this.destroy$)).subscribe({
      next: (trips) => {
        this.vehicleTrips = trips.map((t: any) => ({
          id: t.id?.toString() || '',
          startTime: new Date(t.startTime),
          endTime: new Date(t.endTime),
          distance: t.distanceKm || 0,
          startAddress: t.startAddress || 'N/A',
          endAddress: t.endAddress || 'N/A'
        }));
        this.cdr.detectChanges();
      },
      error: () => {
        this.vehicleTrips = [];
      }
    });

    // Load costs/expenses from API
    this.apiService.getCosts({ vehicleId: parseInt(vehicle.id) }).pipe(takeUntil(this.destroy$)).subscribe({
      next: (costs) => {
        this.vehicleExpenses = costs.map((c: any) => ({
          id: c.id?.toString() || '',
          date: new Date(c.date),
          type: c.type || 'other',
          description: c.description || '',
          amount: c.amount || 0,
          mileage: c.mileage
        }));
        this.calculateMonthlyExpenses();
        this.cdr.detectChanges();
      },
      error: () => {
        this.vehicleExpenses = [];
        this.calculateMonthlyExpenses();
      }
    });
    
    // Get current location if GPS enabled
    if (vehicle.hasGPS) {
      this.apiService.getVehicleHistory(parseInt(vehicle.id), new Date(Date.now() - 86400000), new Date(), 1).pipe(takeUntil(this.destroy$)).subscribe({
        next: (positions) => {
          if (positions.length > 0) {
            const pos = positions[0];
            if (pos.address) {
              this.currentLocation = pos.address;
            } else {
              this.geocodingService.reverseGeocode(pos.latitude, pos.longitude).pipe(takeUntil(this.destroy$)).subscribe({
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

    // Load GPS stats for the month
    if (vehicle.hasGPS) {
      const monthStart = new Date();
      monthStart.setDate(1);
      monthStart.setHours(0, 0, 0, 0);
      this.apiService.getVehicleGpsStats(parseInt(vehicle.id), monthStart, new Date()).pipe(takeUntil(this.destroy$)).subscribe({
        next: (stats) => {
          if (this.selectedDetailVehicle) {
            this.selectedDetailVehicle.totalTrips = stats.tripCount || 0;
            this.selectedDetailVehicle.monthlyDistance = stats.totalDistanceKm || 0;
            this.cdr.detectChanges();
          }
        }
      });
    }
  }

  syncMileageFromGps() {
    if (!this.selectedDetailVehicle || this.syncingMileage) return;
    
    this.syncingMileage = true;
    const vehicleId = parseInt(this.selectedDetailVehicle.id);
    
    this.apiService.syncVehicleMileage(vehicleId).pipe(takeUntil(this.destroy$)).subscribe({
      next: (result: any) => {
        this.syncingMileage = false;
        if (result.updated && this.selectedDetailVehicle) {
          this.selectedDetailVehicle.mileage = result.newMileage;
          // Update in the lists too
          const vehicleInList = this.allVehicles.find(v => v.id === this.selectedDetailVehicle!.id);
          if (vehicleInList) {
            vehicleInList.mileage = result.newMileage;
          }
          this.cdr.detectChanges();
        }
      },
      error: (err) => {
        this.syncingMileage = false;
        console.error('Failed to sync mileage:', err);
      }
    });
  }

  /**
   * Opens a minimal prompt for the immobilisation reason, then POSTs to
   * /api/vehicles/{id}/immobilize. The reason is optional but strongly
   * recommended for the audit trail — a second admin should know why
   * the alerts were muted without asking around.
   *
   * Strictly server-side: this never sends any TCP command to the GPS
   * boîtier (don't confuse with the GO/STOP system).
   */
  openVehicleImmobilizationDialog(vehicle: VehicleExtended): void {
    const reason = window.prompt(
      `Couper les alertes automatiques pour ${vehicle.plate || vehicle.name} ?\n\n` +
      'Indiquez une raison (atelier, parking longue durée, boîtier déposé, …).\n' +
      'Laissez vide si la raison n\'a pas besoin d\'être tracée.\n\n' +
      'ℹ️ Aucune commande n\'est envoyée au boîtier GPS.',
      ''
    );
    if (reason === null) return; // user cancelled

    this.apiService.immobilizeVehicle(parseInt(vehicle.id), reason.trim() || null)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (result: any) => {
          vehicle.isImmobilized = true;
          vehicle.immobilizationReason = result?.reason ?? null;
          vehicle.immobilizationStartedAt = result?.startedAt ?? new Date().toISOString();
          // Keep the list-side cached copy in sync (sidebar / list filters
          // might read it)
          const inList = this.allVehicles.find(v => v.id === vehicle.id);
          if (inList) {
            inList.isImmobilized = true;
            inList.immobilizationReason = vehicle.immobilizationReason;
            inList.immobilizationStartedAt = vehicle.immobilizationStartedAt;
          }
          this.cdr.detectChanges();
        },
        error: (err) => {
          console.error('Failed to immobilize vehicle:', err);
          window.alert('Impossible d\'immobiliser le véhicule — vérifiez la connexion.');
        }
      });
  }

  /**
   * Re-enables every automatic alert service for the vehicle. We don't
   * ask for confirmation because operators flip this back the second the
   * vehicle leaves the garage and an extra click would just slow them
   * down.
   */
  clearVehicleImmobilization(vehicle: VehicleExtended): void {
    this.apiService.clearVehicleImmobilization(parseInt(vehicle.id))
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          vehicle.isImmobilized = false;
          vehicle.immobilizationReason = null;
          vehicle.immobilizationStartedAt = null;
          const inList = this.allVehicles.find(v => v.id === vehicle.id);
          if (inList) {
            inList.isImmobilized = false;
            inList.immobilizationReason = null;
            inList.immobilizationStartedAt = null;
          }
          this.cdr.detectChanges();
        },
        error: (err) => {
          console.error('Failed to clear vehicle immobilization:', err);
          window.alert('Impossible de réactiver les alertes — vérifiez la connexion.');
        }
      });
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

  saveVehicle(vehicleData: any) {
    console.log('Saving vehicle:', vehicleData);

    // Build payload matching backend CreateVehicleCommand / UpdateVehicleCommand
    const payload: any = {
      name: vehicleData.name,
      type: vehicleData.type || 'citadine',
      brand: vehicleData.brand || null,
      model: vehicleData.model || null,
      plate: vehicleData.plate || null,
      year: vehicleData.year || null,
      color: vehicleData.color || null,
      mileage: vehicleData.mileage || 0,
      fuelType: vehicleData.fuelType || null,
      fuelTankCapacity: vehicleData.fuelTankCapacity || null,
      status: vehicleData.status || 'available'
    };

    // GPS device handling
    if (vehicleData.hasGPS) {
      if (vehicleData.gpsDeviceId) {
        payload.gpsDeviceId = parseInt(vehicleData.gpsDeviceId, 10);
      } else if (vehicleData.gpsImei) {
        payload.newGpsDevice = {
          deviceUid: vehicleData.gpsImei,
          simNumber: vehicleData.gpsSimNumber || null,
          simOperator: vehicleData.gpsSimOperator || null,
          brand: vehicleData.gpsBrand || null,
          model: vehicleData.gpsModel || null,
          installationDate: vehicleData.gpsInstallationDate || null
        };
      }
    }

    if (this.selectedVehicle && this.selectedVehicle.id) {
      payload.id = parseInt(this.selectedVehicle.id, 10);
      this.apiService.updateVehicle(payload.id, payload).pipe(takeUntil(this.destroy$)).subscribe({
        next: () => {
          this.closePopup();
          this.loadVehicles();
        },
        error: (err) => {
          console.error('Error updating vehicle:', err);
          alert('Erreur lors de la mise à jour du véhicule');
        }
      });
    } else {
      this.apiService.createVehicle(payload).pipe(takeUntil(this.destroy$)).subscribe({
        next: () => {
          this.closePopup();
          this.loadVehicles();
        },
        error: (err) => {
          console.error('Error creating vehicle:', err);
          alert('Erreur lors de la création du véhicule');
        }
      });
    }
  }

  openCostsPopup(vehicle: Vehicle) {
    this.selectedVehicleForCosts = vehicle;
    this.isCostsPopupOpen = true;
  }

  closeCostsPopup() {
    this.isCostsPopupOpen = false;
    this.selectedVehicleForCosts = null;
  }

  // ============ FREE MAINTENANCE MODAL ============

  openFreeMaintModal(vehicle: VehicleExtended | null) {
    if (!vehicle) return;
    this.freeMaintVehicle = vehicle;
    this.freeMaintError = null;
    this.freeMaintSubmitting = false;
    this.freeMaintForm_templateId = null;
    this.freeMaintForm_count = 2;
    this.freeMaintForm_source = 'Achat neuf';
    this.freeMaintForm_expiry = '';
    this.freeMaintForm_notes = '';
    this.freeMaintExistingItems = [];
    this.isFreeMaintModalOpen = true;

    // Load templates (all active)
    this.apiService.getMaintenanceTemplates({ isActive: true, pageSize: 200 }).subscribe({
      next: (result) => {
        const items = (result?.items as any[]) || [];
        this.freeMaintTemplates = items.filter(t => t.isActive !== false);
        if (this.freeMaintTemplates.length > 0 && this.freeMaintForm_templateId == null) {
          this.freeMaintForm_templateId = this.freeMaintTemplates[0].id;
        }
        this.cdr.detectChanges();
      },
      error: () => {
        this.freeMaintTemplates = [];
        this.cdr.detectChanges();
      }
    });

    // Load existing free credits on this vehicle
    const vehicleIdNum = typeof vehicle.id === 'string' ? parseInt(vehicle.id, 10) : vehicle.id;
    if (vehicleIdNum) {
      this.apiService.getVehicleMaintenanceStatus(vehicleIdNum).subscribe({
        next: (status) => {
          this.freeMaintExistingItems = (status?.maintenanceItems || [])
            .filter(i => (i.freeUsesTotal ?? 0) > 0);
          this.cdr.detectChanges();
        },
        error: () => {
          this.freeMaintExistingItems = [];
          this.cdr.detectChanges();
        }
      });
    }
  }

  closeFreeMaintModal() {
    this.isFreeMaintModalOpen = false;
    this.freeMaintVehicle = null;
    this.freeMaintError = null;
  }

  adjustFreeMaintCount(delta: number) {
    const next = (this.freeMaintForm_count || 0) + delta;
    if (next >= 1 && next <= 99) {
      this.freeMaintForm_count = next;
    }
  }

  submitFreeMaintDeclaration() {
    if (!this.freeMaintVehicle || !this.freeMaintForm_templateId) return;
    if (this.freeMaintForm_count < 1) {
      this.freeMaintError = "Le nombre d'entretiens offerts doit être supérieur à zéro.";
      return;
    }

    const vehicleIdNum = typeof this.freeMaintVehicle.id === 'string'
      ? parseInt(this.freeMaintVehicle.id, 10)
      : this.freeMaintVehicle.id;

    if (!vehicleIdNum) {
      this.freeMaintError = "Identifiant véhicule invalide.";
      return;
    }

    this.freeMaintSubmitting = true;
    this.freeMaintError = null;

    const request: DeclareFreeMaintenancesRequest = {
      vehicleId: vehicleIdNum,
      templateId: this.freeMaintForm_templateId,
      count: this.freeMaintForm_count,
      source: this.freeMaintForm_source || undefined,
      expiryDate: this.freeMaintForm_expiry
        ? new Date(this.freeMaintForm_expiry).toISOString()
        : undefined,
      notes: this.freeMaintForm_notes || undefined
    };

    this.apiService.declareFreeMaintenances(request).subscribe({
      next: () => {
        this.freeMaintSubmitting = false;
        this.closeFreeMaintModal();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.freeMaintSubmitting = false;
        this.freeMaintError = err?.error?.message || err?.message || "Erreur lors de la déclaration. Réessayez.";
        this.cdr.detectChanges();
      }
    });
  }

  clearFreeMaint(item: MaintenanceItemDto) {
    if (!confirm(`Supprimer le crédit "${item.templateName}" (${item.freeUsesRemaining}/${item.freeUsesTotal} restant(s)) ?`)) return;

    this.apiService.clearFreeMaintenance(item.scheduleId).subscribe({
      next: () => {
        this.freeMaintExistingItems = this.freeMaintExistingItems.filter(i => i.scheduleId !== item.scheduleId);
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.freeMaintError = err?.error?.message || "Erreur lors de la suppression.";
        this.cdr.detectChanges();
      }
    });
  }

  goToVehicleInfo(vehicle: any) {
    this.selectedInfoVehicleId = vehicle.id;
    this.selectedInfoVehicleName = (vehicle.brand || '') + ' ' + (vehicle.model || '') + (vehicle.plate ? ' (' + vehicle.plate + ')' : '');
    this.isInfoPanelOpen = true;
    this.cdr.detectChanges();
  }

  closeInfoPanel() {
    this.isInfoPanelOpen = false;
    this.selectedInfoVehicleId = null;
    this.cdr.detectChanges();
  }

  onInfoSaved() {
    this.loadVehicles();
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

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
