import { Component, Input, Output, EventEmitter, OnInit, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { VehicleCost, Vehicle } from '../../models/types';
import { MockDataService } from '../../services/mock-data.service';

@Component({
  selector: 'app-vehicle-costs-popup',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="popup-overlay" *ngIf="isOpen" (click)="onOverlayClick($event)">
      <div class="popup-container">
        <div class="popup-header">
          <div class="header-info">
            <h3>{{ vehicle?.name }}</h3>
            <span class="vehicle-plate">{{ vehicle?.plate }}</span>
          </div>
          <button class="close-btn" (click)="close()">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="18" y1="6" x2="6" y2="18"/>
              <line x1="6" y1="6" x2="18" y2="18"/>
            </svg>
          </button>
        </div>

        <!-- Tabs -->
        <div class="tabs">
          <button class="tab" [class.active]="activeTab === 'list'" (click)="activeTab = 'list'">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="8" y1="6" x2="21" y2="6"/>
              <line x1="8" y1="12" x2="21" y2="12"/>
              <line x1="8" y1="18" x2="21" y2="18"/>
              <line x1="3" y1="6" x2="3.01" y2="6"/>
              <line x1="3" y1="12" x2="3.01" y2="12"/>
              <line x1="3" y1="18" x2="3.01" y2="18"/>
            </svg>
            Historique
          </button>
          <button class="tab" [class.active]="activeTab === 'add'" (click)="activeTab = 'add'; resetForm()">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"/>
              <line x1="5" y1="12" x2="19" y2="12"/>
            </svg>
            Ajouter
          </button>
        </div>

        <div class="popup-body">
          <!-- List Tab -->
          <div class="tab-content" *ngIf="activeTab === 'list'">
            <!-- Summary -->
            <div class="summary-bar">
              <div class="summary-item">
                <span class="summary-label">Total</span>
                <span class="summary-value">{{ getTotalCost() | number:'1.0-0' }} DT</span>
              </div>
              <div class="summary-item fuel">
                <span class="summary-label">Carburant</span>
                <span class="summary-value">{{ getFuelCost() | number:'1.0-0' }} DT</span>
              </div>
              <div class="summary-item maintenance">
                <span class="summary-label">Maintenance</span>
                <span class="summary-value">{{ getMaintenanceCost() | number:'1.0-0' }} DT</span>
              </div>
              <div class="summary-item other">
                <span class="summary-label">Autres</span>
                <span class="summary-value">{{ getOtherCost() | number:'1.0-0' }} DT</span>
              </div>
            </div>

            <!-- Costs List -->
            <div class="costs-list">
              @for (cost of vehicleCosts; track cost.id) {
                <div class="cost-item">
                  <div class="cost-icon" [class]="cost.type">
                    <svg *ngIf="cost.type === 'fuel'" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M3 22V6a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v16"/>
                      <path d="M15 22V10a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2v2a2 2 0 0 1-2 2h-4"/>
                      <path d="M3 22h12"/>
                    </svg>
                    <svg *ngIf="cost.type === 'maintenance'" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>
                    </svg>
                    <svg *ngIf="cost.type !== 'fuel' && cost.type !== 'maintenance'" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <line x1="12" y1="1" x2="12" y2="23"/>
                      <path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>
                    </svg>
                  </div>
                  <div class="cost-info">
                    <span class="cost-desc">{{ cost.description }}</span>
                    <span class="cost-meta">
                      {{ getTypeLabel(cost.type) }}
                      <span *ngIf="cost.mileage"> • {{ cost.mileage | number }} km</span>
                      <span *ngIf="cost.liters"> • {{ cost.liters }}L</span>
                    </span>
                  </div>
                  <div class="cost-right">
                    <span class="cost-amount">{{ cost.amount | number:'1.0-0' }} DT</span>
                    <span class="cost-date">{{ formatDate(cost.date) }}</span>
                  </div>
                  <div class="cost-actions">
                    <button class="action-btn" title="Modifier" (click)="editCost(cost)">
                      <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                        <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                      </svg>
                    </button>
                    <button class="action-btn danger" title="Supprimer" (click)="deleteCost(cost)">
                      <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <polyline points="3 6 5 6 21 6"/>
                        <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                      </svg>
                    </button>
                  </div>
                </div>
              }

              @if (vehicleCosts.length === 0) {
                <div class="empty-state">
                  <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                    <line x1="12" y1="1" x2="12" y2="23"/>
                    <path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>
                  </svg>
                  <p>Aucune dépense enregistrée</p>
                  <button class="btn-add-empty" (click)="activeTab = 'add'">Ajouter une dépense</button>
                </div>
              }
            </div>
          </div>

          <!-- Add/Edit Tab -->
          <div class="tab-content" *ngIf="activeTab === 'add'">
            <form (ngSubmit)="saveCost()">
              <div class="form-grid">
                <div class="form-group">
                  <label for="type">Type de dépense *</label>
                  <select id="type" name="type" [(ngModel)]="costForm.type" required (change)="onTypeChange()">
                    <option value="">Sélectionner...</option>
                    <option value="fuel">Carburant</option>
                    <option value="maintenance">Maintenance</option>
                    <option value="insurance">Assurance</option>
                    <option value="tax">Vignette</option>
                    <option value="toll">Péage</option>
                    <option value="parking">Parking</option>
                    <option value="fine">Amende</option>
                    <option value="other">Autre</option>
                  </select>
                </div>

                <div class="form-group">
                  <label for="date">Date *</label>
                  <input type="date" id="date" name="date" [(ngModel)]="costForm.dateStr" required>
                </div>

                <div class="form-group">
                  <label for="amount">Montant (DT) *</label>
                  <input type="number" id="amount" name="amount" [(ngModel)]="costForm.amount" required min="0" step="0.01">
                </div>

                <div class="form-group">
                  <label for="mileage">Kilométrage</label>
                  <input type="number" id="mileage" name="mileage" [(ngModel)]="costForm.mileage" min="0">
                </div>

                <!-- Fuel specific fields -->
                <div class="form-group" *ngIf="costForm.type === 'fuel'">
                  <label for="fuelType">Type de carburant</label>
                  <select id="fuelType" name="fuelType" [(ngModel)]="costForm.fuelType">
                    <option value="">Sélectionner...</option>
                    <option value="diesel">Diesel</option>
                    <option value="gasoline">Essence</option>
                    <option value="electric">Électrique</option>
                  </select>
                </div>

                <div class="form-group" *ngIf="costForm.type === 'fuel'">
                  <label for="liters">Litres</label>
                  <input type="number" id="liters" name="liters" [(ngModel)]="costForm.liters" min="0" step="0.1">
                </div>

                <div class="form-group full-width">
                  <label for="description">Description *</label>
                  <input type="text" id="description" name="description" [(ngModel)]="costForm.description" required placeholder="Ex: Plein carburant, Péage autoroute...">
                </div>

                <div class="form-group">
                  <label for="receiptNumber">N° Reçu/Facture</label>
                  <input type="text" id="receiptNumber" name="receiptNumber" [(ngModel)]="costForm.receiptNumber" placeholder="Ex: FAC-2024-001">
                </div>
              </div>

              <div class="form-actions">
                <button type="button" class="btn-cancel" (click)="activeTab = 'list'">Annuler</button>
                <button type="submit" class="btn-save">
                  {{ editingCost ? 'Mettre à jour' : 'Enregistrer' }}
                </button>
              </div>
            </form>
          </div>
        </div>
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
      background: rgba(15, 23, 42, 0.6);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
      backdrop-filter: blur(2px);
    }

    .popup-container {
      background: white;
      border-radius: 8px;
      width: 90%;
      max-width: 700px;
      max-height: 85vh;
      display: flex;
      flex-direction: column;
      box-shadow: 0 20px 40px rgba(0, 0, 0, 0.15);
    }

    .popup-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 14px 18px;
      border-bottom: 1px solid #e2e8f0;
      background: linear-gradient(135deg, #3b82f6 0%, #1d4ed8 100%);
      border-radius: 8px 8px 0 0;
      color: white;
    }

    .header-info h3 {
      margin: 0;
      font-size: 15px;
      font-weight: 600;
    }

    .vehicle-plate {
      font-size: 11px;
      opacity: 0.8;
    }

    .close-btn {
      background: rgba(255, 255, 255, 0.2);
      border: none;
      color: white;
      cursor: pointer;
      padding: 6px;
      border-radius: 4px;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .close-btn:hover {
      background: rgba(255, 255, 255, 0.3);
    }

    .tabs {
      display: flex;
      border-bottom: 1px solid #e2e8f0;
      background: #f8fafc;
    }

    .tab {
      flex: 1;
      padding: 12px 16px;
      border: none;
      background: transparent;
      color: #64748b;
      font-size: 12px;
      font-weight: 500;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 6px;
      border-bottom: 2px solid transparent;
      transition: all 0.2s;
    }

    .tab:hover {
      color: #3b82f6;
      background: white;
    }

    .tab.active {
      color: #3b82f6;
      background: white;
      border-bottom-color: #3b82f6;
    }

    .popup-body {
      flex: 1;
      overflow-y: auto;
      padding: 16px;
    }

    .summary-bar {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 10px;
      margin-bottom: 16px;
    }

    .summary-item {
      background: #f8fafc;
      padding: 10px;
      border-radius: 6px;
      text-align: center;
    }

    .summary-item.fuel { background: #fff7ed; }
    .summary-item.maintenance { background: #eff6ff; }
    .summary-item.other { background: #f1f5f9; }

    .summary-label {
      display: block;
      font-size: 10px;
      color: #64748b;
      margin-bottom: 4px;
    }

    .summary-value {
      font-size: 14px;
      font-weight: 700;
      color: #1e293b;
    }

    .costs-list {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .cost-item {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 12px;
      background: #f8fafc;
      border-radius: 6px;
      transition: background 0.2s;
    }

    .cost-item:hover {
      background: #f1f5f9;
    }

    .cost-icon {
      width: 32px;
      height: 32px;
      border-radius: 6px;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }

    .cost-icon.fuel { background: #ffedd5; color: #ea580c; }
    .cost-icon.maintenance { background: #dbeafe; color: #2563eb; }
    .cost-icon.insurance { background: #dcfce7; color: #16a34a; }
    .cost-icon.tax { background: #fef3c7; color: #d97706; }
    .cost-icon.toll { background: #e0e7ff; color: #4f46e5; }
    .cost-icon.parking { background: #f3e8ff; color: #9333ea; }
    .cost-icon.fine { background: #fee2e2; color: #dc2626; }
    .cost-icon.other { background: #f1f5f9; color: #64748b; }

    .cost-info {
      flex: 1;
      min-width: 0;
    }

    .cost-desc {
      display: block;
      font-size: 12px;
      font-weight: 500;
      color: #1e293b;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .cost-meta {
      font-size: 10px;
      color: #94a3b8;
    }

    .cost-right {
      text-align: right;
    }

    .cost-amount {
      display: block;
      font-size: 13px;
      font-weight: 600;
      color: #1e293b;
    }

    .cost-date {
      font-size: 10px;
      color: #94a3b8;
    }

    .cost-actions {
      display: flex;
      gap: 4px;
    }

    .action-btn {
      padding: 4px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 4px;
      color: #64748b;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .action-btn:hover {
      background: #f1f5f9;
      color: #1e293b;
    }

    .action-btn.danger:hover {
      background: #fee2e2;
      color: #dc2626;
      border-color: #fecaca;
    }

    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      padding: 40px;
      color: #94a3b8;
    }

    .empty-state svg {
      margin-bottom: 12px;
      opacity: 0.5;
    }

    .empty-state p {
      font-size: 13px;
      margin-bottom: 12px;
    }

    .btn-add-empty {
      padding: 8px 16px;
      background: #3b82f6;
      color: white;
      border: none;
      border-radius: 4px;
      font-size: 12px;
      cursor: pointer;
    }

    .btn-add-empty:hover {
      background: #2563eb;
    }

    /* Form Styles */
    .form-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 14px;
    }

    .form-group {
      display: flex;
      flex-direction: column;
      gap: 4px;
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
      padding: 8px 10px;
      border: 1px solid #e2e8f0;
      border-radius: 4px;
      font-size: 13px;
      color: #1e293b;
    }

    .form-group input:focus,
    .form-group select:focus {
      outline: none;
      border-color: #3b82f6;
      box-shadow: 0 0 0 2px rgba(59, 130, 246, 0.1);
    }

    .form-actions {
      display: flex;
      justify-content: flex-end;
      gap: 10px;
      margin-top: 20px;
      padding-top: 16px;
      border-top: 1px solid #e2e8f0;
    }

    .btn-cancel {
      padding: 8px 16px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 4px;
      color: #64748b;
      font-size: 12px;
      cursor: pointer;
    }

    .btn-cancel:hover {
      background: #f8fafc;
    }

    .btn-save {
      padding: 8px 20px;
      background: #3b82f6;
      border: none;
      border-radius: 4px;
      color: white;
      font-size: 12px;
      font-weight: 500;
      cursor: pointer;
    }

    .btn-save:hover {
      background: #2563eb;
    }
  `]
})
export class VehicleCostsPopupComponent implements OnInit, OnChanges {
  @Input() isOpen = false;
  @Input() vehicle: Vehicle | null = null;
  @Output() closed = new EventEmitter<void>();

  activeTab: 'list' | 'add' = 'list';
  vehicleCosts: VehicleCost[] = [];
  editingCost: VehicleCost | null = null;

  costForm: any = {
    type: '',
    dateStr: '',
    amount: 0,
    mileage: null,
    description: '',
    receiptNumber: '',
    fuelType: '',
    liters: null
  };

  constructor(private dataService: MockDataService) {}

  ngOnInit() {
    this.loadCosts();
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['vehicle'] || changes['isOpen']) {
      this.loadCosts();
      this.activeTab = 'list';
    }
  }

  loadCosts() {
    if (this.vehicle) {
      this.vehicleCosts = this.dataService.getVehicleCostsByVehicle(this.vehicle.id)
        .sort((a, b) => new Date(b.date).getTime() - new Date(a.date).getTime());
    }
  }

  getTotalCost(): number {
    return this.vehicleCosts.reduce((sum, c) => sum + c.amount, 0);
  }

  getFuelCost(): number {
    return this.vehicleCosts.filter(c => c.type === 'fuel').reduce((sum, c) => sum + c.amount, 0);
  }

  getMaintenanceCost(): number {
    return this.vehicleCosts.filter(c => c.type === 'maintenance').reduce((sum, c) => sum + c.amount, 0);
  }

  getOtherCost(): number {
    return this.vehicleCosts.filter(c => c.type !== 'fuel' && c.type !== 'maintenance').reduce((sum, c) => sum + c.amount, 0);
  }

  getTypeLabel(type: string): string {
    const labels: any = {
      fuel: 'Carburant',
      maintenance: 'Maintenance',
      insurance: 'Assurance',
      tax: 'Vignette',
      toll: 'Péage',
      parking: 'Parking',
      fine: 'Amende',
      other: 'Autre'
    };
    return labels[type] || type;
  }

  formatDate(date: Date): string {
    return new Date(date).toLocaleDateString('fr-FR', { day: '2-digit', month: 'short' });
  }

  onOverlayClick(event: MouseEvent) {
    if ((event.target as HTMLElement).classList.contains('popup-overlay')) {
      this.close();
    }
  }

  close() {
    this.closed.emit();
  }

  resetForm() {
    this.editingCost = null;
    const today = new Date().toISOString().split('T')[0];
    this.costForm = {
      type: '',
      dateStr: today,
      amount: 0,
      mileage: this.vehicle?.mileage || null,
      description: '',
      receiptNumber: '',
      fuelType: '',
      liters: null
    };
  }

  onTypeChange() {
    if (this.costForm.type !== 'fuel') {
      this.costForm.fuelType = '';
      this.costForm.liters = null;
    }
  }

  editCost(cost: VehicleCost) {
    this.editingCost = cost;
    this.costForm = {
      type: cost.type,
      dateStr: new Date(cost.date).toISOString().split('T')[0],
      amount: cost.amount,
      mileage: cost.mileage || null,
      description: cost.description,
      receiptNumber: cost.receiptNumber || '',
      fuelType: cost.fuelType || '',
      liters: cost.liters || null
    };
    this.activeTab = 'add';
  }

  saveCost() {
    if (!this.vehicle || !this.costForm.type || !this.costForm.dateStr || !this.costForm.amount) {
      return;
    }

    const company = this.dataService.getCurrentCompany();
    
    const costData: Partial<VehicleCost> = {
      vehicleId: this.vehicle.id,
      companyId: company?.id || '',
      type: this.costForm.type,
      date: new Date(this.costForm.dateStr),
      amount: this.costForm.amount,
      description: this.costForm.description,
      mileage: this.costForm.mileage || undefined,
      receiptNumber: this.costForm.receiptNumber || undefined,
      fuelType: this.costForm.fuelType || undefined,
      liters: this.costForm.liters || undefined
    };

    if (this.editingCost) {
      this.dataService.updateVehicleCost(this.editingCost.id, costData);
    } else {
      this.dataService.addVehicleCost(costData as VehicleCost);
    }

    this.loadCosts();
    this.activeTab = 'list';
    this.resetForm();
  }

  deleteCost(cost: VehicleCost) {
    if (confirm('Êtes-vous sûr de vouloir supprimer cette dépense ?')) {
      this.dataService.deleteVehicleCost(cost.id);
      this.loadCosts();
    }
  }
}
