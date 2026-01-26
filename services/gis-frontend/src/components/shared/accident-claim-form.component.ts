import { Component, Input, Output, EventEmitter, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';

interface Vehicle {
  id: string;
  name: string;
  plate: string;
  brand: string;
  model: string;
}

interface Driver {
  id: string;
  name: string;
  licenseNumber: string;
  phone: string;
}

interface ThirdParty {
  name: string;
  phone: string;
  vehiclePlate: string;
  vehicleModel: string;
  insuranceCompany: string;
  insuranceNumber: string;
}

@Component({
  selector: 'app-accident-claim-form',
  standalone: true,
  imports: [CommonModule, FormsModule],
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 }))
      ])
    ]),
    trigger('slideIn', [
      transition(':enter', [
        style({ transform: 'translateX(20px)', opacity: 0 }),
        animate('300ms ease-out', style({ transform: 'translateX(0)', opacity: 1 }))
      ])
    ])
  ],
  template: `
    <div class="form-overlay" (click)="close()">
      <div class="form-container" (click)="$event.stopPropagation()" @fadeIn>
        <!-- Header -->
        <div class="form-header">
          <div class="header-info">
            <h2>{{ claim ? 'Modifier la Déclaration' : 'Nouvelle Déclaration de Sinistre' }}</h2>
            <p>Étape {{ currentStep }} sur {{ totalSteps }}</p>
          </div>
          <button class="btn-close" (click)="close()">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
            </svg>
          </button>
        </div>

        <!-- Progress Bar -->
        <div class="progress-bar">
          <div class="progress-steps">
            <div class="step" *ngFor="let step of steps; let i = index" 
                 [class.active]="currentStep === i + 1"
                 [class.completed]="currentStep > i + 1"
                 (click)="goToStep(i + 1)">
              <div class="step-number">
                <span *ngIf="currentStep <= i + 1">{{ i + 1 }}</span>
                <svg *ngIf="currentStep > i + 1" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
                  <polyline points="20 6 9 17 4 12"/>
                </svg>
              </div>
              <span class="step-label">{{ step }}</span>
            </div>
          </div>
          <div class="progress-line">
            <div class="progress-fill" [style.width.%]="((currentStep - 1) / (totalSteps - 1)) * 100"></div>
          </div>
        </div>

        <!-- Form Content -->
        <div class="form-content">
          <!-- Step 1: Accident Info -->
          <div class="step-content" *ngIf="currentStep === 1" @slideIn>
            <h3>📍 Informations sur l'Accident</h3>
            
            <div class="form-row">
              <div class="form-group">
                <label>Date de l'accident *</label>
                <input type="date" [(ngModel)]="formData.accidentDate" required>
              </div>
              <div class="form-group">
                <label>Heure *</label>
                <input type="time" [(ngModel)]="formData.accidentTime" required>
              </div>
            </div>

            <div class="form-group">
              <label>Localisation *</label>
              <input type="text" [(ngModel)]="formData.location" placeholder="Adresse complète du lieu de l'accident" required>
            </div>

            <div class="form-row">
              <div class="form-group">
                <label>Conditions météo</label>
                <select [(ngModel)]="formData.weatherConditions">
                  <option value="">Sélectionner</option>
                  <option value="clear">Ensoleillé</option>
                  <option value="cloudy">Nuageux</option>
                  <option value="rain">Pluie</option>
                  <option value="fog">Brouillard</option>
                  <option value="night">Nuit</option>
                </select>
              </div>
              <div class="form-group">
                <label>État de la route</label>
                <select [(ngModel)]="formData.roadConditions">
                  <option value="">Sélectionner</option>
                  <option value="dry">Sèche</option>
                  <option value="wet">Mouillée</option>
                  <option value="icy">Verglacée</option>
                  <option value="construction">Travaux</option>
                </select>
              </div>
            </div>

            <div class="form-group">
              <label>Numéro de rapport de police (si applicable)</label>
              <input type="text" [(ngModel)]="formData.policeReportNumber" placeholder="Ex: PV-2026-XXXX">
            </div>
          </div>

          <!-- Step 2: Vehicle & Driver -->
          <div class="step-content" *ngIf="currentStep === 2" @slideIn>
            <h3>🚗 Véhicule & Conducteur</h3>

            <div class="form-group">
              <label>Véhicule impliqué *</label>
              <select [(ngModel)]="formData.vehicleId" (ngModelChange)="onVehicleChange($event)" required>
                <option value="">Sélectionner un véhicule</option>
                <option *ngFor="let v of vehicles" [value]="v.id">{{ v.name }} - {{ v.plate }}</option>
              </select>
            </div>

            <div class="vehicle-info" *ngIf="selectedVehicle">
              <div class="info-card">
                <span class="info-label">Véhicule</span>
                <span class="info-value">{{ selectedVehicle.brand }} {{ selectedVehicle.model }}</span>
              </div>
              <div class="info-card">
                <span class="info-label">Immatriculation</span>
                <span class="info-value plate">{{ selectedVehicle.plate }}</span>
              </div>
            </div>

            <div class="form-group">
              <label>Conducteur au moment de l'accident *</label>
              <select [(ngModel)]="formData.driverId" (ngModelChange)="onDriverChange($event)" required>
                <option value="">Sélectionner un conducteur</option>
                <option *ngFor="let d of drivers" [value]="d.id">{{ d.name }}</option>
              </select>
            </div>

            <div class="driver-info" *ngIf="selectedDriver">
              <div class="info-card">
                <span class="info-label">N° Permis</span>
                <span class="info-value">{{ selectedDriver.licenseNumber }}</span>
              </div>
              <div class="info-card">
                <span class="info-label">Téléphone</span>
                <span class="info-value">{{ selectedDriver.phone }}</span>
              </div>
            </div>

            <div class="form-group">
              <label>Kilométrage au moment de l'accident</label>
              <input type="number" [(ngModel)]="formData.mileageAtAccident" placeholder="Ex: 45000">
            </div>
          </div>

          <!-- Step 3: Third Party -->
          <div class="step-content" *ngIf="currentStep === 3" @slideIn>
            <h3>👥 Tiers Impliqué</h3>

            <div class="toggle-group">
              <label class="toggle">
                <input type="checkbox" [(ngModel)]="formData.thirdPartyInvolved">
                <span class="toggle-slider"></span>
                <span class="toggle-label">Un tiers est impliqué dans l'accident</span>
              </label>
            </div>

            <div class="third-party-form" *ngIf="formData.thirdPartyInvolved" @slideIn>
              <div class="form-row">
                <div class="form-group">
                  <label>Nom du conducteur tiers</label>
                  <input type="text" [(ngModel)]="formData.thirdParty.name" placeholder="Nom complet">
                </div>
                <div class="form-group">
                  <label>Téléphone</label>
                  <input type="tel" [(ngModel)]="formData.thirdParty.phone" placeholder="+216 XX XXX XXX">
                </div>
              </div>

              <div class="form-row">
                <div class="form-group">
                  <label>Immatriculation véhicule tiers</label>
                  <input type="text" [(ngModel)]="formData.thirdParty.vehiclePlate" placeholder="Ex: 123 TUN 4567">
                </div>
                <div class="form-group">
                  <label>Modèle véhicule</label>
                  <input type="text" [(ngModel)]="formData.thirdParty.vehicleModel" placeholder="Ex: Peugeot 208">
                </div>
              </div>

              <div class="form-row">
                <div class="form-group">
                  <label>Compagnie d'assurance</label>
                  <input type="text" [(ngModel)]="formData.thirdParty.insuranceCompany" placeholder="Nom de l'assureur">
                </div>
                <div class="form-group">
                  <label>N° Police d'assurance</label>
                  <input type="text" [(ngModel)]="formData.thirdParty.insuranceNumber" placeholder="Numéro de police">
                </div>
              </div>

              <div class="form-group">
                <label>Témoins (optionnel)</label>
                <textarea [(ngModel)]="formData.witnesses" rows="2" placeholder="Noms et coordonnées des témoins"></textarea>
              </div>
            </div>

            <div class="no-third-party" *ngIf="!formData.thirdPartyInvolved">
              <div class="info-box">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/>
                </svg>
                <p>Aucun tiers impliqué. Passez à l'étape suivante pour décrire les dommages.</p>
              </div>
            </div>
          </div>

          <!-- Step 4: Damage Assessment -->
          <div class="step-content" *ngIf="currentStep === 4" @slideIn>
            <h3>💥 Évaluation des Dommages</h3>

            <div class="form-group">
              <label>Gravité des dommages *</label>
              <div class="severity-options">
                <label class="severity-option" *ngFor="let sev of severityOptions" 
                       [class.selected]="formData.severity === sev.value">
                  <input type="radio" name="severity" [value]="sev.value" [(ngModel)]="formData.severity">
                  <span class="severity-icon">{{ sev.icon }}</span>
                  <span class="severity-label">{{ sev.label }}</span>
                  <span class="severity-desc">{{ sev.description }}</span>
                </label>
              </div>
            </div>

            <div class="form-row">
              <div class="form-group">
                <label>Estimation des dommages (TND) *</label>
                <input type="number" [(ngModel)]="formData.estimatedDamage" placeholder="0.00" required>
              </div>
            </div>

            <div class="form-group">
              <label>Zones endommagées</label>
              <div class="damage-zones">
                <label class="zone-checkbox" *ngFor="let zone of damageZones">
                  <input type="checkbox" [checked]="formData.damagedZones.includes(zone.value)"
                         (change)="toggleDamageZone(zone.value)">
                  <span class="zone-icon">{{ zone.icon }}</span>
                  <span>{{ zone.label }}</span>
                </label>
              </div>
            </div>

            <div class="form-group">
              <label>Description détaillée des dommages *</label>
              <textarea [(ngModel)]="formData.description" rows="4" 
                        placeholder="Décrivez en détail les circonstances de l'accident et les dommages constatés..." required></textarea>
            </div>
          </div>

          <!-- Step 5: Photos & Documents -->
          <div class="step-content" *ngIf="currentStep === 5" @slideIn>
            <h3>📷 Photos & Documents</h3>

            <div class="upload-section">
              <div class="upload-area" (click)="triggerFileInput()" 
                   (dragover)="onDragOver($event)" (drop)="onDrop($event)">
                <input type="file" #fileInput multiple accept="image/*" (change)="onFilesSelected($event)" hidden>
                <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                  <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/>
                  <polyline points="17 8 12 3 7 8"/>
                  <line x1="12" y1="3" x2="12" y2="15"/>
                </svg>
                <p>Cliquez ou glissez-déposez vos photos ici</p>
                <span>JPG, PNG jusqu'à 10 Mo chacune</span>
              </div>

              <div class="photos-preview" *ngIf="formData.photos.length > 0">
                <div class="photo-item" *ngFor="let photo of formData.photos; let i = index">
                  <img [src]="photo.preview" [alt]="photo.name">
                  <button class="remove-photo" (click)="removePhoto(i)">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                    </svg>
                  </button>
                  <span class="photo-name">{{ photo.name }}</span>
                </div>
              </div>
            </div>

            <div class="form-group">
              <label>Documents joints (constat amiable, devis, etc.)</label>
              <div class="documents-list">
                <div class="document-item" *ngFor="let doc of formData.documents; let i = index">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                    <polyline points="14 2 14 8 20 8"/>
                  </svg>
                  <span>{{ doc.name }}</span>
                  <button class="remove-doc" (click)="removeDocument(i)">×</button>
                </div>
                <button class="btn-add-doc" (click)="triggerDocInput()">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
                  </svg>
                  Ajouter un document
                </button>
                <input type="file" #docInput accept=".pdf,.doc,.docx" (change)="onDocumentSelected($event)" hidden>
              </div>
            </div>

            <div class="form-group">
              <label>Notes additionnelles</label>
              <textarea [(ngModel)]="formData.additionalNotes" rows="3" 
                        placeholder="Informations complémentaires..."></textarea>
            </div>
          </div>

          <!-- Step 6: Review -->
          <div class="step-content" *ngIf="currentStep === 6" @slideIn>
            <h3>✅ Récapitulatif</h3>

            <div class="review-section">
              <div class="review-card">
                <h4>📍 Accident</h4>
                <div class="review-grid">
                  <div class="review-item">
                    <label>Date & Heure</label>
                    <span>{{ formData.accidentDate }} à {{ formData.accidentTime }}</span>
                  </div>
                  <div class="review-item">
                    <label>Lieu</label>
                    <span>{{ formData.location }}</span>
                  </div>
                  <div class="review-item" *ngIf="formData.policeReportNumber">
                    <label>Rapport Police</label>
                    <span>{{ formData.policeReportNumber }}</span>
                  </div>
                </div>
              </div>

              <div class="review-card">
                <h4>🚗 Véhicule</h4>
                <div class="review-grid">
                  <div class="review-item">
                    <label>Véhicule</label>
                    <span>{{ selectedVehicle?.name }} ({{ selectedVehicle?.plate }})</span>
                  </div>
                  <div class="review-item">
                    <label>Conducteur</label>
                    <span>{{ selectedDriver?.name }}</span>
                  </div>
                </div>
              </div>

              <div class="review-card" *ngIf="formData.thirdPartyInvolved">
                <h4>👥 Tiers</h4>
                <div class="review-grid">
                  <div class="review-item">
                    <label>Nom</label>
                    <span>{{ formData.thirdParty.name || 'Non renseigné' }}</span>
                  </div>
                  <div class="review-item">
                    <label>Véhicule</label>
                    <span>{{ formData.thirdParty.vehiclePlate || 'Non renseigné' }}</span>
                  </div>
                </div>
              </div>

              <div class="review-card highlight">
                <h4>💰 Dommages</h4>
                <div class="review-grid">
                  <div class="review-item">
                    <label>Gravité</label>
                    <span class="severity-badge" [class]="formData.severity">{{ getSeverityLabel(formData.severity) }}</span>
                  </div>
                  <div class="review-item">
                    <label>Estimation</label>
                    <span class="amount">{{ formatCurrency(formData.estimatedDamage) }}</span>
                  </div>
                </div>
                <div class="review-item full">
                  <label>Description</label>
                  <p>{{ formData.description }}</p>
                </div>
              </div>

              <div class="review-card" *ngIf="formData.photos.length > 0">
                <h4>📷 Photos ({{ formData.photos.length }})</h4>
                <div class="review-photos">
                  <img *ngFor="let photo of formData.photos" [src]="photo.preview" [alt]="photo.name">
                </div>
              </div>
            </div>

            <div class="submit-notice">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/>
              </svg>
              <p>Vérifiez les informations avant de soumettre. Vous pourrez encore modifier la déclaration tant qu'elle est en brouillon.</p>
            </div>
          </div>
        </div>

        <!-- Footer -->
        <div class="form-footer">
          <button class="btn-secondary" (click)="previousStep()" *ngIf="currentStep > 1">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="19" y1="12" x2="5" y2="12"/><polyline points="12 19 5 12 12 5"/>
            </svg>
            Précédent
          </button>
          <button class="btn-secondary" (click)="close()" *ngIf="currentStep === 1">
            Annuler
          </button>
          
          <div class="footer-right">
            <button class="btn-outline" (click)="saveAsDraft()" *ngIf="currentStep === 6">
              Enregistrer brouillon
            </button>
            <button class="btn-primary" (click)="nextStep()" *ngIf="currentStep < 6" [disabled]="!isStepValid()">
              Suivant
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="5" y1="12" x2="19" y2="12"/><polyline points="12 5 19 12 12 19"/>
              </svg>
            </button>
            <button class="btn-success" (click)="submit()" *ngIf="currentStep === 6">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/>
              </svg>
              Soumettre la déclaration
            </button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .form-overlay {
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(0, 0, 0, 0.6);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
      padding: 20px;
    }

    .form-container {
      background: white;
      border-radius: 16px;
      width: 100%;
      max-width: 900px;
      max-height: 90vh;
      display: flex;
      flex-direction: column;
      overflow: hidden;
    }

    /* Header */
    .form-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 20px 24px;
      border-bottom: 1px solid #e2e8f0;
    }

    .header-info h2 {
      font-size: 20px;
      font-weight: 700;
      color: #1e293b;
      margin: 0 0 4px;
    }

    .header-info p {
      font-size: 13px;
      color: #64748b;
      margin: 0;
    }

    .btn-close {
      width: 36px;
      height: 36px;
      border: none;
      background: #f1f5f9;
      border-radius: 8px;
      color: #64748b;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .btn-close:hover {
      background: #e2e8f0;
    }

    /* Progress Bar */
    .progress-bar {
      padding: 20px 24px;
      background: #f8fafc;
      border-bottom: 1px solid #e2e8f0;
    }

    .progress-steps {
      display: flex;
      justify-content: space-between;
      margin-bottom: 12px;
    }

    .step {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 8px;
      cursor: pointer;
      flex: 1;
    }

    .step-number {
      width: 32px;
      height: 32px;
      border-radius: 50%;
      background: #e2e8f0;
      color: #64748b;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 14px;
      font-weight: 600;
      transition: all 0.3s;
    }

    .step.active .step-number {
      background: #3b82f6;
      color: white;
    }

    .step.completed .step-number {
      background: #16a34a;
      color: white;
    }

    .step-label {
      font-size: 11px;
      color: #64748b;
      text-align: center;
    }

    .step.active .step-label {
      color: #3b82f6;
      font-weight: 600;
    }

    .step.completed .step-label {
      color: #16a34a;
    }

    .progress-line {
      height: 4px;
      background: #e2e8f0;
      border-radius: 2px;
      overflow: hidden;
    }

    .progress-fill {
      height: 100%;
      background: linear-gradient(90deg, #16a34a, #3b82f6);
      border-radius: 2px;
      transition: width 0.3s ease;
    }

    /* Form Content */
    .form-content {
      flex: 1;
      overflow-y: auto;
      padding: 24px;
    }

    .step-content h3 {
      font-size: 18px;
      font-weight: 600;
      color: #1e293b;
      margin: 0 0 24px;
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
      font-size: 13px;
      font-weight: 500;
      color: #374151;
      margin-bottom: 8px;
    }

    .form-group input,
    .form-group select,
    .form-group textarea {
      width: 100%;
      padding: 12px 14px;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      font-size: 14px;
      color: #1e293b;
      transition: all 0.2s;
    }

    .form-group input:focus,
    .form-group select:focus,
    .form-group textarea:focus {
      outline: none;
      border-color: #3b82f6;
      box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
    }

    .form-group textarea {
      resize: vertical;
      min-height: 100px;
    }

    /* Vehicle/Driver Info Cards */
    .vehicle-info,
    .driver-info {
      display: flex;
      gap: 12px;
      margin-bottom: 20px;
    }

    .info-card {
      flex: 1;
      padding: 12px 16px;
      background: #f8fafc;
      border-radius: 10px;
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .info-label {
      font-size: 11px;
      color: #64748b;
    }

    .info-value {
      font-size: 14px;
      font-weight: 600;
      color: #1e293b;
    }

    .info-value.plate {
      font-family: monospace;
    }

    /* Toggle */
    .toggle-group {
      margin-bottom: 24px;
    }

    .toggle {
      display: flex;
      align-items: center;
      gap: 12px;
      cursor: pointer;
    }

    .toggle input {
      display: none;
    }

    .toggle-slider {
      width: 48px;
      height: 26px;
      background: #e2e8f0;
      border-radius: 13px;
      position: relative;
      transition: background 0.3s;
    }

    .toggle-slider::after {
      content: '';
      position: absolute;
      width: 22px;
      height: 22px;
      background: white;
      border-radius: 50%;
      top: 2px;
      left: 2px;
      transition: transform 0.3s;
      box-shadow: 0 1px 3px rgba(0,0,0,0.2);
    }

    .toggle input:checked + .toggle-slider {
      background: #3b82f6;
    }

    .toggle input:checked + .toggle-slider::after {
      transform: translateX(22px);
    }

    .toggle-label {
      font-size: 14px;
      color: #1e293b;
    }

    /* Third Party Form */
    .third-party-form {
      background: #f8fafc;
      border-radius: 12px;
      padding: 20px;
      margin-top: 16px;
    }

    .no-third-party {
      margin-top: 16px;
    }

    .info-box {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 16px;
      background: #eff6ff;
      border-radius: 10px;
      color: #3b82f6;
    }

    .info-box p {
      margin: 0;
      font-size: 14px;
    }

    /* Severity Options */
    .severity-options {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 12px;
    }

    .severity-option {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 8px;
      padding: 16px 12px;
      border: 2px solid #e2e8f0;
      border-radius: 12px;
      cursor: pointer;
      transition: all 0.2s;
      text-align: center;
    }

    .severity-option:hover {
      border-color: #94a3b8;
    }

    .severity-option.selected {
      border-color: #3b82f6;
      background: #eff6ff;
    }

    .severity-option input {
      display: none;
    }

    .severity-icon {
      font-size: 28px;
    }

    .severity-label {
      font-size: 13px;
      font-weight: 600;
      color: #1e293b;
    }

    .severity-desc {
      font-size: 11px;
      color: #64748b;
    }

    /* Damage Zones */
    .damage-zones {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 10px;
    }

    .zone-checkbox {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 12px;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      cursor: pointer;
      font-size: 13px;
      transition: all 0.2s;
    }

    .zone-checkbox:hover {
      background: #f8fafc;
    }

    .zone-checkbox input:checked + .zone-icon + span {
      font-weight: 600;
    }

    .zone-checkbox input {
      display: none;
    }

    .zone-checkbox:has(input:checked) {
      border-color: #3b82f6;
      background: #eff6ff;
    }

    .zone-icon {
      font-size: 16px;
    }

    /* Upload Section */
    .upload-section {
      margin-bottom: 24px;
    }

    .upload-area {
      border: 2px dashed #e2e8f0;
      border-radius: 12px;
      padding: 40px;
      text-align: center;
      cursor: pointer;
      transition: all 0.2s;
    }

    .upload-area:hover {
      border-color: #3b82f6;
      background: #f8fafc;
    }

    .upload-area svg {
      color: #94a3b8;
      margin-bottom: 12px;
    }

    .upload-area p {
      font-size: 14px;
      color: #1e293b;
      margin: 0 0 4px;
    }

    .upload-area span {
      font-size: 12px;
      color: #64748b;
    }

    .photos-preview {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 12px;
      margin-top: 16px;
    }

    .photo-item {
      position: relative;
      aspect-ratio: 1;
      border-radius: 10px;
      overflow: hidden;
      background: #f1f5f9;
    }

    .photo-item img {
      width: 100%;
      height: 100%;
      object-fit: cover;
    }

    .remove-photo {
      position: absolute;
      top: 6px;
      right: 6px;
      width: 24px;
      height: 24px;
      background: rgba(0,0,0,0.6);
      border: none;
      border-radius: 50%;
      color: white;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .photo-name {
      position: absolute;
      bottom: 0;
      left: 0;
      right: 0;
      padding: 6px 8px;
      background: rgba(0,0,0,0.6);
      color: white;
      font-size: 10px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    /* Documents List */
    .documents-list {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .document-item {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 10px 14px;
      background: #f8fafc;
      border-radius: 8px;
      font-size: 13px;
    }

    .document-item svg {
      color: #3b82f6;
    }

    .document-item span {
      flex: 1;
    }

    .remove-doc {
      width: 24px;
      height: 24px;
      border: none;
      background: none;
      color: #94a3b8;
      font-size: 18px;
      cursor: pointer;
    }

    .remove-doc:hover {
      color: #dc2626;
    }

    .btn-add-doc {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 8px;
      padding: 12px;
      border: 1px dashed #e2e8f0;
      border-radius: 8px;
      background: none;
      color: #64748b;
      font-size: 13px;
      cursor: pointer;
    }

    .btn-add-doc:hover {
      border-color: #3b82f6;
      color: #3b82f6;
    }

    /* Review Section */
    .review-section {
      display: flex;
      flex-direction: column;
      gap: 16px;
    }

    .review-card {
      background: #f8fafc;
      border-radius: 12px;
      padding: 16px 20px;
    }

    .review-card.highlight {
      background: #eff6ff;
      border: 1px solid #bfdbfe;
    }

    .review-card h4 {
      font-size: 14px;
      font-weight: 600;
      color: #1e293b;
      margin: 0 0 12px;
    }

    .review-grid {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 12px;
    }

    .review-item {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .review-item.full {
      grid-column: 1 / -1;
      margin-top: 12px;
    }

    .review-item label {
      font-size: 11px;
      color: #64748b;
    }

    .review-item span,
    .review-item p {
      font-size: 14px;
      color: #1e293b;
      margin: 0;
    }

    .review-item .amount {
      font-weight: 700;
      font-size: 18px;
      color: #dc2626;
    }

    .review-photos {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
    }

    .review-photos img {
      width: 60px;
      height: 60px;
      object-fit: cover;
      border-radius: 8px;
    }

    .severity-badge {
      display: inline-flex;
      padding: 4px 10px;
      border-radius: 20px;
      font-size: 12px;
      font-weight: 600;
      width: fit-content;
    }

    .severity-badge.minor { background: #dcfce7; color: #16a34a; }
    .severity-badge.moderate { background: #fef3c7; color: #d97706; }
    .severity-badge.major { background: #fee2e2; color: #dc2626; }
    .severity-badge.total_loss { background: #1e293b; color: white; }

    .submit-notice {
      display: flex;
      align-items: flex-start;
      gap: 12px;
      padding: 16px;
      background: #fef3c7;
      border-radius: 10px;
      margin-top: 16px;
    }

    .submit-notice svg {
      color: #d97706;
      flex-shrink: 0;
    }

    .submit-notice p {
      margin: 0;
      font-size: 13px;
      color: #92400e;
    }

    /* Footer */
    .form-footer {
      display: flex;
      justify-content: space-between;
      padding: 16px 24px;
      border-top: 1px solid #e2e8f0;
      background: #f8fafc;
    }

    .footer-right {
      display: flex;
      gap: 12px;
    }

    .btn-secondary {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 12px 20px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      font-size: 14px;
      font-weight: 500;
      color: #64748b;
      cursor: pointer;
    }

    .btn-secondary:hover {
      background: #f1f5f9;
    }

    .btn-outline {
      padding: 12px 20px;
      background: white;
      border: 1px solid #3b82f6;
      border-radius: 10px;
      font-size: 14px;
      font-weight: 500;
      color: #3b82f6;
      cursor: pointer;
    }

    .btn-outline:hover {
      background: #eff6ff;
    }

    .btn-primary {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 12px 24px;
      background: linear-gradient(135deg, #3b82f6, #2563eb);
      color: white;
      border: none;
      border-radius: 10px;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-primary:hover:not(:disabled) {
      transform: translateY(-1px);
      box-shadow: 0 4px 12px rgba(59, 130, 246, 0.4);
    }

    .btn-primary:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }

    .btn-success {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 12px 24px;
      background: linear-gradient(135deg, #16a34a, #15803d);
      color: white;
      border: none;
      border-radius: 10px;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
    }

    .btn-success:hover {
      transform: translateY(-1px);
      box-shadow: 0 4px 12px rgba(22, 163, 74, 0.4);
    }

    /* Responsive */
    @media (max-width: 768px) {
      .form-container {
        max-height: 100vh;
        border-radius: 0;
      }

      .form-row {
        grid-template-columns: 1fr;
      }

      .severity-options {
        grid-template-columns: repeat(2, 1fr);
      }

      .damage-zones {
        grid-template-columns: repeat(2, 1fr);
      }

      .photos-preview {
        grid-template-columns: repeat(2, 1fr);
      }

      .progress-steps {
        overflow-x: auto;
        padding-bottom: 8px;
      }

      .step-label {
        display: none;
      }

      .form-footer {
        flex-direction: column;
        gap: 12px;
      }

      .footer-right {
        width: 100%;
        justify-content: stretch;
      }

      .footer-right button {
        flex: 1;
      }
    }
  `]
})
export class AccidentClaimFormComponent implements OnInit {
  @Input() claim: any = null;
  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<any>();

