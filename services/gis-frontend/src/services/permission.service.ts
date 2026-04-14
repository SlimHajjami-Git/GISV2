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

    // Company admins always have full report access
    if (user.isCompanyAdmin) return true;

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
      'monthly_fuel': 'reportMonthlyCosts'
    };

    const featureKey = reportMapping[reportKey];
    if (featureKey && features[featureKey] === false) return false;

    // Step 3: Check per-user report permissions
    const up = user.userPermissions;
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
