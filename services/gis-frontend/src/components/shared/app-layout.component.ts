import { Component, OnInit, OnDestroy, HostListener, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { environment } from '../../environments/environment';
import { Router, RouterModule } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiService } from '../../services/api.service';
import { AuthService } from '../../services/auth.service';
import { ThemeService } from '../../services/theme.service';
import { PermissionService, ModuleKey } from '../../services/permission.service';
import { NotificationService, Notification } from '../../services/notification.service';
import { SignalRService } from '../../services/signalr.service';
import { GeocodingService } from '../../services/geocoding.service';
import { OfflineVehiclesService } from '../../services/offline-vehicles.service';
import { VersionCheckService } from '../../services/version-check.service';
import { SubscriptionStatusService, SubscriptionBanner } from '../../services/subscription-status.service';
import { ChatComponent } from './chat.component';
import { AccidentDecisionModalComponent } from './accident-decision-modal.component';
import { OfflineVehiclesBellComponent } from './offline-vehicles-bell.component';
import * as L from 'leaflet';

// Bell threading (Calypso 7) ─ catégories utilisées pour ranger les
// notifications dans des onglets, et regrouper les événements géofence
// répétitifs en threads pour qu'un véhicule qui fait 50 entrées/sorties
// n'occupe qu'une ligne dans la cloche au lieu de 50.
export type NotifTabKey = 'all' | 'critical' | 'geofence' | 'driving' | 'maintenance' | 'tours' | 'system';

interface NotifThreadGroup {
  isThread: true;
  key: string;
  vehicleName: string;
  geofenceName: string;
  count: number;
  unreadCount: number;
  firstAt: string;
  lastAt: string;
  children: Notification[];
}

type NotifBucket = Notification | NotifThreadGroup;

