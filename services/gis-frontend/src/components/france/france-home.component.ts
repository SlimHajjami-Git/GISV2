import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * Page 5 — Accueil, d'après l'écran validé.
 *
 * <p>Huit blocs dans l'ordre de la maquette : hero, bandeau des univers,
 * produit réel, anticipation, aperçu tarifaire, mise en route, appel à
 * l'action. Le diagramme du hero porte des <b>étiquettes</b> et non des
 * chiffres : des valeurs inventées y feraient passer une illustration pour une
 * capture, ce que le document maître interdit.</p>
 *
 * <p>Aucun bloc Géozones : la règle est explicite, ce site ne présente pas
 * Calypso comme une offre de géolocalisation.</p>
 */
@Component({
  selector: 'app-france-home',
  standalone: true,
  imports: [RouterLink],
  template: `
    <!-- ═══ 1 · HERO ═══ -->
    <section class="hero band-glow">
      <div class="shell hero-grid">
        <div>
          <span class="pill">Logiciel de gestion de parc automobile</span>
          <h1>
            Gérez votre parc.<br>
            Maîtrisez vos coûts.<br>
            <span class="grad-txt">Anticipez</span> vos entretiens.
          </h1>
          <p class="lede">
            Calypso centralise vos véhicules, entretiens, réparations, carburant,
            dépenses et échéances dans une seule plateforme intelligente.
          </p>
          <p class="ai-line">
            <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="M12 3l1.9 5.1L19 10l-5.1 1.9L12 17l-1.9-5.1L5 10l5.1-1.9z"/></svg>
            Intelligence artificielle intégrée
          </p>
          <div class="hero-cta">
            <a class="btn btn-grad" routerLink="/inscription">Essayer gratuitement</a>
            <a class="btn btn-line" href="#produit">Découvrir Calypso</a>
          </div>
          <ul class="badges">
            @for (b of reassurances; track b) {
              <li>
                <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2.2" aria-hidden="true"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>
                {{ b }}
              </li>
            }
          </ul>
        </div>

        <!-- Diagramme : six univers autour de la marque. Des ETIQUETTES, pas des
             chiffres — une valeur inventée ferait passer ceci pour une capture. -->
        <div class="orbit" aria-label="Les six univers couverts par Calypso">
          <div class="rings" aria-hidden="true">
            <span class="halo"></span>
            <span class="ring r3"></span>
            <span class="ring r2"></span>
          </div>
          <div class="orbit-core">
            <img src="/assets/calypso-logo.svg" alt="Calypso" width="504" height="170">
          </div>
          <div class="o-node n-t">
            <span class="o-ic" style="border-color:rgba(96,165,250,.55);background:rgba(59,130,246,.14)"><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.8"><path d="M14.7 6.3a4 4 0 0 0 4.6 5.7l-8.3 8.3a2 2 0 0 1-2.8-2.8l8.3-8.3a4 4 0 0 0-1.8-2.9z"/></svg></span>
            <span class="o-lbl">ENTRETIEN</span>
          </div>
          <div class="o-node n-tr">
            <span class="o-ic" style="border-color:rgba(52,211,153,.55);background:rgba(52,211,153,.14)"><svg viewBox="0 0 24 24" fill="none" stroke="#34D399" stroke-width="1.8"><path d="M4 20V5a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v15M3 20h13M17 9l2.5 2.5V17a1.5 1.5 0 0 1-3 0V9z"/></svg></span>
            <span class="o-lbl">CARBURANT</span>
          </div>
          <div class="o-node n-br">
            <span class="o-ic" style="border-color:rgba(167,139,250,.55);background:rgba(139,92,246,.16)"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.8"><circle cx="12" cy="12" r="3.2"/><path d="M12 2.5v3M12 18.5v3M2.5 12h3M18.5 12h3M5.3 5.3l2.1 2.1M16.6 16.6l2.1 2.1M18.7 5.3l-2.1 2.1M7.4 16.6l-2.1 2.1"/></svg></span>
            <span class="o-lbl">RÉPARATION</span>
          </div>
          <div class="o-node n-b">
            <span class="o-ic" style="border-color:rgba(96,165,250,.55);background:rgba(59,130,246,.14)"><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.8"><rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/></svg></span>
            <span class="o-lbl">ÉCHÉANCE</span>
          </div>
          <div class="o-node n-bl">
            <span class="o-ic" style="border-color:rgba(236,72,153,.5);background:rgba(236,72,153,.14)"><svg viewBox="0 0 24 24" fill="none" stroke="#EC4899" stroke-width="1.8"><path d="M9 4a3 3 0 0 0-3 3 3 3 0 0 0-1 5.8A3 3 0 0 0 8 18a3 3 0 0 0 4-1V4.9A3 3 0 0 0 9 4zM15 4a3 3 0 0 1 3 3 3 3 0 0 1 1 5.8A3 3 0 0 1 16 18a3 3 0 0 1-4-1"/></svg></span>
            <span class="o-lbl">IA</span>
          </div>
          <div class="o-node n-tl">
            <span class="o-ic" style="border-color:rgba(129,140,248,.55);background:rgba(99,102,241,.16)"><svg viewBox="0 0 24 24" fill="none" stroke="#818CF8" stroke-width="1.8"><path d="M3 13h18M5 13l1.6-5.2A2 2 0 0 1 8.5 6.4h7a2 2 0 0 1 1.9 1.4L19 13v5h-2.2v-2H7.2v2H5z"/><circle cx="7.8" cy="16" r="1.1"/><circle cx="16.2" cy="16" r="1.1"/></svg></span>
            <span class="o-lbl">PARC</span>
          </div>
        </div>
      </div>

      <!-- ═══ 2 · BANDEAU DES UNIVERS ═══ -->
      <div class="shell">
        <div class="strip">
            <a routerLink="/fr/fonctionnalites">
              <svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.8"><path d="M14.7 6.3a4 4 0 0 0 4.6 5.7l-8.3 8.3a2 2 0 0 1-2.8-2.8l8.3-8.3a4 4 0 0 0-1.8-2.9z"/></svg>
              <span>Entretiens</span>
            </a>
            <a routerLink="/fr/fonctionnalites">
              <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.8"><circle cx="12" cy="12" r="3.2"/><path d="M12 2.5v3M12 18.5v3M2.5 12h3M18.5 12h3M5.3 5.3l2.1 2.1M16.6 16.6l2.1 2.1M18.7 5.3l-2.1 2.1M7.4 16.6l-2.1 2.1"/></svg>
              <span>Réparations</span>
            </a>
            <a routerLink="/fr/fonctionnalites">
              <svg viewBox="0 0 24 24" fill="none" stroke="#34D399" stroke-width="1.8"><path d="M4 20V5a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v15M3 20h13M17 9l2.5 2.5V17a1.5 1.5 0 0 1-3 0V9z"/></svg>
              <span>Carburant</span>
            </a>
            <a routerLink="/fr/fonctionnalites">
              <svg viewBox="0 0 24 24" fill="none" stroke="#818CF8" stroke-width="1.8"><path d="M12 2v20M17 6H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/></svg>
              <span>Dépenses</span>
            </a>
            <a routerLink="/fr/fonctionnalites">
              <svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.8"><rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/></svg>
              <span>Échéances</span>
            </a>
            <a routerLink="/fr/fonctionnalites">
              <svg viewBox="0 0 24 24" fill="none" stroke="#EC4899" stroke-width="1.8"><path d="M9 4a3 3 0 0 0-3 3 3 3 0 0 0-1 5.8A3 3 0 0 0 8 18a3 3 0 0 0 4-1V4.9A3 3 0 0 0 9 4zM15 4a3 3 0 0 1 3 3 3 3 0 0 1 1 5.8A3 3 0 0 1 16 18a3 3 0 0 1-4-1"/></svg>
              <span>IA</span>
            </a>
        </div>
      </div>
    </section>

    <!-- ═══ 3 · PRODUIT RÉEL ═══ -->
    <section id="produit" class="band-2 tight">
      <div class="shell">
        <div class="feature-split rise">
          <div>
            <h2>Tout votre parc.<br>Un seul endroit.</h2>
            <p>Découvrez Calypso à travers son interface réelle.</p>
          </div>
          <div class="shot">
            <img src="/assets/france/produit-tableau-de-bord-sombre.webp" width="1708" height="921"
                 alt="Tableau de bord Calypso : véhicules suivis, coût total, consommation,
                      alertes, répartition des coûts, entretiens et échéances à venir."
                 loading="eager">
          </div>
        </div>
      </div>
    </section>

    <!-- ═══ 4 · ANTICIPATION ═══ -->
    <section class="band-glow tight">
      <div class="shell">
        <div class="sec-head rise">
          <h2>Ne vous contentez plus de suivre. <span class="grad-txt">Anticipez.</span></h2>
          <p>
            Calypso analyse les données de votre parc pour vous aider à détecter
            les anomalies, anticiper les entretiens et mieux maîtriser vos coûts.
          </p>
        </div>
        <div class="cards rise">
          <div class="card">
            <div class="ic"><svg viewBox="0 0 24 24" width="21" height="21" fill="none" stroke="#60A5FA" stroke-width="1.8"><rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/></svg></div>
            <h3>Anticiper</h3>
            <p>Entretiens et échéances à venir, signalés avant qu'ils ne tombent.</p>
          </div>
          <div class="card">
            <div class="ic"><svg viewBox="0 0 24 24" width="21" height="21" fill="none" stroke="#F59E0B" stroke-width="1.8"><path d="M12 4.2 21 19H3z"/><path d="M12 10v4"/></svg></div>
            <h3>Détecter</h3>
            <p>Anomalies et situations nécessitant votre attention.</p>
          </div>
          <div class="card">
            <div class="ic"><svg viewBox="0 0 24 24" width="21" height="21" fill="none" stroke="#A78BFA" stroke-width="1.8"><path d="M4 20V9M10 20V4M16 20v-7M22 20H2"/></svg></div>
            <h3>Comprendre</h3>
            <p>Coûts, dépenses et consommation, remis en perspective.</p>
          </div>
        </div>
      </div>
    </section>

    <!-- ═══ 5 · APERÇU TARIFAIRE ═══ -->
    <section class="band-2 tight">
      <div class="shell">
        <div class="pricing-teaser rise">
          <div class="pt-lead">
            <h2>Une gestion complète.<br>Un tarif simple.</h2>
          </div>
          <div class="pt-price">
            <span class="pt-from">À partir de</span>
            <span class="pt-n">3 €</span>
            <span class="pt-u">/ véhicule / mois</span>
          </div>
          <p class="pt-txt">
            Toutes les fonctionnalités essentielles pour gérer votre parc,
            réunies dans une seule solution.
          </p>
          <ul class="pt-list">
            @for (b of reassurances; track b) {
              <li>
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2.4" aria-hidden="true"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>
                {{ b }}
              </li>
            }
          </ul>
          <div class="pt-cta">
            <a class="btn btn-grad" routerLink="/inscription">Essayer gratuitement</a>
            <a class="btn btn-line" routerLink="/fr/tarifs">Voir les tarifs</a>
          </div>
        </div>
      </div>
    </section>

    <!-- ═══ 6 · MISE EN ROUTE ═══ -->
    <section class="band-glow tight">
      <div class="shell">
        <div class="start-block rise">
          <h2>Commencez<br>simplement.</h2>
          <div class="start-grid">
            <div class="start-item">
              <span class="si-ic"><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.8"><rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/></svg></span>
              <h3>{{ trialDays }} jours gratuits</h3>
              <p>Découvrez Calypso avec votre propre parc.</p>
            </div>
            <div class="start-item">
              <span class="si-ic"><svg viewBox="0 0 24 24" fill="none" stroke="#818CF8" stroke-width="1.8"><rect x="2.5" y="5" width="19" height="14" rx="2.5"/><path d="M2.5 10h19"/></svg></span>
              <h3>Sans carte bancaire</h3>
              <p>Aucun moyen de paiement demandé pour commencer.</p>
            </div>
            <div class="start-item">
              <span class="si-ic"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.8"><path d="M5 15.5 8.5 19M4.5 19.5 9 15M14 4.5c3.5-1.5 6 1 4.5 4.5-1.2 2.8-4.6 6-8 8.5L7 14C9.5 10.6 11.2 5.7 14 4.5z"/></svg></span>
              <h3>Mise en route rapide</h3>
              <p>Créez votre compte et ajoutez vos véhicules.</p>
            </div>
            <div class="start-item">
              <span class="si-ic"><svg viewBox="0 0 24 24" fill="none" stroke="#34D399" stroke-width="1.8"><path d="M12 3l7.5 3v6c0 4.4-3.1 7.6-7.5 9-4.4-1.4-7.5-4.6-7.5-9V6z"/><path d="m9 12 2 2 4-4.5"/></svg></span>
              <h3>Vos données restent les vôtres</h3>
              <p>Vos données sont sécurisées et restent votre propriété.</p>
            </div>
          </div>
        </div>
      </div>
    </section>

    <!-- ═══ 7 · APPEL À L'ACTION ═══ -->
    <section class="band-2">
      <div class="shell">
        <div class="final-band rise">
          <div>
            <h2>Prêt à simplifier la gestion de votre parc ?</h2>
          </div>
          <p class="fb-txt">Découvrez Calypso gratuitement pendant {{ trialDays }} jours.</p>
          <div class="fb-cta">
            <a class="btn btn-grad" routerLink="/inscription">Essayer gratuitement</a>
            <small>Sans carte bancaire &nbsp;•&nbsp; Sans engagement</small>
          </div>
        </div>
      </div>
    </section>
  `
})
export class FranceHomeComponent {
  /** Durée annoncée sur le site France. Le document maître impose 7 jours. */
  readonly trialDays = 7;

  /** Les trois réassurances, écrites une seule fois et réutilisées. */
  readonly reassurances = ['7 jours gratuits', 'Sans carte bancaire', 'Sans engagement'];

}
