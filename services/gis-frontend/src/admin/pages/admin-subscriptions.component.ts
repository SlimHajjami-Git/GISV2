import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService, SubscriptionType } from '../services/admin.service';
import { SubscriptionFeaturesEditorComponent, SubscriptionFeatures } from '../components/subscription-features-editor.component';

@Component({
  selector: 'admin-subscriptions',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent, SubscriptionFeaturesEditorComponent],
  template: `
    <admin-layout pageTitle="Gestion des Abonnements">
      <div class="subscriptions-page">
        <!-- Header -->
        <div class="page-header">
          <div class="header-left">
            <h2>Types d'Abonnements</h2>
            <p class="subtitle">Gérez les formules d'abonnement et leurs tarifs</p>
          </div>
          <button class="btn-primary" (click)="openAddModal()">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
            </svg>
            Nouveau Type
          </button>
        </div>

        <!-- Filters -->
        <div class="filters-bar">
          <div class="search-box">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
            </svg>
            <input type="text" placeholder="Rechercher..." [(ngModel)]="searchQuery" (input)="filterTypes()" />
          </div>
          <select [(ngModel)]="companyTypeFilter" (change)="filterTypes()">
            <option value="all">Tous les types</option>
            <option value="transport">Transport</option>
            <option value="location">Location</option>
            <option value="autre">Autre</option>
          </select>
          <select [(ngModel)]="statusFilter" (change)="filterTypes()">
            <option value="all">Tous les statuts</option>
            <option value="active">Actif</option>
            <option value="inactive">Inactif</option>
          </select>
        </div>

        <!-- Subscription Types Grid -->
        <div class="types-grid">
          <div class="type-card" *ngFor="let type of filteredTypes" [class.inactive]="!type.isActive">
            <div class="card-header">
              <div class="type-info">
                <h3>{{ type.name }}</h3>
                              </div>
              <div class="type-badges">
                <span class="badge" [class]="'badge-' + type.targetCompanyType">
                  {{ getCompanyTypeLabel(type.targetCompanyType) }}
                </span>
                <span class="badge" [class.badge-active]="type.isActive" [class.badge-inactive]="!type.isActive">
                  {{ type.isActive ? 'Actif' : 'Inactif' }}
                </span>
              </div>
            </div>

            <p class="type-description" *ngIf="type.description">{{ type.description }}</p>

            <div class="pricing-section">
              <h4>Tarification</h4>
              <div class="price-single">
                <span class="price-value-large">{{ type.yearlyPrice | number:'1.2-2' }} DT</span>
                <span class="price-period">/an</span>
              </div>
            </div>

            <div class="limits-section">
              <h4>Limites</h4>
              <div class="limits-grid">
                <div class="limit-item">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <rect x="1" y="3" width="15" height="13" rx="2"/><path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-5"/>
                    <circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/>
                  </svg>
                  <span>{{ type.maxVehicles }} véhicules</span>
                </div>
                <div class="limit-item">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/>
                  </svg>
                  <span>{{ type.maxUsers }} utilisateurs</span>
                </div>
                <div class="limit-item">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <circle cx="12" cy="10" r="3"/><path d="M12 2a8 8 0 0 0-8 8c0 5.33 8 12 8 12s8-6.67 8-12a8 8 0 0 0-8-8z"/>
                  </svg>
                  <span>{{ type.maxGpsDevices }} GPS</span>
                </div>
                <div class="limit-item">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polygon points="1 6 1 22 8 18 16 22 23 18 23 2 16 6 8 2 1 6"/>
                  </svg>
                  <span>{{ type.maxGeofences }} zones</span>
                </div>
              </div>
            </div>

            <div class="features-section">
              <h4>Fonctionnalités</h4>
              <div class="features-grid">
                <span class="feature-tag" [class.enabled]="type.gpsTracking">
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polyline *ngIf="type.gpsTracking" points="20 6 9 17 4 12"/>
                    <line *ngIf="!type.gpsTracking" x1="18" y1="6" x2="6" y2="18"/>
                  </svg>
                  Suivi GPS
                </span>
                <span class="feature-tag" [class.enabled]="type.gpsInstallation">
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polyline *ngIf="type.gpsInstallation" points="20 6 9 17 4 12"/>
                    <line *ngIf="!type.gpsInstallation" x1="18" y1="6" x2="6" y2="18"/>
                  </svg>
                  Installation GPS
                </span>
                <span class="feature-tag" [class.enabled]="type.apiAccess">
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polyline *ngIf="type.apiAccess" points="20 6 9 17 4 12"/>
                    <line *ngIf="!type.apiAccess" x1="18" y1="6" x2="6" y2="18"/>
                  </svg>
                  Accès API
                </span>
                <span class="feature-tag" [class.enabled]="type.advancedReports">
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polyline *ngIf="type.advancedReports" points="20 6 9 17 4 12"/>
                    <line *ngIf="!type.advancedReports" x1="18" y1="6" x2="6" y2="18"/>
                  </svg>
                  Rapports avancés
                </span>
                <span class="feature-tag" [class.enabled]="type.fuelAnalysis">
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polyline *ngIf="type.fuelAnalysis" points="20 6 9 17 4 12"/>
                    <line *ngIf="!type.fuelAnalysis" x1="18" y1="6" x2="6" y2="18"/>
                  </svg>
                  Analyse carburant
                </span>
                <span class="feature-tag" [class.enabled]="type.drivingBehavior">
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polyline *ngIf="type.drivingBehavior" points="20 6 9 17 4 12"/>
                    <line *ngIf="!type.drivingBehavior" x1="18" y1="6" x2="6" y2="18"/>
                  </svg>
                  Comportement conduite
                </span>
              </div>
            </div>

            <div class="card-actions">
              <button class="btn-icon" (click)="editType(type)" title="Modifier">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                  <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                </svg>
              </button>
              <button class="btn-icon" [class.active]="type.isActive" (click)="toggleStatus(type)" [title]="type.isActive ? 'Désactiver' : 'Activer'">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path *ngIf="type.isActive" d="M18.36 6.64a9 9 0 1 1-12.73 0"/>
                  <line *ngIf="type.isActive" x1="12" y1="2" x2="12" y2="12"/>
                  <path *ngIf="!type.isActive" d="M5 12.55a11 11 0 0 1 14.08 0"/>
                  <circle *ngIf="!type.isActive" cx="12" cy="12" r="3"/>
                </svg>
              </button>
              <button class="btn-icon btn-danger" (click)="confirmDelete(type)" title="Supprimer">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                </svg>
              </button>
            </div>
          </div>

          <div class="empty-state" *ngIf="filteredTypes.length === 0">
            <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
              <path d="M12 2L2 7l10 5 10-5-10-5z"/><path d="M2 17l10 5 10-5"/><path d="M2 12l10 5 10-5"/>
            </svg>
            <h3>Aucun type d'abonnement</h3>
            <p>Créez votre premier type d'abonnement pour commencer.</p>
            <button class="btn-primary" (click)="openAddModal()">Créer un type</button>
          </div>
        </div>
      </div>

      <!-- Add/Edit Modal -->
      <div class="popup-overlay" *ngIf="showAddModal || showEditModal" (click)="closeModals()">
        <div class="popup-container subscription-popup" (click)="$event.stopPropagation()">
          <div class="popup-header">
            <div class="header-title">
              <div class="header-icon">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <rect x="1" y="4" width="22" height="16" rx="2" ry="2"/><line x1="1" y1="10" x2="23" y2="10"/>
                </svg>
              </div>
              <h2>{{ showEditModal ? 'Modifier l\'abonnement' : 'Nouvel abonnement' }}</h2>
            </div>
            <button class="close-btn" (click)="closeModals()" title="Fermer">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
              </svg>
            </button>
          </div>

          <div class="popup-body">
            <div class="form-tabs">
              <button [class.active]="activeTab === 'general'" (click)="activeTab = 'general'">Général</button>
              <button [class.active]="activeTab === 'pricing'" (click)="activeTab = 'pricing'">Tarification</button>
              <button [class.active]="activeTab === 'limits'" (click)="activeTab = 'limits'">Limites</button>
              <button [class.active]="activeTab === 'features'" (click)="activeTab = 'features'">Fonctionnalités</button>
            </div>

            <!-- General Tab -->
            <div class="tab-content" *ngIf="activeTab === 'general'">
              <div class="form-section">
                <div class="section-title">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                  </svg>
                  <span>Informations générales</span>
                </div>
                <div class="form-grid">
                  <div class="form-group full-width">
                    <label>Nom <span class="required">*</span></label>
                    <input type="text" [(ngModel)]="form.name" placeholder="Ex: Standard, Premium, Enterprise..." />
                  </div>
                  <div class="form-group full-width">
                    <label>Description</label>
                    <textarea [(ngModel)]="form.description" rows="3" placeholder="Description du type d'abonnement..."></textarea>
                  </div>
                  <div class="form-group full-width">
                    <label>Type de société cible</label>
                    <select [(ngModel)]="form.targetCompanyType">
                      <option value="all">Tous les types</option>
                      <option value="transport">Transport</option>
                      <option value="location">Location</option>
                      <option value="autre">Autre</option>
                    </select>
                  </div>
                </div>
              </div>
            </div>

            <!-- Pricing Tab -->
            <div class="tab-content" *ngIf="activeTab === 'pricing'">
              <div class="form-section">
                <div class="section-title">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <line x1="12" y1="1" x2="12" y2="23"/><path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>
                  </svg>
                  <span>Tarif Annuel</span>
                </div>
                <div class="form-grid">
                  <div class="form-group">
                    <label>Prix Annuel (DT) <span class="required">*</span></label>
                    <input type="number" [(ngModel)]="form.yearlyPrice" min="0" step="0.01" />
                  </div>
                  <div class="form-group">
                    <label>Durée (jours)</label>
                    <input type="number" [(ngModel)]="form.yearlyDurationDays" min="1" />
                    <small class="hint">Par défaut: 365 jours</small>
                  </div>
                </div>
              </div>
            </div>

            <!-- Limits Tab -->
            <div class="tab-content" *ngIf="activeTab === 'limits'">
              <div class="form-section">
                <div class="section-title">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <rect x="3" y="3" width="18" height="18" rx="2"/><path d="M9 9h6v6H9z"/>
                  </svg>
                  <span>Limites de ressources</span>
                </div>
                <div class="form-grid">
                  <div class="form-group">
                    <label>Véhicules maximum</label>
                    <input type="number" [(ngModel)]="form.maxVehicles" min="1" />
                  </div>
                  <div class="form-group">
                    <label>Utilisateurs maximum</label>
                    <input type="number" [(ngModel)]="form.maxUsers" min="1" />
                  </div>
                  <div class="form-group">
                    <label>Appareils GPS maximum</label>
                    <input type="number" [(ngModel)]="form.maxGpsDevices" min="0" />
                  </div>
                  <div class="form-group">
                    <label>Géofences maximum</label>
                    <input type="number" [(ngModel)]="form.maxGeofences" min="0" />
                  </div>
                  <div class="form-group full-width">
                    <label>Rétention historique (jours)</label>
                    <input type="number" [(ngModel)]="form.historyRetentionDays" min="1" />
                    <small class="hint">Durée de conservation des données de position GPS</small>
                  </div>
                </div>
              </div>
            </div>

            <!-- Features Tab -->
            <div class="tab-content features-tab" *ngIf="activeTab === 'features'">
              <div class="form-section">
                <subscription-features-editor
                  [initialFeatures]="getFormFeatures()"
                  (featuresChange)="onFeaturesChange($event)">
                </subscription-features-editor>
              </div>
            </div>
          </div>

          <div class="popup-footer">
            <button class="btn-secondary" (click)="closeModals()">Annuler</button>
            <button class="btn-primary" (click)="saveType()" [disabled]="!form.name || !form.yearlyPrice">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/>
              </svg>
              {{ showEditModal ? 'Mettre à jour' : 'Créer' }}
            </button>
          </div>
        </div>
      </div>

      <!-- Delete Confirmation Modal -->
      <div class="modal-overlay" *ngIf="showDeleteModal" (click)="closeDeleteModal()">
        <div class="modal modal-small" (click)="$event.stopPropagation()">
          <div class="modal-header">
            <h3>Confirmer la suppression</h3>
            <button class="btn-close" (click)="closeDeleteModal()">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
              </svg>
            </button>
          </div>
          <div class="modal-body">
            <p>Êtes-vous sûr de vouloir supprimer le type d'abonnement <strong>{{ typeToDelete?.name }}</strong> ?</p>
            <p class="warning">Cette action est irréversible.</p>
          </div>
          <div class="modal-footer">
            <button class="btn-secondary" (click)="closeDeleteModal()">Annuler</button>
            <button class="btn-danger" (click)="deleteType()">Supprimer</button>
          </div>
        </div>
      </div>
    </admin-layout>
  `,
  styles: [`
    .subscriptions-page { padding: 0; }

    .page-header {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      margin-bottom: 24px;
    }

    .header-left h2 { margin: 0 0 4px 0; font-size: 24px; color: #1f2937; }
    .subtitle { margin: 0; color: #64748b; font-size: 14px; }

    .btn-primary {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 20px;
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
      color: #fff;
      border: none;
      border-radius: 8px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.2s;
    }
    .btn-primary:hover { transform: translateY(-1px); box-shadow: 0 4px 12px rgba(0, 212, 170, 0.3); }
    .btn-primary:disabled { opacity: 0.5; cursor: not-allowed; transform: none; }

    .filters-bar {
      display: flex;
      gap: 12px;
      margin-bottom: 24px;
      flex-wrap: wrap;
    }

    .search-box {
      display: flex;
      align-items: center;
      gap: 10px;
      background: #fff;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      padding: 8px 14px;
      flex: 1;
      min-width: 200px;
    }
    .search-box svg { color: #64748b; }
    .search-box input { flex: 1; border: none; background: transparent; font-size: 14px; outline: none; }

    .filters-bar select {
      padding: 10px 14px;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      background: #fff;
      font-size: 14px;
      color: #1f2937;
      cursor: pointer;
    }

    .types-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(380px, 1fr));
      gap: 20px;
    }

    .type-card {
      background: #fff;
      border-radius: 12px;
      border: 1px solid #e2e8f0;
      padding: 20px;
      transition: all 0.2s;
    }
    .type-card:hover { box-shadow: 0 4px 12px rgba(0, 0, 0, 0.08); }
    .type-card.inactive { opacity: 0.7; background: #f8fafc; }

    .card-header {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      margin-bottom: 12px;
    }

    .type-info h3 { margin: 0 0 4px 0; font-size: 18px; color: #1f2937; }
    .type-code { font-size: 12px; color: #64748b; font-family: monospace; background: #f1f5f9; padding: 2px 6px; border-radius: 4px; }

    .type-badges { display: flex; gap: 6px; flex-wrap: wrap; }
    .badge {
      padding: 4px 10px;
      border-radius: 20px;
      font-size: 11px;
      font-weight: 500;
      text-transform: uppercase;
    }
    .badge-transport { background: #dbeafe; color: #1d4ed8; }
    .badge-location { background: #fef3c7; color: #b45309; }
    .badge-autre { background: #e0e7ff; color: #4338ca; }
    .badge-all { background: #f1f5f9; color: #64748b; }
    .badge-active { background: #dcfce7; color: #16a34a; }
    .badge-inactive { background: #fee2e2; color: #dc2626; }

    .type-description { color: #64748b; font-size: 13px; margin: 0 0 16px 0; line-height: 1.5; }

    .pricing-section, .limits-section, .features-section {
      margin-bottom: 16px;
      padding-top: 16px;
      border-top: 1px solid #f1f5f9;
    }

    .pricing-section h4, .limits-section h4, .features-section h4 {
      margin: 0 0 12px 0;
      font-size: 13px;
      color: #64748b;
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }

    .price-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 12px;
    }

    .price-item {
      text-align: center;
      padding: 10px;
      background: #f8fafc;
      border-radius: 8px;
    }
    .price-label { display: block; font-size: 11px; color: #64748b; margin-bottom: 4px; }
    .price-value { display: block; font-size: 14px; font-weight: 600; color: #1f2937; }

    .price-single {
      display: flex;
      align-items: baseline;
      justify-content: center;
      gap: 4px;
      padding: 16px;
      background: linear-gradient(135deg, #f0fdf4 0%, #dcfce7 100%);
      border-radius: 8px;
    }
    .price-value-large { font-size: 28px; font-weight: 700; color: #16a34a; }
    .price-period { font-size: 14px; color: #64748b; }

    .limits-grid {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 8px;
    }

    .limit-item {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 13px;
      color: #475569;
    }
    .limit-item svg { color: #00d4aa; }

    .features-grid {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
    }

    .feature-tag {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 4px 10px;
      border-radius: 20px;
      font-size: 11px;
      background: #fee2e2;
      color: #dc2626;
    }
    .feature-tag.enabled { background: #dcfce7; color: #16a34a; }

    .card-actions {
      display: flex;
      justify-content: flex-end;
      gap: 8px;
      margin-top: 16px;
      padding-top: 16px;
      border-top: 1px solid #f1f5f9;
    }

    .btn-icon {
      width: 32px;
      height: 32px;
      border: 1px solid #e2e8f0;
      background: #fff;
      border-radius: 6px;
      color: #64748b;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }
    .btn-icon:hover { border-color: #00d4aa; color: #00d4aa; }
    .btn-icon.active { color: #16a34a; border-color: #16a34a; }
    .btn-icon.btn-danger:hover { border-color: #dc2626; color: #dc2626; }

    .empty-state {
      grid-column: 1 / -1;
      text-align: center;
      padding: 60px 20px;
      background: #fff;
      border-radius: 12px;
      border: 1px dashed #e2e8f0;
    }
    .empty-state svg { color: #cbd5e1; margin-bottom: 16px; }
    .empty-state h3 { margin: 0 0 8px 0; color: #64748b; }
    .empty-state p { margin: 0 0 20px 0; color: #94a3b8; }

    /* Enhanced Popup Styles */
    .popup-overlay {
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(15, 23, 42, 0.6);
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
      background: #ffffff;
      border-radius: 12px;
      box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.25);
      max-width: 700px;
      width: 100%;
      max-height: 85vh;
      overflow: hidden;
      display: flex;
      flex-direction: column;
      animation: slideUp 0.3s cubic-bezier(0.16, 1, 0.3, 1);
    }

    .subscription-popup {
      max-width: 700px;
    }

    @keyframes slideUp {
      from { transform: translateY(30px) scale(0.97); opacity: 0; }
      to { transform: translateY(0) scale(1); opacity: 1; }
    }

    .popup-header {
      padding: 16px 24px;
      border-bottom: 1px solid #e2e8f0;
      display: flex;
      align-items: center;
      justify-content: space-between;
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
    }

    .header-title {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .header-icon {
      width: 36px;
      height: 36px;
      background: rgba(255, 255, 255, 0.2);
      border-radius: 10px;
      display: flex;
      align-items: center;
      justify-content: center;
      color: white;
    }

    .popup-header h2 {
      margin: 0;
      font-size: 16px;
      font-weight: 600;
      color: white;
    }

    .popup-header .close-btn {
      background: rgba(255, 255, 255, 0.15);
      border: none;
      color: white;
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
      background: rgba(255, 255, 255, 0.25);
    }

    .popup-body {
      padding: 0;
      overflow-y: auto;
      flex: 1;
      background: #f8fafc;
    }

    .form-section {
      padding: 20px 24px;
      background: white;
      border-bottom: 1px solid #e2e8f0;
    }

    .form-section:last-child {
      border-bottom: none;
    }

    .section-title {
      display: flex;
      align-items: center;
      gap: 10px;
      margin-bottom: 16px;
      font-size: 13px;
      font-weight: 600;
      color: #475569;
      text-transform: uppercase;
      letter-spacing: 0.03em;
    }

    .section-title svg {
      color: #6366f1;
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
      margin-bottom: 0;
    }

    .form-grid .form-group.full-width {
      grid-column: 1 / -1;
    }

    .form-grid .form-group label {
      font-size: 12px;
      font-weight: 500;
      color: #475569;
      margin-bottom: 0;
    }

    .form-grid .form-group input,
    .form-grid .form-group select,
    .form-grid .form-group textarea {
      padding: 10px 14px;
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      color: #1e293b;
      font-size: 13px;
      transition: all 0.2s;
    }

    .form-grid .form-group input:focus,
    .form-grid .form-group select:focus,
    .form-grid .form-group textarea:focus {
      outline: none;
      border-color: #6366f1;
      background: white;
      box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.1);
    }

    .required { color: #ef4444; }
    .hint { font-size: 11px; color: #94a3b8; margin-top: 4px; }

    .popup-footer {
      padding: 16px 24px;
      border-top: 1px solid #e2e8f0;
      display: flex;
      gap: 12px;
      justify-content: flex-end;
      background: white;
    }

    .popup-footer .btn-secondary,
    .btn-secondary {
      padding: 10px 20px;
      background: white;
      color: #64748b;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      font-weight: 500;
      font-size: 13px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .popup-footer .btn-secondary:hover,
    .btn-secondary:hover {
      background: #f8fafc;
      border-color: #cbd5e1;
    }

    .popup-footer .btn-primary {
      padding: 10px 20px;
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
      color: white;
      border: none;
      border-radius: 8px;
      font-weight: 500;
      font-size: 13px;
      cursor: pointer;
      display: flex;
      align-items: center;
      gap: 8px;
      transition: all 0.2s;
      box-shadow: 0 2px 4px rgba(102, 126, 234, 0.25);
    }

    .popup-footer .btn-primary:hover {
      transform: translateY(-1px);
      box-shadow: 0 4px 12px rgba(102, 126, 234, 0.35);
    }

    .popup-footer .btn-primary:disabled {
      opacity: 0.5;
      cursor: not-allowed;
      transform: none;
    }

    .form-tabs {
      display: flex;
      gap: 8px;
      padding: 16px 24px;
      background: white;
      border-bottom: 1px solid #e2e8f0;
    }
    .form-tabs button {
      padding: 8px 16px;
      border: none;
      background: #f1f5f9;
      color: #64748b;
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
      border-radius: 6px;
      transition: all 0.2s;
    }
    .form-tabs button:hover { background: #e2e8f0; }
    .form-tabs button.active { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: #fff; }

    .tab-content { padding: 0; }

    .features-tab {
      padding: 0 !important;
    }

    .features-tab subscription-features-editor {
      display: block;
    }

    .features-tab .form-section {
      padding: 0;
    }

    /* Legacy Modal Styles for Delete Confirmation */
    .modal-overlay {
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(15, 23, 42, 0.6);
      backdrop-filter: blur(4px);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
      padding: 20px;
      animation: fadeIn 0.2s ease-out;
    }

    .modal {
      background: #fff;
      border-radius: 12px;
      width: 100%;
      max-width: 700px;
      max-height: 90vh;
      overflow: hidden;
      display: flex;
      flex-direction: column;
      animation: slideUp 0.3s cubic-bezier(0.16, 1, 0.3, 1);
    }
    .modal.modal-small { max-width: 450px; }

    .modal-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 20px 24px;
      border-bottom: 1px solid #e2e8f0;
    }
    .modal-header h3 { margin: 0; font-size: 18px; color: #1f2937; }

    .btn-close {
      width: 32px;
      height: 32px;
      border: none;
      background: #f1f5f9;
      border-radius: 8px;
      color: #64748b;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
    }
    .btn-close:hover { background: #e2e8f0; color: #1f2937; }

    .modal-body {
      flex: 1;
      overflow-y: auto;
      padding: 24px;
    }

    .form-group {
      margin-bottom: 16px;
    }
    .form-group label {
      display: block;
      margin-bottom: 6px;
      font-size: 13px;
      font-weight: 500;
      color: #374151;
    }
    .form-group input, .form-group select, .form-group textarea {
      width: 100%;
      padding: 10px 12px;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      font-size: 14px;
      transition: border-color 0.2s;
    }
    .form-group input:focus, .form-group select:focus, .form-group textarea:focus {
      outline: none;
      border-color: #00d4aa;
    }
    .form-group input:disabled { background: #f8fafc; color: #94a3b8; }
    .form-group small { display: block; margin-top: 4px; font-size: 11px; color: #94a3b8; }

    .form-row { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }

    .price-preview {
      margin-top: 24px;
      padding: 16px;
      background: #f8fafc;
      border-radius: 8px;
    }
    .price-preview h4 { margin: 0 0 12px 0; font-size: 13px; color: #64748b; }

    .preview-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 12px; }

    .preview-item {
      text-align: center;
      padding: 12px;
      background: #fff;
      border-radius: 8px;
      border: 1px solid #e2e8f0;
    }
    .preview-item .cycle { display: block; font-size: 11px; color: #64748b; margin-bottom: 4px; }
    .preview-item .price { display: block; font-size: 16px; font-weight: 600; color: #1f2937; }
    .preview-item .savings { display: block; font-size: 11px; color: #16a34a; margin-top: 4px; }

    .features-form {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 12px;
    }

    .toggle-item {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 12px;
      background: #f8fafc;
      border-radius: 8px;
      cursor: pointer;
      transition: all 0.2s;
    }
    .toggle-item:hover { background: #f1f5f9; }
    .toggle-item input[type="checkbox"] {
      width: 18px;
      height: 18px;
      accent-color: #00d4aa;
    }
    .toggle-label { font-size: 13px; color: #1f2937; }

    .modal-footer {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 16px 24px;
      border-top: 1px solid #e2e8f0;
    }

    .btn-secondary {
      padding: 10px 20px;
      border: 1px solid #e2e8f0;
      background: #fff;
      color: #64748b;
      border-radius: 8px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.2s;
    }
    .btn-secondary:hover { border-color: #cbd5e1; color: #1f2937; }

    .btn-danger {
      padding: 10px 20px;
      background: #dc2626;
      color: #fff;
      border: none;
      border-radius: 8px;
      font-weight: 500;
      cursor: pointer;
    }
    .btn-danger:hover { background: #b91c1c; }

    .warning { color: #dc2626; font-size: 13px; margin-top: 12px; }

    @media (max-width: 768px) {
      .types-grid { grid-template-columns: 1fr; }
      .form-row { grid-template-columns: 1fr; }
      .preview-grid { grid-template-columns: 1fr; }
      .features-form { grid-template-columns: 1fr; }
    }
  `]
})
export class AdminSubscriptionsComponent implements OnInit {
  subscriptionTypes: SubscriptionType[] = [];
  filteredTypes: SubscriptionType[] = [];

