import { Injectable } from '@angular/core';
import { AuthService, AuthUser } from './auth.service';

export interface SubscriptionFeatures {
  gpsTracking: boolean;
  gpsInstallation: boolean;
  apiAccess: boolean;
  advancedReports: boolean;
  realTimeAlerts: boolean;
  historyPlayback: boolean;
  fuelAnalysis: boolean;
  drivingBehavior: boolean;
  moduleDashboard: boolean;
  moduleMonitoring: boolean;
  moduleVehicles: boolean;
  moduleEmployees: boolean;
  moduleGeofences: boolean;
  moduleMaintenance: boolean;
  moduleCosts: boolean;
  moduleFuel: boolean;
  moduleReports: boolean;
  moduleSettings: boolean;
  moduleUsers: boolean;
  moduleSuppliers: boolean;
  moduleDocuments: boolean;
  moduleAccidents: boolean;
  moduleFleetManagement: boolean;
  moduleTours: boolean;
  reportTrips: boolean;
  reportFuel: boolean;
  reportSpeed: boolean;
  reportStops: boolean;
  reportMileage: boolean;
  reportCosts: boolean;
  reportMaintenance: boolean;
  reportDaily: boolean;
  reportMonthly: boolean;
  reportMileagePeriod: boolean;
  reportSpeedInfraction: boolean;
  reportDrivingBehavior: boolean;
  reportMonthlyCosts: boolean;
  maxVehicles: number;
  maxUsers: number;
  maxGpsDevices: number;
  maxGeofences: number;
  historyRetentionDays: number;
}

export type ModuleKey = 
  | 'dashboard'
  | 'monitoring'
  | 'vehicles'
  | 'employees'
  | 'geofences'
  | 'maintenance'
  | 'costs'
  | 'reports'
  | 'settings'
  | 'users'
  | 'suppliers'
  | 'documents'
  | 'accidents'
  | 'fleet_management'
  | 'tours'
  | 'playback'
  | 'carburant'
  | 'repairs'
  | 'expenses';

@Injectable({
  providedIn: 'root'
})
export class PermissionService {
  private moduleMapping: Record<ModuleKey, keyof SubscriptionFeatures> = {
    'dashboard': 'moduleDashboard',
    'monitoring': 'moduleMonitoring',
    'vehicles': 'moduleVehicles',
    'employees': 'moduleEmployees',
    'geofences': 'moduleGeofences',
    'maintenance': 'moduleMaintenance',
    'costs': 'moduleCosts',
    'reports': 'moduleReports',
    'settings': 'moduleSettings',
    'users': 'moduleUsers',
    'suppliers': 'moduleSuppliers',
    'documents': 'moduleDocuments',
    'accidents': 'moduleAccidents',
    'fleet_management': 'moduleFleetManagement',
    'tours': 'moduleTours',
    'playback': 'moduleMonitoring',
    'carburant': 'moduleFuel',
    'repairs': 'moduleMaintenance',
    'expenses': 'moduleCosts'
  };

  constructor(private authService: AuthService) {}

  private userPermissionMapping: Record<string, string> = {
    'dashboard': 'always',
    'monitoring': 'canMonitoring',
    'vehicles': 'canVehicles',
    'employees': 'canDrivers',
    'geofences': 'canGeofences',
    'maintenance': 'canMaintenance',
    'costs': 'canCosts',
    'reports': 'canReports',
    'settings': 'canSettings',
    'users': 'canUsers',
    'suppliers': 'canSuppliers',
    'documents': 'canDocuments',
    'accidents': 'canAccidents',
    'fleet_management': 'canFleetManagement',
    'tours': 'canTours',
    'playback': 'canPlayback',
    'carburant': 'canFuel',
    'repairs': 'canMaintenance',
    'expenses': 'canCosts'
  };

  hasModuleAccess(module: ModuleKey): boolean {
    const user = this.authService.getCurrentUserSync();
    
    if (!user) return false;
    
    // System admins have access to everything
    if (user.isSystemAdmin) return true;
    
    // Step 1: Check subscription features (company-level limit)
    const features = user.subscriptionFeatures;
    if (features) {
      const featureKey = this.moduleMapping[module];
      if (featureKey && features[featureKey] === false) {
        return false;
      }
    }
    
    // Step 2: Company admins have full access
    if (user.isCompanyAdmin) {
      return true;
    }
    
    // Step 3: Check per-user module permissions
    const up = user.userPermissions;
    if (up) {
      const permKey = this.userPermissionMapping[module];
      if (permKey === 'always') return true; // dashboard always accessible
      if (permKey && (up as any)[permKey] === true) return true;
      if (permKey && (up as any)[permKey] === false) return false;
    }

    // Step 4: Admin access level = all modules
    if (up?.accessLevel === 'admin') {
      return true;
    }
    
    // Fallback: dashboard always accessible
    return module === 'dashboard';
  }

  hasFeature(feature: keyof SubscriptionFeatures): boolean {
    const user = this.authService.getCurrentUserSync();
    
    if (!user) return false;
    if (user.isSystemAdmin) return true;
    
    const features = user.subscriptionFeatures;
    if (!features) return false;
    
    return !!features[feature];
  }

  isSystemAdmin(): boolean {
    const user = this.authService.getCurrentUserSync();
    return user?.isSystemAdmin ?? false;
  }

