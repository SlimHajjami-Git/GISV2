import { Component, Input, Output, EventEmitter, OnInit, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

export interface SubscriptionFeatures {
  gpsTracking: boolean;
  gpsInstallation: boolean;
  realTimeAlerts: boolean;
  historyPlayback: boolean;
  advancedReports: boolean;
  fuelAnalysis: boolean;
  drivingBehavior: boolean;
  apiAccess: boolean;
}

interface FeatureCategory {
  key: string;
  name: string;
  description: string;
  icon: string;
  features: FeatureItem[];
}

interface FeatureItem {
  key: keyof SubscriptionFeatures;
  name: string;
  description: string;
}

@Component({
  selector: 'subscription-features-editor',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="features-editor">
      <div class="features-header">
        <h3>Fonctionnalités de l'abonnement</h3>
        <p class="features-subtitle">Sélectionnez les fonctionnalités incluses dans cet abonnement</p>
      </div>

      <div class="features-categories">
        <div class="feature-category" *ngFor="let category of categories">
          <div class="category-header">
            <span class="category-icon" [innerHTML]="category.icon"></span>
            <div class="category-info">
              <span class="category-name">{{ category.name }}</span>
              <span class="category-desc">{{ category.description }}</span>
            </div>
            <label class="toggle-all" (click)="$event.stopPropagation()">
              <input 
                type="checkbox" 
                [checked]="isCategoryFullyEnabled(category)"
                [indeterminate]="isCategoryPartiallyEnabled(category)"
                (change)="toggleCategory(category, $event)"
              >
              <span>Tout</span>
            </label>
          </div>

          <div class="category-features">
            <div class="feature-item" *ngFor="let feature of category.features">
              <label class="feature-toggle">
                <div class="toggle-switch">
                  <input 
                    type="checkbox"
                    [checked]="features[feature.key]"
                    (change)="toggleFeature(feature.key, $event)"
                  >
                  <span class="toggle-slider"></span>
                </div>
                <div class="feature-info">
                  <span class="feature-name">{{ feature.name }}</span>
                  <span class="feature-desc">{{ feature.description }}</span>
                </div>
              </label>
            </div>
          </div>
        </div>
      </div>

      <div class="features-summary">
        <div class="summary-item">
          <span class="summary-count">{{ enabledCount }}</span>
          <span class="summary-label">fonctionnalités activées</span>
        </div>
        <div class="summary-item" [class.premium]="hasPremiumFeatures">
          <span class="summary-icon">{{ hasPremiumFeatures ? '⭐' : '📦' }}</span>
          <span class="summary-label">{{ hasPremiumFeatures ? 'Abonnement Premium' : 'Abonnement Standard' }}</span>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .features-editor {
      background: #fff;
      border-radius: 12px;
      border: 1px solid #e0e0e0;
      overflow: hidden;
    }

    .features-header {
      padding: 20px 24px;
      border-bottom: 1px solid #e0e0e0;
      background: linear-gradient(135deg, #f8fafc 0%, #fff 100%);
    }

    .features-header h3 {
      margin: 0 0 4px 0;
      font-size: 16px;
      font-weight: 600;
      color: #1a1a2e;
    }

    .features-subtitle {
      margin: 0;
      font-size: 13px;
      color: #666;
    }

    .features-categories {
      padding: 16px;
      display: flex;
      flex-direction: column;
      gap: 16px;
    }

    .feature-category {
      background: #f8fafc;
      border-radius: 10px;
      border: 1px solid #e2e8f0;
      overflow: hidden;
    }

    .category-header {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 14px 16px;
      background: #fff;
      border-bottom: 1px solid #e2e8f0;
    }

    .category-icon {
      display: flex;
      align-items: center;
      justify-content: center;
      width: 40px;
      height: 40px;
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
      border-radius: 10px;
      color: #fff;
    }

    .category-icon svg {
      width: 20px;
      height: 20px;
    }

    .category-info {
      flex: 1;
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .category-name {
      font-weight: 600;
      font-size: 14px;
      color: #1a1a2e;
    }

    .category-desc {
      font-size: 12px;
      color: #64748b;
    }

    .toggle-all {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 12px;
      color: #64748b;
      cursor: pointer;
    }

    .toggle-all input {
      cursor: pointer;
      width: 16px;
      height: 16px;
    }

    .category-features {
      padding: 12px 16px;
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(250px, 1fr));
      gap: 12px;
    }

    .feature-item {
      background: #fff;
      border-radius: 8px;
      border: 1px solid #e2e8f0;
      transition: all 0.2s;
    }

    .feature-item:hover {
      border-color: #00d4aa;
      box-shadow: 0 2px 8px rgba(0, 212, 170, 0.1);
    }

    .feature-toggle {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px;
      cursor: pointer;
    }

    .toggle-switch {
      position: relative;
      width: 44px;
      height: 24px;
      flex-shrink: 0;
    }

    .toggle-switch input {
      opacity: 0;
      width: 0;
      height: 0;
    }

    .toggle-slider {
      position: absolute;
      cursor: pointer;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background-color: #e2e8f0;
      border-radius: 24px;
      transition: 0.3s;
    }

    .toggle-slider:before {
      position: absolute;
      content: "";
      height: 18px;
      width: 18px;
      left: 3px;
      bottom: 3px;
      background-color: white;
      border-radius: 50%;
      transition: 0.3s;
      box-shadow: 0 2px 4px rgba(0,0,0,0.1);
    }

    .toggle-switch input:checked + .toggle-slider {
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
    }

    .toggle-switch input:checked + .toggle-slider:before {
      transform: translateX(20px);
    }

    .feature-info {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .feature-name {
      font-weight: 500;
      font-size: 13px;
      color: #1a1a2e;
    }

    .feature-desc {
      font-size: 11px;
      color: #64748b;
    }

    .features-summary {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 16px 24px;
      background: #f8fafc;
      border-top: 1px solid #e0e0e0;
    }

    .summary-item {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .summary-count {
      font-size: 24px;
      font-weight: 700;
      color: #00a388;
    }

    .summary-label {
      font-size: 13px;
      color: #64748b;
    }

    .summary-icon {
      font-size: 20px;
    }

    .summary-item.premium .summary-label {
      color: #00a388;
      font-weight: 500;
    }
  `]
})
export class SubscriptionFeaturesEditorComponent implements OnInit, OnChanges {
  @Input() initialFeatures: SubscriptionFeatures = {
    gpsTracking: false,
    gpsInstallation: false,
    realTimeAlerts: true,
    historyPlayback: true,
    advancedReports: false,
    fuelAnalysis: false,
    drivingBehavior: false,
    apiAccess: false
  };
  
  @Output() featuresChange = new EventEmitter<SubscriptionFeatures>();

  features: SubscriptionFeatures = { ...this.initialFeatures };

  categories: FeatureCategory[] = [
    {
      key: 'gps',
      name: 'GPS & Suivi',
      description: 'Fonctionnalités de localisation et suivi',
      icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="3"/><circle cx="12" cy="12" r="8"/><line x1="12" y1="2" x2="12" y2="4"/><line x1="12" y1="20" x2="12" y2="22"/><line x1="2" y1="12" x2="4" y2="12"/><line x1="20" y1="12" x2="22" y2="12"/></svg>',
      features: [
        { key: 'gpsTracking', name: 'Suivi GPS temps réel', description: 'Position des véhicules en direct' },
        { key: 'gpsInstallation', name: 'Installation GPS incluse', description: 'Service d\'installation gratuit' },
        { key: 'historyPlayback', name: 'Historique des trajets', description: 'Replay des trajets passés' },
        { key: 'realTimeAlerts', name: 'Alertes temps réel', description: 'Notifications instantanées' }
      ]
    },
    {
      key: 'analytics',
      name: 'Analyse & Rapports',
      description: 'Outils d\'analyse avancée',
      icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 21H3V3"/><path d="M21 9l-6 6-4-4-6 6"/></svg>',
      features: [
        { key: 'advancedReports', name: 'Rapports avancés', description: 'Rapports détaillés et personnalisés' },
        { key: 'fuelAnalysis', name: 'Analyse de carburant', description: 'Suivi consommation et coûts' },
        { key: 'drivingBehavior', name: 'Comportement de conduite', description: 'Analyse du style de conduite' }
      ]
    },
    {
      key: 'integration',
      name: 'Intégration',
      description: 'Options d\'intégration externe',
      icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="16 18 22 12 16 6"/><polyline points="8 6 2 12 8 18"/></svg>',
      features: [
        { key: 'apiAccess', name: 'Accès API', description: 'Intégration avec systèmes tiers' }
      ]
    }
  ];

  get enabledCount(): number {
    return Object.values(this.features).filter(v => v).length;
  }

  get hasPremiumFeatures(): boolean {
    return this.features.advancedReports || this.features.fuelAnalysis || 
           this.features.drivingBehavior || this.features.apiAccess;
  }

  ngOnInit() {
    this.features = { ...this.initialFeatures };
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['initialFeatures']) {
      this.features = { ...this.initialFeatures };
    }
  }

  toggleFeature(key: keyof SubscriptionFeatures, event: Event) {
    const checked = (event.target as HTMLInputElement).checked;
    this.features[key] = checked;
    this.emitChange();
  }

  toggleCategory(category: FeatureCategory, event: Event) {
    const checked = (event.target as HTMLInputElement).checked;
    category.features.forEach(feature => {
      this.features[feature.key] = checked;
    });
    this.emitChange();
  }

  isCategoryFullyEnabled(category: FeatureCategory): boolean {
    return category.features.every(f => this.features[f.key]);
  }

  isCategoryPartiallyEnabled(category: FeatureCategory): boolean {
    const enabledCount = category.features.filter(f => this.features[f.key]).length;
    return enabledCount > 0 && enabledCount < category.features.length;
  }

  getFeatures(): SubscriptionFeatures {
    return { ...this.features };
  }

  private emitChange() {
    this.featuresChange.emit({ ...this.features });
  }
}
