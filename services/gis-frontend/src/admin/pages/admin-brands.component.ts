import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService } from '../services/admin.service';
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

interface BrandDetail {
  id: number;
  name: string;
  logoUrl?: string;
  isActive: boolean;
  models: VehicleModel[];
}

@Component({
  selector: 'admin-brands',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent],
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
    <admin-layout pageTitle="Marques & Modèles">
      <div class="brands-page">
        <!-- Header -->
        <div class="page-header">
          <div class="header-left">
            <div class="search-box">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
              </svg>
              <input type="text" [(ngModel)]="searchQuery" (input)="filterBrands()" placeholder="Rechercher une marque..." />
            </div>
          </div>
          <button class="add-btn" (click)="openBrandForm()">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
            </svg>
            Nouvelle marque
          </button>
        </div>

        <!-- Brands Grid -->
        <div class="brands-grid">
          <div class="brand-card" *ngFor="let brand of filteredBrands" (click)="selectBrand(brand)">
            <div class="brand-logo">
              <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                <circle cx="12" cy="12" r="10"/>
                <path d="M12 6v6l4 2"/>
              </svg>
            </div>
            <div class="brand-info">
              <h3>{{ brand.name }}</h3>
              <span class="model-count">{{ brand.modelCount }} modèle{{ brand.modelCount !== 1 ? 's' : '' }}</span>
            </div>
            <div class="brand-actions">
              <button class="action-btn edit" (click)="editBrand(brand); $event.stopPropagation()" title="Modifier">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                  <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                </svg>
              </button>
              <button class="action-btn delete" (click)="deleteBrand(brand); $event.stopPropagation()" title="Supprimer">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <polyline points="3,6 5,6 21,6"/>
                  <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                </svg>
              </button>
            </div>
          </div>
        </div>

        <!-- Empty State -->
        <div class="empty-state" *ngIf="filteredBrands.length === 0 && !loading">
          <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
            <circle cx="12" cy="12" r="10"/>
            <path d="M12 6v6l4 2"/>
          </svg>
          <h3>Aucune marque</h3>
          <p>Ajoutez des marques de véhicules pour commencer</p>
        </div>

        <!-- Brand Detail Panel (slide-in) -->
        <div class="detail-overlay" *ngIf="selectedBrand" @fadeIn (click)="closeBrandDetail()">
          <div class="detail-panel" @slideIn (click)="$event.stopPropagation()">
            <div class="panel-header">
              <div class="panel-header-content">
                <div class="brand-icon">🚗</div>
                <div class="panel-header-info">
                  <h2>{{ selectedBrand.name }}</h2>
                  <p>{{ selectedBrand.models.length }} modèle{{ selectedBrand.models.length !== 1 ? 's' : '' }}</p>
                </div>
              </div>
              <button class="btn-close-panel" (click)="closeBrandDetail()">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                </svg>
              </button>
            </div>

            <div class="panel-body">
              <!-- Add Model Section -->
              <div class="add-model-section">
                <h3>Ajouter un modèle</h3>
                <div class="add-model-form">
                  <input type="text" [(ngModel)]="newModelName" placeholder="Nom du modèle" />
                  <select [(ngModel)]="newModelType">
                    <option value="">Type</option>
                    <option value="citadine">Citadine</option>
                    <option value="suv">SUV</option>
                    <option value="utilitaire">Utilitaire</option>
                    <option value="camion">Camion</option>
                    <option value="other">Autre</option>
                  </select>
                  <button class="btn-add-model" (click)="addModel()" [disabled]="!newModelName">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
                    </svg>
                  </button>
                </div>
              </div>

              <!-- Models List -->
              <div class="models-section">
                <h3>Modèles</h3>
                <div class="models-list">
                  <div class="model-item" *ngFor="let model of selectedBrand.models">
                    <div class="model-info">
                      <span class="model-name">{{ model.name }}</span>
                      <span class="model-type" *ngIf="model.vehicleType">{{ model.vehicleType | titlecase }}</span>
                    </div>
                    <div class="model-actions">
                      <button class="action-btn small edit" (click)="editModel(model)" title="Modifier">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                          <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                        </svg>
                      </button>
                      <button class="action-btn small delete" (click)="deleteModel(model)" title="Supprimer">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <polyline points="3,6 5,6 21,6"/>
                          <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                        </svg>
                      </button>
                    </div>
                  </div>
                  <div class="empty-models" *ngIf="selectedBrand.models.length === 0">
                    <p>Aucun modèle pour cette marque</p>
                  </div>
                </div>
              </div>
            </div>

            <div class="panel-footer">
              <button class="btn-secondary" (click)="closeBrandDetail()">Fermer</button>
            </div>
          </div>
        </div>

        <!-- Brand Form Panel (slide-in) -->
        <div class="form-overlay" *ngIf="showBrandForm" @fadeIn (click)="closeBrandForm()">
          <div class="form-panel" @slideIn (click)="$event.stopPropagation()">
            <div class="form-header">
              <div class="form-header-content">
                <div class="form-icon">🏷️</div>
                <div class="form-header-info">
                  <h2>{{ editingBrand ? 'Modifier la marque' : 'Nouvelle marque' }}</h2>
                  <p>{{ editingBrand ? editingBrand.name : 'Ajoutez une nouvelle marque' }}</p>
                </div>
              </div>
              <button class="btn-close-panel" (click)="closeBrandForm()">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                </svg>
              </button>
            </div>

            <div class="form-body">
              <div class="form-group">
                <label>Nom de la marque *</label>
                <input type="text" [(ngModel)]="brandForm.name" placeholder="Ex: Peugeot" required />
              </div>
              <div class="form-group">
                <label>URL du logo</label>
                <input type="text" [(ngModel)]="brandForm.logoUrl" placeholder="https://..." />
              </div>
            </div>

            <div class="form-footer">
              <button class="btn-secondary" (click)="closeBrandForm()">Annuler</button>
              <button class="btn-primary" (click)="saveBrand()" [disabled]="!brandForm.name">
                {{ editingBrand ? 'Enregistrer' : 'Créer' }}
              </button>
            </div>
          </div>
        </div>

        <!-- Model Edit Modal -->
        <div class="modal-overlay" *ngIf="editingModel" (click)="closeModelEdit()">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h2>Modifier le modèle</h2>
              <button class="close-btn" (click)="closeModelEdit()">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                </svg>
              </button>
            </div>
            <div class="modal-body">
              <div class="form-group">
                <label>Nom du modèle *</label>
                <input type="text" [(ngModel)]="modelForm.name" placeholder="Ex: 208" required />
              </div>
              <div class="form-group">
                <label>Type de véhicule</label>
                <select [(ngModel)]="modelForm.vehicleType">
                  <option value="">Sélectionner</option>
                  <option value="citadine">Citadine</option>
                  <option value="suv">SUV</option>
                  <option value="utilitaire">Utilitaire</option>
                  <option value="camion">Camion</option>
                  <option value="other">Autre</option>
                </select>
              </div>
            </div>
            <div class="modal-footer">
              <button class="btn-secondary" (click)="closeModelEdit()">Annuler</button>
              <button class="btn-primary" (click)="saveModel()" [disabled]="!modelForm.name">Enregistrer</button>
            </div>
          </div>
        </div>
      </div>
    </admin-layout>
  `,
  styles: [`
    .brands-page {
      display: flex;
      flex-direction: column;
      gap: 24px;
    }

    .page-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      flex-wrap: wrap;
      gap: 16px;
    }

    .header-left {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .search-box {
      display: flex;
      align-items: center;
      gap: 10px;
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      padding: 10px 14px;
      width: 280px;
    }

    .search-box svg { color: #64748b; }

    .search-box input {
      flex: 1;
      border: none;
      background: transparent;
      color: #1f2937;
      font-size: 14px;
      outline: none;
    }

    .add-btn {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 20px;
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
      border: none;
      border-radius: 10px;
      color: #fff;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .add-btn:hover {
      transform: translateY(-2px);
      box-shadow: 0 4px 16px rgba(0, 212, 170, 0.3);
    }

    .brands-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
      gap: 16px;
    }

    .brand-card {
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 12px;
      padding: 20px;
      display: flex;
      align-items: center;
      gap: 16px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .brand-card:hover {
      border-color: #00d4aa;
      box-shadow: 0 4px 12px rgba(0, 212, 170, 0.15);
      transform: translateY(-2px);
    }

    .brand-logo {
      width: 56px;
      height: 56px;
      background: linear-gradient(135deg, #f0fdf9 0%, #e0f7f3 100%);
      border-radius: 12px;
      display: flex;
      align-items: center;
      justify-content: center;
      color: #00a388;
    }

    .brand-info {
      flex: 1;
    }

    .brand-info h3 {
      margin: 0 0 4px 0;
      font-size: 16px;
      font-weight: 600;
      color: #1f2937;
    }

    .model-count {
      font-size: 13px;
      color: #64748b;
    }

    .brand-actions {
      display: flex;
      gap: 8px;
    }

    .action-btn {
      width: 32px;
      height: 32px;
      border: none;
      border-radius: 8px;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }

    .action-btn.small {
      width: 28px;
      height: 28px;
    }

    .action-btn.edit {
      background: rgba(59, 130, 246, 0.15);
      color: #3b82f6;
    }

    .action-btn.delete {
      background: rgba(239, 68, 68, 0.15);
      color: #ef4444;
    }

    .action-btn:hover {
      transform: scale(1.1);
    }

    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 60px 20px;
      color: #64748b;
    }

    .empty-state svg {
      margin-bottom: 16px;
      opacity: 0.5;
    }

    .empty-state h3 {
      margin: 0 0 8px 0;
      font-size: 18px;
      color: #1f2937;
    }

    .empty-state p {
      margin: 0;
      font-size: 14px;
    }

    /* Slide-in panels */
    .detail-overlay, .form-overlay {
      position: fixed;
      inset: 0;
      background: rgba(0, 0, 0, 0.5);
      z-index: 1000;
      display: flex;
      justify-content: flex-end;
    }

    .detail-panel, .form-panel {
      width: 480px;
      max-width: 100%;
      height: 100%;
      background: #fff;
      display: flex;
      flex-direction: column;
      box-shadow: -4px 0 20px rgba(0, 0, 0, 0.15);
    }

    .panel-header, .form-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 20px 24px;
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
      color: #fff;
    }

    .panel-header-content, .form-header-content {
      display: flex;
      align-items: center;
      gap: 16px;
    }

    .brand-icon, .form-icon {
      font-size: 32px;
    }

    .panel-header-info h2, .form-header-info h2 {
      margin: 0;
      font-size: 18px;
      font-weight: 600;
    }

    .panel-header-info p, .form-header-info p {
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

    .panel-body, .form-body {
      flex: 1;
      overflow-y: auto;
      padding: 24px;
    }

    .add-model-section {
      background: #f8fafc;
      border-radius: 12px;
      padding: 16px;
      margin-bottom: 24px;
    }

    .add-model-section h3 {
      margin: 0 0 12px 0;
      font-size: 14px;
      font-weight: 600;
      color: #374151;
    }

    .add-model-form {
      display: flex;
      gap: 8px;
    }

    .add-model-form input, .add-model-form select {
      flex: 1;
      padding: 10px 12px;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      font-size: 14px;
      outline: none;
    }

    .add-model-form input:focus, .add-model-form select:focus {
      border-color: #00d4aa;
    }

    .btn-add-model {
      width: 40px;
      height: 40px;
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
      border: none;
      border-radius: 8px;
      color: #fff;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }

    .btn-add-model:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }

    .models-section h3 {
      margin: 0 0 16px 0;
      font-size: 14px;
      font-weight: 600;
      color: #374151;
    }

    .models-list {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .model-item {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 12px 16px;
      background: #fff;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      transition: all 0.2s;
    }

    .model-item:hover {
      border-color: #00d4aa;
    }

    .model-info {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .model-name {
      font-size: 14px;
      font-weight: 500;
      color: #1f2937;
    }

    .model-type {
      font-size: 12px;
      padding: 2px 8px;
      background: rgba(0, 212, 170, 0.1);
      color: #00a388;
      border-radius: 6px;
    }

    .model-actions {
      display: flex;
      gap: 6px;
    }

    .empty-models {
      padding: 24px;
      text-align: center;
      color: #64748b;
      font-size: 14px;
    }

    .panel-footer, .form-footer {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 16px 24px;
      border-top: 1px solid #e2e8f0;
    }

    .form-group {
      margin-bottom: 16px;
    }

    .form-group label {
      display: block;
      margin-bottom: 6px;
      font-size: 14px;
      font-weight: 500;
      color: #374151;
    }

    .form-group input, .form-group select {
      width: 100%;
      padding: 12px 14px;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      font-size: 14px;
      outline: none;
      transition: border-color 0.2s;
    }

    .form-group input:focus, .form-group select:focus {
      border-color: #00d4aa;
    }

    .btn-secondary {
      padding: 10px 20px;
      background: #f1f5f9;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      color: #1f2937;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-secondary:hover {
      background: #e2e8f0;
    }

    .btn-primary {
      padding: 10px 20px;
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
      border: none;
      border-radius: 10px;
      color: #fff;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-primary:hover {
      box-shadow: 0 4px 16px rgba(0, 212, 170, 0.3);
    }

    .btn-primary:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }

    /* Modal */
    .modal-overlay {
      position: fixed;
      inset: 0;
      background: rgba(0, 0, 0, 0.7);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1100;
    }

    .modal {
      background: white;
      border-radius: 16px;
      width: 400px;
      max-width: 90%;
      box-shadow: 0 24px 70px rgba(0, 0, 0, 0.3);
    }

    .modal-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 20px 24px;
      border-bottom: 1px solid #e2e8f0;
    }

    .modal-header h2 {
      margin: 0;
      font-size: 18px;
      font-weight: 600;
      color: #1f2937;
    }

    .close-btn {
      width: 36px;
      height: 36px;
      border: none;
      background: #f1f5f9;
      border-radius: 10px;
      color: #64748b;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .modal-body {
      padding: 24px;
    }

    .modal-footer {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 16px 24px;
      border-top: 1px solid #e2e8f0;
    }
  `]
})
export class AdminBrandsComponent implements OnInit {
  brands: Brand[] = [];
  filteredBrands: Brand[] = [];
  selectedBrand: BrandDetail | null = null;
  editingBrand: Brand | null = null;
  editingModel: VehicleModel | null = null;
  
  searchQuery = '';
  loading = false;
  showBrandForm = false;
  
  brandForm = { name: '', logoUrl: '' };
  modelForm = { name: '', vehicleType: '' };
  newModelName = '';
  newModelType = '';

  constructor(
    private router: Router,
    private http: HttpClient,
    private adminService: AdminService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit() {
    if (!this.adminService.isAuthenticated()) {
      this.router.navigate(['/admin/login']);
      return;
    }
    this.loadBrands();
  }

  loadBrands() {
    this.loading = true;
    this.http.get<Brand[]>('/api/brands').subscribe({
      next: (brands) => {
        this.brands = brands;
        this.filterBrands();
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.brands = [];
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  filterBrands() {
    if (!this.searchQuery) {
      this.filteredBrands = this.brands;
    } else {
      this.filteredBrands = this.brands.filter(b =>
        b.name.toLowerCase().includes(this.searchQuery.toLowerCase())
      );
    }
  }

  selectBrand(brand: Brand) {
    this.http.get<BrandDetail>(`/api/brands/${brand.id}`).subscribe({
      next: (detail) => {
        this.selectedBrand = detail;
        this.cdr.detectChanges();
      }
    });
  }

  closeBrandDetail() {
    this.selectedBrand = null;
    this.newModelName = '';
    this.newModelType = '';
  }

  openBrandForm() {
    this.editingBrand = null;
    this.brandForm = { name: '', logoUrl: '' };
    this.showBrandForm = true;
  }

  editBrand(brand: Brand) {
    this.editingBrand = brand;
    this.brandForm = { name: brand.name, logoUrl: brand.logoUrl || '' };
    this.showBrandForm = true;
  }

  closeBrandForm() {
    this.showBrandForm = false;
    this.editingBrand = null;
  }

  saveBrand() {
    if (!this.brandForm.name) return;

    if (this.editingBrand) {
      this.http.put(`/api/brands/${this.editingBrand.id}`, {
        name: this.brandForm.name,
        logoUrl: this.brandForm.logoUrl || null,
        isActive: true
      }).subscribe({
        next: () => {
          this.closeBrandForm();
          this.loadBrands();
        }
      });
    } else {
      this.http.post('/api/brands', {
        name: this.brandForm.name,
        logoUrl: this.brandForm.logoUrl || null
      }).subscribe({
        next: () => {
          this.closeBrandForm();
          this.loadBrands();
        }
      });
    }
  }

  deleteBrand(brand: Brand) {
    if (confirm(`Supprimer la marque "${brand.name}" et tous ses modèles ?`)) {
      this.http.delete(`/api/brands/${brand.id}`).subscribe({
        next: () => this.loadBrands()
      });
    }
  }

  addModel() {
    if (!this.selectedBrand || !this.newModelName) return;

    this.http.post(`/api/brands/${this.selectedBrand.id}/models`, {
      name: this.newModelName,
      vehicleType: this.newModelType || null
    }).subscribe({
      next: () => {
        this.newModelName = '';
        this.newModelType = '';
        this.selectBrand({ id: this.selectedBrand!.id, name: this.selectedBrand!.name, modelCount: 0 });
      }
    });
  }

  editModel(model: VehicleModel) {
    this.editingModel = model;
    this.modelForm = { name: model.name, vehicleType: model.vehicleType || '' };
  }

  closeModelEdit() {
    this.editingModel = null;
  }

  saveModel() {
    if (!this.editingModel || !this.modelForm.name) return;

    this.http.put(`/api/brands/models/${this.editingModel.id}`, {
      name: this.modelForm.name,
      vehicleType: this.modelForm.vehicleType || null,
      isActive: true
    }).subscribe({
      next: () => {
        this.closeModelEdit();
        if (this.selectedBrand) {
          this.selectBrand({ id: this.selectedBrand.id, name: this.selectedBrand.name, modelCount: 0 });
        }
      }
    });
  }

  deleteModel(model: VehicleModel) {
    if (confirm(`Supprimer le modèle "${model.name}" ?`)) {
      this.http.delete(`/api/brands/models/${model.id}`).subscribe({
        next: () => {
          if (this.selectedBrand) {
            this.selectBrand({ id: this.selectedBrand.id, name: this.selectedBrand.name, modelCount: 0 });
          }
        }
      });
    }
  }
}
