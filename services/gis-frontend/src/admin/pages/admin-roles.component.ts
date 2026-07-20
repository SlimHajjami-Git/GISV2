import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService, Role } from '../services/admin.service';

@Component({
  selector: 'admin-roles',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent],
  template: `
    <admin-layout pageTitle="Gestion des Rôles">
      <div class="roles-container">
        <!-- Header -->
        <div class="page-header">
          <div class="header-left">
            <h2>Rôles</h2>
            <span class="count">{{ roles.length }} rôle(s)</span>
          </div>
          <button class="btn-primary" (click)="openCreateModal()">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
            </svg>
            Nouveau Rôle
          </button>
        </div>

        <!-- Roles Grid -->
        <div class="roles-grid">
          <div *ngFor="let role of roles" class="role-card" [class.system]="role.isSystem">
            <div class="role-header">
              <div class="role-icon" [class.system]="role.isSystem" [class.admin]="role.roleType === 'company_admin'">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>
                </svg>
              </div>
              <div class="role-info">
                <h3>{{ role.name }}</h3>
                <span class="role-type">{{ getRoleTypeLabel(role.roleType) }}</span>
              </div>
              <div class="role-badges">
                <span *ngIf="role.isSystem" class="badge system">Système</span>
                <span *ngIf="role.isDefault" class="badge default">Par défaut</span>
              </div>
            </div>
            
            <p class="role-description">{{ role.description || 'Aucune description' }}</p>
            
            <div class="role-stats">
              <div class="stat">
                <span class="stat-value">{{ role.usersCount }}</span>
                <span class="stat-label">Utilisateurs</span>
              </div>
              <div class="stat">
                <span class="stat-value">{{ getPermissionsCount(role) }}</span>
                <span class="stat-label">Permissions</span>
              </div>
            </div>
            
            <div class="role-actions" *ngIf="!role.isSystem">
              <button class="btn-icon" (click)="editRole(role)" title="Modifier">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                  <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                </svg>
              </button>
              <button class="btn-icon danger" (click)="confirmDelete(role)" title="Supprimer" [disabled]="role.usersCount > 0">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <polyline points="3,6 5,6 21,6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                </svg>
              </button>
            </div>
          </div>
        </div>

        <!-- Create/Edit Modal -->
        <div class="modal-overlay" *ngIf="showModal" (mousedown)="closeModal()">
          <div class="modal" (mousedown)="$event.stopPropagation()">
            <div class="modal-header">
              <h3>{{ editingRole ? 'Modifier le rôle' : 'Nouveau rôle' }}</h3>
              <button class="btn-close" (click)="closeModal()">×</button>
            </div>
            <div class="modal-body">
              <div class="form-group">
                <label>Nom du rôle *</label>
                <input type="text" [(ngModel)]="formData.name" placeholder="Ex: Manager, Chauffeur...">
              </div>
              <div class="form-group">
                <label>Description</label>
                <textarea [(ngModel)]="formData.description" placeholder="Description du rôle..."></textarea>
              </div>
              <div class="form-group">
                <label>Type de rôle</label>
                <select [(ngModel)]="formData.roleType">
                  <option value="employee">Employé</option>
                  <option value="company_admin">Administrateur</option>
                  <option value="custom">Personnalisé</option>
                </select>
              </div>
              <div class="form-group checkbox">
                <label>
                  <input type="checkbox" [(ngModel)]="formData.isDefault">
                  Rôle par défaut pour les nouveaux utilisateurs
                </label>
              </div>
              
              <div class="permissions-section">
                <h4>Permissions</h4>
                <div class="permissions-grid">
                  <label *ngFor="let perm of availablePermissions" class="permission-item">
                    <input type="checkbox" [checked]="hasPermission(perm.key)" (change)="togglePermission(perm.key)">
                    <span>{{ perm.label }}</span>
                  </label>
                </div>
              </div>
            </div>
            <div class="modal-footer">
              <button class="btn-secondary" (click)="closeModal()">Annuler</button>
              <button class="btn-primary" (click)="saveRole()" [disabled]="!formData.name">
                {{ editingRole ? 'Enregistrer' : 'Créer' }}
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
              <p>Êtes-vous sûr de vouloir supprimer le rôle <strong>{{ roleToDelete?.name }}</strong> ?</p>
              <p class="warning" *ngIf="roleToDelete?.usersCount">
                Ce rôle est assigné à {{ roleToDelete?.usersCount }} utilisateur(s).
              </p>
            </div>
            <div class="modal-footer">
              <button class="btn-secondary" (click)="showDeleteConfirm = false">Annuler</button>
              <button class="btn-danger" (click)="deleteRole()">Supprimer</button>
            </div>
          </div>
        </div>
      </div>
    </admin-layout>
  `,
  styles: [`
    .roles-container {
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
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 12px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      color: var(--adm-sub);
    }
    .header-left h2::before {
      content: '';
      width: 3px;
      height: 12px;
      border-radius: 2px;
      background: var(--adm-indigo);
    }
    .count {
      background: rgba(100, 116, 139, 0.10);
      padding: 4px 12px;
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
      transition: background 0.2s;
    }
    .btn-primary:hover { background: var(--adm-indigo-ink); }
    .btn-primary:disabled { background: #94a3b8; cursor: not-allowed; }
    
    .roles-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
      gap: 20px;
    }
    .role-card {
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 16px;
      padding: 20px;
      box-shadow: var(--adm-shadow);
      transition: transform 0.2s, box-shadow 0.2s;
      animation: rise 0.3s ease-out;
    }
    @keyframes rise {
      from { opacity: 0; transform: translateY(8px); }
      to { opacity: 1; transform: none; }
    }
    .role-card:hover {
      transform: translateY(-1px);
      box-shadow: var(--adm-shadow-hover);
    }
    .role-card.system {
      border: 1px solid var(--adm-border);
      background: #f8fafc;
    }
    .role-header {
      display: flex;
      align-items: flex-start;
      gap: 12px;
      margin-bottom: 12px;
    }
    .role-icon {
      width: 48px;
      height: 48px;
      border-radius: 12px;
      background: rgba(79, 70, 229, 0.10);
      color: var(--adm-indigo);
      display: flex;
      align-items: center;
      justify-content: center;
    }
    .role-icon.system { background: var(--adm-track); color: var(--adm-slate-ink); }
    .role-icon.admin { background: rgba(217, 119, 6, 0.12); color: var(--adm-amber-ink); }
    .role-info { flex: 1; }
    .role-info h3 {
      margin: 0 0 4px 0;
      font-size: 16px;
      font-weight: 600;
      color: var(--adm-ink);
    }
    .role-type {
      font-size: 12px;
      color: var(--adm-sub);
    }
    .role-badges {
      display: flex;
      gap: 6px;
    }
    .badge {
      padding: 4px 10px;
      border-radius: 999px;
      font-size: 11px;
      font-weight: 700;
    }
    .badge.system { background: rgba(100, 116, 139, 0.10); color: var(--adm-slate-ink); }
    .badge.default { background: rgba(5, 150, 105, 0.10); color: var(--adm-green-ink); }

    .role-description {
      font-size: 13px;
      color: var(--adm-sub);
      margin: 0 0 16px 0;
      line-height: 1.5;
    }
    .role-stats {
      display: flex;
      gap: 24px;
      padding: 12px 0;
      border-top: 1px solid var(--adm-border);
      margin-bottom: 12px;
    }
    .stat {
      display: flex;
      flex-direction: column;
      border-left: 3px solid var(--adm-indigo);
      border-radius: 2px;
      padding-left: 10px;
    }
    .stat:nth-child(2) { border-left-color: var(--adm-cyan); }
    .stat-value {
      font-size: 24px;
      font-weight: 800;
      color: var(--adm-ink);
      font-variant-numeric: tabular-nums;
    }
    .stat-label {
      font-size: 11px;
      color: var(--adm-sub);
      text-transform: uppercase;
      letter-spacing: 0.06em;
    }
    .role-actions {
      display: flex;
      gap: 8px;
      justify-content: flex-end;
    }
    .btn-icon {
      width: 36px;
      height: 36px;
      border-radius: 8px;
      border: 1px solid var(--adm-border);
      background: #fff;
      color: var(--adm-sub);
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }
    .btn-icon:hover { background: #f8fafc; border-color: var(--adm-indigo); color: var(--adm-indigo); }
    .btn-icon.danger:hover { background: rgba(220, 38, 38, 0.08); border-color: var(--adm-red); color: var(--adm-red); }
    .btn-icon:disabled { opacity: 0.5; cursor: not-allowed; }
    
    /* Modal */
    .modal-overlay {
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
    }
    .modal {
      background: var(--adm-card);
      border-radius: 18px;
      box-shadow: 0 24px 60px -24px rgba(2, 6, 23, 0.45);
      width: 100%;
      max-width: 560px;
      max-height: 90vh;
      overflow: hidden;
      display: flex;
      flex-direction: column;
    }
    .modal.modal-sm { max-width: 400px; }
    .modal-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 20px 24px;
      border-bottom: 1px solid var(--adm-border);
    }
    .modal-header h3 { margin: 0; font-size: 16px; font-weight: 700; color: var(--adm-ink); }
    .btn-close {
      width: 32px;
      height: 32px;
      border-radius: 8px;
      border: none;
      background: var(--adm-track);
      color: var(--adm-sub);
      font-size: 20px;
      cursor: pointer;
      transition: all 0.2s;
    }
    .btn-close:hover { background: var(--adm-border); color: var(--adm-ink); }
    .modal-body {
      padding: 24px;
      overflow-y: auto;
    }
    .modal-footer {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 16px 24px;
      border-top: 1px solid var(--adm-border);
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
    .form-group textarea,
    .form-group select {
      width: 100%;
      padding: 10px 14px;
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      font-size: 14px;
      color: var(--adm-ink);
      background: #fff;
      outline: none;
      transition: border-color 0.2s, box-shadow 0.2s;
    }
    .form-group input[type="text"]:focus,
    .form-group textarea:focus,
    .form-group select:focus {
      border-color: var(--adm-indigo);
      box-shadow: 0 0 0 3px rgba(79, 70, 229, 0.12);
    }
    .form-group input[type="checkbox"] { accent-color: var(--adm-indigo); }
    .form-group textarea {
      min-height: 80px;
      resize: vertical;
    }
    .form-group.checkbox label {
      display: flex;
      align-items: center;
      gap: 8px;
      cursor: pointer;
    }
    .permissions-section h4 {
      margin: 0 0 12px 0;
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 12px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      color: var(--adm-sub);
    }
    .permissions-section h4::before {
      content: '';
      width: 3px;
      height: 12px;
      border-radius: 2px;
      background: var(--adm-indigo);
    }
    .permissions-grid {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 8px;
    }
    .permission-item {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 8px;
      border-radius: 8px;
      cursor: pointer;
      font-size: 13px;
      color: var(--adm-ink);
    }
    .permission-item input[type="checkbox"] { accent-color: var(--adm-indigo); }
    .permission-item:hover { background: #f8fafc; }

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
    .btn-secondary:hover { background: #f8fafc; border-color: var(--adm-indigo); }
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
      .role-card { animation: none; }
      .role-card:hover { transform: none; }
    }
  `]
})
export class AdminRolesComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  roles: Role[] = [];
  showModal = false;
  showDeleteConfirm = false;
  editingRole: Role | null = null;
  roleToDelete: Role | null = null;
  
  formData: {
    name: string;
    description: string;
    roleType: 'system_admin' | 'company_admin' | 'employee' | 'custom';
    isDefault: boolean;
    permissions: Record<string, any>;
  } = {
    name: '',
    description: '',
    roleType: 'employee',
    isDefault: false,
    permissions: {}
  };

  availablePermissions = [
    { key: 'dashboard', label: 'Tableau de bord' },
    { key: 'monitoring', label: 'Monitoring' },
    { key: 'vehicles', label: 'Véhicules' },
    { key: 'employees', label: 'Employés' },
    { key: 'gpsDevices', label: 'Appareils GPS' },
    { key: 'maintenance', label: 'Maintenance' },
    { key: 'costs', label: 'Coûts' },
    { key: 'reports', label: 'Rapports' },
    { key: 'geofences', label: 'Géofences' },
    { key: 'notifications', label: 'Notifications' },
    { key: 'settings', label: 'Paramètres' },
    { key: 'users', label: 'Utilisateurs' }
  ];

  constructor(private adminService: AdminService) {}

  ngOnInit() {
    this.loadRoles();
  }

  loadRoles() {
    this.adminService.getRoles().pipe(takeUntil(this.destroy$)).subscribe(roles => {
      this.roles = roles;
    });
  }

  getRoleTypeLabel(type: string): string {
    const labels: Record<string, string> = {
      'system_admin': 'Super Administrateur',
      'company_admin': 'Administrateur',
      'employee': 'Employé',
      'custom': 'Personnalisé'
    };
    return labels[type] || type;
  }

  getPermissionsCount(role: Role): number {
    if (!role.permissions) return 0;
    if (role.permissions['all']) return this.availablePermissions.length;
    return Object.keys(role.permissions).filter(k => role.permissions![k]).length;
  }

  hasPermission(key: string): boolean {
    return this.formData.permissions[key] === true || this.formData.permissions['all'] === true;
  }

  togglePermission(key: string) {
    if (this.formData.permissions[key]) {
      delete this.formData.permissions[key];
    } else {
      this.formData.permissions[key] = true;
    }
  }

  openCreateModal() {
    this.editingRole = null;
    this.formData = {
      name: '',
      description: '',
      roleType: 'employee',
      isDefault: false,
      permissions: {}
    };
    this.showModal = true;
  }

  editRole(role: Role) {
    this.editingRole = role;
    this.formData = {
      name: role.name,
      description: role.description || '',
      roleType: role.roleType,
      isDefault: role.isDefault,
      permissions: role.permissions ? { ...role.permissions } : {}
    };
    this.showModal = true;
  }

  closeModal() {
    this.showModal = false;
    this.editingRole = null;
  }

  saveRole() {
    const roleData = {
      name: this.formData.name,
      description: this.formData.description || undefined,
      roleType: this.formData.roleType,
      isDefault: this.formData.isDefault,
      permissions: Object.keys(this.formData.permissions).length > 0 ? this.formData.permissions : undefined
    };

    if (this.editingRole) {
      this.adminService.updateRole(this.editingRole.id, roleData).pipe(takeUntil(this.destroy$)).subscribe({
        next: () => {
          this.loadRoles();
          this.closeModal();
        },
        error: (err) => console.error('Error updating role:', err)
      });
    } else {
      this.adminService.createRole(roleData).pipe(takeUntil(this.destroy$)).subscribe({
        next: () => {
          this.loadRoles();
          this.closeModal();
        },
        error: (err) => console.error('Error creating role:', err)
      });
    }
  }

  confirmDelete(role: Role) {
    this.roleToDelete = role;
    this.showDeleteConfirm = true;
  }

  deleteRole() {
    if (!this.roleToDelete) return;
    
    this.adminService.deleteRole(this.roleToDelete.id).pipe(takeUntil(this.destroy$)).subscribe({
      next: () => {
        this.loadRoles();
        this.showDeleteConfirm = false;
        this.roleToDelete = null;
      },
      error: (err) => console.error('Error deleting role:', err)
    });
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
