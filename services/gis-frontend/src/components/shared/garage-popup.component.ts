import { Component, Input, Output, EventEmitter, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';

// Interface pour les garages
export interface Garage {
  id?: string;
  name: string;
  address: string;
  city: string;
  postalCode: string;
  phone: string;
  email: string;
  contactName: string;
  services: string[];
  rating: number;
  isActive: boolean;
  notes?: string;
  createdAt?: Date;
  updatedAt?: Date;
}

@Component({
  selector: 'app-garage-popup',
  standalone: true,
  imports: [CommonModule, FormsModule],
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 }))
      ]),
      transition(':leave', [
        animate('150ms ease-in', style({ opacity: 0 }))
      ])
    ]),
    trigger('slideIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateX(20px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateX(0)' }))
      ]),
      transition(':leave', [
        animate('200ms ease-in', style({ opacity: 0, transform: 'translateX(20px)' }))
      ])
    ])
  ],
  template: `
    <div class="popup-overlay" *ngIf="isOpen" @fadeIn (click)="close()">
      <div class="popup-container" @slideIn (click)="$event.stopPropagation()">
        <!-- Header -->
        <div class="popup-header">
          <h2>{{ garage?.id ? 'Modifier le garage' : 'Nouveau garage' }}</h2>
          <button class="btn-close" (click)="close()">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="18" y1="6" x2="6" y2="18"/>
              <line x1="6" y1="6" x2="18" y2="18"/>
            </svg>
          </button>
        </div>

        <!-- Formulaire -->
        <form class="popup-form" (ngSubmit)="onSubmit()" #garageForm="ngForm">
          <!-- Section Informations générales -->
          <div class="form-section">
            <h3 class="section-title">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M3 21h18"/>
                <path d="M5 21V7l8-4 8 4v14"/>
                <path d="M9 21v-4a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2v4"/>
              </svg>
              Informations générales
            </h3>
            
            <div class="form-row">
              <div class="form-group full">
                <label for="name">Nom du garage *</label>
                <input type="text" id="name" name="name" [(ngModel)]="formData.name" required
                       placeholder="Ex: Garage Central Tunis" class="form-control"
                       [class.invalid]="nameInput.invalid && nameInput.touched" #nameInput="ngModel">
                <span class="error-message" *ngIf="nameInput.invalid && nameInput.touched">
                  Le nom est requis
                </span>
              </div>
            </div>

            <div class="form-row">
              <div class="form-group full">
                <label for="address">Adresse *</label>
                <input type="text" id="address" name="address" [(ngModel)]="formData.address" required
                       placeholder="Ex: 45 Avenue Habib Bourguiba" class="form-control"
                       [class.invalid]="addressInput.invalid && addressInput.touched" #addressInput="ngModel">
                <span class="error-message" *ngIf="addressInput.invalid && addressInput.touched">
                  L'adresse est requise
                </span>
              </div>
            </div>

            <div class="form-row two-cols">
              <div class="form-group">
                <label for="city">Ville *</label>
                <input type="text" id="city" name="city" [(ngModel)]="formData.city" required
                       placeholder="Ex: Tunis" class="form-control"
                       [class.invalid]="cityInput.invalid && cityInput.touched" #cityInput="ngModel">
                <span class="error-message" *ngIf="cityInput.invalid && cityInput.touched">
                  La ville est requise
                </span>
              </div>
              <div class="form-group">
                <label for="postalCode">Code postal</label>
                <input type="text" id="postalCode" name="postalCode" [(ngModel)]="formData.postalCode"
                       placeholder="Ex: 1000" class="form-control">
              </div>
            </div>
          </div>

          <!-- Section Contact -->
          <div class="form-section">
            <h3 class="section-title">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72 12.84 12.84 0 0 0 .7 2.81 2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45 12.84 12.84 0 0 0 2.81.7A2 2 0 0 1 22 16.92z"/>
              </svg>
              Contact
            </h3>

            <div class="form-row two-cols">
              <div class="form-group">
                <label for="phone">Téléphone *</label>
                <input type="tel" id="phone" name="phone" [(ngModel)]="formData.phone" required
                       placeholder="+216 XX XXX XXX" class="form-control"
                       [class.invalid]="phoneInput.invalid && phoneInput.touched" #phoneInput="ngModel">
                <span class="error-message" *ngIf="phoneInput.invalid && phoneInput.touched">
                  Le téléphone est requis
                </span>
              </div>
              <div class="form-group">
                <label for="email">Email</label>
                <input type="email" id="email" name="email" [(ngModel)]="formData.email"
                       placeholder="contact@garage.tn" class="form-control">
              </div>
            </div>

            <div class="form-row">
              <div class="form-group full">
                <label for="contactName">Nom du contact</label>
                <input type="text" id="contactName" name="contactName" [(ngModel)]="formData.contactName"
                       placeholder="Ex: Mohamed Ben Ali" class="form-control">
              </div>
            </div>
          </div>

          <!-- Section Services -->
          <div class="form-section">
            <h3 class="section-title">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>
              </svg>
              Services proposés
            </h3>

            <div class="services-grid">
              <label class="service-checkbox" *ngFor="let service of availableServices">
                <input type="checkbox" [checked]="isServiceSelected(service.value)" 
                       (change)="toggleService(service.value)">
                <span class="checkbox-custom"></span>
                <span class="service-label">{{ service.label }}</span>
              </label>
            </div>
          </div>

          <!-- Section Évaluation et statut -->
          <div class="form-section">
            <h3 class="section-title">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/>
              </svg>
              Évaluation et statut
            </h3>

            <div class="form-row two-cols">
              <div class="form-group">
                <label for="rating">Note (0-5)</label>
                <div class="rating-input">
                  <input type="range" id="rating" name="rating" [(ngModel)]="formData.rating" 
                         min="0" max="5" step="0.5" class="range-slider">
                  <span class="rating-value">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="#f59e0b">
                      <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/>
                    </svg>
                    {{ formData.rating.toFixed(1) }}
                  </span>
                </div>
              </div>
              <div class="form-group">
                <label>Statut</label>
                <div class="status-toggle">
                  <button type="button" class="toggle-btn" [class.active]="formData.isActive" 
                          (click)="formData.isActive = true">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                      <polyline points="22 4 12 14.01 9 11.01"/>
                    </svg>
                    Actif
                  </button>
                  <button type="button" class="toggle-btn" [class.active]="!formData.isActive" 
                          (click)="formData.isActive = false">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <circle cx="12" cy="12" r="10"/>
                      <line x1="15" y1="9" x2="9" y2="15"/>
                      <line x1="9" y1="9" x2="15" y2="15"/>
                    </svg>
                    Inactif
                  </button>
                </div>
              </div>
            </div>

            <div class="form-row">
              <div class="form-group full">
                <label for="notes">Notes / Remarques</label>
                <textarea id="notes" name="notes" [(ngModel)]="formData.notes" rows="3"
                          placeholder="Informations complémentaires..." class="form-control"></textarea>
              </div>
            </div>
          </div>

          <!-- Actions -->
          <div class="popup-actions">
            <button type="button" class="btn-cancel" (click)="close()">Annuler</button>
            <button type="submit" class="btn-save" [disabled]="garageForm.invalid">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/>
                <polyline points="17 21 17 13 7 13 7 21"/>
                <polyline points="7 3 7 8 15 8"/>
              </svg>
              {{ garage?.id ? 'Enregistrer' : 'Créer le garage' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `,
  styles: [`
    /* ===== OVERLAY ===== */
    .popup-overlay {
      position: fixed;
      inset: 0;
      background: rgba(0, 0, 0, 0.5);
      display: flex;
      justify-content: flex-end;
      z-index: 1000;
    }

    /* ===== CONTAINER ===== */
    .popup-container {
      width: 100%;
      max-width: 520px;
      height: 100vh;
      background: white;
      display: flex;
      flex-direction: column;
      overflow: hidden;
    }

    /* ===== HEADER ===== */
    .popup-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 16px 20px;
      background: linear-gradient(135deg, #1e3a5f 0%, #2d5a87 100%);
      color: white;
    }

    .popup-header h2 {
      margin: 0;
      font-size: 18px;
      font-weight: 600;
    }

    .btn-close {
      background: rgba(255,255,255,0.1);
      border: none;
      width: 32px;
      height: 32px;
      border-radius: 6px;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      color: white;
      transition: background 0.2s;
    }

    .btn-close:hover {
      background: rgba(255,255,255,0.2);
    }

    /* ===== FORM ===== */
    .popup-form {
      flex: 1;
      overflow-y: auto;
      padding: 20px;
    }

    .form-section {
      margin-bottom: 24px;
      padding-bottom: 20px;
      border-bottom: 1px solid #e2e8f0;
    }

    .form-section:last-of-type {
      border-bottom: none;
    }

    .section-title {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 14px;
      font-weight: 600;
      color: #1e293b;
      margin: 0 0 16px;
    }

    .section-title svg {
      color: #3b82f6;
    }

    .form-row {
      margin-bottom: 16px;
    }

    .form-row:last-child {
      margin-bottom: 0;
    }

    .form-row.two-cols {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 16px;
    }

    .form-group {
      display: flex;
      flex-direction: column;
    }

    .form-group.full {
      width: 100%;
    }

    .form-group label {
      font-size: 12px;
      font-weight: 500;
      color: #64748b;
      margin-bottom: 6px;
    }

    .form-control {
      padding: 10px 12px;
      border: 1px solid #e2e8f0;
      border-radius: 6px;
      font-size: 13px;
      font-family: inherit;
      color: #1e293b;
      transition: border-color 0.2s, box-shadow 0.2s;
    }

    .form-control:focus {
      outline: none;
      border-color: #3b82f6;
      box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
    }

    .form-control.invalid {
      border-color: #ef4444;
    }

    .form-control::placeholder {
      color: #94a3b8;
    }

    textarea.form-control {
      resize: vertical;
      min-height: 80px;
    }

    .error-message {
      font-size: 11px;
      color: #ef4444;
      margin-top: 4px;
    }

    /* ===== SERVICES GRID ===== */
    .services-grid {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 10px;
    }

    .service-checkbox {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 10px 12px;
      background: #f8fafc;
      border-radius: 6px;
      cursor: pointer;
      transition: background 0.2s;
    }

    .service-checkbox:hover {
      background: #f1f5f9;
    }

    .service-checkbox input {
      display: none;
    }

    .checkbox-custom {
      width: 18px;
      height: 18px;
      border: 2px solid #cbd5e1;
      border-radius: 4px;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }

    .service-checkbox input:checked + .checkbox-custom {
      background: #3b82f6;
      border-color: #3b82f6;
    }

    .service-checkbox input:checked + .checkbox-custom::after {
      content: '';
      width: 5px;
      height: 9px;
      border: solid white;
      border-width: 0 2px 2px 0;
      transform: rotate(45deg);
      margin-top: -2px;
    }

    .service-label {
      font-size: 12px;
      color: #1e293b;
    }

    /* ===== RATING INPUT ===== */
    .rating-input {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .range-slider {
      flex: 1;
      -webkit-appearance: none;
      height: 6px;
      background: #e2e8f0;
      border-radius: 3px;
      outline: none;
    }

    .range-slider::-webkit-slider-thumb {
      -webkit-appearance: none;
      width: 18px;
      height: 18px;
      background: #f59e0b;
      border-radius: 50%;
      cursor: pointer;
      box-shadow: 0 2px 4px rgba(0,0,0,0.2);
    }

    .rating-value {
      display: flex;
      align-items: center;
      gap: 4px;
      font-size: 14px;
      font-weight: 600;
      color: #f59e0b;
      min-width: 50px;
    }

    /* ===== STATUS TOGGLE ===== */
    .status-toggle {
      display: flex;
      gap: 8px;
    }

    .toggle-btn {
      flex: 1;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 6px;
      padding: 10px 12px;
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 6px;
      font-size: 12px;
      color: #64748b;
      cursor: pointer;
      transition: all 0.2s;
    }

    .toggle-btn.active {
      background: #dcfce7;
      border-color: #22c55e;
      color: #16a34a;
    }

    .toggle-btn:not(.active):hover {
      background: #f1f5f9;
    }

    /* ===== ACTIONS ===== */
    .popup-actions {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 16px 20px;
      background: #f8fafc;
      border-top: 1px solid #e2e8f0;
    }

    .btn-cancel {
      padding: 10px 20px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 6px;
      font-size: 13px;
      color: #64748b;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-cancel:hover {
      background: #f1f5f9;
      border-color: #cbd5e1;
    }

    .btn-save {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 20px;
      background: linear-gradient(135deg, #3b82f6 0%, #2563eb 100%);
      border: none;
      border-radius: 6px;
      font-size: 13px;
      font-weight: 500;
      color: white;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-save:hover:not(:disabled) {
      background: linear-gradient(135deg, #2563eb 0%, #1d4ed8 100%);
    }

    .btn-save:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }

    /* ===== RESPONSIVE ===== */
    @media (max-width: 600px) {
      .popup-container {
        max-width: 100%;
      }

      .form-row.two-cols {
        grid-template-columns: 1fr;
      }

      .services-grid {
        grid-template-columns: 1fr;
      }
    }
  `]
})
export class GaragePopupComponent implements OnChanges {
  @Input() isOpen = false;
  @Input() garage: Garage | null = null;
  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<Garage>();

