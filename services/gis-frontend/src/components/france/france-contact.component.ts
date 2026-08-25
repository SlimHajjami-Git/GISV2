import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

/**
 * Page 9 — Contact.
 *
 * <p><b>Aucune coordonnée n'est affichée, et c'est une règle, pas un oubli.</b>
 * Le document maître impose que le formulaire soit le seul canal public et
 * interdit d'afficher téléphone, courriel, adresse ou identité de l'éditeur.
 * Une version antérieure de cette page portait un bloc « à communiquer » :
 * il est supprimé — l'emplacement lui-même n'a pas lieu d'être.</p>
 *
 * <p>Le formulaire n'est pas encore relié : aucun point d'entrée de contact
 * n'existe côté API. Un formulaire qui affiche « message envoyé » sans rien
 * envoyer est pire que pas de formulaire du tout — il fait perdre des demandes
 * clients en silence. L'écran de confirmation prévu par la spécification n'est
 * donc montré que lorsqu'un envoi a réellement abouti.</p>
 */
@Component({
  selector: 'app-france-contact',
  standalone: true,
  imports: [FormsModule, RouterLink],
  template: `
    <section class="band-glow tight">
      <div class="shell">
        @if (!sent) {
          <div class="sec-head rise">
            <h2>Contactez-nous</h2>
            <p>Une question, un besoin spécifique ? Notre équipe est là pour vous aider.</p>
          </div>

          <form class="contact-form solo rise" (ngSubmit)="submit($event)">
            <label for="c-nom">Nom</label>
            <input id="c-nom" name="nom" type="text" [(ngModel)]="nom"
                   placeholder="Votre nom" autocomplete="name" required>

            <label for="c-mail">E-mail</label>
            <input id="c-mail" name="email" type="email" [(ngModel)]="email"
                   placeholder="vous@exemple.com" autocomplete="email" required>

            <label for="c-tel">Téléphone</label>
            <input id="c-tel" name="telephone" type="tel" [(ngModel)]="telephone"
                   placeholder="Votre numéro" autocomplete="tel" required>

            <label for="c-sujet">Sujet</label>
            <div class="select-wrap">
              <select id="c-sujet" name="sujet" [(ngModel)]="sujet" required>
                <option value="" disabled>Choisissez un sujet</option>
                @for (s of sujets; track s) {
                  <option [value]="s">{{ s }}</option>
                }
              </select>
            </div>

            <label for="c-msg">Message</label>
            <textarea id="c-msg" name="message" rows="6" [(ngModel)]="message"
                      placeholder="Votre message…"></textarea>

            <button class="btn btn-grad" type="submit" style="width:100%"
                    [disabled]="!valid()">Envoyer le message</button>

            @if (attempted) {
              <p class="legal-warn" style="margin-top:18px" role="status">
                L'envoi n'est pas encore raccordé : aucune boîte de réception n'a
                été définie pour ce site. Votre message n'a donc pas été transmis.
              </p>
            }
          </form>
        } @else {
          <div class="sent-state rise">
            <div class="sent-ic" aria-hidden="true">
              <svg viewBox="0 0 24 24" fill="none" stroke="#34D399" stroke-width="2"
                   stroke-linecap="round" stroke-linejoin="round">
                <circle cx="12" cy="12" r="9"/><path d="m8.5 12 2.5 2.5 4.5-5"/>
              </svg>
            </div>
            <h2>Message envoyé !</h2>
            <p>
              Merci, votre demande a bien été transmise. Notre équipe vous
              répondra dans les meilleurs délais.
            </p>
            <a class="btn btn-grad" routerLink="/fr">Retour à l'accueil</a>
          </div>
        }
      </div>
    </section>
  `
})
export class FranceContactComponent {
  nom = '';
  email = '';
  telephone = '';
  sujet = '';
  message = '';

  attempted = false;
  sent = false;

  /** Sujets proposés. Aucun ne suppose un pays ni un canal direct. */
  readonly sujets = [
    'Demande d\'information',
    'Demande de démonstration',
    'Question sur les tarifs',
    'Support technique',
    'Partenariat',
    'Autre'
  ];

  valid(): boolean {
    return this.nom.trim().length > 0
      && this.email.trim().length > 0
      && this.telephone.trim().length >= 6
      && this.sujet.length > 0;
  }

  submit(event: Event): void {
    event.preventDefault();
    // Tant qu'aucune destination n'existe, on le dit plutôt que de basculer
    // sur l'écran « Message envoyé ! » — qui affirmerait une transmission
    // n'ayant pas eu lieu.
    this.attempted = true;
  }
}
