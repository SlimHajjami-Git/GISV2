import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MockDataService } from '../services/mock-data.service';
import { AppLayoutComponent } from './shared/app-layout.component';
import { Company, Vehicle } from '../models/types';

interface UserRole {
  id: string;
  name: string;
  label: string;
  icon: string;
}

interface PagePermission {
  id: string;
  name: string;
  label: string;
  icon: string;
}

interface ManagedUser {
  id: string;
  name: string;
  email: string;
  phone: string;
  roles: string[];
  permissions: string[];
  assignedVehicles: string[];
  status: 'active' | 'inactive';
  createdAt: Date;
  lastLogin?: Date;
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
            <p class="subtitle">Gérez les accès et permissions de votre équipe</p>
          </div>
          <button class="btn-primary" (click)="openUserModal()">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"/>
              <line x1="5" y1="12" x2="19" y2="12"/>
            </svg>
            Nouvel Utilisateur
          </button>
        </div>

        <!-- Stats Cards -->
        <div class="stats-row">
          <div class="stat-card">
            <div class="stat-icon users">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/>
                <circle cx="9" cy="7" r="4"/>
                <path d="M23 21v-2a4 4 0 0 0-3-3.87"/>
                <path d="M16 3.13a4 4 0 0 1 0 7.75"/>
              </svg>
            </div>
            <div class="stat-info">
              <span class="stat-value">{{ users.length }}</span>
              <span class="stat-label">Utilisateurs</span>
            </div>
          </div>
          <div class="stat-card">
            <div class="stat-icon active">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                <polyline points="22 4 12 14.01 9 11.01"/>
              </svg>
            </div>
            <div class="stat-info">
              <span class="stat-value">{{ getActiveUsers() }}</span>
              <span class="stat-label">Actifs</span>
            </div>
          </div>
          <div class="stat-card">
            <div class="stat-icon drivers">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10"/>
                <circle cx="12" cy="10" r="3"/>
                <path d="M7 20.662V19a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v1.662"/>
              </svg>
            </div>
            <div class="stat-info">
              <span class="stat-value">{{ getDriversCount() }}</span>
              <span class="stat-label">Conducteurs</span>
            </div>
          </div>
          <div class="stat-card">
            <div class="stat-icon supervisors">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M12 2L2 7l10 5 10-5-10-5z"/>
                <path d="M2 17l10 5 10-5"/>
                <path d="M2 12l10 5 10-5"/>
              </svg>
            </div>
            <div class="stat-info">
              <span class="stat-value">{{ getSupervisorsCount() }}</span>
              <span class="stat-label">Superviseurs</span>
            </div>
          </div>
        </div>

        <!-- Users Table -->
        <div class="users-card">
          <div class="card-header">
            <div class="search-box">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="11" cy="11" r="8"/>
                <line x1="21" y1="21" x2="16.65" y2="16.65"/>
              </svg>
              <input type="text" [(ngModel)]="searchQuery" placeholder="Rechercher un utilisateur..." (input)="filterUsers()">
            </div>
            <div class="filter-group">
              <select [(ngModel)]="filterRole" (change)="filterUsers()">
                <option value="">Tous les rôles</option>
                <option value="driver">Conducteurs</option>
                <option value="supervisor">Superviseurs</option>
              </select>
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
                  <th>Rôles</th>
                  <th>Véhicules assignés</th>
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
                    <div class="roles-list">
                      <span *ngFor="let role of user.roles" class="role-badge" [class]="role">
                        {{ getRoleLabel(role) }}
                      </span>
                    </div>
                  </td>
                  <td>
                    <div class="vehicles-count">
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <rect x="1" y="3" width="15" height="13" rx="2"/>
                        <polygon points="16 8 20 8 23 11 23 16 16 16 16 8"/>
                        <circle cx="5.5" cy="18.5" r="2.5"/>
                        <circle cx="18.5" cy="18.5" r="2.5"/>
                      </svg>
                      <span>{{ user.assignedVehicles.length }} véhicule(s)</span>
                    </div>
                  </td>
                  <td>
                    <div class="permissions-count">
                      {{ user.permissions.length }}/{{ availablePermissions.length }} pages
                    </div>
                  </td>
                  <td>
                    <span class="status-badge" [class]="user.status">
                      {{ user.status === 'active' ? 'Actif' : 'Inactif' }}
                    </span>
                  </td>
                  <td>
                    <div class="action-buttons">
                      <button class="btn-icon" title="Modifier" (click)="editUser(user)">
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

            <div class="empty-state" *ngIf="filteredUsers.length === 0">
              <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/>
                <circle cx="9" cy="7" r="4"/>
                <line x1="17" y1="11" x2="23" y2="11"/>
              </svg>
              <p>Aucun utilisateur trouvé</p>
            </div>
          </div>
        </div>

