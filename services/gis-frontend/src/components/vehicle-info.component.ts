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
                  <input type="text" [(ngModel)]="vehicleForm.marque" name="marque" placeholder="Ex: Renault">
                </div>
                <div class="form-group">
                  <label>Modèle</label>
                  <input type="text" [(ngModel)]="vehicleForm.modele" name="modele" placeholder="Ex: Kangoo">
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
                    <option value="leasing">Leasing</option>
                  </select>
                </div>
                <div class="form-group">
                  <label>Prix d'achat</label>
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
                <div class="form-group"></div>
              </div>
            </div>
          </form>
        </div>

        <!-- Panel Footer -->
        <div class="panel-footer">
          <button type="button" class="btn-secondary" (click)="close()">Annuler</button>
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
  vehicleForm: any = {
    marque: '',
    modele: '',
    dateMiseEnCirculation: '',
    carburant: '',
    typeAcquisition: 'achat',
    prixAchat: null,
    traiteMensuelle: null
  };

  constructor(
    private api: ApiService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['isOpen'] && this.isOpen && this.vehicleId) {
      this.loadVehicle();
    }
  }

  loadVehicle(): void {
    if (!this.vehicleId) return;
    this.loading = true;
    this.api.getVehicle(this.vehicleId).subscribe({
      next: (vehicle: any) => {
        if (vehicle) {
          this.vehicleForm.marque = vehicle.marque || vehicle.brand || '';
          this.vehicleForm.modele = vehicle.modele || vehicle.model || '';
          this.vehicleForm.dateMiseEnCirculation = vehicle.dateMiseEnCirculation || vehicle.registrationDate || '';
          this.vehicleForm.carburant = vehicle.carburant || vehicle.fuelType || '';
          this.vehicleForm.typeAcquisition = vehicle.typeAcquisition || vehicle.acquisitionType || 'achat';
          this.vehicleForm.prixAchat = vehicle.prixAchat || vehicle.purchasePrice || null;
          this.vehicleForm.traiteMensuelle = vehicle.traiteMensuelle || vehicle.leasingMonthlyPayment || null;
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
    this.api.updateVehicle(this.vehicleId, this.vehicleForm).subscribe({
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
  }

  close(): void {
    this.closed.emit();
  }

  onOverlayClick(event: MouseEvent): void {
    if (event.target === event.currentTarget) {
      this.close();
    }
  }
}
