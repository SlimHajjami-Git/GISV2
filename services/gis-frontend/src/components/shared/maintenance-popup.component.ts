import { Component, EventEmitter, Input, Output, OnInit, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MaintenanceRecord, Vehicle } from '../../models/types';

@Component({
  selector: 'app-maintenance-popup',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="popup-overlay" *ngIf="isOpen" (click)="onOverlayClick($event)">
      <div class="popup-container" (click)="$event.stopPropagation()">
        <div class="popup-header">
          <h2>{{ record?.id ? 'Modifier la maintenance' : 'Nouvelle maintenance' }}</h2>
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
              <label for="vehicleId">Véhicule *</label>
              <select id="vehicleId" name="vehicleId" [(ngModel)]="formData.vehicleId" required>
                <option value="">Sélectionner un véhicule</option>
                @for (vehicle of vehicles; track vehicle.id) {
                  <option [value]="vehicle.id">{{ vehicle.name }} ({{ vehicle.plate }})</option>
                }
              </select>
            </div>

            <div class="form-group">
              <label for="type">Type de maintenance *</label>
              <select id="type" name="type" [(ngModel)]="formData.type" required>
                <option value="">Sélectionner un type</option>
                <option value="scheduled">Révision programmée</option>
                <option value="repair">Réparation</option>
                <option value="oil_change">Vidange</option>
                <option value="tire_change">Pneumatiques</option>
                <option value="inspection">Contrôle technique</option>
                <option value="other">Autre</option>
              </select>
            </div>

            <div class="form-group">
              <label for="date">Date *</label>
              <input
                type="date"
                id="date"
                name="date"
                [(ngModel)]="formData.dateStr"
                required
              />
            </div>

            <div class="form-group">
              <label for="status">Statut *</label>
              <select id="status" name="status" [(ngModel)]="formData.status" required>
                <option value="scheduled">Planifié</option>
                <option value="in_progress">En cours</option>
                <option value="completed">Terminé</option>
                <option value="cancelled">Annulé</option>
              </select>
            </div>

            <div class="form-group full-width">
              <label for="description">Description *</label>
              <textarea
                id="description"
                name="description"
                [(ngModel)]="formData.description"
                required
                rows="2"
                placeholder="Décrivez les travaux effectués ou à effectuer..."
              ></textarea>
            </div>

            <div class="form-group">
              <label for="mileageAtService">Kilométrage</label>
              <input
                type="number"
                id="mileageAtService"
                name="mileageAtService"
                [(ngModel)]="formData.mileageAtService"
                min="0"
                placeholder="Ex: 50000"
              />
            </div>

            <div class="form-group">
              <label for="serviceProvider">Prestataire</label>
              <input
                type="text"
                id="serviceProvider"
                name="serviceProvider"
                [(ngModel)]="formData.serviceProvider"
                placeholder="Ex: Garage Central"
              />
            </div>
          </div>

          <!-- Cost Section -->
          <div class="cost-section">
            <div class="section-header">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="12" y1="1" x2="12" y2="23"/>
                <path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>
              </svg>
              <span>Coûts</span>
            </div>
            <div class="form-grid">
              <div class="form-group">
                <label for="laborCost">Main d'œuvre (DH)</label>
                <input
                  type="number"
                  id="laborCost"
                  name="laborCost"
                  [(ngModel)]="formData.laborCost"
                  min="0"
                  placeholder="0"
                  (input)="calculateTotal()"
                />
              </div>

              <div class="form-group">
                <label for="partsCost">Pièces (DH)</label>
                <input
                  type="number"
                  id="partsCost"
                  name="partsCost"
                  [(ngModel)]="formData.partsCost"
                  min="0"
                  placeholder="0"
                  (input)="calculateTotal()"
                />
              </div>

              <div class="form-group total-group">
                <label>Total</label>
                <div class="total-value">{{ formData.totalCost | number:'1.0-0' }} DH</div>
              </div>
            </div>
          </div>

          <!-- Notes Section -->
          <div class="form-group full-width">
            <label for="notes">Notes additionnelles</label>
            <textarea
              id="notes"
              name="notes"
              [(ngModel)]="formData.notes"
              rows="2"
              placeholder="Remarques, recommandations..."
            ></textarea>
          </div>

          <div class="popup-footer">
            <button type="button" class="btn-secondary" (click)="close()">
              Annuler
            </button>
            <button type="submit" class="btn-primary">
              {{ record?.id ? 'Mettre à jour' : 'Enregistrer' }}
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
      from { opacity: 0; }
      to { opacity: 1; }
    }

    .popup-container {
      background: white;
      border-radius: 6px;
      box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.15);
      max-width: 650px;
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
    .form-group select,
    .form-group textarea {
      padding: 8px 12px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 3px;
      color: #1e293b;
      font-family: var(--font-family);
      font-size: 12px;
      transition: all 0.15s;
    }

    .form-group textarea {
      resize: vertical;
      min-height: 60px;
    }

    .form-group input:focus,
    .form-group select:focus,
    .form-group textarea:focus {
      outline: none;
      border-color: #3b82f6;
    }

    .form-group input::placeholder,
    .form-group textarea::placeholder {
      color: #94a3b8;
    }

    .cost-section {
      margin-top: 20px;
      padding-top: 20px;
      border-top: 1px solid #e2e8f0;
    }

    .section-header {
      display: flex;
      align-items: center;
      gap: 8px;
      margin-bottom: 16px;
      color: #16a34a;
      font-weight: 600;
      font-size: 13px;
    }

    .section-header svg {
      color: #16a34a;
    }

    .total-group {
      display: flex;
      flex-direction: column;
      justify-content: flex-end;
    }

    .total-value {
      padding: 8px 12px;
      background: #f0fdf4;
      border: 1px solid #bbf7d0;
      border-radius: 3px;
      color: #16a34a;
      font-weight: 600;
      font-size: 14px;
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
    }
  `]
})
export class MaintenancePopupComponent implements OnInit, OnChanges {
  @Input() isOpen = false;
  @Input() record: MaintenanceRecord | null = null;
  @Input() vehicles: Vehicle[] = [];
  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<Partial<MaintenanceRecord>>();

  formData: any = {
    vehicleId: '',
    type: '',
    dateStr: '',
    status: 'scheduled',
    description: '',
    mileageAtService: 0,
    serviceProvider: '',
    laborCost: 0,
    partsCost: 0,
    totalCost: 0,
    notes: ''
  };

  ngOnInit() {
    this.initForm();
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['record'] || changes['isOpen']) {
      this.initForm();
    }
  }

  initForm() {
    if (this.record) {
      const date = new Date(this.record.date);
      this.formData = {
        ...this.record,
        dateStr: date.toISOString().split('T')[0]
      };
    } else {
      this.formData = {
        vehicleId: '',
        type: '',
        dateStr: new Date().toISOString().split('T')[0],
        status: 'scheduled',
        description: '',
        mileageAtService: 0,
        serviceProvider: '',
        laborCost: 0,
        partsCost: 0,
        totalCost: 0,
        notes: ''
      };
    }
  }

  calculateTotal() {
    this.formData.totalCost = (this.formData.laborCost || 0) + (this.formData.partsCost || 0);
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
    const result: Partial<MaintenanceRecord> = {
      ...this.formData,
      date: new Date(this.formData.dateStr)
    };
    delete (result as any).dateStr;
    this.saved.emit(result);
  }
}
