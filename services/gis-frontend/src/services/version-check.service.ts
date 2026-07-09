import { Injectable, OnDestroy } from '@angular/core';
import { BehaviorSubject, Subscription, timer } from 'rxjs';

/**
 * Détecte les nouveaux déploiements du frontend pour les onglets restés
 * ouverts longtemps : index.html est servi en no-cache et référence le bundle
 * haché (main-XXXX.js). Quand le hash servi diffère de celui du bundle en
 * cours d'exécution, une nouvelle version est en ligne → app-layout affiche
 * un bandeau « Actualiser ». Évite que les utilisateurs restent des jours sur
 * un vieux bundle sans jamais faire Ctrl+Shift+R après un déploiement.
 *
 * Vérifie toutes les 10 minutes + à chaque retour sur l'onglet.
 * Le rechargement ne touche pas le localStorage : la session reste ouverte.
 */
@Injectable({ providedIn: 'root' })
export class VersionCheckService implements OnDestroy {
  private static readonly CHECK_INTERVAL_MS = 10 * 60 * 1000;

  /** true dès qu'un bundle plus récent est disponible côté serveur. */
  public newVersionAvailable$ = new BehaviorSubject<boolean>(false);

  private sub: Subscription | null = null;
  private started = false;
  private readonly onVisible = () => {
    if (document.visibilityState === 'visible') this.check();
  };

  /** Appelé par app-layout au démarrage. Idempotent. */
  start(): void {
    if (this.started) return;
    this.started = true;
    this.sub = timer(VersionCheckService.CHECK_INTERVAL_MS, VersionCheckService.CHECK_INTERVAL_MS)
      .subscribe(() => this.check());
    document.addEventListener('visibilitychange', this.onVisible);
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
    document.removeEventListener('visibilitychange', this.onVisible);
  }

  reloadNow(): void {
    window.location.reload();
  }

  /** Hash du bundle actuellement chargé dans CET onglet. */
  private currentBundle(): string | null {
    const script = document.querySelector('script[src*="main-"]') as HTMLScriptElement | null;
    const m = script?.src?.match(/main-[A-Z0-9]+\.js/i);
    return m ? m[0] : null;
  }

  private async check(): Promise<void> {
    if (this.newVersionAvailable$.value) return;   // déjà signalé
    const current = this.currentBundle();
    if (!current) return;                          // dev server / structure inattendue
    try {
      const res = await fetch('/index.html', { cache: 'no-store' });
      if (!res.ok) return;
      const html = await res.text();
      const served = html.match(/main-[A-Z0-9]+\.js/i);
      if (served && served[0].toLowerCase() !== current.toLowerCase()) {
        this.newVersionAvailable$.next(true);
      }
    } catch {
      // Hors-ligne / erreur réseau → nouvel essai au prochain cycle.
    }
  }
}
