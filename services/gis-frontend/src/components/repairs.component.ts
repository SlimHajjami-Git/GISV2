import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';
import { AppLayoutComponent } from './shared/app-layout.component';
import { ApiService } from '../services/api.service';
import { PdfExportService, PdfGroup } from '../services/pdf-export.service';
import { trigger, transition, style, animate } from '@angular/animations';

type RepairSortKey = 'reference' | 'repairDate' | 'vehicleName' | 'partsCost' | 'laborCost' | 'totalCost' | 'status';

interface RepairPart {
  id?: number;
  partName: string;
  partReference: string;
  quantity: number;
  unitPrice: number;
  subtotal: number;
  notes: string;
}

interface Repair {
  id?: number;
  vehicleId: number;
  vehicleName: string;
  vehiclePlate: string;
  supplierId?: number;
  supplierName?: string;
  reference: string;
  description: string;
  repairDate: string;
  mileageAtRepair?: number;
  laborCost: number;
  partsCost: number;
  totalCost: number;
  status: string;
  invoiceNumber: string;
  notes: string;
  parts: RepairPart[];
}

interface Vehicle {
  id: number;
  name: string;
  plateNumber: string;
  mileage: number;
}

@Component({
  selector: 'app-repairs',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent],
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 }))
      ])
    ]),
    trigger('slideIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateX(20px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateX(0)' }))
      ])
    ])
  ],
  template: `
    <app-layout>
      <div class="repairs-page">
        <!-- Filter Bar -->
        <div class="filter-bar">
          <div class="search-wrapper">
            <svg class="search-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/>
            </svg>
            <input type="text" class="search-input" placeholder="Rechercher une reparation..." [(ngModel)]="searchQuery" (input)="filterRepairs()">
          </div>
          <select class="filter-select" [(ngModel)]="filterVehicle" (change)="filterRepairs()">
            <option value="">Tous les vehicules</option>
            <option *ngFor="let v of vehicles" [value]="v.id">{{ v.name }} - {{ v.plateNumber }}</option>
          </select>
          <select class="filter-select" [(ngModel)]="filterStatus" (change)="filterRepairs()">
            <option value="">Tous les statuts</option>
            <option value="pending">En attente</option>
            <option value="in_progress">En cours</option>
            <option value="completed">Termine</option>
          </select>
          <button class="btn-export" (click)="exportPdf()" [disabled]="filteredRepairs.length === 0" title="Exporter un PDF groupé par véhicule">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
              <polyline points="14 2 14 8 20 8"/>
              <line x1="16" y1="13" x2="8" y2="13"/>
              <line x1="16" y1="17" x2="8" y2="17"/>
              <polyline points="10 9 9 9 8 9"/>
            </svg>
            Exporter PDF
          </button>
          <button class="btn-add" (click)="openAddRepair()">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
            </svg>
            Nouvelle reparation
          </button>
        </div>

        <!-- Stats Bar -->
        <div class="stats-bar">
          <div class="stat-item">
            <div class="stat-icon info">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ stats.totalRepairs }}</span>
              <span class="stat-label">Total reparations</span>
            </div>
          </div>
          <div class="stat-item">
            <div class="stat-icon warning">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ stats.pendingRepairs }}</span>
              <span class="stat-label">En attente</span>
            </div>
          </div>
          <div class="stat-item">
            <div class="stat-icon active">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ stats.completedRepairs }}</span>
              <span class="stat-label">Terminees</span>
            </div>
          </div>
          <div class="stat-item">
            <div class="stat-icon cost">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="12" y1="1" x2="12" y2="23"/><path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ stats.totalCost | number:'1.0-0' }} DT</span>
              <span class="stat-label">Cout total</span>
            </div>
          </div>
        </div>

        <!-- Repairs Table -->
        <div class="table-container" @fadeIn>
          <table class="repairs-table" *ngIf="filteredRepairs.length > 0">
            <thead>
              <tr>
                <th class="col-ref sortable" [class.active]="sortColumn === 'reference'" (click)="toggleSort('reference')">
                  Référence
                  <span class="sort-indicator">{{ getSortIndicator('reference') }}</span>
                </th>
                <th class="col-date sortable" [class.active]="sortColumn === 'repairDate'" (click)="toggleSort('repairDate')">
                  Date
                  <span class="sort-indicator">{{ getSortIndicator('repairDate') }}</span>
                </th>
                <th class="col-vehicle sortable" [class.active]="sortColumn === 'vehicleName'" (click)="toggleSort('vehicleName')">
                  Véhicule
                  <span class="sort-indicator">{{ getSortIndicator('vehicleName') }}</span>
                </th>
                <th class="col-description">Description</th>
                <th class="col-parts-count">Pièces</th>
                <th class="col-parts-cost sortable" [class.active]="sortColumn === 'partsCost'" (click)="toggleSort('partsCost')">
                  Pièces (DT)
                  <span class="sort-indicator">{{ getSortIndicator('partsCost') }}</span>
                </th>
                <th class="col-labor-cost sortable" [class.active]="sortColumn === 'laborCost'" (click)="toggleSort('laborCost')">
                  M. œuvre (DT)
                  <span class="sort-indicator">{{ getSortIndicator('laborCost') }}</span>
                </th>
                <th class="col-total-cost sortable" [class.active]="sortColumn === 'totalCost'" (click)="toggleSort('totalCost')">
                  Total (DT)
                  <span class="sort-indicator">{{ getSortIndicator('totalCost') }}</span>
                </th>
                <th class="col-status sortable" [class.active]="sortColumn === 'status'" (click)="toggleSort('status')">
                  Statut
                  <span class="sort-indicator">{{ getSortIndicator('status') }}</span>
                </th>
                <th class="col-actions">Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr class="repair-row" *ngFor="let repair of getSortedRepairs()" (click)="viewRepair(repair)">
                <td class="col-ref">
                  <div class="ref-cell">
                    <span class="repair-icon">🔧</span>
                    <span class="repair-ref">{{ repair.reference || '—' }}</span>
                  </div>
                </td>
                <td class="col-date">{{ repair.repairDate | date:'dd/MM/yyyy' }}</td>
                <td class="col-vehicle">
                  <div class="vehicle-cell">
                    <span class="vehicle-name">{{ repair.vehicleName }}</span>
                    <span class="vehicle-plate">{{ repair.vehiclePlate }}</span>
                  </div>
                </td>
                <td class="col-description">
                  <span class="description-text" [title]="repair.description">{{ repair.description || '—' }}</span>
                </td>
                <td class="col-parts-count">
                  <span class="parts-badge" *ngIf="repair.parts.length > 0">{{ repair.parts.length }}</span>
                  <span class="parts-badge empty" *ngIf="repair.parts.length === 0">—</span>
                </td>
                <td class="col-parts-cost num">{{ repair.partsCost | number:'1.2-2' }}</td>
                <td class="col-labor-cost num">{{ repair.laborCost | number:'1.2-2' }}</td>
                <td class="col-total-cost num strong">{{ repair.totalCost | number:'1.2-2' }}</td>
                <td class="col-status">
                  <span class="status-badge" [class]="repair.status">{{ getStatusLabel(repair.status) }}</span>
                </td>
                <td class="col-actions">
                  <div class="actions-cell">
                    <button class="btn-action view" (click)="viewRepair(repair); $event.stopPropagation()" title="Détails">
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>
                      </svg>
                    </button>
                    <button class="btn-action edit" (click)="editRepair(repair); $event.stopPropagation()" title="Modifier">
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                        <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                      </svg>
                    </button>
                    <button class="btn-action delete" (click)="confirmDelete(repair); $event.stopPropagation()" title="Supprimer">
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                      </svg>
                    </button>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>

          <div class="empty-state" *ngIf="filteredRepairs.length === 0">
            <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
              <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>
            </svg>
            <h3>Aucune reparation trouvee</h3>
            <p>Modifiez vos filtres ou ajoutez une nouvelle reparation</p>
          </div>
        </div>
      </div>

      <!-- Add/Edit Repair Panel -->
      <div class="overlay" *ngIf="isPanelOpen" @fadeIn (click)="closePanel()">
        <div class="panel" @slideIn (click)="$event.stopPropagation()">
          <div class="panel-header">
            <h2>{{ editingRepair ? 'Modifier la reparation' : 'Nouvelle reparation' }}</h2>
            <button class="btn-close" (click)="closePanel()">×</button>
          </div>

          <div class="panel-body">
            <!-- Vehicle Selection -->
            <div class="form-section">
              <h4>Vehicule</h4>
              <div class="vehicle-select-wrapper">
                <select class="form-control" [(ngModel)]="form.vehicleId" (change)="onVehicleChange()">
                  <option value="">Selectionnez un vehicule</option>
                  <option *ngFor="let v of vehicles" [value]="v.id">{{ v.name }} - {{ v.plateNumber }}</option>
                </select>
              </div>
              <div class="vehicle-info-box" *ngIf="selectedVehicle">
                <span class="info-label">Kilometrage actuel:</span>
                <span class="info-value">{{ selectedVehicle.mileage || 0 }} km</span>
              </div>
            </div>

            <!-- Supplier Selection -->
            <div class="form-section">
              <h4>Fournisseur / Garage</h4>
              <div class="supplier-select-row">
                <select class="form-control" [(ngModel)]="form.supplierId" style="flex:1">
                  <option value="">-- Aucun fournisseur --</option>
                  <option *ngFor="let s of suppliers" [value]="s.id">{{ s.name }} <span *ngIf="s.type">({{ s.type }})</span></option>
                </select>
                <button class="btn-add-supplier" (click)="showAddSupplier = true" title="Ajouter un fournisseur">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
                  </svg>
                </button>
              </div>
              <!-- Quick add supplier inline -->
              <div class="quick-add-supplier" *ngIf="showAddSupplier">
                <input type="text" class="form-control" [(ngModel)]="newSupplierName" placeholder="Nom du fournisseur">
                <select class="form-control" [(ngModel)]="newSupplierType">
                  <option value="garage">Garage</option>
                  <option value="parts">Pièces</option>
                  <option value="tires">Pneus</option>
                  <option value="general">Général</option>
                </select>
                <button class="btn-save-supplier" (click)="addNewSupplier()" [disabled]="!newSupplierName">Ajouter</button>
                <button class="btn-cancel-supplier" (click)="showAddSupplier = false">Annuler</button>
              </div>
            </div>

            <!-- Repair Details -->
            <div class="form-section">
              <h4>Details de la reparation</h4>
              <div class="form-row">
                <div class="form-group">
                  <label>Date *</label>
                  <input type="date" class="form-control" [(ngModel)]="form.repairDate">
                </div>
                <div class="form-group">
                  <label>Kilometrage</label>
                  <input type="number" class="form-control" [(ngModel)]="form.mileageAtRepair" placeholder="km">
                </div>
              </div>
              <div class="form-group">
                <label>Description</label>
                <textarea class="form-control" [(ngModel)]="form.description" rows="2" placeholder="Decrivez la reparation..."></textarea>
              </div>
              <div class="form-row">
                <div class="form-group">
                  <label>N° Facture</label>
                  <input type="text" class="form-control" [(ngModel)]="form.invoiceNumber" placeholder="FAC-XXXX">
                </div>
                <div class="form-group" *ngIf="editingRepair">
                  <label>Statut</label>
                  <select class="form-control" [(ngModel)]="form.status">
                    <option value="pending">En attente</option>
                    <option value="in_progress">En cours</option>
                    <option value="completed">Termine</option>
                  </select>
                </div>
              </div>
            </div>

            <!-- Parts Section -->
            <div class="form-section">
              <div class="section-header">
                <h4>Pieces detachees</h4>
                <button class="btn-add-part" (click)="addPart()">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
                  </svg>
                  Ajouter
                </button>
              </div>

              <div class="parts-table" *ngIf="form.parts.length > 0">
                <div class="parts-header">
                  <span class="col-name">Designation</span>
                  <span class="col-ref">Reference</span>
                  <span class="col-qty">Qte</span>
                  <span class="col-price">Prix unit.</span>
                  <span class="col-subtotal">Sous-total</span>
                  <span class="col-action"></span>
                </div>
                <div class="parts-row" *ngFor="let part of form.parts; let i = index">
                  <input class="col-name" [(ngModel)]="part.partName" placeholder="Nom de la piece">
                  <input class="col-ref" [(ngModel)]="part.partReference" placeholder="Ref">
                  <input class="col-qty" type="number" [(ngModel)]="part.quantity" min="1" (change)="calculatePartSubtotal(part)">
                  <input class="col-price" type="number" [(ngModel)]="part.unitPrice" min="0" step="0.01" (change)="calculatePartSubtotal(part)">
                  <span class="col-subtotal">{{ part.subtotal | number:'1.2-2' }} DT</span>
                  <button class="col-action btn-remove" (click)="removePart(i)">×</button>
                </div>
              </div>

              <div class="empty-parts" *ngIf="form.parts.length === 0">
                <p>Aucune piece ajoutee</p>
              </div>
            </div>

            <!-- Labor Cost -->
            <div class="form-section">
              <h4>Main d'oeuvre</h4>
              <div class="form-group">
                <label>Cout main d'oeuvre (DT)</label>
                <input type="number" class="form-control" [(ngModel)]="form.laborCost" min="0" step="0.01" placeholder="0.00">
              </div>
            </div>

            <!-- Cost Summary -->
            <div class="cost-summary">
              <div class="summary-row">
                <span>Total pieces</span>
                <span>{{ getPartsCost() | number:'1.2-2' }} DT</span>
              </div>
              <div class="summary-row">
                <span>Main d'oeuvre</span>
                <span>{{ form.laborCost | number:'1.2-2' }} DT</span>
              </div>
              <div class="summary-row total">
                <span>TOTAL</span>
                <span>{{ getTotalCost() | number:'1.2-2' }} DT</span>
              </div>
            </div>

            <!-- Notes -->
            <div class="form-section">
              <div class="form-group">
                <label>Notes</label>
                <textarea class="form-control" [(ngModel)]="form.notes" rows="2" placeholder="Notes supplementaires..."></textarea>
              </div>
            </div>
          </div>

          <div class="panel-footer">
            <button class="btn-cancel" (click)="closePanel()">Annuler</button>
            <button class="btn-save" (click)="saveRepair()" [disabled]="!isFormValid()">
              {{ editingRepair ? 'Mettre a jour' : 'Enregistrer' }}
            </button>
          </div>
        </div>
      </div>

      <!-- View Details Panel -->
      <div class="overlay" *ngIf="viewingRepair" @fadeIn (click)="closeView()">
        <div class="panel" @slideIn (click)="$event.stopPropagation()">
          <div class="panel-header view">
            <h2>Details de la reparation</h2>
            <button class="btn-close" (click)="closeView()">×</button>
          </div>

          <div class="panel-body">
            <div class="detail-header">
              <div class="detail-ref">{{ viewingRepair.reference }}</div>
              <span class="status-badge large" [class]="viewingRepair.status">{{ getStatusLabel(viewingRepair.status) }}</span>
            </div>

            <div class="detail-section">
              <h4>Vehicule</h4>
              <div class="detail-row">
                <span class="detail-label">Vehicule</span>
                <span class="detail-value">{{ viewingRepair.vehicleName }} - {{ viewingRepair.vehiclePlate }}</span>
              </div>
              <div class="detail-row" *ngIf="viewingRepair.mileageAtRepair">
                <span class="detail-label">Kilometrage</span>
                <span class="detail-value">{{ viewingRepair.mileageAtRepair || 0 }} km</span>
              </div>
            </div>

            <div class="detail-section">
              <h4>Reparation</h4>
              <div class="detail-row">
                <span class="detail-label">Date</span>
                <span class="detail-value">{{ viewingRepair.repairDate | date:'dd/MM/yyyy' }}</span>
              </div>
              <div class="detail-row" *ngIf="viewingRepair.description">
                <span class="detail-label">Description</span>
                <span class="detail-value">{{ viewingRepair.description }}</span>
              </div>
              <div class="detail-row" *ngIf="viewingRepair.invoiceNumber">
                <span class="detail-label">N° Facture</span>
                <span class="detail-value">{{ viewingRepair.invoiceNumber }}</span>
              </div>
            </div>

            <div class="detail-section" *ngIf="viewingRepair.parts.length > 0">
              <h4>Pieces ({{ viewingRepair.parts.length }})</h4>
              <div class="detail-parts-table">
                <div class="detail-parts-row header">
                  <span>Designation</span>
                  <span>Qte</span>
                  <span>Prix unit.</span>
                  <span>Sous-total</span>
                </div>
                <div class="detail-parts-row" *ngFor="let part of viewingRepair.parts">
                  <span>{{ part.partName }}<small *ngIf="part.partReference"> ({{ part.partReference }})</small></span>
                  <span>{{ part.quantity }}</span>
                  <span>{{ part.unitPrice | number:'1.2-2' }} DT</span>
                  <span>{{ part.subtotal | number:'1.2-2' }} DT</span>
                </div>
              </div>
            </div>

            <div class="detail-costs">
              <div class="detail-cost-row">
                <span>Total pieces</span>
                <span>{{ viewingRepair.partsCost | number:'1.2-2' }} DT</span>
              </div>
              <div class="detail-cost-row">
                <span>Main d'oeuvre</span>
                <span>{{ viewingRepair.laborCost | number:'1.2-2' }} DT</span>
              </div>
              <div class="detail-cost-row total">
                <span>TOTAL</span>
                <span>{{ viewingRepair.totalCost | number:'1.2-2' }} DT</span>
              </div>
            </div>
          </div>

          <div class="panel-footer">
            <button class="btn-cancel" (click)="closeView()">Fermer</button>
            <button class="btn-edit" (click)="editFromView()">Modifier</button>
          </div>
        </div>
      </div>

      <!-- Delete Confirmation Modal -->
      <div class="modal-overlay" *ngIf="showDeleteConfirm" (click)="cancelDelete()">
        <div class="modal-content" (click)="$event.stopPropagation()">
          <div class="modal-header">
            <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="#dc2626" stroke-width="2">
              <circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/>
            </svg>
            <h3>Confirmer la suppression</h3>
          </div>
          <p>Etes-vous sur de vouloir supprimer la reparation <strong>{{ repairToDelete?.reference }}</strong> ?</p>
          <p class="warning-text">Cette action est irreversible.</p>
          <div class="modal-actions">
            <button class="btn-cancel" (click)="cancelDelete()">Annuler</button>
            <button class="btn-delete" (click)="deleteRepair()">Supprimer</button>
          </div>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    .repairs-page { flex:1; background:#f1f5f9; display:flex; flex-direction:column; min-height:calc(100vh - 42px); }

    .filter-bar { display:flex; align-items:center; gap:12px; padding:10px 14px; background:white; border-bottom:1px solid #e2e8f0; }
    .search-wrapper { position:relative; flex:1; max-width:300px; }
    .search-icon { position:absolute; left:10px; top:50%; transform:translateY(-50%); color:#94a3b8; }
    .search-input { width:100%; padding:6px 10px 6px 32px; font-size:12px; border:1px solid #e2e8f0; border-radius:3px; }
    .search-input:focus { outline:none; border-color:#3b82f6; }
    .filter-select { padding:6px 10px; background:white; border:1px solid #e2e8f0; border-radius:3px; font-size:12px; cursor:pointer; }
    .filter-select:focus { outline:none; border-color:#3b82f6; }
    .btn-export { display:flex; align-items:center; gap:6px; padding:6px 12px; background:white; color:#475569; border:1px solid #e2e8f0; border-radius:3px; font-size:12px; font-weight:500; cursor:pointer; margin-left:auto; transition:all .15s; }
    .btn-export:hover:not(:disabled) { background:#f1f5f9; border-color:#cbd5e1; color:#1e293b; }
    .btn-export:disabled { opacity:.4; cursor:not-allowed; }
    .btn-add { display:flex; align-items:center; gap:6px; padding:6px 12px; background:#3b82f6; color:white; border:none; border-radius:3px; font-size:12px; font-weight:500; cursor:pointer; }
    .btn-add:hover { background:#2563eb; }

    .stats-bar { display:flex; gap:16px; padding:12px 14px; background:white; border-bottom:1px solid #e2e8f0; }
    .stat-item { display:flex; align-items:center; gap:10px; padding:8px 14px; background:#f8fafc; border-radius:6px; }
    .stat-icon { width:32px; height:32px; border-radius:6px; display:flex; align-items:center; justify-content:center; }
    .stat-icon.active { background:#dcfce7; color:#16a34a; }
    .stat-icon.warning { background:#fef3c7; color:#d97706; }
    .stat-icon.info { background:#dbeafe; color:#2563eb; }
    .stat-icon.cost { background:#f3e8ff; color:#7c3aed; }
    .stat-content { display:flex; flex-direction:column; }
    .stat-value { font-size:16px; font-weight:600; color:#1e293b; }
    .stat-label { font-size:11px; color:#64748b; }

    /* Table */
    .table-container { flex:1; padding:16px 24px; overflow-x:auto; }
    .repairs-table {
      width:100%;
      border-collapse:separate;
      border-spacing:0;
      background:#fff;
      border-radius:10px;
      border:1px solid #e2e8f0;
      overflow:hidden;
    }
    .repairs-table thead { background:#f8fafc; }
    .repairs-table th {
      padding:10px 12px;
      font-size:10px;
      font-weight:600;
      color:#64748b;
      text-transform:uppercase;
      letter-spacing:0.5px;
      text-align:left;
      border-bottom:1px solid #e2e8f0;
      white-space:nowrap;
      user-select:none;
    }
    .repairs-table th.sortable {
      cursor:pointer;
      transition:color .15s;
    }
    .repairs-table th.sortable:hover { color:#1e293b; }
    .repairs-table th.sortable.active { color:#3b82f6; }
    .sort-indicator { margin-left:4px; font-size:9px; color:#cbd5e1; }
    .repairs-table th.sortable.active .sort-indicator { color:#3b82f6; }

    .repairs-table td {
      padding:12px;
      font-size:12px;
      color:#334155;
      border-bottom:1px solid #f1f5f9;
      vertical-align:middle;
    }
    .repair-row { cursor:pointer; transition:background .15s; }
    .repair-row:hover { background:#f8fafc; }
    .repair-row:last-child td { border-bottom:none; }

    .ref-cell { display:flex; align-items:center; gap:8px; }
    .repair-icon { font-size:16px; line-height:1; }
    .repair-ref { font-size:12px; font-weight:600; color:#1e293b; font-family:monospace; }

    .vehicle-cell { display:flex; flex-direction:column; gap:2px; }
    .vehicle-name { font-size:12px; font-weight:500; color:#1e293b; }
    .vehicle-plate { font-size:10px; font-family:monospace; background:#e2e8f0; padding:1px 5px; border-radius:3px; color:#64748b; align-self:flex-start; }

    .description-text {
      display:-webkit-box;
      -webkit-line-clamp:2;
      -webkit-box-orient:vertical;
      overflow:hidden;
      max-width:220px;
      color:#64748b;
      font-size:12px;
      line-height:1.4;
    }

    .parts-badge { display:inline-block; background:#e2e8f0; color:#475569; padding:2px 8px; border-radius:10px; font-size:11px; font-weight:600; }
    .parts-badge.empty { background:transparent; color:#cbd5e1; }

    td.num { text-align:right; font-variant-numeric:tabular-nums; font-family:monospace; }
    td.num.strong { font-weight:700; color:#16a34a; font-size:12.5px; }

    .status-badge { display:inline-block; padding:3px 8px; border-radius:4px; font-size:10px; font-weight:600; white-space:nowrap; }
    .status-badge.completed { background:#dcfce7; color:#16a34a; }
    .status-badge.pending { background:#fef3c7; color:#d97706; }
    .status-badge.in_progress { background:#dbeafe; color:#2563eb; }
    .status-badge.large { font-size:12px; padding:6px 12px; }

    .actions-cell { display:flex; align-items:center; gap:4px; }
    .btn-action { width:26px; height:26px; border:1px solid #e2e8f0; border-radius:4px; background:white; display:flex; align-items:center; justify-content:center; cursor:pointer; transition:all .15s; }
    .btn-action.view { color:#2563eb; }
    .btn-action.view:hover { background:#dbeafe; border-color:#2563eb; }
    .btn-action.edit { color:#f59e0b; }
    .btn-action.edit:hover { background:#fef3c7; border-color:#f59e0b; }
    .btn-action.delete { color:#dc2626; }
    .btn-action.delete:hover { background:#fee2e2; border-color:#dc2626; }

    /* Column widths */
    .col-ref { min-width:140px; }
    .col-date { min-width:100px; }
    .col-vehicle { min-width:160px; }
    .col-description { min-width:180px; max-width:240px; }
    .col-parts-count { min-width:60px; text-align:center; }
    .col-parts-cost, .col-labor-cost, .col-total-cost { min-width:100px; text-align:right; }
    .col-status { min-width:100px; }
    .col-actions { min-width:100px; }

    .supplier-select-row { display:flex; gap:8px; align-items:center; }
    .btn-add-supplier { width:34px; height:34px; border:1px solid #e2e8f0; border-radius:6px; background:#f0fdf4; color:#16a34a; display:flex; align-items:center; justify-content:center; cursor:pointer; flex-shrink:0; }
    .btn-add-supplier:hover { background:#dcfce7; border-color:#16a34a; }
    .quick-add-supplier { display:flex; gap:6px; margin-top:8px; align-items:center; padding:10px; background:#f8fafc; border-radius:6px; border:1px dashed #cbd5e1; flex-wrap:wrap; }
    .quick-add-supplier .form-control { flex:1; min-width:120px; }
    .btn-save-supplier { padding:6px 12px; background:#16a34a; color:white; border:none; border-radius:4px; font-size:11px; font-weight:500; cursor:pointer; white-space:nowrap; }
    .btn-save-supplier:hover { background:#15803d; }
    .btn-save-supplier:disabled { opacity:0.5; cursor:not-allowed; }
    .btn-cancel-supplier { padding:6px 12px; background:white; border:1px solid #e2e8f0; border-radius:4px; font-size:11px; color:#64748b; cursor:pointer; white-space:nowrap; }
    .btn-cancel-supplier:hover { background:#f1f5f9; }

    .empty-state { grid-column:1/-1; text-align:center; padding:60px 20px; color:#64748b; }
    .empty-state svg { margin-bottom:16px; opacity:.5; }
    .empty-state h3 { margin:0 0 8px; color:#1e293b; }
    .empty-state p { margin:0; font-size:13px; }

    .overlay { position:fixed; top:0; left:0; right:0; bottom:0; background:rgba(0,0,0,0.5); display:flex; justify-content:flex-end; z-index:1000; }
    .panel { width:560px; max-width:100%; background:white; display:flex; flex-direction:column; box-shadow:-4px 0 20px rgba(0,0,0,0.15); }
    .panel-header { display:flex; justify-content:space-between; align-items:center; padding:16px 20px; background:#1e3a5f; color:white; }
    .panel-header.view { background:#2563eb; }
    .panel-header h2 { margin:0; font-size:16px; font-weight:600; }
    .btn-close { width:32px; height:32px; border:none; background:rgba(255,255,255,0.1); color:white; border-radius:6px; font-size:20px; cursor:pointer; }
    .btn-close:hover { background:rgba(255,255,255,0.2); }

    .panel-body { flex:1; overflow-y:auto; padding:20px; }
    .form-section { margin-bottom:20px; }
    .form-section h4 { margin:0 0 12px; font-size:13px; font-weight:600; color:#1e293b; }
    .section-header { display:flex; justify-content:space-between; align-items:center; margin-bottom:12px; }
    .section-header h4 { margin:0; }
    .form-row { display:grid; grid-template-columns:1fr 1fr; gap:12px; }
    .form-group { margin-bottom:12px; }
    .form-group label { display:block; font-size:11px; font-weight:500; color:#64748b; margin-bottom:4px; }
    .form-control { width:100%; padding:8px 10px; border:1px solid #e2e8f0; border-radius:6px; font-size:13px; }
    .form-control:focus { outline:none; border-color:#3b82f6; }

    .vehicle-select-wrapper { margin-bottom:8px; }
    .vehicle-info-box { display:flex; gap:8px; padding:8px 12px; background:#f8fafc; border-radius:6px; font-size:12px; }
    .info-label { color:#64748b; }
    .info-value { font-weight:600; color:#1e293b; }

    .btn-add-part { display:flex; align-items:center; gap:4px; padding:6px 10px; background:#f0fdf4; color:#16a34a; border:1px solid #86efac; border-radius:4px; font-size:11px; cursor:pointer; }
    .btn-add-part:hover { background:#dcfce7; }

    .parts-table { border:1px solid #e2e8f0; border-radius:6px; overflow:hidden; }
    .parts-header, .parts-row { display:grid; grid-template-columns:1fr 80px 50px 80px 90px 36px; gap:8px; padding:8px 10px; align-items:center; }
    .parts-header { background:#f8fafc; font-size:10px; font-weight:600; color:#64748b; border-bottom:1px solid #e2e8f0; }
    .parts-row { border-bottom:1px solid #f1f5f9; }
    .parts-row:last-child { border-bottom:none; }
    .parts-row input { padding:6px 8px; border:1px solid #e2e8f0; border-radius:4px; font-size:12px; }
    .parts-row input:focus { outline:none; border-color:#3b82f6; }
    .col-subtotal { font-size:12px; font-weight:500; color:#1e293b; text-align:right; }
    .btn-remove { width:28px; height:28px; border:1px solid #fee2e2; background:white; color:#dc2626; border-radius:4px; cursor:pointer; font-size:16px; }
    .btn-remove:hover { background:#fee2e2; }

    .empty-parts { padding:20px; text-align:center; color:#94a3b8; font-size:12px; background:#f8fafc; border-radius:6px; }

    .cost-summary { background:#1e3a5f; color:white; padding:16px; border-radius:8px; margin-bottom:16px; }
    .summary-row { display:flex; justify-content:space-between; padding:6px 0; font-size:13px; }
    .summary-row.total { border-top:1px solid rgba(255,255,255,0.2); margin-top:8px; padding-top:12px; font-size:16px; font-weight:700; }

    .panel-footer { display:flex; justify-content:flex-end; gap:10px; padding:16px 20px; border-top:1px solid #e2e8f0; background:#f8fafc; }
    .btn-cancel { padding:8px 16px; background:white; border:1px solid #e2e8f0; border-radius:6px; font-size:13px; cursor:pointer; }
    .btn-cancel:hover { background:#f1f5f9; }
    .btn-save { padding:8px 20px; background:#3b82f6; color:white; border:none; border-radius:6px; font-size:13px; font-weight:500; cursor:pointer; }
    .btn-save:hover { background:#2563eb; }
    .btn-save:disabled { background:#94a3b8; cursor:not-allowed; }
    .btn-edit { padding:8px 20px; background:#f59e0b; color:white; border:none; border-radius:6px; font-size:13px; font-weight:500; cursor:pointer; }
    .btn-edit:hover { background:#d97706; }

    .detail-header { display:flex; justify-content:space-between; align-items:center; margin-bottom:20px; }
    .detail-ref { font-size:20px; font-weight:700; color:#1e293b; }
    .detail-section { margin-bottom:20px; }
    .detail-section h4 { margin:0 0 12px; font-size:12px; font-weight:600; color:#64748b; text-transform:uppercase; }
    .detail-row { display:flex; justify-content:space-between; padding:8px 0; border-bottom:1px solid #f1f5f9; }
    .detail-label { font-size:13px; color:#64748b; }
    .detail-value { font-size:13px; font-weight:500; color:#1e293b; }

    .detail-parts-table { border:1px solid #e2e8f0; border-radius:6px; overflow:hidden; }
    .detail-parts-row { display:grid; grid-template-columns:1fr 50px 80px 90px; gap:8px; padding:10px 12px; font-size:12px; border-bottom:1px solid #f1f5f9; }
    .detail-parts-row.header { background:#f8fafc; font-weight:600; color:#64748b; }
    .detail-parts-row:last-child { border-bottom:none; }
    .detail-parts-row small { color:#94a3b8; }

    .detail-costs { background:#f8fafc; padding:16px; border-radius:8px; }
    .detail-cost-row { display:flex; justify-content:space-between; padding:6px 0; font-size:13px; }
    .detail-cost-row.total { border-top:1px solid #e2e8f0; margin-top:8px; padding-top:12px; font-size:16px; font-weight:700; color:#16a34a; }

    .modal-overlay { position:fixed; top:0; left:0; right:0; bottom:0; background:rgba(0,0,0,0.5); display:flex; align-items:center; justify-content:center; z-index:1100; }
    .modal-content { background:white; border-radius:12px; padding:24px; max-width:400px; width:90%; }
    .modal-header { display:flex; align-items:center; gap:12px; margin-bottom:16px; }
    .modal-header h3 { margin:0; font-size:16px; color:#1e293b; }
    .modal-content p { margin:0 0 8px; font-size:13px; color:#64748b; }
    .warning-text { color:#dc2626; font-size:12px; }
    .modal-actions { display:flex; justify-content:flex-end; gap:10px; margin-top:20px; }
    .btn-delete { padding:8px 16px; background:#dc2626; color:white; border:none; border-radius:6px; font-size:13px; cursor:pointer; }
    .btn-delete:hover { background:#b91c1c; }

    @media (max-width:768px) {
      .filter-bar { flex-wrap:wrap; }
      .search-wrapper { max-width:100%; order:1; flex-basis:100%; }
      .repairs-list { grid-template-columns:1fr; }
      .panel { width:100%; }
      .form-row { grid-template-columns:1fr; }
      .parts-header, .parts-row { grid-template-columns:1fr 60px 40px 60px 70px 30px; font-size:10px; }
    }
  `]
})
export class RepairsComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  repairs: Repair[] = [];
  filteredRepairs: Repair[] = [];
  vehicles: Vehicle[] = [];
  selectedVehicle: Vehicle | null = null;

  searchQuery = '';
  filterVehicle = '';
  filterStatus = '';

  sortColumn: RepairSortKey = 'repairDate';
  sortDirection: 'asc' | 'desc' = 'desc';

  stats = { totalRepairs: 0, pendingRepairs: 0, completedRepairs: 0, totalCost: 0 };

  isPanelOpen = false;
  editingRepair: Repair | null = null;
  viewingRepair: Repair | null = null;

  showDeleteConfirm = false;
  repairToDelete: Repair | null = null;

  form = this.getEmptyForm();

  // Suppliers
  suppliers: { id: number; name: string; type: string }[] = [];
  showAddSupplier = false;
  newSupplierName = '';
  newSupplierType = 'garage';

  constructor(
    private apiService: ApiService,
    private pdfService: PdfExportService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit() {
    this.loadVehicles();
    this.loadRepairs();
    this.loadSuppliers();
  }

  getEmptyForm() {
    return {
      vehicleId: '',
      supplierId: '' as string,
      repairDate: new Date().toISOString().split('T')[0],
      mileageAtRepair: null as number | null,
      description: '',
      invoiceNumber: '',
      status: 'completed',
      laborCost: 0,
      notes: '',
      parts: [] as RepairPart[]
    };
  }

  loadVehicles() {
    this.apiService.getVehicles().pipe(takeUntil(this.destroy$)).subscribe({
      next: (vehicles) => {
        this.vehicles = vehicles.map(v => ({
          id: v.id,
          name: v.name,
          plateNumber: v.plateNumber,
          mileage: v.mileage || 0
        }));
      },
      error: () => {
        this.vehicles = [
          { id: 1, name: 'Peugeot 208', plateNumber: '245 TUN 7890', mileage: 52000 },
          { id: 2, name: 'Renault Clio', plateNumber: '189 TUN 4521', mileage: 78500 },
          { id: 3, name: 'Citroen Berlingo', plateNumber: '312 TUN 1122', mileage: 125000 }
        ];
      }
    });
  }

  loadSuppliers() {
    this.apiService.getSuppliers({ pageSize: 200, isActive: true }).pipe(takeUntil(this.destroy$)).subscribe({
      next: (result) => {
        this.suppliers = result.items.map(s => ({ id: s.id, name: s.name, type: s.type }));
        this.cdr.detectChanges();
      },
      error: () => { this.suppliers = []; }
    });
  }

  addNewSupplier() {
    if (!this.newSupplierName) return;
    this.apiService.createSupplier({ name: this.newSupplierName, type: this.newSupplierType }).pipe(takeUntil(this.destroy$)).subscribe({
      next: (id) => {
        this.suppliers.push({ id, name: this.newSupplierName, type: this.newSupplierType });
        this.form.supplierId = id.toString();
        this.newSupplierName = '';
        this.showAddSupplier = false;
        this.cdr.detectChanges();
      },
      error: (err) => console.error('Error creating supplier:', err)
    });
  }

  loadRepairs() {
    this.apiService.getRepairs({ pageSize: 100 }).pipe(takeUntil(this.destroy$)).subscribe({
      next: (result) => {
        this.repairs = result.items.map(r => ({
          id: r.id,
          vehicleId: r.vehicleId,
          vehicleName: r.vehicleName || '',
          vehiclePlate: r.vehiclePlate || '',
          supplierId: r.supplierId,
          supplierName: r.supplierName,
          reference: r.reference || '',
          description: r.description || '',
          repairDate: r.repairDate,
          mileageAtRepair: r.mileageAtRepair,
          laborCost: r.laborCost,
          partsCost: r.partsCost,
          totalCost: r.totalCost,
          status: r.status,
          invoiceNumber: r.invoiceNumber || '',
          notes: r.notes || '',
          parts: (r.parts || []).map(p => ({
            id: p.id,
            partName: p.partName,
            partReference: p.partReference || '',
            quantity: p.quantity,
            unitPrice: p.unitPrice,
            subtotal: p.subtotal,
            notes: p.notes || ''
          }))
        }));
        this.filterRepairs();
        this.calculateStats();
        this.cdr.detectChanges();
      },
      error: (err) => { console.error('Error loading repairs:', err); this.cdr.detectChanges(); }
    });
  }

  filterRepairs() {
    let result = [...this.repairs];
    if (this.searchQuery) {
      const q = this.searchQuery.toLowerCase();
      result = result.filter(r =>
        r.reference.toLowerCase().includes(q) ||
        r.vehicleName.toLowerCase().includes(q) ||
        r.description.toLowerCase().includes(q)
      );
    }
    if (this.filterVehicle) {
      result = result.filter(r => r.vehicleId === +this.filterVehicle);
    }
    if (this.filterStatus) {
      result = result.filter(r => r.status === this.filterStatus);
    }
    this.filteredRepairs = result;
  }

  toggleSort(column: RepairSortKey) {
    if (this.sortColumn === column) {
      this.sortDirection = this.sortDirection === 'asc' ? 'desc' : 'asc';
    } else {
      this.sortColumn = column;
      this.sortDirection = column === 'repairDate' ? 'desc' : 'asc';
    }
  }

  getSortIndicator(column: RepairSortKey): string {
    if (this.sortColumn !== column) return '⇅';
    return this.sortDirection === 'asc' ? '▲' : '▼';
  }

  getSortedRepairs(): Repair[] {
    const data = [...this.filteredRepairs];
    const dir = this.sortDirection === 'asc' ? 1 : -1;
    const col = this.sortColumn;
    return data.sort((a, b) => {
      let va: any;
      let vb: any;
      switch (col) {
        case 'reference':
          va = (a.reference || '').toLowerCase();
          vb = (b.reference || '').toLowerCase();
          break;
        case 'repairDate':
          va = a.repairDate ? new Date(a.repairDate).getTime() : 0;
          vb = b.repairDate ? new Date(b.repairDate).getTime() : 0;
          break;
        case 'vehicleName':
          va = `${a.vehicleName} ${a.vehiclePlate}`.toLowerCase();
          vb = `${b.vehicleName} ${b.vehiclePlate}`.toLowerCase();
          break;
        case 'partsCost':
          va = a.partsCost || 0;
          vb = b.partsCost || 0;
          break;
        case 'laborCost':
          va = a.laborCost || 0;
          vb = b.laborCost || 0;
          break;
        case 'totalCost':
          va = a.totalCost || 0;
          vb = b.totalCost || 0;
          break;
        case 'status':
          va = a.status || '';
          vb = b.status || '';
          break;
      }
      if (va < vb) return -1 * dir;
      if (va > vb) return 1 * dir;
      return 0;
    });
  }

  exportPdf() {
    if (this.filteredRepairs.length === 0) return;

    // Group repairs by vehicleId
    const vehicleMap = new Map<number, { repairs: Repair[]; label: string; subtitle: string }>();
    for (const r of this.filteredRepairs) {
      const existing = vehicleMap.get(r.vehicleId);
      if (existing) {
        existing.repairs.push(r);
      } else {
        vehicleMap.set(r.vehicleId, {
          repairs: [r],
          label: r.vehicleName || `Véhicule #${r.vehicleId}`,
          subtitle: r.vehiclePlate || ''
        });
      }
    }

    // Sort vehicles alphabetically by name
    const sortedVehicles = Array.from(vehicleMap.values()).sort((a, b) =>
      a.label.localeCompare(b.label, 'fr')
    );

    // Build groups
    const groups: PdfGroup[] = [];
    let grandTotalParts = 0;
    let grandTotalLabor = 0;
    let grandTotal = 0;
    let grandCount = 0;

    for (const v of sortedVehicles) {
      // Sort repairs by date desc within each vehicle
      const rs = [...v.repairs].sort((a, b) =>
        new Date(b.repairDate).getTime() - new Date(a.repairDate).getTime()
      );
      const subParts = rs.reduce((s, r) => s + (r.partsCost || 0), 0);
      const subLabor = rs.reduce((s, r) => s + (r.laborCost || 0), 0);
      const subTotal = rs.reduce((s, r) => s + (r.totalCost || 0), 0);

      grandTotalParts += subParts;
      grandTotalLabor += subLabor;
      grandTotal += subTotal;
      grandCount += rs.length;

      groups.push({
        groupLabel: v.label,
        groupSubtitle: v.subtitle,
        rows: rs.map(r => ({
          reference: r.reference || '—',
          date: this.formatDate(r.repairDate),
          description: r.description || '—',
          parts: r.parts.length > 0 ? r.parts.length.toString() : '—',
          partsCost: (r.partsCost || 0).toFixed(2),
          laborCost: (r.laborCost || 0).toFixed(2),
          totalCost: (r.totalCost || 0).toFixed(2),
          status: this.getStatusLabel(r.status)
        })),
        subtotal: `${rs.length} réparation(s) — Pièces: ${subParts.toFixed(2)} DT | M. œuvre: ${subLabor.toFixed(2)} DT | Total: ${subTotal.toFixed(2)} DT`
      });
    }

    this.pdfService.exportGroupedReport({
      title: 'Historique des réparations',
      subtitle: `${grandCount} réparation(s) — ${sortedVehicles.length} véhicule(s)`,
      columns: [
        { header: 'Référence', dataKey: 'reference' },
        { header: 'Date', dataKey: 'date' },
        { header: 'Description', dataKey: 'description' },
        { header: 'Pièces', dataKey: 'parts' },
        { header: 'Pièces (DT)', dataKey: 'partsCost' },
        { header: 'M. œuvre (DT)', dataKey: 'laborCost' },
        { header: 'Total (DT)', dataKey: 'totalCost' },
        { header: 'Statut', dataKey: 'status' }
      ],
      groups,
      grandTotal: `TOTAL GÉNÉRAL — ${grandCount} réparation(s) | Pièces: ${grandTotalParts.toFixed(2)} DT | M. œuvre: ${grandTotalLabor.toFixed(2)} DT | Total: ${grandTotal.toFixed(2)} DT`
    });
  }

  private formatDate(d: string): string {
    if (!d) return '—';
    try {
      const date = new Date(d);
      const dd = String(date.getDate()).padStart(2, '0');
      const mm = String(date.getMonth() + 1).padStart(2, '0');
      const yyyy = date.getFullYear();
      return `${dd}/${mm}/${yyyy}`;
    } catch { return d; }
  }

  calculateStats() {
    this.stats = {
      totalRepairs: this.repairs.length,
      pendingRepairs: this.repairs.filter(r => r.status === 'pending' || r.status === 'in_progress').length,
      completedRepairs: this.repairs.filter(r => r.status === 'completed').length,
      totalCost: this.repairs.reduce((sum, r) => sum + r.totalCost, 0)
    };
  }

  getStatusLabel(status: string): string {
    const labels: Record<string, string> = {
      pending: 'En attente',
      in_progress: 'En cours',
      completed: 'Termine'
    };
    return labels[status] || status;
  }

  onVehicleChange() {
    this.selectedVehicle = this.vehicles.find(v => v.id === +this.form.vehicleId) || null;
    if (this.selectedVehicle) {
      this.form.mileageAtRepair = this.selectedVehicle.mileage;
    }
  }

  openAddRepair() {
    this.form = this.getEmptyForm();
    this.editingRepair = null;
    this.selectedVehicle = null;
    this.isPanelOpen = true;
  }

  editRepair(repair: Repair) {
    this.editingRepair = repair;
    this.form = {
      vehicleId: repair.vehicleId.toString(),
      supplierId: repair.supplierId?.toString() || '',
      repairDate: repair.repairDate,
      mileageAtRepair: repair.mileageAtRepair || null,
      description: repair.description,
      invoiceNumber: repair.invoiceNumber,
      status: repair.status,
      laborCost: repair.laborCost,
      notes: repair.notes,
      parts: repair.parts.map(p => ({ ...p }))
    };
    this.selectedVehicle = this.vehicles.find(v => v.id === repair.vehicleId) || null;
    this.isPanelOpen = true;
  }

  viewRepair(repair: Repair) {
    this.viewingRepair = repair;
  }

  closeView() {
    this.viewingRepair = null;
  }

  editFromView() {
    if (this.viewingRepair) {
      this.editRepair(this.viewingRepair);
      this.viewingRepair = null;
    }
  }

  closePanel() {
    this.isPanelOpen = false;
    this.editingRepair = null;
    this.form = this.getEmptyForm();
  }

  addPart() {
    this.form.parts.push({
      partName: '',
      partReference: '',
      quantity: 1,
      unitPrice: 0,
      subtotal: 0,
      notes: ''
    });
  }

  removePart(index: number) {
    this.form.parts.splice(index, 1);
  }

  calculatePartSubtotal(part: RepairPart) {
    part.subtotal = part.quantity * part.unitPrice;
  }

  getPartsCost(): number {
    return this.form.parts.reduce((sum, p) => sum + (p.quantity * p.unitPrice), 0);
  }

  getTotalCost(): number {
    return this.getPartsCost() + (this.form.laborCost || 0);
  }

  isFormValid(): boolean {
    return !!this.form.vehicleId && !!this.form.repairDate;
  }

  saveRepair() {
    if (!this.isFormValid()) return;

    const parts = this.form.parts.map((p: RepairPart) => ({
      partName: p.partName,
      partReference: p.partReference,
      quantity: p.quantity,
      unitPrice: p.unitPrice,
      notes: p.notes
    }));

    if (this.editingRepair) {
      this.apiService.updateRepair(this.editingRepair.id!, {
        vehicleId: +this.form.vehicleId,
        supplierId: this.form.supplierId ? +this.form.supplierId : undefined,
        description: this.form.description,
        repairDate: this.form.repairDate,
        mileageAtRepair: this.form.mileageAtRepair || undefined,
        laborCost: this.form.laborCost,
        status: this.form.status,
        invoiceNumber: this.form.invoiceNumber,
        notes: this.form.notes,
        parts
      }).subscribe({
        next: () => {
          this.loadRepairs();
          this.closePanel();
        },
        error: (err) => console.error('Error updating repair:', err)
      });
    } else {
      this.apiService.createRepair({
        vehicleId: +this.form.vehicleId,
        supplierId: this.form.supplierId ? +this.form.supplierId : undefined,
        description: this.form.description,
        repairDate: this.form.repairDate,
        mileageAtRepair: this.form.mileageAtRepair || undefined,
        laborCost: this.form.laborCost,
        invoiceNumber: this.form.invoiceNumber,
        notes: this.form.notes,
        parts
      }).subscribe({
        next: () => {
          this.loadRepairs();
          this.closePanel();
        },
        error: (err) => console.error('Error creating repair:', err)
      });
    }
  }

  confirmDelete(repair: Repair) {
    this.repairToDelete = repair;
    this.showDeleteConfirm = true;
  }

  cancelDelete() {
    this.repairToDelete = null;
    this.showDeleteConfirm = false;
  }

  deleteRepair() {
    if (this.repairToDelete && this.repairToDelete.id) {
      this.apiService.deleteRepair(this.repairToDelete.id).pipe(takeUntil(this.destroy$)).subscribe({
        next: () => {
          this.loadRepairs();
          this.cancelDelete();
        },
        error: (err) => {
          console.error('Error deleting repair:', err);
          this.cancelDelete();
        }
      });
    } else {
      this.cancelDelete();
    }
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
