import { Component, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FranceAuthComponent } from './france/france-auth.component';
import { RegionService } from '../services/region.service';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { environment } from '../environments/environment';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, FranceAuthComponent],
  template: `
    @if (europe) {
      <app-france-auth>
        <h1 class="fa-title">Se <em>connecter</em></h1>
        <p class="fa-sub">Accédez à votre espace Calypso et gérez votre parc en toute simplicité.</p>

        <div class="fa-card">
          <div class="fa-badge">
            <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.6" stroke-linecap="round">
              <circle cx="12" cy="9" r="3.4"/>
              <path d="M5.5 19.5a6.8 6.8 0 0 1 13 0"/>
            </svg>
          </div>
          <form (ngSubmit)="onSubmit()">
            <label for="email">E-mail <span class="req">*</span></label>
            <div class="fa-field">
              <svg class="pre" viewBox="0 0 24 24" fill="none" stroke="#6B7A94" stroke-width="1.8"><rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/></svg>
              <input id="email" name="email" type="email" autocomplete="email"
                     [(ngModel)]="email" placeholder="votre@email.com" required>
            </div>

            <label for="password">Mot de passe <span class="req">*</span></label>
            <div class="fa-field">
              <svg class="pre" viewBox="0 0 24 24" fill="none" stroke="#6B7A94" stroke-width="1.8"><rect x="5" y="10.5" width="14" height="9.5" rx="2.2"/><path d="M8.5 10.5V8a3.5 3.5 0 0 1 7 0v2.5"/></svg>
              <input id="password" name="password" [type]="showPassword ? 'text' : 'password'"
                     autocomplete="current-password" [(ngModel)]="password"
                     placeholder="Votre mot de passe" required>
              <button type="button" class="fa-eye" (click)="showPassword = !showPassword"
                      [attr.aria-label]="showPassword ? 'Masquer le mot de passe' : 'Afficher le mot de passe'">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M2 12s3.6-6.5 10-6.5S22 12 22 12s-3.6 6.5-10 6.5S2 12 2 12z"/><circle cx="12" cy="12" r="2.8"/></svg>
              </button>
            </div>

            <div class="fa-row">
              <label class="fa-check">
                <input type="checkbox" name="remember" [(ngModel)]="rememberMe">
                Se souvenir de moi
              </label>
              <a routerLink="/mot-de-passe-oublie" class="fa-link">Mot de passe oublié ?</a>
            </div>

            @if (errorMessage) { <div class="fa-error">{{ errorMessage }}</div> }

            <button type="submit" class="fa-btn grad" [disabled]="isLoading">
              {{ isLoading ? 'Connexion…' : 'Se connecter' }}
            </button>
          </form>

          @if (signupEnabled) {
            <div class="fa-or">ou</div>
            <a routerLink="/inscription" class="fa-btn line">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round"><circle cx="9" cy="9" r="3.2"/><path d="M3 19a6 6 0 0 1 12 0M18 8v6M21 11h-6"/></svg>
              Créer un compte
            </a>
            <p class="fa-foot-note">
              Pas encore de compte ? <a routerLink="/inscription" class="fa-link">Créez-en un</a> en quelques clics.
            </p>
          }
        </div>
      </app-france-auth>
    } @else {
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
          <h1>Connexion</h1>
          <p>Accédez à votre tableau de bord</p>
        </div>

        <form (ngSubmit)="onSubmit()" class="auth-form">
          <div class="form-group">
            <label for="email">Email</label>
            <input
              type="email"
              id="email"
              [(ngModel)]="email"
              name="email"
              placeholder="exemple@entreprise.com"
              required
            />
          </div>

          <div class="form-group">
            <label for="password">Mot de passe</label>
            <!-- Œil afficher/masquer : sur mobile surtout, saisir un mot de passe
                 à l'aveugle est la première cause d'échec de connexion. Le
                 formulaire du parcours Europe l'avait déjà. -->
            <div class="pw-field">
              <input
                [type]="showPassword ? 'text' : 'password'"
                id="password"
                [(ngModel)]="password"
                name="password"
                placeholder="••••••••"
                autocomplete="current-password"
                required
              />
              <button type="button" class="pw-eye" (click)="showPassword = !showPassword"
                      [attr.aria-label]="showPassword ? 'Masquer le mot de passe' : 'Afficher le mot de passe'"
                      [attr.aria-pressed]="showPassword">
                <svg *ngIf="!showPassword" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M2 12s3.6-6.5 10-6.5S22 12 22 12s-3.6 6.5-10 6.5S2 12 2 12z"/><circle cx="12" cy="12" r="2.8"/>
                </svg>
                <svg *ngIf="showPassword" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M2 12s3.6-6.5 10-6.5c1.7 0 3.2.5 4.5 1.1M22 12s-3.6 6.5-10 6.5c-1.7 0-3.2-.5-4.5-1.1"/>
                  <path d="M3 3l18 18"/>
                </svg>
              </button>
            </div>
          </div>

          <div class="form-options">
            <label class="checkbox-label">
              <input type="checkbox" [(ngModel)]="rememberMe" name="rememberMe" />
              <span>Se souvenir de moi</span>
            </label>
            <a routerLink="/mot-de-passe-oublie" class="link">Mot de passe oublié ?</a>
          </div>

          @if (errorMessage) {
            <div class="error-message">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/>
              </svg>
              {{ errorMessage }}
            </div>
          }

          <button type="button" class="btn-primary btn-full" [disabled]="isLoading" (click)="onSubmit()">
            {{ isLoading ? 'Connexion…' : 'Se connecter' }}
          </button>
        </form>

        <!-- Le lien d'inscription suit le même drapeau par déploiement que la
             route : là où l'inscription libre n'est pas vendue, il n'apparaît pas
             et le message d'origine reste affiché. -->
        <div class="auth-footer">
          @if (signupEnabled) {
            <p>Pas encore de compte ? <a routerLink="/inscription" class="link">Créer un compte</a></p>
          } @else {
            <p>Contactez votre administrateur pour obtenir un accès</p>
          }
        </div>
      </div>

      <div class="auth-side">
        <h2>Gérez votre flotte en toute simplicité</h2>
        <div class="features-list">
          <div class="feature-item">
            <span class="feature-icon">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0118 0z"/><circle cx="12" cy="10" r="3"/></svg>
            </span>
            <div>
              <h3>Suivi GPS en temps réel</h3>
              <p>Positions, trajets et alertes de toute votre flotte</p>
            </div>
          </div>
          <div class="feature-item">
            <span class="feature-icon">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="1" y="3" width="15" height="13" rx="2"/><path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-5"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/></svg>
            </span>
            <div>
              <h3>Gestion complète du parc</h3>
              <p>Entretiens, dépenses, carburant et documents</p>
            </div>
          </div>
          <div class="feature-item">
            <span class="feature-icon">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="20" x2="18" y2="10"/><line x1="12" y1="20" x2="12" y2="4"/><line x1="6" y1="20" x2="6" y2="14"/></svg>
            </span>
            <div>
              <h3>Rapports et analyses</h3>
              <p>Tableaux de bord et exports Excel/PDF</p>
            </div>
          </div>
        </div>
      </div>
    </div>
    }
  `,
  styles: [`
    /* Charte « Calypso Command » — plein écran carbone signature,
       carte blanche, indigo en accent (aligné sur l'admin et le dashboard). */
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
      width: 440px; max-width: 100%;
      padding: 48px;
      box-sizing: border-box;
      background: #fff;
      border-radius: 18px;
      position: relative; z-index: 1;
      box-shadow: 0 24px 60px -24px rgba(2,6,23,.65), 0 48px 120px -32px rgba(2,6,23,.55);
      animation: rise .35s ease-out both;
    }

    .auth-header { margin-bottom: 28px; }

    .logo { display: flex; align-items: center; gap: 14px; margin-bottom: 24px; }
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

    .form-group { margin-bottom: 18px; }
    .form-group label { display: block; font-weight: 600; color: #374151; margin-bottom: 8px; font-size: 13.5px; }
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
    /* Champ mot de passe avec bouton œil : le bouton se superpose à droite,
       et l'input réserve la place pour ne pas passer sous l'icône. */
    .pw-field { position: relative; }
    .pw-field input { padding-right: 46px; }
    .pw-eye {
      position: absolute;
      top: 50%;
      right: 6px;
      transform: translateY(-50%);
      width: 34px;
      height: 34px;
      display: grid;
      place-items: center;
      background: none;
      border: none;
      border-radius: 8px;
      padding: 0;
      cursor: pointer;
      color: #6B7A94;
    }
    .pw-eye svg { width: 19px; height: 19px; }
    .pw-eye:hover { color: var(--c-indigo); background: #eef2ff; }
    .pw-eye:focus-visible { outline: 2px solid var(--c-indigo); outline-offset: 1px; }
    .form-group input:focus {
      outline: none;
      background: #fff;
      border-color: var(--c-indigo);
      box-shadow: 0 0 0 3px rgba(79,70,229,.12);
    }

    .form-options { display: flex; justify-content: space-between; align-items: center; margin-bottom: 20px; }
    .checkbox-label { display: flex; align-items: center; gap: 8px; cursor: pointer; font-size: 13.5px; color: var(--c-sub); }
    .checkbox-label input[type="checkbox"] { width: 16px; height: 16px; cursor: pointer; accent-color: var(--c-indigo); }
    .link { color: var(--c-indigo); text-decoration: none; font-weight: 600; font-size: 13.5px; }
    .link:hover { color: var(--c-indigo-ink); }

    .btn-primary {
      width: 100%;
      padding: 14px;
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
    .btn-primary:disabled { opacity: .7; cursor: not-allowed; }

    .auth-footer { text-align: center; padding-top: 22px; margin-top: 24px; border-top: 1px solid var(--c-border); }
    .auth-footer p { color: var(--c-sub); font-size: 13px; margin: 0; }

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
      .auth-card { width: 100%; max-width: 440px; }
    }

    @media (max-width: 480px) {
      .auth-page { padding: 24px 16px; }
      .auth-card { padding: 32px 24px; }
    }
  `]
})
export class LoginComponent {
  /** L habillage commercial n est servi qu au parcours europeen. */
  readonly europe = inject(RegionService).isEurope;

