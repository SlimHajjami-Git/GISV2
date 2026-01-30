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
    <div class="panel-overlay" *ngIf="isOpen" @fadeIn (click)="onOverlayClick($event)">
      <div class="slide-panel" @slideIn (click)="$event.stopPropagation()">
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
            </div>

            <!-- GPS Section -->
            <div class="form-section">
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
                <select id="gpsBrand" name="gpsBrand" [(ngModel)]="formData.gpsBrand">
                  <option value="">Sélectionner</option>
                  <option value="NEMS">NEMS</option>
                  <option value="Other">Autre</option>
                </select>
              </div>

              <div class="form-group">
                <label for="gpsModel">Version GPS</label>
                <select id="gpsModel" name="gpsModel" [(ngModel)]="formData.gpsModel">
                  <option value="">Sélectionner</option>
                  <option value="S">NEMS S</option>
                  <option value="L">NEMS L</option>
                </select>
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

              <div class="form-group">
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
      inset: 0;
      background: rgba(0, 0, 0, 0.5);
      z-index: 1000;
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
  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<Partial<Vehicle>>();

  availableGpsDevices: any[] = [];
  gpsMode: 'existing' | 'new' = 'existing';
  
  brands: Brand[] = [];
  models: VehicleModel[] = [];
  fuelTypes = [
    { code: 'diesel', name: 'Diesel' },
    { code: 'essence', name: 'Essence' },
    { code: 'gpl', name: 'GPL' },
    { code: 'electrique', name: 'Électrique' },
    { code: 'hybride', name: 'Hybride' }
  ];
  loadingModels = false;

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
    gpsFuelSensorMode: 'raw_255'
  };

  constructor(private apiService: ApiService, private http: HttpClient, private cdr: ChangeDetectorRef) {}

  ngOnInit() {
    this.loadBrands();
    this.loadAvailableDevices();
    if (this.vehicle) {
      this.formData = { ...this.vehicle };
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
      this.loadAvailableDevices();
      if (this.vehicle) {
        this.formData = { ...this.vehicle };
        this.gpsMode = this.vehicle.gpsDeviceId ? 'existing' : 'new';
        // Load brands first, then resolve brandId from brand name if needed
        this.loadBrandsAndResolve();
      } else {
        this.loadBrands();
        this.resetForm();
      }
    }
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
      }
    } else {
      this.formData.gpsImei = '';
      this.formData.gpsMat = '';
      this.formData.gpsFuelSensorMode = 'raw_255';
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
      gpsFuelSensorMode: 'raw_255'
    };
    this.gpsMode = 'existing';
  }

  onSubmit() {
    this.saved.emit(this.formData);
  }
}
