import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AppLayoutComponent } from './shared/app-layout.component';
import { trigger, transition, style, animate } from '@angular/animations';

interface AccidentClaim {
  id: string;
  claimNumber: string;
  vehicleId: string;
  vehicleName: string;
  vehiclePlate: string;
  driverName: string;
  accidentDate: Date;
  accidentTime: string;
  location: string;
  weatherConditions?: string;
  roadConditions?: string;
  description: string;
  status: 'draft' | 'submitted' | 'under_review' | 'approved' | 'rejected' | 'closed';
  severity: 'minor' | 'moderate' | 'major' | 'total_loss';
  estimatedDamage: number;
  approvedAmount?: number;
  thirdPartyInvolved: boolean;
  thirdPartyName?: string;
  thirdPartyPhone?: string;
  thirdPartyVehicle?: string;
  thirdPartyInsurance?: string;
  policeReportNumber?: string;
  damagedZones?: string[];
  createdAt: Date;
  updatedAt: Date;
  photos: string[];
}

@Component({
  selector: 'app-accident-claims',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent],
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 }))
      ])
    ]),
    trigger('slideIn', [
      transition(':enter', [
        style({ transform: 'translateX(100%)' }),
        animate('300ms ease-out', style({ transform: 'translateX(0)' }))
      ])
    ])
  ],
  template: `
    <app-layout>
      <div class="claims-page">
        <!-- Filter Bar (comme maintenance) -->
        <div class="filter-bar">
          <div class="search-wrapper">
            <svg class="search-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/>
            </svg>
            <input type="text" class="search-input" placeholder="Rechercher..." [(ngModel)]="searchQuery" (input)="filterClaims()">
          </div>
          <select class="filter-select" [(ngModel)]="statusFilter" (change)="filterClaims()">
            <option value="">Tous les statuts</option>
            <option value="draft">Brouillon</option>
            <option value="submitted">Soumis</option>
            <option value="under_review">En révision</option>
            <option value="approved">Approuvé</option>
            <option value="rejected">Rejeté</option>
            <option value="closed">Clôturé</option>
          </select>
          <select class="filter-select" [(ngModel)]="severityFilter" (change)="filterClaims()">
            <option value="">Toutes gravités</option>
            <option value="minor">Mineur</option>
            <option value="moderate">Modéré</option>
            <option value="major">Majeur</option>
            <option value="total_loss">Perte totale</option>
          </select>
          <button class="btn-add" (click)="openAddForm()">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"/>
              <line x1="5" y1="12" x2="19" y2="12"/>
            </svg>
            Nouveau sinistre
          </button>
        </div>

        <!-- Stats Bar (comme maintenance) -->
        <div class="stats-bar">
          <div class="stat-item">
            <div class="stat-icon total">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                <polyline points="14 2 14 8 20 8"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ claims.length }}</span>
              <span class="stat-label">Total</span>
            </div>
          </div>
          <div class="stat-item">
            <div class="stat-icon pending">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ getClaimsByStatus('under_review').length }}</span>
              <span class="stat-label">En cours</span>
            </div>
          </div>
          <div class="stat-item">
            <div class="stat-icon approved">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                <polyline points="22 4 12 14.01 9 11.01"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ getClaimsByStatus('approved').length }}</span>
              <span class="stat-label">Approuvés</span>
            </div>
          </div>
          <div class="stat-item cost">
            <div class="stat-icon cost">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="12" y1="1" x2="12" y2="23"/>
                <path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ getTotalEstimatedDamage() | number:'1.0-0' }} DT</span>
              <span class="stat-label">Dommages</span>
            </div>
          </div>
        </div>

        <!-- Table -->
        <div class="table-container">
          <table class="data-table">
            <thead>
              <tr>
                <th>N° Sinistre</th>
                <th>Véhicule</th>
                <th>Conducteur</th>
                <th>Date</th>
                <th>Gravité</th>
                <th>Montant</th>
                <th>Statut</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              @for (claim of filteredClaims; track claim.id) {
                <tr (click)="selectClaim(claim)">
                  <td class="claim-number">{{ claim.claimNumber }}</td>
                  <td>
                    <div class="vehicle-info">
                      <span class="vehicle-name">{{ claim.vehicleName }}</span>
                      <span class="vehicle-plate">{{ claim.vehiclePlate }}</span>
                    </div>
                  </td>
                  <td>{{ claim.driverName }}</td>
                  <td class="date-cell">
                    <span class="date">{{ formatDate(claim.accidentDate) }}</span>
                    <span class="time">{{ claim.accidentTime }}</span>
                  </td>
                  <td>
                    <span class="severity-badge" [class]="claim.severity">
                      {{ getSeverityLabel(claim.severity) }}
                    </span>
                  </td>
                  <td class="amount-cell">{{ claim.estimatedDamage | number:'1.0-0' }} DT</td>
                  <td>
                    <span class="status-badge" [class]="claim.status">
                      {{ getStatusLabel(claim.status) }}
                    </span>
                  </td>
                  <td>
                    <div class="action-buttons">
                      <button class="action-btn" title="Détails" (click)="selectClaim(claim); $event.stopPropagation()">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/>
                          <circle cx="12" cy="12" r="3"/>
                        </svg>
                      </button>
                      <button class="action-btn" title="Modifier" (click)="editClaim(claim); $event.stopPropagation()" *ngIf="claim.status === 'draft'">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                          <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                        </svg>
                      </button>
                      <button class="action-btn danger" title="Supprimer" (click)="deleteClaim(claim); $event.stopPropagation()" *ngIf="claim.status === 'draft'">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <polyline points="3 6 5 6 21 6"/>
                          <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                        </svg>
                      </button>
                    </div>
                  </td>
                </tr>
              }
            </tbody>
          </table>

          @if (filteredClaims.length === 0) {
            <div class="empty-state">
              <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                <circle cx="12" cy="12" r="10"/>
                <line x1="12" y1="8" x2="12" y2="12"/>
                <line x1="12" y1="16" x2="12.01" y2="16"/>
              </svg>
              <p>Aucun sinistre enregistré</p>
              <span>Ajoutez une nouvelle déclaration</span>
            </div>
          }
        </div>
      </div>

      <!-- PANNEAU DÉTAIL SINISTRE (slide-in depuis la droite) -->
      <div class="detail-overlay" *ngIf="selectedClaim" @fadeIn (click)="closeClaim()">
        <div class="detail-panel" @slideIn (click)="$event.stopPropagation()">
          <!-- Header -->
          <div class="panel-header" [class]="selectedClaim.severity">
            <div class="panel-header-content">
              <div class="claim-icon">⚠️</div>
              <div class="panel-header-info">
                <h2>{{ selectedClaim.claimNumber }}</h2>
                <p>{{ selectedClaim.vehicleName }} · {{ selectedClaim.vehiclePlate }}</p>
              </div>
            </div>
            <button class="btn-close-panel" (click)="closeClaim()">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="18" y1="6" x2="6" y2="18"/>
                <line x1="6" y1="6" x2="18" y2="18"/>
              </svg>
            </button>
          </div>

          <!-- Contenu scrollable -->
          <div class="panel-body">
            <!-- Statut et gravité -->
            <div class="info-section">
              <div class="status-severity-row">
                <div class="status-badge-large" [class]="selectedClaim.status">
                  {{ getStatusLabel(selectedClaim.status) }}
                </div>
                <div class="severity-badge-large" [class]="selectedClaim.severity">
                  {{ getSeverityLabel(selectedClaim.severity) }}
                </div>
              </div>
            </div>

            <!-- Infos Accident -->
            <div class="info-section">
              <h3 class="section-title">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z"/>
                  <circle cx="12" cy="10" r="3"/>
                </svg>
                Accident
              </h3>
              <div class="specs-grid">
                <div class="spec-item">
                  <span class="spec-label">Date</span>
                  <span class="spec-value">{{ formatDate(selectedClaim.accidentDate) }}</span>
                </div>
                <div class="spec-item">
                  <span class="spec-label">Heure</span>
                  <span class="spec-value">{{ selectedClaim.accidentTime }}</span>
                </div>
                <div class="spec-item full">
                  <span class="spec-label">Localisation</span>
                  <span class="spec-value">{{ selectedClaim.location }}</span>
                </div>
              </div>
            </div>

            <!-- Véhicule & Conducteur -->
            <div class="info-section">
              <h3 class="section-title">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <rect x="1" y="3" width="15" height="13" rx="2"/>
                  <polygon points="16 8 20 8 23 11 23 16 16 16 16 8"/>
                  <circle cx="5.5" cy="18.5" r="2.5"/>
                  <circle cx="18.5" cy="18.5" r="2.5"/>
                </svg>
                Véhicule & Conducteur
              </h3>
              <div class="specs-grid">
                <div class="spec-item">
                  <span class="spec-label">Véhicule</span>
                  <span class="spec-value">{{ selectedClaim.vehicleName }}</span>
                </div>
                <div class="spec-item">
                  <span class="spec-label">Immatriculation</span>
                  <span class="spec-value plate">{{ selectedClaim.vehiclePlate }}</span>
                </div>
                <div class="spec-item full">
                  <span class="spec-label">Conducteur</span>
                  <span class="spec-value">{{ selectedClaim.driverName }}</span>
                </div>
              </div>
            </div>

            <!-- Tiers -->
            <div class="info-section" *ngIf="selectedClaim.thirdPartyInvolved">
              <h3 class="section-title">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/>
                  <circle cx="9" cy="7" r="4"/>
                  <path d="M23 21v-2a4 4 0 0 0-3-3.87"/>
                  <path d="M16 3.13a4 4 0 0 1 0 7.75"/>
                </svg>
                Tiers Impliqué
              </h3>
              <div class="specs-grid">
                <div class="spec-item" *ngIf="selectedClaim.thirdPartyName">
                  <span class="spec-label">Nom</span>
                  <span class="spec-value">{{ selectedClaim.thirdPartyName }}</span>
                </div>
                <div class="spec-item" *ngIf="selectedClaim.thirdPartyPhone">
                  <span class="spec-label">Téléphone</span>
                  <span class="spec-value">{{ selectedClaim.thirdPartyPhone }}</span>
                </div>
                <div class="spec-item" *ngIf="selectedClaim.thirdPartyVehicle">
                  <span class="spec-label">Véhicule</span>
                  <span class="spec-value">{{ selectedClaim.thirdPartyVehicle }}</span>
                </div>
                <div class="spec-item" *ngIf="selectedClaim.thirdPartyInsurance">
                  <span class="spec-label">Assurance</span>
                  <span class="spec-value">{{ selectedClaim.thirdPartyInsurance }}</span>
                </div>
              </div>
            </div>

            <!-- Dommages -->
            <div class="info-section">
              <h3 class="section-title">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="12" y1="1" x2="12" y2="23"/>
                  <path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>
                </svg>
                Évaluation des Dommages
              </h3>
              <div class="damage-cards">
                <div class="damage-card">
                  <span class="damage-label">Estimation</span>
                  <span class="damage-value">{{ selectedClaim.estimatedDamage | number:'1.0-0' }} DT</span>
                </div>
                <div class="damage-card approved" *ngIf="selectedClaim.approvedAmount">
                  <span class="damage-label">Approuvé</span>
                  <span class="damage-value">{{ selectedClaim.approvedAmount | number:'1.0-0' }} DT</span>
                </div>
              </div>
              <div class="damaged-zones" *ngIf="selectedClaim.damagedZones && selectedClaim.damagedZones.length > 0">
                <span class="zones-label">Zones endommagées:</span>
                <div class="zones-tags">
                  <span class="zone-tag" *ngFor="let zone of selectedClaim.damagedZones">{{ zone }}</span>
                </div>
              </div>
            </div>

            <!-- Description -->
            <div class="info-section">
              <h3 class="section-title">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                  <polyline points="14 2 14 8 20 8"/>
                  <line x1="16" y1="13" x2="8" y2="13"/>
                  <line x1="16" y1="17" x2="8" y2="17"/>
                </svg>
                Description
              </h3>
              <p class="description-text">{{ selectedClaim.description }}</p>
            </div>

            <!-- Historique -->
            <div class="info-section">
              <h3 class="section-title">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="12" cy="12" r="10"/>
                  <polyline points="12 6 12 12 16 14"/>
                </svg>
                Historique
              </h3>
              <div class="timeline">
                <div class="timeline-item">
                  <div class="timeline-dot"></div>
                  <div class="timeline-content">
                    <span class="timeline-date">{{ selectedClaim.createdAt | date:'dd/MM/yyyy HH:mm' }}</span>
                    <span class="timeline-text">Déclaration créée</span>
                  </div>
                </div>
                <div class="timeline-item" *ngIf="selectedClaim.status !== 'draft'">
                  <div class="timeline-dot active"></div>
                  <div class="timeline-content">
                    <span class="timeline-date">{{ selectedClaim.updatedAt | date:'dd/MM/yyyy HH:mm' }}</span>
                    <span class="timeline-text">Dernière mise à jour</span>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- Footer Actions -->
          <div class="panel-footer">
            <button class="btn-secondary" (click)="closeClaim()">Fermer</button>
            <button class="btn-edit" *ngIf="selectedClaim.status === 'draft'" (click)="editClaim(selectedClaim)">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
              </svg>
              Modifier
            </button>
            <button class="btn-submit" *ngIf="selectedClaim.status === 'draft'" (click)="submitClaim(selectedClaim)">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                <polyline points="22 4 12 14.01 9 11.01"/>
              </svg>
              Soumettre
            </button>
          </div>
        </div>
      </div>

      <!-- FORMULAIRE CRÉATION/ÉDITION SINISTRE (slide-in) -->
      <div class="form-overlay" *ngIf="isFormOpen" @fadeIn (click)="closeForm()">
        <div class="form-panel" @slideIn (click)="$event.stopPropagation()">
          <!-- Header -->
          <div class="form-header">
            <div class="form-header-content">
              <div class="form-icon">📝</div>
              <div class="form-header-info">
                <h2>{{ editingClaim ? 'Modifier le sinistre' : 'Nouveau sinistre' }}</h2>
                <p>{{ editingClaim ? editingClaim.claimNumber : 'Remplissez les informations' }}</p>
              </div>
            </div>
            <button class="btn-close-panel" (click)="closeForm()">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
              </svg>
            </button>
          </div>

          <!-- Form Body -->
          <div class="form-body">
            <!-- Véhicule & Conducteur -->
            <div class="form-section">
              <h3 class="form-section-title">🚗 Véhicule & Conducteur</h3>
              <div class="form-row">
                <div class="form-group">
                  <label>Véhicule *</label>
                  <select [(ngModel)]="formData.vehicleId" (ngModelChange)="onVehicleChange($event)" required>
                    <option value="">Sélectionner un véhicule</option>
                    <option *ngFor="let v of vehicles" [value]="v.id">{{ v.name }} - {{ v.plate }}</option>
                  </select>
                </div>
                <div class="form-group">
                  <label>Conducteur *</label>
                  <select [(ngModel)]="formData.driverId" (ngModelChange)="onDriverChange($event)" required>
                    <option value="">Sélectionner un conducteur</option>
                    <option *ngFor="let d of drivers" [value]="d.id">{{ d.name }}</option>
                  </select>
                </div>
              </div>
            </div>

            <!-- Date & Lieu -->
            <div class="form-section">
              <h3 class="form-section-title">📍 Date & Lieu</h3>
              <div class="form-row">
                <div class="form-group">
                  <label>Date de l'accident *</label>
                  <input type="date" [(ngModel)]="formData.accidentDate" required>
                </div>
                <div class="form-group">
                  <label>Heure *</label>
                  <input type="time" [(ngModel)]="formData.accidentTime" required>
                </div>
              </div>
              <div class="form-group">
                <label>Localisation *</label>
                <input type="text" [(ngModel)]="formData.location" placeholder="Adresse complète du lieu" required>
              </div>
            </div>

            <!-- Gravité & Dommages -->
            <div class="form-section">
              <h3 class="form-section-title">💥 Gravité & Dommages</h3>
              <div class="form-group">
                <label>Gravité *</label>
                <div class="severity-selector">
                  <label class="severity-option" *ngFor="let sev of severityOptions"
                         [class.selected]="formData.severity === sev.value">
                    <input type="radio" name="severity" [value]="sev.value" [(ngModel)]="formData.severity">
                    <span class="severity-dot" [class]="sev.value"></span>
                    <span>{{ sev.label }}</span>
                  </label>
                </div>
              </div>
              <div class="form-group">
                <label>Estimation des dommages (DT) *</label>
                <input type="number" [(ngModel)]="formData.estimatedDamage" placeholder="0" required>
              </div>
              <div class="form-group">
                <label>Zones endommagées</label>
                <div class="zones-selector">
                  <label class="zone-option" *ngFor="let zone of zoneOptions"
                         [class.selected]="formData.damagedZones.includes(zone)">
                    <input type="checkbox" [checked]="formData.damagedZones.includes(zone)"
                           (change)="toggleZone(zone)">
                    <span>{{ zone }}</span>
                  </label>
                </div>
              </div>
            </div>

            <!-- Tiers -->
            <div class="form-section">
              <h3 class="form-section-title">👥 Tiers Impliqué</h3>
              <div class="form-group">
                <label class="toggle-label">
                  <input type="checkbox" [(ngModel)]="formData.thirdPartyInvolved">
                  <span class="toggle-switch"></span>
                  <span>Un tiers est impliqué</span>
                </label>
              </div>
              <div class="third-party-fields" *ngIf="formData.thirdPartyInvolved">
                <div class="form-row">
                  <div class="form-group">
                    <label>Nom du tiers</label>
                    <input type="text" [(ngModel)]="formData.thirdPartyName" placeholder="Nom complet">
                  </div>
                  <div class="form-group">
                    <label>Téléphone</label>
                    <input type="tel" [(ngModel)]="formData.thirdPartyPhone" placeholder="+216 XX XXX XXX">
                  </div>
                </div>
                <div class="form-row">
                  <div class="form-group">
                    <label>Véhicule tiers</label>
                    <input type="text" [(ngModel)]="formData.thirdPartyVehicle" placeholder="Marque modèle - Immat">
                  </div>
                  <div class="form-group">
                    <label>Assurance tiers</label>
                    <input type="text" [(ngModel)]="formData.thirdPartyInsurance" placeholder="Compagnie d'assurance">
                  </div>
                </div>
              </div>
            </div>

            <!-- Description -->
            <div class="form-section">
              <h3 class="form-section-title">📝 Description</h3>
              <div class="form-group">
                <label>Circonstances de l'accident *</label>
                <textarea [(ngModel)]="formData.description" rows="4" 
                          placeholder="Décrivez les circonstances de l'accident..." required></textarea>
              </div>
            </div>
          </div>

          <!-- Form Footer -->
          <div class="form-footer">
            <button class="btn-secondary" (click)="closeForm()">Annuler</button>
            <button class="btn-save" (click)="saveClaim()" [disabled]="!isFormValid()">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/>
                <polyline points="17 21 17 13 7 13 7 21"/>
                <polyline points="7 3 7 8 15 8"/>
              </svg>
              {{ editingClaim ? 'Enregistrer' : 'Créer le sinistre' }}
            </button>
          </div>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    .claims-page {
      flex: 1;
      background: #f1f5f9;
      padding: 20px;
    }

    /* Filter Bar (identique maintenance) */
    .filter-bar {
      display: flex;
      align-items: center;
      gap: 12px;
      margin-bottom: 16px;
      flex-wrap: wrap;
    }

    .search-wrapper {
      flex: 1;
      min-width: 200px;
      position: relative;
      display: flex;
      align-items: center;
    }

    .search-icon {
      position: absolute;
      left: 12px;
      color: #94a3b8;
    }

    .search-input {
      width: 100%;
      padding: 10px 12px 10px 36px;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      font-size: 13px;
      background: white;
    }

    .search-input:focus {
      outline: none;
      border-color: #3b82f6;
    }

    .filter-select {
      padding: 10px 12px;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      font-size: 13px;
      background: white;
      cursor: pointer;
    }

    .btn-add {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 10px 16px;
      background: #1e3a5f;
      color: white;
      border: none;
      border-radius: 8px;
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
      transition: background 0.2s;
    }

    .btn-add:hover {
      background: #2d5a87;
    }

    /* Stats Bar (identique maintenance) */
    .stats-bar {
      display: flex;
      gap: 12px;
      margin-bottom: 16px;
      flex-wrap: wrap;
    }

    .stat-item {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 12px 16px;
      background: white;
      border-radius: 10px;
      box-shadow: 0 1px 2px rgba(0,0,0,0.05);
    }

    .stat-icon {
      width: 36px;
      height: 36px;
      border-radius: 8px;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .stat-icon.total { background: #eff6ff; color: #3b82f6; }
    .stat-icon.pending { background: #fef3c7; color: #f59e0b; }
    .stat-icon.approved { background: #dcfce7; color: #16a34a; }
    .stat-icon.cost { background: #fee2e2; color: #dc2626; }

    .stat-content {
      display: flex;
      flex-direction: column;
    }

    .stat-value {
      font-size: 18px;
      font-weight: 700;
      color: #1e293b;
    }

    .stat-label {
      font-size: 11px;
      color: #64748b;
    }

    /* Table Container */
    .table-container {
      background: white;
      border-radius: 10px;
      box-shadow: 0 1px 2px rgba(0,0,0,0.05);
      overflow: hidden;
    }

    .data-table {
      width: 100%;
      border-collapse: collapse;
    }

    .data-table th {
      text-align: left;
      padding: 12px 16px;
      background: #f8fafc;
      font-size: 11px;
      font-weight: 600;
      color: #64748b;
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }

    .data-table td {
      padding: 12px 16px;
      border-top: 1px solid #f1f5f9;
      font-size: 13px;
      color: #1e293b;
    }

    .data-table tr:hover {
      background: #f8fafc;
      cursor: pointer;
    }

    .claim-number {
      font-family: monospace;
      font-weight: 600;
      color: #3b82f6;
    }

    .vehicle-info {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .vehicle-name {
      font-weight: 500;
    }

    .vehicle-plate {
      font-size: 11px;
      color: #64748b;
      font-family: monospace;
    }

    .date-cell {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .date-cell .time {
      font-size: 11px;
      color: #64748b;
    }

    .amount-cell {
      font-weight: 600;
      color: #dc2626;
    }

    /* Badges */
    .severity-badge {
      display: inline-flex;
      padding: 4px 8px;
      border-radius: 4px;
      font-size: 11px;
      font-weight: 600;
    }

    .severity-badge.minor { background: #dcfce7; color: #16a34a; }
    .severity-badge.moderate { background: #fef3c7; color: #d97706; }
    .severity-badge.major { background: #fee2e2; color: #dc2626; }
    .severity-badge.total_loss { background: #1e293b; color: white; }

    .status-badge {
      display: inline-flex;
      padding: 4px 8px;
      border-radius: 4px;
      font-size: 11px;
      font-weight: 600;
    }

    .status-badge.draft { background: #f1f5f9; color: #64748b; }
    .status-badge.submitted { background: #dbeafe; color: #2563eb; }
    .status-badge.under_review { background: #fef3c7; color: #d97706; }
    .status-badge.approved { background: #dcfce7; color: #16a34a; }
    .status-badge.rejected { background: #fee2e2; color: #dc2626; }
    .status-badge.closed { background: #1e293b; color: white; }

    /* Action Buttons */
    .action-buttons {
      display: flex;
      gap: 4px;
    }

    .action-btn {
      width: 28px;
      height: 28px;
      border: 1px solid #e2e8f0;
      border-radius: 6px;
      background: white;
      color: #64748b;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }

    .action-btn:hover {
      color: #3b82f6;
      border-color: #3b82f6;
    }

    .action-btn.danger:hover {
      color: #dc2626;
      border-color: #dc2626;
    }

    /* Empty State */
    .empty-state {
      text-align: center;
      padding: 60px 20px;
      color: #64748b;
    }

    .empty-state svg {
      margin-bottom: 12px;
      opacity: 0.5;
    }

    .empty-state p {
      margin: 0 0 4px;
      font-weight: 500;
      color: #1e293b;
    }

    .empty-state span {
      font-size: 12px;
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

    .panel-header.minor { background: linear-gradient(135deg, #22c55e 0%, #16a34a 100%); }
    .panel-header.moderate { background: linear-gradient(135deg, #f59e0b 0%, #d97706 100%); }
    .panel-header.major { background: linear-gradient(135deg, #ef4444 0%, #dc2626 100%); }
    .panel-header.total_loss { background: linear-gradient(135deg, #1e293b 0%, #0f172a 100%); }

    .panel-header-content {
      display: flex;
      align-items: center;
      gap: 14px;
    }

    .claim-icon {
      font-size: 32px;
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

    .section-title {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 13px;
      font-weight: 600;
      color: #1e293b;
      margin: 0 0 14px;
    }

    .section-title svg {
      color: #3b82f6;
    }

    /* Status & Severity Row */
    .status-severity-row {
      display: flex;
      gap: 10px;
    }

    .status-badge-large,
    .severity-badge-large {
      padding: 6px 12px;
      border-radius: 6px;
      font-size: 12px;
      font-weight: 600;
    }

    .status-badge-large.draft { background: #f1f5f9; color: #64748b; }
    .status-badge-large.submitted { background: #dbeafe; color: #2563eb; }
    .status-badge-large.under_review { background: #fef3c7; color: #d97706; }
    .status-badge-large.approved { background: #dcfce7; color: #16a34a; }
    .status-badge-large.rejected { background: #fee2e2; color: #dc2626; }
    .status-badge-large.closed { background: #1e293b; color: white; }

    .severity-badge-large.minor { background: #dcfce7; color: #16a34a; }
    .severity-badge-large.moderate { background: #fef3c7; color: #d97706; }
    .severity-badge-large.major { background: #fee2e2; color: #dc2626; }
    .severity-badge-large.total_loss { background: #1e293b; color: white; }

    /* Specs Grid */
    .specs-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
    }

    .spec-item {
      background: #f8fafc;
      padding: 10px 12px;
      border-radius: 8px;
    }

    .spec-item.full {
      grid-column: 1 / -1;
    }

    .spec-label {
      display: block;
      font-size: 11px;
      color: #64748b;
      margin-bottom: 4px;
    }

    .spec-value {
      font-size: 13px;
      font-weight: 500;
      color: #1e293b;
    }

    .spec-value.plate {
      font-family: monospace;
      background: #e2e8f0;
      padding: 2px 6px;
      border-radius: 4px;
      display: inline-block;
    }

    /* Damage Cards */
    .damage-cards {
      display: flex;
      gap: 12px;
      margin-bottom: 12px;
    }

    .damage-card {
      flex: 1;
      padding: 14px;
      background: #fee2e2;
      border-radius: 10px;
      text-align: center;
    }

    .damage-card.approved {
      background: #dcfce7;
    }

    .damage-label {
      display: block;
      font-size: 11px;
      color: #64748b;
      margin-bottom: 4px;
    }

    .damage-value {
      font-size: 20px;
      font-weight: 700;
      color: #dc2626;
    }

    .damage-card.approved .damage-value {
      color: #16a34a;
    }

    .damaged-zones {
      margin-top: 12px;
    }

    .zones-label {
      font-size: 11px;
      color: #64748b;
      display: block;
      margin-bottom: 8px;
    }

    .zones-tags {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
    }

    .zone-tag {
      padding: 4px 10px;
      background: #f1f5f9;
      border-radius: 4px;
      font-size: 11px;
      color: #475569;
    }

    /* Description */
    .description-text {
      font-size: 13px;
      color: #475569;
      line-height: 1.6;
      margin: 0;
      padding: 12px;
      background: #f8fafc;
      border-radius: 8px;
    }

    /* Timeline */
    .timeline {
      display: flex;
      flex-direction: column;
      gap: 12px;
    }

    .timeline-item {
      display: flex;
      gap: 12px;
    }

    .timeline-dot {
      width: 10px;
      height: 10px;
      border-radius: 50%;
      background: #e2e8f0;
      margin-top: 4px;
    }

    .timeline-dot.active {
      background: #3b82f6;
    }

    .timeline-content {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .timeline-date {
      font-size: 11px;
      color: #64748b;
    }

    .timeline-text {
      font-size: 13px;
      color: #1e293b;
    }

    /* Panel Footer */
    .panel-footer {
      display: flex;
      justify-content: flex-end;
      gap: 10px;
      padding: 16px 20px;
      border-top: 1px solid #e2e8f0;
      background: #f8fafc;
    }

    .btn-secondary {
      padding: 10px 16px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      font-size: 13px;
      font-weight: 500;
      color: #64748b;
      cursor: pointer;
    }

    .btn-secondary:hover {
      background: #f1f5f9;
    }

    .btn-edit {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 10px 16px;
      background: #3b82f6;
      border: none;
      border-radius: 8px;
      font-size: 13px;
      font-weight: 500;
      color: white;
      cursor: pointer;
    }

    .btn-edit:hover {
      background: #2563eb;
    }

    .btn-submit {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 10px 16px;
      background: #16a34a;
      border: none;
      border-radius: 8px;
      font-size: 13px;
      font-weight: 500;
      color: white;
      cursor: pointer;
    }

    .btn-submit:hover {
      background: #15803d;
    }

    /* ===== FORM PANEL ===== */
    .form-overlay {
      position: fixed;
      inset: 0;
      background: rgba(0, 0, 0, 0.5);
      display: flex;
      justify-content: flex-end;
      z-index: 1001;
    }

    .form-panel {
      width: 100%;
      max-width: 520px;
      height: 100vh;
      background: white;
      display: flex;
      flex-direction: column;
      box-shadow: -4px 0 20px rgba(0,0,0,0.15);
    }

    .form-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 20px;
      background: linear-gradient(135deg, #3b82f6 0%, #2563eb 100%);
      color: white;
    }

    .form-header-content {
      display: flex;
      align-items: center;
      gap: 14px;
    }

    .form-icon {
      font-size: 28px;
      line-height: 1;
    }

    .form-header-info h2 {
      margin: 0;
      font-size: 18px;
      font-weight: 600;
    }

    .form-header-info p {
      margin: 4px 0 0;
      font-size: 13px;
      opacity: 0.9;
    }

    .form-body {
      flex: 1;
      overflow-y: auto;
      padding: 20px;
    }

    .form-section {
      margin-bottom: 24px;
      padding-bottom: 20px;
      border-bottom: 1px solid #e2e8f0;
    }

    .form-section:last-child {
      border-bottom: none;
    }

    .form-section-title {
      font-size: 14px;
      font-weight: 600;
      color: #1e293b;
      margin: 0 0 16px;
    }

    .form-row {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
    }

    .form-group {
      margin-bottom: 14px;
    }

    .form-group label {
      display: block;
      font-size: 12px;
      font-weight: 500;
      color: #64748b;
      margin-bottom: 6px;
    }

    .form-group input,
    .form-group select,
    .form-group textarea {
      width: 100%;
      padding: 10px 12px;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      font-size: 13px;
      color: #1e293b;
      transition: border-color 0.2s;
    }

    .form-group input:focus,
    .form-group select:focus,
    .form-group textarea:focus {
      outline: none;
      border-color: #3b82f6;
    }

    .form-group textarea {
      resize: vertical;
      min-height: 80px;
    }

    /* Severity Selector */
    .severity-selector {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
    }

    .severity-option {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 8px 12px;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      cursor: pointer;
      font-size: 12px;
      transition: all 0.2s;
    }

    .severity-option:hover {
      border-color: #94a3b8;
    }

    .severity-option.selected {
      border-color: #3b82f6;
      background: #eff6ff;
    }

    .severity-option input {
      display: none;
    }

    .severity-dot {
      width: 10px;
      height: 10px;
      border-radius: 50%;
    }

    .severity-dot.minor { background: #22c55e; }
    .severity-dot.moderate { background: #f59e0b; }
    .severity-dot.major { background: #ef4444; }
    .severity-dot.total_loss { background: #1e293b; }

    /* Zones Selector */
    .zones-selector {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
    }

    .zone-option {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 6px 10px;
      border: 1px solid #e2e8f0;
      border-radius: 6px;
      cursor: pointer;
      font-size: 12px;
      transition: all 0.2s;
    }

    .zone-option:hover {
      border-color: #94a3b8;
    }

    .zone-option.selected {
      border-color: #3b82f6;
      background: #eff6ff;
    }

    .zone-option input {
      display: none;
    }

    /* Toggle */
    .toggle-label {
      display: flex;
      align-items: center;
      gap: 10px;
      cursor: pointer;
      font-size: 13px;
      color: #1e293b;
    }

    .toggle-label input {
      display: none;
    }

    .toggle-switch {
      width: 40px;
      height: 22px;
      background: #e2e8f0;
      border-radius: 11px;
      position: relative;
      transition: background 0.2s;
    }

    .toggle-switch::after {
      content: '';
      position: absolute;
      width: 18px;
      height: 18px;
      background: white;
      border-radius: 50%;
      top: 2px;
      left: 2px;
      transition: transform 0.2s;
      box-shadow: 0 1px 3px rgba(0,0,0,0.2);
    }

    .toggle-label input:checked + .toggle-switch {
      background: #3b82f6;
    }

    .toggle-label input:checked + .toggle-switch::after {
      transform: translateX(18px);
    }

    .third-party-fields {
      margin-top: 16px;
      padding: 16px;
      background: #f8fafc;
      border-radius: 10px;
    }

    /* Form Footer */
    .form-footer {
      display: flex;
      justify-content: flex-end;
      gap: 10px;
      padding: 16px 20px;
      border-top: 1px solid #e2e8f0;
      background: #f8fafc;
    }

    .btn-save {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 10px 20px;
      background: #1e3a5f;
      border: none;
      border-radius: 8px;
      font-size: 13px;
      font-weight: 500;
      color: white;
      cursor: pointer;
      transition: background 0.2s;
    }

    .btn-save:hover:not(:disabled) {
      background: #2d5a87;
    }

    .btn-save:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }

    /* Responsive */
    @media (max-width: 768px) {
      .filter-bar {
        flex-direction: column;
        align-items: stretch;
      }

      .stats-bar {
        flex-direction: column;
      }

      .data-table {
        display: block;
        overflow-x: auto;
      }

      .detail-panel,
      .form-panel {
        max-width: 100%;
      }

      .specs-grid,
      .form-row {
        grid-template-columns: 1fr;
      }

      .damage-cards {
        flex-direction: column;
      }

      .severity-selector {
        flex-direction: column;
      }
    }
  `]
})
export class AccidentClaimsComponent implements OnInit {
  claims: AccidentClaim[] = [];
  filteredClaims: AccidentClaim[] = [];
  selectedClaim: AccidentClaim | null = null;
  editingClaim: AccidentClaim | null = null;
  isFormOpen = false;