  /** Bascule de visibilite du mot de passe, comme sur la capture validee. */
  showPassword = false;

  email = '';
  password = '';
  rememberMe = false;
  isLoading = false;
  errorMessage = '';
  brand = (environment as any).brandName || 'Calypso';
  // Même drapeau par déploiement que la route /inscription : sur un serveur qui ne
  // vend pas l'inscription libre, la route n'existe pas et le lien mènerait nulle part.
  signupEnabled = (environment as any).selfSignup === true;

  constructor(
    private router: Router,
    private authService: AuthService,
    private cdr: ChangeDetectorRef
  ) {
    // « Se souvenir de moi » : e-mail pré-rempli si mémorisé lors d'une
    // connexion précédente (le mot de passe n'est jamais stocké).
    const remembered = localStorage.getItem('remembered_email');
    if (remembered) {
      this.email = remembered;
      this.rememberMe = true;
    }
  }

  onSubmit() {
    this.isLoading = true;
    this.errorMessage = '';
    this.cdr.detectChanges();

    this.authService.login(this.email, this.password, this.rememberMe).subscribe({
      next: (user) => {
        this.isLoading = false;
        if (user) {
          if (this.rememberMe) {
            localStorage.setItem('remembered_email', this.email);
          } else {
            localStorage.removeItem('remembered_email');
          }
          this.router.navigate(['/dashboard']);
        } else {
          this.errorMessage = 'Email ou mot de passe incorrect';
        }
        // Le callback peut s'exécuter hors du cycle de détection Angular
        // (selon la chaîne d'observables du login) : sans ce detectChanges,
        // le message d'erreur ne s'affichait qu'à l'interaction suivante.
        this.cdr.detectChanges();
      },
      error: () => {
        this.isLoading = false;
        this.errorMessage = 'Email ou mot de passe incorrect';
        this.cdr.detectChanges();
      }
    });
  }
}
