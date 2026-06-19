import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { ApiService } from '../services/api.service';
import { UserPreferencesService } from '../services/user-preferences.service';
import { AppLayoutComponent } from './shared/app-layout.component';

interface ProfileModel {
  fullName: string;
  email: string;
  phone: string;
  position: string;
  role: string;
  industry: string;
  address: string;
  city: string;
  country: string;
  language: string;
  timezone: string;
  currency: string;
  dateFormat: string;
  distanceUnit: string;
  speedUnit: string;
  volumeUnit: string;
  temperatureUnit: string;
}

const PREFERENCES_KEY = 'userProfilePreferences';
const ALERT_PREFS_KEY = 'userAlertPreferences';

const DEFAULT_PREFERENCES: Partial<ProfileModel> = {
  position: '',
  industry: 'transport',
  address: '',
  city: '',
  country: 'TN',
  language: 'fr',
  timezone: 'Africa/Tunis',
  dateFormat: 'dd/MM/yyyy',
  distanceUnit: 'km',
  speedUnit: 'kmh',
  volumeUnit: 'L',
  temperatureUnit: 'C'
};

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, AppLayoutComponent],
  template: `
    <app-layout>
      <div class="profile-page">
        <!-- Hero identity banner -->
        <section class="identity-hero">
          <div class="hero-pattern"></div>
          <div class="hero-content">
            <div class="identity-left">
              <div class="avatar-halo">
                <div class="avatar-disc">{{ getInitials() }}</div>
                <button class="avatar-upload" type="button" title="Changer la photo de profil">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                    <path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z"/>
                    <circle cx="12" cy="13" r="4"/>
                  </svg>
                </button>
              </div>
              <div class="identity-meta">
                <span class="eyebrow">Mon profil</span>
                <h1 class="display-name">{{ profile.fullName || 'Sans nom' }}</h1>
                <div class="identity-chips">
                  <span class="chip chip-role">{{ profile.role }}</span>
                  <span class="chip chip-company" *ngIf="company?.name">
                    <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
                      <path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>
                    </svg>
                    {{ company?.name }}
                  </span>
                  <span class="chip chip-email" *ngIf="profile.email">{{ profile.email }}</span>
                </div>
              </div>
            </div>
            <div class="identity-right">
              <div class="stat-block">
                <span class="stat-label">Statut</span>
                <span class="stat-value"><span class="dot"></span>Actif</span>
              </div>
            </div>
          </div>
        </section>

        <!-- Main form sections -->
        <div class="profile-grid">
          <!-- Personal Info -->
          <article class="section-card" style="--stagger:1">
            <header class="section-head">
              <div class="section-medal gradient-sky">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/>
                  <circle cx="12" cy="7" r="4"/>
                </svg>
              </div>
              <div class="section-text">
                <h2>Informations personnelles</h2>
                <p>Ces informations sont visibles par votre équipe.</p>
              </div>
            </header>
            <div class="section-body">
              <div class="field">
                <label>Nom complet</label>
                <input type="text" [(ngModel)]="profile.fullName" placeholder="Votre nom">
              </div>
              <div class="field">
                <label>Adresse email</label>
                <input type="email" [(ngModel)]="profile.email" placeholder="votre@email.com">
              </div>
              <div class="field-row">
                <div class="field">
                  <label>Téléphone</label>
                  <input type="tel" [(ngModel)]="profile.phone" placeholder="+216 XX XXX XXX">
                </div>
                <div class="field">
                  <label>Poste</label>
                  <input type="text" [(ngModel)]="profile.position" placeholder="Ex. Gestionnaire de flotte">
                </div>
              </div>
            </div>
          </article>

          <!-- Company -->
          <article class="section-card" style="--stagger:2">
            <header class="section-head">
              <div class="section-medal gradient-amber">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>
                  <polyline points="9 22 9 12 15 12 15 22"/>
                </svg>
              </div>
              <div class="section-text">
                <h2>Entreprise</h2>
                <p>Informations sur votre société.</p>
              </div>
            </header>
            <div class="section-body">
              <div class="field">
                <label>Nom de l'entreprise</label>
                <input type="text" [value]="company?.name || ''" disabled>
              </div>
              <div class="field">
                <label>Secteur d'activité</label>
                <select [(ngModel)]="profile.industry">
                  <option value="transport">Transport & Logistique</option>
                  <option value="construction">Construction</option>
                  <option value="livraison">Livraison</option>
                  <option value="location">Location de véhicules</option>
                  <option value="autre">Autre</option>
                </select>
              </div>
              <div class="field">
                <label>Adresse</label>
                <input type="text" [(ngModel)]="profile.address" placeholder="Adresse de l'entreprise">
              </div>
              <div class="field-row">
                <div class="field">
                  <label>Ville</label>
                  <input type="text" [(ngModel)]="profile.city" placeholder="Tunis">
                </div>
                <div class="field">
                  <label>Pays</label>
                  <select [(ngModel)]="profile.country">
                    <option value="TN">Tunisie</option>
                    <option value="MA">Maroc</option>
                    <option value="DZ">Algérie</option>
                    <option value="FR">France</option>
                    <option value="SA">Arabie Saoudite</option>
                    <option value="AE">Émirats Arabes Unis</option>
                  </select>
                </div>
              </div>
            </div>
          </article>

          <!-- Regional -->
          <article class="section-card" style="--stagger:3">
            <header class="section-head">
              <div class="section-medal gradient-violet">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                  <circle cx="12" cy="12" r="10"/>
                  <line x1="2" y1="12" x2="22" y2="12"/>
                  <path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"/>
                </svg>
              </div>
              <div class="section-text">
                <h2>Paramètres régionaux</h2>
                <p>Langue, fuseau horaire et format local.</p>
              </div>
            </header>
            <div class="section-body">
              <div class="field-row">
                <div class="field">
                  <label>Langue</label>
                  <select [(ngModel)]="profile.language">
                    <option value="fr">Français</option>
                    <option value="en">English</option>
                    <option value="ar">العربية</option>
                  </select>
                </div>
                <div class="field">
                  <label>Fuseau horaire</label>
                  <select [(ngModel)]="profile.timezone">
                    <option value="Africa/Tunis">Tunis (GMT+1)</option>
                    <option value="Africa/Casablanca">Casablanca (GMT+1)</option>
                    <option value="Europe/Paris">Paris (GMT+1)</option>
                    <option value="UTC">UTC</option>
                  </select>
                </div>
              </div>
              <div class="field-row">
                <div class="field">
                  <label>Monnaie</label>
                  <select [(ngModel)]="profile.currency">
                    <option value="TND">Dinar Tunisien (TND)</option>
                    <option value="MAD">Dirham Marocain (MAD)</option>
                    <option value="DZD">Dinar Algérien (DZD)</option>
                    <option value="EUR">Euro (EUR)</option>
                    <option value="USD">Dollar US (USD)</option>
                    <option value="SAR">Riyal Saoudien (SAR)</option>
                    <option value="AED">Dirham Émirati (AED)</option>
                  </select>
                </div>
                <div class="field">
                  <label>Format de date</label>
                  <select [(ngModel)]="profile.dateFormat">
                    <option value="dd/MM/yyyy">JJ/MM/AAAA</option>
                    <option value="MM/dd/yyyy">MM/JJ/AAAA</option>
                    <option value="yyyy-MM-dd">AAAA-MM-JJ</option>
                  </select>
                </div>
              </div>
            </div>
          </article>

          <!-- Units -->
          <article class="section-card" style="--stagger:4">
            <header class="section-head">
              <div class="section-medal gradient-emerald">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M22 12h-4l-3 9L9 3l-3 9H2"/>
                </svg>
              </div>
              <div class="section-text">
                <h2>Unités de mesure</h2>
                <p>Conventions utilisées dans les rapports.</p>
              </div>
            </header>
            <div class="section-body">
              <div class="field-row">
                <div class="field">
                  <label>Distance</label>
                  <select [(ngModel)]="profile.distanceUnit">
                    <option value="km">Kilomètres (km)</option>
                    <option value="mi">Miles (mi)</option>
                  </select>
                </div>
                <div class="field">
                  <label>Vitesse</label>
                  <select [(ngModel)]="profile.speedUnit">
                    <option value="kmh">km/h</option>
                    <option value="mph">mph</option>
                  </select>
                </div>
              </div>
              <div class="field-row">
                <div class="field">
                  <label>Volume (carburant)</label>
                  <select [(ngModel)]="profile.volumeUnit">
                    <option value="L">Litres (L)</option>
                    <option value="gal">Gallons (gal)</option>
                  </select>
                </div>
                <div class="field">
                  <label>Température</label>
                  <select [(ngModel)]="profile.temperatureUnit">
                    <option value="C">Celsius (°C)</option>
                    <option value="F">Fahrenheit (°F)</option>
                  </select>
                </div>
              </div>
            </div>
          </article>
        </div>

        <!-- Alerts preferences (tabs: Vitesse / Géofencing+Documents+Remorquage / Offline / Entretiens) -->
        <article class="section-card" style="--stagger:5; margin-top:20px;">
          <header class="section-head">
            <div class="section-medal gradient-violet">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/>
                <path d="M13.73 21a2 2 0 0 1-3.46 0"/>
              </svg>
            </div>
            <div class="section-text">
              <h2>Notifications</h2>
              <p>Choisissez les alertes à recevoir dans la cloche.</p>
            </div>
          </header>
          <div class="section-body">
            <div class="alert-grid">
              <label class="alert-check">
                <input type="checkbox" [(ngModel)]="alertPrefs.vitesse">
                <div class="alert-info">
                  <span class="alert-title">🏎️ Vitesse</span>
                  <span class="alert-desc">Excès de vitesse / infractions</span>
                </div>
              </label>
              <label class="alert-check">
                <input type="checkbox" [(ngModel)]="alertPrefs.geofencingDocsTow">
                <div class="alert-info">
                  <span class="alert-title">🗺️ Géofencing + Documents + Remorquage</span>
                  <span class="alert-desc">Franchissement zone, échéance doc, remorquage</span>
                </div>
              </label>
              <label class="alert-check">
                <input type="checkbox" [(ngModel)]="alertPrefs.offline">
                <div class="alert-info">
                  <span class="alert-title">🔌 Offline</span>
                  <span class="alert-desc">Véhicules hors ligne</span>
                </div>
              </label>
              <label class="alert-check">
                <input type="checkbox" [(ngModel)]="alertPrefs.entretiens">
                <div class="alert-info">
                  <span class="alert-title">🔧 Entretiens</span>
                  <span class="alert-desc">Rappels d'entretien</span>
                </div>
              </label>
            </div>
          </div>
        </article>

        <!-- Password change -->
        <article class="section-card" style="--stagger:6; margin-top:20px;">
          <header class="section-head">
            <div class="section-medal gradient-amber">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                <rect x="3" y="11" width="18" height="11" rx="2" ry="2"/>
                <path d="M7 11V7a5 5 0 0 1 10 0v4"/>
              </svg>
            </div>
            <div class="section-text">
              <h2>Mot de passe</h2>
              <p>Modifiez votre mot de passe en toute sécurité.</p>
            </div>
          </header>
          <div class="section-body">
            <div class="field">
              <label>Mot de passe actuel</label>
              <input type="password" [(ngModel)]="passwordForm.current" placeholder="••••••••" autocomplete="current-password">
            </div>
            <div class="field-row">
              <div class="field">
                <label>Nouveau mot de passe</label>
                <input type="password" [(ngModel)]="passwordForm.next" placeholder="6 caractères minimum" autocomplete="new-password">
              </div>
              <div class="field">
                <label>Confirmation</label>
                <input type="password" [(ngModel)]="passwordForm.confirm" placeholder="Répéter le mot de passe" autocomplete="new-password">
              </div>
            </div>
            <div class="pwd-actions">
              <button class="btn-save" (click)="changePassword()" [disabled]="changingPassword">
                <span *ngIf="!changingPassword" class="save-content">
                  Changer le mot de passe
                </span>
                <span *ngIf="changingPassword" class="save-content">
                  <span class="spinner"></span> Modification...
                </span>
              </button>
            </div>
            <div *ngIf="passwordMessage" class="inline-feedback" [class.error]="passwordMessage.type === 'error'">
              {{ passwordMessage.text }}
            </div>
          </div>
        </article>

        <!-- Rapport d'accident quick link -->
        <article class="section-card" style="--stagger:7; margin-top:20px;">
          <header class="section-head">
            <div class="section-medal gradient-amber" style="background:linear-gradient(140deg,#f87171 0%,#dc2626 100%);">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/>
                <line x1="12" y1="9" x2="12" y2="13"/>
                <line x1="12" y1="17" x2="12.01" y2="17"/>
              </svg>
            </div>
            <div class="section-text">
              <h2>Rapport d'accident</h2>
              <p>Accédez aux rapports d'accidents détectés automatiquement.</p>
            </div>
          </header>
          <div class="section-body">
            <a routerLink="/accidents" class="btn-save" style="text-decoration:none; display:inline-flex; width:fit-content;">
              Ouvrir les rapports
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                <line x1="5" y1="12" x2="19" y2="12"/>
                <polyline points="12 5 19 12 12 19"/>
              </svg>
            </a>
          </div>
        </article>

        <!-- Floating toast -->
        <div class="toast-slot" *ngIf="message" [class.toast-error]="message.type === 'error'">
          <div class="toast-icon" *ngIf="message.type === 'success'">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
              <polyline points="20 6 9 17 4 12"/>
            </svg>
          </div>
          <div class="toast-icon" *ngIf="message.type === 'error'">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
              <circle cx="12" cy="12" r="10"/>
              <line x1="12" y1="8" x2="12" y2="12"/>
              <line x1="12" y1="16" x2="12.01" y2="16"/>
            </svg>
          </div>
          {{ message.text }}
        </div>

        <!-- Sticky action bar -->
        <div class="action-bar">
          <span class="dirty-note">Les modifications s'appliquent après enregistrement.</span>
          <div class="action-buttons">
            <button class="btn-ghost" (click)="resetForm()" [disabled]="saving">Annuler</button>
            <button class="btn-save" (click)="saveProfile()" [disabled]="saving">
              <span *ngIf="!saving" class="save-content">
                Enregistrer
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                  <line x1="5" y1="12" x2="19" y2="12"/>
                  <polyline points="12 5 19 12 12 19"/>
                </svg>
              </span>
              <span *ngIf="saving" class="save-content">
                <span class="spinner"></span>
                Enregistrement...
              </span>
            </button>
          </div>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    :host {
      --ink-900: #0f172a;
      --ink-700: #334155;
      --ink-500: #64748b;
      --ink-400: #94a3b8;
      --ink-200: #e2e8f0;
      --ink-100: #f1f5f9;
      --ink-50: #f8fafc;
      --bg-page: #f6f7fb;
      --primary: #3b82f6;
      --primary-ink: #1d4ed8;
      --primary-soft: #eff6ff;
      --success: #10b981;
      --success-soft: #ecfdf5;
      --danger: #ef4444;
      --danger-soft: #fef2f2;
      --radius-lg: 18px;
      --radius-md: 12px;
      --radius-sm: 8px;
    }

    .profile-page {
      padding: 28px 32px 120px;
      max-width: 1280px;
      margin: 0 auto;
      background: var(--bg-page);
      min-height: calc(100vh - 64px);
    }

    /* ============ HERO BANNER ============ */
    .identity-hero {
      position: relative;
      border-radius: var(--radius-lg);
      overflow: hidden;
      background:
        radial-gradient(120% 120% at 0% 0%, #1e3a8a 0%, transparent 55%),
        radial-gradient(120% 120% at 100% 100%, #0ea5e9 0%, transparent 55%),
        linear-gradient(135deg, #0f172a 0%, #1e293b 100%);
      color: #fff;
      padding: 36px 40px;
      box-shadow: 0 20px 48px -24px rgba(15, 23, 42, 0.45);
      animation: fadeRise .55s ease both;
    }

    .hero-pattern {
      position: absolute;
      inset: 0;
      background-image:
        linear-gradient(90deg, rgba(255,255,255,0.05) 1px, transparent 1px),
        linear-gradient(0deg, rgba(255,255,255,0.05) 1px, transparent 1px);
      background-size: 28px 28px;
      mask-image: radial-gradient(ellipse at top right, black 20%, transparent 75%);
      -webkit-mask-image: radial-gradient(ellipse at top right, black 20%, transparent 75%);
      pointer-events: none;
    }

    .hero-content {
      position: relative;
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 32px;
      flex-wrap: wrap;
    }

    .identity-left {
      display: flex;
      align-items: center;
      gap: 24px;
      min-width: 0;
    }

    .avatar-halo {
      position: relative;
      flex-shrink: 0;
    }

    .avatar-disc {
      width: 84px;
      height: 84px;
      border-radius: 22px;
      background: linear-gradient(145deg, #60a5fa 0%, #2563eb 100%);
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 28px;
      font-weight: 700;
      color: #fff;
      letter-spacing: -0.02em;
      box-shadow:
        0 0 0 3px rgba(255,255,255,0.12),
        0 20px 40px -14px rgba(59, 130, 246, 0.55);
    }

    .avatar-upload {
      position: absolute;
      bottom: -4px;
      right: -4px;
      width: 30px;
      height: 30px;
      border-radius: 50%;
      background: #fff;
      color: #1e293b;
      border: none;
      display: flex;
      align-items: center;
      justify-content: center;
      cursor: pointer;
      box-shadow: 0 6px 14px -4px rgba(0, 0, 0, 0.35);
      transition: transform .18s ease;
    }

    .avatar-upload:hover {
      transform: scale(1.08) rotate(-4deg);
    }

    .identity-meta {
      min-width: 0;
      display: flex;
      flex-direction: column;
      gap: 6px;
    }

    .eyebrow {
      text-transform: uppercase;
      letter-spacing: 0.22em;
      font-size: 10px;
      font-weight: 700;
      color: #93c5fd;
    }

    .display-name {
      margin: 0;
      font-size: 30px;
      line-height: 1.15;
      font-weight: 700;
      letter-spacing: -0.025em;
      color: #fff;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      max-width: 520px;
    }

    .identity-chips {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      margin-top: 8px;
    }

    .chip {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 5px 12px;
      border-radius: 999px;
      font-size: 12px;
      font-weight: 600;
      border: 1px solid rgba(255,255,255,0.14);
      background: rgba(255,255,255,0.08);
      color: #e2e8f0;
      backdrop-filter: blur(8px);
    }

    .chip-role {
      background: rgba(96, 165, 250, 0.18);
      border-color: rgba(96, 165, 250, 0.4);
      color: #bfdbfe;
    }

    .chip-email {
      color: #cbd5e1;
    }

    .identity-right {
      display: flex;
      align-items: center;
    }

    .stat-block {
      padding: 12px 18px;
      border-radius: 14px;
      background: rgba(255,255,255,0.08);
      border: 1px solid rgba(255,255,255,0.12);
      display: flex;
      flex-direction: column;
      gap: 4px;
      min-width: 120px;
    }

    .stat-label {
      font-size: 11px;
      text-transform: uppercase;
      letter-spacing: 0.12em;
      color: #94a3b8;
      font-weight: 600;
    }

    .stat-value {
      font-size: 14px;
      font-weight: 600;
      color: #fff;
      display: inline-flex;
      align-items: center;
      gap: 8px;
    }

    .dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: #22c55e;
      box-shadow: 0 0 0 3px rgba(34, 197, 94, 0.25);
      display: inline-block;
    }

    /* ============ SECTION GRID ============ */
    .profile-grid {
      margin-top: 28px;
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 20px;
    }

    .section-card {
      background: #fff;
      border: 1px solid var(--ink-200);
      border-radius: var(--radius-lg);
      overflow: hidden;
      transition: transform .25s ease, box-shadow .25s ease, border-color .25s ease;
      animation: fadeRise .5s ease both;
      animation-delay: calc(var(--stagger, 0) * 80ms + 120ms);
    }

    .section-card:hover {
      transform: translateY(-2px);
      box-shadow: 0 18px 40px -24px rgba(15, 23, 42, 0.18);
      border-color: #cbd5e1;
    }

    .section-head {
      display: flex;
      align-items: center;
      gap: 14px;
      padding: 20px 24px;
      border-bottom: 1px solid var(--ink-100);
    }

    .section-medal {
      width: 40px;
      height: 40px;
      border-radius: 12px;
      display: flex;
      align-items: center;
      justify-content: center;
      color: #fff;
      flex-shrink: 0;
      box-shadow: 0 10px 24px -10px rgba(15, 23, 42, 0.35);
    }

    .gradient-sky { background: linear-gradient(140deg, #60a5fa 0%, #2563eb 100%); }
    .gradient-amber { background: linear-gradient(140deg, #fbbf24 0%, #ea580c 100%); }
    .gradient-violet { background: linear-gradient(140deg, #a78bfa 0%, #7c3aed 100%); }
    .gradient-emerald { background: linear-gradient(140deg, #34d399 0%, #059669 100%); }

    .section-text h2 {
      margin: 0 0 2px 0;
      font-size: 15px;
      font-weight: 700;
      color: var(--ink-900);
      letter-spacing: -0.01em;
    }

    .section-text p {
      margin: 0;
      font-size: 12px;
      color: var(--ink-500);
    }

    .section-body {
      padding: 22px 24px 24px;
      display: flex;
      flex-direction: column;
      gap: 16px;
    }

    .field-row {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 14px;
    }

    .field {
      display: flex;
      flex-direction: column;
      gap: 6px;
      min-width: 0;
    }

    .field label {
      font-size: 11px;
      font-weight: 600;
      color: var(--ink-500);
      text-transform: uppercase;
      letter-spacing: 0.08em;
    }

    .field input,
    .field select {
      width: 100%;
      padding: 11px 14px;
      border: 1px solid var(--ink-200);
      border-radius: var(--radius-sm);
      font-size: 14px;
      font-family: inherit;
      color: var(--ink-900);
      background: #fff;
      transition: border-color .2s ease, box-shadow .2s ease, background .2s ease;
    }

    .field select {
      appearance: none;
      background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='8' viewBox='0 0 12 8' fill='none' stroke='%2364748b' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpolyline points='1 1 6 7 11 1'/%3E%3C/svg%3E");
      background-repeat: no-repeat;
      background-position: right 14px center;
      padding-right: 38px;
    }

    .field input:hover,
    .field select:hover {
      border-color: #cbd5e1;
    }

    .field input:focus,
    .field select:focus {
      outline: none;
      border-color: var(--primary);
      box-shadow: 0 0 0 4px rgba(59, 130, 246, 0.12);
    }

    .field input:disabled {
      background: var(--ink-50);
      color: var(--ink-400);
      cursor: not-allowed;
    }

    /* ============ ALERT PREFS ============ */
    .alert-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
    }
    .alert-check {
      display: flex;
      align-items: flex-start;
      gap: 10px;
      padding: 12px 14px;
      border: 1px solid var(--ink-200);
      border-radius: var(--radius-sm);
      cursor: pointer;
      transition: border-color .2s ease, background .2s ease;
    }
    .alert-check:hover { border-color: #cbd5e1; background: var(--ink-50); }
    .alert-check input[type="checkbox"] {
      width: 16px; height: 16px; accent-color: var(--primary);
      margin-top: 2px; cursor: pointer;
    }
    .alert-info { display: flex; flex-direction: column; gap: 2px; }
    .alert-title { font-size: 13px; font-weight: 600; color: var(--ink-900); }
    .alert-desc { font-size: 11px; color: var(--ink-500); }

    /* ============ PASSWORD ACTIONS ============ */
    .pwd-actions { display: flex; justify-content: flex-end; }
    .inline-feedback {
      margin-top: 8px; padding: 10px 14px; border-radius: 8px;
      background: #ecfdf5; color: #047857; font-size: 12px; font-weight: 600;
      border: 1px solid #a7f3d0;
    }
    .inline-feedback.error {
      background: var(--danger-soft); color: #991b1b; border-color: #fecaca;
    }

    @media (max-width: 720px) {
      .alert-grid { grid-template-columns: 1fr; }
    }

    /* ============ ACTION BAR ============ */
    .action-bar {
      position: sticky;
      bottom: 20px;
      margin-top: 28px;
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 16px;
      padding: 14px 20px;
      background: #fff;
      border: 1px solid var(--ink-200);
      border-radius: 14px;
      box-shadow: 0 20px 40px -20px rgba(15, 23, 42, 0.22);
      animation: fadeRise .6s ease both;
      animation-delay: 450ms;
      z-index: 5;
    }

    .dirty-note {
      font-size: 12px;
      color: var(--ink-500);
    }

    .action-buttons {
      display: flex;
      gap: 10px;
    }

    .btn-ghost,
    .btn-save {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      padding: 11px 20px;
      border-radius: 10px;
      font-size: 14px;
      font-weight: 600;
      font-family: inherit;
      cursor: pointer;
      transition: all .2s ease;
      border: 1px solid transparent;
    }

    .btn-ghost {
      background: var(--ink-100);
      color: var(--ink-700);
    }

    .btn-ghost:hover:not(:disabled) {
      background: var(--ink-200);
    }

    .btn-save {
      background: linear-gradient(135deg, #3b82f6 0%, #2563eb 100%);
      color: #fff;
      box-shadow: 0 12px 24px -12px rgba(59, 130, 246, 0.55);
    }

    .btn-save:hover:not(:disabled) {
      transform: translateY(-1px);
      box-shadow: 0 16px 30px -12px rgba(59, 130, 246, 0.65);
    }

    .btn-save:active:not(:disabled) {
      transform: translateY(0);
    }

    .btn-ghost:disabled,
    .btn-save:disabled {
      opacity: 0.55;
      cursor: not-allowed;
    }

    .save-content {
      display: inline-flex;
      align-items: center;
      gap: 8px;
    }

    .spinner {
      width: 14px;
      height: 14px;
      border: 2px solid rgba(255,255,255,0.4);
      border-top-color: #fff;
      border-radius: 50%;
      animation: spin .7s linear infinite;
      display: inline-block;
    }

    /* ============ TOAST ============ */
    .toast-slot {
      position: fixed;
      top: 88px;
      right: 28px;
      display: inline-flex;
      align-items: center;
      gap: 10px;
      padding: 12px 18px;
      border-radius: 12px;
      font-size: 13px;
      font-weight: 600;
      background: #fff;
      color: #065f46;
      border: 1px solid #a7f3d0;
      box-shadow: 0 24px 48px -16px rgba(16, 185, 129, 0.25);
      animation: slideInRight .35s cubic-bezier(.2,.9,.3,1.1) both;
      z-index: 50;
    }

    .toast-slot.toast-error {
      color: #991b1b;
      border-color: #fecaca;
      box-shadow: 0 24px 48px -16px rgba(239, 68, 68, 0.22);
    }

    .toast-icon {
      width: 22px;
      height: 22px;
      border-radius: 50%;
      background: var(--success-soft);
      color: var(--success);
      display: inline-flex;
      align-items: center;
      justify-content: center;
    }

    .toast-slot.toast-error .toast-icon {
      background: var(--danger-soft);
      color: var(--danger);
    }

    /* ============ ANIMATIONS ============ */
    @keyframes fadeRise {
      from { opacity: 0; transform: translateY(14px); }
      to { opacity: 1; transform: translateY(0); }
    }

    @keyframes slideInRight {
      from { opacity: 0; transform: translateX(24px); }
      to { opacity: 1; transform: translateX(0); }
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }

    /* ============ RESPONSIVE ============ */
    @media (max-width: 1024px) {
      .profile-grid {
        grid-template-columns: 1fr;
      }
    }

    @media (max-width: 720px) {
      .profile-page {
        padding: 20px 16px 140px;
      }

      .identity-hero {
        padding: 24px 22px;
      }

      .identity-left {
        flex-direction: column;
        align-items: flex-start;
        gap: 18px;
      }

      .display-name {
        font-size: 24px;
      }

      .identity-right {
        width: 100%;
      }

      .stat-block {
        width: 100%;
      }

      .field-row {
        grid-template-columns: 1fr;
      }

      .action-bar {
        flex-direction: column;
        align-items: stretch;
        text-align: center;
      }

      .action-buttons {
        width: 100%;
      }

      .btn-ghost,
      .btn-save {
        flex: 1;
        justify-content: center;
      }
    }
  `]
})
export class ProfileComponent implements OnInit {
  company: { name: string } | null = null;
  saving = false;
  message: { type: 'success' | 'error'; text: string } | null = null;

