import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subject, takeUntil } from 'rxjs';
import { AppLayoutComponent } from './shared/app-layout.component';
import { USER_PREF_PIPES } from '../pipes/user-preference-pipes';
import { ApiService, SubscriptionOrder } from '../services/api.service';
import { AuthService } from '../services/auth.service';
import { environment } from '../environments/environment';

/**
 * Abonnement & paiement.
 *
 * CE QUE CETTE PAGE PROMET — rien qu'elle ne puisse tenir. Le paiement en ligne
 * n'est branché sur AUCUN prestataire : le bouton est donc inactif et le dit. On
 * ne montre ni historique de facturation ni reçu, parce qu'aucune trace de
 * paiement n'existe en base (la société ne porte que son dernier règlement et le
 * montant du prochain). Un tableau vide « Historique » laisserait croire à une
 * fonctionnalité en panne plutôt qu'absente.
 *
 * TOUT VIENT DU SERVEUR — le plan courant, l'échéance et le montant dû sortent de
 * /api/subscriptions/current ; la grille des offres de /api/subscriptions. Aucun
 * prix n'est recalculé côté écran, et aucun symbole monétaire n'est écrit en dur :
 * la version précédente affichait « € » sur un déploiement facturé en dinars.
 */
@Component({
  selector: 'app-subscription',
  standalone: true,
  imports: [CommonModule, AppLayoutComponent, ...USER_PREF_PIPES],
  template: `
    <app-layout>
      <div class="sub-page">
        <header class="page-head">
          <div>
            <h1>Abonnement</h1>
            <p class="sub">Votre offre, son échéance et les formules disponibles.</p>
          </div>
        </header>

        @if (loading) {
          <div class="card skeleton">Chargement de votre abonnement…</div>
        } @else {

          <!-- Offre en cours -->
          <section class="card current" [class.expiring]="daysRemaining !== null && daysRemaining <= 7">
            <div class="current-main">
              <span class="label">Offre en cours</span>
              <h2>{{ currentPlan?.name || 'Aucune offre active' }}</h2>
              @if (currentPlan?.description) {
                <p class="desc">{{ currentPlan.description }}</p>
              }
            </div>

            <div class="current-facts">
              <div class="fact">
                <span class="fact-label">Échéance</span>
                @if (expiresAt) {
                  <span class="fact-value">{{ expiresAt | date:'dd MMMM yyyy' }}</span>
                  @if (daysRemaining !== null) {
                    <span class="fact-note" [class.warn]="daysRemaining <= 7">
                      {{ daysRemaining > 0 ? 'dans ' + daysRemaining + ' jour(s)' : 'échue' }}
                    </span>
                  }
                } @else {
                  <span class="fact-value muted">—</span>
                }
              </div>
              <div class="fact">
                <span class="fact-label">Prochain règlement</span>
                @if (nextPaymentAmount != null) {
                  <span class="fact-value">{{ nextPaymentAmount | appCurrency }}</span>
                  <span class="fact-note">{{ billingCycleLabel }}</span>
                } @else {
                  <span class="fact-value muted">—</span>
                }
              </div>
            </div>
          </section>

          <!-- Consommation par rapport aux limites du plan -->
          @if (usage) {
            <section class="card">
              <h3 class="card-title">Utilisation</h3>
              <div class="quota-grid">
                @for (q of quotas; track q.label) {
                  <div class="quota">
                    <div class="quota-head">
                      <span>{{ q.label }}</span>
                      <!-- Une limite à 0 signifie « pas inclus dans l'offre »,
                           jamais « illimité ». L'affichage « ∞ » promettait des
                           boîtiers GPS sans limite sur un plan qui n'en autorise
                           aucun — exactement l'inverse. -->
                      <span class="quota-num">{{ q.current }} <span class="of">/ {{ q.max > 0 ? q.max : 'non inclus' }}</span></span>
                    </div>
                    <div class="bar"><div class="bar-fill" [style.width.%]="q.pct" [class.full]="q.pct >= 90"></div></div>
                  </div>
                }
              </div>
            </section>
          }

          <!-- Commande en cours : le client a commandé, la plateforme valide à
               réception du règlement. Une seule commande en attente à la fois. -->
          @if (pendingOrder) {
            <section class="card order-pending">
              <div class="order-head">
                <span class="order-dot"></span>
                <div>
                  <h3 class="card-title tight">Commande en attente de validation</h3>
                  <p class="order-line">
                    {{ pendingOrder.planName }} — {{ cycleLabel(pendingOrder.billingCycle) }} —
                    <strong>{{ pendingOrder.amount | appCurrency }}</strong>
                    <span class="order-date">passée le {{ pendingOrder.createdAt | date:'dd/MM/yyyy' }}</span>
                  </p>
                  <p class="order-hint">
                    Nous activerons votre abonnement dès réception du règlement, sous 24&nbsp;h ouvrées.
                    Vous serez averti dans l'application.
                  </p>
                </div>
                <button class="btn-cancel-order" [disabled]="busy" (click)="cancelPending()">Annuler</button>
              </div>
            </section>
          }

          <!-- Dernier rejet : le client doit savoir pourquoi, sinon il recommande. -->
          @if (lastRejection) {
            <section class="card order-rejected">
              <strong>Votre dernière commande a été refusée.</strong>
              <p>{{ lastRejection.note }}</p>
            </section>
          }

          <!-- Grille des offres -->
          <section class="card">
            <h3 class="card-title">Nos formules</h3>
            <div class="plans">
              @for (p of plans; track p.id) {
                <article class="plan" [class.is-current]="p.id === currentPlan?.id">
                  <header>
                    <h4>{{ p.name }}</h4>
                    @if (p.id === currentPlan?.id) { <span class="badge">Votre offre</span> }
                  </header>
                  <div class="price">
                    <strong>{{ p.yearlyPrice | appCurrency }}</strong>
                    <span>/ an</span>
                  </div>
                  @if (p.monthlyPrice > 0) {
                    <p class="price-alt">ou {{ p.monthlyPrice | appCurrency }} / mois</p>
                  }
                  <ul class="plan-limits">
                    <li>{{ p.maxVehicles }} véhicules</li>
                    <li>{{ p.maxUsers }} utilisateurs</li>
                    @if (p.gpsTracking) { <li>Suivi GPS temps réel</li> } @else { <li>Gestion sans boîtier GPS</li> }
                  </ul>

                  <!-- Seule l'offre GPA (sans matériel) s'achète en ligne : les
                       offres GPS impliquent des boîtiers, et pour l'installation
                       une intervention — elles se vendent avec un humain au bout
                       du fil. Le serveur refuse de toute façon les autres. -->
                  @if (isPurchasable(p)) {
                    <button class="btn-plan buy" [disabled]="busy || !!pendingOrder"
                            (click)="openPurchase(p)">
                      {{ pendingOrder ? 'Commande en cours…' : 'Choisir cette formule' }}
                    </button>
                  } @else {
                    <button class="btn-plan" disabled title="Offre avec matériel GPS — contactez-nous">Nous contacter</button>
                  }
                </article>
              }
            </div>

            <!-- Panneau d'achat : choix du cycle, montant calculé par le serveur
                 à la commande — l'affichage ici n'est qu'un aperçu. -->
            @if (purchasePlan) {
              <div class="purchase-panel">
                <h4>Commander « {{ purchasePlan.name }} »</h4>
                <div class="cycle-choices">
                  @for (c of availableCycles(purchasePlan); track c.value) {
                    <label class="cycle" [class.active]="selectedCycle === c.value">
                      <input type="radio" name="cycle" [value]="c.value"
                             [checked]="selectedCycle === c.value"
                             (change)="selectedCycle = c.value" />
                      <span class="cycle-name">{{ c.label }}</span>
                      <span class="cycle-price">{{ c.price | appCurrency }}</span>
                    </label>
                  }
                </div>
                <div class="purchase-actions">
                  <button class="btn-plan buy" [disabled]="busy" (click)="confirmPurchase()">
                    {{ busy ? 'Envoi…' : 'Confirmer ma commande' }}
                  </button>
                  <button class="btn-cancel-order" [disabled]="busy" (click)="purchasePlan = null">Retour</button>
                </div>
                <p class="order-hint">
                  Après votre commande, nous vous contactons pour le règlement
                  (virement ou autre) et activons l'abonnement dès réception.
                </p>
                @if (purchaseError) { <p class="purchase-error">{{ purchaseError }}</p> }
              </div>
            }

            <div class="notice">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/>
              </svg>
              <div>
                <strong>Le règlement se fait hors ligne pour l'instant.</strong>
                <p>Votre commande est enregistrée immédiatement&nbsp;; l'abonnement est activé dès réception du règlement, sous 24&nbsp;h ouvrées.</p>
              </div>
            </div>
          </section>
        }
      </div>
    </app-layout>
  `,
  styles: [`
    .sub-page { max-width: 1100px; margin: 0 auto; padding: 4px 0 40px; }

    .page-head { margin-bottom: 22px; }
    .page-head h1 { font-size: 24px; font-weight: 800; color: var(--text-primary, #0f172a); margin: 0 0 4px; letter-spacing: -.02em; }
    .page-head .sub { margin: 0; color: var(--text-secondary, #64748b); font-size: 14px; }

    .card {
      background: var(--bg-card, #fff);
      border: 1px solid var(--border-color, #e6eaf2);
      border-radius: 14px;
      padding: 22px 24px;
      margin-bottom: 18px;
    }
    .card-title { font-size: 15px; font-weight: 700; color: var(--text-primary, #0f172a); margin: 0 0 18px; }
    .skeleton { color: var(--text-secondary, #64748b); font-size: 14px; }

    .current { display: flex; flex-wrap: wrap; gap: 28px; align-items: flex-start; justify-content: space-between; }
    .current.expiring { border-color: #fca5a5; }
    .current-main .label { font-size: 11.5px; font-weight: 700; letter-spacing: .06em; text-transform: uppercase; color: #4f46e5; }
    .current-main h2 { font-size: 22px; font-weight: 800; color: var(--text-primary, #0f172a); margin: 6px 0 4px; }
    .current-main .desc { margin: 0; font-size: 13.5px; color: var(--text-secondary, #64748b); max-width: 46ch; }

    .current-facts { display: flex; gap: 34px; }
    .fact { display: flex; flex-direction: column; gap: 3px; }
    .fact-label { font-size: 12px; color: var(--text-secondary, #64748b); }
    .fact-value { font-size: 17px; font-weight: 700; color: var(--text-primary, #0f172a); }
    .fact-value.muted { color: #94a3b8; font-weight: 600; }
    .fact-note { font-size: 12px; color: var(--text-secondary, #64748b); }
    .fact-note.warn { color: #b91c1c; font-weight: 600; }

    .quota-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(210px, 1fr)); gap: 18px; }
    .quota-head { display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 7px; font-size: 13px; color: var(--text-secondary, #64748b); }
    .quota-num { font-weight: 700; color: var(--text-primary, #0f172a); }
    .quota-num .of { font-weight: 500; color: #94a3b8; }
    .bar { height: 6px; border-radius: 99px; background: var(--bg-hover, #eef2f7); overflow: hidden; }
    .bar-fill { height: 100%; background: #4f46e5; border-radius: 99px; transition: width .3s; }
    .bar-fill.full { background: #dc2626; }

    .plans { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 16px; }
    .plan {
      border: 1px solid var(--border-color, #e6eaf2);
      border-radius: 12px;
      padding: 18px;
      display: flex; flex-direction: column; gap: 10px;
    }
    .plan.is-current { border-color: #4f46e5; box-shadow: 0 0 0 3px rgba(79,70,229,.1); }
    .plan header { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
    .plan h4 { margin: 0; font-size: 15.5px; font-weight: 700; color: var(--text-primary, #0f172a); }
    .badge { font-size: 11px; font-weight: 700; color: #4f46e5; background: rgba(79,70,229,.1); padding: 3px 8px; border-radius: 99px; }
    .price { display: flex; align-items: baseline; gap: 5px; }
    .price strong { font-size: 20px; font-weight: 800; color: var(--text-primary, #0f172a); }
    .price span { font-size: 12.5px; color: var(--text-secondary, #64748b); }
    .price-alt { margin: -4px 0 0; font-size: 12.5px; color: var(--text-secondary, #64748b); }
    .plan-limits { list-style: none; margin: 4px 0 0; padding: 0; display: flex; flex-direction: column; gap: 5px; }
    .plan-limits li { font-size: 13px; color: var(--text-secondary, #64748b); }
    .btn-plan {
      margin-top: auto;
      padding: 10px; border-radius: 9px; border: 1px solid var(--border-color, #e6eaf2);
      background: var(--bg-hover, #f8fafc); color: #94a3b8;
      font-size: 13.5px; font-weight: 600; cursor: not-allowed;
    }

    .btn-plan.buy {
      background: #4f46e5; color: #fff; border-color: #4f46e5; cursor: pointer;
      transition: all .2s;
    }
    .btn-plan.buy:hover:not(:disabled) { background: #4338ca; }
    .btn-plan.buy:disabled { opacity: .6; cursor: default; }

    .order-pending { border-color: #f59e0b; }
    .order-head { display: flex; gap: 14px; align-items: flex-start; }
    .order-dot {
      width: 10px; height: 10px; border-radius: 50%; margin-top: 6px;
      background: #f59e0b; flex-shrink: 0;
      box-shadow: 0 0 0 4px rgba(245,158,11,.15);
    }
    .card-title.tight { margin-bottom: 6px; }
    .order-line { margin: 0 0 4px; font-size: 14px; color: var(--text-primary, #0f172a); }
    .order-date { color: var(--text-secondary, #64748b); font-size: 12.5px; margin-left: 8px; }
    .order-hint { margin: 6px 0 0; font-size: 12.5px; color: var(--text-secondary, #64748b); line-height: 1.5; }
    .btn-cancel-order {
      margin-left: auto; padding: 8px 16px; border-radius: 9px;
      background: transparent; border: 1px solid var(--border-color, #e6eaf2);
      color: #b91c1c; font-size: 13px; font-weight: 600; cursor: pointer;
    }
    .btn-cancel-order:disabled { opacity: .5; cursor: default; }

    .order-rejected {
      border-color: #fca5a5; font-size: 13.5px; color: var(--text-primary, #0f172a);
    }
    .order-rejected p { margin: 6px 0 0; color: var(--text-secondary, #64748b); }

    .purchase-panel {
      margin-top: 18px; padding: 18px;
      border: 1px solid #c7d2fe; border-radius: 12px;
      background: rgba(79,70,229,.04);
    }
    .purchase-panel h4 { margin: 0 0 14px; font-size: 15px; font-weight: 700; color: var(--text-primary, #0f172a); }
    .cycle-choices { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 10px; }
    .cycle {
      display: flex; flex-direction: column; gap: 4px;
      padding: 12px 14px; border: 1px solid var(--border-color, #e6eaf2);
      border-radius: 10px; cursor: pointer; background: var(--bg-card, #fff);
    }
    .cycle.active { border-color: #4f46e5; box-shadow: 0 0 0 3px rgba(79,70,229,.12); }
    .cycle input { display: none; }
    .cycle-name { font-size: 13px; font-weight: 600; color: var(--text-secondary, #64748b); }
    .cycle-price { font-size: 16px; font-weight: 800; color: var(--text-primary, #0f172a); }
    .purchase-actions { display: flex; gap: 10px; margin-top: 14px; }
    .purchase-actions .btn-plan { margin-top: 0; width: auto; padding: 10px 22px; }
    .purchase-error { margin: 10px 0 0; font-size: 13px; color: #b91c1c; }

    .notice {
      display: flex; gap: 12px; align-items: flex-start;
      margin-top: 20px; padding: 14px 16px;
      background: rgba(79,70,229,.06);
      border: 1px solid rgba(79,70,229,.2);
      border-radius: 11px;
      color: #3730a3;
    }
    .notice strong { display: block; font-size: 13.5px; margin-bottom: 3px; }
    .notice p { margin: 0; font-size: 13px; color: var(--text-secondary, #64748b); line-height: 1.5; }

    @media (max-width: 720px) {
      .current { flex-direction: column; gap: 20px; }
      .current-facts { gap: 24px; }
    }
  `]
})
export class SubscriptionComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();

  loading = true;
  plans: any[] = [];
  currentPlan: any = null;
  usage: any = null;
  expiresAt: string | null = null;
  nextPaymentAmount: number | null = null;
  billingCycle: string | null = null;

  // ── Achat en libre-service ──
  orders: SubscriptionOrder[] = [];
  purchasePlan: any = null;
  selectedCycle: 'monthly' | 'quarterly' | 'yearly' = 'yearly';
  busy = false;
  purchaseError: string | null = null;

  // Miroir de CreateSubscriptionOrderCommand.SelfPurchasablePlanCodes : seule
  // l'offre GPA (sans matériel) s'achète sans humain. Le serveur fait foi ;
  // ici on évite juste d'afficher un bouton qui échouera.
  private static readonly SELF_PURCHASABLE_CODES = ['plan-basique'];

  brand = (environment as any).brandName || 'Calypso';

  constructor(
    private apiService: ApiService,
    private authService: AuthService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.apiService.getCurrentSubscription().pipe(takeUntil(this.destroy$)).subscribe({
      next: (res: any) => {
        this.currentPlan = res?.subscriptionType ?? null;
        this.usage = res?.usage ?? null;
        this.expiresAt = res?.expiresAt ?? null;
        this.nextPaymentAmount = res?.nextPaymentAmount ?? null;
        this.billingCycle = res?.billingCycle ?? null;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => { this.loading = false; this.cdr.detectChanges(); }
    });

    this.apiService.getSubscriptions().pipe(takeUntil(this.destroy$)).subscribe({
      next: (res: any) => { this.plans = res ?? []; this.cdr.detectChanges(); },
      error: () => { this.plans = []; }
    });

    this.loadOrders();
  }

  private loadOrders(): void {
    this.apiService.getMySubscriptionOrders().pipe(takeUntil(this.destroy$)).subscribe({
      next: (res) => { this.orders = res ?? []; this.cdr.detectChanges(); },
      error: () => { this.orders = []; }
    });
  }

  /** La commande en attente, s'il y en a une (le serveur n'en autorise qu'une). */
  get pendingOrder(): SubscriptionOrder | null {
    return this.orders.find(o => o.status === 'pending') ?? null;
  }

  /** Le dernier rejet, montré seulement s'il est plus récent que toute autre issue. */
  get lastRejection(): SubscriptionOrder | null {
    const latest = this.orders[0]; // le serveur renvoie les plus récentes en tête
    return latest?.status === 'rejected' && latest.note ? latest : null;
  }

  isPurchasable(plan: any): boolean {
    return SubscriptionComponent.SELF_PURCHASABLE_CODES.includes(plan?.code)
      && plan?.isActive !== false;
  }

  cycleLabel(cycle: string): string {
    switch (cycle) {
      case 'monthly': return 'mensuel';
      case 'quarterly': return 'trimestriel';
      case 'yearly': return 'annuel';
      default: return cycle;
    }
  }

  /** Les cycles proposables pour un plan : un cycle à 0 n'est pas vendable. */
  availableCycles(plan: any): Array<{ value: 'monthly' | 'quarterly' | 'yearly'; label: string; price: number }> {
    const all: Array<{ value: 'monthly' | 'quarterly' | 'yearly'; label: string; price: number }> = [
      { value: 'monthly', label: 'Mensuel', price: plan?.monthlyPrice ?? 0 },
      { value: 'quarterly', label: 'Trimestriel', price: plan?.quarterlyPrice ?? 0 },
      { value: 'yearly', label: 'Annuel', price: plan?.yearlyPrice ?? 0 }
    ];
    return all.filter(c => c.price > 0);
  }

  openPurchase(plan: any): void {
    this.purchasePlan = plan;
    this.purchaseError = null;
    const cycles = this.availableCycles(plan);
    // Par défaut l'annuel (le moins cher au mois), sinon le premier disponible.
    this.selectedCycle = cycles.some(c => c.value === 'yearly') ? 'yearly' : (cycles[0]?.value ?? 'yearly');
  }

  confirmPurchase(): void {
    if (!this.purchasePlan || this.busy) return;
    this.busy = true;
    this.purchaseError = null;
    this.apiService.createSubscriptionOrder(this.purchasePlan.id, this.selectedCycle)
      .pipe(takeUntil(this.destroy$)).subscribe({
        next: () => {
          this.busy = false;
          this.purchasePlan = null;
          this.loadOrders();
        },
        error: (err) => {
          this.busy = false;
          this.purchaseError = err?.error?.message || err?.error?.error
            || 'La commande n\'a pas pu être enregistrée. Réessayez ou contactez-nous.';
          this.cdr.detectChanges();
        }
      });
  }

  cancelPending(): void {
    const order = this.pendingOrder;
    if (!order || this.busy) return;
    if (!confirm('Annuler cette commande ?')) return;
    this.busy = true;
    this.apiService.cancelSubscriptionOrder(order.id)
      .pipe(takeUntil(this.destroy$)).subscribe({
        next: () => { this.busy = false; this.loadOrders(); },
        error: () => { this.busy = false; this.loadOrders(); }
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  /** Jours restants avant échéance, ou null si aucune échéance n'est posée. */
  get daysRemaining(): number | null {
    if (!this.expiresAt) return null;
    const ms = new Date(this.expiresAt).getTime() - Date.now();
    return Math.ceil(ms / 86400000);
  }

  get billingCycleLabel(): string {
    switch (this.billingCycle) {
      case 'monthly': return 'facturation mensuelle';
      case 'quarterly': return 'facturation trimestrielle';
      case 'yearly': return 'facturation annuelle';
      default: return '';
    }
  }

  /** Les quotas du plan, mis en regard de la consommation réelle. */
  get quotas(): Array<{ label: string; current: number; max: number; pct: number }> {
    if (!this.usage) return [];
    const build = (label: string, node: any) => {
      const current = node?.current ?? 0;
      const max = node?.max ?? 0;
      return { label, current, max, pct: max > 0 ? Math.min(100, (current / max) * 100) : 0 };
    };
    return [
      build('Véhicules', this.usage.vehicles),
      build('Utilisateurs', this.usage.users),
      build('Boîtiers GPS', this.usage.devices),
      build('Géofences', this.usage.geofences)
    ];
  }
}
