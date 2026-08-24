import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * Page 6 — Politique de confidentialité.
 *
 * <p>Le corps décrit ce que le produit fait réellement des données, ce qui est
 * vérifiable. En revanche l'identité du responsable de traitement, ses
 * coordonnées et l'hébergeur sont laissés à compléter : une politique de
 * confidentialité est un document opposable, et y inscrire une raison sociale
 * inventée engagerait l'entreprise sur une fiction.</p>
 */
@Component({
  selector: 'app-france-privacy',
  standalone: true,
  imports: [RouterLink],
  template: `
    <section class="band-white tight">
      <div class="shell">
        <div class="legal">
          <h2>Politique de confidentialité</h2>
          <p class="legal-date">Document en préparation — non encore applicable.</p>

          <div class="legal-warn">
            <strong>À compléter avant publication.</strong> Identité du responsable
            de traitement (raison sociale, forme, siège), coordonnées du délégué à
            la protection des données le cas échéant, hébergeur et durées de
            conservation retenues. Ces éléments n'ont pas été inventés.
          </div>

          <p>Chez Calypso, nous accordons une grande importance à la protection de
             vos données personnelles. Cette politique explique quelles données
             nous collectons, pourquoi nous les collectons et comment nous les
             utilisons.</p>

          <h3>1. Données collectées</h3>
          <p>Nous collectons les données que vous nous fournissez directement
             lorsque vous créez un compte, utilisez le service ou nous contactez :</p>
          <ul>
            <li>identité et coordonnées professionnelles du compte (nom, courriel, téléphone) ;</li>
            <li>données de votre parc : véhicules, entretiens, réparations, pleins de carburant, dépenses et échéances ;</li>
            <li>données de position des véhicules, lorsque vous équipez ceux-ci d'un boîtier ;</li>
            <li>données techniques de connexion nécessaires à la sécurité du service.</li>
          </ul>

          <h3>2. Utilisation des données</h3>
          <p>Vos données sont utilisées pour fournir et améliorer le service,
             gérer votre compte, vous alerter sur les échéances et anomalies de
             votre parc, vous contacter si nécessaire, et respecter nos
             obligations légales. Elles ne servent pas à de la prospection pour
             le compte de tiers.</p>

          <h3>3. Partage des données</h3>
          <p>Nous ne vendons jamais vos données. Elles peuvent être partagées
             uniquement avec nos prestataires techniques agissant sur notre
             instruction, et avec les autorités légales lorsque la loi l'exige.</p>

          <h3>4. Vos droits</h3>
          <p>Conformément au RGPD, vous disposez des droits d'accès, de
             rectification, d'effacement, d'opposition, de limitation et de
             portabilité de vos données. Vous pouvez les exercer auprès du
             responsable de traitement dont les coordonnées figureront ci-dessus.</p>

          <h3>5. Conservation</h3>
          <p>Les données de votre parc sont conservées tant que votre compte est
             actif. Les durées précises de conservation après clôture restent à
             arrêter et seront indiquées ici.</p>

          <p style="margin-top:28px">
            Voir également nos <a routerLink="/fr/mentions-legales">mentions légales</a>.
          </p>
        </div>
      </div>
    </section>
  `
})
export class FrancePrivacyComponent {}
