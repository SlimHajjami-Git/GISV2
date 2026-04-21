import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';
import { AppLayoutComponent } from './shared/app-layout.component';
import { ApiService } from '../services/api.service';

interface AlertEmail {
  id: number;
  email: string;
  alertType: string;
}

const ALERT_TYPES: Record<string, string> = {
  'assurance': 'Assurance',
  'taxe_circulation': 'Taxe Circulation',
  'visite_technique': 'Visite Technique',
  'entretien': 'Entretien',
  // Server-side accident detection (AccidentDetectionService, V7 MEMS filter)
  // routes its email fan-out through this alert_type.
  'accident': 'Accident'
};

@Component({
  selector: 'app-alert-emails',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent],
  template: `
    <app-layout>
      <div class="alert-emails-page">
        <div class="page-header">
          <h1 class="page-title">ALERTES PAR EMAILS</h1>
          <button class="btn btn-add" (click)="toggleAddForm()" *ngIf="!showAddForm">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"/>
              <line x1="5" y1="12" x2="19" y2="12"/>
            </svg>
            Ajouter
          </button>
        </div>

        <!-- Add Form -->
        <div class="add-form-card" *ngIf="showAddForm">
          <h3 class="form-title">Nouvelle alerte email</h3>
          <div class="form-row">
            <div class="form-group">
              <label class="form-label">Email</label>
              <input type="email" class="form-input" [(ngModel)]="newEmail.email" placeholder="exemple&#64;domaine.com">
            </div>
            <div class="form-group">
              <label class="form-label">Type</label>
              <select class="form-input" [(ngModel)]="newEmail.alertType">
                <option value="" disabled>Choisir un type</option>
                <option *ngFor="let type of alertTypeKeys" [value]="type">{{ alertTypeLabels[type] }}</option>
              </select>
            </div>
            <div class="form-actions">
              <button class="btn btn-confirm" (click)="createAlertEmail()" [disabled]="!newEmail.email || !newEmail.alertType">Confirmer</button>
              <button class="btn btn-cancel" (click)="cancelAdd()">Annuler</button>
            </div>
          </div>
        </div>

        <!-- Table -->
        <div class="table-card">
          <div class="table-wrapper">
            <table class="data-table">
              <thead>
                <tr>
                  <th>Email</th>
                  <th>Type</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                <tr *ngIf="loading">
                  <td colspan="3" class="loading-cell">Chargement...</td>
                </tr>
                <tr *ngIf="!loading && alertEmails.length === 0">
                  <td colspan="3" class="empty-cell">
                    Aucune alerte email configurée.
                    <br><span class="hint-text">Par défaut, les alertes seront envoyées à l'adresse de l'administrateur.</span>
                  </td>
                </tr>
                <ng-container *ngFor="let item of alertEmails">
                  <!-- View mode -->
                  <tr *ngIf="editingId !== item.id" class="data-row">
                    <td>{{ item.email }}</td>
                    <td>
                      <span class="type-badge" [ngClass]="'type-' + item.alertType">{{ alertTypeLabels[item.alertType] || item.alertType }}</span>
                    </td>
                    <td class="actions-cell">
                      <button class="btn btn-test" (click)="sendTest(item)" [disabled]="testingId === item.id" title="Envoyer un email de test à cette adresse">
                        {{ testingId === item.id ? 'Envoi...' : 'Tester' }}
                      </button>
                      <button class="btn btn-edit" (click)="startEdit(item)">Modifier</button>
                      <button class="btn btn-delete" (click)="confirmDelete(item)">Supprimer</button>
                    </td>
                  </tr>
                  <!-- Edit mode -->
                  <tr *ngIf="editingId === item.id" class="edit-row">
                    <td>
                      <input type="email" class="form-input inline-input" [(ngModel)]="editEmail.email">
                    </td>
                    <td>
                      <select class="form-input inline-input" [(ngModel)]="editEmail.alertType">
                        <option *ngFor="let type of alertTypeKeys" [value]="type">{{ alertTypeLabels[type] }}</option>
                      </select>
                    </td>
                    <td class="actions-cell">
                      <button class="btn btn-confirm" (click)="saveEdit()" [disabled]="!editEmail.email || !editEmail.alertType">Confirmer</button>
                      <button class="btn btn-cancel" (click)="cancelEdit()">Annuler</button>
                    </td>
                  </tr>
                </ng-container>
              </tbody>
            </table>
          </div>
        </div>

        <!-- Toast notification for test email result -->
        <div class="toast" *ngIf="toast" [ngClass]="'toast-' + toast.kind">
          <span>{{ toast.message }}</span>
        </div>

        <!-- Delete confirmation overlay -->
        <div class="delete-overlay" *ngIf="deletingItem" (click)="cancelDelete()">
          <div class="delete-dialog" (click)="$event.stopPropagation()">
            <h3>Confirmer la suppression</h3>
            <p>Voulez-vous vraiment supprimer l'alerte pour <strong>{{ deletingItem.email }}</strong> ?</p>
            <div class="dialog-actions">
              <button class="btn btn-delete" (click)="executeDelete()">Supprimer</button>
              <button class="btn btn-cancel" (click)="cancelDelete()">Annuler</button>
            </div>
          </div>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    .alert-emails-page {
      padding: 24px;
      max-width: 960px;
      margin: 0 auto;
    }

    .page-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 24px;
    }

    .page-title {
      font-size: 20px;
      font-weight: 700;
      color: #e2e8f0;
      letter-spacing: 0.5px;
      margin: 0;
    }

    /* Buttons */
    .btn {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 8px 16px;
      border: none;
      border-radius: 6px;
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
      transition: opacity 0.15s, transform 0.1s;
    }
    .btn:hover { opacity: 0.85; }
    .btn:active { transform: scale(0.97); }
    .btn:disabled { opacity: 0.4; cursor: not-allowed; }

    .btn-add {
      background: #6366f1;
      color: #fff;
    }
    .btn-confirm {
      background: #6366f1;
      color: #fff;
    }
    .btn-cancel {
      background: rgba(148, 163, 184, 0.15);
      color: #94a3b8;
    }
    .btn-edit {
      background: #14b8a6;
      color: #fff;
    }
    .btn-delete {
      background: #ef4444;
      color: #fff;
    }
    .btn-test {
      background: rgba(99, 102, 241, 0.18);
      color: #a5b4fc;
      border: 1px solid rgba(99, 102, 241, 0.35);
    }

    /* Add form card */
    .add-form-card {
      background: rgba(30, 41, 59, 0.8);
      border: 1px solid rgba(99, 102, 241, 0.2);
      border-radius: 10px;
      padding: 20px;
      margin-bottom: 24px;
    }

    .form-title {
      font-size: 15px;
      font-weight: 600;
      color: #e2e8f0;
      margin: 0 0 16px 0;
    }

    .form-row {
      display: flex;
      gap: 16px;
      align-items: flex-end;
      flex-wrap: wrap;
    }

    .form-group {
      flex: 1;
      min-width: 180px;
    }

    .form-label {
      display: block;
      font-size: 12px;
      font-weight: 500;
      color: #94a3b8;
      margin-bottom: 6px;
    }

    .form-input {
      width: 100%;
      padding: 9px 12px;
      background: rgba(15, 23, 42, 0.6);
      border: 1px solid rgba(148, 163, 184, 0.15);
      border-radius: 6px;
      color: #e2e8f0;
      font-size: 13px;
      outline: none;
      transition: border-color 0.15s;
      box-sizing: border-box;
    }
    .form-input:focus {
      border-color: #6366f1;
    }
    .form-input option {
      background: #1e293b;
      color: #e2e8f0;
    }

    .form-actions {
      display: flex;
      gap: 8px;
      align-items: flex-end;
      padding-bottom: 1px;
    }

    /* Table card */
    .table-card {
      background: rgba(30, 41, 59, 0.8);
      border: 1px solid rgba(148, 163, 184, 0.08);
      border-radius: 10px;
      overflow: hidden;
    }

    .table-wrapper {
      overflow-x: auto;
    }

    .data-table {
      width: 100%;
      border-collapse: collapse;
    }

    .data-table thead {
      background: #1e293b;
    }

    .data-table th {
      padding: 12px 16px;
      font-size: 11px;
      font-weight: 600;
      color: #94a3b8;
      text-align: left;
      text-transform: uppercase;
      letter-spacing: 0.5px;
      border-bottom: 1px solid rgba(148, 163, 184, 0.08);
    }

    .data-table td {
      padding: 12px 16px;
      font-size: 13px;
      color: #e2e8f0;
      border-bottom: 1px solid rgba(148, 163, 184, 0.05);
    }

    .data-row:nth-child(even) {
      background: rgba(15, 23, 42, 0.3);
    }

    .data-row:hover {
      background: rgba(99, 102, 241, 0.06);
    }

    .edit-row {
      background: rgba(99, 102, 241, 0.08);
    }

    .inline-input {
      width: 100%;
      min-width: 120px;
    }

    .loading-cell,
    .empty-cell {
      text-align: center;
      padding: 32px 16px !important;
      color: #64748b;
      font-size: 13px;
    }

    .actions-cell {
      display: flex;
      gap: 8px;
      white-space: nowrap;
    }

    /* Type badges */
    .type-badge {
      display: inline-block;
      padding: 3px 10px;
      border-radius: 4px;
      font-size: 12px;
      font-weight: 500;
      background: rgba(99, 102, 241, 0.15);
      color: #a5b4fc;
    }
    .type-assurance { background: rgba(99, 102, 241, 0.15); color: #a5b4fc; }
    .type-taxe_circulation { background: rgba(234, 179, 8, 0.15); color: #fbbf24; }
    .type-visite_technique { background: rgba(20, 184, 166, 0.15); color: #5eead4; }
    .type-entretien { background: rgba(239, 68, 68, 0.15); color: #fca5a5; }
    .type-accident { background: rgba(190, 18, 60, 0.18); color: #fda4af; }

    /* Delete confirmation overlay */
    .delete-overlay {
      position: fixed;
      inset: 0;
      background: rgba(0, 0, 0, 0.6);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
    }

    .delete-dialog {
      background: #1e293b;
      border: 1px solid rgba(148, 163, 184, 0.12);
      border-radius: 10px;
      padding: 24px;
      max-width: 400px;
      width: 90%;
    }

    .delete-dialog h3 {
      font-size: 16px;
      font-weight: 600;
      color: #e2e8f0;
      margin: 0 0 12px 0;
    }

    .delete-dialog p {
      font-size: 13px;
      color: #94a3b8;
      margin: 0 0 20px 0;
      line-height: 1.5;
    }

    .delete-dialog strong {
      color: #e2e8f0;
    }

    .dialog-actions {
      display: flex;
      gap: 8px;
      justify-content: flex-end;
    }

    .hint-text {
      font-size: 12px;
      color: rgba(148,163,184,0.6);
      margin-top: 4px;
      display: inline-block;
    }

    /* Toast */
    .toast {
      position: fixed;
      bottom: 24px;
      right: 24px;
      padding: 12px 18px;
      border-radius: 8px;
      font-size: 13px;
      font-weight: 500;
      box-shadow: 0 8px 24px rgba(0, 0, 0, 0.35);
      z-index: 1100;
      animation: toast-in 0.2s ease-out;
    }
    .toast-success {
      background: rgba(20, 184, 166, 0.95);
      color: #fff;
    }
    .toast-error {
      background: rgba(239, 68, 68, 0.95);
      color: #fff;
    }
    @keyframes toast-in {
      from { opacity: 0; transform: translateY(10px); }
      to { opacity: 1; transform: translateY(0); }
    }
  `]
})
export class AlertEmailsComponent implements OnInit, OnDestroy {
  alertEmails: AlertEmail[] = [];
  loading = false;

