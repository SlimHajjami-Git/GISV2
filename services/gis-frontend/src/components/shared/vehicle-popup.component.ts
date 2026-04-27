import { Component, EventEmitter, Input, Output, OnInit, OnChanges, SimpleChanges, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Vehicle } from '../../models/types';
import { ApiService } from '../../services/api.service';
import { trigger, transition, style, animate } from '@angular/animations';

interface Brand {
  id: number;
  name: string;
  logoUrl?: string;
  modelCount: number;
}

interface VehicleModel {
  id: number;
  name: string;
  vehicleType?: string;
}

export interface CompanyOption {
  id: number;
  name: string;
}

@Component({
  selector: 'app-vehicle-popup',
  standalone: true,
  imports: [CommonModule, FormsModule],
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 }))
      ])
    ]),
    trigger('slideIn', [
      transition(':enter', [
        style({ transform: 'translateX(100%)' }),
        animate('300ms ease-out', style({ transform: 'translateX(0)' }))
      ])
    ])
  ],
  template: `
    <div class="panel-overlay" *ngIf="isOpen" @fadeIn (mousedown)="onOverlayClick($event)">
      <div class="slide-panel" @slideIn (mousedown)="$event.stopPropagation()">
        <!-- Panel Header -->
        <div class="panel-header">
          <div class="panel-header-content">
            <div class="panel-icon">🚗</div>
            <div class="panel-header-info">
              <h2>{{ vehicle?.id ? 'Modifier le véhicule' : 'Nouveau véhicule' }}</h2>
              <p>{{ vehicle?.id ? formData.name : 'Remplissez les informations' }}</p>
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
          <form (ngSubmit)="onSubmit()">
            <!-- Company Selection (Admin only) -->
            <div class="form-section" *ngIf="companies && companies.length > 0">
              <h3 class="section-title">🏢 Société</h3>
              <div class="form-group">
                <label for="companyId">Société *</label>
                <select id="companyId" name="companyId" [(ngModel)]="formData.companyId" required>
                  <option [value]="null" disabled>-- Sélectionner une société --</option>
                  <option *ngFor="let company of companies" [value]="company.id">{{ company.name }}</option>
                </select>
              </div>
            </div>

            <!-- Vehicle Info Section -->
            <div class="form-section">
              <h3 class="section-title">📋 Informations véhicule</h3>
              <div class="form-row">
                <div class="form-group">
                  <label for="name">Nom du véhicule *</label>
                  <input type="text" id="name" name="name" [(ngModel)]="formData.name" required placeholder="Ex: Camion principal" />
                </div>
                <div class="form-group">
                  <label for="plate">Plaque *</label>
                  <input type="text" id="plate" name="plate" [(ngModel)]="formData.plate" required placeholder="Ex: ABC-1234" />
                </div>
              </div>

              <div class="form-row">
                <div class="form-group">
                  <label for="brandId">Marque *</label>
                  <select id="brandId" name="brandId" [(ngModel)]="formData.brandId" (ngModelChange)="onBrandChange($event)" required>
                    <option [value]="null">-- Sélectionner --</option>
                    <option *ngFor="let brand of brands" [value]="brand.id">{{ brand.name }}</option>
                  </select>
                </div>
                <div class="form-group">
                  <label for="modelId">Modèle *</label>
                  <select id="modelId" name="modelId" [(ngModel)]="formData.modelId" (ngModelChange)="onModelChange($event)" required [disabled]="!formData.brandId || loadingModels">
                    <option [value]="null">{{ loadingModels ? 'Chargement...' : '-- Sélectionner --' }}</option>
                    <option *ngFor="let model of models" [value]="model.id">{{ model.name }}</option>
                  </select>
                </div>
              </div>

              <div class="form-row">
                <div class="form-group">
                  <label for="year">Année *</label>
                  <input type="number" id="year" name="year" [(ngModel)]="formData.year" required min="1900" max="2100" placeholder="Ex: 2023" />
                </div>
                <div class="form-group">
                  <label for="type">Type *</label>
                  <select id="type" name="type" [(ngModel)]="formData.type" required>
                    <option value="">Sélectionner</option>
                    <option value="camion">Camion</option>
                    <option value="citadine">Citadine</option>
                    <option value="suv">SUV</option>
                    <option value="utilitaire">Utilitaire</option>
                    <option value="other">Autre</option>
                  </select>
                </div>
              </div>

              <div class="form-row">
                <div class="form-group">
                  <label for="status">Statut *</label>
                  <select id="status" name="status" [(ngModel)]="formData.status" required>
                    <option value="">Sélectionner</option>
                    <option value="available">Disponible</option>
                    <option value="in_use">En service</option>
                    <option value="maintenance">En maintenance</option>
                  </select>
                </div>
                <div class="form-group">
                  <label for="mileage">Kilométrage *</label>
                  <input type="number" id="mileage" name="mileage" [(ngModel)]="formData.mileage" required min="0" placeholder="Ex: 50000" />
                </div>
              </div>

              <div class="form-row">
                <div class="form-group">
                  <label for="color">Couleur</label>
                  <input type="text" id="color" name="color" [(ngModel)]="formData.color" placeholder="Ex: Blanc" />
                </div>
                <div class="form-group">
                  <label for="fuelType">Type de carburant *</label>
                  <select id="fuelType" name="fuelType" [(ngModel)]="formData.fuelType" required>
                    <option value="">Sélectionner</option>
                    <option *ngFor="let ft of fuelTypes" [value]="ft.code">{{ ft.name }}</option>
                  </select>
                </div>
              </div>

              <div class="form-row">
                <div class="form-group">
                  <label for="fuelTankCapacity">Capacité réservoir (L)</label>
                  <input type="number" id="fuelTankCapacity" name="fuelTankCapacity" [(ngModel)]="formData.fuelTankCapacity" min="0" placeholder="Ex: 60" />
                </div>
              </div>
            </div>

            <!--
              GPS Section — admin-only.
              Calypso 7: company users no longer manage GPS hardware from the
              vehicle popup; that responsibility lives in /admin/vehicules
              (system admin) and on the dedicated /gps-devices page. The
              GPS-related fields on the vehicle entity are still preserved on
              save (see onSubmit) so a non-admin save never wipes them.
            -->
            <div class="form-section" *ngIf="isSystemAdminContext">
              <h3 class="section-title">📍 GPS</h3>
              <div class="form-group">
                <label class="checkbox-label">
                  <input type="checkbox" name="hasGPS" [(ngModel)]="formData.hasGPS" (change)="onHasGpsChange()" />
                  <span>Ce véhicule dispose d'un GPS</span>
                </label>
              </div>

          <!-- GPS Device Section -->
          <div class="gps-section" *ngIf="formData.hasGPS">
            <div class="section-header">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z"/>
                <circle cx="12" cy="10" r="3"/>
              </svg>
              <span>Appareil GPS</span>
            </div>
            
            <!-- GPS Selection Mode -->
            <div class="gps-mode-selector">
              <label class="radio-label">
                <input type="radio" name="gpsMode" value="existing" [(ngModel)]="gpsMode" (change)="onGpsModeChange()">
                <span>Sélectionner un appareil existant</span>
              </label>
              <label class="radio-label">
                <input type="radio" name="gpsMode" value="new" [(ngModel)]="gpsMode" (change)="onGpsModeChange()">
                <span>Ajouter un nouvel appareil</span>
              </label>
            </div>

            <!-- Existing GPS Device Selection -->
            <div class="form-group" *ngIf="gpsMode === 'existing'">
              <label for="gpsDeviceId">Appareil GPS disponible</label>
              <select id="gpsDeviceId" name="gpsDeviceId" [(ngModel)]="formData.gpsDeviceId" (change)="onExistingDeviceSelected()" class="gps-select">
                <option [value]="null">-- Sélectionner un appareil --</option>
                <option *ngFor="let device of availableGpsDevices" [value]="device.id">
                  {{ device.deviceUid }} {{ device.mat ? '(MAT: ' + device.mat + ')' : '' }}
                  {{ device.lastCommunication ? '- Dernière comm: ' + formatDate(device.lastCommunication) : '- Jamais connecté' }}
                </option>
              </select>
              <p class="help-text" *ngIf="availableGpsDevices.length === 0">
                Aucun appareil GPS disponible. Ajoutez-en un nouveau ci-dessous.
              </p>
            </div>

            <!-- Fuel sensor mode for existing device -->
            <div class="form-group" *ngIf="gpsMode === 'existing' && formData.gpsDeviceId">
              <label for="gpsFuelSensorModeExisting">Mode capteur carburant</label>
              <select id="gpsFuelSensorModeExisting" name="gpsFuelSensorModeExisting" [(ngModel)]="formData.gpsFuelSensorMode">
                <option value="raw_255">Brut 0-255 (défaut)</option>
                <option value="percent">Pourcentage 0-100%</option>
                <option value="liters">Litres</option>
                <option value="half_liter">½ Litre (1 unité = 0.5L)</option>
              </select>
              <p class="help-text">Définit comment interpréter les données carburant du GPS</p>
            </div>

            <!-- SIM Number for existing device -->
            <div class="form-grid" *ngIf="gpsMode === 'existing' && formData.gpsDeviceId">
              <div class="form-group">
                <label for="gpsSimNumberExisting">Numéro SIM (Téléphone)</label>
                <input
                  type="text"
                  id="gpsSimNumberExisting"
                  name="gpsSimNumberExisting"
                  [(ngModel)]="formData.gpsSimNumber"
                  placeholder="Ex: +216 50 123 456"
                />
              </div>
              <div class="form-group">
                <label for="gpsSimOperatorExisting">Opérateur SIM</label>
                <select id="gpsSimOperatorExisting" name="gpsSimOperatorExisting" [(ngModel)]="formData.gpsSimOperator">
                  <option value="">Sélectionner</option>
                  <option value="ooredoo">Ooredoo</option>
                  <option value="orange_tunisie">Orange Tunisie</option>
                  <option value="tunisie_telecom">Tunisie Telecom</option>
                  <option value="other">Autre</option>
                </select>
              </div>
            </div>

            <!-- New GPS Device Form -->
            <div class="form-grid" *ngIf="gpsMode === 'new'">
              <div class="form-group">
                <label for="gpsImei">IMEI / ID Appareil *</label>
                <input
                  type="text"
                  id="gpsImei"
                  name="gpsImei"
                  [(ngModel)]="formData.gpsImei"
                  placeholder="Ex: 358762109054321"
                  maxlength="20"
                  required
                />
              </div>

              <div class="form-group">
                <label for="gpsSimNumber">Numéro SIM</label>
                <input
                  type="text"
                  id="gpsSimNumber"
                  name="gpsSimNumber"
                  [(ngModel)]="formData.gpsSimNumber"
                  placeholder="Ex: +216 50 123 456"
                />
              </div>

              <div class="form-group">
                <label for="gpsSimOperator">Opérateur SIM</label>
                <select id="gpsSimOperator" name="gpsSimOperator" [(ngModel)]="formData.gpsSimOperator">
                  <option value="">Sélectionner</option>
                  <option value="ooredoo">Ooredoo</option>
                  <option value="orange_tunisie">Orange Tunisie</option>
                  <option value="tunisie_telecom">Tunisie Telecom</option>
                  <option value="other">Autre</option>
                </select>
              </div>

              <div class="form-group">
                <label for="gpsBrand">Marque GPS</label>
                <select id="gpsBrand" name="gpsBrand" [(ngModel)]="formData.gpsBrand" (ngModelChange)="onGpsBrandChange($event)">
                  <option value="">Sélectionner</option>
                  <option value="NEMS">NEMS</option>
                  <option value="NORON">Noron</option>
                  <option value="Other">Autre</option>
                </select>
              </div>

              <div class="form-group">
                <label for="gpsModel">Version GPS</label>
                <select id="gpsModel" name="gpsModel" [(ngModel)]="formData.gpsModel" *ngIf="formData.gpsBrand !== 'Other'">
                  <option value="">Sélectionner</option>
                  <ng-container *ngIf="formData.gpsBrand === 'NEMS'">
                    <option value="S">NEMS S</option>
                    <option value="L">NEMS L</option>
                  </ng-container>
                  <ng-container *ngIf="formData.gpsBrand === 'NORON'">
                    <option value="NR024">NR024</option>
                  </ng-container>
                </select>
                <input *ngIf="formData.gpsBrand === 'Other'" type="text" id="gpsModel" name="gpsModel" [(ngModel)]="formData.gpsModel" placeholder="Ex: GT06N" />
              </div>

              <!-- Noron info note -->
              <div class="gps-info-note" *ngIf="formData.gpsBrand === 'NORON'">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4"/><path d="M12 8h.01"/></svg>
                <span>Ce tracker fournit uniquement la position GPS et la vitesse. Pas de données carburant, odomètre ou température.</span>
              </div>

              <div class="form-group">
                <label for="gpsInstallationDate">Date d'installation</label>
                <input
                  type="date"
                  id="gpsInstallationDate"
                  name="gpsInstallationDate"
                  [(ngModel)]="formData.gpsInstallationDate"
                />
              </div>

              <div class="form-group">
                <label for="gpsMat">Matricule GPS</label>
                <input
                  type="text"
                  id="gpsMat"
                  name="gpsMat"
                  [(ngModel)]="formData.gpsMat"
                  placeholder="Ex: MAT-001"
                />
              </div>

              <div class="form-group" *ngIf="formData.gpsBrand !== 'NORON'">
                <label for="gpsFuelSensorMode">Mode capteur carburant</label>
                <select id="gpsFuelSensorMode" name="gpsFuelSensorMode" [(ngModel)]="formData.gpsFuelSensorMode">
                  <option value="raw_255">Brut 0-255 (défaut)</option>
                  <option value="percent">Pourcentage 0-100%</option>
                  <option value="liters">Litres</option>
                  <option value="half_liter">½ Litre (1 unité = 0.5L)</option>
                </select>
                <p class="help-text">Définit comment interpréter les données carburant du GPS</p>
              </div>
            </div>
          </div>

            <!--
              Acquisition & financement — moved from the legacy vehicle-info
              panel (Calypso 7). The vehicle now has a single edit surface,
              with sections that mirror real-world steps: identification → GPS
              (admin) → financement → échéancier (calculated read-only).
            -->
            <div class="form-section">
              <h3 class="section-title">💰 Acquisition & financement</h3>
              <div class="form-row">
                <div class="form-group">
                  <label for="acquisitionType">Type d'acquisition</label>
                  <select id="acquisitionType" name="acquisitionType" [(ngModel)]="formData.acquisitionType">
                    <option value="purchase">Achat</option>
                    <option value="leasing">Auto-financement</option>
                  </select>
                </div>
                <div class="form-group">
                  <label for="purchaseDate">Date d'achat</label>
                  <input type="date" id="purchaseDate" name="purchaseDate" [(ngModel)]="formData.purchaseDate" />
                </div>
              </div>
              <div class="form-row">
                <div class="form-group">
                  <label for="registrationDate">Date mise en circulation</label>
                  <input type="date" id="registrationDate" name="registrationDate" [(ngModel)]="formData.registrationDate" />
                </div>
                <div class="form-group">
                  <label for="purchasePrice">{{ formData.acquisitionType === 'leasing' ? 'Montant Auto-financement' : "Prix d'achat" }}</label>
                  <div class="input-with-suffix">
                    <input type="number" id="purchasePrice" name="purchasePrice" [(ngModel)]="formData.purchasePrice" min="0" placeholder="0.00" />
                    <span class="input-suffix">TND</span>
                  </div>
                </div>
              </div>

              <ng-container *ngIf="formData.acquisitionType === 'leasing'">
                <div class="form-row">
                  <div class="form-group">
                    <label for="leasingMonthlyPayment">Traite mensuelle</label>
                    <div class="input-with-suffix">
                      <input type="number" id="leasingMonthlyPayment" name="leasingMonthlyPayment" [(ngModel)]="formData.leasingMonthlyPayment" min="0" placeholder="0.00" />
                      <span class="input-suffix">TND/mois</span>
                    </div>
                  </div>
                  <div class="form-group">
                    <label for="leasingDurationMonths">Durée du leasing</label>
                    <div class="input-with-suffix">
                      <input type="number" id="leasingDurationMonths" name="leasingDurationMonths" [(ngModel)]="formData.leasingDurationMonths" min="1" max="120" placeholder="36" />
                      <span class="input-suffix">mois</span>
                    </div>
                  </div>
                </div>
                <div class="form-row">
                  <div class="form-group">
                    <label for="leasingStartDate">Date début leasing</label>
                    <input type="date" id="leasingStartDate" name="leasingStartDate" [(ngModel)]="formData.leasingStartDate" />
                  </div>
                  <div class="form-group">
                    <label for="leasingPaymentDay">Jour de paiement</label>
                    <select id="leasingPaymentDay" name="leasingPaymentDay" [(ngModel)]="formData.leasingPaymentDay">
                      <option [ngValue]="null">— Choisir —</option>
                      <option *ngFor="let d of paymentDays" [ngValue]="d">{{ d }}</option>
                    </select>
                  </div>
                </div>

                <!-- Calculated payment schedule (read-only) -->
                <div class="leasing-schedule"
                     *ngIf="formData.leasingStartDate && formData.leasingDurationMonths && formData.leasingMonthlyPayment">
                  <label class="schedule-title">Échéancier des paiements</label>
                  <div class="schedule-summary">
                    <span>{{ formData.leasingDurationMonths }} mois × {{ formData.leasingMonthlyPayment | number:'1.2-2' }} TND
                      = <strong>{{ (formData.leasingDurationMonths * formData.leasingMonthlyPayment) | number:'1.2-2' }} TND</strong>
                    </span>
                  </div>
                  <div class="schedule-table-wrapper">
                    <table class="schedule-table">
                      <thead>
                        <tr><th>N°</th><th>Date</th><th>Montant</th><th>Statut</th></tr>
                      </thead>
                      <tbody>
                        <tr *ngFor="let payment of getPaymentSchedule()" [class.paid]="payment.isPast" [class.current]="payment.isCurrent">
                          <td>{{ payment.index }}</td>
                          <td>{{ payment.date }}</td>
                          <td>{{ formData.leasingMonthlyPayment | number:'1.2-2' }} TND</td>
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
              </ng-container>
            </div>

            </div>
          </form>
        </div>

        <!-- Panel Footer -->
        <div class="panel-footer">
          <button type="button" class="btn-secondary" (click)="close()">Annuler</button>
          <button type="button" class="btn-primary" (click)="onSubmit()">
            {{ vehicle?.id ? 'Mettre à jour' : 'Ajouter' }}
          </button>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .panel-overlay {
      position: fixed;
      top: 42px; /* Start below navbar (42px height) so navbar stays accessible */
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(0, 0, 0, 0.5);
      z-index: 1050; /* Above page content, below navbar (1100) */
      display: flex;
      justify-content: flex-end;
    }

    .slide-panel {
      width: 520px;
      max-width: 100%;
      height: 100%;
      background: #fff;
      display: flex;
      flex-direction: column;
      box-shadow: -4px 0 20px rgba(0, 0, 0, 0.15);
    }

    .panel-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 20px 24px;
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
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
      color: #00a388;
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

    .company-select {
      margin-bottom: 16px;
      padding-bottom: 16px;
      border-bottom: 1px solid #e2e8f0;
    }

    .company-dropdown {
      width: 100%;
      padding: 10px 12px;
      background: white;
      border: 1px solid #3b82f6;
      border-radius: 4px;
      font-size: 13px;
      color: #1e293b;
      font-weight: 500;
    }

    .company-dropdown:focus {
      outline: none;
      border-color: #2563eb;
      box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.15);
    }

    .form-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 16px;
      margin-top: 12px;
    }

    .gps-section {
      margin-top: 20px;
      padding-top: 20px;
      border-top: 1px solid #e2e8f0;
    }

    .section-header {
      display: flex;
      align-items: center;
      gap: 8px;
      margin-bottom: 16px;
      color: #3b82f6;
      font-weight: 600;
      font-size: 13px;
    }

    .section-header svg {
      color: #3b82f6;
    }

    .gps-mode-selector {
      display: flex;
      gap: 20px;
      margin-bottom: 16px;
      padding: 12px;
      background: #f8fafc;
      border-radius: 6px;
    }

    .radio-label {
      display: flex;
      align-items: center;
      gap: 8px;
      cursor: pointer;
      font-size: 12px;
      color: #475569;
    }

    .radio-label input[type="radio"] {
      accent-color: #3b82f6;
    }

    .gps-select {
      width: 100%;
      padding: 10px 12px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 4px;
      font-size: 12px;
      color: #1e293b;
    }

    .gps-select:focus {
      outline: none;
      border-color: #3b82f6;
    }

    .gps-info-note {
      display: flex;
      align-items: flex-start;
      gap: 8px;
      padding: 10px 12px;
      background: #fef3c7;
      border: 1px solid #fde68a;
      border-radius: 6px;
      margin-top: 8px;
      grid-column: 1 / -1;
    }

    .gps-info-note svg {
      flex-shrink: 0;
      color: #d97706;
      margin-top: 1px;
    }

    .gps-info-note span {
      font-size: 11px;
      color: #92400e;
      line-height: 1.4;
    }

    .help-text {
      font-size: 11px;
      color: #94a3b8;
      margin-top: 6px;
      font-style: italic;
    }

    .popup-footer {
      padding: 14px 20px;
      border-top: 1px solid #e2e8f0;
      display: flex;
      gap: 10px;
      justify-content: flex-end;
      background: #f8fafc;
    }

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
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
      color: white;
      border: none;
      border-radius: 10px;
      font-weight: 600;
      font-size: 14px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-primary:hover {
      box-shadow: 0 4px 16px rgba(0, 212, 170, 0.3);
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

    /* ── Acquisition: input + suffix (TND, mois, …) ── */
    .input-with-suffix {
      position: relative;
    }
    .input-with-suffix input {
      width: 100%;
      padding-right: 56px;
    }
    .input-suffix {
      position: absolute;
      top: 50%;
      right: 10px;
      transform: translateY(-50%);
      font-size: 11px;
      color: #64748b;
      pointer-events: none;
    }

    /* ── Acquisition: échéancier leasing (read-only) ── */
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

    @media (max-width: 640px) {
      .form-row {
        grid-template-columns: 1fr;
      }

      .slide-panel {
        width: 100%;
      }

      .panel-body {
        padding: 16px;
      }
    }
  `]
})
export class VehiclePopupComponent implements OnInit, OnChanges {
  @Input() isOpen = false;
  @Input() vehicle: Vehicle | null = null;
  @Input() companies: CompanyOption[] = [];
  @Input() defaultCompanyId: number | null = null;
  /**
   * Calypso 7: GPS hardware management (assign / new device / SIM / fuel sensor mode)
   * is restricted to the system admin context (`/admin/vehicules`). Company
   * users editing their own fleet should never see those fields. The
   * underlying GPS columns on the vehicle row stay intact on save — see
   * onSubmit which does NOT clobber them when this flag is false.
   */
  @Input() isSystemAdminContext = false;
  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<Partial<Vehicle>>();

