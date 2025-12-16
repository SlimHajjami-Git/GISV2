import { Component, EventEmitter, Input, Output, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Vehicle } from '../../models/types';

@Component({
  selector: 'app-vehicle-popup',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="popup-overlay" *ngIf="isOpen" (click)="onOverlayClick($event)">
      <div class="popup-container" (click)="$event.stopPropagation()">
        <div class="popup-header">
          <h2>{{ vehicle?.id ? 'Modifier le véhicule' : 'Nouveau véhicule' }}</h2>
          <button class="close-btn" (click)="close()">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="18" y1="6" x2="6" y2="18"/>
              <line x1="6" y1="6" x2="18" y2="18"/>
            </svg>
          </button>
        </div>

        <form class="popup-body" (ngSubmit)="onSubmit()">
          <div class="form-grid">
            <div class="form-group">
              <label for="name">Nom du véhicule *</label>
              <input
                type="text"
                id="name"
                name="name"
                [(ngModel)]="formData.name"
                required
                placeholder="Ex: Camion principal"
              />
            </div>

            <div class="form-group">
              <label for="plate">Plaque d'immatriculation *</label>
              <input
                type="text"
                id="plate"
                name="plate"
                [(ngModel)]="formData.plate"
                required
                placeholder="Ex: ABC-1234"
              />
            </div>

            <div class="form-group">
              <label for="brand">Marque *</label>
              <input
                type="text"
                id="brand"
                name="brand"
                [(ngModel)]="formData.brand"
                required
                placeholder="Ex: Toyota"
              />
            </div>

            <div class="form-group">
              <label for="model">Modèle *</label>
              <input
                type="text"
                id="model"
                name="model"
                [(ngModel)]="formData.model"
                required
                placeholder="Ex: Corolla"
              />
            </div>

            <div class="form-group">
              <label for="year">Année *</label>
              <input
                type="number"
                id="year"
                name="year"
                [(ngModel)]="formData.year"
                required
                min="1900"
                max="2100"
                placeholder="Ex: 2023"
              />
            </div>

            <div class="form-group">
              <label for="type">Type *</label>
              <select id="type" name="type" [(ngModel)]="formData.type" required>
                <option value="">Sélectionner un type</option>
                <option value="camion">Camion</option>
                <option value="citadine">Citadine</option>
                <option value="suv">SUV</option>
                <option value="utilitaire">Utilitaire</option>
                <option value="other">Autre</option>
              </select>
            </div>

            <div class="form-group">
              <label for="status">Statut *</label>
              <select id="status" name="status" [(ngModel)]="formData.status" required>
                <option value="">Sélectionner un statut</option>
                <option value="available">Disponible</option>
                <option value="in_use">En service</option>
                <option value="maintenance">En maintenance</option>
              </select>
            </div>

            <div class="form-group">
              <label for="mileage">Kilométrage *</label>
              <input
                type="number"
                id="mileage"
                name="mileage"
                [(ngModel)]="formData.mileage"
                required
                min="0"
                placeholder="Ex: 50000"
              />
            </div>

            <div class="form-group full-width">
              <label class="checkbox-label">
                <input
                  type="checkbox"
                  name="hasGPS"
                  [(ngModel)]="formData.hasGPS"
                />
                <span>Ce véhicule dispose d'un GPS</span>
              </label>
            </div>
          </div>

          <div class="popup-footer">
            <button type="button" class="btn-secondary" (click)="close()">
              Annuler
            </button>
            <button type="submit" class="btn-primary">
              {{ vehicle?.id ? 'Mettre à jour' : 'Ajouter' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `,
  styles: [`
    .popup-overlay {
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(0, 0, 0, 0.5);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
      padding: 20px;
      animation: fadeIn 0.2s ease-out;
    }

    @keyframes fadeIn {
      from {
        opacity: 0;
      }
      to {
        opacity: 1;
      }
    }

    .popup-container {
      background: white;
      border-radius: 6px;
      box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.15);
      max-width: 600px;
      width: 100%;
      max-height: 90vh;
      overflow: hidden;
      display: flex;
      flex-direction: column;
      animation: slideUp 0.3s ease-out;
    }

    @keyframes slideUp {
      from {
        transform: translateY(20px);
        opacity: 0;
      }
      to {
        transform: translateY(0);
        opacity: 1;
      }
    }

    .popup-header {
      padding: 14px 20px;
      border-bottom: 1px solid #e2e8f0;
      display: flex;
      align-items: center;
      justify-content: space-between;
      background: #f8fafc;
    }

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
      padding: 20px;
      overflow-y: auto;
      flex: 1;
    }

    .form-grid {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 16px;
    }

    .form-group {
      display: flex;
      flex-direction: column;
      gap: 6px;
    }

    .form-group.full-width {
      grid-column: 1 / -1;
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
      border-color: #3b82f6;
    }

    .form-group input::placeholder {
      color: #94a3b8;
    }

    .checkbox-label {
      display: flex;
      align-items: center;
      gap: 8px;
      cursor: pointer;
      user-select: none;
    }

    .checkbox-label input[type="checkbox"] {
      width: 14px;
      height: 14px;
      cursor: pointer;
      accent-color: #3b82f6;
    }

    .checkbox-label span {
      font-weight: 500;
      font-size: 12px;
      color: #1e293b;
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
      padding: 8px 16px;
      background: #3b82f6;
      color: white;
      border: none;
      border-radius: 3px;
      font-family: var(--font-family);
      font-weight: 500;
      font-size: 12px;
      cursor: pointer;
      transition: all 0.15s;
    }

    .btn-primary:hover {
      background: #2563eb;
    }

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

    @media (max-width: 640px) {
      .form-grid {
        grid-template-columns: 1fr;
      }

      .popup-container {
        max-height: 100vh;
        border-radius: 0;
      }

      .popup-header,
      .popup-body,
      .popup-footer {
        padding: 20px;
      }
    }
  `]
})
export class VehiclePopupComponent implements OnInit {
  @Input() isOpen = false;
  @Input() vehicle: Vehicle | null = null;
  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<Partial<Vehicle>>();

  formData: Partial<Vehicle> = {
    name: '',
    plate: '',
    brand: '',
    model: '',
    year: new Date().getFullYear(),
    type: 'citadine',
    status: 'available',
    mileage: 0,
    hasGPS: false
  };

  ngOnInit() {
    if (this.vehicle) {
      this.formData = { ...this.vehicle };
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
    this.saved.emit(this.formData);
  }
}
