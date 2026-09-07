import { Component, EventEmitter, Input, Output, OnInit, OnChanges, SimpleChanges, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Vehicle } from '../../models/types';
import { ApiService, AcquisitionPaymentDto } from '../../services/api.service';
import { UserPreferencesService } from '../../services/user-preferences.service';
import { USER_PREF_PIPES } from '../../pipes/user-preference-pipes';
import { trigger, transition, style, animate } from '@angular/animations';
import { environment } from '../../environments/environment';

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
  imports: [CommonModule, FormsModule, ...USER_PREF_PIPES],
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
                <div class="form-group">
                  <label for="registrationDate">Date de mise en circulation</label>
                  <input type="date" id="registrationDate" name="registrationDate" [(ngModel)]="formData.registrationDate" />
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
                  <option *ngFor="let op of simOperators" [value]="op.value">{{ op.label }}</option>
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
                  <option *ngFor="let op of simOperators" [value]="op.value">{{ op.label }}</option>
                  <option value="other">Autre</option>
                </select>
              </div>

              <div class="form-group">
                <label for="gpsBrand">Marque GPS</label>
                <select id="gpsBrand" name="gpsBrand" [(ngModel)]="formData.gpsBrand" (ngModelChange)="onGpsBrandChange($event)">
                  <option value="">Sélectionner</option>
                  <option value="NEMS">NEMS</option>
                  <option value="NORON">Noron</option>
                  <option value="Teltonika">Teltonika</option>
                  <option value="Concox">Concox / GT06</option>
                  <option value="Coban">Coban / TK103</option>
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
                  <ng-container *ngIf="formData.gpsBrand === 'Teltonika'">
                    <option value="FMB120">FMB120</option>
                    <option value="FMB130">FMB130</option>
                    <option value="FMB150">FMB150</option>
                  </ng-container>
                  <ng-container *ngIf="formData.gpsBrand === 'Concox'">
                    <option value="GT06">GT06</option>
                    <option value="GT06N">GT06N</option>
                    <option value="TR06">TR06</option>
                    <option value="JM-VL01">JM-VL01</option>
                  </ng-container>
                  <ng-container *ngIf="formData.gpsBrand === 'Coban'">
                    <option value="TK103">TK103</option>
                    <option value="TK303">TK303</option>
                    <option value="TK104">TK104</option>
                  </ng-container>
                </select>
                <input *ngIf="formData.gpsBrand === 'Other'" type="text" id="gpsModel" name="gpsModel" [(ngModel)]="formData.gpsModel" placeholder="Ex: GT06N" />
              </div>

              <!-- Noron info note -->
              <div class="gps-info-note" *ngIf="formData.gpsBrand === 'NORON'">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4"/><path d="M12 8h.01"/></svg>
                <span>Ce tracker fournit uniquement la position GPS et la vitesse. Pas de données carburant, odomètre ou température.</span>
              </div>

              <!-- Teltonika info note -->
              <div class="gps-info-note" *ngIf="formData.gpsBrand === 'Teltonika'">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4"/><path d="M12 8h.01"/></svg>
                <span>Configurer le boîtier (Configurator ou SMS) vers le port TCP 6400, protocole Codec 8 ou 8 Extended. L'IMEI saisi ci-dessus doit correspondre à l'IMEI affiché sur l'étiquette du boîtier.</span>
              </div>

              <!-- Concox / GT06 info note -->
              <div class="gps-info-note" *ngIf="formData.gpsBrand === 'Concox'">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4"/><path d="M12 8h.01"/></svg>
                <span>Configurer le boîtier par SMS vers le port TCP 6500 (protocole binaire GT06). Fournit position, vitesse et alarmes basiques — pas de capteurs MEMS ni carburant.</span>
              </div>

              <!-- Coban / TK103 info note -->
              <div class="gps-info-note" *ngIf="formData.gpsBrand === 'Coban'">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4"/><path d="M12 8h.01"/></svg>
                <span>Configurer le boîtier par SMS vers le port TCP 6600 (protocole texte TK103). Fournit position et vitesse uniquement — pas de capteurs MEMS, carburant ou kilométrage.</span>
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

              <div class="form-group" *ngIf="formData.gpsBrand !== 'NORON' && formData.gpsBrand !== 'Teltonika' && formData.gpsBrand !== 'Concox' && formData.gpsBrand !== 'Coban'">
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
            </div>
            <!-- ↑ closes the admin-gated GPS .form-section. Without this
                 close, the Acquisition section below was nested INSIDE the
                 GPS section and disappeared whenever isSystemAdminContext
                 was false (i.e. for every company-level user). -->

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
                  <label for="purchasePrice">{{ formData.acquisitionType === 'leasing' ? 'Montant Auto-financement' : "Prix d'achat" }}</label>
                  <div class="input-with-suffix">
                    <input type="number" id="purchasePrice" name="purchasePrice" [(ngModel)]="formData.purchasePrice" min="0" placeholder="0.00" />
                    <span class="input-suffix">{{ currencyCode }}</span>
                  </div>
                </div>
              </div>

              <ng-container *ngIf="formData.acquisitionType === 'leasing'">
                <div class="form-row">
                  <div class="form-group">
                    <label for="leasingMonthlyPayment">Traite mensuelle</label>
                    <div class="input-with-suffix">
                      <input type="number" id="leasingMonthlyPayment" name="leasingMonthlyPayment" [(ngModel)]="formData.leasingMonthlyPayment" min="0" placeholder="0.00" />
                      <span class="input-suffix">{{ currencyCode }}/mois</span>
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

              </ng-container>

              <!-- Échéancier ENREGISTRÉ (table acquisition_payments, synchronisée par
                   le serveur depuis le contrat) : véhicule existant dont le contrat
                   n'a pas été modifié depuis l'ouverture. Actions par ligne hors
                   contexte admin. -->
              <div class="leasing-schedule" *ngIf="showServerSchedule">
                <label class="schedule-title">Échéancier des paiements</label>
                <div class="schedule-summary">
                  <span *ngIf="scheduleLoading">Chargement de l'échéancier…</span>
                  <span *ngIf="!scheduleLoading">
                    <strong>{{ schedulePaidCount }}/{{ scheduleTotalCount }}</strong> payées
                    · reste <strong>{{ scheduleRemaining | appCurrency }}</strong>
                    <ng-container *ngIf="scheduleSkippedCount > 0"> · {{ scheduleSkippedCount }} ignorée(s)</ng-container>
                  </span>
                </div>
                <div class="schedule-table-wrapper" *ngIf="!scheduleLoading">
                  <table class="schedule-table">
                    <thead>
                      <tr><th>N°</th><th>Date</th><th>Montant</th><th>Statut</th><th *ngIf="!isSystemAdminContext"></th></tr>
                    </thead>
                    <tbody>
                      <tr *ngFor="let p of schedule"
                          [class.paid]="p.status === 'paid' || (p.status === 'planned' && p.counted)"
                          [class.current]="p.id === currentPaymentId"
                          [class.skipped]="p.status === 'skipped'">
                        <td>{{ p.kind === 'mensualite' ? p.seq : (p.kind === 'apport' ? 'Apport' : 'Achat') }}</td>
                        <td>{{ formatDue(p.dueDate) }}</td>
                        <td>{{ (p.paidAmount ?? p.amount) | appCurrency }}</td>
                        <td><span class="payment-badge {{ paymentBadge(p).cls }}" [title]="p.note || ''">{{ paymentBadge(p).text }}</span></td>
                        <td class="schedule-actions" *ngIf="!isSystemAdminContext">
                          <button type="button" class="sched-btn ok" *ngIf="p.status !== 'paid'" (click)="openPayEdit(p)" title="Marquer payée…" [disabled]="paymentBusyId === p.id">
                            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
                          </button>
                          <button type="button" class="sched-btn undo" *ngIf="p.status !== 'planned'" (click)="setPaymentStatus(p, 'planned')" [title]="p.status === 'paid' ? 'Annuler le paiement' : 'Rétablir cette échéance'" [disabled]="paymentBusyId === p.id">
                            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><polyline points="1 4 1 10 7 10"/><path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10"/></svg>
                          </button>
                          <button type="button" class="sched-btn skip" *ngIf="p.status !== 'skipped'" (click)="setPaymentStatus(p, 'skipped')" title="Ignorer cette échéance" [disabled]="paymentBusyId === p.id">
                            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
                          </button>
                          <a *ngIf="p.receiptUrl" class="sched-btn clip has-receipt" [href]="p.receiptUrl" target="_blank" title="Voir la quittance">
                            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M21.44 11.05l-9.19 9.19a6 6 0 0 1-8.49-8.49l9.19-9.19a4 4 0 0 1 5.66 5.66l-9.2 9.19a2 2 0 0 1-2.83-2.83l8.49-8.48"/></svg>
                          </a>
                          <button type="button" class="sched-btn clip" (click)="receiptTargetId = p.id; scheduleReceiptInput.click()" [title]="p.receiptUrl ? 'Remplacer la quittance' : 'Joindre une quittance'" [disabled]="uploadingReceiptId === p.id">
                            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M21.44 11.05l-9.19 9.19a6 6 0 0 1-8.49-8.49l9.19-9.19a4 4 0 0 1 5.66 5.66l-9.2 9.19a2 2 0 0 1-2.83-2.83l8.49-8.48"/></svg>
                          </button>
                        </td>
                      </tr>
                    </tbody>
                  </table>
                </div>
                <input #scheduleReceiptInput type="file" accept="image/*,application/pdf" hidden (change)="onScheduleReceiptFile($event)">
              </div>
              <p class="schedule-error" *ngIf="scheduleError">{{ scheduleError }}</p>

              <!-- APERÇU calculé (nouveau véhicule, contrat modifié non enregistré,
                   ou contexte admin sans accès à l'échéancier client) : purement
                   calendaire, remplacé par l'échéancier définitif après enregistrement. -->
              <div class="leasing-schedule preview"
                   *ngIf="!showServerSchedule && formData.acquisitionType === 'leasing' && formData.leasingStartDate && formData.leasingDurationMonths && formData.leasingMonthlyPayment">
                <label class="schedule-title">Aperçu de l'échéancier</label>
                <div class="schedule-summary">
                  <span>{{ formData.leasingDurationMonths }} mois × {{ formData.leasingMonthlyPayment | appCurrency }}
                    = <strong>{{ (formData.leasingDurationMonths * formData.leasingMonthlyPayment) | appCurrency }}</strong>
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
                        <td>{{ formData.leasingMonthlyPayment | appCurrency }}</td>
                        <td>
                          <span class="payment-badge paid" *ngIf="payment.isPast">Payé</span>
                          <span class="payment-badge current" *ngIf="payment.isCurrent">En cours</span>
                          <span class="payment-badge upcoming" *ngIf="!payment.isPast && !payment.isCurrent">À venir</span>
                        </td>
                      </tr>
                    </tbody>
                  </table>
                </div>
                <p class="schedule-note">
                  {{ isSystemAdminContext && vehicle?.id
                     ? 'Aperçu calculé — le suivi des paiements se fait depuis l’espace client.'
                     : 'Échéancier définitif après enregistrement.' }}
                </p>
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

      <!-- Mini-modale « Marquer payée » (échéance d'acquisition) — frère du
           panneau, au-dessus de lui ; mousedown stoppé pour ne pas fermer le popup. -->
      <div class="pay-overlay" *ngIf="payEdit.open" (mousedown)="$event.stopPropagation()" (click)="closePayEdit()">
        <div class="pay-modal" (click)="$event.stopPropagation()">
          <div class="pay-head">
            <h4>Marquer l'échéance payée</h4>
            <button type="button" class="pay-close" (click)="closePayEdit()" title="Fermer">×</button>
          </div>
          <div class="pay-body" *ngIf="payEdit.payment as p">
            <p class="pay-sub">{{ paymentLabel(p) }} · échéance le {{ formatDue(p.dueDate) }}</p>
            <label for="payDate">Date de paiement</label>
            <input id="payDate" type="date" [(ngModel)]="payEdit.date" [disabled]="payEdit.saving">
            <label for="payAmount">Montant payé <span class="pay-req">*</span></label>
            <div class="input-with-suffix">
              <input id="payAmount" type="number" step="0.001" min="0" [(ngModel)]="payEdit.amount" [disabled]="payEdit.saving" placeholder="0.000">
              <span class="input-suffix">{{ currencyCode }}</span>
            </div>
            <p class="pay-hint" *ngIf="!hasPayAmount()">Saisissez le montant réglé.</p>
            <p class="pay-hint" *ngIf="hasPayAmount() && payAmountDiffers()">Montant prévu au contrat : {{ p.amount | appCurrency }}</p>
            <label for="payNote">Note</label>
            <textarea id="payNote" rows="2" maxlength="500" [(ngModel)]="payEdit.note" [disabled]="payEdit.saving" placeholder="Référence de virement, remarque…"></textarea>
            <p class="pay-error" *ngIf="payEdit.error">{{ payEdit.error }}</p>
          </div>
          <div class="pay-foot">
            <button type="button" class="btn-secondary" (click)="closePayEdit()" [disabled]="payEdit.saving">Annuler</button>
            <button type="button" class="btn-primary" (click)="savePayEdit()" [disabled]="!canSavePayEdit()">
              {{ payEdit.saving ? 'Enregistrement…' : 'Enregistrer' }}
            </button>
          </div>
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
    /* Lignes atténuées (payées / ignorées) : l'opacité est posée sur les
       cellules, pas sur le tr, pour que les boutons d'action restent nets. */
    .schedule-table tr.paid > td:not(.schedule-actions) { opacity: 0.6; }
    .schedule-table tr.current { background: #eff6ff; }
    .schedule-table tr.skipped > td:not(.schedule-actions) { opacity: 0.5; }
    .schedule-table tr.skipped td:nth-child(3) { text-decoration: line-through; }
    .payment-badge {
      display: inline-block;
      padding: 2px 8px;
      border-radius: 10px;
      font-size: 11px;
      font-weight: 500;
      white-space: nowrap;
    }
    .payment-badge.paid { background: #dcfce7; color: #166534; }
    .payment-badge.confirmed { background: #16a34a; color: #ffffff; }
    .payment-badge.current { background: #dbeafe; color: #1e40af; }
    .payment-badge.upcoming { background: #f1f5f9; color: #64748b; }
    .payment-badge.skipped { background: #e2e8f0; color: #475569; }
    /* Réservé au badge optionnel « En retard » (planifiée échue depuis > 7 j). */
    .payment-badge.late { background: #fef3c7; color: #92400e; }

    .leasing-schedule.preview { border-style: dashed; }
    .schedule-note {
      margin: 8px 0 0;
      font-size: 11px;
      color: #92400e;
      background: #fffbeb;
      border: 1px solid #fde68a;
      border-radius: 6px;
      padding: 6px 10px;
    }
    .schedule-error {
      margin: 8px 0 0;
      font-size: 11.5px;
      color: #b91c1c;
    }

    /* Actions par ligne (✓ payée, ↺ rétablir, ✕ ignorer, 📎 quittance) */
    .schedule-actions {
      display: flex;
      gap: 4px;
      justify-content: flex-end;
      white-space: nowrap;
    }
    .sched-btn {
      width: 24px;
      height: 24px;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      border: none;
      border-radius: 6px;
      cursor: pointer;
      background: #f1f5f9;
      color: #475569;
      text-decoration: none;
      transition: background .15s, color .15s;
    }
    .sched-btn:disabled { opacity: .5; cursor: wait; }
    .sched-btn.ok { background: #dcfce7; color: #166534; }
    .sched-btn.ok:hover:not(:disabled) { background: #bbf7d0; }
    .sched-btn.undo:hover:not(:disabled) { background: #e2e8f0; color: #0f172a; }
    .sched-btn.skip { background: #fee2e2; color: #b91c1c; }
    .sched-btn.skip:hover:not(:disabled) { background: #fecaca; }
    .sched-btn.clip:hover:not(:disabled) { background: #ede9fe; color: #6d28d9; }
    .sched-btn.clip.has-receipt { background: #ede9fe; color: #6d28d9; }

    /* Mini-modale « Marquer payée » */
    .pay-overlay {
      position: fixed;
      inset: 0;
      background: rgba(15, 23, 42, 0.45);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1090; /* au-dessus du panneau (1050), sous la navbar (1100) */
      padding: 20px;
    }
    .pay-modal {
      background: #fff;
      border-radius: 14px;
      width: 100%;
      max-width: 400px;
      box-shadow: 0 20px 60px rgba(0, 0, 0, 0.3);
      overflow: hidden;
    }
    .pay-head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 16px;
      padding: 14px 18px;
      border-bottom: 1px solid #e2e8f0;
    }
    .pay-head h4 { margin: 0; font-size: 15px; font-weight: 700; color: #0f172a; }
    .pay-close {
      background: #f1f5f9;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      width: 30px;
      height: 30px;
      font-size: 18px;
      line-height: 1;
      display: flex;
      align-items: center;
      justify-content: center;
      cursor: pointer;
      color: #475569;
    }
    .pay-close:hover { background: #e2e8f0; }
    .pay-body { padding: 16px 18px 4px; }
    .pay-sub { margin: 0 0 14px; font-size: 12.5px; color: #64748b; }
    .pay-body label {
      display: block;
      font-size: 11px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: .05em;
      color: #475569;
      margin: 10px 0 6px;
    }
    .pay-body input, .pay-body textarea {
      width: 100%;
      box-sizing: border-box;
      padding: 8px 10px;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      font-size: 13px;
      font-family: inherit;
    }
    .pay-body textarea { resize: vertical; }
    .pay-hint { margin: 6px 0 0; font-size: 11.5px; color: #b45309; }
    .pay-req { color: #dc2626; }
    .pay-error {
      margin: 10px 0 0;
      padding: 8px 10px;
      background: #fef2f2;
      border: 1px solid #fecaca;
      border-radius: 8px;
      font-size: 12px;
      color: #b91c1c;
    }
    .pay-foot {
      display: flex;
      justify-content: flex-end;
      gap: 8px;
      padding: 14px 18px;
      border-top: 1px solid #e2e8f0;
      background: #f8fafc;
    }

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
  // Per-deployment SIM operators (Tunisia by default; Algeria on the Bougeo
  // build) — sourced from environment.ts, never hardcoded in the template.
  readonly simOperators: { value: string; label: string }[] =
    (environment as any).simOperators ?? [];

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

  // ── Échéancier ENREGISTRÉ (acquisition_payments) d'un véhicule existant ──
  /** Lignes serveur, triées apport/achat d'abord puis mensualités 1..N. */
  schedule: AcquisitionPaymentDto[] = [];
  scheduleLoading = false;
  scheduleError = '';
  /** « En cours » = première mensualité planifiée non échue. */
  currentPaymentId: number | null = null;
  schedulePaidCount = 0;
  scheduleTotalCount = 0;
  scheduleSkippedCount = 0;
  scheduleRemaining = 0;
  /** Empreinte des 7 champs du contrat à l'ouverture : si elle change, l'échéancier
   *  serveur ne reflète plus le formulaire → on repasse sur l'aperçu calculé. */
  private contractSnapshot = '';
  paymentBusyId: number | null = null;
  uploadingReceiptId: number | null = null;
  receiptTargetId: number | null = null;
  payEdit = {
    open: false,
    payment: null as AcquisitionPaymentDto | null,
    date: '',
    amount: 0,
    note: '',
    saving: false,
    error: ''
  };

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

  constructor(private apiService: ApiService, private http: HttpClient, private cdr: ChangeDetectorRef, private userPrefs: UserPreferencesService) {}

  /** Active currency code for input adornments (e.g. "TND", "EUR"). */
  get currencyCode(): string { return this.userPrefs.current.currency; }

  ngOnInit() {
    this.loadBrands();
    this.loadFuelTypes();
    if (this.isSystemAdminContext) {
      this.loadAvailableDevices();
    }
    if (this.vehicle) {
      this.hydrateFormFromVehicle(this.vehicle);
      this.contractSnapshot = this.contractKey();
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
      this.resetScheduleState();
      if (this.vehicle) {
        this.hydrateFormFromVehicle(this.vehicle);
        this.contractSnapshot = this.contractKey();
        this.gpsMode = this.vehicle.gpsDeviceId ? 'existing' : 'new';
        // Load brands first, then resolve brandId from brand name if needed
        this.loadBrandsAndResolve();
        // Échéancier enregistré : uniquement dans l'app client. L'espace admin
        // n'a pas de jeton client (l'intercepteur n'emprunte jamais admin_token
        // sur une route client) → l'appel partirait sans jeton ; il garde
        // l'aperçu calculé en lecture seule.
        const vehicleId = parseInt(String(this.vehicle.id));
        if (!this.isSystemAdminContext && !isNaN(vehicleId)) {
          this.loadSchedule(vehicleId);
        }
      } else {
        this.loadBrands();
        this.resetForm();
        this.contractSnapshot = '';
      }
    } else if (changes['isOpen'] && !changes['isOpen'].currentValue) {
      this.resetScheduleState();
    }
  }

  // ── Échéancier enregistré ──────────────────────────────────────────────────

  private resetScheduleState(): void {
    this.schedule = [];
    this.scheduleLoading = false;
    this.scheduleError = '';
    this.currentPaymentId = null;
    this.schedulePaidCount = this.scheduleTotalCount = this.scheduleSkippedCount = 0;
    this.scheduleRemaining = 0;
    this.paymentBusyId = null;
    this.uploadingReceiptId = null;
    this.receiptTargetId = null;
    this.payEdit = { open: false, payment: null, date: '', amount: 0, note: '', saving: false, error: '' };
  }

  /** Empreinte normalisée des champs d'acquisition (nombres et chaînes comparés en texte). */
  private contractKey(): string {
    const f = this.formData || {};
    const norm = (v: any) => (v === null || v === undefined || v === '') ? '' : String(v);
    return [
      f.acquisitionType === 'leasing' ? 'leasing' : 'purchase',
      norm(f.purchaseDate), norm(f.purchasePrice),
      norm(f.leasingMonthlyPayment), norm(f.leasingDurationMonths),
      norm(f.leasingStartDate), norm(f.leasingPaymentDay)
    ].join('|');
  }

  /** Le contrat a été modifié dans le formulaire depuis l'ouverture (non enregistré). */
  get contractDirty(): boolean {
    return !!this.vehicle?.id && this.contractSnapshot !== this.contractKey();
  }

  /**
   * Échéancier serveur affiché : véhicule existant, app client, contrat
   * inchangé, et au moins une ligne (ou chargement en cours). Sinon l'aperçu
   * calendaire prend le relais.
   */
  get showServerSchedule(): boolean {
    if (!this.vehicle?.id || this.isSystemAdminContext || this.contractDirty) return false;
    return this.scheduleLoading || this.schedule.length > 0;
  }

  private loadSchedule(vehicleId: number, silent = false): void {
    // Verrou de sécurité : /api/acquisition-payments est une route CLIENT.
    // Ouvert depuis l'espace admin (jeton admin_token), l'appel partirait sans
    // jeton — ou pire, avec une session client restée dans le navigateur, donc
    // sur l'échéancier d'une autre société.
    if (this.isSystemAdminContext || !localStorage.getItem('auth_token')) return;
    if (!silent) this.scheduleLoading = true;
    this.scheduleError = '';
    this.apiService.getAcquisitionPayments({ vehicleId, includeFuture: true }).subscribe({
      next: (rows) => {
        // Le popup a pu changer de véhicule entre-temps.
        if (parseInt(String(this.vehicle?.id)) !== vehicleId) return;
        this.schedule = (rows || []).slice().sort((a, b) => {
          const ka = a.kind === 'mensualite' ? 1 : 0;
          const kb = b.kind === 'mensualite' ? 1 : 0;
          return ka !== kb ? ka - kb : a.seq - b.seq;
        });
        this.scheduleLoading = false;
        this.recomputeScheduleStats();
        this.cdr.detectChanges();
      },
      error: (err) => {
        console.error('Error loading acquisition payments:', err);
        this.schedule = [];
        this.scheduleLoading = false;
        this.scheduleError = err?.status === 404
          ? ''   // endpoint absent (déploiement partiel) → aperçu calculé, sans bruit
          : (err?.error?.message || "L'échéancier enregistré n'a pas pu être chargé — aperçu calculé affiché.");
        this.recomputeScheduleStats();
        this.cdr.detectChanges();
      }
    });
  }

  private recomputeScheduleStats(): void {
    const rows = this.schedule;
    const isPaidLike = (p: AcquisitionPaymentDto) => p.status === 'paid' || (p.status === 'planned' && p.counted);
    this.scheduleSkippedCount = rows.filter(p => p.status === 'skipped').length;
    this.scheduleTotalCount = rows.length - this.scheduleSkippedCount;
    this.schedulePaidCount = rows.filter(isPaidLike).length;
    this.scheduleRemaining = rows
      .filter(p => p.status === 'planned' && !p.counted)
      .reduce((s, p) => s + (Number(p.amount) || 0), 0);
    this.currentPaymentId = rows.find(p => p.status === 'planned' && !p.counted)?.id ?? null;
  }

  formatDue(dueDate: string): string {
    if (!dueDate) return '';
    const d = new Date(String(dueDate).slice(0, 10) + 'T00:00:00');
    return isNaN(d.getTime()) ? '' : d.toLocaleDateString('fr-FR', { year: 'numeric', month: 'short', day: 'numeric' });
  }

  private shortDate(iso: string | null | undefined): string {
    if (!iso) return '';
    const d = new Date(iso);
    return isNaN(d.getTime()) ? '' : d.toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit' });
  }

  paymentLabel(p: AcquisitionPaymentDto): string {
    switch (p.kind) {
      case 'mensualite': return `Mensualité ${p.seq}/${p.total || p.seq}`;
      case 'apport': return 'Apport';
      default: return 'Achat véhicule';
    }
  }

  /**
   * Statuts : « Payée le JJ/MM » (confirmée), « Payée (auto) » (planifiée
   * échue — présomption calendaire, comptée en dépense), « En cours »
   * (prochaine à payer), « À venir », « Ignorée ».
   */
  paymentBadge(p: AcquisitionPaymentDto): { cls: string; text: string } {
    if (p.status === 'paid') {
      const when = this.shortDate(p.paidAt);
      return { cls: 'confirmed', text: when ? `Payée le ${when}` : 'Payée' };
    }
    if (p.status === 'skipped') return { cls: 'skipped', text: 'Ignorée' };
    if (p.counted) return { cls: 'paid', text: 'Payée (auto)' };
    if (p.id === this.currentPaymentId) return { cls: 'current', text: 'En cours' };
    return { cls: 'upcoming', text: 'À venir' };
  }

  /** Jour LOCAL : toISOString() donnerait la veille entre minuit et 1 h en TN/DZ. */
  private todayLocal(): string {
    const d = new Date();
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  }

  openPayEdit(p: AcquisitionPaymentDto): void {
    const today = this.todayLocal();
    const due = String(p.dueDate || '').slice(0, 10);
    this.payEdit = {
      open: true,
      payment: p,
      date: due && due <= today ? due : today,
      amount: Number(p.paidAmount ?? p.amount) || 0,
      note: p.note || '',
      saving: false,
      error: ''
    };
  }

  closePayEdit(): void {
    if (this.payEdit.saving) return;
    this.payEdit = { ...this.payEdit, open: false, payment: null, error: '' };
  }

  /**
   * Un champ montant vidé met `amount` à null, et `Number(null)` vaut 0 : sans ce
   * contrôle on enregistrerait un paiement de 0 et l'échéance sortirait du coût.
   */
  hasPayAmount(): boolean {
    const a = this.payEdit.amount as unknown;
    if (a === null || a === undefined || String(a).trim() === '') return false;
    const n = Number(a);
    return !isNaN(n) && n >= 0;
  }

  canSavePayEdit(): boolean {
    return !!this.payEdit.payment && !!this.payEdit.date && this.hasPayAmount() && !this.payEdit.saving;
  }

  payAmountDiffers(): boolean {
    const p = this.payEdit.payment;
    return !!p && Math.abs(Number(this.payEdit.amount) - Number(p.amount)) > 0.001;
  }

  savePayEdit(): void {
    const p = this.payEdit.payment;
    if (!this.canSavePayEdit() || !p) return;
    this.payEdit.saving = true;
    this.payEdit.error = '';
    // Midi local : la date calendaire choisie survit à la conversion UTC.
    const paidAt = new Date(this.payEdit.date + 'T12:00:00').toISOString();
    const note = (this.payEdit.note || '').trim();
    this.apiService.updateAcquisitionPayment(p.id, {
      status: 'paid', paidAt,
      // Montant omis si rien n'est saisi (le serveur retombe sur le montant prévu) ;
      // note en chaîne vide = effacée, null signifierait « ne pas y toucher ».
      ...(this.hasPayAmount() ? { paidAmount: Number(this.payEdit.amount) } : {}),
      note: note.slice(0, 500)
    }).subscribe({
      next: (updated) => {
        this.payEdit = { ...this.payEdit, open: false, payment: null, saving: false };
        this.applyUpdatedPayment(p.id, updated);
      },
      error: (err) => {
        this.payEdit.saving = false;
        this.payEdit.error = err?.error?.message || "L'enregistrement du paiement a échoué. Réessayez.";
        this.cdr.detectChanges();
      }
    });
  }

  setPaymentStatus(p: AcquisitionPaymentDto, status: 'planned' | 'skipped'): void {
    if (this.paymentBusyId) return;
    this.paymentBusyId = p.id;
    this.apiService.updateAcquisitionPayment(p.id, { status }).subscribe({
      next: (updated) => {
        this.paymentBusyId = null;
        this.applyUpdatedPayment(p.id, updated);
      },
      error: (err) => {
        this.paymentBusyId = null;
        console.error('Error updating acquisition payment:', err);
        alert(err?.error?.message || "La mise à jour de l'échéance a échoué.");
        this.cdr.detectChanges();
      }
    });
  }

  /** Remplace la ligne par le DTO renvoyé puis recharge en silence (counted/overdue à jour). */
  private applyUpdatedPayment(id: number, updated: AcquisitionPaymentDto | null | undefined): void {
    if (updated && updated.id === id) {
      this.schedule = this.schedule.map(row => row.id === id ? { ...row, ...updated } : row);
      this.recomputeScheduleStats();
    }
    this.cdr.detectChanges();
    const vehicleId = parseInt(String(this.vehicle?.id));
    if (!isNaN(vehicleId)) this.loadSchedule(vehicleId, true);
  }

  onScheduleReceiptFile(event: any): void {
    const file: File | undefined = event?.target?.files?.[0];
    if (event?.target) event.target.value = '';
    const id = this.receiptTargetId;
    this.receiptTargetId = null;
    // Un refus précédent ne doit pas rester affiché sous un envoi qui réussit.
    this.scheduleError = '';
    if (!file || !id) return;
    // Le serveur plafonne à 12 Mo : autant refuser tout de suite plutôt que de
    // laisser partir une photo de téléphone entière pour finir en 413.
    if (file.size > 12_000_000) {
      this.scheduleError = 'Quittance trop lourde (12 Mo maximum). Réduisez la photo avant de la joindre.';
      this.cdr.detectChanges();
      return;
    }
    this.uploadingReceiptId = id;
    this.cdr.detectChanges();
    this.apiService.uploadAcquisitionPaymentReceipt(id, file).subscribe({
      next: (res) => {
        this.uploadingReceiptId = null;
        const row = this.schedule.find(r => r.id === id);
        if (row && res?.receiptUrl) row.receiptUrl = res.receiptUrl;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.uploadingReceiptId = null;
        const msg = err?.status === 413
          ? 'Fichier trop volumineux (maximum 12 Mo). Réduisez la taille ou envoyez une photo compressée.'
          : (err?.error?.message || "L'envoi de la quittance a échoué.");
        alert(msg);
        this.cdr.detectChanges();
      }
    });
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
   * APERÇU calendaire de l'échéancier — utilisé seulement pour un véhicule
   * pas encore créé, un contrat modifié non enregistré, ou le contexte admin.
   * L'échéancier de référence est celui du serveur (table acquisition_payments,
   * `schedule`), chargé à l'ouverture d'un véhicule existant ; il porte les
   * paiements réels (date, montant, quittance, échéances ignorées).
   *
   * Statuts de l'aperçu (recette client 01/09/2026) : une mensualité est
   * réputée « Payé » DÈS QUE sa date est atteinte — même règle que l'écran
   * Dépenses et que le serveur (`counted`). « En cours » = la prochaine
   * mensualité à payer ; le reste = « À venir ». Même calcul de dates que
   * AcquisitionSchedule.LeasingDues côté serveur (jour plafonné au 28,
   * 1re mensualité le mois suivant si le jour de paiement précède le début).
   */
  getPaymentSchedule(): { index: number; date: string; isPast: boolean; isCurrent: boolean }[] {
    const duration = this.formData.leasingDurationMonths;
    const startStr = this.formData.leasingStartDate;
    const payDay = this.formData.leasingPaymentDay || 1;
    if (!duration || !startStr) return [];

    const start = new Date(String(startStr).slice(0, 10) + 'T00:00:00');
    if (isNaN(start.getTime())) return [];
    const now = new Date();
    const out: { index: number; date: string; isPast: boolean; isCurrent: boolean }[] = [];

    // clamp to 28 to avoid month overflow on Feb / 30-day months.
    const day = Math.min(payDay, 28);
    // Si le jour de paiement du mois de départ précède la date de début du
    // contrat, la 1re mensualité tombe le mois suivant — un contrat signé le
    // 18/05 avec prélèvement le 14 ne génère pas d'échéance « payée » au
    // 14/05, antérieure au contrat. (Même règle dans expenses.component.ts.)
    const offset = day < start.getDate() ? 1 : 0;

    for (let i = 0; i < duration; i++) {
      const d = new Date(start.getFullYear(), start.getMonth() + i + offset, day);
      out.push({
        index: i + 1,
        date: d.toLocaleDateString('fr-FR', { year: 'numeric', month: 'short', day: 'numeric' }),
        isPast: d <= now,
        isCurrent: false
      });
    }
    // La prochaine mensualité à payer est « En cours ».
    const current = out.find(p => !p.isPast);
    if (current) current.isCurrent = true;
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
    } else if (brand === 'Teltonika') {
      // Default to FMB130 — the most common Teltonika tracker in the fleet.
      // Operators can switch to FMB120 (entry-level) or FMB150 (industrial
      // variant) in the version dropdown below. Fuel sensor mode is irrelevant:
      // FMB does not expose a canonical fuel-level IO, so we hide the field.
      this.formData.gpsModel = 'FMB130';
      this.formData.gpsFuelSensorMode = 'raw_255';
    } else if (brand === 'Concox') {
      // GT06 is the entry-level Concox tracker — what most cheap fleet
      // installs ship with. GT06N is the LTE-capable refresh.
      this.formData.gpsModel = 'GT06';
      this.formData.gpsFuelSensorMode = 'raw_255';
    } else if (brand === 'Coban') {
      // TK103 is the original Coban; TK303 / TK104 are minor variants
      // speaking the same wire protocol.
      this.formData.gpsModel = 'TK103';
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
