import { ChangeDetectorRef, Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminOnlinePayment, AdminService, ClicToPayStatus } from '../services/admin.service';
import { OnlinePaymentResult } from '../../services/api.service';
import {
  isClicToPayPage, paymentErrorMessage, paymentStatusLabel, paymentView, PaymentView, shouldAutoVerify
} from '../../components/paiement.helpers';

/**
 * Console admin « Paiement ClicToPay » (migration 067) : éprouver le compte marchand AVANT de
 * l'ouvrir aux clients.
 *
 * Un paiement de test, d'un montant libre et borné, part sur la page ClicToPay ; ClicToPay
 * renvoie ici (/admin/paiement-test?ref=…) et la page fait VÉRIFIER la tentative par le serveur
 * (getOrderStatusExtended.do) — le retour du navigateur ne prouve rien. Aucune commande ni aucun
 * abonnement n'est touché. La liste montre toutes les tentatives, tests et clients.
 */
@Component({
  selector: 'admin-paiement-test',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent],
  template: `
    <admin-layout pageTitle="Paiement ClicToPay">
      <div class="pay-page">
        <div class="page-header">
          <div>
            <h2><span class="title-dash"></span>Paiement ClicToPay</h2>
            <p class="subtitle">Éprouver le paiement par carte depuis la console, avant de l'ouvrir aux clients. Aucune commande ni aucun abonnement n'est touché.</p>
          </div>
          <button class="btn-refresh" (click)="reload()">Actualiser</button>
        </div>

        <!-- ── Configuration du serveur ─────────────────────────────── -->
        <section class="card">
          <div class="card-head"><h3>Configuration de ce serveur</h3></div>
          @if (status) {
            <div class="chips">
              <span class="chip" [class.ok]="status.configured" [class.bad]="!status.configured">
                {{ status.configured ? 'Identifiants marchand posés' : 'Identifiants absents' }}
              </span>
              @if (status.configured) {
                <span class="chip" [class.warn]="status.testEnvironment" [class.ok]="!status.testEnvironment">
                  {{ status.testEnvironment ? 'Environnement de TEST' : 'Environnement de PRODUCTION' }} · {{ status.host }}
                </span>
              }
              <span class="chip" [class.ok]="status.clientCheckoutEnabled">
                Clients : {{ status.clientCheckoutEnabled ? 'bouton « Payer par carte » affiché' : 'bouton masqué' }}
              </span>
              @if (status.pilotCompanyIds) {
                <span class="chip">Sociétés pilotes : {{ status.pilotCompanyIds }}</span>
              }
              <span class="chip">Gabarit {{ status.mobilePageView ? 'mobile activé' : 'DESKTOP (mobile désactivé)' }}</span>
            </div>
            @if (!status.configured) {
              <p class="hint">Posez ClicToPay__UserName, ClicToPay__Password et ClicToPay__Enabled=true sur le déploiement gis-api (voir docs/paiement-clictopay.md).</p>
            }
          } @else if (statusError) {
            <p class="error">{{ statusError }}</p>
          } @else {
            <p class="muted">Chargement…</p>
          }
        </section>

        <!-- ── Résultat d'un retour de ClicToPay ───────────────────────── -->
        @if (reference) {
          <section class="card result" [attr.data-tone]="view?.tone || 'pending'" aria-live="polite">
            <div class="card-head">
              <h3>Retour de ClicToPay</h3>
              <span class="mono muted">{{ reference }}</span>
            </div>
            @if (verifying && !result) {
              <p class="muted"><span class="spinner"></span> Vérification auprès de ClicToPay…</p>
            }
            @if (result) {
              <div class="result-main">
                <span class="status-badge" [ngClass]="result.status">{{ label(result.status) }}</span>
                <strong>{{ view?.title }}</strong>
              </div>
              <p class="result-msg">{{ result.message }}</p>
              <dl class="facts">
                <div><dt>Montant</dt><dd>{{ money(result.amount, result.currency) }}</dd></div>
                <div><dt>Carte</dt><dd class="mono">{{ result.cardPanMasked || '—' }}</dd></div>
                <div><dt>N° d'autorisation</dt><dd class="mono">{{ result.approvalCode || '—' }}</dd></div>
                <div><dt>Payé le</dt><dd>{{ result.paidAt ? (result.paidAt | date:'dd/MM/yyyy HH:mm:ss') : '—' }}</dd></div>
              </dl>
              <div class="row-actions">
                <button class="btn-refresh" [disabled]="verifying" (click)="verify(reference)">{{ verifying ? 'Vérification…' : 'Revérifier' }}</button>
                <button class="btn-link" (click)="clearReference()">Fermer</button>
              </div>
            }
            @if (verifyError) { <p class="error">{{ verifyError }}</p> }
          </section>
        }

        <!-- ── Lancer un paiement de test ──────────────────────────────── -->
        <section class="card">
          <div class="card-head"><h3>Lancer un paiement de test</h3></div>
          <div class="form-row">
            <label class="field">
              <span>Montant</span>
              <input type="number" [(ngModel)]="amount" [min]="status?.minAmount ?? 0.1" [max]="status?.maxAmount ?? 1000" step="0.001" inputmode="decimal">
            </label>
            <label class="field">
              <span>Devise</span>
              <select [(ngModel)]="currency">
                <option value="TND">TND</option>
                <option value="EUR">EUR</option>
                <option value="USD">USD</option>
              </select>
            </label>
            <button class="btn-primary" [disabled]="starting || !status?.configured" (click)="startTest()">
              {{ starting ? 'Redirection…' : 'Payer sur ClicToPay' }}
            </button>
          </div>
          <p class="hint">
            Entre {{ status?.minAmount ?? 0.1 }} et {{ status?.maxAmount ?? 1000 }}. Sur l'environnement de test, utilisez les cartes
            de test fournies par ClicToPay (une acceptée, une refusée) : aucun débit réel. La page ClicToPay se saisit au clavier ;
            après paiement, ClicToPay revient ici et le résultat est vérifié automatiquement.
          </p>
          @if (startError) { <p class="error">{{ startError }}</p> }
        </section>

        <!-- ── Dernières tentatives ────────────────────────────────────── -->
        <section class="card">
          <div class="card-head">
            <h3>Dernières tentatives</h3>
            <div class="tabs" role="tablist">
              <button role="tab" [class.active]="kind === null" (click)="setKind(null)">Toutes</button>
              <button role="tab" [class.active]="kind === 'admin_test'" (click)="setKind('admin_test')">Tests console</button>
              <button role="tab" [class.active]="kind === 'subscription'" (click)="setKind('subscription')">Clients</button>
            </div>
          </div>
          @if (loadingList) {
            <p class="muted">Chargement…</p>
          } @else if (!payments.length) {
            <p class="muted">Aucune tentative pour l'instant.</p>
          } @else {
            <div class="table-container">
              <table>
                <thead>
                  <tr><th>Date</th><th>Type</th><th>Société</th><th>Montant</th><th>Statut</th><th>Carte</th><th>Autorisation</th><th>Référence</th></tr>
                </thead>
                <tbody>
                  @for (p of payments; track p.id) {
                    <tr>
                      <td>{{ p.createdAt | date:'dd/MM/yyyy HH:mm' }}</td>
                      <td>{{ p.kind === 'admin_test' ? 'Test' : 'Commande #' + p.orderId }}</td>
                      <td>{{ p.companyName || ('#' + p.companyId) }}</td>
                      <td class="num">{{ money(p.amount, p.currency) }}</td>
                      <td>
                        <span class="status-badge" [ngClass]="p.status">{{ label(p.status) }}</span>
                        @if (p.note) { <div class="note">{{ p.note }}</div> }
                        @if (p.status === 'failed' && p.actionDescription) { <div class="note bank">{{ p.actionDescription }}</div> }
                        @if (p.status === 'pending') { <button class="btn-link verify" (click)="verify(p.reference)">Vérifier</button> }
                      </td>
                      <td class="mono">{{ p.cardPanMasked || '—' }}</td>
                      <td class="mono">{{ p.approvalCode || '—' }}</td>
                      <td class="mono small">{{ p.reference }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </section>
      </div>
    </admin-layout>
  `,
  styles: [`
    .pay-page { padding: 24px; display: flex; flex-direction: column; gap: 18px; }
    .page-header { display: flex; justify-content: space-between; align-items: flex-start; gap: 16px; flex-wrap: wrap; }
    .page-header h2 { margin: 0; display: flex; align-items: center; gap: 8px; font-size: 12px; font-weight: 700; text-transform: uppercase; letter-spacing: .08em; color: var(--adm-sub); }
    .title-dash { display: inline-block; width: 12px; height: 3px; border-radius: 2px; background: var(--adm-indigo); }
    .subtitle { margin: 6px 0 0; font-size: 13px; color: var(--adm-sub); max-width: 720px; }
    .card { background: #fff; border: 1px solid var(--adm-border); border-radius: 14px; padding: 16px 18px; }
    .card-head { display: flex; justify-content: space-between; align-items: center; gap: 12px; flex-wrap: wrap; margin-bottom: 12px; }
    .card-head h3 { margin: 0; font-size: 14px; font-weight: 700; }
    .chips { display: flex; flex-wrap: wrap; gap: 8px; }
    .chip { padding: 5px 10px; border-radius: 999px; font-size: 12px; font-weight: 600; background: rgba(148,163,184,.18); color: #334155; }
    .chip.ok { background: rgba(22,163,74,.13); color: #15803d; }
    .chip.warn { background: rgba(217,119,6,.14); color: #b45309; }
    .chip.bad { background: rgba(220,38,38,.12); color: #b91c1c; }
    .form-row { display: flex; gap: 12px; align-items: flex-end; flex-wrap: wrap; }
    .field { display: flex; flex-direction: column; gap: 5px; font-size: 12px; color: var(--adm-sub); font-weight: 600; }
    .field input, .field select { padding: 9px 12px; border: 1px solid var(--adm-border); border-radius: 10px; font-size: 13px; background: #fff; min-width: 140px; color: #0f172a; }
    .field input:focus-visible, .field select:focus-visible { outline: 2px solid var(--adm-indigo); outline-offset: 1px; }
    .btn-refresh, .btn-primary { padding: 9px 14px; border-radius: 10px; border: 1px solid var(--adm-border); background: #fff; cursor: pointer; font-size: 13px; min-height: 38px; }
    .btn-primary { background: var(--adm-indigo); color: #fff; border-color: var(--adm-indigo); font-weight: 600; }
    .btn-primary:disabled, .btn-refresh:disabled { opacity: .5; cursor: default; }
    .btn-link { background: none; border: none; color: var(--adm-indigo); cursor: pointer; font-size: 13px; padding: 4px 6px; }
    .hint { margin: 10px 0 0; font-size: 12.5px; color: var(--adm-sub); line-height: 1.55; }
    .muted { color: var(--adm-sub); font-size: 13px; }
    .error { margin: 10px 0 0; color: #b91c1c; font-size: 13px; font-weight: 600; }
    .result { border-left: 4px solid #6366f1; }
    .result[data-tone='success'] { border-left-color: #16a34a; }
    .result[data-tone='error'] { border-left-color: #dc2626; }
    .result[data-tone='warning'] { border-left-color: #d97706; }
    .result-main { display: flex; align-items: center; gap: 10px; font-size: 15px; }
    .result-msg { margin: 8px 0 12px; font-size: 13px; color: #334155; }
    .facts { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 10px; margin: 0 0 12px; }
    .facts > div { border: 1px solid var(--adm-border); border-radius: 10px; padding: 8px 10px; }
    .facts dt { font-size: 11px; text-transform: uppercase; letter-spacing: .05em; color: var(--adm-sub); }
    .facts dd { margin: 3px 0 0; font-weight: 650; font-size: 13.5px; }
    .row-actions { display: flex; gap: 8px; align-items: center; }
    .tabs { display: inline-flex; border: 1px solid var(--adm-border); border-radius: 10px; overflow: hidden; }
    .tabs button { padding: 7px 12px; border: none; background: #fff; font-size: 12.5px; cursor: pointer; color: #334155; }
    .tabs button + button { border-left: 1px solid var(--adm-border); }
    .tabs button.active { background: var(--adm-indigo); color: #fff; }
    .table-container { overflow-x: auto; }
    table { width: 100%; border-collapse: collapse; font-size: 13px; }
    th { text-align: left; font-size: 11px; text-transform: uppercase; letter-spacing: .06em; color: var(--adm-sub); padding: 8px 10px; border-bottom: 1px solid var(--adm-border); white-space: nowrap; }
    td { padding: 8px 10px; border-bottom: 1px solid var(--adm-border); vertical-align: top; }
    .num { text-align: right; white-space: nowrap; font-variant-numeric: tabular-nums; }
    .mono { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 12px; }
    .small { font-size: 11px; }
    .note { margin-top: 4px; font-size: 11.5px; color: #b45309; min-width: 200px; max-width: 300px; line-height: 1.4; }
    .note.bank { color: var(--adm-sub); }
    .btn-link.verify { display: block; padding: 4px 0 0; }
    .status-badge { display: inline-block; padding: 3px 8px; border-radius: 999px; font-size: 11px; font-weight: 600; background: rgba(148,163,184,.2); color: #334155; }
    .status-badge.paid { background: rgba(22,163,74,.15); color: #15803d; }
    .status-badge.pending { background: rgba(217,119,6,.15); color: #b45309; }
    .status-badge.failed, .status-badge.error, .status-badge.expired { background: rgba(220,38,38,.12); color: #b91c1c; }
    .status-badge.anomaly { background: rgba(217,119,6,.25); color: #92400e; }
    .spinner { display: inline-block; width: 12px; height: 12px; border: 2px solid rgba(99,102,241,.25); border-top-color: #6366f1; border-radius: 50%; animation: spin .8s linear infinite; vertical-align: -2px; margin-right: 6px; }
    @keyframes spin { to { transform: rotate(360deg); } }
  `]
})
export class AdminPaiementTestComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  private timer: ReturnType<typeof setTimeout> | null = null;
  private attempts = 0;

  status: ClicToPayStatus | null = null;
  statusError = '';

  amount = 1;
  currency = 'TND';
  starting = false;
  startError = '';

  reference = '';
  result: OnlinePaymentResult | null = null;
  view: PaymentView | null = null;
  verifying = false;
  verifyError = '';

  kind: 'subscription' | 'admin_test' | null = null;
  payments: AdminOnlinePayment[] = [];
  loadingList = false;

  constructor(
    private admin: AdminService,
    private route: ActivatedRoute,
    private router: Router,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.loadStatus();
    this.loadPayments();
    // Retour de ClicToPay : ?ref=… (et ?echec=1 sur la page d'échec, jamais lu pour décider).
    const ref = (this.route.snapshot.queryParamMap.get('ref') || '').trim();
    if (ref) this.verify(ref);
  }

  reload(): void {
    this.loadStatus();
    this.loadPayments();
  }

  loadStatus(): void {
    this.statusError = '';
    this.admin.getClicToPayStatus().pipe(takeUntil(this.destroy$)).subscribe({
      next: (s) => { this.status = s; this.cdr.detectChanges(); },
      error: (err) => { this.statusError = paymentErrorMessage(err, 'Configuration illisible.'); this.cdr.detectChanges(); }
    });
  }

  loadPayments(): void {
    this.loadingList = true;
    this.admin.getAdminPayments(50, this.kind ?? undefined).pipe(takeUntil(this.destroy$)).subscribe({
      next: (rows) => { this.payments = rows ?? []; this.loadingList = false; this.cdr.detectChanges(); },
      error: () => { this.payments = []; this.loadingList = false; this.cdr.detectChanges(); }
    });
  }

  setKind(kind: 'subscription' | 'admin_test' | null): void {
    this.kind = kind;
    this.loadPayments();
  }

  startTest(): void {
    if (this.starting) return;
    this.startError = '';
    const amount = Number(this.amount);
    if (!isFinite(amount) || amount <= 0) {
      this.startError = 'Saisissez un montant.';
      return;
    }
    this.starting = true;
    this.admin.startClicToPayTest(amount, this.currency).pipe(takeUntil(this.destroy$)).subscribe({
      next: (start) => {
        if (!isClicToPayPage(start.formUrl)) {
          this.starting = false;
          this.startError = 'ClicToPay a renvoyé une adresse de paiement inattendue : rien n\'est ouvert.';
          this.cdr.detectChanges();
          return;
        }
        this.redirectTo(start.formUrl as string);
      },
      error: (err) => {
        this.starting = false;
        this.startError = paymentErrorMessage(err, 'Le paiement de test n\'a pas pu être préparé.');
        this.loadPayments();
        this.cdr.detectChanges();
      }
    });
  }

  verify(reference: string): void {
    if (this.verifying) return;
    if (reference !== this.reference) { this.attempts = 0; this.result = null; this.view = null; }
    this.reference = reference;
    this.verifying = true;
    this.verifyError = '';
    this.attempts++;
    this.admin.verifyAdminPayment(reference).pipe(takeUntil(this.destroy$)).subscribe({
      next: (res) => {
        this.verifying = false;
        this.result = res;
        this.view = paymentView({ status: res.status, orderConfirmed: res.status === 'paid' });
        this.loadPayments();
        // La banque n'a pas encore tranché : quelques revérifications espacées.
        if (shouldAutoVerify(res.status, this.attempts)) {
          this.timer = setTimeout(() => this.verify(reference), 3000);
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.verifying = false;
        this.verifyError = paymentErrorMessage(err, 'Vérification impossible.');
        this.cdr.detectChanges();
      }
    });
  }

  clearReference(): void {
    if (this.timer) { clearTimeout(this.timer); this.timer = null; }
    this.reference = '';
    this.result = null;
    this.view = null;
    this.router.navigate([], { relativeTo: this.route, queryParams: {} });
  }

  label(status: string): string {
    return paymentStatusLabel(status);
  }

  /** 1.5 TND → « 1,500 TND » (millimes), 3 EUR → « 3,00 EUR ». */
  money(amount: number, currency: string): string {
    const digits = currency === 'TND' ? 3 : 2;
    return `${Number(amount).toLocaleString('fr-FR', { minimumFractionDigits: digits, maximumFractionDigits: digits })} ${currency}`;
  }

  /** Quitte la console pour la page ClicToPay (isolé pour les tests). */
  redirectTo(url: string): void {
    window.location.assign(url);
  }

  ngOnDestroy(): void {
    if (this.timer) clearTimeout(this.timer);
    this.destroy$.next();
    this.destroy$.complete();
  }
}
