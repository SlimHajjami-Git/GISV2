import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { AppLayoutComponent } from './shared/app-layout.component';
import { USER_PREF_PIPES } from '../pipes/user-preference-pipes';
import { ApiService, RepairDto } from '../services/api.service';
import { UserPreferencesService } from '../services/user-preferences.service';
import { PdfExportService, PdfGroup } from '../services/pdf-export.service';
import {
  ScanFactureComponent, ResultatScanFacture, EchecScanFacture, LigneFactureScannee
} from './shared/scan-facture.component';
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
  /** Type d'intervention (electrique | mecanique | freinage | pneumatique | carrosserie | autre), optionnel. */
  repairType: string | null;
  /**
   * Sinistre à l'origine de la réparation (phase 5 du dossier, migration 049).
   * Sans lui, l'écran laissait cliquer Supprimer sur une ligne que le serveur refuse
   * de supprimer : la fenêtre se fermait, la ligne restait, le client y voyait un bug.
   */
  accidentEventId?: number | null;
  parts: RepairPart[];
}

/** Types d'intervention (rapport « Fréquence des réparations », 04/09/2026). Valeurs = colonne repairs.repair_type. */
const REPAIR_TYPES: { value: string; label: string }[] = [
  { value: 'electrique', label: 'Électrique' },
  { value: 'mecanique', label: 'Mécanique' },
  { value: 'freinage', label: 'Freinage' },
  { value: 'pneumatique', label: 'Pneumatique' },
  { value: 'carrosserie', label: 'Carrosserie' },
  { value: 'autre', label: 'Autres' }
];

interface Vehicle {
  id: number;
  name: string;
  plateNumber: string;
  mileage: number;
}

/**
 * Ce que le scan a proposé, montré en tête du formulaire : la confiance de la
 * lecture et surtout ce qui n'a PAS pu être repris (plaque inconnue, fournisseur
 * absent, lignes sans case dans ce formulaire). Rien n'est enregistré tant que
 * l'utilisateur n'a pas validé.
 */
interface ScanInfo {
  /** high | medium | low ('' quand l'analyse a échoué). */
  confidence: string;
  /** Vrai quand le document est joint mais que l'IA n'a rien pu lire. */
  echec: boolean;
  plaque: string;
  vehiculeTrouve: boolean;
  fournisseur: string;
  fournisseurTrouve: boolean;
  /** Total lu sur la facture (null si illisible) — sert à montrer l'écart. */
  totalFacture: number | null;
  lignesIgnorees: number;
  avoir: boolean;
  receiptUrl: string;
}

/**
 * Le lien du document scanné est rangé dans les notes : la réparation n'a pas de
 * champ justificatif en base (voir remarques de la recette du 19/09/2026).
 */
const PREFIXE_JUSTIFICATIF = 'Justificatif : ';

/** Libellés de facture pris tels qu'ils viennent : sans accents ni casse pour les comparer. */
function sansAccents(s: string): string {
  return (s || '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');
}