@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [CommonModule, RouterModule, ChatComponent, AccidentDecisionModalComponent, OfflineVehiclesBellComponent],
  template: `
    <div class="app-container">
      <!-- Bandeau nouvelle version : un déploiement a eu lieu, l'onglet tourne sur un vieux bundle -->
      <div *ngIf="versionCheck.newVersionAvailable$ | async" style="display:flex;align-items:center;justify-content:center;gap:14px;flex-wrap:wrap;background:linear-gradient(90deg,#2563eb,#1d4ed8);color:#fff;padding:8px 16px;font-size:13px;font-weight:600;position:sticky;top:0;z-index:3000;box-shadow:0 2px 8px rgba(0,0,0,0.2);">
        <span>🔄 Une nouvelle version de Calypso est disponible</span>
        <button type="button" (click)="versionCheck.reloadNow()" style="background:rgba(255,255,255,0.25);border:1px solid rgba(255,255,255,0.6);color:#fff;padding:4px 12px;border-radius:6px;font-weight:600;cursor:pointer;white-space:nowrap;">Actualiser maintenant</button>
      </div>
      <!-- Bandeau impersonation ("voir en tant que") -->
      <div *ngIf="isImpersonating()" style="display:flex;align-items:center;justify-content:center;gap:14px;flex-wrap:wrap;background:linear-gradient(90deg,#f59e0b,#f97316);color:#fff;padding:8px 16px;font-size:13px;font-weight:600;position:sticky;top:0;z-index:3000;box-shadow:0 2px 8px rgba(0,0,0,0.2);">
        <span>👁️ Vous voyez l'application <b>en tant que {{ impersonatedName }}</b><span *ngIf="impersonatedCompany"> — société <b>{{ impersonatedCompany }}</b></span></span>
        <button type="button" (click)="stopImpersonation()" style="background:rgba(255,255,255,0.25);border:1px solid rgba(255,255,255,0.6);color:#fff;padding:4px 12px;border-radius:6px;font-weight:600;cursor:pointer;white-space:nowrap;">↩ Revenir à mon compte</button>
      </div>
      <!-- Bandeau abonnement : ambre = expire bientôt (admins société), rouge = J-7 ou grâce après expiration (tous) -->
      <ng-container *ngIf="subStatus.banner$ | async as subBanner">
        <div *ngIf="subBannerVisible(subBanner)" [style.background]="subBanner.level==='danger' ? 'linear-gradient(90deg,#dc2626,#b91c1c)' : 'linear-gradient(90deg,#d97706,#b45309)'" style="display:flex;align-items:center;justify-content:center;gap:14px;flex-wrap:wrap;color:#fff;padding:8px 16px;font-size:13px;font-weight:600;position:sticky;top:0;z-index:3000;box-shadow:0 2px 8px rgba(0,0,0,0.2);">
          <span>{{ subBanner.level==='danger' ? '🔴' : '⏳' }} {{ subBannerText(subBanner) }}</span>
          <button *ngIf="subBanner.level==='warning'" type="button" (click)="dismissSubBanner()" aria-label="Masquer" style="background:transparent;border:none;color:rgba(255,255,255,0.85);font-size:16px;line-height:1;cursor:pointer;padding:2px 6px;">✕</button>
        </div>
      </ng-container>
      <!-- WIALON-STYLE TOP NAVIGATION BAR -->
      <nav class="top-nav">
        <!-- Logo Calypso (pin tourbillon + œil) -->
        <div class="nav-brand" (click)="navigate('/dashboard')">
          <div class="brand-logo">
            <svg width="24" height="32" viewBox="0 0 48 64" fill="none" xmlns="http://www.w3.org/2000/svg" aria-label="Calypso">
              <defs>
                <linearGradient id="calypsoPin" x1="6" y1="4" x2="40" y2="60" gradientUnits="userSpaceOnUse">
                  <stop offset="0" stop-color="#5fe3bd"/>
                  <stop offset="0.45" stop-color="#23a6c9"/>
                  <stop offset="1" stop-color="#1b3f9e"/>
                </linearGradient>
              </defs>
              <!-- goutte / pin -->
              <path d="M24 1.5C11.6 1.5 1.7 11.2 1.7 23.4 1.7 39 24 62.5 24 62.5S46.3 39 46.3 23.4C46.3 11.2 36.4 1.5 24 1.5Z" fill="url(#calypsoPin)"/>
              <!-- vague / tourbillon -->
              <path d="M11.5 28.5C11 17.8 19.4 9.9 30.2 11.6" stroke="#fff" stroke-width="2.7" stroke-linecap="round" fill="none"/>
              <path d="M17.5 32.2C16.4 24.2 22.6 18.4 30 19.6" stroke="#fff" stroke-width="2.3" stroke-linecap="round" fill="none" opacity="0.85"/>
              <!-- œil -->
              <circle cx="31.2" cy="14.4" r="3.1" fill="#fff"/>
            </svg>
          </div>
          <span class="brand-text">{{ brandName }}</span>
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
          <a *ngIf="hasModule('playback')" [routerLink]="['/playback']" routerLinkActive="active" class="nav-link" title="Tracer Playback">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <polygon points="5 3 19 12 5 21 5 3"/>
            </svg>
            <span>Tracer Playback</span>
          </a>

          <!--
            Exploitation dropdown.
            Véhicules entry removed (Calypso 7) — the menu now only links
            to driver / tour / geofencing pages. Vehicle management has its
            own entry point via the dashboard / direct URL /vehicles.
          -->
          <div class="nav-group" *ngIf="hasModule('employees') || hasModule('tours') || hasModule('geofences')">
            <button class="nav-group-btn" [class.active]="openNavGroup === 'exploitation'" (click)="toggleNavGroup('exploitation', $event)">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M7 17m-2 0a2 2 0 1 0 4 0a2 2 0 1 0 -4 0"/><path d="M17 17m-2 0a2 2 0 1 0 4 0a2 2 0 1 0 -4 0"/><path d="M5 17h-2v-6l2-5h9l4 5h1a2 2 0 0 1 2 2v4h-2m-4 0h-6m-6 -6h15m-6 0v-5"/>
              </svg>
              <span>Exploitation</span>
              <svg class="chevron" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="6 9 12 15 18 9"/></svg>
            </button>
            <div class="nav-group-dropdown" *ngIf="openNavGroup === 'exploitation'" (click)="$event.stopPropagation()">
              <a *ngIf="hasModule('employees')" [routerLink]="['/drivers']" routerLinkActive="active" class="nav-dropdown-item" (click)="openNavGroup = null">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/></svg>
                Chauffeurs
              </a>
              <a *ngIf="hasModule('tours')" [routerLink]="['/tournees']" routerLinkActive="active" class="nav-dropdown-item" (click)="openNavGroup = null">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M9 17H5a2 2 0 0 0-2 2 2 2 0 0 0 2 2h2a2 2 0 0 0 2-2zm12-2h-4a2 2 0 0 0-2 2 2 2 0 0 0 2 2h2a2 2 0 0 0 2-2z"/><polyline points="9 17 12 5 15 17"/></svg>
                Tournées
              </a>
              <a *ngIf="hasModule('geofences')" [routerLink]="['/geofences']" routerLinkActive="active" class="nav-dropdown-item" (click)="openNavGroup = null">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="10" r="3"/><path d="M12 21.7C17.3 17 20 13 20 10a8 8 0 1 0-16 0c0 3 2.7 7 8 11.7z"/></svg>
                Géofencing
              </a>
            </div>
          </div>

          <!-- Maintenance dropdown -->
          <div class="nav-group" *ngIf="hasModule('maintenance') || hasModule('carburant')">
            <button class="nav-group-btn" [class.active]="openNavGroup === 'maintenance'" (click)="toggleNavGroup('maintenance', $event)">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>
              </svg>
              <span>Maintenance</span>
              <svg class="chevron" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="6 9 12 15 18 9"/></svg>
            </button>
            <div class="nav-group-dropdown" *ngIf="openNavGroup === 'maintenance'" (click)="$event.stopPropagation()">
              <a *ngIf="hasModule('maintenance')" [routerLink]="['/entretien-programmable']" routerLinkActive="active" class="nav-dropdown-item" (click)="openNavGroup = null">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg>
                Entretiens
              </a>
              <a *ngIf="hasModule('maintenance')" [routerLink]="['/reparations']" routerLinkActive="active" class="nav-dropdown-item" (click)="openNavGroup = null">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/></svg>
                Réparations
              </a>
              <a *ngIf="hasModule('carburant')" [routerLink]="['/carburant']" routerLinkActive="active" class="nav-dropdown-item" (click)="openNavGroup = null">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 22V8l9-6 9 6v14"/><path d="M12 22V12"/><circle cx="18" cy="6" r="2"/></svg>
                Carburants
              </a>
            </div>
          </div>

          <!-- Finances dropdown -->
          <div class="nav-group" *ngIf="hasModule('costs') || hasModule('documents') || hasModule('suppliers')">
            <button class="nav-group-btn" [class.active]="openNavGroup === 'finance'" (click)="toggleNavGroup('finance', $event)">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M12 2v20M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>
              </svg>
              <span>Finances</span>
              <svg class="chevron" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="6 9 12 15 18 9"/></svg>
            </button>
            <div class="nav-group-dropdown" *ngIf="openNavGroup === 'finance'" (click)="$event.stopPropagation()">
              <a *ngIf="hasModule('costs')" [routerLink]="['/depenses']" routerLinkActive="active" class="nav-dropdown-item" (click)="openNavGroup = null">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 2v20M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/></svg>
                Dépenses
              </a>
              <a *ngIf="hasModule('documents')" [routerLink]="['/documents']" routerLinkActive="active" class="nav-dropdown-item" (click)="openNavGroup = null">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
                Échéances
              </a>
              <a *ngIf="hasModule('suppliers')" [routerLink]="['/suppliers']" routerLinkActive="active" class="nav-dropdown-item" (click)="openNavGroup = null">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 21h18"/><path d="M5 21V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2v16"/></svg>
                Fournisseurs
              </a>
            </div>
          </div>

          <!-- Incidents dropdown -->
          <div class="nav-group" *ngIf="hasModule('accidents')">
            <button class="nav-group-btn" [class.active]="openNavGroup === 'incidents'" (click)="toggleNavGroup('incidents', $event)">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/>
              </svg>
              <span>Incidents</span>
              <svg class="chevron" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="6 9 12 15 18 9"/></svg>
            </button>
            <div class="nav-group-dropdown" *ngIf="openNavGroup === 'incidents'" (click)="$event.stopPropagation()">
              <a *ngIf="hasModule('accidents')" [routerLink]="['/accident-reports']" routerLinkActive="active" class="nav-dropdown-item" (click)="openNavGroup = null">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
                Rapports d'accident
              </a>
            </div>
          </div>

          <!-- Rapports (standalone) -->
          <a *ngIf="hasModule('reports')" [routerLink]="['/reports']" routerLinkActive="active" class="nav-link" title="Rapports">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
              <polyline points="14 2 14 8 20 8"/>
              <line x1="16" y1="13" x2="8" y2="13"/>
              <line x1="16" y1="17" x2="8" y2="17"/>
            </svg>
            <span>Rapports</span>
          </a>
        </div>

        <!-- Right Actions -->
        <div class="nav-actions">
          <!-- Simuler accident (user id=1 only — manual test hook for the blocking decision modal) -->
          <button *ngIf="isUserOne()" class="nav-icon-btn simulate-accident-btn" (click)="simulateAccident()" [disabled]="simulating" title="Simuler un accident (utilisateur test)">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/>
              <line x1="12" y1="9" x2="12" y2="13"/>
              <line x1="12" y1="17" x2="12.01" y2="17"/>
            </svg>
          </button>

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

          <!-- Offline Vehicles Bell (same look as notification bell, red badge when vehicles are offline) -->
          <app-offline-vehicles-bell></app-offline-vehicles-bell>

          <!-- Notification Bell -->
          <div class="notification-wrapper">
            <button class="nav-icon-btn notification-btn" [class.has-unread]="bellBadgeCount > 0" (click)="toggleNotifications($event)" title="Notifications">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/>
                <path d="M13.73 21a2 2 0 0 1-3.46 0"/>
              </svg>
              <span class="notification-badge" *ngIf="bellBadgeCount > 0">{{ bellBadgeCount > 9 ? '9+' : bellBadgeCount }}</span>
            </button>

            <!-- Notification Dropdown -->
            <div class="notification-dropdown" *ngIf="showNotifications" (click)="$event.stopPropagation()">
              <div class="dropdown-header">
                <h3>Notifications</h3>
                <button class="mark-read-btn" (click)="markAllAsRead()" *ngIf="unreadCount > 0">
                  Tout marquer comme lu
                </button>
              </div>

              <!-- Calypso 7 (bell threading) : onglets de catégorie. Le clic
                   sur un onglet filtre la liste. Les compteurs montrent le
                   total brut (avant regroupement) pour que l'utilisateur sache
                   exactement combien d'événements il y a. Toutes les catégories
                   sont toujours affichees, meme a 0, pour que l'utilisateur
                   ait une vision complete de la structure de ses notifs. -->
              <div class="notif-tabs">
                <button class="tab-btn" [class.active]="activeNotifTab === 'all'" (click)="setNotifTab('all')">Tout<span class="tab-count">{{ notifTabCount('all') }}</span></button>
                <button class="tab-btn critical" [class.active]="activeNotifTab === 'critical'" [class.empty]="notifTabCount('critical') === 0" (click)="setNotifTab('critical')">Critiques<span class="tab-count">{{ notifTabCount('critical') }}</span></button>
                <button class="tab-btn" [class.active]="activeNotifTab === 'geofence'" [class.empty]="notifTabCount('geofence') === 0" (click)="setNotifTab('geofence')">Géofences<span class="tab-count">{{ notifTabCount('geofence') }}</span></button>
                <button class="tab-btn" [class.active]="activeNotifTab === 'driving'" [class.empty]="notifTabCount('driving') === 0" (click)="setNotifTab('driving')">Conduite<span class="tab-count">{{ notifTabCount('driving') }}</span></button>
                <button class="tab-btn" [class.active]="activeNotifTab === 'maintenance'" [class.empty]="notifTabCount('maintenance') === 0" (click)="setNotifTab('maintenance')">Maintenance<span class="tab-count">{{ notifTabCount('maintenance') }}</span></button>
                <button class="tab-btn" [class.active]="activeNotifTab === 'tours'" [class.empty]="notifTabCount('tours') === 0" (click)="setNotifTab('tours')">Tournées<span class="tab-count">{{ notifTabCount('tours') }}</span></button>
                <button class="tab-btn" [class.active]="activeNotifTab === 'system'" [class.empty]="notifTabCount('system') === 0" (click)="setNotifTab('system')">Système<span class="tab-count">{{ notifTabCount('system') }}</span></button>
              </div>

              <div class="dropdown-body">
                @if (visibleBuckets().length > 0) {
                  <div class="notification-list">
                    @for (bucket of visibleBuckets(); track $index) {
                      @if (isThread(bucket)) {
                        <!-- Thread géofence : N événements même véhicule + même zone, fenêtre 1h -->
                        <div class="notif-thread" [class.has-unread]="bucket.unreadCount > 0">
                          <div class="thread-header" (click)="toggleThread(bucket.key, $event)">
                            <div class="notif-icon geofence">
                              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <circle cx="12" cy="10" r="3"/><path d="M12 21.7C17.3 17 20 13 20 10a8 8 0 1 0-16 0c0 3 2.7 7 8 11.7z"/>
                              </svg>
                            </div>
                            <div class="notif-content">
                              <span class="notif-title">
                                {{ bucket.vehicleName }} ↔ {{ bucket.geofenceName }}
                                <span class="thread-badge">{{ bucket.count }}</span>
                              </span>
                              <span class="notif-message">{{ threadSummary(bucket) }} · {{ threadRange(bucket) }}</span>
                            </div>
                            <svg class="thread-caret" [class.open]="isThreadExpanded(bucket.key)" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                              <polyline points="6 9 12 15 18 9"/>
                            </svg>
                          </div>
                          @if (isThreadExpanded(bucket.key)) {
                            <div class="thread-children">
                              @for (child of bucket.children; track child.id) {
                                <div class="notification-item nested" [class.unread]="!child.isRead" (click)="onNotificationClick(child)">
                                  <div class="notif-icon geofence">
                                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                      <circle cx="12" cy="10" r="3"/><path d="M12 21.7C17.3 17 20 13 20 10a8 8 0 1 0-16 0c0 3 2.7 7 8 11.7z"/>
                                    </svg>
                                  </div>
                                  <div class="notif-content">
                                    <span class="notif-message">{{ child.message }}</span>
                                    <span class="notif-time">{{ formatNotifTime(child.createdAt) }} · {{ formatNotifDate(child.createdAt) }}</span>
                                  </div>
                                  <div class="notif-unread-dot" *ngIf="!child.isRead"></div>
                                </div>
                              }
                            </div>
                          }
                        </div>
                      } @else {
                        <div class="notification-item" [class.unread]="!bucket.isRead" (click)="onNotificationClick(bucket)">
                          <div class="notif-icon" [class]="getNotifIconClass(bucket.type)">
                            <svg *ngIf="bucket.type === 'accident_detected'" width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                              <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/>
                              <line x1="12" y1="9" x2="12" y2="13"/>
                              <line x1="12" y1="17" x2="12.01" y2="17"/>
                            </svg>
                            <svg *ngIf="bucket.type === 'speed_alert' || bucket.type === 'driving_behavior'" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                              <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/>
                            </svg>
                            <svg *ngIf="bucket.type === 'geofence' || bucket.type === 'geofence_event'" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                              <circle cx="12" cy="10" r="3"/><path d="M12 21.7C17.3 17 20 13 20 10a8 8 0 1 0-16 0c0 3 2.7 7 8 11.7z"/>
                            </svg>
                            <svg *ngIf="bucket.type === 'admin_action'" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                              <path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/>
                            </svg>
                            <svg *ngIf="bucket.type === 'maintenance_due'" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                              <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>
                            </svg>
                            <svg *ngIf="bucket.type === 'vehicle_stop'" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                              <rect x="6" y="4" width="4" height="16"/><rect x="14" y="4" width="4" height="16"/>
                            </svg>
                            <svg *ngIf="bucket.type === 'tour_started' || bucket.type === 'tour_waypoint' || bucket.type === 'tour_completed'" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                              <path d="M9 17H5a2 2 0 0 0-2 2 2 2 0 0 0 2 2h2a2 2 0 0 0 2-2zm12-2h-4a2 2 0 0 0-2 2 2 2 0 0 0 2 2h2a2 2 0 0 0 2-2z"/><polyline points="9 17 12 5 15 17"/>
                            </svg>
                            <svg *ngIf="!['accident_detected','speed_alert','driving_behavior','geofence','geofence_event','admin_action','maintenance_due','vehicle_stop','tour_started','tour_waypoint','tour_completed'].includes(bucket.type)" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                              <circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/>
                            </svg>
                          </div>
                          <div class="notif-content">
                            <span class="notif-title">{{ bucket.title }}</span>
                            <span class="notif-message">{{ bucket.message }}</span>
                            <span class="notif-time">{{ formatNotifTime(bucket.createdAt) }} · {{ formatNotifDate(bucket.createdAt) }}</span>
                          </div>
                          <div class="notif-unread-dot" *ngIf="!bucket.isRead"></div>
                        </div>
                      }
                    }
                  </div>
                } @else {
                  <div class="empty-notifications">
                    <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                      <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/>
                      <path d="M13.73 21a2 2 0 0 1-3.46 0"/>
                    </svg>
                    <p>Aucune notification dans cette catégorie</p>
                  </div>
                }
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
                <a *ngIf="hasModule('settings')" class="dropdown-item" (click)="onSettingsClick()">
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
                <a *ngIf="hasModule('vehicles')" class="dropdown-item" (click)="onVehiclesClick()">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M7 17m-2 0a2 2 0 1 0 4 0a2 2 0 1 0 -4 0"/>
                    <path d="M17 17m-2 0a2 2 0 1 0 4 0a2 2 0 1 0 -4 0"/>
                    <path d="M5 17h-2v-6l2-5h9l4 5h1a2 2 0 0 1 2 2v4h-2m-4 0h-6m-6 -6h15m-6 0v-5"/>
                  </svg>
                  <span>Véhicules</span>
                </a>
                <a *ngIf="hasModule('fleet_management') && isLocationCompany()" class="dropdown-item" (click)="onEmpruntsClick()">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <rect x="1" y="3" width="15" height="13" rx="2"/>
                    <path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-5"/>
                    <circle cx="5.5" cy="18.5" r="2.5"/>
                    <circle cx="18.5" cy="18.5" r="2.5"/>
                  </svg>
                  <span>Emprunts</span>
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

      <!-- Strictly-blocking accident decision modal.
           Subscribes to SignalR 'accident_detected' notifications with
           metadata.requiresDecision === 'true' and shows the confirm /
           dismiss / postpone choice to company admins. -->
      <app-accident-decision-modal></app-accident-decision-modal>

      <!-- Global Geofence Event Modal (opens from notification click on any page) -->
      <div class="gf-modal-overlay" *ngIf="showGeofenceModal" (click)="closeGeofenceModal()">
        <div class="gf-modal" (click)="$event.stopPropagation()">
          <div class="gf-modal-header">
            <h3>{{ gfModalData?.geofenceName || 'Géofence' }}</h3>
            <button class="gf-modal-close" (click)="closeGeofenceModal()">&times;</button>
          </div>
          <div class="gf-modal-body">
            <div class="gf-modal-map" id="gfModalMap"></div>
            <div class="gf-modal-info">
              <div class="gf-info-row">
                <span class="gf-icon">&#128663;</span>
                <span class="gf-label">Véhicule</span>
                <span class="gf-value">{{ gfModalData?.vehicleName }}</span>
              </div>
              <div class="gf-info-row">
                <span class="gf-icon">&#128205;</span>
                <span class="gf-label">Adresse</span>
                <span class="gf-value">{{ gfModalData?.address || 'Chargement...' }}</span>
              </div>
              <div class="gf-info-row">
                <span class="gf-icon">&#128336;</span>
                <span class="gf-label">Date / Heure</span>
                <span class="gf-value">{{ gfModalData?.timestamp }}</span>
              </div>
              <div class="gf-info-row">
                <span class="gf-icon">{{ gfModalData?.eventType === 'entry' ? '&#9989;' : '&#10060;' }}</span>
                <span class="gf-label">Événement</span>
                <span class="gf-value">{{ gfModalData?.eventType === 'entry' ? 'Entrée dans la zone' : 'Sortie de la zone' }}</span>
              </div>
            </div>
            <div class="gf-history" *ngIf="gfHistory.length > 0">
              <h4>Historique des passages</h4>
              <table class="gf-history-table">
                <thead>
                  <tr>
                    <th>Date / Heure</th>
                    <th>Véhicule</th>
                    <th>Événement</th>
                  </tr>
                </thead>
                <tbody>
                  <tr *ngFor="let evt of gfHistory" (click)="onGfHistoryClick(evt)"
                      [class.selected]="selectedGfHistoryId === evt.id"
                      style="cursor: pointer;" title="Voir sur la carte">
                    <td>{{ formatDate(evt.timestamp) }}</td>
                    <td>{{ evt.vehicleName }}</td>
                    <td>
                      <span class="gf-event-badge" [class.entry]="evt.type === 'entry'" [class.exit]="evt.type === 'exit'">
                        {{ evt.type === 'entry' ? 'Entrée' : 'Sortie' }}
                      </span>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </div>
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
      z-index: 1100;
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
      font-size: 18px;
      font-weight: 700;
      /* Slate-blue of the Calypso wordmark */
      color: #2b5876;
      letter-spacing: -0.3px;
    }

    :host-context([data-theme="dark"]) .nav-brand {
      border-color: #334155;
    }

    :host-context([data-theme="dark"]) .nav-brand:hover {
      background: rgba(255, 255, 255, 0.05);
    }

    /* Keep the wordmark legible on the dark theme. */
    :host-context([data-theme="dark"]) .brand-text {
      color: #cfe3f2;
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

    /* Debug-only "Simuler accident" trigger — red to signal "test button, do not ship". */
    .simulate-accident-btn {
      color: #ef4444;
    }

    .simulate-accident-btn:hover:not(:disabled) {
      background: rgba(239, 68, 68, 0.12);
      color: #dc2626;
    }

    .simulate-accident-btn:disabled {
      opacity: 0.5;
      cursor: not-allowed;
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
      width: 720px;
      max-height: 540px;
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
    .notif-icon.accident {
      background: #fee2e2;
      color: #991b1b;
      box-shadow: 0 0 0 2px rgba(153, 27, 27, 0.12);
    }

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

    /* Geofence Event Modal (global, in app-layout) */
    .gf-modal-overlay {
      position: fixed; inset: 0; z-index: 10000;
      background: rgba(0,0,0,0.5); backdrop-filter: blur(4px);
      display: flex; align-items: center; justify-content: center;
      animation: gfFadeIn 0.2s ease-out;
    }
    @keyframes gfFadeIn { from { opacity: 0; } to { opacity: 1; } }
    .gf-modal {
      background: #fff; border-radius: 12px; width: 700px; max-width: 95vw;
      max-height: 85vh; overflow: hidden; box-shadow: 0 25px 60px rgba(0,0,0,0.3);
      display: flex; flex-direction: column;
    }
    .gf-modal-header {
      display: flex; align-items: center; justify-content: space-between;
      padding: 16px 20px; background: #1e3a8a; color: #fff;
    }
    .gf-modal-header h3 { margin: 0; font-size: 16px; font-weight: 600; }
    .gf-modal-close {
      background: none; border: none; color: #fff; font-size: 24px;
      cursor: pointer; line-height: 1; padding: 0 4px; opacity: 0.8;
    }
    .gf-modal-close:hover { opacity: 1; }
    .gf-modal-body {
      padding: 16px 20px; overflow-y: auto; flex: 1;
    }
    .gf-modal-map {
      height: 280px; border-radius: 8px; overflow: hidden;
      margin-bottom: 16px; border: 1px solid #e2e8f0;
    }
    .gf-modal-info {
      display: grid; grid-template-columns: 1fr 1fr; gap: 10px;
      margin-bottom: 16px;
    }
    .gf-info-row {
      display: flex; align-items: center; gap: 8px;
      padding: 8px 12px; background: #f8fafc; border-radius: 8px;
    }
    .gf-icon { font-size: 16px; flex-shrink: 0; }
    .gf-label { font-size: 11px; color: #64748b; font-weight: 600; text-transform: uppercase; }
    .gf-value { font-size: 13px; color: #1e293b; font-weight: 500; margin-left: auto; text-align: right; }
    .gf-history h4 { font-size: 14px; color: #1e293b; margin: 0 0 10px; }
    .gf-history-table { width: 100%; border-collapse: collapse; font-size: 12px; }
    .gf-history-table th {
      text-align: left; padding: 8px 12px; background: #f8fafc;
      color: #64748b; font-weight: 600; border-bottom: 2px solid #e2e8f0;
    }
    .gf-history-table td { padding: 8px 12px; border-bottom: 1px solid #f1f5f9; color: #334155; }
    .gf-history-table tbody tr { cursor: pointer; transition: background 0.15s; }
    .gf-history-table tbody tr:hover { background: #f1f5f9; }
    .gf-history-table tbody tr.selected { background: #dbeafe; }
    .gf-event-badge {
      display: inline-block; padding: 2px 10px; border-radius: 12px;
      font-size: 11px; font-weight: 600;
    }
    .gf-event-badge.entry { background: #dcfce7; color: #166534; }
    .gf-event-badge.exit { background: #fee2e2; color: #991b1b; }

    /* Calypso 7 — bell tabs + threading
       Style « Linear / Gmail » : segments fins, une seule ligne,
       fitting 5-6 onglets dans 420px. Pas d'emoji dans les libellés
       (l'icône colorée à gauche de chaque event suffit). */
    .notif-tabs {
      display: flex;
      align-items: center;
      justify-content: flex-start;
      gap: 0;
      padding: 0 8px;
      border-bottom: 1px solid #e2e8f0;
      background: white;
      overflow-x: auto;
      overflow-y: hidden;
      scrollbar-width: thin;
    }
    .notif-tabs::-webkit-scrollbar { height: 4px; }
    .notif-tabs::-webkit-scrollbar-track { background: transparent; }
    .notif-tabs::-webkit-scrollbar-thumb { background: #cbd5e1; border-radius: 2px; }
    .notif-tabs::-webkit-scrollbar-thumb:hover { background: #94a3b8; }
    .tab-btn {
      flex-shrink: 0;
      display: inline-flex;
      align-items: center;
      gap: 5px;
      padding: 10px 9px;
      background: transparent;
      border: none;
      border-bottom: 2px solid transparent;
      font-size: 12px;
      color: #64748b;
      cursor: pointer;
      font-weight: 500;
      transition: color 0.15s, border-color 0.15s;
      white-space: nowrap;
      margin-bottom: -1px;
    }
    .tab-btn:hover { color: #1e293b; }
    .tab-btn.active {
      color: #2563eb;
      border-bottom-color: #2563eb;
      font-weight: 600;
    }
    .tab-btn.critical.active {
      color: #dc2626;
      border-bottom-color: #dc2626;
    }
    .tab-count {
      background: #f1f5f9;
      color: #64748b;
      padding: 1px 7px;
      border-radius: 10px;
      font-size: 10px;
      font-weight: 600;
      min-width: 20px;
      text-align: center;
      transition: all 0.15s;
    }
    .tab-btn.active .tab-count { background: #dbeafe; color: #2563eb; }
    .tab-btn.critical.active .tab-count { background: #fee2e2; color: #dc2626; }
    /* Onglet sans aucune notif : toujours visible mais grise pour
       indiquer qu'il est cliquable / consultable sans contenu actuel. */
    .tab-btn.empty { color: #cbd5e1; }
    .tab-btn.empty .tab-count { background: #f8fafc; color: #cbd5e1; }
    .tab-btn.empty:hover { color: #94a3b8; }

    /* Thread card — matche le style notification-item, juste un chevron
       en plus pour le toggle, et un badge discret avec le compteur. */
    .notif-thread { display: block; }
    .thread-header {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px 16px;
      cursor: pointer;
      transition: background 0.15s;
      border-bottom: 1px solid #f1f5f9;
      background: white;
    }
    .thread-header:hover { background: #f8fafc; }
    .notif-thread.has-unread .thread-header { background: #f0f7ff; }
    .notif-thread.has-unread .thread-header:hover { background: #e6f1ff; }
    .thread-badge {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      background: #2563eb;
      color: white;
      font-size: 10px;
      font-weight: 700;
      padding: 1px 7px;
      border-radius: 10px;
      margin-left: 6px;
      min-width: 20px;
    }
    .thread-caret {
      flex-shrink: 0;
      color: #94a3b8;
      transition: transform 0.2s ease;
    }
    .thread-caret.open { transform: rotate(180deg); }
    .thread-children {
      background: #f8fafc;
      max-height: 280px;
      overflow-y: auto;
      border-bottom: 1px solid #f1f5f9;
    }
    .thread-children .notification-item {
      padding: 10px 16px 10px 36px;
      border: none;
      border-bottom: 1px solid #e2e8f0;
      border-radius: 0;
      background: transparent;
      position: relative;
    }
    .thread-children .notification-item:last-child { border-bottom: none; }
    .thread-children .notification-item:hover { background: #eff6ff; }
    .thread-children .notification-item::before {
      content: '';
      position: absolute;
      left: 22px;
      top: 0;
      bottom: 0;
      width: 2px;
      background: #cbd5e1;
    }
    .thread-children .notification-item .notif-icon { display: none; }
  `]
})
export class AppLayoutComponent implements OnInit, OnDestroy {
  /** Brand name shown in the navbar — per-deployment (Calypso / Bougeo / …). */
  readonly brandName = environment.brandName;

