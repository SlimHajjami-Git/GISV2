import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MockDataService } from '../services/mock-data.service';
import { AppLayoutComponent } from './shared/app-layout.component';
import { Company } from '../models/types';

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent],
  template: `
    <app-layout>
      <div class="profile-page">
        <div class="page-header">
          <h1>Mon Profil</h1>
          <p class="subtitle">Gérez vos informations personnelles et préférences</p>
        </div>

        <div class="profile-content">
          <!-- 4 Cards Grid -->
          <div class="cards-grid-4">
            <!-- Top Left: Personal Information -->
            <div class="profile-card">
              <div class="card-title">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/>
                  <circle cx="12" cy="7" r="4"/>
                </svg>
                <h3>Informations personnelles</h3>
              </div>
              <div class="card-body">
                <div class="avatar-row">
                  <div class="avatar-large">{{ getInitials() }}</div>
                  <div class="avatar-info">
                    <span class="role-badge">{{ profile.role }}</span>
                    <button class="btn-change-avatar">
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z"/>
                        <circle cx="12" cy="13" r="4"/>
                      </svg>
                      Changer photo
                    </button>
                  </div>
                </div>
                <div class="form-group">
                  <label>Nom complet</label>
                  <input type="text" [(ngModel)]="profile.fullName" placeholder="Votre nom">
                </div>
                <div class="form-group">
                  <label>Email</label>
                  <input type="email" [(ngModel)]="profile.email" placeholder="votre@email.com">
                </div>
                <div class="form-group">
                  <label>Téléphone</label>
                  <input type="tel" [(ngModel)]="profile.phone" placeholder="+212 6XX XXX XXX">
                </div>
                <div class="form-group">
                  <label>Poste</label>
                  <input type="text" [(ngModel)]="profile.position" placeholder="Gestionnaire de flotte">
                </div>
              </div>
            </div>

            <!-- Top Right: Company Information -->
            <div class="profile-card">
              <div class="card-title">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>
                  <polyline points="9 22 9 12 15 12 15 22"/>
                </svg>
                <h3>Entreprise</h3>
              </div>
              <div class="card-body">
                <div class="form-group">
                  <label>Nom de l'entreprise</label>
                  <input type="text" [value]="company?.name" disabled>
                </div>
                <div class="form-group">
                  <label>Secteur d'activité</label>
                  <select [(ngModel)]="profile.industry">
                    <option value="transport">Transport & Logistique</option>
                    <option value="construction">Construction</option>
                    <option value="livraison">Livraison</option>
                    <option value="location">Location de véhicules</option>
                    <option value="autre">Autre</option>
                  </select>
                </div>
                <div class="form-group">
                  <label>Adresse</label>
                  <input type="text" [(ngModel)]="profile.address" placeholder="Adresse de l'entreprise">
                </div>
                <div class="form-group">
                  <label>Ville</label>
                  <input type="text" [(ngModel)]="profile.city" placeholder="Casablanca">
                </div>
                <div class="form-group">
                  <label>Pays</label>
                  <select [(ngModel)]="profile.country">
                    <option value="MA">🇲🇦 Maroc</option>
                    <option value="FR">🇫🇷 France</option>
                    <option value="TN">🇹🇳 Tunisie</option>
                    <option value="DZ">🇩🇿 Algérie</option>
                    <option value="SA">🇸🇦 Arabie Saoudite</option>
                    <option value="AE">🇦🇪 Émirats Arabes Unis</option>
                  </select>
                </div>
              </div>
            </div>

            <!-- Bottom Left: Regional Settings -->
            <div class="profile-card">
              <div class="card-title">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="12" cy="12" r="10"/>
                  <line x1="2" y1="12" x2="22" y2="12"/>
                  <path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"/>
                </svg>
                <h3>Paramètres régionaux</h3>
              </div>
              <div class="card-body">
                <div class="form-group">
                  <label>Langue</label>
                  <select [(ngModel)]="profile.language">
                    <option value="fr">🇫🇷 Français</option>
                    <option value="en">🇬🇧 English</option>
                    <option value="ar">🇸🇦 العربية</option>
                  </select>
                </div>
                <div class="form-group">
                  <label>Fuseau horaire</label>
                  <select [(ngModel)]="profile.timezone">
                    <option value="Africa/Casablanca">Casablanca (GMT+1)</option>
                    <option value="Europe/Paris">Paris (GMT+1)</option>
                    <option value="UTC">UTC</option>
                  </select>
                </div>
                <div class="form-group">
                  <label>Monnaie</label>
                  <select [(ngModel)]="profile.currency">
                    <option value="MAD">🇲🇦 Dirham Marocain (MAD)</option>
                    <option value="EUR">🇪🇺 Euro (EUR)</option>
                    <option value="USD">🇺🇸 Dollar US (USD)</option>
                    <option value="GBP">🇬🇧 Livre Sterling (GBP)</option>
                    <option value="TND">🇹🇳 Dinar Tunisien (TND)</option>
                    <option value="DZD">🇩🇿 Dinar Algérien (DZD)</option>
                    <option value="SAR">🇸🇦 Riyal Saoudien (SAR)</option>
                    <option value="AED">🇦🇪 Dirham Émirati (AED)</option>
                  </select>
                </div>
                <div class="form-group">
                  <label>Format de date</label>
                  <select [(ngModel)]="profile.dateFormat">
                    <option value="dd/MM/yyyy">DD/MM/YYYY</option>
                    <option value="MM/dd/yyyy">MM/DD/YYYY</option>
                    <option value="yyyy-MM-dd">YYYY-MM-DD</option>
                  </select>
                </div>
              </div>
            </div>

            <!-- Bottom Right: Units & Measurements -->
            <div class="profile-card">
              <div class="card-title">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M22 12h-4l-3 9L9 3l-3 9H2"/>
                </svg>
                <h3>Unités de mesure</h3>
              </div>
              <div class="card-body">
                <div class="form-group">
                  <label>Distance</label>
                  <select [(ngModel)]="profile.distanceUnit">
                    <option value="km">Kilomètres (km)</option>
                    <option value="mi">Miles (mi)</option>
                  </select>
                </div>
                <div class="form-group">
                  <label>Vitesse</label>
                  <select [(ngModel)]="profile.speedUnit">
                    <option value="kmh">km/h</option>
                    <option value="mph">mph</option>
                  </select>
                </div>
                <div class="form-group">
                  <label>Volume (carburant)</label>
                  <select [(ngModel)]="profile.volumeUnit">
                    <option value="L">Litres (L)</option>
                    <option value="gal">Gallons (gal)</option>
                  </select>
                </div>
                <div class="form-group">
                  <label>Température</label>
                  <select [(ngModel)]="profile.temperatureUnit">
                    <option value="C">Celsius (°C)</option>
                    <option value="F">Fahrenheit (°F)</option>
                  </select>
                </div>
              </div>
            </div>
          </div>

          <!-- Actions -->
          <div class="actions-bar">
            <button class="btn-secondary" (click)="resetForm()">Annuler les modifications</button>
            <button class="btn-primary" (click)="saveProfile()">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/>
                <polyline points="17 21 17 13 7 13 7 21"/>
                <polyline points="7 3 7 8 15 8"/>
              </svg>
              Enregistrer
            </button>
          </div>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    .profile-page {
      padding: 24px;
      width: 100%;
      max-width: 100%;
    }

    .page-header {
      margin-bottom: 24px;
    }

    .page-header h1 {
      font-size: 24px;
      font-weight: 700;
      color: #1f2937;
      margin: 0 0 4px 0;
    }

    .subtitle {
      color: #6b7280;
      font-size: 14px;
      margin: 0;
    }

    .profile-content {
      display: flex;
      flex-direction: column;
      gap: 24px;
    }

    /* 4 Cards Grid */
    .cards-grid-4 {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 20px;
    }

    /* Avatar Row in Personal Info */
    .avatar-row {
      display: flex;
      align-items: center;
      gap: 16px;
      margin-bottom: 8px;
    }

    .avatar-large {
      width: 64px;
      height: 64px;
      border-radius: 12px;
      background: linear-gradient(135deg, #6366f1 0%, #8b5cf6 100%);
      color: white;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 22px;
      font-weight: 700;
      flex-shrink: 0;
    }

    .avatar-info {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .role-badge {
      display: inline-block;
      padding: 4px 10px;
      background: #e0e7ff;
      color: #4f46e5;
      border-radius: 20px;
      font-size: 11px;
      font-weight: 600;
      width: fit-content;
    }

    .btn-change-avatar {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 6px 10px;
      background: #f3f4f6;
      border: 1px solid #e5e7eb;
      border-radius: 6px;
      color: #4b5563;
      font-size: 12px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-change-avatar:hover {
      background: #e5e7eb;
      color: #1f2937;
    }

    .profile-card {
      background: white;
      border-radius: 12px;
      border: 1px solid #e5e7eb;
      overflow: hidden;
    }

    .card-title {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 16px 20px;
      border-bottom: 1px solid #e5e7eb;
      background: #f9fafb;
    }

    .card-title svg {
      color: #6366f1;
    }

    .card-title h3 {
      margin: 0;
      font-size: 15px;
      font-weight: 600;
      color: #1f2937;
    }

    .card-body {
      padding: 20px;
      display: flex;
      flex-direction: column;
      gap: 12px;
    }

    .form-group {
      display: flex;
      flex-direction: column;
      gap: 6px;
    }

    .form-group label {
      font-size: 13px;
      font-weight: 500;
      color: #374151;
    }

    .form-group input,
    .form-group select {
      padding: 10px 12px;
      border: 1px solid #d1d5db;
      border-radius: 8px;
      font-size: 14px;
      color: #1f2937;
      background: white;
      transition: border-color 0.2s, box-shadow 0.2s;
    }

    .form-group input:focus,
    .form-group select:focus {
      outline: none;
      border-color: #6366f1;
      box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.1);
    }

    .form-group input:disabled {
      background: #f3f4f6;
      color: #6b7280;
      cursor: not-allowed;
    }

    /* Actions Bar */
    .actions-bar {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding-top: 8px;
    }

    .btn-primary,
    .btn-secondary {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 20px;
      border-radius: 8px;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-primary {
      background: #6366f1;
      color: white;
      border: none;
    }

    .btn-primary:hover {
      background: #4f46e5;
    }

    .btn-secondary {
      background: white;
      color: #374151;
      border: 1px solid #d1d5db;
    }

    .btn-secondary:hover {
      background: #f9fafb;
    }

    /* Responsive */
    @media (max-width: 600px) {
      .profile-page {
        padding: 16px;
      }

      .card-header {
        flex-direction: column;
        text-align: center;
      }

      .actions-bar {
        flex-direction: column;
      }

      .btn-primary,
      .btn-secondary {
        width: 100%;
        justify-content: center;
      }
    }
  `]
})
export class ProfileComponent implements OnInit {
  company: Company | null = null;

