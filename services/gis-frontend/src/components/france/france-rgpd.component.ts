import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * Page 16 — RGPD.
 *
 * <p>Centrée sur les principes, comme le demande le document maître :
 * transparence, finalité, sécurité, droits des personnes et conservation.
 * Aucun DPO n'est annoncé et aucune coordonnée n'est donnée — la règle est
 * explicite : pas de « Contact DPO » tant qu'aucun DPO ni aucune coordonnée
 * n'ont été officiellement fournis. L'exercice des droits passe donc par le
 * formulaire de contact, seul canal public.</p>
 */
@Component({
  selector: 'app-france-rgpd',
  standalone: true,
  imports: [RouterLink],
  template: `
    <section class="band-2 tight">
      <div class="shell">
        <div class="legal">
          <h2>RGPD</h2>
          <p class="legal-date">Document en préparation — non encore applicable.</p>

          <div class="legal-warn">
            <strong>À compléter avant publication.</strong> Identité du responsable
            de traitement et durées de conservation retenues. Ces éléments n'ont
            pas été inventés.
          </div>

          <p>Calypso traite des données personnelles dans le cadre de la gestion
             de parc automobile. Cette page présente les principes qui encadrent
             ces traitements.</p>

          <h3>Transparence</h3>
          <p>Vous êtes informé des catégories de données collectées et de
             l'usage qui en est fait. Le détail figure dans notre
             <a routerLink="/fr/confidentialite">politique de confidentialité</a>.</p>

          <h3>Finalités déterminées et légitimes</h3>
          <p>Les données servent à fournir le service : gérer votre compte, tenir
             l'historique de votre parc, vous alerter sur les échéances et les
             anomalies, et assurer la sécurité de la plateforme. Elles ne sont pas
             réutilisées à d'autres fins.</p>

          <h3>Sécurité</h3>
          <p>Des mesures techniques et organisationnelles adaptées protègent les
             données contre l'accès non autorisé, l'altération et la perte.</p>

          <h3>Vos droits</h3>
          <p>Vous disposez, dans les conditions prévues par la réglementation,
             des droits d'accès, de rectification, d'effacement, d'opposition, de
             limitation et de portabilité.</p>
          <p>Pour les exercer, adressez votre demande via le
             <a routerLink="/fr/contact">formulaire de contact</a>.</p>

          <h3>Conservation</h3>
          <p>Les données sont conservées le temps nécessaire aux finalités
             ci-dessus et aux obligations applicables. Les durées précises seront
             indiquées ici une fois arrêtées.</p>
        </div>
      </div>
    </section>
  `
})
export class FranceRgpdComponent {}