  formData: Garage = this.getEmptyGarage();

  availableServices = [
    { value: 'mecanique', label: 'Mécanique générale' },
    { value: 'carrosserie', label: 'Carrosserie' },
    { value: 'electricite', label: 'Électricité auto' },
    { value: 'pneumatique', label: 'Pneumatiques' },
    { value: 'vidange', label: 'Vidange & Entretien' },
    { value: 'climatisation', label: 'Climatisation' },
    { value: 'diagnostic', label: 'Diagnostic électronique' }
  ];

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['garage'] || changes['isOpen']) {
      if (this.isOpen) {
        this.formData = this.garage ? { ...this.garage, services: [...this.garage.services] } : this.getEmptyGarage();
      }
    }
  }

  getEmptyGarage(): Garage {
    return {
      name: '',
      address: '',
      city: '',
      postalCode: '',
      phone: '',
      email: '',
      contactName: '',
      services: [],
      rating: 3.0,
      isActive: true,
      notes: ''
    };
  }

  isServiceSelected(service: string): boolean {
    return this.formData.services.includes(service);
  }

  toggleService(service: string): void {
    const index = this.formData.services.indexOf(service);
    if (index === -1) {
      this.formData.services.push(service);
    } else {
      this.formData.services.splice(index, 1);
    }
  }

  close(): void {
    this.closed.emit();
  }

  onSubmit(): void {
    if (this.formData.name && this.formData.address && this.formData.city && this.formData.phone) {
      this.saved.emit({ ...this.formData });
    }
  }
}
