import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AppLayoutComponent } from './shared/app-layout.component';
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
}

interface VehicleMaintenanceStatus {
  vehicleId: string;
  vehicleName: string;
  vehiclePlate: string;
  currentMileage: number;
  maintenanceItems: MaintenanceItem[];
}

interface MaintenanceItem {
  templateId: string;
  templateName: string;
  lastDoneDate: Date | null;
  lastDoneKm: number | null;
  nextDueKm: number | null;
  status: 'ok' | 'upcoming' | 'due' | 'overdue';
  kmUntilDue: number | null;
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
      <div class="maintenance-page">
        <div class="tabs-bar">
          <button class="tab-btn" [class.active]="activeTab === 'templates'" (click)="activeTab = 'templates'">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
            </svg>
            Modèles d'entretien
          </button>
          <button class="tab-btn" [class.active]="activeTab === 'schedule'" (click)="activeTab = 'schedule'">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <rect x="3" y="4" width="18" height="18" rx="2" ry="2"/>
              <line x1="16" y1="2" x2="16" y2="6"/>
              <line x1="8" y1="2" x2="8" y2="6"/>
            </svg>
            Planning véhicules
          </button>
        </div>

        <!-- TAB 1: Modèles -->
        <div class="tab-content" *ngIf="activeTab === 'templates'" @fadeIn>
          <div class="filter-bar">
            <div class="search-wrapper">
              <svg class="search-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/>
              </svg>
              <input type="text" class="search-input" placeholder="Rechercher..." [(ngModel)]="searchQuery" (input)="filterTemplates()">
            </div>
            <select class="filter-select" [(ngModel)]="categoryFilter" (change)="filterTemplates()">
              <option value="">Toutes catégories</option>
              <option *ngFor="let cat of categories" [value]="cat">{{ cat }}</option>
            </select>
            <button class="btn-add" (click)="openTemplateForm()">+ Nouveau modèle</button>
          </div>

          <div class="stats-bar">
            <div class="stat-item"><div class="stat-icon total">📋</div><div class="stat-content"><span class="stat-value">{{ templates.length }}</span><span class="stat-label">Modèles</span></div></div>
            <div class="stat-item"><div class="stat-icon active">✅</div><div class="stat-content"><span class="stat-value">{{ getActiveTemplates() }}</span><span class="stat-label">Actifs</span></div></div>
            <div class="stat-item"><div class="stat-icon critical">⚠️</div><div class="stat-content"><span class="stat-value">{{ getCriticalTemplates() }}</span><span class="stat-label">Critiques</span></div></div>
          </div>

          <div class="templates-grid">
            @for (t of filteredTemplates; track t.id) {
              <div class="template-card" [class.inactive]="!t.isActive" (click)="selectTemplate(t)">
                <div class="card-header">
                  <div class="card-icon" [class]="t.priority">🔧</div>
                  <span class="priority-badge" [class]="t.priority">{{ getPriorityLabel(t.priority) }}</span>
                </div>
                <h3 class="card-title">{{ t.name }}</h3>
                <p class="card-desc">{{ t.description }}</p>
                <div class="card-intervals">
                  <span *ngIf="t.intervalKm">{{ t.intervalKm | number }} km</span>
                  <span *ngIf="t.intervalMonths">{{ t.intervalMonths }} mois</span>
                </div>
                <div class="card-footer">
                  <span class="category-tag">{{ t.category }}</span>
                  <span class="cost">~{{ t.estimatedCost | number }} DT</span>
                </div>
              </div>
            }
          </div>
          <div class="empty-state" *ngIf="filteredTemplates.length === 0"><p>Aucun modèle</p></div>
        </div>

        <!-- TAB 2: Planning Agenda -->
        <div class="tab-content" *ngIf="activeTab === 'schedule'" @fadeIn>
          <div class="filter-bar">
            <div class="search-wrapper">
              <svg class="search-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/>
              </svg>
              <input type="text" class="search-input" placeholder="Rechercher..." [(ngModel)]="vehicleSearchQuery" (input)="filterVehicles()">
            </div>
            <div class="view-toggle">
              <button class="view-btn" [class.active]="scheduleView === 'agenda'" (click)="scheduleView = 'agenda'">📅 Agenda</button>
              <button class="view-btn" [class.active]="scheduleView === 'vehicles'" (click)="scheduleView = 'vehicles'">🚗 Véhicules</button>
            </div>
          </div>

          <!-- AGENDA VIEW -->
          <div class="agenda-view" *ngIf="scheduleView === 'agenda'">
            <!-- Summary cards -->
            <div class="agenda-summary">
              <div class="summary-card urgent"><span class="sum-count">{{ urgentItems.length }}</span><span class="sum-label">Urgent</span></div>
              <div class="summary-card soon"><span class="sum-count">{{ soonItems.length }}</span><span class="sum-label">À venir</span></div>
              <div class="summary-card ok"><span class="sum-count">{{ okItems.length }}</span><span class="sum-label">OK</span></div>
            </div>

            <!-- Section URGENT -->
            <div class="agenda-section" *ngIf="urgentItems.length > 0">
              <div class="section-header urgent">
                <span class="section-icon">🔴</span>
                <span class="section-title">Urgent</span>
                <span class="section-count">{{ urgentItems.length }}</span>
              </div>
              <div class="agenda-items">
                @for (item of urgentItems; track item.templateId + item.vehicleId) {
                  <div class="agenda-item urgent">
                    <div class="timeline-dot"></div>
                    <div class="item-content">
                      <div class="item-main">
                        <span class="item-maint">{{ item.templateName }}</span>
                        <span class="item-vehicle">{{ item.vehicleName }}</span>
                      </div>
                      <div class="item-meta">
                        <span class="item-plate">{{ item.vehiclePlate }}</span>
                        <span class="item-km urgent">{{ item.kmUntilDue !== null && item.kmUntilDue < 0 ? 'Dépassé ' + ((-item.kmUntilDue) | number) + ' km' : 'Maintenant' }}</span>
                      </div>
                    </div>
                    <div class="item-actions">
                      <button class="btn-action done" (click)="openMarkDoneFromAgenda(item)">✓ Fait</button>
                    </div>
                  </div>
                }
              </div>
            </div>

            <!-- Section À VENIR -->
            <div class="agenda-section" *ngIf="soonItems.length > 0">
              <div class="section-header soon">
                <span class="section-icon">🟠</span>
                <span class="section-title">À venir</span>
                <span class="section-count">{{ soonItems.length }}</span>
              </div>
              <div class="agenda-items">
                @for (item of soonItems; track item.templateId + item.vehicleId) {
                  <div class="agenda-item soon">
                    <div class="timeline-dot"></div>
                    <div class="item-content">
                      <div class="item-main">
                        <span class="item-maint">{{ item.templateName }}</span>
                        <span class="item-vehicle">{{ item.vehicleName }}</span>
                      </div>
                      <div class="item-meta">
                        <span class="item-plate">{{ item.vehiclePlate }}</span>
                        <span class="item-km soon">Dans {{ item.kmUntilDue | number }} km</span>
                      </div>
                    </div>
                    <div class="item-actions">
                      <button class="btn-action done" (click)="openMarkDoneFromAgenda(item)">✓ Fait</button>
                    </div>
                  </div>
                }
              </div>
            </div>

