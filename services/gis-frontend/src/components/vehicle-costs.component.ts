import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Observable, Subject, takeUntil } from 'rxjs';
import { ApiService } from '../services/api.service';
import { VehicleCost, Vehicle, Company } from '../models/types';
import { AppLayoutComponent } from './shared/app-layout.component';
import { USER_PREF_PIPES } from '../pipes/user-preference-pipes';

/**
 * Crédits déduits des coûts : avoir fournisseur et remboursement d'assurance, montant
 * POSITIF compté en crédit, comme VehicleCostCategory côté serveur. Codes, libellés et
 * synonymes anciens (« avoir », « Remb. assurance »…) de la table ExpenseImportRow.Types :
 * un synonyme ajouté là-bas s'ajoute ici. Exportée pour la fiche véhicule : une copie par
 * composant divergerait au premier synonyme ajouté.
 */
const COST_CREDIT_FAMILIES = new Map<string, 'insurance_refund' | 'credit_note'>([
  ['insurance_refund', 'insurance_refund'],
  ['remboursement assurance', 'insurance_refund'],
  ['remb. assurance', 'insurance_refund'],
  ['credit_note', 'credit_note'],
  ['avoir fournisseur', 'credit_note'],
  ['avoir', 'credit_note'],
  ['credit note', 'credit_note'],
]);

/** Type de dépense normalisé comme RepairTypeClassifier.Normalize : accents retirés, minuscules, espaces réduits. */
export function normalizeCostType(type: string | null | undefined): string {
  return (type || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '')
    .toLowerCase().replace(/\s+/g, ' ').trim();
}

/** Code du crédit (insurance_refund, credit_note) d'un type de dépense ; null pour une dépense. */
export function costCreditFamily(type: string | null | undefined): 'insurance_refund' | 'credit_note' | null {
  // Map plutôt qu'objet : un type saisi « constructor » tombait sur Object.prototype et passait en crédit.
  return COST_CREDIT_FAMILIES.get(normalizeCostType(type)) ?? null;
}

/**
 * Postes Carburant, Entretien et Réparations des rapports (VehicleCostCategory.Classify) :
 * codes, libellés et synonymes de la même table ExpenseImportRow.Types, normalisés. Un plein
 * ancien « carburant » est du carburant dans les rapports ; comparé au seul code « fuel », il
 * tombait ici en « Autres ». Un synonyme ajouté là-bas s'ajoute ici.
 */
const COST_CATEGORY_FAMILIES = new Map<string, 'fuel' | 'maintenance' | 'repair'>([
  ['fuel', 'fuel'],
  ['carburant', 'fuel'],
  ['maintenance', 'maintenance'],
  ['entretien', 'maintenance'],
  ['repair', 'repair'],
  ['reparation', 'repair'],
  ['reparation accident', 'repair'],
]);

/** Poste (fuel, maintenance, repair) d'un type de dépense ; null pour les autres types et les crédits. */
export function costCategoryFamily(type: string | null | undefined): 'fuel' | 'maintenance' | 'repair' | null {
  return COST_CATEGORY_FAMILIES.get(normalizeCostType(type)) ?? null;
}

/**
 * Même règle que le serveur (POST et PUT /api/costs) : un montant doit être > 0. En
 * modification, un montant INCHANGÉ n'est pas recontrôlé : « marquer fait » crée une
 * dépense à 0 pour un entretien gratuit, qui doit rester corrigeable (date, description)
 * sans inventer un montant. Un montant négatif reste toujours refusé.
 */
export function costAmountError(amount: number, editing: { amount?: number | string | null } | null): string | null {
  if (amount > 0) return null;
  const inchange = !!editing && amount === 0 && Number(editing.amount) === 0;
  return inchange ? null : 'Le montant doit être supérieur à zéro.';
}

/**
 * Détail du plein que l'écran Coûts envoie à POST et PUT /api/costs. PUT remplace litres,
 * carburant et prix au litre par ce qu'il reçoit. Le prix au litre n'a pas de champ à
 * l'écran : il est renvoyé tel quel tant que montant et litres ne changent pas ; sinon il ne
 * correspond plus au plein, et PUT recalculerait le montant saisi à partir de ce prix.
 * Hors carburant, les champs du plein sont masqués : un plein reclassé (péage, assurance)
 * gardait ses litres, qui ressortaient dans l'export Excel des dépenses.
 */
