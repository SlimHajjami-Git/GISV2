import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { AppLayoutComponent } from './shared/app-layout.component';

interface Department {
  id: number;
  name: string;
  description?: string;
  isActive: boolean;
  vehicleCount: number;
}

interface FuelType {
  id: number;
  code: string;
  name: string;
  isSystem: boolean;
  currentPrice?: number;
}

interface Vehicle {
  id: number;
  name: string;
  plate?: string;
  speedLimit?: number;
}

interface VehicleForAssignment {
  id: number;
  name: string;
  plate?: string;
  type?: string;
  brand?: string;
  model?: string;
  isAssigned: boolean;
  currentDepartmentId?: number;
  currentDepartmentName?: string;
}

interface PartCategory {
  id: number;
  name: string;
  description?: string;
  icon?: string;
  partsCount: number;
}

interface VehiclePart {
  id: number;
  categoryId: number;
  name: string;
  description?: string;
  partNumber?: string;
}

interface PartPricing {
  id: number;
  partId: number;
  partName: string;
  categoryName: string;
  price: number;
  supplier?: string;
  notes?: string;
  updatedAt: string;
}

@Component({
  selector: 'app-fleet-management',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent],
  template: `
    <app-layout>
      <div class="settings-page">
        <div class="page-header">
          <h1>Gestion de Flotte</h1>
          <p class="subtitle">Gérez les départements, types de carburant et limites de vitesse</p>
        </div>

        <div class="settings-content">
          <div class="settings-nav">
            <button 
              *ngFor="let tab of tabs" 
              class="nav-item" 
              [class.active]="activeTab === tab.id"
              (click)="activeTab = tab.id; loadTabData()">
              <span class="nav-icon" [innerHTML]="tab.icon"></span>
              <span>{{ tab.label }}</span>
            </button>
          </div>

          <div class="settings-panel">
            <!-- Departments -->
            <div class="panel-section" *ngIf="activeTab === 'departments'">
              <h2>Départements</h2>
              <p class="section-desc">Organisez vos véhicules par département ou service</p>

              <div class="settings-group">
                <div class="group-header">
                  <h3>Liste des départements</h3>
                  <button class="btn-primary-sm" (click)="openDepartmentModal()">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
                    </svg>
                    Ajouter
                  </button>
                </div>
                
                <div class="loading-state" *ngIf="loading">
                  <div class="spinner"></div>
                  <span>Chargement...</span>
                </div>

                <div class="items-list" *ngIf="!loading">
                  <div class="list-item" *ngFor="let dept of departments">
                    <div class="item-info">
                      <span class="item-name">{{ dept.name }}</span>
                      <span class="item-desc">{{ dept.description || 'Aucune description' }}</span>
                    </div>
                    <div class="item-meta">
                      <span class="badge">{{ dept.vehicleCount }} véhicules</span>
                      <span class="status-dot" [class.active]="dept.isActive" [class.inactive]="!dept.isActive"></span>
                    </div>
                    <div class="item-actions">
                      <button class="action-btn assign" (click)="openVehicleAssignmentModal(dept)" title="Affecter véhicules">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <rect x="1" y="3" width="15" height="13" rx="2"/>
                          <polygon points="16 8 20 8 23 11 23 16 16 16 16 8"/>
                          <circle cx="5.5" cy="18.5" r="2.5"/>
                          <circle cx="18.5" cy="18.5" r="2.5"/>
                        </svg>
                      </button>
                      <button class="action-btn" (click)="editDepartment(dept)" title="Modifier">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                          <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                        </svg>
                      </button>
                      <button class="action-btn danger" (click)="deleteDepartment(dept)" title="Supprimer" [disabled]="dept.vehicleCount > 0">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                        </svg>
                      </button>
                    </div>
                  </div>
                  <div class="empty-list" *ngIf="departments.length === 0">
                    <p>Aucun département créé</p>
                  </div>
                </div>
              </div>
            </div>

            <!-- Fuel Types -->
            <div class="panel-section" *ngIf="activeTab === 'fuel'">
              <h2>Types de carburant</h2>
              <p class="section-desc">Gérez les types de carburant et leurs prix actuels</p>

              <div class="settings-group">
                <h3>Carburants disponibles</h3>
                
                <div class="loading-state" *ngIf="loading">
                  <div class="spinner"></div>
                  <span>Chargement...</span>
                </div>

                <div class="items-list" *ngIf="!loading">
                  <div class="list-item" *ngFor="let fuel of fuelTypes">
                    <div class="item-info">
                      <span class="item-name">{{ fuel.name }}</span>
                      <span class="item-desc">Code: {{ fuel.code }}</span>
                    </div>
                    <div class="item-meta">
                      <span class="price-tag" *ngIf="fuel.currentPrice">{{ fuel.currentPrice | number:'1.3-3' }} DT/L</span>
                      <span class="badge system" *ngIf="fuel.isSystem">Système</span>
                    </div>
                    <div class="item-actions">
                      <button class="action-btn" (click)="editFuelPrice(fuel)" title="Modifier prix">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <line x1="12" y1="1" x2="12" y2="23"/><path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>
                        </svg>
                      </button>
                    </div>
                  </div>
                  <div class="empty-list" *ngIf="fuelTypes.length === 0">
                    <p>Aucun type de carburant</p>
                  </div>
                </div>
              </div>

              <div class="settings-group">
                <h3>Ajouter un prix</h3>
                <div class="inline-form">
                  <div class="form-group">
                    <label>Type de carburant</label>
                    <select [(ngModel)]="newPrice.fuelTypeId">
                      <option [ngValue]="null">Sélectionner...</option>
                      <option *ngFor="let fuel of fuelTypes" [ngValue]="fuel.id">{{ fuel.name }}</option>
                    </select>
                  </div>
                  <div class="form-group">
                    <label>Prix par litre (DT)</label>
                    <input type="number" [(ngModel)]="newPrice.price" step="0.001" placeholder="0.000">
                  </div>
                  <button class="btn-primary" (click)="saveFuelPrice()" [disabled]="!newPrice.fuelTypeId || !newPrice.price">
                    Enregistrer
                  </button>
                </div>
              </div>
            </div>

            <!-- Speed Limits -->
            <div class="panel-section" *ngIf="activeTab === 'speed'">
              <h2>Limites de vitesse</h2>
              <p class="section-desc">Configurez les limites de vitesse par véhicule</p>

              <div class="settings-group">
                <h3>Véhicules et limites</h3>
                
                <div class="loading-state" *ngIf="loading">
                  <div class="spinner"></div>
                  <span>Chargement...</span>
                </div>

                <div class="items-list" *ngIf="!loading">
                  <div class="list-item" *ngFor="let vehicle of vehicles">
                    <div class="item-info">
                      <span class="item-name">{{ vehicle.name }}</span>
                      <span class="item-desc">{{ vehicle.plate || 'Sans immatriculation' }}</span>
                    </div>
                    <div class="speed-input">
                      <input type="number" [(ngModel)]="vehicle.speedLimit" min="0" max="200" placeholder="km/h">
                      <span class="unit">km/h</span>
                    </div>
                    <div class="item-actions">
                      <button class="action-btn save" (click)="saveSpeedLimit(vehicle)" title="Enregistrer">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <polyline points="20 6 9 17 4 12"/>
                        </svg>
                      </button>
                    </div>
                  </div>
                  <div class="empty-list" *ngIf="vehicles.length === 0">
                    <p>Aucun véhicule</p>
                  </div>
                </div>
              </div>

              <div class="settings-group">
                <h3>Limite par défaut</h3>
                <div class="setting-item">
                  <div class="setting-info">
                    <span class="setting-label">Vitesse maximale par défaut</span>
                    <span class="setting-desc">Appliquée aux nouveaux véhicules</span>
                  </div>
                  <div class="speed-input">
                    <input type="number" [(ngModel)]="defaultSpeedLimit" min="0" max="200">
                    <span class="unit">km/h</span>
                  </div>
                </div>
                <button class="btn-primary" (click)="applyDefaultSpeed()">
                  Appliquer à tous les véhicules
                </button>
              </div>
            </div>

            <!-- Parts Pricing -->
            <div class="panel-section" *ngIf="activeTab === 'parts'">
              <h2>Tarification des pièces</h2>
              <p class="section-desc">Définissez les prix des pièces pour votre entreprise</p>

              <div class="parts-layout">
                <!-- Categories sidebar -->
                <div class="categories-sidebar">
                  <h4>Catégories</h4>
                  <div class="loading-state" *ngIf="loading">
                    <div class="spinner"></div>
                  </div>
                  <div class="category-list" *ngIf="!loading">
                    <div 
                      class="category-item" 
                      *ngFor="let cat of partCategories"
                      [class.active]="selectedPartCategory?.id === cat.id"
                      (click)="selectPartCategory(cat)">
                      <span class="cat-icon">{{ cat.icon || '🔧' }}</span>
                      <span class="cat-name">{{ cat.name }}</span>
                      <span class="cat-count">{{ cat.partsCount }}</span>
                    </div>
                    <div class="empty-list" *ngIf="partCategories.length === 0">
                      <p>Aucune catégorie</p>
                    </div>
                  </div>
                </div>

                <!-- Parts list -->
                <div class="parts-main">
                  <div class="parts-header" *ngIf="selectedPartCategory">
                    <h3>{{ selectedPartCategory.icon || '🔧' }} {{ selectedPartCategory.name }}</h3>
                  </div>

                  <div class="parts-grid" *ngIf="selectedPartCategory && categoryParts.length > 0">
                    <div class="part-price-card" *ngFor="let part of categoryParts">
                      <div class="part-card-info">
                        <span class="part-name">{{ part.name }}</span>
                        <span class="part-desc" *ngIf="part.description">{{ part.description }}</span>
                      </div>
                      <div class="part-card-price">
                        <span class="price-value" *ngIf="getPartPrice(part.id)">
                          {{ getPartPrice(part.id)!.price | number:'1.2-2' }} DT
                        </span>
                        <span class="no-price" *ngIf="!getPartPrice(part.id)">Non défini</span>
                      </div>
                      <div class="part-card-actions">
                        <button class="action-btn" (click)="openPartPricingModal(part)" title="Définir le prix">
                          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                            <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                          </svg>
                        </button>
                      </div>
                    </div>
                  </div>

                  <div class="empty-parts" *ngIf="selectedPartCategory && categoryParts.length === 0">
                    <p>Aucune pièce dans cette catégorie</p>
                  </div>

                  <div class="select-category-prompt" *ngIf="!selectedPartCategory">
                    <div class="prompt-icon">👆</div>
                    <p>Sélectionnez une catégorie pour voir les pièces</p>
                  </div>
                </div>
              </div>

              <!-- Configured Prices Summary -->
              <div class="settings-group" *ngIf="partPricing.length > 0">
                <h3>Prix configurés</h3>
                <div class="items-list">
                  <div class="list-item" *ngFor="let pricing of partPricing">
                    <div class="item-info">
                      <span class="item-name">{{ pricing.partName }}</span>
                      <span class="item-desc">{{ pricing.categoryName }}</span>
                    </div>
                    <div class="item-meta">
                      <span class="price-tag">{{ pricing.price | number:'1.2-2' }} DT</span>
                      <span class="supplier-tag" *ngIf="pricing.supplier">{{ pricing.supplier }}</span>
                    </div>
                    <div class="item-actions">
                      <button class="action-btn danger" (click)="deletePartPricing(pricing)" title="Supprimer">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                        </svg>
                      </button>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Department Modal -->
      <div class="modal-overlay" *ngIf="showDepartmentModal" (click)="closeDepartmentModal()">
        <div class="modal" (click)="$event.stopPropagation()">
          <div class="modal-header">
            <h3>{{ editingDepartment ? 'Modifier' : 'Nouveau' }} département</h3>
            <button class="close-btn" (click)="closeDepartmentModal()">×</button>
          </div>
          <div class="modal-body">
            <div class="form-group">
              <label>Nom du département</label>
              <input type="text" [(ngModel)]="departmentForm.name" placeholder="Ex: Transport, Livraison...">
            </div>
            <div class="form-group">
              <label>Description</label>
              <textarea [(ngModel)]="departmentForm.description" placeholder="Description optionnelle..." rows="3"></textarea>
            </div>
            <div class="setting-item">
              <div class="setting-info">
                <span class="setting-label">Actif</span>
                <span class="setting-desc">Le département est visible et utilisable</span>
              </div>
              <label class="toggle">
                <input type="checkbox" [(ngModel)]="departmentForm.isActive">
                <span class="slider"></span>
              </label>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn-secondary" (click)="closeDepartmentModal()">Annuler</button>
            <button class="btn-primary" (click)="saveDepartment()" [disabled]="!departmentForm.name">Enregistrer</button>
          </div>
        </div>
      </div>

      <!-- Vehicle Assignment Modal -->
      <div class="modal-overlay" *ngIf="showVehicleAssignmentModal" (click)="closeVehicleAssignmentModal()">
        <div class="modal modal-large" (click)="$event.stopPropagation()">
          <div class="modal-header">
            <h3>Affecter des véhicules à "{{ assigningDepartment?.name }}"</h3>
            <button class="close-btn" (click)="closeVehicleAssignmentModal()">×</button>
          </div>
          <div class="modal-body">
            <div class="vehicle-selection-header">
              <span class="selection-count">{{ getSelectedVehicleCount() }} véhicule(s) sélectionné(s)</span>
              <div class="selection-actions">
                <button class="btn-text" (click)="selectAllVehicles()">Tout sélectionner</button>
                <button class="btn-text" (click)="deselectAllVehicles()">Tout désélectionner</button>
              </div>
            </div>
            
            <div class="loading-state" *ngIf="loadingVehicles">
              <div class="spinner"></div>
              <span>Chargement des véhicules...</span>
            </div>

            <div class="vehicle-cards-grid" *ngIf="!loadingVehicles">
              <div 
                class="vehicle-card" 
                *ngFor="let vehicle of availableVehicles"
                [class.selected]="vehicle.isAssigned"
                [class.assigned-other]="vehicle.currentDepartmentId && vehicle.currentDepartmentId !== assigningDepartment?.id"
                (click)="toggleVehicleSelection(vehicle)">
                <div class="vehicle-card-checkbox">
                  <input type="checkbox" [checked]="vehicle.isAssigned" (click)="$event.stopPropagation()">
                </div>
                <div class="vehicle-card-icon">
                  <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                    <rect x="1" y="3" width="15" height="13" rx="2"/>
                    <polygon points="16 8 20 8 23 11 23 16 16 16 16 8"/>
                    <circle cx="5.5" cy="18.5" r="2.5"/>
                    <circle cx="18.5" cy="18.5" r="2.5"/>
                  </svg>
                </div>
                <div class="vehicle-card-info">
                  <span class="vehicle-name">{{ vehicle.name }}</span>
                  <span class="vehicle-plate">{{ vehicle.plate || 'Sans immatriculation' }}</span>
                  <span class="vehicle-type" *ngIf="vehicle.type">{{ vehicle.type }}</span>
                </div>
                <div class="vehicle-card-status" *ngIf="vehicle.currentDepartmentName && vehicle.currentDepartmentId !== assigningDepartment?.id">
                  <span class="other-dept-badge">{{ vehicle.currentDepartmentName }}</span>
                </div>
              </div>
              
              <div class="empty-list" *ngIf="availableVehicles.length === 0">
                <p>Aucun véhicule disponible</p>
              </div>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn-secondary" (click)="closeVehicleAssignmentModal()">Annuler</button>
            <button class="btn-primary" (click)="saveVehicleAssignments()">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="20 6 9 17 4 12"/>
              </svg>
              Enregistrer
            </button>
          </div>
        </div>
      </div>

      <!-- Part Pricing Modal -->
      <div class="modal-overlay" *ngIf="showPartPricingModal" (click)="closePartPricingModal()">
        <div class="modal" (click)="$event.stopPropagation()">
          <div class="modal-header">
            <h3>Prix de "{{ selectedPart?.name }}"</h3>
            <button class="close-btn" (click)="closePartPricingModal()">×</button>
          </div>
          <div class="modal-body">
            <div class="form-group">
              <label>Prix (DT) *</label>
              <input type="number" [(ngModel)]="partPricingForm.price" placeholder="0.00" min="0" step="0.01">
            </div>
            <div class="form-group">
              <label>Fournisseur</label>
              <input type="text" [(ngModel)]="partPricingForm.supplier" placeholder="Nom du fournisseur...">
            </div>
            <div class="form-group">
              <label>Notes</label>
              <textarea [(ngModel)]="partPricingForm.notes" placeholder="Notes optionnelles..." rows="2"></textarea>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn-secondary" (click)="closePartPricingModal()">Annuler</button>
            <button class="btn-primary" (click)="savePartPricing()" [disabled]="!partPricingForm.price">Enregistrer</button>
          </div>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    .settings-page {
      padding: 24px;
      width: 100%;
      max-width: 100%;
    }

    .page-header {
      margin-bottom: 24px;
    }

    .page-header h1 {
      font-size: 24px;
      font-weight: 700;
      color: #1f2937;
      margin: 0 0 4px 0;
    }

    .subtitle {
      color: #6b7280;
      font-size: 14px;
      margin: 0;
    }

    .settings-content {
      display: flex;
      gap: 24px;
      background: white;
      border-radius: 12px;
      border: 1px solid #e5e7eb;
      overflow: hidden;
      min-height: 600px;
    }

    /* Navigation */
    .settings-nav {
      width: 220px;
      background: #f9fafb;
      border-right: 1px solid #e5e7eb;
      padding: 16px;
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .nav-item {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px 16px;
      border: none;
      background: transparent;
      border-radius: 8px;
      font-size: 14px;
      font-weight: 500;
      color: #4b5563;
      cursor: pointer;
      transition: all 0.15s;
      text-align: left;
    }

    .nav-item:hover {
      background: #e5e7eb;
      color: #1f2937;
    }

    .nav-item.active {
      background: #6366f1;
      color: white;
    }

    .nav-icon {
      width: 20px;
      height: 20px;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    /* Panel */
    .settings-panel {
      flex: 1;
      padding: 24px;
      overflow-y: auto;
    }

    .panel-section h2 {
      font-size: 20px;
      font-weight: 600;
      color: #1f2937;
      margin: 0 0 4px 0;
    }

    .section-desc {
      color: #6b7280;
      font-size: 14px;
      margin: 0 0 24px 0;
    }

    .settings-group {
      margin-bottom: 32px;
    }

    .settings-group h3 {
      font-size: 14px;
      font-weight: 600;
      color: #374151;
      margin: 0 0 16px 0;
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }

    .group-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 16px;
    }

    .group-header h3 {
      margin: 0;
    }

    /* Items List */
    .items-list {
      border: 1px solid #e5e7eb;
      border-radius: 8px;
      overflow: hidden;
    }

    .list-item {
      display: flex;
      align-items: center;
      padding: 12px 16px;
      border-bottom: 1px solid #f3f4f6;
      gap: 16px;
    }

    .list-item:last-child {
      border-bottom: none;
    }

    .list-item:hover {
      background: #f9fafb;
    }

    .item-info {
      flex: 1;
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .item-name {
      font-size: 14px;
      font-weight: 500;
      color: #1f2937;
    }

    .item-desc {
      font-size: 12px;
      color: #6b7280;
    }

    .item-meta {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .badge {
      padding: 4px 8px;
      background: #e5e7eb;
      border-radius: 4px;
      font-size: 11px;
      color: #4b5563;
    }

    .badge.system {
      background: #dbeafe;
      color: #1d4ed8;
    }

    .price-tag {
      font-size: 14px;
      font-weight: 600;
      color: #059669;
    }

    .status-dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
    }

    .status-dot.active {
      background: #10b981;
    }

    .status-dot.inactive {
      background: #ef4444;
    }

    .item-actions {
      display: flex;
      gap: 4px;
    }

    .action-btn {
      width: 28px;
      height: 28px;
      border: none;
      background: transparent;
      border-radius: 4px;
      color: #6b7280;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.15s;
    }

    .action-btn:hover {
      background: #e5e7eb;
      color: #1f2937;
    }

    .action-btn.danger:hover {
      background: #fee2e2;
      color: #dc2626;
    }

    .action-btn.save:hover {
      background: #dcfce7;
      color: #16a34a;
    }

    .action-btn:disabled {
      opacity: 0.4;
      cursor: not-allowed;
    }

    .empty-list {
      padding: 24px;
      text-align: center;
      color: #6b7280;
      font-size: 14px;
    }

    /* Setting Item */
    .setting-item {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 12px 0;
      border-bottom: 1px solid #f3f4f6;
    }

    .setting-item:last-child {
      border-bottom: none;
    }

    .setting-info {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .setting-label {
      font-size: 14px;
      font-weight: 500;
      color: #1f2937;
    }

    .setting-desc {
      font-size: 12px;
      color: #6b7280;
    }

    /* Toggle Switch */
    .toggle {
      position: relative;
      display: inline-block;
      width: 44px;
      height: 24px;
    }

    .toggle input {
      opacity: 0;
      width: 0;
      height: 0;
    }

    .slider {
      position: absolute;
      cursor: pointer;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background-color: #d1d5db;
      transition: 0.3s;
      border-radius: 24px;
    }

    .slider:before {
      position: absolute;
      content: "";
      height: 18px;
      width: 18px;
      left: 3px;
      bottom: 3px;
      background-color: white;
      transition: 0.3s;
      border-radius: 50%;
    }

    .toggle input:checked + .slider {
      background-color: #6366f1;
    }

    .toggle input:checked + .slider:before {
      transform: translateX(20px);
    }

    /* Speed Input */
    .speed-input {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .speed-input input {
      width: 80px;
      padding: 8px 12px;
      border: 1px solid #d1d5db;
      border-radius: 6px;
      font-size: 14px;
      text-align: right;
    }

    .speed-input .unit {
      font-size: 12px;
      color: #6b7280;
    }

    /* Inline Form */
    .inline-form {
      display: flex;
      gap: 16px;
      align-items: flex-end;
      flex-wrap: wrap;
    }

    .form-group {
      display: flex;
      flex-direction: column;
      gap: 6px;
    }

    .form-group label {
      font-size: 12px;
      font-weight: 500;
      color: #374151;
    }

    .form-group input,
    .form-group select,
    .form-group textarea {
      padding: 10px 12px;
      border: 1px solid #d1d5db;
      border-radius: 6px;
      font-size: 14px;
      color: #1f2937;
    }

    .form-group input:focus,
    .form-group select:focus,
    .form-group textarea:focus {
      outline: none;
      border-color: #6366f1;
      box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.1);
    }

    .form-group select {
      min-width: 180px;
    }

    /* Buttons */
    .btn-primary {
      padding: 10px 20px;
      background: #6366f1;
      color: white;
      border: none;
      border-radius: 6px;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.15s;
    }

    .btn-primary:hover {
      background: #4f46e5;
    }

    .btn-primary:disabled {
      background: #a5b4fc;
      cursor: not-allowed;
    }

    .btn-primary-sm {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 6px 12px;
      background: #6366f1;
      color: white;
      border: none;
      border-radius: 6px;
      font-size: 12px;
      font-weight: 500;
      cursor: pointer;
    }

    .btn-primary-sm:hover {
      background: #4f46e5;
    }

    .btn-secondary {
      padding: 10px 20px;
      background: white;
      color: #374151;
      border: 1px solid #d1d5db;
      border-radius: 6px;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
    }

    .btn-secondary:hover {
      background: #f9fafb;
    }

    /* Loading */
    .loading-state {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 12px;
      padding: 40px;
      color: #6b7280;
    }

    .spinner {
      width: 20px;
      height: 20px;
      border: 2px solid #e5e7eb;
      border-top-color: #6366f1;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }

    /* Modal */
    .modal-overlay {
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(0, 0, 0, 0.5);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
    }

    .modal {
      background: white;
      border-radius: 12px;
      width: 100%;
      max-width: 480px;
      box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.1);
    }

    .modal-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 16px 20px;
      border-bottom: 1px solid #e5e7eb;
    }

    .modal-header h3 {
      margin: 0;
      font-size: 16px;
      font-weight: 600;
      color: #1f2937;
    }

    .close-btn {
      width: 28px;
      height: 28px;
      border: none;
      background: #f3f4f6;
      border-radius: 6px;
      font-size: 18px;
      color: #6b7280;
      cursor: pointer;
    }

    .close-btn:hover {
      background: #e5e7eb;
    }

    .modal-body {
      padding: 20px;
    }

    .modal-body .form-group {
      margin-bottom: 16px;
    }

    .modal-body .form-group input,
    .modal-body .form-group textarea {
      width: 100%;
    }

    .modal-footer {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 16px 20px;
      border-top: 1px solid #e5e7eb;
    }

    /* Vehicle Assignment Modal */
    .modal-large {
      max-width: 800px;
    }

    .vehicle-selection-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 16px;
      padding-bottom: 12px;
      border-bottom: 1px solid #e5e7eb;
    }

    .selection-count {
      font-size: 14px;
      font-weight: 500;
      color: #374151;
    }

    .selection-actions {
      display: flex;
      gap: 12px;
    }

    .btn-text {
      background: none;
      border: none;
      color: #6366f1;
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
      padding: 4px 8px;
      border-radius: 4px;
    }

    .btn-text:hover {
      background: #eef2ff;
    }

    .vehicle-cards-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
      gap: 12px;
      max-height: 400px;
      overflow-y: auto;
      padding: 4px;
    }

    .vehicle-card {
      position: relative;
      background: white;
      border: 2px solid #e5e7eb;
      border-radius: 10px;
      padding: 16px;
      cursor: pointer;
      transition: all 0.2s;
      display: flex;
      flex-direction: column;
      align-items: center;
      text-align: center;
      gap: 8px;
    }

    .vehicle-card:hover {
      border-color: #a5b4fc;
      box-shadow: 0 4px 12px rgba(99, 102, 241, 0.1);
    }

    .vehicle-card.selected {
      border-color: #6366f1;
      background: #eef2ff;
    }

    .vehicle-card.assigned-other {
      opacity: 0.7;
    }

    .vehicle-card-checkbox {
      position: absolute;
      top: 8px;
      right: 8px;
    }

    .vehicle-card-checkbox input {
      width: 18px;
      height: 18px;
      accent-color: #6366f1;
      cursor: pointer;
    }

    .vehicle-card-icon {
      width: 48px;
      height: 48px;
      background: #f3f4f6;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      color: #6b7280;
    }

    .vehicle-card.selected .vehicle-card-icon {
      background: #c7d2fe;
      color: #4f46e5;
    }

    .vehicle-card-info {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .vehicle-name {
      font-size: 14px;
      font-weight: 600;
      color: #1f2937;
    }

    .vehicle-plate {
      font-size: 12px;
      color: #6b7280;
    }

    .vehicle-type {
      font-size: 11px;
      color: #9ca3af;
      text-transform: capitalize;
    }

    .vehicle-card-status {
      margin-top: 4px;
    }

    .other-dept-badge {
      font-size: 10px;
      padding: 2px 6px;
      background: #fef3c7;
      color: #92400e;
      border-radius: 4px;
    }

    .action-btn.assign:hover {
      background: #dbeafe;
      color: #2563eb;
    }

    /* Parts Pricing Styles */
    .parts-layout {
      display: flex;
      gap: 24px;
      min-height: 400px;
    }

    .categories-sidebar {
      width: 220px;
      background: #f9fafb;
      border-radius: 8px;
      padding: 16px;
      flex-shrink: 0;
    }

    .categories-sidebar h4 {
      margin: 0 0 12px;
      font-size: 14px;
      font-weight: 600;
      color: #374151;
    }

    .category-list {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .category-item {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 10px 12px;
      border-radius: 6px;
      cursor: pointer;
      transition: all 0.2s;
      background: white;
      border: 1px solid #e5e7eb;
    }

    .category-item:hover {
      border-color: #00d4aa;
    }

    .category-item.active {
      background: linear-gradient(135deg, rgba(0, 212, 170, 0.1) 0%, rgba(0, 163, 136, 0.1) 100%);
      border-color: #00d4aa;
    }

    .cat-icon {
      font-size: 18px;
    }

    .cat-name {
      flex: 1;
      font-size: 13px;
      font-weight: 500;
      color: #374151;
    }

    .cat-count {
      font-size: 11px;
      padding: 2px 6px;
      background: #e5e7eb;
      border-radius: 10px;
      color: #6b7280;
    }

    .parts-main {
      flex: 1;
    }

    .parts-header h3 {
      margin: 0 0 16px;
      font-size: 16px;
      font-weight: 600;
      color: #1f2937;
    }

    .parts-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
      gap: 12px;
    }

    .part-price-card {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 14px 16px;
      background: white;
      border: 1px solid #e5e7eb;
      border-radius: 8px;
      transition: all 0.2s;
    }

    .part-price-card:hover {
      border-color: #00d4aa;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.06);
    }

    .part-card-info {
      flex: 1;
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .part-card-info .part-name {
      font-size: 14px;
      font-weight: 500;
      color: #1f2937;
    }

    .part-card-info .part-desc {
      font-size: 12px;
      color: #6b7280;
    }

    .part-card-price {
      text-align: right;
    }

    .price-value {
      font-size: 14px;
      font-weight: 600;
      color: #00a388;
    }

    .no-price {
      font-size: 12px;
      color: #9ca3af;
    }

    .supplier-tag {
      font-size: 11px;
      padding: 2px 8px;
      background: #e0f2fe;
      color: #0369a1;
      border-radius: 4px;
      margin-left: 8px;
    }

    .empty-parts, .select-category-prompt {
      text-align: center;
      padding: 48px 24px;
      color: #6b7280;
      background: #f9fafb;
      border-radius: 8px;
    }

    .prompt-icon {
      font-size: 32px;
      margin-bottom: 12px;
    }

    @media (max-width: 768px) {
      .settings-content {
        flex-direction: column;
      }
      .settings-nav {
        width: 100%;
        flex-direction: row;
        overflow-x: auto;
        border-right: none;
        border-bottom: 1px solid #e5e7eb;
      }
      .inline-form {
        flex-direction: column;
        align-items: stretch;
      }
      .vehicle-cards-grid {
        grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
      }
    }
  `]
})
export class FleetManagementComponent implements OnInit {
  activeTab = 'departments';
  loading = false;
  
