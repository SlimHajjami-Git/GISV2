import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService, SubscriptionType } from '../services/admin.service';

@Component({
  selector: 'admin-subscriptions',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent],
  template: `
    <admin-layout pageTitle="Gestion des Abonnements">
      <div class="subscriptions-container">
        <!-- Header -->
        <div class="page-header">
          <div class="header-left">
            <h2>Types d'Abonnements</h2>
            <span class="count">{{ subscriptionTypes.length }} type(s)</span>
          </div>
          <button class="btn-primary" (click)="openCreateModal()">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
            </svg>
            Nouveau Type
          </button>
        </div>

        <!-- Subscription Types Grid -->
        <div class="types-grid">
          <div *ngFor="let type of subscriptionTypes" class="type-card" [class.inactive]="!type.isActive">
            <div class="type-header">
              <div class="type-icon" [class.premium]="type.advancedReports">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <rect x="1" y="4" width="22" height="16" rx="2" ry="2"/><line x1="1" y1="10" x2="23" y2="10"/>
                </svg>
              </div>
              <div class="type-info">
                <h3>{{ type.name }}</h3>
                <span class="type-price">{{ type.yearlyPrice | number:'1.0-0' }} DT/an</span>
              </div>
              <div class="type-badges">
                <span *ngIf="!type.isActive" class="badge inactive">Inactif</span>
                <span *ngIf="type.gpsTracking" class="badge gps">GPS</span>
              </div>
            </div>
            
            <p class="type-description">{{ type.description || 'Aucune description' }}</p>
            
            <div class="type-stats">
              <div class="stat">
                <span class="stat-value">{{ type.maxVehicles }}</span>
                <span class="stat-label">Véhicules</span>
              </div>
              <div class="stat">
                <span class="stat-value">{{ type.maxUsers }}</span>
                <span class="stat-label">Utilisateurs</span>
              </div>
              <div class="stat">
                <span class="stat-value">{{ getModulesCount(type) }}</span>
                <span class="stat-label">Modules</span>
              </div>
              <div class="stat">
                <span class="stat-value">{{ getReportsCount(type) }}</span>
                <span class="stat-label">Rapports</span>
              </div>
            </div>
            
            <div class="type-actions">
              <button class="btn-icon" (click)="editType(type)" title="Modifier">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                  <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                </svg>
              </button>
              <button class="btn-icon" [class.active]="type.isActive" (click)="toggleStatus(type)" [title]="type.isActive ? 'Désactiver' : 'Activer'">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="12" cy="12" r="10"/><path d="M12 6v6l4 2"/>
                </svg>
              </button>
              <button class="btn-icon danger" (click)="confirmDelete(type)" title="Supprimer">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <polyline points="3,6 5,6 21,6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                </svg>
              </button>
            </div>
          </div>
        </div>

        <!-- Create/Edit Modal -->
        <div class="modal-overlay" *ngIf="showModal" (click)="closeModal()">
          <div class="modal modal-large" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h3>{{ editingType ? 'Modifier l\\'abonnement' : 'Nouvel abonnement' }}</h3>
              <button class="btn-close" (click)="closeModal()">×</button>
            </div>
            <div class="modal-body">
              <!-- Tabs -->
              <div class="form-tabs">
                <button [class.active]="activeTab === 'general'" (click)="activeTab = 'general'">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                  </svg>
                  Général
                </button>
                <button [class.active]="activeTab === 'limits'" (click)="activeTab = 'limits'">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <rect x="3" y="3" width="18" height="18" rx="2"/><path d="M9 9h6v6H9z"/>
                  </svg>
                  Limites
                </button>
                <button [class.active]="activeTab === 'modules'" (click)="activeTab = 'modules'">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/>
                    <rect x="14" y="14" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/>
                  </svg>
                  Modules
                </button>
                <button [class.active]="activeTab === 'reports'" (click)="activeTab = 'reports'">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                    <polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/>
                  </svg>
                  Rapports
                </button>
                <button [class.active]="activeTab === 'features'" (click)="activeTab = 'features'">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/>
                  </svg>
                  Options
                </button>
              </div>

              <!-- General Tab -->
              <div class="tab-content" *ngIf="activeTab === 'general'">
                <div class="form-group">
                  <label>Nom de l'abonnement *</label>
                  <input type="text" [(ngModel)]="formData.name" placeholder="Ex: Standard, Premium, Enterprise...">
                </div>
                <div class="form-group">
                  <label>Description</label>
                  <textarea [(ngModel)]="formData.description" placeholder="Description du type d'abonnement..." rows="3"></textarea>
                </div>
                <div class="form-row">
                  <div class="form-group">
                    <label>Prix annuel (DT) *</label>
                    <input type="number" [(ngModel)]="formData.yearlyPrice" min="0" step="0.01">
                  </div>
                  <div class="form-group">
                    <label>Type de société cible</label>
                    <select [(ngModel)]="formData.targetCompanyType">
                      <option value="all">Tous les types</option>
                      <option value="transport">Transport</option>
                      <option value="location">Location</option>
                      <option value="autre">Autre</option>
                    </select>
                  </div>
                </div>
              </div>

              <!-- Limits Tab -->
              <div class="tab-content" *ngIf="activeTab === 'limits'">
                <div class="form-row">
                  <div class="form-group">
                    <label>Véhicules maximum</label>
                    <input type="number" [(ngModel)]="formData.maxVehicles" min="1">
                  </div>
                  <div class="form-group">
                    <label>Utilisateurs maximum</label>
                    <input type="number" [(ngModel)]="formData.maxUsers" min="1">
                  </div>
                </div>
                <div class="form-row">
                  <div class="form-group">
                    <label>Appareils GPS maximum</label>
                    <input type="number" [(ngModel)]="formData.maxGpsDevices" min="0">
                  </div>
                  <div class="form-group">
                    <label>Géofences maximum</label>
                    <input type="number" [(ngModel)]="formData.maxGeofences" min="0">
                  </div>
                </div>
                <div class="form-group">
                  <label>Rétention historique (jours)</label>
                  <input type="number" [(ngModel)]="formData.historyRetentionDays" min="1">
                  <small>Durée de conservation des données GPS</small>
                </div>
              </div>

              <!-- Modules Tab -->
              <div class="tab-content" *ngIf="activeTab === 'modules'">
                <div class="permissions-header">
                  <h4>Modules disponibles</h4>
                  <button class="btn-select-all" (click)="toggleAllModules()">
                    {{ areAllModulesSelected() ? 'Tout désélectionner' : 'Tout sélectionner' }}
                  </button>
                </div>
                <div class="permissions-grid">
                  <label *ngFor="let module of availableModules" class="permission-item" [class.enabled]="formData.modules[module.key]">
                    <input type="checkbox" [(ngModel)]="formData.modules[module.key]">
                    <div class="perm-icon">
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/>
                        <rect x="14" y="14" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/>
                      </svg>
                    </div>
                    <span>{{ module.label }}</span>
                  </label>
                </div>
              </div>

              <!-- Reports Tab -->
              <div class="tab-content" *ngIf="activeTab === 'reports'">
                <div class="permissions-header">
                  <h4>Rapports disponibles</h4>
                  <button class="btn-select-all" (click)="toggleAllReports()">
                    {{ areAllReportsSelected() ? 'Tout désélectionner' : 'Tout sélectionner' }}
                  </button>
                </div>
                <div class="permissions-grid">
                  <label *ngFor="let report of availableReports" class="permission-item" [class.enabled]="formData.reports[report.key]">
                    <input type="checkbox" [(ngModel)]="formData.reports[report.key]">
                    <div class="perm-icon">
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                        <polyline points="14 2 14 8 20 8"/>
                      </svg>
                    </div>
                    <span>{{ report.label }}</span>
                  </label>
                </div>
              </div>

              <!-- Features Tab -->
              <div class="tab-content" *ngIf="activeTab === 'features'">
                <div class="permissions-header">
                  <h4>Options avancées</h4>
                </div>
                <div class="permissions-grid">
                  <label *ngFor="let feature of availableFeatures" class="permission-item" [class.enabled]="formData.features[feature.key]">
                    <input type="checkbox" [(ngModel)]="formData.features[feature.key]">
                    <div class="perm-icon">
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/>
                      </svg>
                    </div>
                    <span>{{ feature.label }}</span>
                  </label>
                </div>
              </div>
            </div>
            <div class="modal-footer">
              <button class="btn-secondary" (click)="closeModal()">Annuler</button>
              <button class="btn-primary" (click)="saveType()" [disabled]="!formData.name || !formData.yearlyPrice">
                {{ editingType ? 'Enregistrer' : 'Créer' }}
              </button>
            </div>
          </div>
        </div>

        <!-- Delete Confirmation -->
        <div class="modal-overlay" *ngIf="showDeleteConfirm" (click)="showDeleteConfirm = false">
          <div class="modal modal-sm" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h3>Confirmer la suppression</h3>
            </div>
            <div class="modal-body">
              <p>Êtes-vous sûr de vouloir supprimer l'abonnement <strong>{{ typeToDelete?.name }}</strong> ?</p>
              <p class="warning">Cette action est irréversible.</p>
            </div>
            <div class="modal-footer">
              <button class="btn-secondary" (click)="showDeleteConfirm = false">Annuler</button>
              <button class="btn-danger" (click)="deleteType()">Supprimer</button>
            </div>
          </div>
        </div>
      </div>
    </admin-layout>
  `,
  styles: [`
    .subscriptions-container {
      padding: 24px;
    }
    .page-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 24px;
    }
    .header-left {
      display: flex;
      align-items: center;
      gap: 12px;
    }
    .header-left h2 {
      margin: 0;
      font-size: 24px;
      font-weight: 600;
    }
    .count {
      background: #e2e8f0;
      padding: 4px 12px;
      border-radius: 20px;
      font-size: 14px;
      color: #64748b;
    }
    .btn-primary {
      display: flex;
      align-items: center;
      gap: 8px;
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
      color: white;
      border: none;
      padding: 10px 20px;
      border-radius: 8px;
      cursor: pointer;
      font-weight: 500;
      transition: all 0.2s;
    }
    .btn-primary:hover { transform: translateY(-1px); box-shadow: 0 4px 12px rgba(102, 126, 234, 0.3); }
    .btn-primary:disabled { background: #94a3b8; cursor: not-allowed; transform: none; }
    
    .types-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(340px, 1fr));
      gap: 20px;
    }
    .type-card {
      background: white;
      border-radius: 12px;
      padding: 20px;
      box-shadow: 0 1px 3px rgba(0,0,0,0.1);
      transition: transform 0.2s, box-shadow 0.2s;
    }
    .type-card:hover {
      transform: translateY(-2px);
      box-shadow: 0 4px 12px rgba(0,0,0,0.15);
    }
    .type-card.inactive {
      opacity: 0.6;
      background: #f8fafc;
    }
    .type-header {
      display: flex;
      align-items: flex-start;
      gap: 12px;
      margin-bottom: 12px;
    }
    .type-icon {
      width: 48px;
      height: 48px;
      border-radius: 12px;
      background: #eff6ff;
      color: #3b82f6;
      display: flex;
      align-items: center;
      justify-content: center;
    }
    .type-icon.premium { background: #fef3c7; color: #f59e0b; }
    .type-info { flex: 1; }
    .type-info h3 {
      margin: 0 0 4px 0;
      font-size: 16px;
      font-weight: 600;
    }
    .type-price {
      font-size: 14px;
      color: #16a34a;
      font-weight: 600;
    }
    .type-badges {
      display: flex;
      gap: 6px;
    }
    .badge {
      padding: 4px 8px;
      border-radius: 4px;
      font-size: 11px;
      font-weight: 500;
    }
    .badge.inactive { background: #fee2e2; color: #dc2626; }
    .badge.gps { background: #dcfce7; color: #16a34a; }
    
    .type-description {
      font-size: 14px;
      color: #64748b;
      margin: 0 0 16px 0;
      line-height: 1.5;
    }
    .type-stats {
      display: flex;
      gap: 16px;
      padding: 12px 0;
      border-top: 1px solid #e2e8f0;
      margin-bottom: 12px;
    }
    .stat {
      display: flex;
      flex-direction: column;
      flex: 1;
      text-align: center;
    }
    .stat-value {
      font-size: 18px;
      font-weight: 600;
      color: #1e293b;
    }
    .stat-label {
      font-size: 11px;
      color: #94a3b8;
    }
    .type-actions {
      display: flex;
      gap: 8px;
      justify-content: flex-end;
    }
    .btn-icon {
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
    .btn-icon:hover { background: #f1f5f9; color: #3b82f6; }
    .btn-icon.active { color: #16a34a; border-color: #16a34a; }
    .btn-icon.danger:hover { background: #fef2f2; color: #ef4444; }
    
    /* Modal */
    .modal-overlay {
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(0,0,0,0.5);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
    }
    .modal {
      background: white;
      border-radius: 16px;
      width: 100%;
      max-width: 560px;
      max-height: 90vh;
      overflow: hidden;
      display: flex;
      flex-direction: column;
    }
    .modal.modal-sm { max-width: 400px; }
    .modal.modal-large { max-width: 700px; }
    .modal-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 20px 24px;
      border-bottom: 1px solid #e2e8f0;
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
      color: white;
    }
    .modal-header h3 { margin: 0; font-size: 18px; }
    .btn-close {
      width: 32px;
      height: 32px;
      border-radius: 8px;
      border: none;
      background: rgba(255,255,255,0.2);
      color: white;
      font-size: 20px;
      cursor: pointer;
    }
    .btn-close:hover { background: rgba(255,255,255,0.3); }
    .modal-body {
      padding: 0;
      overflow-y: auto;
      flex: 1;
    }
    .modal-footer {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 16px 24px;
      border-top: 1px solid #e2e8f0;
    }

    /* Tabs */
    .form-tabs {
      display: flex;
      gap: 4px;
      padding: 16px 24px;
      background: #f8fafc;
      border-bottom: 1px solid #e2e8f0;
      overflow-x: auto;
    }
    .form-tabs button {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 8px 14px;
      border: none;
      background: transparent;
      color: #64748b;
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
      border-radius: 6px;
      white-space: nowrap;
      transition: all 0.2s;
    }
    .form-tabs button:hover { background: #e2e8f0; }
    .form-tabs button.active { 
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); 
      color: white; 
    }

    .tab-content {
      padding: 24px;
    }

    .form-group {
      margin-bottom: 20px;
    }
    .form-group label {
      display: block;
      font-size: 14px;
      font-weight: 500;
      margin-bottom: 8px;
      color: #374151;
    }
    .form-group input[type="text"],
    .form-group input[type="number"],
    .form-group textarea,
    .form-group select {
      width: 100%;
      padding: 10px 14px;
      border: 1px solid #d1d5db;
      border-radius: 8px;
      font-size: 14px;
      transition: border-color 0.2s;
    }
    .form-group input:focus,
    .form-group textarea:focus,
    .form-group select:focus {
      outline: none;
      border-color: #667eea;
      box-shadow: 0 0 0 3px rgba(102, 126, 234, 0.1);
    }
    .form-group textarea {
      min-height: 80px;
      resize: vertical;
    }
    .form-group small {
      display: block;
      margin-top: 4px;
      font-size: 12px;
      color: #94a3b8;
    }
    .form-row {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 16px;
    }

    /* Permissions Section */
    .permissions-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 16px;
    }
    .permissions-header h4 {
      margin: 0;
      font-size: 14px;
      font-weight: 600;
      color: #374151;
    }
    .btn-select-all {
      padding: 6px 12px;
      border: 1px solid #d1d5db;
      background: white;
      border-radius: 6px;
      font-size: 12px;
      color: #64748b;
      cursor: pointer;
      transition: all 0.2s;
    }
    .btn-select-all:hover {
      background: #f1f5f9;
      border-color: #667eea;
      color: #667eea;
    }
    .permissions-grid {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 10px;
    }
    .permission-item {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 12px;
      border-radius: 8px;
      cursor: pointer;
      font-size: 13px;
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      transition: all 0.2s;
    }
    .permission-item:hover { background: #f1f5f9; border-color: #cbd5e1; }
    .permission-item.enabled { 
      background: #f0fdf4; 
      border-color: #86efac;
    }
    .permission-item input[type="checkbox"] {
      width: 16px;
      height: 16px;
      accent-color: #16a34a;
    }
    .perm-icon {
      width: 28px;
      height: 28px;
      border-radius: 6px;
      background: #e2e8f0;
      color: #64748b;
      display: flex;
      align-items: center;
      justify-content: center;
    }
    .permission-item.enabled .perm-icon {
      background: #dcfce7;
      color: #16a34a;
    }
    
    .btn-secondary {
      padding: 10px 20px;
      border: 1px solid #d1d5db;
      background: white;
      border-radius: 8px;
      cursor: pointer;
      font-weight: 500;
    }
    .btn-secondary:hover { background: #f9fafb; }
    .btn-danger {
      padding: 10px 20px;
      background: #ef4444;
      color: white;
      border: none;
      border-radius: 8px;
      cursor: pointer;
      font-weight: 500;
    }
    .btn-danger:hover { background: #dc2626; }
    .warning {
      color: #f59e0b;
      font-size: 14px;
    }

    @media (max-width: 768px) {
      .types-grid { grid-template-columns: 1fr; }
      .form-row { grid-template-columns: 1fr; }
      .permissions-grid { grid-template-columns: 1fr; }
      .form-tabs { flex-wrap: wrap; }
    }
  `]
})
export class AdminSubscriptionsComponent implements OnInit {
  subscriptionTypes: SubscriptionType[] = [];
  showModal = false;
  showDeleteConfirm = false;
  editingType: SubscriptionType | null = null;
  typeToDelete: SubscriptionType | null = null;
  activeTab: 'general' | 'limits' | 'modules' | 'reports' | 'features' = 'general';
  
