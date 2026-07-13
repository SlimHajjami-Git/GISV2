import { Injectable, OnDestroy } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { BehaviorSubject, Subscription, timer } from 'rxjs';
import { environment } from '../environments/environment';

/** Réponse de GET /api/subscriptions/banner (voir SubscriptionPolicy côté API). */
export interface SubscriptionBanner {
  level: 'none' | 'warning' | 'danger' | 'blocked';
  reason: 'active' | 'expiring' | 'grace' | 'expired' | 'suspended' | 'cancelled';
  expiresAt: string | null;
  daysRemaining: number | null;
  graceDaysLeft: number | null;
}

/**
 * Surveille l'état de l'abonnement de la société pour la bannière d'app-layout :
 *  - warning (J-30)  → bannière ambre, visible par les admins de société ;
 *  - danger  (J-7 ou grâce après expiration) → bannière rouge, tout le monde ;
 *  - blocked (suspendu / grâce écoulée) → redirection vers l'écran de blocage.
 * Même rythme que VersionCheckService : poll 10 min + retour d'onglet, et
 * refresh() immédiat quand SignalR pousse « SubscriptionChanged ».
 */
@Injectable({ providedIn: 'root' })
export class SubscriptionStatusService implements OnDestroy {
  private static readonly CHECK_INTERVAL_MS = 10 * 60 * 1000;

  public banner$ = new BehaviorSubject<SubscriptionBanner | null>(null);

  private sub: Subscription | null = null;
  private started = false;
  private readonly onVisible = () => {
    if (document.visibilityState === 'visible') this.refresh();
  };

  constructor(private http: HttpClient, private router: Router) {}

  /** Appelé par app-layout au démarrage. Idempotent. */
  start(): void {
    if (this.started) return;
    this.started = true;
    this.refresh();
    this.sub = timer(SubscriptionStatusService.CHECK_INTERVAL_MS, SubscriptionStatusService.CHECK_INTERVAL_MS)
      .subscribe(() => this.refresh());
    document.addEventListener('visibilitychange', this.onVisible);
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
    document.removeEventListener('visibilitychange', this.onVisible);
  }

  refresh(): void {
    if (!localStorage.getItem('auth_token')) return;   // pas connecté → rien à vérifier
    this.http.get<SubscriptionBanner>(`${environment.apiUrl}/subscriptions/banner`).subscribe({
      next: (b) => {
        this.banner$.next(b);
        // Bloqué (suspension sys_admin ou grâce écoulée) : écran dédié.
        if (b?.level === 'blocked' && !this.router.url.startsWith('/abonnement-suspendu')) {
          this.router.navigate(['/abonnement-suspendu'], { queryParams: { reason: b.reason } });
        }
      },
      error: () => { /* réseau/401 : nouvel essai au prochain cycle */ }
    });
  }
}