  showAddForm = false;
  newEmail: { email: string; alertType: string } = { email: '', alertType: '' };

  editingId: number | null = null;
  editEmail: { email: string; alertType: string } = { email: '', alertType: '' };

  deletingItem: AlertEmail | null = null;

  testingId: number | null = null;
  toast: { kind: 'success' | 'error'; message: string } | null = null;
  private toastTimer: any;

  alertTypeLabels = ALERT_TYPES;
  alertTypeKeys = Object.keys(ALERT_TYPES);

  private destroy$ = new Subject<void>();

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.loadAlertEmails();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  loadAlertEmails(): void {
    this.loading = true;
    this.api.getAlertEmails().pipe(takeUntil(this.destroy$)).subscribe({
      next: (data) => {
        this.alertEmails = data;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
      }
    });
  }

  // --- Add ---
  toggleAddForm(): void {
    this.showAddForm = true;
    // Pre-fill with admin email if no alert emails configured yet
    const adminEmail = this.api.getCurrentUserSync()?.email || '';
    this.newEmail = { email: this.alertEmails.length === 0 ? adminEmail : '', alertType: '' };
  }

  cancelAdd(): void {
    this.showAddForm = false;
    this.newEmail = { email: '', alertType: '' };
  }

  createAlertEmail(): void {
    if (!this.newEmail.email || !this.newEmail.alertType) return;
    this.api.createAlertEmail(this.newEmail).pipe(takeUntil(this.destroy$)).subscribe({
      next: () => {
        this.showAddForm = false;
        this.newEmail = { email: '', alertType: '' };
        this.loadAlertEmails();
      }
    });
  }

