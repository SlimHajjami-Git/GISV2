import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AdminService } from '../services/admin.service';

@Component({
  selector: 'admin-login',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="login-container">
      <div class="login-background">
        <div class="bg-gradient"></div>
        <div class="bg-pattern"></div>
      </div>

      <div class="login-card">
        <div class="login-header">
          <div class="logo">
            <span class="logo-icon">C</span>
            <div class="logo-text">
              <span class="brand">Calypso</span>
              <span class="subtitle">Portail Admin</span>
            </div>
          </div>
          <p class="welcome-text">Bon retour ! Connectez-vous pour continuer.</p>
        </div>

        <form (ngSubmit)="login()" class="login-form">
          <div class="form-group">
            <label for="email">Adresse e-mail</label>
            <div class="input-wrapper">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/>
                <polyline points="22,6 12,13 2,6"/>
              </svg>
              <input
                type="email"
                id="email"
                [(ngModel)]="email"
                name="email"
                placeholder="admin@Calypso.tn"
                required
              />
            </div>
          </div>

          <div class="form-group">
            <label for="password">Mot de passe</label>
            <div class="input-wrapper">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="3" y="11" width="18" height="11" rx="2" ry="2"/>
                <path d="M7 11V7a5 5 0 0 1 10 0v4"/>
              </svg>
              <input
                [type]="showPassword ? 'text' : 'password'"
                id="password"
                [(ngModel)]="password"
                name="password"
                placeholder="Votre mot de passe"
                required
              />
              <button type="button" class="toggle-password" (click)="showPassword = !showPassword">
                <svg *ngIf="!showPassword" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/>
                  <circle cx="12" cy="12" r="3"/>
                </svg>
                <svg *ngIf="showPassword" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24"/>
                  <line x1="1" y1="1" x2="23" y2="23"/>
                </svg>
              </button>
            </div>
          </div>

          <div class="form-options">
            <label class="remember-me">
              <input type="checkbox" [(ngModel)]="rememberMe" name="rememberMe" />
              <span class="checkmark"></span>
              Se souvenir de moi
            </label>
            <a href="#" class="forgot-link">Mot de passe oublié ?</a>
          </div>

          <div class="error-message" *ngIf="error">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="12" cy="12" r="10"/>
              <line x1="12" y1="8" x2="12" y2="12"/>
              <line x1="12" y1="16" x2="12.01" y2="16"/>
            </svg>
            {{ error }}
          </div>

          <button type="submit" class="login-btn" [disabled]="loading">
            <span *ngIf="!loading">Se connecter</span>
            <span *ngIf="loading" class="loading-spinner"></span>
          </button>
        </form>

        <div class="login-footer">
          <div class="security-badge">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>
            </svg>
            <span>Accès sécurisé — Administrateurs uniquement</span>
          </div>
          <p class="copyright">© 2026 Calypso. Tous droits réservés.</p>
        </div>
      </div>

      <div class="login-info">
        <h2>Centre de contrôle Calypso</h2>
        <ul class="features-list">
          <li>
            <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/>
              <circle cx="9" cy="7" r="4"/>
              <path d="M23 21v-2a4 4 0 0 0-3-3.87"/>
              <path d="M16 3.13a4 4 0 0 1 0 7.75"/>
            </svg>
            <div>
              <h3>Gestion des sociétés</h3>
              <p>Sociétés, abonnements et permissions centralisés</p>
            </div>
          </li>
          <li>
            <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M22 12h-4l-3 9L9 3l-3 9H2"/>
            </svg>
            <div>
              <h3>Supervision système</h3>
              <p>Santé des services et flotte GPS en temps réel</p>
            </div>
          </li>
          <li>
            <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <rect x="3" y="3" width="7" height="7"/>
              <rect x="14" y="3" width="7" height="7"/>
              <rect x="14" y="14" width="7" height="7"/>
              <rect x="3" y="14" width="7" height="7"/>
            </svg>
            <div>
              <h3>Tableaux de bord</h3>
              <p>Usage, performance et facturation en un coup d&#39;œil</p>
            </div>
          </li>
        </ul>
      </div>
    </div>
  `,
  styles: [`
    /* Page hors shell admin-layout : les tokens --adm-* ne sont pas herites,
       ils sont donc redefinis localement (valeurs de la spec Calypso Command). */
    .login-container {
      --adm-indigo: #4f46e5;
      --adm-indigo-ink: #4338ca;
      --adm-red: #dc2626;
      --adm-red-ink: #b91c1c;
      --adm-card: #ffffff;
      --adm-border: #e6eaf2;
      --adm-ink: #0f172a;
      --adm-sub: #64748b;
      --adm-carb1: #0d1425;
      --adm-carb2: #16213a;
      --adm-glow: rgba(99, 102, 241, 0.24);

      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 72px;
      padding: 48px 32px;
      box-sizing: border-box;
      position: relative;
      background: linear-gradient(135deg, var(--adm-carb1) 0%, var(--adm-carb2) 100%);
    }

    .login-background {
      position: absolute;
      inset: 0;
      overflow: hidden;
      pointer-events: none;
    }

    .bg-gradient {
      position: absolute;
      top: -50%;
      right: -20%;
      width: 80%;
      height: 150%;
      background: radial-gradient(ellipse at center, var(--adm-glow) 0%, transparent 70%);
      animation: pulse 8s ease-in-out infinite;
    }

    .bg-pattern {
      position: absolute;
      inset: 0;
      background-image:
        radial-gradient(rgba(255, 255, 255, 0.05) 1px, transparent 1px);
      background-size: 40px 40px;
    }

    @keyframes pulse {
      0%, 100% { transform: scale(1); opacity: 0.5; }
      50% { transform: scale(1.1); opacity: 0.8; }
    }

    @keyframes rise {
      from { opacity: 0; transform: translateY(8px); }
      to { opacity: 1; transform: none; }
    }

    .login-card {
      width: 440px;
      max-width: 100%;
      padding: 48px;
      box-sizing: border-box;
      display: flex;
      flex-direction: column;
      justify-content: center;
      background: var(--adm-card);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 18px;
      position: relative;
      z-index: 1;
      box-shadow:
        0 24px 60px -24px rgba(2, 6, 23, 0.65),
        0 48px 120px -32px rgba(2, 6, 23, 0.55);
      animation: rise 0.35s ease-out both;
    }

    .login-header {
      margin-bottom: 32px;
    }

    .logo {
      display: flex;
      align-items: center;
      gap: 14px;
      margin-bottom: 24px;
    }

    .logo-icon {
      width: 52px;
      height: 52px;
      background: var(--adm-indigo);
      border-radius: 14px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 700;
      font-size: 24px;
      color: #fff;
      box-shadow: 0 8px 24px rgba(79, 70, 229, 0.35);
    }

    .logo-text {
      display: flex;
      flex-direction: column;
    }

    .brand {
      font-size: 24px;
      font-weight: 700;
      color: var(--adm-ink);
    }

    .subtitle {
      font-size: 14px;
      color: var(--adm-indigo);
      font-weight: 600;
    }

    .welcome-text {
      color: var(--adm-sub);
      font-size: 15px;
      margin: 0;
    }

    .login-form {
      display: flex;
      flex-direction: column;
      gap: 20px;
    }

    .form-group {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .form-group label {
      font-size: 14px;
      font-weight: 500;
      color: #374151;
    }

    .input-wrapper {
      display: flex;
      align-items: center;
      gap: 12px;
      background: #f8fafc;
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      padding: 14px 16px;
      transition: all 0.2s;
    }

    .input-wrapper:focus-within {
      border-color: var(--adm-indigo);
      background: #ffffff;
      box-shadow: 0 0 0 3px rgba(79, 70, 229, 0.12);
    }

    .input-wrapper svg {
      color: var(--adm-sub);
      flex-shrink: 0;
    }

    .input-wrapper input {
      flex: 1;
      border: none;
      background: transparent;
      color: var(--adm-ink);
      font-size: 15px;
      outline: none;
    }

    .input-wrapper input::placeholder {
      color: #94a3b8;
    }

    .toggle-password {
      background: none;
      border: none;
      padding: 0;
      cursor: pointer;
      color: var(--adm-sub);
      display: flex;
      align-items: center;
      transition: color 0.2s;
    }

    .toggle-password:hover {
      color: var(--adm-ink);
    }

    .form-options {
      display: flex;
      align-items: center;
      justify-content: space-between;
    }

    .remember-me {
      display: flex;
      align-items: center;
      gap: 10px;
      font-size: 14px;
      color: var(--adm-sub);
      cursor: pointer;
      position: relative;
      padding-left: 28px;
    }

    .remember-me input {
      position: absolute;
      opacity: 0;
      cursor: pointer;
    }

    .checkmark {
      position: absolute;
      left: 0;
      width: 18px;
      height: 18px;
      background: #f8fafc;
      border: 1px solid #cbd5e1;
      border-radius: 4px;
      transition: all 0.2s;
    }

    .remember-me input:checked ~ .checkmark {
      background: var(--adm-indigo);
      border-color: var(--adm-indigo);
    }

    .checkmark::after {
      content: '';
      position: absolute;
      display: none;
      left: 6px;
      top: 2px;
      width: 4px;
      height: 9px;
      border: solid #fff;
      border-width: 0 2px 2px 0;
      transform: rotate(45deg);
    }

    .remember-me input:checked ~ .checkmark::after {
      display: block;
    }

    .forgot-link {
      font-size: 14px;
      color: var(--adm-indigo);
      font-weight: 500;
      text-decoration: none;
      transition: color 0.2s;
    }

    .forgot-link:hover {
      color: var(--adm-indigo-ink);
    }

    .error-message {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 12px 16px;
      background: rgba(220, 38, 38, 0.08);
      border: 1px solid rgba(220, 38, 38, 0.25);
      border-radius: 10px;
      color: var(--adm-red-ink);
      font-size: 14px;
    }

    .login-btn {
      width: 100%;
      padding: 14px 18px;
      background: var(--adm-indigo);
      border: none;
      border-radius: 10px;
      color: #fff;
      font-size: 15px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
      display: flex;
      align-items: center;
      justify-content: center;
      margin-top: 8px;
    }

    .login-btn:hover:not(:disabled) {
      background: var(--adm-indigo-ink);
      transform: translateY(-1px);
      box-shadow: 0 8px 24px rgba(79, 70, 229, 0.35);
    }

    .login-btn:disabled {
      opacity: 0.7;
      cursor: not-allowed;
    }

    .loading-spinner {
      width: 20px;
      height: 20px;
      border: 2px solid rgba(255, 255, 255, 0.3);
      border-top-color: #fff;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }

    .login-footer {
      margin-top: 32px;
      text-align: center;
    }

    .security-badge {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      padding: 8px 16px;
      background: rgba(79, 70, 229, 0.10);
      border-radius: 999px;
      color: var(--adm-indigo-ink);
      font-size: 12px;
      font-weight: 600;
      margin-bottom: 16px;
    }

    .copyright {
      font-size: 12px;
      color: var(--adm-sub);
      margin: 0;
    }

    .login-info {
      max-width: 460px;
      display: flex;
      flex-direction: column;
      justify-content: center;
      position: relative;
      z-index: 1;
      animation: rise 0.45s ease-out both;
    }

    .login-info h2 {
      font-size: 32px;
      font-weight: 700;
      color: #ffffff;
      margin: 0 0 40px 0;
      letter-spacing: -0.01em;
    }

    .features-list {
      list-style: none;
      padding: 0;
      margin: 0;
      display: flex;
      flex-direction: column;
      gap: 32px;
    }

    .features-list li {
      display: flex;
      align-items: flex-start;
      gap: 20px;
    }

    .features-list li svg {
      flex-shrink: 0;
      color: #a5b4fc;
      padding: 12px;
      background: rgba(99, 102, 241, 0.16);
      border: 1px solid rgba(99, 102, 241, 0.28);
      border-radius: 12px;
    }

    .features-list li h3 {
      font-size: 18px;
      font-weight: 600;
      color: #e2e8f0;
      margin: 0 0 6px 0;
    }

    .features-list li p {
      font-size: 14px;
      color: #94a3b8;
      margin: 0;
      line-height: 1.5;
    }

    @media (prefers-reduced-motion: reduce) {
      .bg-gradient {
        animation: none;
      }
      .login-card,
      .login-info {
        animation: none;
      }
    }

    @media (max-width: 1024px) {
      .login-info {
        display: none;
      }
      .login-card {
        width: 100%;
        max-width: 440px;
      }
    }

    @media (max-width: 480px) {
      .login-container {
        padding: 24px 16px;
      }
      .login-card {
        padding: 32px 24px;
      }
    }
  `]
})
export class AdminLoginComponent {
  email = '';
  password = '';
  showPassword = false;
  rememberMe = false;
  loading = false;
  error = '';

  constructor(
    private router: Router,
    private adminService: AdminService
  ) {
    if (this.adminService.isAuthenticated()) {
      this.router.navigate(['/admin/dashboard']);
    }
  }

  login() {
    if (!this.email || !this.password) {
      this.error = 'Veuillez saisir votre e-mail et votre mot de passe';
      return;
    }

    this.loading = true;
    this.error = '';

    this.adminService.login(this.email, this.password).subscribe({
      next: () => {
        this.router.navigate(['/admin/dashboard']);
      },
      error: (err) => {
        this.error = 'E-mail ou mot de passe incorrect';
        this.loading = false;
      }
    });
  }
}
