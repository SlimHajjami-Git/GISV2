import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * Page 7 — Mentions légales.
 *
 * <p>Les mentions légales sont obligatoires et opposables : elles identifient
 * l'éditeur et l'hébergeur. La maquette porte une raison sociale, un capital,
 * un RCS et une adresse de remplissage ; les reprendre reviendrait à publier
 * une fausse identité d'entreprise. Les rubriques qui ne dépendent pas de
 * cette identité sont rédigées, les autres sont explicitement en attente.</p>
 */
@Component({
  selector: 'app-france-legal',
  standalone: true,
  imports: [RouterLink],
  template: `
    <section class="band-2 tight">
      <div class="shell">
        <div class="legal">
          <h2>Mentions légales</h2>
          <p class="legal-date">Document en préparation — non encore applicable.</p>

          <div class="legal-warn">
            <strong>À compléter avant publication.</strong> Raison sociale, forme
            juridique et capital, adresse du siège, numéro RCS et TVA
            intracommunautaire, directeur de la publication, coordonnées de
            contact, ainsi que l'identité et l'adresse de l'hébergeur. Aucune de
            ces valeurs n'a été inventée ici.
          </div>

          <h3>Éditeur du site</h3>
          <p>Raison sociale, forme juridique, capital social, siège, RCS, TVA
             intracommunautaire et directeur de la publication : à communiquer.</p>

          <h3>Hébergement</h3>
          <p>Nom et adresse de l'hébergeur : à communiquer.</p>

          <h3>Propriété intellectuelle</h3>
          <p>L'ensemble du contenu présent sur ce site — textes, images, logos,
             marques et éléments d'interface — est protégé par le droit d'auteur
             et le droit des marques. Toute reproduction, représentation ou
             adaptation, totale ou partielle, est interdite sans autorisation
             écrite préalable.</p>

          <h3>Responsabilité</h3>
          <p>Les informations diffusées sur ce site sont fournies à titre
             indicatif et peuvent évoluer. L'éditeur s'efforce d'en assurer
             l'exactitude mais ne saurait être tenu responsable des erreurs
             éventuelles, d'une indisponibilité temporaire du service, ni de
             l'usage qui serait fait des informations mises à disposition.</p>

          <h3>Liens vers des sites tiers</h3>
          <p>Ce site peut renvoyer vers des sites tiers dont l'éditeur ne
             maîtrise pas le contenu et dont il ne saurait être tenu responsable.</p>

          <h3>Données personnelles</h3>
          <p>Le traitement des données personnelles est décrit dans notre
             <a routerLink="/fr/confidentialite">politique de confidentialité</a>.</p>

          <h3>Droit applicable</h3>
          <p>Les présentes mentions sont soumises au droit français.</p>
        </div>
      </div>
    </section>
  `
})
export class FranceLegalComponent {}