  formData = this.getEmptyForm();

  availableModules = [
    { key: 'dashboard', label: 'Tableau de bord' },
    { key: 'monitoring', label: 'Monitoring GPS' },
    { key: 'vehicles', label: 'Véhicules' },
    { key: 'employees', label: 'Employés' },
    { key: 'geofences', label: 'Géofences' },
    { key: 'maintenance', label: 'Maintenance' },
    { key: 'costs', label: 'Coûts' },
    { key: 'reports', label: 'Rapports' },
    { key: 'settings', label: 'Paramètres' },
    { key: 'users', label: 'Utilisateurs' },
    { key: 'suppliers', label: 'Fournisseurs' },
    { key: 'documents', label: 'Documents' },
    { key: 'accidents', label: 'Sinistres' },
    { key: 'fleet_management', label: 'Gestion Flotte' }
  ];

  availableReports = [
    { key: 'trips', label: 'Rapport de trajets' },
    { key: 'fuel', label: 'Rapport carburant' },
    { key: 'speed', label: 'Rapport vitesse' },
    { key: 'stops', label: 'Rapport arrêts' },
    { key: 'mileage', label: 'Rapport kilométrique' },
    { key: 'costs', label: 'Rapport coûts' },
    { key: 'maintenance', label: 'Rapport maintenance' },
    { key: 'daily', label: 'Rapport journalier' },
    { key: 'monthly', label: 'Rapport mensuel' },
    { key: 'mileage_period', label: 'Kilométrage par période' },
    { key: 'speed_infraction', label: 'Infractions vitesse' },
    { key: 'driving_behavior', label: 'Comportement conduite' }
  ];

