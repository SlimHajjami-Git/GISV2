import { Component, inject, ViewEncapsulation } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { FranceAuthComponent } from './france-auth.component';

/**
 * CONTACT du site europeen.
 *
 * L'etat « Message envoye ! » reproduit la capture CONTACT (1402 x 1122) :
 * cercle de validation, titre a seconde partie violette, carte centree avec
 * icone enveloppe, texte, point separateur et bouton « Retour a l'accueil ».
 *
 * Le formulaire lui-meme n'a PAS de capture : il applique le meme langage
 * (habillage d'authentification, champs fa-field) avec les champs du document
 * maitre. La liste deroulante du sujet est stylee comme les autres champs.
 *
 * L'ecran de confirmation n'apparait QUE sur un succes reel : afficher
 * « Message envoye ! » sans transmission ferait perdre la demande en silence.
 */
@Component({
  selector: 'app-france-contact',
  standalone: true,
  imports: [RouterLink, FormsModule, FranceAuthComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
    @if (!sent) {
      <app-france-auth>
        <div class="fa-badge">
          <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.6"><rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/></svg>
        </div>
        <h1 class="fa-title">Contactez<em>-nous</em></h1>
        <p class="fa-sub">
          Une question, une démonstration, un projet ?<br>
          Notre équipe vous répond dans les meilleurs délais.
        </p>

        <div class="fa-card">
          <form (ngSubmit)="submit($event)">
            <label for="c-nom">Nom et prénom <span class="req">*</span></label>
            <div class="fa-field">
              <svg class="pre" viewBox="0 0 24 24" fill="none" stroke="#6B7A94" stroke-width="1.8"><circle cx="12" cy="9" r="3.2"/><path d="M5.5 19.5a6.8 6.8 0 0 1 13 0"/></svg>
              <input id="c-nom" name="nom" type="text" [(ngModel)]="nom"
                     placeholder="Votre nom et prénom" autocomplete="name" required>
            </div>

            <div class="fa-grid2">
              <div>
                <label for="c-email">E-mail <span class="req">*</span></label>
                <div class="fa-field">
                  <svg class="pre" viewBox="0 0 24 24" fill="none" stroke="#6B7A94" stroke-width="1.8"><rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/></svg>
                  <input id="c-email" name="email" type="email" [(ngModel)]="email"
                         placeholder="exemple@domaine.com" autocomplete="email" required>
                </div>
              </div>
              <div>
                <label for="c-tel">Téléphone <span class="req">*</span></label>
                <div class="fa-field">
                  <svg class="pre" viewBox="0 0 24 24" fill="none" stroke="#6B7A94" stroke-width="1.8"><path d="M5 4h4l2 5-2.5 1.5a11 11 0 0 0 5 5L15 13l5 2v4a1 1 0 0 1-1 1A16 16 0 0 1 4 5a1 1 0 0 1 1-1z"/></svg>
                  <input id="c-tel" name="telephone" type="tel" [(ngModel)]="telephone"
                         placeholder="+33 6 12 34 56 78" autocomplete="tel" required>
                </div>
              </div>
            </div>

            <label for="c-sujet">Sujet <span class="req">*</span></label>
            <div class="fa-field">
              <svg class="pre" viewBox="0 0 24 24" fill="none" stroke="#6B7A94" stroke-width="1.8"><path d="M7 3h7l4 4v14H7z"/><path d="M14 3v4h4M10 12h5M10 16h5"/></svg>
              <select id="c-sujet" name="sujet" [(ngModel)]="sujet" required>
                <option value="" disabled>Choisissez un sujet</option>
                @for (s of sujets; track s) { <option [value]="s">{{ s }}</option> }
              </select>
              <span class="fa-chev"></span>
            </div>

            <label for="c-msg">Message</label>
            <div class="fa-field fa-area">
              <textarea id="c-msg" name="message" rows="5" [(ngModel)]="message"
                        placeholder="Décrivez votre besoin…"></textarea>
            </div>

            @if (erreur) { <div class="fa-error">{{ erreur }}</div> }

            <button type="submit" class="fa-btn grad" [disabled]="loading || !valid()">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m22 2-7 20-4-9-9-4z"/></svg>
              {{ loading ? 'Envoi…' : 'Envoyer le message' }}
            </button>
          </form>
        </div>
      </app-france-auth>
    } @else {
      <app-france-auth>
        <div class="fa-badge fa-badge-lg">
          <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.8" stroke-linecap="round"><path d="m6 12.5 4 4L18.5 8"/></svg>
        </div>
        <h1 class="fa-title">Message <em>envoyé&nbsp;!</em></h1>
        <p class="fa-sub">
          Merci, votre demande a bien été transmise.<br>
          Notre équipe vous répondra dans les meilleurs délais.
        </p>

        <div class="fa-card fa-center">
          <span class="fa-card-ic fa-mail-ic">
            <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.6"><rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/></svg>
          </span>
          <p class="fa-sent-txt">
            Nous mettons tout en œuvre pour vous apporter<br>
            la meilleure expérience avec Calypso.
          </p>
          <div class="fa-dotline" aria-hidden="true"><span></span></div>
          <a routerLink="/fr" class="fa-btn grad">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9"><path d="M4 11.5 12 4l8 7.5M6.5 10v9h11v-9"/></svg>
            Retour à l'accueil
          </a>
        </div>
      </app-france-auth>
    }
  `,
  styles: [`
    /* Complements propres au contact, dans l'echelle de l'habillage (--a). */
    .fa-badge-lg { width: calc(110 * var(--a)); height: calc(110 * var(--a)); }
    .fa-badge-lg svg { width: calc(52 * var(--a)); height: calc(52 * var(--a)); }
    .fa-area textarea {
      width: 100%; min-height: calc(120 * var(--a)); resize: vertical;
      background: rgba(255,255,255,.03); border: 1px solid rgba(255,255,255,.14);
      border-radius: calc(10 * var(--a)); color: #E7ECF5;
      font: inherit; font-size: calc(14 * var(--a)); line-height: 1.55;
      padding: calc(12 * var(--a)) calc(14 * var(--a));
      outline: none;
    }
    .fa-area textarea::placeholder { color: #6B7A94; }
    .fa-area textarea:focus { border-color: rgba(167,139,250,.65); }

    /* Etat « Message envoye ! » : carte centree de la capture. */
    .fa-center { text-align: center; padding: calc(40 * var(--a)) calc(56 * var(--a)) calc(44 * var(--a)); }
    .fa-mail-ic {
      width: calc(86 * var(--a)); height: calc(86 * var(--a));
      margin: 0 auto; display: grid;
    }
    .fa-mail-ic svg { width: calc(40 * var(--a)); height: calc(40 * var(--a)); }
    .fa-sent-txt {
      margin: calc(26 * var(--a)) 0 0;
      font-size: calc(16 * var(--a)); line-height: calc(26 * var(--a)); color: #C7D2E4;
    }
    .fa-dotline {
      position: relative; margin: calc(28 * var(--a)) auto;
      height: 1px; background: rgba(255,255,255,.12);
    }
    .fa-dotline span {
      position: absolute; left: 50%; top: 50%; transform: translate(-50%,-50%);
      width: calc(7 * var(--a)); height: calc(7 * var(--a)); border-radius: 50%;
      background: #A78BFA;
    }
  `]
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
      next: () => { this.loading = false; this.sent = true; },
      error: (err) => {
        this.loading = false;
        this.erreur = err?.error?.message
          || 'Votre message n\'a pas pu être transmis. Réessayez dans un instant.';
      }
    });
  }
}
