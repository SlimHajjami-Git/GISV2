import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../environments/environment';
import { FranceAuthComponent } from './france/france-auth.component';

/**
 * Écran « Réinitialiser le mot de passe », d'après la capture validée.
 *
 * <p>Le jeton vient de l'URL. Il n'est <b>pas</b> vérifié à l'ouverture de la
 * page : un point d'entrée qui dirait « ce jeton est valide » avant même qu'on
 * propose un mot de passe permettrait de tester des jetons à la chaîne. Il est
 * donc soumis en même temps que le mot de passe, une seule fois.</p>
 *
 * <p>Les règles affichées sont celles que le serveur applique réellement —
 * dix caractères. La capture en annonce huit ; le document maître tranche :
 * « utiliser les règles réellement appliquées par l'application ». Afficher
 * huit ferait échouer une saisie pourtant conforme à ce qui est écrit.</p>
 */
@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [FormsModule, RouterLink, FranceAuthComponent],
  template: `
    <app-france-auth>
      <div class="fa-badge">
        <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7" stroke-linecap="round">
          <rect x="5" y="10.5" width="14" height="9.5" rx="2.2"/>
          <path d="M8.5 10.5V8a3.5 3.5 0 0 1 7 0v2.5"/>
          <path d="m10 15 1.6 1.6L14.5 14"/>
        </svg>
      </div>

      <h1 class="fa-title">Réinitialisez <em>votre mot de passe</em></h1>
      <p class="fa-sub">
        Choisissez un nouveau mot de passe sécurisé<br>
        pour accéder à votre espace Calypso.
      </p>

      <div class="fa-card">
        @if (!token) {
          <div class="fa-error">
            Ce lien est incomplet. Ouvrez-le directement depuis le courriel reçu,
            ou demandez-en un nouveau.
          </div>
          <a routerLink="/mot-de-passe-oublie" class="fa-btn grad">Demander un nouveau lien</a>
        } @else if (!done) {
          <label for="pwd">Nouveau mot de passe <span class="req">*</span></label>
          <div class="fa-field">
            <svg class="pre" viewBox="0 0 24 24" fill="none" stroke="#6B7A94" stroke-width="1.8"><rect x="5" y="10.5" width="14" height="9.5" rx="2.2"/><path d="M8.5 10.5V8a3.5 3.5 0 0 1 7 0v2.5"/></svg>
            <input id="pwd" name="pwd" [type]="show ? 'text' : 'password'" autocomplete="new-password"
                   [(ngModel)]="password" placeholder="Entrez votre nouveau mot de passe" required>
            <button type="button" class="fa-eye" (click)="show = !show"
                    [attr.aria-label]="show ? 'Masquer le mot de passe' : 'Afficher le mot de passe'">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M2 12s3.6-6.5 10-6.5S22 12 22 12s-3.6 6.5-10 6.5S2 12 2 12z"/><circle cx="12" cy="12" r="2.8"/></svg>
            </button>
          </div>

          <label for="pwd2">Confirmer le mot de passe <span class="req">*</span></label>
          <div class="fa-field">
            <svg class="pre" viewBox="0 0 24 24" fill="none" stroke="#6B7A94" stroke-width="1.8"><rect x="5" y="10.5" width="14" height="9.5" rx="2.2"/><path d="M8.5 10.5V8a3.5 3.5 0 0 1 7 0v2.5"/></svg>
            <input id="pwd2" name="pwd2" [type]="show ? 'text' : 'password'" autocomplete="new-password"
                   [(ngModel)]="confirm" placeholder="Confirmez votre nouveau mot de passe" required>
            <button type="button" class="fa-eye" (click)="show = !show"
                    [attr.aria-label]="show ? 'Masquer le mot de passe' : 'Afficher le mot de passe'">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M2 12s3.6-6.5 10-6.5S22 12 22 12s-3.6 6.5-10 6.5S2 12 2 12z"/><circle cx="12" cy="12" r="2.8"/></svg>
            </button>
          </div>

          <p class="fa-must">Votre mot de passe doit :</p>
          <ul class="fa-rules three">
            <li [class.ok]="password.length >= 10">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>
              Contenir au moins 10 caractères
            </li>
            <li [class.ok]="hasUpper">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>
              Inclure au moins une majuscule
            </li>
            <li [class.ok]="hasSpecial">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>
              Inclure au moins un caractère spécial
            </li>
            <li [class.ok]="hasLower">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>
              Inclure au moins une minuscule
            </li>
            <li [class.ok]="hasDigit">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>
              Inclure au moins un chiffre
            </li>
            <li [class.ok]="password.length > 0 && password === confirm">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>
              Correspondre dans les deux champs
            </li>
          </ul>

          @if (error) { <div class="fa-error">{{ error }}</div> }

          <button type="button" class="fa-btn grad" [disabled]="loading || !valid()" (click)="submit()">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9"><rect x="5" y="10.5" width="14" height="9.5" rx="2.2"/><path d="M8.5 10.5V8a3.5 3.5 0 0 1 7 0v2.5"/></svg>
            {{ loading ? 'Enregistrement…' : 'Enregistrer mon nouveau mot de passe' }}
          </button>
        } @else {
          <div class="fa-ok">Votre mot de passe a été changé.</div>
          <p style="color:#9AA7BD;font-size:14.5px;line-height:1.6;margin:0 0 20px">
            Par sécurité, <strong style="color:#fff">toutes vos sessions ouvertes ont été
            fermées</strong> — y compris sur vos autres appareils.
          </p>
          <a routerLink="/login" class="fa-btn grad">Accéder à Calypso</a>
        }

        <div class="fa-or">ou</div>

        <a routerLink="/login" class="fa-btn line">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 12H5M11 18l-6-6 6-6"/></svg>
          Retour à la connexion
        </a>
      </div>
    </app-france-auth>
  `,
  styles: [`
    .pw-rules { list-style: none; margin: -6px 0 22px; padding: 0; display: grid; gap: 9px; }
    .pw-rules li {
      display: flex; align-items: center; gap: 9px;
      font-size: 13.5px; color: #6B7A94; transition: color .16s ease;
    }
    .pw-rules svg { width: 15px; height: 15px; flex: none; }
    /* La regle passe au vert QUAND elle est satisfaite : la validation se lit
       pendant la saisie, pas apres un refus. */
    .pw-rules li.ok { color: #6EE7B7; }
  `]
})
export class ResetPasswordComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  token = '';
  password = '';
  confirm = '';
  show = false;
  loading = false;
  done = false;
  error = '';

  ngOnInit(): void {
    this.token = (this.route.snapshot.queryParamMap.get('token') || '').trim();

    // Le jeton est retiré de la barre d'adresse dès qu'il est en mémoire : il
    // donne accès au compte, et une URL se copie, se partage et se retrouve
    // dans l'historique du navigateur.
    if (this.token) {
      this.router.navigate([], { replaceUrl: true, queryParams: {}, relativeTo: this.route });
    }
  }

  get hasUpper(): boolean { return /[A-Z]/.test(this.password); }
  get hasLower(): boolean { return /[a-z]/.test(this.password); }
  get hasDigit(): boolean { return /[0-9]/.test(this.password); }
  get hasSpecial(): boolean { return /[^A-Za-z0-9]/.test(this.password); }

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
