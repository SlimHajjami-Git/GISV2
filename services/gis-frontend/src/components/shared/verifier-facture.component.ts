import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ResultatScanFacture } from './scan-facture.component';

/**
 * Panneau « Vérifier la facture » — la revue AVANT enregistrement d'une facture
 * scannée : les champs pré-remplis par l'IA, le détail ligne par ligne, et
 * l'aperçu du document.
 *
 * Né le 29/09/2026 de l'écran Dépenses, seul à le porter. Entretien et
 * Réparations avaient leur propre traitement : le scan remplissait directement
 * leur formulaire et n'affichait qu'un résumé de ce qui avait été lu — les
 * lignes de la facture, pourtant extraites, n'étaient jamais montrées. Demande
 * de Slim : « ça doit donner la même interface que sur dépense, avec les lignes
 * de la facture et les champs aussi ».
 *
 * Le composant est PRÉSENTATIONNEL : il ne connaît ni dépense, ni entretien, ni
 * réparation, n'appelle aucune API et n'enregistre rien. Il édite le modèle qu'on
 * lui confie (ngModel écrit dans ses propriétés) et émet `valider` ; l'écran hôte
 * décide de ce qu'il en fait. C'est ce qui permet à l'entretien de garder son
 * rapprochement ligne → modèle d'entretien, que ce panneau ne saurait pas faire.
 */

/** Une ligne facturée, telle qu'elle est relue et corrigée dans le panneau. */
export interface LigneRevueFacture {
  label: string;
  amount: number;
  category: string;
}

/** Ce que le panneau affiche et laisse corriger. Les champs sont modifiés EN PLACE. */
export interface RevueFacture {
  vehicleId: string;
  category: string;
  date: string;
  amount: number;
  /** L'IA a lu une facture négative (avoir fournisseur). */
  creditNote: boolean;
  supplierName: string;
  invoiceNumber: string;
  description: string;
  /** Plaque lue sur le document, même si aucun véhicule ne correspond. */
  vehiclePlate: string;
  /** high | medium | low, tel que rendu par l'IA. */
  confidence: string;
  receiptUrl: string;
  items: LigneRevueFacture[];
}

export interface VehiculeRevue { id: number | string; plate: string; name: string; }
export interface CategorieRevue { value: string; label: string; }

