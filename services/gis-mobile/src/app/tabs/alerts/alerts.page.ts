import { Component, OnInit, OnDestroy } from '@angular/core';
import { Subscription } from 'rxjs';
import { AlertController, ToastController } from '@ionic/angular';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { SignalRService, SignalRNotification } from '../../core/services/signalr.service';
import { Notification } from '../../core/models/types';

@Component({
  selector: 'app-alerts',
  standalone: false,
  template: `
    <ion-header>
      <ion-toolbar color="primary">
        <ion-title>Notifications</ion-title>
        <ion-buttons slot="end">
          <ion-button (click)="markAllRead()" *ngIf="unreadCount > 0">
            <ion-icon name="checkmark-done-outline" slot="icon-only"></ion-icon>
          </ion-button>
        </ion-buttons>
      </ion-toolbar>
      <ion-toolbar>
        <ion-segment [(ngModel)]="activeSegment" (ionChange)="segmentChanged()">
          <ion-segment-button value="all">
            <ion-label>Toutes</ion-label>
          </ion-segment-button>
          <ion-segment-button value="unread">
            <ion-label>Non lues ({{ unreadCount }})</ion-label>
          </ion-segment-button>
          <ion-segment-button value="alerts">
            <ion-label>Alertes</ion-label>
          </ion-segment-button>
        </ion-segment>
      </ion-toolbar>
    </ion-header>

    <ion-content [fullscreen]="true">
      <ion-refresher slot="fixed" (ionRefresh)="onRefresh($event)">
        <ion-refresher-content pullingText="Tirer pour rafraîchir"></ion-refresher-content>
      </ion-refresher>

      <!-- Real-time notifications banner -->
      <div class="realtime-banner" *ngIf="realtimeNotifications.length > 0" (click)="showRealtimeNotification()">
        <ion-icon name="notifications" color="warning"></ion-icon>
        <span>{{ realtimeNotifications.length }} nouvelle(s) notification(s)</span>
        <ion-icon name="chevron-forward"></ion-icon>
      </div>

      <ion-list>
        <ion-item-sliding *ngFor="let n of displayedNotifications; trackBy: trackById">
          <ion-item [class.unread]="!n.isRead" (click)="openNotification(n)">
            <div slot="start" class="notif-icon" [ngClass]="getNotifColorClass(n)">
              <ion-icon [name]="getNotifIcon(n)"></ion-icon>
            </div>
            <ion-label>
              <h2 class="notif-title">{{ n.title }}</h2>
              <p class="notif-message">{{ n.message }}</p>
              <p class="notif-time">
                <ion-icon name="time-outline"></ion-icon>
                {{ formatTime(n.createdAt) }}
              </p>
            </ion-label>
            <div slot="end" *ngIf="!n.isRead">
              <div class="unread-dot"></div>
            </div>
          </ion-item>
          <ion-item-options side="end">
            <ion-item-option color="primary" (click)="markRead(n)">
              <ion-icon name="checkmark" slot="icon-only"></ion-icon>
            </ion-item-option>
          </ion-item-options>
        </ion-item-sliding>
      </ion-list>

      <ion-infinite-scroll (ionInfinite)="loadMore($event)" [disabled]="!hasMore">
        <ion-infinite-scroll-content loadingSpinner="crescent"></ion-infinite-scroll-content>
      </ion-infinite-scroll>

      <div class="empty-state" *ngIf="displayedNotifications.length === 0 && !loading">
        <ion-icon name="notifications-off-outline"></ion-icon>
        <p>Aucune notification</p>
      </div>

      <ion-spinner *ngIf="loading" name="crescent" class="center-spinner"></ion-spinner>
    </ion-content>
  `,
  styles: [`
    .realtime-banner {
      display: flex; align-items: center; gap: 8px;
      padding: 10px 16px;
      background: rgba(245,158,11,0.12);
      border-bottom: 1px solid rgba(245,158,11,0.2);
      font-size: 13px; font-weight: 500;
      cursor: pointer;
    }
    .realtime-banner ion-icon:last-child { margin-left: auto; }
    ion-item.unread {
      --background: rgba(59,130,246,0.04);
    }
    .notif-icon {
      width: 40px; height: 40px; border-radius: 50%;
      display: flex; align-items: center; justify-content: center;
      flex-shrink: 0;
    }
    .notif-icon ion-icon { font-size: 18px; }
    .notif-icon.speed { background: rgba(239,68,68,0.12); color: #ef4444; }
    .notif-icon.geofence { background: rgba(59,130,246,0.12); color: #3b82f6; }
    .notif-icon.maintenance { background: rgba(245,158,11,0.12); color: #f59e0b; }
    .notif-icon.driving { background: rgba(168,85,247,0.12); color: #a855f7; }
    .notif-icon.immobilization { background: rgba(220,38,38,0.12); color: #dc2626; }
    .notif-icon.default { background: rgba(107,114,128,0.12); color: #6b7280; }
    .notif-title { font-weight: 600; font-size: 14px; margin-bottom: 2px; }
    .notif-message { font-size: 13px; color: var(--ion-color-medium); }
    .notif-time {
      font-size: 11px; color: var(--ion-color-medium);
      display: flex; align-items: center; gap: 4px; margin-top: 4px;
    }
    .notif-time ion-icon { font-size: 12px; }
    .unread-dot {
      width: 8px; height: 8px; border-radius: 50%;
      background: var(--ion-color-primary);
    }
    .empty-state { text-align: center; padding: 40px 20px; color: var(--ion-color-medium); }
    .empty-state ion-icon { font-size: 48px; display: block; margin: 0 auto 12px; }
    .center-spinner { display: block; margin: 40px auto; }
  `]
})
export class AlertsPage implements OnInit, OnDestroy {
  notifications: Notification[] = [];
  displayedNotifications: Notification[] = [];
  realtimeNotifications: SignalRNotification[] = [];
  activeSegment = 'all';
  unreadCount = 0;
  loading = true;
  page = 1;
  hasMore = true;

