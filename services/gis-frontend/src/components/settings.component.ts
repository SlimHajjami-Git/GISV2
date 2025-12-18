import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MockDataService } from '../services/mock-data.service';
import { AppLayoutComponent } from './shared/app-layout.component';

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent],
  template: `
    <app-layout>
      <div class="settings-page">
        <div class="page-header">
          <h1>Paramètres</h1>
          <p class="subtitle">Configurez votre application selon vos préférences</p>
        </div>

        <div class="settings-content">
          <div class="settings-nav">
            <button 
              *ngFor="let tab of tabs" 
              class="nav-item" 
              [class.active]="activeTab === tab.id"
              (click)="activeTab = tab.id">
              <span class="nav-icon" [innerHTML]="tab.icon"></span>
              <span>{{ tab.label }}</span>
            </button>
          </div>

          <div class="settings-panel">
            <!-- Notifications Settings -->
            <div class="panel-section" *ngIf="activeTab === 'notifications'">
              <h2>Notifications</h2>
              <p class="section-desc">Gérez comment et quand vous recevez des notifications</p>

              <div class="settings-group">
                <h3>Alertes en temps réel</h3>
                <div class="setting-item">
                  <div class="setting-info">
                    <span class="setting-label">Excès de vitesse</span>
                    <span class="setting-desc">Notification quand un véhicule dépasse la limite</span>
                  </div>
                  <label class="toggle">
                    <input type="checkbox" [(ngModel)]="settings.notifications.speeding">
                    <span class="slider"></span>
                  </label>
                </div>
                <div class="setting-item">
                  <div class="setting-info">
                    <span class="setting-label">Entrée/Sortie de zone</span>
                    <span class="setting-desc">Notification lors du franchissement d'une geofence</span>
                  </div>
                  <label class="toggle">
                    <input type="checkbox" [(ngModel)]="settings.notifications.geofence">
                    <span class="slider"></span>
                  </label>
                </div>
                <div class="setting-item">
                  <div class="setting-info">
                    <span class="setting-label">Arrêt prolongé</span>
                    <span class="setting-desc">Notification si un véhicule est à l'arrêt trop longtemps</span>
                  </div>
                  <label class="toggle">
                    <input type="checkbox" [(ngModel)]="settings.notifications.idling">
                    <span class="slider"></span>
                  </label>
                </div>
                <div class="setting-item">
                  <div class="setting-info">
                    <span class="setting-label">Maintenance</span>
                    <span class="setting-desc">Rappels de maintenance programmée</span>
                  </div>
                  <label class="toggle">
                    <input type="checkbox" [(ngModel)]="settings.notifications.maintenance">
                    <span class="slider"></span>
                  </label>
                </div>
              </div>

              <div class="settings-group">
                <h3>Canaux de notification</h3>
                <div class="setting-item">
                  <div class="setting-info">
                    <span class="setting-label">Notifications push</span>
                    <span class="setting-desc">Recevoir des notifications dans le navigateur</span>
                  </div>
                  <label class="toggle">
                    <input type="checkbox" [(ngModel)]="settings.notifications.push">
                    <span class="slider"></span>
                  </label>
                </div>
                <div class="setting-item">
                  <div class="setting-info">
                    <span class="setting-label">Notifications email</span>
                    <span class="setting-desc">Recevoir un résumé par email</span>
                  </div>
                  <label class="toggle">
                    <input type="checkbox" [(ngModel)]="settings.notifications.email">
                    <span class="slider"></span>
                  </label>
                </div>
                <div class="setting-item">
                  <div class="setting-info">
                    <span class="setting-label">Notifications SMS</span>
                    <span class="setting-desc">Recevoir les alertes critiques par SMS</span>
                  </div>
                  <label class="toggle">
                    <input type="checkbox" [(ngModel)]="settings.notifications.sms">
                    <span class="slider"></span>
                  </label>
                </div>
              </div>

              <div class="settings-group">
                <h3>Heures silencieuses</h3>
                <div class="setting-item">
                  <div class="setting-info">
                    <span class="setting-label">Activer les heures silencieuses</span>
                    <span class="setting-desc">Pas de notifications pendant cette période</span>
                  </div>
                  <label class="toggle">
                    <input type="checkbox" [(ngModel)]="settings.notifications.quietHours">
                    <span class="slider"></span>
                  </label>
                </div>
                <div class="time-range" *ngIf="settings.notifications.quietHours">
                  <div class="form-group">
                    <label>De</label>
                    <input type="time" [(ngModel)]="settings.notifications.quietStart">
                  </div>
                  <div class="form-group">
                    <label>À</label>
                    <input type="time" [(ngModel)]="settings.notifications.quietEnd">
                  </div>
                </div>
              </div>
            </div>

            <!-- Display Settings -->
            <div class="panel-section" *ngIf="activeTab === 'display'">
              <h2>Affichage</h2>
              <p class="section-desc">Personnalisez l'apparence de l'application</p>

              <div class="settings-group">
                <h3>Thème</h3>
                <div class="theme-options">
                  <div 
                    class="theme-option" 
                    [class.active]="settings.display.theme === 'light'"
                    (click)="settings.display.theme = 'light'">
                    <div class="theme-preview light">
                      <div class="preview-header"></div>
                      <div class="preview-content"></div>
                    </div>
                    <span>Clair</span>
                  </div>
                  <div 
                    class="theme-option" 
                    [class.active]="settings.display.theme === 'dark'"
                    (click)="settings.display.theme = 'dark'">
                    <div class="theme-preview dark">
                      <div class="preview-header"></div>
                      <div class="preview-content"></div>
                    </div>
                    <span>Sombre</span>
                  </div>
                  <div 
                    class="theme-option" 
                    [class.active]="settings.display.theme === 'auto'"
                    (click)="settings.display.theme = 'auto'">
                    <div class="theme-preview auto">
                      <div class="preview-header"></div>
                      <div class="preview-content"></div>
                    </div>
                    <span>Automatique</span>
                  </div>
                </div>
              </div>

              <div class="settings-group">
                <h3>Carte</h3>
                <div class="setting-item">
                  <div class="setting-info">
                    <span class="setting-label">Style de carte</span>
                    <span class="setting-desc">Apparence de la carte de monitoring</span>
                  </div>
                  <select [(ngModel)]="settings.display.mapStyle">
                    <option value="streets">Rues</option>
                    <option value="satellite">Satellite</option>
                    <option value="hybrid">Hybride</option>
                    <option value="terrain">Terrain</option>
                  </select>
                </div>
                <div class="setting-item">
                  <div class="setting-info">
                    <span class="setting-label">Afficher les noms des véhicules</span>
                    <span class="setting-desc">Labels sur les marqueurs de la carte</span>
                  </div>
                  <label class="toggle">
                    <input type="checkbox" [(ngModel)]="settings.display.showVehicleLabels">
                    <span class="slider"></span>
                  </label>
                </div>
                <div class="setting-item">
                  <div class="setting-info">
                    <span class="setting-label">Clustering des véhicules</span>
                    <span class="setting-desc">Regrouper les véhicules proches sur la carte</span>
                  </div>
                  <label class="toggle">
                    <input type="checkbox" [(ngModel)]="settings.display.clustering">
                    <span class="slider"></span>
                  </label>
                </div>
              </div>

              <div class="settings-group">
                <h3>Tableau de bord</h3>
                <div class="setting-item">
                  <div class="setting-info">
                    <span class="setting-label">Actualisation automatique</span>
                    <span class="setting-desc">Rafraîchir les données automatiquement</span>
                  </div>
                  <select [(ngModel)]="settings.display.refreshInterval">
                    <option value="10">Toutes les 10 secondes</option>
                    <option value="30">Toutes les 30 secondes</option>
                    <option value="60">Toutes les minutes</option>
                    <option value="0">Manuel uniquement</option>
                  </select>
                </div>
                <div class="setting-item">
                  <div class="setting-info">
                    <span class="setting-label">Animations</span>
                    <span class="setting-desc">Activer les animations de l'interface</span>
                  </div>
                  <label class="toggle">
                    <input type="checkbox" [(ngModel)]="settings.display.animations">
                    <span class="slider"></span>
                  </label>
                </div>
              </div>
            </div>

            <!-- Security Settings -->
            <div class="panel-section" *ngIf="activeTab === 'security'">
              <h2>Sécurité</h2>
              <p class="section-desc">Protégez votre compte et vos données</p>

              <div class="settings-group">
                <h3>Mot de passe</h3>
                <div class="password-form">
                  <div class="form-group">
                    <label>Mot de passe actuel</label>
                    <input type="password" [(ngModel)]="passwordForm.current" placeholder="••••••••">
                  </div>
                  <div class="form-group">
                    <label>Nouveau mot de passe</label>
                    <input type="password" [(ngModel)]="passwordForm.new" placeholder="••••••••">
                  </div>
                  <div class="form-group">
                    <label>Confirmer le nouveau mot de passe</label>
                    <input type="password" [(ngModel)]="passwordForm.confirm" placeholder="••••••••">
                  </div>
                  <button class="btn-primary" (click)="changePassword()">Changer le mot de passe</button>
                </div>
              </div>

              <div class="settings-group">
                <h3>Authentification à deux facteurs</h3>
                <div class="setting-item">
                  <div class="setting-info">
                    <span class="setting-label">Activer 2FA</span>
                    <span class="setting-desc">Ajouter une couche de sécurité supplémentaire</span>
                  </div>
                  <label class="toggle">
                    <input type="checkbox" [(ngModel)]="settings.security.twoFactor">
                    <span class="slider"></span>
                  </label>
                </div>
              </div>

              <div class="settings-group">
                <h3>Sessions</h3>
                <div class="setting-item">
                  <div class="setting-info">
                    <span class="setting-label">Déconnexion automatique</span>
                    <span class="setting-desc">Se déconnecter après une période d'inactivité</span>
                  </div>
                  <select [(ngModel)]="settings.security.autoLogout">
                    <option value="15">15 minutes</option>
                    <option value="30">30 minutes</option>
                    <option value="60">1 heure</option>
                    <option value="0">Jamais</option>
                  </select>
                </div>
                <button class="btn-danger-outline" (click)="logoutAllSessions()">
                  Déconnecter toutes les sessions
                </button>
              </div>
            </div>

            <!-- Data Settings -->
            <div class="panel-section" *ngIf="activeTab === 'data'">
              <h2>Données</h2>
              <p class="section-desc">Gérez vos données et exportations</p>

              <div class="settings-group">
                <h3>Export de données</h3>
                <div class="export-options">
                  <button class="export-btn" (click)="exportData('vehicles')">
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <rect x="1" y="3" width="15" height="13" rx="2"/>
                      <polygon points="16 8 20 8 23 11 23 16 16 16 16 8"/>
                      <circle cx="5.5" cy="18.5" r="2.5"/>
                      <circle cx="18.5" cy="18.5" r="2.5"/>
                    </svg>
                    <span>Exporter véhicules</span>
                  </button>
                  <button class="export-btn" (click)="exportData('employees')">
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/>
                      <circle cx="9" cy="7" r="4"/>
                      <path d="M23 21v-2a4 4 0 0 0-3-3.87"/>
                      <path d="M16 3.13a4 4 0 0 1 0 7.75"/>
                    </svg>
                    <span>Exporter conducteurs</span>
                  </button>
                  <button class="export-btn" (click)="exportData('trips')">
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <polyline points="22 12 18 12 15 21 9 3 6 12 2 12"/>
                    </svg>
                    <span>Exporter trajets</span>
                  </button>
                  <button class="export-btn" (click)="exportData('reports')">
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                      <polyline points="14 2 14 8 20 8"/>
                      <line x1="16" y1="13" x2="8" y2="13"/>
                      <line x1="16" y1="17" x2="8" y2="17"/>
                    </svg>
                    <span>Exporter rapports</span>
                  </button>
                </div>
              </div>

              <div class="settings-group">
                <h3>Rétention des données</h3>
                <div class="setting-item">
                  <div class="setting-info">
                    <span class="setting-label">Historique des positions</span>
                    <span class="setting-desc">Durée de conservation des données GPS</span>
                  </div>
                  <select [(ngModel)]="settings.data.positionRetention">
                    <option value="30">30 jours</option>
                    <option value="90">90 jours</option>
                    <option value="180">6 mois</option>
                    <option value="365">1 an</option>
                  </select>
                </div>
                <div class="setting-item">
                  <div class="setting-info">
                    <span class="setting-label">Historique des alertes</span>
                    <span class="setting-desc">Durée de conservation des notifications</span>
                  </div>
                  <select [(ngModel)]="settings.data.alertRetention">
                    <option value="30">30 jours</option>
                    <option value="90">90 jours</option>
                    <option value="180">6 mois</option>
                  </select>
                </div>
              </div>

              <div class="settings-group danger-zone">
                <h3>Zone de danger</h3>
                <div class="danger-actions">
                  <div class="danger-item">
                    <div class="danger-info">
                      <span class="danger-label">Supprimer toutes les alertes</span>
                      <span class="danger-desc">Cette action est irréversible</span>
                    </div>
                    <button class="btn-danger" (click)="clearAlerts()">Supprimer</button>
                  </div>
                  <div class="danger-item">
                    <div class="danger-info">
                      <span class="danger-label">Réinitialiser les paramètres</span>
                      <span class="danger-desc">Remettre tous les paramètres par défaut</span>
                    </div>
                    <button class="btn-danger" (click)="resetSettings()">Réinitialiser</button>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- Save Button -->
        <div class="save-bar">
          <button class="btn-primary" (click)="saveSettings()">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/>
              <polyline points="17 21 17 13 7 13 7 21"/>
              <polyline points="7 3 7 8 15 8"/>
            </svg>
            Enregistrer les paramètres
          </button>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    .settings-page {
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

    .settings-content {
      display: flex;
      gap: 24px;
      background: white;
      border-radius: 12px;
      border: 1px solid #e5e7eb;
      overflow: hidden;
      min-height: 600px;
    }

    /* Navigation */
    .settings-nav {
      width: 220px;
      background: #f9fafb;
      border-right: 1px solid #e5e7eb;
      padding: 16px;
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .nav-item {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px 16px;
      border: none;
      background: transparent;
      border-radius: 8px;
      font-size: 14px;
      font-weight: 500;
      color: #4b5563;
      cursor: pointer;
      transition: all 0.15s;
      text-align: left;
    }

    .nav-item:hover {
      background: #e5e7eb;
      color: #1f2937;
    }

    .nav-item.active {
      background: #6366f1;
      color: white;
    }

    .nav-icon {
      width: 20px;
      height: 20px;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    /* Panel */
    .settings-panel {
      flex: 1;
      padding: 24px;
      overflow-y: auto;
    }

    .panel-section h2 {
      font-size: 20px;
      font-weight: 600;
      color: #1f2937;
      margin: 0 0 4px 0;
    }

    .section-desc {
      color: #6b7280;
      font-size: 14px;
      margin: 0 0 24px 0;
    }

    .settings-group {
      margin-bottom: 32px;
    }

    .settings-group h3 {
      font-size: 14px;
      font-weight: 600;
      color: #374151;
      margin: 0 0 16px 0;
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }

    .setting-item {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 12px 0;
      border-bottom: 1px solid #f3f4f6;
    }

    .setting-item:last-child {
      border-bottom: none;
    }

    .setting-info {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .setting-label {
      font-size: 14px;
      font-weight: 500;
      color: #1f2937;
    }

    .setting-desc {
      font-size: 12px;
      color: #6b7280;
    }

    /* Toggle Switch */
    .toggle {
      position: relative;
      display: inline-block;
      width: 44px;
      height: 24px;
    }

    .toggle input {
      opacity: 0;
      width: 0;
      height: 0;
    }

    .slider {
      position: absolute;
      cursor: pointer;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background-color: #d1d5db;
      transition: 0.3s;
      border-radius: 24px;
    }

    .slider:before {
      position: absolute;
      content: "";
      height: 18px;
      width: 18px;
      left: 3px;
      bottom: 3px;
      background-color: white;
      transition: 0.3s;
      border-radius: 50%;
    }

    .toggle input:checked + .slider {
      background-color: #6366f1;
    }

    .toggle input:checked + .slider:before {
      transform: translateX(20px);
    }

    /* Select */
    .setting-item select,
    .form-group select {
      padding: 8px 12px;
      border: 1px solid #d1d5db;
      border-radius: 6px;
      font-size: 14px;
      color: #1f2937;
      background: white;
      min-width: 180px;
    }

    /* Time Range */
    .time-range {
      display: flex;
      gap: 16px;
      margin-top: 12px;
    }

    .time-range .form-group {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .time-range label {
      font-size: 12px;
      color: #6b7280;
    }

    .time-range input {
      padding: 8px 12px;
      border: 1px solid #d1d5db;
      border-radius: 6px;
      font-size: 14px;
    }

    /* Theme Options */
    .theme-options {
      display: flex;
      gap: 16px;
    }

    .theme-option {
      cursor: pointer;
      text-align: center;
    }

    .theme-preview {
      width: 100px;
      height: 70px;
      border-radius: 8px;
      border: 2px solid #e5e7eb;
      overflow: hidden;
      margin-bottom: 8px;
      transition: border-color 0.2s;
    }

    .theme-option.active .theme-preview {
      border-color: #6366f1;
    }

    .theme-preview.light {
      background: #ffffff;
    }

    .theme-preview.light .preview-header {
      height: 16px;
      background: #f3f4f6;
    }

    .theme-preview.dark {
      background: #1f2937;
    }

    .theme-preview.dark .preview-header {
      height: 16px;
      background: #111827;
    }

    .theme-preview.auto {
      background: linear-gradient(90deg, #ffffff 50%, #1f2937 50%);
    }

    .theme-preview.auto .preview-header {
      height: 16px;
      background: linear-gradient(90deg, #f3f4f6 50%, #111827 50%);
    }

    .theme-option span {
      font-size: 13px;
      color: #4b5563;
    }

    .theme-option.active span {
      color: #6366f1;
      font-weight: 500;
    }

    /* Password Form */
    .password-form {
      max-width: 400px;
    }

    .password-form .form-group {
      margin-bottom: 16px;
    }

    .password-form label {
      display: block;
      font-size: 13px;
      font-weight: 500;
      color: #374151;
      margin-bottom: 6px;
    }

    .password-form input {
      width: 100%;
      padding: 10px 12px;
      border: 1px solid #d1d5db;
      border-radius: 6px;
      font-size: 14px;
    }

    /* Export Options */
    .export-options {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 12px;
    }

    .export-btn {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 16px;
      background: #f9fafb;
      border: 1px solid #e5e7eb;
      border-radius: 8px;
      font-size: 14px;
      font-weight: 500;
      color: #374151;
      cursor: pointer;
      transition: all 0.2s;
    }

    .export-btn:hover {
      background: #f3f4f6;
      border-color: #6366f1;
      color: #6366f1;
    }

    .export-btn svg {
      color: #6b7280;
    }

    .export-btn:hover svg {
      color: #6366f1;
    }

    /* Danger Zone */
    .danger-zone {
      border: 1px solid #fecaca;
      border-radius: 8px;
      padding: 16px;
      background: #fef2f2;
    }

    .danger-zone h3 {
      color: #dc2626;
    }

    .danger-actions {
      display: flex;
      flex-direction: column;
      gap: 12px;
    }

    .danger-item {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 12px;
      background: white;
      border-radius: 6px;
    }

    .danger-info {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .danger-label {
      font-size: 14px;
      font-weight: 500;
      color: #1f2937;
    }

    .danger-desc {
      font-size: 12px;
      color: #6b7280;
    }

    /* Buttons */
    .btn-primary {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 20px;
      background: #6366f1;
      color: white;
      border: none;
      border-radius: 8px;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
      transition: background 0.2s;
    }

    .btn-primary:hover {
      background: #4f46e5;
    }

    .btn-danger {
      padding: 8px 16px;
      background: #dc2626;
      color: white;
      border: none;
      border-radius: 6px;
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
    }

    .btn-danger:hover {
      background: #b91c1c;
    }

    .btn-danger-outline {
      padding: 10px 16px;
      background: white;
      color: #dc2626;
      border: 1px solid #fecaca;
      border-radius: 6px;
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
      margin-top: 12px;
    }

    .btn-danger-outline:hover {
      background: #fef2f2;
    }

    /* Save Bar */
    .save-bar {
      display: flex;
      justify-content: flex-end;
      margin-top: 24px;
    }

    /* Responsive */
    @media (max-width: 768px) {
      .settings-content {
        flex-direction: column;
      }

      .settings-nav {
        width: 100%;
        flex-direction: row;
        overflow-x: auto;
        border-right: none;
        border-bottom: 1px solid #e5e7eb;
      }

      .nav-item {
        white-space: nowrap;
      }

      .export-options {
        grid-template-columns: 1fr;
      }

      .theme-options {
        flex-wrap: wrap;
      }
    }
  `]
})
export class SettingsComponent implements OnInit {
  activeTab = 'notifications';

