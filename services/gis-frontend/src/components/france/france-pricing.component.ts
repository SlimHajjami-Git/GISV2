import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * Page 7 — Tarifs.
 *
 * <p><b>Deux périodicités, jamais trois.</b> Annuel 3 € et semestriel 4 € par
 * véhicule et par mois, exactement le même périmètre fonctionnel. Aucune
 * formule « Entreprise », aucun troisième palier, aucun tableau comparatif.</p>
 *
 * <p><b>Contradiction tranchée.</b> Le document maître écrit « Semestriel :
 * 2 € » alors que l'écran validé affiche 4 €. À 2 €, l'engagement le plus long
 * coûterait plus cher que le plus court et la mention « Économisez 25 % »
 * n'aurait plus de sens : 3 € est bien 25 % de moins que 4 €. C'est l'écran qui
 * fait foi ici, et la remise reste CALCULÉE à partir des deux prix pour qu'elle
 * ne puisse jamais les contredire.</p>
 */
@Component({
  selector: 'app-france-pricing',
  standalone: true,
  imports: [RouterLink],
  template: `
    <section class="band-glow tight">
      <div class="shell">
        <div class="sec-head rise">
          <h2>Des tarifs simples et transparents</h2>
          <p>Toutes les fonctionnalités Calypso sont incluses. Sans surprise.</p>
        </div>

        <div class="plans rise">
          <div class="plan reco">
            <div class="badge">RECOMMANDÉE</div>
            <div class="plan-top">
              <span class="plan-ic" aria-hidden="true">
                <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.8"><rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/></svg>
              </span>
              <span class="term">Annuel</span>
            </div>
            <div class="price">
              <span class="n">3 €</span>
              <span class="u">/ véhicule / mois</span>
            </div>
            <p class="sub">{{ 3 * 12 }} € / véhicule / an</p>
            <p class="pill-note accent">Économisez {{ savingPct }} %</p>
            <ul class="ticks">
              @for (f of features; track f) { <li>{{ f }}</li> }
            </ul>
            <a class="btn btn-grad plan-cta" routerLink="/inscription">
              <span>Essayer gratuitement</span>
              <small>Sans carte bancaire</small>
            </a>
          </div>

          <div class="plan">
            <div class="plan-top">
              <span class="plan-ic" aria-hidden="true">
                <svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.8"><path d="M21 12a9 9 0 1 1-3-6.7"/><path d="M21 4v5h-5"/></svg>
              </span>
              <span class="term">Semestriel</span>
            </div>
            <div class="price">
              <span class="n">4 €</span>
              <span class="u">/ véhicule / mois</span>
            </div>
            <p class="sub">{{ 4 * 6 }} € / véhicule / semestre</p>
            <p class="pill-note">Plus de flexibilité</p>
            <ul class="ticks">
              @for (f of features; track f) { <li>{{ f }}</li> }
            </ul>
            <a class="btn btn-line plan-cta" routerLink="/inscription">
              <span>Essayer gratuitement</span>
              <small>Sans carte bancaire</small>
            </a>
          </div>
        </div>

        <div class="reassure rise">
          <div class="re-item">
            <span class="re-ic" style="background:rgba(139,92,246,.16)">
              <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.8" aria-hidden="true"><path d="M5 15.5 8.5 19M4.5 19.5 9 15M14 4.5c3.5-1.5 6 1 4.5 4.5-1.2 2.8-4.6 6-8 8.5L7 14C9.5 10.6 11.2 5.7 14 4.5z"/></svg>
            </span>
            <div>
              <h3>Essayez Calypso gratuitement pendant {{ trialDays }} jours</h3>
              <p>Accédez à toutes les fonctionnalités. Sans carte bancaire. Sans engagement.</p>
            </div>
          </div>
          <div class="re-item">
            <span class="re-ic" style="background:rgba(52,211,153,.14)">
              <svg viewBox="0 0 24 24" fill="none" stroke="#34D399" stroke-width="1.8" aria-hidden="true"><path d="M12 3l7.5 3v6c0 4.4-3.1 7.6-7.5 9-4.4-1.4-7.5-4.6-7.5-9V6z"/><path d="m9 12 2 2 4-4.5"/></svg>
            </span>
            <div>
              <h3>Vos données sont sécurisées</h3>
              <p>Confidentialité garantie.</p>
            </div>
          </div>
        </div>
      </div>
    </section>

    <section class="band-2 tight">
      <div class="shell">
        <div class="sec-head rise">
          <h2>Questions fréquentes sur les tarifs</h2>
        </div>
        <div class="faq rise">
          <details>
            <summary>Quelle est la différence entre l'annuel et le semestriel ?</summary>
            <p class="ans">La durée d'engagement, rien d'autre. Le produit est
               identique dans les deux cas : c'est l'engagement plus long qui
               fait baisser le prix mensuel de 4 € à 3 € par véhicule.</p>
          </details>
          <details>
            <summary>Le prix dépend-il du nombre d'utilisateurs ?</summary>
            <p class="ans">Non. La facturation se fait au véhicule, et le nombre
               d'utilisateurs est illimité — un chef de parc, un comptable et un
               atelier peuvent travailler sur le même compte sans coût
               supplémentaire.</p>
          </details>
          <details>
            <summary>Y a-t-il des fonctionnalités réservées à une formule plus chère ?</summary>
            <p class="ans">Non, et c'est volontaire. Les deux périodicités donnent
               accès exactement au même produit. Vous ne choisissez qu'une durée.</p>
          </details>
          <details>
            <summary>Mes données m'appartiennent-elles ?</summary>
            <p class="ans">Oui. Elles restent votre propriété et sont exportables à
               tout moment en PDF ou en Excel. Voir notre
               <a routerLink="/fr/confidentialite">politique de confidentialité</a>.</p>
          </details>
        </div>
      </div>
    </section>

    <section class="band-glow">
      <div class="shell final">
        <h2>Prêt à simplifier la gestion de votre parc ?</h2>
        <a class="btn btn-grad" routerLink="/inscription">Essayer gratuitement</a>
      </div>
    </section>
  `
})
export class FrancePricingComponent {
  private static readonly ANNUAL = 3;
  private static readonly HALF_YEAR = 4;

  /** Durée annoncée de l'essai. Le document maître impose 7 jours. */
  readonly trialDays = 7;

  /**
   * Périmètre STRICTEMENT identique dans les deux périodicités — c'est la règle.
   * Sont volontairement absents « API et intégrations », « Application mobile »,
   * « Gestion multi-sociétés » et « Assistance en français » : le document
   * maître les retire de la liste commerciale publique.
   */
  readonly features = [
    'Entretiens & échéances',
    'Réparations',
    'Carburant & consommation',
    'Dépenses & budget',
    'Tableau de bord & pilotage',
    'Intelligence Calypso',
    'Toutes les fonctionnalités Calypso',
    'Mises à jour incluses',
    '7 jours gratuits'
  ];

  /**
   * Remise affichée. Calculée, jamais saisie : un taux écrit à la main finit
   * toujours par contredire les montants le jour où l'un des deux bouge.
   */
  get savingPct(): number {
    return Math.round(
      (1 - FrancePricingComponent.ANNUAL / FrancePricingComponent.HALF_YEAR) * 100);
  }
}
