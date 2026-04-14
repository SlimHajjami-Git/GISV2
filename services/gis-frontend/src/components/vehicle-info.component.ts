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
      <div class="vi-page">
        <!-- Breadcrumb + Back -->
        <div class="vi-topbar">
          <button class="vi-back" (click)="goBack()">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M19 12H5"/><polyline points="12 19 5 12 12 5"/>
            </svg>
          </button>
          <div class="vi-breadcrumb">
            <span class="vi-bc-link" (click)="goBack()">Véhicules</span>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="9 18 15 12 9 6"/></svg>
            <span class="vi-bc-current">Fiche véhicule</span>
          </div>
        </div>

        <!-- Loading -->
        <div class="vi-loading" *ngIf="loading">
          <div class="vi-spinner"></div>
        </div>

        <!-- Content -->
        <div class="vi-content" *ngIf="!loading">
          <!-- Header card -->
          <div class="vi-hero">
            <div class="vi-hero-icon">
              <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                <rect x="1" y="3" width="15" height="13" rx="2"/>
                <path d="M16 8h2a2 2 0 0 1 2 2v1l2 1v4h-2"/>
                <circle cx="5.5" cy="18.5" r="2.5"/>
                <circle cx="18.5" cy="18.5" r="2.5"/>
              </svg>
            </div>
            <div class="vi-hero-text">
              <h1 class="vi-title">{{ vehicleForm.marque || 'Nouveau' }} {{ vehicleForm.modele || 'Véhicule' }}</h1>
              <span class="vi-subtitle">Informations du véhicule</span>
            </div>
          </div>

          <!-- Form grid -->
          <div class="vi-grid">
            <!-- Col 1: Identification -->
            <div class="vi-card">
              <div class="vi-card-head">
                <div class="vi-card-dot"></div>
                <span>Identification</span>
              </div>
              <div class="vi-card-body">
                <label class="vi-field">
                  <span class="vi-label">Marque</span>
                  <input type="text" [(ngModel)]="vehicleForm.marque" placeholder="Ex: Renault">
                </label>
                <label class="vi-field">
                  <span class="vi-label">Modèle</span>
                  <input type="text" [(ngModel)]="vehicleForm.modele" placeholder="Ex: Kangoo">
                </label>
                <label class="vi-field">
                  <span class="vi-label">Mise en circulation</span>
                  <input type="date" [(ngModel)]="vehicleForm.dateMiseEnCirculation">
                </label>
                <label class="vi-field">
                  <span class="vi-label">Carburant</span>
                  <select [(ngModel)]="vehicleForm.carburant">
                    <option value="">Sélectionner</option>
                    <option value="diesel">Diesel</option>
                    <option value="essence">Essence</option>
                    <option value="hybride">Hybride</option>
                    <option value="electrique">Électrique</option>
                  </select>
                </label>
              </div>
            </div>

            <!-- Col 2: Acquisition -->
            <div class="vi-card">
              <div class="vi-card-head">
                <div class="vi-card-dot accent"></div>
                <span>Acquisition</span>
              </div>
              <div class="vi-card-body">
                <div class="vi-toggle-row">
                  <button class="vi-toggle" [class.active]="vehicleForm.typeAcquisition === 'achat'" (click)="vehicleForm.typeAcquisition = 'achat'">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 2v20M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/></svg>
                    Achat
                  </button>
                  <button class="vi-toggle" [class.active]="vehicleForm.typeAcquisition === 'leasing'" (click)="vehicleForm.typeAcquisition = 'leasing'">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="2" y="5" width="20" height="14" rx="2"/><line x1="2" y1="10" x2="22" y2="10"/></svg>
                    Leasing
                  </button>
                </div>
                <label class="vi-field">
                  <span class="vi-label">Prix d'achat</span>
                  <div class="vi-input-suffix">
                    <input type="number" [(ngModel)]="vehicleForm.prixAchat" placeholder="0.00">
                    <span class="vi-suffix">MAD</span>
                  </div>
                </label>
                <label class="vi-field" *ngIf="vehicleForm.typeAcquisition === 'leasing'">
                  <span class="vi-label">Traite mensuelle</span>
                  <div class="vi-input-suffix">
                    <input type="number" [(ngModel)]="vehicleForm.traiteMensuelle" placeholder="0.00">
                    <span class="vi-suffix">MAD/mois</span>
                  </div>
                </label>
              </div>
            </div>
          </div>

          <!-- Save bar -->
          <div class="vi-actions">
            <button class="vi-save" (click)="save()" [disabled]="saving">
              <svg *ngIf="!saving" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/>
                <polyline points="17 21 17 13 7 13 7 21"/>
                <polyline points="7 3 7 8 15 8"/>
              </svg>
              <span class="vi-save-spinner" *ngIf="saving"></span>
              {{ saving ? 'Enregistrement...' : 'Enregistrer les modifications' }}
            </button>
          </div>
        </div>

        <!-- Toast -->
        <div class="vi-toast" *ngIf="showSuccess">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
            <polyline points="22 4 12 14.01 9 11.01"/>
          </svg>
          Modifications enregistrées
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    .vi-page {
      padding: 20px 28px;
      width: 100%;
      min-height: calc(100vh - 42px);
      background: #0f172a;
    }

    /* ── Topbar ── */
    .vi-topbar {
      display: flex;
      align-items: center;
      gap: 12px;
      margin-bottom: 24px;
    }

    .vi-back {
      display: grid;
      place-items: center;
      width: 36px;
      height: 36px;
      border-radius: 10px;
      border: 1px solid rgba(71,85,105,0.3);
      background: rgba(30,41,59,0.6);
      color: #94a3b8;
      cursor: pointer;
      transition: all 0.2s;
    }
    .vi-back:hover { color: #e2e8f0; border-color: #6366f1; background: rgba(99,102,241,0.1); }

    .vi-breadcrumb {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 13px;
      color: #475569;
    }
    .vi-bc-link { color: #64748b; cursor: pointer; transition: color 0.15s; }
    .vi-bc-link:hover { color: #818cf8; }
    .vi-bc-current { color: #cbd5e1; font-weight: 500; }

    /* ── Loading ── */
    .vi-loading {
      display: grid;
      place-items: center;
      padding: 120px 0;
    }
    .vi-spinner {
      width: 28px; height: 28px;
      border: 3px solid rgba(99,102,241,0.15);
      border-top-color: #6366f1;
      border-radius: 50%;
      animation: spin 0.65s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }

    /* ── Hero header ── */
    .vi-hero {
      display: flex;
      align-items: center;
      gap: 16px;
      margin-bottom: 24px;
      padding: 20px 24px;
      background: linear-gradient(135deg, rgba(99,102,241,0.08) 0%, rgba(30,41,59,0.6) 100%);
      border: 1px solid rgba(99,102,241,0.15);
      border-radius: 14px;
    }
    .vi-hero-icon {
      display: grid;
      place-items: center;
      width: 52px; height: 52px;
      border-radius: 12px;
      background: rgba(99,102,241,0.12);
      color: #818cf8;
      flex-shrink: 0;
    }
    .vi-title {
      font-size: 20px;
      font-weight: 700;
      color: #f1f5f9;
      margin: 0;
      letter-spacing: -0.3px;
    }
    .vi-subtitle {
      font-size: 13px;
      color: #64748b;
    }

    /* ── Card grid ── */
    .vi-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 20px;
    }

    .vi-card {
      background: rgba(30,41,59,0.55);
      border: 1px solid rgba(71,85,105,0.25);
      border-radius: 14px;
      overflow: hidden;
      transition: border-color 0.2s;
    }
    .vi-card:hover { border-color: rgba(99,102,241,0.3); }

    .vi-card-head {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 14px 20px;
      font-size: 13px;
      font-weight: 600;
      color: #cbd5e1;
      letter-spacing: 0.3px;
      border-bottom: 1px solid rgba(71,85,105,0.2);
    }
    .vi-card-dot {
      width: 8px; height: 8px;
      border-radius: 50%;
      background: #22d3ee;
    }
    .vi-card-dot.accent { background: #818cf8; }

    .vi-card-body {
      padding: 20px;
      display: flex;
      flex-direction: column;
      gap: 16px;
    }

    /* ── Fields ── */
    .vi-field {
      display: flex;
      flex-direction: column;
      gap: 6px;
    }
    .vi-label {
      font-size: 11px;
      font-weight: 600;
      color: #64748b;
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }

    .vi-field input,
    .vi-field select {
      width: 100%;
      padding: 10px 14px;
      background: rgba(15,23,42,0.5);
      border: 1px solid rgba(71,85,105,0.3);
      border-radius: 10px;
      color: #e2e8f0;
      font-size: 14px;
      font-family: inherit;
      transition: border-color 0.2s, box-shadow 0.2s;
      box-sizing: border-box;
    }
    .vi-field input:focus,
    .vi-field select:focus {
      outline: none;
      border-color: #6366f1;
      box-shadow: 0 0 0 3px rgba(99,102,241,0.12);
    }
    .vi-field input::placeholder { color: #475569; }

    .vi-field select {
      cursor: pointer;
      appearance: none;
      background-image: url("data:image/svg+xml,%3Csvg width='10' height='6' viewBox='0 0 10 6' fill='none' xmlns='http://www.w3.org/2000/svg'%3E%3Cpath d='M1 1L5 5L9 1' stroke='%2394a3b8' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'/%3E%3C/svg%3E");
      background-repeat: no-repeat;
      background-position: right 14px center;
      padding-right: 36px;
    }
    .vi-field select option { background: #1e293b; color: #e2e8f0; }

    /* Date input fix */
    .vi-field input[type="date"]::-webkit-calendar-picker-indicator { filter: invert(0.6); }

    /* Input with suffix */
    .vi-input-suffix {
      position: relative;
    }
    .vi-input-suffix input {
      width: 100%;
      padding-right: 64px;
      padding: 10px 14px;
      padding-right: 70px;
      background: rgba(15,23,42,0.5);
      border: 1px solid rgba(71,85,105,0.3);
      border-radius: 10px;
      color: #e2e8f0;
      font-size: 14px;
      font-family: inherit;
      transition: border-color 0.2s, box-shadow 0.2s;
      box-sizing: border-box;
    }
    .vi-input-suffix input:focus {
      outline: none;
      border-color: #6366f1;
      box-shadow: 0 0 0 3px rgba(99,102,241,0.12);
    }
    .vi-input-suffix input::placeholder { color: #475569; }
    .vi-suffix {
      position: absolute;
      right: 14px;
      top: 50%;
      transform: translateY(-50%);
      font-size: 12px;
      font-weight: 500;
      color: #475569;
      pointer-events: none;
    }

    /* ── Toggle buttons ── */
    .vi-toggle-row {
      display: flex;
      gap: 8px;
    }
    .vi-toggle {
      flex: 1;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 8px;
      padding: 10px;
      border-radius: 10px;
      border: 1px solid rgba(71,85,105,0.3);
      background: rgba(15,23,42,0.4);
      color: #64748b;
      font-size: 13px;
      font-weight: 500;
      font-family: inherit;
      cursor: pointer;
      transition: all 0.2s;
    }
    .vi-toggle:hover { border-color: rgba(99,102,241,0.3); color: #94a3b8; }
    .vi-toggle.active {
      background: rgba(99,102,241,0.12);
      border-color: rgba(99,102,241,0.4);
      color: #a5b4fc;
    }

    /* ── Actions bar ── */
    .vi-actions {
      display: flex;
      justify-content: flex-end;
      margin-top: 24px;
    }

    .vi-save {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 11px 28px;
      background: #6366f1;
      border: none;
      border-radius: 10px;
      color: white;
      font-size: 14px;
      font-weight: 600;
      font-family: inherit;
      cursor: pointer;
      transition: all 0.2s;
    }
    .vi-save:hover:not(:disabled) { background: #4f46e5; transform: translateY(-1px); box-shadow: 0 4px 12px rgba(99,102,241,0.3); }
    .vi-save:active:not(:disabled) { transform: translateY(0); }
    .vi-save:disabled { opacity: 0.5; cursor: not-allowed; }

    .vi-save-spinner {
      width: 16px; height: 16px;
      border: 2px solid rgba(255,255,255,0.3);
      border-top-color: white;
      border-radius: 50%;
      animation: spin 0.65s linear infinite;
      display: inline-block;
    }

    /* ── Toast ── */
    .vi-toast {
      position: fixed;
      bottom: 24px;
      right: 24px;
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 14px 22px;
      background: rgba(22,101,52,0.95);
      border: 1px solid rgba(34,197,94,0.4);
      border-radius: 12px;
      color: #dcfce7;
      font-size: 13px;
      font-weight: 500;
      backdrop-filter: blur(12px);
      box-shadow: 0 8px 24px rgba(0,0,0,0.3);
      animation: toastIn 0.35s cubic-bezier(0.16, 1, 0.3, 1);
      z-index: 1000;
    }
    @keyframes toastIn {
      from { opacity: 0; transform: translateY(16px) scale(0.96); }
      to { opacity: 1; transform: translateY(0) scale(1); }
    }

    /* ── Responsive ── */
    @media (max-width: 768px) {
      .vi-page { padding: 16px; }
      .vi-grid { grid-template-columns: 1fr; }
      .vi-hero { padding: 16px; }
      .vi-title { font-size: 17px; }
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