  searchQuery = '';
  companyTypeFilter = 'all';
  statusFilter = 'all';

  showAddModal = false;
  showEditModal = false;
  showDeleteModal = false;
  selectedType: SubscriptionType | null = null;
  typeToDelete: SubscriptionType | null = null;

  activeTab: 'general' | 'pricing' | 'limits' | 'features' = 'general';

  form = this.getEmptyForm();

  constructor(
    private router: Router,
    private adminService: AdminService
  ) {}

  ngOnInit() {
    if (!this.adminService.isAuthenticated()) {
      this.router.navigate(['/admin/login']);
      return;
    }
    this.loadData();
  }

  loadData() {
    this.adminService.getSubscriptionTypes().subscribe({
      next: (types) => {
        this.subscriptionTypes = types;
        this.filterTypes();
      },
      error: (err) => console.error('Error loading subscription types:', err)
    });
  }

  filterTypes() {
    this.filteredTypes = this.subscriptionTypes.filter(type => {
      const matchesSearch = !this.searchQuery ||
        type.name.toLowerCase().includes(this.searchQuery.toLowerCase());
      const matchesCompanyType = this.companyTypeFilter === 'all' || 
        type.targetCompanyType === 'all' || 
        type.targetCompanyType === this.companyTypeFilter;
      const matchesStatus = this.statusFilter === 'all' ||
        (this.statusFilter === 'active' && type.isActive) ||
        (this.statusFilter === 'inactive' && !type.isActive);
      return matchesSearch && matchesCompanyType && matchesStatus;
    });
  }