  /** Impersonation ("voir en tant que") — bandeau + retour super-admin. */
  isImpersonating(): boolean { return this.authService.isImpersonating(); }
  get impersonatedName(): string { return this.authService.getCurrentUserSync()?.name || ''; }
  get impersonatedCompany(): string { return this.authService.getCurrentUserSync()?.companyName || ''; }
  stopImpersonation(): void { this.authService.stopImpersonation(); window.location.href = '/admin/clients'; }
  notifications: Notification[] = [];
  showNotifications = false;
  showUserMenu = false;
  openNavGroup: string | null = null;
  unreadCount = 0;
  private subs: Subscription[] = [];

  // Bell tabs + threading state (Calypso 7)
  activeNotifTab: NotifTabKey = 'all';
  expandedThreads: { [key: string]: boolean } = {};

  /**
   * Compteur affiche sur la pastille de la cloche : on ignore volontairement
   * les notifs de type geofence_event qui floodent la base (87% du volume),
   * sinon le badge resterait coince a "9+" en permanence et ne servirait
   * plus de signal pour les vraies alertes (accident, maintenance, etc.).
   * Les events geofence restent visibles dans l'onglet Geofences avec leur
   * propre compteur.
   */
  get bellBadgeCount(): number {
    return this.notifications.filter(n =>
      !n.isRead && this.notifCategory(n.type) !== 'geofence'
    ).length;
  }