  profile = {
    fullName: '',
    email: '',
    phone: '+212 661 234 567',
    position: 'Gestionnaire de flotte',
    role: 'Administrateur',
    industry: 'transport',
    address: '123 Boulevard Mohammed V',
    city: 'Casablanca',
    country: 'MA',
    language: 'fr',
    timezone: 'Africa/Casablanca',
    currency: 'MAD',
    dateFormat: 'dd/MM/yyyy',
    distanceUnit: 'km',
    speedUnit: 'kmh',
    volumeUnit: 'L',
    temperatureUnit: 'C'
  };

  constructor(
    private router: Router,
    private dataService: MockDataService
  ) {}

  ngOnInit() {
    if (!this.dataService.isAuthenticated()) {
      this.router.navigate(['/login']);
      return;
    }

    this.company = this.dataService.getCurrentCompany();
    if (this.company) {
      this.profile.fullName = this.company.name + ' Admin';
      this.profile.email = 'admin@' + this.company.name.toLowerCase().replace(/\s/g, '') + '.com';
    }
  }

  getInitials(): string {
    return this.profile.fullName
      .split(' ')
      .map(n => n[0])
      .join('')
      .substring(0, 2)
      .toUpperCase();
  }

  saveProfile() {
    // Save to localStorage for persistence
    localStorage.setItem('userProfile', JSON.stringify(this.profile));
    alert('Profil enregistré avec succès!');
  }

  resetForm() {
    this.ngOnInit();
  }
}