@Component({
  selector: 'app-verifier-facture',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
      <div class="sm-overlay" (click)="annuler.emit()">
        <div class="sm-card" (click)="$event.stopPropagation()">

          <!-- En-tête -->
          <div class="sm-head">
            <div class="sm-head-icon">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M3 7V5a2 2 0 0 1 2-2h2M17 3h2a2 2 0 0 1 2 2v2M21 17v2a2 2 0 0 1-2 2h-2M7 21H5a2 2 0 0 1-2-2v-2"/>
                <path d="M12 8l1 2.2 2.2 1-2.2 1L12 14.4l-1-2.2-2.2-1 2.2-1z" fill="currentColor" stroke="none"/>
              </svg>
            </div>
            <div class="sm-head-txt">
              <h3>Vérifier la facture</h3>
              <p>Champs pré-remplis par l'IA — vérifiez et corrigez avant d'enregistrer.</p>
            </div>
            @if (modele.confidence) {
              <span class="sm-conf sm-conf-{{ modele.confidence }}">
                <i class="sm-dot"></i>{{ libelleConfiance() }}
              </span>
            }
            <button class="sm-close" (click)="annuler.emit()" aria-label="Fermer">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
            </button>
          </div>

          <!-- Corps : champs + aperçu du document -->
          <div class="sm-body">
            <div class="sm-fields">
              <div class="sm-grid">
                <div class="sm-field">
                  <label class="sm-label">Véhicule <span class="sm-req">*</span></label>
                  <select class="sm-input" [(ngModel)]="modele.vehicleId" [disabled]="vehiculeVerrouille">
                    <option value="">Sélectionner…</option>
                    @for (v of vehicules; track v.id) { <option [value]="v.id">{{ v.plate }} — {{ v.name }}</option> }
                  </select>
                  @if (vehiculeVerrouille) {
                    <small class="sm-hint">Facture rattachée à ce véhicule — fermez et rouvrez sur un autre pour changer.</small>
                    @if (modele.vehiclePlate && !memePlaqueQueLeVehicule()) {
                      <small class="sm-warn">Plaque lue « {{ modele.vehiclePlate }} » — différente : vérifiez que la facture concerne bien ce véhicule.</small>
                    }
                  } @else if (modele.vehiclePlate && !modele.vehicleId) {
                    <small class="sm-warn">Plaque détectée « {{ modele.vehiclePlate }} » — introuvable, choisissez le véhicule.</small>
                  }
                </div>
                <div class="sm-field">
                  <label class="sm-label">Catégorie <span class="sm-req">*</span></label>
                  <select class="sm-input" [(ngModel)]="modele.category">
                    @for (c of categories; track c.value) { <option [value]="c.value">{{ c.label }}</option> }
                  </select>
                  @if (messageAvoirQuitte) {
                    <small class="sm-warn">Facture négative : hors « Avoir fournisseur », le montant compte comme une dépense.</small>
                  }
                </div>
                <div class="sm-field">
                  <label class="sm-label">Date <span class="sm-req">*</span></label>
                  <input class="sm-input" type="date" [(ngModel)]="modele.date">
                </div>
                <div class="sm-field">
                  <label class="sm-label">Montant TTC <span class="sm-req">*</span></label>
                  <div class="sm-amount">
                    <input class="sm-input sm-input-amount" type="number" step="0.001" min="0" [(ngModel)]="modele.amount" placeholder="0.000">
                    <span class="sm-currency">{{ devise }}</span>
                  </div>
                  @if (montantObligatoire && montantInvalide()) {
                    <small class="sm-warn">Montant supérieur à zéro requis.</small>
                  }
                </div>
                @if (messageAvoirDetecte) {
                  <div class="sm-field sm-field-full">
                    <small class="sm-warn">Facture négative détectée : enregistrée comme avoir fournisseur, déduite des coûts.</small>
                  </div>
                }
                <div class="sm-field">
                  <label class="sm-label">Fournisseur</label>
                  <input class="sm-input" type="text" [(ngModel)]="modele.supplierName" placeholder="Garage, station, assureur…">
                </div>
                <div class="sm-field">
                  <label class="sm-label">N° facture</label>
                  <input class="sm-input" type="text" [(ngModel)]="modele.invoiceNumber" placeholder="Ex : FA-2026-0042">
                </div>
                <div class="sm-field sm-field-full">
                  <label class="sm-label">Description</label>
                  <textarea class="sm-input" rows="2" [(ngModel)]="modele.description" placeholder="Prestations, pièces, carburant…"></textarea>
                </div>

                <!-- Détail de la facture : lignes extraites par l'IA, éditables.
                     Enregistrées AVEC la dépense (une facture = une seule ligne
                     dans la liste) et visibles dans le panneau de la dépense. -->
                <div class="sm-field sm-field-full sm-items">
                  <div class="sm-items-head">
                    <label class="sm-label">
                      Détail de la facture
                      @if (modele.items.length > 0) { <span class="sm-count">{{ modele.items.length }}</span> }
                    </label>
                    @if (modele.items.length > 0) {
                      <span class="sm-items-hint">Visible en ouvrant la dépense</span>
                    }
                  </div>

                  @if (modele.items.length > 0) {
                    <div class="sm-items-tbl">
                      <div class="sm-item-cols">
                        <span>Désignation</span>
                        <span>Catégorie</span>
                        <span class="sm-col-amt">Montant</span>
                        <span></span>
                      </div>

                      @for (it of modele.items; track $index) {
                        <div class="sm-item-row">
                          <input class="sm-input sm-input-xs sm-item-label" type="text" [(ngModel)]="it.label" placeholder="Désignation…">
                          <select class="sm-input sm-input-xs sm-item-cat" [(ngModel)]="it.category">
                            @for (c of categories; track c.value) { <option [value]="c.value">{{ c.label }}</option> }
                          </select>
                          <div class="sm-amount sm-item-amt">
                            <input class="sm-input sm-input-xs" type="number" step="0.001" min="0" [(ngModel)]="it.amount" placeholder="0.000">
                            <span class="sm-currency sm-currency-xs">{{ devise }}</span>
                          </div>
                          <button class="sm-item-del" (click)="retirerLigne($index)" title="Supprimer la ligne">
                            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
                          </button>
                        </div>
                      }

                      <div class="sm-items-tblfoot">
                        <button class="sm-add-line" (click)="ajouterLigne()">+ Ajouter une ligne</button>
                        <span class="sm-sum" [class.sm-sum-warn]="sommeDiffere()">
                          Somme : <strong>{{ sommeLignes() | number:'1.3-3' }} {{ devise }}</strong>
                          @if (sommeDiffere()) { <em>≠ total {{ modele.amount | number:'1.3-3' }}</em> }
                        </span>
                      </div>
                    </div>
                  } @else {
                    <div class="sm-items-empty">
                      <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">
                        <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/>
                        <line x1="8" y1="13" x2="16" y2="13"/><line x1="8" y1="17" x2="13" y2="17"/>
                      </svg>
                      <p>L'IA n'a pas détecté de lignes détaillées sur cette facture.<br>Vous pouvez les saisir manuellement — elles resteront attachées à cette dépense.</p>
                      <button class="sm-add-line" (click)="ajouterLigne()">+ Ajouter des lignes</button>
                    </div>
                  }
                </div>
              </div>
            </div>

            @if (modele.receiptUrl) {
              <aside class="sm-doc">
                <span class="sm-doc-title">Document scanné</span>
                @if (isPdf(modele.receiptUrl)) {
                  <a [href]="modele.receiptUrl" target="_blank" class="sm-doc-pdf">
                    <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">
                      <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/>
                    </svg>
                    <span>Ouvrir le PDF</span>
                  </a>
                } @else {
                  <a class="sm-doc-img" [href]="modele.receiptUrl" target="_blank" title="Agrandir">
                    <img [src]="modele.receiptUrl" alt="Facture scannée" />
                  </a>
                }
                <span class="sm-doc-hint">Cliquez pour agrandir</span>
              </aside>
            }
          </div>

          <!-- Pied : note + actions -->
          <div class="sm-foot">
            <span class="sm-foot-note">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>
              Rien n'est enregistré sans votre validation
            </span>
            <div class="sm-foot-actions">
              <button class="sm-btn-ghost" (click)="annuler.emit()">Annuler</button>
              <button class="sm-btn-primary" (click)="valider.emit(modele)" [disabled]="!peutValider()">
                @if (!enregistrement) {
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
                }
                {{ enregistrement ? 'Enregistrement…' : libelleValider }}
              </button>
            </div>
          </div>
        </div>
      </div>
  `,
  styles: [`
.sm-overlay {
  position: fixed;
  top: 42px; left: 0; right: 0; bottom: 0;  /* sous la navbar, comme les autres overlays */
  background: rgba(15, 23, 42, 0.55);
  backdrop-filter: blur(3px);
  display: flex; align-items: center; justify-content: center;
  z-index: 1051;
  padding: 20px;
  animation: smFade .18s ease-out;
}
@keyframes smFade { from { opacity: 0; } to { opacity: 1; } }

.sm-card {
  background: #ffffff;
  border-radius: 16px;
  width: min(860px, 100%);
  max-height: calc(100vh - 100px);
  display: flex; flex-direction: column;
  box-shadow: 0 24px 64px -12px rgba(15, 23, 42, .35), 0 2px 8px rgba(15, 23, 42, .12);
  overflow: hidden;
  animation: smRise .22s cubic-bezier(.21, 1.02, .55, 1);
}
@keyframes smRise { from { opacity: 0; transform: translateY(14px) scale(.985); } to { opacity: 1; transform: none; } }

/* En-tête */
.sm-head {
  display: flex; align-items: center; gap: 12px;
  padding: 18px 20px 16px;
  border-bottom: 1px solid #eef2f7;
}
.sm-head-icon {
  flex: 0 0 auto;
  width: 40px; height: 40px; border-radius: 12px;
  display: grid; place-items: center;
  color: #ffffff;
  background: linear-gradient(135deg, #8b5cf6, #6d28d9);
  box-shadow: 0 6px 14px -4px rgba(109, 40, 217, .5);
}
.sm-head-txt { flex: 1 1 auto; min-width: 0; }
.sm-head-txt h3 { margin: 0; font-size: 16px; font-weight: 700; color: #0f172a; letter-spacing: -.01em; }
.sm-head-txt p { margin: 2px 0 0; font-size: 12.5px; color: #64748b; }

.sm-conf {
  flex: 0 0 auto;
  display: inline-flex; align-items: center; gap: 6px;
  padding: 4px 10px; border-radius: 999px;
  font-size: 11px; font-weight: 700; letter-spacing: .02em;
}
.sm-dot { width: 6px; height: 6px; border-radius: 50%; background: currentColor; }
.sm-conf-high   { background: #ecfdf5; color: #047857; border: 1px solid #a7f3d0; }
.sm-conf-medium { background: #fefce8; color: #a16207; border: 1px solid #fde68a; }
.sm-conf-low    { background: #fef2f2; color: #b91c1c; border: 1px solid #fecaca; }

.sm-close {
  flex: 0 0 auto;
  width: 30px; height: 30px; border-radius: 8px;
  display: grid; place-items: center;
  border: none; background: transparent; color: #94a3b8; cursor: pointer;
  transition: background .15s, color .15s;
}
.sm-close:hover { background: #f1f5f9; color: #334155; }

/* Corps */
.sm-body {
  display: flex; gap: 20px;
  padding: 18px 20px;
  overflow-y: auto;
}
.sm-fields { flex: 1 1 auto; min-width: 0; }
.sm-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 14px 14px;
}
.sm-field { display: flex; flex-direction: column; min-width: 0; }
.sm-field-full { grid-column: 1 / -1; }

.sm-label {
  font-size: 11px; font-weight: 700; letter-spacing: .05em; text-transform: uppercase;
  color: #64748b; margin-bottom: 6px;
}
.sm-req { color: #7c3aed; }

.sm-input {
  width: 100%; box-sizing: border-box;
  padding: 9px 12px;
  border: 1px solid #e2e8f0; border-radius: 10px;
  font-size: 13.5px; color: #0f172a; background: #ffffff;
  transition: border-color .15s, box-shadow .15s;
  font-family: inherit;
}
.sm-input::placeholder { color: #b6c2d4; }
.sm-input:focus {
  outline: none;
  border-color: #a78bfa;
  box-shadow: 0 0 0 3px rgba(124, 58, 237, .14);
}
select.sm-input { cursor: pointer; }
textarea.sm-input { resize: vertical; min-height: 56px; }

/* Montant : champ mis en avant + devise intégrée */
.sm-amount { position: relative; }
.sm-input-amount { font-weight: 700; font-size: 15px; padding-right: 54px; }
.sm-currency {
  position: absolute; right: 12px; top: 50%; transform: translateY(-50%);
  font-size: 12px; font-weight: 700; color: #7c3aed;
  pointer-events: none;
}

.sm-warn { display: block; margin-top: 5px; font-size: 11.5px; color: #b45309; }
/* Information neutre (véhicule imposé par l'écran) — ce n'est pas un avertissement. */
.sm-hint { display: block; margin-top: 5px; font-size: 11.5px; color: #64748b; }
.sm-input:disabled { background: #f1f5f9; color: #475569; cursor: not-allowed; }

/* Détail de la facture (lignes décomposables) */
.sm-items {
  padding: 12px;
  background: #faf9ff;
  border: 1px solid #ece9fb;
  border-radius: 12px;
}
.sm-items-head {
  display: flex; align-items: center; justify-content: space-between; gap: 10px;
  flex-wrap: wrap;
  margin-bottom: 10px;
}
.sm-items-head .sm-label { margin-bottom: 0; display: inline-flex; align-items: center; gap: 7px; }
.sm-count {
  display: inline-grid; place-items: center;
  min-width: 18px; height: 18px; padding: 0 5px; border-radius: 999px;
  background: #7c3aed; color: #fff;
  font-size: 10.5px; font-weight: 800; letter-spacing: 0;
}

.sm-switch {
  display: inline-flex; align-items: center; gap: 8px;
  font-size: 12px; font-weight: 600; color: #475569; cursor: pointer;
  user-select: none;
}
.sm-switch input { display: none; }
.sm-switch-track {
  width: 32px; height: 18px; border-radius: 999px;
  background: #e2e8f0; position: relative; transition: background .18s;
  flex: 0 0 auto;
}
.sm-switch-thumb {
  position: absolute; top: 2px; left: 2px;
  width: 14px; height: 14px; border-radius: 50%;
  background: #ffffff; box-shadow: 0 1px 3px rgba(15,23,42,.3);
  transition: transform .18s;
}
.sm-switch input:checked + .sm-switch-track { background: #7c3aed; }
.sm-switch input:checked + .sm-switch-track .sm-switch-thumb { transform: translateX(14px); }

/* Tableau des lignes */
.sm-items-tbl {
  background: #ffffff;
  border: 1px solid #e9e4f8;
  border-radius: 10px;
  overflow: hidden;
}
.sm-item-cols, .sm-item-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 128px 118px 26px;
  gap: 8px; align-items: center;
  padding: 7px 10px;
}
.sm-item-grid-split { grid-template-columns: 18px minmax(0, 1fr) 128px 118px 26px; }

.sm-item-cols {
  padding-top: 8px; padding-bottom: 6px;
  background: #faf9ff;
  border-bottom: 1px solid #efeafc;
}
.sm-item-cols span {
  font-size: 10px; font-weight: 700; letter-spacing: .06em; text-transform: uppercase;
  color: #94a3b8;
}
.sm-col-amt { padding-left: 2px; }

.sm-item-row { border-bottom: 1px solid #f4f1fd; }
.sm-item-row:last-of-type { border-bottom: none; }
.sm-item-row:hover { background: #fcfbff; }
.sm-item-off { opacity: .45; }
.sm-item-check { width: 15px; height: 15px; accent-color: #7c3aed; cursor: pointer; }

.sm-input-xs { padding: 7px 10px; font-size: 12.5px; border-radius: 8px; border-color: #ece8f9; }
.sm-item-amt .sm-input-xs { padding-right: 44px; font-weight: 600; }
.sm-currency-xs { font-size: 10.5px; right: 10px; }

.sm-item-del {
  width: 26px; height: 26px; border-radius: 7px;
  display: grid; place-items: center;
  border: none; background: transparent; color: #cbd5e1; cursor: pointer;
  transition: background .15s, color .15s;
}
.sm-item-del:hover { background: #fee2e2; color: #dc2626; }

.sm-items-tblfoot {
  display: flex; align-items: center; justify-content: space-between; gap: 10px;
  flex-wrap: wrap;
  padding: 8px 10px;
  background: #faf9ff;
  border-top: 1px solid #efeafc;
}
.sm-add-line {
  border: 1px dashed #c4b5fd; background: transparent; color: #7c3aed;
  padding: 6px 12px; border-radius: 8px;
  font-size: 12px; font-weight: 600; cursor: pointer;
  transition: background .15s, border-color .15s;
}
.sm-add-line:hover { background: #f5f3ff; border-color: #a78bfa; }

.sm-sum { font-size: 12px; color: #475569; }
.sm-sum strong { color: #0f172a; }
.sm-sum em { font-style: normal; margin-left: 6px; color: #b45309; font-weight: 600; }
.sm-sum-warn { color: #b45309; }

.sm-split-note {
  display: flex; align-items: flex-start; gap: 7px;
  margin: 10px 0 0; padding: 8px 10px;
  font-size: 11.5px; line-height: 1.45; color: #5b21b6;
  background: #f5f3ff; border-radius: 8px;
}
.sm-split-note svg { flex: 0 0 auto; margin-top: 1px; }

/* État vide : l'IA n'a pas trouvé de lignes */
.sm-items-empty {
  display: flex; flex-direction: column; align-items: center; gap: 8px;
  padding: 18px 12px;
  background: #ffffff;
  border: 1px dashed #ddd6fe;
  border-radius: 10px;
  color: #a78bfa;
  text-align: center;
}
.sm-items-empty p { margin: 0; font-size: 12px; line-height: 1.5; color: #64748b; }
.sm-items-hint { font-size: 11px; color: #94a3b8; font-style: italic; }
/* Aperçu du document */
.sm-doc {
  flex: 0 0 220px;
  display: flex; flex-direction: column; gap: 8px;
  padding: 12px;
  background: #f8fafc;
  border: 1px solid #eef2f7; border-radius: 12px;
  align-self: flex-start;
}
.sm-doc-title {
  font-size: 11px; font-weight: 700; letter-spacing: .05em; text-transform: uppercase;
  color: #64748b;
}
.sm-doc-img { display: block; border-radius: 8px; overflow: hidden; border: 1px solid #e2e8f0; background: #fff; }
.sm-doc-img img {
  display: block; width: 100%; max-height: 300px; object-fit: contain;
  cursor: zoom-in; transition: transform .2s ease;
}
.sm-doc-img:hover img { transform: scale(1.025); }
.sm-doc-pdf {
  display: flex; flex-direction: column; align-items: center; gap: 8px;
  padding: 22px 12px;
  border-radius: 8px; border: 1px dashed #cbd5e1; background: #ffffff;
  color: #475569; text-decoration: none; font-size: 13px; font-weight: 600;
  transition: border-color .15s, color .15s;
}
.sm-doc-pdf:hover { border-color: #a78bfa; color: #6d28d9; }
.sm-doc-hint { font-size: 11px; color: #94a3b8; text-align: center; }

/* Pied */
.sm-foot {
  display: flex; align-items: center; justify-content: space-between; gap: 12px;
  padding: 14px 20px;
  border-top: 1px solid #eef2f7;
  background: #fbfcfe;
}
.sm-foot-note {
  display: inline-flex; align-items: center; gap: 6px;
  font-size: 12px; color: #64748b;
}
.sm-foot-actions { display: flex; gap: 10px; }

.sm-btn-ghost {
  padding: 9px 16px; border-radius: 10px;
  border: 1px solid #e2e8f0; background: #ffffff;
  font-size: 13px; font-weight: 600; color: #475569; cursor: pointer;
  transition: background .15s, border-color .15s;
}
.sm-btn-ghost:hover { background: #f8fafc; border-color: #cbd5e1; }

.sm-btn-primary {
  display: inline-flex; align-items: center; gap: 7px;
  padding: 9px 18px; border-radius: 10px; border: none;
  background: linear-gradient(135deg, #7c3aed, #6d28d9);
  color: #ffffff; font-size: 13px; font-weight: 700; cursor: pointer;
  box-shadow: 0 6px 14px -4px rgba(109, 40, 217, .45);
  transition: transform .15s, box-shadow .15s, opacity .15s;
}
.sm-btn-primary:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 9px 18px -4px rgba(109, 40, 217, .5);
}
.sm-btn-primary:disabled { opacity: .45; cursor: not-allowed; box-shadow: none; }

@media (max-width: 720px) {
  .sm-body { flex-direction: column; }
  .sm-grid { grid-template-columns: 1fr; }
  .sm-doc { flex: 1 1 auto; width: 100%; box-sizing: border-box; }
  .sm-foot { flex-direction: column-reverse; align-items: stretch; }
  .sm-foot-actions { justify-content: stretch; }
  .sm-foot-actions button { flex: 1; }
  .sm-foot-note { justify-content: center; }
  /* Lignes : désignation pleine largeur, catégorie+montant en dessous */
  .sm-item-cols { display: none; }
  .sm-item-row { grid-template-columns: minmax(0, 1fr) 108px 26px; }
  .sm-item-row.sm-item-grid-split { grid-template-columns: 18px minmax(0, 1fr) 108px 26px; }
  .sm-item-cat { grid-column: 1 / -2; }
  .sm-item-row.sm-item-grid-split .sm-item-cat { grid-column: 2 / -2; }
}
  `]
})
export class VerifierFactureComponent {
  /** Modèle édité EN PLACE : l'hôte garde la référence et la relit après `valider`. */
  @Input({ required: true }) modele!: RevueFacture;
  @Input() vehicules: VehiculeRevue[] = [];
  @Input() categories: CategorieRevue[] = [];
  @Input() devise = 'TND';
  @Input() libelleValider = 'Enregistrer';
  /** Vrai pendant l'enregistrement : le bouton se grise et change de libellé. */
  @Input() enregistrement = false;
  /**
   * Catégorie qui représente un avoir fournisseur sur CET écran. Vide = l'écran
   * n'a pas cette notion, les deux avertissements d'avoir ne s'affichent jamais.
   */
  @Input() categorieAvoir = '';
  /**
   * Le véhicule est imposé par l'écran (Entretien : la fiche s'ouvre POUR un
   * véhicule). La liste reste visible — l'utilisateur doit voir sur quoi il
   * enregistre — mais elle est verrouillée, et la plaque lue sert d'alerte au
   * lieu de servir à choisir.
   */
  @Input() vehiculeVerrouille = false;
  /**
   * Un montant strictement positif est-il exigé pour valider ? Vrai sur Dépenses, où
   * le serveur refuse zéro. Faux sur Entretien et Réparations : là, le panneau ne fait
   * que préparer un formulaire, et une facture au total illisible — cas fréquent — doit
   * pouvoir passer pour être complétée à la main, comme avant le 29/09/2026.
   */
  @Input() montantObligatoire = true;

  @Output() valider = new EventEmitter<RevueFacture>();
  @Output() annuler = new EventEmitter<void>();

  isPdf(url: string): boolean { return (url || '').toLowerCase().endsWith('.pdf'); }

  libelleConfiance(): string {
    return ({ high: 'élevée', medium: 'moyenne', low: 'faible' } as Record<string, string>)[this.modele.confidence]
      || this.modele.confidence;
  }

  // ── Détail de la facture (lignes) ──────────────────────────────────────────
  sommeLignes(): number {
    return this.modele.items.reduce((s, it) => s + (Number(it.amount) || 0), 0);
  }

  /** Somme des lignes ≠ total facture (tolérance 0,01) → avertissement visuel. */
  sommeDiffere(): boolean {
    if (!this.modele.items.length || !this.modele.amount) return false;
    return Math.abs(this.sommeLignes() - Number(this.modele.amount)) > 0.01;
  }

  ajouterLigne(): void {
    this.modele.items.push({ label: '', amount: 0, category: this.modele.category || 'other' });
  }

  retirerLigne(i: number): void { this.modele.items.splice(i, 1); }

  // Montant strictement positif : le serveur refuse zéro et négatif (un avoir lu
  // par l'IA à -120 activait le bouton pour un enregistrement voué au refus ; il est
  // converti à la lecture en avoir fournisseur positif). Le motif s'affiche sous le
  // champ, sinon le bouton grisé reste inexpliqué.
  montantInvalide(): boolean {
    return !(Number(this.modele.amount) > 0);
  }

  /** Avoir détecté et catégorie conservée : le message ne ment pas si l'utilisateur la change. */
  get messageAvoirDetecte(): boolean {
    return !!this.categorieAvoir && this.modele.creditNote && this.modele.category === this.categorieAvoir;
  }

  /**
   * Avoir détecté mais catégorie changée : le montant, passé en valeur absolue, partirait
   * en DÉPENSE sans que rien ne le signale (relecture du 16/09/2026).
   */
  get messageAvoirQuitte(): boolean {
    return !!this.categorieAvoir && this.modele.creditNote && this.modele.category !== this.categorieAvoir;
  }

  /** Plaque lue == plaque du véhicule imposé ? Comparaison tolérante (espaces, casse). */
  memePlaqueQueLeVehicule(): boolean {
    const v = this.vehicules.find(x => String(x.id) === String(this.modele.vehicleId));
    if (!v) return true;   // véhicule inconnu de la liste : pas d'alerte hasardeuse
    const norm = (s: string) => (s || '').toLowerCase().replace(/[^a-z0-9]/g, '');
    return norm(v.plate) === norm(this.modele.vehiclePlate);
  }

  peutValider(): boolean {
    return !!this.modele.vehicleId
      && (!this.montantObligatoire || !this.montantInvalide())
      && !this.enregistrement;
  }
}

/**
 * Ce que l'IA a lu → ce que le panneau affiche. `vehicleId` est fourni par l'écran,
 * qui seul sait rapprocher une plaque de SON parc ; vide, l'utilisateur choisira.
 *
 * Les `null` de l'extraction deviennent des chaînes vides : un `null` dans un
 * ngModel affiche « null » dans le champ.
 */
export function revueDepuisScan(res: ResultatScanFacture, vehicleId = ''): RevueFacture {
  const x = res.extraction;
  return {
    vehicleId,
    category: x.isCreditNote ? 'credit_note' : (x.category || 'other'),
    date: x.date || new Date().toISOString().split('T')[0],
    amount: x.total ?? 0,
    creditNote: x.isCreditNote,
    supplierName: x.supplierName || '',
    invoiceNumber: x.invoiceNumber || '',
    description: x.descriptionComplete || '',
    vehiclePlate: x.vehiclePlate || '',
    confidence: x.confidence || '',
    receiptUrl: res.receiptUrl,
    // Copie : le panneau ajoute et retire des lignes, l'extraction d'origine ne
    // doit pas bouger (l'écran peut vouloir comparer au document).
    items: (x.items || []).map(l => ({ label: l.label, amount: l.amount, category: l.category }))
  };
}

/**
 * Ce que l'utilisateur a relu → la même forme que le scan, pour les écrans qui
 * savent déjà remplir leur formulaire depuis un `ResultatScanFacture` (Entretien
 * rapproche chaque ligne d'un modèle, Réparations répartit en pièces et main
 * d'œuvre). Ils gardent ainsi leur logique, appliquée aux valeurs CORRIGÉES.
 *
 * La plaque est laissée telle que lue : l'écran reçoit le véhicule choisi à part,
 * et n'a pas à re-deviner ce que l'utilisateur vient de trancher à la main.
 */
export function scanDepuisRevue(modele: RevueFacture, origine: ResultatScanFacture): ResultatScanFacture {
  return {
    ...origine,
    receiptUrl: modele.receiptUrl,
    extraction: {
      ...origine.extraction,
      supplierName: modele.supplierName || null,
      invoiceNumber: modele.invoiceNumber || null,
      date: modele.date || null,
      total: Number(modele.amount) || null,
      category: modele.category || null,
      descriptionComplete: modele.description || '',
      confidence: modele.confidence || null,
      isCreditNote: modele.creditNote,
      items: modele.items
        .filter(l => (l.label || '').trim() || Number(l.amount) > 0)
        .map(l => ({ label: (l.label || '').trim(), amount: Number(l.amount) || 0, category: l.category || 'other' }))
    }
  };
}
