import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * Page 17 — Politique de cookies.
 *
 * <p><b>Aucun outil, traceur ou fournisseur n'est listé.</b> La règle est
 * explicite : ne rien nommer qui ne soit réellement installé. Or l'inventaire
 * des cookies réellement posés par l'application n'a pas été fourni. La page
 * expose donc les principes et les catégories, et réserve la liste effective —
 * la remplir d'exemples plausibles reviendrait à publier une déclaration
 * fausse, ce qui est précisément ce qu'une politique de cookies doit éviter.</p>
 *
 * <p>Le mécanisme de consentement n'est pas non plus simulé : tant qu'aucun
 * cookie non nécessaire n'est confirmé, un bandeau proposant de « refuser »
 * quelque chose d'inexistant n'aurait aucun sens.</p>
 */
@Component({
  selector: 'app-france-cookies',
  standalone: true,
  imports: [RouterLink],
  template: `
    <section class="band-2 tight">
      <div class="shell">
        <div class="legal">
          <h2>Politique de cookies</h2>
          <p class="legal-date">Document en préparation — non encore applicable.</p>

          <div class="legal-warn">
            <strong>À compléter avant publication.</strong> L'inventaire des
            cookies réellement déposés par le site et l'application — nom,
            finalité, durée, catégorie — doit être fourni. Aucun outil ni
            fournisseur n'est listé ici tant que ce relevé n'a pas été établi :
            nommer un traceur qui n'est pas installé, ou en oublier un qui l'est,
            rendrait cette page fausse.
          </div>

          <h3>Qu'est-ce qu'un cookie ?</h3>
          <p>Un cookie est un petit fichier déposé sur votre appareil lors de la
             consultation d'un site. Il permet notamment de conserver votre
             session d'un écran à l'autre, ou de mémoriser une préférence
             d'affichage.</p>

          <h3>Catégories</h3>
          <p><strong>Cookies strictement nécessaires.</strong> Sans eux, le
             service ne peut pas fonctionner : maintien de la session après
             connexion, sécurité, mémorisation de vos préférences d'interface.
             Ils ne requièrent pas de consentement.</p>
          <p><strong>Autres catégories.</strong> Mesure d'audience, confort ou
             contenus tiers : elles ne sont utilisées que si elles sont
             réellement mises en place, et sont alors listées ci-dessus avec
             leur finalité.</p>

          <h3>Votre choix</h3>
          <p>Lorsque des cookies non strictement nécessaires sont utilisés, vous
             pouvez les accepter, les refuser ou les personnaliser, et modifier
             votre choix à tout moment.</p>
          <p>Votre navigateur permet également de bloquer ou supprimer les
             cookies déjà déposés ; bloquer les cookies nécessaires empêche
             toutefois la connexion au service.</p>

          <h3>En savoir plus</h3>
          <p>Le traitement des données personnelles est décrit dans notre
             <a routerLink="/fr/confidentialite">politique de confidentialité</a>
             et nos principes figurent sur la page
             <a routerLink="/fr/rgpd">RGPD</a>.</p>
        </div>
      </div>
    </section>
  `
})
export class FranceCookiesComponent {}
