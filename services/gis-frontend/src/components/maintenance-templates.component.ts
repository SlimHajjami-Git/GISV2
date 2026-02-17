import { Component, OnInit, OnDestroy, NgZone, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';
import { AppLayoutComponent } from './shared/app-layout.component';
import { ApiService } from '../services/api.service';
import { trigger, transition, style, animate } from '@angular/animations';

interface MaintenanceTemplate {
  id: string;
  name: string;
  description: string;
  intervalKm: number | null;
  intervalMonths: number | null;
  estimatedCost: number;
  priority: 'low' | 'medium' | 'high' | 'critical';
  category: string;
  isActive: boolean;
  notifyKmBefore?: number | null;
  notifyDaysBefore?: number | null;
}

interface InvoiceLine {
  templateId: string | null;
  description: string;
  price: number | null;
  isCustom: boolean;
  isNewTemplate: boolean;
  newTemplateName: string;
  newTemplateCategory: string;
  newTemplateIntervalKm: number | null;
  newTemplateIntervalMonths: number | null;
}

interface VehicleMaintenanceStatus {
  vehicleId: string;
  vehicleName: string;
  vehiclePlate: string;
  currentMileage: number;
  maintenanceItems: MaintenanceItem[];
}

interface MaintenanceItem {
  scheduleId: number | null;
  templateId: string;
  templateName: string;
  lastDoneDate: Date | null;
  lastDoneKm: number | null;
  nextDueKm: number | null;
  status: 'ok' | 'upcoming' | 'due' | 'overdue';
  kmUntilDue: number | null;
}

interface FlatRow {
  vehicleId: string;
  vehicleName: string;
  vehiclePlate: string;
  currentMileage: number;
  templateId: string;
  templateName: string;
  scheduleId: number | null;
  lastDoneDate: Date | null;
  lastDoneKm: number | null;
  nextDueKm: number | null;
  status: 'ok' | 'upcoming' | 'due' | 'overdue';
  kmUntilDue: number | null;
  progressPercent: number;
}

@Component({
  selector: 'app-maintenance-templates',
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
        style({ transform: 'translateX(100%)' }),
        animate('300ms ease-out', style({ transform: 'translateX(0)' }))
      ])
    ])
  ],
  template: `
    <app-layout>
      <div class="page">
        <!-- Filter Bar -->
        <div class="filter-bar">
          <div class="search-wrapper">
            <svg class="search-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/></svg>
            <input type="text" class="search-input" placeholder="Rechercher vehicule ou entretien..." [(ngModel)]="searchQuery" (input)="rebuildRows()">
          </div>
          <select class="filter-select" [(ngModel)]="statusFilter" (change)="rebuildRows()">
            <option value="">Tous les statuts</option>
            <option value="overdue">En retard</option>
            <option value="due">A faire</option>
            <option value="upcoming">A venir</option>
            <option value="ok">OK</option>
          </select>
          <button class="btn-assign" (click)="openAssignPanel()">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M16 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="8.5" cy="7" r="4"/><line x1="20" y1="8" x2="20" y2="14"/><line x1="23" y1="11" x2="17" y2="11"/></svg>
            Affecter
          </button>
          <button class="btn-add" (click)="openTemplateForm()">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
            Nouveau modele
          </button>
        </div>

        <!-- Stats Bar -->
        <div class="stats-bar">
          <div class="stat-item">
            <div class="stat-icon info"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/><rect x="14" y="14" width="7" height="7"/></svg></div>
            <div class="stat-content"><span class="stat-value">{{ templates.length }}</span><span class="stat-label">Modeles</span></div>
          </div>
          <div class="stat-item">
            <div class="stat-icon danger"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg></div>
            <div class="stat-content"><span class="stat-value">{{ countByStatus('overdue') + countByStatus('due') }}</span><span class="stat-label">Urgents</span></div>
          </div>
          <div class="stat-item">
            <div class="stat-icon warning"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg></div>
            <div class="stat-content"><span class="stat-value">{{ countByStatus('upcoming') }}</span><span class="stat-label">A venir</span></div>
          </div>
          <div class="stat-item">
            <div class="stat-icon active"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg></div>
            <div class="stat-content"><span class="stat-value">{{ countByStatus('ok') }}</span><span class="stat-label">OK</span></div>
          </div>
          <div class="stat-item">
            <div class="stat-icon vehicles"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="1" y="3" width="15" height="13"/><polygon points="16 8 20 8 23 11 23 16 16 16 16 8"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/></svg></div>
            <div class="stat-content"><span class="stat-value">{{ vehicleSchedules.length }}</span><span class="stat-label">Vehicules</span></div>
          </div>
        </div>

        <!-- Template Pills -->
        <div class="pills-bar">
          <div class="pills-scroll">
            <button class="pill" [class.active]="!templateFilter" (click)="templateFilter = ''; rebuildRows()">Tous</button>
            @for (t of templates; track t.id) {
              <button class="pill" [class.active]="templateFilter === t.id" [class]="t.priority" (click)="templateFilter = templateFilter === t.id ? '' : t.id; rebuildRows()">
                <span class="pill-dot" [class]="t.priority"></span>
                {{ t.name }}
                <span class="pill-count" *ngIf="getTemplateCount(t.id) > 0">{{ getTemplateCount(t.id) }}</span>
              </button>
            }
          </div>
          <button class="pill-manage" (click)="showModels = !showModels" title="Gerer les modeles">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"/></svg>
          </button>
        </div>

        <!-- Models management panel (collapsible) -->
        <div class="models-panel" *ngIf="showModels" @fadeIn>
          <div class="models-grid">
            @for (t of filteredTemplates; track t.id) {
              <div class="model-chip" [class]="t.priority" [class.inactive]="!t.isActive" (click)="selectTemplate(t)">
                <div class="chip-left">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/></svg>
                  <div>
                    <span class="chip-name">{{ t.name }}</span>
                    <span class="chip-meta">{{ t.intervalKm ? (t.intervalKm | number) + ' km' : '' }}{{ t.intervalKm && t.intervalMonths ? ' / ' : '' }}{{ t.intervalMonths ? t.intervalMonths + ' mois' : '' }} · {{ t.category }}</span>
                  </div>
                </div>
                <span class="chip-cost">~{{ t.estimatedCost | number }} DT</span>
              </div>
            }
          </div>
        </div>

        <!-- Main Table -->
        <div class="table-wrap">
          <table class="main-table" *ngIf="filteredRows.length > 0">
            <thead>
              <tr>
                <th class="col-vehicle" (click)="sortBy('vehicleName')">Vehicule <span class="sort-icon">{{ getSortIcon('vehicleName') }}</span></th>
                <th class="col-maint" (click)="sortBy('templateName')">Entretien <span class="sort-icon">{{ getSortIcon('templateName') }}</span></th>
                <th class="col-progress">Progression</th>
                <th class="col-km" (click)="sortBy('kmUntilDue')">Km restants <span class="sort-icon">{{ getSortIcon('kmUntilDue') }}</span></th>
                <th class="col-status" (click)="sortBy('status')">Statut <span class="sort-icon">{{ getSortIcon('status') }}</span></th>
                <th class="col-date">Dernier</th>
                <th class="col-actions">Actions</th>
              </tr>
            </thead>
            <tbody>
              @for (row of filteredRows; track row.vehicleId + row.templateId) {
                <tr [class]="row.status">
                  <td class="col-vehicle">
                    <div class="cell-vehicle">
                      <div class="vehicle-avatar" [class]="row.status">{{ row.vehicleName.charAt(0) }}</div>
                      <div>
                        <span class="cell-name">{{ row.vehicleName }}</span>
                        <span class="cell-plate">{{ row.vehiclePlate }}</span>
                      </div>
                    </div>
                  </td>
                  <td class="col-maint">
                    <span class="cell-maint-name">{{ row.templateName }}</span>
                  </td>
                  <td class="col-progress">
                    <div class="progress-cell">
                      <div class="progress-track">
                        <div class="progress-fill" [class]="row.status" [style.width.%]="row.progressPercent"></div>
                      </div>
                      <span class="progress-pct" [class]="row.status">{{ row.progressPercent | number:'1.0-0' }}%</span>
                    </div>
                  </td>
                  <td class="col-km">
                    <span class="cell-km" [class]="row.status">
                      {{ row.kmUntilDue !== null ? (row.kmUntilDue > 0 ? (row.kmUntilDue | number) + ' km' : ((-row.kmUntilDue) | number) + ' km depasses') : '-' }}
                    </span>
                  </td>
                  <td class="col-status">
                    <span class="status-tag" [class]="row.status">{{ getStatusLabel(row.status) }}</span>
                  </td>
                  <td class="col-date">
                    <span class="cell-date">{{ row.lastDoneDate ? formatDate(row.lastDoneDate) : 'Jamais' }}</span>
                  </td>
                  <td class="col-actions">
                    <button class="btn-act history" (click)="openHistory(row)" title="Historique">
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg>
                    </button>
                    <button class="btn-act done" (click)="openMarkDoneFromRow(row)" title="Marquer fait">
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><polyline points="20 6 9 17 4 12"/></svg>
                    </button>
                    <button class="btn-act del" (click)="removeFromRow(row)" title="Retirer">
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </table>

          <div class="empty-state" *ngIf="filteredRows.length === 0">
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/></svg>
            <h3>Aucun entretien</h3>
            <p>Creez des modeles et assignez-les a vos vehicules</p>
          </div>
        </div>
      </div>

      <!-- ==================== PANELS ==================== -->

      <!-- Template Form Panel -->
      <div class="overlay" *ngIf="isFormOpen" @fadeIn (click)="closeForm()">
        <div class="panel" @slideIn (click)="$event.stopPropagation()">
          <div class="panel-head blue">
            <h2>{{ editing ? 'Modifier' : 'Nouveau' }} modele</h2>
            <button class="panel-close" (click)="closeForm()"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg></button>
          </div>
          <div class="panel-body">
            <div class="field"><label>Nom *</label><input [(ngModel)]="form.name" placeholder="Ex: Vidange moteur"></div>
            <div class="field"><label>Description</label><textarea [(ngModel)]="form.description" rows="2" placeholder="Description..."></textarea></div>
            <div class="field-row">
              <div class="field"><label>Categorie *</label><select [(ngModel)]="form.category"><option value="">Choisir</option><option *ngFor="let c of categories" [value]="c">{{c}}</option></select></div>
              <div class="field"><label>Priorite</label><select [(ngModel)]="form.priority"><option value="low">Faible</option><option value="medium">Moyenne</option><option value="high">Haute</option><option value="critical">Critique</option></select></div>
            </div>
            <div class="field-row">
              <div class="field"><label>Intervalle (km)</label><input type="number" [(ngModel)]="form.intervalKm" placeholder="10000"></div>
              <div class="field"><label>Intervalle (mois)</label><input type="number" [(ngModel)]="form.intervalMonths" placeholder="12"></div>
            </div>
            <div class="field toggle"><label><input type="checkbox" [(ngModel)]="form.isActive"><span class="switch"></span> Actif</label></div>
            <div class="divider"></div>
            <h4 class="sub-title">Notifications (optionnel)</h4>
            <div class="field-row">
              <div class="field"><label>Notifier a X km restants</label><input type="number" [(ngModel)]="form.notifyKmBefore" placeholder="1000"></div>
              <div class="field"><label>Notifier X jours avant</label><input type="number" [(ngModel)]="form.notifyDaysBefore" placeholder="7"></div>
            </div>
          </div>
          <div class="panel-foot"><button class="btn-cancel" (click)="closeForm()">Annuler</button><button class="btn-save" (click)="saveTemplate()" [disabled]="!isFormValid()">Enregistrer</button></div>
        </div>
      </div>

      <!-- Mark Done Panel -->
      <div class="overlay" *ngIf="isMarkOpen" @fadeIn (click)="closeMarkDone()">
        <div class="panel" @slideIn (click)="$event.stopPropagation()">
          <div class="panel-head green">
            <h2>Entretien effectue</h2>
            <button class="panel-close" (click)="closeMarkDone()"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg></button>
          </div>
          <div class="panel-body">
            <div class="recap-box">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#64748b" stroke-width="2"><rect x="1" y="3" width="15" height="13"/><polygon points="16 8 20 8 23 11 23 16 16 16 16 8"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/></svg>
              <div><strong>{{ markData.vehicleName }}</strong><br><span class="text-muted">{{ markData.vehiclePlate }}</span></div>
            </div>
            <div class="field-row">
              <div class="field"><label>Date *</label><input type="date" [(ngModel)]="markData.date"></div>
              <div class="field"><label>Kilometrage *</label><input type="number" [(ngModel)]="markData.mileage" placeholder="45000"></div>
            </div>
            <div class="field"><label>Fournisseur / Garage</label><input [(ngModel)]="markData.supplier" placeholder="Nom du prestataire..."></div>
            <div class="invoice-section">
              <h4 class="sub-title">Detail de la facture</h4>
              <div class="invoice-lines">
                <div class="inv-line" *ngFor="let line of markData.invoiceLines; let i = index">
                  <div class="inv-top">
                    <select [ngModel]="line.templateId || (line.isNewTemplate ? 'new' : (line.isCustom ? 'other' : ''))" (ngModelChange)="onLineTemplateChange(line, $event)">
                      <option value="" disabled>Choisir...</option>
                      <optgroup label="Entretiens du vehicule"><option *ngFor="let t of getVehicleMaintenanceTemplates()" [value]="t.id">{{ t.name }}</option></optgroup>
                      <optgroup label="Autres modeles"><option *ngFor="let t of getOtherTemplatesForDropdown()" [value]="t.id">{{ t.name }}</option></optgroup>
                      <option value="new">+ Creer un nouveau modele</option>
                      <option value="other">Autre (saisie libre)</option>
                    </select>
                    <button class="btn-act del" (click)="removeInvoiceLine(i)" *ngIf="markData.invoiceLines.length > 1"><svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg></button>
                  </div>
                  <div class="new-tpl-fields" *ngIf="line.isNewTemplate">
                    <div class="field"><label>Nom du modele *</label><input [(ngModel)]="line.newTemplateName" placeholder="Ex: Courroie distribution"></div>
                    <div class="field-row">
                      <div class="field"><label>Categorie</label><select [(ngModel)]="line.newTemplateCategory"><option value="">Choisir</option><option *ngFor="let c of categories" [value]="c">{{c}}</option></select></div>
                      <div class="field"><label>Intervalle (km)</label><input type="number" [(ngModel)]="line.newTemplateIntervalKm" placeholder="10000"></div>
                    </div>
                    <div class="new-tpl-hint">
                      <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/></svg>
                      Ce modele sera cree et automatiquement affecte a ce vehicule
                    </div>
                  </div>
                  <div class="inv-bottom">
                    <input class="inv-desc" [(ngModel)]="line.description" [placeholder]="line.isCustom ? 'Description...' : (line.isNewTemplate ? line.newTemplateName || 'Nom...' : line.description)" [readonly]="!line.isCustom && !line.isNewTemplate && line.templateId">
                    <div class="inv-price"><input type="number" [(ngModel)]="line.price" placeholder="0"><span>DT</span></div>
                  </div>
                  <div class="inv-hint" *ngIf="line.templateId && lastPaidPrices.get(line.templateId)">Dernier prix: {{ lastPaidPrices.get(line.templateId) | number }} DT</div>
                </div>
              </div>
              <button class="btn-add-line" (click)="addInvoiceLine()"><svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg> Ajouter une ligne</button>
              <div class="inv-total"><span>Total</span><strong>{{ getInvoiceTotal() | number:'1.2-2' }} DT</strong></div>
            </div>
            <div class="field"><label>Notes</label><textarea [(ngModel)]="markData.notes" rows="2" placeholder="Remarques..."></textarea></div>
          </div>
          <div class="panel-foot"><button class="btn-cancel" (click)="closeMarkDone()">Annuler</button><button class="btn-save green" (click)="confirmMarkDone()" [disabled]="!isMarkValid()">Confirmer</button></div>
        </div>
      </div>

      <!-- Detail Panel -->
      <div class="overlay" *ngIf="selected" @fadeIn (click)="closeDetail()">
        <div class="panel" @slideIn (click)="$event.stopPropagation()">
          <div class="panel-head" [class]="selected.priority">
            <h2>{{ selected.name }}</h2>
            <button class="panel-close" (click)="closeDetail()"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg></button>
          </div>
          <div class="panel-body">
            <div class="detail-badges">
              <span class="priority-badge" [class]="selected.priority">{{ getPriorityLabel(selected.priority) }}</span>
              <span class="active-tag" [class.off]="!selected.isActive">{{ selected.isActive ? 'Actif' : 'Inactif' }}</span>
            </div>
            <div class="detail-section" *ngIf="selected.description"><h4>Description</h4><p>{{ selected.description }}</p></div>
            <div class="detail-section">
              <h4>Intervalles</h4>
              <div class="detail-intervals">
                <div class="int-card" *ngIf="selected.intervalKm"><span class="int-val">{{ selected.intervalKm | number }}</span><span class="int-unit">km</span></div>
                <span class="int-or" *ngIf="selected.intervalKm && selected.intervalMonths">ou</span>
                <div class="int-card" *ngIf="selected.intervalMonths"><span class="int-val">{{ selected.intervalMonths }}</span><span class="int-unit">mois</span></div>
              </div>
            </div>
          </div>
          <div class="panel-foot">
            <button class="btn-cancel" (click)="closeDetail()">Fermer</button>
            <button class="btn-save" (click)="editTemplate(selected)">Modifier</button>
            <button class="btn-delete-confirm" (click)="deleteTemplate(selected)">Supprimer</button>
          </div>
        </div>
      </div>

      <!-- Add to Vehicle Panel -->
      <div class="overlay" *ngIf="isAddToVehicleOpen" @fadeIn (click)="closeAddToVehicle()">
        <div class="panel" @slideIn (click)="$event.stopPropagation()">
          <div class="panel-head purple">
            <h2>Affecter des entretiens</h2>
            <button class="panel-close" (click)="closeAddToVehicle()"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg></button>
          </div>
          <div class="panel-body">
            <div class="field"><label>Vehicule *</label>
              <select [(ngModel)]="addToVehicleData.vehicleId" (ngModelChange)="onAssignVehicleChange($event)">
                <option value="">Choisir un vehicule...</option>
                <option *ngFor="let v of allVehicles" [value]="v.id">{{ v.name }} - {{ v.plate }}</option>
              </select>
            </div>
            <div class="recap-box" *ngIf="addToVehicleData.vehicleId">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#64748b" stroke-width="2"><rect x="1" y="3" width="15" height="13"/><polygon points="16 8 20 8 23 11 23 16 16 16 16 8"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/></svg>
              <div><strong>{{ addToVehicleData.vehicleName }}</strong><br><span class="text-muted">{{ addToVehicleData.vehiclePlate }} - {{ addToVehicleData.vehicleMileage | number }} km</span></div>
            </div>
            <div class="tpl-list" *ngIf="addToVehicleData.vehicleId">
              @for (t of getAvailableTemplatesForVehicle(); track t.id) {
                <div class="tpl-item" [class.selected]="isTemplateSelected(t.id)" (click)="toggleTemplateSelection(t)">
                  <div class="tpl-check" [class.checked]="isTemplateSelected(t.id)"><svg *ngIf="isTemplateSelected(t.id)" width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3"><polyline points="20 6 9 17 4 12"/></svg></div>
                  <div class="tpl-item-info"><span class="tpl-item-name">{{ t.name }}</span><span class="tpl-item-interval">{{ t.intervalKm ? (t.intervalKm | number) + ' km' : '' }}{{ t.intervalKm && t.intervalMonths ? ' / ' : '' }}{{ t.intervalMonths ? t.intervalMonths + ' mois' : '' }}</span></div>
                  <span class="tpl-item-cost">~{{ t.estimatedCost }} DT</span>
                </div>
              }
              <div class="empty-state small" *ngIf="getAvailableTemplatesForVehicle().length === 0"><p>Tous les entretiens sont deja assignes</p></div>
            </div>
          </div>
          <div class="panel-foot">
            <span class="foot-info" *ngIf="addToVehicleData.selectedTemplateIds.length > 0">{{ addToVehicleData.selectedTemplateIds.length }} selectionne(s)</span>
            <button class="btn-cancel" (click)="closeAddToVehicle()">Annuler</button>
            <button class="btn-save purple" (click)="confirmAddToVehicle()" [disabled]="addToVehicleData.selectedTemplateIds.length === 0">Ajouter</button>
          </div>
        </div>
      </div>

      <!-- History Panel -->
      <div class="overlay" *ngIf="isHistoryOpen" @fadeIn (click)="closeHistory()">
        <div class="panel" @slideIn (click)="$event.stopPropagation()">
          <div class="panel-head orange">
            <h2>Historique</h2>
            <button class="panel-close" (click)="closeHistory()"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg></button>
          </div>
          <div class="panel-body">
            <div class="recap-box">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#64748b" stroke-width="2"><rect x="1" y="3" width="15" height="13"/><polygon points="16 8 20 8 23 11 23 16 16 16 16 8"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/></svg>
              <div><strong>{{ historyVehicleName }}</strong> — {{ historyTemplateName }}<br><span class="text-muted">{{ historyVehiclePlate }}</span></div>
            </div>
            <div class="history-loading" *ngIf="historyLoading">Chargement...</div>
            <div class="history-list" *ngIf="!historyLoading && historyLogs.length > 0">
              <div class="history-item" *ngFor="let log of historyLogs">
                <div class="history-dot"></div>
                <div class="history-content">
                  <div class="history-date">{{ formatDate(log.doneDate) }}</div>
                  <div class="history-details">
                    <span class="history-km">{{ log.doneKm | number }} km</span>
                    <span class="history-cost">{{ log.actualCost | number:'1.2-2' }} DT</span>
                    <span class="history-supplier" *ngIf="log.supplierName">{{ log.supplierName }}</span>
                  </div>
                  <div class="history-notes" *ngIf="log.notes">{{ log.notes }}</div>
                </div>
              </div>
            </div>
            <div class="empty-state small" *ngIf="!historyLoading && historyLogs.length === 0">
              <p>Aucun historique pour cet entretien</p>
            </div>
          </div>
          <div class="panel-foot"><button class="btn-cancel" (click)="closeHistory()">Fermer</button></div>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    .page { flex:1; background:#f1f5f9; display:flex; flex-direction:column; min-height:calc(100vh - 42px); }

    /* Filter Bar */
    .filter-bar { display:flex; align-items:center; gap:12px; padding:10px 14px; background:white; border-bottom:1px solid #e2e8f0; }
    .search-wrapper { position:relative; flex:1; max-width:300px; }
    .search-icon { position:absolute; left:10px; top:50%; transform:translateY(-50%); color:#94a3b8; }
    .search-input { width:100%; padding:6px 10px 6px 32px; font-size:12px; border:1px solid #e2e8f0; border-radius:3px; background:white; color:#1e293b; }
    .search-input:focus { outline:none; border-color:#3b82f6; }
    .filter-select { padding:6px 10px; background:white; border:1px solid #e2e8f0; border-radius:3px; color:#1e293b; font-size:12px; cursor:pointer; }
    .btn-assign { display:flex; align-items:center; gap:6px; padding:6px 12px; background:#7c3aed; color:white; border:none; border-radius:3px; font-size:12px; font-weight:500; cursor:pointer; margin-left:auto; }
    .btn-assign:hover { background:#6d28d9; }
    .btn-add { display:flex; align-items:center; gap:6px; padding:6px 12px; background:#3b82f6; color:white; border:none; border-radius:3px; font-size:12px; font-weight:500; cursor:pointer; }
    .btn-add:hover { background:#2563eb; }

    /* Stats Bar */
    .stats-bar { display:flex; gap:16px; padding:12px 14px; background:white; border-bottom:1px solid #e2e8f0; }
    .stat-item { display:flex; align-items:center; gap:10px; padding:8px 14px; background:#f8fafc; border-radius:6px; }
    .stat-icon { width:32px; height:32px; border-radius:6px; display:flex; align-items:center; justify-content:center; }
    .stat-icon.info { background:#dbeafe; color:#2563eb; }
    .stat-icon.danger { background:#fee2e2; color:#dc2626; }
    .stat-icon.warning { background:#fef3c7; color:#d97706; }
    .stat-icon.active { background:#dcfce7; color:#16a34a; }
    .stat-icon.vehicles { background:#f3e8ff; color:#7c3aed; }
    .stat-content { display:flex; flex-direction:column; }
    .stat-value { font-size:16px; font-weight:600; color:#1e293b; }
    .stat-label { font-size:11px; color:#64748b; }

    /* Template Pills */
    .pills-bar { display:flex; align-items:center; gap:8px; padding:10px 14px; background:white; border-bottom:1px solid #e2e8f0; }
    .pills-scroll { display:flex; gap:6px; flex:1; overflow-x:auto; padding-bottom:2px; }
    .pills-scroll::-webkit-scrollbar { height:3px; }
    .pills-scroll::-webkit-scrollbar-thumb { background:#cbd5e1; border-radius:3px; }
    .pill { display:flex; align-items:center; gap:5px; padding:5px 12px; border:1px solid #e2e8f0; border-radius:20px; background:white; font-size:11px; font-weight:500; color:#64748b; cursor:pointer; white-space:nowrap; transition:all .15s; }
    .pill:hover { border-color:#94a3b8; color:#1e293b; }
    .pill.active { background:#1e3a5f; border-color:#1e3a5f; color:white; }
    .pill-dot { width:6px; height:6px; border-radius:50%; }
    .pill-dot.low { background:#22c55e; }
    .pill-dot.medium { background:#f59e0b; }
    .pill-dot.high { background:#ef4444; }
    .pill-dot.critical { background:#1e293b; }
    .pill.active .pill-dot { background:white; }
    .pill-count { background:rgba(0,0,0,.08); padding:1px 6px; border-radius:8px; font-size:10px; }
    .pill.active .pill-count { background:rgba(255,255,255,.2); }
    .pill-manage { width:32px; height:32px; border:1px solid #e2e8f0; border-radius:6px; background:white; color:#64748b; cursor:pointer; display:flex; align-items:center; justify-content:center; flex-shrink:0; }
    .pill-manage:hover { background:#f1f5f9; }

    /* Models Panel */
    .models-panel { padding:10px 14px; background:#f8fafc; border-bottom:1px solid #e2e8f0; }
    .models-grid { display:flex; flex-wrap:wrap; gap:8px; }
    .model-chip { display:flex; align-items:center; justify-content:space-between; gap:12px; padding:10px 14px; background:white; border:1px solid #e2e8f0; border-radius:6px; cursor:pointer; transition:all .15s; min-width:240px; flex:1; max-width:360px; }
    .model-chip:hover { border-color:#3b82f6; box-shadow:0 2px 8px rgba(0,0,0,.06); }
    .model-chip.inactive { opacity:.5; }
    .model-chip.low { border-left:3px solid #22c55e; }
    .model-chip.medium { border-left:3px solid #f59e0b; }
    .model-chip.high { border-left:3px solid #ef4444; }
    .model-chip.critical { border-left:3px solid #1e293b; }
    .chip-left { display:flex; align-items:center; gap:10px; color:#475569; }
    .chip-name { display:block; font-size:12px; font-weight:600; color:#1e293b; }
    .chip-meta { font-size:10px; color:#94a3b8; }
    .chip-cost { font-size:12px; font-weight:600; color:#1e293b; white-space:nowrap; }

    /* Main Table */
    .table-wrap { flex:1; padding:12px; overflow:auto; }
    .main-table { width:100%; border-collapse:separate; border-spacing:0; background:white; border-radius:8px; border:1px solid #e2e8f0; overflow:hidden; }
    .main-table thead th { padding:10px 14px; font-size:11px; font-weight:600; color:#64748b; text-transform:uppercase; letter-spacing:.3px; background:#f8fafc; border-bottom:1px solid #e2e8f0; text-align:left; cursor:pointer; user-select:none; white-space:nowrap; }
    .main-table thead th:hover { color:#1e293b; }
    .sort-icon { font-size:10px; color:#94a3b8; }
    .main-table tbody tr { transition:background .1s; }
    .main-table tbody tr:hover { background:#f8fafc; }
    .main-table tbody tr.overdue { background:#fef2f2; }
    .main-table tbody tr.overdue:hover { background:#fee2e2; }
    .main-table tbody tr.due { background:#fffbeb; }
    .main-table tbody tr.due:hover { background:#fef3c7; }
    .main-table tbody td { padding:10px 14px; font-size:12px; color:#475569; border-bottom:1px solid #f1f5f9; vertical-align:middle; }
    .main-table tbody tr:last-child td { border-bottom:none; }
    .col-vehicle { min-width:180px; }
    .col-maint { min-width:120px; }
    .col-progress { min-width:140px; }
    .col-km { min-width:110px; }
    .col-status { min-width:90px; }
    .col-date { min-width:80px; }
    .col-actions { min-width:70px; }

    /* Table cells */
    .cell-vehicle { display:flex; align-items:center; gap:10px; }
    .vehicle-avatar { width:32px; height:32px; border-radius:6px; display:flex; align-items:center; justify-content:center; font-size:13px; font-weight:700; color:white; flex-shrink:0; }
    .vehicle-avatar.ok { background:#22c55e; }
    .vehicle-avatar.upcoming { background:#3b82f6; }
    .vehicle-avatar.due { background:#f59e0b; }
    .vehicle-avatar.overdue { background:#dc2626; }
    .cell-name { display:block; font-size:12px; font-weight:600; color:#1e293b; }
    .cell-plate { font-family:monospace; font-size:10px; color:#94a3b8; }
    .cell-maint-name { font-weight:500; color:#1e293b; }
    .progress-cell { display:flex; align-items:center; gap:8px; }
    .progress-track { flex:1; height:6px; background:#e2e8f0; border-radius:3px; overflow:hidden; min-width:60px; }
    .progress-fill { height:100%; border-radius:3px; transition:width .4s ease; }
    .progress-fill.ok { background:#22c55e; }
    .progress-fill.upcoming { background:#3b82f6; }
    .progress-fill.due { background:#f59e0b; }
    .progress-fill.overdue { background:#dc2626; }
    .progress-pct { font-size:11px; font-weight:600; min-width:30px; }
    .progress-pct.ok { color:#16a34a; }
    .progress-pct.upcoming { color:#3b82f6; }
    .progress-pct.due { color:#d97706; }
    .progress-pct.overdue { color:#dc2626; }
    .cell-km { font-weight:500; white-space:nowrap; }
    .cell-km.ok { color:#16a34a; }
    .cell-km.upcoming { color:#3b82f6; }
    .cell-km.due { color:#d97706; }
    .cell-km.overdue { color:#dc2626; }
    .status-tag { padding:3px 8px; border-radius:3px; font-size:10px; font-weight:600; white-space:nowrap; }
    .status-tag.ok { background:#dcfce7; color:#16a34a; }
    .status-tag.upcoming { background:#dbeafe; color:#2563eb; }
    .status-tag.due { background:#fef3c7; color:#d97706; }
    .status-tag.overdue { background:#fee2e2; color:#dc2626; }
    .cell-date { font-size:11px; color:#94a3b8; }
    .btn-act { width:28px; height:28px; border-radius:4px; display:flex; align-items:center; justify-content:center; cursor:pointer; border:1px solid #e2e8f0; background:white; transition:all .15s; }
    .btn-act.done { color:#16a34a; }
    .btn-act.done:hover { background:#dcfce7; border-color:#16a34a; }
    .btn-act.history { color:#f59e0b; }
    .btn-act.history:hover { background:#fef3c7; border-color:#f59e0b; }
    .btn-act.del { color:#dc2626; }
    .btn-act.del:hover { background:#fee2e2; border-color:#dc2626; }

    /* Empty */
    .empty-state { text-align:center; padding:60px 20px; color:#94a3b8; }
    .empty-state.small { padding:20px; font-size:12px; }
    .empty-state svg { margin-bottom:12px; }
    .empty-state h3 { margin:0 0 4px; font-size:14px; font-weight:600; color:#64748b; }
    .empty-state p { margin:0; font-size:12px; }

    /* Panels */
    .overlay { position:fixed; inset:0; background:rgba(0,0,0,.4); display:flex; justify-content:flex-end; z-index:1000; }
    .panel { width:100%; max-width:460px; height:100vh; background:white; display:flex; flex-direction:column; box-shadow:-4px 0 20px rgba(0,0,0,.1); }
    .panel-head { display:flex; justify-content:space-between; align-items:center; padding:18px 20px; color:white; }
    .panel-head.blue { background:linear-gradient(135deg,#3b82f6,#2563eb); }
    .panel-head.green { background:linear-gradient(135deg,#22c55e,#16a34a); }
    .panel-head.purple { background:linear-gradient(135deg,#8b5cf6,#7c3aed); }
    .panel-head.orange { background:linear-gradient(135deg,#f59e0b,#d97706); }
    .panel-head.low { background:linear-gradient(135deg,#22c55e,#16a34a); }
    .panel-head.medium { background:linear-gradient(135deg,#f59e0b,#d97706); }
    .panel-head.high { background:linear-gradient(135deg,#ef4444,#dc2626); }
    .panel-head.critical { background:linear-gradient(135deg,#1e293b,#0f172a); }
    .panel-head h2 { margin:0; font-size:16px; font-weight:600; }
    .panel-close { background:rgba(255,255,255,.2); border:none; width:28px; height:28px; border-radius:6px; color:white; cursor:pointer; display:flex; align-items:center; justify-content:center; }
    .panel-body { flex:1; overflow-y:auto; padding:20px; }
    .panel-foot { display:flex; justify-content:flex-end; align-items:center; gap:8px; padding:14px 20px; border-top:1px solid #e2e8f0; background:#f8fafc; }

    /* History */
    .history-loading { text-align:center; padding:20px; color:#94a3b8; font-size:12px; }
    .history-list { display:flex; flex-direction:column; gap:0; border-left:2px solid #e2e8f0; margin-left:10px; padding-left:0; }
    .history-item { display:flex; gap:12px; padding:12px 0 12px 16px; position:relative; }
    .history-item:not(:last-child) { border-bottom:1px solid #f1f5f9; }
    .history-dot { width:10px; height:10px; border-radius:50%; background:#f59e0b; border:2px solid white; box-shadow:0 0 0 2px #f59e0b; flex-shrink:0; margin-top:4px; position:absolute; left:-7px; }
    .history-content { margin-left:12px; flex:1; }
    .history-date { font-size:13px; font-weight:600; color:#1e293b; margin-bottom:4px; }
    .history-details { display:flex; gap:12px; flex-wrap:wrap; }
    .history-km { font-size:12px; color:#3b82f6; font-weight:500; background:#eff6ff; padding:2px 8px; border-radius:3px; }
    .history-cost { font-size:12px; color:#16a34a; font-weight:500; background:#f0fdf4; padding:2px 8px; border-radius:3px; }
    .history-supplier { font-size:12px; color:#7c3aed; font-weight:500; background:#f5f3ff; padding:2px 8px; border-radius:3px; }
    .history-notes { font-size:11px; color:#94a3b8; margin-top:4px; font-style:italic; }

    /* Fields */
    .field { margin-bottom:14px; }
    .field label { display:block; font-size:11px; font-weight:600; color:#64748b; margin-bottom:5px; }
    .field input, .field select, .field textarea { width:100%; padding:8px 10px; border:1px solid #e2e8f0; border-radius:3px; font-size:12px; color:#1e293b; }
    .field input:focus, .field select:focus, .field textarea:focus { outline:none; border-color:#3b82f6; }
    .field-row { display:grid; grid-template-columns:1fr 1fr; gap:10px; }
    .field.toggle label { display:flex; align-items:center; gap:8px; cursor:pointer; font-size:12px; color:#1e293b; }
    .field.toggle input { display:none; width:auto; }
    .switch { width:36px; height:20px; background:#e2e8f0; border-radius:10px; position:relative; transition:background .2s; }
    .switch::after { content:''; position:absolute; width:16px; height:16px; background:white; border-radius:50%; top:2px; left:2px; transition:transform .2s; }
    .field.toggle input:checked + .switch { background:#3b82f6; }
    .field.toggle input:checked + .switch::after { transform:translateX(16px); }
    .divider { height:1px; background:#e2e8f0; margin:16px 0; }
    .sub-title { font-size:12px; font-weight:600; color:#1e293b; margin:0 0 12px; }
    .text-muted { color:#94a3b8; font-size:11px; }

    /* Buttons */
    .btn-cancel { padding:8px 14px; background:white; border:1px solid #e2e8f0; border-radius:3px; font-size:12px; color:#64748b; cursor:pointer; }
    .btn-save { padding:8px 16px; background:#3b82f6; border:none; border-radius:3px; font-size:12px; font-weight:500; color:white; cursor:pointer; }
    .btn-save:disabled { opacity:.4; cursor:not-allowed; }
    .btn-save.green { background:#16a34a; }
    .btn-save.purple { background:#7c3aed; }
    .btn-delete-confirm { padding:8px 14px; background:white; border:1px solid #fecaca; border-radius:3px; font-size:12px; color:#dc2626; cursor:pointer; }
    .priority-badge { padding:3px 8px; border-radius:3px; font-size:10px; font-weight:600; }
    .priority-badge.low { background:#dcfce7; color:#16a34a; }
    .priority-badge.medium { background:#fef3c7; color:#d97706; }
    .priority-badge.high { background:#fee2e2; color:#dc2626; }
    .priority-badge.critical { background:#1e293b; color:white; }
    .active-tag { padding:4px 10px; border-radius:3px; font-size:11px; font-weight:600; background:#dcfce7; color:#16a34a; }
    .active-tag.off { background:#f1f5f9; color:#94a3b8; }
    .detail-badges { display:flex; gap:8px; margin-bottom:16px; }
    .detail-section { margin-bottom:18px; }
    .detail-section h4 { font-size:11px; font-weight:600; color:#94a3b8; margin:0 0 8px; text-transform:uppercase; }
    .detail-section p { font-size:12px; color:#475569; margin:0; padding:10px; background:#f8fafc; border-radius:6px; border:1px solid #e2e8f0; }
    .detail-intervals { display:flex; align-items:center; gap:12px; }
    .int-card { flex:1; padding:14px; background:#f8fafc; border-radius:6px; text-align:center; border:1px solid #e2e8f0; }
    .int-val { display:block; font-size:22px; font-weight:700; color:#1e293b; }
    .int-unit { font-size:11px; color:#64748b; }
    .int-or { font-size:11px; font-weight:600; color:#94a3b8; }

    /* Invoice */
    .recap-box { display:flex; align-items:center; gap:12px; padding:12px; background:#f8fafc; border-radius:6px; margin-bottom:16px; border:1px solid #e2e8f0; }
    .recap-box strong { font-size:13px; color:#1e293b; }
    .invoice-section { background:#f8fafc; border-radius:6px; padding:14px; margin-bottom:14px; border:1px solid #e2e8f0; }
    .invoice-lines { display:flex; flex-direction:column; gap:8px; }
    .inv-line { background:white; border-radius:6px; padding:10px; border:1px solid #e2e8f0; }
    .inv-top { display:flex; gap:6px; align-items:center; margin-bottom:8px; }
    .inv-top select { flex:1; padding:6px 10px; border:1px solid #e2e8f0; border-radius:3px; font-size:12px; }
    .inv-bottom { display:flex; gap:8px; align-items:center; }
    .inv-desc { flex:1; padding:6px 10px; border:1px solid #e2e8f0; border-radius:3px; font-size:12px; background:#f8fafc; }
    .inv-desc:not([readonly]) { background:white; }
    .inv-price { display:flex; align-items:center; gap:4px; }
    .inv-price input { width:80px; padding:6px 10px; border:1px solid #e2e8f0; border-radius:3px; font-size:12px; text-align:right; }
    .inv-price span { font-size:11px; color:#64748b; }
    .inv-hint { margin-top:6px; padding:6px 8px; background:#dbeafe; border-radius:3px; font-size:10px; color:#1d4ed8; }
    .new-tpl-fields { background:#f0fdf4; border:1px solid #bbf7d0; border-radius:6px; padding:10px; margin-bottom:8px; }
    .new-tpl-fields .field { margin-bottom:8px; }
    .new-tpl-fields .field:last-of-type { margin-bottom:0; }
    .new-tpl-hint { display:flex; align-items:center; gap:6px; font-size:10px; color:#16a34a; margin-top:8px; padding:6px 8px; background:#dcfce7; border-radius:3px; }
    .btn-add-line { display:flex; align-items:center; justify-content:center; gap:6px; width:100%; padding:8px; border:1px dashed #cbd5e1; border-radius:3px; background:transparent; color:#64748b; font-size:11px; cursor:pointer; margin-top:8px; }
    .inv-total { display:flex; justify-content:space-between; align-items:center; padding-top:12px; border-top:1px solid #e2e8f0; margin-top:12px; }
    .inv-total span { font-size:12px; color:#64748b; }
    .inv-total strong { font-size:18px; color:#16a34a; }

    /* Add to vehicle */
    .tpl-list { display:flex; flex-direction:column; gap:6px; }
    .tpl-item { display:flex; align-items:center; gap:10px; padding:10px 12px; background:white; border:2px solid #e2e8f0; border-radius:6px; cursor:pointer; transition:all .15s; }
    .tpl-item:hover { border-color:#8b5cf6; }
    .tpl-item.selected { border-color:#8b5cf6; background:#faf5ff; }
    .tpl-check { width:20px; height:20px; border:2px solid #e2e8f0; border-radius:4px; display:flex; align-items:center; justify-content:center; color:white; flex-shrink:0; }
    .tpl-check.checked { background:#7c3aed; border-color:#7c3aed; }
    .tpl-item-info { flex:1; }
    .tpl-item-name { display:block; font-size:12px; font-weight:600; color:#1e293b; }
    .tpl-item-interval { font-size:10px; color:#94a3b8; }
    .tpl-item-cost { font-size:11px; font-weight:600; color:#64748b; }
    .foot-info { flex:1; font-size:12px; color:#64748b; }

    @media (max-width:768px) { .field-row { grid-template-columns:1fr; } .stats-bar { flex-wrap:wrap; gap:8px; } }
  `]
})
export class MaintenanceTemplatesComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  templates: MaintenanceTemplate[] = [];
  filteredTemplates: MaintenanceTemplate[] = [];
  selected: MaintenanceTemplate | null = null;
  editing: MaintenanceTemplate | null = null;
  isFormOpen = false;
  searchQuery = '';
  categoryFilter = '';
  statusFilter = '';
  templateFilter = '';
  categories = ['Moteur', 'Freinage', 'Transmission', 'Filtres', 'Electrique', 'Suspension', 'Autre'];
  form: any = this.getEmptyForm();
  vehicleSchedules: VehicleMaintenanceStatus[] = [];
  filteredVehicleSchedules: VehicleMaintenanceStatus[] = [];
  expanded: string[] = [];
  vehicleSearchQuery = '';
  isMarkOpen = false;
  markData: any = this.getEmptyMark();
  isAddToVehicleOpen = false;
  addToVehicleData: any = this.getEmptyAddToVehicle();
  allVehicles: {id: string; name: string; plate: string; mileage: number}[] = [];
  showModels = false;
  loading = false;
  lastPaidPrices: Map<string, number> = new Map();
  isHistoryOpen = false;
  historyLoading = false;
  historyLogs: any[] = [];
  historyVehicleName = '';
  historyVehiclePlate = '';
  historyTemplateName = '';
  sortColumn = 'status';
  sortDir: 'asc' | 'desc' = 'asc';
  allRows: FlatRow[] = [];
  filteredRows: FlatRow[] = [];
  activeTab: 'templates' | 'schedule' = 'templates';
  scheduleView: 'agenda' | 'vehicles' = 'agenda';
  showOkItems = false;
  urgentItems: any[] = [];
  soonItems: any[] = [];
  okItems: any[] = [];

  constructor(private apiService: ApiService, private ngZone: NgZone, private cdr: ChangeDetectorRef) {}

  ngOnInit() { this.loadTemplates(); this.loadVehicles(); this.loadAllVehicles(); }

  getEmptyForm() { return { name:'', description:'', category:'', priority:'medium', intervalKm:null, intervalMonths:null, estimatedCost:0, isActive:true, notifyKmBefore:null, notifyDaysBefore:null }; }
  getEmptyMark() { 
    return { vehicleId:'', vehicleName:'', vehiclePlate:'', templateId:'', maintenanceName:'', date:new Date().toISOString().split('T')[0], mileage:null, supplier:'', notes:'', invoiceLines: [] as InvoiceLine[] }; 
  }
  getEmptyAddToVehicle() { return { vehicleId:'', vehicleName:'', vehiclePlate:'', vehicleMileage:0, selectedTemplateIds:[] as string[] }; }

  loadTemplates() {
    this.loading = true;
    this.apiService.getMaintenanceTemplates({ pageSize: 100 }).pipe(takeUntil(this.destroy$)).subscribe({
      next: (result) => {
        this.ngZone.run(() => {
          this.templates = result.items.map(t => ({ id: t.id?.toString() || '', name: t.name, description: t.description || '', intervalKm: t.intervalKm ?? null, intervalMonths: t.intervalMonths ?? null, estimatedCost: t.estimatedCost || 0, priority: (t.priority as any) || 'medium', category: t.category || 'Autre', isActive: t.isActive }));
          this.filteredTemplates = [...this.templates];
          this.loading = false;
          this.rebuildRows();
          this.cdr.detectChanges();
        });
      },
      error: (err) => { console.error('Error loading templates:', err); this.loading = false; }
    });
  }

  loadVehicles() {
    this.apiService.getVehicleMaintenanceSchedule({ pageSize: 100 }).pipe(takeUntil(this.destroy$)).subscribe({
      next: (result) => {
        this.ngZone.run(() => {
          this.vehicleSchedules = result.items.map(v => ({
            vehicleId: v.vehicleId.toString(), vehicleName: v.vehicleName || '', vehiclePlate: v.vehiclePlate || '', currentMileage: v.currentMileage || 0,
            maintenanceItems: (v.maintenanceItems || []).map(m => ({ scheduleId: m.scheduleId ?? null, templateId: m.templateId?.toString() || '', templateName: m.templateName || '', lastDoneDate: m.lastDoneDate ? new Date(m.lastDoneDate) : null, lastDoneKm: m.lastDoneKm ?? null, nextDueKm: m.nextDueKm ?? null, status: (m.status as any) || 'ok', kmUntilDue: m.kmUntilDue ?? null }))
          }));
          this.filteredVehicleSchedules = [...this.vehicleSchedules];
          this.rebuildRows();
          this.cdr.detectChanges();
        });
      },
      error: (err) => console.error('Error loading vehicle schedules:', err)
    });
  }

  rebuildRows() {
    const rows: FlatRow[] = [];
    for (const v of this.vehicleSchedules) {
      for (const m of v.maintenanceItems) {
        const tpl = this.templates.find(t => t.id === m.templateId);
        const intervalKm = tpl?.intervalKm || 10000;
        const used = intervalKm - (m.kmUntilDue ?? intervalKm);
        const pct = Math.min(100, Math.max(0, (used / intervalKm) * 100));
        rows.push({
          vehicleId: v.vehicleId, vehicleName: v.vehicleName, vehiclePlate: v.vehiclePlate, currentMileage: v.currentMileage,
          templateId: m.templateId, templateName: m.templateName, scheduleId: m.scheduleId, lastDoneDate: m.lastDoneDate, lastDoneKm: m.lastDoneKm, nextDueKm: m.nextDueKm,
          status: m.status, kmUntilDue: m.kmUntilDue, progressPercent: pct
        });
      }
    }
    this.allRows = rows;
    this.applyFilters();
  }

  applyFilters() {
    let r = [...this.allRows];
    if (this.searchQuery) {
      const q = this.searchQuery.toLowerCase();
      r = r.filter(row => row.vehicleName.toLowerCase().includes(q) || row.vehiclePlate.toLowerCase().includes(q) || row.templateName.toLowerCase().includes(q));
    }
    if (this.statusFilter) r = r.filter(row => row.status === this.statusFilter);
    if (this.templateFilter) r = r.filter(row => row.templateId === this.templateFilter);
    // Sort
    const statusOrder: Record<string, number> = { overdue: 0, due: 1, upcoming: 2, ok: 3 };
    r.sort((a, b) => {
      let va: any, vb: any;
      if (this.sortColumn === 'status') { va = statusOrder[a.status] ?? 4; vb = statusOrder[b.status] ?? 4; }
      else if (this.sortColumn === 'kmUntilDue') { va = a.kmUntilDue ?? 99999; vb = b.kmUntilDue ?? 99999; }
      else if (this.sortColumn === 'vehicleName') { va = a.vehicleName; vb = b.vehicleName; }
      else if (this.sortColumn === 'templateName') { va = a.templateName; vb = b.templateName; }
      else { va = 0; vb = 0; }
      const cmp = va < vb ? -1 : va > vb ? 1 : 0;
      return this.sortDir === 'asc' ? cmp : -cmp;
    });
    this.filteredRows = r;
    // Also update filteredTemplates
    let ft = [...this.templates];
    if (this.searchQuery) { const q = this.searchQuery.toLowerCase(); ft = ft.filter(t => t.name.toLowerCase().includes(q)); }
    this.filteredTemplates = ft;
  }

  sortBy(col: string) {
    if (this.sortColumn === col) this.sortDir = this.sortDir === 'asc' ? 'desc' : 'asc';
    else { this.sortColumn = col; this.sortDir = 'asc'; }
    this.applyFilters();
  }

  getSortIcon(col: string): string {
    if (this.sortColumn !== col) return '';
    return this.sortDir === 'asc' ? '▲' : '▼';
  }

  countByStatus(s: string): number { return this.allRows.filter(r => r.status === s).length; }
  getTemplateCount(id: string): number { return this.allRows.filter(r => r.templateId === id).length; }

  getPriorityLabel(p: string) { return { low:'Faible', medium:'Moyenne', high:'Haute', critical:'Critique' }[p] || p; }
  getStatusLabel(s: string) { return { ok:'OK', upcoming:'A venir', due:'A faire', overdue:'En retard' }[s] || s; }
  formatDate(d: Date) { return new Date(d).toLocaleDateString('fr-FR'); }
  getCount(v: VehicleMaintenanceStatus, s: string) { return v.maintenanceItems.filter(m => m.status === s).length; }
  toggleVehicle(id: string) { this.expanded.includes(id) ? this.expanded = this.expanded.filter(x => x !== id) : this.expanded.push(id); }

  selectTemplate(t: MaintenanceTemplate) { this.selected = t; }
  closeDetail() { this.selected = null; }
  openTemplateForm() { this.editing = null; this.form = this.getEmptyForm(); this.isFormOpen = true; }
  editTemplate(t: MaintenanceTemplate) { this.editing = t; this.form = { ...t }; this.isFormOpen = true; this.selected = null; }
  closeForm() { this.isFormOpen = false; this.editing = null; }
  isFormValid() { return this.form.name && this.form.category && (this.form.intervalKm || this.form.intervalMonths); }
  saveTemplate() {
    if (!this.isFormValid()) return;
    const d = { name: this.form.name, description: this.form.description || '', category: this.form.category, priority: this.form.priority, intervalKm: this.form.intervalKm || undefined, intervalMonths: this.form.intervalMonths || undefined, estimatedCost: this.form.estimatedCost || 0, isActive: this.form.isActive };
    if (this.editing) {
      this.apiService.updateMaintenanceTemplate(parseInt(this.editing.id), d).pipe(takeUntil(this.destroy$)).subscribe({ next: () => { this.loadTemplates(); this.closeForm(); }, error: (err) => console.error(err) });
    } else {
      this.apiService.createMaintenanceTemplate(d).pipe(takeUntil(this.destroy$)).subscribe({ next: () => { this.loadTemplates(); this.closeForm(); }, error: (err) => console.error(err) });
    }
  }
  deleteTemplate(t: MaintenanceTemplate) { 
    if (confirm('Supprimer ce modele?')) { 
      this.apiService.deleteMaintenanceTemplate(parseInt(t.id)).pipe(takeUntil(this.destroy$)).subscribe({ next: () => { this.loadTemplates(); this.closeDetail(); }, error: (err) => console.error(err) });
    } 
  }

  openMarkDone(v: VehicleMaintenanceStatus, m: MaintenanceItem) { 
    const lastPrice = this.lastPaidPrices.get(m.templateId) || null;
    this.markData = { vehicleId:v.vehicleId, vehicleName:v.vehicleName, vehiclePlate:v.vehiclePlate, templateId:m.templateId, maintenanceName:m.templateName, date:new Date().toISOString().split('T')[0], mileage:v.currentMileage, supplier:'', notes:'', invoiceLines: [{ templateId: m.templateId, description: m.templateName, price: lastPrice, isCustom: false, isNewTemplate: false, newTemplateName: '', newTemplateCategory: '', newTemplateIntervalKm: null, newTemplateIntervalMonths: null }] }; 
    this.isMarkOpen = true; 
  }

  openMarkDoneFromRow(row: FlatRow) {
    const v = this.vehicleSchedules.find(x => x.vehicleId === row.vehicleId);
    const m = v?.maintenanceItems.find(x => x.templateId === row.templateId);
    if (v && m) this.openMarkDone(v, m);
  }

  removeFromRow(row: FlatRow) {
    if (!confirm('Retirer cet entretien ?')) return;
    if (row.scheduleId) {
      this.apiService.removeMaintenanceSchedule(row.scheduleId).pipe(takeUntil(this.destroy$)).subscribe({ next: () => this.loadVehicles(), error: (err) => console.error(err) });
    }
  }

  closeMarkDone() { this.isMarkOpen = false; this.markData = this.getEmptyMark(); }
  isMarkValid() {
    if (!this.markData.date || !this.markData.mileage) return false;
    const hasExisting = this.markData.invoiceLines.some((l: InvoiceLine) => l.templateId && l.price);
    const hasNew = this.markData.invoiceLines.some((l: InvoiceLine) => l.isNewTemplate && l.newTemplateName && l.price);
    return hasExisting || hasNew;
  }
  addInvoiceLine() { this.markData.invoiceLines.push({ templateId: null, description: '', price: null, isCustom: false, isNewTemplate: false, newTemplateName: '', newTemplateCategory: '', newTemplateIntervalKm: null, newTemplateIntervalMonths: null }); }
  onLineTemplateChange(line: InvoiceLine, templateId: string) {
    if (templateId === 'new') {
      line.templateId = null; line.isCustom = false; line.isNewTemplate = true;
      line.description = ''; line.price = null;
      line.newTemplateName = ''; line.newTemplateCategory = ''; line.newTemplateIntervalKm = null; line.newTemplateIntervalMonths = null;
    } else if (templateId === 'other') {
      line.templateId = null; line.isCustom = true; line.isNewTemplate = false; line.description = ''; line.price = null;
    } else {
      const t = this.templates.find(x => x.id === templateId);
      if (t) { line.templateId = t.id; line.isCustom = false; line.isNewTemplate = false; line.description = t.name; line.price = this.lastPaidPrices.get(t.id) || null; }
    }
  }
  getVehicleMaintenanceTemplates(): MaintenanceTemplate[] {
    const v = this.vehicleSchedules.find(x => x.vehicleId === this.markData.vehicleId);
    if (!v) return this.templates.filter(t => t.isActive);
    return this.templates.filter(t => t.isActive && v.maintenanceItems.some(m => m.templateId === t.id));
  }
  getOtherTemplatesForDropdown(): MaintenanceTemplate[] {
    const ids = this.getVehicleMaintenanceTemplates().map(t => t.id);
    return this.templates.filter(t => t.isActive && !ids.includes(t.id));
  }
  removeInvoiceLine(i: number) { if (this.markData.invoiceLines.length > 1) this.markData.invoiceLines.splice(i, 1); }
  getInvoiceTotal(): number { return this.markData.invoiceLines.reduce((s: number, l: any) => s + (l.price || 0), 0); }
  confirmMarkDone() {
    const vehicleId = parseInt(this.markData.vehicleId);
    const existingLines = this.markData.invoiceLines.filter((l: InvoiceLine) => l.templateId && l.price);
    const newTplLines = this.markData.invoiceLines.filter((l: InvoiceLine) => l.isNewTemplate && l.newTemplateName && l.price);
    const totalLines = existingLines.length + newTplLines.length;
    if (totalLines === 0) { this.closeMarkDone(); return; }

    let done = 0;
    const onDone = () => { done++; if (done === totalLines) { this.loadTemplates(); this.loadVehicles(); this.closeMarkDone(); } };

    for (const line of existingLines) {
      this.lastPaidPrices.set(line.templateId!, line.price!);
      this.apiService.markMaintenanceDone({ vehicleId, templateId: parseInt(line.templateId!), date: this.markData.date, mileage: this.markData.mileage, cost: line.price!, notes: this.markData.notes || undefined }).pipe(takeUntil(this.destroy$)).subscribe({ next: onDone, error: onDone });
    }

    for (const line of newTplLines) {
      this.apiService.createMaintenanceTemplate({
        name: line.newTemplateName, description: '', category: line.newTemplateCategory || 'Autre',
        priority: 'medium', intervalKm: line.newTemplateIntervalKm || undefined, intervalMonths: line.newTemplateIntervalMonths || undefined,
        estimatedCost: line.price!, isActive: true
      }).pipe(takeUntil(this.destroy$)).subscribe({
        next: (tplId: number) => {
          this.apiService.assignMaintenanceTemplate(vehicleId, tplId).pipe(takeUntil(this.destroy$)).subscribe({
            next: () => {
              this.apiService.markMaintenanceDone({ vehicleId, templateId: tplId, date: this.markData.date, mileage: this.markData.mileage, cost: line.price!, notes: this.markData.notes || undefined }).pipe(takeUntil(this.destroy$)).subscribe({ next: onDone, error: onDone });
            },
            error: onDone
          });
        },
        error: onDone
      });
    }
  }

  openHistory(row: FlatRow) {
    this.historyVehicleName = row.vehicleName;
    this.historyVehiclePlate = row.vehiclePlate;
    this.historyTemplateName = row.templateName;
    this.historyLogs = [];
    this.historyLoading = true;
    this.isHistoryOpen = true;
    this.apiService.getMaintenanceLogs(parseInt(row.vehicleId), parseInt(row.templateId)).pipe(takeUntil(this.destroy$)).subscribe({
      next: (logs) => { this.ngZone.run(() => { this.historyLogs = logs; this.historyLoading = false; this.cdr.detectChanges(); }); },
      error: (err) => { console.error(err); this.historyLoading = false; }
    });
  }
  closeHistory() { this.isHistoryOpen = false; this.historyLogs = []; }

  loadAllVehicles() {
    this.apiService.getVehicles().pipe(takeUntil(this.destroy$)).subscribe({
      next: (vehicles) => { this.ngZone.run(() => { this.allVehicles = vehicles.map((v: any) => ({ id: v.id?.toString() || '', name: v.name || `${v.brand || ''} ${v.model || ''}`.trim(), plate: v.plate || '', mileage: v.mileage || 0 })); this.cdr.detectChanges(); }); },
      error: (err) => console.error(err)
    });
  }

  openAssignPanel() { this.addToVehicleData = this.getEmptyAddToVehicle(); this.isAddToVehicleOpen = true; }
  onAssignVehicleChange(vehicleId: string) {
    const v = this.allVehicles.find(x => x.id === vehicleId);
    if (v) { this.addToVehicleData.vehicleName = v.name; this.addToVehicleData.vehiclePlate = v.plate; this.addToVehicleData.vehicleMileage = v.mileage; }
    this.addToVehicleData.selectedTemplateIds = [];
  }
  openAddToVehicle(v: VehicleMaintenanceStatus) { this.addToVehicleData = { vehicleId:v.vehicleId, vehicleName:v.vehicleName, vehiclePlate:v.vehiclePlate, vehicleMileage:v.currentMileage, selectedTemplateIds:[] }; this.isAddToVehicleOpen = true; }
  closeAddToVehicle() { this.isAddToVehicleOpen = false; }
  getAvailableTemplatesForVehicle(): MaintenanceTemplate[] {
    const v = this.vehicleSchedules.find(x => x.vehicleId === this.addToVehicleData.vehicleId);
    const ids = v ? v.maintenanceItems.map(m => m.templateId) : [];
    return this.templates.filter(t => t.isActive && !ids.includes(t.id));
  }
  toggleTemplateSelection(t: MaintenanceTemplate) { const i = this.addToVehicleData.selectedTemplateIds.indexOf(t.id); if (i === -1) this.addToVehicleData.selectedTemplateIds.push(t.id); else this.addToVehicleData.selectedTemplateIds.splice(i, 1); }
  isTemplateSelected(id: string): boolean { return this.addToVehicleData.selectedTemplateIds.includes(id); }
  selectAllTemplates() { this.addToVehicleData.selectedTemplateIds = this.getAvailableTemplatesForVehicle().map(t => t.id); }
  deselectAllTemplates() { this.addToVehicleData.selectedTemplateIds = []; }
  confirmAddToVehicle() {
    if (this.addToVehicleData.selectedTemplateIds.length === 0) return;
    const vid = parseInt(this.addToVehicleData.vehicleId);
    let done = 0; const total = this.addToVehicleData.selectedTemplateIds.length;
    for (const tid of this.addToVehicleData.selectedTemplateIds) {
      this.apiService.assignMaintenanceTemplate(vid, parseInt(tid)).pipe(takeUntil(this.destroy$)).subscribe({
        next: () => { done++; if (done === total) { this.loadVehicles(); this.closeAddToVehicle(); } },
        error: () => { done++; if (done === total) { this.loadVehicles(); this.closeAddToVehicle(); } }
      });
    }
  }

  removeMaintenanceFromVehicle(v: VehicleMaintenanceStatus, m: MaintenanceItem) {
    if (!confirm('Retirer?')) return;
    if (m.scheduleId) { this.apiService.removeMaintenanceSchedule(m.scheduleId).pipe(takeUntil(this.destroy$)).subscribe({ next: () => this.loadVehicles(), error: (err) => console.error(err) }); }
    else { v.maintenanceItems = v.maintenanceItems.filter(x => x.templateId !== m.templateId); this.rebuildRows(); }
  }

  // Keep for compatibility
  updateAgendaItems() {}
  filterVehicles() { this.filteredVehicleSchedules = [...this.vehicleSchedules]; this.rebuildRows(); }
  filterTemplates() { this.filteredTemplates = [...this.templates]; }
  getAgendaItems() { return []; }
  openMarkDoneFromAgenda(item: any) { this.openMarkDoneFromRow(item); }
  getProgressPercent(m: any) { return 0; }
  getTotalByStatus(s: string) { return 0; }
  getActiveTemplates() { return this.templates.filter(t => t.isActive).length; }
  getCriticalTemplates() { return 0; }
  getAllTemplatesForDropdown() { return this.templates.filter(t => t.isActive); }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