  availableFeatures = [
    { key: 'gpsTracking', label: 'Suivi GPS temps réel' },
    { key: 'gpsInstallation', label: 'Installation GPS incluse' },
    { key: 'apiAccess', label: 'Accès API' },
    { key: 'advancedReports', label: 'Rapports avancés' },
    { key: 'realTimeAlerts', label: 'Alertes temps réel' },
    { key: 'historyPlayback', label: 'Lecture historique' },
    { key: 'fuelAnalysis', label: 'Analyse carburant' },
    { key: 'drivingBehavior', label: 'Comportement conduite' }
  ];

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
      },
      error: (err) => console.error('Error loading subscription types:', err)
    });
  }

  getModulesCount(type: SubscriptionType): number {
    let count = 0;
    if (type.moduleDashboard) count++;
    if (type.moduleMonitoring) count++;
    if (type.moduleVehicles) count++;
    if (type.moduleEmployees) count++;
    if (type.moduleGeofences) count++;
    if (type.moduleMaintenance) count++;
    if (type.moduleCosts) count++;
    if (type.moduleReports) count++;
    if (type.moduleSettings) count++;
    if (type.moduleUsers) count++;
    if (type.moduleSuppliers) count++;
    if (type.moduleDocuments) count++;
    if (type.moduleAccidents) count++;
    if (type.moduleFleetManagement) count++;
    return count;
  }

  getReportsCount(type: SubscriptionType): number {
    let count = 0;
    if (type.reportTrips) count++;
    if (type.reportFuel) count++;
    if (type.reportSpeed) count++;
    if (type.reportStops) count++;
    if (type.reportMileage) count++;
    if (type.reportCosts) count++;
    if (type.reportMaintenance) count++;
    if (type.reportDaily) count++;
    if (type.reportMonthly) count++;
    if (type.reportMileagePeriod) count++;
    if (type.reportSpeedInfraction) count++;
    if (type.reportDrivingBehavior) count++;
    return count;
  }

  openCreateModal() {
    this.editingType = null;
    this.formData = this.getEmptyForm();
    this.activeTab = 'general';
    this.showModal = true;
  }

  editType(type: SubscriptionType) {
    this.editingType = type;
    this.formData = {
      name: type.name,
      description: type.description || '',
      targetCompanyType: type.targetCompanyType,
      yearlyPrice: type.yearlyPrice,
      maxVehicles: type.maxVehicles,
      maxUsers: type.maxUsers,
      maxGpsDevices: type.maxGpsDevices,
      maxGeofences: type.maxGeofences,
      historyRetentionDays: type.historyRetentionDays || 30,
      modules: {
        dashboard: type.moduleDashboard || false,
        monitoring: type.moduleMonitoring || false,
        vehicles: type.moduleVehicles || false,
        employees: type.moduleEmployees || false,
        geofences: type.moduleGeofences || false,
        maintenance: type.moduleMaintenance || false,
        costs: type.moduleCosts || false,
        reports: type.moduleReports || false,
        settings: type.moduleSettings || false,
        users: type.moduleUsers || false,
        suppliers: type.moduleSuppliers || false,
        documents: type.moduleDocuments || false,
        accidents: type.moduleAccidents || false,
        fleet_management: type.moduleFleetManagement || false
      },
      reports: {
        trips: type.reportTrips || false,
        fuel: type.reportFuel || false,
        speed: type.reportSpeed || false,
        stops: type.reportStops || false,
        mileage: type.reportMileage || false,
        costs: type.reportCosts || false,
        maintenance: type.reportMaintenance || false,
        daily: type.reportDaily || false,
        monthly: type.reportMonthly || false,
        mileage_period: type.reportMileagePeriod || false,
        speed_infraction: type.reportSpeedInfraction || false,
        driving_behavior: type.reportDrivingBehavior || false
      },
      features: {
        gpsTracking: type.gpsTracking || false,
        gpsInstallation: type.gpsInstallation || false,
        apiAccess: type.apiAccess || false,
        advancedReports: type.advancedReports || false,
        realTimeAlerts: type.realTimeAlerts || false,
        historyPlayback: type.historyPlayback || false,
        fuelAnalysis: type.fuelAnalysis || false,
        drivingBehavior: type.drivingBehavior || false
      }
    };
    this.activeTab = 'general';
    this.showModal = true;
  }

  closeModal() {
    this.showModal = false;
    this.editingType = null;
  }

  toggleStatus(type: SubscriptionType) {
    this.adminService.updateSubscriptionType(type.id, { isActive: !type.isActive }).subscribe({
      next: () => {
        type.isActive = !type.isActive;
      },
      error: (err) => console.error('Error toggling status:', err)
    });
  }

  toggleAllModules() {
    const allSelected = this.areAllModulesSelected();
    this.availableModules.forEach(m => {
      this.formData.modules[m.key] = !allSelected;
    });
  }

  toggleAllReports() {
    const allSelected = this.areAllReportsSelected();
    this.availableReports.forEach(r => {
      this.formData.reports[r.key] = !allSelected;
    });
  }

  areAllModulesSelected(): boolean {
    return this.availableModules.every(m => this.formData.modules[m.key]);
  }

  areAllReportsSelected(): boolean {
    return this.availableReports.every(r => this.formData.reports[r.key]);
  }

  saveType() {
    const data: any = {
      name: this.formData.name,
      code: this.formData.name.toLowerCase().replace(/\s+/g, '-'),
      description: this.formData.description || undefined,
      targetCompanyType: this.formData.targetCompanyType,
      yearlyPrice: this.formData.yearlyPrice,
      maxVehicles: this.formData.maxVehicles,
      maxUsers: this.formData.maxUsers,
      maxGpsDevices: this.formData.maxGpsDevices,
      maxGeofences: this.formData.maxGeofences,
      historyRetentionDays: this.formData.historyRetentionDays,
      // Features
      gpsTracking: this.formData.features['gpsTracking'],
      gpsInstallation: this.formData.features['gpsInstallation'],
      apiAccess: this.formData.features['apiAccess'],
      advancedReports: this.formData.features['advancedReports'],
      realTimeAlerts: this.formData.features['realTimeAlerts'],
      historyPlayback: this.formData.features['historyPlayback'],
      fuelAnalysis: this.formData.features['fuelAnalysis'],
      drivingBehavior: this.formData.features['drivingBehavior'],
      // Modules
      moduleDashboard: this.formData.modules['dashboard'],
      moduleMonitoring: this.formData.modules['monitoring'],
      moduleVehicles: this.formData.modules['vehicles'],
      moduleEmployees: this.formData.modules['employees'],
      moduleGeofences: this.formData.modules['geofences'],
      moduleMaintenance: this.formData.modules['maintenance'],
      moduleCosts: this.formData.modules['costs'],
      moduleReports: this.formData.modules['reports'],
      moduleSettings: this.formData.modules['settings'],
      moduleUsers: this.formData.modules['users'],
      moduleSuppliers: this.formData.modules['suppliers'],
      moduleDocuments: this.formData.modules['documents'],
      moduleAccidents: this.formData.modules['accidents'],
      moduleFleetManagement: this.formData.modules['fleet_management'],
      // Reports
      reportTrips: this.formData.reports['trips'],
      reportFuel: this.formData.reports['fuel'],
      reportSpeed: this.formData.reports['speed'],
      reportStops: this.formData.reports['stops'],
      reportMileage: this.formData.reports['mileage'],
      reportCosts: this.formData.reports['costs'],
      reportMaintenance: this.formData.reports['maintenance'],
      reportDaily: this.formData.reports['daily'],
      reportMonthly: this.formData.reports['monthly'],
      reportMileagePeriod: this.formData.reports['mileage_period'],
      reportSpeedInfraction: this.formData.reports['speed_infraction'],
      reportDrivingBehavior: this.formData.reports['driving_behavior']
    };

    if (this.editingType) {
      this.adminService.updateSubscriptionType(this.editingType.id, data).subscribe({
        next: () => {
          this.loadData();
          this.closeModal();
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
          this.closeModal();
        },
        error: (err) => {
          console.error('Error creating type:', err);
          alert(err.error?.message || 'Erreur lors de la création');
        }
      });
    }
  }

  confirmDelete(type: SubscriptionType) {
    this.typeToDelete = type;
    this.showDeleteConfirm = true;
  }

  deleteType() {
    if (!this.typeToDelete) return;
    
    this.adminService.deleteSubscriptionType(this.typeToDelete.id).subscribe({
      next: () => {
        this.loadData();
        this.showDeleteConfirm = false;
        this.typeToDelete = null;
      },
      error: (err) => console.error('Error deleting type:', err)
    });
  }

  private getEmptyForm() {
    return {
      name: '',
      description: '',
      targetCompanyType: 'all',
      yearlyPrice: 0,
      maxVehicles: 10,
      maxUsers: 5,
      maxGpsDevices: 10,
      maxGeofences: 20,
      historyRetentionDays: 30,
      modules: {
        dashboard: true,
        monitoring: false,
        vehicles: true,
        employees: true,
        geofences: false,
        maintenance: true,
        costs: true,
        reports: true,
        settings: true,
        users: true,
        suppliers: true,
        documents: true,
        accidents: true,
        fleet_management: false
      } as Record<string, boolean>,
      reports: {
        trips: true,
        fuel: false,
        speed: true,
        stops: true,
        mileage: true,
        costs: true,
        maintenance: true,
        daily: true,
        monthly: false,
        mileage_period: false,
        speed_infraction: true,
        driving_behavior: false
      } as Record<string, boolean>,
      features: {
        gpsTracking: true,
        gpsInstallation: false,
        apiAccess: false,
        advancedReports: false,
        realTimeAlerts: true,
        historyPlayback: true,
        fuelAnalysis: false,
        drivingBehavior: false
      } as Record<string, boolean>
    };
  }
}