export function fuelDetailToSave(
  form: { type?: string | null; fuelType?: string | null; liters?: number | string | null },
  amount: number,
  original: { amount: number | string; liters?: number | string | null; pricePerLiter?: number | string | null } | null
): { fuelType: string | null; liters: number | null; pricePerLiter: number | null } {
  if (costCategoryFamily(form.type) !== 'fuel') {
    return { fuelType: null, liters: null, pricePerLiter: null };
  }
  const liters = form.liters ? Number(form.liters) : null;
  const pricePerLiter = original?.pricePerLiter != null
    && amount === Number(original.amount)
    && liters === (original.liters ? Number(original.liters) : null)
    ? Number(original.pricePerLiter)
    : null;
  return { fuelType: form.fuelType || null, liters, pricePerLiter };
}

@Component({
  selector: 'app-vehicle-costs',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent, ...USER_PREF_PIPES],
  template: `
    <app-layout>
      <div class="costs-page">
        <div class="filter-bar">
          <div class="search-wrapper">
            <svg class="search-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/>
            </svg>
            <input type="text" class="search-input" placeholder="Rechercher..." [(ngModel)]="searchQuery" (input)="filterCosts()">
          </div>
          <select class="filter-select" [(ngModel)]="filterType" (change)="filterCosts()">
            <option value="">Tous les types</option>
            <option value="fuel">Carburant</option>
            <option value="maintenance">Maintenance</option>
            <option value="insurance">Assurance</option>
            <option value="tax">Vignette/Taxe</option>
            <option value="toll">Péage</option>
            <option value="parking">Parking</option>
            <option value="fine">Amende</option>
            <option value="other">Autre</option>
            <option value="credit_note">Avoir fournisseur</option>
            <option value="insurance_refund">Remb. assurance</option>
          </select>
          <select class="filter-select" [(ngModel)]="filterVehicle" (change)="filterCosts()">
            <option value="">Tous les véhicules</option>
            @for (vehicle of vehicles; track vehicle.id) {
              <option [value]="vehicle.id">{{ vehicle.name }}</option>
            }
          </select>
          <select class="filter-select" [(ngModel)]="filterPeriod" (change)="filterCosts()">
            <option value="">Toute période</option>
            <option value="week">Cette semaine</option>
            <option value="month">Ce mois</option>
            <option value="quarter">Ce trimestre</option>
            <option value="year">Cette année</option>
          </select>
          <button class="btn-add" (click)="openAddPopup()">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"/>
              <line x1="5" y1="12" x2="19" y2="12"/>
            </svg>
            Ajouter dépense
          </button>
        </div>

        <!-- Stats Bar -->
        <div class="stats-bar">
          <div class="stat-item fuel">
            <div class="stat-icon fuel">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M3 22V4a1 1 0 0 1 1-1h8a1 1 0 0 1 1 1v18"/>
                <path d="M13 10h4a1 1 0 0 1 1 1v6"/>
                <path d="M18 22V17"/>
                <path d="M21 13v4a1 1 0 0 1-1 1h-2"/>
                <rect x="3" y="7" width="10" height="5"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ getFuelCost() | appCurrency:0 }}</span>
              <span class="stat-label">Carburant</span>
            </div>
          </div>
          <div class="stat-item maintenance">
            <div class="stat-icon maintenance">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ getMaintenanceCost() | appCurrency:0 }}</span>
              <span class="stat-label">Maintenance</span>
            </div>
          </div>
          <div class="stat-item insurance">
            <div class="stat-icon insurance">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ getInsuranceCost() | appCurrency:0 }}</span>
              <span class="stat-label">Assurance</span>
            </div>
          </div>
          <div class="stat-item total">
            <div class="stat-icon total">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="12" y1="1" x2="12" y2="23"/>
                <path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ getTotalCost() | appCurrency:0 }}</span>
              <span class="stat-label">Total dépenses</span>
            </div>
          </div>
        </div>

        <!-- Summary by Vehicle -->
        <div class="summary-section">
          <h3>Résumé par véhicule</h3>
          <div class="vehicle-summaries">
            @for (summary of vehicleSummaries; track summary.vehicleId) {
              <div class="vehicle-summary-card">
                <div class="vehicle-header">
                  <span class="vehicle-name">{{ summary.vehicleName }}</span>
                  <span class="vehicle-plate">{{ summary.vehiclePlate }}</span>
                </div>
                <div class="summary-stats">
                  <div class="summary-item">
                    <span class="summary-label">Carburant</span>
                    <span class="summary-value fuel">{{ summary.fuelCost | appCurrency:0 }}</span>
                  </div>
                  <div class="summary-item">
                    <span class="summary-label">Maintenance</span>
                    <span class="summary-value maintenance">{{ summary.maintenanceCost | appCurrency:0 }}</span>
                  </div>
                  <div class="summary-item">
                    <span class="summary-label">Autres</span>
                    <span class="summary-value other">{{ summary.otherCost | appCurrency:0 }}</span>
                  </div>
                  <div class="summary-item total">
                    <span class="summary-label">Total</span>
                    <span class="summary-value total">{{ summary.totalCost | appCurrency:0 }}</span>
                  </div>
                </div>
              </div>
            }
          </div>
        </div>

        <!-- Costs Table -->
        <div class="table-container">
          <table class="data-table">
            <thead>
              <tr>
                <th>Date</th>
                <th>Véhicule</th>
                <th>Type</th>
                <th>Description</th>
                <th>Montant</th>
                <th>Référence</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              @for (cost of costs; track cost.id) {
                <tr>
                  <td class="date-cell">{{ formatDate(cost.date) }}</td>
                  <td>
                    <span class="vehicle-name">{{ getVehicleName(cost.vehicleId) }}</span>
                  </td>
                  <td>
                    <span class="type-badge" [class]="creditFamily(cost.type) || categoryFamily(cost.type) || cost.type">
                      {{ getTypeLabel(cost.type) }}
                    </span>
                  </td>
                  <td class="description-cell">{{ cost.description }}</td>
                  <td class="amount-cell" [class.credit]="isCredit(cost.type)">{{ isCredit(cost.type) ? '−' : '' }}{{ cost.amount | appCurrency:0 }}</td>
                  <td class="reference-cell">{{ cost.receiptNumber || '-' }}</td>
                  <td>
                    <div class="action-buttons">
                      <button class="action-btn" title="Modifier" (click)="editCost(cost)">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                          <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                        </svg>
                      </button>
                      <button class="action-btn danger" title="Supprimer" (click)="deleteCost(cost)">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <polyline points="3 6 5 6 21 6"/>
                          <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                        </svg>
                      </button>
                    </div>
                  </td>
                </tr>
              }
            </tbody>
          </table>

          @if (costs.length === 0) {
            <div class="empty-state">
              <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                <line x1="12" y1="1" x2="12" y2="23"/>
                <path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>
              </svg>
              <p>Aucune dépense enregistrée</p>
              <span>Ajoutez une nouvelle dépense pour commencer</span>
            </div>
          }
        </div>

        <!-- Add/Edit Popup -->
        <div class="popup-overlay" *ngIf="showPopup" (click)="closePopup()">
          <div class="popup-container" (click)="$event.stopPropagation()">
            <div class="popup-header">
              <h2>{{ editingCost ? 'Modifier la dépense' : 'Nouvelle dépense' }}</h2>
              <button class="close-btn" (click)="closePopup()">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="18" y1="6" x2="6" y2="18"/>
                  <line x1="6" y1="6" x2="18" y2="18"/>
                </svg>
              </button>
            </div>

            <form class="popup-body" (ngSubmit)="saveCost()">
              <div class="form-grid">
                <div class="form-group">
                  <label for="costVehicle">Véhicule *</label>
                  <!-- La modification (PUT) ne change jamais le véhicule d'une dépense. -->
                  <select id="costVehicle" [(ngModel)]="costForm.vehicleId" name="vehicleId" required
                          [disabled]="!!editingCost"
                          [title]="editingCost ? vehicleLockedTitle : ''">
                    <option value="">Sélectionner</option>
                    @for (vehicle of vehicles; track vehicle.id) {
                      <option [value]="vehicle.id">{{ vehicle.name }}</option>
                    }
                  </select>
                </div>

                <div class="form-group">
                  <label for="costType">Type *</label>
                  <select id="costType" [(ngModel)]="costForm.type" name="type" required>
                    <option value="">Sélectionner</option>
                    <option value="fuel">Carburant</option>
                    <option value="maintenance">Maintenance</option>
                    <option value="insurance">Assurance</option>
                    <option value="tax">Vignette/Taxe</option>
                    <option value="toll">Péage</option>
                    <option value="parking">Parking</option>
                    <option value="fine">Amende</option>
                    <option value="other">Autre</option>
                    <option value="credit_note">Avoir fournisseur</option>
                  </select>
                </div>

                <div class="form-group">
                  <label for="costDate">Date *</label>
                  <input type="date" id="costDate" [(ngModel)]="costForm.dateStr" name="date" required />
                </div>

                <div class="form-group">
                  <label for="costAmount">Montant *</label>
                  <input type="number" id="costAmount" [(ngModel)]="costForm.amount" name="amount" required min="0.01" step="0.01" placeholder="0" />
                  @if (isCredit(costForm.type)) {
                    <span class="form-hint">Montant en positif : il est déduit des coûts.</span>
                  }
                </div>

                <div class="form-group full-width">
                  <label for="costDescription">Description</label>
                  <input type="text" id="costDescription" [(ngModel)]="costForm.description" name="description" placeholder="Description de la dépense" />
                </div>

                <div class="form-group">
                  <label for="costReceipt">N° Facture/Reçu</label>
                  <input type="text" id="costReceipt" [(ngModel)]="costForm.receiptNumber" name="receiptNumber" placeholder="Ex: FAC-2024-001" />
                </div>

                <div class="form-group">
                  <label for="costMileage">Kilométrage</label>
                  <input type="number" id="costMileage" [(ngModel)]="costForm.mileage" name="mileage" min="0" placeholder="0" />
                </div>

                <!-- Fuel specific fields -->
                @if (categoryFamily(costForm.type) === 'fuel') {
                  <div class="form-group">
                    <label for="fuelType">Type carburant</label>
                    <select id="fuelType" [(ngModel)]="costForm.fuelType" name="fuelType">
                      <option value="">Sélectionner</option>
                      <option value="diesel">Diesel</option>
                      <option value="gasoline">Essence</option>
                      <option value="electric">Électrique</option>
                    </select>
                  </div>

                  <div class="form-group">
                    <label for="liters">Litres</label>
                    <input type="number" id="liters" [(ngModel)]="costForm.liters" name="liters" min="0" step="0.01" placeholder="0" />
                  </div>
                }
              </div>

              @if (saveError) {
                <div class="save-error" role="alert">{{ saveError }}</div>
              }
              <div class="popup-footer">
                <button type="button" class="btn-secondary" (click)="closePopup()">Annuler</button>
                <button type="submit" class="btn-primary" [disabled]="saving">{{ editingCost ? 'Mettre à jour' : 'Enregistrer' }}</button>
              </div>
            </form>
          </div>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    .costs-page {
      flex: 1;
      background: #f1f5f9;
      display: flex;
      flex-direction: column;
      min-height: calc(100vh - 42px);
    }

    .filter-bar {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 14px;
      background: white;
      border-bottom: 1px solid #e2e8f0;
      flex-wrap: wrap;
    }

    .search-wrapper {
      position: relative;
      flex: 1;
      max-width: 200px;
      min-width: 150px;
    }

    .search-icon {
      position: absolute;
      left: 10px;
      top: 50%;
      transform: translateY(-50%);
      color: #94a3b8;
    }

    .search-input {
      width: 100%;
      padding: 6px 10px 6px 32px;
      font-family: var(--font-family);
      font-size: 12px;
      border: 1px solid #e2e8f0;
      border-radius: 3px;
      background: white;
      color: #1e293b;
    }

    .search-input:focus {
      outline: none;
      border-color: #3b82f6;
    }

    .filter-select {
      padding: 6px 10px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 3px;
      color: #1e293b;
      font-family: var(--font-family);
      font-size: 12px;
      cursor: pointer;
    }

    .btn-add {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 6px 12px;
      background: #3b82f6;
      color: white;
      border: none;
      border-radius: 3px;
      font-family: var(--font-family);
      font-size: 12px;
      font-weight: 500;
      cursor: pointer;
      transition: background 0.15s;
      margin-left: auto;
    }

    .btn-add:hover {
      background: #2563eb;
    }

    .stats-bar {
      display: flex;
      gap: 12px;
      padding: 14px;
      background: white;
      border-bottom: 1px solid #e2e8f0;
      flex-wrap: wrap;
    }

    .stat-item {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 10px 14px;
      background: #f8fafc;
      border-radius: 4px;
      min-width: 150px;
    }

    .stat-icon {
      width: 32px;
      height: 32px;
      border-radius: 4px;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .stat-icon.fuel { background: #fef3c7; color: #d97706; }
    .stat-icon.maintenance { background: #dbeafe; color: #2563eb; }
    .stat-icon.insurance { background: #f3e8ff; color: #9333ea; }
    .stat-icon.total { background: #dcfce7; color: #16a34a; }

    .stat-content {
      display: flex;
      flex-direction: column;
    }

    .stat-value {
      font-size: 14px;
      font-weight: 600;
      color: #1e293b;
    }

    .stat-label {
      font-size: 10px;
      color: #64748b;
    }

    .summary-section {
      padding: 14px;
      background: white;
      border-bottom: 1px solid #e2e8f0;
    }

    .summary-section h3 {
      font-size: 12px;
      font-weight: 600;
      color: #64748b;
      margin: 0 0 12px 0;
    }

    .vehicle-summaries {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(250px, 1fr));
      gap: 12px;
    }

    .vehicle-summary-card {
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 4px;
      padding: 12px;
    }

    .vehicle-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 10px;
      padding-bottom: 8px;
      border-bottom: 1px solid #e2e8f0;
    }

    .vehicle-header .vehicle-name {
      font-weight: 600;
      font-size: 12px;
      color: #1e293b;
    }

    .vehicle-header .vehicle-plate {
      font-size: 10px;
      color: #64748b;
      background: white;
      padding: 2px 6px;
      border-radius: 2px;
    }

    .summary-stats {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 8px;
    }

    .summary-item {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .summary-label {
      font-size: 9px;
      color: #94a3b8;
      text-transform: uppercase;
    }

    .summary-value {
      font-size: 12px;
      font-weight: 500;
    }

    .summary-value.fuel { color: #d97706; }
    .summary-value.maintenance { color: #2563eb; }
    .summary-value.other { color: #64748b; }
    .summary-value.total { color: #16a34a; font-weight: 600; }

    .summary-item.total {
      grid-column: 1 / -1;
      padding-top: 8px;
      border-top: 1px dashed #e2e8f0;
    }

    .table-container {
      flex: 1;
      background: white;
      overflow: auto;
    }

    .data-table {
      width: 100%;
      border-collapse: collapse;
      font-size: 12px;
    }

    .data-table th,
    .data-table td {
      padding: 10px 14px;
      text-align: left;
      border-bottom: 1px solid #f1f5f9;
    }

    .data-table th {
      background: #f8fafc;
      color: #64748b;
      font-weight: 500;
      font-size: 10px;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      position: sticky;
      top: 0;
    }

    .data-table tbody tr:hover {
      background: #f8fafc;
    }

    .date-cell {
      white-space: nowrap;
      color: #64748b;
    }

    .vehicle-name {
      font-weight: 500;
      color: #1e293b;
    }

    .type-badge {
      display: inline-block;
      padding: 3px 8px;
      border-radius: 3px;
      font-size: 10px;
      font-weight: 500;
    }

    .type-badge.fuel { background: #fef3c7; color: #d97706; }
    .type-badge.maintenance { background: #dbeafe; color: #2563eb; }
    .type-badge.insurance { background: #f3e8ff; color: #9333ea; }
    .type-badge.tax { background: #fee2e2; color: #dc2626; }
    .type-badge.toll { background: #e0e7ff; color: #4f46e5; }
    .type-badge.parking { background: #cffafe; color: #0891b2; }
    .type-badge.fine { background: #fecaca; color: #b91c1c; }
    .type-badge.other { background: #f1f5f9; color: #64748b; }
    .type-badge.repair, .type-badge.reparation { background: #ffedd5; color: #c2410c; }
    .type-badge.credit_note, .type-badge.insurance_refund { background: #d1fae5; color: #047857; }

    .description-cell {
      max-width: 200px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      color: #64748b;
    }

    .amount-cell {
      font-weight: 600;
      color: #16a34a;
      white-space: nowrap;
    }

    .amount-cell.credit { color: #047857; }

    .form-hint {
      font-size: 11px;
      color: #64748b;
    }

    .reference-cell {
      color: #94a3b8;
      font-size: 11px;
    }

    .action-buttons {
      display: flex;
      gap: 6px;
    }

    .action-btn {
      padding: 4px;
      background: transparent;
      border: 1px solid #e2e8f0;
      border-radius: 3px;
      color: #64748b;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .action-btn:hover {
      background: #f8fafc;
      color: #1e293b;
    }

    .action-btn.danger:hover {
      background: #fee2e2;
      color: #dc2626;
      border-color: #fecaca;
    }

    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 60px 20px;
      color: #94a3b8;
    }

    .empty-state svg {
      margin-bottom: 16px;
      opacity: 0.5;
    }

    .empty-state p {
      font-size: 14px;
      font-weight: 500;
      color: #64748b;
      margin-bottom: 4px;
    }

    .empty-state span {
      font-size: 12px;
    }

    /* Popup Styles */
    .popup-overlay {
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(0, 0, 0, 0.5);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1200; /* > 1100 : la barre du haut collante masquait le haut du modal */
      padding: 20px;
    }

    .popup-container {
      background: white;
      border-radius: 6px;
      box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.15);
      max-width: 550px;
      width: 100%;
      max-height: 90vh;
      overflow: hidden;
      display: flex;
      flex-direction: column;
    }

    .popup-header {
      padding: 14px 20px;
      border-bottom: 1px solid #e2e8f0;
      display: flex;
      align-items: center;
      justify-content: space-between;
      background: #f8fafc;
    }

    .popup-header h2 {
      margin: 0;
      font-size: 14px;
      font-weight: 600;
      color: #1e293b;
    }

    .close-btn {
      background: none;
      border: none;
      color: #94a3b8;
      cursor: pointer;
      padding: 6px;
      border-radius: 3px;
      display: flex;
    }

    .close-btn:hover {
      background: #f1f5f9;
      color: #1e293b;
    }

    .popup-body {
      padding: 20px;
      overflow-y: auto;
      flex: 1;
    }

    .form-grid {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 16px;
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
      border: 1px solid #e2e8f0;
      border-radius: 3px;
      font-family: var(--font-family);
      font-size: 12px;
      color: #1e293b;
    }

    .form-group input:focus,
    .form-group select:focus {
      outline: none;
      border-color: #3b82f6;
    }

    .save-error {
      padding: 8px 20px;
      background: #fef2f2;
      border-top: 1px solid #fecaca;
      color: #b91c1c;
      font-size: 12px;
      line-height: 1.4;
    }

    .popup-footer {
      padding: 14px 20px;
      border-top: 1px solid #e2e8f0;
      display: flex;
      gap: 10px;
      justify-content: flex-end;
      background: #f8fafc;
    }

    .btn-primary {
      padding: 8px 16px;
      background: #3b82f6;
      color: white;
      border: none;
      border-radius: 3px;
      font-weight: 500;
      font-size: 12px;
      cursor: pointer;
    }

    .btn-primary:hover {
      background: #2563eb;
    }

    .btn-secondary {
      padding: 8px 16px;
      background: white;
      color: #64748b;
      border: 1px solid #e2e8f0;
      border-radius: 3px;
      font-weight: 500;
      font-size: 12px;
      cursor: pointer;
    }

    .btn-secondary:hover {
      background: #f8fafc;
      color: #1e293b;
    }
  `]
})
export class VehicleCostsComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  costs: VehicleCost[] = [];
  allCosts: VehicleCost[] = [];
  vehicles: Vehicle[] = [];
  company: Company | null = null;

  searchQuery = '';
  filterType = '';
  filterVehicle = '';
  filterPeriod = '';

  showPopup = false;
  editingCost: VehicleCost | null = null;
  costForm: any = {};
  /** Refus de l'enregistrement : il n'était écrit que dans la console, la fenêtre se fermait. */
  saveError: string | null = null;
  saving = false;
  readonly vehicleLockedTitle = "Le véhicule d'une dépense enregistrée ne se change pas";

  vehicleSummaries: any[] = [];

  constructor(
    private router: Router,
    private apiService: ApiService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit() {
    if (!this.apiService.isAuthenticated()) {
      this.router.navigate(['/login']);
      return;
    }

    this.loadData();
  }

  loadData() {
    this.apiService.getCosts().pipe(takeUntil(this.destroy$)).subscribe({
      next: (costs) => {
        this.allCosts = costs;
        this.costs = [...this.allCosts];
        this.calculateSummaries();
        this.cdr.detectChanges();
      },
      error: (err) => console.error('Error loading costs:', err)
    });

    this.apiService.getVehicles().pipe(takeUntil(this.destroy$)).subscribe({
      // La synthèse par véhicule part de la liste des véhicules : arrivée après les
      // dépenses, elle laissait « Résumé par véhicule » vide.
      next: (vehicles) => { this.vehicles = vehicles; this.calculateSummaries(); this.cdr.detectChanges(); },
      error: (err) => console.error('Error loading vehicles:', err)
    });
  }

  filterCosts() {
    const now = new Date();
    let startDate: Date | null = null;

    switch (this.filterPeriod) {
      case 'week':
        startDate = new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000);
        break;
      case 'month':
        startDate = new Date(now.getFullYear(), now.getMonth(), 1);
        break;
      case 'quarter':
        const quarter = Math.floor(now.getMonth() / 3);
        startDate = new Date(now.getFullYear(), quarter * 3, 1);
        break;
      case 'year':
        startDate = new Date(now.getFullYear(), 0, 1);
        break;
    }

    this.costs = this.allCosts.filter(c => {
      // Description facultative (colonne nullable, scan IA, import) : une seule
      // dépense sans description faisait échouer toute la recherche.
      const matchesSearch = !this.searchQuery ||
        (c.description || '').toLowerCase().includes(this.searchQuery.toLowerCase());
      // Filtre d'un crédit ou d'un poste : ses lignes anciennes (« avoir », « carburant »…),
      // comptées avec lui, sont listées avec lui.
      const matchesType = !this.filterType || c.type === this.filterType
        || costCreditFamily(c.type) === this.filterType
        || costCategoryFamily(c.type) === this.filterType;
      const matchesVehicle = !this.filterVehicle || c.vehicleId === this.filterVehicle;
      const matchesPeriod = !startDate || new Date(c.date) >= startDate;
      return matchesSearch && matchesType && matchesVehicle && matchesPeriod;
    });
  }

  calculateSummaries() {
    const summaryMap = new Map<string, any>();

    this.vehicles.forEach(v => {
      summaryMap.set(v.id, {
        vehicleId: v.id,
        vehicleName: v.name,
        vehiclePlate: v.plate,
        fuelCost: 0,
        maintenanceCost: 0,
        otherCost: 0,
        totalCost: 0,
        count: 0
      });
    });

    this.allCosts.forEach(c => {
      const summary = summaryMap.get(c.vehicleId);
      if (summary) {
        const amount = this.signedAmount(c);
        const family = costCategoryFamily(c.type);
        if (family === 'fuel') {
          summary.fuelCost += amount;
        } else if (family === 'maintenance') {
          summary.maintenanceCost += amount;
        } else {
          summary.otherCost += amount;
        }
        summary.totalCost += amount;
        summary.count++;
      }
    });

    // Un véhicule qui n'a qu'un avoir a un total négatif : il reste dans la synthèse.
    this.vehicleSummaries = Array.from(summaryMap.values())
      .filter(s => s.count > 0)
      .sort((a, b) => b.totalCost - a.totalCost);
  }

  /**
   * Avoir fournisseur et remboursement d'assurance (synonymes anciens compris) : crédits
   * déduits, comme les totaux serveur. Additionnés bruts, ils gonflaient le total de
   * l'écran du montant rendu.
   */
  creditFamily(type: string | null | undefined): string | null {
    return costCreditFamily(type);
  }

  isCredit(type: string | null | undefined): boolean {
    return this.creditFamily(type) !== null;
  }

  categoryFamily(type: string | null | undefined): string | null {
    return costCategoryFamily(type);
  }

  signedAmount(c: VehicleCost): number {
    // Crédit en valeur absolue, comme VehicleCostCategory.SignedAmount : un avoir ancien
    // saisi à −120 ne doit pas devenir une dépense de +120.
    return this.isCredit(c.type) ? -Math.abs(c.amount) : c.amount;
  }

  getFuelCost(): number {
    return this.allCosts.filter(c => costCategoryFamily(c.type) === 'fuel').reduce((sum, c) => sum + c.amount, 0);
  }

  getMaintenanceCost(): number {
    return this.allCosts.filter(c => costCategoryFamily(c.type) === 'maintenance').reduce((sum, c) => sum + c.amount, 0);
  }

  getInsuranceCost(): number {
    return this.allCosts.filter(c => c.type === 'insurance').reduce((sum, c) => sum + c.amount, 0);
  }

  getTotalCost(): number {
    return this.allCosts.reduce((sum, c) => sum + this.signedAmount(c), 0);
  }

  getTypeLabel(type: string): string {
    const labels: any = {
      fuel: 'Carburant',
      maintenance: 'Maintenance',
      repair: 'Réparation',
      reparation: 'Réparation',
      insurance: 'Assurance',
      insurance_refund: 'Remboursement assurance',
      credit_note: 'Avoir fournisseur',
      technical_inspection: 'Visite technique',
      tax: 'Vignette/Taxe',
      registration: 'Carte grise',
      transport_permit: 'Autorisation transport',
      toll: 'Péage',
      parking: 'Parking',
      fine: 'Amende',
      other: 'Autre'
    };
    // Ligne ancienne au libellé d'un crédit ou d'un poste (« avoir », « carburant ») : même libellé que son code.
    return labels[type] || labels[this.creditFamily(type) ?? ''] || labels[this.categoryFamily(type) ?? ''] || type;
  }

  getVehicleName(vehicleId: string): string {
    const vehicle = this.vehicles.find(v => v.id === vehicleId);
    return vehicle ? vehicle.name : vehicleId;
  }

  formatDate(date: Date): string {
    return new Date(date).toLocaleDateString('fr-FR', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric'
    });
  }

  openAddPopup() {
    this.editingCost = null;
    this.costForm = {
      vehicleId: '',
      type: '',
      dateStr: new Date().toISOString().split('T')[0],
      amount: 0,
      description: '',
      receiptNumber: '',
      mileage: 0,
      fuelType: '',
      liters: 0
    };
    this.saveError = null;
    this.showPopup = true;
  }

  editCost(cost: VehicleCost) {
    this.editingCost = cost;
    this.costForm = {
      ...cost,
      dateStr: new Date(cost.date).toISOString().split('T')[0]
    };
    this.saveError = null;
    this.showPopup = true;
  }

  closePopup() {
    this.showPopup = false;
    this.editingCost = null;
    this.saveError = null;
  }

  saveCost() {
    if (this.saving) return;
    const amount = Number(this.costForm.amount);
    if (!this.costForm.vehicleId || !this.costForm.type || !this.costForm.dateStr) {
      this.saveError = 'Renseignez le véhicule, le type et la date.';
      return;
    }
    // Même règle que le serveur (montant > 0, 0 inchangé toléré en modification) :
    // refusé là-bas, le message se perdait.
    const amountError = costAmountError(amount, this.editingCost);
    if (amountError) {
      this.saveError = amountError;
      return;
    }

    const fuel = fuelDetailToSave(this.costForm, amount, this.editingCost);

    // Champs que POST et PUT /api/costs lisent, et rien d'autre : le formulaire porte
    // aussi les colonnes d'affichage de la liste. Le justificatif est renvoyé tel quel,
    // PUT l'écraserait sinon.
    const costData = {
      vehicleId: Number(this.costForm.vehicleId),
      type: this.costForm.type,
      // Chaîne vide, jamais null : c'est ce que l'écran enregistrait avant.
      description: this.costForm.description ?? '',
      amount,
      date: new Date(this.costForm.dateStr),
      mileage: this.costForm.mileage ? Number(this.costForm.mileage) : null,
      receiptNumber: this.costForm.receiptNumber || null,
      receiptUrl: this.costForm.receiptUrl || null,
      fuelType: fuel.fuelType,
      liters: fuel.liters,
      pricePerLiter: fuel.pricePerLiter
    };

    // Modification en place (PUT). Elle supprimait puis recréait la dépense : si la
    // recréation était refusée (montant, catégorie ancienne), la dépense d'origine
    // était perdue, avec le journal d'entretien qui la référençait.
    const request$: Observable<unknown> = this.editingCost
      ? this.apiService.updateCost(Number(this.editingCost.id), costData)
      : this.apiService.createCost(costData);

    this.saving = true;
    this.saveError = null;
    request$.pipe(takeUntil(this.destroy$)).subscribe({
      next: () => {
        this.saving = false;
        this.closePopup();
        this.loadData();
      },
      error: (err) => {
        this.saving = false;
        this.saveError = err?.error?.message
          || (err?.status === 404
            ? 'Dépense introuvable : elle a peut-être été supprimée. Rechargez la page.'
            : "La dépense n'a pas été enregistrée. Vérifiez les valeurs saisies puis réessayez.");
        this.cdr.detectChanges();
      }
    });
  }

  deleteCost(cost: VehicleCost) {
    if (confirm('Êtes-vous sûr de vouloir supprimer cette dépense ?')) {
      this.apiService.deleteCost(parseInt(cost.id)).pipe(takeUntil(this.destroy$)).subscribe({
        next: () => this.loadData(),
        error: (err) => console.error('Error deleting cost:', err)
      });
    }
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
