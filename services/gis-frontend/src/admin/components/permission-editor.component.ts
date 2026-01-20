import { Component, Input, Output, EventEmitter, OnInit, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminService, PermissionTemplate, SubscriptionPermissions } from '../services/admin.service';

interface PermissionState {
  [category: string]: {
    [subPermission: string]: boolean;
  };
}

interface CategoryInfo {
  key: string;
  name: string;
  icon: string;
  isBase: boolean;
  requiresFeature: string;
  subPermissions: string[];
  isAvailable: boolean;
  isExpanded: boolean;
}

@Component({
  selector: 'permission-editor',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="permission-editor">
      <div class="permission-header">
        <h3>Permissions du rôle</h3>
        <p class="permission-subtitle" *ngIf="subscriptionName">
          Limité par l'abonnement: <strong>{{ subscriptionName }}</strong>
        </p>
      </div>

      <div class="permission-categories" *ngIf="categories.length > 0">
        <div 
          *ngFor="let category of categories" 
          class="permission-category"
          [class.disabled]="!category.isAvailable"
          [class.expanded]="category.isExpanded"
        >
          <div class="category-header" (click)="toggleCategory(category)">
            <div class="category-info">
              <span class="category-icon" [innerHTML]="getCategoryIcon(category.icon)"></span>
              <span class="category-name">{{ category.name }}</span>
              <span class="category-badge base" *ngIf="category.isBase">Base</span>
              <span class="category-badge feature" *ngIf="!category.isBase && category.isAvailable">
                {{ getFeatureLabel(category.requiresFeature) }}
              </span>
              <span class="category-badge locked" *ngIf="!category.isAvailable">
                🔒 Non disponible
              </span>
            </div>
            <div class="category-controls">
              <label class="toggle-all" (click)="$event.stopPropagation()" *ngIf="category.isAvailable">
                <input 
                  type="checkbox" 
                  [checked]="isCategoryFullyEnabled(category.key)"
                  [indeterminate]="isCategoryPartiallyEnabled(category.key)"
                  (change)="toggleAllInCategory(category.key, $event)"
                >
                <span class="toggle-label">Tout</span>
              </label>
              <svg class="expand-icon" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline *ngIf="!category.isExpanded" points="6 9 12 15 18 9"/>
                <polyline *ngIf="category.isExpanded" points="18 15 12 9 6 15"/>
              </svg>
            </div>
          </div>

          <div class="category-permissions" *ngIf="category.isExpanded && category.isAvailable">
            <div 
              *ngFor="let subPerm of category.subPermissions" 
              class="permission-item"
              [class.disabled]="!isSubPermissionAvailable(category.key, subPerm)"
            >
              <label class="permission-checkbox">
                <input 
                  type="checkbox"
                  [checked]="permissions[category.key]?.[subPerm] || false"
                  [disabled]="!isSubPermissionAvailable(category.key, subPerm)"
                  (change)="togglePermission(category.key, subPerm, $event)"
                >
                <span class="permission-name">{{ getSubPermissionLabel(subPerm) }}</span>
                <span class="permission-lock" *ngIf="!isSubPermissionAvailable(category.key, subPerm)">
                  🔒
                </span>
              </label>
            </div>
          </div>
        </div>
      </div>

      <div class="permission-loading" *ngIf="loading">
        <div class="spinner"></div>
        <span>Chargement des permissions...</span>
      </div>

      <div class="permission-error" *ngIf="error">
        <span class="error-icon">⚠️</span>
        <span>{{ error }}</span>
      </div>
    </div>
  `,
  styles: [`
    .permission-editor {
      background: #fff;
      border-radius: 8px;
      border: 1px solid #e0e0e0;
    }

    .permission-header {
      padding: 16px 20px;
      border-bottom: 1px solid #e0e0e0;
    }

    .permission-header h3 {
      margin: 0 0 4px 0;
      font-size: 16px;
      font-weight: 600;
      color: #1a1a2e;
    }

    .permission-subtitle {
      margin: 0;
      font-size: 13px;
      color: #666;
    }

    .permission-categories {
      max-height: 500px;
      overflow-y: auto;
    }

    .permission-category {
      border-bottom: 1px solid #f0f0f0;
    }

    .permission-category:last-child {
      border-bottom: none;
    }

    .permission-category.disabled {
      opacity: 0.6;
    }

    .category-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 12px 20px;
      cursor: pointer;
      transition: background 0.2s;
    }

    .category-header:hover {
      background: #f8f9fa;
    }

    .category-info {
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .category-icon {
      display: flex;
      align-items: center;
      justify-content: center;
      width: 32px;
      height: 32px;
      background: #e8f4fc;
      border-radius: 8px;
      color: #4a6cf7;
    }

    .category-icon svg {
      width: 18px;
      height: 18px;
    }

    .category-name {
      font-weight: 500;
      color: #1a1a2e;
    }

    .category-badge {
      padding: 2px 8px;
      border-radius: 4px;
      font-size: 11px;
      font-weight: 500;
    }

    .category-badge.base {
      background: #e8f5e9;
      color: #2e7d32;
    }

    .category-badge.feature {
      background: #e3f2fd;
      color: #1565c0;
    }

    .category-badge.locked {
      background: #ffebee;
      color: #c62828;
    }

    .category-controls {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .toggle-all {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 12px;
      color: #666;
      cursor: pointer;
    }

    .toggle-all input {
      cursor: pointer;
    }

    .expand-icon {
      color: #999;
      font-size: 20px;
    }

    .category-permissions {
      padding: 0 20px 12px 52px;
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
      gap: 8px;
    }

    .permission-item {
      padding: 6px 0;
    }

    .permission-item.disabled {
      opacity: 0.5;
    }

    .permission-checkbox {
      display: flex;
      align-items: center;
      gap: 8px;
      cursor: pointer;
      font-size: 13px;
      color: #333;
    }

    .permission-checkbox input {
      cursor: pointer;
    }

    .permission-checkbox input:disabled {
      cursor: not-allowed;
    }

    .permission-lock {
      font-size: 12px;
    }

    .permission-loading,
    .permission-error {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 10px;
      padding: 40px;
      color: #666;
    }

    .permission-error {
      color: #c62828;
    }

    .spinner {
      width: 24px;
      height: 24px;
      border: 3px solid #f0f0f0;
      border-top-color: #4a6cf7;
      border-radius: 50%;
      animation: spin 1s linear infinite;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }

  `]
})
export class PermissionEditorComponent implements OnInit, OnChanges {
  @Input() subscriptionId: number | null = null;
  @Input() subscriptionName: string = '';
  @Input() initialPermissions: PermissionState = {};
  @Output() permissionsChange = new EventEmitter<PermissionState>();

  categories: CategoryInfo[] = [];
  permissions: PermissionState = {};
  subscriptionPermissions: SubscriptionPermissions | null = null;
  loading = false;
  error = '';

  private subPermissionLabels: { [key: string]: string } = {
    'view': 'Voir',
    'create': 'Créer',
    'edit': 'Modifier',
    'delete': 'Supprimer',
    'export': 'Exporter',
    'real_time': 'Temps réel',
    'history': 'Historique',
    'alerts': 'Alertes',
    'gps_devices': 'Appareils GPS',
    'drivers': 'Chauffeurs',
    'staff': 'Personnel',
    'schedule': 'Planification',
    'fuel': 'Carburant',
    'maintenance': 'Maintenance',
    'other': 'Autres',
    'trip': 'Trajet',
    'speed': 'Vitesse',
    'stops': 'Arrêts',
    'distance': 'Distance',
    'cost': 'Coût',
    'behavior': 'Comportement',
    'enabled': 'Activé',
    'read': 'Lecture',
    'write': 'Écriture'
  };

  private svgIcons: { [key: string]: string } = {
    'dashboard': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/><rect x="14" y="14" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/></svg>',
    'gps_fixed': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="3"/><circle cx="12" cy="12" r="8"/><line x1="12" y1="2" x2="12" y2="4"/><line x1="12" y1="20" x2="12" y2="22"/><line x1="2" y1="12" x2="4" y2="12"/><line x1="20" y1="12" x2="22" y2="12"/></svg>',
    'directions_car': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="1" y="3" width="15" height="13" rx="2"/><path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-5"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/></svg>',
    'fence': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polygon points="12 2 22 8.5 22 15.5 12 22 2 15.5 2 8.5 12 2"/><line x1="12" y1="22" x2="12" y2="15.5"/><polyline points="22 8.5 12 15.5 2 8.5"/></svg>',
    'people': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>',
    'build': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/></svg>',
    'attach_money': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="12" y1="1" x2="12" y2="23"/><path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/></svg>',
    'assessment': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="20" x2="18" y2="10"/><line x1="12" y1="20" x2="12" y2="4"/><line x1="6" y1="20" x2="6" y2="14"/><rect x="2" y="2" width="20" height="20" rx="2"/></svg>',
    'analytics': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 21H3V3"/><path d="M21 9l-6 6-4-4-6 6"/></svg>',
    'settings': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"/></svg>',
    'manage_accounts': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>',
    'api': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="16 18 22 12 16 6"/><polyline points="8 6 2 12 8 18"/></svg>'
  };

  private featureLabels: { [key: string]: string } = {
    'gps_tracking': 'GPS',
    'advanced_reports': 'Premium',
    'api_access': 'API',
    'fuel_analysis': 'Carburant',
    'driving_behavior': 'Conduite'
  };

  constructor(private adminService: AdminService) {}

  getCategoryIcon(iconKey: string): string {
    return this.svgIcons[iconKey] || this.svgIcons['dashboard'];
  }

  getFeatureLabel(feature: string): string {
    return this.featureLabels[feature] || feature;
  }

  ngOnInit() {
    this.loadPermissionTemplate();
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['subscriptionId'] && !changes['subscriptionId'].firstChange) {
      this.loadSubscriptionPermissions();
    }
    if (changes['initialPermissions']) {
      this.permissions = { ...this.initialPermissions };
    }
  }

  private loadPermissionTemplate() {
    this.loading = true;
    this.error = '';

    this.adminService.getPermissionTemplate().subscribe({
      next: (template) => {
        this.categories = Object.entries(template).map(([key, value]) => ({
          key,
          name: value._meta.name,
          icon: value._meta.icon,
          isBase: value._meta.isBase,
          requiresFeature: value._meta.requiresFeature,
          subPermissions: value.subPermissions,
          isAvailable: value._meta.isBase,
          isExpanded: false
        }));

        // Initialize permissions state
        this.categories.forEach(cat => {
          if (!this.permissions[cat.key]) {
            this.permissions[cat.key] = {};
          }
        });

        if (this.subscriptionId) {
          this.loadSubscriptionPermissions();
        } else {
          this.loading = false;
        }
      },
      error: (err) => {
        this.error = 'Erreur lors du chargement des permissions';
        this.loading = false;
        console.error('Error loading permission template:', err);
      }
    });
  }

  private loadSubscriptionPermissions() {
    if (!this.subscriptionId) {
      this.categories.forEach(cat => {
        cat.isAvailable = cat.isBase;
      });
      return;
    }

    this.adminService.getSubscriptionPermissions(this.subscriptionId).subscribe({
      next: (subPerms) => {
        this.subscriptionPermissions = subPerms;

        // Update category availability based on subscription
        this.categories.forEach(cat => {
          if (cat.isBase) {
            cat.isAvailable = true;
          } else if (subPerms.features && cat.requiresFeature) {
            cat.isAvailable = subPerms.features[cat.requiresFeature] || false;
          }
        });

        this.loading = false;
      },
      error: (err) => {
        console.error('Error loading subscription permissions:', err);
        this.loading = false;
      }
    });
  }

  toggleCategory(category: CategoryInfo) {
    category.isExpanded = !category.isExpanded;
  }

  togglePermission(categoryKey: string, subPerm: string, event: Event) {
    const checked = (event.target as HTMLInputElement).checked;
    
    if (!this.permissions[categoryKey]) {
      this.permissions[categoryKey] = {};
    }
    
    this.permissions[categoryKey][subPerm] = checked;
    this.emitChange();
  }

  toggleAllInCategory(categoryKey: string, event: Event) {
    const checked = (event.target as HTMLInputElement).checked;
    const category = this.categories.find(c => c.key === categoryKey);
    
    if (!category) return;

    if (!this.permissions[categoryKey]) {
      this.permissions[categoryKey] = {};
    }

    category.subPermissions.forEach(subPerm => {
      if (this.isSubPermissionAvailable(categoryKey, subPerm)) {
        this.permissions[categoryKey][subPerm] = checked;
      }
    });

    this.emitChange();
  }

  isCategoryFullyEnabled(categoryKey: string): boolean {
    const category = this.categories.find(c => c.key === categoryKey);
    if (!category || !this.permissions[categoryKey]) return false;

    const availablePerms = category.subPermissions.filter(sp => 
      this.isSubPermissionAvailable(categoryKey, sp)
    );

    return availablePerms.every(sp => this.permissions[categoryKey][sp] === true);
  }

  isCategoryPartiallyEnabled(categoryKey: string): boolean {
    const category = this.categories.find(c => c.key === categoryKey);
    if (!category || !this.permissions[categoryKey]) return false;

    const availablePerms = category.subPermissions.filter(sp => 
      this.isSubPermissionAvailable(categoryKey, sp)
    );

    const enabledCount = availablePerms.filter(sp => 
      this.permissions[categoryKey][sp] === true
    ).length;

    return enabledCount > 0 && enabledCount < availablePerms.length;
  }

  isSubPermissionAvailable(categoryKey: string, subPerm: string): boolean {
    if (!this.subscriptionPermissions) return true;

    const catPerms = this.subscriptionPermissions[categoryKey];
    if (!catPerms || typeof catPerms !== 'object') return true;

    const subPermValue = (catPerms as { [key: string]: boolean })[subPerm];
    return subPermValue !== false;
  }

  getSubPermissionLabel(subPerm: string): string {
    return this.subPermissionLabels[subPerm] || subPerm;
  }

  getPermissions(): PermissionState {
    return this.permissions;
  }

  private emitChange() {
    this.permissionsChange.emit({ ...this.permissions });
  }
}
