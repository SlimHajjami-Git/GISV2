import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { ApiService } from '../services/api.service';
import { AppLayoutComponent } from './shared/app-layout.component';
import { ToastService } from '../services/toast.service';

interface Role {
  id: number;
  name: string;
  description?: string;
  isSystem: boolean;
  isCompanyAdmin: boolean;
  permissions?: Record<string, any>;
  usersCount?: number;
}

interface UserPermissions {
  accessLevel: string;
  canMonitoring: boolean;
  canVehicles: boolean;
  canDrivers: boolean;
  canReports: boolean;
  canGeofences: boolean;
  canMaintenance: boolean;
  canCosts: boolean;
  canFuel: boolean;
  canDocuments: boolean;
  canAccidents: boolean;
  canUsers: boolean;
  canSettings: boolean;
  canSuppliers: boolean;
  canFleetManagement: boolean;
  canTours: boolean;
  canPlayback: boolean;
  // Per-report permissions
  canReportTrips: boolean;
  canReportFuel: boolean;
  canReportSpeed: boolean;
  canReportStops: boolean;
  canReportMileage: boolean;
  canReportCosts: boolean;
  canReportMaintenance: boolean;
  canReportDaily: boolean;
  canReportMonthly: boolean;
  canReportMileagePeriod: boolean;
  canReportSpeedInfraction: boolean;
  canReportDrivingBehavior: boolean;
  canReportMonthlyCosts: boolean;
}

interface User {
  id: number;
  name: string;
  email: string;
  phone?: string;
  roleId: number;
  roleName?: string;
  isCompanyAdmin: boolean;
  status: string;
  createdAt: string;
  lastLoginAt?: string;
  assignedVehicleIds?: number[];
  userPermissions?: UserPermissions;
}

interface VehicleOption {
  id: number;
  name: string;
  plate: string;
}

