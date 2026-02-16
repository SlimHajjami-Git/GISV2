import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AppLayoutComponent } from './shared/app-layout.component';
import { ApiService } from '../services/api.service';
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

            <!-- Photos -->
            <div class="info-section" *ngIf="claimPhotos.length > 0">
              <h3 class="section-title">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <rect x="3" y="3" width="18" height="18" rx="2" ry="2"/>
                  <circle cx="8.5" cy="8.5" r="1.5"/>
                  <polyline points="21 15 16 10 5 21"/>
                </svg>
                Photos ({{ claimPhotos.length }})
              </h3>
              <div class="detail-photos-grid">
                <div class="detail-photo" *ngFor="let photo of claimPhotos" (click)="openPhotoFullscreen(photo)">
                  <img [src]="getPhotoUrl(photo.fileUrl)" [alt]="photo.fileName">
                </div>
              </div>
            </div>

            <!-- Fullscreen photo viewer -->
            <div class="photo-viewer-overlay" *ngIf="fullscreenPhoto" (click)="fullscreenPhoto = null">
              <img [src]="getPhotoUrl(fullscreenPhoto.fileUrl)" [alt]="fullscreenPhoto.fileName" (click)="$event.stopPropagation()">
              <button class="photo-viewer-close" (click)="fullscreenPhoto = null">×</button>
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

            <!-- Police Report & Constat -->
            <div class="form-section">
              <h3 class="form-section-title">� Constat & Police</h3>
              <div class="form-row">
                <div class="form-group">
                  <label>N° Rapport de police</label>
                  <input type="text" [(ngModel)]="formData.policeReportNumber" placeholder="Ex: PV-2026-001234">
                </div>
                <div class="form-group">
                  <label>Conditions météo</label>
                  <select [(ngModel)]="formData.weatherConditions">
                    <option value="">Sélectionner</option>
                    <option value="clear">Clair / Ensoleillé</option>
                    <option value="cloudy">Nuageux</option>
                    <option value="rain">Pluie</option>
                    <option value="fog">Brouillard</option>
                    <option value="night">Nuit</option>
                  </select>
                </div>
              </div>
              <div class="form-group">
                <label>Conditions de la route</label>
                <select [(ngModel)]="formData.roadConditions">
                  <option value="">Sélectionner</option>
                  <option value="dry">Sèche</option>
                  <option value="wet">Mouillée</option>
                  <option value="icy">Verglacée</option>
                  <option value="unpaved">Non goudronnée</option>
                </select>
              </div>
            </div>

            <!-- Tiers Impliqué - repositioned before description for better flow -->
            <div class="form-section">
              <h3 class="form-section-title">
                <span class="section-title-row">
                  👥 Tiers Impliqué
                  <label class="toggle-inline">
                    <input type="checkbox" [(ngModel)]="formData.thirdPartyInvolved">
                    <span class="toggle-switch-small"></span>
                  </label>
                </span>
              </h3>
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

            <!-- Photos -->
            <div class="form-section">
              <h3 class="form-section-title">📷 Photos</h3>
              <div class="upload-zone" (click)="triggerPhotoUpload($event)">
                <input type="file" #photoInput multiple accept="image/*" (change)="onPhotosSelected($event)" style="display:none">
                <svg width="36" height="36" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                  <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/>
                  <polyline points="17 8 12 3 7 8"/>
                  <line x1="12" y1="3" x2="12" y2="15"/>
                </svg>
                <p>Cliquez ou glissez vos photos ici</p>
                <span>JPG, PNG, WEBP — max 10 Mo</span>
              </div>
              <div class="photos-grid" *ngIf="pendingPhotos.length > 0 || existingPhotos.length > 0">
                <div class="photo-thumb" *ngFor="let photo of existingPhotos; let i = index">
                  <img [src]="getPhotoUrl(photo.fileUrl)" [alt]="photo.fileName">
                  <button type="button" class="remove-photo-btn" (click)="removeExistingPhoto(photo, i); $event.stopPropagation()">×</button>
                </div>
                <div class="photo-thumb" *ngFor="let photo of pendingPhotos; let i = index">
                  <img [src]="photo.preview" [alt]="photo.file.name">
                  <button type="button" class="remove-photo-btn" (click)="removePendingPhoto(i); $event.stopPropagation()">×</button>
                </div>
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

    .section-title-row {
      display: flex;
      align-items: center;
      justify-content: space-between;
      width: 100%;
    }

    .toggle-inline {
      display: flex;
      align-items: center;
      cursor: pointer;
    }

    .toggle-inline input {
      display: none;
    }

    .toggle-switch-small {
      width: 36px;
      height: 20px;
      background: #e2e8f0;
      border-radius: 10px;
      position: relative;
      transition: background 0.2s;
    }

    .toggle-switch-small::after {
      content: '';
      position: absolute;
      width: 16px;
      height: 16px;
      background: white;
      border-radius: 50%;
      top: 2px;
      left: 2px;
      transition: transform 0.2s;
      box-shadow: 0 1px 3px rgba(0,0,0,0.2);
    }

    .toggle-inline input:checked + .toggle-switch-small {
      background: #3b82f6;
    }

    .toggle-inline input:checked + .toggle-switch-small::after {
      transform: translateX(16px);
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

    /* Upload Zone */
    .upload-zone {
      border: 2px dashed #e2e8f0;
      border-radius: 12px;
      padding: 28px;
      text-align: center;
      cursor: pointer;
      transition: all 0.2s;
      margin-bottom: 12px;
    }
    .upload-zone:hover {
      border-color: #3b82f6;
      background: #f8fafc;
    }
    .upload-zone svg { color: #94a3b8; margin-bottom: 8px; }
    .upload-zone p { font-size: 13px; color: #1e293b; margin: 0 0 4px; }
    .upload-zone span { font-size: 11px; color: #94a3b8; }

    .photos-grid {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 10px;
    }
    .photo-thumb {
      position: relative;
      aspect-ratio: 1;
      border-radius: 8px;
      overflow: hidden;
      background: #f1f5f9;
    }
    .photo-thumb img {
      width: 100%;
      height: 100%;
      object-fit: cover;
    }
    .remove-photo-btn {
      position: absolute;
      top: 4px;
      right: 4px;
      width: 22px;
      height: 22px;
      background: rgba(0,0,0,0.6);
      border: none;
      border-radius: 50%;
      color: white;
      font-size: 14px;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      line-height: 1;
    }
    .remove-photo-btn:hover { background: #dc2626; }

    /* Detail Photos Grid */
    .detail-photos-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 8px;
    }
    .detail-photo {
      aspect-ratio: 1;
      border-radius: 8px;
      overflow: hidden;
      cursor: pointer;
      transition: transform 0.2s;
    }
    .detail-photo:hover { transform: scale(1.03); }
    .detail-photo img {
      width: 100%;
      height: 100%;
      object-fit: cover;
    }

    /* Fullscreen Photo Viewer */
    .photo-viewer-overlay {
      position: fixed;
      top: 0; left: 0; right: 0; bottom: 0;
      background: rgba(0,0,0,0.9);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 2000;
      padding: 40px;
    }
    .photo-viewer-overlay img {
      max-width: 90%;
      max-height: 90%;
      border-radius: 8px;
      object-fit: contain;
    }
    .photo-viewer-close {
      position: absolute;
      top: 20px;
      right: 20px;
      width: 40px;
      height: 40px;
      background: rgba(255,255,255,0.2);
      border: none;
      border-radius: 50%;
      color: white;
      font-size: 24px;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
    }
    .photo-viewer-close:hover { background: rgba(255,255,255,0.4); }
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

  // Form data - loaded from API
  vehicles: { id: string; name: string; plate: string }[] = [];
  drivers: { id: string; name: string }[] = [];

  severityOptions = [
    { value: 'minor', label: 'Mineur' },
    { value: 'moderate', label: 'Modéré' },
    { value: 'major', label: 'Majeur' },
    { value: 'total_loss', label: 'Perte totale' }
  ];

  zoneOptions = ['Avant', 'Arrière', 'Côté gauche', 'Côté droit', 'Toit', 'Pare-brise', 'Moteur', 'Roues'];

  formData: any = this.getEmptyFormData();

  // Photo management
  pendingPhotos: { file: File; preview: string }[] = [];
  existingPhotos: any[] = [];
  claimPhotos: any[] = [];
  fullscreenPhoto: any = null;

  loading = false;

  constructor(
    private router: Router,
    private cdr: ChangeDetectorRef,
    private apiService: ApiService
  ) {}

  ngOnInit() {
    this.loadClaims();
    this.loadVehiclesAndDrivers();
  }

  loadVehiclesAndDrivers() {
    this.apiService.getVehicles().subscribe({
      next: (vehicles: any[]) => {
        this.vehicles = vehicles.map(v => ({
          id: v.id?.toString() || '',
          name: v.name || v.brand + ' ' + v.model || '',
          plate: v.plate || v.licensePlate || ''
        }));
        this.cdr.detectChanges();
      }
    });
    this.apiService.getUsers().subscribe({
      next: (users: any[]) => {
        this.drivers = users.map(u => ({
          id: u.id?.toString() || '',
          name: u.fullName || u.name || `${u.firstName || ''} ${u.lastName || ''}`.trim()
        }));
        this.cdr.detectChanges();
      }
    });
  }

  loadClaims() {
    this.loading = true;
    this.apiService.getAccidentClaims({ pageSize: 100 }).subscribe({
      next: (result) => {
        this.claims = result.items.map((c: any) => ({
          id: c.id?.toString() || '',
          claimNumber: c.claimNumber || '',
          vehicleId: c.vehicleId?.toString() || '',
          vehicleName: c.vehicleName || '',
          vehiclePlate: c.vehiclePlate || '',
          driverName: c.driverName || '',
          accidentDate: c.accidentDate ? new Date(c.accidentDate) : new Date(),
          accidentTime: c.accidentTime || '',
          location: c.location || '',
          weatherConditions: c.weatherConditions,
          roadConditions: c.roadConditions,
          description: c.description || '',
          status: (c.status as AccidentClaim['status']) || 'draft',
          severity: (c.severity as AccidentClaim['severity']) || 'minor',
          estimatedDamage: c.estimatedDamage || 0,
          approvedAmount: c.approvedAmount,
          thirdPartyInvolved: c.thirdPartyInvolved || false,
          thirdPartyName: c.thirdPartyName,
          thirdPartyPhone: c.thirdPartyPhone,
          thirdPartyVehicle: c.thirdPartyVehicle,
          thirdPartyInsurance: c.thirdPartyInsurance,
          policeReportNumber: c.policeReportNumber || '',
          damagedZones: c.damagedZones || [],
          createdAt: c.createdAt ? new Date(c.createdAt) : new Date(),
          updatedAt: c.updatedAt ? new Date(c.updatedAt) : new Date(),
          photos: c.photos || []
        }));
        this.filterClaims();
        this.loading = false;
      },
      error: (err) => {
        console.error('Error loading claims:', err);
        this.loading = false;
      }
    });
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
    this.claimPhotos = [];
    // Load photos from API
    const claimId = parseInt(claim.id);
    if (claimId) {
      this.apiService.getAccidentClaimDocuments(claimId).subscribe({
        next: (docs) => {
          this.claimPhotos = docs.filter(d => d.documentType === 'photo' || d.mimeType?.startsWith('image/'));
          this.cdr.detectChanges();
        }
      });
    }
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
      policeReportNumber: '',
      weatherConditions: '',
      roadConditions: '',
      description: ''
    };
  }

  triggerPhotoUpload(event: Event) {
    event.preventDefault();
    event.stopPropagation();
    const input = (event.currentTarget as HTMLElement).querySelector('input[type="file"]') as HTMLInputElement;
    if (input) input.click();
  }

  openAddForm() {
    this.editingClaim = null;
    this.formData = this.getEmptyFormData();
    this.pendingPhotos = [];
    this.existingPhotos = [];
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
    this.pendingPhotos = [];
    this.existingPhotos = [];
    // Load existing photos for editing
    const claimId = parseInt(claim.id);
    if (claimId) {
      this.apiService.getAccidentClaimDocuments(claimId).subscribe({
        next: (docs) => {
          this.existingPhotos = docs.filter(d => d.documentType === 'photo' || d.mimeType?.startsWith('image/'));
          this.cdr.detectChanges();
        }
      });
    }
    this.isFormOpen = true;
    this.closeClaim();
  }

  closeForm() {
    this.isFormOpen = false;
    this.editingClaim = null;
    this.formData = this.getEmptyFormData();
    this.pendingPhotos = [];
    this.existingPhotos = [];
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
    const request: any = {
      vehicleId: parseInt(this.formData.vehicleId) || 0,
      driverId: parseInt(this.formData.driverId) || undefined,
      accidentDate: this.formData.accidentDate,
      accidentTime: this.formData.accidentTime,
      location: this.formData.location,
      description: this.formData.description,
      severity: this.formData.severity,
      estimatedDamage: this.formData.estimatedDamage || 0,
      damagedZones: this.formData.damagedZones,
      thirdPartyInvolved: this.formData.thirdPartyInvolved,
      thirdPartyName: this.formData.thirdPartyName || undefined,
      thirdPartyPhone: this.formData.thirdPartyPhone || undefined,
      thirdPartyVehiclePlate: this.formData.thirdPartyVehicle || undefined,
      thirdPartyInsurance: this.formData.thirdPartyInsurance || undefined,
      policeReportNumber: this.formData.policeReportNumber || undefined,
      weatherConditions: this.formData.weatherConditions || undefined,
      roadConditions: this.formData.roadConditions || undefined
    };

    if (this.editingClaim) {
      const claimId = parseInt(this.editingClaim.id);
      this.apiService.updateAccidentClaim(claimId, request).subscribe({
        next: () => {
          this.uploadPendingPhotos(claimId);
          this.loadClaims();
          this.closeForm();
        },
        error: (err) => console.error('Update error:', err)
      });
    } else {
      this.apiService.createAccidentClaim(request).subscribe({
        next: (claimId) => {
          this.uploadPendingPhotos(claimId);
          this.loadClaims();
          this.closeForm();
        },
        error: (err) => console.error('Create error:', err)
      });
    }
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

  // Photo handling
  onPhotosSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    if (!input.files) return;
    Array.from(input.files).forEach(file => {
      if (!file.type.startsWith('image/') || file.size > 10_000_000) return;
      const reader = new FileReader();
      reader.onload = (e) => {
        this.pendingPhotos.push({ file, preview: e.target?.result as string });
        this.cdr.detectChanges();
      };
      reader.readAsDataURL(file);
    });
    input.value = '';
  }

  onPhotoDrop(event: DragEvent) {
    event.preventDefault();
    event.stopPropagation();
    if (!event.dataTransfer?.files) return;
    Array.from(event.dataTransfer.files).forEach(file => {
      if (!file.type.startsWith('image/') || file.size > 10_000_000) return;
      const reader = new FileReader();
      reader.onload = (e) => {
        this.pendingPhotos.push({ file, preview: e.target?.result as string });
        this.cdr.detectChanges();
      };
      reader.readAsDataURL(file);
    });
  }

  removePendingPhoto(index: number) {
    this.pendingPhotos.splice(index, 1);
  }

  removeExistingPhoto(photo: any, index: number) {
    const claimId = parseInt(this.editingClaim?.id || '0');
    if (claimId && photo.id) {
      this.apiService.deleteAccidentClaimDocument(claimId, photo.id).subscribe();
    }
    this.existingPhotos.splice(index, 1);
  }

  uploadPendingPhotos(claimId: number) {
    if (this.pendingPhotos.length === 0) return;
    const files = this.pendingPhotos.map(p => p.file);
    this.apiService.uploadAccidentClaimDocuments(claimId, files, 'photo').subscribe({
      next: () => {
        this.pendingPhotos = [];
        this.cdr.detectChanges();
      },
      error: (err) => console.error('Upload error:', err)
    });
  }

  getPhotoUrl(fileUrl: string): string {
    if (fileUrl.startsWith('http')) return fileUrl;
    return fileUrl;
  }

  openPhotoFullscreen(photo: any) {
    this.fullscreenPhoto = photo;
  }
}
