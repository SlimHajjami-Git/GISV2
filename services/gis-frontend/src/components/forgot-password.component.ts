import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../environments/environment';
import { FranceAuthComponent } from './france/france-auth.component';

/**
 * Écran « Mot de passe oublié ? », d'après la capture validée.
 *
 * <p>L'écran <b>ne dit jamais</b> si l'adresse est connue. Ce n'est pas une
 * imprécision : afficher « adresse inconnue » transformerait la page en
 * annuaire, où n'importe qui pourrait vérifier adresse par adresse lesquelles
 * possèdent un compte. Le serveur répond d'ailleurs la même chose dans les
 * deux cas — l'écran ne fait que rester cohérent avec lui.</p>
 */
@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [FormsModule, RouterLink, FranceAuthComponent],
  template: `
    <app-france-auth>
      <div class="fa-badge">
        <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7" stroke-linecap="round">
          <rect x="5" y="10.5" width="14" height="9.5" rx="2.2"/>
          <path d="M8.5 10.5V8a3.5 3.5 0 0 1 7 0v2.5"/>
          <path d="M12 14v2.5"/>
        </svg>
      </div>

      <h1 class="fa-title">Mot de passe <em>oublié&nbsp;?</em></h1>
      <p class="fa-sub">
        Pas de souci ! Saisissez votre adresse e-mail ci-dessous.<br>
        Nous vous enverrons un lien pour réinitialiser votre mot de passe.
      </p>

      <div class="fa-card">
        @if (!sent) {
          <div class="fa-card-head">
            <span class="fa-card-ic">
              <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7"><rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/></svg>
            </span>
            <div>
              <h2>Votre e-mail <span class="req">*</span></h2>
              <p>Saisissez l'adresse e-mail associée à votre compte Calypso.</p>
            </div>
          </div>

          <form (ngSubmit)="submit()">
            <div class="fa-field">
              <svg class="pre" viewBox="0 0 24 24" fill="none" stroke="#6B7A94" stroke-width="1.8"><rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/></svg>
              <input id="email" name="email" type="email" autocomplete="email"
                     [(ngModel)]="email" placeholder="exemple@domaine.com" required>
            </div>

            @if (error) { <div class="fa-error">{{ error }}</div> }

            <button type="submit" class="fa-btn grad" [disabled]="loading || !email.trim()">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m22 2-7 20-4-9-9-4z"/></svg>
              {{ loading ? 'Envoi…' : 'Réinitialiser mon mot de passe' }}
            </button>
          </form>
        } @else {
          <div class="fa-ok">
            Si un compte est associé à cette adresse, le lien vient d'y être envoyé.
          </div>
          <p style="color:#9AA7BD;font-size:14.5px;line-height:1.6;margin:0 0 20px">
            Le lien est valable <strong style="color:#fff">une heure</strong> et ne peut
            servir qu'une seule fois. Rien reçu ? Regardez dans les indésirables :
            le courriel peut mettre quelques minutes à arriver.
          </p>
          <button type="button" class="fa-btn line" (click)="again()">
            Réessayer avec une autre adresse
          </button>
        }

        <div class="fa-or">ou</div>

        <a routerLink="/login" class="fa-btn line">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 12H5M11 18l-6-6 6-6"/></svg>
          Retour à la connexion
        </a>
      </div>
    </app-france-auth>
  `
})
export class ForgotPasswordComponent {
  private readonly http = inject(HttpClient);

  email = '';
  loading = false;
  sent = false;
  error = '';

  submit(): void {
    if (this.loading || !this.email.trim()) return;
    this.loading = true;
    this.error = '';

    this.http.post<{ message: string }>(
      `${environment.apiUrl}/auth/forgot-password`, { email: this.email.trim() }
    ).subscribe({
      next: () => { this.loading = false; this.sent = true; },
      error: () => {
        this.loading = false;
        // Même en cas d'échec réseau on ne révèle rien de l'adresse.
        this.error = "L'envoi n'a pas abouti. Vérifiez votre connexion et réessayez.";
      }
    });
  }

  again(): void {
    this.sent = false;
    this.email = '';
  }
}