  private subs: Subscription[] = [];

  constructor(
    private api: ApiService,
    private signalr: SignalRService,
    private authService: AuthService,
    private alertCtrl: AlertController,
    private toastCtrl: ToastController
  ) {}

  ngOnInit() {
    this.loadNotifications();

    this.subs.push(
      this.signalr.notification$.subscribe(notif => {
        this.realtimeNotifications.unshift(notif);
        this.unreadCount++;
      })
    );

    this.subs.push(
      this.signalr.unreadCount$.subscribe(count => {
        this.unreadCount = count;
      })
    );

    // Also get initial unread count from API
    this.api.getUnreadCount().subscribe({
      next: (data) => this.unreadCount = data.count || 0
    });
  }

  ngOnDestroy() {
    this.subs.forEach(s => s.unsubscribe());
  }

  loadNotifications() {
    this.loading = true;
    this.api.getNotifications(this.page, 20).subscribe({
      next: (data) => {
        const items = data.items || data || [];
        this.notifications = [...this.notifications, ...items];
        this.segmentChanged();
        this.hasMore = items.length === 20;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
      }
    });
  }

  segmentChanged() {
    switch (this.activeSegment) {
      case 'unread':
        this.displayedNotifications = this.notifications.filter(n => !n.isRead);
        break;
      case 'alerts':
        this.displayedNotifications = this.notifications.filter(n =>
          n.type?.includes('speed') || n.type?.includes('geofence') || n.type?.includes('alert') || n.priority === 'high'
        );
        break;
      default:
        this.displayedNotifications = [...this.notifications];
    }
  }

  showRealtimeNotification() {
    // Merge real-time notifications into main list
    const mapped = this.realtimeNotifications.map(n => ({
      id: n.id,
      type: n.type,
      title: n.title,
      message: n.message,
      priority: n.priority,
      referenceType: n.referenceType,
      referenceId: n.referenceId,
      actionUrl: n.actionUrl,
      metadata: n.metadata,
      isRead: false,
      createdAt: n.createdAt
    }));
    this.notifications = [...mapped, ...this.notifications];
    this.realtimeNotifications = [];
    this.segmentChanged();
  }

  openNotification(n: Notification) {
    if (n.type === 'immobilization_request' && !n.isRead) {
      this.showImmobilizationDialog(n);
      return;
    }
    if (!n.isRead) {
      this.markRead(n);
    }
  }