  currentStep = 1;
  totalSteps = 6;
  steps = ['Accident', 'Véhicule', 'Tiers', 'Dommages', 'Photos', 'Récapitulatif'];

  vehicles: Vehicle[] = [];
  drivers: Driver[] = [];
  selectedVehicle: Vehicle | null = null;
  selectedDriver: Driver | null = null;

  formData: any = {
    accidentDate: '',
    accidentTime: '',
    location: '',
    weatherConditions: '',
    roadConditions: '',
    policeReportNumber: '',
    vehicleId: '',
    driverId: '',
    mileageAtAccident: null,
    thirdPartyInvolved: false,
    thirdParty: {
      name: '',
      phone: '',
      vehiclePlate: '',
      vehicleModel: '',
      insuranceCompany: '',
      insuranceNumber: ''
    },
    witnesses: '',
    severity: '',
    estimatedDamage: null,
    damagedZones: [] as string[],
    description: '',
    photos: [] as { file: File; preview: string; name: string }[],
    documents: [] as { file: File; name: string }[],
    additionalNotes: ''
  };

  severityOptions = [
    { value: 'minor', label: 'Mineur', icon: '🟢', description: 'Rayures, bosses légères' },
    { value: 'moderate', label: 'Modéré', icon: '🟡', description: 'Dommages réparables' },
    { value: 'major', label: 'Majeur', icon: '🟠', description: 'Dommages importants' },
    { value: 'total_loss', label: 'Perte totale', icon: '🔴', description: 'Véhicule irrécupérable' }
  ];

