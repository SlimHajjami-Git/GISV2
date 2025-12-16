import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { MockDataService } from '../../services/mock-data.service';

@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [CommonModule, RouterModule],
  template: `
    <div class="app-container">
      <!-- WIALON-STYLE TOP NAVIGATION BAR -->
      <nav class="top-nav">
        <!-- Logo -->
        <div class="nav-brand" (click)="navigate('/dashboard')">
          <div class="brand-logo">
            <svg width="26" height="26" viewBox="0 0 32 32" fill="none">
              <rect width="32" height="32" rx="6" fill="#6366f1"/>
              <path d="M8 16L14 22L24 10" stroke="white" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>
          </div>
          <span class="brand-text">Calipso</span>
        </div>

        <!-- Navigation Links -->
        <div class="nav-links">
          <a [routerLink]="['/dashboard']" routerLinkActive="active" class="nav-link">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <rect x="3" y="3" width="7" height="7" rx="1"/>
              <rect x="14" y="3" width="7" height="7" rx="1"/>
              <rect x="3" y="14" width="7" height="7" rx="1"/>
              <rect x="14" y="14" width="7" height="7" rx="1"/>
            </svg>
            <span>Dashboard</span>
          </a>
          <a [routerLink]="['/monitoring']" routerLinkActive="active" class="nav-link">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>
            </svg>
            <span>Monitoring</span>
          </a>
          <a [routerLink]="['/reports']" routerLinkActive="active" class="nav-link">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
              <polyline points="14 2 14 8 20 8"/>
              <line x1="16" y1="13" x2="8" y2="13"/>
            </svg>
            <span>Reports</span>
          </a>
          <a [routerLink]="['/geofences']" routerLinkActive="active" class="nav-link">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="12" cy="10" r="3"/>
              <path d="M12 21.7C17.3 17 20 13 20 10a8 8 0 1 0-16 0c0 3 2.7 7 8 11.7z"/>
            </svg>
            <span>Geofences</span>
          </a>
          <a [routerLink]="['/employees']" routerLinkActive="active" class="nav-link">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/>
              <circle cx="9" cy="7" r="4"/>
            </svg>
            <span>Drivers</span>
          </a>
          <a [routerLink]="['/notifications']" routerLinkActive="active" class="nav-link">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/>
              <path d="M13.73 21a2 2 0 0 1-3.46 0"/>
            </svg>
            <span>Notifications</span>
          </a>
          <a [routerLink]="['/vehicles']" routerLinkActive="active" class="nav-link">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <rect x="1" y="3" width="15" height="13" rx="2"/>
              <polygon points="16 8 20 8 23 11 23 16 16 16 16 8"/>
              <circle cx="5.5" cy="18.5" r="2.5"/>
              <circle cx="18.5" cy="18.5" r="2.5"/>
            </svg>
            <span>Units</span>
          </a>
        </div>

        <!-- Right Actions -->
        <div class="nav-actions">
          <button class="nav-icon-btn" title="Export">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/>
              <polyline points="7 10 12 15 17 10"/>
              <line x1="12" y1="15" x2="12" y2="3"/>
            </svg>
          </button>
          <div class="nav-separator"></div>
          <div class="user-menu" (click)="toggleUserMenu()">
            <div class="user-avatar">{{ getUserInitials() }}</div>
            <span class="user-name">{{ getUserName() }}</span>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <polyline points="6 9 12 15 18 9"/>
            </svg>
          </div>
        </div>
      </nav>

      <!-- MAIN CONTENT AREA -->
      <main class="main-content">
        <ng-content></ng-content>
      </main>
    </div>
  `,
  styles: [`
    .app-container {
      min-height: 100vh;
      display: flex;
      flex-direction: column;
      background: var(--bg-page);
    }

    /* ===== WIALON-STYLE TOP NAVIGATION BAR ===== */
    .top-nav {
      height: 42px;
      background: #f0f0f0;
      border-bottom: 1px solid #d0d0d0;
      display: flex;
      align-items: center;
      padding: 0 12px;
      position: sticky;
      top: 0;
      z-index: 1000;
    }

    /* ===== BRAND / LOGO ===== */
    .nav-brand {
      display: flex;
      align-items: center;
      gap: 8px;
      cursor: pointer;
      padding: 4px 12px 4px 4px;
      border-right: 1px solid #d0d0d0;
      margin-right: 8px;
      flex-shrink: 0;
    }

    .nav-brand:hover {
      background: rgba(0, 0, 0, 0.05);
    }

    .brand-logo {
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .brand-text {
      font-size: 16px;
      font-weight: 700;
      color: #333;
      letter-spacing: -0.3px;
    }

    /* ===== NAVIGATION LINKS ===== */
    .nav-links {
      display: flex;
      align-items: center;
      gap: 2px;
      flex: 1;
    }

    .nav-link {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 8px 12px;
      border-radius: 4px;
      color: #555;
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.15s;
      text-decoration: none;
      white-space: nowrap;
    }

    .nav-link:hover {
      color: #333;
      background: rgba(0, 0, 0, 0.06);
    }

    .nav-link.active {
      color: #6366f1;
      background: rgba(99, 102, 241, 0.12);
      font-weight: 600;
    }

    .nav-link svg {
      flex-shrink: 0;
      opacity: 0.8;
    }

    .nav-link.active svg {
      opacity: 1;
    }

    /* ===== RIGHT ACTIONS ===== */
    .nav-actions {
      display: flex;
      align-items: center;
      gap: 8px;
      flex-shrink: 0;
      margin-left: auto;
    }

    .nav-icon-btn {
      width: 32px;
      height: 32px;
      border-radius: 4px;
      background: transparent;
      border: none;
      color: #666;
      display: flex;
      align-items: center;
      justify-content: center;
      cursor: pointer;
      transition: all 0.15s;
    }

    .nav-icon-btn:hover {
      background: rgba(0, 0, 0, 0.06);
      color: #333;
    }

    .nav-separator {
      width: 1px;
      height: 24px;
      background: #d0d0d0;
      margin: 0 4px;
    }

    .user-menu {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 4px 8px;
      border-radius: 4px;
      cursor: pointer;
      transition: all 0.15s;
    }

    .user-menu:hover {
      background: rgba(0, 0, 0, 0.06);
    }

    .user-avatar {
      width: 28px;
      height: 28px;
      border-radius: 4px;
      background: #6366f1;
      color: white;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 11px;
      font-weight: 600;
      flex-shrink: 0;
    }

    .user-name {
      font-size: 13px;
      font-weight: 500;
      color: #333;
    }

    .user-menu svg {
      color: #666;
    }

    /* ===== MAIN CONTENT ===== */
    .main-content {
      flex: 1;
      display: flex;
      flex-direction: column;
    }

    /* ===== RESPONSIVE ===== */
    @media (max-width: 1200px) {
      .nav-link span {
        display: none;
      }

      .nav-link {
        padding: 8px 10px;
      }
    }

    @media (max-width: 768px) {
      .top-nav {
        padding: 0 8px;
      }

      .nav-links {
        display: none;
      }

      .user-name {
        display: none;
      }

      .nav-brand {
        border-right: none;
        margin-right: 0;
      }
    }
  `]
})
export class AppLayoutComponent {
  constructor(
    private router: Router,
    private dataService: MockDataService
  ) {}

  navigate(path: string) {
    this.router.navigate([path]);
  }

  toggleUserMenu() {
    // Future: implement dropdown menu
  }

  getUserName(): string {
    const company = this.dataService.getCurrentCompany();
    return company ? company.name : 'User';
  }

  getUserInitials(): string {
    const company = this.dataService.getCurrentCompany();
    if (!company) return 'U';
    return company.name.substring(0, 2).toUpperCase();
  }

  logout() {
    this.dataService.logout();
    this.router.navigate(['/login']);
  }
}
