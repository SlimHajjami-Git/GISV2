import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { AdminLayoutComponent } from '../components/admin-layout.component';

interface NotifUser {
  id: number;
  fullName: string;
  email: string;
  companyId: number;
  companyName: string;
}

interface SendResult {
  success: boolean;
  notificationId: number;
  sentTo: string;
  sentToEmail: string;
}

@Component({
  selector: 'admin-notifications',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent],
  template: `
    <admin-layout pageTitle="Test Notifications">
      <div class="notif-page">
        <div class="page-header">
          <h2>Envoyer une notification de test</h2>
          <p class="subtitle">Vérifiez que le système de notifications fonctionne en envoyant une notification à un utilisateur.</p>
        </div>

        <div class="notif-form-card">
          <div class="form-grid">
            <!-- User selection -->
            <div class="form-group full-width">
              <label>Destinataire</label>
              <div class="search-wrapper">
                <input type="text"
                       [(ngModel)]="userSearch"
                       (input)="filterUsers()"
                       (focus)="showDropdown = true"
                       placeholder="Rechercher un utilisateur par nom ou email..."
                       class="search-input" />
                <div class="user-dropdown" *ngIf="showDropdown && filteredUsers.length > 0">
                  <div class="user-option" *ngFor="let u of filteredUsers"
                       (click)="selectUser(u)"
                       [class.selected]="selectedUser?.id === u.id">
                    <div class="user-info">
                      <span class="user-name">{{ u.fullName }}</span>
                      <span class="user-email">{{ u.email }}</span>
                    </div>
                    <span class="user-company">{{ u.companyName }}</span>
                  </div>
                </div>
              </div>
              <div class="selected-badge" *ngIf="selectedUser">
                <span class="badge-avatar">{{ selectedUser.fullName.charAt(0) }}</span>
                <span>{{ selectedUser.fullName }} — {{ selectedUser.email }}</span>
                <button class="badge-remove" (click)="clearUser()">×</button>
              </div>
            </div>

            <!-- Title -->
            <div class="form-group">
              <label>Titre</label>
              <input type="text" [(ngModel)]="form.title" placeholder="Titre de la notification" />
            </div>

            <!-- Type -->
            <div class="form-group">
              <label>Type</label>
              <select [(ngModel)]="form.type">
                <option value="info">Info</option>
                <option value="speed_alert">Alerte vitesse</option>
                <option value="geofence">Géofence</option>
                <option value="driving_behavior">Comportement conduite</option>
                <option value="admin_action">Action admin</option>
                <option value="maintenance_due">Maintenance</option>
              </select>
            </div>

            <!-- Message -->
            <div class="form-group full-width">
              <label>Message</label>
              <textarea [(ngModel)]="form.message" rows="3" placeholder="Message de la notification..."></textarea>
            </div>

            <!-- Priority -->
            <div class="form-group">
              <label>Priorité</label>
              <div class="priority-options">
                <label class="radio-option" *ngFor="let p of priorities">
                  <input type="radio" name="priority" [value]="p.value" [(ngModel)]="form.priority" />
                  <span class="radio-label" [class]="'priority-' + p.value">{{ p.label }}</span>
                </label>
              </div>
            </div>
          </div>

          <!-- Send button -->
          <div class="form-actions">
            <button class="send-btn" (click)="sendNotification()" [disabled]="!canSend || sending">
              <svg *ngIf="!sending" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="22" y1="2" x2="11" y2="13"/><polygon points="22 2 15 22 11 13 2 9 22 2"/>
              </svg>
              <span *ngIf="sending" class="spinner"></span>
              {{ sending ? 'Envoi...' : 'Envoyer la notification' }}
            </button>
          </div>
        </div>

        <!-- History -->
        <div class="history-card" *ngIf="sentHistory.length > 0">
          <h3>Historique des envois</h3>
          <div class="history-list">
            <div class="history-item" *ngFor="let h of sentHistory">
              <div class="history-icon success">✓</div>
              <div class="history-content">
                <span class="history-title">{{ h.title }}</span>
                <span class="history-detail">Envoyé à <strong>{{ h.sentTo }}</strong> ({{ h.sentToEmail }})</span>
              </div>
              <span class="history-time">{{ h.time }}</span>
            </div>
          </div>
        </div>
      </div>
    </admin-layout>
  `,
  styles: [`
    .notif-page {
      max-width: 800px;
    }

    .page-header {
      margin-bottom: 24px;
    }

    .page-header h2 {
      font-size: 20px;
      font-weight: 700;
      color: var(--adm-ink);
      margin: 0 0 6px;
    }

    .subtitle {
      color: var(--adm-sub);
      font-size: 14px;
      margin: 0;
    }

    .notif-form-card, .history-card {
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 16px;
      box-shadow: var(--adm-shadow);
      padding: 24px;
      margin-bottom: 20px;
      animation: rise 0.25s ease both;
    }

    @keyframes rise {
      from { opacity: 0; transform: translateY(8px); }
      to { opacity: 1; transform: none; }
    }

    @media (prefers-reduced-motion: reduce) {
      .notif-form-card, .history-card { animation: none; }
    }

    .form-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 16px;
    }

    .form-group.full-width { grid-column: 1 / -1; }

    .form-group label {
      display: block;
      font-size: 13px;
      font-weight: 600;
      color: var(--adm-ink);
      margin-bottom: 6px;
    }

    .form-group input[type="text"],
    .form-group textarea,
    .form-group select {
      width: 100%;
      padding: 10px 12px;
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      font-size: 14px;
      color: var(--adm-ink);
      background: var(--adm-card);
      transition: border-color 0.15s, box-shadow 0.15s;
      box-sizing: border-box;
    }

    .form-group input:focus,
    .form-group textarea:focus,
    .form-group select:focus {
      outline: none;
      border-color: var(--adm-indigo);
      box-shadow: 0 0 0 3px rgba(79, 70, 229, 0.12);
    }

    .search-wrapper { position: relative; }

    .search-input { padding-right: 36px !important; }

    .user-dropdown {
      position: absolute;
      top: 100%;
      left: 0;
      right: 0;
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 12px;
      box-shadow: var(--adm-shadow-hover);
      max-height: 240px;
      overflow-y: auto;
      z-index: 50;
      margin-top: 4px;
    }

    .user-option {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 10px 14px;
      cursor: pointer;
      transition: background 0.1s;
    }

    .user-option:hover { background: #f8fafc; }
    .user-option.selected { background: rgba(79, 70, 229, 0.08); }

    .user-info {
      display: flex;
      flex-direction: column;
    }

    .user-name { font-size: 13px; font-weight: 600; color: var(--adm-ink); }
    .user-email { font-size: 12px; color: var(--adm-sub); }
    .user-company { font-size: 11px; font-weight: 600; color: var(--adm-slate-ink); background: var(--adm-track); padding: 2px 8px; border-radius: 999px; }

    .selected-badge {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      background: rgba(79, 70, 229, 0.10);
      border: 1px solid rgba(79, 70, 229, 0.22);
      border-radius: 999px;
      padding: 6px 12px;
      margin-top: 8px;
      font-size: 13px;
      font-weight: 600;
      color: var(--adm-indigo-ink);
    }

    .badge-avatar {
      width: 24px;
      height: 24px;
      border-radius: 50%;
      background: var(--adm-indigo);
      color: #fff;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 12px;
      font-weight: 700;
    }

    .badge-remove {
      background: none;
      border: none;
      color: var(--adm-indigo);
      font-size: 18px;
      cursor: pointer;
      padding: 0 4px;
    }

    .priority-options {
      display: flex;
      gap: 12px;
    }

    .radio-option {
      display: flex;
      align-items: center;
      gap: 6px;
      cursor: pointer;
    }

    .radio-label {
      font-size: 11px;
      font-weight: 700;
      padding: 3px 10px;
      border-radius: 999px;
    }

    .priority-low { background: rgba(5, 150, 105, 0.10); color: var(--adm-green-ink); }
    .priority-normal { background: rgba(8, 145, 178, 0.10); color: var(--adm-cyan-ink); }
    .priority-high { background: rgba(220, 38, 38, 0.10); color: var(--adm-red-ink); }

    .form-actions {
      margin-top: 24px;
      display: flex;
      justify-content: flex-end;
    }

    .send-btn {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      padding: 9px 18px;
      background: var(--adm-indigo);
      color: #fff;
      border: none;
      border-radius: 10px;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      transition: background 0.15s;
    }

    .send-btn:hover:not(:disabled) { background: var(--adm-indigo-ink); }
    .send-btn:disabled { opacity: 0.5; cursor: not-allowed; }

    .spinner {
      width: 16px;
      height: 16px;
      border: 2px solid rgba(255,255,255,0.3);
      border-top-color: #fff;
      border-radius: 50%;
      animation: spin 0.6s linear infinite;
    }

    @keyframes spin { to { transform: rotate(360deg); } }

    .history-card h3 {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 12px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      color: var(--adm-sub);
      margin: 0 0 16px;
    }

    .history-card h3::before {
      content: '';
      width: 3px;
      height: 12px;
      border-radius: 2px;
      background: var(--adm-indigo);
    }

    .history-list {
      display: flex;
      flex-direction: column;
      gap: 10px;
    }

    .history-item {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 14px;
      background: #f8fafc;
      border: 1px solid #eef2f7;
      border-radius: 10px;
    }

    .history-icon {
      width: 32px;
      height: 32px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 14px;
      font-weight: bold;
      flex-shrink: 0;
    }

    .history-icon.success { background: rgba(5, 150, 105, 0.12); color: var(--adm-green-ink); }

    .history-content {
      flex: 1;
      display: flex;
      flex-direction: column;
    }

    .history-title { font-size: 13px; font-weight: 600; color: var(--adm-ink); }
    .history-detail { font-size: 12px; color: var(--adm-sub); }
    .history-time { font-size: 11px; color: var(--adm-sub); white-space: nowrap; font-variant-numeric: tabular-nums; }
  `]
})
export class AdminNotificationsComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  users: NotifUser[] = [];
  filteredUsers: NotifUser[] = [];
  selectedUser: NotifUser | null = null;
  userSearch = '';
  showDropdown = false;
  sending = false;

  form = {
    title: 'Notification de test',
    message: 'Ceci est une notification de test envoyée depuis le panneau admin.',
    type: 'info',
    priority: 'normal'
  };

  priorities = [
    { value: 'low', label: 'Basse' },
    { value: 'normal', label: 'Normale' },
    { value: 'high', label: 'Haute' }
  ];

  sentHistory: { title: string; sentTo: string; sentToEmail: string; time: string }[] = [];

  get canSend(): boolean {
    return !!this.selectedUser && !!this.form.title.trim() && !!this.form.message.trim();
  }

  constructor(private http: HttpClient) {}

  ngOnInit(): void {
    this.loadUsers();
    document.addEventListener('click', () => this.showDropdown = false);
  }

  loadUsers(): void {
    this.http.get<NotifUser[]>('/api/admin/notifications/users').pipe(takeUntil(this.destroy$)).subscribe({
      next: (users) => {
        this.users = users;
        this.filteredUsers = users.slice(0, 20);
      },
      error: (err) => console.error('Failed to load users:', err)
    });
  }

  filterUsers(): void {
    this.showDropdown = true;
    const q = this.userSearch.toLowerCase();
    this.filteredUsers = this.users
      .filter(u => u.fullName.toLowerCase().includes(q) || u.email.toLowerCase().includes(q) || u.companyName.toLowerCase().includes(q))
      .slice(0, 20);
  }

  selectUser(user: NotifUser): void {
    this.selectedUser = user;
    this.userSearch = '';
    this.showDropdown = false;
  }

  clearUser(): void {
    this.selectedUser = null;
  }

  sendNotification(): void {
    if (!this.canSend || this.sending) return;
    this.sending = true;

    const payload = {
      userId: this.selectedUser!.id,
      title: this.form.title,
      message: this.form.message,
      type: this.form.type,
      priority: this.form.priority
    };

    this.http.post<SendResult>('/api/admin/notifications/send', payload).pipe(takeUntil(this.destroy$)).subscribe({
      next: (result) => {
        this.sending = false;
        this.sentHistory.unshift({
          title: this.form.title,
          sentTo: result.sentTo,
          sentToEmail: result.sentToEmail,
          time: new Date().toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
        });
      },
      error: (err) => {
        this.sending = false;
        console.error('Failed to send notification:', err);
        alert('Erreur: ' + (err.error?.message || 'Impossible d\'envoyer la notification'));
      }
    });
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
