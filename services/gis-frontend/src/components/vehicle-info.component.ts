import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ApiService } from '../services/api.service';
import { AppLayoutComponent } from './shared/app-layout.component';

@Component({
  selector: 'app-vehicle-info',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent],
  template: `
    <app-layout>
      <div class="vehicle-info-page">
        <!-- Top bar -->
        <div class="top-bar">
          <button class="btn-back" (click)="goBack()">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M19 12H5"/><polyline points="12 19 5 12 12 5"/>
            </svg>
            Retour
          </button>
          <h1 class="page-title">Fiche Véhicule</h1>
        </div>

        <!-- Loading state -->
        <div class="loading-state" *ngIf="loading">
          <div class="spinner"></div>
          <span>Chargement...</span>
        </div>

        <div class="content" *ngIf="!loading">
          <!-- Section 1: General Info -->
          <div class="info-card">
            <div class="card-header">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="1" y="3" width="15" height="13" rx="2"/>
                <path d="M16 8h2a2 2 0 0 1 2 2v1l2 1v4h-2"/>
                <circle cx="5.5" cy="18.5" r="2.5"/>
                <circle cx="18.5" cy="18.5" r="2.5"/>
              </svg>
              <h2>Informations Générales</h2>
            </div>
            <div class="card-body">
              <div class="form-row">
                <div class="form-group">
                  <label>Marque</label>
                  <input type="text" [(ngModel)]="vehicleForm.marque" placeholder="Ex: Renault">
                </div>
                <div class="form-group">
                  <label>Modèle</label>
                  <input type="text" [(ngModel)]="vehicleForm.modele" placeholder="Ex: Kangoo">
                </div>
              </div>
              <div class="form-row">
                <div class="form-group">
                  <label>Date mise en circulation</label>
                  <input type="date" [(ngModel)]="vehicleForm.dateMiseEnCirculation">
                </div>
                <div class="form-group">
                  <label>Carburant</label>
                  <select [(ngModel)]="vehicleForm.carburant">
                    <option value="">-- Sélectionner --</option>
                    <option value="diesel">Diesel</option>
                    <option value="essence">Essence</option>
                    <option value="hybride">Hybride</option>
                    <option value="electrique">Électrique</option>
                  </select>
                </div>
              </div>
              <div class="form-group">
                <label>Type d'acquisition</label>
                <div class="radio-group">
                  <label class="radio-label">
                    <input type="radio" name="acquisitionType" value="achat" [(ngModel)]="vehicleForm.typeAcquisition">
                    <span class="radio-mark"></span>
                    Achat
                  </label>
                  <label class="radio-label">
                    <input type="radio" name="acquisitionType" value="leasing" [(ngModel)]="vehicleForm.typeAcquisition">
                    <span class="radio-mark"></span>
                    Leasing
                  </label>
                </div>
              </div>
              <div class="form-row">
                <div class="form-group">
                  <label>Prix d'achat (MAD)</label>
                  <input type="number" [(ngModel)]="vehicleForm.prixAchat" placeholder="0.00">
                </div>
                <div class="form-group" *ngIf="vehicleForm.typeAcquisition === 'leasing'">
                  <label>Traite mensuelle (MAD)</label>
                  <input type="number" [(ngModel)]="vehicleForm.traiteMensuelle" placeholder="0.00">
                </div>
              </div>
            </div>
          </div>

          <!-- Note: Documents (assurance, taxe, visite technique) are managed in Échéances (/documents) -->

          <!-- Save Button -->
          <div class="actions-bar">
            <button class="btn-save" (click)="save()" [disabled]="saving">
              <svg *ngIf="!saving" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/>
                <polyline points="17 21 17 13 7 13 7 21"/>
                <polyline points="7 3 7 8 15 8"/>
              </svg>
              <span class="spinner-sm" *ngIf="saving"></span>
              {{ saving ? 'Enregistrement...' : 'Enregistrer' }}
            </button>
          </div>

          <!-- Success toast -->
          <div class="toast" *ngIf="showSuccess">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
              <polyline points="22 4 12 14.01 9 11.01"/>
            </svg>
            Modifications enregistrées avec succès
          </div>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    .vehicle-info-page {
      padding: 24px;
      width: 100%;
      min-height: calc(100vh - 42px);
      background: #0f172a;
    }

    .top-bar {
      display: flex;
      align-items: center;
      gap: 16px;
      margin-bottom: 24px;
    }

    .btn-back {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 8px 14px;
      background: rgba(30,41,59,0.8);
      border: 1px solid rgba(71,85,105,0.3);
      border-radius: 8px;
      color: #94a3b8;
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-back:hover {
      background: rgba(30,41,59,1);
      color: #e2e8f0;
      border-color: rgba(71,85,105,0.6);
    }

    .page-title {
      font-size: 22px;
      font-weight: 700;
      color: #e2e8f0;
      margin: 0;
    }

    .loading-state {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 12px;
      padding: 80px 0;
      color: #94a3b8;
      font-size: 14px;
    }

    .spinner {
      width: 24px;
      height: 24px;
      border: 3px solid rgba(99,102,241,0.2);
      border-top-color: #6366f1;
      border-radius: 50%;
      animation: spin 0.7s linear infinite;
    }

    .spinner-sm {
      width: 16px;
      height: 16px;
      border: 2px solid rgba(255,255,255,0.3);
      border-top-color: white;
      border-radius: 50%;
      animation: spin 0.7s linear infinite;
      display: inline-block;
    }

    @keyframes spin { to { transform: rotate(360deg); } }

    .content {
      display: flex;
      flex-direction: column;
      gap: 20px;
      max-width: 800px;
    }

    /* Cards */
    .info-card {
      background: rgba(30,41,59,0.8);
      border: 1px solid rgba(71,85,105,0.3);
      border-radius: 12px;
      overflow: hidden;
    }

    .card-header {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 16px 20px;
      border-bottom: 1px solid rgba(71,85,105,0.3);
      color: #6366f1;
    }

    .card-header h2 {
      font-size: 15px;
      font-weight: 600;
      color: #e2e8f0;
      margin: 0;
    }

    .card-body {
      padding: 20px;
    }

    /* Form elements */
    .form-row {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 16px;
    }

    .form-group {
      margin-bottom: 16px;
    }

    .form-group label {
      display: block;
      font-size: 12px;
      font-weight: 500;
      color: #94a3b8;
      margin-bottom: 6px;
    }

    .form-group input,
    .form-group select {
      width: 100%;
      padding: 10px 12px;
      background: #1e293b;
      border: 1px solid #475569;
      border-radius: 8px;
      color: white;
      font-size: 13px;
      font-family: inherit;
      transition: border-color 0.2s;
    }

    .form-group input:focus,
    .form-group select:focus {
      outline: none;
      border-color: #6366f1;
      box-shadow: 0 0 0 3px rgba(99,102,241,0.15);
    }

    .form-group input::placeholder {
      color: #64748b;
    }

    .form-group select {
      cursor: pointer;
      -webkit-appearance: none;
      appearance: none;
      background-image: url("data:image/svg+xml,%3Csvg width='10' height='6' viewBox='0 0 10 6' fill='none' xmlns='http://www.w3.org/2000/svg'%3E%3Cpath d='M1 1L5 5L9 1' stroke='%2394a3b8' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'/%3E%3C/svg%3E");
      background-repeat: no-repeat;
      background-position: right 12px center;
      padding-right: 32px;
    }

    .form-group select option {
      background: #1e293b;
      color: white;
    }

    /* Radio buttons */
    .radio-group {
      display: flex;
      gap: 24px;
      margin-top: 4px;
    }

    .radio-label {
      display: flex;
      align-items: center;
      gap: 8px;
      cursor: pointer;
      font-size: 13px;
      color: #e2e8f0;
    }

    .radio-label input[type="radio"] {
      width: auto;
      appearance: none;
      -webkit-appearance: none;
      width: 18px;
      height: 18px;
      border: 2px solid #475569;
      border-radius: 50%;
      background: #1e293b;
      cursor: pointer;
      position: relative;
      transition: all 0.2s;
      padding: 0;
      margin: 0;
    }

    .radio-label input[type="radio"]:checked {
      border-color: #6366f1;
    }

    .radio-label input[type="radio"]:checked::after {
      content: '';
      position: absolute;
      top: 3px;
      left: 3px;
      width: 8px;
      height: 8px;
      background: #6366f1;
      border-radius: 50%;
    }


    /* Actions */
    .actions-bar {
      display: flex;
      justify-content: flex-end;
    }

    .btn-save {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 24px;
      background: #6366f1;
      border: none;
      border-radius: 8px;
      color: white;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
      font-family: inherit;
    }

    .btn-save:hover:not(:disabled) {
      background: #4f46e5;
    }

    .btn-save:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }

    /* Toast */
    .toast {
      position: fixed;
      bottom: 24px;
      right: 24px;
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 12px 20px;
      background: #166534;
      border: 1px solid #22c55e;
      border-radius: 8px;
      color: #dcfce7;
      font-size: 13px;
      font-weight: 500;
      box-shadow: 0 4px 16px rgba(0,0,0,0.3);
      animation: slideUp 0.3s ease;
      z-index: 1000;
    }

    @keyframes slideUp {
      from { opacity: 0; transform: translateY(12px); }
      to { opacity: 1; transform: translateY(0); }
    }

    /* Date input color fix for dark theme */
    .form-group input[type="date"]::-webkit-calendar-picker-indicator {
      filter: invert(0.7);
    }

    /* Responsive */
    @media (max-width: 640px) {
      .vehicle-info-page { padding: 16px; }
      .form-row { grid-template-columns: 1fr; }
      .page-title { font-size: 18px; }
    }
  `]
})
export class VehicleInfoComponent implements OnInit {
  vehicleId: number = 0;
  loading = true;
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
    private route: ActivatedRoute,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.vehicleId = Number(this.route.snapshot.paramMap.get('id'));
    this.loadVehicle();
  }

  loadVehicle(): void {
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
      },
      error: () => {
        this.loading = false;
      }
    });
  }

  save(): void {
    this.saving = true;
    this.api.updateVehicle(this.vehicleId, this.vehicleForm).subscribe({
      next: () => {
        this.saving = false;
        this.showSuccess = true;
        setTimeout(() => this.showSuccess = false, 3000);
      },
      error: () => {
        this.saving = false;
      }
    });
  }

  goBack(): void {
    this.router.navigate(['/vehicles']);
  }
}