  tabs = [
    { id: 'notifications', label: 'Notifications', icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.73 21a2 2 0 0 1-3.46 0"/></svg>' },
    { id: 'display', label: 'Affichage', icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="2" y="3" width="20" height="14" rx="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="17" x2="12" y2="21"/></svg>' },
    { id: 'security', label: 'Sécurité', icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>' },
    { id: 'data', label: 'Données', icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><ellipse cx="12" cy="5" rx="9" ry="3"/><path d="M21 12c0 1.66-4 3-9 3s-9-1.34-9-3"/><path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5"/></svg>' }
  ];

  settings = {
    notifications: {
      speeding: true,
      geofence: true,
      idling: true,
      maintenance: true,
      push: true,
      email: true,
      sms: false,
      quietHours: false,
      quietStart: '22:00',
      quietEnd: '07:00'
    },
    display: {
      theme: 'light',
      mapStyle: 'streets',
      showVehicleLabels: true,
      clustering: true,
      refreshInterval: '30',
      animations: true
    },
    security: {
      twoFactor: false,
      autoLogout: '30'
    },
    data: {
      positionRetention: '90',
      alertRetention: '30'
    }
  };

  passwordForm = {
    current: '',
    new: '',
    confirm: ''
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

    // Load saved settings
    const saved = localStorage.getItem('appSettings');
    if (saved) {
      this.settings = { ...this.settings, ...JSON.parse(saved) };
    }
  }

  saveSettings() {
    localStorage.setItem('appSettings', JSON.stringify(this.settings));
    alert('Paramètres enregistrés avec succès!');
  }

  changePassword() {
    if (!this.passwordForm.current || !this.passwordForm.new || !this.passwordForm.confirm) {
      alert('Veuillez remplir tous les champs');
      return;
    }
    if (this.passwordForm.new !== this.passwordForm.confirm) {
      alert('Les mots de passe ne correspondent pas');
      return;
    }
    alert('Mot de passe changé avec succès!');
    this.passwordForm = { current: '', new: '', confirm: '' };
  }

  logoutAllSessions() {
    if (confirm('Êtes-vous sûr de vouloir déconnecter toutes les sessions?')) {
      alert('Toutes les sessions ont été déconnectées');
    }
  }

  exportData(type: string) {
    alert(`Export des ${type} en cours...`);
  }

  clearAlerts() {
    if (confirm('Êtes-vous sûr de vouloir supprimer toutes les alertes?')) {
      alert('Toutes les alertes ont été supprimées');
    }
  }

  resetSettings() {
    if (confirm('Êtes-vous sûr de vouloir réinitialiser tous les paramètres?')) {
      localStorage.removeItem('appSettings');
      this.ngOnInit();
      alert('Paramètres réinitialisés');
    }
  }
}
