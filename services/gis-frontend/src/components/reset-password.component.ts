import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../environments/environment';

/**
 * Écran de choix du nouveau mot de passe, atteint par le lien reçu par courriel.
 *
 * <p>Le jeton vient de l'URL. Il n'est <b>pas</b> vérifié à l'ouverture de la
 * page : un point d'entrée qui dirait « ce jeton est valide » avant même qu'on
 * propose un mot de passe permettrait de tester des jetons à la chaîne. Il est
 * donc soumis en même temps que le mot de passe, une seule fois.</p>
 */
@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="auth-page">
      <div class="auth-card">
        <div class="auth-header">
          <div class="logo">
            <img src="/assets/calypso-logo.svg" alt="Calypso">
          </div>
          <h1>{{ done ? 'Mot de passe changé' : 'Nouveau mot de passe' }}</h1>
          <p>
            {{ done
              ? 'Vous pouvez maintenant vous connecter avec ce nouveau mot de passe.'
              : 'Choisissez un mot de passe que vous n’utilisez nulle part ailleurs.' }}
          </p>
        </div>

        @if (!token) {
          <div class="error-message">
            Ce lien est incomplet. Ouvrez-le directement depuis le courriel reçu,
            ou demandez-en un nouveau.
          </div>
          <a routerLink="/mot-de-passe-oublie" class="btn-primary btn-full as-link">
            Demander un nouveau lien
          </a>
        } @else if (!done) {
          <form (ngSubmit)="submit()">
            <div class="form-group">
              <label for="pwd">Nouveau mot de passe</label>
              <input id="pwd" name="pwd" type="password" autocomplete="new-password"
                     [(ngModel)]="password" placeholder="••••••••••" required />
              <small class="hint">Au moins 10 caractères.</small>
            </div>

            <div class="form-group">
              <label for="pwd2">Confirmez le mot de passe</label>
              <input id="pwd2" name="pwd2" type="password" autocomplete="new-password"
                     [(ngModel)]="confirm" placeholder="••••••••••" required />
            </div>

            @if (error) {
              <div class="error-message">{{ error }}</div>
            }

            <button type="submit" class="btn-primary btn-full" [disabled]="loading || !valid()">
              {{ loading ? 'Enregistrement…' : 'Changer mon mot de passe' }}
            </button>
          </form>
        } @else {
          <div class="ok-box">
            <p>
              Par sécurité, <strong>toutes vos sessions ouvertes ont été
              fermées</strong> — y compris sur vos autres appareils.
            </p>
            <a routerLink="/login" class="btn-primary btn-full as-link">Se connecter</a>
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
    .hint { display: block; margin-top: 6px; font-size: 12px; color: #94a3b8; }
    .btn-primary {
      background: #4f46e5; color: #fff; border: 0; border-radius: 10px;
      padding: 13px 20px; font-size: 15px; font-weight: 600; cursor: pointer;
      font-family: inherit; transition: background .2s;
    }
    .btn-primary:hover:not(:disabled) { background: #4338ca; }
    .btn-primary:disabled { opacity: .55; cursor: not-allowed; }
    .btn-full { width: 100%; }
    .as-link { display: block; text-align: center; text-decoration: none; box-sizing: border-box; }
    .error-message {
      background: #fef2f2; border: 1px solid #fecaca; color: #b91c1c;
      border-radius: 10px; padding: 11px 14px; font-size: 14px; margin-bottom: 16px;
    }
    .ok-box p { font-size: 14.5px; color: #334155; line-height: 1.6; margin: 0 0 18px; }
    .auth-footer { text-align: center; margin-top: 22px; }
    .auth-footer p { font-size: 14px; color: #64748b; margin: 0; }
    .link { color: #4f46e5; text-decoration: none; font-weight: 600; }
    .link:hover { text-decoration: underline; }
  `]
})
export class ResetPasswordComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  token = '';
  password = '';
  confirm = '';
  loading = false;
  done = false;
  error = '';

  ngOnInit(): void {
    this.token = (this.route.snapshot.queryParamMap.get('token') || '').trim();

    // Le jeton est retiré de la barre d'adresse dès qu'il est en mémoire : il
    // donne accès au compte, et une URL se copie, se partage et se retrouve
    // dans l'historique du navigateur.
    if (this.token) {
      this.router.navigate([], {
        replaceUrl: true, queryParams: {}, relativeTo: this.route
      });
    }
  }

  valid(): boolean {
    return this.password.length >= 10 && this.password === this.confirm;
  }

  submit(): void {
    if (this.loading) return;

    if (this.password.length < 10) {
      this.error = 'Le mot de passe doit compter au moins 10 caractères.';
      return;
    }
    if (this.password !== this.confirm) {
      this.error = 'Les deux mots de passe ne correspondent pas.';
      return;
    }

    this.loading = true;
    this.error = '';

    this.http.post<{ success: boolean; message: string }>(
      `${environment.apiUrl}/auth/reset-password`,
      { token: this.token, newPassword: this.password }
    ).subscribe({
      next: () => { this.loading = false; this.done = true; },
      error: (err) => {
        this.loading = false;
        this.error = err?.error?.message
          || "Ce lien n'est plus valable. Demandez-en un nouveau depuis l'écran de connexion.";
      }
    });
  }
}
