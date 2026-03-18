import { Component, OnInit, OnDestroy, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiService } from '../../services/api.service';
import { AuthService } from '../../services/auth.service';
import { ThemeService } from '../../services/theme.service';
import { PermissionService, ModuleKey } from '../../services/permission.service';
import { NotificationService, Notification } from '../../services/notification.service';
import { SignalRService } from '../../services/signalr.service';
import { ChatComponent } from './chat.component';

@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [CommonModule, RouterModule, ChatComponent],
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
          <span class="brand-text">Calypso</span>
        </div>

        <!-- Navigation Links -->
        <div class="nav-links">
          <a *ngIf="hasModule('dashboard')" [routerLink]="['/dashboard']" routerLinkActive="active" class="nav-link" title="Tableau de bord">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <rect x="3" y="3" width="7" height="7" rx="1"/>
              <rect x="14" y="3" width="7" height="7" rx="1"/>
              <rect x="3" y="14" width="7" height="7" rx="1"/>
              <rect x="14" y="14" width="7" height="7" rx="1"/>
            </svg>
            <span>Tableau de bord</span>
          </a>
          <a *ngIf="hasModule('monitoring')" [routerLink]="['/monitoring']" routerLinkActive="active" [routerLinkActiveOptions]="{exact: true}" class="nav-link" title="Suivi en direct">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>
            </svg>
            <span>Suivi en direct</span>
          </a>
          <a *ngIf="hasModule('monitoring')" [routerLink]="['/playback']" routerLinkActive="active" class="nav-link" title="Tracer Playback">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <polygon points="5 3 19 12 5 21 5 3"/>
            </svg>
            <span>Tracer Playback</span>
          </a>

          <!-- Opérations dropdown -->
          <div class="nav-group" *ngIf="hasModule('reports') || hasModule('geofences')">
            <button class="nav-group-btn" [class.active]="openNavGroup === 'ops'" (click)="toggleNavGroup('ops', $event)">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                <polyline points="14 2 14 8 20 8"/>
              </svg>
              <span>Opérations</span>
              <svg class="chevron" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="6 9 12 15 18 9"/></svg>
            </button>
            <div class="nav-group-dropdown" *ngIf="openNavGroup === 'ops'" (click)="$event.stopPropagation()">
              <a *ngIf="hasModule('reports')" [routerLink]="['/reports']" routerLinkActive="active" class="nav-dropdown-item" (click)="openNavGroup = null">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/></svg>
                Rapports
              </a>
              <a *ngIf="hasModule('geofences')" [routerLink]="['/geofences']" routerLinkActive="active" class="nav-dropdown-item" (click)="openNavGroup = null">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="10" r="3"/><path d="M12 21.7C17.3 17 20 13 20 10a8 8 0 1 0-16 0c0 3 2.7 7 8 11.7z"/></svg>
                Géozones
              </a>
              <a [routerLink]="['/tournees']" routerLinkActive="active" class="nav-dropdown-item" (click)="openNavGroup = null">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M9 17H5a2 2 0 0 0-2 2 2 2 0 0 0 2 2h2a2 2 0 0 0 2-2zm12-2h-4a2 2 0 0 0-2 2 2 2 0 0 0 2 2h2a2 2 0 0 0 2-2z"/><polyline points="9 17 12 5 15 17"/></svg>
                Tournées
              </a>
            </div>
          </div>

          <!-- Flotte dropdown -->
          <div class="nav-group" *ngIf="hasModule('maintenance') || hasModule('suppliers')">
            <button class="nav-group-btn" [class.active]="openNavGroup === 'fleet'" (click)="toggleNavGroup('fleet', $event)">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>
              </svg>
              <span>Flotte</span>
              <svg class="chevron" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="6 9 12 15 18 9"/></svg>
            </button>
            <div class="nav-group-dropdown" *ngIf="openNavGroup === 'fleet'" (click)="$event.stopPropagation()">
              <a *ngIf="hasModule('maintenance')" [routerLink]="['/entretien-programmable']" routerLinkActive="active" class="nav-dropdown-item" (click)="openNavGroup = null">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg>
                Entretien
              </a>
              <a *ngIf="hasModule('maintenance')" [routerLink]="['/reparations']" routerLinkActive="active" class="nav-dropdown-item" (click)="openNavGroup = null">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/></svg>
                Réparations
              </a>
              <a *ngIf="hasModule('suppliers')" [routerLink]="['/suppliers']" routerLinkActive="active" class="nav-dropdown-item" (click)="openNavGroup = null">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 21h18"/><path d="M5 21V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2v16"/></svg>
                Fournisseurs
              </a>
            </div>
          </div>

          <!-- Finances dropdown -->
          <div class="nav-group" *ngIf="hasModule('costs')">
            <button class="nav-group-btn" [class.active]="openNavGroup === 'finance'" (click)="toggleNavGroup('finance', $event)">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M12 2v20M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>
              </svg>
              <span>Finances</span>
              <svg class="chevron" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="6 9 12 15 18 9"/></svg>
            </button>
            <div class="nav-group-dropdown" *ngIf="openNavGroup === 'finance'" (click)="$event.stopPropagation()">
              <a [routerLink]="['/depenses']" routerLinkActive="active" class="nav-dropdown-item" (click)="openNavGroup = null">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 2v20M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/></svg>
                Dépenses
              </a>
              <a [routerLink]="['/carburant']" routerLinkActive="active" class="nav-dropdown-item" (click)="openNavGroup = null">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 22V8l9-6 9 6v14"/><path d="M12 22V12"/><circle cx="18" cy="6" r="2"/></svg>
                Carburant
              </a>
            </div>
          </div>

          <!-- Personnel dropdown -->
          <div class="nav-group" *ngIf="hasModule('employees') || hasModule('documents') || hasModule('accidents')">
            <button class="nav-group-btn" [class.active]="openNavGroup === 'hr'" (click)="toggleNavGroup('hr', $event)">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/>
                <circle cx="9" cy="7" r="4"/>
              </svg>
              <span>Personnel</span>
              <svg class="chevron" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="6 9 12 15 18 9"/></svg>
            </button>
            <div class="nav-group-dropdown" *ngIf="openNavGroup === 'hr'" (click)="$event.stopPropagation()">
              <a *ngIf="hasModule('employees')" [routerLink]="['/drivers']" routerLinkActive="active" class="nav-dropdown-item" (click)="openNavGroup = null">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/></svg>
                Chauffeurs
              </a>
              <a *ngIf="hasModule('documents')" [routerLink]="['/documents']" routerLinkActive="active" class="nav-dropdown-item" (click)="openNavGroup = null">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
                Échéances
              </a>
              <a *ngIf="hasModule('accidents')" [routerLink]="['/sinistres']" routerLinkActive="active" class="nav-dropdown-item" (click)="openNavGroup = null">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
                Sinistres
              </a>
            </div>
          </div>
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
                    <div class="notification-item" [class.unread]="!notif.isRead" (click)="onNotificationClick(notif)">
                      <div class="notif-icon" [class]="getNotifIconClass(notif.type)">
                        <!-- Speed / Driving behavior -->
                        <svg *ngIf="notif.type === 'speed_alert' || notif.type === 'driving_behavior'" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/>
                        </svg>
                        <!-- Geofence -->
                        <svg *ngIf="notif.type === 'geofence' || notif.type === 'geofence_event'" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <circle cx="12" cy="10" r="3"/><path d="M12 21.7C17.3 17 20 13 20 10a8 8 0 1 0-16 0c0 3 2.7 7 8 11.7z"/>
                        </svg>
                        <!-- Admin action (employee actions) -->
                        <svg *ngIf="notif.type === 'admin_action'" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/>
                        </svg>
                        <!-- Maintenance -->
                        <svg *ngIf="notif.type === 'maintenance_due'" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>
                        </svg>
                        <!-- Vehicle stop -->
                        <svg *ngIf="notif.type === 'vehicle_stop'" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <rect x="6" y="4" width="4" height="16"/><rect x="14" y="4" width="4" height="16"/>
                        </svg>
                        <!-- Tour events -->
                        <svg *ngIf="notif.type === 'tour_started' || notif.type === 'tour_waypoint' || notif.type === 'tour_completed'" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <path d="M9 17H5a2 2 0 0 0-2 2 2 2 0 0 0 2 2h2a2 2 0 0 0 2-2zm12-2h-4a2 2 0 0 0-2 2 2 2 0 0 0 2 2h2a2 2 0 0 0 2-2z"/><polyline points="9 17 12 5 15 17"/>
                        </svg>
                        <!-- Default -->
                        <svg *ngIf="!['speed_alert','driving_behavior','geofence','geofence_event','admin_action','maintenance_due','vehicle_stop','tour_started','tour_waypoint','tour_completed'].includes(notif.type)" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/>
                        </svg>
                      </div>
                      <div class="notif-content">
                        <span class="notif-title">{{ notif.title }}</span>
                        <span class="notif-message">{{ notif.message }}</span>
                        <span class="notif-time">{{ formatNotifTime(notif.createdAt) }} · {{ formatNotifDate(notif.createdAt) }}</span>
                      </div>
                      <div class="notif-unread-dot" *ngIf="!notif.isRead"></div>
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
                  <span class="user-email">{{ getUserEmail() }}</span>
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
                <a *ngIf="hasModule('users')" class="dropdown-item" (click)="onUsersClick()">
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

      <!-- Chat Widget -->
      <app-chat></app-chat>
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
      z-index: 10000;
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

    /* ===== NAV GROUP DROPDOWNS ===== */
    .nav-group {
      position: relative;
    }

    .nav-group-btn {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 8px 10px;
      border-radius: 4px;
      color: var(--text-secondary, #555);
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.15s;
      white-space: nowrap;
      background: none;
      border: none;
      font-family: inherit;
    }

    .nav-group-btn:hover {
      color: var(--text-primary, #333);
      background: rgba(0, 0, 0, 0.06);
    }

    .nav-group-btn.active {
      color: #6366f1;
      background: rgba(99, 102, 241, 0.12);
    }

    :host-context([data-theme="dark"]) .nav-group-btn:hover {
      background: rgba(255, 255, 255, 0.08);
    }

    :host-context([data-theme="dark"]) .nav-group-btn.active {
      background: rgba(99, 102, 241, 0.2);
    }

    .nav-group-btn svg {
      flex-shrink: 0;
      opacity: 0.8;
    }

    .nav-group-btn .chevron {
      opacity: 0.5;
      transition: transform 0.2s;
    }

    .nav-group-btn.active .chevron {
      transform: rotate(180deg);
      opacity: 0.8;
    }

    .nav-group-dropdown {
      position: absolute;
      top: calc(100% + 4px);
      left: 0;
      min-width: 180px;
      background: white;
      border-radius: 8px;
      box-shadow: 0 4px 16px rgba(0, 0, 0, 0.12), 0 0 0 1px rgba(0, 0, 0, 0.06);
      padding: 4px;
      z-index: 1002;
      animation: navDropIn 0.15s ease-out;
    }

    @keyframes navDropIn {
      from { opacity: 0; transform: translateY(-4px); }
      to { opacity: 1; transform: translateY(0); }
    }

    :host-context([data-theme="dark"]) .nav-group-dropdown {
      background: #1e293b;
      box-shadow: 0 4px 16px rgba(0, 0, 0, 0.3), 0 0 0 1px rgba(255, 255, 255, 0.08);
    }

    .nav-dropdown-item {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 8px 12px;
      border-radius: 6px;
      color: var(--text-secondary, #555);
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.12s;
      text-decoration: none;
      white-space: nowrap;
    }

    .nav-dropdown-item:hover {
      color: var(--text-primary, #333);
      background: rgba(0, 0, 0, 0.05);
    }

    .nav-dropdown-item.active {
      color: #6366f1;
      background: rgba(99, 102, 241, 0.1);
      font-weight: 600;
    }

    :host-context([data-theme="dark"]) .nav-dropdown-item:hover {
      background: rgba(255, 255, 255, 0.08);
    }

    :host-context([data-theme="dark"]) .nav-dropdown-item.active {
      background: rgba(99, 102, 241, 0.2);
    }

    .nav-dropdown-item svg {
      flex-shrink: 0;
      opacity: 0.7;
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
    .notif-icon.maintenance { background: #fef2f2; color: #dc2626; }
    .notif-icon.admin-action { background: #ecfdf5; color: #059669; }
    .notif-icon.tour { background: #ecfdf5; color: #10b981; }
    .notif-icon.other { background: #e0e7ff; color: #4f46e5; }

    .notif-content {
      flex: 1;
      min-width: 0;
    }

    .notif-title {
      display: block;
      font-size: 13px;
      font-weight: 600;
      color: #1f2937;
      line-height: 1.3;
      margin-bottom: 2px;
    }

    .notif-message {
      display: block;
      font-size: 12px;
      color: #6b7280;
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
      position: relative;
      z-index: 1;
    }

    /* ===== RESPONSIVE ===== */
    /* Laptops: icon-only mode */
    @media (max-width: 1200px) {
      .nav-link span,
      .nav-group-btn span,
      .nav-group-btn .chevron {
        display: none;
      }
      .nav-link,
      .nav-group-btn {
        padding: 8px 10px;
      }
    }

    /* Tablets */
    @media (max-width: 992px) {
      .nav-links {
        max-width: calc(100vw - 250px);
      }
      .nav-link {
        padding: 8px 8px;
      }
      .user-name {
        display: none;
      }
    }

    /* Mobile */
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
export class AppLayoutComponent implements OnInit, OnDestroy {
  notifications: Notification[] = [];
  showNotifications = false;
  showUserMenu = false;
  openNavGroup: string | null = null;
  unreadCount = 0;
  private subs: Subscription[] = [];

  constructor(
    private router: Router,
    private apiService: ApiService,
    private authService: AuthService,
    private themeService: ThemeService,
    private permissionService: PermissionService,
    private notificationService: NotificationService,
    private signalR: SignalRService
  ) {}

  get isDarkMode(): boolean {
    return this.themeService.isDarkMode;
  }

  toggleTheme(): void {
    this.themeService.toggleTheme();
  }

  ngOnInit() {
    // Start SignalR globally so real-time notifications work on all pages
    this.signalR.startConnection();

    this.loadNotifications();
    this.notificationService.loadUnreadCount();

    // Subscribe to real-time unread count
    this.subs.push(
      this.notificationService.unreadCount$.subscribe(count => {
        this.unreadCount = count;
      })
    );

    // Subscribe to real-time new notifications
    this.subs.push(
      this.notificationService.notifications$.subscribe(notifications => {
        this.notifications = notifications;
      })
    );
  }

  ngOnDestroy() {
    this.subs.forEach(s => s.unsubscribe());
    this.signalR.stopConnection();
  }

  loadNotifications() {
    this.notificationService.getNotifications(1, 20).subscribe({
      next: (page) => {
        this.notifications = page.items;
        this.unreadCount = page.unreadCount;
        this.notificationService.notifications$.next(page.items);
        this.notificationService.unreadCount$.next(page.unreadCount);
      },
      error: (err) => console.error('Error loading notifications:', err)
    });
  }

  navigate(path: string) {
    this.router.navigate([path]);
  }

  hasModule(moduleName: string): boolean {
    return this.permissionService.hasModuleAccess(moduleName as ModuleKey);
  }

  toggleNavGroup(group: string, event: Event) {
    event.stopPropagation();
    this.openNavGroup = this.openNavGroup === group ? null : group;
    this.showNotifications = false;
    this.showUserMenu = false;
  }

  toggleUserMenu(event: Event) {
    event.stopPropagation();
    this.showUserMenu = !this.showUserMenu;
    if (this.showUserMenu) {
      this.showNotifications = false;
      this.openNavGroup = null;
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
    window.open('https://docs.calypso.ma', '_blank');
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
    this.openNavGroup = null;
  }

  markAllAsRead() {
    this.notificationService.markAllAsReadLocal();
  }

  onNotificationClick(notif: Notification) {
    if (!notif.isRead) {
      this.notificationService.markAsReadLocal(notif.id);
    }
    this.showNotifications = false;

    // Navigate based on type with specific handling
    if (notif.type === 'geofence_event' || notif.type === 'geofence') {
      const meta = notif.metadata;
      if (meta?.latitude && meta?.longitude) {
        this.router.navigate(['/monitoring'], {
          queryParams: {
            lat: meta.latitude,
            lng: meta.longitude,
            zoom: 17,
            geofenceId: meta.geofenceId || notif.referenceId,
            vehicleId: meta.vehicleId
          }
        });
      } else if (notif.actionUrl) {
        this.router.navigateByUrl(notif.actionUrl);
      } else {
        this.router.navigate(['/monitoring']);
      }
    } else if (notif.actionUrl) {
      this.router.navigateByUrl(notif.actionUrl);
    } else if (notif.type === 'maintenance_due') {
      this.router.navigate(['/entretien-programmable']);
    } else if (notif.type === 'speed_alert') {
      this.router.navigate(['/monitoring']);
    } else {
      this.router.navigate(['/notifications']);
    }
  }

  viewAllNotifications() {
    this.showNotifications = false;
    this.router.navigate(['/notifications']);
  }

  getNotifIconClass(type: string): string {
    const typeMap: { [key: string]: string } = {
      speed_alert: 'speeding',
      driving_behavior: 'speeding',
      vehicle_stop: 'stopped',
      geofence_event: 'geofence',
      geofence: 'geofence',
      maintenance_due: 'maintenance',
      admin_action: 'admin-action',
      tour_started: 'tour',
      tour_waypoint: 'tour',
      tour_completed: 'tour',
      user_created: 'other',
      system: 'other'
    };
    return typeMap[type] || 'other';
  }

  formatNotifTime(timestamp: string | Date): string {
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

  formatNotifDate(timestamp: string | Date): string {
    if (!timestamp) return '';
    const date = new Date(timestamp);
    return date.toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric' })
      + ' ' + date.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
  }

  getUserName(): string {
    const authUser = this.authService.getCurrentUserSync();
    if (authUser?.name) return authUser.name;
    const apiUser = this.apiService.getCurrentUserSync();
    return apiUser ? apiUser.name : 'User';
  }

  getUserEmail(): string {
    const authUser = this.authService.getCurrentUserSync();
    if (authUser?.email) return authUser.email;
    const apiUser = this.apiService.getCurrentUserSync();
    return apiUser ? apiUser.email : '';
  }

  getUserInitials(): string {
    const name = this.getUserName();
    if (!name || name === 'User') return 'U';
    return name.split(' ').map(n => n[0]).join('').substring(0, 2).toUpperCase();
  }

  logout() {
    this.apiService.logout();
    this.router.navigate(['/login']);
  }
}
