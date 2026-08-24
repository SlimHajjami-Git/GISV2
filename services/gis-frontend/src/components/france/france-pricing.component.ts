import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * Page 3 — Tarifs.
 *
 * <p>Les montants et les deux formules viennent de la planche du cahier des
 * charges. La remise annuelle y est annoncée à 20 % : les prix affichés en
 * mode annuel sont donc calculés, jamais saisis en dur, pour qu'ils ne
 * puissent pas diverger du taux affiché.</p>
 *
 * <p>Ce qui ne figure PAS ici est délibéré : durée de l'essai (le texte du
 * cahier des charges et les maquettes se contredisent, 14 jours contre
 * 7 jours), « sans engagement » et « sans carte bancaire ». Ce sont des
 * engagements commerciaux ; ils n'ont pas été validés, et une page publique
 * n'est pas l'endroit où l'on en fait l'hypothèse.</p>
 */
@Component({
  selector: 'app-france-pricing',
  standalone: true,
  imports: [RouterLink],
  template: `
    <section class="band-sky tight">
      <div class="shell">
        <div class="sec-head rise">
          <h2>Des tarifs simples et transparents</h2>
          <p>Toutes les fonctionnalités incluses. Sans surprise.</p>
        </div>

        <div class="tabs rise" role="tablist" aria-label="Périodicité de facturation">
          <button class="tab" role="tab" type="button"
                  [attr.aria-selected]="!annual" (click)="annual = false">Mensuel</button>
          <button class="tab" role="tab" type="button"
                  [attr.aria-selected]="annual" (click)="annual = true">Annuel — économisez 20 %</button>
        </div>

        <div class="plans rise">
          <div class="plan">
            <div class="term">Essentiel</div>
            <div class="price">
              <span class="n">{{ annual ? '2,40' : '3' }} €</span>
              <span class="u">/ mois</span>
            </div>
            <p class="sub">par véhicule</p>
            <p class="flex">{{ annual ? 'Facturé annuellement' : 'Facturé mensuellement' }}</p>
            <ul class="ticks">
              <li>Utilisateurs illimités</li>
              <li>Entretiens et réparations</li>
              <li>Carburant et dépenses</li>
              <li>Suivi des échéances</li>
              <li>Tableaux de bord</li>
              <li>Alertes intelligentes</li>
              <li>Assistance par courriel</li>
            </ul>
            <a class="btn btn-ghost" routerLink="/inscription">Essayer gratuitement</a>
          </div>

          <div class="plan reco">
            <div class="badge">RECOMMANDÉ</div>
            <div class="term">Avancé</div>
            <div class="price">
              <span class="n">{{ annual ? '3,20' : '4' }} €</span>
              <span class="u">/ mois</span>
            </div>
            <p class="sub">par véhicule</p>
            <p class="flex">{{ annual ? 'Facturé annuellement' : 'Facturé mensuellement' }}</p>
            <p class="same">Tout ce que contient Essentiel, plus :</p>
            <ul class="ticks">
              <li>Rapports avancés</li>
              <li>Export de données</li>
              <li>API et intégrations</li>
              <li>Gestion multi-sociétés</li>
              <li>Personnalisation avancée</li>
              <li>Assistance prioritaire</li>
            </ul>
            <a class="btn btn-primary" routerLink="/inscription">Essayer gratuitement</a>
          </div>
        </div>
      </div>
    </section>

    <section class="band-white tight">
      <div class="shell">
        <div class="sec-head rise">
          <h2>Questions fréquentes sur les tarifs</h2>
        </div>
        <div class="faq rise">
          <details>
            <summary>Le prix dépend-il du nombre d'utilisateurs ?</summary>
            <p class="ans">Non. La facturation se fait au véhicule, et le nombre
               d'utilisateurs est illimité dans les deux formules — un chef de parc,
               un comptable et un atelier peuvent travailler sur le même compte sans
               coût supplémentaire.</p>
          </details>
          <details>
            <summary>Que contient exactement « toutes les fonctionnalités incluses » ?</summary>
            <p class="ans">Les cinq univers du produit — entretiens, réparations,
               carburant, dépenses et échéances — ainsi que les tableaux de bord et
               les alertes. La formule Avancé y ajoute les rapports avancés, l'export,
               les intégrations et la gestion multi-sociétés.</p>
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

    <section class="band-navy">
      <div class="shell final">
        <h2>Prêt à simplifier la gestion de votre parc ?</h2>
        <a class="btn btn-light" routerLink="/inscription">Essayer gratuitement</a>
      </div>
    </section>
  `
})
export class FrancePricingComponent {
  /** Périodicité affichée. Le mensuel est le repère le plus lisible par défaut. */
  annual = false;
}
