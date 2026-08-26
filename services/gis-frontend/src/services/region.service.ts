import { Injectable } from '@angular/core';
import { environment } from '../environments/environment';

export type Region = 'europe' | 'default';

/**
 * Décide quelle vitrine publique présenter : la vitrine commerciale France ou
 * l'accueil habituel.
 *
 * <p><b>Le signal est le nom de domaine, et lui seul.</b> Une première version
 * se fiait au fuseau horaire du navigateur. C'était faux, et démontré en
 * production : la Tunisie est à UTC+1 et le sélecteur Windows met en tête
 * « (UTC+01:00) Bruxelles, Copenhague, Madrid, Paris ». Un poste tunisien
 * réglé ainsi se déclare <code>Europe/Paris</code> et recevait la vitrine
 * France. Le fuseau ne dit pas où se trouve le visiteur, il dit comment sa
 * machine a été configurée — ce n'est pas la même question.</p>
 *
 * <p><b>Second signal : le pays de l'adresse IP</b>, résolu par NOTRE API sur
 * une base locale (DB-IP Lite) — aucun tiers appelé, adresse ni conservée ni
 * journalisée, la politique de confidentialité le mentionne. La réponse (le
 * pays seul) est gardée pour la SESSION. Ce signal est demandé par la page
 * d'aiguillage au premier chargement ; s'il tarde ou échoue, l'accueil
 * habituel s'affiche — un Européen a toujours /fr en un clic, l'inverse
 * serait plus coûteux. Quand un domaine européen pointera ici, le domaine
 * (<code>europeanHostnames</code>) restera prioritaire et instantané.</p>
 *
 * <p><b>Hors de la page publique, ce service ne décide de rien.</b> Une fois
 * l'utilisateur connecté, c'est le pays de sa société qui fait foi : sans quoi
 * un client français en déplacement à Tunis verrait son application changer
 * d'apparence en cours de route.</p>
 */
@Injectable({ providedIn: 'root' })
export class RegionService {
  private static readonly OVERRIDE_KEY = 'calypso_region_override';

  private cached: Region | null = null;

  get region(): Region {
    if (this.cached === null) this.cached = this.resolve();
    return this.cached;
  }

  get isEurope(): boolean {
    return this.region === 'europe';
  }

  private static readonly VISIT_KEY = 'calypso_fr_visit';

  private resolve(): Region {
    // Forçage explicite, pour recetter les deux vitrines depuis n'importe où.
    const forced = this.readOverride();
    if (forced) return forced;

    const host = this.hostname();
    const listed = (environment.europeanHostnames ?? []).some(
      h => host === h.toLowerCase() || host.endsWith('.' + h.toLowerCase()));
    if (listed) return 'europe';

    // Pays resolu par l'API plus tot dans la session (voir resolveByCountry).
    const geo = this.readGeo();
    if (geo === 'europe') return 'europe';

    // Visite en cours du site France. Indispensable tant qu'aucun domaine
    // européen ne pointe ici : sans cela, un visiteur qui parcourt /fr et clique
    // « Essayer gratuitement » atterrirait sur l'inscription tunisienne, et le
    // parcours se casserait au milieu. La marque est de SESSION — elle disparaît
    // à la fermeture de l'onglet et n'engage rien pour la visite suivante.
    return this.visitedFranceSite() ? 'europe' : 'default';
  }

  private static readonly GEO_KEY = 'calypso_region_geo';

  /**
   * Interroge l'API pour connaître la région du pays de l'appelant, et met le
   * résultat en cache de SESSION. Appelée par la page d'aiguillage uniquement.
   * Ne lève jamais : en cas d'échec ou de délai dépassé, la région courante
   * (donc « default », sauf autre signal) est renvoyée telle quelle.
   */
  async resolveByCountry(timeoutMs = 600): Promise<Region> {
    if (this.region === 'europe') return 'europe';
    if (this.readGeo() !== null) return this.region;
    try {
      const rep = await fetch(environment.apiUrl + '/public/region',
        { signal: AbortSignal.timeout(timeoutMs) });
      if (!rep.ok) return this.region;
      const data = await rep.json() as { region?: string };
      const region: Region = data.region === 'europe' ? 'europe' : 'default';
      try { sessionStorage.setItem(RegionService.GEO_KEY, region); } catch {}
      this.cached = null; // la prochaine lecture retraverse resolve()
      return region;
    } catch {
      return this.region;
    }
  }

  private readGeo(): Region | null {
    try {
      const v = sessionStorage.getItem(RegionService.GEO_KEY);
      return v === 'europe' || v === 'default' ? v : null;
    } catch {
      return null;
    }
  }

  /**
   * Mémorise que le visiteur est entré sur le site France. Appelé par la coque
   * de ce site, pas ailleurs.
   */
  markFranceVisit(): void {
    try {
      sessionStorage.setItem(RegionService.VISIT_KEY, '1');
      // Le cache doit suivre, sinon la valeur calculée avant l'entrée sur le
      // site resterait « default » pour toute la session.
      this.cached = 'europe';
    } catch {
      // Stockage refusé : on retombe sur le domaine, sans casser la navigation.
    }
  }

  private visitedFranceSite(): boolean {
    try {
      return sessionStorage.getItem(RegionService.VISIT_KEY) === '1';
    } catch {
      return false;
    }
  }

  private hostname(): string {
    try {
      return (window.location.hostname || '').toLowerCase();
    } catch {
      return '';
    }
  }

  /** Lit le forçage dans l'URL, puis dans le stockage local. */
  private readOverride(): Region | null {
    try {
      const fromUrl = new URLSearchParams(window.location.search).get('region');
      if (fromUrl === 'europe' || fromUrl === 'default') {
        localStorage.setItem(RegionService.OVERRIDE_KEY, fromUrl);
        return fromUrl;
      }
      const stored = localStorage.getItem(RegionService.OVERRIDE_KEY);
      if (stored === 'europe' || stored === 'default') return stored;
    } catch {
      // Navigation privée ou stockage refusé : on retombe sur le domaine.
    }
    return null;
  }
}