  // Password change form
  passwordForm = { current: '', next: '', confirm: '' };
  changingPassword = false;
  passwordMessage: { type: 'success' | 'error'; text: string } | null = null;

  // Alert preferences (local-only right now; read by notification bell to filter types)
  alertPrefs = {
    vitesse: true,
    geofencingDocsTow: true,
    offline: true,
    entretiens: true
  };

  profile: ProfileModel = {
    fullName: '',
    email: '',
    phone: '',
    position: '',
    role: '',
    industry: 'transport',
    address: '',
    city: '',
    country: 'TN',
    language: 'fr',
    timezone: 'Africa/Tunis',
    currency: '',
    dateFormat: 'dd/MM/yyyy',
    distanceUnit: 'km',
    speedUnit: 'kmh',
    volumeUnit: 'L',
    temperatureUnit: 'C'
  };

  constructor(
    private router: Router,
    private authService: AuthService,
    private apiService: ApiService,
    private userPrefs: UserPreferencesService
  ) {
    // Seed the currency selector with the active currency so the form never
    // shows a hardcoded default; loadProfile() re-syncs from the service.
    this.profile.currency = this.userPrefs.current.currency;
  }

  ngOnInit() {
    if (!this.authService.isAuthenticated()) {
      this.router.navigate(['/login']);
      return;
    }
    this.loadProfile();
  }

