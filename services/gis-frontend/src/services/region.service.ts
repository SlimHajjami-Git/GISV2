import { Injectable } from '@angular/core';

export type Region = 'europe' | 'default';

/**
 * Détermine si le visiteur se trouve en Europe, pour décider quelle vitrine
 * publique lui présenter.
 *
 * <p><b>Pourquoi le fuseau horaire et non l'adresse IP.</b> Une géolocalisation
 * par IP est un traitement de donnée personnelle : il faudrait la déclarer, la
 * justifier et la faire reposer sur un prestataire tiers — sur le site même qui
 * promet de protéger les données. Le fuseau horaire est lu dans le navigateur,
 * ne sort jamais du poste, n'identifie personne et ne coûte aucun appel réseau.
 * Il sépare parfaitement <code>Europe/Paris</code> de <code>Africa/Tunis</code>,
 * qui est le seul cas qui nous occupe aujourd'hui.</p>
 *
 * <p><b>Ce que ce service ne fait PAS.</b> Il ne décide de rien une fois
 * l'utilisateur connecté. À partir de là, c'est le pays de la société qui fait
 * foi : sans quoi un client français en déplacement à Tunis verrait son
 * application changer d'apparence en cours de route.</p>
 */
@Injectable({ providedIn: 'root' })
export class RegionService {
  private static readonly OVERRIDE_KEY = 'calypso_region_override';

  private cached: Region | null = null;

  /**
   * Région du visiteur. Le résultat est mis en cache : la valeur ne peut pas
   * changer pendant une visite, et on évite de relire le fuseau à chaque appel.
   */
  get region(): Region {
    if (this.cached === null) {
      this.cached = this.resolve();
    }
    return this.cached;
  }

  get isEurope(): boolean {
    return this.region === 'europe';
  }

  private resolve(): Region {
    // 1. Forçage explicite — indispensable pour recetter les deux vitrines
    //    depuis n'importe où. `?region=europe` dans l'URL, mémorisé ensuite.
    const forced = this.readOverride();
    if (forced) return forced;

    // 2. Fuseau horaire du navigateur.
    const zone = this.timeZone();
    if (zone.startsWith('Europe/')) return 'europe';
    // Un fuseau africain ou asiatique tranche dans l'autre sens sans ambiguïté.
    if (zone.startsWith('Africa/') || zone.startsWith('Asia/')) return 'default';

    // 3. Repli sur la langue quand le fuseau ne dit rien d'utile (UTC, robots
    //    d'indexation, navigateurs verrouillés). `fr-TN` reste tunisien.
    const lang = (navigator.language || '').toLowerCase();
    if (lang.endsWith('-tn') || lang.endsWith('-dz') || lang.endsWith('-ma')) return 'default';
    if (lang.startsWith('fr') || lang.startsWith('de') || lang.startsWith('es')
        || lang.startsWith('it') || lang.startsWith('nl') || lang.startsWith('pt')) {
      return 'europe';
    }

    return 'default';
  }

  private timeZone(): string {
    try {
      return Intl.DateTimeFormat().resolvedOptions().timeZone || '';
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
      // Navigation privée ou stockage refusé : on retombe sur la détection.
    }
    return null;
  }
}
