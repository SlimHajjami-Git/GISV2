import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';
import { ApiService } from '../../services/api.service';
import { AuthService } from '../../services/auth.service';
import { SignalRService, SignalRNotification } from '../../services/signalr.service';

/**
 * Pending accident shown in the modal.
 *
 * One instance per unhandled accident_detected SignalR event — if a second
 * accident fires while the first is on-screen, it is queued and shown as
 * soon as the current one is resolved (Confirmer / Fausse alerte / Reporter).
 */
interface PendingAccident {
  accidentEventId: number;
  vehicleLabel: string;
  incidentAt: string | null;
  locationGovernorate?: string | null;
  locationCommune?: string | null;
}

/**
 * Strictly-blocking accident decision modal.
 *
 * Mounted once at the root of <app-layout> so it's available on every
 * authenticated page. Listens to SignalRService.notification$ for
 * `type === 'accident_detected'` events that carry
 * `metadata.requiresDecision === 'true'` — and only company admins see it
 * at all (the backend already filters admins, this is belt-and-braces).
 *
 * Three exits:
 * - Confirmer l'accident → POST /confirm, dequeue, advance to next.
 * - Fausse alerte → POST /dismiss, dequeue, advance to next.
 * - Reporter → only hides the current one locally; the notification row
 *   stays pending on the backend so the admin can come back to it via the
 *   bell icon → /rapport-accident/:id fallback buttons.
 *
 * The modal cannot be dismissed by clicking the backdrop or pressing
 * Escape (no handler wired). That is deliberate — UX choice was "Bloquant
 * strict".
 */