  availableGpsDevices: any[] = [];
  gpsMode: 'existing' | 'new' = 'existing';

  brands: Brand[] = [];
  models: VehicleModel[] = [];

  /**
   * Calypso 7: fuel types are managed centrally under
   * Paramètres → Carburant. We load them from `/api/fuelprices/types` on
   * popup open. The hardcoded list below is kept ONLY as a fallback so
   * the dropdown stays usable if the request errors out.
   */
  fuelTypes: { code: string; name: string }[] = [
    { code: 'diesel', name: 'Diesel' },
    { code: 'essence', name: 'Essence' },
    { code: 'sans_plomb', name: 'Essence Sans Plomb' },
    { code: 'gpl', name: 'GPL' },
    { code: 'gnv', name: 'GNV' },
    { code: 'electrique', name: 'Électrique' },
    { code: 'hybride', name: 'Hybride' },
    { code: 'hybride_rechargeable', name: 'Hybride Rechargeable' }
  ];
  loadingModels = false;

  /** Day-of-month options for the leasing payment day dropdown (1..28). */
  readonly paymentDays: number[] = Array.from({ length: 28 }, (_, i) => i + 1);

  formData: any = {
    name: '',
    plate: '',
    brand: '',
    model: '',
    brandId: null,
    modelId: null,
    year: new Date().getFullYear(),
    type: 'citadine',
    status: 'available',
    mileage: 0,
    fuelTankCapacity: null,
    color: '',
    fuelType: 'diesel',
    companyId: null,
    hasGPS: false,
    gpsDeviceId: undefined,
    gpsImei: '',
    gpsSimNumber: '',
    gpsSimOperator: undefined,
    gpsBrand: '',
    gpsModel: '',
    gpsInstallationDate: undefined,
    gpsMat: '',
    gpsFuelSensorMode: 'raw_255',
    // Acquisition (Calypso 7 — fused from the legacy vehicle-info panel).
    acquisitionType: 'purchase',
    purchaseDate: '',
    purchasePrice: null,
    leasingMonthlyPayment: null,
    leasingDurationMonths: null,
    leasingStartDate: '',
    leasingPaymentDay: null,
    registrationDate: ''
  };

