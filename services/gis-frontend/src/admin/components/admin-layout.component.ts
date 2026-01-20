import { Component, Input, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { AdminService, AdminUser } from '../services/admin.service';

interface NavItem {
  icon: string;
  label: string;
  route: string;
  badge?: number;
}

@Component({
  selector: 'admin-layout',
  standalone: true,
  imports: [CommonModule, RouterModule],
  template: `
    <div class="admin-container">
      <aside class="sidebar" [class.collapsed]="sidebarCollapsed">
        <div class="sidebar-header">
          <div class="logo" *ngIf="!sidebarCollapsed">
            <span class="logo-icon">B</span>
            <span class="logo-text">Calypso<span class="highlight">Admin</span></span>
          </div>
          <button class="collapse-btn" (click)="toggleSidebar()">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path *ngIf="!sidebarCollapsed" d="M15 18l-6-6 6-6"/>
              <path *ngIf="sidebarCollapsed" d="M9 18l6-6-6-6"/>
            </svg>
          </button>
        </div>

        <nav class="sidebar-nav">
          <a *ngFor="let item of navItems"
             [routerLink]="['/admin', item.route]"
             routerLinkActive="active"
             class="nav-item"
             [title]="sidebarCollapsed ? item.label : ''">
            <span class="nav-icon" [innerHTML]="item.icon"></span>
            <span class="nav-label" *ngIf="!sidebarCollapsed">{{ item.label }}</span>
            <span class="nav-badge" *ngIf="item.badge && !sidebarCollapsed">{{ item.badge }}</span>
          </a>
        </nav>

        <div class="sidebar-footer" *ngIf="!sidebarCollapsed">
          <div class="admin-info" *ngIf="adminUser">
            <div class="admin-avatar">{{ adminUser.name.charAt(0) }}</div>
            <div class="admin-details">
              <span class="admin-name">{{ adminUser.name }}</span>
              <span class="admin-role">{{ adminUser.role | titlecase }}</span>
            </div>
          </div>
          <button class="logout-btn" (click)="logout()">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/>
              <polyline points="16,17 21,12 16,7"/>
              <line x1="21" y1="12" x2="9" y2="12"/>
            </svg>
            <span>Logout</span>
          </button>
        </div>
      </aside>

      <main class="main-content">
        <header class="top-header">
          <div class="header-left">
            <h1 class="page-title">{{ pageTitle }}</h1>
          </div>
          <div class="header-right">
            <div class="header-search">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="11" cy="11" r="8"/>
                <line x1="21" y1="21" x2="16.65" y2="16.65"/>
              </svg>
              <input type="text" placeholder="Search clients, users..." />
            </div>
            <button class="header-btn notification-btn">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/>
                <path d="M13.73 21a2 2 0 0 1-3.46 0"/>
              </svg>
              <span class="notification-badge">3</span>
            </button>
            <div class="header-time">{{ currentTime }}</div>
          </div>
        </header>

        <div class="content-area">
          <ng-content></ng-content>
        </div>
      </main>
    </div>
  `,
  styles: [`
    .admin-container {
      display: flex;
      min-height: 100vh;
      background: #f1f5f9;
      color: #1f2937;
    }

    .sidebar {
      width: 260px;
      background: #ffffff;
      border-right: 1px solid #e2e8f0;
      display: flex;
      flex-direction: column;
      transition: width 0.3s ease;
      position: fixed;
      height: 100vh;
      z-index: 100;
      box-shadow: 2px 0 8px rgba(0, 0, 0, 0.05);
    }

    .sidebar.collapsed {
      width: 72px;
    }

    .sidebar-header {
      padding: 20px 16px;
      display: flex;
      align-items: center;
      justify-content: space-between;
      border-bottom: 1px solid #e2e8f0;
    }

    .logo {
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .logo-icon {
      width: 36px;
      height: 36px;
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
      border-radius: 10px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 700;
      font-size: 18px;
      color: #fff;
    }

    .logo-text {
      font-size: 20px;
      font-weight: 600;
      color: #1f2937;
    }

    .logo-text .highlight {
      color: #00d4aa;
    }

    .collapse-btn {
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
      transition: all 0.2s;
    }

    .collapse-btn:hover {
      background: #e2e8f0;
      color: #1f2937;
    }

    .sidebar-nav {
      flex: 1;
      padding: 16px 12px;
      overflow-y: auto;
    }

    .nav-item {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px 14px;
      border-radius: 10px;
      color: #64748b;
      text-decoration: none;
      margin-bottom: 4px;
      transition: all 0.2s;
      position: relative;
    }

    .nav-item:hover {
      background: #f1f5f9;
      color: #1f2937;
    }

    .nav-item.active {
      background: linear-gradient(135deg, rgba(0, 212, 170, 0.15) 0%, rgba(0, 163, 136, 0.15) 100%);
      color: #00a388;
    }

    .nav-item.active::before {
      content: '';
      position: absolute;
      left: 0;
      top: 50%;
      transform: translateY(-50%);
      width: 3px;
      height: 24px;
      background: #00d4aa;
      border-radius: 0 3px 3px 0;
    }

    .nav-icon {
      width: 20px;
      height: 20px;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }

    .nav-label {
      font-size: 14px;
      font-weight: 500;
      white-space: nowrap;
    }

    .nav-badge {
      margin-left: auto;
      background: #ef4444;
      color: #fff;
      font-size: 11px;
      font-weight: 600;
      padding: 2px 8px;
      border-radius: 10px;
    }

    .sidebar-footer {
      padding: 16px;
      border-top: 1px solid #e2e8f0;
    }

    .admin-info {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px;
      background: #f8fafc;
      border-radius: 10px;
      margin-bottom: 12px;
    }

    .admin-avatar {
      width: 40px;
      height: 40px;
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
      border-radius: 10px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 600;
      font-size: 16px;
      color: #fff;
    }

    .admin-details {
      display: flex;
      flex-direction: column;
    }

    .admin-name {
      font-size: 14px;
      font-weight: 600;
      color: #1f2937;
    }

    .admin-role {
      font-size: 12px;
      color: #64748b;
    }

    .logout-btn {
      width: 100%;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 8px;
      padding: 10px;
      border: 1px solid rgba(239, 68, 68, 0.3);
      background: rgba(239, 68, 68, 0.1);
      border-radius: 8px;
      color: #ef4444;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.2s;
    }

    .logout-btn:hover {
      background: rgba(239, 68, 68, 0.2);
      border-color: rgba(239, 68, 68, 0.5);
    }

    .main-content {
      flex: 1;
      margin-left: 260px;
      display: flex;
      flex-direction: column;
      transition: margin-left 0.3s ease;
    }

    .sidebar.collapsed + .main-content,
    .sidebar.collapsed ~ .main-content {
      margin-left: 72px;
    }

    .top-header {
      height: 64px;
      background: #ffffff;
      backdrop-filter: blur(10px);
      border-bottom: 1px solid #e2e8f0;
      padding: 0 24px;
      display: flex;
      align-items: center;
      justify-content: space-between;
      position: sticky;
      top: 0;
      z-index: 50;
    }

    .page-title {
      font-size: 20px;
      font-weight: 600;
      color: #1f2937;
      margin: 0;
    }

    .header-right {
      display: flex;
      align-items: center;
      gap: 16px;
    }

    .header-search {
      display: flex;
      align-items: center;
      gap: 10px;
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      padding: 8px 14px;
      width: 280px;
    }

    .header-search svg {
      color: #64748b;
      flex-shrink: 0;
    }

    .header-search input {
      flex: 1;
      border: none;
      background: transparent;
      color: #1f2937;
      font-size: 14px;
      outline: none;
    }

    .header-search input::placeholder {
      color: #94a3b8;
    }

    .header-btn {
      width: 40px;
      height: 40px;
      border: none;
      background: #f8fafc;
      border-radius: 10px;
      color: #64748b;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      position: relative;
      transition: all 0.2s;
    }

    .header-btn:hover {
      background: #e2e8f0;
      color: #1f2937;
    }

    .notification-badge {
      position: absolute;
      top: 6px;
      right: 6px;
      width: 16px;
      height: 16px;
      background: #ef4444;
      border-radius: 50%;
      font-size: 10px;
      font-weight: 600;
      color: #fff;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .header-time {
      font-size: 14px;
      color: #64748b;
      font-weight: 500;
      padding: 8px 14px;
      background: #f8fafc;
      border-radius: 8px;
    }

    .content-area {
      flex: 1;
      padding: 24px;
      overflow-y: auto;
    }

    @media (max-width: 1024px) {
      .sidebar {
        width: 72px;
      }
      .sidebar .nav-label,
      .sidebar .sidebar-footer {
        display: none;
      }
      .main-content {
        margin-left: 72px;
      }
      .header-search {
        width: 200px;
      }
    }
  `]
})
export class AdminLayoutComponent implements OnInit {
  @Input() pageTitle = 'Dashboard';

  sidebarCollapsed = false;
  adminUser: AdminUser | null = null;
  currentTime = '';

  navItems: NavItem[] = [
    {
      icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/><rect x="14" y="14" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/></svg>',
      label: 'Dashboard',
      route: 'dashboard'
    },
    {
      icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>',
      label: 'Sociétés',
      route: 'clients'
    },
    {
      icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 4H3c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h18c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2z"/><path d="M1 10h22"/></svg>',
      label: 'Abonnements',
      route: 'subscriptions'
    },
        {
      icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>',
      label: 'Users',
      route: 'users',
      badge: 3
    },
    {
      icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="1" y="3" width="15" height="13" rx="2"/><path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-5"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/></svg>',
      label: 'Vehicules',
      route: 'vehicles'
    },
    {
      icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M22 12h-4l-3 9L9 3l-3 9H2"/></svg>',
      label: 'System Health',
      route: 'health'
    },
    {
      icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>',
      label: 'Feature Control',
      route: 'features'
    },
    {
      icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14,2 14,8 20,8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/><polyline points="10,9 9,9 8,9"/></svg>',
      label: 'Estimates',
      route: 'estimates'
    },
    {
      icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="22,12 18,12 15,21 9,3 6,12 2,12"/></svg>',
      label: 'Activity Logs',
      route: 'activity'
    },
    {
      icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"/></svg>',
      label: 'Settings',
      route: 'settings'
    }
  ];

  constructor(
    private router: Router,
    private adminService: AdminService
  ) {}

  ngOnInit() {
    this.adminUser = this.adminService.getAdminUser();
    this.updateTime();
    setInterval(() => this.updateTime(), 1000);
  }

  updateTime() {
    const now = new Date();
    this.currentTime = now.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
  }

  toggleSidebar() {
    this.sidebarCollapsed = !this.sidebarCollapsed;
  }

  logout() {
    this.adminService.logout();
    this.router.navigate(['/admin/login']);
  }
}
