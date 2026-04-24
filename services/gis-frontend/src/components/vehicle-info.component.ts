import { Component, Input, Output, EventEmitter, OnChanges, SimpleChanges, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../services/api.service';

@Component({
  selector: 'app-vehicle-info',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="panel-overlay" *ngIf="isOpen" (mousedown)="onOverlayClick($event)">
      <div class="slide-panel" (mousedown)="$event.stopPropagation()">
        <!-- Panel Header -->
        <div class="panel-header">
          <div class="panel-header-content">
            <div class="panel-icon">📋</div>
            <div class="panel-header-info">
              <h2>Fiche véhicule</h2>
              <p>{{ vehicleForm.marque && vehicleForm.modele ? vehicleForm.marque + ' ' + vehicleForm.modele : vehicleName || 'Informations complémentaires' }}</p>
            </div>
          </div>
          <button class="btn-close-panel" (click)="close()">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="18" y1="6" x2="6" y2="18"/>
              <line x1="6" y1="6" x2="18" y2="18"/>
            </svg>
          </button>
        </div>

        <!-- Panel Body -->
        <div class="panel-body">
          <!-- Loading -->
          <div class="loading-state" *ngIf="loading">
            <div class="spinner"></div>
          </div>

          <form *ngIf="!loading" (ngSubmit)="save()">
            <!-- Section: Identification -->
            <div class="form-section">
              <h3 class="section-title">🚗 Identification</h3>
              <div class="form-row">
                <div class="form-group">
                  <label>Marque</label>
                  <select [(ngModel)]="vehicleForm.marque" name="marque" (ngModelChange)="onBrandChange($event)">
                    <option value="">Sélectionner une marque</option>
                    <option *ngFor="let brand of brands" [value]="brand.name">{{ brand.name }}</option>
                  </select>
                </div>
                <div class="form-group">
                  <label>Modèle</label>
                  <select [(ngModel)]="vehicleForm.modele" name="modele" [disabled]="!vehicleForm.marque || loadingModels">
                    <option value="">{{ loadingModels ? 'Chargement...' : 'Sélectionner un modèle' }}</option>
                    <option *ngFor="let model of models" [value]="model.name">{{ model.name }}</option>
                  </select>
                </div>
              </div>
              <div class="form-row">
                <div class="form-group">
                  <label>Date mise en circulation</label>
                  <input type="date" [(ngModel)]="vehicleForm.dateMiseEnCirculation" name="dateMiseEnCirculation">
                </div>
                <div class="form-group">
                  <label>Carburant</label>
                  <select [(ngModel)]="vehicleForm.carburant" name="carburant">
                    <option value="">Sélectionner</option>
                    <option value="diesel">Diesel</option>
                    <option value="essence">Essence</option>
                    <option value="hybride">Hybride</option>
                    <option value="electrique">Électrique</option>
                  </select>
                </div>
              </div>
            </div>

            <!-- Section: Acquisition -->
            <div class="form-section">
              <h3 class="section-title">💰 Acquisition</h3>
              <div class="form-row">
                <div class="form-group">
                  <label>Type d'acquisition</label>
                  <select [(ngModel)]="vehicleForm.typeAcquisition" name="typeAcquisition">
                    <option value="achat">Achat</option>
                    <option value="leasing">Auto-financement</option>
                  </select>
                </div>
                <div class="form-group">
                  <label>Date d'achat</label>
                  <input type="date" [(ngModel)]="vehicleForm.dateAchat" name="dateAchat">
                </div>
              </div>
              <div class="form-row">
                <div class="form-group">
                  <label>{{ vehicleForm.typeAcquisition === 'leasing' ? 'Montant Auto-financement' : "Prix d'achat" }}</label>
                  <div class="input-with-suffix">
                    <input type="number" [(ngModel)]="vehicleForm.prixAchat" name="prixAchat" placeholder="0.00">
                    <span class="input-suffix">TND</span>
                  </div>
                </div>
              </div>
              <div class="form-row" *ngIf="vehicleForm.typeAcquisition === 'leasing'">
                <div class="form-group">
                  <label>Traite mensuelle</label>
                  <div class="input-with-suffix">
                    <input type="number" [(ngModel)]="vehicleForm.traiteMensuelle" name="traiteMensuelle" placeholder="0.00">
                    <span class="input-suffix">TND/mois</span>
                  </div>
                </div>
                <div class="form-group">
                  <label>Durée du leasing</label>
                  <div class="input-with-suffix">
                    <input type="number" [(ngModel)]="vehicleForm.leasingDurationMonths" name="leasingDurationMonths" placeholder="36" min="1" max="120">
                    <span class="input-suffix">mois</span>
                  </div>
                </div>
              </div>
              <div class="form-row" *ngIf="vehicleForm.typeAcquisition === 'leasing'">
                <div class="form-group">
                  <label>Date début leasing</label>
                  <input type="date" [(ngModel)]="vehicleForm.leasingStartDate" name="leasingStartDate">
                </div>
                <div class="form-group">
                  <label>Jour de paiement</label>
                  <select [(ngModel)]="vehicleForm.leasingPaymentDay" name="leasingPaymentDay">
                    <option [ngValue]="null">— Choisir —</option>
                    <option *ngFor="let d of paymentDays" [ngValue]="d">{{d}}</option>
                  </select>
                </div>
              </div>
              <!-- Échéancier leasing -->
              <div class="leasing-schedule" *ngIf="vehicleForm.typeAcquisition === 'leasing' && vehicleForm.leasingStartDate && vehicleForm.leasingDurationMonths && vehicleForm.traiteMensuelle">
                <label class="schedule-title">Échéancier des paiements</label>
                <div class="schedule-summary">
                  <span>{{vehicleForm.leasingDurationMonths}} mois × {{vehicleForm.traiteMensuelle | number:'1.2-2'}} TND = <strong>{{vehicleForm.leasingDurationMonths * vehicleForm.traiteMensuelle | number:'1.2-2'}} TND</strong></span>
                </div>
                <div class="schedule-table-wrapper">
                  <table class="schedule-table">
                    <thead>
                      <tr><th>N°</th><th>Date</th><th>Montant</th><th>Statut</th></tr>
                    </thead>
                    <tbody>
                      <tr *ngFor="let payment of getPaymentSchedule()" [class.paid]="payment.isPast" [class.current]="payment.isCurrent">
                        <td>{{payment.index}}</td>
                        <td>{{payment.date}}</td>
                        <td>{{vehicleForm.traiteMensuelle | number:'1.2-2'}} TND</td>
                        <td>
                          <span class="payment-badge paid" *ngIf="payment.isPast">Payé</span>
                          <span class="payment-badge current" *ngIf="payment.isCurrent">En cours</span>
                          <span class="payment-badge upcoming" *ngIf="!payment.isPast && !payment.isCurrent">À venir</span>
                        </td>
                      </tr>
                    </tbody>
                  </table>
                </div>
              </div>
            </div>
          </form>
        </div>

        <!-- Panel Footer -->
        <div class="panel-footer">
          <button type="button" class="btn-secondary" (click)="close()">Annuler</button>
          <button type="button" class="btn-secondary" *ngIf="vehicleForm.typeAcquisition === 'leasing'" (click)="openCredit()">
            Visualiser crédit
          </button>
          <button type="button" class="btn-primary" (click)="save()" [disabled]="saving">
            {{ saving ? 'Enregistrement...' : 'Enregistrer' }}
          </button>
        </div>
      </div>
    </div>

    <!-- Success Toast -->
    <div class="toast-success" *ngIf="showSuccess">
      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
        <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
        <polyline points="22 4 12 14.01 9 11.01"/>
      </svg>
      Modifications enregistrées
    </div>
  `,
  styles: [`
    .panel-overlay {
      position: fixed;
      top: 42px;
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(0, 0, 0, 0.5);
      z-index: 1050;
      display: flex;
      justify-content: flex-end;
      animation: fadeIn 0.2s ease-out;
    }

    @keyframes fadeIn { from { opacity: 0; } to { opacity: 1; } }

    .slide-panel {
      width: 520px;
      max-width: 100%;
      height: 100%;
      background: #fff;
      display: flex;
      flex-direction: column;
      box-shadow: -4px 0 20px rgba(0, 0, 0, 0.15);
      animation: slideIn 0.3s ease-out;
    }

    @keyframes slideIn { from { transform: translateX(100%); } to { transform: translateX(0); } }

    .panel-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 20px 24px;
      background: linear-gradient(135deg, #6366f1 0%, #4f46e5 100%);
      color: #fff;
    }

    .panel-header-content {
      display: flex;
      align-items: center;
      gap: 16px;
    }

    .panel-icon {
      font-size: 32px;
    }

    .panel-header-info h2 {
      margin: 0;
      font-size: 18px;
      font-weight: 600;
    }

    .panel-header-info p {
      margin: 4px 0 0;
      font-size: 13px;
      opacity: 0.9;
    }

    .btn-close-panel {
      width: 36px;
      height: 36px;
      border: none;
      background: rgba(255,255,255,0.2);
      border-radius: 8px;
      color: #fff;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }

    .btn-close-panel:hover {
      background: rgba(255,255,255,0.3);
    }

    .panel-body {
      flex: 1;
      overflow-y: auto;
      padding: 24px;
    }

    /* ── Loading ── */
    .loading-state {
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 60px 0;
    }
    .spinner {
      width: 24px;
      height: 24px;
      border: 3px solid #e2e8f0;
      border-top-color: #6366f1;
      border-radius: 50%;
      animation: spin 0.65s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }

    /* ── Form sections (same as vehicle-popup) ── */
    .form-section {
      margin-bottom: 24px;
      padding-bottom: 24px;
      border-bottom: 1px solid #e2e8f0;
    }

    .form-section:last-child {
      border-bottom: none;
      margin-bottom: 0;
    }

    .section-title {
      margin: 0 0 16px 0;
      font-size: 14px;
      font-weight: 600;
      color: #4f46e5;
    }

    .form-row {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 16px;
      margin-bottom: 16px;
    }

    .form-row:last-child {
      margin-bottom: 0;
    }

    .form-group {
      display: flex;
      flex-direction: column;
      gap: 6px;
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

    /* ── Input with suffix ── */
    .input-with-suffix {
      position: relative;
    }

    .input-with-suffix input {
      width: 100%;
      padding: 8px 12px;
      padding-right: 60px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 3px;
      color: #1e293b;
      font-family: var(--font-family);
      font-size: 12px;
      transition: all 0.15s;
    }

    .input-with-suffix input:focus {
      outline: none;
      border-color: #6366f1;
      box-shadow: 0 0 0 2px rgba(99, 102, 241, 0.08);
    }

    .input-with-suffix input::placeholder {
      color: #94a3b8;
    }

    .input-suffix {
      position: absolute;
      right: 10px;
      top: 50%;
      transform: translateY(-50%);
      font-size: 11px;
      font-weight: 500;
      color: #94a3b8;
      pointer-events: none;
    }

    /* ── Footer (same as vehicle-popup) ── */
    .panel-footer {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 16px 24px;
      border-top: 1px solid #e2e8f0;
      background: #f8fafc;
    }

    .btn-primary {
      padding: 10px 20px;
      background: linear-gradient(135deg, #6366f1 0%, #4f46e5 100%);
      color: white;
      border: none;
      border-radius: 10px;
      font-weight: 600;
      font-size: 14px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-primary:hover:not(:disabled) {
      box-shadow: 0 4px 16px rgba(99, 102, 241, 0.3);
    }

    .btn-primary:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }

    .btn-secondary {
      padding: 10px 20px;
      background: #f1f5f9;
      color: #1f2937;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      font-weight: 500;
      font-size: 14px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-secondary:hover {
      background: #e2e8f0;
    }

    /* ── Toast ── */
    .toast-success {
      position: fixed;
      bottom: 20px;
      right: 20px;
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 16px;
      background: #10b981;
      color: white;
      border-radius: 6px;
      font-size: 12px;
      font-weight: 500;
      box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);
      animation: toastIn 0.3s ease-out;
      z-index: 1100;
    }
    @keyframes toastIn {
      from { opacity: 0; transform: translateY(12px); }
      to { opacity: 1; transform: translateY(0); }
    }

    /* ── Leasing schedule ── */
    .leasing-schedule {
      margin-top: 8px;
      padding: 12px;
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
    }
    .schedule-title {
      font-weight: 600;
      font-size: 13px;
      color: #1f2937;
      margin-bottom: 8px;
      display: block;
    }
    .schedule-summary {
      font-size: 12px;
      color: #64748b;
      margin-bottom: 10px;
      padding: 6px 10px;
      background: #fff;
      border-radius: 6px;
      border: 1px solid #e2e8f0;
    }
    .schedule-table-wrapper {
      max-height: 200px;
      overflow-y: auto;
      border-radius: 6px;
      border: 1px solid #e2e8f0;
    }
    .schedule-table {
      width: 100%;
      border-collapse: collapse;
      font-size: 12px;
    }
    .schedule-table th {
      background: #f1f5f9;
      padding: 6px 10px;
      text-align: left;
      font-weight: 600;
      color: #475569;
      position: sticky;
      top: 0;
    }
    .schedule-table td {
      padding: 5px 10px;
      border-top: 1px solid #f1f5f9;
      color: #334155;
    }
    .schedule-table tr.paid { opacity: 0.6; }
    .schedule-table tr.current { background: #eff6ff; }
    .payment-badge {
      display: inline-block;
      padding: 2px 8px;
      border-radius: 10px;
      font-size: 11px;
      font-weight: 500;
    }
    .payment-badge.paid { background: #dcfce7; color: #166534; }
    .payment-badge.current { background: #dbeafe; color: #1e40af; }
    .payment-badge.upcoming { background: #f1f5f9; color: #64748b; }

    /* ── Responsive ── */
    @media (max-width: 640px) {
      .form-row { grid-template-columns: 1fr; }
      .slide-panel { width: 100%; }
      .panel-body { padding: 16px; }
    }
  `]
})
export class VehicleInfoComponent implements OnChanges {
  @Input() isOpen = false;
  @Input() vehicleId: number | null = null;
  @Input() vehicleName: string = '';
  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<void>();

  loading = false;
  saving = false;
  showSuccess = false;
  brands: any[] = [];
  models: any[] = [];
  loadingModels = false;
  paymentDays = Array.from({ length: 28 }, (_, i) => i + 1);
  vehicleForm: any = {
    marque: '',
    modele: '',
    dateMiseEnCirculation: '',
    carburant: '',
    typeAcquisition: 'achat',
    dateAchat: '',
    prixAchat: null,
    traiteMensuelle: null,
    leasingDurationMonths: null,
    leasingStartDate: '',
    leasingPaymentDay: null
  };

  constructor(
    private api: ApiService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['isOpen'] && this.isOpen && this.vehicleId) {
      this.loadBrands();
      this.loadVehicle();
    }
  }

  loadBrands(): void {
    this.api.getBrands().subscribe({
      next: (brands) => {
        this.brands = brands;
        this.cdr.detectChanges();
      },
      error: () => {}
    });
  }

  onBrandChange(brandName: string): void {
    this.vehicleForm.modele = '';
    this.models = [];
    if (!brandName) return;
    const brand = this.brands.find(b => b.name === brandName);
    if (!brand) return;
    this.loadingModels = true;
    this.cdr.detectChanges();
    this.api.getBrandModels(brand.id).subscribe({
      next: (models) => {
        this.models = models;
        this.loadingModels = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.loadingModels = false;
        this.cdr.detectChanges();
      }
    });
  }

  loadVehicle(): void {
    if (!this.vehicleId) return;
    this.loading = true;
    this.api.getVehicle(this.vehicleId).subscribe({
      next: (vehicle: any) => {
        if (vehicle) {
          this.vehicleForm.marque = vehicle.marque || vehicle.brand || '';
          this.vehicleForm.modele = vehicle.modele || vehicle.model || '';
          // Calypso 6 (P5): dates arrive from backend as ISO strings
          // (e.g. "2024-03-15T00:00:00Z"). HTML <input type="date"> needs
          // pure YYYY-MM-DD — slice the first 10 chars to hydrate correctly.
          const rawMec = vehicle.dateMiseEnCirculation || vehicle.registrationDate || '';
          this.vehicleForm.dateMiseEnCirculation = rawMec ? String(rawMec).substring(0, 10) : '';
          this.vehicleForm.carburant = vehicle.carburant || vehicle.fuelType || '';
          // Backend returns acquisitionType as "purchase" | "leasing"; the
          // dropdown expects "achat" | "leasing" — map explicitly so the
          // <select> has a matching <option> and does not reset to empty.
          const backendAcq = vehicle.typeAcquisition || vehicle.acquisitionType || '';
          this.vehicleForm.typeAcquisition = backendAcq === 'leasing' ? 'leasing' : 'achat';
          const rawPurchase = vehicle.dateAchat || vehicle.purchaseDate || rawMec || '';
          this.vehicleForm.dateAchat = rawPurchase ? String(rawPurchase).substring(0, 10) : '';
          this.vehicleForm.prixAchat = vehicle.prixAchat || vehicle.purchasePrice || null;
          this.vehicleForm.traiteMensuelle = vehicle.traiteMensuelle || vehicle.leasingMonthlyPayment || null;
          this.vehicleForm.leasingDurationMonths = vehicle.leasingDurationMonths || null;
          this.vehicleForm.leasingStartDate = vehicle.leasingStartDate ? String(vehicle.leasingStartDate).substring(0, 10) : '';
          this.vehicleForm.leasingPaymentDay = vehicle.leasingPaymentDay || null;
          // Load models for the existing brand
          if (this.vehicleForm.marque) {
            const brand = this.brands.find(b => b.name === this.vehicleForm.marque);
            if (brand) {
              this.api.getBrandModels(brand.id).subscribe({
                next: (models) => { this.models = models; this.cdr.detectChanges(); },
                error: () => {}
              });
            }
          }
        }
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  save(): void {
    if (!this.vehicleId) return;
    this.saving = true;

    // Calypso 6 (P5): the backend UpdateVehicleCommand uses camelCase English
    // property names (brand, model, purchaseDate, purchasePrice, ...). The
    // form uses French names (marque, modele, dateAchat, prixAchat, ...).
    // We must load the current vehicle first, merge the form deltas into the
    // English payload and then PUT — otherwise required scalar fields
    // (Name, Type, Status, Mileage) would be wiped to null/0 on save.
    this.api.getVehicle(this.vehicleId).subscribe({
      next: (existing: any) => {
        const typeAcq = this.vehicleForm.typeAcquisition === 'leasing' ? 'leasing' : 'purchase';
        const payload: any = {
          id: this.vehicleId,
          name: existing?.name ?? '',
          type: existing?.type ?? 'camion',
          brand: this.vehicleForm.marque || existing?.brand || null,
          model: this.vehicleForm.modele || existing?.model || null,
          plate: existing?.plate ?? null,
          year: existing?.year ?? null,
          color: existing?.color ?? null,
          status: existing?.status ?? 'available',
          mileage: existing?.mileage ?? 0,
          fuelType: this.vehicleForm.carburant || existing?.fuelType || null,
          fuelTankCapacity: existing?.fuelTankCapacity ?? null,
          assignedDriverId: existing?.assignedDriverId ?? null,
          assignedSupervisorId: existing?.assignedSupervisorId ?? null,
          // Acquisition
          acquisitionType: typeAcq,
          purchasePrice: this.vehicleForm.prixAchat ?? null,
          purchaseDate: this.vehicleForm.dateAchat || null,
          leasingMonthlyPayment: this.vehicleForm.traiteMensuelle ?? null,
          leasingDurationMonths: this.vehicleForm.leasingDurationMonths ?? null,
          leasingStartDate: this.vehicleForm.leasingStartDate || null,
          leasingPaymentDay: this.vehicleForm.leasingPaymentDay ?? null,
          registrationDate: this.vehicleForm.dateMiseEnCirculation || existing?.registrationDate || null,
          // Document dates (preserve existing, the info panel doesn't edit them)
          insuranceStartDate: existing?.insuranceStartDate ?? null,
          insuranceExpiry: existing?.insuranceExpiry ?? null,
          insuranceReminderDays: existing?.insuranceReminderDays ?? null,
          taxStartDate: existing?.taxStartDate ?? null,
          taxExpiry: existing?.taxExpiry ?? null,
          taxReminderDays: existing?.taxReminderDays ?? null,
          technicalInspectionStartDate: existing?.technicalInspectionStartDate ?? null,
          technicalInspectionExpiry: existing?.technicalInspectionExpiry ?? null,
          technicalInspectionReminderDays: existing?.technicalInspectionReminderDays ?? null
        };

        this.api.updateVehicle(this.vehicleId!, payload).subscribe({
          next: () => {
            this.saving = false;
            this.showSuccess = true;
            this.saved.emit();
            setTimeout(() => { this.showSuccess = false; this.cdr.detectChanges(); }, 3000);
            this.cdr.detectChanges();
          },
          error: () => {
            this.saving = false;
            this.cdr.detectChanges();
          }
        });
      },
      error: () => {
        this.saving = false;
        this.cdr.detectChanges();
      }
    });
  }

  getPaymentSchedule(): { index: number; date: string; isPast: boolean; isCurrent: boolean }[] {
    const duration = this.vehicleForm.leasingDurationMonths;
    const startStr = this.vehicleForm.leasingStartDate;
    const payDay = this.vehicleForm.leasingPaymentDay || 1;
    if (!duration || !startStr) return [];

    const start = new Date(startStr + 'T00:00:00');
    const now = new Date();
    const payments: { index: number; date: string; isPast: boolean; isCurrent: boolean }[] = [];

    for (let i = 0; i < duration; i++) {
      const d = new Date(start.getFullYear(), start.getMonth() + i, Math.min(payDay, 28));
      const nextD = new Date(start.getFullYear(), start.getMonth() + i + 1, Math.min(payDay, 28));
      const isPast = d < now && nextD <= now;
      const isCurrent = d <= now && nextD > now;
      payments.push({
        index: i + 1,
        date: d.toLocaleDateString('fr-FR', { year: 'numeric', month: 'short', day: 'numeric' }),
        isPast,
        isCurrent
      });
    }
    return payments;
  }

  close(): void {
    this.closed.emit();
  }

  onOverlayClick(event: MouseEvent): void {
    if (event.target === event.currentTarget) {
      this.close();
    }
  }

  openCredit(): void {
    // Scroll smoothly to the payment schedule section if it's visible
    const el = document.querySelector('.leasing-schedule');
    if (el) {
      el.scrollIntoView({ behavior: 'smooth', block: 'start' });
      return;
    }
    // If schedule isn't showing yet (missing data) warn the user
    if (!this.vehicleForm.traiteMensuelle || !this.vehicleForm.leasingDurationMonths || !this.vehicleForm.leasingStartDate) {
      alert('Complétez la traite mensuelle, la durée et la date de début pour visualiser le crédit.');
    }
  }
}