  getCompanyTypeLabel(type: string): string {
    const labels: Record<string, string> = {
      'transport': 'Transport',
      'location': 'Location',
      'autre': 'Autre',
      'all': 'Tous'
    };
    return labels[type] || type;
  }

  openAddModal() {
    this.form = this.getEmptyForm();
    this.activeTab = 'general';
    this.showAddModal = true;
  }

  editType(type: SubscriptionType) {
    this.selectedType = type;
    this.form = { 
      ...type,
      description: type.description || ''
    };
    this.activeTab = 'general';
    this.showEditModal = true;
  }

  toggleStatus(type: SubscriptionType) {
    this.adminService.updateSubscriptionType(type.id, { isActive: !type.isActive }).subscribe({
      next: () => {
        type.isActive = !type.isActive;
      },
      error: (err) => {
        console.error('Error toggling status:', err);
        alert('Erreur lors de la mise à jour du statut');
      }
    });
  }

  confirmDelete(type: SubscriptionType) {
    this.typeToDelete = type;
    this.showDeleteModal = true;
  }

  deleteType() {
    if (this.typeToDelete) {
      this.adminService.deleteSubscriptionType(this.typeToDelete.id).subscribe({
        next: () => {
          this.loadData();
          this.closeDeleteModal();
        },
        error: (err) => {
          console.error('Error deleting type:', err);
          alert(err.error?.message || 'Erreur lors de la suppression');
        }
      });
    }
  }

