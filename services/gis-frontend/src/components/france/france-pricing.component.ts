import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * Page 3 — Tarifs.
 *
 * <p><b>Une seule offre, deux durées.</b> Les formules « Essentiel » et
 * « Avancé » de la maquette ont été abandonnées : le prix ne dépend plus du
 * périmètre fonctionnel mais de la durée d'engagement — 3 € par véhicule et
 * par mois en annuel, 4 € en semestriel. Tout le produit est inclus dans les
 * deux cas, ce qui rend la page beaucoup plus simple à lire et supprime la
 * question « qu'est-ce que je perds si je prends la moins chère ».</p>
 *
 * <p>La remise annoncée n'est pas saisie en dur : elle est calculée à partir
 * des deux prix, pour qu'elle ne puisse jamais diverger de ce qui est
 * réellement facturé.</p>
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
          <p>Toutes les fonctionnalités incluses. Sans surprise.</p>
        </div>

        <div class="tabs rise" role="tablist" aria-label="Durée d'engagement">
          <button class="tab" role="tab" type="button"
                  [attr.aria-selected]="annual" (click)="annual = true">
            Annuel — économisez {{ savingPct }} %
          </button>
          <button class="tab" role="tab" type="button"
                  [attr.aria-selected]="!annual" (click)="annual = false">
            Semestriel
          </button>
        </div>

        <div class="offer rise">
          <div class="plan reco">
            <div class="badge">TOUT INCLUS</div>
            <div class="term">{{ annual ? 'Engagement annuel' : 'Engagement semestriel' }}</div>
            <div class="price">
              <span class="n">{{ annual ? 3 : 4 }} €</span>
              <span class="u">/ mois</span>
            </div>
            <p class="sub">par véhicule</p>
            <p class="flex">
              {{ annual ? 'Facturé une fois par an' : 'Facturé tous les six mois' }}
            </p>

            <p class="same">Tout le produit, sans option ni palier :</p>
            <ul class="ticks two">
              <li>Utilisateurs illimités</li>
              <li>Entretiens et réparations</li>
              <li>Carburant et dépenses</li>
              <li>Suivi des échéances</li>
              <li>Tableaux de bord temps réel</li>
              <li>Alertes intelligentes</li>
              <li>Rapports avancés</li>
              <li>Export de données</li>
              <li>API et intégrations</li>
              <li>Gestion multi-sociétés</li>
              <li>Application mobile</li>
              <li>Assistance en français</li>
            </ul>
            <a class="btn btn-grad" routerLink="/inscription">Essayer gratuitement</a>
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
            <p class="ans">Non, et c'est volontaire. Rapports avancés, export,
               API, multi-sociétés : tout est inclus. Vous ne choisissez qu'une
               durée.</p>
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
  /** L'annuel est proposé par défaut : c'est l'offre la plus avantageuse. */
  annual = true;

  private static readonly ANNUAL = 3;
  private static readonly HALF_YEAR = 4;

  /**
   * Remise affichée sur l'onglet annuel. Calculée, jamais saisie : un taux
   * écrit en dur finit toujours par contredire les prix le jour où l'un des
   * deux bouge.
   */
  get savingPct(): number {
    const a = FrancePricingComponent.ANNUAL;
    const h = FrancePricingComponent.HALF_YEAR;
    return Math.round((1 - a / h) * 100);
  }
}