  private async showImmobilizationDialog(n: Notification): Promise<void> {
    const user = this.authService.getCurrentUserSync();
    if (!user || parseInt(user.id) !== 1) {
      // Not the approver — just mark read
      this.markRead(n);
      return;
    }

    const meta = n.metadata || {};
    const commandType = meta.commandType || 'STOP';
    const vehicleName = meta.vehicleName || 'Véhicule';
    const requestedByName = meta.requestedByName || 'Un utilisateur';
    const isStop = commandType === 'STOP';

    const alert = await this.alertCtrl.create({
      header: isStop ? "DEMANDE D'ARRÊT" : 'DEMANDE DE LIBÉRATION',
      message: isStop
        ? `<strong>${requestedByName}</strong> demande l'<strong style="color:#dc2626">ARRÊT</strong> du véhicule <strong>"${vehicleName}"</strong>`
        : `<strong>${requestedByName}</strong> demande la <strong style="color:#16a34a">LIBÉRATION</strong> du véhicule <strong>"${vehicleName}"</strong>`,
      cssClass: 'immobilization-alert',
      backdropDismiss: false,
      buttons: [
        {
          text: 'Refuser',
          role: 'cancel',
          handler: () => {
            this.api.rejectImmobilization(n.id).subscribe({
              next: async () => {
                this.markRead(n);
                const toast = await this.toastCtrl.create({
                  message: `Demande refusée pour ${vehicleName}`,
                  duration: 3000, color: 'warning', position: 'top'
                });
                await toast.present();
              },
              error: async () => {
                const toast = await this.toastCtrl.create({
                  message: 'Erreur lors du refus', duration: 3000, color: 'danger', position: 'top'
                });
                await toast.present();
              }
            });
          }
        },
        {
          text: 'ACCEPTER',
          cssClass: 'alert-danger-btn',
          handler: () => {
            this.api.approveImmobilization(n.id).subscribe({
              next: async () => {
                this.markRead(n);
                const toast = await this.toastCtrl.create({
                  message: `Demande approuvée pour ${vehicleName}`,
                  duration: 3000, color: 'success', position: 'top'
                });
                await toast.present();
              },
              error: async (err) => {
                const msg = err?.error?.message || "Erreur lors de l'approbation";
                const toast = await this.toastCtrl.create({
                  message: msg, duration: 3000, color: 'danger', position: 'top'
                });
                await toast.present();
              }
            });
          }
        }
      ]
    });
    await alert.present();
  }

  markRead(n: Notification) {
    n.isRead = true;
    this.api.markNotificationRead(n.id).subscribe();
    this.unreadCount = Math.max(0, this.unreadCount - 1);
    this.segmentChanged();
  }

  markAllRead() {
    this.api.markAllNotificationsRead().subscribe();
    this.notifications.forEach(n => n.isRead = true);
    this.unreadCount = 0;
    this.segmentChanged();
  }

  loadMore(event: any) {
    this.page++;
    this.api.getNotifications(this.page, 20).subscribe({
      next: (data) => {
        const items = data.items || data || [];
        this.notifications = [...this.notifications, ...items];
        this.hasMore = items.length === 20;
        this.segmentChanged();
        event.target.complete();
      },
      error: () => event.target.complete()
    });
  }

  async onRefresh(event: any) {
    this.page = 1;
    this.notifications = [];
    this.loadNotifications();
    setTimeout(() => event.target.complete(), 1500);
  }

  getNotifIcon(n: Notification): string {
    const type = (n.type || '').toLowerCase();
    if (type.includes('immobilization')) return 'lock-closed';
    if (type.includes('speed')) return 'speedometer';
    if (type.includes('geofence')) return 'location';
    if (type.includes('maintenance')) return 'build';
    if (type.includes('driving') || type.includes('behavior')) return 'car-sport';
    if (type.includes('fuel')) return 'water';
    return 'notifications';
  }

  getNotifColorClass(n: Notification): string {
    const type = (n.type || '').toLowerCase();
    if (type.includes('immobilization')) return 'immobilization';
    if (type.includes('speed')) return 'speed';
    if (type.includes('geofence')) return 'geofence';
    if (type.includes('maintenance')) return 'maintenance';
    if (type.includes('driving') || type.includes('behavior')) return 'driving';
    return 'default';
  }

  formatTime(dateStr: string): string {
    if (!dateStr) return '';
    const date = new Date(dateStr);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffMins = Math.floor(diffMs / 60000);

    if (diffMins < 1) return "À l'instant";
    if (diffMins < 60) return `Il y a ${diffMins} min`;
    if (diffMins < 1440) return `Il y a ${Math.floor(diffMins / 60)}h`;
    return date.toLocaleDateString('fr-FR', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' });
  }

  trackById(_: number, n: Notification) {
    return n.id;
  }
}