  private loadProfile() {
    const user = this.authService.getCurrentUserSync();
    if (!user) return;

    this.company = { name: user.companyName };

    // Backend-backed fields from the authenticated user
    this.profile.fullName = user.name || '';
    this.profile.email = user.email || '';
    this.profile.phone = user.phone || '';
    this.profile.role = (user.roles && user.roles[0]) || 'Utilisateur';

    // Calypso 9 p5 — regional + units now flow through UserPreferencesService
    // so the values on screen match what the rest of the app actually uses
    // (pipes, monitoring map, etc.). The legacy PREFERENCES_KEY is still
    // read for first-time migration of fields that aren't owned by the
    // service (position, industry, address, city, country).
    try {
      const stored = localStorage.getItem(PREFERENCES_KEY);
      const prefs = stored ? JSON.parse(stored) : {};
      Object.assign(this.profile, DEFAULT_PREFERENCES, prefs);
    } catch {
      Object.assign(this.profile, DEFAULT_PREFERENCES);
    }
    const p = this.userPrefs.current;
    this.profile.language = p.language;
    this.profile.timezone = p.timezone;
    this.profile.currency = p.currency;
    this.profile.dateFormat = p.dateFormat;
    this.profile.distanceUnit = p.distanceUnit;
    this.profile.speedUnit = p.speedUnit;
    this.profile.volumeUnit = p.volumeUnit;
    this.profile.temperatureUnit = p.temperatureUnit;

    // Alert preferences (stored locally)
    try {
      const storedAlerts = localStorage.getItem(ALERT_PREFS_KEY);
      if (storedAlerts) {
        Object.assign(this.alertPrefs, JSON.parse(storedAlerts));
      }
    } catch {
      /* keep defaults */
    }
  }

