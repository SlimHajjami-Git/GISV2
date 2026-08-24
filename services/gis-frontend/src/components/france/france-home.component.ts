import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/** Page 1 — Accueil. Reprend la planche : hero clair, univers, bandeau nuit. */
@Component({
  selector: 'app-france-home',
  standalone: true,
  imports: [RouterLink],
  template: `
    <section class="hero band-sky">
      <div class="shell hero-grid">
        <div>
          <h1>
            Gérez votre parc.<br>
            <span class="accent">Maîtrisez vos coûts.</span><br>
            Anticipez vos entretiens.
          </h1>
          <p class="lede">
            Calypso centralise entretiens, réparations, carburant, dépenses et
            échéances dans une plateforme unique, pensée pour les gestionnaires
            de flotte.
          </p>
          <p class="incl">Toutes les fonctionnalités incluses.</p>
          <div class="hero-cta">
            <a class="btn btn-primary" routerLink="/inscription">Essayer gratuitement</a>
            <a class="btn btn-ghost" routerLink="/fr/fonctionnalites">Voir les fonctionnalités</a>
          </div>
          <p class="hero-note">
            <span>Mise en service immédiate</span>
            <span class="dot"></span>
            <span>Assistance en français</span>
          </p>
        </div>
        <div class="shot rise">
          <img src="/assets/france/produit-tableau-de-bord-clair.webp" width="1536" height="1024"
               alt="Tableau de bord Calypso : véhicules en circulation, consommation de
                    carburant, dépenses par catégorie et alertes récentes." loading="eager">
        </div>
      </div>
    </section>

    <section class="band-white tight">
      <div class="shell">
        <div class="sec-head rise">
          <h2>Tout votre parc. Un seul endroit.</h2>
          <p>Cinq univers qui couvrent le quotidien d'un gestionnaire de flotte.</p>
        </div>
        <div class="univers rise">
          <div class="uni">
            <div class="ic">
              <svg width="21" height="21" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="2" aria-hidden="true"><path d="M14.7 6.3a4 4 0 0 0 4.6 5.7l-8.3 8.3a2 2 0 0 1-2.8-2.8l8.3-8.3a4 4 0 0 0-1.8-2.9z"/></svg>
            </div>
            <h3>Entretiens</h3>
            <p>Planifiez et suivez chaque révision, sans rien laisser passer.</p>
          </div>
          <div class="uni">
            <div class="ic">
              <svg width="21" height="21" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="2" aria-hidden="true"><path d="M3 13h18M5 13l1.5-5A2 2 0 0 1 8.4 6.5h7.2a2 2 0 0 1 1.9 1.5L19 13v5h-2v-2H7v2H5z"/></svg>
            </div>
            <h3>Réparations</h3>
            <p>Pièces, main-d'œuvre et garanties conservées par véhicule.</p>
          </div>
          <div class="uni">
            <div class="ic">
              <svg width="21" height="21" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="2" aria-hidden="true"><path d="M4 20V5a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v15M3 20h13M17 9l2.5 2.5V17a1.5 1.5 0 0 1-3 0V9z"/></svg>
            </div>
            <h3>Carburant</h3>
            <p>Consommation réelle, prix au litre et dérives détectées.</p>
          </div>
          <div class="uni">
            <div class="ic">
              <svg width="21" height="21" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="2" aria-hidden="true"><path d="M12 2v20M17 6H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/></svg>
            </div>
            <h3>Dépenses</h3>
            <p>Toutes les charges du parc réunies et ventilées.</p>
          </div>
          <div class="uni">
            <div class="ic">
              <svg width="21" height="21" viewBox="0 0 24 24" fill="none" stroke="#1B4FD8" stroke-width="2" aria-hidden="true"><rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/></svg>
            </div>
            <h3>Échéances</h3>
            <p>Assurance, visite technique, vignette : jamais en retard.</p>
          </div>
        </div>
      </div>
    </section>

    <section class="band-navy">
      <div class="shell">
        <div class="sec-head rise">
          <h2>Ne vous contentez plus de suivre. Anticipez.</h2>
          <p>Calypso lit les données de votre flotte et vous prévient avant la panne.</p>
        </div>
        <div class="icards rise">
          <div class="icard">
            <div class="k">ALERTES</div>
            <h3>Alertes intelligentes</h3>
            <p>Une dérive de consommation, une échéance qui approche, un véhicule
               qui ne démarre plus : vous êtes prévenu, pas informé après coup.</p>
          </div>
          <div class="icard">
            <div class="k">TEMPS RÉEL</div>
            <h3>Tableaux de bord en temps réel</h3>
            <p>L'état du parc à l'instant présent, pas la photo d'hier soir.</p>
          </div>
          <div class="icard">
            <div class="k">ÉCONOMIES</div>
            <h3>Réduction des coûts</h3>
            <p>Les postes qui dérapent ressortent seuls, chiffres à l'appui.</p>
          </div>
        </div>
        <div class="final">
          <a class="btn btn-light" routerLink="/inscription">Essayer gratuitement</a>
        </div>
      </div>
    </section>
  `
})
export class FranceHomeComponent {}