@Component({
  selector: 'app-user-management',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent],
  template: `
    <app-layout>
      <div class="user-management-page">
        <div class="page-header">
          <div class="header-left">
            <h1>Gestion des Utilisateurs</h1>
            <p class="subtitle">Gérez les utilisateurs de votre équipe</p>
          </div>
          <div class="header-actions">
            <button class="btn-primary" (click)="openUserModal()">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
              </svg>
              Nouvel Utilisateur
            </button>
          </div>
        </div>


        <!-- Stats Cards -->
        <div class="stats-row">
          <div class="stat-card">
            <div class="stat-icon users">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/>
              </svg>
            </div>
            <div class="stat-info">
              <span class="stat-value">{{ users.length }}</span>
              <span class="stat-label">Total</span>
            </div>
          </div>
          <div class="stat-card">
            <div class="stat-icon active">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/>
              </svg>
            </div>
            <div class="stat-info">
              <span class="stat-value">{{ getActiveUsersCount() }}</span>
              <span class="stat-label">Actifs</span>
            </div>
          </div>
          <div class="stat-card">
            <div class="stat-icon supervisors">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M12 2L2 7l10 5 10-5-10-5z"/><path d="M2 17l10 5 10-5"/>
              </svg>
            </div>
            <div class="stat-info">
              <span class="stat-value">{{ getAdminsCount() }}</span>
              <span class="stat-label">Admins</span>
            </div>
          </div>
          <div class="stat-card">
            <div class="stat-icon drivers">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>
              </svg>
            </div>
            <div class="stat-info">
              <span class="stat-value">{{ getInactiveUsersCount() }}</span>
              <span class="stat-label">Inactifs</span>
            </div>
          </div>
        </div>

        <!-- Users Table -->
        <div class="users-card">
          <div class="card-header">
            <div class="search-box">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
              </svg>
              <input type="text" [(ngModel)]="searchQuery" placeholder="Rechercher..." (input)="filterUsers()">
            </div>
            <div class="filter-group">
              <select [(ngModel)]="filterStatus" (change)="filterUsers()">
                <option value="">Tous les statuts</option>
                <option value="active">Actifs</option>
                <option value="inactive">Inactifs</option>
              </select>
            </div>
          </div>

          <div class="table-container">
            <table class="users-table">
              <thead>
                <tr>
                  <th>Utilisateur</th>
                  <th>Permissions</th>
                  <th>Statut</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                <tr *ngFor="let user of filteredUsers">
                  <td>
                    <div class="user-info">
                      <div class="user-avatar">{{ getInitials(user.name) }}</div>
                      <div class="user-details">
                        <span class="user-name">{{ user.name }}</span>
                        <span class="user-email">{{ user.email }}</span>
                      </div>
                    </div>
                  </td>
                  <td>
                    <span class="role-badge" [class.admin]="user.isCompanyAdmin">
                      {{ user.isCompanyAdmin ? 'Admin' : getPermissionsLabel(user) }}
                    </span>
                  </td>
                  <td>
                    <span class="status-badge" [class]="user.status">
                      {{ user.status === 'active' ? 'Actif' : 'Inactif' }}
                    </span>
                  </td>
                  <td>
                    <div class="action-buttons">
                      <button class="btn-icon" title="Modifier" (click)="openUserModal(user)">
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                          <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                        </svg>
                      </button>
                      <button class="btn-icon danger" title="Supprimer" (click)="deleteUser(user)">
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <polyline points="3 6 5 6 21 6"/>
                          <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                        </svg>
                      </button>
                    </div>
                  </td>
                </tr>
              </tbody>
            </table>
            <div class="empty-state" *ngIf="filteredUsers.length === 0 && !loading">
              <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/>
              </svg>
              <p>Aucun utilisateur trouvé</p>
            </div>
          </div>
        </div>

        <!-- User Modal -->
        <div class="modal-overlay" *ngIf="showUserModal" (mousedown)="closeUserModal()">
          <div class="modal-content" (mousedown)="$event.stopPropagation()">
            <div class="modal-header">
              <h2>{{ editingUser ? 'Modifier' : 'Nouvel' }} Utilisateur</h2>
              <button class="btn-close" (click)="closeUserModal()">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                </svg>
              </button>
            </div>

            <!-- Step Tabs -->
            <div class="step-tabs">
              <button class="step-tab" [class.active]="userModalStep === 1" (click)="userModalStep = 1">
                <span class="step-num">1</span> Général
              </button>
              <button class="step-tab" [class.active]="userModalStep === 2" (click)="userModalStep = 2">
                <span class="step-num">2</span> Permissions
              </button>
              <button class="step-tab" [class.active]="userModalStep === 3" (click)="userModalStep = 3">
                <span class="step-num">3</span> Véhicules
              </button>
            </div>

            <div class="modal-body">
              <!-- Step 1: Général -->
              <div *ngIf="userModalStep === 1">
                <div class="form-section">
                  <div class="form-grid">
                    <div class="form-group">
                      <label>Prénom *</label>
                      <input type="text" [(ngModel)]="userForm.firstName" placeholder="Prénom">
                    </div>
                    <div class="form-group">
                      <label>Nom *</label>
                      <input type="text" [(ngModel)]="userForm.lastName" placeholder="Nom">
                    </div>
                    <div class="form-group">
                      <label>Email *</label>
                      <input type="email" [(ngModel)]="userForm.email" placeholder="email@exemple.com">
                    </div>
                    <div class="form-group">
                      <label>Téléphone</label>
                      <input type="tel" [(ngModel)]="userForm.phone" placeholder="+216 XX XXX XXX">
                    </div>
                    <div class="form-group" *ngIf="!editingUser">
                      <label>Mot de passe *</label>
                      <input type="password" [(ngModel)]="userForm.password" placeholder="••••••••">
                    </div>
                    <div class="form-group" *ngIf="editingUser">
                      <label>Statut</label>
                      <select [(ngModel)]="userForm.status">
                        <option value="active">Actif</option>
                        <option value="inactive">Inactif</option>
                      </select>
                    </div>
                  </div>
                </div>
              </div>

              <!-- Step 2: Permissions -->
              <div *ngIf="userModalStep === 2">
                <div class="form-section">
                  <h3 class="section-title">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>
                    Accès aux modules
                  </h3>
                  <p class="section-hint">Cochez les modules auxquels cet utilisateur aura accès. Le tableau de bord est toujours accessible.</p>
                  <div class="permissions-grid">
                    <label class="perm-check">
                      <input type="checkbox" [(ngModel)]="userForm.canMonitoring">
                      <span class="perm-label">📍 Monitoring GPS</span>
                      <span class="perm-desc">Suivi en temps réel sur carte</span>
                    </label>
                    <label class="perm-check">
                      <input type="checkbox" [(ngModel)]="userForm.canVehicles">
                      <span class="perm-label">🚗 Véhicules</span>
                      <span class="perm-desc">Gestion du parc automobile</span>
                    </label>
                    <label class="perm-check">
                      <input type="checkbox" [(ngModel)]="userForm.canDrivers">
                      <span class="perm-label">👤 Chauffeurs</span>
                      <span class="perm-desc">Gestion des chauffeurs</span>
                    </label>
                    <label class="perm-check">
                      <input type="checkbox" [(ngModel)]="userForm.canReports">
                      <span class="perm-label">📊 Rapports</span>
                      <span class="perm-desc">Rapports d'activité et analyse</span>
                    </label>
                    <!-- Sub-report permissions accordion (shown when canReports is checked) -->
                    <div class="report-sub-permissions" *ngIf="userForm.canReports">
                      <div class="sub-perm-header">
                        <span class="sub-perm-title">Types de rapports autorisés</span>
                        <div class="sub-perm-actions">
                          <button type="button" class="btn-mini" (click)="selectAllReports()">Tous</button>
                          <button type="button" class="btn-mini" (click)="deselectAllReports()">Aucun</button>
                        </div>
                      </div>
                      <div class="sub-perm-grid">
                        <label class="sub-perm-item">
                          <input type="checkbox" [(ngModel)]="userForm.canReportTrips">
                          <span>🛣️ Trajets</span>
                        </label>
                        <label class="sub-perm-item">
                          <input type="checkbox" [(ngModel)]="userForm.canReportStops">
                          <span>🅿️ Arrêts</span>
                        </label>
                        <label class="sub-perm-item">
                          <input type="checkbox" [(ngModel)]="userForm.canReportMileage">
                          <span>📏 Kilométrique</span>
                        </label>
                        <label class="sub-perm-item">
                          <input type="checkbox" [(ngModel)]="userForm.canReportMileagePeriod">
                          <span>📈 Km par période</span>
                        </label>
                        <label class="sub-perm-item">
                          <input type="checkbox" [(ngModel)]="userForm.canReportDaily">
                          <span>📅 Journalier</span>
                        </label>
                        <label class="sub-perm-item">
                          <input type="checkbox" [(ngModel)]="userForm.canReportSpeed">
                          <span>🏎️ Vitesse</span>
                        </label>
                        <label class="sub-perm-item">
                          <input type="checkbox" [(ngModel)]="userForm.canReportSpeedInfraction">
                          <span>⚠️ Infractions vitesse</span>
                        </label>
                        <label class="sub-perm-item">
                          <input type="checkbox" [(ngModel)]="userForm.canReportDrivingBehavior">
                          <span>🚦 Comportement</span>
                        </label>
                        <label class="sub-perm-item">
                          <input type="checkbox" [(ngModel)]="userForm.canReportFuel">
                          <span>⛽ Carburant</span>
                        </label>
                        <label class="sub-perm-item">
                          <input type="checkbox" [(ngModel)]="userForm.canReportCosts">
                          <span>🔩 Réparations</span>
                        </label>
                        <label class="sub-perm-item">
                          <input type="checkbox" [(ngModel)]="userForm.canReportMaintenance">
                          <span>🔧 Maintenance</span>
                        </label>
                        <label class="sub-perm-item">
                          <input type="checkbox" [(ngModel)]="userForm.canReportMonthly">
                          <span>📊 Mensuel flotte</span>
                        </label>
                        <label class="sub-perm-item">
                          <input type="checkbox" [(ngModel)]="userForm.canReportMonthlyCosts">
                          <span>💰 Coûts mensuel</span>
                        </label>
                      </div>
                    </div>
                    <label class="perm-check">
                      <input type="checkbox" [(ngModel)]="userForm.canGeofences">
                      <span class="perm-label">🗺️ Géofences</span>
                      <span class="perm-desc">Zones géographiques</span>
                    </label>
                    <label class="perm-check">
                      <input type="checkbox" [(ngModel)]="userForm.canMaintenance">
                      <span class="perm-label">🔧 Maintenance</span>
                      <span class="perm-desc">Entretiens et réparations</span>
                    </label>
                    <label class="perm-check">
                      <input type="checkbox" [(ngModel)]="userForm.canCosts">
                      <span class="perm-label">💰 Dépenses</span>
                      <span class="perm-desc">Coûts et dépenses</span>
                    </label>
                    <label class="perm-check">
                      <input type="checkbox" [(ngModel)]="userForm.canFuel">
                      <span class="perm-label">⛽ Carburant</span>
                      <span class="perm-desc">Gestion du carburant</span>
                    </label>
                    <label class="perm-check">
                      <input type="checkbox" [(ngModel)]="userForm.canDocuments">
                      <span class="perm-label">📄 Documents</span>
                      <span class="perm-desc">Échéances et documents</span>
                    </label>
                    <label class="perm-check">
                      <input type="checkbox" [(ngModel)]="userForm.canAccidents">
                      <span class="perm-label">⚠️ Sinistres</span>
                      <span class="perm-desc">Déclaration d'accidents</span>
                    </label>
                    <label class="perm-check">
                      <input type="checkbox" [(ngModel)]="userForm.canSuppliers">
                      <span class="perm-label">🏪 Fournisseurs</span>
                      <span class="perm-desc">Garages et prestataires</span>
                    </label>
                    <label class="perm-check">
                      <input type="checkbox" [(ngModel)]="userForm.canFleetManagement">
                      <span class="perm-label">🚛 Gestion flotte</span>
                      <span class="perm-desc">Configuration avancée du parc</span>
                    </label>
                    <label class="perm-check">
                      <input type="checkbox" [(ngModel)]="userForm.canTours">
                      <span class="perm-label">🗺️ Tournées</span>
                      <span class="perm-desc">Gestion des tournées</span>
                    </label>
                    <label class="perm-check">
                      <input type="checkbox" [(ngModel)]="userForm.canPlayback">
                      <span class="perm-label">⏪ Tracer Playback</span>
                      <span class="perm-desc">Lecture historique des trajets</span>
                    </label>
                    <label class="perm-check perm-critical">
                      <input type="checkbox" [(ngModel)]="userForm.canUsers">
                      <span class="perm-label">👥 Gestion utilisateurs</span>
                      <span class="perm-desc">Créer/modifier des utilisateurs</span>
                    </label>
                    <label class="perm-check perm-critical">
                      <input type="checkbox" [(ngModel)]="userForm.canSettings">
                      <span class="perm-label">⚙️ Paramètres</span>
                      <span class="perm-desc">Configuration de l'application</span>
                    </label>
                  </div>
                  <div class="perm-actions">
                    <button class="btn-text" (click)="selectAllPermissions()">Tout cocher</button>
                    <button class="btn-text" (click)="deselectAllPermissions()">Tout décocher</button>
                  </div>
                </div>
              </div>

              <!-- Step 3: Véhicules -->
              <div *ngIf="userModalStep === 3">
                <div class="form-section">
                  <h3 class="section-title">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="1" y="3" width="15" height="13"/><polygon points="16 8 20 8 23 11 23 16 16 16 16 8"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/></svg>
                    Véhicules assignés
                  </h3>
                  <p class="section-hint">Sélectionnez les véhicules que cet utilisateur pourra superviser. Les administrateurs ont accès à tous les véhicules.</p>
                  <div class="vehicle-checkboxes">
                    <label class="vehicle-check" *ngFor="let v of availableVehicles">
                      <input type="checkbox" [checked]="isVehicleSelected(v.id)" (change)="toggleVehicle(v.id)">
                      <span class="check-label">{{ v.name }} <span class="plate-tag">{{ v.plate }}</span></span>
                    </label>
                    <div class="empty-hint" *ngIf="availableVehicles.length === 0">Aucun véhicule disponible</div>
                  </div>
                </div>
              </div>
            </div>

            <div class="modal-footer">
              <button class="btn-secondary" *ngIf="userModalStep > 1" (click)="prevUserStep()">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="15 18 9 12 15 6"/></svg>
                Précédent
              </button>
              <div style="flex:1"></div>
              <button class="btn-secondary" (click)="closeUserModal()">Annuler</button>
              <button class="btn-primary" *ngIf="hasNextUserStep()" (click)="nextUserStep()">
                Suivant
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="9 18 15 12 9 6"/></svg>
              </button>
              <button class="btn-primary" *ngIf="!hasNextUserStep()" (click)="saveUser()">
                {{ editingUser ? 'Enregistrer' : 'Créer' }}
              </button>
            </div>
          </div>
        </div>

      </div>
    </app-layout>
  `,
  styles: [`
    .user-management-page {
      padding: 24px;
      width: 100%;
    }

    .page-header {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      margin-bottom: 24px;
    }

    .header-left h1 {
      font-size: 24px;
      font-weight: 700;
      color: #1f2937;
      margin: 0 0 4px 0;
    }

    .subtitle {
      color: #6b7280;
      font-size: 14px;
      margin: 0;
    }

    .btn-primary {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 20px;
      background: #6366f1;
      color: white;
      border: none;
      border-radius: 8px;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
      transition: background 0.2s;
    }

    .btn-primary:hover {
      background: #4f46e5;
    }

    .btn-secondary {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 20px;
      background: white;
      color: #374151;
      border: 1px solid #d1d5db;
      border-radius: 8px;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
    }

    .btn-secondary:hover {
      background: #f9fafb;
    }

    .header-actions {
      display: flex;
      gap: 12px;
    }

    /* Tabs */
    .tabs {
      display: flex;
      gap: 8px;
      margin-bottom: 24px;
      background: #f3f4f6;
      padding: 4px;
      border-radius: 10px;
      width: fit-content;
    }

    .tab {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 20px;
      background: transparent;
      border: none;
      border-radius: 8px;
      font-size: 14px;
      font-weight: 500;
      color: #6b7280;
      cursor: pointer;
      transition: all 0.2s;
    }

    .tab:hover {
      color: #374151;
    }

    .tab.active {
      background: white;
      color: #1f2937;
      box-shadow: 0 1px 3px rgba(0,0,0,0.1);
    }

    /* Stats Row */
    .stats-row {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 16px;
      margin-bottom: 24px;
    }

    .stat-card {
      background: white;
      border-radius: 12px;
      border: 1px solid #e5e7eb;
      padding: 20px;
      display: flex;
      align-items: center;
      gap: 16px;
    }

    .stat-icon {
      width: 48px;
      height: 48px;
      border-radius: 12px;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .stat-icon.users { background: #e0e7ff; color: #4f46e5; }
    .stat-icon.active { background: #d1fae5; color: #059669; }
    .stat-icon.drivers { background: #fef3c7; color: #d97706; }
    .stat-icon.supervisors { background: #dbeafe; color: #2563eb; }

    .stat-info {
      display: flex;
      flex-direction: column;
    }

    .stat-value {
      font-size: 24px;
      font-weight: 700;
      color: #1f2937;
    }

    .stat-label {
      font-size: 13px;
      color: #6b7280;
    }

    /* Users Card */
    .users-card {
      background: white;
      border-radius: 12px;
      border: 1px solid #e5e7eb;
      overflow: hidden;
    }

    .card-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 16px 20px;
      border-bottom: 1px solid #e5e7eb;
      background: #f9fafb;
    }

    .search-box {
      display: flex;
      align-items: center;
      gap: 8px;
      background: white;
      border: 1px solid #d1d5db;
      border-radius: 8px;
      padding: 8px 12px;
      width: 300px;
    }

    .search-box svg {
      color: #9ca3af;
    }

    .search-box input {
      border: none;
      outline: none;
      flex: 1;
      font-size: 14px;
    }

    .filter-group {
      display: flex;
      gap: 12px;
    }

    .filter-group select {
      padding: 8px 12px;
      border: 1px solid #d1d5db;
      border-radius: 8px;
      font-size: 14px;
      background: white;
    }

    /* Table */
    .table-container {
      overflow-x: auto;
    }

    .users-table {
      width: 100%;
      border-collapse: collapse;
    }

    .users-table th,
    .users-table td {
      padding: 16px 20px;
      text-align: left;
      border-bottom: 1px solid #f3f4f6;
    }

    .users-table th {
      font-size: 12px;
      font-weight: 600;
      color: #6b7280;
      text-transform: uppercase;
      letter-spacing: 0.5px;
      background: #fafafa;
    }

    .users-table tr:hover {
      background: #f9fafb;
    }

    .user-info {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .user-avatar {
      width: 40px;
      height: 40px;
      border-radius: 10px;
      background: linear-gradient(135deg, #6366f1 0%, #8b5cf6 100%);
      color: white;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 14px;
      font-weight: 600;
    }

    .user-details {
      display: flex;
      flex-direction: column;
    }

    .user-name {
      font-weight: 500;
      color: #1f2937;
    }

    .user-email {
      font-size: 12px;
      color: #6b7280;
    }

    .roles-list {
      display: flex;
      gap: 6px;
      flex-wrap: wrap;
    }

    .role-badge {
      padding: 4px 10px;
      border-radius: 20px;
      font-size: 11px;
      font-weight: 600;
      background: #e0e7ff;
      color: #3730a3;
    }

    .role-badge.admin {
      background: #fef3c7;
      color: #92400e;
    }

    .type-badge {
      padding: 4px 10px;
      border-radius: 20px;
      font-size: 11px;
      font-weight: 600;
      background: #f3f4f6;
      color: #6b7280;
    }

    .type-badge.admin {
      background: #fef3c7;
      color: #92400e;
    }

    .type-badge.system {
      background: #dbeafe;
      color: #1e40af;
    }

    .role-info .role-name {
      font-weight: 500;
      color: #1f2937;
    }

    .vehicles-count {
      display: flex;
      align-items: center;
      gap: 6px;
      color: #6b7280;
      font-size: 13px;
    }

    .permissions-count {
      font-size: 13px;
      color: #6b7280;
    }

    .status-badge {
      padding: 4px 12px;
      border-radius: 20px;
      font-size: 12px;
      font-weight: 500;
    }

    .status-badge.active {
      background: #d1fae5;
      color: #065f46;
    }

    .status-badge.inactive {
      background: #fee2e2;
      color: #991b1b;
    }

    .action-buttons {
      display: flex;
      gap: 8px;
    }

    .btn-icon {
      width: 32px;
      height: 32px;
      border: none;
      background: #f3f4f6;
      border-radius: 6px;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      color: #6b7280;
      transition: all 0.2s;
    }

    .btn-icon:hover {
      background: #e5e7eb;
      color: #1f2937;
    }

    .btn-icon.danger:hover {
      background: #fee2e2;
      color: #dc2626;
    }

    .empty-state {
      padding: 48px;
      text-align: center;
      color: #9ca3af;
    }

    .empty-state svg {
      margin-bottom: 12px;
    }

    /* Modal */
    .modal-overlay {
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(0, 0, 0, 0.5);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
      padding: 20px;
    }

    .modal-content {
      background: white;
      border-radius: 16px;
      width: 100%;
      max-width: 700px;
      max-height: 90vh;
      display: flex;
      flex-direction: column;
    }

    .modal-content.modal-sm {
      max-width: 450px;
    }

    .modal-content.modal-lg {
      max-width: 800px;
    }

    .permissions-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 10px;
    }

    .permission-checkbox {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 12px;
      border: 1px solid #e5e7eb;
      border-radius: 8px;
      cursor: pointer;
      transition: all 0.2s;
      position: relative;
    }

    .permission-checkbox input {
      display: none;
    }

    .permission-checkbox:hover {
      border-color: #6366f1;
    }

    .permission-checkbox.selected {
      border-color: #6366f1;
      background: #eef2ff;
    }

    .permission-checkbox .perm-name {
      font-size: 13px;
      color: #374151;
      flex: 1;
    }

    .permission-checkbox .check-icon {
      display: none;
      color: #6366f1;
    }

    .permission-checkbox.selected .check-icon {
      display: block;
    }

    .admin-notice {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 16px;
      background: #fef3c7;
      border-radius: 8px;
      color: #92400e;
      font-size: 13px;
      margin-top: 16px;
    }

    .admin-notice svg {
      flex-shrink: 0;
    }

    /* Permission Categories */
    .permission-category {
      border: 1px solid #e5e7eb;
      border-radius: 12px;
      padding: 16px;
      margin-bottom: 16px;
    }

    .permission-category.critical {
      border-color: #fbbf24;
      background: #fffbeb;
    }

    .category-header {
      display: flex;
      align-items: flex-start;
      gap: 12px;
      margin-bottom: 16px;
      padding-bottom: 12px;
      border-bottom: 1px solid #e5e7eb;
    }

    .category-icon {
      font-size: 24px;
      line-height: 1;
    }

    .category-info h3 {
      margin: 0 0 4px 0;
      font-size: 15px;
      font-weight: 600;
      color: #1f2937;
    }

    .category-desc {
      margin: 0;
      font-size: 12px;
      color: #6b7280;
    }

    .permissions-list {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .permission-item {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px;
      border: 1px solid #e5e7eb;
      border-radius: 8px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .permission-item input {
      display: none;
    }

    .permission-item:hover {
      border-color: #6366f1;
      background: #f9fafb;
    }

    .permission-item.selected {
      border-color: #6366f1;
      background: #eef2ff;
    }

    .permission-item.critical-perm {
      border-color: #fbbf24;
    }

    .permission-item.critical-perm.selected {
      background: #fef3c7;
      border-color: #f59e0b;
    }

    .perm-content {
      flex: 1;
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .perm-label {
      font-size: 14px;
      font-weight: 500;
      color: #1f2937;
    }

    .perm-desc {
      font-size: 12px;
      color: #6b7280;
    }

    .permission-item .check-icon {
      display: none;
      color: #6366f1;
      flex-shrink: 0;
    }

    .permission-item.selected .check-icon {
      display: block;
    }

    .permission-item.compact {
      padding: 10px 12px;
    }

    .reports-list {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 8px;
    }

    .reports-list .perm-desc {
      font-size: 11px;
    }

    .permission-category.disabled {
      opacity: 0.6;
      background: #f9fafb;
    }

    .category-desc.warning {
      color: #d97706;
      font-weight: 500;
    }

    .disabled-overlay {
      padding: 20px;
      text-align: center;
      color: #6b7280;
      font-size: 13px;
      background: #f3f4f6;
      border-radius: 8px;
    }

    .disabled-overlay p {
      margin: 0;
    }

    @media (max-width: 768px) {
      .reports-list {
        grid-template-columns: 1fr;
      }
    }

    .checkbox-group {
      margin-top: 8px;
    }

    .checkbox-label {
      display: flex;
      align-items: center;
      gap: 10px;
      cursor: pointer;
      font-weight: 500;
    }

    .checkbox-label input[type="checkbox"] {
      width: 18px;
      height: 18px;
      cursor: pointer;
    }

    .hint {
      font-size: 12px;
      color: #6b7280;
      margin: 4px 0 0 28px;
    }

    .btn-icon:disabled {
      opacity: 0.4;
      cursor: not-allowed;
    }

    .btn-icon:disabled:hover {
      background: #f3f4f6;
      color: #6b7280;
    }

    .step-tabs {
      display: flex;
      border-bottom: 1px solid #e5e7eb;
      background: #f9fafb;
      padding: 0 24px;
      gap: 0;
    }

    .step-tab {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 12px 16px;
      border: none;
      background: none;
      font-size: 13px;
      font-weight: 500;
      color: #9ca3af;
      cursor: pointer;
      border-bottom: 2px solid transparent;
      transition: all 0.15s;
      white-space: nowrap;
    }

    .step-tab:hover {
      color: #6b7280;
    }

    .step-tab.active {
      color: #6366f1;
      border-bottom-color: #6366f1;
    }

    .step-num {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 20px;
      height: 20px;
      border-radius: 50%;
      background: #e5e7eb;
      color: #6b7280;
      font-size: 11px;
      font-weight: 600;
    }

    .step-tab.active .step-num {
      background: #6366f1;
      color: white;
    }

    .modal-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 20px 24px;
      border-bottom: 1px solid #e5e7eb;
    }

    .modal-header h2 {
      margin: 0;
      font-size: 18px;
      font-weight: 600;
      color: #1f2937;
    }

    .btn-close {
      width: 32px;
      height: 32px;
      border: none;
      background: #f3f4f6;
      border-radius: 8px;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      color: #6b7280;
    }

    .btn-close:hover {
      background: #e5e7eb;
    }

    .modal-body {
      padding: 24px;
      overflow-y: auto;
      flex: 1;
    }

    .form-section {
      margin-bottom: 28px;
    }

    .form-section:last-child {
      margin-bottom: 0;
    }

    .form-section h3 {
      font-size: 15px;
      font-weight: 600;
      color: #1f2937;
      margin: 0 0 4px 0;
    }

    .section-desc {
      font-size: 13px;
      color: #6b7280;
      margin: 0 0 16px 0;
    }

    .form-grid {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 16px;
    }

    .form-group {
      display: flex;
      flex-direction: column;
      gap: 6px;
    }

    .form-group label {
      font-size: 13px;
      font-weight: 500;
      color: #374151;
    }

    .form-group input,
    .form-group select {
      padding: 10px 12px;
      border: 1px solid #d1d5db;
      border-radius: 8px;
      font-size: 14px;
    }

    .form-group input:focus,
    .form-group select:focus {
      outline: none;
      border-color: #6366f1;
      box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.1);
    }

    /* Roles Grid */
    .roles-grid {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 12px;
    }

    .role-checkbox {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 16px;
      border: 2px solid #e5e7eb;
      border-radius: 12px;
      cursor: pointer;
      transition: all 0.2s;
      position: relative;
    }

    .role-checkbox input {
      display: none;
    }

    .role-checkbox.selected {
      border-color: #6366f1;
      background: #f0f0ff;
    }

    .role-content {
      display: flex;
      align-items: center;
      gap: 12px;
      flex: 1;
    }

    .role-icon {
      width: 40px;
      height: 40px;
      border-radius: 10px;
      background: #f3f4f6;
      display: flex;
      align-items: center;
      justify-content: center;
      color: #6b7280;
    }

    .role-checkbox.selected .role-icon {
      background: #e0e7ff;
      color: #4f46e5;
    }

    .role-name {
      font-weight: 500;
      color: #1f2937;
    }

    .check-icon {
      display: none;
      color: #6366f1;
    }

    .role-checkbox.selected .check-icon {
      display: block;
    }

    /* Permissions Grid */
    .permissions-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 10px;
    }

    .permission-checkbox {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 12px;
      border: 1px solid #e5e7eb;
      border-radius: 8px;
      cursor: pointer;
      transition: all 0.2s;
      font-size: 13px;
    }

    .permission-checkbox input {
      display: none;
    }

    .permission-checkbox.selected {
      border-color: #6366f1;
      background: #f0f0ff;
    }

    .perm-icon {
      color: #6b7280;
    }

    .permission-checkbox.selected .perm-icon {
      color: #6366f1;
    }

    .perm-name {
      flex: 1;
      color: #374151;
    }

    .permission-checkbox .check-icon {
      display: none;
      color: #6366f1;
    }

    .permission-checkbox.selected .check-icon {
      display: block;
    }

    .permission-actions {
      display: flex;
      gap: 16px;
      margin-top: 12px;
    }

    .btn-link {
      background: none;
      border: none;
      color: #6366f1;
      font-size: 13px;
      cursor: pointer;
      padding: 0;
    }

    .btn-link:hover {
      text-decoration: underline;
    }

    /* Vehicles Grid */
    .vehicles-grid {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 10px;
      max-height: 200px;
      overflow-y: auto;
    }

    .vehicle-checkbox {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 12px;
      border: 1px solid #e5e7eb;
      border-radius: 8px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .vehicle-checkbox input {
      display: none;
    }

    .vehicle-checkbox.selected {
      border-color: #6366f1;
      background: #f0f0ff;
    }

    .vehicle-info {
      display: flex;
      align-items: center;
      gap: 10px;
      flex: 1;
    }

    .vehicle-icon {
      font-size: 20px;
    }

    .vehicle-details {
      display: flex;
      flex-direction: column;
    }

    .vehicle-name {
      font-size: 13px;
      font-weight: 500;
      color: #1f2937;
    }

    .vehicle-plate {
      font-size: 11px;
      color: #6b7280;
    }

    .vehicle-checkbox .check-icon {
      display: none;
      color: #6366f1;
    }

    .vehicle-checkbox.selected .check-icon {
      display: block;
    }

    /* Vehicle Assignment */
    .section-title { display:flex; align-items:center; gap:8px; font-size:14px; font-weight:600; color:#1e293b; margin:0 0 4px; }
    .section-hint { font-size:12px; color:#94a3b8; margin:0 0 12px; }
    .vehicle-checkboxes { display:grid; grid-template-columns:repeat(auto-fill,minmax(220px,1fr)); gap:8px; max-height:200px; overflow-y:auto; padding:4px 0; }
    .vehicle-check { display:flex; align-items:center; gap:8px; padding:8px 12px; background:#f8fafc; border:1px solid #e2e8f0; border-radius:6px; cursor:pointer; font-size:13px; transition:all .15s; }
    .vehicle-check:hover { border-color:#3b82f6; background:#eff6ff; }
    .vehicle-check input[type="checkbox"] { accent-color:#3b82f6; width:16px; height:16px; }
    .check-label { flex:1; }
    .plate-tag { display:inline-block; background:#e2e8f0; color:#475569; padding:1px 6px; border-radius:3px; font-size:11px; font-weight:500; margin-left:4px; }
    .empty-hint { font-size:12px; color:#94a3b8; padding:12px; text-align:center; }

    /* Permissions Grid */
    .permissions-grid { display:grid; grid-template-columns:repeat(auto-fill,minmax(240px,1fr)); gap:8px; max-height:320px; overflow-y:auto; padding:4px 0; }
    .perm-check { display:flex; flex-wrap:wrap; align-items:center; gap:6px; padding:10px 12px; background:#f8fafc; border:1px solid #e2e8f0; border-radius:8px; cursor:pointer; transition:all .15s; }
    .perm-check:hover { border-color:#6366f1; background:#f0f0ff; }
    .perm-check input[type="checkbox"] { accent-color:#6366f1; width:16px; height:16px; }
    .perm-label { font-size:13px; font-weight:600; color:#1e293b; flex:1; min-width:120px; }
    .perm-desc { font-size:11px; color:#94a3b8; width:100%; padding-left:22px; }
    .perm-check.perm-critical { border-color:#fecaca; background:#fff5f5; }
    .perm-check.perm-critical:hover { border-color:#f87171; background:#fef2f2; }
    .perm-actions { display:flex; gap:12px; margin-top:12px; }
    .btn-text { background:none; border:none; color:#6366f1; font-size:12px; font-weight:500; cursor:pointer; padding:4px 8px; border-radius:4px; }
    .btn-text:hover { background:#f0f0ff; }

    /* Report sub-permissions accordion */
    .report-sub-permissions {
      grid-column: 1 / -1;
      background: #f0f4ff;
      border: 1px solid #c7d2fe;
      border-radius: 8px;
      padding: 12px;
      animation: slideDown 0.2s ease-out;
    }
    @keyframes slideDown {
      from { opacity: 0; max-height: 0; transform: translateY(-8px); }
      to { opacity: 1; max-height: 500px; transform: translateY(0); }
    }
    .sub-perm-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 10px;
    }
    .sub-perm-title {
      font-size: 12px;
      font-weight: 600;
      color: #4338ca;
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }
    .sub-perm-actions { display: flex; gap: 6px; }
    .btn-mini {
      background: #e0e7ff;
      border: 1px solid #a5b4fc;
      color: #4338ca;
      font-size: 11px;
      font-weight: 500;
      cursor: pointer;
      padding: 2px 8px;
      border-radius: 4px;
      transition: all 0.15s;
    }
    .btn-mini:hover { background: #c7d2fe; }
    .sub-perm-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(160px, 1fr));
      gap: 6px;
    }
    .sub-perm-item {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 5px 8px;
      background: #fff;
      border: 1px solid #e0e7ff;
      border-radius: 6px;
      cursor: pointer;
      font-size: 12px;
      color: #334155;
      transition: all 0.15s;
    }
    .sub-perm-item:hover { border-color: #818cf8; background: #f5f3ff; }
    .sub-perm-item input[type="checkbox"] { accent-color: #6366f1; width: 14px; height: 14px; cursor: pointer; }

    /* Modal Footer */
    .modal-footer {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 16px 24px;
      border-top: 1px solid #e5e7eb;
      background: #f9fafb;
    }

    .btn-secondary {
      padding: 10px 20px;
      background: white;
      color: #374151;
      border: 1px solid #d1d5db;
      border-radius: 8px;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
    }

    .btn-secondary:hover {
      background: #f9fafb;
    }

    /* Responsive */
    @media (max-width: 900px) {
      .stats-row {
        grid-template-columns: repeat(2, 1fr);
      }

      .permissions-grid {
        grid-template-columns: repeat(2, 1fr);
      }
    }

    @media (max-width: 600px) {
      .stats-row {
        grid-template-columns: 1fr;
      }

      .card-header {
        flex-direction: column;
        gap: 12px;
      }

      .search-box {
        width: 100%;
      }

      .filter-group {
        width: 100%;
      }

      .filter-group select {
        flex: 1;
      }

      .form-grid,
      .roles-grid,
      .vehicles-grid {
        grid-template-columns: 1fr;
      }

      .permissions-grid {
        grid-template-columns: 1fr;
      }
    }
  `]
})
export class UserManagementComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  // Data
  users: User[] = [];
  filteredUsers: User[] = [];
  roles: Role[] = [];
  loading = false;

  // Subscription permissions
  subscriptionPermissions: any = null;
  
  // Available modules/reports from subscription
  availableModules: { key: string; label: string; enabled: boolean }[] = [];
  availableReports: { key: string; label: string; enabled: boolean }[] = [];

  // Filters
  searchQuery = '';
  filterRoleId: number | null = null;
  filterStatus = '';

  // Active tab
  activeTab: 'users' | 'roles' = 'users';

  // User Modal
  showUserModal = false;
  editingUser: User | null = null;
  userModalStep = 1;
  userForm = {
    firstName: '',
    lastName: '',
    email: '',
    phone: '',
    password: '',
    roleId: 0,
    status: 'active',
    assignedVehicleIds: [] as number[],
    canMonitoring: true,
    canVehicles: true,
    canDrivers: false,
    canReports: false,
    canGeofences: false,
    canMaintenance: false,
    canCosts: false,
    canFuel: false,
    canDocuments: false,
    canAccidents: false,
    canUsers: false,
    canSettings: false,
    canSuppliers: false,
    canFleetManagement: false,
    canTours: false,
    canPlayback: false,
    // Per-report permissions (default true = all reports accessible when canReports is on)
    canReportTrips: true,
    canReportFuel: true,
    canReportSpeed: true,
    canReportStops: true,
    canReportMileage: true,
    canReportCosts: true,
    canReportMaintenance: true,
    canReportDaily: true,
    canReportMonthly: true,
    canReportMileagePeriod: true,
    canReportSpeedInfraction: true,
    canReportDrivingBehavior: true,
    canReportMonthlyCosts: true
  };
  availableVehicles: VehicleOption[] = [];

  // Role Modal
  showRoleModal = false;
  editingRole: Role | null = null;
  roleForm = {
    name: '',
    description: '',
    isCompanyAdmin: false,
    modules: {} as Record<string, boolean>,
    reports: {} as Record<string, boolean>
  };

  // Permissions organized by category with descriptions
  permissionCategories = [
    {
      name: 'Suivi GPS',
      description: 'Accès aux fonctionnalités de localisation et tracking en temps réel',
      icon: '📍',
      permissions: [
        { key: 'moduleDashboard', label: 'Tableau de bord', desc: 'Vue d\'ensemble avec statistiques et alertes' },
        { key: 'moduleMonitoring', label: 'Monitoring GPS', desc: 'Suivi en temps réel des véhicules sur carte' },
        { key: 'moduleGeofences', label: 'Zones géographiques', desc: 'Création et gestion des zones de contrôle' }
      ]
    },
    {
      name: 'Gestion du parc',
      description: 'Administration des véhicules, employés et fournisseurs',
      icon: '🚗',
      permissions: [
        { key: 'moduleVehicles', label: 'Véhicules', desc: 'Gestion du parc automobile (ajout, modification, suppression)' },
        { key: 'moduleEmployees', label: 'Employés', desc: '⚠️ CRITIQUE - Gestion des chauffeurs et personnel' },
        { key: 'moduleSuppliers', label: 'Fournisseurs/Garages', desc: 'Gestion des prestataires et garages partenaires' }
      ]
    },
    {
      name: 'Maintenance & Coûts',
      description: 'Suivi des entretiens, réparations et dépenses',
      icon: '🔧',
      permissions: [
        { key: 'moduleMaintenance', label: 'Maintenance', desc: 'Entretiens programmés et réparations' },
        { key: 'moduleCosts', label: 'Dépenses & Carburant', desc: 'Suivi des coûts, pleins carburant, dépenses' }
      ]
    },
    {
      name: 'Documents & Sinistres',
      description: 'Gestion documentaire et déclarations d\'accidents',
      icon: '📄',
      permissions: [
        { key: 'moduleDocuments', label: 'Échéances', desc: 'Documents administratifs et leurs dates d\'expiration' },
        { key: 'moduleAccidents', label: 'Sinistres', desc: 'Déclaration et suivi des accidents' }
      ]
    },
    {
      name: 'Rapports & Analyse',
      description: 'Génération de rapports et analyse des données',
      icon: '📊',
      permissions: [
        { key: 'moduleReports', label: 'Rapports', desc: 'Accès à tous les rapports d\'activité' }
      ]
    },
    {
      name: 'Administration',
      description: '⚠️ Fonctions sensibles réservées aux responsables',
      icon: '⚙️',
      critical: true,
      permissions: [
        { key: 'moduleUsers', label: 'Gestion utilisateurs', desc: '⚠️ CRITIQUE - Créer/modifier/supprimer des utilisateurs et rôles' },
        { key: 'moduleFleetManagement', label: 'Gestion de flotte', desc: '⚠️ CRITIQUE - Configuration avancée du parc' },
        { key: 'moduleSettings', label: 'Paramètres', desc: 'Configuration générale de l\'application' }
      ]
    }
  ];

  // Report permissions with descriptions
  reportPermissions = [
    { key: 'reportTrips', label: 'Trajets', desc: 'Historique des trajets effectués' },
    { key: 'reportFuel', label: 'Carburant', desc: 'Consommation et pleins de carburant' },
    { key: 'reportSpeed', label: 'Vitesse', desc: 'Analyse des vitesses' },
    { key: 'reportStops', label: 'Arrêts', desc: 'Durée et localisation des arrêts' },
    { key: 'reportMileage', label: 'Kilométrage', desc: 'Distances parcourues' },
    { key: 'reportCosts', label: 'Coûts', desc: 'Analyse des dépenses' },
    { key: 'reportMaintenance', label: 'Maintenance', desc: 'Historique des entretiens' },
    { key: 'reportDaily', label: 'Quotidien', desc: 'Rapport journalier d\'activité' },
    { key: 'reportMonthly', label: 'Mensuel', desc: 'Synthèse mensuelle' },
    { key: 'reportSpeedInfraction', label: 'Infractions vitesse', desc: 'Dépassements de vitesse' },
    { key: 'reportDrivingBehavior', label: 'Comportement conduite', desc: 'Analyse du style de conduite' }
  ];

  // Legacy labels for compatibility
  moduleLabels: Record<string, string> = {};
  reportLabels: Record<string, string> = {};

  constructor(
    private router: Router,
    private apiService: ApiService,
    private toast: ToastService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit() {
    if (!this.apiService.isAuthenticated()) {
      this.router.navigate(['/login']);
      return;
    }
    // Build legacy labels from categories
    this.permissionCategories.forEach(cat => {
      cat.permissions.forEach(p => {
        this.moduleLabels[p.key] = p.label;
      });
    });
    this.reportPermissions.forEach(r => {
      this.reportLabels[r.key] = r.label;
    });
    this.loadData();
  }

  loadData() {
    this.loading = true;
    this.loadSubscriptionPermissions();
    this.loadRoles();
    this.loadUsers();
  }

  loadSubscriptionPermissions() {
    this.apiService.getCurrentSubscription().pipe(takeUntil(this.destroy$)).subscribe({
      next: (data) => {
        console.log('Subscription data received:', data);
        const sub = data.subscriptionType || data.SubscriptionType;
        if (sub) {
          this.subscriptionPermissions = sub;
          this.buildAvailablePermissions(sub);
        } else {
          // If no subscription data, use all modules as available
          this.buildDefaultPermissions();
        }
      },
      error: (err) => {
        console.error('Error loading subscription:', err);
        // Fallback to default permissions
        this.buildDefaultPermissions();
      }
    });
  }

  buildDefaultPermissions() {
    // Use all modules as available by default
    this.availableModules = Object.keys(this.moduleLabels).map(key => ({
      key,
      label: this.moduleLabels[key],
      enabled: true
    }));
    this.availableReports = Object.keys(this.reportLabels).map(key => ({
      key,
      label: this.reportLabels[key],
      enabled: true
    }));
    console.log('Using default permissions - modules:', this.availableModules.length);
    this.cdr.detectChanges();
  }

  buildAvailablePermissions(sub: any) {
    this.availableModules = [];
    this.availableReports = [];

    console.log('Building permissions from subscription:', sub);

    // Helper to check both camelCase and PascalCase
    const getValue = (obj: any, key: string): boolean => {
      // Try camelCase first (moduleDashboard)
      if (obj[key] === true) return true;
      // Try PascalCase (ModuleDashboard)
      const pascalKey = key.charAt(0).toUpperCase() + key.slice(1);
      if (obj[pascalKey] === true) return true;
      return false;
    };

    // Build modules list from subscription
    Object.keys(this.moduleLabels).forEach(key => {
      const enabled = getValue(sub, key);
      if (enabled) {
        this.availableModules.push({
          key,
          label: this.moduleLabels[key],
          enabled
        });
      }
    });

    // Build reports list from subscription
    Object.keys(this.reportLabels).forEach(key => {
      const enabled = getValue(sub, key);
      if (enabled) {
        this.availableReports.push({
          key,
          label: this.reportLabels[key],
          enabled
        });
      }
    });

    console.log('Available modules:', this.availableModules);
    console.log('Available reports:', this.availableReports);
    this.cdr.detectChanges();
  }

  loadRoles() {
    this.apiService.getRoles(false).pipe(takeUntil(this.destroy$)).subscribe({
      next: (roles) => {
        this.roles = roles;
        this.cdr.detectChanges();
      },
      error: (err) => {
        console.error('Error loading roles:', err);
        this.toast.error('Erreur', 'Erreur lors du chargement des rôles');
      }
    });
  }

  loadUsers() {
    this.apiService.getUsers().pipe(takeUntil(this.destroy$)).subscribe({
      next: (users) => {
        this.users = users;
        this.filterUsers();
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        console.error('Error loading users:', err);
        this.toast.error('Erreur', 'Erreur lors du chargement des utilisateurs');
        this.loading = false;
      }
    });
  }

  // ============ TAB SWITCHING ============

  setActiveTab(tab: 'users' | 'roles') {
    this.activeTab = tab;
  }

  // ============ USER MANAGEMENT ============

  filterUsers() {
    this.filteredUsers = this.users.filter(user => {
      const matchesSearch = !this.searchQuery || 
        user.name.toLowerCase().includes(this.searchQuery.toLowerCase()) ||
        user.email.toLowerCase().includes(this.searchQuery.toLowerCase());

      const matchesRole = !this.filterRoleId || user.roleId === this.filterRoleId;
      const matchesStatus = !this.filterStatus || user.status === this.filterStatus;

      return matchesSearch && matchesRole && matchesStatus;
    });
  }

  openUserModal(user?: User) {
    this.loadAvailableVehicles();
    if (user) {
      this.editingUser = user;
      const nameParts = user.name.split(' ');
      const up = user.userPermissions;
      this.userForm = {
        firstName: nameParts[0] || '',
        lastName: nameParts.slice(1).join(' ') || '',
        email: user.email,
        phone: user.phone || '',
        password: '',
        roleId: user.roleId,
        status: user.status,
        assignedVehicleIds: [...(user.assignedVehicleIds || [])],
        canMonitoring: up?.canMonitoring ?? true,
        canVehicles: up?.canVehicles ?? true,
        canDrivers: up?.canDrivers ?? false,
        canReports: up?.canReports ?? false,
        canGeofences: up?.canGeofences ?? false,
        canMaintenance: up?.canMaintenance ?? false,
        canCosts: up?.canCosts ?? false,
        canFuel: up?.canFuel ?? false,
        canDocuments: up?.canDocuments ?? false,
        canAccidents: up?.canAccidents ?? false,
        canUsers: up?.canUsers ?? false,
        canSettings: up?.canSettings ?? false,
        canSuppliers: up?.canSuppliers ?? false,
        canFleetManagement: up?.canFleetManagement ?? false,
        canTours: up?.canTours ?? false,
        canPlayback: up?.canPlayback ?? false,
        canReportTrips: up?.canReportTrips ?? true,
        canReportFuel: up?.canReportFuel ?? true,
        canReportSpeed: up?.canReportSpeed ?? true,
        canReportStops: up?.canReportStops ?? true,
        canReportMileage: up?.canReportMileage ?? true,
        canReportCosts: up?.canReportCosts ?? true,
        canReportMaintenance: up?.canReportMaintenance ?? true,
        canReportDaily: up?.canReportDaily ?? true,
        canReportMonthly: up?.canReportMonthly ?? true,
        canReportMileagePeriod: up?.canReportMileagePeriod ?? true,
        canReportSpeedInfraction: up?.canReportSpeedInfraction ?? true,
        canReportDrivingBehavior: up?.canReportDrivingBehavior ?? true,
        canReportMonthlyCosts: up?.canReportMonthlyCosts ?? true
      };
    } else {
      this.editingUser = null;
      this.userForm = {
        firstName: '',
        lastName: '',
        email: '',
        phone: '',
        password: '',
        roleId: this.roles.find(r => !r.isCompanyAdmin)?.id || (this.roles.length > 0 ? this.roles[0].id : 0),
        status: 'active',
        assignedVehicleIds: [],
        canMonitoring: true,
        canVehicles: true,
        canDrivers: false,
        canReports: false,
        canGeofences: false,
        canMaintenance: false,
        canCosts: false,
        canFuel: false,
        canDocuments: false,
        canAccidents: false,
        canUsers: false,
        canSettings: false,
        canSuppliers: false,
        canFleetManagement: false,
        canTours: false,
        canPlayback: false,
        canReportTrips: true,
        canReportFuel: true,
        canReportSpeed: true,
        canReportStops: true,
        canReportMileage: true,
        canReportCosts: true,
        canReportMaintenance: true,
        canReportDaily: true,
        canReportMonthly: true,
        canReportMileagePeriod: true,
        canReportSpeedInfraction: true,
        canReportDrivingBehavior: true,
        canReportMonthlyCosts: true
      };
    }
    this.userModalStep = 1;
    this.showUserModal = true;
  }

  closeUserModal() {
    this.showUserModal = false;
    this.editingUser = null;
    this.userModalStep = 1;
  }

  private getUserSteps(): number[] {
    return [1, 2, 3];
  }

  hasNextUserStep(): boolean {
    const steps = this.getUserSteps();
    const currentIdx = steps.indexOf(this.userModalStep);
    return currentIdx < steps.length - 1;
  }

  nextUserStep() {
    const steps = this.getUserSteps();
    const currentIdx = steps.indexOf(this.userModalStep);
    if (currentIdx < steps.length - 1) {
      this.userModalStep = steps[currentIdx + 1];
    }
  }

  prevUserStep() {
    const steps = this.getUserSteps();
    const currentIdx = steps.indexOf(this.userModalStep);
    if (currentIdx > 0) {
      this.userModalStep = steps[currentIdx - 1];
    }
  }

  selectAllPermissions() {
    this.userForm.canMonitoring = true;
    this.userForm.canVehicles = true;
    this.userForm.canDrivers = true;
    this.userForm.canReports = true;
    this.userForm.canGeofences = true;
    this.userForm.canMaintenance = true;
    this.userForm.canCosts = true;
    this.userForm.canFuel = true;
    this.userForm.canDocuments = true;
    this.userForm.canAccidents = true;
    this.userForm.canUsers = true;
    this.userForm.canSettings = true;
    this.userForm.canSuppliers = true;
    this.userForm.canFleetManagement = true;
    this.userForm.canTours = true;
    this.userForm.canPlayback = true;
    this.selectAllReports();
  }

  deselectAllPermissions() {
    this.userForm.canMonitoring = false;
    this.userForm.canVehicles = false;
    this.userForm.canDrivers = false;
    this.userForm.canReports = false;
    this.userForm.canGeofences = false;
    this.userForm.canMaintenance = false;
    this.userForm.canCosts = false;
    this.userForm.canFuel = false;
    this.userForm.canDocuments = false;
    this.userForm.canAccidents = false;
    this.userForm.canUsers = false;
    this.userForm.canSettings = false;
    this.userForm.canSuppliers = false;
    this.userForm.canFleetManagement = false;
    this.userForm.canTours = false;
    this.userForm.canPlayback = false;
    this.deselectAllReports();
  }

  selectAllReports() {
    this.userForm.canReportTrips = true;
    this.userForm.canReportFuel = true;
    this.userForm.canReportSpeed = true;
    this.userForm.canReportStops = true;
    this.userForm.canReportMileage = true;
    this.userForm.canReportCosts = true;
    this.userForm.canReportMaintenance = true;
    this.userForm.canReportDaily = true;
    this.userForm.canReportMonthly = true;
    this.userForm.canReportMileagePeriod = true;
    this.userForm.canReportSpeedInfraction = true;
    this.userForm.canReportDrivingBehavior = true;
    this.userForm.canReportMonthlyCosts = true;
  }

  deselectAllReports() {
    this.userForm.canReportTrips = false;
    this.userForm.canReportFuel = false;
    this.userForm.canReportSpeed = false;
    this.userForm.canReportStops = false;
    this.userForm.canReportMileage = false;
    this.userForm.canReportCosts = false;
    this.userForm.canReportMaintenance = false;
    this.userForm.canReportDaily = false;
    this.userForm.canReportMonthly = false;
    this.userForm.canReportMileagePeriod = false;
    this.userForm.canReportSpeedInfraction = false;
    this.userForm.canReportDrivingBehavior = false;
    this.userForm.canReportMonthlyCosts = false;
  }

  saveUser() {
    if (!this.userForm.firstName || !this.userForm.lastName || !this.userForm.email || !this.userForm.roleId) {
      this.toast.error('Erreur', 'Veuillez remplir tous les champs requis (Prénom, Nom, Email, Rôle)');
      return;
    }
    if (!this.editingUser && (!this.userForm.password || this.userForm.password.length < 6)) {
      this.toast.error('Erreur', 'Le mot de passe doit contenir au moins 6 caractères');
      return;
    }

    const permissionPayload = {
      canMonitoring: this.userForm.canMonitoring,
      canVehicles: this.userForm.canVehicles,
      canDrivers: this.userForm.canDrivers,
      canReports: this.userForm.canReports,
      canGeofences: this.userForm.canGeofences,
      canMaintenance: this.userForm.canMaintenance,
      canCosts: this.userForm.canCosts,
      canFuel: this.userForm.canFuel,
      canDocuments: this.userForm.canDocuments,
      canAccidents: this.userForm.canAccidents,
      canUsers: this.userForm.canUsers,
      canSettings: this.userForm.canSettings,
      canSuppliers: this.userForm.canSuppliers,
      canFleetManagement: this.userForm.canFleetManagement,
      canTours: this.userForm.canTours,
      canPlayback: this.userForm.canPlayback,
      canReportTrips: this.userForm.canReportTrips,
      canReportFuel: this.userForm.canReportFuel,
      canReportSpeed: this.userForm.canReportSpeed,
      canReportStops: this.userForm.canReportStops,
      canReportMileage: this.userForm.canReportMileage,
      canReportCosts: this.userForm.canReportCosts,
      canReportMaintenance: this.userForm.canReportMaintenance,
      canReportDaily: this.userForm.canReportDaily,
      canReportMonthly: this.userForm.canReportMonthly,
      canReportMileagePeriod: this.userForm.canReportMileagePeriod,
      canReportSpeedInfraction: this.userForm.canReportSpeedInfraction,
      canReportDrivingBehavior: this.userForm.canReportDrivingBehavior,
      canReportMonthlyCosts: this.userForm.canReportMonthlyCosts
    };

    if (this.editingUser) {
      this.apiService.updateUser(this.editingUser.id, {
        firstName: this.userForm.firstName,
        lastName: this.userForm.lastName,
        email: this.userForm.email,
        phone: this.userForm.phone,
        roleId: this.userForm.roleId,
        status: this.userForm.status,
        assignedVehicleIds: this.userForm.assignedVehicleIds,
        ...permissionPayload
      }).subscribe({
        next: () => {
          this.toast.success('Succès', 'Utilisateur modifié avec succès');
          this.loadUsers();
          this.closeUserModal();
        },
        error: (err) => {
          console.error('Error updating user:', err);
          this.toast.error('Erreur', err.error?.message || 'Erreur lors de la modification');
        }
      });
    } else {
      this.apiService.createUser({
        firstName: this.userForm.firstName,
        lastName: this.userForm.lastName,
        email: this.userForm.email,
        phone: this.userForm.phone,
        password: this.userForm.password,
        roleId: this.userForm.roleId,
        assignedVehicleIds: this.userForm.assignedVehicleIds,
        ...permissionPayload
      }).subscribe({
        next: () => {
          this.toast.success('Succès', 'Utilisateur créé avec succès');
          this.loadUsers();
          this.closeUserModal();
        },
        error: (err) => {
          console.error('Error creating user:', err);
          this.toast.error('Erreur', err.error?.message || 'Erreur lors de la création');
        }
      });
    }
  }

  deleteUser(user: User) {
    if (confirm(`Êtes-vous sûr de vouloir supprimer ${user.name} ?`)) {
      this.apiService.deleteUser(user.id).pipe(takeUntil(this.destroy$)).subscribe({
        next: () => {
          this.toast.success('Succès', 'Utilisateur supprimé');
          this.loadUsers();
        },
        error: (err) => {
          console.error('Error deleting user:', err);
          this.toast.error('Erreur', err.error?.message || 'Erreur lors de la suppression');
        }
      });
    }
  }

  // ============ VEHICLE ASSIGNMENT HELPERS ============

  loadAvailableVehicles() {
    this.apiService.getVehicles().pipe(takeUntil(this.destroy$)).subscribe({
      next: (vehicles: any[]) => {
        this.availableVehicles = vehicles.map((v: any) => ({
          id: v.id,
          name: v.name || `${v.brand || ''} ${v.model || ''}`.trim(),
          plate: v.plate || ''
        }));
      },
      error: () => { this.availableVehicles = []; }
    });
  }

  isVehicleSelected(vehicleId: number): boolean {
    return this.userForm.assignedVehicleIds.includes(vehicleId);
  }

  toggleVehicle(vehicleId: number) {
    const idx = this.userForm.assignedVehicleIds.indexOf(vehicleId);
    if (idx >= 0) {
      this.userForm.assignedVehicleIds.splice(idx, 1);
    } else {
      this.userForm.assignedVehicleIds.push(vehicleId);
    }
  }

  isSelectedRoleAdmin(): boolean {
    if (!this.userForm.roleId) return false;
    const role = this.roles.find(r => r.id === this.userForm.roleId);
    return role?.isCompanyAdmin ?? false;
  }

  // ============ ROLE MANAGEMENT ============

  openRoleModal(role?: Role) {
    // Initialize modules from categories and reports
    const modules: Record<string, boolean> = {};
    const reports: Record<string, boolean> = {};
    
    // Initialize all modules from categories
    this.permissionCategories.forEach(cat => {
      cat.permissions.forEach(p => {
        modules[p.key] = false;
      });
    });
    // Initialize all reports
    this.reportPermissions.forEach(r => {
      reports[r.key] = false;
    });

    if (role) {
      this.editingRole = role;
      // Load existing permissions from role
      if (role.permissions) {
        Object.keys(role.permissions).forEach(key => {
          if (key.startsWith('module') && modules.hasOwnProperty(key)) {
            modules[key] = role.permissions![key] === true;
          } else if (key.startsWith('report') && reports.hasOwnProperty(key)) {
            reports[key] = role.permissions![key] === true;
          }
        });
      }
      this.roleForm = {
        name: role.name,
        description: role.description || '',
        isCompanyAdmin: role.isCompanyAdmin,
        modules,
        reports
      };
    } else {
      this.editingRole = null;
      // Default for new role: enable basic permissions only
      modules['moduleDashboard'] = true;
      modules['moduleVehicles'] = true;
      this.roleForm = {
        name: '',
        description: '',
        isCompanyAdmin: false,
        modules,
        reports
      };
    }
    this.showRoleModal = true;
  }

  closeRoleModal() {
    this.showRoleModal = false;
    this.editingRole = null;
  }

  onReportToggle(reportKey: string) {
    // When a report is selected, automatically enable moduleReports
    if (this.roleForm.reports[reportKey] && !this.roleForm.modules['moduleReports']) {
      this.roleForm.modules['moduleReports'] = true;
    }
  }

  onModuleToggle(moduleKey: string) {
    // When moduleReports is disabled, clear all individual report permissions
    if (moduleKey === 'moduleReports' && !this.roleForm.modules['moduleReports']) {
      Object.keys(this.roleForm.reports).forEach(key => {
        this.roleForm.reports[key] = false;
      });
    }
  }

  saveRole() {
    if (!this.roleForm.name) {
      this.toast.error('Erreur', 'Le nom du rôle est requis');
      return;
    }

    // Build permissions object from modules and reports
    const permissions: Record<string, any> = {};
    Object.entries(this.roleForm.modules).forEach(([key, value]) => {
      permissions[key] = value;
    });
    Object.entries(this.roleForm.reports).forEach(([key, value]) => {
      permissions[key] = value;
    });

    if (this.editingRole) {
      this.apiService.updateRole(this.editingRole.id, {
        name: this.roleForm.name,
        description: this.roleForm.description,
        isCompanyAdmin: this.roleForm.isCompanyAdmin,
        permissions
      }).subscribe({
        next: () => {
          this.toast.success('Succès', 'Rôle modifié avec succès');
          this.loadRoles();
          this.closeRoleModal();
        },
        error: (err) => {
          console.error('Error updating role:', err);
          this.toast.error('Erreur', err.error?.message || 'Erreur lors de la modification');
        }
      });
    } else {
      this.apiService.createRole({
        name: this.roleForm.name,
        description: this.roleForm.description,
        isCompanyAdmin: this.roleForm.isCompanyAdmin,
        permissions
      }).subscribe({
        next: () => {
          this.toast.success('Succès', 'Rôle créé avec succès');
          this.loadRoles();
          this.closeRoleModal();
        },
        error: (err) => {
          console.error('Error creating role:', err);
          this.toast.error('Erreur', err.error?.message || 'Erreur lors de la création');
        }
      });
    }
  }

  deleteRole(role: Role) {
    if (role.isSystem) {
      this.toast.error('Erreur', 'Impossible de supprimer un rôle système');
      return;
    }
    if (confirm(`Êtes-vous sûr de vouloir supprimer le rôle "${role.name}" ?`)) {
      this.apiService.deleteRole(role.id).pipe(takeUntil(this.destroy$)).subscribe({
        next: () => {
          this.toast.success('Succès', 'Rôle supprimé');
          this.loadRoles();
        },
        error: (err) => {
          console.error('Error deleting role:', err);
          this.toast.error('Erreur', err.error?.message || 'Erreur lors de la suppression');
        }
      });
    }
  }

  // ============ HELPERS ============

  getInitials(name: string): string {
    return name.split(' ').map(n => n[0]).join('').substring(0, 2).toUpperCase();
  }

  getRoleName(roleId: number): string {
    const role = this.roles.find(r => r.id === roleId);
    return role ? role.name : 'N/A';
  }

  getActiveUsersCount(): number {
    return this.users.filter(u => u.status === 'active').length;
  }

  getAdminsCount(): number {
    return this.users.filter(u => u.isCompanyAdmin).length;
  }

  getUsersCountByRole(roleId: number): number {
    return this.users.filter(u => u.roleId === roleId).length;
  }

  getInactiveUsersCount(): number {
    return this.users.filter(u => u.status !== 'active').length;
  }

  getPermissionsLabel(user: User): string {
    if (!user.userPermissions) return 'Standard';
    const perms = user.userPermissions;
    const count = [
      perms.canMonitoring, perms.canVehicles, perms.canDrivers,
      perms.canReports, perms.canGeofences, perms.canMaintenance,
      perms.canCosts, perms.canFuel, perms.canDocuments, perms.canAccidents,
      perms.canUsers, perms.canSettings, perms.canSuppliers,
      perms.canFleetManagement, perms.canTours, perms.canPlayback
    ].filter(Boolean).length;
    return `${count} module${count > 1 ? 's' : ''}`;
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