  changePassword() {
    if (this.changingPassword) return;
    this.passwordMessage = null;
    const { current, next, confirm } = this.passwordForm;
    if (!current) {
      this.passwordMessage = { type: 'error', text: 'Veuillez saisir votre mot de passe actuel.' };
      return;
    }
    if (!next || next.length < 6) {
      this.passwordMessage = { type: 'error', text: 'Le nouveau mot de passe doit faire au moins 6 caractères.' };
      return;
    }
    if (next !== confirm) {
      this.passwordMessage = { type: 'error', text: 'La confirmation ne correspond pas.' };
      return;
    }
    if (next === current) {
      this.passwordMessage = { type: 'error', text: 'Le nouveau mot de passe doit être différent de l\'ancien.' };
      return;
    }

    this.changingPassword = true;
    this.apiService.changeMyPassword({ currentPassword: current, newPassword: next }).subscribe({
      next: () => {
        this.changingPassword = false;
        this.passwordForm = { current: '', next: '', confirm: '' };
        this.passwordMessage = { type: 'success', text: 'Mot de passe modifié avec succès.' };
        setTimeout(() => { this.passwordMessage = null; }, 4000);
      },
      error: (err: any) => {
        this.changingPassword = false;
        const apiMessage = err?.error?.message || err?.error?.Message || err?.error?.title;
        this.passwordMessage = {
          type: 'error',
          text: apiMessage || 'Impossible de modifier le mot de passe.'
        };
      }
    });
  }