        <!-- User Modal -->
        <div class="modal-overlay" *ngIf="showModal" (click)="closeModal()">
          <div class="modal-content" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h2>{{ editingUser ? 'Modifier' : 'Nouvel' }} Utilisateur</h2>
              <button class="btn-close" (click)="closeModal()">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="18" y1="6" x2="6" y2="18"/>
                  <line x1="6" y1="6" x2="18" y2="18"/>
                </svg>
              </button>
            </div>

            <div class="modal-body">
              <!-- Basic Info -->
              <div class="form-section">
                <h3>Informations de base</h3>
                <div class="form-grid">
                  <div class="form-group">
                    <label>Nom complet *</label>
                    <input type="text" [(ngModel)]="userForm.name" placeholder="Nom et prénom">
                  </div>
                  <div class="form-group">
                    <label>Email *</label>
                    <input type="email" [(ngModel)]="userForm.email" placeholder="email@exemple.com">
                  </div>
                  <div class="form-group">
                    <label>Téléphone</label>
                    <input type="tel" [(ngModel)]="userForm.phone" placeholder="+212 6XX XXX XXX">
                  </div>
                  <div class="form-group">
                    <label>Statut</label>
                    <select [(ngModel)]="userForm.status">
                      <option value="active">Actif</option>
                      <option value="inactive">Inactif</option>
                    </select>
                  </div>
                </div>
              </div>

              <!-- Roles -->
              <div class="form-section">
                <h3>Rôles</h3>
                <p class="section-desc">Un utilisateur peut avoir plusieurs rôles</p>
                <div class="roles-grid">
                  <label *ngFor="let role of availableRoles" class="role-checkbox" [class.selected]="isRoleSelected(role.id)">
                    <input type="checkbox" [checked]="isRoleSelected(role.id)" (change)="toggleRole(role.id)">
                    <div class="role-content">
                      <span class="role-icon" [innerHTML]="role.icon"></span>
                      <div class="role-info">
                        <span class="role-name">{{ role.label }}</span>
                      </div>
                    </div>
                    <svg class="check-icon" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
                      <polyline points="20 6 9 17 4 12"/>
                    </svg>
                  </label>
                </div>
              </div>

              <!-- Permissions -->
              <div class="form-section">
                <h3>Permissions d'accès aux pages</h3>
                <p class="section-desc">Sélectionnez les pages accessibles par cet utilisateur</p>
                <div class="permissions-grid">
                  <label *ngFor="let perm of availablePermissions" class="permission-checkbox" [class.selected]="isPermissionSelected(perm.id)">
                    <input type="checkbox" [checked]="isPermissionSelected(perm.id)" (change)="togglePermission(perm.id)">
                    <span class="perm-icon" [innerHTML]="perm.icon"></span>
                    <span class="perm-name">{{ perm.label }}</span>
                    <svg class="check-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
                      <polyline points="20 6 9 17 4 12"/>
                    </svg>
                  </label>
                </div>
                <div class="permission-actions">
                  <button class="btn-link" (click)="selectAllPermissions()">Tout sélectionner</button>
                  <button class="btn-link" (click)="deselectAllPermissions()">Tout désélectionner</button>
                </div>
              </div>

              <!-- Vehicle Assignment -->
              <div class="form-section">
                <h3>Véhicules assignés</h3>
                <p class="section-desc">Sélectionnez les véhicules que cet utilisateur peut gérer</p>
                <div class="vehicles-grid">
                  <label *ngFor="let vehicle of vehicles" class="vehicle-checkbox" [class.selected]="isVehicleSelected(vehicle.id)">
                    <input type="checkbox" [checked]="isVehicleSelected(vehicle.id)" (change)="toggleVehicle(vehicle.id)">
                    <div class="vehicle-info">
                      <span class="vehicle-icon">🚗</span>
                      <div class="vehicle-details">
                        <span class="vehicle-name">{{ vehicle.name }}</span>
                        <span class="vehicle-plate">{{ vehicle.plate }}</span>
                      </div>
                    </div>
                    <svg class="check-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
                      <polyline points="20 6 9 17 4 12"/>
                    </svg>
                  </label>
                </div>
                <div class="permission-actions" *ngIf="vehicles.length > 0">
                  <button class="btn-link" (click)="selectAllVehicles()">Tous les véhicules</button>
                  <button class="btn-link" (click)="deselectAllVehicles()">Aucun véhicule</button>
                </div>
              </div>
            </div>

