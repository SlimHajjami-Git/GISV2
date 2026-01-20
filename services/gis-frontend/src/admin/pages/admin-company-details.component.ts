import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService, Client, AdminVehicle, Role, SystemUser } from '../services/admin.service';
import { VehiclePopupComponent } from '../../components/shared/vehicle-popup.component';
import { PermissionEditorComponent } from '../components/permission-editor.component';

type CompanyRole = Role & { userCount?: number };

@Component({
  selector: 'admin-company-details',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent, VehiclePopupComponent, PermissionEditorComponent],
  template: `
    <admin-layout [pageTitle]="company?.name || 'Détails Société'">
      <div *ngIf="loading" class="loading-container">
        <div class="loading-spinner"></div>
        <p>Chargement...</p>
      </div>
      
      <div class="company-details-page" *ngIf="!loading && company">
        <!-- Breadcrumb -->
        <div class="breadcrumb">
          <a (click)="goBack()">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M15 18l-6-6 6-6"/>
            </svg>
            Sociétés
          </a>
          <span class="separator">/</span>
          <span class="current">{{ company.name }}</span>
        </div>

        <!-- Company Header Card -->
        <div class="company-header-card">
          <div class="company-avatar">{{ company.name.charAt(0) }}</div>
          <div class="company-info">
            <div class="company-title">
              <h1>{{ company.name }}</h1>
              <span class="status-badge" [class]="company.status">{{ company.status | titlecase }}</span>
            </div>
            <div class="company-meta">
              <span class="meta-item">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/>
                  <polyline points="22,6 12,13 2,6"/>
                </svg>
                {{ company.email }}
              </span>
              <span class="meta-item" *ngIf="company.phone">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72"/>
                </svg>
                {{ company.phone }}
              </span>
              <span class="meta-item">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <rect x="3" y="4" width="18" height="18" rx="2" ry="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/>
                </svg>
                Créé le {{ formatDate(company.createdAt) }}
              </span>
            </div>
          </div>
          <div class="company-actions">
            <button class="btn-secondary" (click)="editCompany()">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
              </svg>
              Modifier
            </button>
          </div>
        </div>

        <!-- Stats Cards -->
        <div class="stats-grid">
          <div class="stat-card">
            <div class="stat-icon vehicles">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="1" y="3" width="15" height="13" rx="2"/><path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-5"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ company.currentVehicles }}<span class="stat-max">/{{ company.maxVehicles }}</span></span>
              <span class="stat-label">Véhicules</span>
            </div>
          </div>
          <div class="stat-card">
            <div class="stat-icon users">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ company.currentUsers }}</span>
              <span class="stat-label">Utilisateurs</span>
            </div>
          </div>
          <div class="stat-card">
            <div class="stat-icon roles">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ roles.length }}</span>
              <span class="stat-label">Rôles</span>
            </div>
          </div>
          <div class="stat-card">
            <div class="stat-icon subscription">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M21 4H3c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h18c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2z"/><path d="M1 10h22"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value plan">{{ company.subscriptionName || 'Aucun' }}</span>
              <span class="stat-label">Plan actif</span>
            </div>
          </div>
        </div>

        <!-- Tabs -->
        <div class="tabs-container">
          <div class="tabs">
            <button class="tab" [class.active]="activeTab === 'vehicles'" (click)="activeTab = 'vehicles'">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="1" y="3" width="15" height="13" rx="2"/><path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-5"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/>
              </svg>
              Véhicules ({{ vehicles.length }})
            </button>
            <button class="tab" [class.active]="activeTab === 'roles'" (click)="activeTab = 'roles'">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>
              </svg>
              Rôles ({{ roles.length }})
            </button>
            <button class="tab" [class.active]="activeTab === 'users'" (click)="activeTab = 'users'">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/>
              </svg>
              Utilisateurs ({{ users.length }})
            </button>
          </div>
        </div>

        <!-- Vehicles Tab -->
        <div class="tab-content" *ngIf="activeTab === 'vehicles'">
          <div class="section-header">
            <div class="search-filter">
              <div class="search-box">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
                </svg>
                <input type="text" [(ngModel)]="vehicleSearch" (input)="filterVehicles()" placeholder="Rechercher un véhicule..." />
              </div>
              <select class="filter-select" [(ngModel)]="vehicleStatusFilter" (change)="filterVehicles()">
                <option value="all">Tous les statuts</option>
                <option value="available">Disponible</option>
                <option value="in_use">En service</option>
                <option value="maintenance">Maintenance</option>
              </select>
            </div>
            <button class="btn-primary" (click)="openAddVehicle()">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
              </svg>
              Ajouter Véhicule
            </button>
          </div>

          <div class="vehicles-table" *ngIf="filteredVehicles.length > 0">
            <table>
              <thead>
                <tr>
                  <th>Véhicule</th>
                  <th>Immatriculation</th>
                  <th>Type</th>
                  <th>Statut</th>
                  <th>GPS</th>
                  <th>Kilométrage</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                <tr *ngFor="let vehicle of filteredVehicles">
                  <td class="vehicle-cell">
                    <div class="vehicle-info">
                      <span class="vehicle-name">{{ vehicle.name }}</span>
                      <span class="vehicle-brand" *ngIf="vehicle.brand">{{ vehicle.brand }} {{ vehicle.model }}</span>
                    </div>
                  </td>
                  <td>{{ vehicle.plate || 'N/A' }}</td>
                  <td>
                    <span class="type-badge">{{ vehicle.type | titlecase }}</span>
                  </td>
                  <td>
                    <span class="status-pill" [class]="vehicle.status">
                      {{ getVehicleStatusLabel(vehicle.status) }}
                    </span>
                  </td>
                  <td>
                    <span class="gps-status" [class.active]="vehicle.hasGps">
                      {{ vehicle.hasGps ? 'Actif' : 'Non' }}
                    </span>
                  </td>
                  <td>{{ vehicle.mileage | number }} km</td>
                  <td>
                    <div class="table-actions">
                      <button class="action-btn" title="Modifier" (click)="editVehicle(vehicle)">
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                          <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                        </svg>
                      </button>
                    </div>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <div class="empty-state" *ngIf="filteredVehicles.length === 0">
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
              <rect x="1" y="3" width="15" height="13" rx="2"/><path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-5"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/>
            </svg>
            <h3>Aucun véhicule trouvé</h3>
            <p *ngIf="vehicles.length === 0">Cette société n'a pas encore de véhicules</p>
            <p *ngIf="vehicles.length > 0">Aucun véhicule ne correspond à vos critères</p>
            <button class="btn-primary" (click)="openAddVehicle()" *ngIf="vehicles.length === 0">
              Ajouter le premier véhicule
            </button>
          </div>
        </div>

        <!-- Roles Tab -->
        <div class="tab-content" *ngIf="activeTab === 'roles'">
          <div class="section-header">
            <div class="search-filter">
              <div class="search-box">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
                </svg>
                <input type="text" [(ngModel)]="roleSearch" (input)="filterRoles()" placeholder="Rechercher un rôle..." />
              </div>
            </div>
            <button class="btn-primary" (click)="openAddRole()">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
              </svg>
              Ajouter Rôle
            </button>
          </div>

          <div class="roles-grid" *ngIf="filteredRoles.length > 0">
            <div class="role-card" *ngFor="let role of filteredRoles" [class.system]="role.isSystem">
              <div class="role-header">
                <div class="role-icon" [class]="role.roleType">
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>
                  </svg>
                </div>
                <div class="role-info">
                  <h4>{{ role.name }}</h4>
                  <span class="role-type">{{ getRoleTypeLabel(role.roleType) }}</span>
                </div>
                <div class="role-badges">
                  <span class="badge system" *ngIf="role.isSystem">Système</span>
                  <span class="badge default" *ngIf="role.isDefault">Par défaut</span>
                </div>
              </div>
              <p class="role-description" *ngIf="role.description">{{ role.description }}</p>
              <div class="role-stats">
                <span class="users-count">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/>
                  </svg>
                  {{ role.userCount }} utilisateur{{ role.userCount !== 1 ? 's' : '' }}
                </span>
              </div>
              <div class="role-actions" *ngIf="!role.isSystem">
                <button class="action-btn" title="Modifier" (click)="editRole(role)">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                    <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                  </svg>
                </button>
                <button class="action-btn delete" title="Supprimer" (click)="deleteRole(role)">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                  </svg>
                </button>
              </div>
            </div>
          </div>

          <div class="empty-state" *ngIf="filteredRoles.length === 0">
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
              <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>
            </svg>
            <h3>Aucun rôle trouvé</h3>
            <p *ngIf="roles.length === 0">Cette société n'a pas encore de rôles personnalisés</p>
            <p *ngIf="roles.length > 0">Aucun rôle ne correspond à votre recherche</p>
            <button class="btn-primary" (click)="openAddRole()" *ngIf="roles.length === 0">
              Créer le premier rôle
            </button>
          </div>
        </div>

        <!-- Users Tab -->
        <div class="tab-content" *ngIf="activeTab === 'users'">
          <div class="section-header">
            <div class="search-filter">
              <div class="search-box">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
                </svg>
                <input type="text" [(ngModel)]="userSearch" (input)="filterUsers()" placeholder="Rechercher un utilisateur..." />
              </div>
              <select class="filter-select" [(ngModel)]="userStatusFilter" (change)="filterUsers()">
                <option value="all">Tous les statuts</option>
                <option value="active">Actif</option>
                <option value="suspended">Suspendu</option>
              </select>
            </div>
            <button class="btn-primary" (click)="openAddUser()">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
              </svg>
              Ajouter Utilisateur
            </button>
          </div>

          <div class="users-table" *ngIf="filteredUsers.length > 0">
            <table>
              <thead>
                <tr>
                  <th>Utilisateur</th>
                  <th>Email</th>
                  <th>CIN</th>
                  <th>Rôle</th>
                  <th>Véhicules</th>
                  <th>Statut</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                <tr *ngFor="let user of filteredUsers">
                  <td class="user-cell">
                    <div class="user-info">
                      <div class="user-avatar">{{ user.name.charAt(0) }}</div>
                      <div class="user-details">
                        <span class="user-name">{{ user.name }}</span>
                        <span class="user-phone" *ngIf="user.phone">{{ user.phone }}</span>
                      </div>
                    </div>
                  </td>
                  <td>{{ user.email }}</td>
                  <td>{{ user.cin || 'N/A' }}</td>
                  <td>
                    <span class="role-badge" *ngIf="user.roleName">{{ user.roleName }}</span>
                    <span class="role-badge default" *ngIf="!user.roleName">Aucun</span>
                  </td>
                  <td>
                    <span class="vehicle-count">{{ user.assignedVehicleIds?.length || 0 }} véhicule(s)</span>
                  </td>
                  <td>
                    <span class="status-pill" [class]="user.status">
                      {{ user.status === 'active' ? 'Actif' : 'Suspendu' }}
                    </span>
                  </td>
                  <td>
                    <div class="table-actions">
                      <button class="action-btn" title="Modifier" (click)="editUser(user)">
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                          <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                        </svg>
                      </button>
                      <button class="action-btn" [class.active]="user.status === 'active'" 
                              (click)="toggleUserStatus(user)" 
                              [title]="user.status === 'active' ? 'Suspendre' : 'Activer'">
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <path *ngIf="user.status === 'active'" d="M18.36 6.64a9 9 0 1 1-12.73 0"/>
                          <line *ngIf="user.status === 'active'" x1="12" y1="2" x2="12" y2="12"/>
                          <path *ngIf="user.status !== 'active'" d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                          <polyline *ngIf="user.status !== 'active'" points="22 4 12 14.01 9 11.01"/>
                        </svg>
                      </button>
                    </div>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <div class="empty-state" *ngIf="filteredUsers.length === 0">
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
              <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/>
            </svg>
            <h3>Aucun utilisateur trouvé</h3>
            <p *ngIf="users.length === 0">Cette société n'a pas encore d'utilisateurs</p>
            <p *ngIf="users.length > 0">Aucun utilisateur ne correspond à vos critères</p>
            <button class="btn-primary" (click)="openAddUser()" *ngIf="users.length === 0">
              Ajouter le premier utilisateur
            </button>
          </div>
        </div>

        <!-- Vehicle Popup -->
        <app-vehicle-popup
          [isOpen]="showVehiclePopup"
          [vehicle]="selectedVehicle"
          (closed)="closeVehiclePopup()"
          (saved)="onVehicleSaved($event)">
        </app-vehicle-popup>

        <!-- Role Modal -->
        <div class="popup-overlay" *ngIf="showRoleModal" (click)="closeRoleModal()">
          <div class="popup-container role-popup" (click)="$event.stopPropagation()">
            <div class="popup-header">
              <div class="header-title">
                <div class="header-icon">
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M12 2a10 10 0 1 0 10 10A10 10 0 0 0 12 2zm0 18a8 8 0 1 1 8-8 8 8 0 0 1-8 8z"/><path d="M12 6a2 2 0 1 0 2 2 2 2 0 0 0-2-2zm0 10a6 6 0 0 0 4.24-1.76l-1.42-1.42A4 4 0 1 1 12 8v2a2 2 0 1 1-2 2H8a4 4 0 0 0 4 4z"/>
                  </svg>
                </div>
                <h2>Nouveau rôle</h2>
              </div>
              <button class="close-btn" (click)="closeRoleModal()" title="Fermer">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                </svg>
              </button>
            </div>
            
            <div class="popup-body">
              <div class="form-section">
                <div class="section-title">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                  </svg>
                  <span>Informations du rôle</span>
                </div>
                <div class="form-grid">
                  <div class="form-group">
                    <label>Nom du rôle <span class="required">*</span></label>
                    <input type="text" [(ngModel)]="roleForm.name" placeholder="Superviseur" />
                  </div>
                  <div class="form-group">
                    <label>Type de rôle</label>
                    <select [(ngModel)]="roleForm.roleType">
                      <option value="employee">Employé</option>
                      <option value="custom">Personnalisé</option>
                    </select>
                  </div>
                  <div class="form-group full-width">
                    <label>Description</label>
                    <textarea [(ngModel)]="roleForm.description" placeholder="Description du rôle..." rows="2"></textarea>
                  </div>
                  <div class="form-group full-width">
                    <label class="checkbox-label-inline">
                      <input type="checkbox" [(ngModel)]="roleForm.isDefault" />
                      <span class="checkmark"></span>
                      <span>Rôle par défaut pour les nouveaux utilisateurs</span>
                    </label>
                  </div>
                </div>
              </div>

              <div class="form-section">
                <div class="section-title">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/>
                  </svg>
                  <span>Permissions</span>
                </div>
                <permission-editor
                  [subscriptionId]="company?.subscriptionId || null"
                  [subscriptionName]="company?.subscriptionName || ''"
                  [initialPermissions]="roleForm.permissions"
                  (permissionsChange)="onPermissionsChange($event)">
                </permission-editor>
              </div>
            </div>

            <div class="popup-footer">
              <button type="button" class="btn-secondary" (click)="closeRoleModal()">Annuler</button>
              <button type="button" class="btn-primary" (click)="saveRole()" [disabled]="!roleForm.name">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/><polyline points="17,21 17,13 7,13 7,21"/><polyline points="7,3 7,8 15,8"/>
                </svg>
                Créer le rôle
              </button>
            </div>
          </div>
        </div>
      </div>

      <!-- User Modal -->
      <div class="popup-overlay" *ngIf="showUserModal" (click)="closeUserModal()">
        <div class="popup-container user-popup" (click)="$event.stopPropagation()">
          <div class="popup-header">
            <div class="header-title">
              <div class="header-icon">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/>
                </svg>
              </div>
              <h2>Nouvel utilisateur</h2>
            </div>
            <button class="close-btn" (click)="closeUserModal()" title="Fermer">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
              </svg>
            </button>
          </div>
          
          <div class="popup-body">
            <!-- Personal Information Section -->
            <div class="form-section">
              <div class="section-title">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/>
                </svg>
                <span>Informations personnelles</span>
              </div>
              <div class="form-grid">
                <div class="form-group">
                  <label>Prénom <span class="required">*</span></label>
                  <input type="text" [(ngModel)]="userForm.firstName" placeholder="Mohamed" />
                </div>
                <div class="form-group">
                  <label>Nom <span class="required">*</span></label>
                  <input type="text" [(ngModel)]="userForm.lastName" placeholder="Alami" />
                </div>
                <div class="form-group">
                  <label>Date de naissance</label>
                  <input type="date" [(ngModel)]="userForm.dateOfBirth" />
                </div>
                <div class="form-group">
                  <label>CIN <span class="required">*</span></label>
                  <input type="text" [(ngModel)]="userForm.cin" placeholder="AB123456" />
                </div>
              </div>
            </div>

            <!-- Account Information Section -->
            <div class="form-section">
              <div class="section-title">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/>
                </svg>
                <span>Informations du compte</span>
              </div>
              <div class="form-grid">
                <div class="form-group">
                  <label>Email <span class="required">*</span></label>
                  <input type="email" [(ngModel)]="userForm.email" placeholder="email@exemple.com" />
                </div>
                <div class="form-group" *ngIf="!editingUser">
                  <label>Mot de passe <span class="required">*</span></label>
                  <input type="password" [(ngModel)]="userForm.password" placeholder="••••••••" />
                </div>
                <div class="form-group">
                  <label>Téléphone</label>
                  <input type="tel" [(ngModel)]="userForm.phone" placeholder="+212 6XX XXX XXX" />
                </div>
                <div class="form-group">
                  <label>Rôle</label>
                  <select [(ngModel)]="userForm.roleId">
                    <option [ngValue]="null">-- Sélectionner un rôle --</option>
                    <option *ngFor="let role of roles" [ngValue]="role.id">{{ role.name }}</option>
                  </select>
                </div>
              </div>
            </div>

            <!-- Vehicle Assignment Section -->
            <div class="form-section">
              <div class="section-title">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <rect x="1" y="3" width="15" height="13" rx="2"/><path d="M16 8h4l3 3v5h-7V8z"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/>
                </svg>
                <span>Véhicules supervisés</span>
                <span class="badge" *ngIf="userForm.assignedVehicleIds.length > 0">{{ userForm.assignedVehicleIds.length }}</span>
              </div>
              <div class="vehicle-grid" *ngIf="vehicles.length > 0">
                <div class="vehicle-card" 
                     *ngFor="let vehicle of vehicles" 
                     [class.selected]="isVehicleAssigned(vehicle.id)"
                     (click)="toggleVehicleAssignment(vehicle.id)">
                  <div class="vehicle-card-check">
                    <svg *ngIf="isVehicleAssigned(vehicle.id)" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
                      <polyline points="20 6 9 17 4 12"/>
                    </svg>
                  </div>
                  <div class="vehicle-card-icon">
                    <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                      <rect x="1" y="3" width="15" height="13" rx="2"/><path d="M16 8h4l3 3v5h-7V8z"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/>
                    </svg>
                  </div>
                  <div class="vehicle-card-info">
                    <span class="vehicle-card-name">{{ vehicle.name || 'Véhicule' }}</span>
                    <span class="vehicle-card-plate" *ngIf="vehicle.plate">{{ vehicle.plate }}</span>
                  </div>
                  <div class="vehicle-card-status" [class.active]="vehicle.status === 'available' || vehicle.status === 'in_use'">
                    <span class="status-dot"></span>
                  </div>
                </div>
              </div>
              <div class="no-vehicles" *ngIf="vehicles.length === 0">
                <div class="no-vehicles-icon">
                  <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                    <rect x="1" y="3" width="15" height="13" rx="2"/><path d="M16 8h4l3 3v5h-7V8z"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/>
                  </svg>
                </div>
                <span class="no-vehicles-text">Aucun véhicule disponible</span>
                <span class="no-vehicles-hint">Ajoutez des véhicules à cette société pour les assigner</span>
              </div>
            </div>
          </div>

          <div class="popup-footer">
            <button type="button" class="btn-secondary" (click)="closeUserModal()">
              Annuler
            </button>
            <button type="button" class="btn-primary" (click)="saveUser()" [disabled]="!isUserFormValid()">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/><polyline points="17,21 17,13 7,13 7,21"/><polyline points="7,3 7,8 15,8"/>
              </svg>
              Ajouter l'utilisateur
            </button>
          </div>
        </div>
      </div>

      <!-- Loading State -->
      <div class="loading-state" *ngIf="loading">
        <div class="spinner"></div>
        <p>Chargement des données...</p>
      </div>

      <!-- Error State -->
      <div class="error-state" *ngIf="!loading && !company">
        <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
          <circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/>
        </svg>
        <h3>Société non trouvée</h3>
        <p>La société demandée n'existe pas ou a été supprimée</p>
        <button class="btn-primary" (click)="goBack()">Retour aux sociétés</button>
      </div>
    </admin-layout>
  `,
  styles: [`
    .company-details-page {
      display: flex;
      flex-direction: column;
      gap: 24px;
    }

    /* Breadcrumb */
    .breadcrumb {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 14px;
      color: #64748b;
    }

    .breadcrumb a {
      display: flex;
      align-items: center;
      gap: 4px;
      color: #00a388;
      cursor: pointer;
      text-decoration: none;
      transition: color 0.2s;
    }

    .breadcrumb a:hover {
      color: #00d4aa;
    }

    .breadcrumb .separator {
      color: #cbd5e1;
    }

    .breadcrumb .current {
      color: #1f2937;
      font-weight: 500;
    }

    /* Company Header Card */
    .company-header-card {
      display: flex;
      align-items: center;
      gap: 20px;
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      padding: 24px;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
    }

    .company-avatar {
      width: 72px;
      height: 72px;
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
      border-radius: 16px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 700;
      font-size: 32px;
      color: #fff;
      flex-shrink: 0;
    }

    .company-info {
      flex: 1;
    }

    .company-title {
      display: flex;
      align-items: center;
      gap: 12px;
      margin-bottom: 8px;
    }

    .company-title h1 {
      margin: 0;
      font-size: 24px;
      font-weight: 600;
      color: #1f2937;
    }

    .company-meta {
      display: flex;
      flex-wrap: wrap;
      gap: 16px;
    }

    .meta-item {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 14px;
      color: #64748b;
    }

    .meta-item svg {
      color: #94a3b8;
    }

    .company-actions {
      display: flex;
      gap: 12px;
    }

    /* Status Badge */
    .status-badge {
      padding: 4px 12px;
      border-radius: 12px;
      font-size: 12px;
      font-weight: 500;
    }

    .status-badge.active {
      background: rgba(34, 197, 94, 0.15);
      color: #22c55e;
    }

    .status-badge.suspended {
      background: rgba(239, 68, 68, 0.15);
      color: #ef4444;
    }

    .status-badge.pending {
      background: rgba(249, 115, 22, 0.15);
      color: #f97316;
    }

    /* Stats Grid */
    .stats-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
      gap: 16px;
    }

    .stat-card {
      display: flex;
      align-items: center;
      gap: 16px;
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 12px;
      padding: 20px;
    }

    .stat-icon {
      width: 48px;
      height: 48px;
      border-radius: 12px;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .stat-icon.vehicles {
      background: rgba(59, 130, 246, 0.1);
      color: #3b82f6;
    }

    .stat-icon.users {
      background: rgba(139, 92, 246, 0.1);
      color: #8b5cf6;
    }

    .stat-icon.roles {
      background: rgba(249, 115, 22, 0.1);
      color: #f97316;
    }

    .stat-icon.subscription {
      background: rgba(0, 212, 170, 0.1);
      color: #00a388;
    }

    .stat-content {
      display: flex;
      flex-direction: column;
    }

    .stat-value {
      font-size: 24px;
      font-weight: 700;
      color: #1f2937;
    }

    .stat-value .stat-max {
      font-size: 16px;
      font-weight: 400;
      color: #64748b;
    }

    .stat-value.plan {
      font-size: 18px;
    }

    .stat-label {
      font-size: 13px;
      color: #64748b;
    }

    /* Tabs */
    .tabs-container {
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 12px;
      padding: 4px;
    }

    .tabs {
      display: flex;
      gap: 4px;
    }

    .tab {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 12px 20px;
      background: transparent;
      border: none;
      border-radius: 8px;
      color: #64748b;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.2s;
    }

    .tab:hover {
      background: #f1f5f9;
      color: #1f2937;
    }

    .tab.active {
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
      color: #fff;
    }

    /* Tab Content */
    .tab-content {
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      padding: 24px;
    }

    .section-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 20px;
      flex-wrap: wrap;
      gap: 16px;
    }

    .search-filter {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .search-box {
      display: flex;
      align-items: center;
      gap: 10px;
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      padding: 10px 14px;
      width: 280px;
    }

    .search-box svg {
      color: #64748b;
    }

    .search-box input {
      flex: 1;
      border: none;
      background: transparent;
      color: #1f2937;
      font-size: 14px;
      outline: none;
    }

    .filter-select {
      padding: 10px 14px;
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      color: #1f2937;
      font-size: 14px;
      outline: none;
      cursor: pointer;
    }

    /* Buttons */
    .btn-primary {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 20px;
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
      border: none;
      border-radius: 10px;
      color: #fff;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-primary:hover {
      transform: translateY(-2px);
      box-shadow: 0 4px 16px rgba(0, 212, 170, 0.3);
    }

    .btn-primary:disabled {
      opacity: 0.6;
      cursor: not-allowed;
      transform: none;
    }

    .btn-secondary {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 20px;
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      color: #1f2937;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-secondary:hover {
      background: #e2e8f0;
    }

    /* Vehicles Table */
    .vehicles-table {
      overflow-x: auto;
    }

    .vehicles-table table {
      width: 100%;
      border-collapse: collapse;
    }

    .vehicles-table th,
    .vehicles-table td {
      padding: 14px 16px;
      text-align: left;
      border-bottom: 1px solid #e2e8f0;
    }

    .vehicles-table th {
      font-size: 12px;
      font-weight: 600;
      color: #64748b;
      text-transform: uppercase;
      letter-spacing: 0.5px;
      background: #f8fafc;
    }

    .vehicles-table tr:hover {
      background: #f8fafc;
    }

    .vehicle-cell {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .vehicle-info {
      display: flex;
      flex-direction: column;
    }

    .vehicle-name {
      font-weight: 500;
      color: #1f2937;
    }

    .vehicle-brand {
      font-size: 12px;
      color: #64748b;
    }

    .type-badge {
      padding: 4px 10px;
      background: #f1f5f9;
      border-radius: 6px;
      font-size: 12px;
      color: #64748b;
    }

    .status-pill {
      display: inline-block;
      padding: 4px 10px;
      border-radius: 12px;
      font-size: 12px;
      font-weight: 500;
    }

    .status-pill.available {
      background: rgba(34, 197, 94, 0.15);
      color: #22c55e;
    }

    .status-pill.in_use {
      background: rgba(59, 130, 246, 0.15);
      color: #3b82f6;
    }

    .status-pill.maintenance {
      background: rgba(249, 115, 22, 0.15);
      color: #f97316;
    }

    .gps-status {
      font-size: 13px;
      color: #64748b;
    }

    .gps-status.active {
      color: #22c55e;
      font-weight: 500;
    }

    .table-actions {
      display: flex;
      gap: 8px;
    }

    .action-btn {
      width: 32px;
      height: 32px;
      display: flex;
      align-items: center;
      justify-content: center;
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      color: #64748b;
      cursor: pointer;
      transition: all 0.2s;
    }

    .action-btn:hover {
      background: #e2e8f0;
      color: #1f2937;
    }

    .action-btn.delete:hover {
      background: rgba(239, 68, 68, 0.1);
      border-color: rgba(239, 68, 68, 0.3);
      color: #ef4444;
    }

    /* Roles Grid */
    .roles-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
      gap: 16px;
    }

    .role-card {
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 12px;
      padding: 20px;
      transition: all 0.2s;
    }

    .role-card:hover {
      border-color: #cbd5e1;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.05);
    }

    .role-card.system {
      background: rgba(139, 92, 246, 0.05);
      border-color: rgba(139, 92, 246, 0.2);
    }

    .role-header {
      display: flex;
      align-items: flex-start;
      gap: 12px;
      margin-bottom: 12px;
    }

    .role-icon {
      width: 40px;
      height: 40px;
      border-radius: 10px;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }

    .role-icon.company_admin {
      background: rgba(239, 68, 68, 0.1);
      color: #ef4444;
    }

    .role-icon.employee {
      background: rgba(59, 130, 246, 0.1);
      color: #3b82f6;
    }

    .role-icon.custom {
      background: rgba(249, 115, 22, 0.1);
      color: #f97316;
    }

    .role-info {
      flex: 1;
    }

    .role-info h4 {
      margin: 0 0 4px 0;
      font-size: 16px;
      font-weight: 600;
      color: #1f2937;
    }

    .role-type {
      font-size: 12px;
      color: #64748b;
    }

    .role-badges {
      display: flex;
      gap: 6px;
    }

    .badge {
      padding: 2px 8px;
      border-radius: 6px;
      font-size: 11px;
      font-weight: 500;
    }

    .badge.system {
      background: rgba(139, 92, 246, 0.15);
      color: #8b5cf6;
    }

    .badge.default {
      background: rgba(0, 212, 170, 0.15);
      color: #00a388;
    }

    .role-description {
      margin: 0 0 12px 0;
      font-size: 13px;
      color: #64748b;
      line-height: 1.5;
    }

    .role-stats {
      display: flex;
      align-items: center;
      gap: 16px;
      margin-bottom: 12px;
    }

    .users-count {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 13px;
      color: #64748b;
    }

    .role-actions {
      display: flex;
      gap: 8px;
      padding-top: 12px;
      border-top: 1px solid #e2e8f0;
    }

    /* Empty State */
    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      padding: 60px 20px;
      text-align: center;
      color: #64748b;
    }

    .empty-state svg {
      margin-bottom: 16px;
      opacity: 0.5;
    }

    .empty-state h3 {
      margin: 0 0 8px 0;
      font-size: 18px;
      font-weight: 600;
      color: #1f2937;
    }

    .empty-state p {
      margin: 0 0 20px 0;
      font-size: 14px;
    }

    /* Loading State */
    .loading-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 100px 20px;
    }

    .spinner {
      width: 40px;
      height: 40px;
      border: 3px solid #e2e8f0;
      border-top-color: #00d4aa;
      border-radius: 50%;
      animation: spin 1s linear infinite;
      margin-bottom: 16px;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }

    /* Error State */
    .error-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 100px 20px;
      text-align: center;
    }

    .error-state svg {
      color: #ef4444;
      margin-bottom: 16px;
    }

    .error-state h3 {
      margin: 0 0 8px 0;
      font-size: 20px;
      font-weight: 600;
      color: #1f2937;
    }

    .error-state p {
      margin: 0 0 20px 0;
      font-size: 14px;
      color: #64748b;
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

    .modal {
      background: #ffffff;
      border-radius: 16px;
      width: 100%;
      max-width: 560px;
      max-height: 90vh;
      overflow: hidden;
      display: flex;
      flex-direction: column;
    }

    .modal-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 20px 24px;
      border-bottom: 1px solid #e2e8f0;
    }

    .modal-header h2 {
      margin: 0;
      font-size: 18px;
      font-weight: 600;
      color: #1f2937;
    }

    .close-btn {
      width: 36px;
      height: 36px;
      display: flex;
      align-items: center;
      justify-content: center;
      background: #f8fafc;
      border: none;
      border-radius: 8px;
      color: #64748b;
      cursor: pointer;
      transition: all 0.2s;
    }

    .close-btn:hover {
      background: #e2e8f0;
      color: #1f2937;
    }

    .modal-body {
      padding: 24px;
      overflow-y: auto;
    }

    .form-group {
      margin-bottom: 20px;
    }

    .form-group label {
      display: block;
      margin-bottom: 8px;
      font-size: 14px;
      font-weight: 500;
      color: #1f2937;
    }

    .form-group input,
    .form-group select,
    .form-group textarea {
      width: 100%;
      padding: 12px 14px;
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      font-size: 14px;
      color: #1f2937;
      outline: none;
      transition: all 0.2s;
    }

    .form-group input:focus,
    .form-group select:focus,
    .form-group textarea:focus {
      border-color: #00d4aa;
      background: #fff;
    }

    .checkbox-label {
      display: flex;
      align-items: center;
      gap: 10px;
      cursor: pointer;
    }

    .checkbox-label input {
      width: auto;
    }

    .permissions-section {
      margin-top: 16px;
    }

    .role-modal {
      max-width: 700px;
      max-height: 90vh;
    }

    .role-modal .modal-body {
      max-height: 70vh;
      overflow-y: auto;
    }

    .permission-item input {
      width: auto;
    }

    /* User Popup - Enhanced UI/UX */
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
      max-width: 680px;
      width: 100%;
      max-height: 85vh;
      overflow: hidden;
      display: flex;
      flex-direction: column;
      animation: slideUp 0.3s cubic-bezier(0.16, 1, 0.3, 1);
    }

    .user-popup {
      max-width: 680px;
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
      letter-spacing: -0.01em;
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
      transform: scale(1.05);
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

    .section-title .badge {
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
      color: white;
      font-size: 11px;
      font-weight: 600;
      padding: 2px 8px;
      border-radius: 10px;
      margin-left: auto;
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
      color: #475569;
    }

    .form-grid .form-group label .required {
      color: #ef4444;
      font-weight: 600;
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
      font-family: inherit;
    }

    .form-grid .form-group textarea {
      resize: vertical;
      min-height: 60px;
    }

    .form-grid .form-group input:hover,
    .form-grid .form-group select:hover,
    .form-grid .form-group textarea:hover {
      border-color: #cbd5e1;
    }

    .form-grid .form-group input:focus,
    .form-grid .form-group select:focus,
    .form-grid .form-group textarea:focus {
      outline: none;
      border-color: #6366f1;
      background: white;
      box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.1);
    }

    .form-grid .form-group input::placeholder,
    .form-grid .form-group textarea::placeholder {
      color: #94a3b8;
    }

    .checkbox-label-inline {
      display: flex;
      align-items: center;
      gap: 10px;
      cursor: pointer;
      font-size: 13px;
      color: #475569;
    }

    .checkbox-label-inline input[type="checkbox"] {
      display: none;
    }

    .checkbox-label-inline .checkmark {
      width: 18px;
      height: 18px;
      border: 2px solid #cbd5e1;
      border-radius: 4px;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
      flex-shrink: 0;
    }

    .checkbox-label-inline input[type="checkbox"]:checked + .checkmark {
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
      border-color: transparent;
    }

    .checkbox-label-inline input[type="checkbox"]:checked + .checkmark::after {
      content: '';
      width: 5px;
      height: 9px;
      border: solid white;
      border-width: 0 2px 2px 0;
      transform: rotate(45deg);
      margin-bottom: 2px;
    }

    .role-popup {
      max-width: 750px;
    }

    .popup-footer {
      padding: 16px 24px;
      border-top: 1px solid #e2e8f0;
      display: flex;
      gap: 12px;
      justify-content: flex-end;
      background: white;
    }

    .popup-footer .btn-secondary {
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

    .popup-footer .btn-secondary:hover {
      background: #f8fafc;
      border-color: #cbd5e1;
      color: #1e293b;
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

    .popup-footer .btn-primary:hover:not(:disabled) {
      transform: translateY(-1px);
      box-shadow: 0 4px 12px rgba(102, 126, 234, 0.35);
    }

    .popup-footer .btn-primary:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }

    /* Vehicle Grid - Card-based Selection */
    .vehicle-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
      gap: 12px;
      max-height: 220px;
      overflow-y: auto;
      padding: 4px;
    }

    .vehicle-card {
      position: relative;
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 8px;
      padding: 16px 12px;
      background: white;
      border: 2px solid #e2e8f0;
      border-radius: 12px;
      cursor: pointer;
      transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
    }

    .vehicle-card:hover {
      border-color: #c7d2fe;
      background: #fafbff;
      transform: translateY(-2px);
      box-shadow: 0 4px 12px rgba(99, 102, 241, 0.1);
    }

    .vehicle-card.selected {
      border-color: #6366f1;
      background: linear-gradient(135deg, rgba(99, 102, 241, 0.08) 0%, rgba(118, 75, 162, 0.08) 100%);
      box-shadow: 0 4px 12px rgba(99, 102, 241, 0.15);
    }

    .vehicle-card-check {
      position: absolute;
      top: 8px;
      right: 8px;
      width: 20px;
      height: 20px;
      border: 2px solid #e2e8f0;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
      background: white;
    }

    .vehicle-card.selected .vehicle-card-check {
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
      border-color: transparent;
      color: white;
    }

    .vehicle-card-icon {
      width: 48px;
      height: 48px;
      background: linear-gradient(135deg, #f1f5f9 0%, #e2e8f0 100%);
      border-radius: 12px;
      display: flex;
      align-items: center;
      justify-content: center;
      color: #64748b;
      transition: all 0.2s;
    }

    .vehicle-card.selected .vehicle-card-icon {
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
      color: white;
    }

    .vehicle-card-info {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 4px;
      text-align: center;
    }

    .vehicle-card-name {
      font-size: 12px;
      font-weight: 600;
      color: #1e293b;
      max-width: 100%;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .vehicle-card-plate {
      font-size: 10px;
      font-weight: 500;
      color: white;
      background: linear-gradient(135deg, #64748b 0%, #475569 100%);
      padding: 2px 8px;
      border-radius: 8px;
      letter-spacing: 0.5px;
    }

    .vehicle-card.selected .vehicle-card-plate {
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
    }

    .vehicle-card-status {
      position: absolute;
      top: 8px;
      left: 8px;
    }

    .vehicle-card-status .status-dot {
      display: block;
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: #94a3b8;
    }

    .vehicle-card-status.active .status-dot {
      background: #22c55e;
      box-shadow: 0 0 0 3px rgba(34, 197, 94, 0.2);
    }

    .no-vehicles {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 12px;
      padding: 32px 24px;
      background: linear-gradient(135deg, #f8fafc 0%, #f1f5f9 100%);
      border: 2px dashed #e2e8f0;
      border-radius: 12px;
    }

    .no-vehicles-icon {
      width: 64px;
      height: 64px;
      background: white;
      border-radius: 16px;
      display: flex;
      align-items: center;
      justify-content: center;
      color: #cbd5e1;
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.05);
    }

    .no-vehicles-text {
      font-size: 14px;
      font-weight: 600;
      color: #64748b;
    }

    .no-vehicles-hint {
      font-size: 12px;
      color: #94a3b8;
      text-align: center;
      font-size: 12px;
      font-style: italic;
    }

    /* Users Table */
    .users-table {
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 12px;
      overflow: hidden;
    }

    .users-table table {
      width: 100%;
      border-collapse: collapse;
    }

    .users-table th,
    .users-table td {
      padding: 14px 16px;
      text-align: left;
      border-bottom: 1px solid #e2e8f0;
    }

    .users-table th {
      background: #f8fafc;
      font-size: 12px;
      font-weight: 600;
      color: #64748b;
      text-transform: uppercase;
    }

    .users-table tr:last-child td {
      border-bottom: none;
    }

    .users-table tr:hover {
      background: #f8fafc;
    }

    .user-cell {
      min-width: 200px;
    }

    .user-info {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .user-avatar {
      width: 40px;
      height: 40px;
      background: linear-gradient(135deg, #8b5cf6 0%, #6366f1 100%);
      border-radius: 10px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 600;
      font-size: 16px;
      color: #fff;
      flex-shrink: 0;
    }

    .user-details {
      display: flex;
      flex-direction: column;
    }

    .user-name {
      font-weight: 500;
      color: #1f2937;
    }

    .user-phone {
      font-size: 12px;
      color: #64748b;
    }

    .role-badge {
      display: inline-block;
      padding: 4px 10px;
      background: rgba(99, 102, 241, 0.1);
      color: #6366f1;
      border-radius: 6px;
      font-size: 12px;
      font-weight: 500;
    }

    .role-badge.default {
      background: rgba(100, 116, 139, 0.1);
      color: #64748b;
    }

    .vehicle-count {
      font-size: 13px;
      color: #64748b;
    }

    .status-pill {
      display: inline-block;
      padding: 4px 10px;
      border-radius: 12px;
      font-size: 12px;
      font-weight: 500;
    }

    .status-pill.active {
      background: rgba(34, 197, 94, 0.15);
      color: #22c55e;
    }

    .status-pill.suspended {
      background: rgba(239, 68, 68, 0.15);
      color: #ef4444;
    }

    .table-actions {
      display: flex;
      gap: 8px;
    }

    .action-btn {
      width: 32px;
      height: 32px;
      display: flex;
      align-items: center;
      justify-content: center;
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      color: #64748b;
      cursor: pointer;
      transition: all 0.2s;
    }

    .action-btn:hover {
      background: #e2e8f0;
      color: #1f2937;
    }

    .action-btn.active:hover {
      background: rgba(239, 68, 68, 0.1);
      border-color: rgba(239, 68, 68, 0.3);
      color: #ef4444;
    }

    .modal-footer {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 16px 24px;
      border-top: 1px solid #e2e8f0;
    }

    /* Responsive */
    @media (max-width: 768px) {
      .company-header-card {
        flex-direction: column;
        text-align: center;
      }

      .company-meta {
        justify-content: center;
      }

      .company-actions {
        width: 100%;
        justify-content: center;
      }

      .section-header {
        flex-direction: column;
        align-items: stretch;
      }

      .search-filter {
        flex-direction: column;
      }

      .search-box {
        width: 100%;
      }

      .permissions-grid {
        grid-template-columns: 1fr;
      }
    }
  `]
})
export class AdminCompanyDetailsComponent implements OnInit {
  companyId: number = 0;
  company: Client | null = null;
  vehicles: AdminVehicle[] = [];
  filteredVehicles: AdminVehicle[] = [];
  roles: CompanyRole[] = [];
  filteredRoles: CompanyRole[] = [];
  users: SystemUser[] = [];
  filteredUsers: SystemUser[] = [];
  loading = true;

