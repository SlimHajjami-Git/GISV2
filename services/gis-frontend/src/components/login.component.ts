import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MockDataService } from '../services/mock-data.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="auth-page">
      <div class="auth-container">
        <div class="auth-card">
          <div class="auth-header">
            <div class="logo">
              <svg width="48" height="48" viewBox="0 0 40 40" fill="none">
                <rect width="40" height="40" rx="8" fill="#2563eb"/>
                <path d="M12 20L18 26L28 14" stroke="white" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
              </svg>
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
              <a href="#" class="link">Mot de passe oublié?</a>
            </div>

            <button type="submit" class="btn-primary btn-full">
              Se connecter
            </button>
          </form>

          <div class="auth-footer">
            <p>Pas encore de compte? <a [routerLink]="['/register']" class="link">Créer un compte</a></p>
          </div>
        </div>

        <div class="auth-illustration">
          <div class="illustration-content">
            <h2>Gérez votre flotte en toute simplicité</h2>
            <div class="features-list">
              <div class="feature-item">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none">
                  <circle cx="12" cy="12" r="10" stroke="#2563eb" stroke-width="2"/>
                  <path d="M8 12L11 15L16 9" stroke="#2563eb" stroke-width="2" stroke-linecap="round"/>
                </svg>
                <span>Suivi GPS en temps réel</span>
              </div>
              <div class="feature-item">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none">
                  <circle cx="12" cy="12" r="10" stroke="#2563eb" stroke-width="2"/>
                  <path d="M8 12L11 15L16 9" stroke="#2563eb" stroke-width="2" stroke-linecap="round"/>
                </svg>
                <span>Gestion complète du parc</span>
              </div>
              <div class="feature-item">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none">
                  <circle cx="12" cy="12" r="10" stroke="#2563eb" stroke-width="2"/>
                  <path d="M8 12L11 15L16 9" stroke="#2563eb" stroke-width="2" stroke-linecap="round"/>
                </svg>
                <span>Rapports et analyses</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .auth-page {
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
      padding: 24px;
    }

    .auth-container {
      display: grid;
      grid-template-columns: 1fr 1fr;
      max-width: 1000px;
      width: 100%;
      background: white;
      border-radius: 16px;
      overflow: hidden;
      box-shadow: 0 20px 60px rgba(0, 0, 0, 0.3);
    }

    .auth-card {
      padding: 48px;
    }

    .auth-header {
      text-align: center;
      margin-bottom: 32px;
    }

    .logo {
      display: flex;
      justify-content: center;
      margin-bottom: 24px;
    }

    .auth-header h1 {
      font-size: 32px;
      font-weight: 800;
      color: #111827;
      margin: 0 0 8px;
    }

    .auth-header p {
      color: #6b7280;
      margin: 0;
    }

    .auth-form {
      margin-bottom: 24px;
    }

    .form-group {
      margin-bottom: 20px;
    }

    .form-group label {
      display: block;
      font-weight: 600;
      color: #374151;
      margin-bottom: 8px;
      font-size: 14px;
    }

    .form-group input {
      width: 100%;
      padding: 12px 16px;
      border: 2px solid #e5e7eb;
      border-radius: 8px;
      font-size: 16px;
      transition: all 0.2s;
      box-sizing: border-box;
    }

    .form-group input:focus {
      outline: none;
      border-color: #2563eb;
      box-shadow: 0 0 0 3px rgba(37, 99, 235, 0.1);
    }

    .form-options {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 24px;
    }

    .checkbox-label {
      display: flex;
      align-items: center;
      gap: 8px;
      cursor: pointer;
      font-size: 14px;
      color: #374151;
    }

    .checkbox-label input[type="checkbox"] {
      width: 16px;
      height: 16px;
      cursor: pointer;
    }

    .link {
      color: #2563eb;
      text-decoration: none;
      font-weight: 600;
      font-size: 14px;
    }

    .link:hover {
      text-decoration: underline;
    }

    .btn-primary {
      width: 100%;
      padding: 14px;
      background: #2563eb;
      color: white;
      border: none;
      border-radius: 8px;
      font-size: 16px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-primary:hover {
      background: #1d4ed8;
      transform: translateY(-1px);
      box-shadow: 0 4px 12px rgba(37, 99, 235, 0.3);
    }

    .btn-full {
      width: 100%;
    }

    .auth-footer {
      text-align: center;
      padding-top: 24px;
      border-top: 1px solid #e5e7eb;
    }

    .auth-footer p {
      color: #6b7280;
      font-size: 14px;
    }

    .auth-illustration {
      background: linear-gradient(135deg, #2563eb 0%, #1d4ed8 100%);
      padding: 48px;
      display: flex;
      align-items: center;
      justify-content: center;
      color: white;
    }

    .illustration-content h2 {
      font-size: 32px;
      font-weight: 800;
      margin: 0 0 32px;
      line-height: 1.3;
    }

    .features-list {
      display: flex;
      flex-direction: column;
      gap: 20px;
    }

    .feature-item {
      display: flex;
      align-items: center;
      gap: 16px;
      font-size: 18px;
    }

    .feature-item svg {
      flex-shrink: 0;
    }

    .feature-item path,
    .feature-item circle {
      stroke: white;
    }

    @media (max-width: 968px) {
      .auth-container {
        grid-template-columns: 1fr;
      }

      .auth-illustration {
        display: none;
      }
    }
  `]
})
export class LoginComponent {
  email = '';
  password = '';
  rememberMe = false;

  constructor(
    private router: Router,
    private dataService: MockDataService
  ) {}

  onSubmit() {
    if (this.dataService.login(this.email, this.password)) {
      this.router.navigate(['/dashboard']);
    }
  }
}
