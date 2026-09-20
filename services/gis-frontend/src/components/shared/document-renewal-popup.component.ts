import { Component, Input, Output, EventEmitter, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { ApiService } from '../../services/api.service';
import { UserPreferencesService } from '../../services/user-preferences.service';
import { USER_PREF_PIPES } from '../../pipes/user-preference-pipes';
import {
  ScanFactureComponent, ResultatScanFacture, EchecScanFacture, ExtractionFacture
} from './scan-facture.component';

export interface VehicleDocument {
  id?: number;
  vehicleId: number;
  vehicleName: string;
  vehiclePlate: string;
  type: 'insurance' | 'tax' | 'technical_inspection' | 'registration' | 'transport_permit';
  expiryDate: Date;
  documentNumber?: string;
  documentUrl?: string;
  lastRenewalDate?: Date;
  lastRenewalCost?: number;
  reminderDays: number;
  // 'unknown' : échéance jamais renseignée, désormais transmise par l'écran Échéances.
  status: 'expired' | 'expiring_soon' | 'ok' | 'unknown';
  daysUntilExpiry: number;
}

export interface RenewalFormData {
  vehicleId: number;
  type: string;
  amount: number;
  date: string;
  newExpiryDate: string;
  documentNumber: string;
  provider: string;
  notes: string;
  reminderDays?: number;
  /** Justificatif scanné (/uploads/invoices/...) — enregistré en ReceiptUrl du coût. */
  documentUrl?: string;
}

@Component({
  selector: 'app-document-renewal-popup',
  standalone: true,
  imports: [CommonModule, FormsModule, ScanFactureComponent, ...USER_PREF_PIPES],
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 }))
      ]),
      transition(':leave', [
        animate('150ms ease-in', style({ opacity: 0 }))
      ])
    ]),
    trigger('slideIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateX(20px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateX(0)' }))
      ]),
      transition(':leave', [
        animate('200ms ease-in', style({ opacity: 0, transform: 'translateX(20px)' }))
      ])
    ])
  ],
  template: `
    <div class="popup-overlay" *ngIf="isOpen" @fadeIn (click)="close()">
      <div class="popup-container" @slideIn (click)="$event.stopPropagation()">
        <!-- Header -->
        <div class="popup-header" [class]="document?.type">
          <div class="header-content">
            <span class="header-icon">{{ getTypeIcon(document?.type) }}</span>
            <div class="header-info">
              <h2>Renouveler {{ getTypeLabel(document?.type) }}</h2>
              <p>{{ document?.vehicleName }} - {{ document?.vehiclePlate }}</p>
            </div>
          </div>
          <button class="btn-close" (click)="close()">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="18" y1="6" x2="6" y2="18"/>
              <line x1="6" y1="6" x2="18" y2="18"/>
            </svg>
          </button>
        </div>

        <!-- Current Status -->
        <div class="status-banner" [class]="document?.status">
          <div class="status-icon">
            <svg *ngIf="document?.status === 'expired'" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="12" cy="12" r="10"/>
              <line x1="15" y1="9" x2="9" y2="15"/>
              <line x1="9" y1="9" x2="15" y2="15"/>
            </svg>
            <svg *ngIf="document?.status === 'expiring_soon'" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/>
              <line x1="12" y1="9" x2="12" y2="13"/>
              <line x1="12" y1="17" x2="12.01" y2="17"/>
            </svg>
            <svg *ngIf="document?.status === 'ok'" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
              <polyline points="22 4 12 14.01 9 11.01"/>
            </svg>
            <svg *ngIf="document?.status === 'unknown'" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="12" cy="12" r="10"/>
              <line x1="12" y1="16" x2="12" y2="12"/>
              <line x1="12" y1="8" x2="12.01" y2="8"/>
            </svg>
          </div>
          <div class="status-text">
            <span class="status-label">{{ getStatusText() }}</span>
            <span class="status-date">Expiration actuelle: {{ formatDate(document?.expiryDate) }}</span>
          </div>
        </div>

        <!-- Scan IA : brique partagée avec Dépenses, Entretien, Réparations et
             Carburant. Posée HORS du <form> à dessein — son bouton n'a pas de
             type="button" et déclencherait l'enregistrement du renouvellement. -->
        <div class="scan-bar">
          <app-scan-facture libelle="Scanner la quittance"
                            (scanne)="onFactureScannee($event)"
                            (echec)="onEchecScan($event)"></app-scan-facture>
          <span class="scan-hint">Montant, date, fournisseur et n° lus sur le document.</span>
        </div>

        <!-- Ce que le scan a posé, et ce qu'il a lu SANS l'imposer. -->
        <div class="scan-recap" *ngIf="scanFait">
          <div class="scan-recap-head">
            <span class="scan-recap-title">Pré-rempli par le scan</span>
            <span class="scan-conf" [ngClass]="'scan-conf-' + scanConfiance" *ngIf="scanConfiance">
              Confiance {{ scanConfianceLabel() }}
            </span>
            <a class="scan-doc-link" *ngIf="formData.documentUrl"
               [href]="formData.documentUrl" target="_blank" rel="noopener">Voir le document</a>
          </div>
          <ul class="scan-champs" *ngIf="scanChamps.length">
            <li *ngFor="let champ of scanChamps">{{ champ }}</li>
          </ul>
          <p class="scan-vide" *ngIf="!scanChamps.length">
            Rien n'a pu être lu : saisissez les informations à la main.
          </p>
          <p class="scan-warn" *ngFor="let avert of scanAvertissements">{{ avert }}</p>
          <p class="scan-warn" *ngIf="scanFournisseurIntrouvable">
            Fournisseur lu « {{ scanFournisseurIntrouvable }} » — absent de la liste ; « + Ajouter » le pré-remplit.
          </p>
          <div class="scan-propose" *ngIf="scanExpirationProposee">
            <span>Validité lue sur le document : {{ formatInputDate(scanExpirationProposee) }}</span>
            <button type="button" class="quick-btn" (click)="appliquerExpirationScannee()">Utiliser cette date</button>
          </div>
        </div>

        <!-- Form -->
        <form class="popup-form" (ngSubmit)="onSubmit()" #renewalForm="ngForm">
          <!-- Payment Info Section -->
          <div class="form-section">
            <h3 class="section-title">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="12" y1="1" x2="12" y2="23"/>
                <path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>
              </svg>
              Informations de paiement
            </h3>

            <div class="form-row two-cols">
              <div class="form-group">
                <label for="amount">Montant payé <span class="optional-hint">(facultatif)</span></label>
                <div class="input-with-suffix">
                  <input type="number" id="amount" name="amount" [(ngModel)]="formData.amount"
                         placeholder="0.00" class="form-control" min="0" step="0.01">
                  <span class="suffix">{{ currencyCode }}</span>
                </div>
              </div>
              <div class="form-group">
                <label for="date">Date de paiement <span class="required-star">*</span></label>
                <input type="date" id="date" name="date" [(ngModel)]="formData.date" required class="form-control">
              </div>
            </div>

            <div class="form-row">
              <div class="form-group full">
                <label for="provider">Fournisseur / Compagnie</label>
                <select id="provider" name="provider" [(ngModel)]="formData.provider" class="form-control" *ngIf="!showNewSupplierForm">
                  <option value="">-- Sélectionner un fournisseur --</option>
                  <option *ngFor="let supplier of filteredSuppliers" [value]="supplier.name">
                    {{ supplier.name }}{{ supplier.city ? ' - ' + supplier.city : '' }}
                  </option>
                </select>
                <div class="supplier-hint" *ngIf="!showNewSupplierForm">
                  <span class="hint-text" *ngIf="filteredSuppliers.length === 0">Aucun fournisseur trouvé.</span>
                  <button type="button" class="create-link-btn" (click)="toggleNewSupplierForm()">+ Ajouter</button>
                </div>
                
                <!-- Formulaire création fournisseur inline -->
                <div class="inline-supplier-form" *ngIf="showNewSupplierForm">
                  <!-- Section Informations générales -->
                  <div class="inline-form-section">
                    <h4 class="inline-section-title">
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M3 21h18"/><path d="M5 21V7l8-4 8 4v14"/><path d="M9 21v-4a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2v4"/>
                      </svg>
                      Informations générales
                    </h4>
                    
                    <div class="inline-form-row two-cols">
                      <div class="inline-form-group">
                        <label>Nom *</label>
                        <input type="text" [(ngModel)]="newSupplier.name" name="newSupplierName" 
                               placeholder="Ex: Assurance XYZ" class="form-control">
                      </div>
                      <div class="inline-form-group">
                        <label>Type</label>
                        <select [(ngModel)]="newSupplier.type" name="newSupplierType" class="form-control" disabled>
                          <option value="garage">🔧 Garage</option>
                          <option value="insurance">🛡️ Assurance</option>
                          <option value="vendor">🏭 Vendeur</option>
                          <option value="parts">⚙️ Pièces détachées</option>
                          <option value="fuel">⛽ Carburant</option>
                          <option value="tires">🛞 Pneumatiques</option>
                          <option value="service">🛠️ Service</option>
                          <option value="general">📦 Général</option>
                        </select>
                      </div>
                    </div>

                    <div class="inline-form-row">
                      <div class="inline-form-group full">
                        <label>Adresse</label>
                        <input type="text" [(ngModel)]="newSupplier.address" name="newSupplierAddress" 
                               placeholder="Ex: 45 avenue de la République" class="form-control">
                      </div>
                    </div>

                    <div class="inline-form-row two-cols">
                      <div class="inline-form-group">
                        <label>Ville</label>
                        <input type="text" [(ngModel)]="newSupplier.city" name="newSupplierCity" 
                               placeholder="Ex: Lyon" class="form-control">
                      </div>
                      <div class="inline-form-group">
                        <label>Code postal</label>
                        <input type="text" [(ngModel)]="newSupplier.postalCode" name="newSupplierPostalCode" 
                               placeholder="Ex: 69003" class="form-control">
                      </div>
                    </div>
                  </div>

                  <!-- Section Contact -->
                  <div class="inline-form-section">
                    <h4 class="inline-section-title">
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72"/>
                      </svg>
                      Contact
                    </h4>

                    <div class="inline-form-row two-cols">
                      <div class="inline-form-group">
                        <label>Téléphone</label>
                        <input type="tel" [(ngModel)]="newSupplier.phone" name="newSupplierPhone" 
                               placeholder="+33 X XX XX XX XX" class="form-control">
                      </div>
                      <div class="inline-form-group">
                        <label>Email</label>
                        <input type="email" [(ngModel)]="newSupplier.email" name="newSupplierEmail" 
                               placeholder="contact@exemple.fr" class="form-control">
                      </div>
                    </div>
                  </div>

                  <!-- Boutons -->
                  <div class="inline-form-actions">
                    <button type="button" class="btn-cancel-inline" (click)="toggleNewSupplierForm()">Annuler</button>
                    <button type="button" class="btn-save-inline" (click)="createSupplier()" [disabled]="!newSupplier.name || creatingSupplier">
                      <span class="spinner-small" *ngIf="creatingSupplier"></span>
                      {{ creatingSupplier ? 'Création...' : 'Enregistrer' }}
                    </button>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- Document Info Section -->
          <div class="form-section">
            <h3 class="section-title">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                <polyline points="14 2 14 8 20 8"/>
              </svg>
              Informations du document
            </h3>

            <div class="form-row two-cols">
              <div class="form-group">
                <label for="documentNumber">{{ getDocumentNumberLabel() }}</label>
                <input type="text" id="documentNumber" name="documentNumber" [(ngModel)]="formData.documentNumber"
                       [placeholder]="getDocumentNumberPlaceholder()" class="form-control" maxlength="100">
              </div>
              <div class="form-group">
                <label for="newExpiryDate">Nouvelle date d'expiration <span class="required-star">*</span></label>
                <input type="date" id="newExpiryDate" name="newExpiryDate" [(ngModel)]="formData.newExpiryDate" 
                       required class="form-control">
              </div>
            </div>

            <div class="form-row two-cols">
              <div class="form-group">
                <label for="reminderDays">Rappel avant (jours)</label>
                <div class="input-with-suffix">
                  <input type="number" id="reminderDays" name="reminderDays" [(ngModel)]="formData.reminderDays"
                         placeholder="30" class="form-control" min="1" max="365">
                  <span class="suffix">jours</span>
                </div>
              </div>
              <div class="form-group"></div>
            </div>

            <div class="form-row">
              <div class="form-group full">
                <label for="notes">Notes / Remarques</label>
                <textarea id="notes" name="notes" [(ngModel)]="formData.notes" rows="2" maxlength="1000"
                          placeholder="Informations complémentaires..." class="form-control"></textarea>
              </div>
            </div>
          </div>

          <!-- Quick Date Buttons -->
          <div class="quick-dates">
            <span class="quick-label">Expiration rapide:</span>
            <button type="button" class="quick-btn" (click)="setExpiryFromNow(12)">+1 an</button>
            <button type="button" class="quick-btn" (click)="setExpiryFromNow(6)">+6 mois</button>
            <button type="button" class="quick-btn" (click)="setExpiryFromNow(24)">+2 ans</button>
          </div>

          <!-- Summary -->
          <div class="summary-box" *ngIf="formData.amount > 0">
            <div class="summary-row">
              <span>Type de dépense</span>
              <span class="summary-value">{{ getTypeLabel(document?.type) }}</span>
            </div>
            <div class="summary-row">
              <span>Montant</span>
              <span class="summary-value amount">{{ formData.amount | appCurrency }}</span>
            </div>
            <div class="summary-row" *ngIf="formData.newExpiryDate">
              <span>Valide jusqu'au</span>
              <span class="summary-value">{{ formatInputDate(formData.newExpiryDate) }}</span>
            </div>
          </div>

          <!-- Actions -->
          <div class="popup-actions">
            <button type="button" class="btn-cancel" (click)="close()">Annuler</button>
            <button type="submit" class="btn-save" [disabled]="renewalForm.invalid || saving">
              <svg *ngIf="!saving" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/>
                <polyline points="17 21 17 13 7 13 7 21"/>
                <polyline points="7 3 7 8 15 8"/>
              </svg>
              <span class="spinner" *ngIf="saving"></span>
              {{ saving ? 'Enregistrement...' : 'Enregistrer le renouvellement' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `,
  styles: [`
    /* ===== OVERLAY ===== */
    .popup-overlay {
      position: fixed;
      top: 42px; /* Below navbar */
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(0, 0, 0, 0.5);
      display: flex;
      justify-content: flex-end;
      z-index: 1050;
    }

    /* ===== CONTAINER ===== */
    .popup-container {
      width: 100%;
      max-width: 500px;
      height: 100%;
      background: white;
      display: flex;
      flex-direction: column;
      overflow: hidden;
    }

    /* ===== HEADER ===== */
    .popup-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 16px 20px;
      color: white;
    }

    .popup-header.insurance { background: linear-gradient(135deg, #3b82f6 0%, #1d4ed8 100%); }
    .popup-header.tax { background: linear-gradient(135deg, #f59e0b 0%, #d97706 100%); }
    .popup-header.technical_inspection { background: linear-gradient(135deg, #22c55e 0%, #16a34a 100%); }
    .popup-header.registration { background: linear-gradient(135deg, #8b5cf6 0%, #7c3aed 100%); }
    .popup-header.transport_permit { background: linear-gradient(135deg, #ec4899 0%, #be185d 100%); }

    .header-content {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .header-icon {
      font-size: 28px;
      line-height: 1;
    }

    .header-info h2 {
      margin: 0;
      font-size: 16px;
      font-weight: 600;
    }

    .header-info p {
      margin: 4px 0 0;
      font-size: 12px;
      opacity: 0.9;
    }

    .btn-close {
      background: rgba(255,255,255,0.2);
      border: none;
      width: 32px;
      height: 32px;
      border-radius: 6px;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      color: white;
      transition: background 0.2s;
    }

    .btn-close:hover {
      background: rgba(255,255,255,0.3);
    }

    /* ===== STATUS BANNER ===== */
    .status-banner {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px 20px;
      border-bottom: 1px solid #e2e8f0;
    }

    .status-banner.expired { background: #fef2f2; }
    .status-banner.expiring_soon { background: #fffbeb; }
    .status-banner.ok { background: #f0fdf4; }
    .status-banner.unknown { background: #f8fafc; }

    .status-icon {
      width: 36px;
      height: 36px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .status-banner.expired .status-icon { background: #fee2e2; color: #dc2626; }
    .status-banner.expiring_soon .status-icon { background: #fef3c7; color: #d97706; }
    .status-banner.ok .status-icon { background: #dcfce7; color: #16a34a; }
    .status-banner.unknown .status-icon { background: #e2e8f0; color: #64748b; }

    .status-text {
      display: flex;
      flex-direction: column;
    }

    .status-label {
      font-size: 13px;
      font-weight: 600;
      color: #1e293b;
    }

    .status-date {
      font-size: 11px;
      color: #64748b;
    }

    /* ===== SCAN IA ===== */
    /* La modale ne fait que 500 px : le bandeau passe à la ligne plutôt que de
       déborder (aucun ascenseur horizontal, écran 1536 px de Karim). */
    .scan-bar {
      display: flex;
      align-items: center;
      gap: 10px;
      flex-wrap: wrap;
      padding: 10px 20px;
      background: #faf9ff;
      border-bottom: 1px solid #e2e8f0;
    }

    .scan-hint {
      font-size: 11px;
      color: #64748b;
    }

    .scan-recap {
      padding: 10px 20px 12px;
      background: #f8fafc;
      border-bottom: 1px solid #e2e8f0;
      max-height: 180px;
      overflow-y: auto;
    }

    .scan-recap-head {
      display: flex;
      align-items: center;
      gap: 8px;
      flex-wrap: wrap;
      margin-bottom: 6px;
    }

    .scan-recap-title {
      font-size: 12px;
      font-weight: 600;
      color: #1e293b;
    }

    .scan-conf {
      padding: 2px 8px;
      border-radius: 999px;
      font-size: 10.5px;
      font-weight: 700;
    }

    .scan-conf-high { background: #ecfdf5; color: #047857; border: 1px solid #a7f3d0; }
    .scan-conf-medium { background: #fefce8; color: #a16207; border: 1px solid #fde68a; }
    .scan-conf-low { background: #fef2f2; color: #b91c1c; border: 1px solid #fecaca; }

    .scan-doc-link {
      margin-left: auto;
      font-size: 11px;
      color: #3b82f6;
      text-decoration: none;
    }

    .scan-doc-link:hover { text-decoration: underline; }

    .scan-champs {
      margin: 0;
      padding-left: 18px;
      font-size: 11.5px;
      color: #475569;
    }

    .scan-champs li { margin-bottom: 2px; }

    .scan-vide {
      margin: 0;
      font-size: 11.5px;
      color: #64748b;
    }

    .scan-warn {
      margin: 6px 0 0;
      font-size: 11.5px;
      color: #b45309;
    }

    .scan-propose {
      display: flex;
      align-items: center;
      gap: 8px;
      flex-wrap: wrap;
      margin-top: 8px;
      font-size: 11.5px;
      color: #475569;
    }

    /* ===== FORM ===== */
    .popup-form {
      flex: 1;
      overflow-y: auto;
      padding: 20px;
    }

    .form-section {
      margin-bottom: 24px;
      padding-bottom: 20px;
      border-bottom: 1px solid #e2e8f0;
    }

    .form-section:last-of-type {
      border-bottom: none;
    }

    .section-title {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 14px;
      font-weight: 600;
      color: #1e293b;
      margin: 0 0 16px;
    }

    .section-title svg {
      color: #3b82f6;
    }

    .form-row {
      margin-bottom: 16px;
    }

    .form-row:last-child {
      margin-bottom: 0;
    }

    .form-row.two-cols {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 16px;
    }

    .form-group {
      display: flex;
      flex-direction: column;
    }

    .form-group.full {
      width: 100%;
    }

    .form-group label {
      font-size: 12px;
      font-weight: 500;
      color: #64748b;
      margin-bottom: 6px;
    }

    .required-star {
      color: #e74c3c;
      font-weight: 600;
    }

    .form-control {
      padding: 10px 12px;
      border: 1px solid #e2e8f0;
      border-radius: 6px;
      font-size: 13px;
      font-family: inherit;
      color: #1e293b;
      transition: border-color 0.2s, box-shadow 0.2s;
    }

    .form-control:focus {
      outline: none;
      border-color: #3b82f6;
      box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
    }

    .form-control::placeholder {
      color: #94a3b8;
    }

    textarea.form-control {
      resize: vertical;
      min-height: 60px;
    }

    .input-with-suffix {
      position: relative;
    }

    .input-with-suffix input {
      padding-right: 40px;
    }

    .suffix {
      position: absolute;
      right: 12px;
      top: 50%;
      transform: translateY(-50%);
      color: #64748b;
      font-size: 12px;
      font-weight: 500;
    }

    /* ===== QUICK DATES ===== */
    .quick-dates {
      display: flex;
      align-items: center;
      gap: 8px;
      margin-bottom: 20px;
    }

    .quick-label {
      font-size: 12px;
      color: #64748b;
    }

    .quick-btn {
      padding: 6px 12px;
      background: #f1f5f9;
      border: 1px solid #e2e8f0;
      border-radius: 4px;
      font-size: 11px;
      font-weight: 500;
      color: #475569;
      cursor: pointer;
      transition: all 0.2s;
    }

    .quick-btn:hover {
      background: #e2e8f0;
      border-color: #cbd5e1;
    }

    /* ===== SUMMARY ===== */
    .summary-box {
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      padding: 14px;
      margin-bottom: 20px;
    }

    .summary-row {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 6px 0;
      font-size: 12px;
      color: #64748b;
    }

    .summary-row:not(:last-child) {
      border-bottom: 1px dashed #e2e8f0;
    }

    .summary-value {
      font-weight: 600;
      color: #1e293b;
    }

    .summary-value.amount {
      font-size: 14px;
      color: #16a34a;
    }

    /* ===== ACTIONS ===== */
    .popup-actions {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 16px 20px;
      background: #f8fafc;
      border-top: 1px solid #e2e8f0;
    }

    .btn-cancel {
      padding: 10px 20px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 6px;
      font-size: 13px;
      color: #64748b;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-cancel:hover {
      background: #f1f5f9;
      border-color: #cbd5e1;
    }

    .btn-save {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 20px;
      background: linear-gradient(135deg, #22c55e 0%, #16a34a 100%);
      border: none;
      border-radius: 6px;
      font-size: 13px;
      font-weight: 500;
      color: white;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-save:hover:not(:disabled) {
      background: linear-gradient(135deg, #16a34a 0%, #15803d 100%);
    }

    .btn-save:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }

    .spinner {
      width: 14px;
      height: 14px;
      border: 2px solid rgba(255,255,255,0.3);
      border-top-color: white;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }

    /* ===== SUPPLIER HINT ===== */
    .supplier-hint {
      display: flex;
      align-items: center;
      gap: 8px;
      margin-top: 6px;
      font-size: 11px;
    }

    .hint-text {
      color: #94a3b8;
    }

    .create-link-btn {
      background: none;
      border: none;
      color: #3b82f6;
      font-size: 11px;
      font-weight: 500;
      cursor: pointer;
      padding: 0;
      transition: color 0.2s;
    }

    .create-link-btn:hover {
      color: #2563eb;
      text-decoration: underline;
    }

    /* ===== INLINE SUPPLIER FORM ===== */
    .inline-supplier-form {
      background: linear-gradient(135deg, #f8fafc 0%, #f1f5f9 100%);
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      padding: 16px;
      margin-top: 12px;
    }

    .inline-form-section {
      margin-bottom: 16px;
    }

    .inline-form-section:last-of-type {
      margin-bottom: 12px;
    }

    .inline-section-title {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 13px;
      font-weight: 600;
      color: #1e293b;
      margin-bottom: 12px;
      padding-bottom: 8px;
      border-bottom: 1px solid #e2e8f0;
    }

    .inline-section-title svg {
      color: #3b82f6;
    }

    .inline-form-row {
      margin-bottom: 10px;
    }

    .inline-form-row.two-cols {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
    }

    .inline-form-group {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .inline-form-group.full {
      grid-column: span 2;
    }

    .inline-form-group label {
      font-size: 11px;
      font-weight: 500;
      color: #64748b;
    }

    .inline-form-group .form-control {
      padding: 8px 10px;
      font-size: 12px;
      border: 1px solid #e2e8f0;
      border-radius: 6px;
      background: white;
      transition: border-color 0.2s, box-shadow 0.2s;
    }

    .inline-form-group .form-control:focus {
      border-color: #3b82f6;
      box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
      outline: none;
    }

    .inline-form-group .form-control:disabled {
      background: #f1f5f9;
      color: #64748b;
      cursor: not-allowed;
    }

    .inline-form-actions {
      display: flex;
      justify-content: flex-end;
      gap: 10px;
      padding-top: 12px;
      border-top: 1px solid #e2e8f0;
    }

    .btn-cancel-inline {
      padding: 8px 16px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 6px;
      font-size: 12px;
      font-weight: 500;
      color: #64748b;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-cancel-inline:hover {
      background: #f1f5f9;
      border-color: #cbd5e1;
    }

    .btn-save-inline {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 8px 16px;
      background: linear-gradient(135deg, #3b82f6 0%, #2563eb 100%);
      border: none;
      border-radius: 6px;
      font-size: 12px;
      font-weight: 500;
      color: white;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-save-inline:hover:not(:disabled) {
      background: linear-gradient(135deg, #2563eb 0%, #1d4ed8 100%);
    }

    .btn-save-inline:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }

    .spinner-small {
      width: 12px;
      height: 12px;
      border: 2px solid rgba(255,255,255,0.3);
      border-top-color: white;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
    }

    /* ===== RESPONSIVE ===== */
    @media (max-width: 540px) {
      .popup-container {
        max-width: 100%;
      }

      .form-row.two-cols {
        grid-template-columns: 1fr;
      }
    }
  `]
})
export class DocumentRenewalPopupComponent implements OnChanges {
  @Input() isOpen = false;
  @Input() document: VehicleDocument | null = null;
  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<any>();