  searchQuery = '';
  statusFilter = '';
  severityFilter = '';

  // Form data
  vehicles = [
    { id: 'v1', name: 'Peugeot 208', plate: '245 TUN 7890' },
    { id: 'v2', name: 'Renault Clio', plate: '189 TUN 4521' },
    { id: 'v3', name: 'Citroën Berlingo', plate: '312 TUN 1122' },
    { id: 'v4', name: 'Volkswagen Caddy', plate: '456 TUN 7788' },
    { id: 'v5', name: 'Fiat Ducato', plate: '678 TUN 3344' }
  ];

  drivers = [
    { id: 'd1', name: 'Ahmed Ben Ali' },
    { id: 'd2', name: 'Fatma Trabelsi' },
    { id: 'd3', name: 'Mohamed Sassi' },
    { id: 'd4', name: 'Sami Gharbi' },
    { id: 'd5', name: 'Karim Mejri' }
  ];

  severityOptions = [
    { value: 'minor', label: 'Mineur' },
    { value: 'moderate', label: 'Modéré' },
    { value: 'major', label: 'Majeur' },
    { value: 'total_loss', label: 'Perte totale' }
  ];

  zoneOptions = ['Avant', 'Arrière', 'Côté gauche', 'Côté droit', 'Toit', 'Pare-brise', 'Moteur', 'Roues'];

