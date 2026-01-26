import { Component, Input, Output, EventEmitter, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { ApiService } from '../../services/api.service';

export interface VehicleDocument {
  id?: number;
  vehicleId: number;
  vehicleName: string;
  vehiclePlate: string;
  type: 'insurance' | 'tax' | 'technical_inspection' | 'registration' | 'transport_permit';
  expiryDate: Date;
  documentNumber?: string;
  documentUrl?: string;
  lastRenewalDate?: Date;
  lastRenewalCost?: number;
  reminderDays: number;
  status: 'expired' | 'expiring_soon' | 'ok';
  daysUntilExpiry: number;
}

export interface RenewalFormData {
  vehicleId: number;
  type: string;
  amount: number;
  date: string;
  newExpiryDate: string;
  documentNumber: string;
  provider: string;
  notes: string;
}

@Component({
  selector: 'app-document-renewal-popup',
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
        <div class="popup-header" [class]="document?.type">
          <div class="header-content">
            <span class="header-icon">{{ getTypeIcon(document?.type) }}</span>
            <div class="header-info">
              <h2>Renouveler {{ getTypeLabel(document?.type) }}</h2>
              <p>{{ document?.vehicleName }} - {{ document?.vehiclePlate }}</p>
            </div>
          </div>
          <button class="btn-close" (click)="close()">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="18" y1="6" x2="6" y2="18"/>
              <line x1="6" y1="6" x2="18" y2="18"/>
            </svg>
          </button>
        </div>

        <!-- Current Status -->
        <div class="status-banner" [class]="document?.status">
          <div class="status-icon">
            <svg *ngIf="document?.status === 'expired'" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="12" cy="12" r="10"/>
              <line x1="15" y1="9" x2="9" y2="15"/>
              <line x1="9" y1="9" x2="15" y2="15"/>
            </svg>
            <svg *ngIf="document?.status === 'expiring_soon'" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/>
              <line x1="12" y1="9" x2="12" y2="13"/>
              <line x1="12" y1="17" x2="12.01" y2="17"/>
            </svg>
            <svg *ngIf="document?.status === 'ok'" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
              <polyline points="22 4 12 14.01 9 11.01"/>
            </svg>
          </div>
          <div class="status-text">
            <span class="status-label">{{ getStatusText() }}</span>
            <span class="status-date">Expiration actuelle: {{ formatDate(document?.expiryDate) }}</span>
          </div>
        </div>

        <!-- Form -->
        <form class="popup-form" (ngSubmit)="onSubmit()" #renewalForm="ngForm">
          <!-- Payment Info Section -->
          <div class="form-section">
            <h3 class="section-title">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="12" y1="1" x2="12" y2="23"/>
                <path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>
              </svg>
              Informations de paiement
            </h3>

            <div class="form-row two-cols">
              <div class="form-group">
                <label for="amount">Montant payé *</label>
                <div class="input-with-suffix">
                  <input type="number" id="amount" name="amount" [(ngModel)]="formData.amount" required
                         placeholder="0.00" class="form-control" min="0" step="0.01">
                  <span class="suffix">DT</span>
                </div>
              </div>
              <div class="form-group">
                <label for="date">Date de paiement *</label>
                <input type="date" id="date" name="date" [(ngModel)]="formData.date" required class="form-control">
              </div>
            </div>

            <div class="form-row">
              <div class="form-group full">
                <label for="provider">Fournisseur / Compagnie</label>
                <input type="text" id="provider" name="provider" [(ngModel)]="formData.provider"
                       [placeholder]="getProviderPlaceholder()" class="form-control">
              </div>
            </div>
          </div>

          <!-- Document Info Section -->
          <div class="form-section">
            <h3 class="section-title">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                <polyline points="14 2 14 8 20 8"/>
              </svg>
              Informations du document
            </h3>

            <div class="form-row two-cols">
              <div class="form-group">
                <label for="documentNumber">{{ getDocumentNumberLabel() }}</label>
                <input type="text" id="documentNumber" name="documentNumber" [(ngModel)]="formData.documentNumber"
                       [placeholder]="getDocumentNumberPlaceholder()" class="form-control">
              </div>
              <div class="form-group">
                <label for="newExpiryDate">Nouvelle date d'expiration *</label>
                <input type="date" id="newExpiryDate" name="newExpiryDate" [(ngModel)]="formData.newExpiryDate" 
                       required class="form-control">
              </div>
            </div>

            <div class="form-row">
              <div class="form-group full">
                <label for="notes">Notes / Remarques</label>
                <textarea id="notes" name="notes" [(ngModel)]="formData.notes" rows="2"
                          placeholder="Informations complémentaires..." class="form-control"></textarea>
              </div>
            </div>
          </div>

          <!-- Quick Date Buttons -->
          <div class="quick-dates">
            <span class="quick-label">Expiration rapide:</span>
            <button type="button" class="quick-btn" (click)="setExpiryFromNow(12)">+1 an</button>
            <button type="button" class="quick-btn" (click)="setExpiryFromNow(6)">+6 mois</button>
            <button type="button" class="quick-btn" (click)="setExpiryFromNow(24)">+2 ans</button>
          </div>

          <!-- Summary -->
          <div class="summary-box" *ngIf="formData.amount > 0">
            <div class="summary-row">
              <span>Type de dépense</span>
              <span class="summary-value">{{ getTypeLabel(document?.type) }}</span>
            </div>
            <div class="summary-row">
              <span>Montant</span>
              <span class="summary-value amount">{{ formData.amount | number:'1.2-2' }} DT</span>
            </div>
            <div class="summary-row" *ngIf="formData.newExpiryDate">
              <span>Valide jusqu'au</span>
              <span class="summary-value">{{ formatInputDate(formData.newExpiryDate) }}</span>
            </div>
          </div>

          <!-- Actions -->
          <div class="popup-actions">
            <button type="button" class="btn-cancel" (click)="close()">Annuler</button>
            <button type="submit" class="btn-save" [disabled]="renewalForm.invalid || saving">
              <svg *ngIf="!saving" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/>
                <polyline points="17 21 17 13 7 13 7 21"/>
                <polyline points="7 3 7 8 15 8"/>
              </svg>
              <span class="spinner" *ngIf="saving"></span>
              {{ saving ? 'Enregistrement...' : 'Enregistrer le renouvellement' }}
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
      max-width: 500px;
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
      color: white;
    }

    .popup-header.insurance { background: linear-gradient(135deg, #3b82f6 0%, #1d4ed8 100%); }
    .popup-header.tax { background: linear-gradient(135deg, #f59e0b 0%, #d97706 100%); }
    .popup-header.technical_inspection { background: linear-gradient(135deg, #22c55e 0%, #16a34a 100%); }
    .popup-header.registration { background: linear-gradient(135deg, #8b5cf6 0%, #7c3aed 100%); }
    .popup-header.transport_permit { background: linear-gradient(135deg, #ec4899 0%, #be185d 100%); }

    .header-content {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .header-icon {
      font-size: 28px;
      line-height: 1;
    }

    .header-info h2 {
      margin: 0;
      font-size: 16px;
      font-weight: 600;
    }

    .header-info p {
      margin: 4px 0 0;
      font-size: 12px;
      opacity: 0.9;
    }

    .btn-close {
      background: rgba(255,255,255,0.2);
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
      background: rgba(255,255,255,0.3);
    }

    /* ===== STATUS BANNER ===== */
    .status-banner {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px 20px;
      border-bottom: 1px solid #e2e8f0;
    }

    .status-banner.expired { background: #fef2f2; }
    .status-banner.expiring_soon { background: #fffbeb; }
    .status-banner.ok { background: #f0fdf4; }

    .status-icon {
      width: 36px;
      height: 36px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .status-banner.expired .status-icon { background: #fee2e2; color: #dc2626; }
    .status-banner.expiring_soon .status-icon { background: #fef3c7; color: #d97706; }
    .status-banner.ok .status-icon { background: #dcfce7; color: #16a34a; }

    .status-text {
      display: flex;
      flex-direction: column;
    }

    .status-label {
      font-size: 13px;
      font-weight: 600;
      color: #1e293b;
    }

    .status-date {
      font-size: 11px;
      color: #64748b;
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

    .form-control::placeholder {
      color: #94a3b8;
    }

    textarea.form-control {
      resize: vertical;
      min-height: 60px;
    }

    .input-with-suffix {
      position: relative;
    }

    .input-with-suffix input {
      padding-right: 40px;
    }

    .suffix {
      position: absolute;
      right: 12px;
      top: 50%;
      transform: translateY(-50%);
      color: #64748b;
      font-size: 12px;
      font-weight: 500;
    }

    /* ===== QUICK DATES ===== */
    .quick-dates {
      display: flex;
      align-items: center;
      gap: 8px;
      margin-bottom: 20px;
    }

    .quick-label {
      font-size: 12px;
      color: #64748b;
    }

    .quick-btn {
      padding: 6px 12px;
      background: #f1f5f9;
      border: 1px solid #e2e8f0;
      border-radius: 4px;
      font-size: 11px;
      font-weight: 500;
      color: #475569;
      cursor: pointer;
      transition: all 0.2s;
    }

    .quick-btn:hover {
      background: #e2e8f0;
      border-color: #cbd5e1;
    }

    /* ===== SUMMARY ===== */
    .summary-box {
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      padding: 14px;
      margin-bottom: 20px;
    }

    .summary-row {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 6px 0;
      font-size: 12px;
      color: #64748b;
    }

    .summary-row:not(:last-child) {
      border-bottom: 1px dashed #e2e8f0;
    }

    .summary-value {
      font-weight: 600;
      color: #1e293b;
    }

    .summary-value.amount {
      font-size: 14px;
      color: #16a34a;
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
      background: linear-gradient(135deg, #22c55e 0%, #16a34a 100%);
      border: none;
      border-radius: 6px;
      font-size: 13px;
      font-weight: 500;
      color: white;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-save:hover:not(:disabled) {
      background: linear-gradient(135deg, #16a34a 0%, #15803d 100%);
    }

    .btn-save:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }

    .spinner {
      width: 14px;
      height: 14px;
      border: 2px solid rgba(255,255,255,0.3);
      border-top-color: white;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }

    /* ===== RESPONSIVE ===== */
    @media (max-width: 540px) {
      .popup-container {
        max-width: 100%;
      }

      .form-row.two-cols {
        grid-template-columns: 1fr;
      }
    }
  `]
})
export class DocumentRenewalPopupComponent implements OnChanges {
  @Input() isOpen = false;
  @Input() document: VehicleDocument | null = null;
  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<any>();

  formData: RenewalFormData = this.getEmptyForm();
  saving = false;

  constructor(private apiService: ApiService) {}

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['document'] || changes['isOpen']) {
      if (this.isOpen && this.document) {
        this.formData = this.getEmptyForm();
        this.formData.vehicleId = this.document.vehicleId;
        this.formData.type = this.getCostType(this.document.type);
        this.formData.date = new Date().toISOString().split('T')[0];
        
        // Set default expiry to 1 year from now
        this.setExpiryFromNow(12);
      }
    }
  }

  getEmptyForm(): RenewalFormData {
    return {
      vehicleId: 0,
      type: '',
      amount: 0,
      date: '',
      newExpiryDate: '',
      documentNumber: '',
      provider: '',
      notes: ''
    };
  }

  getCostType(docType: string): string {
    const mapping: { [key: string]: string } = {
      'insurance': 'insurance',
      'tax': 'tax',
      'technical_inspection': 'maintenance',
      'registration': 'other',
      'transport_permit': 'other'
    };
    return mapping[docType] || 'other';
  }

  setExpiryFromNow(months: number): void {
    const date = new Date();
    date.setMonth(date.getMonth() + months);
    this.formData.newExpiryDate = date.toISOString().split('T')[0];
  }

  getTypeLabel(type: string | undefined): string {
    if (!type) return '';
    const labels: { [key: string]: string } = {
      'insurance': 'Assurance',
      'tax': 'Vignette',
      'technical_inspection': 'Visite technique',
      'registration': 'Carte grise',
      'transport_permit': 'Autorisation transport'
    };
    return labels[type] || type;
  }

  getTypeIcon(type: string | undefined): string {
    if (!type) return '📄';
    const icons: { [key: string]: string } = {
      'insurance': '🛡️',
      'tax': '📋',
      'technical_inspection': '🔧',
      'registration': '📄',
      'transport_permit': '🚛'
    };
    return icons[type] || '📄';
  }

  getStatusText(): string {
    if (!this.document) return '';
    switch (this.document.status) {
      case 'expired': return `Expiré depuis ${Math.abs(this.document.daysUntilExpiry)} jour(s)`;
      case 'expiring_soon': return `Expire dans ${this.document.daysUntilExpiry} jour(s)`;
      case 'ok': return 'Document en règle';
      default: return '';
    }
  }

  getProviderPlaceholder(): string {
    if (!this.document) return 'Nom du fournisseur';
    switch (this.document.type) {
      case 'insurance': return 'Ex: CNIA Saada, Wafa Assurance...';
      case 'tax': return 'Ex: Recette des finances';
      case 'technical_inspection': return 'Ex: Centre de contrôle technique';
      default: return 'Nom du fournisseur';
    }
  }

  getDocumentNumberLabel(): string {
    if (!this.document) return 'N° Document';
    switch (this.document.type) {
      case 'insurance': return 'N° Police';
      case 'tax': return 'N° Vignette';
      case 'technical_inspection': return 'N° PV';
      case 'registration': return 'N° Immatriculation';
      default: return 'N° Document';
    }
  }

  getDocumentNumberPlaceholder(): string {
    if (!this.document) return '';
    switch (this.document.type) {
      case 'insurance': return 'Ex: POL-2026-12345';
      case 'tax': return 'Ex: VIG-2026-001';
      case 'technical_inspection': return 'Ex: CT-2026-001';
      default: return '';
    }
  }

  formatDate(date: Date | undefined): string {
    if (!date) return '-';
    return new Date(date).toLocaleDateString('fr-FR', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric'
    });
  }

  formatInputDate(dateStr: string): string {
    if (!dateStr) return '-';
    return new Date(dateStr).toLocaleDateString('fr-FR', {
      day: '2-digit',
      month: 'long',
      year: 'numeric'
    });
  }

  close(): void {
    this.closed.emit();
  }

  onSubmit(): void {
    if (!this.document || !this.formData.amount || !this.formData.newExpiryDate) return;

    this.saving = true;

    // Create cost entry
    const costData = {
      vehicleId: this.document.vehicleId,
      type: this.getCostType(this.document.type),
      description: `Renouvellement ${this.getTypeLabel(this.document.type)}${this.formData.provider ? ' - ' + this.formData.provider : ''}`,
      amount: this.formData.amount,
      date: new Date(this.formData.date),
      receiptNumber: this.formData.documentNumber,
      notes: this.formData.notes
    };

    this.apiService.createCost(costData).subscribe({
      next: (result) => {
        // TODO: Update vehicle expiry date via API
        // For now, emit the renewal data
        this.saved.emit({
          cost: result,
          newExpiryDate: this.formData.newExpiryDate,
          documentNumber: this.formData.documentNumber,
          documentType: this.document?.type
        });
        this.saving = false;
      },
      error: (err) => {
        console.error('Error creating cost:', err);
        this.saving = false;
        // Still emit for demo purposes
        this.saved.emit({
          newExpiryDate: this.formData.newExpiryDate,
          documentNumber: this.formData.documentNumber,
          documentType: this.document?.type
        });
      }
    });
  }
}