  departments: Department[] = [];
  fuelTypes: FuelType[] = [];
  vehicles: Vehicle[] = [];
  defaultSpeedLimit = 90;
  
  showDepartmentModal = false;
  editingDepartment: Department | null = null;
  departmentForm = { name: '', description: '', isActive: true };
  
  newPrice = { fuelTypeId: null as number | null, price: null as number | null };

  // Vehicle assignment
  showVehicleAssignmentModal = false;
  assigningDepartment: Department | null = null;
  availableVehicles: VehicleForAssignment[] = [];
  loadingVehicles = false;

  // Parts pricing
  partCategories: PartCategory[] = [];
  categoryParts: VehiclePart[] = [];
  partPricing: PartPricing[] = [];
  selectedPartCategory: PartCategory | null = null;
  showPartPricingModal = false;
  partPricingForm = { partId: null as number | null, price: null as number | null, supplier: '', notes: '' };
  selectedPart: VehiclePart | null = null;

  tabs = [
    { 
      id: 'departments', 
      label: 'Départements',
      icon: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/></svg>'
    },
    { 
      id: 'fuel', 
      label: 'Carburant',
      icon: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 22V8a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v14"/><path d="M15 13h3a2 2 0 0 1 2 2v5"/><circle cx="17" cy="18" r="2"/><path d="M17 6V3h-4"/></svg>'
    },
    { 
      id: 'speed', 
      label: 'Limites vitesse',
      icon: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><path d="M12 6v6l4 2"/></svg>'
    },
    { 
      id: 'parts', 
      label: 'Prix pièces',
      icon: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/></svg>'
    }
  ];