  // Geofence event modal (global, available on any page)
  showGeofenceModal = false;
  gfModalData: {
    geofenceName: string; vehicleName: string; address: string;
    timestamp: string; eventType: string; lat: number; lng: number;
  } | null = null;
  gfHistory: any[] = [];
  selectedGfHistoryId: number | null = null;
  private gfModalMap?: L.Map;
  private gfHistoryMarker?: L.Marker;

  constructor(
    private router: Router,
    private apiService: ApiService,
    private authService: AuthService,
    private themeService: ThemeService,
    private permissionService: PermissionService,
    private notificationService: NotificationService,
    private signalR: SignalRService,
    private geocodingService: GeocodingService,
    private offlineVehiclesService: OfflineVehiclesService,
    public versionCheck: VersionCheckService,
    public subStatus: SubscriptionStatusService,
    private cdr: ChangeDetectorRef
  ) {}

  /** Bannière abonnement : ambre J-30 (admins société), rouge J-7 / grâce (tous). */
  subBannerVisible(b: SubscriptionBanner | null): boolean {
    if (!b) return false;
    if (b.level === 'danger') return true;
    if (b.level === 'warning') {
      if (sessionStorage.getItem('sub_banner_dismissed') === '1') return false;
      const u = this.authService.getCurrentUserSync() as any;
      return !!(u?.isCompanyAdmin || u?.isSystemAdmin);
    }
    return false;
  }

