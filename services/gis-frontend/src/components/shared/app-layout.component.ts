import { Component, OnInit, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { ApiService } from '../../services/api.service';
import { ThemeService } from '../../services/theme.service';
import { GPSAlert } from '../../models/types';

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
          <a [routerLink]="['/vehicles']" routerLinkActive="active" class="nav-link">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <rect x="1" y="3" width="15" height="13" rx="2"/>
              <polygon points="16 8 20 8 23 11 23 16 16 16 16 8"/>
              <circle cx="5.5" cy="18.5" r="2.5"/>
              <circle cx="18.5" cy="18.5" r="2.5"/>
            </svg>
            <span>Units</span>
          </a>
          <a [routerLink]="['/gps-devices']" routerLinkActive="active" class="nav-link">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z"/>
              <circle cx="12" cy="10" r="3"/>
            </svg>
            <span>GPS</span>
          </a>
          <a [routerLink]="['/maintenance']" routerLinkActive="active" class="nav-link">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>
            </svg>
            <span>Maintenance</span>
          </a>
          <a [routerLink]="['/entretiens-maitres']" routerLinkActive="active" class="nav-link">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <rect x="3" y="4" width="18" height="18" rx="2" ry="2"/>
              <line x1="16" y1="2" x2="16" y2="6"/>
              <line x1="8" y1="2" x2="8" y2="6"/>
              <line x1="3" y1="10" x2="21" y2="10"/>
            </svg>
            <span>Entretiens</span>
          </a>
          <a [routerLink]="['/suppliers']" routerLinkActive="active" class="nav-link">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M3 21h18"/>
              <path d="M9 8h1"/>
              <path d="M9 12h1"/>
              <path d="M9 16h1"/>
              <path d="M14 8h1"/>
              <path d="M14 12h1"/>
              <path d="M14 16h1"/>
              <path d="M5 21V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2v16"/>
            </svg>
            <span>Fournisseurs</span>
          </a>
          <a [routerLink]="['/documents']" routerLinkActive="active" class="nav-link">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
              <polyline points="14 2 14 8 20 8"/>
              <line x1="16" y1="13" x2="8" y2="13"/>
              <line x1="16" y1="17" x2="8" y2="17"/>
            </svg>
            <span>Échéances</span>
          </a>
          <a [routerLink]="['/sinistres']" routerLinkActive="active" class="nav-link">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="12" cy="12" r="10"/>
              <line x1="12" y1="8" x2="12" y2="12"/>
              <line x1="12" y1="16" x2="12.01" y2="16"/>
            </svg>
            <span>Sinistres</span>
          </a>
        </div>

        <!-- Right Actions -->
        <div class="nav-actions">
          <!-- Theme Toggle -->
          <button class="nav-icon-btn theme-toggle" (click)="toggleTheme()" [title]="isDarkMode ? 'Mode clair' : 'Mode sombre'">
            <svg *ngIf="!isDarkMode" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"/>
            </svg>
            <svg *ngIf="isDarkMode" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="12" cy="12" r="5"/>
              <line x1="12" y1="1" x2="12" y2="3"/><line x1="12" y1="21" x2="12" y2="23"/>
              <line x1="4.22" y1="4.22" x2="5.64" y2="5.64"/><line x1="18.36" y1="18.36" x2="19.78" y2="19.78"/>
              <line x1="1" y1="12" x2="3" y2="12"/><line x1="21" y1="12" x2="23" y2="12"/>
              <line x1="4.22" y1="19.78" x2="5.64" y2="18.36"/><line x1="18.36" y1="5.64" x2="19.78" y2="4.22"/>
            </svg>
          </button>

          <!-- Notification Bell -->
          <div class="notification-wrapper">
            <button class="nav-icon-btn notification-btn" [class.has-unread]="unreadCount > 0" (click)="toggleNotifications($event)" title="Notifications">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/>
                <path d="M13.73 21a2 2 0 0 1-3.46 0"/>
              </svg>
              <span class="notification-badge" *ngIf="unreadCount > 0">{{ unreadCount > 9 ? '9+' : unreadCount }}</span>
            </button>

            <!-- Notification Dropdown -->
            <div class="notification-dropdown" *ngIf="showNotifications" (click)="$event.stopPropagation()">
              <div class="dropdown-header">
                <h3>Notifications</h3>
                <button class="mark-read-btn" (click)="markAllAsRead()" *ngIf="unreadCount > 0">
                  Tout marquer comme lu
                </button>
              </div>
              <div class="dropdown-body">
                <div class="notification-list" *ngIf="notifications.length > 0">
                  @for (notif of notifications; track notif.id) {
                    <div class="notification-item" [class.unread]="!notif.resolved" (click)="onNotificationClick(notif)">
                      <div class="notif-icon" [class]="getNotifIconClass(notif.type)">
                        <svg *ngIf="notif.type === 'speeding'" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <circle cx="12" cy="12" r="10"/><path d="M12 6v6l4 2"/>
                        </svg>
                        <svg *ngIf="notif.type === 'geofence'" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <circle cx="12" cy="10" r="3"/><path d="M12 21.7C17.3 17 20 13 20 10a8 8 0 1 0-16 0c0 3 2.7 7 8 11.7z"/>
                        </svg>
                        <svg *ngIf="notif.type === 'stopped'" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <rect x="6" y="4" width="4" height="16"/><rect x="14" y="4" width="4" height="16"/>
                        </svg>
                        <svg *ngIf="notif.type === 'other'" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/>
                        </svg>
                      </div>
                      <div class="notif-content">
                        <span class="notif-message">{{ notif.message }}</span>
                        <span class="notif-time">{{ formatNotifTime(notif.timestamp) }}</span>
                      </div>
                      <div class="notif-unread-dot" *ngIf="!notif.resolved"></div>
                    </div>
                  }
                </div>
                <div class="empty-notifications" *ngIf="notifications.length === 0">
                  <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                    <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/>
                    <path d="M13.73 21a2 2 0 0 1-3.46 0"/>
                  </svg>
                  <p>Aucune notification</p>
                </div>
              </div>
              <div class="dropdown-footer">
                <a (click)="viewAllNotifications()">Voir toutes les notifications</a>
              </div>
            </div>
          </div>

          <div class="nav-separator"></div>
          
          <!-- User Menu -->
          <div class="user-menu-wrapper">
            <div class="user-menu" (click)="toggleUserMenu($event)">
              <div class="user-avatar">{{ getUserInitials() }}</div>
              <span class="user-name">{{ getUserName() }}</span>
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" [class.rotated]="showUserMenu">
                <polyline points="6 9 12 15 18 9"/>
              </svg>
            </div>

            <!-- User Dropdown -->
            <div class="user-dropdown" *ngIf="showUserMenu" (click)="$event.stopPropagation()">
              <div class="user-dropdown-header">
                <div class="user-avatar-large">{{ getUserInitials() }}</div>
                <div class="user-info">
                  <span class="user-fullname">{{ getUserName() }}</span>
                  <span class="user-email">admin&#64;{{ getUserName().toLowerCase().replace(' ', '') }}.com</span>
                </div>
              </div>
              <div class="user-dropdown-divider"></div>
              <div class="user-dropdown-menu">
                <a class="dropdown-item" (click)="onProfileClick()">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/>
                    <circle cx="12" cy="7" r="4"/>
                  </svg>
                  <span>Mon profil</span>
                </a>
                <a class="dropdown-item" (click)="onUsersClick()">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/>
                    <circle cx="9" cy="7" r="4"/>
                    <path d="M23 21v-2a4 4 0 0 0-3-3.87"/>
                    <path d="M16 3.13a4 4 0 0 1 0 7.75"/>
                  </svg>
                  <span>Gestion utilisateurs</span>
                </a>
                <a class="dropdown-item" (click)="onSettingsClick()">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <circle cx="12" cy="12" r="3"/>
                    <path d="M12 1v2M12 21v2M4.22 4.22l1.42 1.42M18.36 18.36l1.42 1.42M1 12h2M21 12h2M4.22 19.78l1.42-1.42M18.36 5.64l1.42-1.42"/>
                  </svg>
                  <span>Paramètres</span>
                </a>
                <a *ngIf="hasModule('fleet_management')" class="dropdown-item" (click)="onFleetManagementClick()">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M12 2L2 7l10 5 10-5-10-5z"/>
                    <path d="M2 17l10 5 10-5"/>
                    <path d="M2 12l10 5 10-5"/>
                  </svg>
                  <span>Gestion Flotte</span>
                </a>
                <a class="dropdown-item" (click)="onHelpClick()">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <circle cx="12" cy="12" r="10"/>
                    <path d="M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3"/>
                    <line x1="12" y1="17" x2="12.01" y2="17"/>
                  </svg>
                  <span>Aide</span>
                </a>
              </div>
              <div class="user-dropdown-divider"></div>
              <div class="user-dropdown-footer">
                <a class="dropdown-item logout" (click)="logout()">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/>
                    <polyline points="16 17 21 12 16 7"/>
                    <line x1="21" y1="12" x2="9" y2="12"/>
                  </svg>
                  <span>Déconnexion</span>
                </a>
              </div>
            </div>
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
      background: var(--bg-nav, #f0f0f0);
      border-bottom: 1px solid var(--border-color, #d0d0d0);
      display: flex;
      align-items: center;
      padding: 0 12px;
      position: sticky;
      top: 0;
      z-index: 1000;
      transition: background 0.3s, border-color 0.3s;
    }

    :host-context([data-theme="dark"]) .top-nav {
      background: #1e293b;
      border-color: #334155;
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
      color: var(--text-primary, #333);
      letter-spacing: -0.3px;
    }

    :host-context([data-theme="dark"]) .nav-brand {
      border-color: #334155;
    }

    :host-context([data-theme="dark"]) .nav-brand:hover {
      background: rgba(255, 255, 255, 0.05);
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
      color: var(--text-secondary, #555);
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.15s;
      text-decoration: none;
      white-space: nowrap;
    }

    .nav-link:hover {
      color: var(--text-primary, #333);
      background: rgba(0, 0, 0, 0.06);
    }

    .nav-link.active {
      color: #6366f1;
      background: rgba(99, 102, 241, 0.12);
      font-weight: 600;
    }

    :host-context([data-theme="dark"]) .nav-link:hover {
      background: rgba(255, 255, 255, 0.08);
    }

    :host-context([data-theme="dark"]) .nav-link.active {
      background: rgba(99, 102, 241, 0.2);
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
      color: var(--text-secondary, #666);
      display: flex;
      align-items: center;
      justify-content: center;
      cursor: pointer;
      transition: all 0.15s;
    }

    .nav-icon-btn:hover {
      background: rgba(0, 0, 0, 0.06);
      color: var(--text-primary, #333);
    }

    :host-context([data-theme="dark"]) .nav-icon-btn:hover {
      background: rgba(255, 255, 255, 0.1);
    }

    .theme-toggle {
      color: var(--text-secondary);
    }

    .theme-toggle:hover {
      color: #f59e0b;
    }

    /* ===== NOTIFICATIONS ===== */
    .notification-wrapper {
      position: relative;
    }

    .notification-btn {
      position: relative;
    }

    .notification-btn.has-unread {
      color: #6366f1;
    }

    .notification-badge {
      position: absolute;
      top: 2px;
      right: 2px;
      min-width: 16px;
      height: 16px;
      padding: 0 4px;
      background: #ef4444;
      color: white;
      font-size: 10px;
      font-weight: 600;
      border-radius: 8px;
      display: flex;
      align-items: center;
      justify-content: center;
      line-height: 1;
    }

    .notification-dropdown {
      position: absolute;
      top: calc(100% + 8px);
      right: 0;
      width: 380px;
      max-height: 480px;
      background: white;
      border-radius: 8px;
      box-shadow: 0 4px 24px rgba(0, 0, 0, 0.15), 0 0 0 1px rgba(0, 0, 0, 0.05);
      display: flex;
      flex-direction: column;
      z-index: 1001;
      overflow: hidden;
    }

    .dropdown-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 14px 16px;
      border-bottom: 1px solid #e5e7eb;
    }

    .dropdown-header h3 {
      margin: 0;
      font-size: 16px;
      font-weight: 600;
      color: #1f2937;
    }

    .mark-read-btn {
      background: none;
      border: none;
      color: #6366f1;
      font-size: 12px;
      font-weight: 500;
      cursor: pointer;
      padding: 4px 8px;
      border-radius: 4px;
    }

    .mark-read-btn:hover {
      background: rgba(99, 102, 241, 0.1);
    }

    .dropdown-body {
      flex: 1;
      overflow-y: auto;
      max-height: 360px;
    }

    .notification-list {
      display: flex;
      flex-direction: column;
    }

    .notification-item {
      display: flex;
      align-items: flex-start;
      gap: 12px;
      padding: 12px 16px;
      cursor: pointer;
      transition: background 0.15s;
      position: relative;
    }

    .notification-item:hover {
      background: #f9fafb;
    }

    .notification-item.unread {
      background: #eff6ff;
    }

    .notification-item.unread:hover {
      background: #dbeafe;
    }

    .notif-icon {
      width: 36px;
      height: 36px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }

    .notif-icon.speeding { background: #fef3c7; color: #d97706; }
    .notif-icon.geofence { background: #dbeafe; color: #2563eb; }
    .notif-icon.stopped { background: #f1f5f9; color: #64748b; }
    .notif-icon.other { background: #e0e7ff; color: #4f46e5; }

    .notif-content {
      flex: 1;
      min-width: 0;
    }

    .notif-message {
      display: block;
      font-size: 13px;
      color: #1f2937;
      line-height: 1.4;
      margin-bottom: 4px;
    }

    .notif-time {
      font-size: 11px;
      color: #9ca3af;
    }

    .notif-unread-dot {
      width: 8px;
      height: 8px;
      background: #6366f1;
      border-radius: 50%;
      flex-shrink: 0;
      margin-top: 6px;
    }

    .empty-notifications {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 40px 20px;
      color: #9ca3af;
    }

    .empty-notifications svg {
      margin-bottom: 12px;
      opacity: 0.5;
    }

    .empty-notifications p {
      margin: 0;
      font-size: 14px;
    }

    .dropdown-footer {
      padding: 12px 16px;
      border-top: 1px solid #e5e7eb;
      text-align: center;
    }

    .dropdown-footer a {
      color: #6366f1;
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
      text-decoration: none;
    }

    .dropdown-footer a:hover {
      text-decoration: underline;
    }

    .nav-separator {
      width: 1px;
      height: 24px;
      background: #d0d0d0;
      margin: 0 4px;
    }

    .user-menu-wrapper {
      position: relative;
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
      transition: transform 0.2s;
    }

    .user-menu svg.rotated {
      transform: rotate(180deg);
    }

    /* User Dropdown */
    .user-dropdown {
      position: absolute;
      top: calc(100% + 8px);
      right: 0;
      width: 280px;
      background: white;
      border-radius: 8px;
      box-shadow: 0 4px 24px rgba(0, 0, 0, 0.15), 0 0 0 1px rgba(0, 0, 0, 0.05);
      z-index: 1001;
      overflow: hidden;
    }

    .user-dropdown-header {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 16px;
      background: linear-gradient(135deg, #6366f1 0%, #8b5cf6 100%);
      color: white;
    }

    .user-avatar-large {
      width: 48px;
      height: 48px;
      border-radius: 8px;
      background: rgba(255, 255, 255, 0.2);
      color: white;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 18px;
      font-weight: 600;
      flex-shrink: 0;
    }

    .user-info {
      display: flex;
      flex-direction: column;
      gap: 2px;
      min-width: 0;
    }

    .user-fullname {
      font-size: 15px;
      font-weight: 600;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .user-email {
      font-size: 12px;
      opacity: 0.85;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .user-dropdown-divider {
      height: 1px;
      background: #e5e7eb;
    }

    .user-dropdown-menu,
    .user-dropdown-footer {
      padding: 8px;
    }

    .dropdown-item {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 12px;
      border-radius: 6px;
      color: #374151;
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.15s;
      text-decoration: none;
    }

    .dropdown-item:hover {
      background: #f3f4f6;
      color: #111827;
    }

    .dropdown-item svg {
      flex-shrink: 0;
      color: #6b7280;
    }

    .dropdown-item:hover svg {
      color: #6366f1;
    }

    .dropdown-item.logout {
      color: #dc2626;
    }

    .dropdown-item.logout:hover {
      background: #fef2f2;
      color: #b91c1c;
    }

    .dropdown-item.logout svg {
      color: #dc2626;
    }

    .dropdown-item.logout:hover svg {
      color: #b91c1c;
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
export class AppLayoutComponent implements OnInit {
  notifications: GPSAlert[] = [];
  showNotifications = false;
  showUserMenu = false;
  unreadCount = 0;
  isCompanyAdmin = false;
  isPlatformAdmin = false;
  modulePermissions: Record<string, boolean> = {};

  constructor(
    private router: Router,
    private apiService: ApiService,
    private themeService: ThemeService
  ) {
    // Load user permissions
    const user = this.apiService.getCurrentUserSync();
    this.isCompanyAdmin = user?.isCompanyAdmin === true;
    this.isPlatformAdmin = user?.userType === 'platform_admin';
    this.modulePermissions = user?.modulePermissions || {};
  }

  get isDarkMode(): boolean {
    return this.themeService.isDarkMode;
  }

  toggleTheme(): void {
    this.themeService.toggleTheme();
  }

  ngOnInit() {
    this.loadNotifications();
  }

  loadNotifications() {
    this.apiService.getAlerts(false, undefined, 20).subscribe({
      next: (alerts) => {
        this.notifications = alerts.map(a => ({
          id: a.id.toString(),
          vehicleId: a.vehicleId?.toString() || '',
          type: a.type as 'speeding' | 'stopped' | 'geofence' | 'other',
          message: a.message,
          timestamp: new Date(a.timestamp),
          resolved: a.resolved
        })) as GPSAlert[];
        this.unreadCount = this.notifications.filter(n => !n.resolved).length;
      },
      error: (err) => console.error('Error loading alerts:', err)
    });
  }

  navigate(path: string) {
    this.router.navigate([path]);
  }

  hasModule(moduleName: string): boolean {
    // Platform admins have access to everything
    if (this.isPlatformAdmin) return true;
    // Company admins have access to all modules in their subscription
    if (this.isCompanyAdmin) return this.modulePermissions[moduleName] === true;
    // Regular users check role + subscription intersection
    return this.modulePermissions[moduleName] === true;
  }

  toggleUserMenu(event: Event) {
    event.stopPropagation();
    this.showUserMenu = !this.showUserMenu;
    if (this.showUserMenu) {
      this.showNotifications = false;
    }
  }

  onProfileClick() {
    this.showUserMenu = false;
    this.router.navigate(['/profile']);
  }

  onSettingsClick() {
    this.showUserMenu = false;
    this.router.navigate(['/settings']);
  }

  onFleetManagementClick() {
    this.showUserMenu = false;
    this.router.navigate(['/fleet-management']);
  }

  onUsersClick() {
    this.showUserMenu = false;
    this.router.navigate(['/users']);
  }

  onHelpClick() {
    this.showUserMenu = false;
    window.open('https://docs.calipso.ma', '_blank');
  }

  toggleNotifications(event: Event) {
    event.stopPropagation();
    this.showNotifications = !this.showNotifications;
    if (this.showNotifications) {
      this.showUserMenu = false;
      this.loadNotifications();
    }
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: Event) {
    this.showNotifications = false;
    this.showUserMenu = false;
  }

  markAllAsRead() {
    this.apiService.resolveAllAlerts().subscribe({
      next: () => this.loadNotifications(),
      error: (err) => console.error('Error resolving alerts:', err)
    });
  }

  onNotificationClick(notif: GPSAlert) {
    if (!notif.resolved) {
      this.apiService.resolveAlert(parseInt(notif.id)).subscribe({
        next: () => this.loadNotifications(),
        error: (err) => console.error('Error resolving alert:', err)
      });
    }
    this.showNotifications = false;
    // Navigate based on notification type
    if (notif.type === 'geofence') {
      this.router.navigate(['/geofences']);
    } else if (notif.type === 'speeding') {
      this.router.navigate(['/monitoring']);
    } else {
      this.router.navigate(['/monitoring']);
    }
  }

  viewAllNotifications() {
    this.showNotifications = false;
    this.router.navigate(['/notifications']);
  }

  getNotifIconClass(type: string): string {
    const typeMap: { [key: string]: string } = {
      speeding: 'speeding',
      stopped: 'stopped',
      geofence: 'geofence',
      other: 'other'
    };
    return typeMap[type] || 'other';
  }

  formatNotifTime(timestamp: Date): string {
    const now = new Date();
    const date = new Date(timestamp);
    const diffMs = now.getTime() - date.getTime();
    const diffMins = Math.floor(diffMs / 60000);
    const diffHours = Math.floor(diffMs / 3600000);
    const diffDays = Math.floor(diffMs / 86400000);

    if (diffMins < 1) return "À l'instant";
    if (diffMins < 60) return `Il y a ${diffMins} min`;
    if (diffHours < 24) return `Il y a ${diffHours}h`;
    if (diffDays < 7) return `Il y a ${diffDays}j`;
    return date.toLocaleDateString('fr-FR', { day: 'numeric', month: 'short' });
  }

  getUserName(): string {
    const user = this.apiService.getCurrentUserSync();
    return user ? user.name : 'User';
  }

  getUserInitials(): string {
    const user = this.apiService.getCurrentUserSync();
    if (!user) return 'U';
    return user.name.substring(0, 2).toUpperCase();
  }

  logout() {
    this.apiService.logout();
    this.router.navigate(['/login']);
  }
}
