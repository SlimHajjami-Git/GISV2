import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../services/api.service';
import { VehicleCost, Vehicle, Company } from '../models/types';
import { AppLayoutComponent } from './shared/app-layout.component';

@Component({
  selector: 'app-vehicle-costs',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent],
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
              <span class="stat-value">{{ getFuelCost() | number:'1.0-0' }} DH</span>
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
              <span class="stat-value">{{ getMaintenanceCost() | number:'1.0-0' }} DH</span>
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
              <span class="stat-value">{{ getInsuranceCost() | number:'1.0-0' }} DH</span>
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
              <span class="stat-value">{{ getTotalCost() | number:'1.0-0' }} DH</span>
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
                    <span class="summary-value fuel">{{ summary.fuelCost | number:'1.0-0' }} DH</span>
                  </div>
                  <div class="summary-item">
                    <span class="summary-label">Maintenance</span>
                    <span class="summary-value maintenance">{{ summary.maintenanceCost | number:'1.0-0' }} DH</span>
                  </div>
                  <div class="summary-item">
                    <span class="summary-label">Autres</span>
                    <span class="summary-value other">{{ summary.otherCost | number:'1.0-0' }} DH</span>
                  </div>
                  <div class="summary-item total">
                    <span class="summary-label">Total</span>
                    <span class="summary-value total">{{ summary.totalCost | number:'1.0-0' }} DH</span>
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
                    <span class="type-badge" [class]="cost.type">
                      {{ getTypeLabel(cost.type) }}
                    </span>
                  </td>
                  <td class="description-cell">{{ cost.description }}</td>
                  <td class="amount-cell">{{ cost.amount | number:'1.0-0' }} DH</td>
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
                  <select id="costVehicle" [(ngModel)]="costForm.vehicleId" name="vehicleId" required>
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
                  </select>
                </div>

                <div class="form-group">
                  <label for="costDate">Date *</label>
                  <input type="date" id="costDate" [(ngModel)]="costForm.dateStr" name="date" required />
                </div>

                <div class="form-group">
                  <label for="costAmount">Montant (DH) *</label>
                  <input type="number" id="costAmount" [(ngModel)]="costForm.amount" name="amount" required min="0" placeholder="0" />
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
                @if (costForm.type === 'fuel') {
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

              <div class="popup-footer">
                <button type="button" class="btn-secondary" (click)="closePopup()">Annuler</button>
                <button type="submit" class="btn-primary">{{ editingCost ? 'Mettre à jour' : 'Enregistrer' }}</button>
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
      z-index: 1000;
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
export class VehicleCostsComponent implements OnInit {
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

  vehicleSummaries: any[] = [];

  constructor(
    private router: Router,
    private apiService: ApiService
  ) {}

  ngOnInit() {
    if (!this.apiService.isAuthenticated()) {
      this.router.navigate(['/login']);
      return;
    }

    this.loadData();
  }

  loadData() {
    this.apiService.getCosts().subscribe({
      next: (costs) => {
        this.allCosts = costs;
        this.costs = [...this.allCosts];
        this.calculateSummaries();
      },
      error: (err) => console.error('Error loading costs:', err)
    });

    this.apiService.getVehicles().subscribe({
      next: (vehicles) => this.vehicles = vehicles,
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
      const matchesSearch = !this.searchQuery ||
        c.description.toLowerCase().includes(this.searchQuery.toLowerCase());
      const matchesType = !this.filterType || c.type === this.filterType;
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
        totalCost: 0
      });
    });

    this.allCosts.forEach(c => {
      const summary = summaryMap.get(c.vehicleId);
      if (summary) {
        if (c.type === 'fuel') {
          summary.fuelCost += c.amount;
        } else if (c.type === 'maintenance') {
          summary.maintenanceCost += c.amount;
        } else {
          summary.otherCost += c.amount;
        }
        summary.totalCost += c.amount;
      }
    });

    this.vehicleSummaries = Array.from(summaryMap.values())
      .filter(s => s.totalCost > 0)
      .sort((a, b) => b.totalCost - a.totalCost);
  }

  getFuelCost(): number {
    return this.allCosts.filter(c => c.type === 'fuel').reduce((sum, c) => sum + c.amount, 0);
  }

  getMaintenanceCost(): number {
    return this.allCosts.filter(c => c.type === 'maintenance').reduce((sum, c) => sum + c.amount, 0);
  }

  getInsuranceCost(): number {
    return this.allCosts.filter(c => c.type === 'insurance').reduce((sum, c) => sum + c.amount, 0);
  }

  getTotalCost(): number {
    return this.allCosts.reduce((sum, c) => sum + c.amount, 0);
  }

  getTypeLabel(type: string): string {
    const labels: any = {
      fuel: 'Carburant',
      maintenance: 'Maintenance',
      insurance: 'Assurance',
      tax: 'Vignette/Taxe',
      toll: 'Péage',
      parking: 'Parking',
      fine: 'Amende',
      other: 'Autre'
    };
    return labels[type] || type;
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
    this.showPopup = true;
  }

  editCost(cost: VehicleCost) {
    this.editingCost = cost;
    this.costForm = {
      ...cost,
      dateStr: new Date(cost.date).toISOString().split('T')[0]
    };
    this.showPopup = true;
  }

  closePopup() {
    this.showPopup = false;
    this.editingCost = null;
  }

  saveCost() {
    const costData: Partial<VehicleCost> = {
      ...this.costForm,
      date: new Date(this.costForm.dateStr),
      companyId: this.company?.id
    };
    delete (costData as any).dateStr;

    if (this.editingCost) {
      // For editing, we don't have an update method, so we delete and recreate
      this.apiService.deleteCost(parseInt(this.editingCost.id)).subscribe({
        next: () => {
          this.apiService.createCost(costData).subscribe({
            next: () => this.loadData(),
            error: (err) => console.error('Error creating cost:', err)
          });
        },
        error: (err) => console.error('Error deleting cost:', err)
      });
    } else {
      this.apiService.createCost(costData).subscribe({
        next: () => this.loadData(),
        error: (err) => console.error('Error creating cost:', err)
      });
    }

    this.closePopup();
  }

  deleteCost(cost: VehicleCost) {
    if (confirm('Êtes-vous sûr de vouloir supprimer cette dépense ?')) {
      this.apiService.deleteCost(parseInt(cost.id)).subscribe({
        next: () => this.loadData(),
        error: (err) => console.error('Error deleting cost:', err)
      });
    }
  }
}
