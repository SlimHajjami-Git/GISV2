import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiService } from '../services/api.service';
import { NotificationService, Notification } from '../services/notification.service';
import { AppLayoutComponent } from './shared/app-layout.component';

@Component({
  selector: 'app-notifications',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent],
  template: `
    <app-layout>
      <div class="notifications-page">
        <!-- Header -->
        <div class="notif-header">
          <div class="header-left">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/>
              <path d="M13.73 21a2 2 0 0 1-3.46 0"/>
            </svg>
            <h1>Notifications</h1>
            <span class="unread-count" *ngIf="unreadCount > 0">{{ unreadCount }}</span>
          </div>
          <div class="header-actions">
            <button class="btn-mark-all" (click)="markAllRead()" *ngIf="unreadCount > 0">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="20 6 9 17 4 12"/></svg>
              Tout marquer lu
            </button>
          </div>
        </div>

        <!-- Filters -->
        <div class="notif-filters">
          <button class="filter-btn" [class.active]="filterType === ''" (click)="filterType = ''; loadNotifications()">Toutes</button>
          <button class="filter-btn" [class.active]="filterType === 'geofence_event'" (click)="filterType = 'geofence_event'; loadNotifications()">
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z"/><circle cx="12" cy="10" r="3"/></svg>
            Géofences
          </button>
          <button class="filter-btn" [class.active]="filterType === 'speed_alert'" (click)="filterType = 'speed_alert'; loadNotifications()">
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg>
            Vitesse
          </button>
          <button class="filter-btn" [class.active]="filterType === 'driving_behavior'" (click)="filterType = 'driving_behavior'; loadNotifications()">
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/></svg>
            Conduite
          </button>
          <button class="filter-btn" [class.active]="filterType === 'maintenance_due'" (click)="filterType = 'maintenance_due'; loadNotifications()">
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/></svg>
            Maintenance
          </button>
          <div class="filter-toggle">
            <label class="toggle-small">
              <input type="checkbox" [(ngModel)]="unreadOnly" (change)="loadNotifications()">
              <span>Non lues uniquement</span>
            </label>
          </div>
        </div>

        <!-- Notification List -->
        <div class="notif-list">
          @for (notif of notifications; track notif.id) {
            <div class="notif-item" [class.unread]="!notif.isRead" (click)="openNotification(notif)">
              <div class="notif-icon" [class]="getNotifIconClass(notif.type)">
                <svg *ngIf="notif.type === 'geofence_event' || notif.type === 'geofence'" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z"/><circle cx="12" cy="10" r="3"/></svg>
                <svg *ngIf="notif.type === 'speed_alert'" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg>
                <svg *ngIf="notif.type === 'driving_behavior'" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/></svg>
                <svg *ngIf="notif.type === 'maintenance_due'" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/></svg>
                <svg *ngIf="!['geofence','geofence_event','speed_alert','driving_behavior','maintenance_due'].includes(notif.type)" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.73 21a2 2 0 0 1-3.46 0"/></svg>
              </div>
              <div class="notif-content">
                <div class="notif-title">{{ notif.title }}</div>
                <div class="notif-message">{{ notif.message }}</div>
                <div class="notif-meta">
                  <span class="notif-type-badge" [class]="notif.type">{{ getTypeLabel(notif.type) }}</span>
                  <span class="notif-priority" [class]="notif.priority" *ngIf="notif.priority === 'high' || notif.priority === 'urgent'">{{ notif.priority === 'high' ? 'Haute' : 'Urgente' }}</span>
                  <span class="notif-time">{{ formatTime(notif.createdAt) }}</span>
                  <span class="notif-datetime">{{ formatDateTime(notif.createdAt) }}</span>
                </div>
              </div>
              <div class="notif-actions">
                <button class="notif-action-btn" title="Marquer comme lu" (click)="markRead(notif); $event.stopPropagation()" *ngIf="!notif.isRead">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="20 6 9 17 4 12"/></svg>
                </button>
                <button class="notif-action-btn danger" title="Supprimer" (click)="deleteNotif(notif); $event.stopPropagation()">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/></svg>
                </button>
              </div>
            </div>
          }

          @if (notifications.length === 0 && !loading) {
            <div class="empty-state">
              <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/>
                <path d="M13.73 21a2 2 0 0 1-3.46 0"/>
              </svg>
              <p>Aucune notification</p>
              <span>Les alertes de votre flotte apparaîtront ici</span>
            </div>
          }

          @if (loading) {
            <div class="loading-state">
              <div class="spinner"></div>
              <span>Chargement...</span>
            </div>
          }

          <!-- Load more -->
          <div class="load-more" *ngIf="hasMore && !loading">
            <button class="btn-load-more" (click)="loadMore()">Charger plus</button>
          </div>
        </div>
      </div>

    </app-layout>
  `,
  styles: [`
    .notifications-page {
      flex: 1;
      background: #f1f5f9;
      display: flex;
      flex-direction: column;
      min-height: calc(100vh - 42px);
    }

    .notif-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 14px 20px;
      background: white;
      border-bottom: 1px solid #e2e8f0;
    }

    .header-left {
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .header-left svg { color: #3b82f6; }

    .header-left h1 {
      margin: 0;
      font-size: 16px;
      font-weight: 600;
      color: #1e293b;
    }

    .unread-count {
      background: #ef4444;
      color: white;
      font-size: 11px;
      font-weight: 700;
      padding: 2px 7px;
      border-radius: 10px;
      min-width: 18px;
      text-align: center;
    }

    .btn-mark-all {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 7px 14px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 6px;
      font-size: 12px;
      color: #3b82f6;
      cursor: pointer;
      font-weight: 500;
    }
    .btn-mark-all:hover { background: #eff6ff; border-color: #93c5fd; }

    .notif-filters {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 10px 20px;
      background: white;
      border-bottom: 1px solid #e2e8f0;
      flex-wrap: wrap;
    }

    .filter-btn {
      display: flex;
      align-items: center;
      gap: 4px;
      padding: 6px 12px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      font-size: 11px;
      color: #64748b;
      cursor: pointer;
      font-weight: 500;
    }
    .filter-btn:hover { background: #f8fafc; }
    .filter-btn.active { background: #eff6ff; border-color: #3b82f6; color: #2563eb; }

    .filter-toggle {
      margin-left: auto;
    }

    .toggle-small {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 11px;
      color: #64748b;
      cursor: pointer;
    }
    .toggle-small input { width: 14px; height: 14px; }

    .notif-list {
      flex: 1;
      overflow-y: auto;
      padding: 8px 20px;
    }

    .notif-item {
      display: flex;
      align-items: flex-start;
      gap: 12px;
      padding: 14px 16px;
      background: white;
      border-radius: 8px;
      margin-bottom: 6px;
      cursor: pointer;
      transition: all 0.15s;
      border: 1px solid #e2e8f0;
    }
    .notif-item:hover { box-shadow: 0 2px 6px rgba(0,0,0,0.06); }
    .notif-item.unread { border-left: 3px solid #3b82f6; background: #fafbff; }

    .notif-icon {
      width: 36px;
      height: 36px;
      border-radius: 8px;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }
    .notif-icon.geofence, .notif-icon.geofence_event { background: #dbeafe; color: #2563eb; }
    .notif-icon.speed_alert { background: #fef3c7; color: #d97706; }
    .notif-icon.driving_behavior { background: #fee2e2; color: #dc2626; }
    .notif-icon.maintenance_due { background: #e0e7ff; color: #4f46e5; }
    .notif-icon.admin_action { background: #f1f5f9; color: #64748b; }

    .notif-content { flex: 1; min-width: 0; }

    .notif-title {
      font-size: 13px;
      font-weight: 600;
      color: #1e293b;
      margin-bottom: 2px;
    }

    .notif-message {
      font-size: 12px;
      color: #64748b;
      line-height: 1.4;
      margin-bottom: 6px;
    }

    .notif-meta {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .notif-type-badge {
      font-size: 10px;
      padding: 2px 6px;
      border-radius: 4px;
      font-weight: 500;
    }
    .notif-type-badge.geofence, .notif-type-badge.geofence_event { background: #dbeafe; color: #2563eb; }
    .notif-type-badge.speed_alert { background: #fef3c7; color: #d97706; }
    .notif-type-badge.driving_behavior { background: #fee2e2; color: #dc2626; }
    .notif-type-badge.maintenance_due { background: #e0e7ff; color: #4f46e5; }

    .notif-priority {
      font-size: 10px;
      padding: 2px 6px;
      border-radius: 4px;
      font-weight: 600;
    }
    .notif-priority.high { background: #fef3c7; color: #d97706; }
    .notif-priority.urgent { background: #fee2e2; color: #dc2626; }

    .notif-time {
      font-size: 10px;
      color: #94a3b8;
    }

    .notif-datetime {
      font-size: 10px;
      color: #94a3b8;
      font-style: italic;
    }

    .notif-actions {
      display: flex;
      gap: 4px;
      flex-shrink: 0;
    }

    .notif-action-btn {
      width: 28px;
      height: 28px;
      border: 1px solid #e2e8f0;
      border-radius: 6px;
      background: white;
      color: #94a3b8;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
    }
    .notif-action-btn:hover { color: #3b82f6; border-color: #93c5fd; }
    .notif-action-btn.danger:hover { color: #dc2626; border-color: #fca5a5; }

    .empty-state {
      text-align: center;
      padding: 60px 20px;
      color: #94a3b8;
    }
    .empty-state svg { margin-bottom: 12px; opacity: 0.4; }
    .empty-state p { margin: 0 0 4px; font-weight: 500; font-size: 14px; color: #64748b; }
    .empty-state span { font-size: 12px; }

    .loading-state {
      text-align: center;
      padding: 40px;
      color: #94a3b8;
      font-size: 12px;
    }

    .spinner {
      width: 24px;
      height: 24px;
      border: 3px solid #e2e8f0;
      border-top-color: #3b82f6;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
      margin: 0 auto 10px;
    }
    @keyframes spin { to { transform: rotate(360deg); } }

    .load-more {
      text-align: center;
      padding: 16px;
    }

    .btn-load-more {
      padding: 8px 20px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 6px;
      font-size: 12px;
      color: #3b82f6;
      cursor: pointer;
      font-weight: 500;
    }
    .btn-load-more:hover { background: #eff6ff; }

  `]
})
export class NotificationsComponent implements OnInit, OnDestroy {
  notifications: Notification[] = [];
  unreadCount = 0;
  loading = false;
  hasMore = false;
  currentPage = 1;
  pageSize = 20;
  filterType = '';
  unreadOnly = false;

