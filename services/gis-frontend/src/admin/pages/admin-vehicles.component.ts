import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService, AdminVehicle, Client } from '../services/admin.service';
import { VehiclePopupComponent } from '../../components/shared/vehicle-popup.component';

@Component({
  selector: 'admin-vehicles',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent, VehiclePopupComponent],
  template: `
    <admin-layout pageTitle="Gestion des Vehicules">
      <div class="vehicles-page">

        <!-- Header: search + filters + add button -->
        <div class="page-header">
          <div class="header-left">
            <div class="search-box">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
              </svg>
              <input type="text" [(ngModel)]="searchQuery" (input)="filterVehicles()" placeholder="Rechercher par matricule, MAT, IMEI, tél, nom..." />
            </div>
            <select class="filter-select" [(ngModel)]="statusFilter" (change)="filterVehicles()">
              <option value="all">Tous les statuts</option>
              <option value="available">Disponible</option>
              <option value="in_use">En service</option>
              <option value="maintenance">Maintenance</option>
            </select>
            <select class="filter-select" [(ngModel)]="companyFilter" (change)="filterVehicles()">
              <option value="all">Toutes les societes</option>
              <option *ngFor="let company of companies" [value]="company.id">{{ company.name }}</option>
            </select>
          </div>
          <button class="add-btn" (click)="openAddModal()">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
            </svg>
            Ajouter Vehicule
          </button>
        </div>

        <!-- Stats bar -->
        <div class="stats-bar">
          <div class="stat-card">
            <span class="stat-number">{{ vehicles.length }}</span>
            <span class="stat-text">Total</span>
          </div>
          <div class="stat-card available">
            <span class="stat-number">{{ countByStatus('available') }}</span>
            <span class="stat-text">Disponibles</span>
          </div>
          <div class="stat-card in-use">
            <span class="stat-number">{{ countByStatus('in_use') }}</span>
            <span class="stat-text">En service</span>
          </div>
          <div class="stat-card maintenance">
            <span class="stat-number">{{ countByStatus('maintenance') }}</span>
            <span class="stat-text">Maintenance</span>
          </div>
          <div class="stat-card gps">
            <span class="stat-number">{{ countWithGps() }}</span>
            <span class="stat-text">Avec GPS</span>
          </div>
        </div>

        <!-- Table -->
        <div class="table-container">
          <table class="vehicles-table" *ngIf="filteredVehicles.length > 0">
            <thead>
              <tr>
                <th class="th-sortable" (click)="toggleSort('plate')">
                  Matricule
                  <span class="sort-icon" *ngIf="sortColumn === 'plate'">{{ sortDirection === 'asc' ? '&#9650;' : '&#9660;' }}</span>
                </th>
                <th class="th-sortable" (click)="toggleSort('gpsMat')">
                  MAT
                  <span class="sort-icon" *ngIf="sortColumn === 'gpsMat'">{{ sortDirection === 'asc' ? '&#9650;' : '&#9660;' }}</span>
                </th>
                <th class="th-sortable" (click)="toggleSort('gpsImei')">
                  IMEI
                  <span class="sort-icon" *ngIf="sortColumn === 'gpsImei'">{{ sortDirection === 'asc' ? '&#9650;' : '&#9660;' }}</span>
                </th>
                <th class="th-sortable" (click)="toggleSort('name')">
                  Nom
                  <span class="sort-icon" *ngIf="sortColumn === 'name'">{{ sortDirection === 'asc' ? '&#9650;' : '&#9660;' }}</span>
                </th>
                <th class="th-sortable" (click)="toggleSort('mileage')">
                  Kilometrage
                  <span class="sort-icon" *ngIf="sortColumn === 'mileage'">{{ sortDirection === 'asc' ? '&#9650;' : '&#9660;' }}</span>
                </th>
                <th>Telephone</th>
                <th class="th-sortable" (click)="toggleSort('companyName')">
                  Societe
                  <span class="sort-icon" *ngIf="sortColumn === 'companyName'">{{ sortDirection === 'asc' ? '&#9650;' : '&#9660;' }}</span>
                </th>
                <th>Statut</th>
                <th class="th-actions">Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let vehicle of filteredVehicles; trackBy: trackById">
                <td class="td-plate">
                  <span class="plate-badge">{{ vehicle.plate || '-' }}</span>
                </td>
                <td class="td-mat">
                  <span class="mat-badge" *ngIf="vehicle.gpsMat">{{ vehicle.gpsMat }}</span>
                  <span class="text-muted" *ngIf="!vehicle.gpsMat">-</span>
                </td>
                <td class="td-imei">
                  <span class="imei-badge" *ngIf="vehicle.gpsImei">{{ vehicle.gpsImei }}</span>
                  <span class="text-muted" *ngIf="!vehicle.gpsImei">-</span>
                </td>
                <td>
                  <div class="cell-name">
                    <span class="vehicle-name">{{ vehicle.name }}</span>
                    <span class="vehicle-sub" *ngIf="vehicle.brand || vehicle.model">{{ vehicle.brand }} {{ vehicle.model }}</span>
                  </div>
                </td>
                <td class="td-km">{{ vehicle.mileage | number }} km</td>
                <td class="td-phone">
                  <span *ngIf="vehicle.gpsSimNumber">{{ vehicle.gpsSimNumber }}</span>
                  <span class="text-muted" *ngIf="!vehicle.gpsSimNumber">-</span>
                </td>
                <td>
                  <span class="company-badge" *ngIf="vehicle.companyName">{{ vehicle.companyName }}</span>
                  <span class="text-muted" *ngIf="!vehicle.companyName">-</span>
                </td>
                <td>
                  <span class="status-badge" [class]="vehicle.status">{{ getStatusLabel(vehicle.status) }}</span>
                </td>
                <td class="td-actions">
                  <button class="action-btn detail" (click)="viewVehicle(vehicle)" title="Detail">
                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>
                    </svg>
                  </button>
                  <button class="action-btn edit" (click)="editVehicle(vehicle)" title="Modifier">
                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                      <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                    </svg>
                  </button>
                  <button class="action-btn delete" (click)="confirmDelete(vehicle)" title="Supprimer">
                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <polyline points="3,6 5,6 21,6"/>
                      <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                    </svg>
                  </button>
                </td>
              </tr>
            </tbody>
          </table>

          <div class="table-footer" *ngIf="filteredVehicles.length > 0">
            <span class="result-count">{{ filteredVehicles.length }} vehicule(s) sur {{ vehicles.length }}</span>
          </div>

          <div class="empty-state" *ngIf="filteredVehicles.length === 0">
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
              <rect x="1" y="3" width="15" height="13" rx="2"/>
              <path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-5"/>
              <circle cx="5.5" cy="18.5" r="2.5"/>
              <circle cx="18.5" cy="18.5" r="2.5"/>
            </svg>
            <h3>Aucun vehicule trouve</h3>
            <p>Essayez de modifier vos filtres ou votre recherche</p>
          </div>
        </div>

        <!-- Vehicle Add/Edit Popup -->
        <app-vehicle-popup
          [isOpen]="showAddModal || showEditModal"
          [vehicle]="selectedVehicleForPopup"
          [companies]="companiesForPopup"
          [defaultCompanyId]="companies.length > 0 ? companies[0].id : null"
          [isSystemAdminContext]="true"
          (closed)="closeModals()"
          (saved)="onVehicleSaved($event)">
        </app-vehicle-popup>

        <!-- Detail Modal -->
        <div class="modal-overlay" *ngIf="showViewModal" (mousedown)="closeModals()">
          <div class="modal detail-modal" (mousedown)="$event.stopPropagation()">
            <div class="modal-header">
              <div class="modal-title-row">
                <h2>Detail Vehicule</h2>
                <span class="status-badge" [class]="selectedVehicle?.status || ''">{{ getStatusLabel(selectedVehicle?.status || '') }}</span>
              </div>
              <button class="close-btn" (click)="closeModals()">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                </svg>
              </button>
            </div>

            <div class="modal-body-scroll" *ngIf="selectedVehicle">
              <!-- Vehicle identity -->
              <div class="detail-section">
                <h4>
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="1" y="3" width="15" height="13" rx="2"/><path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-5"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/></svg>
                  Informations Vehicule
                </h4>
                <div class="detail-grid">
                  <div class="detail-item"><span class="detail-label">Nom</span><span class="detail-value">{{ selectedVehicle.name }}</span></div>
                  <div class="detail-item"><span class="detail-label">Matricule</span><span class="detail-value mono">{{ selectedVehicle.plate || '-' }}</span></div>
                  <div class="detail-item"><span class="detail-label">Type</span><span class="detail-value">{{ selectedVehicle.type | titlecase }}</span></div>
                  <div class="detail-item"><span class="detail-label">Marque</span><span class="detail-value">{{ selectedVehicle.brand || '-' }}</span></div>
                  <div class="detail-item"><span class="detail-label">Modele</span><span class="detail-value">{{ selectedVehicle.model || '-' }}</span></div>
                  <div class="detail-item"><span class="detail-label">Annee</span><span class="detail-value">{{ selectedVehicle.year || '-' }}</span></div>
                  <div class="detail-item"><span class="detail-label">Couleur</span><span class="detail-value">{{ selectedVehicle.color || '-' }}</span></div>
                  <div class="detail-item"><span class="detail-label">Kilometrage</span><span class="detail-value highlight">{{ selectedVehicle.mileage | number }} km</span></div>
                  <div class="detail-item"><span class="detail-label">Carburant</span><span class="detail-value">{{ selectedVehicle.fuelType || '-' }}</span></div>
                  <div class="detail-item"><span class="detail-label">Reservoir</span><span class="detail-value">{{ selectedVehicle.fuelTankCapacity ? selectedVehicle.fuelTankCapacity + ' L' : '-' }}</span></div>
                </div>
              </div>

              <!-- Company -->
              <div class="detail-section">
                <h4>
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><polyline points="9 22 9 12 15 12 15 22"/></svg>
                  Societe
                </h4>
                <div class="detail-grid">
                  <div class="detail-item"><span class="detail-label">Nom</span><span class="detail-value">{{ selectedVehicle.companyName || '-' }}</span></div>
                  <div class="detail-item"><span class="detail-label">ID Societe</span><span class="detail-value mono">{{ selectedVehicle.companyId }}</span></div>
                </div>
              </div>

              <!-- GPS -->
              <div class="detail-section">
                <h4>
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><polygon points="16.24 7.76 14.12 14.12 7.76 16.24 9.88 9.88 16.24 7.76"/></svg>
                  GPS
                </h4>
                <div class="detail-grid" *ngIf="selectedVehicle.hasGps">
                  <div class="detail-item"><span class="detail-label">Statut GPS</span><span class="detail-value gps-on">Actif</span></div>
                  <div class="detail-item"><span class="detail-label">IMEI</span><span class="detail-value mono">{{ selectedVehicle.gpsImei || '-' }}</span></div>
                  <div class="detail-item"><span class="detail-label">MAT</span><span class="detail-value mono">{{ selectedVehicle.gpsMat || '-' }}</span></div>
                  <div class="detail-item"><span class="detail-label">Device ID</span><span class="detail-value mono">{{ selectedVehicle.gpsDeviceId || '-' }}</span></div>
                  <div class="detail-item"><span class="detail-label">Marque GPS</span><span class="detail-value">{{ selectedVehicle.gpsBrand || '-' }}</span></div>
                  <div class="detail-item"><span class="detail-label">Modele GPS</span><span class="detail-value">{{ selectedVehicle.gpsModel || '-' }}</span></div>
                  <div class="detail-item"><span class="detail-label">Firmware</span><span class="detail-value mono">{{ selectedVehicle.gpsFirmwareVersion || '-' }}</span></div>
                  <div class="detail-item"><span class="detail-label">Mode Carburant</span><span class="detail-value">{{ selectedVehicle.gpsFuelSensorMode || '-' }}</span></div>
                  <div class="detail-item"><span class="detail-label">N SIM</span><span class="detail-value mono">{{ selectedVehicle.gpsSimNumber || '-' }}</span></div>
                  <div class="detail-item"><span class="detail-label">Operateur</span><span class="detail-value">{{ selectedVehicle.gpsSimOperator || '-' }}</span></div>
                </div>
                <div class="no-gps" *ngIf="!selectedVehicle.hasGps">
                  <span>Aucun GPS associe</span>
                </div>
              </div>

              <!-- Timestamps -->
              <div class="detail-section">
                <h4>
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><path d="M12 6v6l4 2"/></svg>
                  Historique
                </h4>
                <div class="detail-grid">
                  <div class="detail-item"><span class="detail-label">Cree le</span><span class="detail-value">{{ formatDate(selectedVehicle.createdAt) }}</span></div>
                  <div class="detail-item"><span class="detail-label">Mis a jour</span><span class="detail-value">{{ formatDate(selectedVehicle.updatedAt) }}</span></div>
                </div>
              </div>
            </div>

            <div class="modal-footer">
              <button class="btn-secondary" (click)="closeModals()">Fermer</button>
              <button class="btn-primary" (click)="editVehicle(selectedVehicle!)">Modifier</button>
            </div>
          </div>
        </div>

        <!-- Delete Confirmation Modal -->
        <div class="modal-overlay" *ngIf="showDeleteModal" (mousedown)="closeDeleteModal()">
          <div class="modal delete-modal" (mousedown)="$event.stopPropagation()">
            <div class="modal-header delete-header">
              <h2>Supprimer le vehicule</h2>
              <button class="close-btn" (click)="closeDeleteModal()">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                </svg>
              </button>
            </div>
            <div class="modal-body">
              <div class="delete-warning">
                <div class="delete-icon-circle">
                  <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polyline points="3,6 5,6 21,6"/>
                    <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                    <line x1="10" y1="11" x2="10" y2="17"/>
                    <line x1="14" y1="11" x2="14" y2="17"/>
                  </svg>
                </div>
                <p class="delete-title">Etes-vous sur de vouloir supprimer ce vehicule ?</p>
                <div class="delete-vehicle-info" *ngIf="vehicleToDelete">
                  <span class="delete-vehicle-name">{{ vehicleToDelete.name }}</span>
                  <span class="delete-vehicle-plate" *ngIf="vehicleToDelete.plate">{{ vehicleToDelete.plate }}</span>
                  <span class="delete-vehicle-mat" *ngIf="vehicleToDelete.gpsMat">MAT: {{ vehicleToDelete.gpsMat }}</span>
                </div>
                <p class="warning-text">Cette action est irreversible. Toutes les donnees associees seront perdues.</p>
              </div>
            </div>
            <div class="modal-footer">
              <button class="btn-secondary" (click)="closeDeleteModal()">Annuler</button>
              <button class="btn-danger" (click)="deleteVehicle()">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <polyline points="3,6 5,6 21,6"/>
                  <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                </svg>
                Supprimer
              </button>
            </div>
          </div>
        </div>

      </div>
    </admin-layout>
  `,
  styles: [`
    .vehicles-page { display: flex; flex-direction: column; gap: 20px; }

    @keyframes rise { from { opacity: 0; transform: translateY(8px); } to { opacity: 1; transform: none; } }

    /* Header */
    .page-header { display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 12px; }
    .header-left { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
    .search-box { display: flex; align-items: center; gap: 10px; background: var(--adm-card); border: 1px solid var(--adm-border); border-radius: 10px; padding: 9px 14px; width: 320px; transition: border-color 0.15s, box-shadow 0.15s; }
    .search-box:focus-within { border-color: var(--adm-indigo); box-shadow: 0 0 0 3px rgba(79,70,229,0.12); }
    .search-box svg { color: var(--adm-sub); flex-shrink: 0; }
    .search-box input { flex: 1; border: none; background: transparent; color: var(--adm-ink); font-size: 13px; outline: none; }
    .filter-select { padding: 9px 14px; background: var(--adm-card); border: 1px solid var(--adm-border); border-radius: 10px; color: var(--adm-ink); font-size: 13px; outline: none; cursor: pointer; transition: border-color 0.15s, box-shadow 0.15s; }
    .filter-select:focus { border-color: var(--adm-indigo); box-shadow: 0 0 0 3px rgba(79,70,229,0.12); }
    .add-btn { display: flex; align-items: center; gap: 8px; padding: 9px 18px; background: var(--adm-indigo); border: none; border-radius: 10px; color: #fff; font-size: 13px; font-weight: 600; cursor: pointer; transition: all 0.2s; }
    .add-btn:hover { background: var(--adm-indigo-ink); transform: translateY(-1px); box-shadow: var(--adm-shadow-hover); }

    /* Stats bar */
    .stats-bar { display: flex; gap: 12px; flex-wrap: wrap; }
    .stat-card { background: var(--adm-card); border: 1px solid var(--adm-border); border-left: 3px solid var(--adm-slate); border-radius: 16px; box-shadow: var(--adm-shadow); padding: 14px 20px; display: flex; flex-direction: column; align-items: center; min-width: 100px; flex: 1; animation: rise 0.25s ease-out both; }
    .stat-number { font-size: 25px; font-weight: 800; color: var(--adm-ink); font-variant-numeric: tabular-nums; }
    .stat-text { font-size: 11px; color: var(--adm-sub); text-transform: uppercase; letter-spacing: 0.06em; margin-top: 2px; }
    .stat-card.available { border-left-color: var(--adm-green); }
    .stat-card.in-use { border-left-color: var(--adm-indigo); }
    .stat-card.maintenance { border-left-color: var(--adm-amber); }
    .stat-card.gps { border-left-color: var(--adm-cyan); }

    /* Table container */
    .table-container { background: var(--adm-card); border: 1px solid var(--adm-border); border-radius: 16px; box-shadow: var(--adm-shadow); overflow: hidden; animation: rise 0.25s ease-out both; }
    .vehicles-table { width: 100%; border-collapse: collapse; }
    .vehicles-table thead { background: #f8fafc; }
    .vehicles-table th { padding: 12px 16px; text-align: left; font-size: 11px; font-weight: 700; color: var(--adm-sub); text-transform: uppercase; letter-spacing: 0.06em; border-bottom: 1px solid var(--adm-border); white-space: nowrap; user-select: none; }
    .th-sortable { cursor: pointer; transition: color 0.15s; }
    .th-sortable:hover { color: var(--adm-ink); }
    .sort-icon { font-size: 10px; margin-left: 4px; color: var(--adm-indigo); }
    .th-actions { text-align: center; width: 130px; }

    .vehicles-table td { padding: 12px 16px; font-size: 13px; color: var(--adm-ink); border-bottom: 1px solid #eef2f7; vertical-align: middle; }
    .vehicles-table tbody tr { transition: background 0.15s; }
    .vehicles-table tbody tr:hover { background: #f8fafc; }
    .vehicles-table tbody tr:last-child td { border-bottom: none; }

    /* Cell styles */
    .td-plate { font-family: 'Courier New', monospace; font-weight: 600; }
    .plate-badge { background: var(--adm-track); padding: 4px 10px; border-radius: 6px; font-size: 13px; color: var(--adm-ink); letter-spacing: 0.5px; font-variant-numeric: tabular-nums; }
    .td-mat { font-family: 'Courier New', monospace; }
    .mat-badge { background: rgba(79,70,229,0.10); color: var(--adm-indigo-ink); padding: 4px 10px; border-radius: 999px; font-size: 11px; font-weight: 700; }
    .td-imei { font-family: 'Courier New', monospace; }
    .imei-badge { background: rgba(8,145,178,0.10); color: var(--adm-cyan-ink); padding: 4px 10px; border-radius: 999px; font-size: 11px; font-weight: 700; letter-spacing: 0.3px; }
    .td-km { font-weight: 600; color: var(--adm-ink); white-space: nowrap; font-variant-numeric: tabular-nums; }
    .td-phone { font-family: 'Courier New', monospace; font-size: 12px; font-variant-numeric: tabular-nums; }
    .text-muted { color: #cbd5e1; }
    .cell-name { display: flex; flex-direction: column; }
    .vehicle-name { font-weight: 500; color: var(--adm-ink); }
    .vehicle-sub { font-size: 11px; color: var(--adm-sub); margin-top: 2px; }
    .company-badge { display: inline-block; padding: 3px 10px; background: rgba(100,116,139,0.10); color: var(--adm-slate-ink); border-radius: 999px; font-size: 11px; font-weight: 700; }

    .status-badge { padding: 4px 10px; border-radius: 999px; font-size: 11px; font-weight: 700; white-space: nowrap; }
    .status-badge.available { background: rgba(5,150,105,0.10); color: var(--adm-green-ink); }
    .status-badge.in_use { background: rgba(79,70,229,0.10); color: var(--adm-indigo-ink); }
    .status-badge.maintenance { background: rgba(217,119,6,0.12); color: var(--adm-amber-ink); }

    /* Action buttons */
    .td-actions { text-align: center; white-space: nowrap; }
    .action-btn { width: 32px; height: 32px; border: none; border-radius: 8px; cursor: pointer; display: inline-flex; align-items: center; justify-content: center; transition: all 0.15s; margin: 0 2px; }
    .action-btn.detail { background: rgba(100,116,139,0.10); color: var(--adm-slate-ink); }
    .action-btn.detail:hover { background: rgba(100,116,139,0.18); }
    .action-btn.edit { background: rgba(79,70,229,0.10); color: var(--adm-indigo-ink); }
    .action-btn.edit:hover { background: rgba(79,70,229,0.18); }
    .action-btn.delete { background: rgba(220,38,38,0.10); color: var(--adm-red-ink); }
    .action-btn.delete:hover { background: rgba(220,38,38,0.18); }

    .table-footer { padding: 12px 16px; border-top: 1px solid var(--adm-border); background: #f8fafc; }
    .result-count { font-size: 12px; color: var(--adm-sub); font-variant-numeric: tabular-nums; }

    /* Empty state */
    .empty-state { display: flex; flex-direction: column; align-items: center; padding: 60px 20px; color: var(--adm-sub); }
    .empty-state svg { margin-bottom: 16px; opacity: 0.4; }
    .empty-state h3 { margin: 0 0 6px; font-size: 16px; color: var(--adm-ink); font-weight: 600; }
    .empty-state p { margin: 0; font-size: 13px; }

    /* Modals shared */
    .modal-overlay { position: fixed; inset: 0; background: rgba(15,23,42,0.55); backdrop-filter: blur(4px); display: flex; align-items: center; justify-content: center; z-index: 1000; padding: 20px; }
    .modal { background: var(--adm-card); border-radius: 18px; max-width: 96vw; max-height: 92vh; display: flex; flex-direction: column; overflow: hidden; box-shadow: 0 24px 60px -24px rgba(2,6,23,0.45); }
    .modal-header { display: flex; align-items: center; justify-content: space-between; padding: 18px 24px; border-bottom: 1px solid var(--adm-border); }
    .modal-header h2 { margin: 0; font-size: 17px; font-weight: 600; color: var(--adm-ink); }
    .modal-title-row { display: flex; align-items: center; gap: 12px; }
    .close-btn { width: 34px; height: 34px; border: none; background: var(--adm-track); border-radius: 10px; color: var(--adm-sub); cursor: pointer; display: flex; align-items: center; justify-content: center; transition: all 0.15s; }
    .close-btn:hover { background: var(--adm-border); color: var(--adm-ink); }
    .modal-footer { display: flex; justify-content: flex-end; gap: 10px; padding: 16px 24px; border-top: 1px solid var(--adm-border); }
    .btn-secondary { padding: 9px 18px; background: #fff; border: 1px solid var(--adm-border); border-radius: 10px; color: var(--adm-ink); font-size: 13px; font-weight: 600; cursor: pointer; transition: all 0.15s; }
    .btn-secondary:hover { border-color: var(--adm-indigo); }
    .btn-primary { padding: 9px 18px; background: var(--adm-indigo); border: none; border-radius: 10px; color: #fff; font-size: 13px; font-weight: 600; cursor: pointer; transition: all 0.15s; }
    .btn-primary:hover { background: var(--adm-indigo-ink); }
    .btn-danger { padding: 9px 18px; background: var(--adm-red); border: none; border-radius: 10px; color: #fff; font-size: 13px; font-weight: 600; cursor: pointer; transition: all 0.15s; display: flex; align-items: center; gap: 6px; }
    .btn-danger:hover { background: var(--adm-red-ink); }

    /* Detail Modal */
    .detail-modal { width: 720px; }
    .modal-body-scroll { padding: 20px 24px; overflow-y: auto; max-height: calc(92vh - 140px); display: flex; flex-direction: column; gap: 4px; }
    .detail-section { padding: 16px 0; border-bottom: 1px solid var(--adm-track); }
    .detail-section:last-child { border-bottom: none; }
    .detail-section h4 { margin: 0 0 14px; font-size: 12px; font-weight: 700; color: var(--adm-sub); text-transform: uppercase; letter-spacing: 0.08em; display: flex; align-items: center; gap: 8px; }
    .detail-section h4::before { content: ''; width: 3px; height: 12px; border-radius: 2px; background: var(--adm-indigo); flex-shrink: 0; }
    .detail-section h4 svg { color: var(--adm-indigo); }
    .detail-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 10px 24px; }
    .detail-item { display: flex; flex-direction: column; gap: 2px; padding: 8px 12px; background: #f8fafc; border-radius: 8px; }
    .detail-label { font-size: 11px; color: var(--adm-sub); text-transform: uppercase; letter-spacing: 0.06em; }
    .detail-value { font-size: 14px; color: var(--adm-ink); font-weight: 500; }
    .detail-value.mono { font-family: 'Courier New', monospace; letter-spacing: 0.5px; }
    .detail-value.highlight { color: var(--adm-indigo-ink); font-weight: 700; font-variant-numeric: tabular-nums; }
    .detail-value.gps-on { color: var(--adm-green-ink); font-weight: 600; }
    .no-gps { text-align: center; padding: 20px; color: var(--adm-sub); font-size: 13px; background: #f8fafc; border-radius: 8px; }

    /* Delete Modal */
    .delete-modal { max-width: 440px; }
    .delete-header h2 { color: var(--adm-red); }
    .modal-body { padding: 24px; }
    .delete-warning { text-align: center; }
    .delete-icon-circle { width: 64px; height: 64px; background: rgba(220,38,38,0.10); border-radius: 50%; display: flex; align-items: center; justify-content: center; margin: 0 auto 16px; color: var(--adm-red); }
    .delete-title { font-size: 16px; font-weight: 600; color: var(--adm-ink); margin: 0 0 12px; }
    .delete-vehicle-info { display: flex; flex-direction: column; align-items: center; gap: 4px; padding: 12px; background: #f8fafc; border-radius: 10px; margin-bottom: 12px; }
    .delete-vehicle-name { font-size: 15px; font-weight: 600; color: var(--adm-ink); }
    .delete-vehicle-plate { font-family: monospace; font-size: 13px; color: var(--adm-sub); }
    .delete-vehicle-mat { font-size: 12px; color: var(--adm-indigo-ink); font-weight: 600; }
    .warning-text { font-size: 13px; color: var(--adm-sub); margin: 0; }

    @media (prefers-reduced-motion: reduce) {
      .stat-card, .table-container { animation: none; }
      .add-btn, .add-btn:hover { transform: none; }
    }

    @media (max-width: 900px) {
      .table-container { overflow-x: auto; }
      .vehicles-table { min-width: 950px; }
      .stats-bar { gap: 8px; }
      .stat-card { min-width: 80px; padding: 10px 14px; }
      .stat-number { font-size: 18px; }
      .detail-grid { grid-template-columns: 1fr; }
    }
    @media (max-width: 640px) {
      .header-left { width: 100%; }
      .search-box { width: 100%; }
    }
  `]
})
export class AdminVehiclesComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  vehicles: AdminVehicle[] = [];
  filteredVehicles: AdminVehicle[] = [];
  companies: Client[] = [];

  searchQuery = '';
  statusFilter = 'all';
  companyFilter = 'all';
  sortColumn = 'gpsMat';
  sortDirection: 'asc' | 'desc' = 'asc';

  showAddModal = false;
  showEditModal = false;
  showViewModal = false;
  showDeleteModal = false;
  selectedVehicle: AdminVehicle | null = null;
  vehicleToDelete: AdminVehicle | null = null;

  // Getter for the popup component - converts AdminVehicle to the format expected by VehiclePopupComponent
  get selectedVehicleForPopup(): any {
    if (!this.showEditModal || !this.selectedVehicle) return null;
    return {
      id: this.selectedVehicle.id,
      name: this.selectedVehicle.name,
      plate: this.selectedVehicle.plate,
      brand: this.selectedVehicle.brand,
      model: this.selectedVehicle.model,
      year: this.selectedVehicle.year,
      type: this.selectedVehicle.type,
      status: this.selectedVehicle.status,
      mileage: this.selectedVehicle.mileage,
      fuelTankCapacity: this.selectedVehicle.fuelTankCapacity,
      color: this.selectedVehicle.color,
      fuelType: this.selectedVehicle.fuelType,
      companyId: this.selectedVehicle.companyId,
      hasGPS: this.selectedVehicle.hasGps,
      gpsDeviceId: this.selectedVehicle.gpsDeviceId,
      gpsImei: this.selectedVehicle.gpsImei,
      gpsMat: this.selectedVehicle.gpsMat,
      gpsBrand: this.selectedVehicle.gpsBrand,
      gpsModel: this.selectedVehicle.gpsModel,
      gpsFirmwareVersion: this.selectedVehicle.gpsFirmwareVersion,
      gpsFuelSensorMode: this.selectedVehicle.gpsFuelSensorMode,
      gpsSimNumber: this.selectedVehicle.gpsSimNumber,
      gpsSimOperator: this.selectedVehicle.gpsSimOperator,
      gpsInstallationDate: this.selectedVehicle.gpsInstallationDate
    };
  }

  // Getter to convert companies for the popup
  get companiesForPopup(): { id: number; name: string }[] {
    return this.companies.map(c => ({ id: c.id, name: c.name }));
  }

  vehicleForm = {
    name: '',
    type: 'citadine',
    brand: '',
    model: '',
    plate: '',
    year: new Date().getFullYear(),
    color: '',
    status: 'available' as 'available' | 'in_use' | 'maintenance',
    mileage: 0,
    companyId: 1,
    hasGps: false,
    gpsDeviceId: undefined as number | undefined,
    gpsImei: '',
    gpsMat: '',
    gpsModel: '',
    gpsFirmwareVersion: ''
  };

  constructor(
    private router: Router,
    private adminService: AdminService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit() {
    if (!this.adminService.isAuthenticated()) {
      this.router.navigate(['/admin/login']);
      return;
    }
    this.loadData();
  }

  loadData() {
    console.log('Loading vehicles data...');
    this.adminService.getVehicles().pipe(takeUntil(this.destroy$)).subscribe({
      next: (vehicles) => {
        console.log('Vehicles loaded:', vehicles.length);
        this.vehicles = vehicles;
        this.filterVehicles();
        this.cdr.detectChanges();
      },
      error: (err) => {
        console.error('Error loading vehicles:', err);
        this.vehicles = [];
        this.filterVehicles();
      }
    });

    this.adminService.getClients().pipe(takeUntil(this.destroy$)).subscribe({
      next: (clients) => {
        console.log('Loaded companies for vehicle assignment:', clients.length);
        this.companies = clients;
      },
      error: (err) => {
        console.error('Error loading companies:', err);
        this.companies = [];
      }
    });
  }

  filterVehicles() {
    const q = this.searchQuery.toLowerCase();
    let result = this.vehicles.filter(vehicle => {
      const matchesSearch = !q ||
        vehicle.name.toLowerCase().includes(q) ||
        (vehicle.plate && vehicle.plate.toLowerCase().includes(q)) ||
        (vehicle.gpsMat && vehicle.gpsMat.toLowerCase().includes(q)) ||
        (vehicle.gpsImei && vehicle.gpsImei.toLowerCase().includes(q)) ||
        (vehicle.gpsSimNumber && vehicle.gpsSimNumber.toLowerCase().includes(q)) ||
        (vehicle.companyName && vehicle.companyName.toLowerCase().includes(q)) ||
        (vehicle.brand && vehicle.brand.toLowerCase().includes(q));

      const matchesStatus = this.statusFilter === 'all' || vehicle.status === this.statusFilter;

      const matchesCompany = this.companyFilter === 'all' ||
        vehicle.companyId.toString() === this.companyFilter;

      return matchesSearch && matchesStatus && matchesCompany;
    });

    // Sort
    const col = this.sortColumn as keyof AdminVehicle;
    const dir = this.sortDirection === 'asc' ? 1 : -1;
    result.sort((a, b) => {
      const va = a[col] ?? '';
      const vb = b[col] ?? '';
      if (typeof va === 'number' && typeof vb === 'number') return (va - vb) * dir;
      return String(va).localeCompare(String(vb)) * dir;
    });

    this.filteredVehicles = result;
  }

  toggleSort(column: string) {
    if (this.sortColumn === column) {
      this.sortDirection = this.sortDirection === 'asc' ? 'desc' : 'asc';
    } else {
      this.sortColumn = column;
      this.sortDirection = 'asc';
    }
    this.filterVehicles();
  }

  trackById(index: number, vehicle: AdminVehicle): number {
    return vehicle.id;
  }

  countByStatus(status: string): number {
    return this.vehicles.filter(v => v.status === status).length;
  }

  countWithGps(): number {
    return this.vehicles.filter(v => v.hasGps).length;
  }

  getStatusLabel(status: string): string {
    const labels: Record<string, string> = {
      'available': 'Disponible',
      'in_use': 'En service',
      'maintenance': 'Maintenance'
    };
    return labels[status] || status;
  }

  openAddModal() {
    this.resetForm();
    this.showAddModal = true;
  }

  editVehicle(vehicle: AdminVehicle) {
    this.selectedVehicle = vehicle;
    this.vehicleForm = {
      name: vehicle.name,
      type: vehicle.type,
      brand: vehicle.brand || '',
      model: vehicle.model || '',
      plate: vehicle.plate || '',
      year: vehicle.year || new Date().getFullYear(),
      color: vehicle.color || '',
      status: vehicle.status,
      mileage: vehicle.mileage,
      companyId: vehicle.companyId || 1,
      hasGps: vehicle.hasGps,
      gpsDeviceId: vehicle.gpsDeviceId,
      gpsImei: vehicle.gpsImei || '',
      gpsMat: vehicle.gpsMat || '',
      gpsModel: vehicle.gpsModel || '',
      gpsFirmwareVersion: vehicle.gpsFirmwareVersion || ''
    };
    this.showViewModal = false;
    this.showEditModal = true;
  }

  viewVehicle(vehicle: AdminVehicle) {
    this.selectedVehicle = vehicle;
    this.showViewModal = true;
  }

  confirmDelete(vehicle: AdminVehicle) {
    this.vehicleToDelete = vehicle;
    this.showDeleteModal = true;
  }

  deleteVehicle() {
    if (this.vehicleToDelete) {
      this.adminService.deleteVehicle(this.vehicleToDelete.id).pipe(takeUntil(this.destroy$)).subscribe({
        next: () => {
          this.loadData();
          this.closeDeleteModal();
        },
        error: (err) => {
          // Never fail silently — surface the reason so the operator knows.
          console.error('[AdminVehicles] delete failed', err);
          alert(err?.error?.message || "La suppression du véhicule a échoué. Réessayez.");
          this.closeDeleteModal();
        },
      });
    }
  }

  // Handler for VehiclePopupComponent saved event
  onVehicleSaved(formData: any) {
    console.log('[AdminVehicles] onVehicleSaved received formData:', JSON.stringify({
      hasGPS: formData.hasGPS,
      gpsSimNumber: formData.gpsSimNumber,
      gpsSimOperator: formData.gpsSimOperator,
      gpsImei: formData.gpsImei,
      gpsDeviceId: formData.gpsDeviceId
    }));
    // Use selected companyId from form, fallback to first company or 1
    const selectedCompanyId = formData.companyId 
      ? parseInt(formData.companyId, 10) 
      : (this.companies.length > 0 ? this.companies[0].id : 1);

    const vehicleData: Partial<AdminVehicle> = {
      name: formData.name,
      type: formData.type,
      brand: formData.brand || undefined,
      model: formData.model || undefined,
      plate: formData.plate || undefined,
      year: formData.year || undefined,
      color: formData.color || undefined,
      status: formData.status,
      mileage: formData.mileage || 0,
      fuelType: formData.fuelType || undefined,
      fuelTankCapacity: formData.fuelTankCapacity || undefined,
      companyId: selectedCompanyId,
      hasGps: formData.hasGPS || false,
      gpsDeviceId: formData.hasGPS ? formData.gpsDeviceId : undefined,
      gpsImei: formData.hasGPS ? formData.gpsImei || undefined : undefined,
      gpsMat: formData.hasGPS ? formData.gpsMat || undefined : undefined,
      gpsBrand: formData.hasGPS ? formData.gpsBrand || undefined : undefined,
      gpsModel: formData.hasGPS ? formData.gpsModel || undefined : undefined,
      gpsFirmwareVersion: formData.hasGPS ? formData.gpsFirmwareVersion || undefined : undefined,
      gpsFuelSensorMode: formData.hasGPS ? formData.gpsFuelSensorMode || undefined : undefined,
      gpsSimNumber: formData.hasGPS ? formData.gpsSimNumber || undefined : undefined,
      gpsSimOperator: formData.hasGPS ? formData.gpsSimOperator || undefined : undefined,
      gpsInstallationDate: formData.hasGPS ? formData.gpsInstallationDate || undefined : undefined
    };

    if (this.showEditModal && this.selectedVehicle) {
      this.adminService.updateVehicle(this.selectedVehicle.id, vehicleData).pipe(takeUntil(this.destroy$)).subscribe({
        next: () => {
          this.loadData();
          this.closeModals();
        },
        error: (err) => {
          console.error('Error updating vehicle:', err);
          // Le backend renvoie un message précis (ex: doublon IMEI/MAT/SIM refusé) — l'afficher tel quel.
          alert(err?.error?.message || 'Erreur lors de la mise à jour du véhicule');
        }
      });
    } else {
      this.adminService.createVehicle(vehicleData).pipe(takeUntil(this.destroy$)).subscribe({
        next: () => {
          this.loadData();
          this.closeModals();
        },
        error: (err) => {
          console.error('Error creating vehicle:', err);
          alert(err?.error?.message || 'Erreur lors de la création du véhicule');
        }
      });
    }
  }

  saveVehicle() {
    if (!this.isFormValid()) return;

    const vehicleData: Partial<AdminVehicle> = {
      name: this.vehicleForm.name,
      type: this.vehicleForm.type,
      brand: this.vehicleForm.brand || undefined,
      model: this.vehicleForm.model || undefined,
      plate: this.vehicleForm.plate || undefined,
      year: this.vehicleForm.year || undefined,
      color: this.vehicleForm.color || undefined,
      status: this.vehicleForm.status,
      mileage: this.vehicleForm.mileage,
      companyId: 1,
      hasGps: this.vehicleForm.hasGps,
      gpsDeviceId: this.vehicleForm.hasGps ? this.vehicleForm.gpsDeviceId : undefined,
      gpsImei: this.vehicleForm.hasGps ? this.vehicleForm.gpsImei || undefined : undefined,
      gpsMat: this.vehicleForm.hasGps ? this.vehicleForm.gpsMat || undefined : undefined,
      gpsBrand: this.vehicleForm.hasGps ? (this.vehicleForm as any).gpsBrand || undefined : undefined,
      gpsModel: this.vehicleForm.hasGps ? this.vehicleForm.gpsModel || undefined : undefined,
      gpsFirmwareVersion: this.vehicleForm.hasGps ? this.vehicleForm.gpsFirmwareVersion || undefined : undefined,
      gpsFuelSensorMode: this.vehicleForm.hasGps ? (this.vehicleForm as any).gpsFuelSensorMode || undefined : undefined
    };

    if (this.showEditModal && this.selectedVehicle) {
      this.adminService.updateVehicle(this.selectedVehicle.id, vehicleData).pipe(takeUntil(this.destroy$)).subscribe(() => {
        this.loadData();
        this.closeModals();
      });
    } else {
      this.adminService.createVehicle(vehicleData).pipe(takeUntil(this.destroy$)).subscribe(() => {
        this.loadData();
        this.closeModals();
      });
    }
  }

  isFormValid(): boolean {
    return !!(this.vehicleForm.name && this.vehicleForm.type);
  }

  closeModals() {
    this.showAddModal = false;
    this.showEditModal = false;
    this.showViewModal = false;
    this.selectedVehicle = null;
    this.resetForm();
  }

  closeDeleteModal() {
    this.showDeleteModal = false;
    this.vehicleToDelete = null;
  }

  resetForm() {
    this.vehicleForm = {
      name: '',
      type: 'citadine',
      brand: '',
      model: '',
      plate: '',
      year: new Date().getFullYear(),
      color: '',
      status: 'available',
      mileage: 0,
      companyId: 1,
      hasGps: false,
      gpsDeviceId: undefined,
      gpsImei: '',
      gpsMat: '',
      gpsModel: '',
      gpsFirmwareVersion: ''
    };
  }

  formatDate(date: Date): string {
    if (!date) return 'N/A';
    return new Date(date).toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
