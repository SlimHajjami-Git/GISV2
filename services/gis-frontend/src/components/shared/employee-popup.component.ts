import { Component, EventEmitter, Input, Output, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Vehicle } from '../../models/types';

@Component({
  selector: 'app-employee-popup',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="popup-overlay" *ngIf="isOpen" (click)="onOverlayClick($event)">
      <div class="popup-container" (click)="$event.stopPropagation()">
        <div class="popup-header">
          <div class="header-title">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/>
              <circle cx="9" cy="7" r="4"/>
              <path d="M23 21v-2a4 4 0 0 0-3-3.87"/>
              <path d="M16 3.13a4 4 0 0 1 0 7.75"/>
            </svg>
            <h2>{{ driver ? 'Modifier le chauffeur' : 'Nouveau chauffeur' }}</h2>
          </div>
          <button class="close-btn" (click)="close()">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="18" y1="6" x2="6" y2="18"/>
              <line x1="6" y1="6" x2="18" y2="18"/>
            </svg>
          </button>
        </div>

        <form class="popup-body" (ngSubmit)="onSubmit()">
          <div class="error-banner" *ngIf="errorMessage">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/>
            </svg>
            {{ errorMessage }}
          </div>
          <!-- Section: Identité -->
          <div class="form-section">
            <div class="section-header">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/>
                <circle cx="12" cy="7" r="4"/>
              </svg>
              <span>Identité</span>
            </div>
            <div class="form-grid">
              <div class="form-group">
                <label for="firstName">Prénom *</label>
                <input type="text" id="firstName" name="firstName" [(ngModel)]="formData.firstName" required placeholder="Ex: Mohamed" />
              </div>
              <div class="form-group">
                <label for="lastName">Nom *</label>
                <input type="text" id="lastName" name="lastName" [(ngModel)]="formData.lastName" required placeholder="Ex: Ben Ali" />
              </div>
              <div class="form-group">
                <label for="email">Email *</label>
                <input type="email" id="email" name="email" [(ngModel)]="formData.email" required placeholder="chauffeur@exemple.com" [disabled]="!!driver" />
              </div>
              <div class="form-group">
                <label for="phone">Téléphone</label>
                <input type="tel" id="phone" name="phone" [(ngModel)]="formData.phone" placeholder="+216 50 123 456" />
              </div>
              <div class="form-group" *ngIf="!driver">
                <label for="password">Mot de passe *</label>
                <input type="password" id="password" name="password" [(ngModel)]="formData.password" required placeholder="••••••••" />
              </div>
              <div class="form-group">
                <label for="cin">N° CIN</label>
                <input type="text" id="cin" name="cin" [(ngModel)]="formData.cin" placeholder="Ex: 12345678" />
              </div>
              <div class="form-group">
                <label for="dateOfBirth">Date de naissance</label>
                <input type="date" id="dateOfBirth" name="dateOfBirth" [(ngModel)]="formData.dateOfBirth" />
              </div>
            </div>
          </div>

          <!-- Section: Permis de conduire -->
          <div class="form-section">
            <div class="section-header permit">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="2" y="5" width="20" height="14" rx="2"/>
                <line x1="2" y1="10" x2="22" y2="10"/>
              </svg>
              <span>Permis de conduire</span>
            </div>
            <div class="form-grid">
              <div class="form-group">
                <label for="permitNumber">N° Permis</label>
                <input type="text" id="permitNumber" name="permitNumber" [(ngModel)]="formData.permitNumber" placeholder="Ex: 123456" />
              </div>
              <div class="form-group">
                <label for="permitType">Catégorie</label>
                <select id="permitType" name="permitType" [(ngModel)]="formData.permitType">
                  <option value="">Sélectionner</option>
                  <option value="B">B - Véhicule léger</option>
                  <option value="C">C - Poids lourd</option>
                  <option value="D">D - Transport en commun</option>
                  <option value="CE">CE - Super poids lourd</option>
                  <option value="DE">DE - Transport + remorque</option>
                </select>
              </div>
              <div class="form-group">
                <label for="permitExpiry">Date d'expiration</label>
                <input type="date" id="permitExpiry" name="permitExpiry" [(ngModel)]="formData.permitExpiry" />
              </div>
            </div>
          </div>

          <!-- Section: Véhicule assigné -->
          <div class="form-section">
            <div class="section-header vehicle">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M5 17h-2v-6l2-5h9l4 5v6h-2"/>
                <circle cx="7" cy="17" r="2"/>
                <circle cx="17" cy="17" r="2"/>
              </svg>
              <span>Véhicule assigné</span>
            </div>
            <div class="form-group">
              <label for="assignedVehicleId">Véhicule</label>
              <select id="assignedVehicleId" name="assignedVehicleId" [(ngModel)]="formData.assignedVehicleId">
                <option [ngValue]="null">Aucun véhicule</option>
                <option *ngFor="let v of vehicles" [ngValue]="v.id">{{ v.name }} ({{ v.plate }})</option>
              </select>
            </div>
          </div>

          <!-- Section: Statut -->
          <div class="form-section" *ngIf="driver">
            <div class="form-group">
              <label for="status">Statut</label>
              <select id="status" name="status" [(ngModel)]="formData.status">
                <option value="active">Actif</option>
                <option value="inactive">Inactif</option>
              </select>
            </div>
          </div>

          <div class="popup-footer">
            <button type="button" class="btn-secondary" (click)="close()">Annuler</button>
            <button type="submit" class="btn-primary">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="20 6 9 17 4 12"/>
              </svg>
              {{ driver ? 'Enregistrer' : 'Créer le chauffeur' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `,
  styles: [`
    .popup-overlay {
      position: fixed;
      top: 42px; left: 0; right: 0; bottom: 0; /* top: 42px = below navbar */
      background: rgba(15, 23, 42, 0.6);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1050;
      padding: 20px;
      animation: fadeIn 0.2s ease-out;
    }

    @keyframes fadeIn { from { opacity: 0; } to { opacity: 1; } }

    .popup-container {
      background: white;
      border-radius: 6px;
      box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.15);
      max-width: 580px;
      width: 100%;
      max-height: 90vh;
      overflow: hidden;
      display: flex;
      flex-direction: column;
      animation: slideUp 0.3s ease-out;
    }

    @keyframes slideUp {
      from { transform: translateY(20px); opacity: 0; }
      to { transform: translateY(0); opacity: 1; }
    }

    .popup-header {
      padding: 14px 20px;
      border-bottom: 1px solid #e2e8f0;
      display: flex;
      align-items: center;
      justify-content: space-between;
      background: #f8fafc;
    }

    .header-title {
      display: flex;
      align-items: center;
      gap: 10px;
      color: #1e293b;
    }

    .header-title svg { color: #6366f1; }

    .popup-header h2 {
      margin: 0;
      font-size: 14px;
      font-weight: 600;
      color: #1e293b;
    }

    .close-btn {
      background: none;
      border: none;
      color: #94a3b8;
      cursor: pointer;
      padding: 6px;
      border-radius: 3px;
      transition: all 0.15s;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .close-btn:hover {
      background: #f1f5f9;
      color: #1e293b;
    }

    .popup-body {
      padding: 0;
      overflow-y: auto;
      flex: 1;
    }

    .form-section {
      padding: 16px 20px;
      border-bottom: 1px solid #f1f5f9;
    }

    .form-section:last-of-type {
      border-bottom: none;
    }

    .section-header {
      display: flex;
      align-items: center;
      gap: 8px;
      margin-bottom: 14px;
      font-weight: 600;
      font-size: 12px;
      color: #6366f1;
      text-transform: uppercase;
      letter-spacing: 0.3px;
    }

    .section-header svg { color: #6366f1; }
    .section-header.permit { color: #f59e0b; }
    .section-header.permit svg { color: #f59e0b; }
    .section-header.vehicle { color: #10b981; }
    .section-header.vehicle svg { color: #10b981; }

    .form-grid {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 14px;
    }

    .form-group {
      display: flex;
      flex-direction: column;
      gap: 5px;
    }

    .form-group label {
      font-size: 11px;
      font-weight: 500;
      color: #64748b;
    }

    .form-group input,
    .form-group select {
      padding: 8px 12px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 3px;
      color: #1e293b;
      font-family: var(--font-family);
      font-size: 12px;
      transition: all 0.15s;
    }

    .form-group input:focus,
    .form-group select:focus {
      outline: none;
      border-color: #6366f1;
      box-shadow: 0 0 0 2px rgba(99, 102, 241, 0.08);
    }

    .form-group input::placeholder {
      color: #94a3b8;
    }

    .popup-footer {
      padding: 14px 20px;
      border-top: 1px solid #e2e8f0;
      display: flex;
      gap: 10px;
      justify-content: flex-end;
      background: #f8fafc;
    }

    .btn-primary {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 8px 16px;
      background: #6366f1;
      color: white;
      border: none;
      border-radius: 3px;
      font-family: var(--font-family);
      font-weight: 500;
      font-size: 12px;
      cursor: pointer;
      transition: all 0.15s;
    }

    .btn-primary:hover { background: #4f46e5; }

    .btn-secondary {
      padding: 8px 16px;
      background: white;
      color: #64748b;
      border: 1px solid #e2e8f0;
      border-radius: 3px;
      font-family: var(--font-family);
      font-weight: 500;
      font-size: 12px;
      cursor: pointer;
      transition: all 0.15s;
    }

    .btn-secondary:hover {
      background: #f8fafc;
      color: #1e293b;
    }

    .error-banner {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 14px;
      background: #fef2f2;
      border: 1px solid #fecaca;
      border-radius: 6px;
      color: #dc2626;
      font-size: 13px;
      font-weight: 500;
      margin-bottom: 4px;
    }

    .error-banner svg { flex-shrink: 0; color: #dc2626; }

    @media (max-width: 640px) {
      .form-grid { grid-template-columns: 1fr; }
      .popup-container { max-height: 100vh; border-radius: 0; }
    }
  `]
})
export class EmployeePopupComponent implements OnChanges {
  @Input() isOpen = false;
  @Input() driver: any = null;
  @Input() vehicles: Vehicle[] = [];
  @Input() errorMessage = '';
  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<any>();

  formData: any = {};

  ngOnChanges(changes: SimpleChanges) {
    if (changes['driver'] || changes['isOpen']) {
      this.initForm();
    }
  }

  private initForm() {
    if (this.driver) {
      this.formData = {
        firstName: this.driver.firstName || '',
        lastName: this.driver.lastName || '',
        email: this.driver.email || '',
        phone: this.driver.phone || '',
        permitNumber: this.driver.permitNumber || '',
        permitType: this.driver.permitType || '',
        permitExpiry: this.driver.permitExpiry ? new Date(this.driver.permitExpiry).toISOString().split('T')[0] : '',
        cin: this.driver.cin || '',
        dateOfBirth: this.driver.dateOfBirth ? new Date(this.driver.dateOfBirth).toISOString().split('T')[0] : '',
        assignedVehicleId: this.driver.assignedVehicleId || null,
        status: this.driver.status || 'active'
      };
    } else {
      this.formData = {
        firstName: '', lastName: '', email: '', phone: '', password: '',
        permitNumber: '', permitType: '', permitExpiry: '',
        cin: '', dateOfBirth: '',
        assignedVehicleId: null, status: 'active'
      };
    }
  }

  onOverlayClick(event: MouseEvent) {
    if (event.target === event.currentTarget) {
      this.close();
    }
  }

  close() {
    this.closed.emit();
  }

  onSubmit() {
    this.saved.emit({
      firstName: this.formData.firstName,
      lastName: this.formData.lastName,
      email: this.formData.email,
      phone: this.formData.phone || null,
      password: this.formData.password || undefined,
      permitNumber: this.formData.permitNumber || null,
      permitType: this.formData.permitType || null,
      permitExpiry: this.formData.permitExpiry || null,
      cin: this.formData.cin || null,
      dateOfBirth: this.formData.dateOfBirth || null,
      assignedVehicleId: this.formData.assignedVehicleId || null,
      status: this.formData.status
    });
  }
}