  saveType() {
    const data: any = {
      name: this.form.name,
      code: this.form.name.toLowerCase().replace(/\s+/g, '-'),
      description: this.form.description || undefined,
      targetCompanyType: this.form.targetCompanyType,
      yearlyPrice: this.form.yearlyPrice,
      yearlyDurationDays: this.form.yearlyDurationDays,
      maxVehicles: this.form.maxVehicles,
      maxUsers: this.form.maxUsers,
      maxGpsDevices: this.form.maxGpsDevices,
      maxGeofences: this.form.maxGeofences,
      gpsTracking: this.form.gpsTracking,
      gpsInstallation: this.form.gpsInstallation,
      apiAccess: this.form.apiAccess,
      advancedReports: this.form.advancedReports,
      realTimeAlerts: this.form.realTimeAlerts,
      historyPlayback: this.form.historyPlayback,
      fuelAnalysis: this.form.fuelAnalysis,
      drivingBehavior: this.form.drivingBehavior,
      historyRetentionDays: this.form.historyRetentionDays
    };

    if (this.showEditModal && this.selectedType) {
      this.adminService.updateSubscriptionType(this.selectedType.id, data).subscribe({
        next: () => {
          this.loadData();
          this.closeModals();
        },
        error: (err) => {
          console.error('Error updating type:', err);
          alert(err.error?.message || 'Erreur lors de la mise à jour');
        }
      });
    } else {
      this.adminService.createSubscriptionType(data).subscribe({
        next: () => {
          this.loadData();
          this.closeModals();
        },
        error: (err) => {
          console.error('Error creating type:', err);
          alert(err.error?.message || 'Erreur lors de la création');
        }
      });
    }
  }