  activeTab: 'vehicles' | 'roles' | 'users' = 'vehicles';
  vehicleSearch = '';
  vehicleStatusFilter = 'all';
  roleSearch = '';
  userSearch = '';
  userStatusFilter = 'all';

  showVehiclePopup = false;
  selectedVehicle: any = null;

  showRoleModal = false;
  editingRole: CompanyRole | null = null;
  roleForm: {
    name: string;
    description: string;
    roleType: 'employee' | 'system_admin' | 'company_admin' | 'custom';
    isDefault: boolean;
    permissions: Record<string, any>;
  } = {
    name: '',
    description: '',
    roleType: 'employee',
    isDefault: false,
    permissions: {}
  };

  showUserModal = false;
  editingUser: SystemUser | null = null;
  userForm: {
    firstName: string;
    lastName: string;
    dateOfBirth: string;
    cin: string;
    email: string;
    password: string;
    phone: string;
    roleId: number | null;
    assignedVehicleIds: number[];
  } = {
    firstName: '',
    lastName: '',
    dateOfBirth: '',
    cin: '',
    email: '',
    password: '',
    phone: '',
    roleId: null,
    assignedVehicleIds: []
  };


  constructor(
    private router: Router,
    private route: ActivatedRoute,
    private adminService: AdminService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit() {
    this.route.params.subscribe(params => {
      this.companyId = +params['id'];
      this.loadCompanyDetails();
    });
  }

  loadCompanyDetails() {
    this.loading = true;
    
    this.adminService.getClient(this.companyId).subscribe({
      next: (company) => {
        this.company = company || null;
        if (this.company) {
          this.loadVehicles();
          this.loadRoles();
          this.loadUsers();
        }
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        console.error('Error loading company:', err);
        this.company = null;
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  loadVehicles() {
    this.adminService.getVehicles().subscribe({
      next: (vehicles) => {
        this.vehicles = vehicles.filter(v => v.companyId === this.companyId);
        this.filterVehicles();
      },
      error: (err) => console.error('Error loading vehicles:', err)
    });
  }

  loadRoles() {
    this.adminService.getCompanyRoles(this.companyId).subscribe({
      next: (roles) => {
        this.roles = roles;
        this.filterRoles();
      },
      error: (err) => {
        console.error('Error loading roles:', err);
        this.roles = [];
        this.filterRoles();
      }
    });
  }

  loadUsers() {
    this.adminService.getCompanyUsers(this.companyId).subscribe({
      next: (users) => {
        this.users = users;
        this.filterUsers();
      },
      error: (err) => {
        console.error('Error loading users:', err);
        this.users = [];
        this.filterUsers();
      }
    });
  }

  filterVehicles() {
    this.filteredVehicles = this.vehicles.filter(v => {
      const matchesSearch = !this.vehicleSearch || 
        v.name.toLowerCase().includes(this.vehicleSearch.toLowerCase()) ||
        (v.plate && v.plate.toLowerCase().includes(this.vehicleSearch.toLowerCase()));
      const matchesStatus = this.vehicleStatusFilter === 'all' || v.status === this.vehicleStatusFilter;
      return matchesSearch && matchesStatus;
    });
  }

  filterRoles() {
    this.filteredRoles = this.roles.filter(r => {
      return !this.roleSearch || r.name.toLowerCase().includes(this.roleSearch.toLowerCase());
    });
  }

  filterUsers() {
    this.filteredUsers = this.users.filter(u => {
      const matchesSearch = !this.userSearch || 
        u.name.toLowerCase().includes(this.userSearch.toLowerCase()) ||
        u.email.toLowerCase().includes(this.userSearch.toLowerCase()) ||
        (u.cin && u.cin.toLowerCase().includes(this.userSearch.toLowerCase()));
      const matchesStatus = this.userStatusFilter === 'all' || u.status === this.userStatusFilter;
      return matchesSearch && matchesStatus;
    });
  }

  goBack() {
    this.router.navigate(['/admin/clients']);
  }

  editCompany() {
    this.router.navigate(['/admin/clients'], { queryParams: { edit: this.companyId } });
  }

  formatDate(date: Date): string {
    return new Date(date).toLocaleDateString('fr-FR', { 
      year: 'numeric', 
      month: 'long', 
      day: 'numeric' 
    });
  }

  getVehicleStatusLabel(status: string): string {
    const labels: Record<string, string> = {
      'available': 'Disponible',
      'in_use': 'En service',
      'maintenance': 'Maintenance'
    };
    return labels[status] || status;
  }

  getRoleTypeLabel(type: string): string {
    const labels: Record<string, string> = {
      'system_admin': 'Administrateur système',
      'company_admin': 'Administrateur société',
      'employee': 'Employé',
      'custom': 'Personnalisé'
    };
    return labels[type] || type;
  }


  // Vehicle actions
  openAddVehicle() {
    this.selectedVehicle = null;
    this.showVehiclePopup = true;
  }

  editVehicle(vehicle: AdminVehicle) {
    this.selectedVehicle = {
      id: vehicle.id,
      name: vehicle.name,
      plate: vehicle.plate,
      brand: vehicle.brand,
      model: vehicle.model,
      year: vehicle.year,
      type: vehicle.type,
      status: vehicle.status,
      mileage: vehicle.mileage,
      hasGPS: vehicle.hasGps,
      gpsDeviceId: vehicle.gpsDeviceId
    };
    this.showVehiclePopup = true;
  }

  closeVehiclePopup() {
    this.showVehiclePopup = false;
    this.selectedVehicle = null;
  }

  onVehicleSaved(formData: any) {
    const vehicleData = {
      name: formData.name,
      type: formData.type,
      brand: formData.brand || undefined,
      model: formData.model || undefined,
      plate: formData.plate || undefined,
      year: formData.year || undefined,
      status: formData.status,
      mileage: formData.mileage || 0,
      companyId: this.companyId,
      hasGps: formData.hasGPS || false,
      gpsDeviceId: formData.hasGPS ? formData.gpsDeviceId : undefined
    };

    if (this.selectedVehicle?.id) {
      this.adminService.updateVehicle(this.selectedVehicle.id, vehicleData).subscribe({
        next: () => {
          this.closeVehiclePopup();
          this.loadVehicles();
        },
        error: (err) => {
          console.error('Error updating vehicle:', err);
          alert('Erreur lors de la modification du véhicule');
        }
      });
    } else {
      this.adminService.createVehicle(vehicleData).subscribe({
        next: () => {
          this.closeVehiclePopup();
          this.loadVehicles();
        },
        error: (err) => {
          console.error('Error creating vehicle:', err);
          alert('Erreur lors de la création du véhicule');
        }
      });
    }
  }

  // Role actions
  openAddRole() {
    this.editingRole = null;
    this.roleForm = {
      name: '',
      description: '',
      roleType: 'employee',
      isDefault: false,
      permissions: {}
    };
    this.showRoleModal = true;
  }

  editRole(role: CompanyRole) {
    this.editingRole = role;
    this.roleForm = {
      name: role.name,
      description: role.description || '',
      roleType: role.roleType,
      isDefault: role.isDefault,
      permissions: role.permissions ? { ...role.permissions } : {}
    };
    this.showRoleModal = true;
  }

  closeRoleModal() {
    this.showRoleModal = false;
    this.editingRole = null;
  }

  onPermissionsChange(permissions: Record<string, any>) {
    this.roleForm.permissions = permissions;
  }

  saveRole() {
    const roleData = {
      name: this.roleForm.name,
      description: this.roleForm.description || undefined,
      roleType: this.roleForm.roleType,
      isDefault: this.roleForm.isDefault,
      permissions: this.roleForm.permissions as Record<string, any>
    };

    if (this.editingRole) {
      this.adminService.updateRole(this.editingRole.id, roleData).subscribe({
        next: () => {
          this.closeRoleModal();
          this.loadRoles();
        },
        error: (err) => {
          console.error('Error updating role:', err);
          alert('Erreur lors de la modification du rôle');
        }
      });
    } else {
      this.adminService.createRole(roleData).subscribe({
        next: () => {
          this.closeRoleModal();
          this.loadRoles();
        },
        error: (err) => {
          console.error('Error creating role:', err);
          alert('Erreur lors de la création du rôle');
        }
      });
    }
  }

  deleteRole(role: CompanyRole) {
    if (!confirm(`Êtes-vous sûr de vouloir supprimer le rôle "${role.name}" ?`)) {
      return;
    }

    this.adminService.deleteRole(role.id).subscribe({
      next: () => this.loadRoles(),
      error: (err) => {
        console.error('Error deleting role:', err);
        alert('Erreur lors de la suppression du rôle');
      }
    });
  }

  // User actions
  openAddUser() {
    this.editingUser = null;
    this.userForm = {
      firstName: '',
      lastName: '',
      dateOfBirth: '',
      cin: '',
      email: '',
      password: '',
      phone: '',
      roleId: null,
      assignedVehicleIds: []
    };
    this.showUserModal = true;
  }

  editUser(user: SystemUser) {
    this.editingUser = user;
    const nameParts = user.name.split(' ');
    this.userForm = {
      firstName: nameParts[0] || '',
      lastName: nameParts.slice(1).join(' ') || '',
      dateOfBirth: user.dateOfBirth ? new Date(user.dateOfBirth).toISOString().split('T')[0] : '',
      cin: user.cin || '',
      email: user.email,
      password: '',
      phone: user.phone || '',
      roleId: user.roleId || null,
      assignedVehicleIds: user.assignedVehicleIds || []
    };
    this.showUserModal = true;
  }

  closeUserModal() {
    this.showUserModal = false;
    this.editingUser = null;
  }

  isUserFormValid(): boolean {
    const f = this.userForm;
    const basicValid = f.firstName.trim() && f.lastName.trim() && f.email.trim() && f.cin.trim();
    if (this.editingUser) {
      return !!basicValid;
    }
    return !!(basicValid && f.password.trim());
  }

  isVehicleAssigned(vehicleId: number): boolean {
    return this.userForm.assignedVehicleIds.includes(vehicleId);
  }

  toggleVehicleAssignment(vehicleId: number) {
    const index = this.userForm.assignedVehicleIds.indexOf(vehicleId);
    if (index === -1) {
      this.userForm.assignedVehicleIds.push(vehicleId);
    } else {
      this.userForm.assignedVehicleIds.splice(index, 1);
    }
  }

  saveUser() {
    const fullName = `${this.userForm.firstName.trim()} ${this.userForm.lastName.trim()}`;
    
    if (this.editingUser) {
      const updateData: any = {
        name: fullName,
        email: this.userForm.email,
        phone: this.userForm.phone || undefined,
        dateOfBirth: this.userForm.dateOfBirth ? new Date(this.userForm.dateOfBirth) : undefined,
        cin: this.userForm.cin,
        roleId: this.userForm.roleId,
        assignedVehicleIds: this.userForm.assignedVehicleIds
      };
      if (this.userForm.password) {
        updateData.password = this.userForm.password;
      }

      this.adminService.updateUser(this.editingUser.id, updateData).subscribe({
        next: () => {
          this.closeUserModal();
          this.loadUsers();
        },
        error: (err) => {
          console.error('Error updating user:', err);
          alert(err.error?.message || 'Erreur lors de la modification de l\'utilisateur');
        }
      });
    } else {
      const userData = {
        name: fullName,
        email: this.userForm.email,
        password: this.userForm.password,
        phone: this.userForm.phone || undefined,
        dateOfBirth: this.userForm.dateOfBirth ? new Date(this.userForm.dateOfBirth) : undefined,
        cin: this.userForm.cin,
        companyId: this.companyId,
        roleId: this.userForm.roleId || undefined,
        assignedVehicleIds: this.userForm.assignedVehicleIds
      };

      this.adminService.createUser(userData).subscribe({
        next: () => {
          this.closeUserModal();
          this.loadUsers();
        },
        error: (err) => {
          console.error('Error creating user:', err);
          alert(err.error?.message || 'Erreur lors de la création de l\'utilisateur');
        }
      });
    }
  }

  toggleUserStatus(user: SystemUser) {
    if (user.status === 'active') {
      this.adminService.suspendUser(user.id).subscribe({
        next: () => this.loadUsers(),
        error: (err) => {
          console.error('Error suspending user:', err);
          alert('Erreur lors de la suspension de l\'utilisateur');
        }
      });
    } else {
      this.adminService.activateUser(user.id).subscribe({
        next: () => this.loadUsers(),
        error: (err) => {
          console.error('Error activating user:', err);
          alert('Erreur lors de l\'activation de l\'utilisateur');
        }
      });
    }
  }
}
