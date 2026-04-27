import { ChangeDetectionStrategy, ChangeDetectorRef, Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import {
  AccidentEventListItemDto,
  ApiService,
  CreateManualAccidentRequest,
} from '../services/api.service';
import { AppLayoutComponent } from './shared/app-layout.component';
import { Vehicle } from '../models/types';

/**
 * Calypso 6 (P9) — admin list of every `accident_events` row for the
 * current tenant, plus the "Ajouter un sinistre manuel" form to record an
 * accident the system did not auto-detect.
 *
 * Workflow:
 *   - The table groups rows by status (en attente, à compléter, confirmé,
 *     remorquage détecté). Each row links to /rapport-accident/:id where
 *     the admin can fill the damages form.
 *   - The "Ajouter un sinistre manuel" button opens an inline modal with
 *     vehicle picker + date + optional location + damages fields. On
 *     submit, the row is created with status='confirmed' and the user
 *     can immediately attach a PDF expert via the second step.
 */
@Component({
  selector: 'app-accident-reports-list',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-layout>
      <div class="page">
        <!-- Header -->
        <div class="page-head">
          <div>
            <h1>Rapports d'accident</h1>
            <p class="page-sub">Tous les accidents détectés automatiquement ou déclarés manuellement.</p>
          </div>
          <button class="btn-primary" (click)="openManualForm()">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
              <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
            </svg>
            Ajouter un sinistre manuel
          </button>
        </div>

        <!-- Status filter chips -->
        <div class="filter-row">
          <button class="chip" [class.active]="statusFilter === ''" (click)="setStatus('')">Tous</button>
          <button class="chip pending" [class.active]="statusFilter === 'pending'" (click)="setStatus('pending')">En attente</button>
          <button class="chip awaiting" [class.active]="statusFilter === 'awaiting_details'" (click)="setStatus('awaiting_details')">À compléter</button>
          <button class="chip confirmed" [class.active]="statusFilter === 'confirmed'" (click)="setStatus('confirmed')">Confirmé</button>
          <label class="chip-toggle">
            <input type="checkbox" [(ngModel)]="includeDismissed" (change)="reload()">
            <span>Inclure fausses alertes</span>
          </label>
        </div>

        <!-- Table -->
        <div class="table-wrap">
          <div *ngIf="loading" class="empty"><div class="spinner"></div></div>

          <table class="acc-table" *ngIf="!loading && items.length > 0">
            <thead>
              <tr>
                <th>Date</th>
                <th>Véhicule</th>
                <th>Localisation</th>
                <th>Statut</th>
                <th>Sévérité</th>
                <th>Coût estimé</th>
                <th>Décidé par</th>
                <th>Rapport</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let row of items" (click)="open(row)">
                <td>{{ row.incidentAt | date:'dd/MM/yyyy HH:mm' }}</td>
                <td><span class="vh-label">{{ row.vehicleLabel || ('Véhicule #' + row.vehicleId) }}</span></td>
                <td>{{ formatLocation(row) }}</td>
                <td><span class="status-tag" [class]="row.status">{{ statusLabel(row.status) }}</span></td>
                <td>{{ severityLabel(row.severity) }}</td>
                <td>{{ row.estimatedCost != null ? (row.estimatedCost | number:'1.2-2') + ' DT' : '—' }}</td>
                <td>{{ row.decidedByName || '—' }}</td>
                <td>
                  <a *ngIf="row.pdfReportUrl" [href]="row.pdfReportUrl" target="_blank" (click)="$event.stopPropagation()" class="pdf-link" title="Télécharger le rapport PDF">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
                    PDF
                  </a>
                  <span *ngIf="!row.pdfReportUrl" class="muted">—</span>
                </td>
                <td>
                  <button class="btn-row" (click)="open(row); $event.stopPropagation()">Ouvrir →</button>
                </td>
              </tr>
            </tbody>
          </table>

          <div *ngIf="!loading && items.length === 0" class="empty">
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/></svg>
            <h3>Aucun rapport d'accident</h3>
            <p>Les accidents détectés ou ajoutés manuellement apparaîtront ici.</p>
          </div>
        </div>

        <!-- Pagination -->
        <div class="pager" *ngIf="totalCount > pageSize">
          <button (click)="prev()" [disabled]="page <= 1">‹</button>
          <span>Page {{ page }} / {{ totalPages }}</span>
          <button (click)="next()" [disabled]="page >= totalPages">›</button>
        </div>
      </div>

      <!-- Manual creation modal -->
      <div class="overlay" *ngIf="manualOpen" (click)="closeManualForm()">
        <div class="modal" (click)="$event.stopPropagation()">
          <div class="modal-head">
            <h3>Ajouter un sinistre manuel</h3>
            <button class="modal-close" (click)="closeManualForm()">×</button>
          </div>
          <div class="modal-body">
            <p class="hint">Pour les accidents que le système n'a pas auto-détectés (panne GPS, hors couverture, etc.). Vous pourrez attacher le PDF de l'expert en assurance après création.</p>

            <div class="field">
              <label>Véhicule *</label>
              <select [(ngModel)]="manualForm.vehicleId">
                <option [ngValue]="null">Sélectionner...</option>
                <option *ngFor="let v of vehicles" [ngValue]="v.id">{{ v.name }}<span *ngIf="v.plate"> — {{ v.plate }}</span></option>
              </select>
            </div>

            <div class="field-row">
              <div class="field">
                <label>Date / heure *</label>
                <input type="datetime-local" [(ngModel)]="manualForm.incidentAt">
              </div>
              <div class="field">
                <label>Sévérité</label>
                <select [(ngModel)]="manualForm.severity">
                  <option [ngValue]="null">—</option>
                  <option value="minor">Légère</option>
                  <option value="moderate">Modérée</option>
                  <option value="severe">Grave</option>
                  <option value="total">Totale</option>
                </select>
              </div>
            </div>

            <div class="field-row">
              <div class="field">
                <label>Gouvernorat</label>
                <input type="text" [(ngModel)]="manualForm.locationGovernorate" placeholder="Tunis, Sfax, ...">
              </div>
              <div class="field">
                <label>Commune</label>
                <input type="text" [(ngModel)]="manualForm.locationCommune" placeholder="Le Bardo, ...">
              </div>
            </div>

            <div class="field">
              <label>Description</label>
              <textarea rows="3" [(ngModel)]="manualForm.description" placeholder="Décrivez les circonstances..."></textarea>
            </div>

            <div class="field-row">
              <div class="field">
                <label>Coût estimé (DT)</label>
                <input type="number" [(ngModel)]="manualForm.estimatedCost" step="0.01" placeholder="0.00">
              </div>
              <div class="field">
                <label>N° de sinistre (assurance)</label>
                <input type="text" [(ngModel)]="manualForm.claimNumber" placeholder="AAS-2026-0123">
              </div>
            </div>

            <div class="field">
              <label>PDF expert (facultatif)</label>
              <input type="file" accept="application/pdf" (change)="onFile($event)">
              <p class="hint" *ngIf="manualPdf">📎 {{ manualPdf.name }} ({{ formatBytes(manualPdf.size) }})</p>
            </div>

            <div class="modal-foot">
              <button class="btn-secondary" (click)="closeManualForm()" [disabled]="manualBusy">Annuler</button>
              <button class="btn-primary" (click)="submitManual()" [disabled]="manualBusy || !canSubmitManual()">
                <span *ngIf="!manualBusy">Créer le sinistre</span>
                <span *ngIf="manualBusy">Création…</span>
              </button>
            </div>
          </div>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    .page { flex:1; background:#f1f5f9; min-height:calc(100vh - 42px); padding:16px; }
    .page-head { display:flex; justify-content:space-between; align-items:center; margin-bottom:14px; gap:12px; flex-wrap:wrap; }
    .page-head h1 { margin:0 0 4px 0; font-size:20px; font-weight:700; color:#0f172a; }
    .page-sub { margin:0; font-size:12px; color:#64748b; }

    .btn-primary {
      display:inline-flex; align-items:center; gap:6px;
      padding:8px 14px; background:#dc2626; color:white; border:none;
      border-radius:6px; font-size:12px; font-weight:600; cursor:pointer;
    }
    .btn-primary:hover { background:#b91c1c; }
    .btn-primary:disabled { opacity:.6; cursor:not-allowed; }
    .btn-secondary {
      padding:8px 14px; background:white; color:#475569;
      border:1px solid #e2e8f0; border-radius:6px;
      font-size:12px; font-weight:600; cursor:pointer;
    }
    .btn-secondary:hover { background:#f8fafc; }

    .filter-row { display:flex; gap:8px; margin-bottom:12px; flex-wrap:wrap; align-items:center; }
    .chip {
      padding:6px 12px; background:white; border:1px solid #e2e8f0;
      border-radius:999px; font-size:11px; font-weight:600; color:#475569; cursor:pointer;
    }
    .chip:hover { background:#f8fafc; }
    .chip.active { background:#0f172a; color:white; border-color:#0f172a; }
    .chip.pending.active { background:#f59e0b; border-color:#f59e0b; }
    .chip.awaiting.active { background:#3b82f6; border-color:#3b82f6; }
    .chip.confirmed.active { background:#16a34a; border-color:#16a34a; }
    .chip-toggle { display:inline-flex; align-items:center; gap:6px; font-size:11px; color:#64748b; }

    .table-wrap { background:white; border:1px solid #e2e8f0; border-radius:8px; overflow:hidden; }
    .acc-table { width:100%; border-collapse:collapse; font-size:12px; }
    .acc-table th { text-align:left; padding:10px 12px; background:#f8fafc; font-weight:600; color:#475569; border-bottom:1px solid #e2e8f0; }
    .acc-table td { padding:10px 12px; border-bottom:1px solid #f1f5f9; color:#0f172a; }
    .acc-table tr { cursor:pointer; transition:background .15s; }
    .acc-table tr:hover { background:#f8fafc; }
    .vh-label { font-weight:600; color:#0f172a; }

    .status-tag { display:inline-block; padding:3px 9px; border-radius:999px; font-size:10px; font-weight:700; text-transform:uppercase; letter-spacing:.04em; }
    .status-tag.pending { background:#fef3c7; color:#b45309; }
    .status-tag.awaiting_details { background:#dbeafe; color:#1e40af; }
    .status-tag.confirmed { background:#dcfce7; color:#166534; }
    .status-tag.dismissed { background:#f1f5f9; color:#64748b; }

    .pdf-link {
      display:inline-flex; align-items:center; gap:4px;
      padding:4px 8px; background:#fee2e2; color:#dc2626;
      border-radius:4px; text-decoration:none; font-size:11px; font-weight:600;
    }
    .pdf-link:hover { background:#fecaca; }
    .muted { color:#94a3b8; }

    .btn-row {
      padding:5px 10px; background:transparent; border:1px solid #e2e8f0;
      border-radius:4px; font-size:11px; color:#475569; cursor:pointer;
    }
    .btn-row:hover { background:#f8fafc; border-color:#3b82f6; color:#3b82f6; }

    .empty { padding:60px 20px; text-align:center; color:#94a3b8; }
    .empty h3 { margin:12px 0 4px 0; color:#475569; font-size:14px; }
    .empty p { margin:0; font-size:12px; }
    .spinner { display:inline-block; width:24px; height:24px; border:3px solid #e2e8f0; border-top-color:#3b82f6; border-radius:50%; animation:spin 1s linear infinite; }
    @keyframes spin { to { transform:rotate(360deg); } }

    .pager { display:flex; align-items:center; gap:12px; justify-content:center; padding:14px; font-size:12px; color:#475569; }
    .pager button { padding:6px 12px; background:white; border:1px solid #e2e8f0; border-radius:6px; cursor:pointer; }
    .pager button:disabled { opacity:.4; cursor:not-allowed; }

    /* Manual creation modal */
    .overlay { position:fixed; inset:0; background:rgba(15,23,42,.55); display:flex; align-items:center; justify-content:center; z-index:1100; padding:20px; }
    .modal { width:100%; max-width:560px; max-height:90vh; background:white; border-radius:12px; box-shadow:0 20px 50px rgba(0,0,0,.25); display:flex; flex-direction:column; overflow:hidden; }
    .modal-head { padding:16px 20px; background:linear-gradient(135deg,#dc2626,#b91c1c); color:white; display:flex; justify-content:space-between; align-items:center; }
    .modal-head h3 { margin:0; font-size:15px; font-weight:700; }
    .modal-close { width:28px; height:28px; border-radius:8px; background:rgba(255,255,255,.2); border:none; color:white; cursor:pointer; font-size:18px; line-height:1; }
    .modal-body { padding:20px; overflow-y:auto; }
    .field { margin-bottom:12px; }
    .field-row { display:grid; grid-template-columns:1fr 1fr; gap:10px; margin-bottom:12px; }
    .field label { display:block; font-size:11px; font-weight:600; color:#475569; margin-bottom:4px; }
    .field input, .field select, .field textarea {
      width:100%; padding:7px 10px; font-size:12px;
      border:1px solid #e2e8f0; border-radius:6px; background:white; color:#0f172a;
      font-family: inherit;
    }
    .field input:focus, .field select:focus, .field textarea:focus { outline:none; border-color:#3b82f6; }
    .field textarea { resize:vertical; }
    .hint { font-size:11px; color:#94a3b8; margin:6px 0 0 0; }
    .modal-foot { display:flex; gap:8px; justify-content:flex-end; padding:14px 20px; border-top:1px solid #f1f5f9; background:#f8fafc; }
  `]
})
export class AccidentReportsListComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();

  loading = false;
  items: AccidentEventListItemDto[] = [];
  totalCount = 0;
  page = 1;
  pageSize = 25;
  statusFilter = '';
  includeDismissed = false;

  vehicles: Vehicle[] = [];
  manualOpen = false;
  manualBusy = false;
  manualForm: ManualForm = this.emptyManualForm();
  manualPdf: File | null = null;

  constructor(
    private api: ApiService,
    private router: Router,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.reload();
    this.api.getVehicles().pipe(takeUntil(this.destroy$)).subscribe({
      next: (vehicles) => { this.vehicles = vehicles as Vehicle[]; this.cdr.markForCheck(); },
      error: () => { /* non-blocking — manual form just shows empty list */ }
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  get totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount / this.pageSize));
  }

  reload(): void {
    this.loading = true;
    this.api.listAccidentEvents({
      page: this.page,
      pageSize: this.pageSize,
      status: this.statusFilter || undefined,
      includeDismissed: this.includeDismissed,
    }).pipe(takeUntil(this.destroy$)).subscribe({
      next: (result) => {
        this.items = result.items;
        this.totalCount = result.totalCount;
        this.loading = false;
        this.cdr.markForCheck();
      },
      error: () => {
        this.items = [];
        this.totalCount = 0;
        this.loading = false;
        this.cdr.markForCheck();
      }
    });
  }

  setStatus(status: string): void {
    if (this.statusFilter === status) return;
    this.statusFilter = status;
    this.page = 1;
    this.reload();
  }

  prev(): void { if (this.page > 1) { this.page--; this.reload(); } }
  next(): void { if (this.page < this.totalPages) { this.page++; this.reload(); } }

  open(row: AccidentEventListItemDto): void {
    this.router.navigate(['/rapport-accident', row.id]);
  }

  formatLocation(row: AccidentEventListItemDto): string {
    return [row.locationCommune, row.locationGovernorate].filter(Boolean).join(', ') || '—';
  }

  statusLabel(s: string): string {
    return ({
      pending: 'En attente',
      awaiting_details: 'À compléter',
      confirmed: 'Confirmé',
      dismissed: 'Fausse alerte',
    } as Record<string, string>)[s] || s;
  }

  severityLabel(s: string | null): string {
    if (!s) return '—';
    return ({
      minor: 'Légère',
      moderate: 'Modérée',
      severe: 'Grave',
      total: 'Totale',
    } as Record<string, string>)[s] || s;
  }

  // Manual creation form ---------------------------------------------------

  openManualForm(): void {
    this.manualForm = this.emptyManualForm();
    this.manualPdf = null;
    this.manualOpen = true;
  }

  closeManualForm(): void {
    if (this.manualBusy) return;
    this.manualOpen = false;
  }

  onFile(event: any): void {
    const file: File | undefined = event?.target?.files?.[0];
    this.manualPdf = file ?? null;
  }

  formatBytes(n: number): string {
    if (n < 1024) return `${n} o`;
    if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} ko`;
    return `${(n / 1024 / 1024).toFixed(1)} Mo`;
  }

  canSubmitManual(): boolean {
    return !!this.manualForm.vehicleId && !!this.manualForm.incidentAt;
  }

  submitManual(): void {
    if (!this.canSubmitManual() || this.manualBusy) return;
    this.manualBusy = true;

    // Convert datetime-local string ("YYYY-MM-DDTHH:mm") to ISO with Z so the
    // backend sees UTC. The user typed in their local tz, so we Date() it
    // and then toISOString().
    const incidentIso = new Date(this.manualForm.incidentAt!).toISOString();

    const payload: CreateManualAccidentRequest = {
      vehicleId: Number(this.manualForm.vehicleId),
      incidentAt: incidentIso,
      latitude: null,
      longitude: null,
      locationCommune: this.manualForm.locationCommune || null,
      locationGovernorate: this.manualForm.locationGovernorate || null,
      description: this.manualForm.description || null,
      severity: this.manualForm.severity || null,
      estimatedCost: this.manualForm.estimatedCost ?? null,
      claimNumber: this.manualForm.claimNumber || null,
      internalNotes: null,
    };

    this.api.createManualAccident(payload).pipe(takeUntil(this.destroy$)).subscribe({
      next: ({ accidentEventId }) => {
        // If the user attached a PDF, upload it before closing.
        if (this.manualPdf) {
          this.api.uploadAccidentReportPdf(accidentEventId, this.manualPdf).pipe(takeUntil(this.destroy$)).subscribe({
            next: () => this.afterManualCreated(),
            error: () => {
              alert('Sinistre créé, mais l\'envoi du PDF a échoué. Vous pouvez réessayer depuis la fiche.');
              this.afterManualCreated();
            }
          });
        } else {
          this.afterManualCreated();
        }
      },
      error: (err) => {
        const msg = err?.error?.message || 'Erreur lors de la création du sinistre.';
        alert(msg);
        this.manualBusy = false;
        this.cdr.markForCheck();
      }
    });
  }

  private afterManualCreated(): void {
    this.manualBusy = false;
    this.manualOpen = false;
    this.manualForm = this.emptyManualForm();
    this.manualPdf = null;
    this.reload();
  }

  private emptyManualForm(): ManualForm {
    return {
      vehicleId: null,
      incidentAt: this.todayLocal(),
      severity: null,
      locationGovernorate: '',
      locationCommune: '',
      description: '',
      estimatedCost: null,
      claimNumber: '',
    };
  }

  /** Today's date/time as an HTML datetime-local string (browser timezone). */
  private todayLocal(): string {
    const d = new Date();
    const pad = (n: number) => n.toString().padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
  }
}

interface ManualForm {
  vehicleId: number | string | null;
  incidentAt: string;
  severity: 'minor' | 'moderate' | 'severe' | 'total' | null;
  locationGovernorate: string;
  locationCommune: string;
  description: string;
  estimatedCost: number | null;
  claimNumber: string;
}
