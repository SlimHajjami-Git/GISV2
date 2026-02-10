import { Component, OnInit, NgZone, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
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
        <!-- Header -->
        <div class="page-header">
          <div class="header-content">
            <h1>🔧 Gestion des Entretiens</h1>
            <p>Planifiez et suivez les entretiens de votre flotte</p>
          </div>
          <div class="header-tabs">
            <button class="tab-btn" [class.active]="activeTab === 'templates'" (click)="activeTab = 'templates'">
              📋 Modèles
            </button>
            <button class="tab-btn" [class.active]="activeTab === 'schedule'" (click)="activeTab = 'schedule'">
              📅 Planning
            </button>
          </div>
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

        <!-- TAB 2: Planning -->
        <div class="tab-content" *ngIf="activeTab === 'schedule'" @fadeIn>
          <!-- Stats Summary Cards -->
          <div class="planning-stats">
            <div class="plan-stat-card urgent">
              <div class="stat-icon-wrap urgent">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/>
                </svg>
              </div>
              <div class="stat-info">
                <span class="stat-number">{{ urgentItems.length }}</span>
                <span class="stat-text">Urgents</span>
              </div>
              <span class="stat-desc">Entretiens dépassés ou à faire immédiatement</span>
            </div>
            <div class="plan-stat-card warning">
              <div class="stat-icon-wrap warning">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/>
                </svg>
              </div>
              <div class="stat-info">
                <span class="stat-number">{{ soonItems.length }}</span>
                <span class="stat-text">À venir</span>
              </div>
              <span class="stat-desc">Dans les 5 000 prochains km</span>
            </div>
            <div class="plan-stat-card success">
              <div class="stat-icon-wrap success">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/>
                </svg>
              </div>
              <div class="stat-info">
                <span class="stat-number">{{ okItems.length }}</span>
                <span class="stat-text">OK</span>
              </div>
              <span class="stat-desc">Aucune action requise</span>
            </div>
            <div class="plan-stat-card info">
              <div class="stat-icon-wrap info">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <rect x="1" y="3" width="15" height="13"/><polygon points="16 8 20 8 23 11 23 16 16 16 16 8"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/>
                </svg>
              </div>
              <div class="stat-info">
                <span class="stat-number">{{ filteredVehicleSchedules.length }}</span>
                <span class="stat-text">Véhicules</span>
              </div>
              <span class="stat-desc">Avec entretiens planifiés</span>
            </div>
          </div>

          <!-- Filter & View Toggle -->
          <div class="planning-toolbar">
            <div class="search-box">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/>
              </svg>
              <input type="text" placeholder="Rechercher un véhicule..." [(ngModel)]="vehicleSearchQuery" (input)="filterVehicles()">
            </div>
            <div class="view-switcher">
              <button [class.active]="scheduleView === 'agenda'" (click)="scheduleView = 'agenda'">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="8" y1="6" x2="21" y2="6"/><line x1="8" y1="12" x2="21" y2="12"/><line x1="8" y1="18" x2="21" y2="18"/>
                  <line x1="3" y1="6" x2="3.01" y2="6"/><line x1="3" y1="12" x2="3.01" y2="12"/><line x1="3" y1="18" x2="3.01" y2="18"/>
                </svg>
                Liste
              </button>
              <button [class.active]="scheduleView === 'vehicles'" (click)="scheduleView = 'vehicles'">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/>
                  <rect x="14" y="14" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/>
                </svg>
                Véhicules
              </button>
            </div>
          </div>

          <!-- AGENDA/LIST VIEW -->
          <div class="planning-list" *ngIf="scheduleView === 'agenda'">
            <!-- Urgent Section -->
            <div class="list-section" *ngIf="urgentItems.length > 0">
              <div class="section-title urgent">
                <span class="title-icon">🔴</span>
                <span class="title-text">Action requise immédiatement</span>
                <span class="title-count">{{ urgentItems.length }}</span>
              </div>
              <div class="maintenance-cards">
                @for (item of urgentItems; track item.templateId + item.vehicleId) {
                  <div class="maint-card urgent">
                    <div class="card-left">
                      <div class="card-icon urgent">🔧</div>
                    </div>
                    <div class="card-body">
                      <div class="card-top">
                        <span class="maint-type">{{ item.templateName }}</span>
                        <span class="status-pill urgent">{{ item.kmUntilDue !== null && item.kmUntilDue < 0 ? 'Dépassé' : 'À faire' }}</span>
                      </div>
                      <div class="card-vehicle">
                        <span class="vehicle-name">{{ item.vehicleName }}</span>
                        <span class="vehicle-plate">{{ item.vehiclePlate }}</span>
                      </div>
                      <div class="card-km">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <path d="M22 12h-4l-3 9L9 3l-3 9H2"/>
                        </svg>
                        {{ item.kmUntilDue !== null && item.kmUntilDue < 0 ? ((-item.kmUntilDue) | number) + ' km dépassés' : 'Maintenance due' }}
                      </div>
                    </div>
                    <div class="card-action">
                      <button class="btn-mark-done" (click)="openMarkDoneFromAgenda(item)">
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <polyline points="20 6 9 17 4 12"/>
                        </svg>
                        Fait
                      </button>
                    </div>
                  </div>
                }
              </div>
            </div>

            <!-- Upcoming Section -->
            <div class="list-section" *ngIf="soonItems.length > 0">
              <div class="section-title warning">
                <span class="title-icon">🟠</span>
                <span class="title-text">À prévoir prochainement</span>
                <span class="title-count">{{ soonItems.length }}</span>
              </div>
              <div class="maintenance-cards">
                @for (item of soonItems; track item.templateId + item.vehicleId) {
                  <div class="maint-card warning">
                    <div class="card-left">
                      <div class="card-icon warning">🔧</div>
                    </div>
                    <div class="card-body">
                      <div class="card-top">
                        <span class="maint-type">{{ item.templateName }}</span>
                        <span class="status-pill warning">À venir</span>
                      </div>
                      <div class="card-vehicle">
                        <span class="vehicle-name">{{ item.vehicleName }}</span>
                        <span class="vehicle-plate">{{ item.vehiclePlate }}</span>
                      </div>
                      <div class="card-km">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <path d="M22 12h-4l-3 9L9 3l-3 9H2"/>
                        </svg>
                        Dans {{ item.kmUntilDue | number }} km
                      </div>
                    </div>
                    <div class="card-action">
                      <button class="btn-mark-done" (click)="openMarkDoneFromAgenda(item)">
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <polyline points="20 6 9 17 4 12"/>
                        </svg>
                        Fait
                      </button>
                    </div>
                  </div>
                }
              </div>
            </div>

            <!-- OK Section -->
            <div class="list-section" *ngIf="okItems.length > 0">
              <div class="section-title success" (click)="showOkItems = !showOkItems" style="cursor:pointer">
                <span class="title-icon">🟢</span>
                <span class="title-text">Tout est OK</span>
                <span class="title-count">{{ okItems.length }}</span>
                <span class="expand-icon">{{ showOkItems ? '▲' : '▼' }}</span>
              </div>
              <div class="maintenance-cards" *ngIf="showOkItems">
                @for (item of okItems; track item.templateId + item.vehicleId) {
                  <div class="maint-card ok">
                    <div class="card-left">
                      <div class="card-icon ok">✓</div>
                    </div>
                    <div class="card-body">
                      <div class="card-top">
                        <span class="maint-type">{{ item.templateName }}</span>
                        <span class="status-pill ok">OK</span>
                      </div>
                      <div class="card-vehicle">
                        <span class="vehicle-name">{{ item.vehicleName }}</span>
                        <span class="vehicle-plate">{{ item.vehiclePlate }}</span>
                      </div>
                      <div class="card-km ok">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <path d="M22 12h-4l-3 9L9 3l-3 9H2"/>
                        </svg>
                        Dans {{ item.kmUntilDue | number }} km
                      </div>
                    </div>
                  </div>
                }
              </div>
            </div>

            <!-- Empty State -->
            <div class="empty-planning" *ngIf="urgentItems.length === 0 && soonItems.length === 0 && okItems.length === 0">
              <div class="empty-icon">📋</div>
              <h3>Aucun entretien planifié</h3>
              <p>Commencez par assigner des modèles d'entretien à vos véhicules</p>
              <button class="btn-switch-view" (click)="scheduleView = 'vehicles'">Voir les véhicules</button>
            </div>
          </div>

          <!-- VEHICLES GRID VIEW -->
          <div class="vehicles-grid" *ngIf="scheduleView === 'vehicles'">
            @for (v of filteredVehicleSchedules; track v.vehicleId) {
              <div class="vehicle-tile" [class.expanded]="expanded.includes(v.vehicleId)">
                <div class="tile-header" (click)="toggleVehicle(v.vehicleId)">
                  <div class="tile-icon">
                    <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <rect x="1" y="3" width="15" height="13"/><polygon points="16 8 20 8 23 11 23 16 16 16 16 8"/>
                      <circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/>
                    </svg>
                  </div>
                  <div class="tile-info">
                    <h4>{{ v.vehicleName }}</h4>
                    <div class="tile-meta">
                      <span class="tile-plate">{{ v.vehiclePlate }}</span>
                      <span class="tile-km">{{ v.currentMileage | number }} km</span>
                    </div>
                  </div>
                  <div class="tile-badges">
                    <span class="tile-badge urgent" *ngIf="getCount(v,'overdue') + getCount(v,'due') > 0">{{ getCount(v,'overdue') + getCount(v,'due') }}</span>
                    <span class="tile-badge warning" *ngIf="getCount(v,'upcoming') > 0">{{ getCount(v,'upcoming') }}</span>
                    <span class="tile-badge ok" *ngIf="getCount(v,'ok') > 0">{{ getCount(v,'ok') }}</span>
                  </div>
                  <span class="tile-arrow" [class.open]="expanded.includes(v.vehicleId)">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <polyline points="6 9 12 15 18 9"/>
                    </svg>
                  </span>
                </div>
                <div class="tile-content" *ngIf="expanded.includes(v.vehicleId)">
                  @for (m of v.maintenanceItems; track m.templateId) {
                    <div class="tile-maint-row" [class]="m.status">
                      <div class="row-icon" [class]="m.status">🔧</div>
                      <div class="row-info">
                        <span class="row-name">{{ m.templateName }}</span>
                        <span class="row-date">{{ m.lastDoneDate ? 'Dernier: ' + formatDate(m.lastDoneDate) : 'Jamais effectué' }}</span>
                      </div>
                      <div class="row-status">
                        <span class="row-km" [class]="m.status">
                          {{ m.kmUntilDue !== null ? (m.kmUntilDue > 0 ? 'Dans ' + (m.kmUntilDue | number) + ' km' : 'Dépassé ' + ((-m.kmUntilDue) | number) + ' km') : '-' }}
                        </span>
                        <span class="row-badge" [class]="m.status">{{ getStatusLabel(m.status) }}</span>
                      </div>
                      <div class="row-actions">
                        <button class="btn-done-sm" (click)="openMarkDone(v, m); $event.stopPropagation()" title="Marquer comme fait">✓</button>
                        <button class="btn-remove-sm" (click)="removeMaintenanceFromVehicle(v, m); $event.stopPropagation()" title="Retirer">✕</button>
                      </div>
                    </div>
                  }
                  <button class="btn-add-maint-tile" (click)="openAddToVehicle(v); $event.stopPropagation()">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
                    </svg>
                    Ajouter un entretien
                  </button>
                </div>
              </div>
            }
            
            <!-- Empty vehicles -->
            <div class="empty-vehicles" *ngIf="filteredVehicleSchedules.length === 0">
              <div class="empty-icon">🚗</div>
              <h3>Aucun véhicule avec entretien</h3>
              <p>Assignez des modèles d'entretien à vos véhicules</p>
            </div>
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
            <div class="form-group toggle"><label><input type="checkbox" [(ngModel)]="form.isActive"><span class="switch"></span> Actif</label></div>
            
            <div class="notification-section">
              <h4>🔔 Notifications (optionnel)</h4>
              <p class="section-hint">Configurez quand vous souhaitez être notifié avant l'échéance</p>
              <div class="form-row">
                <div class="form-group"><label>Notifier à X km restants</label><input type="number" [(ngModel)]="form.notifyKmBefore" placeholder="1000"></div>
                <div class="form-group"><label>Notifier X jours avant</label><input type="number" [(ngModel)]="form.notifyDaysBefore" placeholder="7"></div>
              </div>
            </div>
          </div>
          <div class="panel-footer"><button class="btn-cancel" (click)="closeForm()">Annuler</button><button class="btn-save" (click)="saveTemplate()" [disabled]="!isFormValid()">Enregistrer</button></div>
        </div>
      </div>

      <!-- Mark Done Panel -->
      <div class="overlay" *ngIf="isMarkOpen" @fadeIn (click)="closeMarkDone()">
        <div class="panel" @slideIn (click)="$event.stopPropagation()">
          <div class="panel-header green">
            <h2>Entretien effectué</h2>
            <button class="btn-close" (click)="closeMarkDone()">✕</button>
          </div>
          <div class="panel-body">
            <div class="recap"><span class="label">{{ markData.vehicleName }}</span><span class="value">{{ markData.vehiclePlate }}</span></div>
            
            <div class="form-row">
              <div class="form-group"><label>Date *</label><input type="date" [(ngModel)]="markData.date"></div>
              <div class="form-group"><label>Kilométrage *</label><input type="number" [(ngModel)]="markData.mileage" placeholder="45000"></div>
            </div>
            
            <div class="form-group"><label>Fournisseur / Garage</label><input [(ngModel)]="markData.supplier" placeholder="Nom du prestataire..."></div>
            
            <div class="invoice-section">
              <div class="invoice-header">
                <h4>🧾 Détail de la facture</h4>
              </div>
              
              <div class="invoice-lines">
                <div class="invoice-line" *ngFor="let line of markData.invoiceLines; let i = index">
                  <div class="line-top">
                    <select class="line-select" [ngModel]="line.templateId || (line.isCustom ? 'other' : '')" (ngModelChange)="onLineTemplateChange(line, $event)">
                      <option value="" disabled>Choisir un entretien...</option>
                      <optgroup label="Entretiens du véhicule">
                        <option *ngFor="let t of getVehicleMaintenanceTemplates()" [value]="t.id">{{ t.name }}</option>
                      </optgroup>
                      <optgroup label="Autres modèles">
                        <option *ngFor="let t of getOtherTemplatesForDropdown()" [value]="t.id">{{ t.name }}</option>
                      </optgroup>
                      <option value="other">✏️ Autre (saisie libre)</option>
                    </select>
                    <button class="btn-remove-line" (click)="removeInvoiceLine(i)" *ngIf="markData.invoiceLines.length > 1">✕</button>
                  </div>
                  <div class="line-bottom">
                    <input class="line-desc" [(ngModel)]="line.description" [placeholder]="line.isCustom ? 'Description personnalisée...' : line.description" [readonly]="!line.isCustom && line.templateId">
                    <div class="line-price-wrap">
                      <input class="line-price" type="number" [(ngModel)]="line.price" placeholder="0">
                      <span class="price-unit">DT</span>
                    </div>
                  </div>
                  <div class="line-hint" *ngIf="line.templateId && lastPaidPrices.get(line.templateId)">
                    💡 Dernier prix payé: {{ lastPaidPrices.get(line.templateId) | number }} DT
                  </div>
                </div>
              </div>
              
              <button class="btn-add-line" (click)="addInvoiceLine()">
                <span>＋</span> Ajouter un autre entretien
              </button>
              
              <div class="invoice-total">
                <span class="total-label">Total</span>
                <span class="total-value">{{ getInvoiceTotal() | number:'1.2-2' }} DT</span>
              </div>
            </div>
            
            <div class="form-group"><label>Notes</label><textarea [(ngModel)]="markData.notes" rows="2" placeholder="Remarques, observations..."></textarea></div>
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
            <div class="section" *ngIf="selected.notifyKmBefore || selected.notifyDaysBefore"><h4>🔔 Notifications</h4><div class="intervals"><div class="int-card" *ngIf="selected.notifyKmBefore"><span class="int-val">{{ selected.notifyKmBefore | number }}</span><span class="int-unit">km avant</span></div><div class="int-card" *ngIf="selected.notifyDaysBefore"><span class="int-val">{{ selected.notifyDaysBefore }}</span><span class="int-unit">jours avant</span></div></div></div>
            <div class="section" *ngIf="lastPaidPrices.get(selected.id)"><h4>💰 Dernier prix payé</h4><div class="cost-display"><span class="cost-val">{{ lastPaidPrices.get(selected.id) | number }}</span><span class="cost-unit">DT</span></div></div>
          </div>
          <div class="panel-footer"><button class="btn-cancel" (click)="closeDetail()">Fermer</button><button class="btn-edit" (click)="editTemplate(selected)">Modifier</button><button class="btn-delete" (click)="deleteTemplate(selected)">Supprimer</button></div>
        </div>
      </div>

      <!-- Add Maintenance to Vehicle Panel -->
      <div class="overlay" *ngIf="isAddToVehicleOpen" @fadeIn (click)="closeAddToVehicle()">
        <div class="panel" @slideIn (click)="$event.stopPropagation()">
          <div class="panel-header purple">
            <h2>Ajouter des entretiens</h2>
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
            <div class="select-header">
              <h4 class="select-title">Sélectionnez les types d'entretien:</h4>
              <div class="select-actions">
                <button class="btn-select-all" (click)="selectAllTemplates()">Tout sélectionner</button>
                <button class="btn-select-none" (click)="deselectAllTemplates()">Aucun</button>
              </div>
            </div>
            <div class="selected-count" *ngIf="addToVehicleData.selectedTemplateIds.length > 0">
              <span class="count-badge purple">{{ addToVehicleData.selectedTemplateIds.length }}</span> sélectionné(s)
            </div>
            <div class="templates-select-list">
              @for (t of getAvailableTemplatesForVehicle(); track t.id) {
                <div class="template-select-item" [class.selected]="isTemplateSelected(t.id)" (click)="toggleTemplateSelection(t)">
                  <div class="tpl-checkbox" [class.checked]="isTemplateSelected(t.id)">
                    <span *ngIf="isTemplateSelected(t.id)">✓</span>
                  </div>
                  <div class="tpl-icon" [class]="t.priority">🔧</div>
                  <div class="tpl-info">
                    <span class="tpl-name">{{ t.name }}</span>
                    <span class="tpl-interval">{{ t.intervalKm ? (t.intervalKm | number) + ' km' : '' }}{{ t.intervalKm && t.intervalMonths ? ' / ' : '' }}{{ t.intervalMonths ? t.intervalMonths + ' mois' : '' }}</span>
                  </div>
                  <span class="tpl-cost">~{{ t.estimatedCost }} DT</span>
                </div>
              }
              <div class="empty-templates" *ngIf="getAvailableTemplatesForVehicle().length === 0">
                <p>Tous les entretiens sont déjà assignés à ce véhicule</p>
              </div>
            </div>
          </div>
          <div class="panel-footer">
            <span class="footer-info" *ngIf="addToVehicleData.selectedTemplateIds.length > 0">{{ addToVehicleData.selectedTemplateIds.length }} entretien(s)</span>
            <button class="btn-cancel" (click)="closeAddToVehicle()">Annuler</button>
            <button class="btn-save purple" (click)="confirmAddToVehicle()" [disabled]="addToVehicleData.selectedTemplateIds.length === 0">Ajouter</button>
          </div>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    .maintenance-page { flex:1; background:#f1f5f9; min-height:calc(100vh - 42px); }
    
    /* Header */
    .page-header { display:flex; justify-content:space-between; align-items:center; padding:20px 24px; background:white; border-bottom:1px solid #e2e8f0; }
    .header-content h1 { margin:0 0 4px; font-size:20px; font-weight:700; color:#1e293b; }
    .header-content p { margin:0; font-size:13px; color:#64748b; }
    .header-tabs { display:flex; gap:8px; }
    .tab-btn { display:flex; align-items:center; gap:8px; padding:10px 20px; border:none; background:#f1f5f9; border-radius:8px; font-size:13px; font-weight:500; color:#64748b; cursor:pointer; transition:all .2s; }
    .tab-btn:hover { background:#e2e8f0; }
    .tab-btn.active { background:#1e3a5f; color:white; }
    
    /* Content */
    .tab-content { padding:20px 24px; }
    
    /* Filter Bar */
    .filter-bar { display:flex; gap:12px; margin-bottom:20px; flex-wrap:wrap; align-items:center; }
    .search-wrapper { flex:1; min-width:200px; max-width:320px; position:relative; }
    .search-icon { position:absolute; left:12px; top:50%; transform:translateY(-50%); color:#94a3b8; }
    .search-input { width:100%; padding:10px 12px 10px 38px; border:1px solid #e2e8f0; border-radius:8px; font-size:13px; background:white; }
    .search-input:focus { outline:none; border-color:#3b82f6; }
    .filter-select { padding:10px 14px; border:1px solid #e2e8f0; border-radius:8px; font-size:13px; background:white; min-width:160px; }
    .view-toggle { display:flex; gap:4px; background:white; border:1px solid #e2e8f0; padding:4px; border-radius:8px; }
    .view-btn { padding:8px 16px; border:none; background:transparent; border-radius:6px; font-size:12px; font-weight:500; color:#64748b; cursor:pointer; transition:all .2s; }
    .view-btn:hover { color:#1e293b; background:#f8fafc; }
    .view-btn.active { background:#1e3a5f; color:white; }
    .btn-add { padding:10px 18px; background:#1e3a5f; color:white; border:none; border-radius:8px; font-size:13px; font-weight:500; cursor:pointer; margin-left:auto; transition:background .2s; }
    .btn-add:hover { background:#2d4a6f; }
    
    /* Stats Bar */
    .stats-bar { display:flex; gap:16px; margin-bottom:20px; }
    .stat-item { display:flex; align-items:center; gap:12px; padding:16px 20px; background:white; border-radius:12px; box-shadow:0 1px 3px rgba(0,0,0,.05); }
    .stat-icon { width:44px; height:44px; border-radius:10px; display:flex; align-items:center; justify-content:center; font-size:20px; }
    .stat-icon.total { background:#dbeafe; }
    .stat-icon.active { background:#dcfce7; }
    .stat-icon.critical { background:#fee2e2; }
    .stat-content { display:flex; flex-direction:column; }
    .stat-value { font-size:22px; font-weight:700; color:#1e293b; line-height:1; }
    .stat-label { font-size:12px; color:#64748b; margin-top:4px; }
    
    /* Templates Grid */
    .templates-grid { display:grid; grid-template-columns:repeat(auto-fill,minmax(300px,1fr)); gap:16px; }
    .template-card { background:white; border-radius:12px; padding:20px; cursor:pointer; border:2px solid transparent; transition:all .2s; box-shadow:0 1px 3px rgba(0,0,0,.05); }
    .template-card:hover { border-color:#3b82f6; transform:translateY(-2px); box-shadow:0 4px 12px rgba(59,130,246,.15); }
    .template-card.inactive { opacity:.6; }
    .card-header { display:flex; justify-content:space-between; align-items:flex-start; margin-bottom:14px; }
    .card-icon { width:44px; height:44px; border-radius:10px; display:flex; align-items:center; justify-content:center; font-size:20px; }
    .card-icon.low { background:#dcfce7; }
    .card-icon.medium { background:#fef3c7; }
    .card-icon.high { background:#fee2e2; }
    .card-icon.critical { background:#1e293b; }
    .priority-badge { padding:5px 10px; border-radius:6px; font-size:11px; font-weight:600; }
    .priority-badge.low { background:#dcfce7; color:#16a34a; }
    .priority-badge.medium { background:#fef3c7; color:#d97706; }
    .priority-badge.high { background:#fee2e2; color:#dc2626; }
    .priority-badge.critical { background:#1e293b; color:white; }
    .card-title { font-size:16px; font-weight:600; margin:0 0 8px; color:#1e293b; }
    .card-desc { font-size:13px; color:#64748b; margin:0 0 14px; line-height:1.4; }
    .card-intervals { display:flex; gap:16px; font-size:13px; color:#475569; margin-bottom:14px; }
    .card-intervals span { display:flex; align-items:center; gap:4px; }
    .card-footer { display:flex; justify-content:space-between; align-items:center; padding-top:14px; border-top:1px solid #f1f5f9; }
    .category-tag { padding:5px 10px; background:#f1f5f9; border-radius:6px; font-size:11px; font-weight:500; color:#475569; }
    .cost { font-size:14px; font-weight:600; color:#1e293b; }
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
    
    /* Planning Stats */
    .planning-stats { display:grid; grid-template-columns:repeat(4,1fr); gap:16px; margin-bottom:20px; }
    .plan-stat-card { background:white; border-radius:12px; padding:20px; display:flex; flex-direction:column; gap:12px; box-shadow:0 1px 3px rgba(0,0,0,.05); border-left:4px solid transparent; }
    .plan-stat-card.urgent { border-left-color:#dc2626; }
    .plan-stat-card.warning { border-left-color:#f59e0b; }
    .plan-stat-card.success { border-left-color:#16a34a; }
    .plan-stat-card.info { border-left-color:#3b82f6; }
    .stat-icon-wrap { width:48px; height:48px; border-radius:12px; display:flex; align-items:center; justify-content:center; }
    .stat-icon-wrap.urgent { background:#fee2e2; color:#dc2626; }
    .stat-icon-wrap.warning { background:#fef3c7; color:#f59e0b; }
    .stat-icon-wrap.success { background:#dcfce7; color:#16a34a; }
    .stat-icon-wrap.info { background:#dbeafe; color:#3b82f6; }
    .stat-info { display:flex; align-items:baseline; gap:8px; }
    .stat-number { font-size:28px; font-weight:700; color:#1e293b; }
    .stat-text { font-size:14px; font-weight:500; color:#64748b; }
    .stat-desc { font-size:12px; color:#94a3b8; }
    
    /* Planning Toolbar */
    .planning-toolbar { display:flex; justify-content:space-between; align-items:center; margin-bottom:20px; gap:16px; }
    .search-box { flex:1; max-width:320px; display:flex; align-items:center; gap:10px; padding:10px 14px; background:white; border:1px solid #e2e8f0; border-radius:8px; }
    .search-box svg { color:#94a3b8; flex-shrink:0; }
    .search-box input { flex:1; border:none; outline:none; font-size:13px; background:transparent; }
    .view-switcher { display:flex; gap:4px; background:white; border:1px solid #e2e8f0; padding:4px; border-radius:8px; }
    .view-switcher button { display:flex; align-items:center; gap:6px; padding:8px 16px; border:none; background:transparent; border-radius:6px; font-size:13px; font-weight:500; color:#64748b; cursor:pointer; transition:all .2s; }
    .view-switcher button:hover { background:#f8fafc; color:#1e293b; }
    .view-switcher button.active { background:#1e3a5f; color:white; }
    
    /* Planning List */
    .planning-list { display:flex; flex-direction:column; gap:24px; }
    .list-section { background:white; border-radius:12px; overflow:hidden; box-shadow:0 1px 3px rgba(0,0,0,.05); }
    .section-title { display:flex; align-items:center; gap:10px; padding:16px 20px; font-weight:600; border-bottom:1px solid #f1f5f9; }
    .section-title.urgent { background:linear-gradient(135deg,#fef2f2,#fee2e2); }
    .section-title.warning { background:linear-gradient(135deg,#fffbeb,#fef3c7); }
    .section-title.success { background:linear-gradient(135deg,#f0fdf4,#dcfce7); }
    .title-icon { font-size:16px; }
    .title-text { flex:1; font-size:14px; color:#1e293b; }
    .title-count { background:rgba(0,0,0,.1); padding:4px 10px; border-radius:12px; font-size:12px; }
    .expand-icon { color:#94a3b8; font-size:12px; }
    
    /* Maintenance Cards */
    .maintenance-cards { display:flex; flex-direction:column; }
    .maint-card { display:flex; align-items:center; gap:16px; padding:16px 20px; border-bottom:1px solid #f1f5f9; transition:background .2s; }
    .maint-card:last-child { border-bottom:none; }
    .maint-card:hover { background:#f8fafc; }
    .card-left { flex-shrink:0; }
    .maint-card .card-icon { width:44px; height:44px; border-radius:10px; display:flex; align-items:center; justify-content:center; font-size:18px; }
    .maint-card .card-icon.urgent { background:#fee2e2; }
    .maint-card .card-icon.warning { background:#fef3c7; }
    .maint-card .card-icon.ok { background:#dcfce7; }
    .card-body { flex:1; min-width:0; }
    .card-top { display:flex; align-items:center; gap:10px; margin-bottom:6px; }
    .maint-type { font-size:15px; font-weight:600; color:#1e293b; }
    .status-pill { padding:4px 10px; border-radius:12px; font-size:11px; font-weight:600; }
    .status-pill.urgent { background:#fee2e2; color:#dc2626; }
    .status-pill.warning { background:#fef3c7; color:#d97706; }
    .status-pill.ok { background:#dcfce7; color:#16a34a; }
    .card-vehicle { display:flex; align-items:center; gap:10px; margin-bottom:6px; }
    .vehicle-name { font-size:13px; color:#475569; }
    .vehicle-plate { font-family:monospace; font-size:11px; background:#e2e8f0; padding:2px 8px; border-radius:4px; color:#64748b; }
    .card-km { display:flex; align-items:center; gap:6px; font-size:12px; color:#64748b; }
    .card-km.ok { color:#16a34a; }
    .card-action { flex-shrink:0; }
    .btn-mark-done { display:flex; align-items:center; gap:6px; padding:10px 16px; background:#dcfce7; border:none; border-radius:8px; color:#16a34a; font-size:13px; font-weight:500; cursor:pointer; transition:all .2s; }
    .btn-mark-done:hover { background:#16a34a; color:white; }
    
    /* Empty Planning */
    .empty-planning, .empty-vehicles { text-align:center; padding:60px 20px; background:white; border-radius:12px; }
    .empty-planning .empty-icon, .empty-vehicles .empty-icon { font-size:48px; margin-bottom:16px; }
    .empty-planning h3, .empty-vehicles h3 { margin:0 0 8px; font-size:18px; font-weight:600; color:#1e293b; }
    .empty-planning p, .empty-vehicles p { margin:0 0 20px; font-size:14px; color:#64748b; }
    .btn-switch-view { padding:10px 20px; background:#1e3a5f; color:white; border:none; border-radius:8px; font-size:13px; font-weight:500; cursor:pointer; }
    
    /* Vehicles Grid */
    .vehicles-grid { display:flex; flex-direction:column; gap:12px; }
    .vehicle-tile { background:white; border-radius:12px; overflow:hidden; box-shadow:0 1px 3px rgba(0,0,0,.05); }
    .tile-header { display:flex; align-items:center; gap:16px; padding:16px 20px; cursor:pointer; transition:background .2s; }
    .tile-header:hover { background:#f8fafc; }
    .tile-icon { width:48px; height:48px; background:#f1f5f9; border-radius:10px; display:flex; align-items:center; justify-content:center; color:#64748b; }
    .tile-info { flex:1; }
    .tile-info h4 { margin:0 0 4px; font-size:15px; font-weight:600; color:#1e293b; }
    .tile-meta { display:flex; align-items:center; gap:12px; }
    .tile-plate { font-family:monospace; font-size:12px; background:#e2e8f0; padding:2px 8px; border-radius:4px; color:#475569; }
    .tile-km { font-size:12px; color:#64748b; }
    .tile-badges { display:flex; gap:6px; }
    .tile-badge { min-width:24px; height:24px; padding:0 8px; border-radius:12px; display:flex; align-items:center; justify-content:center; font-size:11px; font-weight:600; color:white; }
    .tile-badge.urgent { background:#dc2626; }
    .tile-badge.warning { background:#f59e0b; }
    .tile-badge.ok { background:#16a34a; }
    .tile-arrow { color:#94a3b8; transition:transform .2s; }
    .tile-arrow.open { transform:rotate(180deg); }
    .tile-content { border-top:1px solid #f1f5f9; padding:12px; background:#f8fafc; }
    .tile-maint-row { display:flex; align-items:center; gap:12px; padding:12px; background:white; border-radius:8px; margin-bottom:8px; }
    .tile-maint-row:last-of-type { margin-bottom:0; }
    .row-icon { width:32px; height:32px; border-radius:8px; display:flex; align-items:center; justify-content:center; font-size:14px; }
    .row-icon.ok { background:#dcfce7; }
    .row-icon.upcoming { background:#dbeafe; }
    .row-icon.due { background:#fef3c7; }
    .row-icon.overdue { background:#fee2e2; }
    .row-info { flex:1; min-width:0; }
    .row-name { display:block; font-size:13px; font-weight:500; color:#1e293b; margin-bottom:2px; }
    .row-date { font-size:11px; color:#94a3b8; }
    .row-status { display:flex; flex-direction:column; align-items:flex-end; gap:4px; min-width:120px; }
    .row-km { font-size:12px; font-weight:500; }
    .row-km.ok { color:#16a34a; }
    .row-km.upcoming { color:#3b82f6; }
    .row-km.due { color:#d97706; }
    .row-km.overdue { color:#dc2626; }
    .row-badge { padding:3px 8px; border-radius:4px; font-size:10px; font-weight:600; }
    .row-badge.ok { background:#dcfce7; color:#16a34a; }
    .row-badge.upcoming { background:#dbeafe; color:#2563eb; }
    .row-badge.due { background:#fef3c7; color:#d97706; }
    .row-badge.overdue { background:#fee2e2; color:#dc2626; }
    .row-actions { display:flex; gap:6px; }
    .btn-done-sm, .btn-remove-sm { width:28px; height:28px; border-radius:6px; display:flex; align-items:center; justify-content:center; font-size:12px; cursor:pointer; transition:all .2s; }
    .btn-done-sm { background:#dcfce7; border:1px solid #bbf7d0; color:#16a34a; }
    .btn-done-sm:hover { background:#16a34a; color:white; border-color:#16a34a; }
    .btn-remove-sm { background:white; border:1px solid #fee2e2; color:#dc2626; }
    .btn-remove-sm:hover { background:#fee2e2; }
    .btn-add-maint-tile { display:flex; align-items:center; justify-content:center; gap:8px; width:100%; padding:12px; margin-top:8px; border:2px dashed #cbd5e1; border-radius:8px; background:transparent; color:#64748b; font-size:13px; font-weight:500; cursor:pointer; transition:all .2s; }
    .btn-add-maint-tile:hover { border-color:#3b82f6; color:#3b82f6; background:#eff6ff; }
    
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
    .invoice-section { background:#f8fafc; border-radius:10px; padding:16px; margin-bottom:16px; }
    .invoice-header { display:flex; justify-content:space-between; align-items:center; margin-bottom:16px; }
    .invoice-header h4 { margin:0; font-size:14px; font-weight:600; color:#1e293b; }
    .invoice-lines { display:flex; flex-direction:column; gap:12px; }
    .invoice-line { background:white; border-radius:10px; padding:14px; border:1px solid #e2e8f0; }
    .line-top { display:flex; gap:8px; align-items:center; margin-bottom:10px; }
    .line-select { flex:1; padding:10px 12px; border:1px solid #e2e8f0; border-radius:8px; font-size:13px; background:white; cursor:pointer; }
    .line-select:focus { outline:none; border-color:#3b82f6; }
    .line-bottom { display:flex; gap:10px; align-items:center; }
    .line-desc { flex:1; padding:10px 12px; border:1px solid #e2e8f0; border-radius:8px; font-size:13px; background:#f8fafc; }
    .line-desc:not([readonly]) { background:white; }
    .line-desc[readonly] { color:#64748b; cursor:default; }
    .line-price-wrap { display:flex; align-items:center; gap:6px; }
    .line-price { width:90px; padding:10px 12px; border:1px solid #e2e8f0; border-radius:8px; font-size:13px; text-align:right; }
    .line-price:focus { outline:none; border-color:#3b82f6; }
    .price-unit { font-size:12px; color:#64748b; font-weight:500; }
    .line-hint { margin-top:8px; padding:8px 10px; background:#dbeafe; border-radius:6px; font-size:11px; color:#1d4ed8; }
    .btn-remove-line { width:32px; height:32px; border:1px solid #fee2e2; border-radius:8px; background:white; color:#dc2626; cursor:pointer; font-size:14px; flex-shrink:0; }
    .btn-remove-line:hover { background:#fee2e2; }
    .btn-add-line { display:flex; align-items:center; justify-content:center; gap:8px; width:100%; padding:12px; border:2px dashed #cbd5e1; border-radius:8px; background:transparent; color:#64748b; font-size:13px; cursor:pointer; margin-top:12px; transition:all .2s; }
    .btn-add-line:hover { border-color:#16a34a; color:#16a34a; background:#f0fdf4; }
    .btn-add-line span { font-size:16px; }
    .invoice-total { display:flex; justify-content:space-between; align-items:center; padding-top:16px; border-top:1px solid #e2e8f0; margin-top:16px; }
    .total-label { font-size:14px; font-weight:600; color:#64748b; }
    .total-value { font-size:20px; font-weight:700; color:#16a34a; }
    .select-header { display:flex; justify-content:space-between; align-items:center; margin-bottom:12px; }
    .select-actions { display:flex; gap:8px; }
    .selected-count { display:flex; align-items:center; gap:8px; margin-bottom:12px; font-size:13px; color:#64748b; }
    .count-badge.purple { background:#f3e8ff; color:#7c3aed; padding:4px 10px; border-radius:12px; font-weight:600; }
    .tpl-checkbox { width:22px; height:22px; border:2px solid #e2e8f0; border-radius:6px; display:flex; align-items:center; justify-content:center; font-size:12px; color:white; transition:all .2s; }
    .tpl-checkbox.checked { background:#8b5cf6; border-color:#8b5cf6; }
    .footer-info { flex:1; font-size:13px; color:#64748b; }
    .notification-section { margin-top:16px; padding-top:16px; border-top:1px solid #e2e8f0; }
    .notification-section h4 { margin:0 0 6px; font-size:14px; font-weight:600; color:#1e293b; }
    .section-hint { margin:0 0 12px; font-size:12px; color:#94a3b8; }
    @media (max-width:768px) { .form-row { grid-template-columns:1fr; } .templates-grid { grid-template-columns:1fr; } .alerts-summary { flex-wrap:wrap; } .alert-card { min-width:calc(50% - 6px); } .invoice-row { flex-wrap:wrap; } .inv-col.type, .inv-col.desc, .inv-col.price { width:100%; } }
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
  loading = false;
  lastPaidPrices: Map<string, number> = new Map();

  constructor(private apiService: ApiService, private ngZone: NgZone, private cdr: ChangeDetectorRef) {}

  ngOnInit() { this.loadTemplates(); this.loadVehicles(); this.loadAllVehicles(); }

  getEmptyForm() { return { name:'', description:'', category:'', priority:'medium', intervalKm:null, intervalMonths:null, estimatedCost:0, isActive:true, notifyKmBefore:null, notifyDaysBefore:null }; }
  getEmptyMark() { 
    return { 
      vehicleId:'', vehicleName:'', vehiclePlate:'', templateId:'', maintenanceName:'', 
      date:new Date().toISOString().split('T')[0], mileage:null, supplier:'', notes:'',
      invoiceLines: [] as InvoiceLine[]
    }; 
  }
  getEmptyAddToVehicle() { return { vehicleId:'', vehicleName:'', vehiclePlate:'', vehicleMileage:0, selectedTemplateIds:[] as string[] }; }

  loadTemplates() {
    this.loading = true;
    this.apiService.getMaintenanceTemplates({ pageSize: 100 }).subscribe({
      next: (result) => {
        this.ngZone.run(() => {
          this.templates = result.items.map(t => ({
            id: t.id?.toString() || '',
            name: t.name,
            description: t.description || '',
            intervalKm: t.intervalKm ?? null,
            intervalMonths: t.intervalMonths ?? null,
            estimatedCost: t.estimatedCost || 0,
            priority: (t.priority as 'low' | 'medium' | 'high' | 'critical') || 'medium',
            category: t.category || 'Autre',
            isActive: t.isActive
          }));
          this.filterTemplates();
          this.loading = false;
          this.cdr.detectChanges();
        });
      },
      error: (err) => {
        console.error('Error loading templates:', err);
        this.loading = false;
      }
    });
  }

  loadVehicles() {
    this.apiService.getVehicleMaintenanceSchedule({ pageSize: 100 }).subscribe({
      next: (result) => {
        this.ngZone.run(() => {
          this.vehicleSchedules = result.items.map(v => ({
            vehicleId: v.vehicleId.toString(),
            vehicleName: v.vehicleName || '',
            vehiclePlate: v.vehiclePlate || '',
            currentMileage: v.currentMileage || 0,
            maintenanceItems: (v.maintenanceItems || []).map(m => ({
              templateId: m.templateId?.toString() || '',
              templateName: m.templateName || '',
              lastDoneDate: m.lastDoneDate ? new Date(m.lastDoneDate) : null,
              lastDoneKm: m.lastDoneKm ?? null,
              nextDueKm: m.nextDueKm ?? null,
              status: (m.status as 'ok' | 'upcoming' | 'due' | 'overdue') || 'ok',
              kmUntilDue: m.kmUntilDue ?? null
            }))
          }));
          this.filterVehicles();
          this.cdr.detectChanges();
        });
      },
      error: (err) => console.error('Error loading vehicle schedules:', err)
    });
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
    if (!this.isFormValid()) return;
    
    const templateData = {
      name: this.form.name,
      description: this.form.description || '',
      category: this.form.category,
      priority: this.form.priority,
      intervalKm: this.form.intervalKm || undefined,
      intervalMonths: this.form.intervalMonths || undefined,
      estimatedCost: this.form.estimatedCost || 0,
      isActive: this.form.isActive
    };

    if (this.editing) {
      // Mise à jour via API
      this.apiService.updateMaintenanceTemplate(parseInt(this.editing.id), templateData).subscribe({
        next: () => {
          this.loadTemplates();
          this.closeForm();
        },
        error: (err) => console.error('Error updating template:', err)
      });
    } else {
      // Création via API
      this.apiService.createMaintenanceTemplate(templateData).subscribe({
        next: () => {
          this.loadTemplates();
          this.closeForm();
        },
        error: (err) => console.error('Error creating template:', err)
      });
    }
  }
  deleteTemplate(t: MaintenanceTemplate) { 
    if (confirm('Supprimer ce modèle?')) { 
      this.apiService.deleteMaintenanceTemplate(parseInt(t.id)).subscribe({
        next: () => {
          this.loadTemplates();
          this.closeDetail();
        },
        error: (err) => console.error('Error deleting template:', err)
      });
    } 
  }

  openMarkDone(v: VehicleMaintenanceStatus, m: MaintenanceItem) { 
    const lastPrice = this.lastPaidPrices.get(m.templateId) || null;
    this.markData = { 
      vehicleId:v.vehicleId, vehicleName:v.vehicleName, vehiclePlate:v.vehiclePlate, 
      templateId:m.templateId, maintenanceName:m.templateName, 
      date:new Date().toISOString().split('T')[0], mileage:v.currentMileage, supplier:'', notes:'',
      invoiceLines: [{ templateId: m.templateId, description: m.templateName, price: lastPrice, isCustom: false }]
    }; 
    this.isMarkOpen = true; 
  }
  closeMarkDone() { this.isMarkOpen = false; this.markData = this.getEmptyMark(); }
  isMarkValid() { return this.markData.date && this.markData.mileage && this.getInvoiceTotal() > 0; }
  
  addInvoiceLine() { this.markData.invoiceLines.push({ templateId: null, description: '', price: null, isCustom: false }); }
  
  onLineTemplateChange(line: InvoiceLine, templateId: string) {
    if (templateId === 'other') {
      line.templateId = null;
      line.isCustom = true;
      line.description = '';
      line.price = null;
    } else {
      const t = this.templates.find(x => x.id === templateId);
      if (t) {
        line.templateId = t.id;
        line.isCustom = false;
        line.description = t.name;
        line.price = this.lastPaidPrices.get(t.id) || null;
      }
    }
  }
  
  getVehicleMaintenanceTemplates(): MaintenanceTemplate[] {
    const v = this.vehicleSchedules.find(x => x.vehicleId === this.markData.vehicleId);
    if (!v) return this.templates.filter(t => t.isActive);
    const assignedIds = v.maintenanceItems.map(m => m.templateId);
    return this.templates.filter(t => t.isActive && assignedIds.includes(t.id));
  }
  
  getAllTemplatesForDropdown(): MaintenanceTemplate[] {
    return this.templates.filter(t => t.isActive);
  }
  
  getOtherTemplatesForDropdown(): MaintenanceTemplate[] {
    const vehicleTemplateIds = this.getVehicleMaintenanceTemplates().map(t => t.id);
    return this.templates.filter(t => t.isActive && !vehicleTemplateIds.includes(t.id));
  }
  removeInvoiceLine(index: number) { if (this.markData.invoiceLines.length > 1) this.markData.invoiceLines.splice(index, 1); }
  getInvoiceTotal(): number { return this.markData.invoiceLines.reduce((sum: number, line: any) => sum + (line.price || 0), 0); }
  confirmMarkDone() {
    const v = this.vehicleSchedules.find(x => x.vehicleId === this.markData.vehicleId);
    if (v) {
      // Process each invoice line
      for (const line of this.markData.invoiceLines) {
        if (line.templateId && line.price) {
          // Save last paid price
          this.lastPaidPrices.set(line.templateId, line.price);
          
          // Update maintenance item if it exists for this vehicle
          const m = v.maintenanceItems.find(x => x.templateId === line.templateId);
          if (m) {
            m.lastDoneDate = new Date(this.markData.date);
            m.lastDoneKm = this.markData.mileage;
            const t = this.templates.find(x => x.id === m.templateId);
            if (t && t.intervalKm) { 
              m.nextDueKm = this.markData.mileage + t.intervalKm; 
              m.kmUntilDue = (m.nextDueKm || 0) - this.markData.mileage; 
              m.status = m.kmUntilDue > 5000 ? 'ok' : m.kmUntilDue > 0 ? 'upcoming' : 'overdue'; 
            }
          }
        }
      }
      v.currentMileage = this.markData.mileage;
    }
    this.filterVehicles();
    this.closeMarkDone();
  }

  // Vehicle-centric methods
  loadAllVehicles() {
    this.apiService.getVehicles().subscribe({
      next: (vehicles) => {
        this.ngZone.run(() => {
          this.allVehicles = vehicles.map((v: any) => ({
            id: v.id?.toString() || '',
            name: v.name || `${v.brand || ''} ${v.model || ''}`.trim(),
            plate: v.plate || '',
            mileage: v.mileage || 0
          }));
          this.cdr.detectChanges();
        });
      },
      error: (err) => console.error('Error loading vehicles:', err)
    });
  }

  openAddToVehicle(v: VehicleMaintenanceStatus) {
    this.addToVehicleData = { vehicleId:v.vehicleId, vehicleName:v.vehicleName, vehiclePlate:v.vehiclePlate, vehicleMileage:v.currentMileage, selectedTemplateIds:[] };
    this.isAddToVehicleOpen = true;
  }

  closeAddToVehicle() { this.isAddToVehicleOpen = false; }

  getAvailableTemplatesForVehicle(): MaintenanceTemplate[] {
    const v = this.vehicleSchedules.find(x => x.vehicleId === this.addToVehicleData.vehicleId);
    const assignedIds = v ? v.maintenanceItems.map(m => m.templateId) : [];
    return this.templates.filter(t => t.isActive && !assignedIds.includes(t.id));
  }

  toggleTemplateSelection(t: MaintenanceTemplate) {
    const idx = this.addToVehicleData.selectedTemplateIds.indexOf(t.id);
    if (idx === -1) this.addToVehicleData.selectedTemplateIds.push(t.id);
    else this.addToVehicleData.selectedTemplateIds.splice(idx, 1);
  }
  
  isTemplateSelected(id: string): boolean { return this.addToVehicleData.selectedTemplateIds.includes(id); }
  
  selectAllTemplates() { this.addToVehicleData.selectedTemplateIds = this.getAvailableTemplatesForVehicle().map(t => t.id); }
  deselectAllTemplates() { this.addToVehicleData.selectedTemplateIds = []; }

  confirmAddToVehicle() {
    if (this.addToVehicleData.selectedTemplateIds.length === 0) return;
    let v = this.vehicleSchedules.find(x => x.vehicleId === this.addToVehicleData.vehicleId);
    if (!v) {
      v = { vehicleId:this.addToVehicleData.vehicleId, vehicleName:this.addToVehicleData.vehicleName, vehiclePlate:this.addToVehicleData.vehiclePlate, currentMileage:this.addToVehicleData.vehicleMileage, maintenanceItems:[] };
      this.vehicleSchedules.push(v);
    }
    for (const templateId of this.addToVehicleData.selectedTemplateIds) {
      const t = this.templates.find(x => x.id === templateId);
      if (!t) continue;
      const nextKm = t.intervalKm ? this.addToVehicleData.vehicleMileage + t.intervalKm : null;
      const kmUntil = nextKm ? nextKm - this.addToVehicleData.vehicleMileage : null;
      v.maintenanceItems.push({ templateId:t.id, templateName:t.name, lastDoneDate:null, lastDoneKm:null, nextDueKm:nextKm, status:'upcoming', kmUntilDue:kmUntil });
    }
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
