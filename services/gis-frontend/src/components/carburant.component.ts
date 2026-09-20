import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';
import { AppLayoutComponent } from './shared/app-layout.component';
import {
  ScanFactureComponent, ExtractionFacture,
  ResultatScanFacture, EchecScanFacture
} from './shared/scan-facture.component';
import { ApiService, FuelTypeDto, VehicleWithPositionDto, FuelEntryDto, FuelPriceFullDto } from '../services/api.service';
import { USER_PREF_PIPES } from '../pipes/user-preference-pipes';
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
  odometerKm?: number | null;
  isValid?: boolean;
  errors?: string[];
}

interface ColumnMapping {
  vehiclePlate: number | null;
  volume: number | null;
  pricePerLiter: number | null;
  invoiceDate: number | null;
  fuelType: number | null;
  // Relevé au compteur : facultatif, mais c'est la seule source de kilométrage
  // d'un client sans boîtier. Un relevé de carte carburant en contient presque
  // toujours une colonne.
  odometerKm: number | null;
}

/**
 * Ce qu'un ticket scanné a donné — bandeau de revue au-dessus du formulaire.
 * L'utilisateur doit voir ce qui a été prérempli, avec quelle confiance, et ce
 * que l'IA n'a pas su lire ; rien n'est enregistré tant qu'il n'a pas validé.
 */
interface RevueTicketScanne {
  actif: boolean;
  /** Analyse impossible (panne IA, document illisible) : seul le document est rattaché. */
  echec: boolean;
  /** high | medium | low, tel que rendu par l'extraction. */
  confiance: string;
  /** Champs réellement préremplis, listés à l'utilisateur. */
  champs: string[];
  /** Matricule lu sans rapprochement sûr (absent, ambigu, trop court) : le champ reste vide et on l'affiche. */
  plaqueNonReconnue: string;
  /** Matricule lu quand le rapprochement n'est PAS une égalité : montré tel quel… */
  plaqueRapprochee: string;
  /** …à côté du matricule du parc retenu, pour que l'utilisateur tranche. */
  plaqueRetenue: string;
  /** Date absente du ticket : la date du jour reste en place, il faut le dire. */
  dateNonLue: boolean;
  typeNonLu: boolean;
  /** Catégorie rendue autre que « carburant » : on avertit sans bloquer. */
  categorieInattendue: string;
  avoir: boolean;
  receiptUrl: string;
  stationName: string;
  invoiceNumber: string;
}

/** Ce qu'a donné le rapprochement d'un matricule lu avec le parc. */
interface RapprochementMatricule {
  vehicule: VehicleWithPositionDto | null;
  /** true = égalité sur les lettres et les chiffres ; false = rapprochement approché, à annoncer. */
  exact: boolean;
}

/**
 * Longueur minimale (lettres et chiffres seuls) pour qu'un rapprochement APPROCHÉ
 * de matricule ait un sens. Vérifié sur les 12 matricules réels de la société 7 :
 * en dessous, « 1 » désigne GA-214-RK, « 12 » GK-128-ZF et « 694 » GL-694-PN —
 * c'est-à-dire n'importe quoi.
 */
const LONGUEUR_MIN_RAPPROCHEMENT = 5;

/**
 * Écart de longueur toléré entre le matricule lu et celui du parc. Au-delà de
 * deux caractères, l'inclusion ne prouve plus rien : un fragment est trop maigre,
 * un pavé de texte contient tout et son contraire.
 */
const ECART_MAX_RAPPROCHEMENT = 2;