  constructor(private http: HttpClient, private cdr: ChangeDetectorRef) {}

  ngOnInit() {
    this.loadTabData();
  }

  loadTabData() {
    this.loading = true;
    
    switch (this.activeTab) {
      case 'departments':
        this.loadDepartments();
        break;
      case 'fuel':
        this.loadFuelTypes();
        break;
      case 'speed':
        this.loadVehicles();
        break;
      case 'parts':
        this.loadPartCategories();
        this.loadPartPricing();
        break;
    }
  }

  loadDepartments() {
    this.http.get<any>('/api/fleet/departments').subscribe({
      next: (data) => {
        this.departments = data || [];
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.departments = [];
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  loadFuelTypes() {
    this.http.get<any>('/api/fleet/fuel-types').subscribe({
      next: (data) => {
        this.fuelTypes = data || [];
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.fuelTypes = [];
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  loadVehicles() {
    this.http.get<any>('/api/vehicles').subscribe({
      next: (data) => {
        this.vehicles = (data || []).map((v: any) => ({
          id: v.id,
          name: v.name,
          plate: v.plateNumber || v.plate,
          speedLimit: v.speedLimit || 90
        }));
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.vehicles = [];
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  openDepartmentModal() {
    this.editingDepartment = null;
    this.departmentForm = { name: '', description: '', isActive: true };
    this.showDepartmentModal = true;
  }

  editDepartment(dept: Department) {
    this.editingDepartment = dept;
    this.departmentForm = {
      name: dept.name,
      description: dept.description || '',
      isActive: dept.isActive
    };
    this.showDepartmentModal = true;
  }

  closeDepartmentModal() {
    this.showDepartmentModal = false;
    this.editingDepartment = null;
  }

  saveDepartment() {
    const data = { ...this.departmentForm };
    
    if (this.editingDepartment) {
      this.http.put(`/api/fleet/departments/${this.editingDepartment.id}`, data).subscribe({
        next: () => {
          this.closeDepartmentModal();
          this.loadDepartments();
        },
        error: (err) => console.error('Error updating department:', err)
      });
    } else {
      this.http.post('/api/fleet/departments', data).subscribe({
        next: () => {
          this.closeDepartmentModal();
          this.loadDepartments();
        },
        error: (err) => console.error('Error creating department:', err)
      });
    }
  }

  deleteDepartment(dept: Department) {
    if (dept.vehicleCount > 0) return;
    
    if (confirm(`Supprimer le département "${dept.name}" ?`)) {
      this.http.delete(`/api/fleet/departments/${dept.id}`).subscribe({
        next: () => this.loadDepartments(),
        error: (err) => console.error('Error deleting department:', err)
      });
    }
  }

  editFuelPrice(fuel: FuelType) {
    this.newPrice.fuelTypeId = fuel.id;
    this.newPrice.price = fuel.currentPrice || null;
  }

  saveFuelPrice() {
    if (!this.newPrice.fuelTypeId || !this.newPrice.price) return;
    
    this.http.post(`/api/fleet/fuel-types/${this.newPrice.fuelTypeId}/price`, {
      pricePerLiter: this.newPrice.price
    }).subscribe({
      next: () => {
        this.newPrice = { fuelTypeId: null, price: null };
        this.loadFuelTypes();
      },
      error: (err) => console.error('Error saving fuel price:', err)
    });
  }

  saveSpeedLimit(vehicle: Vehicle) {
    this.http.patch(`/api/vehicles/${vehicle.id}`, {
      speedLimit: vehicle.speedLimit
    }).subscribe({
      next: () => console.log('Speed limit saved'),
      error: (err) => console.error('Error saving speed limit:', err)
    });
  }

  applyDefaultSpeed() {
    if (confirm(`Appliquer la limite de ${this.defaultSpeedLimit} km/h à tous les véhicules ?`)) {
      this.vehicles.forEach(v => v.speedLimit = this.defaultSpeedLimit);
    }
  }

  // Vehicle Assignment Methods
  openVehicleAssignmentModal(dept: Department) {
    this.assigningDepartment = dept;
    this.showVehicleAssignmentModal = true;
    this.loadAvailableVehicles();
  }

  closeVehicleAssignmentModal() {
    this.showVehicleAssignmentModal = false;
    this.assigningDepartment = null;
    this.availableVehicles = [];
  }

  loadAvailableVehicles() {
    if (!this.assigningDepartment) return;
    
    this.loadingVehicles = true;
    this.http.get<VehicleForAssignment[]>(`/api/fleet/departments/${this.assigningDepartment.id}/available-vehicles`).subscribe({
      next: (data) => {
        this.availableVehicles = data || [];
        this.loadingVehicles = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.availableVehicles = [];
        this.loadingVehicles = false;
        this.cdr.detectChanges();
      }
    });
  }

  toggleVehicleSelection(vehicle: VehicleForAssignment) {
    vehicle.isAssigned = !vehicle.isAssigned;
  }

  selectAllVehicles() {
    this.availableVehicles.forEach(v => v.isAssigned = true);
  }

  deselectAllVehicles() {
    this.availableVehicles.forEach(v => v.isAssigned = false);
  }

  getSelectedVehicleCount(): number {
    return this.availableVehicles.filter(v => v.isAssigned).length;
  }

  saveVehicleAssignments() {
    if (!this.assigningDepartment) return;
    
    const selectedVehicleIds = this.availableVehicles
      .filter(v => v.isAssigned)
      .map(v => v.id);
    
    this.http.put(`/api/fleet/departments/${this.assigningDepartment.id}/vehicles`, {
      vehicleIds: selectedVehicleIds
    }).subscribe({
      next: () => {
        this.closeVehicleAssignmentModal();
        this.loadDepartments();
      },
      error: (err) => console.error('Error assigning vehicles:', err)
    });
  }

  // Parts Pricing Methods
  loadPartCategories() {
    this.http.get<PartCategory[]>('/api/parts/categories').subscribe({
      next: (data) => {
        this.partCategories = data || [];
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.partCategories = [];
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  loadPartPricing() {
    this.http.get<PartPricing[]>('/api/fleet/part-pricing').subscribe({
      next: (data) => {
        this.partPricing = data || [];
        this.cdr.detectChanges();
      },
      error: () => {
        this.partPricing = [];
        this.cdr.detectChanges();
      }
    });
  }

  selectPartCategory(category: PartCategory) {
    this.selectedPartCategory = category;
    this.loadCategoryParts(category.id);
  }

  loadCategoryParts(categoryId: number) {
    this.http.get<VehiclePart[]>(`/api/parts/categories/${categoryId}/parts`).subscribe({
      next: (data) => {
        this.categoryParts = data || [];
        this.cdr.detectChanges();
      },
      error: () => {
        this.categoryParts = [];
        this.cdr.detectChanges();
      }
    });
  }

  openPartPricingModal(part: VehiclePart) {
    this.selectedPart = part;
    const existingPrice = this.partPricing.find(p => p.partId === part.id);
    this.partPricingForm = {
      partId: part.id,
      price: existingPrice?.price || null,
      supplier: existingPrice?.supplier || '',
      notes: existingPrice?.notes || ''
    };
    this.showPartPricingModal = true;
  }

  closePartPricingModal() {
    this.showPartPricingModal = false;
    this.selectedPart = null;
    this.partPricingForm = { partId: null, price: null, supplier: '', notes: '' };
  }

  savePartPricing() {
    if (!this.partPricingForm.partId || this.partPricingForm.price === null) return;

    this.http.post('/api/fleet/part-pricing', {
      partId: this.partPricingForm.partId,
      price: this.partPricingForm.price,
      supplier: this.partPricingForm.supplier || null,
      notes: this.partPricingForm.notes || null
    }).subscribe({
      next: () => {
        this.loadPartPricing();
        this.closePartPricingModal();
      },
      error: (err) => console.error('Error saving part pricing:', err)
    });
  }

  deletePartPricing(pricing: PartPricing) {
    if (confirm(`Supprimer le prix pour "${pricing.partName}" ?`)) {
      this.http.delete(`/api/fleet/part-pricing/${pricing.id}`).subscribe({
        next: () => this.loadPartPricing(),
        error: (err) => console.error('Error deleting part pricing:', err)
      });
    }
  }

  getPartPrice(partId: number): PartPricing | undefined {
    return this.partPricing.find(p => p.partId === partId);
  }
}