  getInitials(): string {
    const name = this.profile.fullName || '';
    return name
      .split(/\s+/)
      .filter(n => n.length > 0)
      .map(n => n[0])
      .join('')
      .substring(0, 2)
      .toUpperCase() || '·';
  }

  saveProfile() {
    if (this.saving) return;
    this.message = null;

    const fullName = (this.profile.fullName || '').trim();
    const email = (this.profile.email || '').trim();
    if (!fullName) {
      this.message = { type: 'error', text: 'Le nom complet est obligatoire.' };
      return;
    }
    if (!email) {
      this.message = { type: 'error', text: 'L\'email est obligatoire.' };
      return;
    }

    // Split fullName on first space — matches backend User.Name setter logic
    const parts = fullName.split(/\s+/);
    const firstName = parts.shift() || fullName;
    const lastName = parts.join(' ');

    this.saving = true;
    this.apiService.updateMyProfile({
      firstName,
      lastName,
      email,
      phone: this.profile.phone?.trim() || null
    }).subscribe({
      next: (updated: any) => {
        // Local-only fields not owned by the central service stay in
        // PREFERENCES_KEY for backward compatibility.
        const prefs = {
          position: this.profile.position,
          industry: this.profile.industry,
          address: this.profile.address,
          city: this.profile.city,
          country: this.profile.country
        };
        localStorage.setItem(PREFERENCES_KEY, JSON.stringify(prefs));
        localStorage.setItem(ALERT_PREFS_KEY, JSON.stringify(this.alertPrefs));

        // Regional + units go through the shared preferences service so
        // every consumer (pipes, monitoring, reports, expenses) reacts
        // immediately to the change.
        this.userPrefs.update({
          language: this.profile.language as any,
          timezone: this.profile.timezone,
          currency: this.profile.currency as any,
          dateFormat: this.profile.dateFormat as any,
          distanceUnit: this.profile.distanceUnit as any,
          speedUnit: this.profile.speedUnit as any,
          volumeUnit: this.profile.volumeUnit as any,
          temperatureUnit: this.profile.temperatureUnit as any
        });

        // Sync AuthService so sidebar/header update immediately
        this.authService.patchCurrentUser({
          name: updated?.name || fullName,
          email: updated?.email || email,
          phone: updated?.phone ?? (this.profile.phone || undefined)
        });

        this.saving = false;
        this.message = { type: 'success', text: 'Profil enregistré avec succès.' };
        setTimeout(() => { this.message = null; }, 3000);
      },
      error: (err) => {
        this.saving = false;
        const apiMessage = err?.error?.message || err?.error?.Message;
        this.message = {
          type: 'error',
          text: apiMessage || 'Impossible d\'enregistrer le profil. Vérifiez votre connexion et réessayez.'
        };
      }
    });
  }

  resetForm() {
    this.loadProfile();
    this.message = null;
  }
}
