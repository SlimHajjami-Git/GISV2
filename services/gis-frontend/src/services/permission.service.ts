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
  moduleReports: boolean;
  moduleSettings: boolean;
  moduleUsers: boolean;
  moduleSuppliers: boolean;
  moduleDocuments: boolean;
  moduleAccidents: boolean;
  moduleFleetManagement: boolean;
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
    'carburant': 'moduleCosts',
    'repairs': 'moduleMaintenance',
    'expenses': 'moduleCosts'
  };

  constructor(private authService: AuthService) {}

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
        // Subscription explicitly disables this module
        return false;
      }
    }
    
    // Step 2: Check role permissions (user-level limit)
    // Company admins bypass role check
    if (user.isCompanyAdmin) {
      return true;
    }
    
    // For regular users, check role permissions
    const permissions = user.permissions;
    if (permissions) {
      // Map module key to permission key (e.g., 'users' -> 'moduleUsers')
      const featureKey = this.moduleMapping[module];
      
      // Check if permission exists with module prefix (moduleUsers, moduleFleetManagement, etc.)
      if (featureKey && permissions[featureKey] === false) {
        return false;
      }
      if (featureKey && permissions[featureKey] === true) {
        return true;
      }
      
      // Also check without prefix for backward compatibility
      const permKey = module === 'fleet_management' ? 'fleetManagement' : module;
      if (permissions[permKey] === false) {
        return false;
      }
      if (permissions[permKey] === true) {
        return true;
      }
    }
    
    // Fallback: if no explicit permission, allow basic modules
    return module === 'dashboard' || module === 'vehicles';
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
}
