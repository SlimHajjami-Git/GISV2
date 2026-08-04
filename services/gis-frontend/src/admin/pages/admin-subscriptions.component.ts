import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService, SubscriptionType, SubscriptionOrderAdmin } from '../services/admin.service';
import { environment } from '../../environments/environment';

@Component({
  selector: 'admin-subscriptions',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent],
  template: `
    <admin-layout pageTitle="Gestion des Abonnements">
      <div class="subscriptions-container">

        <!-- Commandes en libre-service : les clients GPA commandent depuis leur
             écran Abonnement ; on confirme ici une fois le règlement reçu. -->
        <div class="orders-section" *ngIf="orders.length > 0 || ordersFilter !== 'pending'">
          <div class="page-header">
            <div class="header-left">
              <h2>Commandes clients</h2>
              <span class="count" [class.hot]="pendingOrdersCount > 0">{{ pendingOrdersCount }} en attente</span>
            </div>
            <select class="orders-filter" [(ngModel)]="ordersFilter" (change)="loadOrders()">
              <option value="pending">En attente</option>
              <option value="all">Toutes</option>
              <option value="confirmed">Confirmées</option>
              <option value="cancelled">Annulées</option>
              <option value="rejected">Rejetées</option>
            </select>
          </div>

          <div class="orders-table-wrap" *ngIf="orders.length > 0; else noOrders">
            <table class="orders-table">
              <thead>
                <tr>
                  <th>Société</th>
                  <th>Offre</th>
                  <th>Cycle</th>
                  <th>Montant</th>
                  <th>Date</th>
                  <th>Statut</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                <tr *ngFor="let o of orders">
                  <td class="cell-company">{{ o.companyName }}</td>
                  <td>{{ o.planName }}</td>
                  <td>{{ cycleLabel(o.billingCycle) }}</td>
                  <td class="cell-amount">{{ o.amount | number:'1.0-2' }} {{ currencyCode }}</td>
                  <td>{{ o.createdAt | date:'dd/MM/yyyy HH:mm' }}</td>
                  <td>
                    <span class="badge" [ngClass]="'st-' + o.status">{{ statusLabel(o.status) }}</span>
                    <div class="order-note" *ngIf="o.note">{{ o.note }}</div>
                  </td>
                  <td class="cell-actions">
                    <ng-container *ngIf="o.status === 'pending'">
                      <button class="btn-confirm" [disabled]="processingOrderId === o.id" (click)="confirmOrder(o)">
                        {{ processingOrderId === o.id ? '…' : 'Confirmer' }}
                      </button>
                      <button class="btn-reject" [disabled]="processingOrderId === o.id" (click)="openReject(o)">Rejeter</button>
                    </ng-container>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          <ng-template #noOrders>
            <p class="orders-empty">Aucune commande {{ ordersFilter === 'pending' ? 'en attente' : '' }}.</p>
          </ng-template>
        </div>

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
                <span class="type-price">{{ type.yearlyPrice | number:'1.0-0' }} {{ currencyCode }}/an</span>
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
        <div class="modal-overlay" *ngIf="showModal" (mousedown)="closeModal()">
          <div class="modal modal-large" (mousedown)="$event.stopPropagation()">
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
                    <label>Prix annuel ({{ currencyCode }}) *</label>
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

        <!-- Reject Order -->
        <div class="modal-overlay" *ngIf="orderToReject" (click)="orderToReject = null">
          <div class="modal modal-sm" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h3>Rejeter la commande</h3>
            </div>
            <div class="modal-body" style="padding: 20px 24px;">
              <p>Commande de <strong>{{ orderToReject?.companyName }}</strong> —
                 {{ orderToReject?.planName }} ({{ cycleLabel(orderToReject?.billingCycle || '') }}).</p>
              <div class="form-group">
                <label>Motif (montré au client)</label>
                <textarea [(ngModel)]="rejectReason" rows="3"
                          placeholder="Ex : règlement non reçu, coordonnées invalides…"></textarea>
              </div>
            </div>
            <div class="modal-footer">
              <button class="btn-secondary" (click)="orderToReject = null">Annuler</button>
              <button class="btn-danger" [disabled]="processingOrderId !== null" (click)="rejectOrder()">Rejeter</button>
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
    @keyframes rise {
      from { opacity: 0; transform: translateY(8px); }
      to { opacity: 1; transform: none; }
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
      font-size: 12px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: .08em;
      color: var(--adm-sub);
      display: flex;
      align-items: center;
      gap: 8px;
    }
    .header-left h2::before {
      content: '';
      width: 3px;
      height: 12px;
      border-radius: 2px;
      background: var(--adm-indigo);
    }
    .count {
      background: rgba(100, 116, 139, .10);
      padding: 3px 10px;
      border-radius: 999px;
      font-size: 11px;
      font-weight: 700;
      color: var(--adm-slate-ink);
      font-variant-numeric: tabular-nums;
    }
    .btn-primary {
      display: flex;
      align-items: center;
      gap: 8px;
      background: var(--adm-indigo);
      color: #fff;
      border: none;
      padding: 9px 18px;
      border-radius: 10px;
      cursor: pointer;
      font-weight: 600;
      transition: all 0.2s;
    }
    .btn-primary:hover { background: var(--adm-indigo-ink); transform: translateY(-1px); box-shadow: var(--adm-shadow-hover); }
    .btn-primary:disabled { background: #94a3b8; cursor: not-allowed; transform: none; box-shadow: none; }

    /* ── Commandes clients ── */
    .orders-section { margin-bottom: 32px; }
    .count.hot { background: rgba(217, 119, 6, .14); color: var(--adm-amber-ink); }
    .orders-filter {
      padding: 8px 12px;
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      background: #fff;
      color: var(--adm-ink);
      font-size: 13px;
      font-weight: 600;
      cursor: pointer;
    }
    .orders-table-wrap {
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 16px;
      box-shadow: var(--adm-shadow);
      overflow-x: auto;
      animation: rise .3s ease both;
    }
    .orders-table { width: 100%; border-collapse: collapse; font-size: 13px; }
    .orders-table th {
      text-align: left;
      padding: 12px 16px;
      font-size: 11px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: .06em;
      color: var(--adm-sub);
      border-bottom: 1px solid var(--adm-border);
      white-space: nowrap;
    }
    .orders-table td {
      padding: 12px 16px;
      border-bottom: 1px solid var(--adm-track);
      color: var(--adm-ink);
      vertical-align: top;
    }
    .orders-table tr:last-child td { border-bottom: none; }
    .cell-company { font-weight: 700; }
    .cell-amount { font-weight: 700; font-variant-numeric: tabular-nums; white-space: nowrap; }
    .badge.st-pending { background: rgba(217, 119, 6, .12); color: var(--adm-amber-ink); }
    .badge.st-confirmed { background: rgba(5, 150, 105, .10); color: var(--adm-green-ink); }
    .badge.st-cancelled { background: rgba(100, 116, 139, .12); color: var(--adm-slate-ink); }
    .badge.st-rejected { background: rgba(220, 38, 38, .10); color: var(--adm-red-ink); }
    .order-note { margin-top: 4px; font-size: 12px; color: var(--adm-sub); max-width: 220px; }
    .cell-actions { white-space: nowrap; text-align: right; }
    .btn-confirm {
      padding: 7px 14px;
      border: none;
      border-radius: 9px;
      background: var(--adm-green);
      color: #fff;
      font-size: 12.5px;
      font-weight: 700;
      cursor: pointer;
      transition: background .2s;
    }
    .btn-confirm:hover:not(:disabled) { background: var(--adm-green-ink); }
    .btn-reject {
      margin-left: 8px;
      padding: 7px 14px;
      border: 1px solid var(--adm-border);
      border-radius: 9px;
      background: #fff;
      color: var(--adm-red-ink);
      font-size: 12.5px;
      font-weight: 700;
      cursor: pointer;
      transition: all .2s;
    }
    .btn-reject:hover:not(:disabled) { border-color: var(--adm-red); background: rgba(220, 38, 38, .06); }
    .btn-confirm:disabled, .btn-reject:disabled { opacity: .55; cursor: default; }
    .orders-empty { color: var(--adm-sub); font-size: 13px; margin: 8px 0 0; }

    .types-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(340px, 1fr));
      gap: 20px;
    }
    .type-card {
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 16px;
      padding: 20px;
      box-shadow: var(--adm-shadow);
      transition: transform 0.2s, box-shadow 0.2s;
      animation: rise .3s ease both;
    }
    .type-card:hover {
      transform: translateY(-1px);
      box-shadow: var(--adm-shadow-hover);
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
      background: rgba(79, 70, 229, .10);
      color: var(--adm-indigo);
      display: flex;
      align-items: center;
      justify-content: center;
    }
    .type-icon.premium { background: rgba(217, 119, 6, .12); color: var(--adm-amber); }
    .type-info { flex: 1; }
    .type-info h3 {
      margin: 0 0 4px 0;
      font-size: 16px;
      font-weight: 700;
      color: var(--adm-ink);
    }
    .type-price {
      font-size: 13px;
      color: var(--adm-green-ink);
      font-weight: 700;
      font-variant-numeric: tabular-nums;
    }
    .type-badges {
      display: flex;
      gap: 6px;
    }
    .badge {
      padding: 3px 10px;
      border-radius: 999px;
      font-size: 11px;
      font-weight: 700;
    }
    .badge.inactive { background: rgba(220, 38, 38, .10); color: var(--adm-red-ink); }
    .badge.gps { background: rgba(8, 145, 178, .10); color: var(--adm-cyan-ink); }
    
    .type-description {
      font-size: 13px;
      color: var(--adm-sub);
      margin: 0 0 16px 0;
      line-height: 1.5;
    }
    .type-stats {
      display: flex;
      gap: 12px;
      padding: 12px 0;
      border-top: 1px solid var(--adm-track);
      margin-bottom: 12px;
    }
    .stat {
      display: flex;
      flex-direction: column;
      flex: 1;
      text-align: left;
      padding-left: 9px;
      border-left: 3px solid var(--adm-indigo);
    }
    .stat:nth-child(2) { border-left-color: var(--adm-cyan); }
    .stat:nth-child(3) { border-left-color: var(--adm-green); }
    .stat:nth-child(4) { border-left-color: var(--adm-amber); }
    .stat-value {
      font-size: 24px;
      font-weight: 800;
      color: var(--adm-ink);
      font-variant-numeric: tabular-nums;
      line-height: 1.15;
    }
    .stat-label {
      font-size: 11px;
      color: var(--adm-sub);
      text-transform: uppercase;
      letter-spacing: .06em;
    }
    .type-actions {
      display: flex;
      gap: 8px;
      justify-content: flex-end;
    }
    .btn-icon {
      width: 36px;
      height: 36px;
      border-radius: 10px;
      border: 1px solid var(--adm-border);
      background: #fff;
      color: var(--adm-sub);
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }
    .btn-icon:hover { background: #f8fafc; color: var(--adm-indigo); border-color: var(--adm-indigo); }
    .btn-icon.active { color: var(--adm-green); border-color: var(--adm-green); }
    .btn-icon.danger:hover { background: rgba(220, 38, 38, .08); color: var(--adm-red); border-color: var(--adm-red); }
    
    /* Modal */
    .modal-overlay {
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(15, 23, 42, .55);
      backdrop-filter: blur(4px);
      -webkit-backdrop-filter: blur(4px);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
    }
    .modal {
      background: var(--adm-card);
      border-radius: 18px;
      box-shadow: 0 24px 60px -24px rgba(2, 6, 23, .45);
      width: 100%;
      max-width: 560px;
      max-height: 90vh;
      overflow: hidden;
      display: flex;
      flex-direction: column;
      animation: rise .25s ease both;
    }
    .modal.modal-sm { max-width: 400px; }
    .modal.modal-large { max-width: 700px; }
    .modal-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 20px 24px;
      border-bottom: 1px solid var(--adm-border);
      background: var(--adm-card);
      color: var(--adm-ink);
    }
    .modal-header h3 { margin: 0; font-size: 16px; font-weight: 700; }
    .btn-close {
      width: 32px;
      height: 32px;
      border-radius: 10px;
      border: none;
      background: var(--adm-track);
      color: var(--adm-sub);
      font-size: 20px;
      cursor: pointer;
      transition: all 0.2s;
    }
    .btn-close:hover { background: #e6eaf2; color: var(--adm-ink); }
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
      border-top: 1px solid var(--adm-border);
    }

    /* Tabs */
    .form-tabs {
      display: flex;
      gap: 4px;
      padding: 16px 24px;
      background: #f8fafc;
      border-bottom: 1px solid var(--adm-border);
      overflow-x: auto;
    }
    .form-tabs button {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 8px 14px;
      border: none;
      background: transparent;
      color: var(--adm-sub);
      font-size: 13px;
      font-weight: 600;
      cursor: pointer;
      border-radius: 8px;
      white-space: nowrap;
      transition: all 0.2s;
    }
    .form-tabs button:hover { background: var(--adm-track); }
    .form-tabs button.active {
      background: var(--adm-indigo);
      color: #fff;
    }

    .tab-content {
      padding: 24px;
    }

    .form-group {
      margin-bottom: 20px;
    }
    .form-group label {
      display: block;
      font-size: 13px;
      font-weight: 600;
      margin-bottom: 8px;
      color: var(--adm-ink);
    }
    .form-group input[type="text"],
    .form-group input[type="number"],
    .form-group textarea,
    .form-group select {
      width: 100%;
      padding: 10px 14px;
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      font-size: 14px;
      color: var(--adm-ink);
      background: #fff;
      transition: border-color 0.2s, box-shadow 0.2s;
    }
    .form-group input:focus,
    .form-group textarea:focus,
    .form-group select:focus {
      outline: none;
      border-color: var(--adm-indigo);
      box-shadow: 0 0 0 3px rgba(79, 70, 229, .12);
    }
    .form-group textarea {
      min-height: 80px;
      resize: vertical;
    }
    .form-group small {
      display: block;
      margin-top: 4px;
      font-size: 12px;
      color: var(--adm-sub);
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
      font-size: 12px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: .08em;
      color: var(--adm-sub);
      display: flex;
      align-items: center;
      gap: 8px;
    }
    .permissions-header h4::before {
      content: '';
      width: 3px;
      height: 12px;
      border-radius: 2px;
      background: var(--adm-indigo);
    }
    .btn-select-all {
      padding: 6px 12px;
      border: 1px solid var(--adm-border);
      background: #fff;
      border-radius: 8px;
      font-size: 12px;
      font-weight: 600;
      color: var(--adm-slate-ink);
      cursor: pointer;
      transition: all 0.2s;
    }
    .btn-select-all:hover {
      background: #f8fafc;
      border-color: var(--adm-indigo);
      color: var(--adm-indigo);
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
      border-radius: 10px;
      cursor: pointer;
      font-size: 13px;
      color: var(--adm-ink);
      background: #f8fafc;
      border: 1px solid var(--adm-border);
      transition: all 0.2s;
    }
    .permission-item:hover { background: var(--adm-track); border-color: #cbd5e1; }
    .permission-item.enabled {
      background: rgba(5, 150, 105, .08);
      border-color: rgba(5, 150, 105, .35);
    }
    .permission-item input[type="checkbox"] {
      width: 16px;
      height: 16px;
      accent-color: var(--adm-green);
    }
    .perm-icon {
      width: 28px;
      height: 28px;
      border-radius: 8px;
      background: var(--adm-track);
      color: var(--adm-sub);
      display: flex;
      align-items: center;
      justify-content: center;
    }
    .permission-item.enabled .perm-icon {
      background: rgba(5, 150, 105, .12);
      color: var(--adm-green-ink);
    }
    
    .btn-secondary {
      padding: 9px 18px;
      border: 1px solid var(--adm-border);
      background: #fff;
      color: var(--adm-ink);
      border-radius: 10px;
      cursor: pointer;
      font-weight: 600;
      transition: all 0.2s;
    }
    .btn-secondary:hover { border-color: var(--adm-indigo); color: var(--adm-indigo-ink); }
    .btn-danger {
      padding: 9px 18px;
      background: var(--adm-red);
      color: #fff;
      border: none;
      border-radius: 10px;
      cursor: pointer;
      font-weight: 600;
      transition: background 0.2s;
    }
    .btn-danger:hover { background: var(--adm-red-ink); }
    .warning {
      color: var(--adm-amber-ink);
      font-size: 13px;
    }

    @media (prefers-reduced-motion: reduce) {
      .type-card, .modal { animation: none; }
      .type-card:hover, .btn-primary:hover { transform: none; }
    }

    @media (max-width: 768px) {
      .types-grid { grid-template-columns: 1fr; }
      .form-row { grid-template-columns: 1fr; }
      .permissions-grid { grid-template-columns: 1fr; }
      .form-tabs { flex-wrap: wrap; }
    }
  `]
})
export class AdminSubscriptionsComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  subscriptionTypes: SubscriptionType[] = [];
  showModal = false;
  showDeleteConfirm = false;
  editingType: SubscriptionType | null = null;
  typeToDelete: SubscriptionType | null = null;
  activeTab: 'general' | 'limits' | 'modules' | 'reports' | 'features' = 'general';

  // ── Commandes clients (libre-service GPA) ──
  orders: SubscriptionOrderAdmin[] = [];
  ordersFilter = 'pending';
  processingOrderId: number | null = null;
  orderToReject: SubscriptionOrderAdmin | null = null;
  rejectReason = '';

  get currencyCode(): string { return (environment as { defaultCurrency?: string }).defaultCurrency || 'TND'; }

  formData = this.getEmptyForm();

  availableModules = [
    { key: 'dashboard', label: 'Tableau de bord' },
    { key: 'monitoring', label: 'Monitoring GPS' },
    { key: 'vehicles', label: 'Véhicules' },
    { key: 'employees', label: 'Chauffeurs' },
    { key: 'geofences', label: 'Géofences' },
    { key: 'maintenance', label: 'Maintenance' },
    { key: 'costs', label: 'Coûts' },
    { key: 'reports', label: 'Rapports' },
    { key: 'settings', label: 'Paramètres' },
    { key: 'users', label: 'Utilisateurs' },
    { key: 'suppliers', label: 'Fournisseurs' },
    { key: 'documents', label: 'Documents' },
    { key: 'accidents', label: 'Sinistres' },
    { key: 'fleet_management', label: 'Gestion Flotte' },
    // Ces deux modules existent en base et valent TRUE par défaut. Sans case ici,
    // aucun plan ne pouvait les éteindre : un abonnement « sans GPS » se retrouvait
    // avec l'écran Tournées ouvert alors qu'il n'a aucun itinéraire à suivre.
    { key: 'carburant', label: 'Carburant' },
    { key: 'tours', label: 'Tournées' }
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
    this.adminService.getSubscriptionTypes().pipe(takeUntil(this.destroy$)).subscribe({
      next: (types) => {
        this.subscriptionTypes = types;
      },
      error: (err) => console.error('Error loading subscription types:', err)
    });
    this.loadOrders();
  }

  // ── Commandes clients ──

  loadOrders() {
    const status = this.ordersFilter === 'all' ? undefined : this.ordersFilter;
    this.adminService.getSubscriptionOrders(status).pipe(takeUntil(this.destroy$)).subscribe({
      next: (orders) => { this.orders = orders; }
    });
  }

  get pendingOrdersCount(): number {
    return this.orders.filter(o => o.status === 'pending').length;
  }

  cycleLabel(cycle: string): string {
    switch (cycle) {
      case 'monthly': return 'Mensuel';
      case 'quarterly': return 'Trimestriel';
      case 'yearly': return 'Annuel';
      default: return cycle;
    }
  }

  statusLabel(status: string): string {
    switch (status) {
      case 'pending': return 'En attente';
      case 'confirmed': return 'Confirmée';
      case 'cancelled': return 'Annulée';
      case 'rejected': return 'Rejetée';
      default: return status;
    }
  }

  confirmOrder(order: SubscriptionOrderAdmin) {
    if (!confirm(`Confirmer la commande de ${order.companyName} (${order.amount} ${this.currencyCode}) ?\n\nLe règlement a bien été reçu ? L'abonnement sera activé immédiatement.`)) return;
    this.processingOrderId = order.id;
    this.adminService.confirmSubscriptionOrder(order.id).pipe(takeUntil(this.destroy$)).subscribe({
      next: () => { this.processingOrderId = null; this.loadOrders(); },
      error: (err) => {
        this.processingOrderId = null;
        alert(err.error?.message || 'Erreur lors de la confirmation');
        this.loadOrders();
      }
    });
  }

  openReject(order: SubscriptionOrderAdmin) {
    this.orderToReject = order;
    this.rejectReason = '';
  }

  rejectOrder() {
    const order = this.orderToReject;
    if (!order) return;
    this.processingOrderId = order.id;
    this.adminService.rejectSubscriptionOrder(order.id, this.rejectReason || undefined).pipe(takeUntil(this.destroy$)).subscribe({
      next: () => {
        this.processingOrderId = null;
        this.orderToReject = null;
        this.loadOrders();
      },
      error: (err) => {
        this.processingOrderId = null;
        alert(err.error?.message || 'Erreur lors du rejet');
      }
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
    if (type.reportMonthlyCosts) count++;
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
        fleet_management: type.moduleFleetManagement || false,
        carburant: type.moduleFuel || false,
        tours: type.moduleTours || false
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
        driving_behavior: type.reportDrivingBehavior || false,
        monthly_costs: type.reportMonthlyCosts || false
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
    this.adminService.updateSubscriptionType(type.id, { isActive: !type.isActive }).pipe(takeUntil(this.destroy$)).subscribe({
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
      moduleFuel: this.formData.modules['carburant'],
      moduleTours: this.formData.modules['tours'],
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
      reportDrivingBehavior: this.formData.reports['driving_behavior'],
      reportMonthlyCosts: this.formData.reports['monthly_costs']
    };

    if (this.editingType) {
      this.adminService.updateSubscriptionType(this.editingType.id, data).pipe(takeUntil(this.destroy$)).subscribe({
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
      this.adminService.createSubscriptionType(data).pipe(takeUntil(this.destroy$)).subscribe({
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
    
    this.adminService.deleteSubscriptionType(this.typeToDelete.id).pipe(takeUntil(this.destroy$)).subscribe({
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
        fleet_management: false,
        carburant: true,
        tours: false
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

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