  formData: RenewalFormData = this.getEmptyForm();
  saving = false;

  // ── Scan IA de la quittance ────────────────────────────────────────────────
  /** Un scan a été tenté : le récapitulatif est affiché. */
  scanFait = false;
  /** high | medium | low — confiance rendue par l'extraction. */
  scanConfiance = '';
  /** Champs réellement posés par le scan, tels qu'annoncés à l'utilisateur. */
  scanChamps: string[] = [];
  /** Valeurs lues mais NON posées (champ déjà saisi) ou qui détonnent. */
  scanAvertissements: string[] = [];
  /** Fournisseur lu absent de la liste : on ne peut pas le sélectionner. */
  scanFournisseurIntrouvable = '';
  /** Validité lue sur le document : proposée, jamais posée d'office. */
  scanExpirationProposee = '';
  /** Nom lu sur la facture, gardé pour le pré-remplissage de « + Ajouter ». */
  private nomFournisseurScanne = '';
  /** Date de paiement posée à l'ouverture : repère « l'utilisateur n'y a pas touché ». */
  private dateParDefaut = '';


  // Suppliers
  allSuppliers: { id: number; name: string; type: string; city?: string }[] = [];
  filteredSuppliers: { id: number; name: string; type: string; city?: string }[] = [];
  
