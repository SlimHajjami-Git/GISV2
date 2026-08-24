import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../environments/environment';

/**
 * Écran « mot de passe oublié » : saisie de l'adresse, envoi du lien.
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
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="auth-page">
      <div class="auth-card">
        <div class="auth-header">
          <div class="logo">
            <img src="/assets/calypso-logo.svg" alt="Calypso">
          </div>
          <h1>{{ sent ? 'Vérifiez votre boîte mail' : 'Mot de passe oublié' }}</h1>
          <p>
            {{ sent
              ? 'Si un compte correspond à cette adresse, le lien vient de partir.'
              : 'Indiquez votre adresse : nous vous enverrons un lien pour en choisir un nouveau.' }}
          </p>
        </div>

        @if (!sent) {
          <form (ngSubmit)="submit()">
            <div class="form-group">
              <label for="email">Adresse email</label>
              <input id="email" name="email" type="email" autocomplete="email"
                     [(ngModel)]="email" placeholder="vous@exemple.com" required />
            </div>

            @if (error) {
              <div class="error-message">{{ error }}</div>
            }

            <button type="submit" class="btn-primary btn-full"
                    [disabled]="loading || !email.trim()">
              {{ loading ? 'Envoi…' : 'Envoyer le lien' }}
            </button>
          </form>
        } @else {
          <div class="sent-box">
            <p>
              Le lien est valable <strong>une heure</strong> et ne peut servir
              qu'une seule fois.
            </p>
            <p class="muted">
              Rien reçu ? Regardez dans les indésirables. Le courriel peut mettre
              quelques minutes à arriver.
            </p>
            <button type="button" class="btn-ghost btn-full" (click)="again()">
              Réessayer avec une autre adresse
            </button>
          </div>
        }

        <div class="auth-footer">
          <p><a routerLink="/login" class="link">Retour à la connexion</a></p>
        </div>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .auth-page {
      min-height: 100vh; display: grid; place-items: center;
      background: #f1f5f9; padding: 24px; font-family: Inter, system-ui, sans-serif;
    }
    .auth-card {
      width: 100%; max-width: 430px; background: #fff; border-radius: 18px;
      padding: 38px 34px; box-shadow: 0 1px 2px rgba(15,23,42,.05), 0 22px 48px -20px rgba(15,23,42,.22);
    }
    .auth-header { text-align: center; margin-bottom: 26px; }
    .logo img { height: 42px; width: auto; display: block; margin: 0 auto 20px; }
    .auth-header h1 { font-size: 22px; font-weight: 700; color: #0f172a; margin: 0 0 8px; }
    .auth-header p { font-size: 14.5px; color: #64748b; margin: 0; line-height: 1.55; }
    .form-group { margin-bottom: 18px; }
    label { display: block; font-size: 13.5px; font-weight: 600; color: #334155; margin-bottom: 7px; }
    input {
      width: 100%; padding: 13px 15px; background: #f8fafc; border: 1px solid #e2e8f0;
      border-radius: 10px; font-size: 15px; color: #0f172a; box-sizing: border-box;
      transition: all .2s; font-family: inherit;
    }
    input:focus { outline: none; background: #fff; border-color: #4f46e5; box-shadow: 0 0 0 3px rgba(79,70,229,.12); }
    .btn-primary {
      background: #4f46e5; color: #fff; border: 0; border-radius: 10px;
      padding: 13px 20px; font-size: 15px; font-weight: 600; cursor: pointer;
      font-family: inherit; transition: background .2s;
    }
    .btn-primary:hover:not(:disabled) { background: #4338ca; }
    .btn-primary:disabled { opacity: .55; cursor: not-allowed; }
    .btn-ghost {
      background: #fff; color: #334155; border: 1px solid #e2e8f0; border-radius: 10px;
      padding: 12px 20px; font-size: 14.5px; font-weight: 600; cursor: pointer; font-family: inherit;
    }
    .btn-ghost:hover { border-color: #cbd5e1; }
    .btn-full { width: 100%; }
    .error-message {
      background: #fef2f2; border: 1px solid #fecaca; color: #b91c1c;
      border-radius: 10px; padding: 11px 14px; font-size: 14px; margin-bottom: 16px;
    }
    .sent-box p { font-size: 14.5px; color: #334155; line-height: 1.6; margin: 0 0 12px; }
    .sent-box .muted { color: #64748b; font-size: 13.5px; margin-bottom: 20px; }
    .auth-footer { text-align: center; margin-top: 22px; }
    .auth-footer p { font-size: 14px; color: #64748b; margin: 0; }
    .link { color: #4f46e5; text-decoration: none; font-weight: 600; }
    .link:hover { text-decoration: underline; }
  `]
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