  closeModals() {
    this.showAddModal = false;
    this.showEditModal = false;
    this.selectedType = null;
    this.form = this.getEmptyForm();
  }

  closeDeleteModal() {
    this.showDeleteModal = false;
    this.typeToDelete = null;
  }

  getFormFeatures(): SubscriptionFeatures {
    return {
      gpsTracking: this.form.gpsTracking,
      gpsInstallation: this.form.gpsInstallation,
      realTimeAlerts: this.form.realTimeAlerts,
      historyPlayback: this.form.historyPlayback,
      advancedReports: this.form.advancedReports,
      fuelAnalysis: this.form.fuelAnalysis,
      drivingBehavior: this.form.drivingBehavior,
      apiAccess: this.form.apiAccess
    };
  }

  onFeaturesChange(features: SubscriptionFeatures) {
    this.form.gpsTracking = features.gpsTracking;
    this.form.gpsInstallation = features.gpsInstallation;
    this.form.realTimeAlerts = features.realTimeAlerts;
    this.form.historyPlayback = features.historyPlayback;
    this.form.advancedReports = features.advancedReports;
    this.form.fuelAnalysis = features.fuelAnalysis;
    this.form.drivingBehavior = features.drivingBehavior;
    this.form.apiAccess = features.apiAccess;
  }

  private getEmptyForm() {
    return {
      id: 0,
      name: '',
      description: '',
      targetCompanyType: 'all',
      yearlyPrice: 0,
      yearlyDurationDays: 365,
      maxVehicles: 10,
      maxUsers: 5,
      maxGpsDevices: 10,
      maxGeofences: 20,
      gpsTracking: true,
      gpsInstallation: false,
      apiAccess: false,
      advancedReports: false,
      realTimeAlerts: true,
      historyPlayback: true,
      fuelAnalysis: false,
      drivingBehavior: false,
      historyRetentionDays: 30,
      isActive: true,
      createdAt: new Date(),
      updatedAt: new Date()
    };
  }
}
