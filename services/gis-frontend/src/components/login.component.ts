import { Component, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { environment } from '../environments/environment';

@Component({
  selector: 'app-login',
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
            <input
              type="password"
              id="password"
              [(ngModel)]="password"
              name="password"
              placeholder="••••••••"
              required
            />
          </div>

          <div class="form-options">
            <label class="checkbox-label">
              <input type="checkbox" [(ngModel)]="rememberMe" name="rememberMe" />
              <span>Se souvenir de moi</span>
            </label>
            <a href="#" class="link">Mot de passe oublié ?</a>
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

        <div class="auth-footer">
          <p>Contactez votre administrateur pour obtenir un accès</p>
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
  email = '';
  password = '';
  rememberMe = false;
  isLoading = false;
  errorMessage = '';
  brand = (environment as any).brandName || 'Calypso';

  constructor(
    private router: Router,
    private authService: AuthService,
    private cdr: ChangeDetectorRef
  ) {}

  onSubmit() {
    this.isLoading = true;
    this.errorMessage = '';
    this.cdr.detectChanges();

    this.authService.login(this.email, this.password).subscribe({
      next: (user) => {
        this.isLoading = false;
        if (user) {
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
