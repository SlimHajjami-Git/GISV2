import { Component, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { environment } from '../environments/environment';

/**
 * Inscription libre — jumeau visuel de l'écran de connexion.
 *
 * AUCUN CHOIX DE PLAN ICI. Le brouillon précédent proposait au visiteur de
 * sélectionner son abonnement et envoyait l'identifiant choisi au serveur : c'était
 * un libre-service, n'importe qui pouvait s'attribuer l'offre la plus complète. Le
 * plan de départ et la durée d'essai viennent désormais de la configuration du
 * serveur, et la requête ne porte aucun champ d'abonnement. Le visiteur découvre
 * les offres APRÈS son inscription, sur l'écran d'abonnement.
 *
 * Le nom de société est facultatif : à défaut, la société prend le nom de la
 * personne — beaucoup d'indépendants n'ont pas encore d'entreprise déclarée.
 */
@Component({
  selector: 'app-register',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="auth-page">
      <div class="auth-bg">
        <div class="bg-glow"></div>
        <div class="bg-dots"></div>
      </div>

      <div class="auth-card">
        <div class="auth-header">
          <div class="logo">
            <span class="logo-icon">
              <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="white" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
                <path d="M5 13l4 4L19 7"/>
              </svg>
            </span>
            <div class="logo-text">
              <span class="brand">{{ brand }}</span>
              <span class="subtitle">Gestion de flotte</span>
            </div>
          </div>
          <h1>Créer un compte</h1>
          <p>{{ trialDays }} jours d'essai — sans carte bancaire</p>
        </div>

        <form (ngSubmit)="onSubmit()" class="auth-form">
          <div class="form-row">
            <div class="form-group">
              <label for="firstName">Prénom</label>
              <input type="text" id="firstName" name="firstName" [(ngModel)]="firstName" placeholder="Sonia" required />
            </div>
            <div class="form-group">
              <label for="lastName">Nom</label>
              <input type="text" id="lastName" name="lastName" [(ngModel)]="lastName" placeholder="Ben Salah" required />
            </div>
          </div>

          <div class="form-group">
            <label for="email">Email professionnel</label>
            <input type="email" id="email" name="email" [(ngModel)]="email" placeholder="exemple@entreprise.com" required />
          </div>

          <div class="form-group">
            <label for="companyName">Nom de la société <span class="optional">facultatif</span></label>
            <input type="text" id="companyName" name="companyName" [(ngModel)]="companyName"
                   [placeholder]="companyPlaceholder" />
            <small class="hint">Sans indication, votre espace prendra votre nom.</small>
          </div>

          <div class="form-group">
            <label for="phone">Téléphone <span class="optional">facultatif</span></label>
            <input type="tel" id="phone" name="phone" [(ngModel)]="phone" placeholder="+213 5 00 00 00 00" />
          </div>

          <div class="form-group">
            <label for="password">Mot de passe</label>
            <input type="password" id="password" name="password" [(ngModel)]="password" placeholder="••••••••••" required />
            <small class="hint">Au moins 10 caractères, mêlant minuscules, majuscules, chiffres ou symboles.</small>
          </div>

          @if (errorMessage) {
            <div class="error-message">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/>
              </svg>
              {{ errorMessage }}
            </div>
          }

          <button type="button" class="btn-primary btn-full" [disabled]="isLoading || !isValid()" (click)="onSubmit()">
            {{ isLoading ? 'Création du compte…' : 'Créer mon compte' }}
          </button>
        </form>

        <div class="auth-footer">
          <p>Vous avez déjà un compte ? <a routerLink="/login" class="link">Se connecter</a></p>
        </div>
      </div>

      <div class="auth-side">
        <h2>Votre parc, en ordre, dès aujourd'hui</h2>
        <div class="features-list">
          <div class="feature-item">
            <span class="feature-icon">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="1" y="3" width="15" height="13" rx="2"/><path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-5"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/></svg>
            </span>
            <div>
              <h3>Vos véhicules et leurs entretiens</h3>
              <p>Échéances, réparations et documents au même endroit</p>
            </div>
          </div>
          <div class="feature-item">
            <span class="feature-icon">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="12" y1="1" x2="12" y2="23"/><path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/></svg>
            </span>
            <div>
              <h3>Dépenses et carburant</h3>
              <p>Saisie manuelle ou scan de vos factures</p>
            </div>
          </div>
          <div class="feature-item">
            <span class="feature-icon">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="20" x2="18" y2="10"/><line x1="12" y1="20" x2="12" y2="4"/><line x1="6" y1="20" x2="6" y2="14"/></svg>
            </span>
            <div>
              <h3>Rapports prêts à exporter</h3>
              <p>Kilométrage, coûts et entretien, en Excel ou PDF</p>
            </div>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    /* Charte « Calypso Command », identique à l'écran de connexion : les deux
       pages se suivent, elles ne doivent pas se ressembler « à peu près ». */
    .auth-page {
      --c-indigo: #4f46e5;
      --c-indigo-ink: #4338ca;
      --c-border: #e6eaf2;
      --c-ink: #0f172a;
      --c-sub: #64748b;

      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 72px;
      padding: 48px 32px;
      box-sizing: border-box;
      position: relative;
      background: linear-gradient(135deg, #0d1425 0%, #16213a 100%);
    }

    .auth-bg { position: absolute; inset: 0; overflow: hidden; pointer-events: none; }
    .bg-glow {
      position: absolute; top: -50%; right: -20%; width: 80%; height: 150%;
      background: radial-gradient(ellipse at center, rgba(99,102,241,.24) 0%, transparent 70%);
      animation: pulse 8s ease-in-out infinite;
    }
    .bg-dots {
      position: absolute; inset: 0;
      background-image: radial-gradient(rgba(255,255,255,.05) 1px, transparent 1px);
      background-size: 40px 40px;
    }
    @keyframes pulse {
      0%, 100% { transform: scale(1); opacity: .5; }
      50% { transform: scale(1.1); opacity: .8; }
    }
    @keyframes rise {
      from { opacity: 0; transform: translateY(8px); }
      to { opacity: 1; transform: none; }
    }

    .auth-card {
      width: 480px; max-width: 100%;
      padding: 44px 48px;
      box-sizing: border-box;
      background: #fff;
      border-radius: 18px;
      position: relative; z-index: 1;
      box-shadow: 0 24px 60px -24px rgba(2,6,23,.65), 0 48px 120px -32px rgba(2,6,23,.55);
      animation: rise .35s ease-out both;
    }

    .auth-header { margin-bottom: 24px; }
    .logo { display: flex; align-items: center; gap: 14px; margin-bottom: 22px; }
    .logo-icon {
      width: 52px; height: 52px;
      background: linear-gradient(135deg, #6366f1 0%, #4f46e5 100%);
      border-radius: 14px;
      display: flex; align-items: center; justify-content: center;
      box-shadow: 0 8px 24px rgba(79,70,229,.35);
    }
    .logo-text { display: flex; flex-direction: column; }
    .brand { font-size: 22px; font-weight: 800; color: var(--c-ink); letter-spacing: -.01em; }
    .subtitle { font-size: 13px; color: var(--c-indigo); font-weight: 600; }

    .auth-header h1 { font-size: 26px; font-weight: 800; color: var(--c-ink); margin: 0 0 6px; letter-spacing: -.02em; }
    .auth-header p { color: var(--c-sub); margin: 0; font-size: 14.5px; }

    .form-row { display: grid; grid-template-columns: 1fr 1fr; gap: 14px; }
    .form-group { margin-bottom: 16px; }
    .form-group label { display: block; font-weight: 600; color: #374151; margin-bottom: 8px; font-size: 13.5px; }
    .optional { font-weight: 500; color: #94a3b8; font-size: 12px; margin-left: 4px; }
    .form-group input {
      width: 100%;
      padding: 13px 15px;
      background: #f8fafc;
      border: 1px solid var(--c-border);
      border-radius: 10px;
      font-size: 15px;
      color: var(--c-ink);
      transition: all .2s;
      box-sizing: border-box;
    }
    .form-group input:focus {
      outline: none;
      background: #fff;
      border-color: var(--c-indigo);
      box-shadow: 0 0 0 3px rgba(79,70,229,.12);
    }
    .hint { display: block; margin-top: 6px; font-size: 12px; color: var(--c-sub); line-height: 1.45; }

    .link { color: var(--c-indigo); text-decoration: none; font-weight: 600; }
    .link:hover { color: var(--c-indigo-ink); }

    .btn-primary {
      width: 100%;
      padding: 14px;
      margin-top: 4px;
      background: var(--c-indigo);
      color: #fff;
      border: none;
      border-radius: 10px;
      font-size: 15px;
      font-weight: 600;
      cursor: pointer;
      transition: all .2s;
    }
    .btn-primary:hover:not(:disabled) {
      background: var(--c-indigo-ink);
      transform: translateY(-1px);
      box-shadow: 0 8px 24px rgba(79,70,229,.35);
    }
    .btn-primary:disabled { opacity: .55; cursor: not-allowed; }

    .auth-footer { text-align: center; padding-top: 20px; margin-top: 22px; border-top: 1px solid var(--c-border); }
    .auth-footer p { color: var(--c-sub); font-size: 13.5px; margin: 0; }

    .error-message {
      display: flex; align-items: center; gap: 10px;
      background: rgba(220,38,38,.08);
      border: 1px solid rgba(220,38,38,.25);
      color: #b91c1c;
      padding: 12px 14px;
      border-radius: 10px;
      font-size: 13.5px;
      margin-bottom: 16px;
    }

    .auth-side {
      max-width: 460px;
      position: relative; z-index: 1;
      animation: rise .45s ease-out both;
    }
    .auth-side h2 {
      font-size: 30px; font-weight: 800; color: #fff;
      margin: 0 0 36px; letter-spacing: -.02em; line-height: 1.2;
    }
    .features-list { display: flex; flex-direction: column; gap: 28px; }
    .feature-item { display: flex; align-items: flex-start; gap: 18px; }
    .feature-icon {
      flex-shrink: 0;
      width: 46px; height: 46px;
      display: flex; align-items: center; justify-content: center;
      color: #a5b4fc;
      background: rgba(99,102,241,.16);
      border: 1px solid rgba(99,102,241,.28);
      border-radius: 12px;
    }
    .feature-item h3 { font-size: 17px; font-weight: 600; color: #e2e8f0; margin: 0 0 5px; }
    .feature-item p { font-size: 13.5px; color: #94a3b8; margin: 0; line-height: 1.5; }

    @media (prefers-reduced-motion: reduce) {
      .bg-glow, .auth-card, .auth-side { animation: none; }
    }
    @media (max-width: 1024px) {
      .auth-side { display: none; }
      .auth-card { width: 100%; max-width: 480px; }
    }
    @media (max-width: 560px) {
      .form-row { grid-template-columns: 1fr; gap: 0; }
    }
    @media (max-width: 480px) {
      .auth-page { padding: 24px 16px; }
      .auth-card { padding: 32px 24px; }
    }
  `]
})
export class RegisterComponent {
  firstName = '';
  lastName = '';
  email = '';
  companyName = '';
  phone = '';
  password = '';
  isLoading = false;
  errorMessage = '';

  brand = (environment as any).brandName || 'Calypso';
  // Purement informatif : la durée qui fait foi est celle du serveur.
  trialDays = (environment as any).selfSignupTrialDays || 14;

  constructor(
    private router: Router,
    private authService: AuthService,
    private cdr: ChangeDetectorRef
  ) {}

  /** Aperçu du nom que prendra l'espace si aucune société n'est saisie. */
  get companyPlaceholder(): string {
    const fullName = `${this.firstName} ${this.lastName}`.trim();
    return fullName.length > 0 ? fullName : 'Nom de votre société';
  }

  isValid(): boolean {
    return this.firstName.trim().length > 0
      && this.lastName.trim().length > 0
      && this.email.trim().length > 0
      && this.password.length >= 10;
  }

  onSubmit() {
    if (!this.isValid()) {
      this.errorMessage = 'Renseignez votre prénom, votre nom, votre email et un mot de passe d’au moins 10 caractères.';
      return;
    }

    this.isLoading = true;
    this.errorMessage = '';
    this.cdr.detectChanges();

    this.authService.registerCompany({
      firstName: this.firstName.trim(),
      lastName: this.lastName.trim(),
      email: this.email.trim(),
      password: this.password,
      companyName: this.companyName.trim() || undefined,
      phone: this.phone.trim() || undefined
    }).subscribe({
      next: () => {
        this.isLoading = false;
        // La session est déjà ouverte : on entre directement dans l'application.
        this.router.navigate(['/dashboard']);
      },
      error: (err) => {
        this.isLoading = false;
        // Le serveur renvoie des messages exploitables (email déjà pris, mot de
        // passe trop faible) : les afficher tels quels plutôt qu'un texte générique.
        this.errorMessage = err?.error?.message
          || err?.error?.errors?.[0]?.errorMessage
          || 'La création du compte a échoué. Vérifiez vos informations et réessayez.';
        this.cdr.detectChanges();
      }
    });
  }
}