  // --- Edit ---
  startEdit(item: AlertEmail): void {
    this.editingId = item.id;
    this.editEmail = { email: item.email, alertType: item.alertType };
  }

  cancelEdit(): void {
    this.editingId = null;
    this.editEmail = { email: '', alertType: '' };
  }

  saveEdit(): void {
    if (this.editingId === null || !this.editEmail.email || !this.editEmail.alertType) return;
    this.api.updateAlertEmail(this.editingId, this.editEmail).pipe(takeUntil(this.destroy$)).subscribe({
      next: () => {
        this.editingId = null;
        this.editEmail = { email: '', alertType: '' };
        this.loadAlertEmails();
      }
    });
  }

  // --- Delete ---
  confirmDelete(item: AlertEmail): void {
    this.deletingItem = item;
  }

  cancelDelete(): void {
    this.deletingItem = null;
  }

  executeDelete(): void {
    if (!this.deletingItem) return;
    this.api.deleteAlertEmail(this.deletingItem.id).pipe(takeUntil(this.destroy$)).subscribe({
      next: () => {
        this.deletingItem = null;
        this.loadAlertEmails();
      }
    });
  }

  // --- Send test email ---
  sendTest(item: AlertEmail): void {
    if (this.testingId !== null) return;
    this.testingId = item.id;
    this.api.testAlertEmail(item.id).pipe(takeUntil(this.destroy$)).subscribe({
      next: () => {
        this.testingId = null;
        this.showToast('success', `Email de test envoyé à ${item.email}`);
      },
      error: () => {
        this.testingId = null;
        this.showToast('error', `Échec de l'envoi à ${item.email}`);
      }
    });
  }

  private showToast(kind: 'success' | 'error', message: string): void {
    this.toast = { kind, message };
    if (this.toastTimer) clearTimeout(this.toastTimer);
    this.toastTimer = setTimeout(() => { this.toast = null; }, 4000);
  }
}
