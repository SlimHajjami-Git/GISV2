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
            <span class="logo-icon">C</span>
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
            <div class="admin-avatar">{{ adminUser.name?.charAt(0) || 'A' }}</div>
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
            <span>Déconnexion</span>
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
              <input type="text" placeholder="Rechercher..." />
            </div>
            <!-- Cloche = abonnements à surveiller (expirent ≤30 j, impayés, bloqués) -->
            <button class="header-btn notification-btn" (click)="goToBilling()"
              [title]="billingAlertCount > 0 ? billingAlertCount + ' abonnement(s) à surveiller' : 'Aucune alerte abonnement'">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/>
                <path d="M13.73 21a2 2 0 0 1-3.46 0"/>
              </svg>
              <span class="notification-badge" *ngIf="billingAlertCount > 0">{{ billingAlertCount > 9 ? '9+' : billingAlertCount }}</span>
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
    /* ══════════════════════════════════════════════════════════
       CALYPSO ADMIN — même langage que le dashboard « Command » :
       sidebar CARBONE signature (constante), contenu clair bento.
       Les tokens --adm-* sont définis ici et HÉRITÉS par toutes
       les pages admin (les variables CSS traversent l'encapsulation).
    ══════════════════════════════════════════════════════════ */
    .admin-container {
      /* accents (teintes 600 lisibles sur surface claire) */
      --adm-indigo:#4f46e5; --adm-indigo-ink:#4338ca;
      --adm-green:#059669;  --adm-green-ink:#047857;
      --adm-amber:#d97706;  --adm-amber-ink:#b45309;
      --adm-red:#dc2626;    --adm-red-ink:#b91c1c;
      --adm-cyan:#0891b2;   --adm-cyan-ink:#0e7490;
      --adm-slate:#64748b;  --adm-slate-ink:#475569;
      /* surfaces */
      --adm-bg:#f4f6fa; --adm-card:#ffffff; --adm-border:#e6eaf2;
      --adm-ink:#0f172a; --adm-sub:#64748b; --adm-track:#eef2f7;
      --adm-shadow:0 1px 2px rgba(15,23,42,.05), 0 10px 28px -14px rgba(15,23,42,.14);
      --adm-shadow-hover:0 2px 4px rgba(15,23,42,.06), 0 18px 40px -16px rgba(15,23,42,.20);
      /* carbone (sidebar) */
      --adm-carb1:#0d1425; --adm-carb2:#16213a; --adm-glow:rgba(99,102,241,.24);

      display: flex;
      min-height: 100vh;
      background: var(--adm-bg);
      color: var(--adm-ink);
      font-variant-numeric: tabular-nums;
    }

    /* ── Sidebar carbone (signature Calypso) ── */
    .sidebar {
      width: 260px;
      background:
        radial-gradient(700px 320px at 110% -10%, var(--adm-glow), transparent 60%),
        linear-gradient(168deg, var(--adm-carb1) 0%, var(--adm-carb2) 55%, var(--adm-carb1) 100%);
      display: flex;
      flex-direction: column;
      transition: width 0.3s ease;
      position: fixed;
      height: 100vh;
      z-index: 100;
      box-shadow: 8px 0 30px -18px rgba(2,6,23,.55);
    }

    .sidebar.collapsed { width: 72px; }

    .sidebar-header {
      padding: 20px 16px;
      display: flex;
      align-items: center;
      justify-content: space-between;
      border-bottom: 1px solid rgba(148,163,184,.14);
    }

    .logo { display: flex; align-items: center; gap: 10px; }

    .logo-icon {
      width: 36px; height: 36px;
      background: linear-gradient(135deg, #6366f1 0%, #4f46e5 100%);
      border-radius: 10px;
      display: flex; align-items: center; justify-content: center;
      font-weight: 800; font-size: 18px; color: #fff;
      box-shadow: 0 6px 16px -6px rgba(99,102,241,.7);
    }

    .logo-text { font-size: 18px; font-weight: 700; color: #f8fafc; letter-spacing: -.01em; }
    .logo-text .highlight { color: #a5b4fc; font-weight: 600; }

    .collapse-btn {
      width: 32px; height: 32px; border: none;
      background: rgba(148,163,184,.12);
      border-radius: 8px; color: #94a3b8; cursor: pointer;
      display: flex; align-items: center; justify-content: center;
      transition: all 0.2s;
    }
    .collapse-btn:hover { background: rgba(148,163,184,.22); color: #e2e8f0; }

    .sidebar-nav { flex: 1; padding: 14px 12px; overflow-y: auto; scrollbar-width: thin; scrollbar-color: rgba(148,163,184,.3) transparent; }

    .nav-item {
      display: flex; align-items: center; gap: 12px;
      padding: 10px 13px;
      border-radius: 10px;
      color: #94a3b8;
      text-decoration: none;
      margin-bottom: 2px;
      transition: all 0.18s;
      position: relative;
    }
    .nav-item:hover { background: rgba(148,163,184,.10); color: #e2e8f0; }
    .nav-item.active {
      background: linear-gradient(90deg, rgba(99,102,241,.22) 0%, rgba(99,102,241,.08) 100%);
      color: #c7d2fe;
    }
    .nav-item.active::before {
      content: '';
      position: absolute; left: 0; top: 50%; transform: translateY(-50%);
      width: 3px; height: 22px;
      background: #818cf8;
      border-radius: 0 3px 3px 0;
      box-shadow: 0 0 12px rgba(129,140,248,.8);
    }

    .nav-icon { width: 20px; height: 20px; display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
    .nav-label { font-size: 13.5px; font-weight: 500; white-space: nowrap; letter-spacing: .01em; }

    .nav-badge {
      margin-left: auto;
      background: #ef4444; color: #fff;
      font-size: 10.5px; font-weight: 700;
      padding: 2px 8px; border-radius: 999px;
      box-shadow: 0 0 10px rgba(239,68,68,.5);
    }

    .sidebar-footer { padding: 14px; border-top: 1px solid rgba(148,163,184,.14); }

    .admin-info {
      display: flex; align-items: center; gap: 12px;
      padding: 10px 12px;
      background: rgba(148,163,184,.08);
      border: 1px solid rgba(148,163,184,.12);
      border-radius: 12px;
      margin-bottom: 10px;
    }

    .admin-avatar {
      width: 38px; height: 38px;
      background: linear-gradient(135deg, #6366f1 0%, #4f46e5 100%);
      border-radius: 10px;
      display: flex; align-items: center; justify-content: center;
      font-weight: 700; font-size: 15px; color: #fff;
    }

    .admin-details { display: flex; flex-direction: column; min-width: 0; }
    .admin-name { font-size: 13.5px; font-weight: 600; color: #f1f5f9; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .admin-role { font-size: 11.5px; color: #94a3b8; }

    .logout-btn {
      width: 100%;
      display: flex; align-items: center; justify-content: center; gap: 8px;
      padding: 9px;
      border: 1px solid rgba(248,113,113,.3);
      background: rgba(239,68,68,.12);
      border-radius: 10px;
      color: #fca5a5;
      font-size: 13px; font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }
    .logout-btn:hover { background: rgba(239,68,68,.22); border-color: rgba(248,113,113,.5); color: #fecaca; }

    /* ── Zone principale ── */
    .main-content {
      flex: 1;
      margin-left: 260px;
      display: flex;
      flex-direction: column;
      transition: margin-left 0.3s ease;
      min-width: 0;
    }
    .sidebar.collapsed + .main-content,
    .sidebar.collapsed ~ .main-content { margin-left: 72px; }

    .top-header {
      height: 62px;
      background: rgba(255,255,255,.85);
      backdrop-filter: blur(10px);
      border-bottom: 1px solid var(--adm-border);
      padding: 0 24px;
      display: flex; align-items: center; justify-content: space-between;
      position: sticky; top: 0; z-index: 50;
    }

    .page-title { font-size: 19px; font-weight: 800; letter-spacing: -.02em; color: var(--adm-ink); margin: 0; }

    .header-right { display: flex; align-items: center; gap: 12px; }

    .header-search {
      display: flex; align-items: center; gap: 10px;
      background: #f8fafc;
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      padding: 8px 14px;
      width: 280px;
      transition: border-color .15s, box-shadow .15s;
    }
    .header-search:focus-within { border-color: var(--adm-indigo); box-shadow: 0 0 0 3px rgba(79,70,229,.12); }
    .header-search svg { color: var(--adm-sub); flex-shrink: 0; }
    .header-search input { flex: 1; border: none; background: transparent; color: var(--adm-ink); font-size: 13.5px; outline: none; }
    .header-search input::placeholder { color: #94a3b8; }

    .header-btn {
      width: 38px; height: 38px; border: 1px solid var(--adm-border);
      background: #fff;
      border-radius: 10px; color: var(--adm-sub); cursor: pointer;
      display: flex; align-items: center; justify-content: center;
      position: relative;
      transition: all 0.2s;
    }
    .header-btn:hover { border-color: var(--adm-indigo); color: var(--adm-indigo); }

    .notification-badge {
      position: absolute; top: -5px; right: -5px;
      min-width: 17px; height: 17px; padding: 0 4px;
      background: #ef4444; border: 2px solid #fff; border-radius: 999px;
      font-size: 9.5px; font-weight: 700; color: #fff;
      display: flex; align-items: center; justify-content: center;
    }

    .header-time {
      font-size: 13px; color: var(--adm-slate-ink); font-weight: 600;
      padding: 8px 13px;
      background: #fff; border: 1px solid var(--adm-border);
      border-radius: 10px;
      font-variant-numeric: tabular-nums;
    }

    .content-area { flex: 1; padding: 24px; overflow-y: auto; }

    @media (max-width: 1024px) {
      .sidebar { width: 72px; }
      .sidebar .nav-label,
      .sidebar .sidebar-footer { display: none; }
      .main-content { margin-left: 72px; }
      .header-search { width: 200px; }
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
      icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 2L2 7l10 5 10-5-10-5z"/><path d="M2 17l10 5 10-5"/><path d="M2 12l10 5 10-5"/></svg>',
      label: 'Marques & Modèles',
      route: 'brands'
    },
    {
      icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/></svg>',
      label: 'Pièces Véhicules',
      route: 'parts'
    },
    {
      icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0118 0z"/><circle cx="12" cy="10" r="3"/></svg>',
      label: 'Monitoring',
      route: 'monitoring'
    },
    {
      icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"/><polyline points="14,2 14,8 20,8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/></svg>',
      label: 'Rapports',
      route: 'reports'
    },
    {
      icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="22,12 18,12 15,21 9,3 6,12 2,12"/></svg>',
      label: 'Trames GPS',
      route: 'trames'
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
      icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18.36 6.64a9 9 0 1 1-12.73 0"/><line x1="12" y1="2" x2="12" y2="12"/></svg>',
      label: 'Auto-Recovery',
      route: 'auto-recovery'
    },
    {
      icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.73 21a2 2 0 0 1-3.46 0"/></svg>',
      label: 'Notifications',
      route: 'notifications'
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

  /** Nombre de sociétés dont l'abonnement demande une action (badge cloche). */
  billingAlertCount = 0;

  ngOnInit() {
    this.adminUser = this.adminService.getAdminUser();
    this.updateTime();
    setInterval(() => this.updateTime(), 1000);
    this.adminService.getBillingOverview().subscribe({
      next: (o) => this.billingAlertCount = o?.count ?? 0,
      error: () => {}
    });
  }

  goToBilling() {
    this.router.navigate(['/admin/clients']);
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