            <!-- Section OK -->
            <div class="agenda-section" *ngIf="okItems.length > 0">
              <div class="section-header ok">
                <span class="section-icon">🟢</span>
                <span class="section-title">OK - Plus tard</span>
                <span class="section-count">{{ okItems.length }}</span>
              </div>
              <div class="agenda-items collapsed" [class.expanded]="showOkItems">
                @for (item of okItems; track item.templateId + item.vehicleId) {
                  <div class="agenda-item ok">
                    <div class="timeline-dot"></div>
                    <div class="item-content">
                      <div class="item-main">
                        <span class="item-maint">{{ item.templateName }}</span>
                        <span class="item-vehicle">{{ item.vehicleName }}</span>
                      </div>
                      <div class="item-meta">
                        <span class="item-plate">{{ item.vehiclePlate }}</span>
                        <span class="item-km ok">Dans {{ item.kmUntilDue | number }} km</span>
                      </div>
                    </div>
                  </div>
                }
              </div>
              <button class="btn-show-more" (click)="showOkItems = !showOkItems" *ngIf="okItems.length > 3">
                {{ showOkItems ? 'Masquer' : 'Voir ' + okItems.length + ' éléments' }}
              </button>
            </div>

            <!-- Empty state -->
            <div class="empty-agenda" *ngIf="urgentItems.length === 0 && soonItems.length === 0 && okItems.length === 0">
              <span class="empty-icon">📋</span>
              <p>Aucun entretien planifié</p>
              <span class="empty-hint">Ajoutez des entretiens depuis la vue Véhicules</span>
            </div>
          </div>

          <!-- VEHICLES VIEW (original) -->
          <div class="vehicles-schedule" *ngIf="scheduleView === 'vehicles'">
            @for (v of filteredVehicleSchedules; track v.vehicleId) {
              <div class="vehicle-card">
                <div class="vehicle-header" (click)="toggleVehicle(v.vehicleId)">
                  <div class="vehicle-info">
                    <h3>{{ v.vehicleName }}</h3>
                    <span class="plate">{{ v.vehiclePlate }}</span>
                    <span class="km">{{ v.currentMileage | number }} km</span>
                  </div>
                  <div class="vehicle-badges">
                    <span class="badge overdue" *ngIf="getCount(v,'overdue')">{{ getCount(v,'overdue') }}</span>
                    <span class="badge due" *ngIf="getCount(v,'due')">{{ getCount(v,'due') }}</span>
                    <span class="badge upcoming" *ngIf="getCount(v,'upcoming')">{{ getCount(v,'upcoming') }}</span>
                    <span class="expand-arrow" [class.open]="expanded.includes(v.vehicleId)">▼</span>
                  </div>
                </div>
                <div class="maintenance-list" *ngIf="expanded.includes(v.vehicleId)">
                  @for (m of v.maintenanceItems; track m.templateId) {
                    <div class="maint-row" [class]="m.status">
                      <div class="maint-info">
                        <span class="maint-name">{{ m.templateName }}</span>
                        <span class="maint-last">{{ m.lastDoneDate ? 'Dernier: ' + formatDate(m.lastDoneDate) + ' @ ' + (m.lastDoneKm | number) + ' km' : 'Jamais effectué' }}</span>
                      </div>
                      <div class="maint-next">
                        <span *ngIf="m.kmUntilDue !== null">{{ m.kmUntilDue > 0 ? 'Dans ' + (m.kmUntilDue | number) + ' km' : 'Dépassé ' + ((-m.kmUntilDue) | number) + ' km' }}</span>
                      </div>
                      <div class="maint-actions">
                        <span class="status-badge" [class]="m.status">{{ getStatusLabel(m.status) }}</span>
                        <button class="btn-done" (click)="openMarkDone(v, m); $event.stopPropagation()">✓</button>
                        <button class="btn-remove" (click)="removeMaintenanceFromVehicle(v, m); $event.stopPropagation()">✕</button>
                      </div>
                    </div>
                  }
                  <button class="btn-add-maint" (click)="openAddToVehicle(v); $event.stopPropagation()">
                    <span>＋</span> Ajouter un entretien
                  </button>
                </div>
              </div>
            }
          </div>
        </div>
      </div>

      <!-- Form Panel -->
      <div class="overlay" *ngIf="isFormOpen" @fadeIn (click)="closeForm()">
        <div class="panel" @slideIn (click)="$event.stopPropagation()">
          <div class="panel-header blue">
            <h2>{{ editing ? 'Modifier' : 'Nouveau' }} modèle</h2>
            <button class="btn-close" (click)="closeForm()">✕</button>
          </div>
          <div class="panel-body">
            <div class="form-group"><label>Nom *</label><input [(ngModel)]="form.name" placeholder="Vidange moteur"></div>
            <div class="form-group"><label>Description</label><textarea [(ngModel)]="form.description" rows="2"></textarea></div>
            <div class="form-row">
              <div class="form-group"><label>Catégorie *</label><select [(ngModel)]="form.category"><option value="">Choisir</option><option *ngFor="let c of categories" [value]="c">{{c}}</option></select></div>
              <div class="form-group"><label>Priorité</label><select [(ngModel)]="form.priority"><option value="low">Faible</option><option value="medium">Moyenne</option><option value="high">Haute</option><option value="critical">Critique</option></select></div>
            </div>
            <div class="form-row">
              <div class="form-group"><label>Intervalle (km)</label><input type="number" [(ngModel)]="form.intervalKm" placeholder="10000"></div>
              <div class="form-group"><label>Intervalle (mois)</label><input type="number" [(ngModel)]="form.intervalMonths" placeholder="12"></div>
            </div>
            <div class="form-group"><label>Coût estimé (DT)</label><input type="number" [(ngModel)]="form.estimatedCost" placeholder="150"></div>
            <div class="form-group toggle"><label><input type="checkbox" [(ngModel)]="form.isActive"><span class="switch"></span> Actif</label></div>
          </div>
          <div class="panel-footer"><button class="btn-cancel" (click)="closeForm()">Annuler</button><button class="btn-save" (click)="saveTemplate()" [disabled]="!isFormValid()">Enregistrer</button></div>
        </div>
      </div>