  damageZones = [
    { value: 'front', label: 'Avant', icon: '⬆️' },
    { value: 'rear', label: 'Arrière', icon: '⬇️' },
    { value: 'left', label: 'Côté gauche', icon: '⬅️' },
    { value: 'right', label: 'Côté droit', icon: '➡️' },
    { value: 'roof', label: 'Toit', icon: '🔝' },
    { value: 'windshield', label: 'Pare-brise', icon: '🪟' },
    { value: 'lights', label: 'Éclairage', icon: '💡' },
    { value: 'mechanical', label: 'Mécanique', icon: '⚙️' }
  ];

  ngOnInit() {
    this.loadVehicles();
    this.loadDrivers();
    
    if (this.claim) {
      this.populateForm();
    } else {
      // Set default date to today
      const today = new Date();
      this.formData.accidentDate = today.toISOString().split('T')[0];
    }
  }

  loadVehicles() {
    // Demo data
    this.vehicles = [
      { id: 'v1', name: 'Peugeot 208', plate: '245 TUN 7890', brand: 'Peugeot', model: '208' },
      { id: 'v2', name: 'Renault Clio', plate: '189 TUN 4521', brand: 'Renault', model: 'Clio' },
      { id: 'v3', name: 'Citroën Berlingo', plate: '312 TUN 1122', brand: 'Citroën', model: 'Berlingo' },
      { id: 'v4', name: 'Volkswagen Caddy', plate: '456 TUN 7788', brand: 'Volkswagen', model: 'Caddy' },
      { id: 'v5', name: 'Fiat Ducato', plate: '678 TUN 3344', brand: 'Fiat', model: 'Ducato' }
    ];
  }