  /**
   * L'écran Abonnement doit-il être proposé à cet utilisateur ?
   *
   * Seulement aux sociétés qui gèrent leur abonnement elles-mêmes : celles en
   * période d'essai, et l'offre de gestion de parc en libre-service. Les clients
   * installés ont un abonnement négocié et facturé à la main ; leur montrer une
   * grille tarifaire et un bouton de paiement inactif ne ferait que les
   * interroger. Le super-administrateur y garde accès pour le support.
   *
   * Le calcul est fait par le serveur (LoginCommandHandler) et transporté dans le
   * jeton d'ouverture de session : la règle reste unique.
   */
  canManageOwnSubscription(): boolean {
    const user = this.authService.getCurrentUserSync();
    if (!user) return false;
    return !!user.isSystemAdmin || !!user.selfServiceSubscription;
  }

  isCompanyAdmin(): boolean {
    const user = this.authService.getCurrentUserSync();
    return user?.isCompanyAdmin ?? false;
  }

  isAnyAdmin(): boolean {
    return this.isSystemAdmin() || this.isCompanyAdmin();
  }

  getSubscriptionFeatures(): SubscriptionFeatures | null {
    const user = this.authService.getCurrentUserSync();
    return user?.subscriptionFeatures ?? null;
  }

  getLimit(limit: 'maxVehicles' | 'maxUsers' | 'maxGpsDevices' | 'maxGeofences' | 'historyRetentionDays'): number {
    const features = this.getSubscriptionFeatures();
    if (!features) return 0;
    return features[limit] ?? 0;
  }

  hasReportAccess(reportKey: string): boolean {
    const user = this.authService.getCurrentUserSync();
    if (!user) return false;
    if (user.isSystemAdmin) return true;

    // ⚠ Un administrateur de société ne contourne PAS la limite d'abonnement :
    // c'est ce que payait sa société. Il contourne seulement les permissions
    // par utilisateur (étape 3 plus bas), puisqu'il les attribue lui-même.
    // Avant, le court-circuit sautait les DEUX étapes : le client du plan
    // « Gestion de parc sans GPS » voyait donc les onglets Trajets, Vitesse et
    // Arrêts, désespérément vides faute de boîtier.
    const isCompanyAdmin = !!user.isCompanyAdmin;

    // Step 1: Check subscription-level report access (company limit)
    const features = user.subscriptionFeatures;
    // If no subscription features configured, allow all reports (don't block)
    if (!features) return true;

    const reportMapping: Record<string, keyof SubscriptionFeatures> = {
      'trips': 'reportTrips',
      'fuel': 'reportFuel',
      'speed': 'reportSpeed',
      'stops': 'reportStops',
      'mileage': 'reportMileage',
      'costs': 'reportCosts',
      'maintenance': 'reportMaintenance',
      'daily': 'reportDaily',
      'monthly': 'reportMonthly',
      'mileage_period': 'reportMileagePeriod',
      'speed_infraction': 'reportSpeedInfraction',
      'driving_behavior': 'reportDrivingBehavior',
      'monthly_costs': 'reportMonthlyCosts',
      'monthly_fuel': 'reportMonthlyCosts',
      // Ces trois clés manquaient. Le rapport n'étant alors rattaché à aucun
      // drapeau, il passait quel que soit l'abonnement : le client « sans GPS »
      // se voyait proposer « Estimation coûts carburant » (calculée sur les
      // positions) et « Carburant réel vs GPS » (verdict anti-fraude), tous deux
      // bâtis sur une distance nulle. Il pouvait refacturer sur des chiffres
      // fabriqués. Les deux dépendent du GPS : ReportFuel les gouverne.
      'fuel_estimation': 'reportFuel',
      'fuel_comparison': 'reportFuel',
      'ai_fleet': 'advancedReports'
    };

    const featureKey = reportMapping[reportKey];
    if (!featureKey) {
      // Défaut FERMÉ et non ouvert : un rapport ajouté sans être rattaché à un
      // drapeau reste invisible tant qu'on ne l'a pas rattaché, au lieu d'être
      // exposé à tous les abonnements par simple oubli — c'est exactement ainsi
      // que les trois clés ci-dessus ont fui pendant des mois.
      console.warn(`[Permissions] Rapport "${reportKey}" non rattaché à un drapeau d'abonnement : masqué par défaut.`);
      return false;
    }
    if (features[featureKey] === false) return false;

    // Step 3: Check per-user report permissions
    // L'administrateur de société attribue lui-même les permissions de ses
    // utilisateurs : il n'est donc pas bridé par elles (mais il l'est bien par
    // l'abonnement, vérifié à l'étape 1 ci-dessus).
    const up = isCompanyAdmin ? null : user.userPermissions;
    if (up) {
      const userReportMapping: Record<string, string> = {
        'trips': 'canReportTrips',
        'fuel': 'canReportFuel',
        'speed': 'canReportSpeed',
        'stops': 'canReportStops',
        'mileage': 'canReportMileage',
        'costs': 'canReportCosts',
        'maintenance': 'canReportMaintenance',
        'daily': 'canReportDaily',
        'monthly': 'canReportMonthly',
        'mileage_period': 'canReportMileagePeriod',
        'speed_infraction': 'canReportSpeedInfraction',
        'driving_behavior': 'canReportDrivingBehavior',
        'monthly_costs': 'canReportMonthlyCosts',
        'monthly_fuel': 'canReportMonthlyCosts'
      };

      const userPermKey = userReportMapping[reportKey];
      if (userPermKey && (up as any)[userPermKey] === false) return false;
    }

    return true;
  }
}