@Component({
  selector: 'app-carburant',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent, ScanFactureComponent, ...USER_PREF_PIPES],
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
            <button class="tab-btn" [class.active]="activeTab === 'consommation'" (click)="activeTab='consommation'; loadConsumption()">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M3 3v18h18"/><path d="M7 13l3-3 4 4 5-6"/>
              </svg>
              Consommation
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
              <div class="panel-title">
                <h2>Nouvelle Entrée Carburant</h2>
                <p>Saisissez les informations de la facture carburant</p>
              </div>
              <!-- Bouton + compteur de quota + envoi : brique partagée avec Dépenses,
                   Entretien, Réparations et Échéances (shared/scan-facture.component.ts).
                   « Ticket » ici : c'est le reçu de la station, pas une facture. -->
              <app-scan-facture libelle="Scanner un ticket"
                                (scanne)="onTicketScanne($event)"
                                (echec)="onEchecScan($event)"></app-scan-facture>
            </div>

            <!-- Ce que le ticket a donné : sans ce rappel, l'utilisateur ne sait pas
                 quels champs viennent de l'IA ni ce qu'elle n'a pas su lire. -->
            <div class="scan-banner" *ngIf="scanTicket.actif">
              <div class="scan-banner-head">
                <span class="scan-banner-title">{{ scanTicket.echec ? 'Ticket non analysé' : 'Ticket scanné' }}</span>
                <span class="scan-conf scan-conf-{{ scanTicket.confiance }}" *ngIf="scanTicket.confiance">
                  Confiance {{ libelleConfiance() }}
                </span>
                <a class="scan-doc" *ngIf="scanTicket.receiptUrl" [href]="scanTicket.receiptUrl" target="_blank">Voir le document</a>
                <button class="scan-banner-close" (click)="detacherTicket()" title="Détacher le ticket de cette saisie">✕</button>
              </div>
              <p class="scan-banner-line" *ngIf="scanTicket.champs.length > 0">
                Préremplis : {{ scanTicket.champs.join(', ') }} — relisez avant d'enregistrer.
              </p>
              <p class="scan-banner-line" *ngIf="scanTicket.echec">
                Saisissez les informations à la main : le document reste rattaché à l'entrée.
              </p>
              <p class="scan-alerte" *ngIf="scanTicket.plaqueNonReconnue">
                ⚠ Matricule lu « {{ scanTicket.plaqueNonReconnue }} » — introuvable dans le parc ou trop incertain, choisissez le véhicule.
              </p>
              <!-- Rapprochement approché : le champ est rempli, mais jamais en silence. -->
              <p class="scan-alerte" *ngIf="scanTicket.plaqueRapprochee">
                ⚠ Matricule lu « {{ scanTicket.plaqueRapprochee }} » — rapproché de « {{ scanTicket.plaqueRetenue }} », vérifiez que c'est le bon véhicule.
              </p>
              <p class="scan-alerte" *ngIf="scanTicket.typeNonLu && !scanTicket.echec">
                ⚠ Type de carburant absent du ticket — choisissez-le.
              </p>
              <!-- Sans cette ligne, la date du jour passerait pour la date du plein. -->
              <p class="scan-alerte" *ngIf="scanTicket.dateNonLue && !scanTicket.echec">
                ⚠ Date absente du ticket — la date en place n'a pas été lue sur le document, vérifiez-la.
              </p>
              <p class="scan-alerte" *ngIf="scanTicket.categorieInattendue">
                ⚠ Document analysé comme « {{ scanTicket.categorieInattendue }} » — vérifiez qu'il s'agit bien d'un plein.
              </p>
              <p class="scan-alerte" *ngIf="scanTicket.avoir">
                ⚠ Avoir détecté : cet écran enregistre des pleins, pas des remboursements.
              </p>
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
                  <label>Volume (Litres)</label>
                  <input type="number" [(ngModel)]="manualEntry.volume" (ngModelChange)="onVolumeOrPriceChange()" class="form-control" placeholder="Ex: 45.5" step="0.01" min="0">
                </div>
                <div class="form-group">
                  <label>Prix par Litre</label>
                  <input type="number" [(ngModel)]="manualEntry.pricePerLiter" (ngModelChange)="onVolumeOrPriceChange()" class="form-control" placeholder="Ex: 1.750" step="0.001" min="0">
                </div>
                <div class="form-group">
                  <label>Date Facture <span class="required">*</span></label>
                  <input type="date" [(ngModel)]="manualEntry.invoiceDate" class="form-control">
                </div>
                <div class="form-group">
                  <label>Montant Total <span class="required">*</span></label>
                  <input type="number" [(ngModel)]="manualEntry.totalAmount" (ngModelChange)="onTotalAmountEdit()" class="form-control" placeholder="Ex: 79.63" step="0.01" min="0">
                  <small class="form-hint">Renseignez volume + prix (calcul auto) OU saisissez directement le montant total.</small>
                </div>
                <!-- Le plein est le moment où le conducteur lit le compteur. Sans
                     boîtier, ce relevé est la SEULE source du kilométrage : il
                     alimente les échéances d'entretien au km et le coût au km. -->
                <div class="form-group">
                  <label>Kilométrage au compteur</label>
                  <input type="number" [(ngModel)]="manualEntry.odometerKm" class="form-control" placeholder="Ex: 145820" step="1" min="0">
                  <small class="form-hint">Met à jour le kilométrage du véhicule — indispensable sans boîtier GPS.</small>
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
                  <tr><th>Statut</th><th>Matricule</th><th>Volume</th><th>Prix/L</th><th>Total</th><th>Date</th><th>Km</th><th>Type</th><th>Erreurs</th></tr>
                </thead>
                <tbody>
                  <tr *ngFor="let e of importEntries" [class.invalid]="!e.isValid">
                    <td><span class="status-dot" [class.valid]="e.isValid"></span></td>
                    <td>{{ e.vehiclePlate }}</td>
                    <td>{{ e.volume | number:'1.2-2' }} L</td>
                    <td>{{ e.pricePerLiter | number:'1.3-3' }}</td>
                    <td>{{ e.totalAmount | number:'1.2-2' }}</td>
                    <td>{{ e.invoiceDate }}</td>
                    <td>{{ e.odometerKm ? (e.odometerKm | number:'1.0-0') : '—' }}</td>
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
                <!-- Filtre par véhicule (recette client 04/09/2026) : à 93 pleins
                     sur quatre véhicules, la liste n'était plus lisible. -->
                <select [(ngModel)]="filterVehicleId" (change)="loadHistory()" class="filter-select">
                  <option [ngValue]="null">Tous les véhicules</option>
                  <option *ngFor="let v of vehicles" [ngValue]="v.id">{{ v.plate || v.name }}</option>
                </select>
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
                    <!-- Sans cette colonne, un relevé au compteur saisi devenait
                         invisible : impossible de vérifier ou de repérer une faute
                         de frappe qui aurait fait bondir le kilométrage. -->
                    <th>Km</th>
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
                    <td>{{ entry.odometerKm ? (entry.odometerKm | number:'1.0-0') : '—' }}</td>
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

          <!-- Consommation réelle (sans GPS) — calcul plein-à-plein depuis les pleins saisis -->
          <div class="tab-panel" *ngIf="activeTab === 'consommation'">
            <div class="panel-header">
              <h2>Consommation réelle (sans GPS)</h2>
              <div class="filters">
                <input type="date" [(ngModel)]="consoStartDate" (change)="loadConsumption()" class="filter-input">
                <input type="date" [(ngModel)]="consoEndDate" (change)="loadConsumption()" class="filter-input">
                <button class="btn-add" (click)="loadConsumption()">Actualiser</button>
              </div>
            </div>

            <div *ngIf="consumption">
              <div style="display:flex;flex-wrap:wrap;gap:12px;padding:12px 16px;">
                <div style="flex:1;min-width:130px;background:#fff;border:1px solid #e2e8f0;border-radius:8px;padding:10px 14px;">
                  <div style="font-size:11px;color:#64748b;">Coût total</div>
                  <div style="font-size:18px;font-weight:700;color:#1e293b;">{{ consumption.totalFuelCost | appCurrency:0 }}</div>
                </div>
                <div style="flex:1;min-width:130px;background:#fff;border:1px solid #e2e8f0;border-radius:8px;padding:10px 14px;">
                  <div style="font-size:11px;color:#64748b;">Litres</div>
                  <div style="font-size:18px;font-weight:700;color:#1e293b;">{{ consumption.totalLiters | number:'1.0-0' }} L</div>
                </div>
                <div style="flex:1;min-width:130px;background:#fff;border:1px solid #e2e8f0;border-radius:8px;padding:10px 14px;">
                  <div style="font-size:11px;color:#64748b;">Distance</div>
                  <div style="font-size:18px;font-weight:700;color:#1e293b;">{{ consumption.totalDistanceKm | number:'1.0-0' }} km</div>
                </div>
                <div style="flex:1;min-width:130px;background:#fff;border:1px solid #e2e8f0;border-radius:8px;padding:10px 14px;">
                  <div style="font-size:11px;color:#64748b;">Conso. moyenne</div>
                  <div style="font-size:18px;font-weight:700;color:#1e293b;">{{ consumption.fleetConsumptionPer100Km != null ? (consumption.fleetConsumptionPer100Km | number:'1.1-1') + ' L/100km' : '—' }}</div>
                </div>
                <div style="flex:1;min-width:130px;background:#fff;border:1px solid #e2e8f0;border-radius:8px;padding:10px 14px;">
                  <div style="font-size:11px;color:#64748b;">Coût / km</div>
                  <div style="font-size:18px;font-weight:700;color:#1e293b;">{{ consumption.fleetCostPerKm != null ? (consumption.fleetCostPerKm | appCurrency:3) : '—' }}</div>
                </div>
              </div>

              <!-- Définition annoncée : le client recoupe litres ÷ km avec le L/100
                   affiché (recette du 04/09/2026), ce qui n'était pas possible avant. -->
              <div style="margin:0 16px 8px;font-size:12px;color:#64748b;">
                Consommation = litres achetés ÷ kilomètres relevés entre le premier et le dernier plein de la période
                (le premier plein est compté : lecture légèrement majorée quand il y a peu de pleins).
              </div>
              <div *ngIf="consumption.ignoredOdometerReadings > 0" style="margin:0 16px 8px;padding:8px 12px;background:#fef2f2;border:1px solid #fecaca;border-radius:6px;color:#991b1b;font-size:12px;">
                ⚠ {{ consumption.ignoredOdometerReadings }} relevé(s) compteur incohérent(s) ignoré(s) — faute de frappe probable. Corrige-les dans l'onglet Historique pour un kilométrage exact.
              </div>
              <div *ngIf="consumption.entriesWithoutOdometer > 0" style="margin:0 16px 8px;padding:8px 12px;background:#fff7ed;border:1px solid #fed7aa;border-radius:6px;color:#9a3412;font-size:12px;">
                ⚠ {{ consumption.entriesWithoutOdometer }} plein(s) sans relevé compteur — saisis le kilométrage au plein pour calculer la consommation.
              </div>

              <div class="table-container" *ngIf="consumption.vehicles?.length">
                <table class="data-table">
                  <thead>
                    <tr>
                      <th>Véhicule</th><th>Type</th><th>Pleins</th><th>Distance</th><th>Litres</th><th>L/100km</th><th>Coût/km</th><th>Coût total</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr *ngFor="let v of consumption.vehicles">
                      <td><span class="plate-badge">{{ v.plate || v.vehicleName }}</span></td>
                      <td><span class="fuel-badge">{{ v.fuelType || '-' }}</span></td>
                      <td>{{ v.entryCount }}</td>
                      <td>{{ v.distanceKm != null ? (v.distanceKm | number:'1.0-0') + ' km' : '—' }}</td>
                      <td>{{ v.totalLiters | number:'1.0-0' }} L</td>
                      <td>
                        {{ v.consumptionPer100Km != null ? (v.consumptionPer100Km | number:'1.1-1') : '—' }}
                        <span *ngIf="v.consumptionPer100Km != null && !v.reliableOdometer" title="Relevés compteur incomplets — fiabilité réduite" style="color:#f59e0b;">⚠</span>
                      </td>
                      <td>{{ v.costPerKm != null ? (v.costPerKm | appCurrency:3) : '—' }}</td>
                      <td class="total-cell">{{ v.totalCost | appCurrency:0 }}</td>
                    </tr>
                  </tbody>
                </table>
              </div>
              <div class="empty-state" *ngIf="!consumption.vehicles?.length">
                <p>Aucune donnée carburant sur la période.</p>
              </div>
            </div>
            <div class="empty-state" *ngIf="loadingConso && !consumption"><p>Chargement…</p></div>
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
    /* Bandeau de revue du ticket scanné — violet de la brique de scan. */
    .scan-banner { margin: 14px 16px 0; padding: 10px 12px; background: #f5f3ff; border: 1px solid #ddd6fe; border-radius: 6px; }
    .scan-banner-head { display: flex; align-items: center; gap: 8px; }
    .scan-banner-title { font-size: 12px; font-weight: 600; color: #5b21b6; }
    .scan-conf { padding: 1px 7px; border-radius: 999px; font-size: 10.5px; font-weight: 700; }
    .scan-conf-high { background: #dcfce7; color: #166534; }
    .scan-conf-medium { background: #fef3c7; color: #92400e; }
    .scan-conf-low { background: #fee2e2; color: #991b1b; }
    .scan-doc { margin-left: auto; font-size: 11px; color: #6d28d9; }
    .scan-banner-close { background: none; border: none; color: #94a3b8; font-size: 12px; cursor: pointer; padding: 0 2px; }
    .scan-banner-line { margin: 6px 0 0; font-size: 11.5px; color: #475569; }
    .scan-alerte { margin: 4px 0 0; font-size: 11.5px; color: #92400e; }
    .form-card { padding: 16px; }
    .form-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px; }
    .form-group { display: flex; flex-direction: column; gap: 6px; }
    .form-group label { font-size: 12px; font-weight: 500; color: #374151; }
    .required { color: #ef4444; }
    .form-control { padding: 8px 12px; border: 1px solid #d1d5db; border-radius: 4px; font-size: 13px; }
    .form-control:focus { outline: none; border-color: #3b82f6; box-shadow: 0 0 0 2px rgba(59,130,246,0.1); }
    .form-hint { display: block; font-size: 11px; color: #64748b; margin-top: 4px; line-height: 1.3; }
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
  
  // Manual entry — totalAmount is editable so operators with only the
  // gross amount on a ticket can save without inventing a volume/prix.
  // totalAmountTouched stays false until the operator types in the total
  // field directly; before then any volume/price change keeps the total
  // auto-synced as volume × pricePerLiter.
  // volume / prix / total acceptent null depuis le scan : un ticket qui n'imprime
  // pas le volume doit laisser le champ VIDE (un « 0 » se lirait comme une valeur
  // lue sur le document). La saisie à la main part toujours de 0, comme avant.
  // Date posée d'office à l'ouverture et après chaque remise à zéro. Tant que le
  // champ vaut encore cette valeur, personne ne l'a choisie : c'est ce qui permet
  // à saisieCommencee() de distinguer une date saisie d'une date par défaut.
  private dateParDefaut = new Date().toISOString().split('T')[0];
  manualEntry = { vehiclePlate: '', fuelTypeId: null as number | null, volume: 0 as number | null, pricePerLiter: 0 as number | null, totalAmount: 0 as number | null, invoiceDate: this.dateParDefaut, odometerKm: null as number | null };
  totalAmountTouched = false;

  /** Ticket scanné en cours de revue (voir onTicketScanne). */
  scanTicket: RevueTicketScanne = this.revueVide();
  
  // Upload
  isDragOver = false;
  workbook: any = null;
  sheetNames: string[] = [];
  selectedSheetIndex = 0;
  excelHeaders: string[] = [];
  excelData: any[][] = [];
  dataStartRow = 2;

  // Mapping
  columnMapping: ColumnMapping = { vehiclePlate: null, volume: null, pricePerLiter: null, invoiceDate: null, fuelType: null, odometerKm: null };
  activeMappingField: keyof ColumnMapping | null = null;
  mappingFields = [
    { key: 'vehiclePlate' as keyof ColumnMapping, label: 'Matricule', required: true },
    { key: 'volume' as keyof ColumnMapping, label: 'Volume', required: true },
    { key: 'pricePerLiter' as keyof ColumnMapping, label: 'Prix/L', required: true },
    { key: 'invoiceDate' as keyof ColumnMapping, label: 'Date', required: true },
    { key: 'fuelType' as keyof ColumnMapping, label: 'Type', required: false },
    { key: 'odometerKm' as keyof ColumnMapping, label: 'Kilométrage', required: false }
  ];

  showPreview = false;
  importEntries: FuelEntry[] = [];
  isSaving = false;

  fuelTypes: FuelTypeDto[] = [];
  vehicles: VehicleWithPositionDto[] = [];
  fuelHistory: FuelEntryDto[] = [];
  fuelPrices: FuelPriceFullDto[] = [];
  defaultFuelTypeId: number | null = null;

  filterVehicleId: number | null = null;

  filterFuelType: number | null = null;
  filterStartDate = '';
  filterEndDate = '';

  // Consommation réelle (sans GPS) — onglet "consommation"
  consoStartDate = new Date(new Date().getFullYear(), 0, 1).toISOString().split('T')[0];
  consoEndDate = new Date().toISOString().split('T')[0];
  consumption: any = null;
  loadingConso = false;

  constructor(private apiService: ApiService, private cdr: ChangeDetectorRef) {}

  ngOnInit() {
    this.loadFuelTypes();
    this.loadVehicles();
    this.loadHistory();
    this.loadCurrentPrices();
  }

  /** GPS-independent fuel consumption (full-to-full from manual fill-ups). */
  loadConsumption() {
    this.loadingConso = true;
    this.apiService.getRealFuelConsumption(this.consoStartDate, this.consoEndDate)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (r) => { this.consumption = r; this.loadingConso = false; this.cdr.detectChanges(); },
        error: (err) => { console.error('Conso error:', err); this.loadingConso = false; this.cdr.detectChanges(); }
      });
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
    } else {
      const price = this.fuelPrices.find(p => p.fuelTypeId === fuelTypeId);
      this.manualEntry.pricePerLiter = price ? price.pricePerLiter : 0;
    }
    // Pricing just changed under the hood — keep the total in sync so
    // long as the operator hasn't pinned a custom total themselves.
    this.onVolumeOrPriceChange();
  }

  loadVehicles() {
    this.apiService.getVehicles().pipe(takeUntil(this.destroy$)).subscribe({
      next: (r: any) => this.vehicles = r.items || r,
      error: (err) => console.error(err)
    });
  }

  loadHistory() {
    const options: any = { pageSize: 100 };
    if (this.filterVehicleId) options.vehicleId = this.filterVehicleId;
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

  // Re-sync the total whenever volume or price moves, UNLESS the
  // operator has already typed a custom total — they're in "free total"
  // mode and we must not overwrite what they entered.
  onVolumeOrPriceChange() {
    if (this.totalAmountTouched) return;
    const v = Number(this.manualEntry.volume) || 0;
    const p = Number(this.manualEntry.pricePerLiter) || 0;
    this.manualEntry.totalAmount = +(v * p).toFixed(2);
  }

  // Mark the total as user-edited so volume/price changes stop
  // overriding it. Resetting to 0 puts us back in "auto" mode.
  onTotalAmountEdit() {
    const t = Number(this.manualEntry.totalAmount) || 0;
    this.totalAmountTouched = t > 0;
  }

  // ── Ticket scanné (IA) ─────────────────────────────────────────────────────
  private revueVide(): RevueTicketScanne {
    return {
      actif: false, echec: false, confiance: '', champs: [], plaqueNonReconnue: '',
      plaqueRapprochee: '', plaqueRetenue: '',
      dateNonLue: false, typeNonLu: false, categorieInattendue: '', avoir: false,
      receiptUrl: '', stationName: '', invoiceNumber: ''
    };
  }

  /**
   * Ticket scanné : <app-scan-facture> a déjà tout fait (envoi, quota, messages
   * d'erreur) et rend l'extraction brute. Cet écran ne décide que du remplissage
   * de SES champs. Rien n'est enregistré : l'utilisateur relit puis valide.
   */
  onTicketScanne(res: ResultatScanFacture): void {
    // Le scan PROPOSE, l'utilisateur DISPOSE : une saisie déjà commencée n'est
    // jamais remplacée en silence.
    if (this.saisieCommencee()
        && !confirm('Une saisie est déjà commencée. La remplacer par les valeurs du ticket ?')) {
      return;
    }
    const x = res.extraction;
    const rapprochement = this.trouverVehiculeParMatricule(x.vehiclePlate);
    const vehicule = rapprochement.vehicule;
    const plaqueLue = this.tronquer(x.vehiclePlate || '', 20);
    const champs: string[] = [];

    this.manualEntry.vehiclePlate = vehicule ? (vehicule.plate || vehicule.name) : '';
    if (vehicule) champs.push('matricule');
    if (x.date) { this.manualEntry.invoiceDate = x.date; champs.push('date'); }

    // null = non imprimé sur le ticket : le champ reste VIDE et l'écran ne
    // recalcule rien (le serveur déduit déjà la valeur manquante quand les deux
    // autres sont lisibles).
    this.manualEntry.volume = x.liters;
    if (x.liters !== null) champs.push('volume');
    this.manualEntry.pricePerLiter = x.pricePerLiter;
    if (x.pricePerLiter !== null) champs.push('prix au litre');

    const typeId = this.deduireTypeCarburant(x);
    if (typeId !== null) { this.manualEntry.fuelTypeId = typeId; champs.push('type'); }

    // Le total du ticket fait foi : timbre fiscal et remise compris, il ne vaut
    // pas toujours volume × prix. On le pose comme un total saisi, ce que le
    // calcul automatique de l'écran respecte déjà (totalAmountTouched) ; sans
    // total lisible, on laisse ce même calcul faire son travail.
    if (x.total !== null && x.total > 0) {
      this.manualEntry.totalAmount = x.total;
      this.totalAmountTouched = true;
      champs.push('montant');
    } else {
      this.totalAmountTouched = false;
      this.onVolumeOrPriceChange();
    }

    const categorie = x.category || '';
    this.scanTicket = {
      actif: true,
      echec: false,
      confiance: x.confidence || '',
      champs,
      plaqueNonReconnue: vehicule ? '' : plaqueLue,
      // Rapprochement qui n'est pas une égalité : on montre TOUJOURS le lu et le retenu.
      plaqueRapprochee: vehicule && !rapprochement.exact ? plaqueLue : '',
      plaqueRetenue: vehicule && !rapprochement.exact ? (vehicule.plate || vehicule.name || '') : '',
      dateNonLue: !x.date,
      typeNonLu: typeId === null,
      // L'avoir a son propre avertissement : inutile de le répéter en catégorie.
      categorieInattendue: categorie && categorie !== 'fuel' && categorie !== 'credit_note'
        ? this.libelleCategorieScan(categorie) : '',
      avoir: x.isCreditNote,
      receiptUrl: res.receiptUrl,
      stationName: this.tronquer(x.supplierName || '', 100),
      invoiceNumber: this.tronquer(x.invoiceNumber || '', 50)
    };
    this.cdr.detectChanges();
  }

  /**
   * Scan échoué : le message a déjà été montré par la brique. Le fichier est
   * souvent stocké malgré tout (panne IA) — le formulaire reste utilisable tel
   * quel et le document reste rattaché pour une saisie à la main.
   */
  onEchecScan(e: EchecScanFacture): void {
    if (!e.receiptUrl) return;   // rien à rattacher, il n'y a rien à dire de plus
    this.scanTicket = { ...this.revueVide(), actif: true, echec: true, confiance: 'low', receiptUrl: e.receiptUrl };
    this.cdr.detectChanges();
  }

  /** Le ticket ne sera pas rattaché à l'entrée : le bandeau disparaît avec lui. */
  detacherTicket(): void {
    this.scanTicket = this.revueVide();
  }

  libelleConfiance(): string {
    const c = this.scanTicket.confiance;
    return ({ high: 'élevée', medium: 'moyenne', low: 'faible' } as Record<string, string>)[c] || c;
  }

  /**
   * Une saisie est en cours dès qu'une valeur vient de l'utilisateur — TOUS les
   * champs qu'un ticket écrase comptent, pas seulement les montants : le type de
   * carburant et la date étaient remplacés sans question alors qu'ils se
   * choisissent aussi à la main.
   *
   * Deux champs ne comptent pas, parce qu'ils ne viennent pas d'une frappe :
   *  - le prix au litre, posé automatiquement par le choix du type (prix de référence) ;
   *  - la date tant qu'elle vaut encore celle posée à l'ouverture (dateParDefaut) —
   *    sinon le bandeau de confirmation s'ouvrirait à chaque scan sur un écran neuf.
   */
  private saisieCommencee(): boolean {
    const m = this.manualEntry;
    return !!m.vehiclePlate
        || m.fuelTypeId !== null
        || (m.volume ?? 0) > 0
        || (m.totalAmount ?? 0) > 0
        || !!m.odometerKm
        || m.invoiceDate !== this.dateParDefaut;
  }

  /**
   * Véhicule reconnu à partir du matricule lu sur le ticket. Comparaison sur les
   * lettres et les chiffres seuls (« GA-214-RK » face à « ga 214 rk ») :
   *
   *  1. ÉGALITÉ — c'est sûr, le champ est rempli sans autre commentaire ;
   *  2. à défaut, RAPPROCHEMENT par inclusion, accepté seulement s'il est
   *     NON AMBIGU (un SEUL véhicule du parc y répond) et assez long pour
   *     vouloir dire quelque chose ; il est alors annoncé à l'utilisateur
   *     (plaqueRapprochee / plaqueRetenue dans le bandeau) ;
   *  3. sinon — rien. Champ laissé vide et matricule lu affiché.
   *
   * L'inclusion nue d'avant remplissait le champ en silence à partir d'un simple
   * fragment : sur les 12 matricules réels de la société 7, « G » rendait
   * GA-214-RK, « 12 » GK-128-ZF et « 694 » GL-694-PN. Un champ vide et un
   * avertissement valent mieux qu'un plein imputé au mauvais véhicule.
   */
  private trouverVehiculeParMatricule(matricule: string | null): RapprochementMatricule {
    const aucun: RapprochementMatricule = { vehicule: null, exact: false };
    if (!matricule) return aucun;
    const cle = (s?: string) => (s || '').toLowerCase().replace(/[^a-z0-9]/g, '');
    const cible = cle(matricule);
    if (!cible) return aucun;

    const egal = this.vehicles.find(v => !!cle(v.plate) && cle(v.plate) === cible);
    if (egal) return { vehicule: egal, exact: true };

    const candidats = this.vehicles.filter(v => {
      const plaque = cle(v.plate);
      if (!plaque) return false;                                            // sans matricule, rien à comparer
      if (Math.min(plaque.length, cible.length) < LONGUEUR_MIN_RAPPROCHEMENT) return false;
      if (Math.abs(plaque.length - cible.length) > ECART_MAX_RAPPROCHEMENT) return false;
      return plaque.includes(cible) || cible.includes(plaque);
    });
    // Deux candidats = on ne sait pas : mieux vaut un champ vide qu'un mauvais véhicule.
    return candidats.length === 1 ? { vehicule: candidats[0], exact: false } : aucun;
  }

  /**
   * Type de carburant déduit du ticket, UNIQUEMENT s'il y est écrit. Deviner
   * « Diesel » parce que le parc en est plein fausserait la consommation d'un
   * véhicule essence : sans mention, l'utilisateur choisit.
   */
  private deduireTypeCarburant(x: ExtractionFacture): number | null {
    // Description et lignes seulement : le nom du fournisseur est écarté, une
    // « Station Essence du Nord » qui vend du gazole ferait dire n'importe quoi.
    const texte = [x.description, ...x.items.map(i => i.label)]
      .filter(t => !!t).join(' ').toLowerCase();
    if (!texte) return null;
    // Ordre important : « sans plomb » et « SP95 » avant « essence », sinon un
    // ticket « Essence Sans Plomb 95 » retomberait sur Essence.
    const regles: Array<{ motif: RegExp; codes: string[] }> = [
      { motif: /sans[\s-]?plomb|\bsp\s?9[58]\b|\be10\b/, codes: ['sans_plomb', 'essence'] },
      { motif: /gazole|gazoil|gas[\s-]?oil|diesel/, codes: ['diesel'] },
      { motif: /\bgpl\b|\blpg\b/, codes: ['gpl'] },
      { motif: /\bgnv\b|\bcng\b/, codes: ['gnv'] },
      { motif: /essence|super/, codes: ['essence', 'sans_plomb'] }
    ];
    for (const regle of regles) {
      if (!regle.motif.test(texte)) continue;
      for (const code of regle.codes) {
        const type = this.fuelTypes.find(t => (t.code || '').toLowerCase() === code);
        if (type) return type.id;
      }
    }
    return null;
  }

  /** Libellé FR d'une catégorie rendue par le scan (vocabulaire de l'écran Dépenses). */
  private libelleCategorieScan(categorie: string): string {
    return ({
      maintenance: 'entretien', repair: 'réparation', insurance: 'assurance', tax: 'taxe',
      toll: 'péage', parking: 'stationnement', fine: 'amende', other: 'divers'
    } as Record<string, string>)[categorie] || categorie;
  }

  /** Coupe à la longueur de la colonne : au-delà, le serveur refuse l'enregistrement. */
  private tronquer(valeur: string, max: number): string {
    return valeur.length > max ? valeur.slice(0, max) : valeur;
  }

  // Manual entry — accept either (volume + price) OR a free total.
  // Both modes still need a vehicle, a fuel type and a date.
  isManualEntryValid(): boolean {
    if (!this.manualEntry.vehiclePlate || this.manualEntry.fuelTypeId === null || !this.manualEntry.invoiceDate) {
      return false;
    }
    const hasVolumeAndPrice = (this.manualEntry.volume ?? 0) > 0 && (this.manualEntry.pricePerLiter ?? 0) > 0;
    const hasTotal = (this.manualEntry.totalAmount ?? 0) > 0;
    return hasVolumeAndPrice || hasTotal;
  }

  resetManualEntry() {
    this.dateParDefaut = new Date().toISOString().split('T')[0];
    this.manualEntry = { vehiclePlate: '', fuelTypeId: null, volume: 0, pricePerLiter: 0, totalAmount: 0, invoiceDate: this.dateParDefaut, odometerKm: null };
    this.totalAmountTouched = false;
    // Le ticket scanné ne survit pas à une remise à zéro : sinon sa station et son
    // justificatif partiraient avec une saisie qui n'a plus rien à voir avec lui.
    this.scanTicket = this.revueVide();
  }

  saveManualEntry() {
    if (!this.isManualEntryValid()) {
      alert('Renseignez matricule, type, date et soit (volume + prix) soit le montant total.');
      return;
    }
    this.isSaving = true;
    // Use the user-entered total when they typed one directly; otherwise
    // fall back to volume × price (already kept in sync by onVolumeOrPriceChange).
    const total = this.totalAmountTouched && (this.manualEntry.totalAmount ?? 0) > 0
      ? (this.manualEntry.totalAmount ?? 0)
      : (this.manualEntry.volume ?? 0) * (this.manualEntry.pricePerLiter ?? 0);
    const ticket = this.scanTicket;
    this.apiService.createFuelEntry({
      vehiclePlate: this.manualEntry.vehiclePlate,
      fuelTypeId: this.manualEntry.fuelTypeId!,
      volume: this.manualEntry.volume ?? 0,
      pricePerLiter: this.manualEntry.pricePerLiter ?? 0,
      totalAmount: total,
      invoiceDate: this.manualEntry.invoiceDate,
      odometerKm: this.manualEntry.odometerKm && this.manualEntry.odometerKm > 0
        ? this.manualEntry.odometerKm
        : undefined,
      // Ce que le ticket apporte en plus des champs du formulaire : la station et
      // le numéro de ticket ont déjà leur colonne côté serveur.
      stationName: ticket.stationName || undefined,
      invoiceNumber: ticket.invoiceNumber || undefined,
      // fuel_entries n'a PAS de colonne justificatif : sans cette note, le fichier
      // stocké par le serveur ne serait rattaché à rien et deviendrait orphelin.
      notes: ticket.receiptUrl ? this.tronquer('Ticket scanné : ' + ticket.receiptUrl, 500) : undefined
    }).pipe(takeUntil(this.destroy$)).subscribe({
      next: () => {
        this.isSaving = false;
        this.resetManualEntry();
        this.loadHistory();
        alert('Entrée carburant enregistrée avec succès');
      },
      error: (err) => {
        console.error(err);
        this.isSaving = false;
        const msg = err?.error?.message || err?.message || 'Erreur lors de l\u0027enregistrement';
        alert(msg);
      }
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
    this.columnMapping = { vehiclePlate: null, volume: null, pricePerLiter: null, invoiceDate: null, fuelType: null, odometerKm: null };
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
        odometerKm: this.columnMapping.odometerKm !== null
          ? (this.parseNumber(r[this.columnMapping.odometerKm]) || null)
          : null,
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
      invoiceDate: e.invoiceDate,
      odometerKm: e.odometerKm && e.odometerKm > 0 ? e.odometerKm : undefined
    }));
    this.apiService.bulkCreateFuelEntries(requests).pipe(takeUntil(this.destroy$)).subscribe({
      next: (result) => {
        console.log(`Import carburant: ${result.success}/${result.total} réussis`);
        if (result.results) {
          const failed = result.results.filter((r: any) => !r.success);
          if (failed.length > 0) {
            console.warn('Entrées échouées:', failed);
            const errorDetails = failed.map((r: any, i: number) => {
              const req = requests[result.results.indexOf(r)];
              return `• ${req?.vehiclePlate || '?'}: ${r.error}`;
            }).join('\n');
            alert(`Import terminé: ${result.success} réussis, ${result.failed} échoués sur ${result.total}\n\nDétails des erreurs:\n${errorDetails}`);
          } else {
            alert(`Import réussi: ${result.success} entrées importées`);
          }
        } else if (result.failed > 0) {
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