      <!-- Mark Done Panel -->
      <div class="overlay" *ngIf="isMarkOpen" @fadeIn (click)="closeMarkDone()">
        <div class="panel narrow" @slideIn (click)="$event.stopPropagation()">
          <div class="panel-header green">
            <h2>Entretien effectué</h2>
            <button class="btn-close" (click)="closeMarkDone()">✕</button>
          </div>
          <div class="panel-body">
            <div class="recap"><span class="label">{{ markData.maintenanceName }}</span><span class="value">{{ markData.vehicleName }} - {{ markData.vehiclePlate }}</span></div>
            <div class="form-row">
              <div class="form-group"><label>Date *</label><input type="date" [(ngModel)]="markData.date"></div>
              <div class="form-group"><label>Kilométrage *</label><input type="number" [(ngModel)]="markData.mileage" placeholder="45000"></div>
            </div>
            <div class="form-group"><label>Coût réel (DT) *</label><input type="number" [(ngModel)]="markData.cost" placeholder="150"></div>
            <div class="form-group"><label>Fournisseur</label><input [(ngModel)]="markData.supplier" placeholder="Garage..."></div>
            <div class="form-group"><label>Notes</label><textarea [(ngModel)]="markData.notes" rows="2"></textarea></div>
          </div>
          <div class="panel-footer"><button class="btn-cancel" (click)="closeMarkDone()">Annuler</button><button class="btn-save green" (click)="confirmMarkDone()" [disabled]="!isMarkValid()">Confirmer</button></div>
        </div>
      </div>

      <!-- Detail Panel -->
      <div class="overlay" *ngIf="selected" @fadeIn (click)="closeDetail()">
        <div class="panel" @slideIn (click)="$event.stopPropagation()">
          <div class="panel-header" [class]="selected.priority">
            <h2>{{ selected.name }}</h2>
            <button class="btn-close" (click)="closeDetail()">✕</button>
          </div>
          <div class="panel-body">
            <div class="badges"><span class="priority-badge" [class]="selected.priority">{{ getPriorityLabel(selected.priority) }}</span><span class="active-badge" [class.inactive]="!selected.isActive">{{ selected.isActive ? 'Actif' : 'Inactif' }}</span></div>
            <div class="section" *ngIf="selected.description"><h4>Description</h4><p>{{ selected.description }}</p></div>
            <div class="section"><h4>Intervalles</h4><div class="intervals"><div class="int-card" *ngIf="selected.intervalKm"><span class="int-val">{{ selected.intervalKm | number }}</span><span class="int-unit">km</span></div><span class="or" *ngIf="selected.intervalKm && selected.intervalMonths">OU</span><div class="int-card" *ngIf="selected.intervalMonths"><span class="int-val">{{ selected.intervalMonths }}</span><span class="int-unit">mois</span></div></div></div>
            <div class="section"><h4>Coût estimé</h4><div class="cost-display"><span class="cost-val">{{ selected.estimatedCost | number }}</span><span class="cost-unit">DT</span></div></div>
          </div>
          <div class="panel-footer"><button class="btn-cancel" (click)="closeDetail()">Fermer</button><button class="btn-edit" (click)="editTemplate(selected)">Modifier</button><button class="btn-delete" (click)="deleteTemplate(selected)">Supprimer</button></div>
        </div>
      </div>