  private subs: Subscription[] = [];

  constructor(
    private router: Router,
    private apiService: ApiService,
    private notificationService: NotificationService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit() {
    if (!this.apiService.isAuthenticated()) {
      this.router.navigate(['/login']);
      return;
    }
    this.loadNotifications();

    // Subscribe to real-time updates
    this.subs.push(
      this.notificationService.unreadCount$.subscribe(c => {
        this.unreadCount = c;
        this.cdr.detectChanges();
      })
    );
    this.subs.push(
      this.notificationService.newNotification$.subscribe(() => {
        this.loadNotifications();
      })
    );
  }

  ngOnDestroy() {
    this.subs.forEach(s => s.unsubscribe());
  }

  loadNotifications() {
    this.loading = true;
    this.currentPage = 1;
    this.notificationService.getNotifications(this.currentPage, this.pageSize, this.unreadOnly, this.filterType || undefined).subscribe({
      next: (page) => {
        this.notifications = page.items;
        this.unreadCount = page.unreadCount;
        this.hasMore = page.items.length >= this.pageSize && page.totalCount > this.pageSize;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.notifications = this.notificationService.notifications$.value;
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  loadMore() {
    this.currentPage++;
    this.notificationService.getNotifications(this.currentPage, this.pageSize, this.unreadOnly, this.filterType || undefined).subscribe({
      next: (page) => {
        this.notifications = [...this.notifications, ...page.items];
        this.hasMore = page.items.length >= this.pageSize;
        this.cdr.detectChanges();
      }
    });
  }

  markRead(notif: Notification) {
    notif.isRead = true;
    this.notificationService.markAsReadLocal(notif.id);
  }

  markAllRead() {
    this.notifications.forEach(n => n.isRead = true);
    this.unreadCount = 0;
    this.notificationService.markAllAsReadLocal();
  }

  deleteNotif(notif: Notification) {
    this.notifications = this.notifications.filter(n => n.id !== notif.id);
    this.notificationService.delete(notif.id).subscribe();
  }

  openNotification(notif: Notification) {
    if (!notif.isRead) this.markRead(notif);
    if (notif.actionUrl) this.router.navigateByUrl(notif.actionUrl);
  }

  getNotifIconClass(type: string): string {
    return type || 'admin_action';
  }

  getTypeLabel(type: string): string {
    const labels: Record<string, string> = {
      geofence: 'Géofence',
      geofence_event: 'Géofence',
      speed_alert: 'Vitesse',
      driving_behavior: 'Conduite',
      maintenance_due: 'Maintenance',
      admin_action: 'Système'
    };
    return labels[type] || type;
  }

  formatTime(dateStr: string): string {
    if (!dateStr) return '';
    const date = new Date(dateStr);
    const now = new Date();
    const diff = now.getTime() - date.getTime();
    const minutes = Math.floor(diff / 60000);
    if (minutes < 1) return "À l'instant";
    if (minutes < 60) return `Il y a ${minutes} min`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `Il y a ${hours}h`;
    const days = Math.floor(hours / 24);
    if (days < 7) return `Il y a ${days}j`;
    return date.toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit' });
  }

  formatDateTime(dateStr: string): string {
    if (!dateStr) return '';
    const date = new Date(dateStr);
    return date.toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric' }) + ' ' + date.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
  }
}

