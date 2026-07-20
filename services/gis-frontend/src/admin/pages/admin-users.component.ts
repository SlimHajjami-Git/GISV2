import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { BehaviorSubject, Subject, combineLatest, forkJoin } from 'rxjs';
import { map, takeUntil, finalize } from 'rxjs/operators';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService, SystemUser, Client, Role } from '../services/admin.service';

interface EditUserForm {
  name: string;
  email: string;
  phone: string;
  status: 'active' | 'suspended' | 'inactive';
  roleId: number | null;
  password: string;
}

@Component({
  selector: 'admin-users',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent],
  template: `
    <admin-layout pageTitle="User Supervision">
      <div class="users-page">
        <div class="page-header">
          <div class="header-left">
            <div class="search-box">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
              </svg>
              <input type="text" [(ngModel)]="searchQuery" (ngModelChange)="onSearchChange($event)" placeholder="Search users..." />
            </div>
            <select class="filter-select" [(ngModel)]="companyFilter" (ngModelChange)="onCompanyChange($event)">
              <option value="all">All Companies</option>
              <option *ngFor="let company of (companies$ | async) || []" [value]="company.id">{{ company.name }}</option>
            </select>
            <select class="filter-select" [(ngModel)]="statusFilter" (ngModelChange)="onStatusChange($event)">
              <option value="all">All Status</option>
              <option value="active">Active</option>
              <option value="suspended">Suspended</option>
              <option value="online">Online Now</option>
            </select>
          </div>
          <div class="header-stats">
            <div class="stat-chip online">
              <span class="dot"></span>
              {{ (onlineCount$ | async) || 0 }} Online
            </div>
            <div class="stat-chip total">
              {{ (totalCount$ | async) || 0 }} Total Users
            </div>
          </div>
        </div>

        <div class="users-table-container">
          <table class="users-table">
            <thead>
              <tr>
                <th>Utilisateur</th>
                <th>Société</th>
                <th>Rôle</th>
                <th>Statut</th>
                <th>Dernière connexion</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let user of (filteredUsers$ | async); trackBy: trackByUserId" [class.suspended]="user.status === 'suspended'">
                <td>
                  <div class="user-cell">
                    <div class="user-avatar" [class.online]="user.isOnline">
                      {{ user.name?.charAt(0) || 'U' }}
                      <span class="online-indicator" *ngIf="user.isOnline"></span>
                    </div>
                    <div class="user-info">
                      <span class="user-name">{{ user.name }}</span>
                      <span class="user-email">{{ user.email }}</span>
                    </div>
                  </div>
                </td>
                <td>
                  <span class="company-badge">{{ user.companyName }}</span>
                </td>
                <td>
                  <span class="role-tag">{{ user.roleName || '—' }}</span>
                </td>
                <td>
                  <span class="status-badge" [class]="user.status">{{ user.status | titlecase }}</span>
                </td>
                <td>
                  <span class="last-login">{{ formatDate(user.lastLoginAt) }}</span>
                </td>
                <td>
                  <div class="actions">
                    <button class="action-btn primary" (click)="openEditModal(user)" title="Modifier l'utilisateur">
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                        <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                      </svg>
                    </button>
                    <button class="action-btn" [class.suspend]="user.status === 'active'" [class.activate]="user.status !== 'active'"
                            (click)="toggleUserStatus(user)" [title]="user.status === 'active' ? 'Suspendre' : 'Activer'">
                      <svg *ngIf="user.status === 'active'" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <circle cx="12" cy="12" r="10"/><line x1="4.93" y1="4.93" x2="19.07" y2="19.07"/>
                      </svg>
                      <svg *ngIf="user.status !== 'active'" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22,4 12,14.01 9,11.01"/>
                      </svg>
                    </button>
                    <button class="action-btn view" (click)="viewUserActivity(user)" title="Voir l'activité">
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <polyline points="22,12 18,12 15,21 9,3 6,12 2,12"/>
                      </svg>
                    </button>
                    <button class="action-btn danger" (click)="askDelete(user)" title="Supprimer">
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
        </div>

        <!-- Calypso 7 — modale unifiee « Modifier l utilisateur ».
             Auparavant la page n offrait qu une edition de permissions a
             plat qui appelait un endpoint inexistant (404). Maintenant on
             expose les vraies operations supportees par AdminUserController :
             update info, change role, reset password, suspend/activate. -->
        <div class="popup-overlay" *ngIf="showEditModal" (click)="closeModal()">
          <div class="popup-container edit-user-popup" (click)="$event.stopPropagation()">
            <div class="popup-header">
              <div class="header-title">
                <div class="header-icon">
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                    <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                  </svg>
                </div>
                <h2>Modifier l'utilisateur</h2>
              </div>
              <button class="close-btn" (click)="closeModal()" title="Fermer">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                </svg>
              </button>
            </div>

            <div class="popup-body" *ngIf="selectedUser && editForm">
              <div class="user-header">
                <div class="user-avatar large">{{ editForm.name?.charAt(0) || 'U' }}</div>
                <div>
                  <h3>{{ selectedUser.name }}</h3>
                  <span class="user-company">{{ selectedUser.companyName }}</span>
                </div>
              </div>

              <div class="form-grid">
                <div class="form-field">
                  <label>Nom complet *</label>
                  <input type="text" [(ngModel)]="editForm.name" placeholder="Prénom Nom" />
                </div>
                <div class="form-field">
                  <label>Email *</label>
                  <input type="email" [(ngModel)]="editForm.email" placeholder="email@domaine.com" />
                </div>
                <div class="form-field">
                  <label>Téléphone</label>
                  <input type="tel" [(ngModel)]="editForm.phone" placeholder="+216 …" />
                </div>
                <div class="form-field">
                  <label>Statut</label>
                  <select [(ngModel)]="editForm.status">
                    <option value="active">Actif</option>
                    <option value="suspended">Suspendu</option>
                    <option value="inactive">Inactif</option>
                  </select>
                </div>
                <div class="form-field full">
                  <label>Rôle (définit les droits)</label>
                  <select [(ngModel)]="editForm.roleId">
                    <option [ngValue]="null" *ngIf="!editForm.roleId">— Choisir —</option>
                    <option *ngFor="let r of companyRoles" [ngValue]="r.id">
                      {{ r.name }}{{ r.roleType === 'company_admin' ? ' (admin société)' : '' }}
                    </option>
                  </select>
                  <p class="field-hint" *ngIf="selectedRole?.description">{{ selectedRole?.description }}</p>
                  <p class="field-hint muted" *ngIf="loadingRoles">Chargement des rôles…</p>
                </div>
                <div class="form-field full">
                  <label>Réinitialiser le mot de passe (optionnel)</label>
                  <input type="text" [(ngModel)]="editForm.password" placeholder="Laisser vide pour ne pas changer" />
                  <p class="field-hint">Au moins 8 caractères. L'utilisateur devra se reconnecter.</p>
                </div>
              </div>

              <p class="form-error" *ngIf="editError">{{ editError }}</p>
            </div>

            <div class="popup-footer">
              <button class="btn-secondary" (click)="closeModal()" [disabled]="saving">Annuler</button>
              <button class="btn-primary" (click)="saveEditUser()" [disabled]="saving">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/>
                </svg>
                {{ saving ? 'Enregistrement…' : 'Enregistrer' }}
              </button>
            </div>
          </div>
        </div>

        <!-- Delete confirmation -->
        <div class="popup-overlay" *ngIf="userToDelete" (click)="userToDelete = null">
          <div class="popup-container delete-popup" (click)="$event.stopPropagation()">
            <div class="popup-header">
              <div class="header-title">
                <div class="header-icon danger">
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polyline points="3 6 5 6 21 6"/>
                    <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                  </svg>
                </div>
                <h2>Supprimer l'utilisateur ?</h2>
              </div>
            </div>
            <div class="popup-body">
              <p>Voulez-vous vraiment supprimer <strong>{{ userToDelete.name }}</strong> ({{ userToDelete.email }}) ?</p>
              <p class="warning">Cette action est définitive. L'utilisateur perdra immédiatement l'accès.</p>
            </div>
            <div class="popup-footer">
              <button class="btn-secondary" (click)="userToDelete = null">Annuler</button>
              <button class="btn-danger" (click)="confirmDelete()">Supprimer</button>
            </div>
          </div>
        </div>
      </div>
    </admin-layout>
  `,
  styles: [`
    .users-page {
      display: flex;
      flex-direction: column;
      gap: 24px;
    }

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
      flex-wrap: wrap;
    }

    .search-box {
      display: flex;
      align-items: center;
      gap: 10px;
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      padding: 9px 14px;
      width: 260px;
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

    .search-box input::placeholder { color: var(--adm-sub); }

    .filter-select {
      padding: 9px 14px;
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

    .header-stats {
      display: flex;
      gap: 12px;
    }

    .stat-chip {
      padding: 7px 14px;
      border-radius: 999px;
      font-size: 12px;
      font-weight: 700;
      display: flex;
      align-items: center;
      gap: 8px;
      font-variant-numeric: tabular-nums;
    }

    .stat-chip.online {
      background: rgba(5, 150, 105, 0.10);
      color: var(--adm-green-ink);
    }

    .stat-chip.total {
      background: rgba(100, 116, 139, 0.10);
      color: var(--adm-slate-ink);
    }

    .stat-chip .dot {
      width: 8px;
      height: 8px;
      background: var(--adm-green);
      border-radius: 50%;
      animation: pulse 2s infinite;
    }

    @keyframes pulse {
      0%, 100% { opacity: 1; }
      50% { opacity: 0.5; }
    }

    .users-table-container {
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 16px;
      box-shadow: var(--adm-shadow);
      overflow: hidden;
      animation: rise 0.3s ease-out;
    }

    @keyframes rise {
      from { opacity: 0; transform: translateY(8px); }
      to { opacity: 1; transform: none; }
    }

    .users-table {
      width: 100%;
      border-collapse: collapse;
    }

    .users-table th {
      padding: 14px 20px;
      text-align: left;
      font-size: 11px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: var(--adm-sub);
      background: #f8fafc;
      border-bottom: 1px solid var(--adm-border);
    }

    .users-table td {
      padding: 14px 20px;
      border-bottom: 1px solid #eef2f7;
      font-size: 13px;
      color: var(--adm-ink);
    }

    .users-table tbody tr { transition: background 0.15s; }
    .users-table tbody tr:hover { background: #f8fafc; }

    .users-table tr:last-child td {
      border-bottom: none;
    }

    .users-table tr.suspended {
      opacity: 0.6;
    }

    .user-cell {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .user-avatar {
      width: 40px;
      height: 40px;
      background: linear-gradient(135deg, #6366f1 0%, var(--adm-indigo) 100%);
      border-radius: 10px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 600;
      font-size: 16px;
      color: #fff;
      position: relative;
    }

    .user-avatar.large {
      width: 56px;
      height: 56px;
      font-size: 22px;
    }

    .online-indicator {
      position: absolute;
      bottom: -2px;
      right: -2px;
      width: 12px;
      height: 12px;
      background: var(--adm-green);
      border: 2px solid var(--adm-card);
      border-radius: 50%;
    }

    .user-info {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .user-name {
      font-size: 13px;
      font-weight: 600;
      color: var(--adm-ink);
    }

    .user-email {
      font-size: 12px;
      color: var(--adm-sub);
    }

    .company-badge {
      padding: 4px 10px;
      background: rgba(100, 116, 139, 0.10);
      border-radius: 999px;
      font-size: 11px;
      font-weight: 700;
      color: var(--adm-slate-ink);
    }

    .roles {
      display: flex;
      gap: 6px;
      flex-wrap: wrap;
    }

    .role-tag {
      padding: 4px 10px;
      background: rgba(79, 70, 229, 0.10);
      color: var(--adm-indigo-ink);
      border-radius: 999px;
      font-size: 11px;
      font-weight: 700;
      text-transform: capitalize;
    }

    .permissions-cell {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .perm-count {
      font-size: 13px;
      color: var(--adm-sub);
    }

    .edit-perms-btn {
      width: 28px;
      height: 28px;
      border: none;
      background: rgba(79, 70, 229, 0.10);
      border-radius: 6px;
      color: var(--adm-indigo);
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }

    .edit-perms-btn:hover {
      background: rgba(79, 70, 229, 0.18);
    }

    .status-badge {
      padding: 4px 10px;
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

    .status-badge.inactive {
      background: rgba(100, 116, 139, 0.10);
      color: var(--adm-slate-ink);
    }

    .last-login {
      font-size: 13px;
      color: var(--adm-sub);
      font-variant-numeric: tabular-nums;
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

    .action-btn.suspend {
      background: rgba(220, 38, 38, 0.10);
      color: var(--adm-red);
    }

    .action-btn.activate {
      background: rgba(5, 150, 105, 0.10);
      color: var(--adm-green);
    }

    .action-btn.view {
      background: rgba(8, 145, 178, 0.10);
      color: var(--adm-cyan-ink);
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
      max-width: 600px;
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
      font-weight: 700;
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
      background: var(--adm-card);
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
    }

    .section-title svg {
      color: var(--adm-indigo);
    }

    .popup-footer {
      padding: 16px 24px;
      border-top: 1px solid var(--adm-border);
      display: flex;
      gap: 12px;
      justify-content: flex-end;
      background: var(--adm-card);
    }

    .popup-footer .btn-secondary,
    .btn-secondary {
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

    .popup-footer .btn-secondary:hover,
    .btn-secondary:hover {
      background: #f8fafc;
      border-color: var(--adm-indigo);
    }

    .popup-footer .btn-primary,
    .btn-primary {
      padding: 9px 18px;
      background: var(--adm-indigo);
      color: #fff;
      border: none;
      border-radius: 10px;
      font-weight: 600;
      font-size: 13px;
      cursor: pointer;
      display: flex;
      align-items: center;
      gap: 8px;
      transition: all 0.2s;
      box-shadow: 0 2px 4px rgba(79, 70, 229, 0.25);
    }

    .popup-footer .btn-primary:hover,
    .btn-primary:hover {
      background: var(--adm-indigo-ink);
      transform: translateY(-1px);
      box-shadow: 0 4px 12px rgba(79, 70, 229, 0.35);
    }

    .user-header {
      display: flex;
      align-items: center;
      gap: 16px;
    }

    .user-header h3 {
      margin: 0 0 4px 0;
      font-size: 18px;
      color: var(--adm-ink);
    }

    .user-company {
      font-size: 14px;
      color: var(--adm-sub);
    }

    .permissions-section h4 {
      margin: 0 0 6px 0;
      font-size: 15px;
      font-weight: 600;
      color: var(--adm-ink);
    }

    .section-desc {
      margin: 0 0 16px 0;
      font-size: 13px;
      color: var(--adm-sub);
    }

    .permissions-grid {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 12px;
    }

    .permission-item {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px 14px;
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      cursor: pointer;
      transition: all 0.2s;
      position: relative;
    }

    .permission-item:hover {
      background: #f8fafc;
    }

    .permission-item input {
      position: absolute;
      opacity: 0;
    }

    .permission-item .checkmark {
      width: 20px;
      height: 20px;
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 5px;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }

    .permission-item input:checked ~ .checkmark {
      background: var(--adm-indigo);
      border-color: var(--adm-indigo);
    }

    .permission-item input:checked ~ .checkmark::after {
      content: '';
      width: 5px;
      height: 10px;
      border: solid #fff;
      border-width: 0 2px 2px 0;
      transform: rotate(45deg);
    }

    .permission-label {
      font-size: 14px;
      color: var(--adm-ink);
    }

    .quick-actions {
      display: flex;
      gap: 12px;
      margin-top: 16px;
    }

    .quick-btn {
      padding: 8px 16px;
      background: #fff;
      border: 1px solid var(--adm-border);
      border-radius: 8px;
      color: var(--adm-sub);
      font-size: 13px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .quick-btn:hover {
      background: #f8fafc;
      border-color: var(--adm-indigo);
      color: var(--adm-ink);
    }

    .modal-footer {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 20px 24px;
      border-top: 1px solid var(--adm-border);
    }

    .btn-primary:disabled, .btn-secondary:disabled { opacity: 0.5; cursor: not-allowed; }

    .btn-danger {
      padding: 9px 18px;
      background: var(--adm-red);
      border: none;
      border-radius: 10px;
      color: #fff;
      font-size: 13px;
      font-weight: 600;
      cursor: pointer;
      transition: background 0.2s;
    }
    .btn-danger:hover { background: var(--adm-red-ink); }

    /* Calypso 7 — refonte modale Edit User */
    .edit-user-popup, .delete-popup { max-width: 560px; }
    .edit-user-popup .popup-body { padding: 20px 24px; }
    .delete-popup .popup-body { padding: 20px 24px; }
    .delete-popup .popup-body p { margin: 0 0 8px 0; color: var(--adm-ink); font-size: 14px; }

    .user-header {
      display: flex; align-items: center; gap: 14px;
      padding-bottom: 16px;
      border-bottom: 1px solid var(--adm-border);
      margin-bottom: 20px;
    }
    .user-avatar.large {
      width: 48px; height: 48px;
      border-radius: 50%;
      display: flex; align-items: center; justify-content: center;
      background: linear-gradient(135deg, #6366f1 0%, var(--adm-indigo) 100%);
      color: #fff; font-size: 18px; font-weight: 700;
    }
    .user-header h3 { margin: 0; color: var(--adm-ink); font-size: 16px; }
    .user-company { color: var(--adm-sub); font-size: 12px; }

    .form-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 14px 16px;
    }
    .form-field { display: flex; flex-direction: column; gap: 6px; }
    .form-field.full { grid-column: 1 / -1; }
    .form-field label {
      font-size: 11px; font-weight: 700;
      color: var(--adm-sub);
      text-transform: uppercase;
      letter-spacing: 0.06em;
    }
    .form-field input, .form-field select {
      padding: 9px 12px;
      background: #fff;
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      color: var(--adm-ink);
      font-size: 13px;
      outline: none;
      transition: border-color 0.2s, box-shadow 0.2s;
    }
    .form-field input:focus, .form-field select:focus {
      border-color: var(--adm-indigo);
      box-shadow: 0 0 0 3px rgba(79, 70, 229, 0.12);
    }
    .field-hint {
      font-size: 11px;
      color: var(--adm-sub);
      margin: 0;
    }
    .field-hint.muted { font-style: italic; }
    .form-error {
      margin-top: 14px;
      padding: 10px 12px;
      background: rgba(220, 38, 38, 0.08);
      border: 1px solid rgba(220, 38, 38, 0.25);
      border-radius: 8px;
      color: var(--adm-red-ink);
      font-size: 12px;
    }

    .header-icon.danger { background: rgba(220, 38, 38, 0.10); color: var(--adm-red); }
    .delete-popup .warning {
      color: var(--adm-red-ink);
      font-size: 12px;
      margin-top: 8px;
    }

    .action-btn.primary {
      background: rgba(79, 70, 229, 0.10);
      color: var(--adm-indigo);
      border-color: rgba(79, 70, 229, 0.3);
    }
    .action-btn.primary:hover { background: rgba(79, 70, 229, 0.18); }
    .action-btn.danger {
      background: rgba(220, 38, 38, 0.10);
      color: var(--adm-red);
      border-color: rgba(220, 38, 38, 0.3);
    }
    .action-btn.danger:hover { background: rgba(220, 38, 38, 0.18); }

    @media (max-width: 640px) {
      .form-grid { grid-template-columns: 1fr; }
    }

    @media (prefers-reduced-motion: reduce) {
      .users-table-container,
      .popup-overlay,
      .popup-container,
      .stat-chip .dot {
        animation: none;
      }
      .action-btn:hover,
      .btn-primary:hover,
      .popup-footer .btn-primary:hover {
        transform: none;
      }
    }
  `]
})
export class AdminUsersComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();

  // Calypso 7 — bug tenace : la liste affichait 0 utilisateurs au chargement
  // et il fallait cliquer sur un filtre pour qu elle apparaisse. Le callback
  // HTTP arrivait hors de la zone Angular (probablement a cause des switchMap
  // dans l auth interceptor pour le refresh proactif), donc ni
  // cdr.detectChanges() ni NgZone.run() ne suffisaient a re-rendre le *ngFor.
  //
  // Solution definitive : transformer l etat (users, companies, filtres) en
  // BehaviorSubject + utiliser l async pipe dans le template. L async pipe
  // gere lui-meme la souscription et appelle markForCheck() a chaque emission,
  // ce qui force un cycle CD peu importe l etat de la zone qui a emis la
  // valeur. C est le pattern Angular canonique pour ce type de bug.
  private usersSubject = new BehaviorSubject<SystemUser[]>([]);
  private companiesSubject = new BehaviorSubject<Client[]>([]);
  private searchSubject = new BehaviorSubject<string>('');
  private companyFilterSubject = new BehaviorSubject<string>('all');
  private statusFilterSubject = new BehaviorSubject<string>('all');

  users$ = this.usersSubject.asObservable();
  companies$ = this.companiesSubject.asObservable();
  totalCount$ = this.usersSubject.pipe(map(users => users.length));
  onlineCount$ = this.usersSubject.pipe(map(users => users.filter(u => u.isOnline).length));
  filteredUsers$ = combineLatest([
    this.usersSubject,
    this.searchSubject,
    this.companyFilterSubject,
    this.statusFilterSubject
  ]).pipe(
    map(([users, search, companyFilter, statusFilter]) => {
      const q = (search || '').toLowerCase();
      return users.filter(user => {
        const matchesSearch = !q ||
          user.name?.toLowerCase().includes(q) ||
          user.email?.toLowerCase().includes(q);
        const matchesCompany = companyFilter === 'all' || user.companyId?.toString() === companyFilter;
        const matchesStatus = statusFilter === 'all' ||
          (statusFilter === 'online' && user.isOnline) ||
          (statusFilter !== 'online' && user.status === statusFilter);
        return matchesSearch && matchesCompany && matchesStatus;
      });
    })
  );

  // ngModel state — purement UI, sert juste a binder les inputs.
  searchQuery = '';
  companyFilter = 'all';
  statusFilter = 'all';

  // Edit modal state (pas dans un Subject, modale ouverte par clic donc
  // deja dans la zone)
  showEditModal = false;
  selectedUser: SystemUser | null = null;
  editForm: EditUserForm | null = null;
  companyRoles: Role[] = [];
  loadingRoles = false;
  saving = false;
  editError = '';

  // Delete confirmation
  userToDelete: SystemUser | null = null;

  get selectedRole(): Role | undefined {
    if (!this.editForm?.roleId) return undefined;
    return this.companyRoles.find(r => r.id === this.editForm!.roleId);
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

  loadData() {
    console.log('[admin-users] loadData() start, token=', !!localStorage.getItem('admin_token'));
    this.adminService.getUsers().pipe(takeUntil(this.destroy$)).subscribe({
      next: (users) => {
        console.log('[admin-users] getUsers() -> received', users?.length, 'users');
        this.usersSubject.next(users || []);
      },
      error: (err) => {
        console.error('[admin-users] getUsers() FAILED:', err);
        this.usersSubject.next([]);
      }
    });

    this.adminService.getClients().pipe(takeUntil(this.destroy$)).subscribe(clients => {
      this.companiesSubject.next(clients || []);
    });
  }

  // ─── Filter handlers (push to subjects → re-derive filteredUsers$) ────
  onSearchChange(value: string) {
    this.searchQuery = value;
    this.searchSubject.next(value);
  }

  onCompanyChange(value: string) {
    this.companyFilter = value;
    this.companyFilterSubject.next(value);
  }

  onStatusChange(value: string) {
    this.statusFilter = value;
    this.statusFilterSubject.next(value);
  }

  trackByUserId(_index: number, user: SystemUser): number {
    return user.id;
  }

  // Helpers internes pour les mutations qui doivent reinjecter dans le subject
  private get currentUsers(): SystemUser[] {
    return this.usersSubject.value;
  }

  private replaceUser(id: number, mutate: (u: SystemUser) => SystemUser) {
    const updated = this.currentUsers.map(u => u.id === id ? mutate(u) : u);
    this.usersSubject.next(updated);
  }

  // ─── Edit user modal ──────────────────────────────────────────────────
  openEditModal(user: SystemUser) {
    this.selectedUser = user;
    this.editForm = {
      name: user.name || '',
      email: user.email || '',
      phone: user.phone || '',
      status: (user.status as any) || 'active',
      roleId: user.roleId || null,
      password: ''
    };
    this.editError = '';
    this.showEditModal = true;

    // Charger les rôles disponibles pour la société de cet utilisateur
    // afin de pouvoir reassigner un rôle valide (Administrateur / Utilisateur
    // ou tout custom créé par cette société). Cf. CreateSocieteCommandHandler
    // qui crée 2 rôles par défaut.
    this.loadingRoles = true;
    this.companyRoles = [];
    this.adminService.getCompanyRoles(user.companyId).pipe(
      takeUntil(this.destroy$),
      finalize(() => this.loadingRoles = false)
    ).subscribe({
      next: (roles) => { this.companyRoles = roles || []; },
      error: () => { this.companyRoles = []; }
    });
  }

  saveEditUser() {
    if (!this.selectedUser || !this.editForm) return;
    if (!this.editForm.name?.trim() || !this.editForm.email?.trim()) {
      this.editError = 'Nom et email sont requis.';
      return;
    }
    if (this.editForm.password && this.editForm.password.length < 8) {
      this.editError = 'Le mot de passe doit faire au moins 8 caractères.';
      return;
    }

    const id = this.selectedUser.id;
    const f = this.editForm;
    const original = this.selectedUser;

    this.saving = true;
    this.editError = '';

    // Sequence : update info → role → status (si changes). On enchaine pour
    // garder l'erreur explicite si une etape echoue.
    const calls: any[] = [];

    // 1) Info de base + mot de passe optionnel via PUT /admin/users/:id
    calls.push(this.adminService.updateUser(id, {
      name: f.name.trim(),
      email: f.email.trim(),
      phone: f.phone?.trim() || undefined,
      ...(f.password ? { password: f.password } : {})
    }));

    // 2) Role (uniquement si change). On utilise l endpoint dedie
    //    PUT /admin/users/:id/role qui prend un RoleId entier et
    //    s appuie sur UpdateAdminUserRoleCommand cote backend.
    if (f.roleId && f.roleId !== original.roleId) {
      calls.push(this.adminService.updateUserRoleAssignment(id, f.roleId));
    }

    // 3) Statut (uniquement si change)
    if (f.status !== original.status) {
      if (f.status === 'suspended' || f.status === 'inactive') {
        calls.push(this.adminService.suspendUser(id));
      } else {
        calls.push(this.adminService.activateUser(id));
      }
    }

    forkJoin(calls).pipe(
      takeUntil(this.destroy$),
      finalize(() => { this.saving = false; this.cdr.detectChanges(); })
    ).subscribe({
      next: () => {
        // Mise a jour locale via le subject pour declencher le re-render
        // de filteredUsers$ via combineLatest.
        const newRoleName = (f.roleId && f.roleId !== original.roleId)
          ? this.companyRoles.find(r => r.id === f.roleId)?.name
          : original.roleName;
        this.replaceUser(id, u => ({
          ...u,
          name: f.name.trim(),
          email: f.email.trim(),
          phone: f.phone?.trim(),
          status: f.status as any,
          roleId: (f.roleId && f.roleId !== original.roleId) ? f.roleId : u.roleId,
          roleName: newRoleName
        }));
        this.closeModal();
      },
      error: (err) => {
        console.error('Update user failed:', err);
        this.editError = err?.error?.message || err?.error?.title || 'Échec de la mise à jour. Vérifiez les valeurs.';
        this.cdr.detectChanges();
      }
    });
  }

  closeModal() {
    this.showEditModal = false;
    this.selectedUser = null;
    this.editForm = null;
    this.companyRoles = [];
    this.editError = '';
  }

  // ─── Suspend / Activate (quick action) ────────────────────────────────
  toggleUserStatus(user: SystemUser) {
    const newStatus: 'active' | 'suspended' = user.status === 'active' ? 'suspended' : 'active';
    const obs = newStatus === 'suspended'
      ? this.adminService.suspendUser(user.id)
      : this.adminService.activateUser(user.id);
    obs.pipe(takeUntil(this.destroy$)).subscribe(() => {
      this.replaceUser(user.id, u => ({ ...u, status: newStatus }));
    });
  }

  // ─── Delete ───────────────────────────────────────────────────────────
  askDelete(user: SystemUser) {
    this.userToDelete = user;
  }

  confirmDelete() {
    if (!this.userToDelete) return;
    const id = this.userToDelete.id;
    this.adminService.deleteUser(id).pipe(takeUntil(this.destroy$)).subscribe({
      next: () => {
        this.usersSubject.next(this.currentUsers.filter(u => u.id !== id));
        this.userToDelete = null;
        this.cdr.detectChanges();
      },
      error: (err) => {
        console.error('Delete user failed:', err);
        this.userToDelete = null;
        this.cdr.detectChanges();
      }
    });
  }

  viewUserActivity(user: SystemUser) {
    this.router.navigate(['/admin/activity'], { queryParams: { userId: user.id } });
  }

  formatDate(date: Date | undefined): string {
    if (!date) return 'Jamais';
    return new Date(date).toLocaleDateString('fr-FR', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
    this.usersSubject.complete();
    this.companiesSubject.complete();
    this.searchSubject.complete();
    this.companyFilterSubject.complete();
    this.statusFilterSubject.complete();
  }
}
