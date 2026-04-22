import { Component, EventEmitter, Input, Output, OnInit, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MaintenanceRecord, Vehicle } from '../../models/types';
import { MaintenanceTemplateDto, SupplierDto, MarkMaintenanceDoneRequest } from '../../services/api.service';

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
              <label for="templateId">Type d'entretien *</label>
              <select id="templateId" name="templateId" [(ngModel)]="formData.templateId" required>
                <option value="">Sélectionner un modèle</option>
                @for (tpl of templates; track tpl.id) {
                  <option [value]="tpl.id">{{ tpl.name }}@if (tpl.category) { - {{ tpl.category }} }</option>
                }
              </select>
              @if (templates.length === 0) {
                <small class="hint warning">Aucun modèle d'entretien n'est défini. Créez-en un dans la page Modèles d'entretien.</small>
              }
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
              <label for="mileage">Kilométrage (km) *</label>
              <input
                type="number"
                id="mileage"
                name="mileage"
                [(ngModel)]="formData.mileage"
                min="0"
                required
                placeholder="Ex: 50000"
              />
            </div>

            <div class="form-group">
              <label for="supplierId">Prestataire / Garage</label>
              <select id="supplierId" name="supplierId" [(ngModel)]="formData.supplierId">
                <option [ngValue]="null">Aucun</option>
                @for (sup of suppliers; track sup.id) {
                  <option [ngValue]="sup.id">{{ sup.name }}</option>
                }
              </select>
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
                <label for="laborCost">Main d'œuvre (DT)</label>
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
                <label for="partsCost">Pièces (DT)</label>
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
                <div class="total-value">{{ formData.totalCost | number:'1.0-0' }} DT</div>
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
            <button type="submit" class="btn-primary" [disabled]="!canSubmit()">
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
      top: 42px; /* Below navbar */
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(0, 0, 0, 0.5);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1050;
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

    .form-group .hint {
      font-size: 11px;
      color: #94a3b8;
    }

    .form-group .hint.warning {
      color: #b45309;
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

    .btn-primary:hover:not(:disabled) {
      background: #2563eb;
    }

    .btn-primary:disabled {
      opacity: 0.5;
      cursor: not-allowed;
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
  @Input() templates: MaintenanceTemplateDto[] = [];
  @Input() suppliers: SupplierDto[] = [];
  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<MarkMaintenanceDoneRequest>();

  formData: any = {
    vehicleId: '',
    templateId: '',
    dateStr: '',
    mileage: 0,
    supplierId: null,
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
    // NOTE: edition d'un log existant n'est pas supportée par /mark-done.
    // Le popup fonctionne en mode "nouvelle saisie" uniquement; si `record`
    // est fourni, on préremplit ce qui est compatible.
    if (this.record) {
      const date = this.record.date ? new Date(this.record.date) : new Date();
      this.formData = {
        vehicleId: this.record.vehicleId ?? '',
        templateId: '',
        dateStr: date.toISOString().split('T')[0],
        mileage: this.record.mileageAtService ?? 0,
        supplierId: null,
        laborCost: this.record.laborCost ?? 0,
        partsCost: this.record.partsCost ?? 0,
        totalCost: this.record.totalCost ?? 0,
        notes: this.record.notes ?? ''
      };
    } else {
      this.formData = {
        vehicleId: '',
        templateId: '',
        dateStr: new Date().toISOString().split('T')[0],
        mileage: 0,
        supplierId: null,
        laborCost: 0,
        partsCost: 0,
        totalCost: 0,
        notes: ''
      };
    }
  }

  calculateTotal() {
    const labor = parseFloat(this.formData.laborCost) || 0;
    const parts = parseFloat(this.formData.partsCost) || 0;
    this.formData.totalCost = labor + parts;
  }

  canSubmit(): boolean {
    const vId = parseInt(this.formData.vehicleId, 10);
    const tId = parseInt(this.formData.templateId, 10);
    return !!vId && !!tId && !!this.formData.dateStr;
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
    if (!this.canSubmit()) {
      return;
    }
    const labor = parseFloat(this.formData.laborCost) || 0;
    const parts = parseFloat(this.formData.partsCost) || 0;
    const total = labor + parts;
    const supplierId = this.formData.supplierId != null
      ? parseInt(this.formData.supplierId, 10) || undefined
      : undefined;

    const payload: MarkMaintenanceDoneRequest = {
      vehicleId: parseInt(this.formData.vehicleId, 10),
      templateId: parseInt(this.formData.templateId, 10),
      date: new Date(this.formData.dateStr).toISOString(),
      mileage: parseInt(this.formData.mileage, 10) || 0,
      cost: total,
      laborCost: labor,
      partsCost: parts,
      supplierId,
      notes: (this.formData.notes ?? '').trim() || undefined,
      applyFreeBenefit: true
    };
    this.saved.emit(payload);
  }
}