  loadDrivers() {
    // Demo data
    this.drivers = [
      { id: 'd1', name: 'Ahmed Ben Ali', licenseNumber: 'TN-2015-78542', phone: '+216 98 123 456' },
      { id: 'd2', name: 'Fatma Trabelsi', licenseNumber: 'TN-2018-45632', phone: '+216 97 654 321' },
      { id: 'd3', name: 'Mohamed Sassi', licenseNumber: 'TN-2012-12478', phone: '+216 95 789 012' },
      { id: 'd4', name: 'Sami Gharbi', licenseNumber: 'TN-2019-98745', phone: '+216 96 456 789' },
      { id: 'd5', name: 'Karim Mejri', licenseNumber: 'TN-2016-36985', phone: '+216 99 321 654' }
    ];
  }

  populateForm() {
    if (this.claim) {
      this.formData = {
        accidentDate: new Date(this.claim.accidentDate).toISOString().split('T')[0],
        accidentTime: this.claim.accidentTime,
        location: this.claim.location,
        weatherConditions: this.claim.weatherConditions || '',
        roadConditions: this.claim.roadConditions || '',
        policeReportNumber: this.claim.policeReportNumber || '',
        vehicleId: this.claim.vehicleId,
        driverId: this.claim.driverId || '',
        mileageAtAccident: this.claim.mileageAtAccident || null,
        thirdPartyInvolved: this.claim.thirdPartyInvolved,
        thirdParty: this.claim.thirdParty || {
          name: '', phone: '', vehiclePlate: '', vehicleModel: '',
          insuranceCompany: '', insuranceNumber: ''
        },
        witnesses: this.claim.witnesses || '',
        severity: this.claim.severity,
        estimatedDamage: this.claim.estimatedDamage,
        damagedZones: this.claim.damagedZones || [],
        description: this.claim.description,
        photos: [],
        documents: [],
        additionalNotes: this.claim.additionalNotes || ''
      };
      this.onVehicleChange(this.formData.vehicleId);
      this.onDriverChange(this.formData.driverId);
    }
  }

