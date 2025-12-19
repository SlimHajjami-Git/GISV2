import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { Subscription } from '../models/types';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="auth-page">
      <div class="auth-container large">
        <div class="auth-card">
          <div class="auth-header">
            <div class="logo">
              <svg width="48" height="48" viewBox="0 0 40 40" fill="none">
                <rect width="40" height="40" rx="8" fill="#2563eb"/>
                <path d="M12 20L18 26L28 14" stroke="white" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
              </svg>
            </div>
            <h1>Créer un compte</h1>
            <p>Commencez à gérer votre flotte dès aujourd'hui</p>
          </div>

          <form (ngSubmit)="onSubmit()" class="auth-form">
            <div class="form-row">
              <div class="form-group">
                <label for="name">Nom complet</label>
                <input
                  type="text"
                  id="name"
                  [(ngModel)]="formData.name"
                  name="name"
                  placeholder="Mohamed Ben Ali"
                  required
                />
              </div>

              <div class="form-group">
                <label for="email">Email</label>
                <input
                  type="email"
                  id="email"
                  [(ngModel)]="formData.email"
                  name="email"
                  placeholder="jean@entreprise.com"
                  required
                />
              </div>
            </div>

            <div class="form-row">
              <div class="form-group">
                <label for="companyName">Nom de l'entreprise</label>
                <input
                  type="text"
                  id="companyName"
                  [(ngModel)]="formData.companyName"
                  name="companyName"
                  placeholder="Mon Entreprise"
                  required
                />
              </div>

              <div class="form-group">
                <label for="companyType">Type d'entreprise</label>
                <select
                  id="companyType"
                  [(ngModel)]="formData.companyType"
                  name="companyType"
                  required
                >
                  <option value="">Sélectionner...</option>
                  <option value="location">Location automobile</option>
                  <option value="transport">Transport</option>
                  <option value="other">Autre</option>
                </select>
              </div>
            </div>

            <div class="form-group">
              <label for="subscription">Formule d'abonnement</label>
              <div class="subscription-options">
                <div
                  *ngFor="let sub of subscriptions"
                  class="subscription-card"
                  [class.selected]="formData.subscriptionId === sub.id"
                  (click)="selectSubscription(sub.id)"
                >
                  <div class="subscription-header">
                    <h3>{{ sub.name }}</h3>
                    <div class="subscription-price">{{ sub.price }}€/mois</div>
                  </div>
                  <ul class="subscription-features">
                    <li *ngFor="let feature of sub.features.slice(0, 3)">{{ feature }}</li>
                  </ul>
                </div>
              </div>
            </div>

            <div class="form-group">
              <label for="maxVehicles">Nombre maximum de véhicules</label>
              <select
                id="maxVehicles"
                [(ngModel)]="formData.maxVehicles"
                name="maxVehicles"
                required
              >
                <option value="">Sélectionner...</option>
                <option value="5">Jusqu'à 5 véhicules</option>
                <option value="10">Jusqu'à 10 véhicules</option>
                <option value="20">Jusqu'à 20 véhicules</option>
                <option value="50">Jusqu'à 50 véhicules</option>
                <option value="100">Jusqu'à 100 véhicules</option>
              </select>
            </div>

            <div class="form-group">
              <label for="password">Mot de passe</label>
              <input
                type="password"
                id="password"
                [(ngModel)]="formData.password"
                name="password"
                placeholder="••••••••"
                required
              />
            </div>

            <div class="form-group">
              <label class="checkbox-label">
                <input type="checkbox" [(ngModel)]="formData.acceptTerms" name="acceptTerms" required />
                <span>J'accepte les <a href="#" class="link">conditions d'utilisation</a> et la <a href="#" class="link">politique de confidentialité</a></span>
              </label>
            </div>

            <button type="submit" class="btn-primary btn-full">
              Créer mon compte
            </button>
          </form>

          <div class="auth-footer">
            <p>Vous avez déjà un compte? <a [routerLink]="['/login']" class="link">Se connecter</a></p>
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
      max-width: 600px;
      width: 100%;
      background: white;
      border-radius: 16px;
      overflow: hidden;
      box-shadow: 0 20px 60px rgba(0, 0, 0, 0.3);
    }

    .auth-container.large {
      max-width: 800px;
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

    .form-row {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 16px;
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

    .form-group input,
    .form-group select {
      width: 100%;
      padding: 12px 16px;
      border: 2px solid #e5e7eb;
      border-radius: 8px;
      font-size: 16px;
      transition: all 0.2s;
      box-sizing: border-box;
    }

    .form-group input:focus,
    .form-group select:focus {
      outline: none;
      border-color: #2563eb;
      box-shadow: 0 0 0 3px rgba(37, 99, 235, 0.1);
    }

    .subscription-options {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 16px;
      margin-top: 12px;
    }

    .subscription-card {
      border: 2px solid #e5e7eb;
      border-radius: 12px;
      padding: 20px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .subscription-card:hover {
      border-color: #2563eb;
      transform: translateY(-2px);
    }

    .subscription-card.selected {
      border-color: #2563eb;
      background: #eff6ff;
    }

    .subscription-header {
      margin-bottom: 12px;
    }

    .subscription-header h3 {
      font-size: 16px;
      font-weight: 700;
      color: #111827;
      margin: 0 0 8px;
    }

    .subscription-price {
      font-size: 20px;
      font-weight: 800;
      color: #2563eb;
    }

    .subscription-features {
      list-style: none;
      padding: 0;
      margin: 0;
    }

    .subscription-features li {
      font-size: 12px;
      color: #6b7280;
      padding: 4px 0;
    }

    .checkbox-label {
      display: flex;
      align-items: flex-start;
      gap: 8px;
      cursor: pointer;
      font-size: 14px;
      color: #374151;
    }

    .checkbox-label input[type="checkbox"] {
      margin-top: 2px;
      width: 16px;
      height: 16px;
      cursor: pointer;
      flex-shrink: 0;
    }

    .link {
      color: #2563eb;
      text-decoration: none;
      font-weight: 600;
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

    @media (max-width: 768px) {
      .form-row,
      .subscription-options {
        grid-template-columns: 1fr;
      }
    }
  `]
})
export class RegisterComponent {
  subscriptions: Subscription[] = [
    { id: 'sub1', name: 'Parc Automobile', type: 'parc', price: 49.99, features: [], gpsTracking: false, gpsInstallation: false },
    { id: 'sub2', name: 'Parc + GPS', type: 'parc_gps', price: 99.99, features: [], gpsTracking: true, gpsInstallation: false },
    { id: 'sub3', name: 'Parc + GPS + Install', type: 'parc_gps_install', price: 149.99, features: [], gpsTracking: true, gpsInstallation: true }
  ];
  formData = {
    name: '',
    email: '',
    companyName: '',
    companyType: '',
    subscriptionId: '',
    maxVehicles: '',
    password: '',
    acceptTerms: false
  };
  isLoading = false;
  errorMessage = '';

  constructor(
    private router: Router,
    private route: ActivatedRoute,
    private authService: AuthService
  ) {
    this.route.queryParams.subscribe(params => {
      if (params['plan']) {
        this.formData.subscriptionId = params['plan'];
      }
    });
  }

  selectSubscription(id: string) {
    this.formData.subscriptionId = id;
  }

  onSubmit() {
    if (!this.formData.acceptTerms) return;
    
    this.isLoading = true;
    this.errorMessage = '';
    
    this.authService.register(
      this.formData.name,
      this.formData.email,
      this.formData.password,
      this.formData.companyName
    ).subscribe({
      next: (user) => {
        this.isLoading = false;
        if (user) {
          this.router.navigate(['/dashboard']);
        } else {
          this.errorMessage = 'Erreur lors de la création du compte';
        }
      },
      error: () => {
        this.isLoading = false;
        this.errorMessage = 'Erreur de connexion au serveur';
      }
    });
  }
}