  subBannerText(b: SubscriptionBanner): string {
    if (b.reason === 'grace')
      return `L'abonnement de votre société a expiré — l'accès sera suspendu dans ${b.graceDaysLeft} jour${(b.graceDaysLeft ?? 0) > 1 ? 's' : ''}`;
    if (b.reason === 'expired')   // suspension auto désactivée : expirée mais tolérée
      return `L'abonnement de votre société a expiré — merci de régulariser votre situation`;
    if ((b.daysRemaining ?? 99) <= 1)
      return `L'abonnement de votre société expire aujourd'hui`;
    return `L'abonnement de votre société expire dans ${b.daysRemaining} jours`;
  }

  dismissSubBanner(): void {
    sessionStorage.setItem('sub_banner_dismissed', '1');
    this.subStatus.banner$.next(this.subStatus.banner$.value); // re-render
  }

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

    // Start the offline-vehicles watcher so the header bell stays in sync on every page
    this.offlineVehiclesService.start();

    // Détection des nouveaux déploiements : bandeau « Actualiser » quand un
    // bundle plus récent est en ligne (onglets restés ouverts longtemps).
    this.versionCheck.start();

    // Surveillance de l'abonnement de la société : bannière J-30/J-7/grâce et
    // redirection vers l'écran de blocage si suspendu. Une suspension par le
    // sys_admin est poussée en temps réel via SignalR → re-vérification immédiate.
    this.subStatus.start();
    this.subs.push(
      this.signalR.subscriptionChanged$.subscribe(() => this.subStatus.refresh())
    );