  formData: any = this.getEmptyFormData();

  constructor(
    private router: Router,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit() {
    this.loadClaims();
  }

  loadClaims() {
    this.claims = [
      {
        id: '1',
        claimNumber: 'SIN-2026-001',
        vehicleId: 'v1',
        vehicleName: 'Peugeot 208',
        vehiclePlate: '245 TUN 7890',
        driverName: 'Ahmed Ben Ali',
        accidentDate: new Date('2026-01-20'),
        accidentTime: '14:30',
        location: 'Avenue Habib Bourguiba, Tunis',
        weatherConditions: 'Ensoleillé',
        description: 'Collision arrière à un feu rouge. Le véhicule devant a freiné brusquement.',
        status: 'under_review',
        severity: 'moderate',
        estimatedDamage: 2500,
        thirdPartyInvolved: true,
        thirdPartyName: 'Kamel Bouazizi',
        thirdPartyPhone: '+216 98 456 789',
        thirdPartyVehicle: 'Toyota Yaris - 178 TUN 3344',
        thirdPartyInsurance: 'STAR Assurances',
        policeReportNumber: 'PV-2026-4521',
        damagedZones: ['Avant', 'Pare-chocs'],
        createdAt: new Date('2026-01-20T15:00:00'),
        updatedAt: new Date('2026-01-21T10:00:00'),
        photos: []
      },
      {
        id: '2',
        claimNumber: 'SIN-2026-002',
        vehicleId: 'v2',
        vehicleName: 'Renault Clio',
        vehiclePlate: '189 TUN 4521',
        driverName: 'Fatma Trabelsi',
        accidentDate: new Date('2026-01-18'),
        accidentTime: '09:15',
        location: 'Route de Sousse, Km 45',
        weatherConditions: 'Pluie',
        roadConditions: 'Mouillée',
        description: 'Sortie de route due à éclatement de pneu. Dommages importants sur le côté droit.',
        status: 'approved',
        severity: 'major',
        estimatedDamage: 8500,
        approvedAmount: 7800,
        thirdPartyInvolved: false,
        damagedZones: ['Côté droit', 'Roue avant', 'Aile'],
        createdAt: new Date('2026-01-18T11:00:00'),
        updatedAt: new Date('2026-01-22T14:00:00'),
        photos: []
      },
      {
        id: '3',
        claimNumber: 'SIN-2026-003',
        vehicleId: 'v3',
        vehicleName: 'Citroën Berlingo',
        vehiclePlate: '312 TUN 1122',
        driverName: 'Mohamed Sassi',
        accidentDate: new Date('2026-01-22'),
        accidentTime: '16:45',
        location: 'Parking Centre Commercial Carrefour, La Marsa',
        description: 'Accrochage en manœuvre de stationnement. Rayures sur pare-chocs arrière.',
        status: 'draft',
        severity: 'minor',
        estimatedDamage: 450,
        thirdPartyInvolved: false,
        damagedZones: ['Arrière'],
        createdAt: new Date('2026-01-22T17:00:00'),
        updatedAt: new Date('2026-01-22T17:00:00'),
        photos: []
      },
      {
        id: '4',
        claimNumber: 'SIN-2026-004',
        vehicleId: 'v4',
        vehicleName: 'Volkswagen Caddy',
        vehiclePlate: '456 TUN 7788',
        driverName: 'Sami Gharbi',
        accidentDate: new Date('2026-01-15'),
        accidentTime: '11:20',
        location: 'Autoroute A1, Sortie Hammamet',
        description: 'Collision avec obstacle sur la voie. Dommages au train avant et airbags déclenchés.',
        status: 'closed',
        severity: 'total_loss',
        estimatedDamage: 25000,
        approvedAmount: 22000,
        thirdPartyInvolved: false,
        policeReportNumber: 'PV-2026-4489',
        damagedZones: ['Avant', 'Moteur', 'Airbags', 'Châssis'],
        createdAt: new Date('2026-01-15T12:00:00'),
        updatedAt: new Date('2026-01-20T16:00:00'),
        photos: []
      }
    ];
    this.filterClaims();
  }

  filterClaims() {
    let result = [...this.claims];

    if (this.searchQuery) {
      const query = this.searchQuery.toLowerCase();
      result = result.filter(c =>
        c.claimNumber.toLowerCase().includes(query) ||
        c.vehicleName.toLowerCase().includes(query) ||
        c.vehiclePlate.toLowerCase().includes(query) ||
        c.driverName.toLowerCase().includes(query)
      );
    }

    if (this.statusFilter) {
      result = result.filter(c => c.status === this.statusFilter);
    }

    if (this.severityFilter) {
      result = result.filter(c => c.severity === this.severityFilter);
    }

    this.filteredClaims = result;
  }

  getClaimsByStatus(status: string): AccidentClaim[] {
    return this.claims.filter(c => c.status === status);
  }

  getTotalEstimatedDamage(): number {
    return this.claims.reduce((sum, c) => sum + c.estimatedDamage, 0);
  }

  formatDate(date: Date): string {
    return new Date(date).toLocaleDateString('fr-FR');
  }

  getSeverityLabel(severity: string): string {
    const labels: any = {
      minor: 'Mineur',
      moderate: 'Modéré',
      major: 'Majeur',
      total_loss: 'Perte Totale'
    };
    return labels[severity] || severity;
  }

  getStatusLabel(status: string): string {
    const labels: any = {
      draft: 'Brouillon',
      submitted: 'Soumis',
      under_review: 'En révision',
      approved: 'Approuvé',
      rejected: 'Rejeté',
      closed: 'Clôturé'
    };
    return labels[status] || status;
  }

  selectClaim(claim: AccidentClaim) {
    this.selectedClaim = claim;
  }

  closeClaim() {
    this.selectedClaim = null;
  }

  // Form methods
  getEmptyFormData() {
    const today = new Date();
    return {
      vehicleId: '',
      driverId: '',
      accidentDate: today.toISOString().split('T')[0],
      accidentTime: '',
      location: '',
      severity: '',
      estimatedDamage: null,
      damagedZones: [] as string[],
      thirdPartyInvolved: false,
      thirdPartyName: '',
      thirdPartyPhone: '',
      thirdPartyVehicle: '',
      thirdPartyInsurance: '',
      description: ''
    };
  }

  openAddForm() {
    this.editingClaim = null;
    this.formData = this.getEmptyFormData();
    this.isFormOpen = true;
  }

  editClaim(claim: AccidentClaim) {
    this.editingClaim = claim;
    this.formData = {
      vehicleId: claim.vehicleId,
      driverId: this.drivers.find(d => d.name === claim.driverName)?.id || '',
      accidentDate: new Date(claim.accidentDate).toISOString().split('T')[0],
      accidentTime: claim.accidentTime,
      location: claim.location,
      severity: claim.severity,
      estimatedDamage: claim.estimatedDamage,
      damagedZones: claim.damagedZones ? [...claim.damagedZones] : [],
      thirdPartyInvolved: claim.thirdPartyInvolved,
      thirdPartyName: claim.thirdPartyName || '',
      thirdPartyPhone: claim.thirdPartyPhone || '',
      thirdPartyVehicle: claim.thirdPartyVehicle || '',
      thirdPartyInsurance: claim.thirdPartyInsurance || '',
      description: claim.description
    };
    this.isFormOpen = true;
    this.closeClaim();
  }

  closeForm() {
    this.isFormOpen = false;
    this.editingClaim = null;
    this.formData = this.getEmptyFormData();
  }

  isFormValid(): boolean {
    return !!(
      this.formData.vehicleId &&
      this.formData.driverId &&
      this.formData.accidentDate &&
      this.formData.accidentTime &&
      this.formData.location &&
      this.formData.severity &&
      this.formData.estimatedDamage &&
      this.formData.description
    );
  }

  toggleZone(zone: string) {
    const index = this.formData.damagedZones.indexOf(zone);
    if (index === -1) {
      this.formData.damagedZones.push(zone);
    } else {
      this.formData.damagedZones.splice(index, 1);
    }
  }

  onVehicleChange(vehicleId: string) {
    // Auto-fill vehicle info if needed
  }

  onDriverChange(driverId: string) {
    // Auto-fill driver info if needed
  }

  saveClaim() {
    const vehicle = this.vehicles.find(v => v.id === this.formData.vehicleId);
    const driver = this.drivers.find(d => d.id === this.formData.driverId);

    if (this.editingClaim) {
      // Update existing claim
      const index = this.claims.findIndex(c => c.id === this.editingClaim!.id);
      if (index !== -1) {
        this.claims[index] = {
          ...this.editingClaim,
          vehicleId: this.formData.vehicleId,
          vehicleName: vehicle?.name || '',
          vehiclePlate: vehicle?.plate || '',
          driverName: driver?.name || '',
          accidentDate: new Date(this.formData.accidentDate),
          accidentTime: this.formData.accidentTime,
          location: this.formData.location,
          severity: this.formData.severity,
          estimatedDamage: this.formData.estimatedDamage,
          damagedZones: this.formData.damagedZones,
          thirdPartyInvolved: this.formData.thirdPartyInvolved,
          thirdPartyName: this.formData.thirdPartyName,
          thirdPartyPhone: this.formData.thirdPartyPhone,
          thirdPartyVehicle: this.formData.thirdPartyVehicle,
          thirdPartyInsurance: this.formData.thirdPartyInsurance,
          description: this.formData.description,
          updatedAt: new Date()
        };
      }
    } else {
      // Create new claim
      const newClaim: AccidentClaim = {
        id: 'c' + Date.now(),
        claimNumber: 'SIN-2026-' + String(this.claims.length + 1).padStart(3, '0'),
        vehicleId: this.formData.vehicleId,
        vehicleName: vehicle?.name || '',
        vehiclePlate: vehicle?.plate || '',
        driverName: driver?.name || '',
        accidentDate: new Date(this.formData.accidentDate),
        accidentTime: this.formData.accidentTime,
        location: this.formData.location,
        description: this.formData.description,
        status: 'draft',
        severity: this.formData.severity,
        estimatedDamage: this.formData.estimatedDamage,
        thirdPartyInvolved: this.formData.thirdPartyInvolved,
        thirdPartyName: this.formData.thirdPartyName,
        thirdPartyPhone: this.formData.thirdPartyPhone,
        thirdPartyVehicle: this.formData.thirdPartyVehicle,
        thirdPartyInsurance: this.formData.thirdPartyInsurance,
        damagedZones: this.formData.damagedZones,
        createdAt: new Date(),
        updatedAt: new Date(),
        photos: []
      };
      this.claims.unshift(newClaim);
    }

    this.filterClaims();
    this.closeForm();
  }

  deleteClaim(claim: AccidentClaim) {
    if (confirm(`Supprimer le sinistre ${claim.claimNumber} ?`)) {
      this.claims = this.claims.filter(c => c.id !== claim.id);
      this.filterClaims();
    }
  }

  submitClaim(claim: AccidentClaim) {
    claim.status = 'submitted';
    claim.updatedAt = new Date();
    this.closeClaim();
  }
}