  onVehicleChange(vehicleId: string) {
    this.selectedVehicle = this.vehicles.find(v => v.id === vehicleId) || null;
  }

  onDriverChange(driverId: string) {
    this.selectedDriver = this.drivers.find(d => d.id === driverId) || null;
  }

  toggleDamageZone(zone: string) {
    const index = this.formData.damagedZones.indexOf(zone);
    if (index === -1) {
      this.formData.damagedZones.push(zone);
    } else {
      this.formData.damagedZones.splice(index, 1);
    }
  }

  // File handling
  triggerFileInput() {
    const input = document.querySelector('input[type="file"][accept="image/*"]') as HTMLInputElement;
    input?.click();
  }

  triggerDocInput() {
    const input = document.querySelector('input[accept=".pdf,.doc,.docx"]') as HTMLInputElement;
    input?.click();
  }

  onFilesSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    if (input.files) {
      Array.from(input.files).forEach(file => {
        const reader = new FileReader();
        reader.onload = (e) => {
          this.formData.photos.push({
            file,
            preview: e.target?.result as string,
            name: file.name
          });
        };
        reader.readAsDataURL(file);
      });
    }
  }

  onDocumentSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files[0]) {
      this.formData.documents.push({
        file: input.files[0],
        name: input.files[0].name
      });
    }
  }

  onDragOver(event: DragEvent) {
    event.preventDefault();
    event.stopPropagation();
  }

  onDrop(event: DragEvent) {
    event.preventDefault();
    event.stopPropagation();
    
    if (event.dataTransfer?.files) {
      Array.from(event.dataTransfer.files).forEach(file => {
        if (file.type.startsWith('image/')) {
          const reader = new FileReader();
          reader.onload = (e) => {
            this.formData.photos.push({
              file,
              preview: e.target?.result as string,
              name: file.name
            });
          };
          reader.readAsDataURL(file);
        }
      });
    }
  }

  removePhoto(index: number) {
    this.formData.photos.splice(index, 1);
  }

  removeDocument(index: number) {
    this.formData.documents.splice(index, 1);
  }

  // Navigation
  goToStep(step: number) {
    if (step < this.currentStep) {
      this.currentStep = step;
    }
  }

  nextStep() {
    if (this.currentStep < this.totalSteps && this.isStepValid()) {
      this.currentStep++;
    }
  }

  previousStep() {
    if (this.currentStep > 1) {
      this.currentStep--;
    }
  }

  isStepValid(): boolean {
    switch (this.currentStep) {
      case 1:
        return !!(this.formData.accidentDate && this.formData.accidentTime && this.formData.location);
      case 2:
        return !!(this.formData.vehicleId && this.formData.driverId);
      case 3:
        return true; // Third party is optional
      case 4:
        return !!(this.formData.severity && this.formData.estimatedDamage && this.formData.description);
      case 5:
        return true; // Photos are optional
      default:
        return true;
    }
  }

  // Actions
  saveAsDraft() {
    this.emitSave('draft');
  }

  submit() {
    this.emitSave('submitted');
  }

  emitSave(status: string) {
    const claimData = {
      vehicleId: this.formData.vehicleId,
      vehicleName: this.selectedVehicle?.name,
      vehiclePlate: this.selectedVehicle?.plate,
      driverName: this.selectedDriver?.name,
      driverId: this.formData.driverId,
      accidentDate: new Date(this.formData.accidentDate),
      accidentTime: this.formData.accidentTime,
      location: this.formData.location,
      weatherConditions: this.formData.weatherConditions,
      roadConditions: this.formData.roadConditions,
      policeReportNumber: this.formData.policeReportNumber,
      mileageAtAccident: this.formData.mileageAtAccident,
      thirdPartyInvolved: this.formData.thirdPartyInvolved,
      thirdParty: this.formData.thirdParty,
      witnesses: this.formData.witnesses,
      severity: this.formData.severity,
      estimatedDamage: this.formData.estimatedDamage,
      damagedZones: this.formData.damagedZones,
      description: this.formData.description,
      additionalNotes: this.formData.additionalNotes,
      status
    };
    this.saved.emit(claimData);
  }

  close() {
    this.closed.emit();
  }

  // Helpers
  getSeverityLabel(severity: string): string {
    const option = this.severityOptions.find(s => s.value === severity);
    return option?.label || severity;
  }

  formatCurrency(amount: number): string {
    return new Intl.NumberFormat('fr-TN', { style: 'currency', currency: 'TND' }).format(amount || 0);
  }
}
