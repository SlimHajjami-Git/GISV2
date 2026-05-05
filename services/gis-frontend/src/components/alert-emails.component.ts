import { Component, OnInit, OnDestroy, Input } from '@angular/core';
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
    <ng-container *ngIf="embedded; else fullPage">
      <ng-container *ngTemplateOutlet="content"></ng-container>
    </ng-container>
    <ng-template #fullPage>
      <app-layout>
        <ng-container *ngTemplateOutlet="content"></ng-container>
      </app-layout>
    </ng-template>
    <ng-template #content>
      <div class="ae-page" [class.ae-embedded]="embedded">
        <!-- Header -->
        <div class="ae-header">
          <div class="ae-header-left">
            <div class="ae-header-icon">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/>
                <polyline points="22,6 12,13 2,6"/>
              </svg>
            </div>
            <div>
              <h1 class="ae-title">Alertes par email</h1>
              <p class="ae-subtitle">Adresses qui reçoivent une copie email des alertes de la flotte</p>
            </div>
          </div>
          <button class="ae-btn-primary" (click)="toggleAddForm()" *ngIf="!showAddForm">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
              <line x1="12" y1="5" x2="12" y2="19"/>
              <line x1="5" y1="12" x2="19" y2="12"/>
            </svg>
            Ajouter une adresse
          </button>
        </div>

        <!-- Info banner (shown when empty) -->
        <div class="ae-info" *ngIf="!loading && alertEmails.length === 0 && !showAddForm">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <circle cx="12" cy="12" r="10"/>
            <line x1="12" y1="16" x2="12" y2="12"/>
            <line x1="12" y1="8" x2="12.01" y2="8"/>
          </svg>
          <span>Aucune alerte email configurée. Par défaut, les alertes sont envoyées aux administrateurs de la société.</span>
        </div>

        <!-- Card with table -->
        <div class="ae-card">
          <!-- Add form (inline, sits at top of table card) -->
          <div class="ae-add-form" *ngIf="showAddForm">
            <div class="ae-form-grid">
              <div class="ae-field">
                <label>Adresse email</label>
                <input type="email" [(ngModel)]="newEmail.email" placeholder="alerte@entreprise.com">
              </div>
              <div class="ae-field">
                <label>Type d'alerte</label>
                <select [(ngModel)]="newEmail.alertType">
                  <option value="" disabled>Choisir un type…</option>
                  <option *ngFor="let type of alertTypeKeys" [value]="type">{{ alertTypeLabels[type] }}</option>
                </select>
              </div>
              <div class="ae-form-actions">
                <button class="ae-btn-primary"
                        (click)="createAlertEmail()"
                        [disabled]="!newEmail.email || !newEmail.alertType">
                  Enregistrer
                </button>
                <button class="ae-btn-ghost" (click)="cancelAdd()">Annuler</button>
              </div>
            </div>
          </div>

          <!-- Table -->
          <div class="ae-table-wrap">
            <table class="ae-table">
              <thead>
                <tr>
                  <th>Adresse email</th>
                  <th>Type d'alerte</th>
                  <th class="ae-actions-col">Actions</th>
                </tr>
              </thead>
              <tbody>
                <tr *ngIf="loading">
                  <td colspan="3" class="ae-empty-cell">
                    <div class="ae-spinner"></div>
                    <span>Chargement…</span>
                  </td>
                </tr>
                <tr *ngIf="!loading && alertEmails.length === 0 && !showAddForm">
                  <td colspan="3" class="ae-empty-cell">
                    <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                      <path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/>
                      <polyline points="22,6 12,13 2,6"/>
                    </svg>
                    <p class="ae-empty-title">Aucune adresse configurée</p>
                    <p class="ae-empty-hint">Cliquez sur « Ajouter une adresse » pour commencer.</p>
                  </td>
                </tr>
                <ng-container *ngFor="let item of alertEmails">
                  <tr *ngIf="editingId !== item.id" class="ae-row">
                    <td class="ae-email-cell">
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/>
                        <polyline points="22,6 12,13 2,6"/>
                      </svg>
                      {{ item.email }}
                    </td>
                    <td>
                      <span class="ae-badge" [ngClass]="'ae-badge-' + item.alertType">
                        {{ alertTypeLabels[item.alertType] || item.alertType }}
                      </span>
                    </td>
                    <td class="ae-actions">
                      <button class="ae-btn-icon" (click)="sendTest(item)"
                              [disabled]="testingId === item.id"
                              title="Envoyer un email de test">
                        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <line x1="22" y1="2" x2="11" y2="13"/>
                          <polygon points="22 2 15 22 11 13 2 9 22 2"/>
                        </svg>
                        {{ testingId === item.id ? 'Envoi…' : 'Tester' }}
                      </button>
                      <button class="ae-btn-icon" (click)="startEdit(item)" title="Modifier">
                        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                          <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                        </svg>
                        Modifier
                      </button>
                      <button class="ae-btn-icon ae-btn-danger" (click)="confirmDelete(item)" title="Supprimer">
                        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <polyline points="3 6 5 6 21 6"/>
                          <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                        </svg>
                      </button>
                    </td>
                  </tr>
                  <tr *ngIf="editingId === item.id" class="ae-row ae-row-editing">
                    <td>
                      <input type="email" class="ae-inline-input" [(ngModel)]="editEmail.email">
                    </td>
                    <td>
                      <select class="ae-inline-input" [(ngModel)]="editEmail.alertType">
                        <option *ngFor="let type of alertTypeKeys" [value]="type">{{ alertTypeLabels[type] }}</option>
                      </select>
                    </td>
                    <td class="ae-actions">
                      <button class="ae-btn-primary" (click)="saveEdit()"
                              [disabled]="!editEmail.email || !editEmail.alertType">Enregistrer</button>
                      <button class="ae-btn-ghost" (click)="cancelEdit()">Annuler</button>
                    </td>
                  </tr>
                </ng-container>
              </tbody>
            </table>
          </div>
        </div>

        <!-- Toast -->
        <div class="ae-toast" *ngIf="toast" [ngClass]="'ae-toast-' + toast.kind">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" *ngIf="toast.kind === 'success'">
            <polyline points="20 6 9 17 4 12"/>
          </svg>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" *ngIf="toast.kind === 'error'">
            <circle cx="12" cy="12" r="10"/>
            <line x1="15" y1="9" x2="9" y2="15"/>
            <line x1="9" y1="9" x2="15" y2="15"/>
          </svg>
          <span>{{ toast.message }}</span>
        </div>

        <!-- Delete dialog -->
        <div class="ae-overlay" *ngIf="deletingItem" (click)="cancelDelete()">
          <div class="ae-dialog" (click)="$event.stopPropagation()">
            <div class="ae-dialog-icon">
              <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="3 6 5 6 21 6"/>
                <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
              </svg>
            </div>
            <h3>Supprimer cette adresse ?</h3>
            <p><strong>{{ deletingItem.email }}</strong> ne recevra plus les alertes de type
              <strong>« {{ alertTypeLabels[deletingItem.alertType] || deletingItem.alertType }} »</strong>.</p>
            <div class="ae-dialog-actions">
              <button class="ae-btn-ghost" (click)="cancelDelete()">Annuler</button>
              <button class="ae-btn-danger-solid" (click)="executeDelete()">Supprimer</button>
            </div>
          </div>
        </div>
      </div>
    </ng-template>
  `,
  styles: [`
    /* Calypso 7 — refonte complète : thème clair, palette Calypso bleue,
       cards blanches avec bordures fines, pour matcher le reste de l'app
       (geofences, settings, vehicles…). L'ancienne version utilisait des
       transparences sombres et était illisible sur le layout clair. */
    :host { display: block; background: #f1f5f9; min-height: calc(100vh - 42px); }
    /* En mode embedded, le composant est dans une page hôte (UserManagement)
       qui gère déjà son propre fond et ses paddings — on neutralise les
       wrappers locaux pour ne pas dupliquer ces marges. */
    :host-context(.ae-as-tab) { background: transparent; min-height: 0; }

    .ae-page {
      padding: 24px;
      max-width: 1100px;
      margin: 0 auto;
    }
    .ae-page.ae-embedded {
      padding: 0;
      max-width: none;
      background: transparent;
    }

    /* Header */
    .ae-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 16px;
      gap: 16px;
      flex-wrap: wrap;
    }
    .ae-header-left {
      display: flex;
      align-items: center;
      gap: 14px;
    }
    .ae-header-icon {
      width: 40px; height: 40px;
      border-radius: 10px;
      background: linear-gradient(135deg, #3b82f6, #2563eb);
      color: #fff;
      display: flex; align-items: center; justify-content: center;
      box-shadow: 0 4px 12px rgba(37, 99, 235, 0.25);
    }
    .ae-title {
      font-size: 18px;
      font-weight: 700;
      color: #1e293b;
      margin: 0;
      line-height: 1.2;
    }
    .ae-subtitle {
      font-size: 12px;
      color: #64748b;
      margin: 2px 0 0;
    }

    /* Buttons */
    .ae-btn-primary,
    .ae-btn-ghost,
    .ae-btn-icon,
    .ae-btn-danger-solid {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 8px 14px;
      border: 1px solid transparent;
      border-radius: 8px;
      font-size: 12px;
      font-weight: 600;
      cursor: pointer;
      transition: background 0.15s, border-color 0.15s, transform 0.05s;
      white-space: nowrap;
    }
    .ae-btn-primary {
      background: #2563eb;
      color: #fff;
      box-shadow: 0 1px 2px rgba(37, 99, 235, 0.25);
    }
    .ae-btn-primary:hover { background: #1d4ed8; }
    .ae-btn-primary:disabled {
      background: #cbd5e1;
      color: #fff;
      cursor: not-allowed;
      box-shadow: none;
    }
    .ae-btn-ghost {
      background: white;
      color: #475569;
      border-color: #e2e8f0;
    }
    .ae-btn-ghost:hover { background: #f8fafc; border-color: #cbd5e1; }

    .ae-btn-icon {
      background: white;
      color: #475569;
      border-color: #e2e8f0;
      padding: 6px 10px;
    }
    .ae-btn-icon:hover { background: #f1f5f9; color: #1e293b; }
    .ae-btn-icon:disabled { color: #cbd5e1; cursor: not-allowed; }
    .ae-btn-icon.ae-btn-danger { color: #dc2626; }
    .ae-btn-icon.ae-btn-danger:hover { background: #fef2f2; border-color: #fecaca; }

    .ae-btn-danger-solid {
      background: #dc2626;
      color: #fff;
    }
    .ae-btn-danger-solid:hover { background: #b91c1c; }

    /* Info banner */
    .ae-info {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 12px 16px;
      background: #eff6ff;
      border: 1px solid #bfdbfe;
      border-radius: 8px;
      color: #1e40af;
      font-size: 13px;
      margin-bottom: 16px;
    }
    .ae-info svg { flex-shrink: 0; color: #2563eb; }

    /* Card */
    .ae-card {
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 12px;
      overflow: hidden;
      box-shadow: 0 1px 2px rgba(0,0,0,0.03);
    }

    /* Add form (inline at top of card) */
    .ae-add-form {
      padding: 16px 20px;
      background: #f8fafc;
      border-bottom: 1px solid #e2e8f0;
    }
    .ae-form-grid {
      display: grid;
      grid-template-columns: 1fr 1fr auto;
      gap: 14px;
      align-items: end;
    }
    .ae-field { display: flex; flex-direction: column; gap: 6px; }
    .ae-field label {
      font-size: 11px;
      font-weight: 600;
      color: #475569;
      text-transform: uppercase;
      letter-spacing: 0.3px;
    }
    .ae-field input,
    .ae-field select {
      padding: 9px 12px;
      border: 1px solid #cbd5e1;
      border-radius: 8px;
      font-size: 13px;
      color: #1e293b;
      background: white;
      outline: none;
      transition: border-color 0.15s, box-shadow 0.15s;
    }
    .ae-field input:focus,
    .ae-field select:focus {
      border-color: #2563eb;
      box-shadow: 0 0 0 3px rgba(37, 99, 235, 0.12);
    }
    .ae-form-actions { display: flex; gap: 8px; }

    /* Table */
    .ae-table-wrap { overflow-x: auto; }
    .ae-table {
      width: 100%;
      border-collapse: collapse;
    }
    .ae-table thead th {
      padding: 12px 20px;
      font-size: 11px;
      font-weight: 600;
      color: #64748b;
      text-align: left;
      text-transform: uppercase;
      letter-spacing: 0.5px;
      background: #f8fafc;
      border-bottom: 1px solid #e2e8f0;
    }
    .ae-actions-col { width: 280px; }
    .ae-table tbody td {
      padding: 14px 20px;
      font-size: 13px;
      color: #1e293b;
      border-bottom: 1px solid #f1f5f9;
    }
    .ae-table tbody tr:last-child td { border-bottom: none; }

    .ae-row { transition: background 0.12s; }
    .ae-row:hover { background: #f8fafc; }
    .ae-row-editing { background: #eff6ff; }

    .ae-email-cell {
      display: flex;
      align-items: center;
      gap: 8px;
      font-weight: 500;
    }
    .ae-email-cell svg { color: #94a3b8; flex-shrink: 0; }

    .ae-actions {
      display: flex;
      gap: 6px;
      flex-wrap: nowrap;
    }

    .ae-inline-input {
      width: 100%;
      padding: 7px 10px;
      border: 1px solid #cbd5e1;
      border-radius: 6px;
      font-size: 13px;
      color: #1e293b;
      background: white;
      outline: none;
    }
    .ae-inline-input:focus {
      border-color: #2563eb;
      box-shadow: 0 0 0 2px rgba(37, 99, 235, 0.12);
    }

    /* Empty state cell */
    .ae-empty-cell {
      text-align: center !important;
      padding: 56px 16px !important;
      color: #94a3b8;
    }
    .ae-empty-cell svg {
      color: #cbd5e1;
      margin: 0 auto 12px;
      display: block;
    }
    .ae-empty-title {
      font-size: 14px;
      font-weight: 600;
      color: #475569;
      margin: 0 0 4px;
    }
    .ae-empty-hint {
      font-size: 12px;
      color: #94a3b8;
      margin: 0;
    }

    .ae-spinner {
      width: 22px; height: 22px;
      border: 2px solid #e2e8f0;
      border-top-color: #2563eb;
      border-radius: 50%;
      animation: ae-spin 0.7s linear infinite;
      margin: 0 auto 8px;
    }
    @keyframes ae-spin { to { transform: rotate(360deg); } }

    /* Type badges */
    .ae-badge {
      display: inline-flex;
      align-items: center;
      gap: 5px;
      padding: 3px 10px;
      border-radius: 12px;
      font-size: 11px;
      font-weight: 600;
      letter-spacing: 0.2px;
      border: 1px solid transparent;
    }
    .ae-badge::before {
      content: '';
      display: inline-block;
      width: 6px; height: 6px;
      border-radius: 50%;
      background: currentColor;
    }
    .ae-badge-assurance       { background: #eff6ff; color: #1d4ed8; border-color: #bfdbfe; }
    .ae-badge-taxe_circulation{ background: #fef3c7; color: #b45309; border-color: #fde68a; }
    .ae-badge-visite_technique{ background: #ecfdf5; color: #047857; border-color: #a7f3d0; }
    .ae-badge-entretien       { background: #fef2f2; color: #b91c1c; border-color: #fecaca; }
    .ae-badge-accident        { background: #fdf2f8; color: #be185d; border-color: #fbcfe8; }

    /* Delete dialog */
    .ae-overlay {
      position: fixed; inset: 0;
      background: rgba(15, 23, 42, 0.55);
      display: flex; align-items: center; justify-content: center;
      z-index: 1000;
      backdrop-filter: blur(2px);
    }
    .ae-dialog {
      background: white;
      border-radius: 12px;
      padding: 24px;
      max-width: 420px;
      width: 90%;
      box-shadow: 0 20px 50px rgba(0,0,0,0.25);
      text-align: center;
    }
    .ae-dialog-icon {
      width: 48px; height: 48px;
      border-radius: 50%;
      background: #fef2f2;
      color: #dc2626;
      display: flex; align-items: center; justify-content: center;
      margin: 0 auto 14px;
    }
    .ae-dialog h3 {
      font-size: 16px;
      font-weight: 700;
      color: #1e293b;
      margin: 0 0 8px;
    }
    .ae-dialog p {
      font-size: 13px;
      color: #475569;
      margin: 0 0 20px;
      line-height: 1.5;
    }
    .ae-dialog strong { color: #1e293b; }
    .ae-dialog-actions {
      display: flex;
      gap: 8px;
      justify-content: center;
    }

    /* Toast */
    .ae-toast {
      position: fixed;
      bottom: 24px; right: 24px;
      display: flex; align-items: center; gap: 10px;
      padding: 12px 16px;
      border-radius: 10px;
      font-size: 13px;
      font-weight: 500;
      box-shadow: 0 8px 24px rgba(0,0,0,0.15);
      z-index: 1100;
      animation: ae-toast-in 0.2s ease-out;
      max-width: 360px;
    }
    .ae-toast-success { background: #047857; color: #fff; }
    .ae-toast-error   { background: #dc2626; color: #fff; }
    @keyframes ae-toast-in {
      from { opacity: 0; transform: translateY(10px); }
      to   { opacity: 1; transform: translateY(0); }
    }

    /* Responsive */
    @media (max-width: 720px) {
      .ae-page { padding: 16px; }
      .ae-form-grid { grid-template-columns: 1fr; }
      .ae-actions { flex-wrap: wrap; }
    }
  `]
})
export class AlertEmailsComponent implements OnInit, OnDestroy {
  /**
   * Quand le composant est mounté comme onglet d'une page parente
   * (UserManagementComponent), embedded=true skippe le wrapper
   * <app-layout> (la page parente le fournit déjà) et retire le
   * padding/header bleu pour fondre l'UI dans son conteneur.
   */
  @Input() embedded = false;

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
