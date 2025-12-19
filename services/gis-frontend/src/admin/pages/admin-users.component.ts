import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService, SystemUser, Client } from '../services/admin.service';

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
              <input type="text" [(ngModel)]="searchQuery" (input)="filterUsers()" placeholder="Search users..." />
            </div>
            <select class="filter-select" [(ngModel)]="companyFilter" (change)="filterUsers()">
              <option value="all">All Companies</option>
              <option *ngFor="let company of companies" [value]="company.id">{{ company.name }}</option>
            </select>
            <select class="filter-select" [(ngModel)]="statusFilter" (change)="filterUsers()">
              <option value="all">All Status</option>
              <option value="active">Active</option>
              <option value="suspended">Suspended</option>
              <option value="online">Online Now</option>
            </select>
          </div>
          <div class="header-stats">
            <div class="stat-chip online">
              <span class="dot"></span>
              {{ onlineCount }} Online
            </div>
            <div class="stat-chip total">
              {{ users.length }} Total Users
            </div>
          </div>
        </div>

        <div class="users-table-container">
          <table class="users-table">
            <thead>
              <tr>
                <th>User</th>
                <th>Company</th>
                <th>Roles</th>
                <th>Permissions</th>
                <th>Status</th>
                <th>Last Login</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let user of filteredUsers" [class.suspended]="user.status === 'suspended'">
                <td>
                  <div class="user-cell">
                    <div class="user-avatar" [class.online]="user.isOnline">
                      {{ user.name.charAt(0) }}
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
                  <div class="roles">
                    <span class="role-tag" *ngFor="let role of user.roles">{{ role }}</span>
                  </div>
                </td>
                <td>
                  <div class="permissions-cell">
                    <span class="perm-count">{{ user.permissions.length }} pages</span>
                    <button class="edit-perms-btn" (click)="openPermissionsModal(user)" title="Edit Permissions">
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                        <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                      </svg>
                    </button>
                  </div>
                </td>
                <td>
                  <span class="status-badge" [class]="user.status">{{ user.status | titlecase }}</span>
                </td>
                <td>
                  <span class="last-login">{{ formatDate(user.lastLogin) }}</span>
                </td>
                <td>
                  <div class="actions">
                    <button class="action-btn" [class.suspend]="user.status === 'active'" [class.activate]="user.status !== 'active'"
                            (click)="toggleUserStatus(user)" [title]="user.status === 'active' ? 'Suspend' : 'Activate'">
                      <svg *ngIf="user.status === 'active'" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <circle cx="12" cy="12" r="10"/><line x1="4.93" y1="4.93" x2="19.07" y2="19.07"/>
                      </svg>
                      <svg *ngIf="user.status !== 'active'" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22,4 12,14.01 9,11.01"/>
                      </svg>
                    </button>
                    <button class="action-btn view" (click)="viewUserActivity(user)" title="View Activity">
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <polyline points="22,12 18,12 15,21 9,3 6,12 2,12"/>
                      </svg>
                    </button>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <div class="modal-overlay" *ngIf="showPermissionsModal" (click)="closeModal()">
          <div class="modal permissions-modal" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h2>Edit User Permissions</h2>
              <button class="close-btn" (click)="closeModal()">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                </svg>
              </button>
            </div>

            <div class="modal-body" *ngIf="selectedUser">
              <div class="user-header">
                <div class="user-avatar large">{{ selectedUser.name.charAt(0) }}</div>
                <div>
                  <h3>{{ selectedUser.name }}</h3>
                  <span class="user-company">{{ selectedUser.companyName }}</span>
                </div>
              </div>

              <div class="permissions-section">
                <h4>Page Access Permissions</h4>
                <p class="section-desc">Select which pages this user can access</p>

                <div class="permissions-grid">
                  <label class="permission-item" *ngFor="let page of allPages">
                    <input type="checkbox" [checked]="selectedPermissions.includes(page)" (change)="togglePermission(page)" />
                    <span class="checkmark"></span>
                    <span class="permission-label">{{ formatPageName(page) }}</span>
                  </label>
                </div>
              </div>

              <div class="quick-actions">
                <button class="quick-btn" (click)="selectAll()">Select All</button>
                <button class="quick-btn" (click)="deselectAll()">Deselect All</button>
              </div>
            </div>

            <div class="modal-footer">
              <button class="btn-secondary" (click)="closeModal()">Cancel</button>
              <button class="btn-primary" (click)="savePermissions()">Save Permissions</button>
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
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 10px;
      padding: 10px 14px;
      width: 260px;
    }

    .search-box svg { color: #8b98a5; }

    .search-box input {
      flex: 1;
      border: none;
      background: transparent;
      color: #e7e9ea;
      font-size: 14px;
      outline: none;
    }

    .filter-select {
      padding: 10px 14px;
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 10px;
      color: #e7e9ea;
      font-size: 14px;
      outline: none;
      cursor: pointer;
    }

    .header-stats {
      display: flex;
      gap: 12px;
    }

    .stat-chip {
      padding: 8px 14px;
      border-radius: 20px;
      font-size: 13px;
      font-weight: 500;
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .stat-chip.online {
      background: rgba(34, 197, 94, 0.15);
      color: #22c55e;
    }

    .stat-chip.total {
      background: rgba(255, 255, 255, 0.05);
      color: #8b98a5;
    }

    .stat-chip .dot {
      width: 8px;
      height: 8px;
      background: #22c55e;
      border-radius: 50%;
      animation: pulse 2s infinite;
    }

    @keyframes pulse {
      0%, 100% { opacity: 1; }
      50% { opacity: 0.5; }
    }

    .users-table-container {
      background: linear-gradient(135deg, rgba(26, 31, 46, 0.8) 0%, rgba(20, 24, 36, 0.9) 100%);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 16px;
      overflow: hidden;
    }

    .users-table {
      width: 100%;
      border-collapse: collapse;
    }

    .users-table th {
      padding: 16px 20px;
      text-align: left;
      font-size: 12px;
      font-weight: 600;
      text-transform: uppercase;
      color: #8b98a5;
      background: rgba(255, 255, 255, 0.03);
      border-bottom: 1px solid rgba(255, 255, 255, 0.08);
    }

    .users-table td {
      padding: 16px 20px;
      border-bottom: 1px solid rgba(255, 255, 255, 0.05);
    }

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
      background: linear-gradient(135deg, #3b82f6 0%, #2563eb 100%);
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
      background: #22c55e;
      border: 2px solid #141824;
      border-radius: 50%;
    }

    .user-info {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .user-name {
      font-size: 14px;
      font-weight: 500;
      color: #e7e9ea;
    }

    .user-email {
      font-size: 12px;
      color: #6b7280;
    }

    .company-badge {
      padding: 4px 10px;
      background: rgba(255, 255, 255, 0.05);
      border-radius: 8px;
      font-size: 13px;
      color: #8b98a5;
    }

    .roles {
      display: flex;
      gap: 6px;
      flex-wrap: wrap;
    }

    .role-tag {
      padding: 4px 8px;
      background: rgba(0, 212, 170, 0.15);
      color: #00d4aa;
      border-radius: 6px;
      font-size: 11px;
      font-weight: 500;
      text-transform: capitalize;
    }

    .permissions-cell {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .perm-count {
      font-size: 13px;
      color: #8b98a5;
    }

    .edit-perms-btn {
      width: 28px;
      height: 28px;
      border: none;
      background: rgba(59, 130, 246, 0.15);
      border-radius: 6px;
      color: #3b82f6;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }

    .edit-perms-btn:hover {
      background: rgba(59, 130, 246, 0.25);
    }

    .status-badge {
      padding: 4px 10px;
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

    .last-login {
      font-size: 13px;
      color: #8b98a5;
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
      background: rgba(239, 68, 68, 0.15);
      color: #ef4444;
    }

    .action-btn.activate {
      background: rgba(34, 197, 94, 0.15);
      color: #22c55e;
    }

    .action-btn.view {
      background: rgba(255, 255, 255, 0.05);
      color: #8b98a5;
    }

    .action-btn:hover {
      transform: scale(1.1);
    }

    .modal-overlay {
      position: fixed;
      inset: 0;
      background: rgba(0, 0, 0, 0.7);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
      padding: 20px;
    }

    .modal {
      background: linear-gradient(135deg, #1a1f2e 0%, #141824 100%);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 20px;
      width: 100%;
      max-width: 560px;
      max-height: 90vh;
      overflow-y: auto;
    }

    .modal-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 20px 24px;
      border-bottom: 1px solid rgba(255, 255, 255, 0.08);
    }

    .modal-header h2 {
      margin: 0;
      font-size: 18px;
      font-weight: 600;
      color: #e7e9ea;
    }

    .close-btn {
      width: 36px;
      height: 36px;
      border: none;
      background: rgba(255, 255, 255, 0.05);
      border-radius: 10px;
      color: #8b98a5;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .modal-body {
      padding: 24px;
    }

    .user-header {
      display: flex;
      align-items: center;
      gap: 16px;
      padding-bottom: 20px;
      margin-bottom: 20px;
      border-bottom: 1px solid rgba(255, 255, 255, 0.08);
    }

    .user-header h3 {
      margin: 0 0 4px 0;
      font-size: 18px;
      color: #e7e9ea;
    }

    .user-company {
      font-size: 14px;
      color: #8b98a5;
    }

    .permissions-section h4 {
      margin: 0 0 6px 0;
      font-size: 15px;
      font-weight: 600;
      color: #e7e9ea;
    }

    .section-desc {
      margin: 0 0 16px 0;
      font-size: 13px;
      color: #6b7280;
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
      background: rgba(255, 255, 255, 0.03);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 10px;
      cursor: pointer;
      transition: all 0.2s;
      position: relative;
    }

    .permission-item:hover {
      background: rgba(255, 255, 255, 0.05);
    }

    .permission-item input {
      position: absolute;
      opacity: 0;
    }

    .permission-item .checkmark {
      width: 20px;
      height: 20px;
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.2);
      border-radius: 5px;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }

    .permission-item input:checked ~ .checkmark {
      background: #00d4aa;
      border-color: #00d4aa;
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
      color: #e7e9ea;
    }

    .quick-actions {
      display: flex;
      gap: 12px;
      margin-top: 16px;
    }

    .quick-btn {
      padding: 8px 16px;
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 8px;
      color: #8b98a5;
      font-size: 13px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .quick-btn:hover {
      background: rgba(255, 255, 255, 0.1);
      color: #e7e9ea;
    }

    .modal-footer {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 20px 24px;
      border-top: 1px solid rgba(255, 255, 255, 0.08);
    }

    .btn-secondary {
      padding: 10px 20px;
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 10px;
      color: #e7e9ea;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
    }

    .btn-primary {
      padding: 10px 20px;
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
      border: none;
      border-radius: 10px;
      color: #fff;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
    }
  `]
})
export class AdminUsersComponent implements OnInit {
  users: SystemUser[] = [];
  filteredUsers: SystemUser[] = [];
  companies: Client[] = [];
  allPages: string[] = [];

  searchQuery = '';
  companyFilter = 'all';
  statusFilter = 'all';

  showPermissionsModal = false;
  selectedUser: SystemUser | null = null;
  selectedPermissions: string[] = [];

  get onlineCount(): number {
    return this.users.filter(u => u.isOnline).length;
  }

  constructor(
    private router: Router,
    private adminService: AdminService
  ) {}

  ngOnInit() {
    if (!this.adminService.isAuthenticated()) {
      this.router.navigate(['/admin/login']);
      return;
    }
    this.allPages = this.adminService.getAllPages();
    this.loadData();
  }

  loadData() {
    this.adminService.getUsers().subscribe(users => {
      this.users = users;
      this.filterUsers();
    });

    this.adminService.getClients().subscribe(clients => {
      this.companies = clients;
    });
  }

  filterUsers() {
    this.filteredUsers = this.users.filter(user => {
      const matchesSearch = !this.searchQuery ||
        user.name.toLowerCase().includes(this.searchQuery.toLowerCase()) ||
        user.email.toLowerCase().includes(this.searchQuery.toLowerCase());
      const matchesCompany = this.companyFilter === 'all' || user.companyId.toString() === this.companyFilter;
      const matchesStatus = this.statusFilter === 'all' ||
        (this.statusFilter === 'online' && user.isOnline) ||
        (this.statusFilter !== 'online' && user.status === this.statusFilter);
      return matchesSearch && matchesCompany && matchesStatus;
    });
  }

  openPermissionsModal(user: SystemUser) {
    this.selectedUser = user;
    this.selectedPermissions = [...user.permissions];
    this.showPermissionsModal = true;
  }

  togglePermission(page: string) {
    const index = this.selectedPermissions.indexOf(page);
    if (index > -1) {
      this.selectedPermissions.splice(index, 1);
    } else {
      this.selectedPermissions.push(page);
    }
  }

  selectAll() {
    this.selectedPermissions = [...this.allPages];
  }

  deselectAll() {
    this.selectedPermissions = [];
  }

  savePermissions() {
    if (this.selectedUser) {
      this.adminService.updateUserPermissions(this.selectedUser.id, this.selectedPermissions).subscribe(() => {
        this.selectedUser!.permissions = [...this.selectedPermissions];
        this.closeModal();
      });
    }
  }

  toggleUserStatus(user: SystemUser) {
    if (user.status === 'active') {
      this.adminService.suspendUser(user.id).subscribe(() => {
        user.status = 'suspended';
      });
    } else {
      this.adminService.activateUser(user.id).subscribe(() => {
        user.status = 'active';
      });
    }
  }

  viewUserActivity(user: SystemUser) {
    this.router.navigate(['/admin/activity'], { queryParams: { userId: user.id } });
  }

  closeModal() {
    this.showPermissionsModal = false;
    this.selectedUser = null;
  }

  formatPageName(page: string): string {
    return page.split('-').map(w => w.charAt(0).toUpperCase() + w.slice(1)).join(' ');
  }

  formatDate(date: Date | undefined): string {
    if (!date) return 'Never';
    return new Date(date).toLocaleDateString('en-US', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
  }
}
