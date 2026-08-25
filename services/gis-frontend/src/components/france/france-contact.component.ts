import { Component, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
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
 * <p>Le formulaire transmet réellement, via <c>POST /api/contact</c>. Le
 * destinataire est une configuration serveur et n'est jamais exposé au
 * navigateur — c'est ce qui permet au formulaire d'être le seul canal public
 * sans qu'aucune adresse n'apparaisse.</p>
 *
 * <p>L'écran « Message envoyé ! » n'apparaît QUE sur un succès réel. Un
 * formulaire qui confirme sans transmettre fait perdre des demandes clients en
 * silence, et personne ne s'en aperçoit avant des semaines : tant que le
 * destinataire n'est pas configuré, le serveur répond une erreur visible et
 * l'écran la montre.</p>
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
                    [disabled]="loading || !valid()">
              {{ loading ? 'Envoi…' : 'Envoyer le message' }}
            </button>

            @if (erreur) {
              <p class="legal-warn" style="margin-top:18px" role="status">{{ erreur }}</p>
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
  private readonly http = inject(HttpClient);

  nom = '';
  email = '';
  telephone = '';
  sujet = '';
  message = '';

  /** Vrai pendant l envoi : le bouton se verrouille pour eviter un doublon. */
  loading = false;
  erreur = '';
  sent = false;

  readonly sujets = [
    'Demande d information',
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
    if (this.loading || !this.valid()) return;

    this.loading = true;
    this.erreur = '';

    this.http.post<{ success: boolean; message: string }>(
      `${environment.apiUrl}/contact`,
      {
        nom: this.nom.trim(),
        email: this.email.trim(),
        telephone: this.telephone.trim(),
        sujet: this.sujet,
        message: this.message.trim()
      }
    ).subscribe({
      // L ecran de confirmation n apparait QUE sur un succes reel : afficher
      // « Message envoye ! » sans transmission ferait perdre la demande en
      // silence, et personne ne s en apercevrait.
      next: () => { this.loading = false; this.sent = true; },
      error: (err) => {
        this.loading = false;
        this.erreur = err?.error?.message
          || "Votre message n a pas pu etre transmis. Reessayez dans un instant.";
      }
    });
  }
}
