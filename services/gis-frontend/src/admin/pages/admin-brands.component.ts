import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Observable, Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
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

/** Réponse des mutations /api/admin/brands (AdminBrandsController). */
interface CatalogResponse {
  id?: number | null;
  reactivated: boolean;
  message: string;
}

/**
 * Lectures sur /api/brands (référentiel ouvert à tous), mutations sur /api/admin/brands :
 * l'intercepteur n'attache admin_token qu'aux URL /api/admin — une mutation sur
 * /api/brands partait sans jeton en session admin (401), et ces routes laissaient
 * n'importe quel client modifier le référentiel de toute la plateforme.
 */
const ADMIN_BRANDS_API = '/api/admin/brands';

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
              <button class="action-btn delete" (click)="deleteBrand(brand); $event.stopPropagation()" [disabled]="saving" title="Désactiver">
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
                  <input type="text" [(ngModel)]="newModelName" (keyup.enter)="addModel()" placeholder="Nom du modèle" />
                  <select [(ngModel)]="newModelType">
                    <option value="">Type</option>
                    <option *ngFor="let t of vehicleTypeOptions" [value]="t.value">{{ t.label }}</option>
                  </select>
                  <button class="btn-add-model" (click)="addModel()" [disabled]="!newModelName.trim() || saving" title="Ajouter">
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
                      <span class="model-type" *ngIf="model.vehicleType">{{ vehicleTypeLabel(model.vehicleType) }}</span>
                    </div>
                    <div class="model-actions">
                      <button class="action-btn small edit" (click)="editModel(model)" title="Modifier">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                          <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                        </svg>
                      </button>
                      <button class="action-btn small delete" (click)="deleteModel(model)" [disabled]="saving" title="Désactiver">
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
              <button class="btn-primary" (click)="saveBrand()" [disabled]="!brandForm.name.trim() || saving">
                {{ saving ? 'Envoi…' : (editingBrand ? 'Enregistrer' : 'Créer') }}
              </button>
            </div>
          </div>
        </div>

        <!-- Model Edit Modal -->
        <div class="modal-overlay" *ngIf="editingModel" (mousedown)="closeModelEdit()">
          <div class="modal" (mousedown)="$event.stopPropagation()">
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
                  <option *ngFor="let t of vehicleTypeOptions" [value]="t.value">{{ t.label }}</option>
                  <!-- Valeur héritée du jeu initial (van, sedan…) : affichée pour ne pas la perdre à l'enregistrement. -->
                  <option *ngIf="isLegacyVehicleType(editingModel.vehicleType)" [value]="editingModel.vehicleType">{{ vehicleTypeLabel(editingModel.vehicleType) }}</option>
                </select>
              </div>
            </div>
            <div class="modal-footer">
              <button class="btn-secondary" (click)="closeModelEdit()">Annuler</button>
              <button class="btn-primary" (click)="saveModel()" [disabled]="!modelForm.name.trim() || saving">{{ saving ? 'Envoi…' : 'Enregistrer' }}</button>
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

    @keyframes rise {
      from { opacity: 0; transform: translateY(8px); }
      to { opacity: 1; transform: none; }
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
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      padding: 10px 14px;
      width: 280px;
      transition: border-color 0.15s, box-shadow 0.15s;
    }

    .search-box:focus-within {
      border-color: var(--adm-indigo);
      box-shadow: 0 0 0 3px rgba(79,70,229,0.12);
    }

    .search-box svg { color: var(--adm-sub); }

    .search-box input {
      flex: 1;
      border: none;
      background: transparent;
      color: var(--adm-ink);
      font-size: 14px;
      outline: none;
    }

    .add-btn {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 9px 18px;
      background: var(--adm-indigo);
      border: none;
      border-radius: 10px;
      color: #fff;
      font-size: 13px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .add-btn:hover {
      background: var(--adm-indigo-ink);
      transform: translateY(-1px);
      box-shadow: var(--adm-shadow-hover);
    }

    .brands-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
      gap: 16px;
    }

    .brand-card {
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 16px;
      box-shadow: var(--adm-shadow);
      padding: 20px;
      display: flex;
      align-items: center;
      gap: 16px;
      cursor: pointer;
      transition: all 0.2s;
      animation: rise 0.25s ease-out both;
    }

    .brand-card:hover {
      border-color: var(--adm-indigo);
      box-shadow: var(--adm-shadow-hover);
      transform: translateY(-1px);
    }

    .brand-logo {
      width: 56px;
      height: 56px;
      background: rgba(79,70,229,0.08);
      border-radius: 12px;
      display: flex;
      align-items: center;
      justify-content: center;
      color: var(--adm-indigo);
    }

    .brand-info {
      flex: 1;
    }

    .brand-info h3 {
      margin: 0 0 4px 0;
      font-size: 16px;
      font-weight: 600;
      color: var(--adm-ink);
    }

    .model-count {
      font-size: 13px;
      color: var(--adm-sub);
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

    .action-btn:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }

    .action-btn.small {
      width: 28px;
      height: 28px;
    }

    .action-btn.edit {
      background: rgba(79,70,229,0.10);
      color: var(--adm-indigo-ink);
    }

    .action-btn.delete {
      background: rgba(220,38,38,0.10);
      color: var(--adm-red-ink);
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
      color: var(--adm-sub);
    }

    .empty-state svg {
      margin-bottom: 16px;
      opacity: 0.5;
    }

    .empty-state h3 {
      margin: 0 0 8px 0;
      font-size: 18px;
      color: var(--adm-ink);
    }

    .empty-state p {
      margin: 0;
      font-size: 14px;
    }

    /* Slide-in panels */
    .detail-overlay, .form-overlay {
      position: fixed;
      inset: 0;
      background: rgba(15,23,42,0.55);
      backdrop-filter: blur(4px);
      z-index: 1000;
      display: flex;
      justify-content: flex-end;
    }

    .detail-panel, .form-panel {
      width: 480px;
      max-width: 100%;
      height: 100%;
      background: var(--adm-card);
      display: flex;
      flex-direction: column;
      box-shadow: -12px 0 32px -12px rgba(2,6,23,0.35);
    }

    .panel-header, .form-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 20px 24px;
      background: linear-gradient(135deg, var(--adm-carb1) 0%, var(--adm-carb2) 100%);
      border-bottom: 1px solid var(--adm-glow);
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
      opacity: 0.75;
    }

    .btn-close-panel {
      width: 36px;
      height: 36px;
      border: none;
      background: rgba(255,255,255,0.12);
      border-radius: 8px;
      color: #fff;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }

    .btn-close-panel:hover {
      background: rgba(255,255,255,0.22);
    }

    .panel-body, .form-body {
      flex: 1;
      overflow-y: auto;
      padding: 24px;
    }

    .add-model-section {
      background: #f8fafc;
      border: 1px solid var(--adm-border);
      border-radius: 12px;
      padding: 16px;
      margin-bottom: 24px;
    }

    .add-model-section h3 {
      margin: 0 0 12px 0;
      font-size: 12px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      color: var(--adm-sub);
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .add-model-section h3::before {
      content: '';
      width: 3px;
      height: 12px;
      border-radius: 2px;
      background: var(--adm-indigo);
      flex-shrink: 0;
    }

    .add-model-form {
      display: flex;
      gap: 8px;
    }

    .add-model-form input, .add-model-form select {
      flex: 1;
      padding: 10px 12px;
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      font-size: 14px;
      outline: none;
      transition: border-color 0.15s, box-shadow 0.15s;
    }

    .add-model-form input:focus, .add-model-form select:focus {
      border-color: var(--adm-indigo);
      box-shadow: 0 0 0 3px rgba(79,70,229,0.12);
    }

    .btn-add-model {
      width: 40px;
      height: 40px;
      background: var(--adm-indigo);
      border: none;
      border-radius: 10px;
      color: #fff;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }

    .btn-add-model:hover:not(:disabled) {
      background: var(--adm-indigo-ink);
    }

    .btn-add-model:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }

    .models-section h3 {
      margin: 0 0 16px 0;
      font-size: 12px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      color: var(--adm-sub);
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .models-section h3::before {
      content: '';
      width: 3px;
      height: 12px;
      border-radius: 2px;
      background: var(--adm-indigo);
      flex-shrink: 0;
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
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      transition: all 0.2s;
    }

    .model-item:hover {
      border-color: var(--adm-indigo);
      box-shadow: var(--adm-shadow);
    }

    .model-info {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .model-name {
      font-size: 14px;
      font-weight: 500;
      color: var(--adm-ink);
    }

    .model-type {
      font-size: 11px;
      font-weight: 700;
      padding: 2px 10px;
      background: rgba(79,70,229,0.10);
      color: var(--adm-indigo-ink);
      border-radius: 999px;
    }

    .model-actions {
      display: flex;
      gap: 6px;
    }

    .empty-models {
      padding: 24px;
      text-align: center;
      color: var(--adm-sub);
      font-size: 14px;
    }

    .panel-footer, .form-footer {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 16px 24px;
      border-top: 1px solid var(--adm-border);
    }

    .form-group {
      margin-bottom: 16px;
    }

    .form-group label {
      display: block;
      margin-bottom: 6px;
      font-size: 13px;
      font-weight: 600;
      color: var(--adm-ink);
    }

    .form-group input, .form-group select {
      width: 100%;
      padding: 12px 14px;
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      font-size: 14px;
      outline: none;
      transition: border-color 0.15s, box-shadow 0.15s;
    }

    .form-group input:focus, .form-group select:focus {
      border-color: var(--adm-indigo);
      box-shadow: 0 0 0 3px rgba(79,70,229,0.12);
    }

    .btn-secondary {
      padding: 9px 18px;
      background: #fff;
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      color: var(--adm-ink);
      font-size: 13px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-secondary:hover {
      border-color: var(--adm-indigo);
    }

    .btn-primary {
      padding: 9px 18px;
      background: var(--adm-indigo);
      border: none;
      border-radius: 10px;
      color: #fff;
      font-size: 13px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-primary:hover {
      background: var(--adm-indigo-ink);
    }

    .btn-primary:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }

    /* Modal */
    .modal-overlay {
      position: fixed;
      inset: 0;
      background: rgba(15,23,42,0.55);
      backdrop-filter: blur(4px);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1100;
    }

    .modal {
      background: var(--adm-card);
      border-radius: 18px;
      width: 400px;
      max-width: 90%;
      box-shadow: 0 24px 60px -24px rgba(2,6,23,0.45);
    }

    .modal-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 20px 24px;
      border-bottom: 1px solid var(--adm-border);
    }

    .modal-header h2 {
      margin: 0;
      font-size: 18px;
      font-weight: 600;
      color: var(--adm-ink);
    }

    .close-btn {
      width: 36px;
      height: 36px;
      border: none;
      background: var(--adm-track);
      border-radius: 10px;
      color: var(--adm-sub);
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.15s;
    }

    .close-btn:hover {
      background: var(--adm-border);
      color: var(--adm-ink);
    }

    .modal-body {
      padding: 24px;
    }

    .modal-footer {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 16px 24px;
      border-top: 1px solid var(--adm-border);
    }

    @media (prefers-reduced-motion: reduce) {
      .brand-card { animation: none; }
      .brand-card:hover, .add-btn:hover, .action-btn:hover { transform: none; }
    }
  `]
})
export class AdminBrandsComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
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
  /** Une mutation est en vol : boutons désactivés pour éviter le double envoi (doublons). */
  saving = false;

  /** Valeurs stockées en base (vehicle_models."VehicleType") et proposées au formulaire véhicule. */
  readonly vehicleTypeOptions = [
    { value: 'citadine', label: 'Citadine' },
    { value: 'suv', label: 'SUV' },
    { value: 'utilitaire', label: 'Utilitaire' },
    { value: 'camion', label: 'Camion' },
    { value: 'other', label: 'Autre' },
  ];

  /** Types anglais du jeu de données initial, encore portés par la majorité des modèles. */
  private readonly legacyVehicleTypeLabels: Record<string, string> = {
    van: 'Fourgon',
    hatchback: 'Compacte',
    sedan: 'Berline',
    truck: 'Camion (truck)',
    pickup: 'Pick-up',
  };

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
    this.http.get<Brand[]>('/api/brands').pipe(takeUntil(this.destroy$)).subscribe({
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
    this.http.get<BrandDetail>(`/api/brands/${brand.id}`).pipe(takeUntil(this.destroy$)).subscribe({
      next: (detail) => {
        this.selectedBrand = detail;
        this.cdr.detectChanges();
      },
      error: (err: HttpErrorResponse) => alert(this.errorMessage(err, 'Impossible de charger la marque.'))
    });
  }

  private refreshSelectedBrand() {
    if (this.selectedBrand) {
      this.selectBrand({ id: this.selectedBrand.id, name: this.selectedBrand.name, modelCount: 0 });
    }
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
    if (!this.brandForm.name.trim() || this.saving) return;

    const body = { name: this.brandForm.name, logoUrl: this.brandForm.logoUrl || null };
    const editing = this.editingBrand;
    const request$ = editing
      ? this.http.put<CatalogResponse>(`${ADMIN_BRANDS_API}/${editing.id}`, body)
      : this.http.post<CatalogResponse>(ADMIN_BRANDS_API, body);

    this.mutate(request$, editing ? 'Impossible d\'enregistrer la marque.' : 'Impossible de créer la marque.', () => {
      this.closeBrandForm();
      this.loadBrands();
    });
  }

  deleteBrand(brand: Brand) {
    if (this.saving) return;
    if (!confirm(`Désactiver la marque « ${brand.name} » ? Elle et ses modèles ne seront plus proposés dans les formulaires véhicule.`)) return;

    this.mutate(this.http.delete<CatalogResponse>(`${ADMIN_BRANDS_API}/${brand.id}`), 'Impossible de désactiver la marque.', () => {
      this.loadBrands();
    });
  }

  addModel() {
    if (!this.selectedBrand || !this.newModelName.trim() || this.saving) return;

    const body = { name: this.newModelName, vehicleType: this.newModelType || null };
    this.mutate(this.http.post<CatalogResponse>(`${ADMIN_BRANDS_API}/${this.selectedBrand.id}/models`, body), 'Impossible d\'ajouter le modèle.', () => {
      // Champs vidés uniquement en cas de succès : après un refus (doublon…), la saisie reste corrigeable.
      this.newModelName = '';
      this.newModelType = '';
      this.refreshSelectedBrand();
      this.loadBrands();
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
    if (!this.editingModel || !this.modelForm.name.trim() || this.saving) return;

    const body = { name: this.modelForm.name, vehicleType: this.modelForm.vehicleType || null };
    this.mutate(this.http.put<CatalogResponse>(`${ADMIN_BRANDS_API}/models/${this.editingModel.id}`, body), 'Impossible d\'enregistrer le modèle.', () => {
      this.closeModelEdit();
      this.refreshSelectedBrand();
    });
  }

  deleteModel(model: VehicleModel) {
    if (this.saving) return;
    if (!confirm(`Désactiver le modèle « ${model.name} » ? Il ne sera plus proposé dans les formulaires véhicule.`)) return;

    this.mutate(this.http.delete<CatalogResponse>(`${ADMIN_BRANDS_API}/models/${model.id}`), 'Impossible de désactiver le modèle.', () => {
      this.refreshSelectedBrand();
      this.loadBrands();
    });
  }

  vehicleTypeLabel(type?: string | null): string {
    if (!type) return '';
    return this.vehicleTypeOptions.find(t => t.value === type)?.label
      ?? (this.isLegacyVehicleType(type) ? this.legacyVehicleTypeLabels[type] : type);
  }

  isLegacyVehicleType(type?: string | null): type is string {
    return !!type && Object.prototype.hasOwnProperty.call(this.legacyVehicleTypeLabels, type);
  }

  /**
   * Envoie une mutation du référentiel. Avant, aucune n'avait de gestion d'erreur ni de
   * rendu forcé : un refus (401, doublon…) ne produisait RIEN à l'écran. Ici le bouton est
   * désactivé pendant l'envoi, le message du serveur est affiché en cas d'échec, et
   * detectChanges() suit chaque retour (Angular 21 sans zone : pas de rendu automatique
   * après une réponse HTTP). onSuccess n'est appelé qu'en cas de succès.
   */
  private mutate(request$: Observable<CatalogResponse | null>, failureMessage: string, onSuccess: () => void) {
    if (this.saving) return;
    this.saving = true;
    this.cdr.detectChanges();

    request$.pipe(takeUntil(this.destroy$)).subscribe({
      next: (res) => {
        this.saving = false;
        onSuccess();
        this.cdr.detectChanges();
        // Homonyme désactivé réutilisé au lieu d'un doublon : l'administrateur doit le savoir.
        if (res?.reactivated && res.message) alert(res.message);
      },
      error: (err: HttpErrorResponse) => {
        this.saving = false;
        this.cdr.detectChanges();
        alert(this.errorMessage(err, failureMessage));
      }
    });
  }

  private errorMessage(err: HttpErrorResponse, fallback: string): string {
    const message = err?.error?.message;
    if (typeof message === 'string' && message.trim()) return message;
    if (err?.status === 0) return `${fallback} Serveur injoignable.`;
    if (err?.status === 401) return `${fallback} Session administrateur expirée : reconnectez-vous.`;
    if (err?.status === 403) return `${fallback} Accès réservé aux administrateurs système.`;
    return `${fallback} (erreur ${err?.status ?? 'inconnue'})`;
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