            <div class="modal-footer">
              <button class="btn-secondary" (click)="closeModal()">Annuler</button>
              <button class="btn-primary" (click)="saveUser()">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/>
                  <polyline points="17 21 17 13 7 13 7 21"/>
                  <polyline points="7 3 7 8 15 8"/>
                </svg>
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
    }

    .role-badge.driver {
      background: #fef3c7;
      color: #92400e;
    }

    .role-badge.supervisor {
      background: #dbeafe;
      color: #1e40af;
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
export class UserManagementComponent implements OnInit {
  company: Company | null = null;
  vehicles: Vehicle[] = [];
  users: ManagedUser[] = [];
  filteredUsers: ManagedUser[] = [];

  searchQuery = '';
  filterRole = '';
  filterStatus = '';

  showModal = false;
  editingUser: ManagedUser | null = null;

  userForm = {
    name: '',
    email: '',
    phone: '',
    status: 'active' as 'active' | 'inactive',
    roles: [] as string[],
    permissions: [] as string[],
    assignedVehicles: [] as string[]
  };

  availableRoles: UserRole[] = [
    { id: 'driver', name: 'driver', label: 'Conducteur', icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><circle cx="12" cy="10" r="3"/><path d="M7 20.662V19a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v1.662"/></svg>' },
    { id: 'supervisor', name: 'supervisor', label: 'Superviseur', icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 2L2 7l10 5 10-5-10-5z"/><path d="M2 17l10 5 10-5"/><path d="M2 12l10 5 10-5"/></svg>' }
  ];

  availablePermissions: PagePermission[] = [
    { id: 'dashboard', name: 'dashboard', label: 'Tableau de bord', icon: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/><rect x="14" y="14" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/></svg>' },
    { id: 'monitoring', name: 'monitoring', label: 'Monitoring', icon: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><polygon points="16.24 7.76 14.12 14.12 7.76 16.24 9.88 9.88 16.24 7.76"/></svg>' },
    { id: 'vehicles', name: 'vehicles', label: 'Véhicules', icon: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="1" y="3" width="15" height="13" rx="2"/><polygon points="16 8 20 8 23 11 23 16 16 16 16 8"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/></svg>' },
    { id: 'drivers', name: 'drivers', label: 'Conducteurs', icon: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/></svg>' },
    { id: 'geofences', name: 'geofences', label: 'Geofencing', icon: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polygon points="1 6 1 22 8 18 16 22 23 18 23 2 16 6 8 2 1 6"/></svg>' },
    { id: 'reports', name: 'reports', label: 'Rapports', icon: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>' },
    { id: 'maintenance', name: 'maintenance', label: 'Maintenance', icon: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/></svg>' },
    { id: 'costs', name: 'costs', label: 'Coûts', icon: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="12" y1="1" x2="12" y2="23"/><path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/></svg>' },
    { id: 'gps-devices', name: 'gps-devices', label: 'Appareils GPS', icon: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="22" y1="12" x2="18" y2="12"/><line x1="6" y1="12" x2="2" y2="12"/><line x1="12" y1="6" x2="12" y2="2"/><line x1="12" y1="22" x2="12" y2="18"/></svg>' }
  ];

  constructor(
    private router: Router,
    private dataService: MockDataService
  ) {}

  ngOnInit() {
    if (!this.dataService.isAuthenticated()) {
      this.router.navigate(['/login']);
      return;
    }

    this.company = this.dataService.getCurrentCompany();
    if (this.company) {
      this.vehicles = this.dataService.getVehiclesByCompany(this.company.id);
    }

    this.loadMockUsers();
    this.filterUsers();
  }

  loadMockUsers() {
    this.users = [
      {
        id: '1',
        name: 'Ahmed Benali',
        email: 'ahmed.benali@example.com',
        phone: '+212 661 234 567',
        roles: ['driver'],
        permissions: ['dashboard', 'monitoring', 'vehicles'],
        assignedVehicles: this.vehicles.slice(0, 2).map(v => v.id),
        status: 'active',
        createdAt: new Date('2024-01-15'),
        lastLogin: new Date()
      },
      {
        id: '2',
        name: 'Karim Tazi',
        email: 'karim.tazi@example.com',
        phone: '+212 662 345 678',
        roles: ['driver', 'supervisor'],
        permissions: ['dashboard', 'monitoring', 'vehicles', 'drivers', 'geofences', 'reports'],
        assignedVehicles: this.vehicles.map(v => v.id),
        status: 'active',
        createdAt: new Date('2024-02-20'),
        lastLogin: new Date()
      },
      {
        id: '3',
        name: 'Youssef Alami',
        email: 'youssef.alami@example.com',
        phone: '+212 663 456 789',
        roles: ['supervisor'],
        permissions: ['dashboard', 'monitoring', 'vehicles', 'drivers', 'geofences', 'reports', 'maintenance', 'costs'],
        assignedVehicles: this.vehicles.map(v => v.id),
        status: 'active',
        createdAt: new Date('2024-03-10')
      },
      {
        id: '4',
        name: 'Omar Fassi',
        email: 'omar.fassi@example.com',
        phone: '+212 664 567 890',
        roles: ['driver'],
        permissions: ['dashboard', 'monitoring'],
        assignedVehicles: this.vehicles.slice(0, 1).map(v => v.id),
        status: 'inactive',
        createdAt: new Date('2024-04-05')
      }
    ];
  }

  filterUsers() {
    this.filteredUsers = this.users.filter(user => {
      const matchesSearch = !this.searchQuery || 
        user.name.toLowerCase().includes(this.searchQuery.toLowerCase()) ||
        user.email.toLowerCase().includes(this.searchQuery.toLowerCase());

      const matchesRole = !this.filterRole || user.roles.includes(this.filterRole);
      const matchesStatus = !this.filterStatus || user.status === this.filterStatus;

      return matchesSearch && matchesRole && matchesStatus;
    });
  }

  getInitials(name: string): string {
    return name.split(' ').map(n => n[0]).join('').substring(0, 2).toUpperCase();
  }

  getRoleLabel(roleId: string): string {
    const role = this.availableRoles.find(r => r.id === roleId);
    return role ? role.label : roleId;
  }

  getActiveUsers(): number {
    return this.users.filter(u => u.status === 'active').length;
  }

  getDriversCount(): number {
    return this.users.filter(u => u.roles.includes('driver')).length;
  }

  getSupervisorsCount(): number {
    return this.users.filter(u => u.roles.includes('supervisor')).length;
  }

  openUserModal() {
    this.editingUser = null;
    this.userForm = {
      name: '',
      email: '',
      phone: '',
      status: 'active',
      roles: [],
      permissions: ['dashboard', 'monitoring'],
      assignedVehicles: []
    };
    this.showModal = true;
  }

  editUser(user: ManagedUser) {
    this.editingUser = user;
    this.userForm = {
      name: user.name,
      email: user.email,
      phone: user.phone,
      status: user.status,
      roles: [...user.roles],
      permissions: [...user.permissions],
      assignedVehicles: [...user.assignedVehicles]
    };
    this.showModal = true;
  }

  closeModal() {
    this.showModal = false;
    this.editingUser = null;
  }

  isRoleSelected(roleId: string): boolean {
    return this.userForm.roles.includes(roleId);
  }

  toggleRole(roleId: string) {
    const index = this.userForm.roles.indexOf(roleId);
    if (index === -1) {
      this.userForm.roles.push(roleId);
    } else {
      this.userForm.roles.splice(index, 1);
    }
  }

  isPermissionSelected(permId: string): boolean {
    return this.userForm.permissions.includes(permId);
  }

  togglePermission(permId: string) {
    const index = this.userForm.permissions.indexOf(permId);
    if (index === -1) {
      this.userForm.permissions.push(permId);
    } else {
      this.userForm.permissions.splice(index, 1);
    }
  }

  selectAllPermissions() {
    this.userForm.permissions = this.availablePermissions.map(p => p.id);
  }

  deselectAllPermissions() {
    this.userForm.permissions = [];
  }

  isVehicleSelected(vehicleId: string): boolean {
    return this.userForm.assignedVehicles.includes(vehicleId);
  }

  toggleVehicle(vehicleId: string) {
    const index = this.userForm.assignedVehicles.indexOf(vehicleId);
    if (index === -1) {
      this.userForm.assignedVehicles.push(vehicleId);
    } else {
      this.userForm.assignedVehicles.splice(index, 1);
    }
  }

  selectAllVehicles() {
    this.userForm.assignedVehicles = this.vehicles.map(v => v.id);
  }

  deselectAllVehicles() {
    this.userForm.assignedVehicles = [];
  }

  saveUser() {
    if (!this.userForm.name || !this.userForm.email) {
      alert('Veuillez remplir le nom et l\'email');
      return;
    }

    if (this.userForm.roles.length === 0) {
      alert('Veuillez sélectionner au moins un rôle');
      return;
    }

    if (this.editingUser) {
      const index = this.users.findIndex(u => u.id === this.editingUser!.id);
      if (index !== -1) {
        this.users[index] = {
          ...this.editingUser,
          ...this.userForm
        };
      }
    } else {
      const newUser: ManagedUser = {
        id: Date.now().toString(),
        ...this.userForm,
        createdAt: new Date()
      };
      this.users.push(newUser);
    }

    this.filterUsers();
    this.closeModal();
  }

  deleteUser(user: ManagedUser) {
    if (confirm(`Êtes-vous sûr de vouloir supprimer ${user.name} ?`)) {
      this.users = this.users.filter(u => u.id !== user.id);
      this.filterUsers();
    }
  }
}