  constructor(private apiService: ApiService, private http: HttpClient, private cdr: ChangeDetectorRef) {}

  ngOnInit() {
    this.loadBrands();
    this.loadFuelTypes();
    if (this.isSystemAdminContext) {
      this.loadAvailableDevices();
    }
    if (this.vehicle) {
      this.hydrateFormFromVehicle(this.vehicle);
      if (this.vehicle.gpsDeviceId) {
        this.gpsMode = 'existing';
      }
    }
    if (this.defaultCompanyId && !this.formData.companyId) {
      this.formData.companyId = this.defaultCompanyId;
    }
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['isOpen'] && changes['isOpen'].currentValue) {
      this.loadFuelTypes();
      if (this.isSystemAdminContext) {
        this.loadAvailableDevices();
      }
      if (this.vehicle) {
        this.hydrateFormFromVehicle(this.vehicle);
        this.gpsMode = this.vehicle.gpsDeviceId ? 'existing' : 'new';
        // Load brands first, then resolve brandId from brand name if needed
        this.loadBrandsAndResolve();
      } else {
        this.loadBrands();
        this.resetForm();
      }
    }
  }

  /**
   * Pull the centralised fuel-types list from
   * <c>/api/fuelprices/types</c> so the dropdown reflects whatever the
   * Paramètres → Carburant page has configured. Errors are non-blocking:
   * the hardcoded fallback declared on the class instance stays in place.
   */
  private loadFuelTypes(): void {
    this.apiService.getFuelTypes().subscribe({
      next: (types) => {
        if (Array.isArray(types) && types.length > 0) {
          this.fuelTypes = types.map(t => ({ code: t.code, name: t.name }));
          this.cdr.detectChanges();
        }
      },
      error: () => {
        // Keep the hardcoded fallback; the dropdown stays usable.
      }
    });
  }

  /**
   * Spread the input vehicle into formData while normalising fields:
   * - Date columns arrive as ISO strings ("2024-03-15T00:00:00Z"); HTML
   *   <input type="date"> needs the bare YYYY-MM-DD slice.
   * - acquisitionType defaults to 'purchase' when empty / unknown.
   */
  private hydrateFormFromVehicle(v: any): void {
    const acq = v.acquisitionType === 'leasing' ? 'leasing' : 'purchase';
    this.formData = {
      ...v,
      acquisitionType: acq,
      purchasePrice: v.purchasePrice ?? null,
      purchaseDate: v.purchaseDate ? String(v.purchaseDate).substring(0, 10) : '',
      leasingMonthlyPayment: v.leasingMonthlyPayment ?? null,
      leasingDurationMonths: v.leasingDurationMonths ?? null,
      leasingStartDate: v.leasingStartDate ? String(v.leasingStartDate).substring(0, 10) : '',
      leasingPaymentDay: v.leasingPaymentDay ?? null,
      registrationDate: v.registrationDate ? String(v.registrationDate).substring(0, 10) : ''
    };
  }

  /**
   * Build the leasing payment schedule between the start date and the
   * configured number of months. Past payments are flagged so the table
   * can render them dimmed; the current month is highlighted.
   */
  getPaymentSchedule(): { index: number; date: string; isPast: boolean; isCurrent: boolean }[] {
    const duration = this.formData.leasingDurationMonths;
    const startStr = this.formData.leasingStartDate;
    const payDay = this.formData.leasingPaymentDay || 1;
    if (!duration || !startStr) return [];

    const start = new Date(startStr + 'T00:00:00');
    if (isNaN(start.getTime())) return [];
    const now = new Date();
    const out: { index: number; date: string; isPast: boolean; isCurrent: boolean }[] = [];

    for (let i = 0; i < duration; i++) {
      // clamp to 28 to avoid month overflow on Feb / 30-day months.
      const d = new Date(start.getFullYear(), start.getMonth() + i, Math.min(payDay, 28));
      const next = new Date(start.getFullYear(), start.getMonth() + i + 1, Math.min(payDay, 28));
      out.push({
        index: i + 1,
        date: d.toLocaleDateString('fr-FR', { year: 'numeric', month: 'short', day: 'numeric' }),
        isPast: d < now && next <= now,
        isCurrent: d <= now && next > now
      });
    }
    return out;
  }

  private loadBrandsAndResolve() {
    this.http.get<Brand[]>('/api/brands').subscribe({
      next: (brands) => {
        this.brands = brands;
        // If we have a brand name but no brandId, find the matching brand
        if (this.formData.brand && !this.formData.brandId) {
          const matchingBrand = brands.find(b => 
            b.name.toLowerCase() === this.formData.brand?.toLowerCase()
          );
          if (matchingBrand) {
            this.formData.brandId = matchingBrand.id;
            this.loadModels(matchingBrand.id);
          }
        } else if (this.formData.brandId) {
          this.loadModels(this.formData.brandId);
        }
        this.cdr.detectChanges();
      },
      error: () => {
        this.brands = [];
      }
    });
  }

  loadBrands() {
    this.http.get<Brand[]>('/api/brands').subscribe({
      next: (brands) => {
        this.brands = brands;
        this.cdr.detectChanges();
      },
      error: () => {
        this.brands = [];
      }
    });
  }

  loadModels(brandId: number, resolveModelName?: string) {
    this.loadingModels = true;
    this.http.get<VehicleModel[]>(`/api/brands/${brandId}/models`).subscribe({
      next: (models) => {
        this.models = models;
        this.loadingModels = false;
        // If we have a model name to resolve, find the matching model
        const modelNameToFind = resolveModelName || this.formData.model;
        if (modelNameToFind && !this.formData.modelId) {
          const matchingModel = models.find(m => 
            m.name.toLowerCase() === modelNameToFind.toLowerCase()
          );
          if (matchingModel) {
            this.formData.modelId = matchingModel.id;
          }
        }
        this.cdr.detectChanges();
      },
      error: () => {
        this.models = [];
        this.loadingModels = false;
      }
    });
  }

  onBrandChange(brandId: number) {
    this.formData.modelId = null;
    this.formData.model = '';
    this.models = [];
    
    if (brandId) {
      const selectedBrand = this.brands.find(b => b.id === Number(brandId));
      if (selectedBrand) {
        this.formData.brand = selectedBrand.name;
      }
      this.loadModels(brandId);
    }
  }

  onModelChange(modelId: number) {
    if (modelId) {
      const selectedModel = this.models.find(m => m.id === Number(modelId));
      if (selectedModel) {
        this.formData.model = selectedModel.name;
        if (selectedModel.vehicleType) {
          this.formData.type = selectedModel.vehicleType;
        }
      }
    }
  }

  loadAvailableDevices() {
    this.apiService.getAvailableGpsDevices().subscribe({
      next: (devices) => {
        this.availableGpsDevices = devices;
        if (devices.length === 0) {
          this.gpsMode = 'new';
        }
      },
      error: (err) => {
        console.error('Error loading GPS devices:', err);
        this.availableGpsDevices = [];
      }
    });
  }

  onHasGpsChange() {
    if (this.formData.hasGPS) {
      this.loadAvailableDevices();
    } else {
      this.formData.gpsDeviceId = undefined;
      this.formData.gpsImei = '';
      this.formData.gpsSimNumber = '';
      this.formData.gpsSimOperator = undefined;
      this.formData.gpsBrand = '';
      this.formData.gpsModel = '';
      this.formData.gpsInstallationDate = undefined;
      this.formData.gpsMat = '';
      this.formData.gpsFuelSensorMode = 'raw_255';
    }
  }

  onGpsBrandChange(brand: string) {
    this.formData.gpsModel = '';
    if (brand === 'NORON') {
      this.formData.gpsModel = 'NR024';
      this.formData.gpsFuelSensorMode = 'raw_255';
    }
  }

  onGpsModeChange() {
    if (this.gpsMode === 'existing') {
      this.formData.gpsImei = '';
      this.formData.gpsSimNumber = '';
      this.formData.gpsSimOperator = undefined;
      this.formData.gpsBrand = '';
      this.formData.gpsModel = '';
      this.formData.gpsMat = '';
      this.formData.gpsFuelSensorMode = 'raw_255';
    } else {
      this.formData.gpsDeviceId = undefined;
    }
  }

  onExistingDeviceSelected() {
    // When selecting an existing device, copy its IMEI, Mat and fuel sensor mode to formData
    // so they are sent to the backend and not lost
    if (this.formData.gpsDeviceId) {
      const selectedDevice = this.availableGpsDevices.find(
        d => d.id === Number(this.formData.gpsDeviceId)
      );
      if (selectedDevice) {
        this.formData.gpsImei = selectedDevice.deviceUid || '';
        this.formData.gpsMat = selectedDevice.mat || '';
        this.formData.gpsFuelSensorMode = selectedDevice.fuelSensorMode || 'raw_255';
        this.formData.gpsSimNumber = selectedDevice.simNumber || '';
        this.formData.gpsSimOperator = selectedDevice.simOperator || '';
      }
    } else {
      this.formData.gpsImei = '';
      this.formData.gpsMat = '';
      this.formData.gpsFuelSensorMode = 'raw_255';
      this.formData.gpsSimNumber = '';
      this.formData.gpsSimOperator = '';
    }
  }

  formatDate(date: string | Date): string {
    if (!date) return '';
    const d = new Date(date);
    return d.toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
  }

  onOverlayClick(event: MouseEvent) {
    if (event.target === event.currentTarget) {
      this.close();
    }
  }

  close() {
    this.closed.emit();
  }

  resetForm() {
    this.formData = {
      name: '',
      plate: '',
      brand: '',
      model: '',
      brandId: null,
      modelId: null,
      year: new Date().getFullYear(),
      type: 'citadine',
      status: 'available',
      mileage: 0,
      color: '',
      fuelType: 'diesel',
      companyId: this.defaultCompanyId,
      hasGPS: false,
      gpsDeviceId: undefined,
      gpsImei: '',
      gpsSimNumber: '',
      gpsSimOperator: undefined,
      gpsBrand: '',
      gpsModel: '',
      gpsInstallationDate: undefined,
      gpsMat: '',
      gpsFuelSensorMode: 'raw_255',
      // Acquisition (Calypso 7).
      acquisitionType: 'purchase',
      purchaseDate: '',
      purchasePrice: null,
      leasingMonthlyPayment: null,
      leasingDurationMonths: null,
      leasingStartDate: '',
      leasingPaymentDay: null,
      registrationDate: ''
    };
    this.gpsMode = 'existing';
  }

  onSubmit() {
    // Calypso 7: when the popup runs in non-admin context the GPS section
    // isn't rendered, so the local formData GPS fields might still hold
    // their initial blank values. We MUST NOT emit those — the parent
    // would PUT them back to the backend and wipe the legitimate GPS
    // assignment configured by a sysadmin earlier. Carry the original
    // vehicle's GPS columns over instead.
    const payload: any = { ...this.formData };
    if (!this.isSystemAdminContext && this.vehicle) {
      payload.hasGPS = this.vehicle.hasGPS;
      payload.gpsDeviceId = this.vehicle.gpsDeviceId;
      payload.gpsImei = (this.vehicle as any).gpsImei;
      payload.gpsSimNumber = this.vehicle.gpsSimNumber;
      payload.gpsSimOperator = this.vehicle.gpsSimOperator;
      payload.gpsBrand = this.vehicle.gpsBrand;
      payload.gpsModel = this.vehicle.gpsModel;
      payload.gpsInstallationDate = this.vehicle.gpsInstallationDate;
      payload.gpsMat = (this.vehicle as any).gpsMat;
      payload.gpsFuelSensorMode = (this.vehicle as any).gpsFuelSensorMode;
    }
    this.saved.emit(payload);
  }
}
