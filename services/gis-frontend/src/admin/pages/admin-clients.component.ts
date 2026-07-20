import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService, Client, SubscriptionType, BillingOverviewItem } from '../services/admin.service';
import { environment } from '../../environments/environment';

@Component({
  selector: 'admin-clients',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent],
  template: `
    <admin-layout pageTitle="Gestion des Sociétés">
      <div class="clients-page">

        <!-- Bandeau supervision abonnements : sociétés qui expirent, en grâce (impayées) ou bloquées -->
        <div class="billing-alerts" *ngIf="billingItems.length">
          <div class="ba-head">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M10.3 4.2 2.8 17.5A2 2 0 0 0 4.5 20.5h15a2 2 0 0 0 1.7-3L13.7 4.2a2 2 0 0 0-3.4 0z"/><path d="M12 9.5v4M12 16.8v.2"/></svg>
            <b>{{ billingItems.length }} abonnement(s) à surveiller</b>
          </div>
          <div class="ba-list">
            <div class="ba-item" *ngFor="let b of billingItems" [class.blocked]="b.level==='blocked'" [class.danger]="b.level==='danger'" (click)="viewClientById(b.id)">
              <span class="ba-name">{{ b.name }}</span>
              <span class="ba-info">
                <ng-container *ngIf="b.reason==='expiring'">expire dans {{ b.daysRemaining }} j</ng-container>
                <ng-container *ngIf="b.reason==='grace'">EXPIRÉ — grâce {{ b.graceDaysLeft }} j</ng-container>
                <ng-container *ngIf="b.reason==='expired' && b.level==='blocked'">BLOQUÉ — expiré depuis {{ -(b.daysRemaining || 0) }} j</ng-container>
                <ng-container *ngIf="b.reason==='expired' && b.level!=='blocked'">expiré depuis {{ -(b.daysRemaining || 0) }} j — suspension auto désactivée</ng-container>
                <ng-container *ngIf="b.reason==='suspended' || b.reason==='cancelled'">suspendu</ng-container>
              </span>
              <span class="ba-unpaid" *ngIf="b.unpaid">impayé</span>
              <span class="ba-amount" *ngIf="b.amountDue">{{ b.amountDue | number:'1.0-0' }} {{ currencyCode }}</span>
            </div>
          </div>
        </div>

        <div class="page-header">
          <div class="header-left">
            <div class="search-box">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
              </svg>
              <input type="text" [(ngModel)]="searchQuery" (input)="filterClients()" placeholder="Rechercher une société..." />
            </div>
            <select class="filter-select" [(ngModel)]="statusFilter" (change)="filterClients()">
              <option value="all">Tous les statuts</option>
              <option value="active">Actif</option>
              <option value="suspended">Suspendu</option>
              <option value="pending">En attente</option>
            </select>
          </div>
          <button class="add-btn" (click)="openWizard()">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
            </svg>
            Ajouter Société
          </button>
        </div>

        <div class="clients-grid">
          <div class="client-card" *ngFor="let client of filteredClients" [class]="client.status">
            <div class="card-header">
              <div class="client-avatar">{{ client.name?.charAt(0) || 'C' }}</div>
              <div class="client-info">
                <h3>{{ client.name }}</h3>
                <span class="client-type">{{ client.type | titlecase }}</span>
              </div>
              <div class="status-badge" [class]="client.status">{{ client.status | titlecase }}</div>
            </div>

            <div class="card-body">
              <div class="info-row">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/>
                  <polyline points="22,6 12,13 2,6"/>
                </svg>
                <span>{{ client.email }}</span>
              </div>
              <div class="info-row" *ngIf="client.phone">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72"/>
                </svg>
                <span>{{ client.phone }}</span>
              </div>

              <div class="stats-row">
                <div class="stat">
                  <span class="stat-value">{{ client.currentVehicles }}/{{ client.maxVehicles }}</span>
                  <span class="stat-label">Véhicules</span>
                </div>
                <div class="stat">
                  <span class="stat-value">{{ client.currentUsers }}</span>
                  <span class="stat-label">Utilisateurs</span>
                </div>
                <div class="stat">
                  <span class="stat-value">{{ client.subscriptionName || 'Aucun' }}</span>
                  <span class="stat-label">Plan</span>
                </div>
              </div>
            </div>

            <div class="card-footer">
              <span class="joined-date">Créé le {{ formatDate(client.createdAt) }}</span>
              <div class="actions">
                <button class="action-btn edit" (click)="editClient(client)" title="Edit">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                    <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                  </svg>
                </button>
                <button class="action-btn" [class.suspend]="client.status === 'active'" [class.activate]="client.status !== 'active'"
                        (click)="toggleClientStatus(client)" [title]="client.status === 'active' ? 'Suspend' : 'Activate'">
                  <svg *ngIf="client.status === 'active'" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <circle cx="12" cy="12" r="10"/><line x1="4.93" y1="4.93" x2="19.07" y2="19.07"/>
                  </svg>
                  <svg *ngIf="client.status !== 'active'" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                    <polyline points="22,4 12,14.01 9,11.01"/>
                  </svg>
                </button>
                <button class="action-btn view" (click)="viewClient(client)" title="View Details">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>
                  </svg>
                </button>
              </div>
            </div>
          </div>
        </div>

        <!-- View/Edit Modal -->
        <div class="popup-overlay" *ngIf="showEditModal || showViewModal" (click)="closeModals()">
          <div class="popup-container" (click)="$event.stopPropagation()">
            <div class="popup-header">
              <div class="header-title">
                <div class="header-icon">
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><polyline points="9,22 9,12 15,12 15,22"/>
                  </svg>
                </div>
                <h2>{{ showViewModal ? 'Détails Société' : 'Modifier Société' }}</h2>
              </div>
              <button class="close-btn" (click)="closeModals()" title="Fermer">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                </svg>
              </button>
            </div>

            <div class="popup-body" *ngIf="showEditModal">
              <div class="form-section">
                <div class="section-title">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>
                  </svg>
                  <span>Informations société</span>
                </div>
                <div class="form-grid">
                  <div class="form-group full-width">
                    <label>Nom de la société <span class="required">*</span></label>
                    <input type="text" [(ngModel)]="clientForm.name" placeholder="Nom de la société" />
                  </div>
                  <div class="form-group">
                    <label>Email société</label>
                    <input type="email" [(ngModel)]="clientForm.email" placeholder="contact@societe.tn" />
                  </div>
                  <div class="form-group">
                    <label>Téléphone</label>
                    <input type="tel" [(ngModel)]="clientForm.phone" placeholder="+216 XX XXX XXX" />
                  </div>
                  <div class="form-group">
                    <label>Type de société <span class="required">*</span></label>
                    <select [(ngModel)]="clientForm.type">
                      <option value="transport">Transport</option>
                      <option value="location">Location</option>
                      <option value="autre">Autre</option>
                    </select>
                  </div>
                  <div class="form-group">
                    <label>Abonnement <span class="required">*</span></label>
                    <select [(ngModel)]="clientForm.subscriptionId">
                      <option [value]="null">Sélectionner un abonnement</option>
                      <option *ngFor="let sub of subscriptionTypes" [value]="sub.id">{{ sub.name }} - {{ sub.yearlyPrice }} {{ currencyCode }}/an</option>
                    </select>
                  </div>
                </div>
              </div>
            </div>

            <div class="popup-body view-mode" *ngIf="showViewModal && selectedClient">
              <div class="view-header">
                <div class="client-avatar large">{{ selectedClient.name?.charAt(0) || 'C' }}</div>
                <div>
                  <h3>{{ selectedClient.name }}</h3>
                  <span class="status-badge" [class]="selectedClient.status">{{ selectedClient.status | titlecase }}</span>
                </div>
              </div>
              <div class="form-section">
                <div class="section-title">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/><polyline points="22,6 12,13 2,6"/>
                  </svg>
                  <span>Informations de contact</span>
                </div>
                <div class="view-row"><span>Email:</span><span>{{ selectedClient.email }}</span></div>
                <div class="view-row"><span>Téléphone:</span><span>{{ selectedClient.phone || 'N/A' }}</span></div>
              </div>
              <div class="form-section">
                <div class="section-title">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <rect x="1" y="4" width="22" height="16" rx="2" ry="2"/><line x1="1" y1="10" x2="23" y2="10"/>
                  </svg>
                  <span>Détails abonnement</span>
                </div>
                <div class="view-row"><span>Plan:</span><span>{{ selectedClient.subscriptionName || 'Aucun' }}</span></div>
                <div class="view-row"><span>Véhicules:</span><span>{{ selectedClient.currentVehicles }} / {{ selectedClient.maxVehicles }}</span></div>
                <div class="view-row"><span>Utilisateurs:</span><span>{{ selectedClient.currentUsers }}</span></div>
              </div>
              <div class="form-section">
                <div class="section-title">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <circle cx="12" cy="12" r="10"/><polyline points="12,6 12,12 16,14"/>
                  </svg>
                  <span>Compte</span>
                </div>
                <div class="view-row"><span>Créé le:</span><span>{{ formatDate(selectedClient.createdAt) }}</span></div>
                <div class="view-row"><span>Dernière activité:</span><span>{{ selectedClient.lastActivity ? formatDate(selectedClient.lastActivity) : 'N/A' }}</span></div>
              </div>
            </div>

            <div class="popup-footer" *ngIf="showEditModal">
              <button class="btn-secondary" (click)="closeModals()">Annuler</button>
              <button class="btn-primary" (click)="saveClient()">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/>
                </svg>
                Modifier
              </button>
            </div>
            <div class="popup-footer" *ngIf="showViewModal">
              <button class="btn-secondary" (click)="closeModals()">Fermer</button>
              <button class="btn-primary" (click)="editClient(selectedClient!)">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                </svg>
                Modifier
              </button>
            </div>
          </div>
        </div>

        <!-- ========== WIZARD MULTI-ÉTAPES ========== -->
        <div class="modal-overlay" *ngIf="showWizard" (mousedown)="closeWizard()">
          <div class="modal wizard-modal" (mousedown)="$event.stopPropagation()">
            
            <!-- Progress Steps -->
            <div class="wizard-progress">
              <div class="progress-step" [class.active]="wizardStep >= 1" [class.completed]="wizardStep > 1">
                <div class="step-number">1</div>
                <span>Société</span>
              </div>
              <div class="progress-line" [class.active]="wizardStep > 1"></div>
              <div class="progress-step" [class.active]="wizardStep >= 2" [class.completed]="wizardStep > 2">
                <div class="step-number">2</div>
                <span>Permissions</span>
              </div>
              <div class="progress-line" [class.active]="wizardStep > 2"></div>
              <div class="progress-step" [class.active]="wizardStep >= 3">
                <div class="step-number">3</div>
                <span>Administrateur</span>
              </div>
            </div>

            <!-- Step 1: Company Info -->
            <div class="wizard-content" *ngIf="wizardStep === 1">
              <div class="wizard-header">
                <h2>Informations de la société</h2>
                <p class="wizard-subtitle">Renseignez les informations de base de la nouvelle société</p>
              </div>

              <div class="wizard-body">
                <div class="form-group">
                  <label>Nom de la société <span class="required">*</span></label>
                  <input type="text" [(ngModel)]="wizardData.companyName" 
                         placeholder="Ex: Transport Express SARL" 
                         [class.error]="wizardErrors['companyName']" />
                  <span class="error-text" *ngIf="wizardErrors['companyName']">{{ wizardErrors['companyName'] }}</span>
                </div>

                <div class="form-row">
                  <div class="form-group">
                    <label>Email de la société <span class="required">*</span></label>
                    <input type="email" [(ngModel)]="wizardData.companyEmail" 
                           placeholder="contact@societe.tn"
                           [class.error]="wizardErrors['companyEmail']" />
                    <span class="error-text" *ngIf="wizardErrors['companyEmail']">{{ wizardErrors['companyEmail'] }}</span>
                  </div>
                  <div class="form-group">
                    <label>Téléphone <span class="required">*</span></label>
                    <input type="tel" [(ngModel)]="wizardData.companyPhone" 
                           placeholder="+216 XX XXX XXX"
                           [class.error]="wizardErrors['companyPhone']" />
                    <span class="error-text" *ngIf="wizardErrors['companyPhone']">{{ wizardErrors['companyPhone'] }}</span>
                  </div>
                </div>

                <div class="form-row">
                  <div class="form-group">
                    <label>Type de société <span class="required">*</span></label>
                    <select [(ngModel)]="wizardData.companyType">
                      <option value="transport">Transport</option>
                      <option value="location">Location</option>
                      <option value="autre">Autre</option>
                    </select>
                  </div>
                  <div class="form-group">
                    <label>Abonnement <span class="required">*</span></label>
                    <select [(ngModel)]="wizardData.subscriptionTypeId" 
                            (change)="onSubscriptionChange()"
                            [class.error]="wizardErrors['subscriptionTypeId']">
                      <option [ngValue]="null">-- Sélectionner --</option>
                      <option *ngFor="let sub of subscriptionTypes" [ngValue]="sub.id">
                        {{ sub.name }} - {{ sub.yearlyPrice }} {{ currencyCode }}/an
                      </option>
                    </select>
                    <span class="error-text" *ngIf="wizardErrors['subscriptionTypeId']">{{ wizardErrors['subscriptionTypeId'] }}</span>
                  </div>
                </div>

                <!-- Subscription Preview -->
                <div class="subscription-preview" *ngIf="selectedSubscription">
                  <div class="preview-header">
                    <span class="preview-icon">💼</span>
                    <span>{{ selectedSubscription.name }}</span>
                  </div>
                  <div class="preview-details">
                    <span><strong>{{ selectedSubscription.maxVehicles }}</strong> véhicules</span>
                    <span><strong>{{ selectedSubscription.maxUsers }}</strong> utilisateurs</span>
                    <span><strong>{{ selectedSubscription.maxGpsDevices }}</strong> GPS</span>
                  </div>
                </div>
              </div>

              <div class="wizard-footer">
                <button class="btn-secondary" (click)="closeWizard()">Annuler</button>
                <button class="btn-primary" (click)="nextStep()" [disabled]="!canProceedStep1()">
                  Suivant
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M9 18l6-6-6-6"/>
                  </svg>
                </button>
              </div>
            </div>

            <!-- Step 2: Subscription Permissions -->
            <div class="wizard-content" *ngIf="wizardStep === 2">
              <div class="wizard-header">
                <h2>Permissions de l'abonnement</h2>
                <p class="wizard-subtitle">Voici les fonctionnalités incluses dans l'abonnement <strong>{{ selectedSubscription?.name }}</strong></p>
              </div>

              <div class="wizard-body permissions-view">
                <!-- Limits Section -->
                <div class="permission-section">
                  <h4>
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <rect x="3" y="3" width="18" height="18" rx="2"/><path d="M9 9h6v6H9z"/>
                    </svg>
                    Limites de ressources
                  </h4>
                  <div class="limits-grid">
                    <div class="limit-card">
                      <span class="limit-value">{{ selectedSubscription?.maxVehicles }}</span>
                      <span class="limit-label">Véhicules max</span>
                    </div>
                    <div class="limit-card">
                      <span class="limit-value">{{ selectedSubscription?.maxUsers }}</span>
                      <span class="limit-label">Utilisateurs max</span>
                    </div>
                    <div class="limit-card">
                      <span class="limit-value">{{ selectedSubscription?.maxGpsDevices }}</span>
                      <span class="limit-label">GPS max</span>
                    </div>
                    <div class="limit-card">
                      <span class="limit-value">{{ selectedSubscription?.maxGeofences }}</span>
                      <span class="limit-label">Géofences max</span>
                    </div>
                  </div>
                </div>

                <!-- Features Section -->
                <div class="permission-section">
                  <h4>
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/>
                    </svg>
                    Fonctionnalités incluses
                  </h4>
                  <div class="features-list">
                    <div class="feature-item" [class.enabled]="selectedSubscription?.gpsTracking">
                      <span class="feature-icon">{{ selectedSubscription?.gpsTracking ? '✓' : '✗' }}</span>
                      <span>Suivi GPS temps réel</span>
                    </div>
                    <div class="feature-item" [class.enabled]="selectedSubscription?.gpsInstallation">
                      <span class="feature-icon">{{ selectedSubscription?.gpsInstallation ? '✓' : '✗' }}</span>
                      <span>Installation GPS incluse</span>
                    </div>
                    <div class="feature-item" [class.enabled]="selectedSubscription?.realTimeAlerts">
                      <span class="feature-icon">{{ selectedSubscription?.realTimeAlerts ? '✓' : '✗' }}</span>
                      <span>Alertes temps réel</span>
                    </div>
                    <div class="feature-item" [class.enabled]="selectedSubscription?.historyPlayback">
                      <span class="feature-icon">{{ selectedSubscription?.historyPlayback ? '✓' : '✗' }}</span>
                      <span>Historique des trajets</span>
                    </div>
                    <div class="feature-item" [class.enabled]="selectedSubscription?.advancedReports">
                      <span class="feature-icon">{{ selectedSubscription?.advancedReports ? '✓' : '✗' }}</span>
                      <span>Rapports avancés</span>
                    </div>
                    <div class="feature-item" [class.enabled]="selectedSubscription?.fuelAnalysis">
                      <span class="feature-icon">{{ selectedSubscription?.fuelAnalysis ? '✓' : '✗' }}</span>
                      <span>Analyse carburant</span>
                    </div>
                    <div class="feature-item" [class.enabled]="selectedSubscription?.drivingBehavior">
                      <span class="feature-icon">{{ selectedSubscription?.drivingBehavior ? '✓' : '✗' }}</span>
                      <span>Comportement de conduite</span>
                    </div>
                    <div class="feature-item" [class.enabled]="selectedSubscription?.apiAccess">
                      <span class="feature-icon">{{ selectedSubscription?.apiAccess ? '✓' : '✗' }}</span>
                      <span>Accès API</span>
                    </div>
                  </div>
                </div>

                <!-- Price Summary -->
                <div class="price-summary">
                  <div class="price-row">
                    <span>Tarif annuel</span>
                    <span class="price-value">{{ selectedSubscription?.yearlyPrice }} {{ currencyCode }}/an</span>
                  </div>
                  <div class="price-row">
                    <span>Rétention historique</span>
                    <span>{{ selectedSubscription?.historyRetentionDays }} jours</span>
                  </div>
                </div>
              </div>

              <div class="wizard-footer">
                <button class="btn-secondary" (click)="previousStep()">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M15 18l-6-6 6-6"/>
                  </svg>
                  Précédent
                </button>
                <button class="btn-primary" (click)="nextStep()">
                  Suivant
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M9 18l6-6-6-6"/>
                  </svg>
                </button>
              </div>
            </div>

            <!-- Step 3: Admin Account -->
            <div class="wizard-content" *ngIf="wizardStep === 3">
              <div class="wizard-header">
                <h2>Chef de société</h2>
                <p class="wizard-subtitle">Créez le compte administrateur principal de la société</p>
              </div>

              <div class="wizard-body">
                <div class="admin-info-banner">
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <circle cx="12" cy="12" r="10"/><path d="M12 16v-4"/><path d="M12 8h.01"/>
                  </svg>
                  <span>Ce compte aura accès à toutes les permissions de l'abonnement et pourra créer des rôles et utilisateurs.</span>
                </div>

                <div class="form-row">
                  <div class="form-group">
                    <label>Prénom <span class="required">*</span></label>
                    <input type="text" [(ngModel)]="wizardData.adminFirstName" 
                           placeholder="Prénom"
                           [class.error]="wizardErrors['adminFirstName']" />
                    <span class="error-text" *ngIf="wizardErrors['adminFirstName']">{{ wizardErrors['adminFirstName'] }}</span>
                  </div>
                  <div class="form-group">
                    <label>Nom <span class="required">*</span></label>
                    <input type="text" [(ngModel)]="wizardData.adminLastName" 
                           placeholder="Nom"
                           [class.error]="wizardErrors['adminLastName']" />
                    <span class="error-text" *ngIf="wizardErrors['adminLastName']">{{ wizardErrors['adminLastName'] }}</span>
                  </div>
                </div>

                <div class="form-group">
                  <label>Téléphone <span class="required">*</span></label>
                  <input type="tel" [(ngModel)]="wizardData.adminPhone" 
                         placeholder="+216 XX XXX XXX"
                         [class.error]="wizardErrors['adminPhone']" />
                  <span class="error-text" *ngIf="wizardErrors['adminPhone']">{{ wizardErrors['adminPhone'] }}</span>
                </div>

                <div class="form-group">
                  <label>Adresse e-mail (identifiant de connexion) <span class="required">*</span></label>
                  <input type="email" [(ngModel)]="wizardData.adminEmail" 
                         placeholder="admin@societe.tn"
                         [class.error]="wizardErrors['adminEmail']" />
                  <span class="error-text" *ngIf="wizardErrors['adminEmail']">{{ wizardErrors['adminEmail'] }}</span>
                </div>

                <div class="form-group">
                  <label>Mot de passe <span class="required">*</span></label>
                  <div class="password-input">
                    <input [type]="showPassword ? 'text' : 'password'" 
                           [(ngModel)]="wizardData.adminPassword" 
                           placeholder="Minimum 8 caractères"
                           [class.error]="wizardErrors['adminPassword']" />
                    <button type="button" class="toggle-password" (click)="showPassword = !showPassword">
                      <svg *ngIf="!showPassword" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>
                      </svg>
                      <svg *ngIf="showPassword" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24"/>
                        <line x1="1" y1="1" x2="23" y2="23"/>
                      </svg>
                    </button>
                  </div>
                  <span class="error-text" *ngIf="wizardErrors['adminPassword']">{{ wizardErrors['adminPassword'] }}</span>
                  <div class="password-strength" *ngIf="wizardData.adminPassword">
                    <div class="strength-bar" [class]="getPasswordStrength()"></div>
                    <span>{{ getPasswordStrengthText() }}</span>
                  </div>
                </div>
              </div>

              <div class="wizard-footer">
                <button class="btn-secondary" (click)="previousStep()">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M15 18l-6-6 6-6"/>
                  </svg>
                  Précédent
                </button>
                <button class="btn-success" (click)="createCompany()" [disabled]="isCreating || !canProceedStep3()">
                  <svg *ngIf="!isCreating" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polyline points="20 6 9 17 4 12"/>
                  </svg>
                  <span *ngIf="isCreating" class="spinner"></span>
                  {{ isCreating ? 'Création...' : 'Créer la société' }}
                </button>
              </div>
            </div>

          </div>
        </div>

        <!-- Success Modal -->
        <div class="modal-overlay" *ngIf="showSuccessModal">
          <div class="modal success-modal" (click)="$event.stopPropagation()">
            <div class="success-content">
              <div class="success-icon">
                <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                  <polyline points="22 4 12 14.01 9 11.01"/>
                </svg>
              </div>
              <h2>Société créée avec succès!</h2>
              <p>La société <strong>{{ wizardData.companyName }}</strong> a été créée.</p>
              <p class="credentials-info">
                Les identifiants de connexion ont été envoyés à <strong>{{ wizardData.adminEmail }}</strong>
              </p>
              <button class="btn-primary" (click)="closeSuccessModal()">Fermer</button>
            </div>
          </div>
        </div>
      </div>
    </admin-layout>
  `,
  styles: [`
    .clients-page {
      display: flex;
      flex-direction: column;
      gap: 24px;
    }

    /* ── Bandeau supervision abonnements ── */
    .billing-alerts {
      background: rgba(217, 119, 6, 0.06);
      border: 1px solid rgba(217, 119, 6, 0.25);
      border-left: 4px solid var(--adm-amber);
      border-radius: 16px;
      padding: 14px 16px;
      box-shadow: var(--adm-shadow);
    }
    .ba-head {
      display: flex; align-items: center; gap: 8px;
      font-size: 13px; color: var(--adm-amber-ink); margin-bottom: 10px;
    }
    .ba-head svg { stroke: var(--adm-amber); flex: none; }
    .ba-list { display: flex; flex-direction: column; gap: 6px; }
    .ba-item {
      display: flex; align-items: center; gap: 10px; flex-wrap: wrap;
      background: var(--adm-card); border: 1px solid rgba(217, 119, 6, 0.28); border-radius: 10px;
      padding: 8px 12px; cursor: pointer; transition: box-shadow .2s, transform .2s;
    }
    .ba-item:hover { box-shadow: var(--adm-shadow-hover); transform: translateY(-1px); }
    .ba-item.danger { border-color: rgba(220, 38, 38, 0.35); }
    .ba-item.blocked { border-color: rgba(220, 38, 38, 0.45); background: rgba(220, 38, 38, 0.06); }
    .ba-name { font-weight: 700; font-size: 13px; color: var(--adm-ink); }
    .ba-info { font-size: 12px; color: var(--adm-sub); flex: 1; }
    .ba-item.danger .ba-info, .ba-item.blocked .ba-info { color: var(--adm-red-ink); font-weight: 600; }
    .ba-unpaid {
      font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: .04em;
      color: var(--adm-red-ink); background: rgba(220, 38, 38, 0.10); border-radius: 999px; padding: 2px 8px;
    }
    .ba-amount { font-size: 12.5px; font-weight: 700; color: var(--adm-amber-ink); font-variant-numeric: tabular-nums; }

    .page-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      flex-wrap: wrap;
      gap: 16px;
    }

    .header-left {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .search-box {
      display: flex;
      align-items: center;
      gap: 10px;
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      padding: 10px 14px;
      width: 280px;
      transition: border-color 0.2s, box-shadow 0.2s;
    }

    .search-box:focus-within {
      border-color: var(--adm-indigo);
      box-shadow: 0 0 0 3px rgba(79, 70, 229, 0.12);
    }

    .search-box svg { color: var(--adm-sub); }

    .search-box input {
      flex: 1;
      border: none;
      background: transparent;
      color: var(--adm-ink);
      font-size: 14px;
      outline: none;
    }

    .filter-select {
      padding: 10px 14px;
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      color: var(--adm-ink);
      font-size: 14px;
      outline: none;
      cursor: pointer;
      transition: border-color 0.2s, box-shadow 0.2s;
    }

    .filter-select:focus {
      border-color: var(--adm-indigo);
      box-shadow: 0 0 0 3px rgba(79, 70, 229, 0.12);
    }

    .add-btn {
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

    .add-btn:hover {
      background: var(--adm-indigo-ink);
      transform: translateY(-1px);
      box-shadow: var(--adm-shadow-hover);
    }

    .clients-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(360px, 1fr));
      gap: 20px;
    }

    .client-card {
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 16px;
      overflow: hidden;
      transition: box-shadow 0.2s, transform 0.2s;
      box-shadow: var(--adm-shadow);
      animation: rise 0.25s ease-out both;
    }

    .client-card:hover {
      box-shadow: var(--adm-shadow-hover);
      transform: translateY(-1px);
    }

    .client-card.suspended {
      opacity: 0.7;
    }

    .card-header {
      display: flex;
      align-items: center;
      gap: 14px;
      padding: 20px;
      border-bottom: 1px solid var(--adm-border);
    }

    .client-avatar {
      width: 48px;
      height: 48px;
      background: linear-gradient(135deg, var(--adm-indigo) 0%, var(--adm-indigo-ink) 100%);
      border-radius: 12px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 700;
      font-size: 20px;
      color: #fff;
    }

    .client-avatar.large {
      width: 64px;
      height: 64px;
      font-size: 28px;
    }

    .client-info {
      flex: 1;
    }

    .client-info h3 {
      margin: 0 0 4px 0;
      font-size: 16px;
      font-weight: 600;
      color: var(--adm-ink);
    }

    .client-type {
      font-size: 13px;
      color: var(--adm-sub);
    }

    .status-badge {
      padding: 3px 10px;
      border-radius: 999px;
      font-size: 11px;
      font-weight: 700;
    }

    .status-badge.active {
      background: rgba(5, 150, 105, 0.10);
      color: var(--adm-green-ink);
    }

    .status-badge.suspended {
      background: rgba(220, 38, 38, 0.10);
      color: var(--adm-red-ink);
    }

    .status-badge.pending {
      background: rgba(217, 119, 6, 0.10);
      color: var(--adm-amber-ink);
    }

    .card-body {
      padding: 20px;
    }

    .info-row {
      display: flex;
      align-items: center;
      gap: 10px;
      margin-bottom: 12px;
      font-size: 13px;
      color: var(--adm-sub);
    }

    .info-row svg {
      color: var(--adm-sub);
    }

    .stats-row {
      display: flex;
      gap: 20px;
      margin-top: 16px;
      padding-top: 16px;
      border-top: 1px solid var(--adm-border);
    }

    .stat {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .stat-value {
      font-size: 16px;
      font-weight: 700;
      color: var(--adm-ink);
      font-variant-numeric: tabular-nums;
    }

    .stat-label {
      font-size: 11px;
      color: var(--adm-sub);
      text-transform: uppercase;
      letter-spacing: 0.06em;
    }

    .card-footer {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 16px 20px;
      background: #f8fafc;
      border-top: 1px solid var(--adm-border);
    }

    .joined-date {
      font-size: 12px;
      color: var(--adm-sub);
    }

    .actions {
      display: flex;
      gap: 8px;
    }

    .action-btn {
      width: 32px;
      height: 32px;
      border: none;
      border-radius: 8px;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }

    .action-btn.edit {
      background: rgba(79, 70, 229, 0.10);
      color: var(--adm-indigo);
    }

    .action-btn.suspend {
      background: rgba(220, 38, 38, 0.10);
      color: var(--adm-red);
    }

    .action-btn.activate {
      background: rgba(5, 150, 105, 0.10);
      color: var(--adm-green);
    }

    .action-btn.view {
      background: var(--adm-track);
      color: var(--adm-sub);
    }

    .action-btn:hover {
      transform: scale(1.1);
    }

    /* Enhanced Popup Styles */
    .popup-overlay {
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(15, 23, 42, 0.55);
      backdrop-filter: blur(4px);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
      padding: 20px;
      animation: fadeIn 0.2s ease-out;
    }

    @keyframes fadeIn {
      from { opacity: 0; }
      to { opacity: 1; }
    }

    .popup-container {
      background: var(--adm-card);
      border-radius: 18px;
      box-shadow: 0 24px 60px -24px rgba(2, 6, 23, 0.45);
      max-width: 680px;
      width: 100%;
      max-height: 85vh;
      overflow: hidden;
      display: flex;
      flex-direction: column;
      animation: slideUp 0.3s cubic-bezier(0.16, 1, 0.3, 1);
    }

    @keyframes slideUp {
      from { transform: translateY(30px) scale(0.97); opacity: 0; }
      to { transform: translateY(0) scale(1); opacity: 1; }
    }

    .popup-header {
      padding: 16px 24px;
      border-bottom: 1px solid var(--adm-border);
      display: flex;
      align-items: center;
      justify-content: space-between;
      background: var(--adm-card);
    }

    .header-title {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .header-icon {
      width: 36px;
      height: 36px;
      background: rgba(79, 70, 229, 0.10);
      border-radius: 10px;
      display: flex;
      align-items: center;
      justify-content: center;
      color: var(--adm-indigo);
    }

    .popup-header h2 {
      margin: 0;
      font-size: 16px;
      font-weight: 600;
      color: var(--adm-ink);
    }

    .popup-header .close-btn {
      background: var(--adm-track);
      border: none;
      color: var(--adm-sub);
      width: 32px;
      height: 32px;
      border-radius: 8px;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }

    .popup-header .close-btn:hover {
      background: var(--adm-border);
      color: var(--adm-ink);
    }

    .popup-body {
      padding: 0;
      overflow-y: auto;
      flex: 1;
      background: #f8fafc;
    }

    .popup-body.view-mode {
      padding: 20px 24px;
      background: white;
    }

    .form-section {
      padding: 20px 24px;
      background: var(--adm-card);
      border-bottom: 1px solid var(--adm-border);
    }

    .form-section:last-child {
      border-bottom: none;
    }

    .section-title {
      display: flex;
      align-items: center;
      gap: 10px;
      margin-bottom: 16px;
      font-size: 12px;
      font-weight: 700;
      color: var(--adm-sub);
      text-transform: uppercase;
      letter-spacing: 0.08em;
    }

    .section-title::before {
      content: '';
      width: 3px;
      height: 12px;
      border-radius: 2px;
      background: var(--adm-indigo);
      flex: none;
    }

    .section-title svg {
      color: var(--adm-indigo);
    }

    .form-grid {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 16px;
    }

    .form-grid .form-group {
      display: flex;
      flex-direction: column;
      gap: 6px;
    }

    .form-grid .form-group.full-width {
      grid-column: 1 / -1;
    }

    .form-grid .form-group label {
      font-size: 12px;
      font-weight: 500;
      color: var(--adm-slate-ink);
    }

    .form-grid .form-group input,
    .form-grid .form-group select {
      padding: 10px 14px;
      background: #f8fafc;
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      color: var(--adm-ink);
      font-size: 13px;
      transition: all 0.2s;
    }

    .form-grid .form-group input:focus,
    .form-grid .form-group select:focus {
      outline: none;
      border-color: var(--adm-indigo);
      background: var(--adm-card);
      box-shadow: 0 0 0 3px rgba(79, 70, 229, 0.12);
    }

    .popup-footer {
      padding: 16px 24px;
      border-top: 1px solid var(--adm-border);
      display: flex;
      gap: 12px;
      justify-content: flex-end;
      background: var(--adm-card);
    }

    .popup-footer .btn-secondary {
      padding: 9px 18px;
      background: #fff;
      color: var(--adm-ink);
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      font-weight: 600;
      font-size: 13px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .popup-footer .btn-secondary:hover {
      border-color: var(--adm-indigo);
    }

    .popup-footer .btn-primary {
      padding: 9px 18px;
      background: var(--adm-indigo);
      color: white;
      border: none;
      border-radius: 10px;
      font-weight: 600;
      font-size: 13px;
      cursor: pointer;
      display: flex;
      align-items: center;
      gap: 8px;
      transition: all 0.2s;
      box-shadow: var(--adm-shadow);
    }

    .popup-footer .btn-primary:hover {
      background: var(--adm-indigo-ink);
      transform: translateY(-1px);
      box-shadow: var(--adm-shadow-hover);
    }

    /* Legacy modal styles for wizard */
    .modal-overlay {
      position: fixed;
      inset: 0;
      background: rgba(15, 23, 42, 0.55);
      backdrop-filter: blur(4px);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
      padding: 20px;
      animation: fadeIn 0.2s ease-out;
    }

    .modal {
      background: var(--adm-card);
      border-radius: 18px;
      width: 100%;
      max-width: 520px;
      max-height: 90vh;
      overflow-y: auto;
      box-shadow: 0 24px 60px -24px rgba(2, 6, 23, 0.45);
      animation: slideUp 0.3s cubic-bezier(0.16, 1, 0.3, 1);
    }

    .modal-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 20px 24px;
      border-bottom: 1px solid var(--adm-border);
    }

    .modal-header h2 {
      margin: 0;
      font-size: 18px;
      font-weight: 600;
      color: var(--adm-ink);
    }

    .close-btn {
      width: 36px;
      height: 36px;
      border: none;
      background: var(--adm-track);
      border-radius: 10px;
      color: var(--adm-sub);
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }

    .close-btn:hover {
      background: var(--adm-border);
      color: var(--adm-ink);
    }

    .modal-body {
      padding: 24px;
      display: flex;
      flex-direction: column;
      gap: 20px;
    }

    .form-group {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .form-group label {
      font-size: 14px;
      font-weight: 500;
      color: var(--adm-slate-ink);
    }

    .form-group input, .form-group select {
      padding: 12px 14px;
      background: #f8fafc;
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      color: var(--adm-ink);
      font-size: 14px;
      outline: none;
      transition: border-color 0.2s, box-shadow 0.2s;
    }

    .form-group input:focus, .form-group select:focus {
      border-color: var(--adm-indigo);
      box-shadow: 0 0 0 3px rgba(79, 70, 229, 0.12);
    }

    .form-row {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 16px;
    }

    .view-header {
      display: flex;
      align-items: center;
      gap: 16px;
      padding-bottom: 20px;
      border-bottom: 1px solid var(--adm-border);
    }

    .view-header h3 {
      margin: 0 0 8px 0;
      font-size: 20px;
      color: var(--adm-ink);
    }

    .view-section {
      padding: 16px 0;
      border-bottom: 1px solid var(--adm-border);
    }

    .view-section:last-child {
      border-bottom: none;
    }

    .view-section h4 {
      margin: 0 0 12px 0;
      font-size: 14px;
      font-weight: 600;
      color: var(--adm-indigo-ink);
    }

    .view-row {
      display: flex;
      justify-content: space-between;
      padding: 8px 0;
      font-size: 14px;
    }

    .view-row span:first-child {
      color: var(--adm-sub);
    }

    .view-row span:last-child {
      color: var(--adm-ink);
      font-weight: 500;
    }

    .modal-footer {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 20px 24px;
      border-top: 1px solid var(--adm-border);
    }

    .btn-secondary {
      padding: 9px 18px;
      background: #fff;
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      color: var(--adm-ink);
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-secondary:hover {
      border-color: var(--adm-indigo);
    }

    .btn-primary {
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

    .btn-primary:hover {
      background: var(--adm-indigo-ink);
      box-shadow: var(--adm-shadow-hover);
    }

    .btn-primary:disabled, .btn-success:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }

    .btn-success {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 12px 24px;
      background: var(--adm-green);
      border: none;
      border-radius: 10px;
      color: #fff;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-success:hover:not(:disabled) {
      background: var(--adm-green-ink);
      box-shadow: var(--adm-shadow-hover);
    }

    /* ========== WIZARD STYLES ========== */
    .wizard-modal {
      max-width: 640px;
      overflow: visible;
    }

    .wizard-progress {
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 24px;
      background: #f8fafc;
      border-bottom: 1px solid var(--adm-border);
    }

    .progress-step {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 8px;
    }

    .step-number {
      width: 36px;
      height: 36px;
      border-radius: 50%;
      background: var(--adm-track);
      color: var(--adm-sub);
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 600;
      font-size: 14px;
      transition: all 0.3s;
    }

    .progress-step.active .step-number {
      background: var(--adm-indigo);
      color: #fff;
      box-shadow: 0 0 0 4px var(--adm-glow);
    }

    .progress-step.completed .step-number {
      background: var(--adm-green);
      color: #fff;
    }

    .progress-step span {
      font-size: 12px;
      color: var(--adm-sub);
      font-weight: 500;
    }

    .progress-step.active span {
      color: var(--adm-indigo-ink);
      font-weight: 600;
    }

    .progress-line {
      width: 60px;
      height: 3px;
      background: var(--adm-track);
      margin: 0 12px;
      margin-bottom: 24px;
      border-radius: 2px;
      transition: all 0.3s;
    }

    .progress-line.active {
      background: var(--adm-indigo);
    }

    .wizard-content {
      display: flex;
      flex-direction: column;
    }

    .wizard-header {
      padding: 24px 24px 0;
    }

    .wizard-header h2 {
      margin: 0 0 8px;
      font-size: 20px;
      font-weight: 600;
      color: var(--adm-ink);
    }

    .wizard-subtitle {
      margin: 0;
      font-size: 14px;
      color: var(--adm-sub);
    }

    .wizard-body {
      padding: 24px;
      display: flex;
      flex-direction: column;
      gap: 20px;
      max-height: 50vh;
      overflow-y: auto;
    }

    .wizard-footer {
      display: flex;
      justify-content: space-between;
      padding: 20px 24px;
      border-top: 1px solid var(--adm-border);
      background: #f8fafc;
      border-radius: 0 0 18px 18px;
    }

    .wizard-footer .btn-primary,
    .wizard-footer .btn-secondary {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .required { color: var(--adm-red); }

    .form-group input.error,
    .form-group select.error {
      border-color: var(--adm-red);
      background: rgba(220, 38, 38, 0.05);
    }

    .error-text {
      font-size: 12px;
      color: var(--adm-red-ink);
      margin-top: 4px;
    }

    .subscription-preview {
      background: rgba(5, 150, 105, 0.08);
      border: 1px solid rgba(5, 150, 105, 0.25);
      border-radius: 12px;
      padding: 16px;
    }

    .preview-header {
      display: flex;
      align-items: center;
      gap: 10px;
      font-weight: 600;
      color: var(--adm-green-ink);
      margin-bottom: 12px;
    }

    .preview-icon { font-size: 20px; }

    .preview-details {
      display: flex;
      gap: 20px;
      font-size: 13px;
      color: var(--adm-green-ink);
    }

    /* Permissions View (Step 2) */
    .permissions-view {
      gap: 24px;
    }

    .permission-section h4 {
      display: flex;
      align-items: center;
      gap: 10px;
      margin: 0 0 16px;
      font-size: 15px;
      font-weight: 600;
      color: var(--adm-ink);
    }

    .permission-section h4 svg {
      color: var(--adm-indigo);
    }

    .limits-grid {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 12px;
    }

    .limit-card {
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-left: 3px solid var(--adm-indigo);
      border-radius: 12px;
      padding: 16px;
      text-align: center;
      box-shadow: var(--adm-shadow);
    }

    .limit-value {
      display: block;
      font-size: 24px;
      font-weight: 800;
      color: var(--adm-ink);
      font-variant-numeric: tabular-nums;
    }

    .limit-label {
      display: block;
      font-size: 11px;
      color: var(--adm-sub);
      margin-top: 4px;
      text-transform: uppercase;
      letter-spacing: 0.06em;
    }

    .features-list {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 10px;
    }

    .feature-item {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 10px 14px;
      background: rgba(220, 38, 38, 0.08);
      border-radius: 10px;
      font-size: 13px;
      color: var(--adm-red-ink);
    }

    .feature-item.enabled {
      background: rgba(5, 150, 105, 0.08);
      color: var(--adm-green-ink);
    }

    .feature-icon {
      font-weight: 700;
      font-size: 14px;
    }

    .price-summary {
      background: #f8fafc;
      border: 1px solid var(--adm-border);
      border-radius: 12px;
      padding: 16px;
    }

    .price-row {
      display: flex;
      justify-content: space-between;
      padding: 8px 0;
      font-size: 14px;
    }

    .price-row:first-child {
      border-bottom: 1px solid var(--adm-border);
      padding-bottom: 12px;
      margin-bottom: 4px;
    }

    .price-value {
      font-weight: 800;
      color: var(--adm-indigo-ink);
      font-size: 18px;
      font-variant-numeric: tabular-nums;
    }

    /* Admin Form (Step 3) */
    .admin-info-banner {
      display: flex;
      align-items: flex-start;
      gap: 12px;
      padding: 14px 16px;
      background: rgba(8, 145, 178, 0.08);
      border: 1px solid rgba(8, 145, 178, 0.25);
      border-radius: 10px;
      font-size: 13px;
      color: var(--adm-cyan-ink);
    }

    .admin-info-banner svg {
      flex-shrink: 0;
      margin-top: 2px;
    }

    .password-input {
      position: relative;
      display: flex;
      align-items: center;
    }

    .password-input input {
      flex: 1;
      padding-right: 44px;
    }

    .toggle-password {
      position: absolute;
      right: 12px;
      background: none;
      border: none;
      color: var(--adm-sub);
      cursor: pointer;
      padding: 4px;
    }

    .password-strength {
      display: flex;
      align-items: center;
      gap: 10px;
      margin-top: 8px;
    }

    .strength-bar {
      flex: 1;
      height: 4px;
      background: var(--adm-track);
      border-radius: 2px;
      overflow: hidden;
    }

    .strength-bar::before {
      content: '';
      display: block;
      height: 100%;
      transition: width 0.3s;
    }

    .strength-bar.weak::before {
      width: 33%;
      background: var(--adm-red);
    }

    .strength-bar.medium::before {
      width: 66%;
      background: var(--adm-amber);
    }

    .strength-bar.strong::before {
      width: 100%;
      background: var(--adm-green);
    }

    .password-strength span {
      font-size: 11px;
      color: var(--adm-sub);
    }

    .spinner {
      width: 16px;
      height: 16px;
      border: 2px solid rgba(255,255,255,0.3);
      border-top-color: #fff;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }

    /* Success Modal */
    .success-modal {
      max-width: 420px;
      text-align: center;
    }

    .success-content {
      padding: 40px 32px;
    }

    .success-icon {
      width: 80px;
      height: 80px;
      margin: 0 auto 24px;
      background: rgba(5, 150, 105, 0.10);
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      color: var(--adm-green);
    }

    .success-content h2 {
      margin: 0 0 16px;
      font-size: 22px;
      color: var(--adm-ink);
    }

    .success-content p {
      margin: 0 0 8px;
      color: var(--adm-sub);
      font-size: 14px;
    }

    .credentials-info {
      background: rgba(5, 150, 105, 0.08);
      padding: 12px 16px;
      border-radius: 10px;
      margin: 16px 0 24px;
      color: var(--adm-green-ink);
    }

    @keyframes rise {
      from { opacity: 0; transform: translateY(8px); }
      to { opacity: 1; transform: none; }
    }

    @media (prefers-reduced-motion: reduce) {
      .client-card,
      .popup-overlay,
      .popup-container,
      .modal-overlay,
      .modal {
        animation: none;
      }
    }

    @media (max-width: 640px) {
      .limits-grid {
        grid-template-columns: repeat(2, 1fr);
      }
      .features-list {
        grid-template-columns: 1fr;
      }
      .progress-line {
        width: 30px;
      }
    }
  `]
})
export class AdminClientsComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  clients: Client[] = [];
  filteredClients: Client[] = [];
  subscriptionTypes: SubscriptionType[] = [];

  searchQuery = '';
  statusFilter = 'all';

  // Edit/View modals
  showEditModal = false;
  showViewModal = false;
  selectedClient: Client | null = null;

  clientForm = {
    name: '',
    email: '',
    phone: '',
    type: 'transport',
    subscriptionId: undefined as number | undefined
  };

  // ========== WIZARD STATE ==========
  showWizard = false;
  wizardStep = 1;
  showSuccessModal = false;
  isCreating = false;
  showPassword = false;

  wizardData = {
    companyName: '',
    companyEmail: '',
    companyPhone: '',
    companyType: 'transport',
    subscriptionTypeId: null as number | null,
    adminFirstName: '',
    adminLastName: '',
    adminPhone: '',
    adminEmail: '',
    adminPassword: ''
  };

  wizardErrors: Record<string, string> = {};
  selectedSubscription: SubscriptionType | null = null;

  get currencyCode(): string {
    return (environment as { defaultCurrency?: string }).defaultCurrency || 'TND';
  }

  constructor(
    private router: Router,
    private adminService: AdminService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit() {
    if (!this.adminService.isAuthenticated()) {
      this.router.navigate(['/admin/login']);
      return;
    }
    this.loadData();
  }

  /** Sociétés à surveiller (bandeau supervision abonnements). */
  billingItems: BillingOverviewItem[] = [];

  viewClientById(id: number) {
    this.router.navigate(['/admin/clients', id]);
  }

  loadData() {
    this.adminService.getBillingOverview().pipe(takeUntil(this.destroy$)).subscribe({
      next: (o) => { this.billingItems = o?.items ?? []; this.cdr.detectChanges(); },
      error: () => {}
    });

    this.adminService.getClients().pipe(takeUntil(this.destroy$)).subscribe(clients => {
      this.clients = clients;
      this.filterClients();
      this.cdr.detectChanges();
    });

    this.adminService.getSubscriptionTypes().pipe(takeUntil(this.destroy$)).subscribe(types => {
      this.subscriptionTypes = types.filter(t => t.isActive);
      this.cdr.detectChanges();
    });
  }

  filterClients() {
    this.filteredClients = this.clients.filter(client => {
      const matchesSearch = !this.searchQuery ||
        client.name.toLowerCase().includes(this.searchQuery.toLowerCase()) ||
        client.email.toLowerCase().includes(this.searchQuery.toLowerCase());
      const matchesStatus = this.statusFilter === 'all' || client.status === this.statusFilter;
      return matchesSearch && matchesStatus;
    });
  }

  // ========== EDIT/VIEW MODALS ==========
  editClient(client: Client) {
    this.selectedClient = client;
    this.clientForm = {
      name: client.name,
      email: client.email,
      phone: client.phone || '',
      type: client.type,
      subscriptionId: client.subscriptionId
    };
    this.showViewModal = false;
    this.showEditModal = true;
  }

  viewClient(client: Client) {
    this.router.navigate(['/admin/clients', client.id]);
  }

  toggleClientStatus(client: Client) {
    if (client.status === 'active') {
      this.adminService.suspendClient(client.id).pipe(takeUntil(this.destroy$)).subscribe(() => {
        client.status = 'suspended';
      });
    } else {
      this.adminService.activateClient(client.id).pipe(takeUntil(this.destroy$)).subscribe(() => {
        client.status = 'active';
      });
    }
  }

  saveClient() {
    const sub = this.subscriptionTypes.find(s => s.id === this.clientForm.subscriptionId);
    const data = {
      ...this.clientForm,
      subscriptionName: sub?.name
    };

    if (this.showEditModal && this.selectedClient) {
      this.adminService.updateClient(this.selectedClient.id, data).pipe(takeUntil(this.destroy$)).subscribe(() => {
        this.loadData();
        this.closeModals();
      });
    }
  }

  closeModals() {
    this.showEditModal = false;
    this.showViewModal = false;
    this.selectedClient = null;
    this.clientForm = { name: '', email: '', phone: '', type: 'transport', subscriptionId: undefined };
  }

  // ========== WIZARD METHODS ==========
  openWizard() {
    this.showWizard = true;
    this.wizardStep = 1;
    this.wizardErrors = {};
    this.wizardData = {
      companyName: '',
      companyEmail: '',
      companyPhone: '',
      companyType: 'transport',
      subscriptionTypeId: null,
      adminFirstName: '',
      adminLastName: '',
      adminPhone: '',
      adminEmail: '',
      adminPassword: ''
    };
    this.selectedSubscription = null;
  }

  closeWizard() {
    this.showWizard = false;
    this.wizardStep = 1;
    this.wizardErrors = {};
  }

  onSubscriptionChange() {
    this.selectedSubscription = this.subscriptionTypes.find(
      s => s.id === this.wizardData.subscriptionTypeId
    ) || null;
  }

  // Step 1 validation
  canProceedStep1(): boolean {
    return !!(
      this.wizardData.companyName.trim() &&
      this.wizardData.companyEmail.trim() &&
      this.wizardData.companyPhone.trim() &&
      this.wizardData.subscriptionTypeId
    );
  }

  validateStep1(): boolean {
    this.wizardErrors = {};
    
    if (!this.wizardData.companyName.trim()) {
      this.wizardErrors['companyName'] = 'Le nom est requis';
    }
    if (!this.wizardData.companyEmail.trim()) {
      this.wizardErrors['companyEmail'] = 'L\'email est requis';
    } else if (!this.isValidEmail(this.wizardData.companyEmail)) {
      this.wizardErrors['companyEmail'] = 'Email invalide';
    }
    if (!this.wizardData.companyPhone.trim()) {
      this.wizardErrors['companyPhone'] = 'Le téléphone est requis';
    }
    if (!this.wizardData.subscriptionTypeId) {
      this.wizardErrors['subscriptionTypeId'] = 'Sélectionnez un abonnement';
    }
    
    return Object.keys(this.wizardErrors).length === 0;
  }

  // Step 3 validation
  canProceedStep3(): boolean {
    return !!(
      this.wizardData.adminFirstName.trim() &&
      this.wizardData.adminLastName.trim() &&
      this.wizardData.adminPhone.trim() &&
      this.wizardData.adminEmail.trim() &&
      this.wizardData.adminPassword.length >= 8
    );
  }

  validateStep3(): boolean {
    this.wizardErrors = {};
    
    if (!this.wizardData.adminFirstName.trim()) {
      this.wizardErrors['adminFirstName'] = 'Le prénom est requis';
    }
    if (!this.wizardData.adminLastName.trim()) {
      this.wizardErrors['adminLastName'] = 'Le nom est requis';
    }
    if (!this.wizardData.adminPhone.trim()) {
      this.wizardErrors['adminPhone'] = 'Le téléphone est requis';
    }
    if (!this.wizardData.adminEmail.trim()) {
      this.wizardErrors['adminEmail'] = 'L\'email est requis';
    } else if (!this.isValidEmail(this.wizardData.adminEmail)) {
      this.wizardErrors['adminEmail'] = 'Email invalide';
    }
    if (!this.wizardData.adminPassword) {
      this.wizardErrors['adminPassword'] = 'Le mot de passe est requis';
    } else if (this.wizardData.adminPassword.length < 8) {
      this.wizardErrors['adminPassword'] = 'Minimum 8 caractères';
    }
    
    return Object.keys(this.wizardErrors).length === 0;
  }

  nextStep() {
    if (this.wizardStep === 1) {
      if (this.validateStep1()) {
        this.wizardStep = 2;
      }
    } else if (this.wizardStep === 2) {
      this.wizardStep = 3;
    }
  }

  previousStep() {
    if (this.wizardStep > 1) {
      this.wizardStep--;
    }
  }

  createCompany() {
    if (!this.validateStep3()) return;
    
    this.isCreating = true;
    
    const data = {
      name: this.wizardData.companyName,
      email: this.wizardData.companyEmail,
      phone: this.wizardData.companyPhone,
      type: this.wizardData.companyType,
      subscriptionTypeId: this.wizardData.subscriptionTypeId,
      adminName: `${this.wizardData.adminFirstName} ${this.wizardData.adminLastName}`,
      adminEmail: this.wizardData.adminEmail,
      adminPassword: this.wizardData.adminPassword
    };

    this.adminService.createClient(data).pipe(takeUntil(this.destroy$)).subscribe({
      next: () => {
        this.isCreating = false;
        this.showWizard = false;
        this.showSuccessModal = true;
        this.loadData();
      },
      error: (err) => {
        this.isCreating = false;
        console.error('Error creating company:', err);
        alert(err.error?.message || 'Erreur lors de la création de la société');
      }
    });
  }

  closeSuccessModal() {
    this.showSuccessModal = false;
    this.wizardData = {
      companyName: '',
      companyEmail: '',
      companyPhone: '',
      companyType: 'transport',
      subscriptionTypeId: null,
      adminFirstName: '',
      adminLastName: '',
      adminPhone: '',
      adminEmail: '',
      adminPassword: ''
    };
  }

  // ========== UTILITY METHODS ==========
  isValidEmail(email: string): boolean {
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);
  }

  getPasswordStrength(): string {
    const pwd = this.wizardData.adminPassword;
    if (pwd.length < 8) return 'weak';
    
    let score = 0;
    if (pwd.length >= 8) score++;
    if (pwd.length >= 12) score++;
    if (/[A-Z]/.test(pwd)) score++;
    if (/[a-z]/.test(pwd)) score++;
    if (/[0-9]/.test(pwd)) score++;
    if (/[^A-Za-z0-9]/.test(pwd)) score++;
    
    if (score >= 5) return 'strong';
    if (score >= 3) return 'medium';
    return 'weak';
  }

  getPasswordStrengthText(): string {
    const strength = this.getPasswordStrength();
    const texts: Record<string, string> = {
      'weak': 'Faible',
      'medium': 'Moyen',
      'strong': 'Fort'
    };
    return texts[strength];
  }

  formatDate(date: Date): string {
    return new Date(date).toLocaleDateString('fr-FR', { year: 'numeric', month: 'short', day: 'numeric' });
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