      <!-- Add Maintenance to Vehicle Panel -->
      <div class="overlay" *ngIf="isAddToVehicleOpen" @fadeIn (click)="closeAddToVehicle()">
        <div class="panel" @slideIn (click)="$event.stopPropagation()">
          <div class="panel-header purple">
            <h2>Ajouter un entretien</h2>
            <button class="btn-close" (click)="closeAddToVehicle()">✕</button>
          </div>
          <div class="panel-body">
            <div class="vehicle-recap-box">
              <span class="recap-icon">🚗</span>
              <div class="recap-info">
                <span class="recap-name">{{ addToVehicleData.vehicleName }}</span>
                <span class="recap-plate">{{ addToVehicleData.vehiclePlate }} • {{ addToVehicleData.vehicleMileage | number }} km</span>
              </div>
            </div>
            <h4 class="select-title">Sélectionnez un type d'entretien:</h4>
            <div class="templates-select-list">
              @for (t of getAvailableTemplatesForVehicle(); track t.id) {
                <div class="template-select-item" [class.selected]="addToVehicleData.selectedTemplateId === t.id" (click)="selectTemplateForVehicle(t)">
                  <div class="tpl-icon" [class]="t.priority">🔧</div>
                  <div class="tpl-info">
                    <span class="tpl-name">{{ t.name }}</span>
                    <span class="tpl-interval">{{ t.intervalKm ? (t.intervalKm | number) + ' km' : '' }}{{ t.intervalKm && t.intervalMonths ? ' / ' : '' }}{{ t.intervalMonths ? t.intervalMonths + ' mois' : '' }}</span>
                  </div>
                  <span class="tpl-cost">~{{ t.estimatedCost }} DT</span>
                  <span class="tpl-check" *ngIf="addToVehicleData.selectedTemplateId === t.id">✓</span>
                </div>
              }
              <div class="empty-templates" *ngIf="getAvailableTemplatesForVehicle().length === 0">
                <p>Tous les entretiens sont déjà assignés à ce véhicule</p>
              </div>
            </div>
          </div>
          <div class="panel-footer">
            <button class="btn-cancel" (click)="closeAddToVehicle()">Annuler</button>
            <button class="btn-save purple" (click)="confirmAddToVehicle()" [disabled]="!addToVehicleData.selectedTemplateId">Ajouter</button>
          </div>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    .maintenance-page { flex:1; background:#f1f5f9; padding:20px; }
    .tabs-bar { display:flex; gap:8px; margin-bottom:20px; background:white; padding:6px; border-radius:10px; }
    .tab-btn { display:flex; align-items:center; gap:8px; padding:10px 16px; border:none; background:transparent; border-radius:8px; font-size:13px; font-weight:500; color:#64748b; cursor:pointer; }
    .tab-btn:hover { background:#f1f5f9; }
    .tab-btn.active { background:#1e3a5f; color:white; }
    .filter-bar { display:flex; gap:12px; margin-bottom:16px; flex-wrap:wrap; }
    .search-wrapper { flex:1; min-width:200px; position:relative; }
    .search-icon { position:absolute; left:12px; top:50%; transform:translateY(-50%); color:#94a3b8; }
    .search-input { width:100%; padding:10px 12px 10px 36px; border:1px solid #e2e8f0; border-radius:8px; font-size:13px; }
    .filter-select { padding:10px 12px; border:1px solid #e2e8f0; border-radius:8px; font-size:13px; background:white; }
    .view-toggle { display:flex; gap:4px; background:#f1f5f9; padding:4px; border-radius:8px; }
    .view-btn { padding:8px 14px; border:none; background:transparent; border-radius:6px; font-size:12px; font-weight:500; color:#64748b; cursor:pointer; transition:all .2s; }
    .view-btn:hover { color:#1e293b; }
    .view-btn.active { background:white; color:#1e293b; box-shadow:0 1px 3px rgba(0,0,0,.1); }
    .btn-add { padding:10px 16px; background:#1e3a5f; color:white; border:none; border-radius:8px; font-size:13px; font-weight:500; cursor:pointer; }
    .stats-bar { display:flex; gap:12px; margin-bottom:16px; }
    .stat-item { display:flex; align-items:center; gap:10px; padding:12px 16px; background:white; border-radius:10px; }
    .stat-icon { font-size:20px; }
    .stat-value { font-size:18px; font-weight:700; color:#1e293b; display:block; }
    .stat-label { font-size:11px; color:#64748b; }
    .templates-grid { display:grid; grid-template-columns:repeat(auto-fill,minmax(280px,1fr)); gap:16px; }
    .template-card { background:white; border-radius:12px; padding:16px; cursor:pointer; border:2px solid transparent; transition:all .2s; }
    .template-card:hover { border-color:#3b82f6; transform:translateY(-2px); }
    .template-card.inactive { opacity:.6; }
    .card-header { display:flex; justify-content:space-between; margin-bottom:12px; }
    .card-icon { width:40px; height:40px; border-radius:10px; display:flex; align-items:center; justify-content:center; font-size:20px; }
    .card-icon.low { background:#dcfce7; }
    .card-icon.medium { background:#fef3c7; }
    .card-icon.high { background:#fee2e2; }
    .card-icon.critical { background:#1e293b; }
    .priority-badge { padding:4px 8px; border-radius:4px; font-size:10px; font-weight:600; }
    .priority-badge.low { background:#dcfce7; color:#16a34a; }
    .priority-badge.medium { background:#fef3c7; color:#d97706; }
    .priority-badge.high { background:#fee2e2; color:#dc2626; }
    .priority-badge.critical { background:#1e293b; color:white; }
    .card-title { font-size:15px; font-weight:600; margin:0 0 6px; }
    .card-desc { font-size:12px; color:#64748b; margin:0 0 12px; }
    .card-intervals { display:flex; gap:12px; font-size:12px; color:#64748b; margin-bottom:12px; }
    .card-footer { display:flex; justify-content:space-between; padding-top:12px; border-top:1px solid #f1f5f9; }
    .category-tag { padding:4px 8px; background:#f1f5f9; border-radius:4px; font-size:11px; color:#475569; }
    .cost { font-size:13px; font-weight:600; }
    .alerts-summary { display:flex; gap:12px; margin-bottom:16px; }
    .alert-card { flex:1; padding:16px; border-radius:10px; text-align:center; }
    .alert-card.overdue { background:#fee2e2; }
    .alert-card.due { background:#fef3c7; }
    .alert-card.upcoming { background:#dbeafe; }
    .alert-card.ok { background:#dcfce7; }
    .alert-count { font-size:24px; font-weight:700; }
    .alert-card.overdue .alert-count { color:#dc2626; }
    .alert-card.due .alert-count { color:#d97706; }
    .alert-card.upcoming .alert-count { color:#2563eb; }
    .alert-card.ok .alert-count { color:#16a34a; }
    .alert-label { font-size:12px; color:#64748b; }
    .vehicles-schedule { display:flex; flex-direction:column; gap:12px; }
    .vehicle-card { background:white; border-radius:10px; overflow:hidden; }
    .vehicle-header { display:flex; justify-content:space-between; align-items:center; padding:16px; cursor:pointer; }
    .vehicle-header:hover { background:#f8fafc; }
    .vehicle-info h3 { margin:0 0 4px; font-size:14px; font-weight:600; }
    .plate { font-family:monospace; font-size:12px; background:#e2e8f0; padding:2px 6px; border-radius:4px; margin-right:8px; }
    .km { font-size:12px; color:#64748b; }
    .vehicle-badges { display:flex; align-items:center; gap:8px; }
    .badge { width:22px; height:22px; border-radius:50%; display:flex; align-items:center; justify-content:center; font-size:10px; font-weight:700; color:white; }
    .badge.overdue { background:#dc2626; }
    .badge.due { background:#f59e0b; }
    .badge.upcoming { background:#3b82f6; }
    .expand-arrow { color:#94a3b8; transition:transform .2s; }
    .expand-arrow.open { transform:rotate(180deg); }
    .maintenance-list { border-top:1px solid #f1f5f9; }
    .maint-row { display:flex; align-items:center; padding:12px 16px; border-bottom:1px solid #f1f5f9; gap:16px; }
    .maint-row.overdue { background:#fef2f2; }
    .maint-row.due { background:#fffbeb; }
    .maint-info { flex:1; }
    .maint-name { display:block; font-size:13px; font-weight:500; margin-bottom:2px; }
    .maint-last { font-size:11px; color:#64748b; }
    .maint-next { min-width:120px; text-align:right; font-size:12px; color:#475569; }
    .maint-actions { display:flex; align-items:center; gap:8px; }
    .status-badge { padding:4px 8px; border-radius:4px; font-size:10px; font-weight:600; }
    .status-badge.ok { background:#dcfce7; color:#16a34a; }
    .status-badge.upcoming { background:#dbeafe; color:#2563eb; }
    .status-badge.due { background:#fef3c7; color:#d97706; }
    .status-badge.overdue { background:#fee2e2; color:#dc2626; }
    .btn-done { width:28px; height:28px; border:1px solid #e2e8f0; border-radius:6px; background:white; color:#16a34a; cursor:pointer; font-weight:bold; }
    .btn-done:hover { background:#dcfce7; border-color:#16a34a; }
    .btn-remove { width:28px; height:28px; border:1px solid #e2e8f0; border-radius:6px; background:white; color:#dc2626; cursor:pointer; font-size:12px; }
    .btn-remove:hover { background:#fee2e2; border-color:#dc2626; }
    .btn-add-maint { display:flex; align-items:center; justify-content:center; gap:8px; width:100%; padding:14px; border:2px dashed #cbd5e1; border-radius:8px; background:transparent; color:#64748b; font-size:13px; font-weight:500; cursor:pointer; margin-top:8px; transition:all .2s; }
    .btn-add-maint:hover { border-color:#8b5cf6; color:#8b5cf6; background:#f5f3ff; }
    .btn-add-maint span { font-size:16px; }
    .empty-state { text-align:center; padding:60px; color:#64748b; }
    .agenda-view { display:flex; flex-direction:column; gap:20px; }
    .agenda-summary { display:flex; gap:12px; }
    .summary-card { flex:1; padding:20px; border-radius:12px; text-align:center; }
    .summary-card.urgent { background:linear-gradient(135deg,#fee2e2,#fecaca); }
    .summary-card.soon { background:linear-gradient(135deg,#fef3c7,#fde68a); }
    .summary-card.ok { background:linear-gradient(135deg,#dcfce7,#bbf7d0); }
    .sum-count { display:block; font-size:32px; font-weight:700; }
    .summary-card.urgent .sum-count { color:#dc2626; }
    .summary-card.soon .sum-count { color:#d97706; }
    .summary-card.ok .sum-count { color:#16a34a; }
    .sum-label { font-size:13px; color:#64748b; }
    .agenda-section { background:white; border-radius:12px; overflow:hidden; }
    .section-header { display:flex; align-items:center; gap:10px; padding:14px 16px; font-weight:600; }
    .section-header.urgent { background:linear-gradient(135deg,#fee2e2,#fecaca); color:#dc2626; }
    .section-header.soon { background:linear-gradient(135deg,#fef3c7,#fde68a); color:#d97706; }
    .section-header.ok { background:linear-gradient(135deg,#dcfce7,#bbf7d0); color:#16a34a; }
    .section-icon { font-size:16px; }
    .section-title { flex:1; font-size:14px; }
    .section-count { background:rgba(0,0,0,.1); padding:2px 8px; border-radius:10px; font-size:12px; }
    .agenda-items { position:relative; padding-left:24px; }
    .agenda-items::before { content:''; position:absolute; left:19px; top:0; bottom:0; width:2px; background:#e2e8f0; }
    .agenda-items.collapsed { max-height:180px; overflow:hidden; }
    .agenda-items.collapsed.expanded { max-height:none; }
    .agenda-item { display:flex; align-items:center; gap:12px; padding:14px 16px 14px 20px; border-bottom:1px solid #f1f5f9; position:relative; }
    .agenda-item:last-child { border-bottom:none; }
    .timeline-dot { position:absolute; left:-5px; width:10px; height:10px; border-radius:50%; border:2px solid white; }
    .agenda-item.urgent .timeline-dot { background:#dc2626; }
    .agenda-item.soon .timeline-dot { background:#f59e0b; }
    .agenda-item.ok .timeline-dot { background:#22c55e; }
    .item-content { flex:1; }
    .item-main { display:flex; align-items:center; gap:8px; margin-bottom:4px; }
    .item-maint { font-size:14px; font-weight:600; color:#1e293b; }
    .item-vehicle { font-size:13px; color:#64748b; }
    .item-meta { display:flex; align-items:center; gap:12px; }
    .item-plate { font-family:monospace; font-size:11px; background:#e2e8f0; padding:2px 6px; border-radius:4px; }
    .item-km { font-size:12px; font-weight:600; }
    .item-km.urgent { color:#dc2626; }
    .item-km.soon { color:#d97706; }
    .item-km.ok { color:#16a34a; }
    .item-actions { display:flex; gap:8px; }
    .btn-action { padding:8px 14px; border:none; border-radius:6px; font-size:12px; font-weight:500; cursor:pointer; transition:all .2s; }
    .btn-action.done { background:#dcfce7; color:#16a34a; }
    .btn-action.done:hover { background:#16a34a; color:white; }
    .btn-show-more { width:100%; padding:12px; border:none; background:#f8fafc; color:#64748b; font-size:12px; cursor:pointer; }
    .btn-show-more:hover { background:#f1f5f9; color:#1e293b; }
    .empty-agenda { text-align:center; padding:60px 20px; background:white; border-radius:12px; }
    .empty-icon { font-size:48px; display:block; margin-bottom:12px; }
    .empty-agenda p { font-size:16px; font-weight:500; color:#64748b; margin:0 0 8px; }
    .empty-hint { font-size:13px; color:#94a3b8; }
    .overlay { position:fixed; inset:0; background:rgba(0,0,0,.5); display:flex; justify-content:flex-end; z-index:1000; }
    .panel { width:100%; max-width:480px; height:100vh; background:white; display:flex; flex-direction:column; }
    .panel.narrow { max-width:400px; }
    .panel-header { display:flex; justify-content:space-between; align-items:center; padding:20px; color:white; }
    .panel-header.blue { background:linear-gradient(135deg,#3b82f6,#2563eb); }
    .panel-header.green { background:linear-gradient(135deg,#22c55e,#16a34a); }
    .panel-header.purple { background:linear-gradient(135deg,#8b5cf6,#7c3aed); }
    .panel-header.low { background:linear-gradient(135deg,#22c55e,#16a34a); }
    .panel-header.medium { background:linear-gradient(135deg,#f59e0b,#d97706); }
    .panel-header.high { background:linear-gradient(135deg,#ef4444,#dc2626); }
    .panel-header.critical { background:linear-gradient(135deg,#1e293b,#0f172a); }
    .panel-header h2 { margin:0; font-size:18px; }
    .btn-close { background:rgba(255,255,255,.2); border:none; width:32px; height:32px; border-radius:8px; color:white; cursor:pointer; font-size:16px; }
    .panel-body { flex:1; overflow-y:auto; padding:20px; }
    .form-group { margin-bottom:14px; }
    .form-group label { display:block; font-size:12px; font-weight:500; color:#64748b; margin-bottom:6px; }
    .form-group input, .form-group select, .form-group textarea { width:100%; padding:10px 12px; border:1px solid #e2e8f0; border-radius:8px; font-size:13px; }
    .form-row { display:grid; grid-template-columns:1fr 1fr; gap:12px; }
    .form-group.toggle label { display:flex; align-items:center; gap:10px; cursor:pointer; font-size:13px; color:#1e293b; }
    .form-group.toggle input { display:none; }
    .switch { width:40px; height:22px; background:#e2e8f0; border-radius:11px; position:relative; transition:background .2s; }
    .switch::after { content:''; position:absolute; width:18px; height:18px; background:white; border-radius:50%; top:2px; left:2px; transition:transform .2s; }
    .form-group.toggle input:checked + .switch { background:#3b82f6; }
    .form-group.toggle input:checked + .switch::after { transform:translateX(18px); }
    .recap { background:#f8fafc; padding:12px; border-radius:8px; margin-bottom:20px; }
    .recap .label { display:block; font-size:14px; font-weight:600; color:#1e293b; margin-bottom:4px; }
    .recap .value { font-size:12px; color:#64748b; }
    .badges { display:flex; gap:10px; margin-bottom:20px; }
    .active-badge { padding:6px 12px; border-radius:6px; font-size:12px; font-weight:600; background:#dcfce7; color:#16a34a; }
    .active-badge.inactive { background:#f1f5f9; color:#64748b; }
    .section { margin-bottom:20px; }
    .section h4 { font-size:13px; font-weight:600; margin:0 0 10px; color:#1e293b; }
    .section p { font-size:13px; color:#475569; margin:0; padding:12px; background:#f8fafc; border-radius:8px; }
    .intervals { display:flex; align-items:center; gap:16px; }
    .int-card { flex:1; padding:16px; background:#f8fafc; border-radius:10px; text-align:center; }
    .int-val { display:block; font-size:24px; font-weight:700; color:#1e293b; }
    .int-unit { font-size:12px; color:#64748b; }
    .or { font-size:12px; font-weight:600; color:#94a3b8; }
    .cost-display { display:flex; align-items:baseline; gap:8px; padding:16px; background:#f8fafc; border-radius:10px; }
    .cost-val { font-size:28px; font-weight:700; color:#1e293b; }
    .cost-unit { font-size:14px; color:#64748b; }
    .panel-footer { display:flex; justify-content:flex-end; gap:10px; padding:16px 20px; border-top:1px solid #e2e8f0; background:#f8fafc; }
    .btn-cancel { padding:10px 16px; background:white; border:1px solid #e2e8f0; border-radius:8px; font-size:13px; color:#64748b; cursor:pointer; }
    .btn-save { padding:10px 20px; background:#1e3a5f; border:none; border-radius:8px; font-size:13px; font-weight:500; color:white; cursor:pointer; }
    .btn-save:disabled { opacity:.5; cursor:not-allowed; }
    .btn-save.green { background:#16a34a; }
    .btn-save.purple { background:#8b5cf6; }
    .btn-save.purple:hover { background:#7c3aed; }
    .btn-edit { padding:10px 16px; background:#3b82f6; border:none; border-radius:8px; font-size:13px; color:white; cursor:pointer; }
    .btn-delete { padding:10px 16px; background:white; border:1px solid #fee2e2; border-radius:8px; font-size:13px; color:#dc2626; cursor:pointer; }
    .vehicle-recap-box { display:flex; align-items:center; gap:12px; padding:16px; background:#f8fafc; border-radius:10px; margin-bottom:20px; }
    .recap-icon { font-size:28px; }
    .recap-info { flex:1; }
    .recap-name { display:block; font-size:16px; font-weight:600; color:#1e293b; }
    .recap-plate { font-size:12px; color:#64748b; }
    .select-title { font-size:14px; font-weight:600; color:#1e293b; margin:0 0 12px; }
    .templates-select-list { display:flex; flex-direction:column; gap:8px; }
    .template-select-item { display:flex; align-items:center; gap:12px; padding:14px; background:white; border:2px solid #e2e8f0; border-radius:10px; cursor:pointer; transition:all .2s; }
    .template-select-item:hover { border-color:#8b5cf6; }
    .template-select-item.selected { border-color:#8b5cf6; background:#f5f3ff; }
    .tpl-icon { width:36px; height:36px; border-radius:8px; display:flex; align-items:center; justify-content:center; font-size:16px; }
    .tpl-icon.low { background:#dcfce7; }
    .tpl-icon.medium { background:#fef3c7; }
    .tpl-icon.high { background:#fee2e2; }
    .tpl-icon.critical { background:#1e293b; }
    .tpl-info { flex:1; }
    .tpl-name { display:block; font-size:14px; font-weight:500; color:#1e293b; }
    .tpl-interval { font-size:11px; color:#64748b; }
    .tpl-cost { font-size:12px; font-weight:600; color:#64748b; }
    .tpl-check { width:24px; height:24px; background:#8b5cf6; color:white; border-radius:50%; display:flex; align-items:center; justify-content:center; font-size:14px; }
    .empty-templates { padding:30px; text-align:center; color:#64748b; font-size:13px; }
    .btn-assign { padding:10px 16px; background:#8b5cf6; border:none; border-radius:8px; font-size:13px; color:white; cursor:pointer; }
    .btn-assign:hover { background:#7c3aed; }
    .assign-template-info { background:#f8fafc; padding:16px; border-radius:10px; margin-bottom:16px; }
    .assign-label { display:block; font-size:11px; color:#64748b; margin-bottom:4px; }
    .assign-name { display:block; font-size:16px; font-weight:600; color:#1e293b; margin-bottom:4px; }
    .assign-interval { font-size:12px; color:#64748b; }
    .assign-actions-bar { display:flex; gap:8px; margin-bottom:16px; }
    .btn-select-all, .btn-select-none { padding:8px 12px; border:1px solid #e2e8f0; border-radius:6px; background:white; font-size:12px; color:#64748b; cursor:pointer; }
    .btn-select-all:hover, .btn-select-none:hover { background:#f1f5f9; }
    .vehicles-checklist { display:flex; flex-direction:column; gap:8px; }
    .vehicle-checkbox { display:flex; align-items:center; gap:12px; padding:12px; background:white; border:2px solid #e2e8f0; border-radius:10px; cursor:pointer; transition:all .2s; }
    .vehicle-checkbox:hover { border-color:#8b5cf6; }
    .vehicle-checkbox.selected { border-color:#8b5cf6; background:#f5f3ff; }
    .vehicle-checkbox.already { opacity:.6; cursor:not-allowed; border-color:#e2e8f0; background:#f8fafc; }
    .vehicle-checkbox input { display:none; }
    .vehicle-check-info { flex:1; }
    .vehicle-check-name { display:block; font-size:14px; font-weight:500; color:#1e293b; }
    .vehicle-check-plate { font-family:monospace; font-size:11px; background:#e2e8f0; padding:2px 6px; border-radius:4px; margin-right:8px; }
    .vehicle-check-km { font-size:11px; color:#64748b; }
    .already-badge { font-size:10px; background:#fef3c7; color:#d97706; padding:4px 8px; border-radius:4px; }
    .check-icon { width:24px; height:24px; background:#8b5cf6; color:white; border-radius:50%; display:flex; align-items:center; justify-content:center; font-size:14px; }
    .assign-count { flex:1; font-size:13px; color:#64748b; }
    @media (max-width:768px) { .form-row { grid-template-columns:1fr; } .templates-grid { grid-template-columns:1fr; } .alerts-summary { flex-wrap:wrap; } .alert-card { min-width:calc(50% - 6px); } }
  `]
})
export class MaintenanceTemplatesComponent implements OnInit {
  activeTab: 'templates' | 'schedule' = 'templates';
  templates: MaintenanceTemplate[] = [];
  filteredTemplates: MaintenanceTemplate[] = [];
  selected: MaintenanceTemplate | null = null;
  editing: MaintenanceTemplate | null = null;
  isFormOpen = false;
  searchQuery = '';
  categoryFilter = '';
  categories = ['Moteur', 'Freinage', 'Transmission', 'Filtres', 'Électrique', 'Suspension', 'Autre'];
  form: any = this.getEmptyForm();
  vehicleSchedules: VehicleMaintenanceStatus[] = [];
  filteredVehicleSchedules: VehicleMaintenanceStatus[] = [];
  expanded: string[] = [];
  vehicleSearchQuery = '';
  statusFilter = '';
  isMarkOpen = false;
  markData: any = this.getEmptyMark();
  isAddToVehicleOpen = false;
  addToVehicleData: any = this.getEmptyAddToVehicle();
  allVehicles: {id: string; name: string; plate: string; mileage: number}[] = [];
  scheduleView: 'agenda' | 'vehicles' = 'agenda';
  showOkItems = false;
  urgentItems: any[] = [];
  soonItems: any[] = [];
  okItems: any[] = [];

  ngOnInit() { this.loadTemplates(); this.loadVehicles(); this.loadAllVehicles(); }

  getEmptyForm() { return { name:'', description:'', category:'', priority:'medium', intervalKm:null, intervalMonths:null, estimatedCost:null, isActive:true }; }
  getEmptyMark() { return { vehicleId:'', vehicleName:'', vehiclePlate:'', templateId:'', maintenanceName:'', date:new Date().toISOString().split('T')[0], mileage:null, cost:null, supplier:'', notes:'' }; }
  getEmptyAddToVehicle() { return { vehicleId:'', vehicleName:'', vehiclePlate:'', vehicleMileage:0, selectedTemplateId:'' }; }

  loadTemplates() {
    this.templates = [
      { id:'1', name:'Vidange moteur', description:'Changement huile moteur et filtre', intervalKm:10000, intervalMonths:12, estimatedCost:150, priority:'high', category:'Moteur', isActive:true },
      { id:'2', name:'Chaîne de distribution', description:'Remplacement chaîne et tendeur', intervalKm:100000, intervalMonths:60, estimatedCost:800, priority:'critical', category:'Moteur', isActive:true },
      { id:'3', name:'Bougies', description:'Remplacement des bougies d\'allumage', intervalKm:30000, intervalMonths:24, estimatedCost:80, priority:'medium', category:'Moteur', isActive:true },
      { id:'4', name:'Disques de freins', description:'Remplacement disques avant et arrière', intervalKm:60000, intervalMonths:36, estimatedCost:350, priority:'high', category:'Freinage', isActive:true },
      { id:'5', name:'Plaquettes de freins', description:'Remplacement plaquettes avant', intervalKm:30000, intervalMonths:24, estimatedCost:120, priority:'high', category:'Freinage', isActive:true },
      { id:'6', name:'Filtre à air', description:'Remplacement filtre à air moteur', intervalKm:20000, intervalMonths:12, estimatedCost:40, priority:'low', category:'Filtres', isActive:true },
      { id:'7', name:'Filtre habitacle', description:'Remplacement filtre climatisation', intervalKm:15000, intervalMonths:12, estimatedCost:30, priority:'low', category:'Filtres', isActive:true },
      { id:'8', name:'Liquide de frein', description:'Purge et remplacement liquide', intervalKm:40000, intervalMonths:24, estimatedCost:60, priority:'medium', category:'Freinage', isActive:true }
    ];
    this.filterTemplates();
  }

  loadVehicles() {
    this.vehicleSchedules = [
      { vehicleId:'v1', vehicleName:'Peugeot 208', vehiclePlate:'245 TUN 7890', currentMileage:52000, maintenanceItems:[
        { templateId:'1', templateName:'Vidange moteur', lastDoneDate:new Date('2025-12-15'), lastDoneKm:45000, nextDueKm:55000, status:'upcoming', kmUntilDue:3000 },
        { templateId:'4', templateName:'Disques de freins', lastDoneDate:null, lastDoneKm:null, nextDueKm:60000, status:'upcoming', kmUntilDue:8000 },
        { templateId:'5', templateName:'Plaquettes de freins', lastDoneDate:new Date('2025-06-10'), lastDoneKm:35000, nextDueKm:65000, status:'ok', kmUntilDue:13000 }
      ]},
      { vehicleId:'v2', vehicleName:'Renault Clio', vehiclePlate:'189 TUN 4521', currentMileage:78500, maintenanceItems:[
        { templateId:'1', templateName:'Vidange moteur', lastDoneDate:new Date('2025-08-20'), lastDoneKm:68000, nextDueKm:78000, status:'overdue', kmUntilDue:-500 },
        { templateId:'3', templateName:'Bougies', lastDoneDate:new Date('2024-03-15'), lastDoneKm:45000, nextDueKm:75000, status:'overdue', kmUntilDue:-3500 },
        { templateId:'6', templateName:'Filtre à air', lastDoneDate:new Date('2025-10-01'), lastDoneKm:75000, nextDueKm:95000, status:'ok', kmUntilDue:16500 }
      ]},
      { vehicleId:'v3', vehicleName:'Citroën Berlingo', vehiclePlate:'312 TUN 1122', currentMileage:125000, maintenanceItems:[
        { templateId:'1', templateName:'Vidange moteur', lastDoneDate:new Date('2025-11-01'), lastDoneKm:120000, nextDueKm:130000, status:'upcoming', kmUntilDue:5000 },
        { templateId:'2', templateName:'Chaîne de distribution', lastDoneDate:null, lastDoneKm:null, nextDueKm:100000, status:'overdue', kmUntilDue:-25000 },
        { templateId:'4', templateName:'Disques de freins', lastDoneDate:new Date('2025-01-20'), lastDoneKm:105000, nextDueKm:165000, status:'ok', kmUntilDue:40000 }
      ]}
    ];
    this.filterVehicles();
  }

  filterTemplates() {
    let r = [...this.templates];
    if (this.searchQuery) { const q = this.searchQuery.toLowerCase(); r = r.filter(t => t.name.toLowerCase().includes(q)); }
    if (this.categoryFilter) r = r.filter(t => t.category === this.categoryFilter);
    this.filteredTemplates = r;
  }

  filterVehicles() {
    let r = [...this.vehicleSchedules];
    if (this.vehicleSearchQuery) { const q = this.vehicleSearchQuery.toLowerCase(); r = r.filter(v => v.vehicleName.toLowerCase().includes(q) || v.vehiclePlate.toLowerCase().includes(q)); }
    if (this.statusFilter) r = r.filter(v => v.maintenanceItems.some(m => m.status === this.statusFilter));
    this.filteredVehicleSchedules = r;
    this.updateAgendaItems();
  }

  updateAgendaItems() {
    this.urgentItems = [];
    this.soonItems = [];
    this.okItems = [];
    for (const v of this.filteredVehicleSchedules) {
      for (const m of v.maintenanceItems) {
        const km = m.kmUntilDue ?? 0;
        const item = { ...m, vehicleId: v.vehicleId, vehicleName: v.vehicleName, vehiclePlate: v.vehiclePlate, currentMileage: v.currentMileage };
        if (km <= 0 || m.status === 'overdue' || m.status === 'due') this.urgentItems.push(item);
        else if (km <= 5000) this.soonItems.push(item);
        else this.okItems.push(item);
      }
    }
    this.urgentItems.sort((a, b) => (a.kmUntilDue ?? 0) - (b.kmUntilDue ?? 0));
    this.soonItems.sort((a, b) => (a.kmUntilDue ?? 0) - (b.kmUntilDue ?? 0));
    this.okItems.sort((a, b) => (a.kmUntilDue ?? 0) - (b.kmUntilDue ?? 0));
  }

  getActiveTemplates() { return this.templates.filter(t => t.isActive).length; }
  getCriticalTemplates() { return this.templates.filter(t => t.priority === 'critical').length; }
  getPriorityLabel(p: string) { return { low:'Faible', medium:'Moyenne', high:'Haute', critical:'Critique' }[p] || p; }
  getStatusLabel(s: string) { return { ok:'OK', upcoming:'À venir', due:'À faire', overdue:'En retard' }[s] || s; }
  formatDate(d: Date) { return new Date(d).toLocaleDateString('fr-FR'); }
  getTotalByStatus(s: string) { return this.vehicleSchedules.reduce((sum, v) => sum + v.maintenanceItems.filter(m => m.status === s).length, 0); }
  getCount(v: VehicleMaintenanceStatus, s: string) { return v.maintenanceItems.filter(m => m.status === s).length; }
  toggleVehicle(id: string) { this.expanded.includes(id) ? this.expanded = this.expanded.filter(x => x !== id) : this.expanded.push(id); }

  selectTemplate(t: MaintenanceTemplate) { this.selected = t; }
  closeDetail() { this.selected = null; }
  openTemplateForm() { this.editing = null; this.form = this.getEmptyForm(); this.isFormOpen = true; }
  editTemplate(t: MaintenanceTemplate) { this.editing = t; this.form = { ...t }; this.isFormOpen = true; this.selected = null; }
  closeForm() { this.isFormOpen = false; this.editing = null; }
  isFormValid() { return this.form.name && this.form.category && (this.form.intervalKm || this.form.intervalMonths); }
  saveTemplate() {
    if (this.editing) { const i = this.templates.findIndex(t => t.id === this.editing!.id); if (i !== -1) this.templates[i] = { ...this.editing, ...this.form }; }
    else { this.templates.unshift({ id:'t'+Date.now(), ...this.form }); }
    this.filterTemplates(); this.closeForm();
  }
  deleteTemplate(t: MaintenanceTemplate) { if (confirm('Supprimer ce modèle?')) { this.templates = this.templates.filter(x => x.id !== t.id); this.filterTemplates(); this.closeDetail(); } }

  openMarkDone(v: VehicleMaintenanceStatus, m: MaintenanceItem) { this.markData = { vehicleId:v.vehicleId, vehicleName:v.vehicleName, vehiclePlate:v.vehiclePlate, templateId:m.templateId, maintenanceName:m.templateName, date:new Date().toISOString().split('T')[0], mileage:v.currentMileage, cost:null, supplier:'', notes:'' }; this.isMarkOpen = true; }
  closeMarkDone() { this.isMarkOpen = false; }
  isMarkValid() { return this.markData.date && this.markData.mileage && this.markData.cost; }
  confirmMarkDone() {
    const v = this.vehicleSchedules.find(x => x.vehicleId === this.markData.vehicleId);
    if (v) {
      const m = v.maintenanceItems.find(x => x.templateId === this.markData.templateId);
      if (m) {
        m.lastDoneDate = new Date(this.markData.date);
        m.lastDoneKm = this.markData.mileage;
        const t = this.templates.find(x => x.id === m.templateId);
        if (t && t.intervalKm) { m.nextDueKm = this.markData.mileage + t.intervalKm; m.kmUntilDue = (m.nextDueKm || 0) - v.currentMileage; m.status = m.kmUntilDue > 5000 ? 'ok' : m.kmUntilDue > 0 ? 'upcoming' : 'overdue'; }
      }
      v.currentMileage = this.markData.mileage;
    }
    this.closeMarkDone();
  }

  // Vehicle-centric methods
  loadAllVehicles() {
    this.allVehicles = [
      { id:'v1', name:'Peugeot 208', plate:'245 TUN 7890', mileage:52000 },
      { id:'v2', name:'Renault Clio', plate:'189 TUN 4521', mileage:78500 },
      { id:'v3', name:'Citroën Berlingo', plate:'312 TUN 1122', mileage:125000 },
      { id:'v4', name:'Dacia Duster', plate:'456 TUN 3344', mileage:35000 },
      { id:'v5', name:'Peugeot Partner', plate:'789 TUN 5566', mileage:92000 },
      { id:'v6', name:'Renault Kangoo', plate:'147 TUN 8899', mileage:68000 }
    ];
  }

  openAddToVehicle(v: VehicleMaintenanceStatus) {
    this.addToVehicleData = { vehicleId:v.vehicleId, vehicleName:v.vehicleName, vehiclePlate:v.vehiclePlate, vehicleMileage:v.currentMileage, selectedTemplateId:'' };
    this.isAddToVehicleOpen = true;
  }

  closeAddToVehicle() { this.isAddToVehicleOpen = false; }

  getAvailableTemplatesForVehicle(): MaintenanceTemplate[] {
    const v = this.vehicleSchedules.find(x => x.vehicleId === this.addToVehicleData.vehicleId);
    const assignedIds = v ? v.maintenanceItems.map(m => m.templateId) : [];
    return this.templates.filter(t => t.isActive && !assignedIds.includes(t.id));
  }

  selectTemplateForVehicle(t: MaintenanceTemplate) {
    this.addToVehicleData.selectedTemplateId = t.id;
  }

  confirmAddToVehicle() {
    const t = this.templates.find(x => x.id === this.addToVehicleData.selectedTemplateId);
    if (!t) return;
    let v = this.vehicleSchedules.find(x => x.vehicleId === this.addToVehicleData.vehicleId);
    if (!v) {
      v = { vehicleId:this.addToVehicleData.vehicleId, vehicleName:this.addToVehicleData.vehicleName, vehiclePlate:this.addToVehicleData.vehiclePlate, currentMileage:this.addToVehicleData.vehicleMileage, maintenanceItems:[] };
      this.vehicleSchedules.push(v);
    }
    const nextKm = t.intervalKm ? this.addToVehicleData.vehicleMileage + t.intervalKm : null;
    const kmUntil = nextKm ? nextKm - this.addToVehicleData.vehicleMileage : null;
    v.maintenanceItems.push({ templateId:t.id, templateName:t.name, lastDoneDate:null, lastDoneKm:null, nextDueKm:nextKm, status:'upcoming', kmUntilDue:kmUntil });
    this.filterVehicles();
    this.closeAddToVehicle();
  }

  removeMaintenanceFromVehicle(v: VehicleMaintenanceStatus, m: MaintenanceItem) {
    if (confirm('Retirer cet entretien du véhicule ?')) {
      v.maintenanceItems = v.maintenanceItems.filter(x => x.templateId !== m.templateId);
      this.filterVehicles();
    }
  }

  // Agenda methods
  getAgendaItems(category: 'urgent' | 'soon' | 'ok'): any[] {
    const items: any[] = [];
    for (const v of this.filteredVehicleSchedules) {
      for (const m of v.maintenanceItems) {
        const km = m.kmUntilDue ?? 0;
        let cat: 'urgent' | 'soon' | 'ok';
        if (km <= 0 || m.status === 'overdue' || m.status === 'due') cat = 'urgent';
        else if (km <= 5000) cat = 'soon';
        else cat = 'ok';
        if (cat === category) {
          items.push({ ...m, vehicleId: v.vehicleId, vehicleName: v.vehicleName, vehiclePlate: v.vehiclePlate, currentMileage: v.currentMileage });
        }
      }
    }
    return items.sort((a, b) => (a.kmUntilDue ?? 0) - (b.kmUntilDue ?? 0));
  }

  openMarkDoneFromAgenda(item: any) {
    const v = this.vehicleSchedules.find(x => x.vehicleId === item.vehicleId);
    const m = v?.maintenanceItems.find(x => x.templateId === item.templateId);
    if (v && m) this.openMarkDone(v, m);
  }
}