    // Subscribe to real-time unread count
    this.subs.push(
      this.notificationService.unreadCount$.subscribe(count => {
        this.unreadCount = count;
      })
    );

    // Subscribe to real-time new notifications
    this.subs.push(
      this.notificationService.notifications$.subscribe(notifications => {
        this.notifications = this.filterByAlertPrefs(notifications);
      })
    );
  }

  ngOnDestroy() {
    this.subs.forEach(s => s.unsubscribe());
    this.offlineVehiclesService.stop();
    this.signalR.stopConnection();
  }

  /**
   * Calypso 6 (P9): the Profile page has 4 checkboxes (vitesse, géofencing+
   * documents+remorquage, offline, entretiens) that the user can toggle to
   * mute categories of notifications. They were saved to localStorage but
   * the bell never read them — every notification still showed up. This
   * helper applies the mute mask so a disabled category disappears from the
   * dropdown. Critical types (accident, admin, low_voltage, password) stay
   * visible regardless because the user must always see them.
   */
  private filterByAlertPrefs(items: any[]): any[] {
    if (!items || items.length === 0) return items || [];
    let prefs: any = null;
    try {
      const raw = localStorage.getItem('userAlertPreferences');
      if (raw) prefs = JSON.parse(raw);
    } catch { /* ignore corrupted storage */ }
    if (!prefs) return items;

    const isAllowed = (type: string): boolean => {
      switch (type) {
        case 'speed_alert':
        case 'speeding':
          return prefs.vitesse !== false;
        case 'geofence_violation':
        case 'geofence_entry':
        case 'geofence_exit':
        case 'document_expiry':
        case 'tow_detected':
        case 'accident_tow':
          return prefs.geofencingDocsTow !== false;
        case 'offline_vehicle':
        case 'vehicle_offline':
          return prefs.offline !== false;
        case 'maintenance_due':
        case 'maintenance_prediction':
          return prefs.entretiens !== false;
        default:
          // Critical types (accident, admin_action, low_voltage, password,
          // power_cut, …) and anything we did not list always pass through.
          return true;
      }
    };
    return items.filter(n => isAllowed(n.type));
  }

  loadNotifications() {
    // Calypso 7 (bell threading) : on charge plus d'items (200 au lieu de 20)
    // pour que le regroupement par véhicule + zone ait du sens. Sans ça, un
    // véhicule qui fait 50 entrées/sorties sur Ezzahra remplissait toute la
    // cloche avec des doublons et noyait les alertes critiques.
    this.notificationService.getNotifications(1, 200).subscribe({
      next: (page) => {
        this.notifications = this.filterByAlertPrefs(page.items);
        this.unreadCount = page.unreadCount;
        this.notificationService.notifications$.next(page.items);
        this.notificationService.unreadCount$.next(page.unreadCount);
      },
      error: (err) => console.error('Error loading notifications:', err)
    });
  }

  // ─────────────────────────────────────────────────────────────────────
  // Bell tabs + threading (Calypso 7)
  // ─────────────────────────────────────────────────────────────────────

  /**
   * Map d'un type de notification vers une catégorie d'onglet de la cloche.
   * Tout type non listé tombe dans "system".
   */
  private notifCategory(type: string): NotifTabKey {
    switch (type) {
      case 'accident_detected':
      case 'accident_possible_damage':
      case 'accident_tow_detected':
      case 'accident_decision':
      case 'accident_tow':
      case 'immobilization_request':
      case 'immobilization_response':
      case 'power_cut':
      case 'low_voltage':
      case 'battery_low':
        return 'critical';
      case 'geofence':
      case 'geofence_event':
      case 'geofence_entry':
      case 'geofence_exit':
      case 'geofence_violation':
        return 'geofence';
      case 'speed_alert':
      case 'speeding':
      case 'driving_behavior':
      // Calypso 7 (post-threading) : un permis de conduire concerne le
      // chauffeur (pas le vehicule), donc semantiquement il appartient a
      // la categorie « Conduite » et pas a « Maintenance ». document_expiry
      // (assurance / vignette / visite technique) reste sur Maintenance.
      case 'driver_permit_expiry':
        return 'driving';
      case 'maintenance_due':
      case 'maintenance_prediction':
      case 'document_expiry':
        return 'maintenance';
      case 'tour_started':
      case 'tour_waypoint':
      case 'tour_completed':
      case 'tour_overdue':
        return 'tours';
      default:
        return 'system';
    }
  }

  /**
   * Compteur d'éléments d'une catégorie. Le compte d'un thread géofence est
   * le nombre d'événements regroupés (chaque enfant compte 1), pas 1 pour
   * tout le thread, pour que l'utilisateur voie qu'il y a bien 50 events si
   * 50 events.
   */
  notifTabCount(tab: NotifTabKey): number {
    if (tab === 'all') return this.notifications.length;
    return this.notifications.filter(n => this.notifCategory(n.type) === tab).length;
  }

  setNotifTab(tab: NotifTabKey) {
    this.activeNotifTab = tab;
  }

  /**
   * Construit la liste à afficher dans la cloche pour l'onglet actif.
   *
   * Pour les notifications géofence, on regroupe en threads les événements
   * consécutifs partageant le même véhicule ET la même zone, avec une
   * fenêtre glissante d'1h entre événements. Un thread isolé d'un seul
   * événement reste affiché tel quel (pas de header inutile).
   *
   * Pour les autres catégories, la liste reste plate (un événement = une
   * ligne) — c'est le flood géofence le vrai problème, pas les autres.
   */
  visibleBuckets(): NotifBucket[] {
    let pool = this.notifications;
    if (this.activeNotifTab !== 'all') {
      pool = pool.filter(n => this.notifCategory(n.type) === this.activeNotifTab);
    }

    const buckets: NotifBucket[] = [];
    const ONE_HOUR_MS = 60 * 60 * 1000;
    let openGroup: NotifThreadGroup | null = null;

    const closeGroup = () => {
      if (openGroup) {
        if (openGroup.count >= 2) {
          buckets.push(openGroup);
        } else {
          buckets.push(openGroup.children[0]);
        }
        openGroup = null;
      }
    };

    for (const n of pool) {
      const isGeofence = this.notifCategory(n.type) === 'geofence';
      if (!isGeofence) {
        closeGroup();
        buckets.push(n);
        continue;
      }

      const m = n.metadata || {};
      const vid = m.vehicleId ?? m.VehicleId;
      const gid = m.geofenceId ?? m.GeofenceId;
      const key = `${vid ?? 'v'}:${gid ?? 'g'}`;
      const ts = new Date(n.createdAt).getTime();

      if (openGroup
          && openGroup.key === key
          && Math.abs(new Date(openGroup.lastAt).getTime() - ts) <= ONE_HOUR_MS) {
        openGroup.children.push(n);
        openGroup.count = openGroup.children.length;
        if (!n.isRead) openGroup.unreadCount++;
        // children are listed most-recent first because the API returns DESC,
        // so lastAt is the *first* element (oldest of the conversation when
        // scrolling) and firstAt is the most recent. Keep both up to date.
        if (ts < new Date(openGroup.lastAt).getTime()) openGroup.lastAt = n.createdAt;
        if (ts > new Date(openGroup.firstAt).getTime()) openGroup.firstAt = n.createdAt;
        continue;
      }

      closeGroup();
      openGroup = {
        isThread: true,
        key,
        vehicleName: m.vehicleName ?? m.VehicleName ?? '?',
        geofenceName: m.geofenceName ?? m.GeofenceName ?? '?',
        count: 1,
        unreadCount: n.isRead ? 0 : 1,
        firstAt: n.createdAt,
        lastAt: n.createdAt,
        children: [n]
      };
    }
    closeGroup();
    return buckets;
  }

  isThread(b: NotifBucket): b is NotifThreadGroup {
    return (b as any).isThread === true;
  }

  toggleThread(key: string, ev: Event) {
    ev.stopPropagation();
    this.expandedThreads[key] = !this.expandedThreads[key];
  }

  isThreadExpanded(key: string): boolean {
    return !!this.expandedThreads[key];
  }

  threadSummary(t: NotifThreadGroup): string {
    const entries = t.children.filter((c: any) => (c.metadata?.eventType ?? c.metadata?.EventType) === 'entry').length;
    const exits = t.children.filter((c: any) => (c.metadata?.eventType ?? c.metadata?.EventType) === 'exit').length;
    if (entries > 0 && exits > 0) return `${entries} entrée(s) · ${exits} sortie(s)`;
    if (entries > 0) return `${entries} entrée(s)`;
    if (exits > 0) return `${exits} sortie(s)`;
    return `${t.count} événement(s)`;
  }

  threadRange(t: NotifThreadGroup): string {
    const a = this.formatNotifTime(t.lastAt);
    const b = this.formatNotifTime(t.firstAt);
    if (a === b) return a;
    return `${a} → ${b}`;
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

  onVehiclesClick() {
    this.showUserMenu = false;
    this.router.navigate(['/vehicles']);
  }

  onEmpruntsClick() {
    this.showUserMenu = false;
    this.router.navigate(['/emprunts']);
  }

  isLocationCompany(): boolean {
    const user = this.authService.getCurrentUserSync();
    return user?.companyType === 'location';
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
      // Calypso 7 (post-threading) : ouvrir la cloche = consulter les
      // notifs = tout marquer comme lu, sans laisser le badge clignoter
      // pour rien. On chaine load -> markAllAsRead pour eviter une race
      // condition ou la reponse de getNotifications arrivait apres
      // markAllAsRead et ecrasait l'etat local marque-lu.
      this.notificationService.getNotifications(1, 200).subscribe({
        next: (page) => {
          this.notifications = this.filterByAlertPrefs(page.items);
          this.unreadCount = page.unreadCount;
          this.notificationService.notifications$.next(page.items);
          this.notificationService.unreadCount$.next(page.unreadCount);
          // Mark all as read APRES la mise a jour locale.
          this.markAllAsRead();
        },
        error: (err) => {
          console.error('Error loading notifications:', err);
          // Si le load fail, on marque quand meme ce qu'on a en local.
          this.markAllAsRead();
        }
      });
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
      // Open geofence modal in-place (no page navigation)
      const meta = notif.metadata;
      if (meta?.latitude && meta?.longitude) {
        const geofenceId = meta.geofenceId || notif.referenceId;
        const vehicleId = meta.vehicleId;
        const lat = parseFloat(meta.latitude);
        const lng = parseFloat(meta.longitude);
        const timestamp = meta.timestamp;
        const eventType = meta.eventType || 'entry';
        this.openGeofenceEventModal(geofenceId, vehicleId, lat, lng, timestamp, eventType);
      } else if (notif.actionUrl) {
        this.router.navigateByUrl(notif.actionUrl);
      }
    } else if (notif.type === 'expense' || notif.type === 'expense_created' || (notif.title && notif.title.includes('Dépense'))) {
      // Navigate to specific expense
      const expenseId = notif.metadata?.expenseId || notif.referenceId;
      if (expenseId) {
        this.router.navigate(['/depenses'], { queryParams: { expenseId } });
      } else {
        this.router.navigate(['/depenses']);
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
      accident_detected: 'accident',
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

  private parseUtcTs(ts: string | Date): Date {
    if (ts instanceof Date) return ts;
    if (!ts) return new Date(NaN);
    // Backend returns UTC without 'Z'; coerce to UTC so display uses local tz (GMT+1)
    const s = ts.endsWith('Z') || /[+-]\d{2}:?\d{2}$/.test(ts) ? ts : ts + 'Z';
    return new Date(s);
  }

  formatNotifTime(timestamp: string | Date): string {
    const now = new Date();
    const date = this.parseUtcTs(timestamp);
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
    const date = this.parseUtcTs(timestamp);
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

  // ─── Debug: simulate accident (user id=1 only) ───────────────────────────
  // The backend endpoint is gated server-side to UserId == 1, but we also
  // hide the button for everyone else so it never shows up in a screenshare.

  simulating = false;

  isUserOne(): boolean {
    const authUser = this.authService.getCurrentUserSync();
    return authUser?.id === '1';
  }

  simulateAccident(): void {
    if (this.simulating) return;
    this.simulating = true;

    this.apiService.simulateAccident().subscribe({
      next: (res) => {
        this.simulating = false;
        // The SignalR notification should arrive within milliseconds and the
        // AccidentDecisionModalComponent (mounted at the root) will pop. We
        // intentionally do not navigate here — the modal is the UX.
        console.log('[Simulate] accident event created:', res.accidentEventId);
      },
      error: (err) => {
        this.simulating = false;
        console.error('[Simulate] failed to create simulated accident', err);
        alert('Échec de la simulation : ' + (err?.error?.error ?? err?.message ?? 'erreur inconnue'));
      },
    });
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

  // ─── Geofence Event Modal (global, opens from notification on any page) ───

  openGeofenceEventModal(geofenceId: number, vehicleId: number, lat: number, lng: number, timestamp?: string, eventType?: string) {
    // Format timestamp
    let formattedTime = '';
    if (timestamp) {
      const d = new Date(timestamp);
      if (!isNaN(d.getTime())) {
        formattedTime = d.toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric' })
          + ' à ' + d.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
      }
    }

    this.gfModalData = {
      geofenceName: 'Chargement...',
      vehicleName: `Véhicule #${vehicleId}`,
      address: 'Chargement...',
      timestamp: formattedTime || new Date().toLocaleString('fr-FR'),
      eventType: eventType || 'entry',
      lat, lng
    };
    this.gfHistory = [];
    this.selectedGfHistoryId = null;
    this.showGeofenceModal = true;
    this.cdr.detectChanges(); // Force Angular to render the modal DOM immediately

    // Init map right away (modal DOM is now guaranteed to exist)
    setTimeout(() => this.initGfModalMap(null, lat, lng), 50);

    // Load vehicle name
    this.apiService.getVehicles().subscribe({
      next: (vehicles: any[]) => {
        const vehicle = vehicles.find((v: any) => v.id === vehicleId);
        if (vehicle && this.gfModalData) {
          this.gfModalData = { ...this.gfModalData, vehicleName: vehicle.plate || vehicle.name || `Véhicule #${vehicleId}` };
        }
      }
    });

    // Load geofence details — draw geofence zone on map when it arrives
    this.apiService.getGeofence(geofenceId).subscribe({
      next: (gf: any) => {
        if (gf && this.gfModalData) {
          this.gfModalData = { ...this.gfModalData, geofenceName: gf.name || 'Géofence' };
          // Draw geofence zone on the already-initialized map
          if (this.gfModalMap) {
            this.drawGeofenceZoneOnMap(gf);
          }
        }
      }
    });

    // Reverse geocode
    this.geocodingService.reverseGeocode(lat, lng).subscribe({
      next: (address: string) => {
        if (this.gfModalData) {
          this.gfModalData = { ...this.gfModalData, address };
        }
      }
    });

    // Load history
    this.apiService.getGeofenceEventsByGeofence(geofenceId, 50).subscribe({
      next: (events: any[]) => { this.gfHistory = events || []; }
    });
  }

  closeGeofenceModal() {
    this.showGeofenceModal = false;
    this.gfModalData = null;
    this.gfHistory = [];
    this.selectedGfHistoryId = null;
    this.gfHistoryMarker = undefined;
    if (this.gfMapResizeObserver) { this.gfMapResizeObserver.disconnect(); this.gfMapResizeObserver = undefined; }
    if (this.gfModalMap) {
      this.gfModalMap.remove();
      this.gfModalMap = undefined;
    }
  }

  private gfMapResizeObserver?: ResizeObserver;

  private initGfModalMap(gf: any | null, lat: number, lng: number) {
    const container = document.getElementById('gfModalMap');
    if (!container) return;
    if (this.gfModalMap) { this.gfModalMap.remove(); }
    if (this.gfMapResizeObserver) { this.gfMapResizeObserver.disconnect(); }

    this.gfModalMap = L.map(container, { zoomControl: true }).setView([lat, lng], 14);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '© OSM', maxZoom: 19
    }).addTo(this.gfModalMap);

    // Vehicle marker
    L.marker([lat, lng], {
      icon: L.divIcon({
        html: '<div style="background:#f59e0b;width:20px;height:20px;border-radius:50%;border:3px solid #fff;box-shadow:0 2px 8px rgba(0,0,0,0.4);display:flex;align-items:center;justify-content:center;font-size:12px;">&#128663;</div>',
        className: '', iconSize: [20, 20], iconAnchor: [10, 10]
      })
    }).addTo(this.gfModalMap);

    // Draw geofence zone if data is already available
    if (gf) { this.drawGeofenceZoneOnMap(gf); }

    // Use ResizeObserver for bulletproof invalidateSize
    const map = this.gfModalMap;
    this.gfMapResizeObserver = new ResizeObserver(() => {
      map?.invalidateSize();
    });
    this.gfMapResizeObserver.observe(container);
    // Also call it on next frames as fallback
    requestAnimationFrame(() => map?.invalidateSize());
    setTimeout(() => map?.invalidateSize(), 300);
  }

  private drawGeofenceZoneOnMap(gf: any) {
    if (!this.gfModalMap) return;
    let layer: any;
    if (gf.type === 'circle' && gf.center) {
      const cLat = gf.center.lat || gf.center.latitude;
      const cLng = gf.center.lng || gf.center.longitude;
      layer = L.circle([cLat, cLng], {
        radius: gf.radius || 200, color: '#3B82F6', weight: 2, fillColor: '#3B82F6', fillOpacity: 0.15
      }).addTo(this.gfModalMap);
    } else if (gf.coordinates?.length > 0) {
      const coords = gf.coordinates.map((c: any) => [c.lat || c.latitude || c[0], c.lng || c.longitude || c[1]] as [number, number]);
      layer = L.polygon(coords, {
        color: '#3B82F6', weight: 2, fillColor: '#3B82F6', fillOpacity: 0.15
      }).addTo(this.gfModalMap);
    }
    // Auto-zoom to fit the entire geofence zone
    if (layer && this.gfModalMap) {
      setTimeout(() => this.gfModalMap?.fitBounds(layer.getBounds(), { padding: [30, 30], maxZoom: 16 }), 200);
    }
  }

  onGfHistoryClick(evt: any) {
    if (!this.gfModalMap || !evt.latitude || !evt.longitude) return;
    this.selectedGfHistoryId = evt.id;

    if (this.gfHistoryMarker) {
      this.gfModalMap.removeLayer(this.gfHistoryMarker);
    }

    this.gfHistoryMarker = L.marker([evt.latitude, evt.longitude], {
      icon: L.divIcon({
        html: `<div style="background:${evt.type === 'entry' ? '#22c55e' : '#ef4444'};width:18px;height:18px;border-radius:50%;border:3px solid #fff;box-shadow:0 2px 8px rgba(0,0,0,0.4);display:flex;align-items:center;justify-content:center;font-size:10px;color:#fff;font-weight:bold;">${evt.type === 'entry' ? 'E' : 'S'}</div>`,
        className: '', iconSize: [18, 18], iconAnchor: [9, 9]
      })
    }).addTo(this.gfModalMap);

    this.gfModalMap.flyTo([evt.latitude, evt.longitude], 15, { duration: 0.5 });

    if (this.gfModalData) {
      const d = new Date(evt.timestamp);
      const formattedTime = d.toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric' })
        + ' à ' + d.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
      this.gfModalData = {
        ...this.gfModalData,
        vehicleName: evt.vehicleName || this.gfModalData.vehicleName,
        eventType: evt.type || this.gfModalData.eventType,
        timestamp: formattedTime
      };
    }
  }

  formatDate(ts: string): string {
    try {
      const d = new Date(ts);
      return d.toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric' })
        + ' ' + d.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
    } catch { return ts; }
  }
}
