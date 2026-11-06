import { Component, OnDestroy, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { ApiService, OnlinePaymentResult } from '../services/api.service';
import { USER_PREF_PIPES } from '../pipes/user-preference-pipes';
import { paymentErrorMessage, paymentView, PaymentView, shouldAutoVerify } from './paiement.helpers';

/**
 * Retour de ClicToPay (/paiement/retour?ref=…, migration 067).
 *
 * RÈGLE DU MANUEL : arriver ici ne prouve rien, ni sur la page « succès » ni sur la page
 * « échec ». L'écran demande au serveur de VÉRIFIER la tentative auprès de ClicToPay ; c'est
 * cette vérification qui valide la commande et prolonge l'abonnement. Le paramètre « echec »
 * n'est donc jamais lu pour décider.
 *
 * Pleine page, sans le cadre de l'application : une société bloquée revient ici après avoir payé,
 * et les appels du cadre (cloche, menus) la renverraient vers l'écran de blocage pendant la
 * vérification.
 */
@Component({
  selector: 'app-payment-return',
  standalone: true,
  imports: [CommonModule, ...USER_PREF_PIPES],
  template: `
    <div class="pay-return">
      <div class="card" [attr.data-tone]="view?.tone || 'pending'" role="status" aria-live="polite">
        @if (testMode) {
          <span class="test-badge">Environnement de test — aucun débit réel</span>
        }

        <div class="icon" aria-hidden="true">
          @switch (state) {
            @case ('verifying') { <span class="spinner"></span> }
            @case ('done') {
              @switch (view?.tone) {
                @case ('success') {
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>
                }
                @case ('error') {
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 6 6 18M6 6l12 12"/></svg>
                }
                @case ('warning') {
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 9v4M12 17h.01"/><path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z"/></svg>
                }
                @default {
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></svg>
                }
              }
            }
            @default {
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 6 6 18M6 6l12 12"/></svg>
            }
          }
        </div>

        @switch (state) {
          @case ('verifying') {
            <h1>Vérification du paiement…</h1>
            <p class="lead">Nous confirmons le résultat auprès de la banque. Ne fermez pas cette page.</p>
          }
          @case ('done') {
            <h1>{{ view?.title }}</h1>
            <p class="lead">{{ result?.message }}</p>

            @if (result) {
              <dl class="facts">
                <div><dt>Montant</dt><dd>{{ result.amount | appCurrency }}</dd></div>
                @if (result.cardPanMasked) {
                  <div><dt>Carte</dt><dd class="mono">{{ result.cardPanMasked }}</dd></div>
                }
                @if (result.approvalCode) {
                  <div><dt>N° d'autorisation</dt><dd class="mono">{{ result.approvalCode }}</dd></div>
                }
                @if (result.orderConfirmed && result.subscriptionExpiresAt) {
                  <div><dt>Abonnement actif jusqu'au</dt><dd>{{ longDate(result.subscriptionExpiresAt) }}</dd></div>
                }
                <div><dt>Référence</dt><dd class="mono">{{ result.reference }}</dd></div>
              </dl>
            }

            @if (view?.tone === 'pending') {
              <p class="hint">
                La banque n'a pas encore confirmé ce paiement. S'il est accepté, votre abonnement sera
                activé automatiquement dans les minutes qui suivent, même si vous fermez cette page.
              </p>
            }
          }
          @default {
            <h1>Paiement introuvable</h1>
            <p class="lead">{{ errorMessage }}</p>
          }
        }

        <div class="actions">
          @if (state === 'done' && view?.tone === 'success') {
            <button class="btn primary" (click)="go('/dashboard')">Accéder à Calypso</button>
            <button class="btn ghost" (click)="go('/subscription')">Voir mon abonnement</button>
          } @else if (state === 'done' && view?.canPayAgain) {
            <button class="btn primary" (click)="go('/subscription')">Payer à nouveau</button>
          } @else if (state === 'done' && view?.tone === 'pending') {
            <button class="btn primary" [disabled]="busy" (click)="verify()">{{ busy ? 'Vérification…' : 'Vérifier à nouveau' }}</button>
            <button class="btn ghost" (click)="go('/subscription')">Retour à mon abonnement</button>
          } @else if (state !== 'verifying') {
            <button class="btn primary" (click)="go('/subscription')">Retour à mon abonnement</button>
          }
        </div>

        <p class="secure">
          <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><rect x="4" y="11" width="16" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/></svg>
          Paiement sécurisé par ClicToPay · 3-D Secure
        </p>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .pay-return {
      --pr-surface: var(--bg-card, #ffffff);
      --pr-page: var(--bg-page, #f1f5f9);
      --pr-ink: var(--text-primary, #0f172a);
      --pr-muted: var(--text-secondary, #64748b);
      --pr-line: var(--border-color, #e2e8f0);
      min-height: 100vh; display: flex; align-items: center; justify-content: center;
      padding: 24px 16px; background: var(--pr-page);
      font-family: var(--font-family, 'Inter', system-ui, sans-serif);
    }
    .card {
      --tone: #4f46e5; --tone-soft: rgba(79, 70, 229, .1);
      width: 100%; max-width: 460px; background: var(--pr-surface); color: var(--pr-ink);
      border: 1px solid var(--pr-line); border-radius: 18px; padding: 32px 28px 22px;
      box-shadow: 0 24px 48px -28px rgba(15, 23, 42, .35); text-align: center;
    }
    .card[data-tone='success'] { --tone: #059669; --tone-soft: rgba(5, 150, 105, .12); }
    .card[data-tone='warning'] { --tone: #d97706; --tone-soft: rgba(217, 119, 6, .12); }
    .card[data-tone='error'] { --tone: #dc2626; --tone-soft: rgba(220, 38, 38, .1); }
    .test-badge {
      display: inline-block; margin-bottom: 14px; padding: 4px 10px; border-radius: 999px;
      font-size: 11.5px; font-weight: 700; color: #92400e; background: #fef3c7; border: 1px solid #fcd34d;
    }
    .icon {
      width: 60px; height: 60px; margin: 0 auto 16px; border-radius: 50%;
      display: grid; place-items: center; color: var(--tone); background: var(--tone-soft);
    }
    .icon svg { width: 28px; height: 28px; }
    .spinner {
      width: 26px; height: 26px; border-radius: 50%;
      border: 3px solid var(--tone-soft); border-top-color: var(--tone);
      animation: spin .8s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }
    @media (prefers-reduced-motion: reduce) { .spinner { animation-duration: 2.4s; } }
    h1 { margin: 0 0 8px; font-size: 21px; font-weight: 800; letter-spacing: -.01em; }
    .lead { margin: 0 0 18px; font-size: 14px; line-height: 1.55; color: var(--pr-muted); }
    .facts {
      margin: 0 0 18px; padding: 4px 0; text-align: left;
      border-top: 1px solid var(--pr-line); border-bottom: 1px solid var(--pr-line);
    }
    .facts > div { display: flex; justify-content: space-between; gap: 12px; padding: 8px 2px; font-size: 13.5px; }
    .facts > div + div { border-top: 1px dashed var(--pr-line); }
    .facts dt { color: var(--pr-muted); }
    .facts dd { margin: 0; font-weight: 650; text-align: right; overflow-wrap: anywhere; }
    .mono { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; font-size: 12.5px; }
    .hint { margin: 0 0 16px; font-size: 12.5px; line-height: 1.55; color: var(--pr-muted); }
    .actions { display: flex; gap: 10px; justify-content: center; flex-wrap: wrap; }
    .btn {
      padding: 10px 18px; border-radius: 10px; font-size: 13.5px; font-weight: 650; cursor: pointer;
      border: 1px solid transparent; min-height: 40px;
    }
    .btn:disabled { opacity: .6; cursor: default; }
    .btn.primary { background: #4f46e5; color: #fff; }
    .btn.primary:hover:not(:disabled) { background: #4338ca; }
    .btn.ghost { background: transparent; color: var(--pr-ink); border-color: var(--pr-line); }
    .btn:focus-visible { outline: 3px solid rgba(79, 70, 229, .45); outline-offset: 2px; }
    .secure {
      margin: 20px 0 0; display: flex; align-items: center; justify-content: center; gap: 6px;
      font-size: 11.5px; color: var(--pr-muted);
    }
  `]
})
export class PaymentReturnComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  private timer: ReturnType<typeof setTimeout> | null = null;
  private attempts = 0;

  state: 'verifying' | 'done' | 'missing' = 'verifying';
  reference = '';
  result: OnlinePaymentResult | null = null;
  view: PaymentView | null = null;
  errorMessage = '';
  busy = false;
  testMode = false;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private api: ApiService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.reference = (this.route.snapshot.queryParamMap.get('ref') || '').trim();
    if (!this.reference) {
      this.state = 'missing';
      this.errorMessage = 'Le lien de retour ne contient pas de référence de paiement. Consultez votre abonnement.';
      return;
    }
    this.verify();
  }

  verify(): void {
    if (this.busy) return;
    this.busy = true;
    this.attempts++;
    this.api.verifyOnlinePayment(this.reference).pipe(takeUntil(this.destroy$)).subscribe({
      next: (res) => {
        this.busy = false;
        this.result = res;
        this.view = paymentView(res);
        this.testMode = res.testMode;
        this.state = 'done';
        // Banque pas encore décidée : quelques revérifications espacées, puis la main au client.
        if (shouldAutoVerify(res.status, this.attempts)) {
          this.state = 'verifying';
          this.timer = setTimeout(() => this.verify(), 3000);
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.busy = false;
        this.state = 'missing';
        this.errorMessage = paymentErrorMessage(err, 'La vérification du paiement a échoué. Réessayez depuis votre abonnement.');
        this.cdr.detectChanges();
      }
    });
  }

  go(path: string): void {
    this.router.navigate([path]);
  }

  /** « 10 septembre 2027 », sans dépendre d'une locale Angular enregistrée. */
  longDate(value: string | null): string {
    if (!value) return '';
    const d = new Date(value);
    return isNaN(d.getTime()) ? '' : d.toLocaleDateString('fr-FR', { day: 'numeric', month: 'long', year: 'numeric' });
  }

  ngOnDestroy(): void {
    if (this.timer) clearTimeout(this.timer);
    this.destroy$.next();
    this.destroy$.complete();
  }
}