  // New supplier form
  showNewSupplierForm = false;
  creatingSupplier = false;
  newSupplier = {
    name: '',
    type: 'insurance',
    address: '',
    city: '',
    postalCode: '',
    phone: '',
    email: ''
  };

  constructor(private apiService: ApiService, private userPrefs: UserPreferencesService) {}

  /** Active currency code for the amount input adornment. */
  get currencyCode(): string { return this.userPrefs.current.currency; }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['document'] || changes['isOpen']) {
      if (this.isOpen && this.document) {
        this.formData = this.getEmptyForm();
        this.formData.vehicleId = this.document.vehicleId;
        this.formData.type = this.getCostType(this.document.type);
        this.formData.date = new Date().toISOString().split('T')[0];
        this.dateParDefaut = this.formData.date;

        // Set default expiry to 1 year from now
        this.setExpiryFromNow(12);

        // Un renouvellement en chasse un autre : le récapitulatif du scan
        // précédent ne doit pas survivre à la réouverture de la modale.
        this.reinitialiserScan();

        // Load suppliers filtered by document type
        this.loadSuppliers();
      }
    }
  }
  
  loadSuppliers(): void {
    this.apiService.getSuppliers({ pageSize: 100, isActive: true }).subscribe({
      next: (result) => {
        this.allSuppliers = result.items.map(s => ({
          id: s.id,
          name: s.name,
          type: s.type || 'general',
          city: s.city
        }));
        this.filterSuppliersByDocumentType();
        // La liste peut arriver APRÈS le scan (modale ouverte puis fichier choisi
        // dans la foulée) : on rejoue l'association du fournisseur lu.
        this.rattacherFournisseurScanne();
      },
      error: (err) => console.error('Error loading suppliers:', err)
    });
  }
  
  filterSuppliersByDocumentType(): void {
    if (!this.document) {
      this.filteredSuppliers = this.allSuppliers;
      return;
    }
    
    // Map document type to supplier type
    const typeMapping: { [key: string]: string[] } = {
      'insurance': ['insurance'],
      'technical_inspection': ['service', 'garage'],
      'tax': ['general', 'service'],
      'registration': ['general', 'service'],
      'transport_permit': ['general', 'service']
    };
    
    const allowedTypes = typeMapping[this.document.type] || ['general'];
    this.filteredSuppliers = this.allSuppliers.filter(s => allowedTypes.includes(s.type));
  }
  
  toggleNewSupplierForm(): void {
    this.showNewSupplierForm = !this.showNewSupplierForm;
    if (this.showNewSupplierForm) {
      // Reset form avec le type par défaut ; le nom lu sur la quittance évite de
      // le retaper quand le fournisseur n'existe pas encore dans la liste.
      this.newSupplier = {
        name: this.nomFournisseurScanne,
        type: this.getSupplierTypeForDocument(),
        address: '',
        city: '',
        postalCode: '',
        phone: '',
        email: ''
      };
    }
  }
  
  createSupplier(): void {
    if (!this.newSupplier.name) return;
    
    this.creatingSupplier = true;
    const supplierData = {
      name: this.newSupplier.name,
      type: this.newSupplier.type,
      address: this.newSupplier.address || undefined,
      city: this.newSupplier.city || undefined,
      postalCode: this.newSupplier.postalCode || undefined,
      phone: this.newSupplier.phone || undefined,
      email: this.newSupplier.email || undefined,
      isActive: true
    };
    
    this.apiService.createSupplier(supplierData).subscribe({
      next: (supplierId) => {
        // Ajouter à la liste et sélectionner
        const createdSupplier = {
          id: supplierId,
          name: this.newSupplier.name,
          type: this.newSupplier.type,
          city: this.newSupplier.city
        };
        this.allSuppliers.push(createdSupplier);
        this.filterSuppliersByDocumentType();
        this.formData.provider = this.newSupplier.name;
        // Le fournisseur lu sur la quittance existe maintenant : l'avertissement n'a plus lieu d'être.
        this.scanFournisseurIntrouvable = '';

        // Reset form
        this.showNewSupplierForm = false;
        this.creatingSupplier = false;
      },
      error: (err) => {
        console.error('Error creating supplier:', err);
        this.creatingSupplier = false;
        alert('Erreur lors de la création du fournisseur');
      }
    });
  }
  
  getSupplierTypeForDocument(): string {
    if (!this.document) return 'general';
    const mapping: { [key: string]: string } = {
      'insurance': 'insurance',
      'technical_inspection': 'service',
      'tax': 'general',
      'registration': 'general',
      'transport_permit': 'general'
    };
    return mapping[this.document.type] || 'general';
  }
  
  getSupplierTypeLabel(): string {
    const type = this.getSupplierTypeForDocument();
    const labels: { [key: string]: string } = {
      'insurance': 'Assurance',
      'service': 'Service',
      'garage': 'Garage',
      'general': 'Fournisseur'
    };
    return labels[type] || type;
  }

  getEmptyForm(): RenewalFormData {
    return {
      vehicleId: 0,
      type: '',
      amount: 0,
      date: '',
      newExpiryDate: '',
      documentNumber: '',
      provider: '',
      notes: '',
      reminderDays: 30,
      documentUrl: ''
    };
  }

  getCostType(docType: string): string {
    const mapping: { [key: string]: string } = {
      'insurance': 'insurance',
      'tax': 'tax',
      'technical_inspection': 'maintenance',
      'registration': 'other',
      'transport_permit': 'other'
    };
    return mapping[docType] || 'other';
  }

  setExpiryFromNow(months: number): void {
    const date = new Date();
    date.setMonth(date.getMonth() + months);
    this.formData.newExpiryDate = date.toISOString().split('T')[0];
  }

  getTypeLabel(type: string | undefined): string {
    if (!type) return '';
    const labels: { [key: string]: string } = {
      'insurance': 'Assurance',
      'tax': 'Vignette',
      'technical_inspection': 'Visite technique',
      'registration': 'Carte grise',
      'transport_permit': 'Autorisation transport'
    };
    return labels[type] || type;
  }

  getTypeIcon(type: string | undefined): string {
    if (!type) return '📄';
    const icons: { [key: string]: string } = {
      'insurance': '🛡️',
      'tax': '📋',
      'technical_inspection': '🔧',
      'registration': '📄',
      'transport_permit': '🚛'
    };
    return icons[type] || '📄';
  }

  getStatusText(): string {
    if (!this.document) return '';
    switch (this.document.status) {
      case 'expired': return `Expiré depuis ${Math.abs(this.document.daysUntilExpiry)} jour(s)`;
      case 'expiring_soon': return `Expire dans ${this.document.daysUntilExpiry} jour(s)`;
      case 'ok': return 'Document en règle';
      case 'unknown': return 'Échéance non renseignée';
      default: return '';
    }
  }

  getProviderPlaceholder(): string {
    if (!this.document) return 'Nom du fournisseur';
    switch (this.document.type) {
      case 'insurance': return 'Ex: AXA, Macif, Groupama...';
      case 'tax': return 'Ex: Trésor public';
      case 'technical_inspection': return 'Ex: Centre de contrôle technique';
      default: return 'Nom du fournisseur';
    }
  }

  getDocumentNumberLabel(): string {
    if (!this.document) return 'N° Document';
    switch (this.document.type) {
      case 'insurance': return 'N° Police';
      case 'tax': return 'N° Vignette';
      case 'technical_inspection': return 'N° PV';
      case 'registration': return 'N° Immatriculation';
      default: return 'N° Document';
    }
  }

  getDocumentNumberPlaceholder(): string {
    if (!this.document) return '';
    switch (this.document.type) {
      case 'insurance': return 'Ex: POL-2026-12345';
      case 'tax': return 'Ex: VIG-2026-001';
      case 'technical_inspection': return 'Ex: CT-2026-001';
      default: return '';
    }
  }

  formatDate(date: Date | undefined): string {
    if (!date) return '-';
    // Jour UTC, comme le décompte des jours restants (DEF-035).
    return new Date(date).toLocaleDateString('fr-FR', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      timeZone: 'UTC'
    });
  }

  formatInputDate(dateStr: string): string {
    if (!dateStr) return '-';
    // Un yyyy-MM-dd est lu en minuit UTC : sans timeZone, un fuseau en retard
    // sur UTC afficherait la veille. Même convention que formatDate (DEF-035).
    return new Date(dateStr).toLocaleDateString('fr-FR', {
      day: '2-digit',
      month: 'long',
      year: 'numeric',
      timeZone: 'UTC'
    });
  }

  // ── Scan IA de la quittance ────────────────────────────────────────────────

  /** Remet le récapitulatif à zéro — sans toucher au formulaire. */
  private reinitialiserScan(): void {
    this.scanFait = false;
    this.scanConfiance = '';
    this.scanChamps = [];
    this.scanAvertissements = [];
    this.scanFournisseurIntrouvable = '';
    this.scanExpirationProposee = '';
    this.nomFournisseurScanne = '';
  }

  /**
   * Quittance scannée : <app-scan-facture> a déjà tout fait (envoi, quota,
   * erreurs) et rend l'extraction brute. Ici on ne fait que PROPOSER :
   * un champ déjà saisi n'est jamais écrasé en silence, la valeur lue part
   * alors dans les avertissements pour que l'utilisateur tranche lui-même.
   */
  onFactureScannee(res: ResultatScanFacture): void {
    const x = res.extraction;
    this.reinitialiserScan();
    this.scanFait = true;
    this.scanConfiance = x.confidence || '';
    if (res.receiptUrl) {
      this.formData.documentUrl = res.receiptUrl;
      this.scanChamps.push('Justificatif rattaché au renouvellement');
    }

    // Montant payé — facultatif ici, donc 0 vaut « non saisi ».
    if (x.total !== null) {
      if (!(Number(this.formData.amount) > 0)) {
        this.formData.amount = x.total;
        this.scanChamps.push(`Montant payé : ${this.formatMontant(x.total)}`);
      } else if (Math.abs(Number(this.formData.amount) - x.total) > 0.001) {
        this.scanAvertissements.push(`Montant lu ${this.formatMontant(x.total)} — champ déjà saisi, non remplacé.`);
      }
    }

    // Date de paiement — la date du jour posée à l'ouverture n'est qu'un défaut.
    if (x.date) {
      if (!this.formData.date || this.formData.date === this.dateParDefaut) {
        this.formData.date = x.date;
        this.scanChamps.push(`Date de paiement : ${this.formatInputDate(x.date)}`);
      } else if (this.formData.date !== x.date) {
        this.scanAvertissements.push(`Date lue ${this.formatInputDate(x.date)} — date déjà modifiée, non remplacée.`);
      }
    }

    // N° de police / vignette / PV.
    if (x.invoiceNumber && !this.formData.documentNumber) {
      this.formData.documentNumber = x.invoiceNumber.slice(0, 100);
      this.scanChamps.push(`${this.getDocumentNumberLabel()} : ${this.formData.documentNumber}`);
    } else if (x.invoiceNumber && this.formData.documentNumber !== x.invoiceNumber) {
      this.scanAvertissements.push(`N° lu « ${x.invoiceNumber} » — champ déjà saisi, non remplacé.`);
    }

    // Notes — la description SEULE : le fournisseur a déjà son propre champ.
    if (x.description && !this.formData.notes) {
      this.formData.notes = x.description.slice(0, 1000);
      this.scanChamps.push('Notes reprises de la facture');
    }

    // Fournisseur : la liste déroulante n'accepte qu'un nom qu'elle connaît.
    this.nomFournisseurScanne = x.supplierName || '';
    this.rattacherFournisseurScanne();

    // Nouvelle date d'expiration : jamais devinée depuis une facture — seul le
    // document qui écrit lui-même sa validité donne lieu à une PROPOSITION.
    const validite = this.lireDateValidite(x);
    if (validite && validite !== this.formData.newExpiryDate) this.scanExpirationProposee = validite;

    this.verifierCoherence(x);
  }

  /**
   * Scan échoué : le message a déjà été montré par la brique. Le fichier est
   * souvent stocké malgré tout (panne IA) — on le rattache alors au
   * renouvellement, la saisie se fait à la main. Le formulaire reste utilisable
   * dans tous les cas, y compris quota atteint ou fonction désactivée.
   */
  onEchecScan(e: EchecScanFacture): void {
    if (!e.receiptUrl) return;
    this.reinitialiserScan();
    this.scanFait = true;
    this.scanConfiance = 'low';
    this.formData.documentUrl = e.receiptUrl;
    this.scanChamps.push('Justificatif rattaché au renouvellement');
    this.scanAvertissements.push('Document illisible par l\'IA : saisissez les informations à la main.');
  }

  /** Sélectionne le fournisseur lu s'il figure dans la liste filtrée, sinon le signale. */
  private rattacherFournisseurScanne(): void {
    const nom = this.nomFournisseurScanne;
    if (!nom || this.formData.provider) return;
    const norm = (s: string) => (s || '').toLowerCase().replace(/[^a-z0-9]/g, '');
    const cible = norm(nom);
    if (!cible) return;
    const trouve = this.filteredSuppliers.find(s => norm(s.name) === cible)
      || this.filteredSuppliers.find(s => norm(s.name) && (norm(s.name).includes(cible) || cible.includes(norm(s.name))));
    if (trouve) {
      this.formData.provider = trouve.name;
      this.scanFournisseurIntrouvable = '';
      this.scanChamps.push(`Fournisseur : ${trouve.name}`);
    } else {
      this.scanFournisseurIntrouvable = nom;
    }
  }

  /** Nombre de caractères lus après le mot d'ancrage — « du 01/01/2026 au 31/12/2026 » tient dedans. */
  private static readonly FENETRE_VALIDITE = 64;

  /**
   * Une quittance d'assurance ou de vignette porte parfois sa propre validité.
   * C'est le SEUL cas où l'IA peut dire quelque chose de la nouvelle échéance :
   * on lit la mention explicite dans la description et les lignes, et on se tait
   * s'il n'y en a pas. Deux règles tirées des formulations réelles :
   *  - une période en donne DEUX (« Période de validité du 01/01/2026 au
   *    31/12/2026 ») et c'est la PLUS TARDIVE qui est l'échéance, jamais la
   *    première venue ;
   *  - une date passée n'est pas une échéance à venir (« Police expirée le
   *    01/09/2025 ») : on ne propose rien plutôt que de faire poser en un clic
   *    une échéance déjà expirée. Le calcul par défaut (+1 an) garde la main.
   */
  private lireDateValidite(x: ExtractionFacture): string {
    const textes = [x.description || '', ...(x.items || []).map(i => i.label || '')].join(' ');
    const ancres = /valable|valide|validit|expir|jusqu/gi;
    const dates = /(\d{4})-(\d{2})-(\d{2})|(\d{1,2})[\/\-.](\d{1,2})[\/\-.](\d{4})/g;

    let meilleure = '';
    let ancre: RegExpExecArray | null;
    while ((ancre = ancres.exec(textes)) !== null) {
      const debut = ancre.index + ancre[0].length;
      const fenetre = textes.slice(debut, debut + DocumentRenewalPopupComponent.FENETRE_VALIDITE);
      dates.lastIndex = 0;
      let d: RegExpExecArray | null;
      while ((d = dates.exec(fenetre)) !== null) {
        const iso = d[1]
          ? this.isoSiEcheanceAVenir(+d[1], +d[2], +d[3])
          : this.isoSiEcheanceAVenir(+d[6], +d[5], +d[4]);
        // Tri lexicographique = tri chronologique sur du yyyy-MM-dd complété.
        if (iso > meilleure) meilleure = iso;
      }
    }
    return meilleure;
  }

  /**
   * Rend yyyy-MM-dd si le triplet lu est une vraie date du calendrier ET qu'elle
   * est strictement postérieure à aujourd'hui, sinon '' (candidat écarté).
   */
  private isoSiEcheanceAVenir(an: number, mois: number, jour: number): string {
    if (an < 1970 || mois < 1 || mois > 12 || jour < 1 || jour > 31) return '';
    const ms = Date.UTC(an, mois - 1, jour);
    const d = new Date(ms);
    // Un 31/02 glisserait au 3 mars : on écarte au lieu de proposer autre chose.
    if (d.getUTCFullYear() !== an || d.getUTCMonth() !== mois - 1 || d.getUTCDate() !== jour) return '';
    const auj = new Date();
    if (ms <= Date.UTC(auj.getFullYear(), auj.getMonth(), auj.getDate())) return '';
    return `${an}-${String(mois).padStart(2, '0')}-${String(jour).padStart(2, '0')}`;
  }

  /** L'utilisateur accepte la validité lue : elle remplace le calcul par défaut. */
  appliquerExpirationScannee(): void {
    if (!this.scanExpirationProposee) return;
    this.formData.newExpiryDate = this.scanExpirationProposee;
    this.scanChamps.push(`Nouvelle date d'expiration : ${this.formatInputDate(this.scanExpirationProposee)}`);
    this.scanExpirationProposee = '';
  }

  /** Le document scanné ressemble-t-il à ce qu'on renouvelle ? On avertit sans bloquer. */
  private verifierCoherence(x: ExtractionFacture): void {
    if (x.isCreditNote) {
      this.scanAvertissements.push('Avoir fournisseur détecté : ce document est un remboursement, pas un paiement.');
    }

    const attendues = this.categoriesAttendues(this.document?.type);
    if (x.category && x.category !== 'other' && !attendues.includes(x.category)) {
      this.scanAvertissements.push(
        `Document lu comme « ${this.libelleCategorie(x.category)} » alors que vous renouvelez ${this.getTypeLabel(this.document?.type)} — vérifiez le fichier.`
      );
    }

    const plaqueVehicule = this.document?.vehiclePlate || '';
    if (x.vehiclePlate && plaqueVehicule) {
      const norm = (s: string) => (s || '').toLowerCase().replace(/[^a-z0-9]/g, '');
      const lue = norm(x.vehiclePlate);
      const attendue = norm(plaqueVehicule);
      if (lue && attendue && !lue.includes(attendue) && !attendue.includes(lue)) {
        this.scanAvertissements.push(
          `Plaque détectée « ${x.vehiclePlate} » — différente de ${plaqueVehicule}. Le renouvellement reste sur ce véhicule.`
        );
      }
    }
  }

  private categoriesAttendues(docType: string | undefined): string[] {
    switch (docType) {
      case 'insurance': return ['insurance'];
      case 'tax': return ['tax'];
      case 'technical_inspection': return ['maintenance', 'repair'];
      // Carte grise, autorisation de transport : taxes et frais administratifs.
      default: return ['tax', 'insurance'];
    }
  }

  private libelleCategorie(code: string): string {
    const labels: { [key: string]: string } = {
      fuel: 'Carburant', maintenance: 'Entretien', repair: 'Réparation',
      insurance: 'Assurance', tax: 'Vignette', toll: 'Péage',
      parking: 'Stationnement', fine: 'Amende', other: 'Autre',
      credit_note: 'Avoir fournisseur'
    };
    return labels[code] || code;
  }

  scanConfianceLabel(): string {
    return ({ high: 'élevée', medium: 'moyenne', low: 'faible' } as { [key: string]: string })[this.scanConfiance] || this.scanConfiance;
  }

  private formatMontant(montant: number): string {
    return montant.toLocaleString('fr-FR', { minimumFractionDigits: 2, maximumFractionDigits: 3 }) + ' ' + this.currencyCode;
  }

  close(): void {
    this.closed.emit();
  }

  onSubmit(): void {
    // Le montant est facultatif (recette client du 26/08/2026) : changer la
    // date d'une vignette sans en connaître le prix est un cas réel.
    if (!this.document || !this.formData.newExpiryDate) return;

    this.saving = true;

    // Use renewDocument API which creates cost AND updates expiry date
    const renewRequest = {
      vehicleId: this.document.vehicleId,
      documentType: this.document.type,
      amount: this.formData.amount,
      paymentDate: this.formData.date,
      newExpiryDate: this.formData.newExpiryDate,
      documentNumber: this.formData.documentNumber || undefined,
      provider: this.formData.provider || undefined,
      notes: this.formData.notes || undefined,
      // Quittance scannée : le serveur la range en ReceiptUrl du coût créé.
      documentUrl: this.formData.documentUrl || undefined
    };

    this.apiService.renewDocument(this.document.vehicleId, renewRequest).subscribe({
      next: (result) => {
        this.saved.emit({
          costId: result.costId,
          newExpiryDate: this.formData.newExpiryDate,
          documentNumber: this.formData.documentNumber,
          documentType: this.document?.type,
          message: result.message
        });
        this.saving = false;
      },
      error: (err) => {
        console.error('Error renewing document:', err);
        this.saving = false;
        // Un 400 porte le refus métier en français (saisie trop longue, etc.).
        alert((err?.status === 400 && err?.error?.message) || 'Erreur lors du renouvellement. Veuillez réessayer.');
      }
    });
  }
}