/** Ligne de temps passé, pas une pièce montée sur le véhicule. */
const LIGNE_MAIN_OEUVRE = /(main\s*d\s*['’`]?\s*(oeuvre|œuvre)|\bm\.?\s?o\.?\b|forfait|diagnosti|deplacement|prestation|heure|taux horaire|service)/;

/** Ni pièce ni prestation : le formulaire n'a aucune case pour ces lignes. */
const LIGNE_HORS_POSTE = /(timbre|remise|escompte|acompte|arrondi|\btva\b|frais de dossier)/;

/** Plaque ou nom de fournisseur réduits à l'essentiel, pour le rapprochement. */
function cleRapprochement(s: string): string {
  return sansAccents(s).replace(/[^a-z0-9]/g, '');
}

/**
 * « 2026-08-03T12:00:00Z » → « 2026-08-03 ». Les réparations sont enregistrées avec une
 * HEURE (repairs.repair_date est un timestamp) et un <input type="date"> refuse tout ce
 * qui n'est pas yyyy-MM-dd : à la modification, le champ Date — pourtant obligatoire —
 * s'affichait VIDE. Découpage de la chaîne, jamais un passage par le fuseau du
 * navigateur qui décalerait la date d'un jour. Chaîne inutilisable → champ vide assumé.
 */
function dateSeule(d: string | null | undefined): string {
  const s = (d || '').trim();
  return /^\d{4}-\d{2}-\d{2}/.test(s) ? s.slice(0, 10) : '';
}

@Component({
  selector: 'app-repairs',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent, ScanFactureComponent, ...USER_PREF_PIPES],
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
          <select class="filter-select" [(ngModel)]="filterVehicle" (change)="onFiltreServeur()">
            <option value="">Tous les vehicules</option>
            <option *ngFor="let v of vehicles" [value]="v.id">{{ v.name }} - {{ v.plateNumber }}</option>
          </select>
          <select class="filter-select" [(ngModel)]="filterStatus" (change)="onFiltreServeur()">
            <option value="">Tous les statuts</option>
            <option value="pending">En attente</option>
            <option value="in_progress">En cours</option>
            <option value="completed">Terminée</option>
            <option value="cancelled">Annulée</option>
          </select>
          <!-- L'export porte sur TOUT l'ensemble filtré, pas sur la page affichée : le dire,
               sinon un tableau paginé laisse croire que le PDF s'arrête à ces 25 lignes. -->
          <button class="btn-export" (click)="exportPdf()" [disabled]="filteredRepairs.length === 0"
                  [title]="'Exporter un PDF groupé par véhicule — ' + filteredRepairs.length + ' réparation(s), toutes pages confondues'">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
              <polyline points="14 2 14 8 20 8"/>
              <line x1="16" y1="13" x2="8" y2="13"/>
              <line x1="16" y1="17" x2="8" y2="17"/>
              <polyline points="10 9 9 9 8 9"/>
            </svg>
            Exporter PDF
          </button>
          <!-- Même geste qu'à l'écran Dépenses : bouton et compteur de quota dans la
               barre d'actions (brique shared/scan-facture.component.ts).
               Le scan repart d'un formulaire VIERGE : tant qu'une fenêtre est ouverte le
               bouton est verrouillé. Le seul recouvrement visuel ne suffisait pas — le
               bouton restait dans l'ordre de TABULATION derrière la fenêtre, et un
               utilisateur au clavier perdait sa saisie sans un mot. -->
          <app-scan-facture [desactive]="fenetreOuverte"
                            raisonDesactivation="Fermez la fenêtre ouverte avant de scanner : le scan repart d'un formulaire vierge."
                            (scanne)="onFactureScannee($event)" (echec)="onEchecScan($event)"></app-scan-facture>
          <!-- Même verrou, exactement pour la même raison : ce bouton vit HORS de la
               fenêtre. L'ombre le cachait sans le sortir de l'ordre de TABULATION — une
               touche Tab (ou un clic dès la fenêtre refermée par erreur) rouvrait un
               formulaire vierge par-dessus la saisie en cours, perdue sans un mot. -->
          <button class="btn-add" (click)="openAddRepair()"
                  [disabled]="fenetreOuverte"
                  [title]="fenetreOuverte ? raisonFenetreOuverte : 'Nouvelle réparation'">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
            </svg>
            Nouvelle reparation
          </button>
        </div>

        <!-- Parc plus grand que ce que l'écran garde en mémoire : ce n'est pas un détail
             d'infobulle, cela change ce que le tableau et la recherche couvrent. -->
        <div class="avertissement-volume" *ngIf="!chargementComplet">
          <strong>Parc volumineux — {{ totalParc }} réparations au total.</strong>
          Seules les {{ repairs.length }} plus récentes sont chargées. {{ porteeChargementPartiel }}
          Choisissez un véhicule ou un statut : le sous-ensemble est alors rechargé en entier.
        </div>

        <!-- Stats Bar -->
        <div class="stats-bar">
          <div class="stat-item" [title]="infobulleTotal">
            <div class="stat-icon info">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ stats.totalRepairs }}</span>
              <span class="stat-label">{{ libelleTotal }}</span>
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
              <span class="stat-label">Terminées</span>
            </div>
          </div>
          <div class="stat-item" *ngIf="stats.cancelledRepairs > 0">
            <div class="stat-icon muted">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ stats.cancelledRepairs }}</span>
              <span class="stat-label">Annulées</span>
            </div>
          </div>
          <div class="stat-item" [title]="infobulleCout">
            <div class="stat-icon cost">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="12" y1="1" x2="12" y2="23"/><path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ stats.totalCost | appCurrency:0 }}</span>
              <span class="stat-label">{{ libelleCout }}</span>
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
                  Pièces ({{ currencyCode }})
                  <span class="sort-indicator">{{ getSortIndicator('partsCost') }}</span>
                </th>
                <th class="col-labor-cost sortable" [class.active]="sortColumn === 'laborCost'" (click)="toggleSort('laborCost')">
                  M. œuvre ({{ currencyCode }})
                  <span class="sort-indicator">{{ getSortIndicator('laborCost') }}</span>
                </th>
                <th class="col-total-cost sortable" [class.active]="sortColumn === 'totalCost'" (click)="toggleSort('totalCost')">
                  Total ({{ currencyCode }})
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
              <tr class="repair-row" *ngFor="let repair of getPageRepairs()" (click)="viewRepair(repair)">
                <td class="col-ref">
                  <div class="ref-cell">
                    <span class="repair-icon">🔧</span>
                    <span class="repair-ref">{{ repair.reference || '—' }}</span>
                  </div>
                  <!-- Dossier de sinistre, sous la référence : le tableau tient déjà dix colonnes
                       sur un écran de 1536 px, une colonne de plus ne passait pas. Texte court. -->
                  <span class="accident-badge" *ngIf="repair.accidentEventId"
                        [title]="'Réparation issue du dossier de sinistre #' + repair.accidentEventId">
                    🚗 Sinistre #{{ repair.accidentEventId }}
                  </span>
                </td>
                <td class="col-date">{{ repair.repairDate | date:'dd/MM/yyyy' }}</td>
                <td class="col-vehicle">
                  <div class="vehicle-cell">
                    <span class="vehicle-name">{{ repair.vehicleName }}</span>
                    <span class="vehicle-plate">{{ repair.vehiclePlate }}</span>
                  </div>
                </td>
                <td class="col-description">
                  <span class="type-tag" *ngIf="repair.repairType">{{ getRepairTypeLabel(repair.repairType) }}</span>
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
                  <!-- Les trois boutons de la ligne vivent eux aussi HORS de la fenêtre :
                       une tabulation y menait pendant une saisie, et « Détails » ou
                       « Modifier » remplaçait le formulaire en cours. Même verrou que le
                       bouton du scan (getter « fenetreOuverte »), même motif en infobulle. -->
                  <div class="actions-cell">
                    <button class="btn-action view" (click)="viewRepair(repair); $event.stopPropagation()"
                            [disabled]="fenetreOuverte"
                            [title]="fenetreOuverte ? raisonFenetreOuverte : 'Détails'">
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>
                      </svg>
                    </button>
                    <button class="btn-action edit" (click)="editRepair(repair); $event.stopPropagation()"
                            [disabled]="fenetreOuverte"
                            [title]="fenetreOuverte ? raisonFenetreOuverte : 'Modifier'">
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                        <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                      </svg>
                    </button>
                    <!-- Réparation née d'un sinistre : le serveur refuse la suppression (le retrait
                         passe par la phase 5 du dossier). Verrou et motif, comme l'écran Dépenses.
                         Le motif du sinistre passe AVANT celui de la fenêtre : il est définitif. -->
                    <button class="btn-action delete" (click)="confirmDelete(repair); $event.stopPropagation()"
                            [disabled]="fenetreOuverte || !!repair.accidentEventId"
                            [title]="repair.accidentEventId
                              ? 'Réparation du dossier de sinistre #' + repair.accidentEventId
                                + ' : videz le coût réel dans la phase 5 du dossier pour la retirer.'
                              : (fenetreOuverte ? raisonFenetreOuverte : 'Supprimer')">
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                      </svg>
                    </button>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>

          <!-- Le pied dit toujours sur COMBIEN de réparations porte ce qui est au-dessus :
               un tableau coupé en pages sans ce repère laissait croire que tout tenait là. -->
          <div class="pager" *ngIf="filteredRepairs.length > 0">
            <button type="button" (click)="allerPage(pageAffichage - 1)" [disabled]="pageAffichage <= 1" title="Page précédente">‹</button>
            <span>Page {{ pageAffichage }} / {{ totalPagesAffichage }} — {{ filteredRepairs.length }} réparation(s)</span>
            <button type="button" (click)="allerPage(pageAffichage + 1)" [disabled]="pageAffichage >= totalPagesAffichage" title="Page suivante">›</button>
          </div>

          <div class="empty-state" *ngIf="filteredRepairs.length === 0">
            <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
              <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>
            </svg>
            <h3>{{ chargementEnCours ? 'Chargement des reparations...' : 'Aucune reparation trouvee' }}</h3>
            <p *ngIf="!chargementEnCours">Modifiez vos filtres ou ajoutez une nouvelle reparation</p>
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
            <!-- Ce que le scan a proposé : l'utilisateur doit voir d'où viennent les champs
                 pré-remplis, avec quelle confiance, et ce qui n'a PAS pu être repris. -->
            <div class="scan-banner" *ngIf="scanInfo">
              <div class="scan-banner-head">
                <span class="scan-banner-title">{{ scanBannerTitre() }}</span>
                <span class="scan-conf" [class]="'scan-conf scan-conf-' + scanInfo.confidence" *ngIf="scanInfo.confidence">
                  Confiance {{ scanConfidenceLabel() }}
                </span>
              </div>
              <p class="scan-hint">{{ scanBannerAide() }}</p>

              <p class="scan-warn" *ngIf="scanInfo.avoir">
                Facture d'avoir : cet écran enregistre un coût. Un remboursement se saisit en Dépenses, catégorie « Avoir fournisseur ».
              </p>
              <p class="scan-warn" *ngIf="scanInfo.plaque && !scanInfo.vehiculeTrouve">
                Plaque détectée « {{ scanInfo.plaque }} » — véhicule introuvable, choisissez-le.
              </p>
              <p class="scan-warn" *ngIf="scanInfo.fournisseur && !scanInfo.fournisseurTrouve">
                Fournisseur « {{ scanInfo.fournisseur }} » — absent de la liste, ajoutez-le avec « + ».
              </p>
              <p class="scan-warn" *ngIf="scanInfo.lignesIgnorees > 0">
                {{ scanInfo.lignesIgnorees }} ligne(s) laissée(s) de côté (timbre, remise, TVA) : ce formulaire n'a pas de case pour elles.
              </p>

              <!-- La facture ne dit pas toujours ce qui revient aux pièces et ce qui revient
                   au temps passé : l'écart se VOIT et ne se comble que sur un clic. -->
              <div class="scan-total" *ngIf="scanInfo.totalFacture !== null">
                <span>Total facture : <strong>{{ scanInfo.totalFacture | appCurrency }}</strong></span>
                <ng-container *ngIf="ecartAvecFacture() as ecart">
                  <span class="scan-ecart">Écart avec la répartition : {{ ecart | appCurrency }}</span>
                  <button class="btn-scan-ecart" *ngIf="ecart > 0" (click)="reporterEcartEnMainOeuvre()"
                          title="La facture ne donne pas cette répartition : le report en main-d'œuvre est votre choix.">
                    Ajouter en main-d'œuvre
                  </button>
                </ng-container>
              </div>

              <a class="scan-doc" *ngIf="scanInfo.receiptUrl" [href]="scanInfo.receiptUrl" target="_blank" rel="noopener">
                Voir le document scanné
              </a>
              <p class="scan-hint" *ngIf="scanInfo.receiptUrl">Son lien est conservé dans les notes, au bas du formulaire.</p>
            </div>

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
                <span class="info-value">{{ (selectedVehicle.mileage || 0) | appDistance:0 }}</span>
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
                <label>Type d'intervention</label>
                <select class="form-control" [(ngModel)]="form.repairType">
                  <option value="">-- Non précisé (déduit de la description) --</option>
                  <option *ngFor="let t of repairTypes" [value]="t.value">{{ t.label }}</option>
                </select>
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
                    <option value="completed">Terminée</option>
                    <!-- « Annulée » se choisit à nouveau (21/09/2026). Elle était cachée
                         parce que la liste Dépenses, le tableau de bord et l'assistant
                         comptaient encore une réparation annulée dans les coûts : le
                         client aurait vu un montant qu'il n'a jamais payé. Les quatre
                         chemins l'excluent désormais (OperatingCostAggregator,
                         DashboardService, expenses.component, AiChatController), le
                         garde-fou ne protégeait donc plus que d'un défaut disparu. -->
                    <option value="cancelled">Annulée</option>
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
                  <span class="col-subtotal">{{ part.subtotal | appCurrency }}</span>
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
                <label>Cout main d'oeuvre ({{ currencyCode }})</label>
                <input type="number" class="form-control" [(ngModel)]="form.laborCost" min="0" step="0.01" placeholder="0.00">
              </div>
            </div>

            <!-- Cost Summary -->
            <div class="cost-summary">
              <div class="summary-row">
                <span>Total pieces</span>
                <span>{{ getPartsCost() | appCurrency }}</span>
              </div>
              <div class="summary-row">
                <span>Main d'oeuvre</span>
                <span>{{ form.laborCost | appCurrency }}</span>
              </div>
              <div class="summary-row total">
                <span>TOTAL</span>
                <span>{{ getTotalCost() | appCurrency }}</span>
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

          <div class="save-error" *ngIf="getAmountError() || saveError" role="alert">{{ getAmountError() || saveError }}</div>
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
                <span class="detail-value">{{ (viewingRepair.mileageAtRepair || 0) | appDistance:0 }}</span>
              </div>
            </div>

            <div class="detail-section">
              <h4>Reparation</h4>
              <div class="detail-row">
                <span class="detail-label">Date</span>
                <span class="detail-value">{{ viewingRepair.repairDate | date:'dd/MM/yyyy' }}</span>
              </div>
              <div class="detail-row" *ngIf="viewingRepair.repairType">
                <span class="detail-label">Type d'intervention</span>
                <span class="detail-value">{{ getRepairTypeLabel(viewingRepair.repairType) }}</span>
              </div>
              <div class="detail-row" *ngIf="viewingRepair.description">
                <span class="detail-label">Description</span>
                <span class="detail-value">{{ viewingRepair.description }}</span>
              </div>
              <div class="detail-row" *ngIf="viewingRepair.invoiceNumber">
                <span class="detail-label">N° Facture</span>
                <span class="detail-value">{{ viewingRepair.invoiceNumber }}</span>
              </div>
              <!-- Document scanné : faute de champ justificatif sur la réparation, son lien
                   vit dans les notes — sans cette ligne il resterait invisible ici. -->
              <div class="detail-row" *ngIf="justificatifUrl(viewingRepair) as url">
                <span class="detail-label">Justificatif</span>
                <span class="detail-value">
                  <a class="justif-link" [href]="url" target="_blank" rel="noopener">Ouvrir le document</a>
                </span>
              </div>
              <div class="detail-row" *ngIf="viewingRepair.accidentEventId">
                <span class="detail-label">Dossier de sinistre</span>
                <span class="detail-value">
                  <span class="accident-badge">🚗 Sinistre #{{ viewingRepair.accidentEventId }}</span>
                </span>
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
                  <span>{{ part.unitPrice | appCurrency }}</span>
                  <span>{{ part.subtotal | appCurrency }}</span>
                </div>
              </div>
            </div>

            <div class="detail-costs">
              <div class="detail-cost-row">
                <span>Total pieces</span>
                <span>{{ viewingRepair.partsCost | appCurrency }}</span>
              </div>
              <div class="detail-cost-row">
                <span>Main d'oeuvre</span>
                <span>{{ viewingRepair.laborCost | appCurrency }}</span>
              </div>
              <div class="detail-cost-row total">
                <span>TOTAL</span>
                <span>{{ viewingRepair.totalCost | appCurrency }}</span>
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
          <!-- Motif du refus renvoyé par le serveur (réparation née d'un sinistre, par exemple) :
               sans lui la fenêtre se fermait sans un mot et la ligne restait à l'écran. -->
          <div class="save-error" *ngIf="saveError" role="alert">{{ saveError }}</div>
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

    /* flex-wrap : un bouton de plus (scan de facture) dans la barre — à 1536 px tout
       tient sur une ligne, et si la fenêtre rétrécit la barre passe à la ligne
       plutôt que de pousser un ascenseur horizontal. */
    .filter-bar { display:flex; flex-wrap:wrap; align-items:center; gap:12px; padding:10px 14px; background:white; border-bottom:1px solid #e2e8f0; }
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
    .btn-add:hover:not(:disabled) { background:#2563eb; }
    /* Fenêtre ouverte : le bouton est verrouillé, il doit le MONTRER — grisé et curseur
       barré, comme « Exporter PDF » sans ligne et comme le bouton du scan. */
    .btn-add:disabled { opacity:.5; cursor:not-allowed; }

    /* Parc au-delà du plafond de chargement : un avertissement qui se lit, pas une infobulle. */
    .avertissement-volume { padding:10px 14px; background:#fffbeb; border-bottom:1px solid #fcd34d; color:#92400e; font-size:12px; line-height:1.5; }
    .avertissement-volume strong { color:#78350f; }
    .stats-bar { display:flex; gap:16px; padding:12px 14px; background:white; border-bottom:1px solid #e2e8f0; }
    .stat-item { display:flex; align-items:center; gap:10px; padding:8px 14px; background:#f8fafc; border-radius:6px; }
    .stat-icon { width:32px; height:32px; border-radius:6px; display:flex; align-items:center; justify-content:center; }
    .stat-icon.active { background:#dcfce7; color:#16a34a; }
    .stat-icon.warning { background:#fef3c7; color:#d97706; }
    .stat-icon.info { background:#dbeafe; color:#2563eb; }
    .stat-icon.cost { background:#f3e8ff; color:#7c3aed; }
    .stat-icon.muted { background:#f1f5f9; color:#64748b; }
    .stat-content { display:flex; flex-direction:column; }
    .stat-value { font-size:16px; font-weight:600; color:#1e293b; }
    /* Intitulé sur une seule ligne : « Réparations filtrées » est plus long que
       « Total réparations » et passait à la ligne en écrasant la barre. */
    .stat-label { font-size:11px; color:#64748b; white-space:nowrap; }

    /* Table */
    .table-container { flex:1; padding:16px 24px; overflow-x:auto; }
    .pager { display:flex; align-items:center; gap:12px; justify-content:center; padding:14px; font-size:12px; color:#475569; }
    .pager button { padding:6px 12px; background:white; border:1px solid #e2e8f0; border-radius:6px; cursor:pointer; }
    .pager button:disabled { opacity:.4; cursor:not-allowed; }
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
    .type-tag { display:inline-block; background:#eff6ff; color:#2563eb; border:1px solid #dbeafe; padding:1px 7px; border-radius:4px; font-size:10px; font-weight:600; margin-bottom:3px; }
    /* Même pilule que le badge « Accident » de l'écran Dépenses, en plus discret. */
    .accident-badge { display:inline-block; margin-top:3px; background:#fee2e2; color:#b91c1c; border:1px solid #fecaca; padding:1px 7px; border-radius:4px; font-size:10px; font-weight:600; white-space:nowrap; }

    td.num { text-align:right; font-variant-numeric:tabular-nums; font-family:monospace; }
    td.num.strong { font-weight:700; color:#16a34a; font-size:12.5px; }

    .status-badge { display:inline-block; padding:3px 8px; border-radius:4px; font-size:10px; font-weight:600; white-space:nowrap; }
    .status-badge.completed { background:#dcfce7; color:#16a34a; }
    .status-badge.pending { background:#fef3c7; color:#d97706; }
    .status-badge.in_progress { background:#dbeafe; color:#2563eb; }
    .status-badge.cancelled { background:#f1f5f9; color:#64748b; }
    .status-badge.large { font-size:12px; padding:6px 12px; }

    .actions-cell { display:flex; align-items:center; gap:4px; }
    .btn-action { width:26px; height:26px; border:1px solid #e2e8f0; border-radius:4px; background:white; display:flex; align-items:center; justify-content:center; cursor:pointer; transition:all .15s; }
    .btn-action.view { color:#2563eb; }
    /* :not(:disabled) sur les trois — un bouton verrouillé qui s'allume au survol
       promet un clic qui n'arrivera jamais. */
    .btn-action.view:hover:not(:disabled) { background:#dbeafe; border-color:#2563eb; }
    .btn-action.edit { color:#f59e0b; }
    .btn-action.edit:hover:not(:disabled) { background:#fef3c7; border-color:#f59e0b; }
    .btn-action.delete { color:#dc2626; }
    .btn-action.delete:hover:not(:disabled) { background:#fee2e2; border-color:#dc2626; }
    .btn-action:disabled { opacity:.4; cursor:not-allowed; }

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

    .overlay { position:fixed; top:0; left:0; right:0; bottom:0; background:rgba(0,0,0,0.5); display:flex; justify-content:flex-end; z-index:1200; /* > 1100 : la barre du haut masquait l en-tete du tiroir */ }
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

    /* Bandeau du scan : ce que l'IA a proposé, et ce qu'elle n'a pas pu reprendre.
       Mêmes teintes violettes que le bouton de la brique, pour que le lien se voie. */
    .scan-banner { margin-bottom:16px; padding:12px; background:#f5f3ff; border:1px solid #ddd6fe; border-radius:8px; }
    .scan-banner-head { display:flex; align-items:center; justify-content:space-between; gap:8px; }
    .scan-banner-title { font-size:12.5px; font-weight:700; color:#5b21b6; }
    .scan-conf { padding:1px 8px; border-radius:999px; font-size:10.5px; font-weight:700; background:#e2e8f0; color:#475569; white-space:nowrap; }
    .scan-conf-high { background:#dcfce7; color:#15803d; }
    .scan-conf-medium { background:#fef3c7; color:#b45309; }
    .scan-conf-low { background:#fee2e2; color:#b91c1c; }
    .scan-hint { margin:6px 0 0; font-size:11px; color:#64748b; line-height:1.4; }
    .scan-warn { margin:6px 0 0; font-size:11.5px; color:#b45309; line-height:1.4; }
    .scan-total { display:flex; flex-wrap:wrap; align-items:center; gap:8px; margin-top:8px; font-size:12px; color:#1e293b; }
    .scan-ecart { color:#b45309; }
    .btn-scan-ecart { padding:3px 8px; background:white; color:#6d28d9; border:1px solid #c4b5fd; border-radius:4px; font-size:11px; font-weight:600; cursor:pointer; }
    .btn-scan-ecart:hover { background:#ede9fe; }
    .scan-doc { display:inline-block; margin-top:8px; font-size:11.5px; font-weight:600; color:#6d28d9; }
    .justif-link { color:#2563eb; text-decoration:underline; }

    .vehicle-select-wrapper { margin-bottom:8px; }
    .vehicle-info-box { display:flex; gap:8px; padding:8px 12px; background:#f8fafc; border-radius:6px; font-size:12px; }
    .info-label { color:#64748b; }
    .info-value { font-weight:600; color:#1e293b; }

    .btn-add-part { display:flex; align-items:center; gap:4px; padding:6px 10px; background:#f0fdf4; color:#16a34a; border:1px solid #86efac; border-radius:4px; font-size:11px; cursor:pointer; }
    .btn-add-part:hover { background:#dcfce7; }

    .parts-table { border:1px solid #e2e8f0; border-radius:6px; overflow:hidden; }
    /* En-tête et ligne de saisie sont DEUX grilles distinctes : sans min-width:0,
       chacune résout « 1fr » à sa façon, car un <input> impose une largeur
       minimale intrinsèque que le texte d'un intitulé n'a pas. Les colonnes de la
       ligne s'élargissaient donc, décalant chaque champ vers la droite par rapport
       à son intitulé — 12,8 px à 520 px utiles, 72,8 px à 460 px — et la ligne
       finissait par déborder du cadre (recette du 21/09/2026). Mesuré : 0 px de
       décalage et 0 débordement de 460 à 520 px avec ces deux règles. */
    .parts-header, .parts-row { display:grid; grid-template-columns:1fr 80px 50px 80px 90px 36px; gap:8px; padding:8px 10px; align-items:center; }
    .parts-header > *, .parts-row > * { min-width:0; }
    .parts-row input { width:100%; box-sizing:border-box; }
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
    .save-error { padding:8px 20px; background:#fef2f2; border-top:1px solid #fecaca; color:#b91c1c; font-size:12px; line-height:1.4; }
    .modal-content .save-error { margin-top:12px; padding:8px 10px; border:1px solid #fecaca; border-radius:6px; }
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
    /* Même précaution que la grille de saisie : l'en-tête et les lignes sont des
       grilles séparées, et un nom de pièce long élargirait « 1fr » ici sans
       élargir « Designation » là-haut — les colonnes ne tomberaient plus en face. */
    .detail-parts-row > * { min-width:0; overflow-wrap:anywhere; }
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

  stats = { totalRepairs: 0, pendingRepairs: 0, completedRepairs: 0, cancelledRepairs: 0, totalCost: 0 };
  /**
   * Nombre de réparations de la société, rendu par le serveur (totalCount) — et non
   * la longueur de la liste chargée. C'est lui que cite l'infobulle « Sur N réparations
   * au total » et le bandeau de parc volumineux.
   */
  totalParc = 0;

  /**
   * Chargement du parc — défaut du 20/09/2026, « c'est pas acceptable que le total soit
   * faux » (Karim).
   *
   * L'écran demandait UNE page de 100 lignes et faisait tout le reste dessus : compteurs,
   * filtres, recherche, tri, export. Une société de 250 réparations lisait « 100 Total
   * réparations », un coût amputé, et 150 réparations introuvables — sans un mot. La
   * société de test n'en a que 38, le défaut ne se voyait pas en local.
   *
   * Choix : charger TOUT le parc, page après page, et garder recherche, tri et
   * pagination dans le navigateur — un seul ensemble en mémoire, donc des compteurs qui
   * recoupent le tableau par construction. Mesuré en base le 20/09/2026 : une réparation
   * rendue par l'API pèse 746 octets en moyenne (786 au maximum, pièces comprises), et le
   * parc grossit d'environ 4 réparations par véhicule et par an (38 réparations pour
   * 12 véhicules en 9,5 mois). 250 lignes ≈ 180 Ko, 1 000 ≈ 0,7 Mo, à comparer aux
   * 28 Ko du plus gros parc d'aujourd'hui. Le plafond ci-dessous n'est qu'un garde-fou.
   */
  taillePageChargement = 500;

  /**
   * Plafond de lignes gardées en mémoire : 2 000 ≈ 1,5 Mo, soit un parc de 500 véhicules
   * sur un an. Au-delà, l'écran ne charge pas plus et le DIT — bandeau visible, et
   * compteurs demandés au serveur pour qu'aucun total ne soit faux.
   */
  limiteChargement = 2000;

  /** Vrai quand tout le parc est en mémoire : les compteurs se calculent alors ici. */
  chargementComplet = true;

  /** Filtres SERVEUR du dernier chargement : dire si l’ensemble en mémoire correspond encore à ce que l’écran demande. */
  private filtreServeurCharge = { vehicle: '', status: '' };

  /** Chargement en cours : le pied de tableau le dit plutôt que de laisser croire à un parc vide. */
  chargementEnCours = false;

  /**
   * Pagination de l'AFFICHAGE : le tri, la recherche et les compteurs portent sur tout
   * l'ensemble filtré, seule la découpe en pages est ici. 25 lignes tiennent sur un
   * écran de 1536 px sans ascenseur interminable.
   */
  taillePageAffichage = 25;
  pageAffichage = 1;

  isPanelOpen = false;
  editingRepair: Repair | null = null;
  viewingRepair: Repair | null = null;

  showDeleteConfirm = false;
  repairToDelete: Repair | null = null;

  /** Refus du serveur lors de l'enregistrement : il n'était écrit que dans la console. */
  saveError: string | null = null;

  /** Résumé du dernier scan de facture, affiché en tête du formulaire (null = saisie manuelle). */
  scanInfo: ScanInfo | null = null;

  form = this.getEmptyForm();

  readonly repairTypes = REPAIR_TYPES;

  // Suppliers
  suppliers: { id: number; name: string; type: string }[] = [];
  showAddSupplier = false;
  newSupplierName = '';
  newSupplierType = 'garage';

  constructor(
    private apiService: ApiService,
    private pdfService: PdfExportService,
    private cdr: ChangeDetectorRef,
    private userPrefs: UserPreferencesService,
    private route: ActivatedRoute
  ) {}

  get currencyCode(): string { return this.userPrefs.current.currency; }

  /**
   * Une fenêtre est ouverte (saisie, détail, confirmation de suppression) : TOUT ce qui
   * ouvrirait ou remplacerait une fenêtre depuis l'arrière-plan est alors VERROUILLÉ —
   * « Scanner une facture », « Nouvelle reparation » et, sur chaque ligne, Détails,
   * Modifier et Supprimer.
   * Ces boutons vivent hors de la fenêtre : l'ombre les cachait mais ne les sortait pas
   * de l'ordre de tabulation — à la touche Tab on les atteignait encore, le formulaire
   * repartait de zéro ou était remplacé, la saisie en cours perdue sans un mot.
   */
  get fenetreOuverte(): boolean {
    return this.isPanelOpen || !!this.viewingRepair || this.showDeleteConfirm;
  }

  /** Motif du verrou, en infobulle, pour que le gris ne soit pas une énigme. */
  readonly raisonFenetreOuverte = 'Fermez la fenêtre ouverte avant d\'en ouvrir une autre : la saisie en cours serait perdue.';

  ngOnInit() {
    // Lien « Voir toutes les interventions » du rapport Fréquence des réparations : /repairs?vehicleId=N
    const vehicleId = this.route.snapshot.queryParamMap.get('vehicleId');
    if (vehicleId && /^\d+$/.test(vehicleId)) this.filterVehicle = vehicleId;
    this.loadVehicles();
    this.loadRepairs();
    this.loadSuppliers();
  }

  getRepairTypeLabel(type: string | null | undefined): string {
    return REPAIR_TYPES.find(t => t.value === type)?.label || type || '';
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
      repairType: '' as string,
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
          { id: 1, name: 'Peugeot 208', plateNumber: 'AB-123-CD', mileage: 52000 },
          { id: 2, name: 'Renault Clio', plateNumber: 'CD-456-EF', mileage: 78500 },
          { id: 3, name: 'Citroen Berlingo', plateNumber: 'EF-789-GH', mileage: 125000 }
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

  /** Ligne du serveur → ligne de l'écran. Un seul endroit, le chargement enchaîne les pages. */
  private versRepair(r: RepairDto): Repair {
    return {
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
      repairType: r.repairType ?? null,
      // Dossier de sinistre : sert au badge de la ligne et au verrou du bouton Supprimer.
      accidentEventId: r.accidentEventId ?? null,
      parts: (r.parts || []).map(p => ({
        id: p.id,
        partName: p.partName,
        partReference: p.partReference || '',
        quantity: p.quantity,
        unitPrice: p.unitPrice,
        subtotal: p.subtotal,
        notes: p.notes || ''
      }))
    };
  }

  loadRepairs() {
    this.chargementEnCours = true;
    this.filtreServeurCharge = { vehicle: this.filterVehicle, status: this.filterStatus };
    this.chargerPage(1, []);
  }

  /**
   * Changement du filtre véhicule ou statut. Ces deux-là, le SERVEUR sait les
   * appliquer : quand le parc dépasse ce que l'écran garde en mémoire, on
   * recharge en les lui passant, et le sous-ensemble demandé redevient complet.
   * Sans cela le bandeau conseillait de filtrer pour « atteindre les autres »
   * alors que le filtre ne faisait que trier les lignes DÉJÀ chargées — il
   * promettait ce qu'il ne pouvait pas tenir (20/09/2026).
   * La recherche par texte, elle, ne s'exprime pas en SQL : elle reste locale,
   * et le bandeau le dit.
   */
  onFiltreServeur() {
    // Recharger aussi quand le dernier chargement portait un filtre SERVEUR et qu'il
    // vient de changer : sinon revenir à « Tous les véhicules » gardait en mémoire le
    // seul sous-ensemble du véhicule précédent et l'écran l'affichait comme s'il était
    // tout le parc — compteurs compris, sans le bandeau « Parc volumineux » qui a
    // disparu en même temps que le chargement est devenu « complet » pour ce filtre.
    const filtreChange = this.filtreServeurCharge.vehicle !== this.filterVehicle
                      || this.filtreServeurCharge.status !== this.filterStatus;
    if (!this.chargementComplet || filtreChange) { this.loadRepairs(); return; }
    this.filterRepairs();
  }

  /**
   * Une page du serveur, puis la suivante tant qu'il reste des réparations : c'est ce
   * qui rend les compteurs, la recherche et le tri vrais pour une société de 250 lignes
   * comme pour une de 38. La liste n'est remplacée qu'à la FIN, sinon le tableau
   * clignoterait page après page.
   *
   * Trois arrêts : plus rien à charger, page vide (un serveur qui rendrait toujours la
   * même page ne doit pas tourner en boucle), ou plafond atteint.
   */
  private chargerPage(page: number, cumul: Repair[]) {
    this.apiService.getRepairs({
        page,
        pageSize: this.taillePageChargement,
        // Filtres que le serveur sait appliquer : sur un gros parc, ils réduisent
        // l'ensemble à charger et rendent le sous-ensemble demandé COMPLET.
        vehicleId: this.filterVehicle ? +this.filterVehicle : undefined,
        status: this.filterStatus || undefined,
      })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (result) => {
          const lot = (result.items || []).map(r => this.versRepair(r));
          const tout = cumul.concat(lot);
          // Le total du parc est connu dès la première page : on le retient TOUT DE SUITE,
          // sinon une erreur sur la page suivante laisserait l'écran croire qu'il a tout.
          this.totalParc = Math.max(result.totalCount ?? tout.length, tout.length);

          if (lot.length > 0 && tout.length < this.totalParc && tout.length < this.limiteChargement) {
            this.chargerPage(page + 1, tout);
            return;
          }

          this.repairs = tout;
          this.chargementComplet = tout.length >= this.totalParc;
          this.chargementEnCours = false;
          this.pageAffichage = 1;
          // filterRepairs() met les compteurs à jour : un seul chemin, jamais deux sources.
          this.filterRepairs();
          this.cdr.detectChanges();
        },
        error: (err) => {
          console.error('Error loading repairs:', err);
          this.chargementEnCours = false;
          // Ce qui a déjà été chargé vaut mieux qu'un tableau vide, mais il en manque :
          // le bandeau et les compteurs du serveur prennent le relais, on ne prétend
          // jamais avoir tout le parc.
          if (cumul.length > 0) {
            this.repairs = cumul;
            this.chargementComplet = cumul.length >= this.totalParc;
            this.pageAffichage = 1;
            this.filterRepairs();
          }
          this.cdr.detectChanges();
        }
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
      // Sans casse : des statuts anciens (« Cancelled ») échappaient au filtre.
      result = result.filter(r => (r.status || '').trim().toLowerCase() === this.filterStatus);
    }
    this.filteredRepairs = result;
    // Un filtre change l'ensemble : on repart de la première page, sinon on resterait
    // sur une page 7 qui n'existe plus et le tableau paraîtrait vide.
    this.pageAffichage = 1;
    // Les compteurs du haut suivent recherche, véhicule et statut. Sans cette mise à
    // jour ils restaient figés sur tout le parc (un véhicule filtré à 72 € et
    // « 36 réparations, 10 570 € » annoncés au-dessus).
    this.majCompteurs();
  }

  /**
   * D'où viennent les quatre compteurs.
   *
   * Tout le parc est en mémoire (le cas de toutes les sociétés GPA d'aujourd'hui) : ils
   * se calculent ici et recoupent EXACTEMENT le tableau, la pagination et l'export PDF.
   *
   * Parc au-delà du plafond : le navigateur n'a qu'une partie des lignes, un calcul
   * local annoncerait un total faux. Le serveur, lui, sait compter toute la société
   * (GET /api/repairs/stats, mêmes règles : annulées comptées, hors montants) et connaît
   * les filtres véhicule et statut. La recherche par texte, elle, ne s'exprime pas là-bas :
   * dans ce seul cas on retombe sur les lignes chargées, et le bandeau le dit.
   */
  private majCompteurs() {
    if (this.chargementComplet || this.searchQuery) {
      this.calculateStats();
      return;
    }

    this.apiService.getRepairStats({
      vehicleId: this.filterVehicle ? +this.filterVehicle : undefined,
      status: this.filterStatus || undefined
    }).pipe(takeUntil(this.destroy$)).subscribe({
      next: (s) => {
        this.stats = {
          totalRepairs: s.totalRepairs,
          pendingRepairs: s.pendingRepairs,
          completedRepairs: s.completedRepairs,
          cancelledRepairs: s.cancelledRepairs ?? 0,
          totalCost: s.totalCost
        };
        this.cdr.detectChanges();
      },
      // Serveur muet : les chiffres des lignes chargées, avec le bandeau qui les
      // relativise, valent mieux que quatre zéros.
      error: () => { this.calculateStats(); this.cdr.detectChanges(); }
    });
  }

  /** Nombre de pages du tableau (au moins une, même vide : « Page 1 / 1 »). */
  get totalPagesAffichage(): number {
    return Math.max(1, Math.ceil(this.filteredRepairs.length / this.taillePageAffichage));
  }

  /** Changement de page, borné : un clic ne doit jamais sortir de l'ensemble filtré. */
  allerPage(page: number) {
    this.pageAffichage = Math.min(Math.max(1, page), this.totalPagesAffichage);
  }

  /**
   * Portée réelle du tableau, de la recherche et des compteurs quand le parc dépasse le
   * plafond de chargement. Affiché en clair : l'écran ne montre jamais un sous-ensemble
   * sans le dire.
   */
  get porteeChargementPartiel(): string {
    return this.searchQuery
      ? `La recherche et les compteurs ne portent que sur ces ${this.repairs.length} lignes.`
      : `Les compteurs portent sur le parc entier ; le tableau, la recherche et l'export PDF `
        + `ne portent que sur ces ${this.repairs.length} lignes.`;
  }

  /**
   * Un filtre masque des lignes : les compteurs ne portent plus tout le parc.
   * Mêmes conditions exactement que filterRepairs(), sinon l'intitulé mentirait
   * sur un cas de bord (une recherche faite d'espaces filtre bel et bien).
   */
  get filtreActif(): boolean {
    return !!(this.searchQuery || this.filterVehicle || this.filterStatus);
  }

  /** Intitulé du premier compteur : « Total » ne se dit que du parc entier. */
  get libelleTotal(): string {
    return this.filtreActif ? 'Réparations filtrées' : 'Total réparations';
  }

  /** Idem pour le coût : filtré, ce n'est plus le total du parc. */
  get libelleCout(): string {
    return this.filtreActif ? 'Coût filtré' : 'Coût total';
  }

  /** Infobulle du premier compteur : rappelle le parc entier quand le filtre masque des lignes. */
  get infobulleTotal(): string {
    return this.filtreActif ? `Sur ${this.totalParc} réparations au total` : '';
  }

  /** Infobulle du coût : périmètre du filtre et exclusion des annulées, quand ils s'appliquent. */
  get infobulleCout(): string {
    const mentions: string[] = [];
    if (this.filtreActif) mentions.push('Sur le filtre en cours');
    if (this.stats.cancelledRepairs > 0) mentions.push('Hors réparations annulées');
    return mentions.join(' — ');
  }

  toggleSort(column: RepairSortKey) {
    if (this.sortColumn === column) {
      this.sortDirection = this.sortDirection === 'asc' ? 'desc' : 'asc';
    } else {
      this.sortColumn = column;
      this.sortDirection = column === 'repairDate' ? 'desc' : 'asc';
    }
    // Le tri reclasse TOUT l'ensemble filtré : rester sur la page 7 après un clic sur
    // une colonne ferait passer à côté de ce que l'on vient de remonter en tête.
    this.pageAffichage = 1;
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

  /**
   * Lignes de la page affichée. Le tri et les filtres portent sur TOUT l'ensemble
   * filtré : seule la découpe est ici, sinon trier ne classerait que la page en cours.
   */
  getPageRepairs(): Repair[] {
    const debut = (this.pageAffichage - 1) * this.taillePageAffichage;
    return this.getSortedRepairs().slice(debut, debut + this.taillePageAffichage);
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
      // Montants hors réparations annulées, comme le « Coût total » de l'écran : les lignes
      // annulées restent listées avec leur statut, mais aucun atelier n'est dû.
      const costed = rs.filter(r => !this.isCancelled(r));
      const subParts = costed.reduce((s, r) => s + (r.partsCost || 0), 0);
      const subLabor = costed.reduce((s, r) => s + (r.laborCost || 0), 0);
      const subTotal = costed.reduce((s, r) => s + (r.totalCost || 0), 0);

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
        subtotal: `${rs.length} réparation(s) — Pièces: ${this.userPrefs.formatCurrency(subParts)} | M. œuvre: ${this.userPrefs.formatCurrency(subLabor)} | Total: ${this.userPrefs.formatCurrency(subTotal)}`
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
        { header: `Pièces (${this.currencyCode})`, dataKey: 'partsCost' },
        { header: `M. œuvre (${this.currencyCode})`, dataKey: 'laborCost' },
        { header: `Total (${this.currencyCode})`, dataKey: 'totalCost' },
        { header: 'Statut', dataKey: 'status' }
      ],
      groups,
      grandTotal: `TOTAL GÉNÉRAL — ${grandCount} réparation(s) | Pièces: ${this.userPrefs.formatCurrency(grandTotalParts)} | M. œuvre: ${this.userPrefs.formatCurrency(grandTotalLabor)} | Total: ${this.userPrefs.formatCurrency(grandTotal)}`
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

  /** Statut enregistré « cancelled », casse et espaces ignorés (valeurs anciennes). */
  isCancelled(repair: Repair | null): boolean {
    return (repair?.status || '').trim().toLowerCase() === 'cancelled';
  }

  calculateStats() {
    // Périmètre = TOUT l'ensemble filtré (filteredRepairs), pas la page affichée : ce
    // sont les mêmes réparations que la barre de pagination annonce et que l'export PDF
    // reprend. Sur tout le parc, les compteurs démentaient le tableau filtré.
    // Appelé par majCompteurs() quand le parc entier est en mémoire — le cas courant ;
    // au-delà du plafond de chargement, les compteurs viennent du serveur.
    // Une réparation annulée (import, API) ne coûte rien : les rapports l'excluent déjà.
    // Elle gonflait le coût total, et sans compteur dédié en attente + terminées ne
    // recoupait plus le total (même règle que GET /repairs/stats).
    const is = (r: Repair, status: string) => (r.status || '').trim().toLowerCase() === status;
    const visibles = this.filteredRepairs;
    const costed = visibles.filter(r => !this.isCancelled(r));
    this.stats = {
      totalRepairs: visibles.length,
      pendingRepairs: visibles.filter(r => is(r, 'pending') || is(r, 'in_progress')).length,
      completedRepairs: visibles.filter(r => is(r, 'completed')).length,
      cancelledRepairs: visibles.length - costed.length,
      totalCost: costed.reduce((sum, r) => sum + r.totalCost, 0)
    };
  }

  getStatusLabel(status: string): string {
    const labels: Record<string, string> = {
      pending: 'En attente',
      in_progress: 'En cours',
      completed: 'Terminée',
      cancelled: 'Annulée'
    };
    return labels[(status || '').trim().toLowerCase()] || status;
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
    this.saveError = null;
    this.scanInfo = null;
    this.isPanelOpen = true;
  }

  // ── Scan de facture (IA) ───────────────────────────────────────────────────

  /**
   * Le scan efface le formulaire pour repartir du document : on ne remplace JAMAIS
   * une saisie en cours sans demander (même règle qu'à l'écran Carburant).
   *
   * Le verrou du bouton (`fenetreOuverte`) empêche de LANCER un scan par-dessus une
   * saisie ; il reste la course inverse — scan lancé fenêtre fermée, l'analyse dure
   * quelques secondes et l'utilisateur ouvre « Nouvelle reparation » entre-temps.
   * Refuser tout court perdrait un scan déjà décompté du quota : on demande.
   */
  private peutRemplacerLaSaisie(): boolean {
    if (!this.isPanelOpen) return true;
    return confirm('Une saisie est en cours. La remplacer par les valeurs de la facture scannée ?');
  }

  /**
   * Facture scannée : <app-scan-facture> a déjà tout fait (envoi, quota, erreurs)
   * et rend l'extraction. Cet écran ne décide que du remplissage de SON formulaire.
   *
   * Le formulaire repart de zéro : une saisie en cours n'est jamais écrasée sans
   * accord (voir `fenetreOuverte` et `peutRemplacerLaSaisie`). Un champ illisible
   * (null) reste vide — le scan propose, l'utilisateur dispose, et rien n'est
   * enregistré tant qu'il n'a pas validé.
   */
  onFactureScannee(res: ResultatScanFacture): void {
    if (!this.peutRemplacerLaSaisie()) return;
    // Fiche de détail ou confirmation de suppression restées ouvertes : rien n'y est
    // saisi, mais deux fenêtres empilées n'ont aucun sens.
    this.viewingRepair = null;
    this.showDeleteConfirm = false;
    this.repairToDelete = null;

    const x = res.extraction;
    this.form = this.getEmptyForm();
    this.editingRepair = null;
    this.selectedVehicle = null;
    this.saveError = null;

    const vehicule = this.matchVehicleByPlate(x.vehiclePlate);
    if (vehicule) {
      this.form.vehicleId = String(vehicule.id);
      this.onVehicleChange();   // reprend le kilométrage courant, comme une sélection à la main
    }

    const fournisseur = this.matchSupplierByName(x.supplierName);
    if (fournisseur) this.form.supplierId = String(fournisseur.id);

    // Même précaution qu'à la modification : l'IA peut rendre un horodatage complet.
    const dateFacture = dateSeule(x.date);
    if (dateFacture) this.form.repairDate = dateFacture;
    this.form.invoiceNumber = x.invoiceNumber || '';
    this.form.description = x.descriptionComplete;
    // Type d'intervention : la facture ne le donne pas (les catégories du scan sont
    // celles des dépenses). Laissé « Non précisé », le rapport le déduit de la description.

    const reparti = this.repartirLignesFacture(x.items);
    this.form.parts = reparti.pieces;
    this.form.laborCost = reparti.mainOeuvre;

    // Justificatif : la réparation n'a pas de champ dédié en base, le lien part dans les notes.
    this.form.notes = res.receiptUrl ? PREFIXE_JUSTIFICATIF + res.receiptUrl : '';

    this.scanInfo = {
      confidence: x.confidence || '',
      echec: false,
      plaque: x.vehiclePlate || '',
      vehiculeTrouve: !!vehicule,
      fournisseur: x.supplierName || '',
      fournisseurTrouve: !!fournisseur,
      totalFacture: x.total,
      lignesIgnorees: reparti.ignorees,
      avoir: x.isCreditNote,
      receiptUrl: res.receiptUrl
    };
    this.isPanelOpen = true;
    this.cdr.detectChanges();
  }

  /**
   * Scan échoué (quota atteint, IA indisponible, format refusé) : le message a déjà
   * été montré par la brique et la saisie à la main reste entière — « Nouvelle
   * reparation » n'est jamais bloquée. Quand le serveur a tout de même stocké le
   * fichier, on ouvre le formulaire VIDE avec le justificatif déjà rattaché.
   */
  onEchecScan(e: EchecScanFacture): void {
    if (!e?.receiptUrl) return;
    if (!this.peutRemplacerLaSaisie()) return;
    this.viewingRepair = null;
    this.showDeleteConfirm = false;
    this.repairToDelete = null;
    this.form = this.getEmptyForm();
    this.editingRepair = null;
    this.selectedVehicle = null;
    this.saveError = null;
    this.form.notes = PREFIXE_JUSTIFICATIF + e.receiptUrl;
    this.scanInfo = {
      confidence: '', echec: true, plaque: '', vehiculeTrouve: false,
      fournisseur: '', fournisseurTrouve: false, totalFacture: null,
      lignesIgnorees: 0, avoir: false, receiptUrl: e.receiptUrl
    };
    this.isPanelOpen = true;
    this.cdr.detectChanges();
  }

  /** Rapprochement par plaque, mêmes règles qu'à l'écran Dépenses (égalité puis inclusion). */
  private matchVehicleByPlate(plaque: string | null): Vehicle | null {
    const cible = cleRapprochement(plaque || '');
    if (!cible) return null;
    return this.vehicles.find(v => cleRapprochement(v.plateNumber) === cible)
        || this.vehicles.find(v => {
             const p = cleRapprochement(v.plateNumber);
             return !!p && (p.includes(cible) || cible.includes(p));
           })
        || null;
  }

  /**
   * Fournisseur reconnu par son nom. L'inclusion n'est tentée qu'à partir de quatre
   * caractères : un garage nommé « AB » rattraperait n'importe quelle facture.
   */
  private matchSupplierByName(nom: string | null): { id: number; name: string; type: string } | null {
    const cible = cleRapprochement(nom || '');
    if (!cible) return null;
    return this.suppliers.find(s => cleRapprochement(s.name) === cible)
        || (cible.length >= 4
              ? this.suppliers.find(s => {
                  const n = cleRapprochement(s.name);
                  return n.length >= 4 && (n.includes(cible) || cible.includes(n));
                })
              : undefined)
        || null;
  }

  /**
   * Lignes de la facture → pièces détachées et main-d'œuvre. Ne partent en
   * main-d'œuvre que les lignes qui le DISENT (main d'œuvre, forfait, heure,
   * diagnostic…) ; timbre, remise et TVA n'ont pas de case ici et sont laissés de
   * côté (comptés, puis signalés) ; tout le reste est une pièce, quantité 1 et prix
   * unitaire = montant de la ligne — la facture ne donne pas les quantités et les
   * inventer fausserait le prix unitaire.
   */
  private repartirLignesFacture(items: LigneFactureScannee[]): { pieces: RepairPart[]; mainOeuvre: number; ignorees: number } {
    const pieces: RepairPart[] = [];
    let mainOeuvre = 0;
    let ignorees = 0;
    for (const it of items || []) {
      const libelle = (it?.label || '').trim();
      const montant = Math.round((Number(it?.amount) || 0) * 100) / 100;
      if (!libelle && montant <= 0) continue;
      const cle = sansAccents(libelle);
      // Montant nul ou négatif (remise) : le serveur refuse un prix unitaire négatif.
      if (montant <= 0 || LIGNE_HORS_POSTE.test(cle)) { ignorees++; continue; }
      if (LIGNE_MAIN_OEUVRE.test(cle)) { mainOeuvre += montant; continue; }
      pieces.push({
        partName: libelle || 'Pièce', partReference: '',
        quantity: 1, unitPrice: montant, subtotal: montant, notes: ''
      });
    }
    return { pieces, mainOeuvre: Math.round(mainOeuvre * 100) / 100, ignorees };
  }

  /**
   * Écart entre le total lu sur la facture et ce que le formulaire totalise (0 =
   * rien à signaler). Timbre fiscal, remise ou facture sans détail : l'écart se
   * montre, il ne se comble jamais tout seul — sinon le scan inventerait une
   * répartition pièces / main-d'œuvre que la facture ne donne pas.
   */
  ecartAvecFacture(): number {
    const total = this.scanInfo?.totalFacture;
    if (total === null || total === undefined) return 0;
    const ecart = Math.round((total - this.getTotalCost()) * 100) / 100;
    return Math.abs(ecart) < 0.01 ? 0 : ecart;
  }

  /** Report de l'écart en main-d'œuvre : un clic de l'utilisateur, jamais un automatisme. */
  reporterEcartEnMainOeuvre(): void {
    const ecart = this.ecartAvecFacture();
    if (ecart <= 0) return;
    this.form.laborCost = Math.round(((Number(this.form.laborCost) || 0) + ecart) * 100) / 100;
  }

  scanBannerTitre(): string {
    return this.scanInfo?.echec ? 'Document joint — saisie à la main' : 'Pré-rempli par le scan de facture';
  }

  scanBannerAide(): string {
    return this.scanInfo?.echec
      ? "L'analyse n'a pas abouti : le document est joint, saisissez les informations."
      : "Vérifiez chaque champ avant d'enregistrer — rien n'est encore enregistré.";
  }

  /** Confiance de la lecture, mêmes mots qu'à l'écran Dépenses. */
  scanConfidenceLabel(): string {
    const c = this.scanInfo?.confidence || '';
    return ({ high: 'élevée', medium: 'moyenne', low: 'faible' } as Record<string, string>)[c] || c;
  }

  /** Lien du document scanné, rangé dans les notes faute de champ dédié (voir onFactureScannee). */
  justificatifUrl(repair: Repair | null): string | null {
    const trouve = /\/uploads\/invoices\/\S+/.exec(repair?.notes || '');
    return trouve ? trouve[0] : null;
  }

  editRepair(repair: Repair) {
    this.editingRepair = repair;
    this.saveError = null;
    this.scanInfo = null;   // modification d'une ligne existante : rien ne vient d'un scan
    this.form = {
      vehicleId: repair.vehicleId.toString(),
      supplierId: repair.supplierId?.toString() || '',
      // Horodatage de la base ramené au jour : sans cela le champ Date s'affichait vide.
      repairDate: dateSeule(repair.repairDate),
      mileageAtRepair: repair.mileageAtRepair || null,
      description: repair.description,
      invoiceNumber: repair.invoiceNumber,
      status: (repair.status || '').trim().toLowerCase(),
      laborCost: repair.laborCost,
      notes: repair.notes,
      repairType: repair.repairType || '',
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
    this.saveError = null;
    this.scanInfo = null;
    // Le bloc « ajouter un fournisseur » survivait à la fermeture : la fenêtre suivante
    // s'ouvrait déjà dépliée, avec le nom à moitié tapé de la fois d'avant.
    this.showAddSupplier = false;
    this.newSupplierName = '';
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
    return !!this.form.vehicleId && !!this.form.repairDate && !this.getAmountError();
  }

  /**
   * Premier montant invalide, mêmes règles et mêmes libellés que le serveur
   * (RepairInputRules) : les attributs min des champs n'empêchent pas de taper un
   * signe moins, et un total négatif était déduit du poste « Réparations » des rapports.
   */
  getAmountError(): string | null {
    if (Number(this.form.laborCost ?? 0) < 0) return "Main-d'œuvre : le montant ne peut pas être négatif.";
    for (let i = 0; i < this.form.parts.length; i++) {
      const p = this.form.parts[i];
      const name = (p.partName || '').trim();
      const label = name ? `Pièce n° ${i + 1} (« ${name} »)` : `Pièce n° ${i + 1}`;
      const quantity = Number(p.quantity);
      if (!Number.isInteger(quantity) || quantity <= 0) return `${label} : la quantité doit être un nombre entier supérieur à zéro.`;
      if (Number(p.unitPrice ?? 0) < 0) return `${label} : le prix unitaire ne peut pas être négatif.`;
    }
    return null;
  }

  /** Motif lisible d'un refus : le message métier du serveur tel quel, sinon un texte en français. */
  private saveErrorMessage(err: any): string {
    if (err?.status === 0) return 'Serveur injoignable : vérifiez la connexion puis réessayez.';
    if (err?.status < 500 && err?.error?.message) return err.error.message;
    if (err?.status === 404) return 'Réparation introuvable : elle a peut-être été supprimée. Rechargez la page.';
    if (err?.status >= 400 && err?.status < 500) return 'Réparation refusée : vérifiez les valeurs saisies puis réessayez.';
    return "Erreur serveur : la réparation n'a pas été enregistrée. Réessayez ; si le problème persiste, contactez le support.";
  }

  private showSaveError(err: any) {
    this.saveError = this.saveErrorMessage(err);
    this.cdr.detectChanges();
  }

  saveRepair() {
    if (!this.isFormValid()) return;
    this.saveError = null;

    // Un champ montant vidé vaut null, refusé par le serveur qui attend un nombre : 0 est ce que le total affiche.
    const parts = this.form.parts.map((p: RepairPart) => ({
      partName: p.partName,
      partReference: p.partReference,
      quantity: p.quantity,
      unitPrice: p.unitPrice ?? 0,
      notes: p.notes
    }));

    if (this.editingRepair) {
      this.apiService.updateRepair(this.editingRepair.id!, {
        vehicleId: +this.form.vehicleId,
        supplierId: this.form.supplierId ? +this.form.supplierId : undefined,
        description: this.form.description,
        repairDate: this.form.repairDate,
        mileageAtRepair: this.form.mileageAtRepair || undefined,
        laborCost: this.form.laborCost ?? 0,
        status: this.form.status,
        invoiceNumber: this.form.invoiceNumber,
        notes: this.form.notes,
        repairType: this.form.repairType || null,
        parts
      }).subscribe({
        next: () => {
          this.loadRepairs();
          this.closePanel();
        },
        error: (err) => this.showSaveError(err)
      });
    } else {
      this.apiService.createRepair({
        vehicleId: +this.form.vehicleId,
        supplierId: this.form.supplierId ? +this.form.supplierId : undefined,
        description: this.form.description,
        repairDate: this.form.repairDate,
        mileageAtRepair: this.form.mileageAtRepair || undefined,
        laborCost: this.form.laborCost ?? 0,
        invoiceNumber: this.form.invoiceNumber,
        notes: this.form.notes,
        repairType: this.form.repairType || null,
        parts
      }).subscribe({
        next: () => {
          this.loadRepairs();
          this.closePanel();
        },
        error: (err) => this.showSaveError(err)
      });
    }
  }

  confirmDelete(repair: Repair) {
    this.repairToDelete = repair;
    this.showDeleteConfirm = true;
    this.saveError = null;
  }

  cancelDelete() {
    this.repairToDelete = null;
    this.showDeleteConfirm = false;
    this.saveError = null;
  }

  deleteRepair() {
    if (this.repairToDelete && this.repairToDelete.id) {
      this.saveError = null;
      this.apiService.deleteRepair(this.repairToDelete.id).pipe(takeUntil(this.destroy$)).subscribe({
        next: () => {
          this.loadRepairs();
          this.cancelDelete();
        },
        // Un refus du serveur (réparation née d'un sinistre) fermait la fenêtre sans un mot
        // et la ligne restait : le client y voyait un bug. La fenêtre reste ouverte sur le motif.
        error: (err) => this.showSaveError(err)
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
