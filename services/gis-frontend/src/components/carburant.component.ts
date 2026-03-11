import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';
import { AppLayoutComponent } from './shared/app-layout.component';
import { ApiService, FuelTypeDto, VehicleWithPositionDto, FuelEntryDto, FuelPriceFullDto } from '../services/api.service';
declare const XLSX: any;

interface FuelEntry {
  id?: number;
  vehiclePlate: string;
  volume: number;
  pricePerLiter: number;
  totalAmount: number;
  fuelTypeId: number;
  fuelTypeName: string;
  invoiceDate: string;
  isValid?: boolean;
  errors?: string[];
}

interface ColumnMapping {
  vehiclePlate: number | null;
  volume: number | null;
  pricePerLiter: number | null;
  invoiceDate: number | null;
  fuelType: number | null;
}

@Component({
  selector: 'app-carburant',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent],
  template: `
    <app-layout>
      <div class="carburant-page">
        <!-- Filter Bar -->
        <div class="filter-bar">
          <div class="page-title">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M3 22V8l9-6 9 6v14"/><circle cx="18" cy="6" r="2"/>
            </svg>
            <span>Gestion Carburant</span>
          </div>
          <div class="tabs-inline">
            <button class="tab-btn" [class.active]="activeTab === 'manual'" (click)="activeTab = 'manual'">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
              </svg>
              Saisie Manuelle
            </button>
            <button class="tab-btn" [class.active]="activeTab === 'import'" (click)="activeTab = 'import'">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/>
                <polyline points="17,8 12,3 7,8"/><line x1="12" y1="3" x2="12" y2="15"/>
              </svg>
              Import Excel
            </button>
            <button class="tab-btn" [class.active]="activeTab === 'history'" (click)="activeTab = 'history'">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10"/><polyline points="12,6 12,12 16,14"/>
              </svg>
              Historique
            </button>
          </div>
        </div>

        <!-- Stats Bar -->
        <div class="stats-bar">
          <div class="stat-item">
            <div class="stat-icon info">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ fuelHistory.length }}</span>
              <span class="stat-label">Entrées</span>
            </div>
          </div>
          <div class="stat-item">
            <div class="stat-icon active">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M12 2v20M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ getAveragePrice() | number:'1.2-2' }}</span>
              <span class="stat-label">Prix Moyen/L</span>
            </div>
          </div>
          <div class="stat-item">
            <div class="stat-icon warning">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M3 22V8l9-6 9 6v14"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ getTotalVolume() | number:'1.0-0' }} L</span>
              <span class="stat-label">Volume Total</span>
            </div>
          </div>
          <div class="stat-item">
            <div class="stat-icon available">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="1" y="4" width="22" height="16" rx="2"/><line x1="1" y1="10" x2="23" y2="10"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ getTotalAmount() | number:'1.2-2' }}</span>
              <span class="stat-label">Montant Total</span>
            </div>
          </div>
        </div>

        <!-- Content Area -->
        <div class="content-area">
          <!-- TAB: Manual Entry -->
          <div class="tab-panel" *ngIf="activeTab === 'manual'">
            <div class="panel-header">
              <h2>Nouvelle Entrée Carburant</h2>
              <p>Saisissez les informations de la facture carburant</p>
            </div>
            
            <div class="form-card">
              <div class="form-grid">
                <div class="form-group">
                  <label>Matricule Véhicule <span class="required">*</span></label>
                  <select [(ngModel)]="manualEntry.vehiclePlate" class="form-control">
                    <option value="">Sélectionner un véhicule...</option>
                    <option *ngFor="let v of vehicles" [value]="v.plate || v.name">{{ v.plate || v.name }}</option>
                  </select>
                </div>
                <div class="form-group">
                  <label>Type Carburant <span class="required">*</span></label>
                  <select [(ngModel)]="manualEntry.fuelTypeId" (ngModelChange)="onFuelTypeChange($event)" class="form-control">
                    <option [ngValue]="null">Sélectionner...</option>
                    <option *ngFor="let ft of fuelTypes" [ngValue]="ft.id">{{ ft.name }}</option>
                  </select>
                </div>
                <div class="form-group">
                  <label>Volume (Litres) <span class="required">*</span></label>
                  <input type="number" [(ngModel)]="manualEntry.volume" class="form-control" placeholder="Ex: 45.5" step="0.01" min="0">
                </div>
                <div class="form-group">
                  <label>Prix par Litre <span class="required">*</span></label>
                  <input type="number" [(ngModel)]="manualEntry.pricePerLiter" class="form-control" placeholder="Ex: 12.450" step="0.001" min="0">
                </div>
                <div class="form-group">
                  <label>Date Facture <span class="required">*</span></label>
                  <input type="date" [(ngModel)]="manualEntry.invoiceDate" class="form-control">
                </div>
                <div class="form-group">
                  <label>Montant Total</label>
                  <div class="computed-value">{{ (manualEntry.volume * manualEntry.pricePerLiter) | number:'1.2-2' }}</div>
                </div>
              </div>
              <div class="form-actions">
                <button class="btn-reset" (click)="resetManualEntry()">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"/>
                    <path d="M3 3v5h5"/>
                  </svg>
                  Réinitialiser
                </button>
                <button class="btn-add" (click)="saveManualEntry()" [disabled]="!isManualEntryValid() || isSaving">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/>
                    <polyline points="17 21 17 13 7 13 7 21"/><polyline points="7 3 7 8 15 8"/>
                  </svg>
                  {{ isSaving ? 'Enregistrement...' : 'Enregistrer' }}
                </button>
              </div>
            </div>

            <!-- Recent entries preview -->
            <div class="recent-entries" *ngIf="fuelHistory.length > 0">
              <h3>Dernières Entrées</h3>
              <table class="data-table">
                <thead>
                  <tr>
                    <th>Date</th>
                    <th>Matricule</th>
                    <th>Type</th>
                    <th>Volume</th>
                    <th>Prix/L</th>
                    <th>Total</th>
                  </tr>
                </thead>
                <tbody>
                  <tr *ngFor="let entry of fuelHistory.slice(0, 5)">
                    <td>{{ entry.invoiceDate | date:'dd/MM/yyyy' }}</td>
                    <td><span class="plate-badge">{{ entry.vehiclePlate || '-' }}</span></td>
                    <td><span class="fuel-badge">{{ entry.fuelTypeName }}</span></td>
                    <td>{{ entry.volume | number:'1.2-2' }} L</td>
                    <td>{{ entry.pricePerLiter | number:'1.3-3' }}</td>
                    <td class="total-cell">{{ entry.totalAmount | number:'1.2-2' }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>

          <!-- TAB: Import Excel -->
          <div class="tab-panel" *ngIf="activeTab === 'import'">
            <!-- Upload Zone -->
            <div class="upload-section" *ngIf="!workbook">
              <div class="panel-header">
                <h2>Import Fichier Excel</h2>
                <p>Importez vos factures carburant depuis un fichier Excel</p>
              </div>
              <div class="upload-zone" [class.dragover]="isDragOver"
                   (dragover)="onDragOver($event)" (dragleave)="onDragLeave($event)" (drop)="onDrop($event)">
                <input type="file" #fileInput (change)="onFileSelected($event)" accept=".xlsx,.xls" style="display:none">
                <div class="upload-content" (click)="fileInput.click()">
                  <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                    <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/>
                    <polyline points="17,8 12,3 7,8"/><line x1="12" y1="3" x2="12" y2="15"/>
                  </svg>
                  <h3>Glissez votre fichier ici</h3>
                  <p>ou cliquez pour sélectionner</p>
                  <span class="file-hint">.xlsx, .xls</span>
                </div>
              </div>
            </div>

            <!-- Mapping Section -->
            <div class="mapping-section" *ngIf="workbook && !showPreview">
              <div class="panel-header">
                <h2>Configuration du Mapping</h2>
                <button class="btn-link" (click)="resetImport()">✕ Annuler</button>
              </div>
              <div class="mapping-grid">
                <div class="mapping-card" *ngFor="let f of mappingFields"
                     [class.mapped]="columnMapping[f.key] !== null"
                     [class.active]="activeMappingField === f.key"
                     (click)="startMapping(f.key)">
                  <span class="field-label">{{ f.label }}{{ f.required ? ' *' : '' }}</span>
                  <span class="field-value">{{ columnMapping[f.key] !== null ? getColumnName(columnMapping[f.key]!) : 'Cliquez pour sélectionner' }}</span>
                  <button *ngIf="columnMapping[f.key] !== null" class="btn-clear" (click)="clearMapping(f.key, $event)">✕</button>
                </div>
              </div>
              <div class="mapping-hint" *ngIf="activeMappingField">
                👆 Cliquez sur une colonne du tableau pour: <strong>{{ getMappingLabel(activeMappingField) }}</strong>
              </div>
              <div class="excel-preview">
                <div class="preview-header">
                  <span>{{ sheetNames[selectedSheetIndex] }}</span>
                  <select *ngIf="sheetNames.length > 1" [(ngModel)]="selectedSheetIndex" (change)="loadSheet()">
                    <option *ngFor="let s of sheetNames; let i = index" [value]="i">{{ s }}</option>
                  </select>
                </div>
                <div class="table-scroll">
                  <table class="excel-table">
                    <thead>
                      <tr>
                        <th class="row-num">#</th>
                        <th *ngFor="let col of excelHeaders; let i = index"
                            [class.mapped]="isColumnMapped(i)" [class.selectable]="activeMappingField !== null"
                            (click)="selectColumn(i)">
                          <span class="col-letter">{{ getExcelColumnLetter(i) }}</span>
                          {{ col || '(vide)' }}
                          <span class="mapped-tag" *ngIf="getColumnMappedField(i)">{{ getColumnMappedField(i) }}</span>
                        </th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr *ngFor="let row of excelData.slice(0, 15); let ri = index">
                        <td class="row-num">{{ ri + 2 }}</td>
                        <td *ngFor="let cell of row; let ci = index" [class.mapped]="isColumnMapped(ci)">{{ cell }}</td>
                      </tr>
                    </tbody>
                  </table>
                </div>
              </div>
              <div class="mapping-actions">
                <div class="start-row">
                  <label>Ligne de début:</label>
                  <input type="number" [(ngModel)]="dataStartRow" min="2">
                </div>
                <button class="btn-add" (click)="processData()" [disabled]="!isMappingComplete()">Valider</button>
              </div>
            </div>

            <!-- Preview Section -->
            <div class="preview-section" *ngIf="showPreview">
              <div class="panel-header">
                <h2>Validation des Données</h2>
                <button class="btn-link" (click)="showPreview = false">← Retour</button>
              </div>
              <div class="validation-stats">
                <div class="vstat success">{{ getValidEntries().length }} valides</div>
                <div class="vstat error" *ngIf="getInvalidEntries().length > 0">{{ getInvalidEntries().length }} erreurs</div>
              </div>
              <div class="default-fuel" *ngIf="columnMapping.fuelType === null">
                <label>Type carburant par défaut:</label>
                <select [(ngModel)]="defaultFuelTypeId">
                  <option *ngFor="let ft of fuelTypes" [ngValue]="ft.id">{{ ft.name }}</option>
                </select>
              </div>
              <table class="data-table">
                <thead>
                  <tr><th>Statut</th><th>Matricule</th><th>Volume</th><th>Prix/L</th><th>Total</th><th>Date</th><th>Type</th><th>Erreurs</th></tr>
                </thead>
                <tbody>
                  <tr *ngFor="let e of importEntries" [class.invalid]="!e.isValid">
                    <td><span class="status-dot" [class.valid]="e.isValid"></span></td>
                    <td>{{ e.vehiclePlate }}</td>
                    <td>{{ e.volume | number:'1.2-2' }} L</td>
                    <td>{{ e.pricePerLiter | number:'1.3-3' }}</td>
                    <td>{{ e.totalAmount | number:'1.2-2' }}</td>
                    <td>{{ e.invoiceDate }}</td>
                    <td><select [(ngModel)]="e.fuelTypeId" *ngIf="e.isValid"><option *ngFor="let ft of fuelTypes" [ngValue]="ft.id">{{ ft.name }}</option></select></td>
                    <td><span *ngFor="let err of e.errors" class="error-tag">{{ err }}</span></td>
                  </tr>
                </tbody>
              </table>
              <div class="preview-actions">
                <button class="btn-reset" (click)="resetImport()">Annuler</button>
                <button class="btn-add" (click)="saveData()" [disabled]="getValidEntries().length === 0 || isSaving">
                  {{ isSaving ? 'Enregistrement...' : 'Enregistrer ' + getValidEntries().length + ' entrées' }}
                </button>
              </div>
            </div>
          </div>

          <!-- TAB: History -->
          <div class="tab-panel" *ngIf="activeTab === 'history'">
            <div class="panel-header">
              <h2>Historique Carburant</h2>
              <div class="filters">
                <select [(ngModel)]="filterFuelType" (change)="loadHistory()" class="filter-select">
                  <option [ngValue]="null">Tous les types</option>
                  <option *ngFor="let ft of fuelTypes" [ngValue]="ft.id">{{ ft.name }}</option>
                </select>
                <input type="date" [(ngModel)]="filterStartDate" (change)="loadHistory()" class="filter-input">
                <input type="date" [(ngModel)]="filterEndDate" (change)="loadHistory()" class="filter-input">
              </div>
            </div>
            <div class="table-container" *ngIf="fuelHistory.length > 0">
              <table class="data-table">
                <thead>
                  <tr>
                    <th>Date</th>
                    <th>Matricule</th>
                    <th>Type</th>
                    <th>Volume</th>
                    <th>Prix/L</th>
                    <th>Total</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  <tr *ngFor="let entry of fuelHistory">
                    <td>{{ entry.invoiceDate | date:'dd/MM/yyyy' }}</td>
                    <td><span class="plate-badge">{{ entry.vehiclePlate || '-' }}</span></td>
                    <td><span class="fuel-badge">{{ entry.fuelTypeName }}</span></td>
                    <td>{{ entry.volume | number:'1.2-2' }} L</td>
                    <td>{{ entry.pricePerLiter | number:'1.3-3' }}</td>
                    <td class="total-cell">{{ entry.totalAmount | number:'1.2-2' }}</td>
                    <td><button class="btn-icon-delete" (click)="deleteEntry(entry)" title="Supprimer">🗑️</button></td>
                  </tr>
                </tbody>
              </table>
            </div>
            <div class="empty-state" *ngIf="fuelHistory.length === 0">
              <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                <path d="M3 22V8l9-6 9 6v14"/>
              </svg>
              <p>Aucune donnée carburant</p>
              <button class="btn-add" (click)="activeTab = 'manual'">Ajouter une entrée</button>
            </div>
          </div>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    .carburant-page { flex: 1; background: #f1f5f9; display: flex; flex-direction: column; min-height: calc(100vh - 42px); }
    .filter-bar { display: flex; align-items: center; gap: 16px; padding: 10px 14px; background: white; border-bottom: 1px solid #e2e8f0; }
    .page-title { display: flex; align-items: center; gap: 8px; font-size: 14px; font-weight: 600; color: #1e293b; }
    .tabs-inline { display: flex; gap: 4px; margin-left: 24px; }
    .tab-btn { display: flex; align-items: center; gap: 6px; padding: 6px 12px; background: transparent; border: 1px solid transparent; border-radius: 4px; color: #64748b; font-size: 12px; font-weight: 500; cursor: pointer; transition: all 0.2s; }
    .tab-btn:hover { background: #f1f5f9; color: #1e293b; }
    .tab-btn.active { background: #eff6ff; border-color: #bfdbfe; color: #2563eb; }
    .stats-bar { display: flex; gap: 16px; padding: 12px 14px; background: white; border-bottom: 1px solid #e2e8f0; }
    .stat-item { display: flex; align-items: center; gap: 10px; padding: 8px 14px; background: #f8fafc; border-radius: 6px; }
    .stat-icon { width: 32px; height: 32px; border-radius: 6px; display: flex; align-items: center; justify-content: center; }
    .stat-icon.active { background: #dcfce7; color: #16a34a; }
    .stat-icon.available { background: #fef3c7; color: #d97706; }
    .stat-icon.warning { background: #fee2e2; color: #dc2626; }
    .stat-icon.info { background: #dbeafe; color: #2563eb; }
    .stat-content { display: flex; flex-direction: column; }
    .stat-value { font-size: 16px; font-weight: 600; color: #1e293b; }
    .stat-label { font-size: 11px; color: #64748b; }
    .content-area { flex: 1; padding: 14px; overflow-y: auto; }
    .tab-panel { background: white; border-radius: 8px; border: 1px solid #e2e8f0; }
    .panel-header { display: flex; justify-content: space-between; align-items: center; padding: 14px 16px; border-bottom: 1px solid #e2e8f0; }
    .panel-header h2 { margin: 0; font-size: 14px; font-weight: 600; color: #1e293b; }
    .panel-header p { margin: 4px 0 0; font-size: 12px; color: #64748b; }
    .btn-link { background: none; border: none; color: #64748b; font-size: 12px; cursor: pointer; }
    .btn-link:hover { color: #ef4444; }
    .form-card { padding: 16px; }
    .form-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px; }
    .form-group { display: flex; flex-direction: column; gap: 6px; }
    .form-group label { font-size: 12px; font-weight: 500; color: #374151; }
    .required { color: #ef4444; }
    .form-control { padding: 8px 12px; border: 1px solid #d1d5db; border-radius: 4px; font-size: 13px; }
    .form-control:focus { outline: none; border-color: #3b82f6; box-shadow: 0 0 0 2px rgba(59,130,246,0.1); }
    .computed-value { padding: 8px 12px; background: #f1f5f9; border-radius: 4px; font-size: 14px; font-weight: 600; color: #16a34a; }
    .form-actions { display: flex; justify-content: flex-end; gap: 10px; margin-top: 16px; padding-top: 16px; border-top: 1px solid #e2e8f0; }
    .btn-reset { display: flex; align-items: center; gap: 6px; padding: 8px 14px; background: white; border: 1px solid #e2e8f0; border-radius: 4px; color: #64748b; font-size: 12px; cursor: pointer; }
    .btn-reset:hover { background: #f1f5f9; }
    .btn-add { display: flex; align-items: center; gap: 6px; padding: 8px 14px; background: #3b82f6; border: none; border-radius: 4px; color: white; font-size: 12px; font-weight: 500; cursor: pointer; }
    .btn-add:hover { background: #2563eb; }
    .btn-add:disabled { opacity: 0.6; cursor: not-allowed; }
    .recent-entries { padding: 16px; border-top: 1px solid #e2e8f0; }
    .recent-entries h3 { margin: 0 0 12px; font-size: 13px; font-weight: 600; color: #374151; }
    .data-table { width: 100%; border-collapse: collapse; font-size: 12px; }
    .data-table th, .data-table td { padding: 10px 12px; text-align: left; border-bottom: 1px solid #e2e8f0; }
    .data-table th { background: #f8fafc; font-weight: 600; color: #475569; }
    .data-table tr:hover { background: #f8fafc; }
    .data-table tr.invalid { background: #fef2f2; }
    .plate-badge { display: inline-block; padding: 2px 8px; background: #e0e7ff; color: #3730a3; border-radius: 4px; font-weight: 500; font-size: 11px; }
    .fuel-badge { display: inline-block; padding: 2px 8px; background: #fef3c7; color: #92400e; border-radius: 4px; font-weight: 500; font-size: 11px; }
    .total-cell { font-weight: 600; color: #16a34a; }
    .btn-icon-delete { background: none; border: none; cursor: pointer; font-size: 14px; padding: 4px; }
    .upload-section { padding: 16px; }
    .upload-zone { border: 2px dashed #d1d5db; border-radius: 8px; padding: 40px; text-align: center; cursor: pointer; transition: all 0.2s; }
    .upload-zone:hover, .upload-zone.dragover { border-color: #3b82f6; background: #eff6ff; }
    .upload-content svg { color: #94a3b8; margin-bottom: 12px; }
    .upload-content h3 { margin: 0; font-size: 14px; color: #1e293b; }
    .upload-content p { margin: 4px 0 0; font-size: 12px; color: #64748b; }
    .file-hint { display: block; margin-top: 8px; font-size: 11px; color: #94a3b8; }
    .mapping-section { padding: 16px; }
    .mapping-grid { display: grid; grid-template-columns: repeat(5, 1fr); gap: 10px; margin-bottom: 16px; }
    .mapping-card { background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 6px; padding: 10px; cursor: pointer; position: relative; transition: all 0.2s; }
    .mapping-card:hover { border-color: #3b82f6; }
    .mapping-card.active { border-color: #3b82f6; background: #eff6ff; }
    .mapping-card.mapped { background: #f0fdf4; border-color: #86efac; }
    .field-label { display: block; font-size: 10px; font-weight: 500; color: #64748b; margin-bottom: 4px; }
    .field-value { font-size: 11px; color: #1e293b; }
    .mapping-card.mapped .field-value { color: #166534; }
    .btn-clear { position: absolute; top: 4px; right: 4px; background: none; border: none; color: #ef4444; cursor: pointer; font-size: 11px; }
    .mapping-hint { background: #fef3c7; border: 1px solid #fcd34d; border-radius: 6px; padding: 10px 14px; margin-bottom: 16px; color: #92400e; font-size: 12px; }
    .excel-preview { border: 1px solid #e2e8f0; border-radius: 6px; overflow: hidden; margin-bottom: 16px; }
    .preview-header { display: flex; justify-content: space-between; align-items: center; padding: 10px 14px; background: #f8fafc; border-bottom: 1px solid #e2e8f0; font-size: 12px; font-weight: 500; }
    .preview-header select { padding: 4px 8px; border: 1px solid #d1d5db; border-radius: 4px; font-size: 11px; }
    .table-scroll { max-height: 250px; overflow: auto; }
    .excel-table { width: 100%; border-collapse: collapse; font-size: 11px; }
    .excel-table th, .excel-table td { padding: 6px 10px; border: 1px solid #e2e8f0; text-align: left; white-space: nowrap; }
    .excel-table th { background: #f8fafc; font-weight: 500; position: sticky; top: 0; }
    .excel-table th.selectable { cursor: pointer; }
    .excel-table th.selectable:hover { background: #dbeafe; }
    .excel-table th.mapped, .excel-table td.mapped { background: #dcfce7 !important; }
    .row-num { background: #f1f5f9 !important; color: #64748b; text-align: center; width: 30px; }
    .col-letter { display: block; font-size: 9px; color: #94a3b8; }
    .mapped-tag { display: inline-block; margin-left: 6px; padding: 1px 4px; background: #10b981; color: white; border-radius: 3px; font-size: 9px; }
    .mapping-actions { display: flex; justify-content: space-between; align-items: center; }
    .start-row { display: flex; align-items: center; gap: 8px; font-size: 12px; }
    .start-row input { width: 60px; padding: 6px; border: 1px solid #d1d5db; border-radius: 4px; font-size: 12px; }
    .preview-section { padding: 16px; }
    .validation-stats { display: flex; gap: 12px; margin-bottom: 16px; }
    .vstat { padding: 8px 16px; border-radius: 6px; font-size: 12px; font-weight: 500; }
    .vstat.success { background: #dcfce7; color: #166534; }
    .vstat.error { background: #fee2e2; color: #991b1b; }
    .default-fuel { display: flex; align-items: center; gap: 10px; padding: 10px 14px; background: #fef3c7; border-radius: 6px; margin-bottom: 16px; font-size: 12px; }
    .default-fuel select { padding: 6px 10px; border: 1px solid #d1d5db; border-radius: 4px; }
    .status-dot { display: inline-block; width: 10px; height: 10px; border-radius: 50%; background: #ef4444; }
    .status-dot.valid { background: #22c55e; }
    .error-tag { display: inline-block; padding: 2px 6px; background: #fee2e2; color: #991b1b; border-radius: 3px; font-size: 10px; margin: 1px; }
    .preview-actions { display: flex; justify-content: flex-end; gap: 10px; margin-top: 16px; }
    .filters { display: flex; gap: 10px; }
    .filter-select, .filter-input { padding: 6px 10px; border: 1px solid #e2e8f0; border-radius: 4px; font-size: 12px; }
    .table-container { padding: 0 16px 16px; }
    .empty-state { text-align: center; padding: 48px; color: #64748b; }
    .empty-state svg { color: #cbd5e1; margin-bottom: 12px; }
    .empty-state p { margin: 0 0 16px; font-size: 13px; }
    @media (max-width: 1024px) { .form-grid { grid-template-columns: repeat(2, 1fr); } .mapping-grid { grid-template-columns: repeat(3, 1fr); } }
    @media (max-width: 768px) { .form-grid { grid-template-columns: 1fr; } .mapping-grid { grid-template-columns: 1fr 1fr; } .stats-bar { flex-wrap: wrap; } }
  `]
})
export class CarburantComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  activeTab = 'manual';
  
  // Manual entry
  manualEntry = { vehiclePlate: '', fuelTypeId: null as number | null, volume: 0, pricePerLiter: 0, invoiceDate: new Date().toISOString().split('T')[0] };
  
  // Upload
  isDragOver = false;
  workbook: any = null;
  sheetNames: string[] = [];
  selectedSheetIndex = 0;
  excelHeaders: string[] = [];
  excelData: any[][] = [];
  dataStartRow = 2;

  // Mapping
  columnMapping: ColumnMapping = { vehiclePlate: null, volume: null, pricePerLiter: null, invoiceDate: null, fuelType: null };
  activeMappingField: keyof ColumnMapping | null = null;
  mappingFields = [
    { key: 'vehiclePlate' as keyof ColumnMapping, label: 'Matricule', required: true },
    { key: 'volume' as keyof ColumnMapping, label: 'Volume', required: true },
    { key: 'pricePerLiter' as keyof ColumnMapping, label: 'Prix/L', required: true },
    { key: 'invoiceDate' as keyof ColumnMapping, label: 'Date', required: true },
    { key: 'fuelType' as keyof ColumnMapping, label: 'Type', required: false }
  ];

  showPreview = false;
  importEntries: FuelEntry[] = [];
  isSaving = false;

  fuelTypes: FuelTypeDto[] = [];
  vehicles: VehicleWithPositionDto[] = [];
  fuelHistory: FuelEntryDto[] = [];
  fuelPrices: FuelPriceFullDto[] = [];
  defaultFuelTypeId: number | null = null;

  filterFuelType: number | null = null;
  filterStartDate = '';
  filterEndDate = '';

  constructor(private apiService: ApiService, private cdr: ChangeDetectorRef) {}

  ngOnInit() {
    this.loadFuelTypes();
    this.loadVehicles();
    this.loadHistory();
    this.loadCurrentPrices();
  }

  loadFuelTypes() {
    this.apiService.getFuelTypes().pipe(takeUntil(this.destroy$)).subscribe({
      next: (types) => { this.fuelTypes = types; if (types.length > 0) this.defaultFuelTypeId = types[0].id; },
      error: (err) => console.error(err)
    });
  }

  loadCurrentPrices() {
    this.apiService.getCurrentActiveFuelPrices().pipe(takeUntil(this.destroy$)).subscribe({
      next: (prices) => { this.fuelPrices = prices; },
      error: (err) => console.error(err)
    });
  }

  onFuelTypeChange(fuelTypeId: number | null) {
    if (!fuelTypeId) {
      this.manualEntry.pricePerLiter = 0;
      return;
    }
    const price = this.fuelPrices.find(p => p.fuelTypeId === fuelTypeId);
    this.manualEntry.pricePerLiter = price ? price.pricePerLiter : 0;
  }

  loadVehicles() {
    this.apiService.getVehicles().pipe(takeUntil(this.destroy$)).subscribe({
      next: (r: any) => this.vehicles = r.items || r,
      error: (err) => console.error(err)
    });
  }

  loadHistory() {
    const options: any = { pageSize: 100 };
    if (this.filterFuelType) options.fuelTypeId = this.filterFuelType;
    if (this.filterStartDate) options.startDate = this.filterStartDate;
    if (this.filterEndDate) options.endDate = this.filterEndDate;
    this.apiService.getFuelEntries(options).pipe(takeUntil(this.destroy$)).subscribe({
      next: (r) => { this.fuelHistory = r.items; this.cdr.detectChanges(); },
      error: (err) => { console.error(err); this.cdr.detectChanges(); }
    });
  }

  getAveragePrice(): number {
    if (this.fuelHistory.length === 0) return 0;
    const total = this.fuelHistory.reduce((s, e) => s + (e.pricePerLiter || 0), 0);
    return total / this.fuelHistory.length;
  }

  getTotalVolume(): number { return this.fuelHistory.reduce((s, e) => s + (e.volume || 0), 0); }
  getTotalAmount(): number { return this.fuelHistory.reduce((s, e) => s + (e.totalAmount || 0), 0); }

  // Manual entry
  isManualEntryValid(): boolean {
    return !!this.manualEntry.vehiclePlate && this.manualEntry.fuelTypeId !== null && 
           this.manualEntry.volume > 0 && this.manualEntry.pricePerLiter > 0 && !!this.manualEntry.invoiceDate;
  }

  resetManualEntry() {
    this.manualEntry = { vehiclePlate: '', fuelTypeId: null, volume: 0, pricePerLiter: 0, invoiceDate: new Date().toISOString().split('T')[0] };
  }

  saveManualEntry() {
    if (!this.isManualEntryValid()) return;
    this.isSaving = true;
    this.apiService.createFuelEntry({
      vehiclePlate: this.manualEntry.vehiclePlate,
      fuelTypeId: this.manualEntry.fuelTypeId!,
      volume: this.manualEntry.volume,
      pricePerLiter: this.manualEntry.pricePerLiter,
      invoiceDate: this.manualEntry.invoiceDate
    }).pipe(takeUntil(this.destroy$)).subscribe({
      next: () => { this.isSaving = false; this.resetManualEntry(); this.loadHistory(); },
      error: (err) => { console.error(err); this.isSaving = false; }
    });
  }

  // File handling
  onDragOver(e: DragEvent) { e.preventDefault(); this.isDragOver = true; }
  onDragLeave(e: DragEvent) { e.preventDefault(); this.isDragOver = false; }
  onDrop(e: DragEvent) {
    e.preventDefault(); this.isDragOver = false;
    const f = e.dataTransfer?.files[0];
    if (f && (f.name.endsWith('.xlsx') || f.name.endsWith('.xls'))) this.processFile(f);
  }
  onFileSelected(e: Event) { const f = (e.target as HTMLInputElement).files?.[0]; if (f) this.processFile(f); }
  async processFile(file: File) {
    const d = await file.arrayBuffer();
    this.workbook = XLSX.read(d, { type: 'array' });
    this.sheetNames = this.workbook.SheetNames;
    this.loadSheet();
  }
  loadSheet() {
    const s = this.workbook.Sheets[this.sheetNames[this.selectedSheetIndex]];
    const j = XLSX.utils.sheet_to_json(s, { header: 1, defval: '' }) as any[][];
    this.excelHeaders = j[0]?.map((h: any) => String(h || '')) || [];
    this.excelData = j.slice(1);
  }
  resetImport() {
    this.workbook = null; this.excelHeaders = []; this.excelData = []; this.showPreview = false; this.importEntries = [];
    this.columnMapping = { vehiclePlate: null, volume: null, pricePerLiter: null, invoiceDate: null, fuelType: null };
  }

  // Mapping
  getExcelColumnLetter(i: number): string { let r = '', n = i; while (n >= 0) { r = String.fromCharCode((n % 26) + 65) + r; n = Math.floor(n / 26) - 1; } return r; }
  getColumnName(i: number): string { return `${this.getExcelColumnLetter(i)}: ${this.excelHeaders[i] || '(vide)'}`; }
  getMappingLabel(k: keyof ColumnMapping): string { return this.mappingFields.find(f => f.key === k)?.label || k; }
  startMapping(k: keyof ColumnMapping) { this.activeMappingField = k; }
  selectColumn(i: number) {
    if (this.activeMappingField) {
      Object.keys(this.columnMapping).forEach(k => { if (this.columnMapping[k as keyof ColumnMapping] === i) this.columnMapping[k as keyof ColumnMapping] = null; });
      this.columnMapping[this.activeMappingField] = i;
      this.activeMappingField = null;
    }
  }
  clearMapping(k: keyof ColumnMapping, e: Event) { e.stopPropagation(); this.columnMapping[k] = null; }
  isColumnMapped(i: number): boolean { return Object.values(this.columnMapping).includes(i); }
  getColumnMappedField(i: number): string { for (const [k, v] of Object.entries(this.columnMapping)) { if (v === i) return this.getMappingLabel(k as keyof ColumnMapping); } return ''; }
  isMappingComplete(): boolean { return this.columnMapping.vehiclePlate !== null && this.columnMapping.volume !== null && this.columnMapping.pricePerLiter !== null && this.columnMapping.invoiceDate !== null; }

  processData() {
    const s = this.workbook.Sheets[this.sheetNames[this.selectedSheetIndex]];
    const j = XLSX.utils.sheet_to_json(s, { header: 1, defval: '' }) as any[][];
    this.importEntries = [];
    for (let i = this.dataStartRow - 1; i < j.length; i++) {
      const r = j[i]; if (!r || r.every((c: any) => !c)) continue;
      const e: FuelEntry = {
        vehiclePlate: String(r[this.columnMapping.vehiclePlate!] || '').trim(),
        volume: this.parseNumber(r[this.columnMapping.volume!]),
        pricePerLiter: this.parseNumber(r[this.columnMapping.pricePerLiter!]),
        totalAmount: 0, invoiceDate: this.parseDate(r[this.columnMapping.invoiceDate!]),
        fuelTypeId: this.defaultFuelTypeId || 0,
        fuelTypeName: this.columnMapping.fuelType !== null ? String(r[this.columnMapping.fuelType]) : '',
        isValid: true, errors: []
      };
      e.totalAmount = e.volume * e.pricePerLiter;
      if (!e.vehiclePlate) { e.errors!.push('Matricule'); e.isValid = false; }
      if (e.volume <= 0) { e.errors!.push('Volume'); e.isValid = false; }
      if (e.pricePerLiter <= 0) { e.errors!.push('Prix'); e.isValid = false; }
      if (!e.invoiceDate) { e.errors!.push('Date'); e.isValid = false; }
      if (e.fuelTypeName) { const ft = this.fuelTypes.find(t => t.code.toLowerCase() === e.fuelTypeName.toLowerCase() || t.name.toLowerCase() === e.fuelTypeName.toLowerCase()); if (ft) e.fuelTypeId = ft.id; }
      // Default to first fuel type if none mapped/matched
      if (!e.fuelTypeId && this.fuelTypes.length > 0) { e.fuelTypeId = this.fuelTypes[0].id; }
      this.importEntries.push(e);
    }
    this.showPreview = true;
  }

  parseNumber(v: any): number { if (typeof v === 'number') return v; if (!v) return 0; return parseFloat(String(v).replace(/[^\d.,\-]/g, '').replace(',', '.')) || 0; }
  parseDate(v: any): string {
    if (!v) return '';
    // XLSX may return a JS Date object directly
    if (v instanceof Date) {
      if (!isNaN(v.getTime())) return v.toISOString().split('T')[0];
      return '';
    }
    // Excel serial number
    if (typeof v === 'number') {
      const d = new Date(Date.UTC(1899, 11, 30) + v * 86400000);
      if (!isNaN(d.getTime())) return d.toISOString().split('T')[0];
      return '';
    }
    const s = String(v).trim();
    if (!s) return '';
    // Try YYYY-MM-DD or YYYY/MM/DD (ISO format) first
    const isoMatch = s.match(/^(\d{4})[\/\-](\d{1,2})[\/\-](\d{1,2})/);
    if (isoMatch) {
      const [, year, month, day] = isoMatch;
      const d = new Date(Date.UTC(+year, +month - 1, +day));
      if (!isNaN(d.getTime())) return d.toISOString().split('T')[0];
    }
    // Try DD/MM/YYYY, DD-MM-YYYY, DD/MM/YY, or MM/DD/YYYY
    const slashMatch = s.match(/^(\d{1,2})[\/\-](\d{1,2})[\/\-](\d{2,4})/);
    if (slashMatch) {
      let [, p1, p2, yearStr] = slashMatch;
      let year = +yearStr;
      if (year < 100) year += year < 50 ? 2000 : 1900;
      let day: number, month: number;
      if (+p1 > 12) {
        day = +p1; month = +p2;
      } else if (+p2 > 12) {
        month = +p1; day = +p2;
      } else {
        day = +p1; month = +p2;
      }
      if (day >= 1 && day <= 31 && month >= 1 && month <= 12) {
        const d = new Date(Date.UTC(year, month - 1, day));
        if (!isNaN(d.getTime())) return d.toISOString().split('T')[0];
      }
    }
    // Last resort: try native Date parsing
    const fallback = new Date(s);
    if (!isNaN(fallback.getTime())) return fallback.toISOString().split('T')[0];
    return '';
  }
  getValidEntries() { return this.importEntries.filter(e => e.isValid); }
  getInvalidEntries() { return this.importEntries.filter(e => !e.isValid); }

  saveData() {
    this.isSaving = true;
    const valid = this.getValidEntries();
    const requests = valid.map(e => ({
      vehiclePlate: e.vehiclePlate,
      fuelTypeId: e.fuelTypeId,
      volume: e.volume,
      pricePerLiter: e.pricePerLiter,
      invoiceDate: e.invoiceDate
    }));
    this.apiService.bulkCreateFuelEntries(requests).pipe(takeUntil(this.destroy$)).subscribe({
      next: (result) => {
        console.log(`Import carburant: ${result.success}/${result.total} réussis`);
        if (result.failed > 0) {
          alert(`Import terminé: ${result.success} réussis, ${result.failed} échoués sur ${result.total}`);
        } else {
          alert(`Import réussi: ${result.success} entrées importées`);
        }
        this.isSaving = false; this.resetImport(); this.loadHistory(); this.activeTab = 'history';
        this.cdr.detectChanges();
      },
      error: (err) => {
        console.error('Bulk import error:', err);
        alert('Erreur lors de l\'import: ' + (err.error?.message || err.message || 'Erreur inconnue'));
        this.isSaving = false; this.cdr.detectChanges();
      }
    });
  }

  deleteEntry(entry: FuelEntryDto) {
    if (entry.id && confirm('Supprimer cette entrée ?')) {
      this.apiService.deleteFuelEntry(entry.id).pipe(takeUntil(this.destroy$)).subscribe({ next: () => this.loadHistory(), error: (err) => console.error(err) });
    }
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