@Component({
  selector: 'app-accident-decision-modal',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="adm-root" *ngIf="current">
      <div class="adm-backdrop"></div>
      <div class="adm-dialog" role="alertdialog" aria-modal="true" aria-labelledby="adm-title">
        <div class="adm-header">
          <div class="adm-icon">
            <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0Z"/>
              <line x1="12" y1="9" x2="12" y2="13"/>
              <line x1="12" y1="17" x2="12.01" y2="17"/>
            </svg>
          </div>
          <div class="adm-heading">
            <h2 id="adm-title">Accident détecté</h2>
            <p class="adm-sub">Votre décision est requise</p>
          </div>
        </div>

        <div class="adm-body">
          <p class="adm-lead">
            Un événement compatible avec un accident grave a été détecté sur un véhicule de votre société.
          </p>
          <dl class="adm-details">
            <div class="adm-row">
              <dt>Véhicule</dt>
              <dd>{{ current.vehicleLabel || 'Véhicule inconnu' }}</dd>
            </div>
            <div class="adm-row" *ngIf="formattedIncidentAt">
              <dt>Moment</dt>
              <dd>{{ formattedIncidentAt }}</dd>
            </div>
            <div class="adm-row" *ngIf="locationLabel">
              <dt>Lieu</dt>
              <dd>{{ locationLabel }}</dd>
            </div>
          </dl>
          <p class="adm-hint">
            Confirmez l'accident pour déclencher la surveillance du remorquage,
            ou signalez une fausse alerte si l'événement n'est pas un accident.
          </p>
          <p class="adm-error" *ngIf="errorMessage">{{ errorMessage }}</p>
        </div>

        <div class="adm-actions">
          <button type="button" class="adm-btn adm-btn-confirm" (click)="confirm()" [disabled]="busy">
            {{ busy === 'confirm' ? 'Confirmation…' : "Confirmer l'accident" }}
          </button>
          <button type="button" class="adm-btn adm-btn-dismiss" (click)="dismiss()" [disabled]="busy">
            {{ busy === 'dismiss' ? 'Envoi…' : 'Fausse alerte' }}
          </button>
          <button type="button" class="adm-btn adm-btn-postpone" (click)="postpone()" [disabled]="busy">
            Reporter
          </button>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .adm-root {
      position: fixed;
      inset: 0;
      z-index: 10000;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 24px;
    }

    .adm-backdrop {
      position: absolute;
      inset: 0;
      background: rgba(5, 10, 22, 0.82);
      backdrop-filter: blur(2px);
      /* NO click handler — this modal is strictly blocking. */
    }

    .adm-dialog {
      position: relative;
      width: min(520px, 100%);
      background: #ffffff;
      color: #0f172a;
      border-radius: 14px;
      box-shadow: 0 24px 60px rgba(0, 0, 0, 0.45);
      overflow: hidden;
      animation: adm-pop 180ms ease-out;
    }

    @keyframes adm-pop {
      from { transform: scale(0.94); opacity: 0; }
      to   { transform: scale(1);    opacity: 1; }
    }

    .adm-header {
      display: flex;
      align-items: center;
      gap: 14px;
      padding: 20px 24px;
      background: linear-gradient(90deg, #dc2626, #ef4444);
      color: #fff;
    }

    .adm-icon {
      width: 48px;
      height: 48px;
      border-radius: 12px;
      background: rgba(255, 255, 255, 0.18);
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .adm-heading h2 {
      margin: 0;
      font-size: 18px;
      font-weight: 700;
    }

    .adm-sub {
      margin: 2px 0 0;
      font-size: 13px;
      opacity: 0.9;
    }

    .adm-body {
      padding: 20px 24px 4px;
      font-size: 14px;
      line-height: 1.55;
    }

    .adm-lead {
      margin: 0 0 14px;
      color: #1f2937;
    }

    .adm-details {
      margin: 0 0 14px;
      padding: 12px 14px;
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      display: grid;
      gap: 6px;
    }

    .adm-row {
      display: grid;
      grid-template-columns: 110px 1fr;
      gap: 8px;
      font-size: 13px;
    }

    .adm-row dt {
      color: #64748b;
      font-weight: 500;
    }

    .adm-row dd {
      margin: 0;
      font-weight: 600;
      color: #0f172a;
    }

    .adm-hint {
      margin: 0 0 4px;
      color: #475569;
      font-size: 13px;
    }

    .adm-error {
      margin: 10px 0 0;
      padding: 8px 10px;
      background: #fef2f2;
      border: 1px solid #fecaca;
      color: #b91c1c;
      border-radius: 6px;
      font-size: 13px;
    }

    .adm-actions {
      display: flex;
      flex-direction: column;
      gap: 8px;
      padding: 16px 24px 22px;
    }

    .adm-btn {
      padding: 11px 14px;
      font-size: 14px;
      font-weight: 600;
      border: 1px solid transparent;
      border-radius: 8px;
      cursor: pointer;
      transition: background 120ms ease, transform 120ms ease;
    }

    .adm-btn:disabled {
      cursor: not-allowed;
      opacity: 0.65;
    }

    .adm-btn-confirm {
      background: #dc2626;
      color: #fff;
    }
    .adm-btn-confirm:hover:not(:disabled) {
      background: #b91c1c;
    }

    .adm-btn-dismiss {
      background: #e2e8f0;
      color: #1f2937;
    }
    .adm-btn-dismiss:hover:not(:disabled) {
      background: #cbd5e1;
    }

    .adm-btn-postpone {
      background: transparent;
      color: #475569;
      border-color: #cbd5e1;
    }
    .adm-btn-postpone:hover:not(:disabled) {
      background: #f1f5f9;
    }

    @media (min-width: 560px) {
      .adm-actions {
        flex-direction: row;
      }
      .adm-btn {
        flex: 1;
      }
      /* Keep the destructive choice prominent on wide screens. */
      .adm-btn-confirm { order: 1; }
      .adm-btn-dismiss { order: 2; }
      .adm-btn-postpone { order: 3; flex: 0 0 auto; min-width: 120px; }
    }
  `]
})
export class AccidentDecisionModalComponent implements OnInit, OnDestroy {
  current: PendingAccident | null = null;
  busy: 'confirm' | 'dismiss' | null = null;
  errorMessage: string | null = null;

  /** FIFO buffer: if a second accident fires while we're handling the first. */
  private queue: PendingAccident[] = [];
  private subs = new Subscription();
  private isAdmin = false;

  constructor(
    private signalr: SignalRService,
    private auth: AuthService,
    private api: ApiService
  ) {}

  ngOnInit(): void {
    // Only admins get the modal at all. The backend already filters the
    // notification to admins only, but the check here covers the case
    // where a non-admin is somehow on the same SignalR hub.
    const user = this.auth.getCurrentUserSync();
    this.isAdmin = !!user?.isCompanyAdmin;
    if (!this.isAdmin) return;

    this.subs.add(
      this.signalr.notification$
        .pipe(
          filter((n): n is SignalRNotification => !!n),
          filter(n => n.type === 'accident_detected'
                   && this.isRequiresDecision(n.metadata))
        )
        .subscribe(n => this.enqueue(n))
    );
  }

  ngOnDestroy(): void {
    this.subs.unsubscribe();
  }

  get formattedIncidentAt(): string | null {
    if (!this.current?.incidentAt) return null;
    const d = new Date(this.current.incidentAt);
    if (isNaN(d.getTime())) return null;
    // Use the browser's locale to render the UTC timestamp in local time.
    return d.toLocaleString('fr-FR', {
      day: '2-digit', month: '2-digit', year: 'numeric',
      hour: '2-digit', minute: '2-digit'
    });
  }

  /**
   * Human-readable location combining commune + governorate when both are
   * present. Empty when neither is set — the template hides the row.
   */
  get locationLabel(): string | null {
    if (!this.current) return null;
    const parts = [this.current.locationCommune, this.current.locationGovernorate]
      .filter((p): p is string => !!p && p.trim().length > 0);
    return parts.length ? parts.join(', ') : null;
  }

  /**
   * Accident confirmed → backend stamps status=confirmed and the tow
   * monitor starts watching. Any error surfaces inline; the modal stays
   * open until the admin retries or picks another action.
   */
  confirm(): void {
    if (!this.current || this.busy) return;
    this.busy = 'confirm';
    this.errorMessage = null;
    const id = this.current.accidentEventId;
    this.api.confirmAccident(id).subscribe({
      next: () => this.finishCurrent(),
      error: () => {
        this.busy = null;
        this.errorMessage = "Impossible de confirmer l'accident. Veuillez réessayer.";
      }
    });
  }

  dismiss(): void {
    if (!this.current || this.busy) return;
    this.busy = 'dismiss';
    this.errorMessage = null;
    const id = this.current.accidentEventId;
    this.api.dismissAccident(id).subscribe({
      next: () => this.finishCurrent(),
      error: () => {
        this.busy = null;
        this.errorMessage = "Impossible d'enregistrer la fausse alerte. Veuillez réessayer.";
      }
    });
  }

  /**
   * "Reporter" keeps the notification pending on the backend and only
   * closes the modal locally. The admin can still make the decision
   * later from the notification bell → /rapport-accident/:id.
   */
  postpone(): void {
    if (!this.current || this.busy) return;
    this.advanceQueue();
  }

  /**
   * Extract a pending accident from a SignalR notification. The metadata
   * is free-form JSON so we defensively coerce each field.
   */
  private enqueue(n: SignalRNotification): void {
    const id = Number(n.metadata?.accidentEventId ?? n.referenceId);
    if (!Number.isFinite(id) || id <= 0) return;

    // Skip if this accident is already queued (same SignalR event may be
    // re-pushed after a reconnect).
    if (this.current?.accidentEventId === id) return;
    if (this.queue.some(p => p.accidentEventId === id)) return;

    const pending: PendingAccident = {
      accidentEventId: id,
      vehicleLabel: String(n.metadata?.vehicleLabel ?? ''),
      incidentAt: typeof n.metadata?.incidentAt === 'string'
        ? n.metadata.incidentAt
        : (n.createdAt ?? null),
      locationGovernorate: (n.metadata?.locationGovernorate as string) ?? null,
      locationCommune: (n.metadata?.locationCommune as string) ?? null,
    };

    if (!this.current) {
      this.current = pending;
    } else {
      this.queue.push(pending);
    }
  }

  private finishCurrent(): void {
    this.busy = null;
    this.errorMessage = null;
    this.advanceQueue();
  }

  private advanceQueue(): void {
    this.current = this.queue.shift() ?? null;
    this.busy = null;
    this.errorMessage = null;
  }

  /**
   * Backend metadata values survive the SignalR round-trip as strings,
   * but a client ever sending a boolean shouldn't break us.
   */
  private isRequiresDecision(metadata: any): boolean {
    const value = metadata?.requiresDecision;
    return value === true || value === 'true';
  }
}
