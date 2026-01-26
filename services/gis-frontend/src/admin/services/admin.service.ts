import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { BehaviorSubject, Observable, of, throwError } from 'rxjs';
import { catchError, map, tap } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface AdminUser {
  id: string;
  email: string;
  name: string;
  role: 'super_admin' | 'admin' | 'support';
  permissions: string[];
  lastLogin?: Date;
}

export interface Client {
  id: number;
  name: string;
  email: string;
  phone?: string;
  type: string;
  subscriptionId?: number;
  subscriptionName?: string;
  maxVehicles: number;
  currentVehicles: number;
  currentUsers: number;
  status: 'active' | 'suspended' | 'pending' | 'cancelled';
  createdAt: Date;
  lastActivity?: Date;
  settings?: ClientSettings;
}

export interface ClientSettings {
  enabledFeatures: string[];
  disabledPages: string[];
  maxUsers: number;
  maxDevices: number;
  apiAccess: boolean;
  customBranding: boolean;
}

export interface SystemUser {
  id: number;
  name: string;
  email: string;
  phone?: string;
  dateOfBirth?: Date;
  cin?: string;
  companyId: number;
  companyName: string;
  roleId?: number;
  roleName?: string;
  roles: string[];
  permissions: string[];
  assignedVehicleIds: number[];
  status: 'active' | 'inactive' | 'suspended';
  lastLoginAt?: Date;
  createdAt: Date;
  isOnline: boolean;
}

export interface CreateUserRequest {
  name: string;
  email: string;
  password: string;
  phone?: string;
  dateOfBirth?: Date;
  cin: string;
  companyId: number;
  roleId?: number;
  assignedVehicleIds?: number[];
}

export interface AdminVehicle {
  id: number;
  name: string;
  type: string;
  brand?: string;
  model?: string;
  plate?: string;
  year?: number;
  color?: string;
  status: 'available' | 'in_use' | 'maintenance';
  hasGps: boolean;
  mileage: number;
  fuelType?: string;
  companyId: number;
  companyName?: string;
  gpsDeviceId?: number;
  gpsImei?: string;
  gpsMat?: string;
  gpsModel?: string;
  gpsFirmwareVersion?: string;
  createdAt: Date;
  updatedAt: Date;
}

export interface ServiceHealth {
  name: string;
  status: 'healthy' | 'degraded' | 'down';
  responseTime: number;
  lastCheck: Date;
  uptime: number;
  details?: Record<string, any>;
}

export interface DashboardStats {
  totalClients: number;
  activeClients: number;
  totalUsers: number;
  usersOnline: number;
  totalVehicles: number;
  activeDevices: number;
  totalPositionsToday: number;
  alertsToday: number;
  revenueThisMonth: number;
  newClientsThisMonth: number;
}

export interface FeatureUsage {
  feature: string;
  usageCount: number;
  uniqueUsers: number;
  trend: number;
}

export interface ActivityLog {
  id: string;
  userId: number;
  userName: string;
  companyId: number;
  companyName: string;
  action: string;
  details: string;
  ipAddress: string;
  timestamp: Date;
}

export interface Estimate {
  id: string;
  clientId?: number;
  clientName: string;
  clientEmail: string;
  items: EstimateItem[];
  subtotal: number;
  tax: number;
  total: number;
  status: 'draft' | 'sent' | 'accepted' | 'rejected' | 'expired';
  validUntil: Date;
  createdAt: Date;
  createdBy: string;
  notes?: string;
}

export interface EstimateItem {
  description: string;
  quantity: number;
  unitPrice: number;
  total: number;
}

export interface MaintenanceMode {
  enabled: boolean;
  pages: string[];
  message: string;
  scheduledEnd?: Date;
}

export interface Role {
  id: number;
  name: string;
  description?: string;
  roleType: 'system_admin' | 'company_admin' | 'employee' | 'custom';
  permissions?: Record<string, any>;
  societeId?: number;
  isSystem: boolean;
  isDefault: boolean;
  usersCount: number;
  createdAt: Date;
  updatedAt: Date;
}

export interface Societe {
  id: number;
  name: string;
  type: string;
  description?: string;
  address?: string;
  city?: string;
  country: string;
  phone?: string;
  email?: string;
  logoUrl?: string;
  taxId?: string;
  rc?: string;
  if?: string;
  isActive: boolean;
  subscriptionStatus: string;
  billingCycle: string;
  subscriptionStartedAt: Date;
  subscriptionExpiresAt?: Date;
  subscriptionTypeId?: number;
  subscriptionTypeName?: string;
  usersCount: number;
  vehiclesCount: number;
  rolesCount: number;
  createdAt: Date;
  updatedAt: Date;
}

export interface SubscriptionType {
  id: number;
  name: string;
  code: string;
  description?: string;
  targetCompanyType: string;
  monthlyPrice: number;
  quarterlyPrice: number;
  yearlyPrice: number;
  monthlyDurationDays: number;
  quarterlyDurationDays: number;
  yearlyDurationDays: number;
  maxVehicles: number;
  maxUsers: number;
  maxGpsDevices: number;
  maxGeofences: number;
  gpsTracking: boolean;
  gpsInstallation: boolean;
  apiAccess: boolean;
  advancedReports: boolean;
  realTimeAlerts: boolean;
  historyPlayback: boolean;
  fuelAnalysis: boolean;
  drivingBehavior: boolean;
  historyRetentionDays: number;
  sortOrder: number;
  isActive: boolean;
  permissions?: Record<string, any>;
  createdAt: Date;
  updatedAt: Date;
}

@Injectable({
  providedIn: 'root'
})
export class AdminService {
  private apiUrl = environment.apiUrl;
  private adminUserSubject = new BehaviorSubject<AdminUser | null>(null);
  adminUser$ = this.adminUserSubject.asObservable();


  constructor(private http: HttpClient) {
    this.checkAdminSession();
  }

  private checkAdminSession(): void {
    const stored = localStorage.getItem('admin_user');
    if (stored) {
      try {
        this.adminUserSubject.next(JSON.parse(stored));
      } catch {
        localStorage.removeItem('admin_user');
      }
    }
  }

  login(email: string, password: string): Observable<AdminUser> {
    return this.http.post<any>(`${this.apiUrl}/auth/login`, { email, password }).pipe(
      map(response => {
        const user: AdminUser = {
          id: response.user.id.toString(),
          email: response.user.email,
          name: response.user.name,
          role: response.user.roles?.includes('admin') ? 'super_admin' : 'admin',
          permissions: response.user.permissions || ['*'],
          lastLogin: new Date()
        };
        localStorage.setItem('admin_user', JSON.stringify(user));
        localStorage.setItem('admin_token', response.token);
        this.adminUserSubject.next(user);
        return user;
      }),
      catchError(err => {
        console.error('Login error:', err);
        return throwError(() => new Error('Invalid credentials'));
      })
    );
  }

  logout(): void {
    localStorage.removeItem('admin_user');
    localStorage.removeItem('admin_token');
    this.adminUserSubject.next(null);
  }

  isAuthenticated(): boolean {
    return !!localStorage.getItem('admin_token');
  }

  getAdminUser(): AdminUser | null {
    return this.adminUserSubject.value;
  }

  private getHeaders(): HttpHeaders {
    const token = localStorage.getItem('admin_token');
    return new HttpHeaders({
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${token}`
    });
  }

  getDashboardStats(): Observable<DashboardStats> {
    return this.http.get<DashboardStats>(`${this.apiUrl}/admin/dashboard/stats`, { headers: this.getHeaders() }).pipe(
      catchError(err => {
        console.error('Error fetching dashboard stats:', err);
        return of({
          totalClients: 0,
          activeClients: 0,
          totalUsers: 0,
          usersOnline: 0,
          totalVehicles: 0,
          activeDevices: 0,
          totalPositionsToday: 0,
          alertsToday: 0,
          revenueThisMonth: 0,
          newClientsThisMonth: 0
        });
      })
    );
  }

  getFeatureUsage(): Observable<FeatureUsage[]> {
    return this.http.get<FeatureUsage[]>(`${this.apiUrl}/admin/dashboard/feature-usage`, { headers: this.getHeaders() }).pipe(
      catchError(err => {
        console.error('Error fetching feature usage:', err);
        return of([]);
      })
    );
  }

  // ==================== COMPANY/CLIENT MANAGEMENT ====================

  getClients(search?: string, status?: string): Observable<Client[]> {
    let url = `${this.apiUrl}/admin/company`;
    const params: string[] = [];
    if (search) params.push(`search=${encodeURIComponent(search)}`);
    if (status && status !== 'all') params.push(`status=${status}`);
    if (params.length > 0) url += '?' + params.join('&');
    
    return this.http.get<Client[]>(url, { headers: this.getHeaders() }).pipe(
      catchError(err => {
        console.error('Error fetching clients:', err);
        return of([]);
      })
    );
  }

  getClient(id: number): Observable<Client | undefined> {
    return this.http.get<Client>(`${this.apiUrl}/admin/company/${id}`, { headers: this.getHeaders() }).pipe(
      catchError(err => {
        console.error('Error fetching client:', err);
        return of(undefined);
      })
    );
  }

  createClient(client: Partial<Client> & { adminEmail?: string; adminPassword?: string; adminName?: string }): Observable<Client> {
    return this.http.post<Client>(`${this.apiUrl}/admin/company`, client, { headers: this.getHeaders() });
  }

  updateClient(id: number, updates: Partial<Client>): Observable<Client> {
    return this.http.put<Client>(`${this.apiUrl}/admin/company/${id}`, updates, { headers: this.getHeaders() });
  }

  suspendClient(id: number): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/admin/company/${id}/suspend`, {}, { headers: this.getHeaders() });
  }

  activateClient(id: number): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/admin/company/${id}/activate`, {}, { headers: this.getHeaders() });
  }

  deleteClient(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/admin/company/${id}`, { headers: this.getHeaders() });
  }

  getClientUsers(companyId: number): Observable<SystemUser[]> {
    return this.http.get<SystemUser[]>(`${this.apiUrl}/admin/company/${companyId}/users`, { headers: this.getHeaders() }).pipe(
      catchError(err => {
        console.error('Error fetching company users:', err);
        return of([]);
      })
    );
  }

  getClientStats(companyId: number): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/admin/company/${companyId}/stats`, { headers: this.getHeaders() });
  }

  // ==================== USER MANAGEMENT ====================

  getUsers(search?: string, status?: string, companyId?: number): Observable<SystemUser[]> {
    let url = `${this.apiUrl}/admin/users`;
    const params: string[] = [];
    if (search) params.push(`search=${encodeURIComponent(search)}`);
    if (status && status !== 'all') params.push(`status=${status}`);
    if (companyId) params.push(`companyId=${companyId}`);
    if (params.length > 0) url += '?' + params.join('&');
    
    return this.http.get<SystemUser[]>(url, { headers: this.getHeaders() }).pipe(
      catchError(err => {
        console.error('Error fetching users:', err);
        return of([]);
      })
    );
  }

  getUser(id: number): Observable<SystemUser | undefined> {
    return this.http.get<SystemUser>(`${this.apiUrl}/admin/users/${id}`, { headers: this.getHeaders() }).pipe(
      catchError(err => {
        console.error('Error fetching user:', err);
        return of(undefined);
      })
    );
  }

  getUsersByCompany(companyId: number): Observable<SystemUser[]> {
    return this.getUsers(undefined, undefined, companyId);
  }

  createUser(user: CreateUserRequest): Observable<SystemUser> {
    return this.http.post<SystemUser>(`${this.apiUrl}/admin/users`, user, { headers: this.getHeaders() });
  }

  getCompanyUsers(companyId: number): Observable<SystemUser[]> {
    return this.http.get<SystemUser[]>(`${this.apiUrl}/admin/company/${companyId}/users`, { headers: this.getHeaders() }).pipe(
      catchError(err => {
        console.error('Error fetching company users:', err);
        return of([]);
      })
    );
  }

  updateUser(id: number, updates: Partial<SystemUser> & { password?: string }): Observable<SystemUser> {
    return this.http.put<SystemUser>(`${this.apiUrl}/admin/users/${id}`, updates, { headers: this.getHeaders() });
  }

  updateUserPermissions(userId: number, permissions: string[]): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/admin/users/${userId}/permissions`, { permissions }, { headers: this.getHeaders() });
  }

  updateUserRoles(userId: number, roles: string[]): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/admin/users/${userId}/roles`, { roles }, { headers: this.getHeaders() });
  }

  suspendUser(userId: number): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/admin/users/${userId}/suspend`, {}, { headers: this.getHeaders() });
  }

  activateUser(userId: number): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/admin/users/${userId}/activate`, {}, { headers: this.getHeaders() });
  }

  deleteUser(userId: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/admin/users/${userId}`, { headers: this.getHeaders() });
  }

  resetUserPassword(userId: number, newPassword: string): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/admin/users/${userId}/reset-password`, { newPassword }, { headers: this.getHeaders() });
  }

  getUserStats(): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/admin/users/stats`, { headers: this.getHeaders() });
  }

  getAvailablePermissions(): Observable<any[]> {
    return this.http.get<any[]>(`${this.apiUrl}/admin/users/available-permissions`, { headers: this.getHeaders() });
  }

  getAvailableRoles(): Observable<any[]> {
    return this.http.get<any[]>(`${this.apiUrl}/admin/users/available-roles`, { headers: this.getHeaders() });
  }

  // ==================== VEHICLE MANAGEMENT ====================

  getVehicles(search?: string, companyId?: number, status?: string): Observable<AdminVehicle[]> {
    let url = `${this.apiUrl}/admin/vehicles`;
    const params: string[] = [];
    if (search) params.push(`search=${encodeURIComponent(search)}`);
    if (companyId) params.push(`companyId=${companyId}`);
    if (status && status !== 'all') params.push(`status=${status}`);
    if (params.length > 0) url += '?' + params.join('&');
    
    return this.http.get<AdminVehicle[]>(url, { headers: this.getHeaders() }).pipe(
      catchError(err => {
        console.error('Error fetching vehicles:', err);
        return of([]);
      })
    );
  }

  getVehicle(id: number): Observable<AdminVehicle | undefined> {
    return this.http.get<AdminVehicle>(`${this.apiUrl}/admin/vehicles/${id}`, { headers: this.getHeaders() }).pipe(
      catchError(err => {
        console.error('Error fetching vehicle:', err);
        return of(undefined);
      })
    );
  }

  getCompanyVehicles(companyId: number): Observable<AdminVehicle[]> {
    return this.http.get<AdminVehicle[]>(`${this.apiUrl}/admin/company/${companyId}/vehicles`, { headers: this.getHeaders() }).pipe(
      catchError(err => {
        console.error('Error fetching company vehicles:', err);
        return of([]);
      })
    );
  }

  createVehicle(vehicle: Partial<AdminVehicle>): Observable<AdminVehicle> {
    return this.http.post<AdminVehicle>(`${this.apiUrl}/admin/vehicles`, vehicle, { headers: this.getHeaders() });
  }

  updateVehicle(id: number, updates: Partial<AdminVehicle>): Observable<AdminVehicle> {
    return this.http.put<AdminVehicle>(`${this.apiUrl}/admin/vehicles/${id}`, updates, { headers: this.getHeaders() });
  }

  deleteVehicle(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/admin/vehicles/${id}`, { headers: this.getHeaders() });
  }

  getServiceHealth(): Observable<ServiceHealth[]> {
    return this.http.get<ServiceHealth[]>(`${this.apiUrl}/admin/health`, { headers: this.getHeaders() }).pipe(
      catchError(err => {
        console.error('Error fetching service health:', err);
        return of([]);
      })
    );
  }

  getActivityLogs(limit: number = 50): Observable<ActivityLog[]> {
    return this.http.get<ActivityLog[]>(`${this.apiUrl}/admin/activity-logs?limit=${limit}`, { headers: this.getHeaders() }).pipe(
      map(logs => logs.map(log => ({ ...log, timestamp: new Date(log.timestamp) }))),
      catchError(err => {
        console.error('Error fetching activity logs:', err);
        return of([]);
      })
    );
  }

  getMaintenanceMode(): Observable<MaintenanceMode> {
    return this.http.get<MaintenanceMode>(`${this.apiUrl}/admin/maintenance`, { headers: this.getHeaders() }).pipe(
      catchError(err => {
        console.error('Error fetching maintenance mode:', err);
        return of({ enabled: false, pages: [], message: '' });
      })
    );
  }

  setMaintenanceMode(mode: MaintenanceMode): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/admin/maintenance`, mode, { headers: this.getHeaders() });
  }

  getEstimates(): Observable<Estimate[]> {
    return this.http.get<Estimate[]>(`${this.apiUrl}/admin/estimates`, { headers: this.getHeaders() }).pipe(
      map(estimates => estimates.map(e => ({
        ...e,
        validUntil: new Date(e.validUntil),
        createdAt: new Date(e.createdAt)
      }))),
      catchError(err => {
        console.error('Error fetching estimates:', err);
        return of([]);
      })
    );
  }

  createEstimate(estimate: Partial<Estimate>): Observable<Estimate> {
    return this.http.post<Estimate>(`${this.apiUrl}/admin/estimates`, estimate, { headers: this.getHeaders() }).pipe(
      map(e => ({
        ...e,
        validUntil: new Date(e.validUntil),
        createdAt: new Date(e.createdAt)
      }))
    );
  }

  updateEstimateStatus(id: string, status: string): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/admin/estimates/${id}/status`, { status }, { headers: this.getHeaders() });
  }

  getAllPages(): string[] {
    return [
      'dashboard',
      'monitoring',
      'vehicles',
      'employees',
      'gps-devices',
      'maintenance',
      'costs',
      'reports',
      'geofences',
      'notifications',
      'settings',
      'users'
    ];
  }

  getSubscriptions(): Observable<any[]> {
    return this.http.get<any[]>(`${this.apiUrl}/admin/subscriptions`, { headers: this.getHeaders() }).pipe(
      catchError(err => {
        console.error('Error fetching subscriptions:', err);
        return of([]);
      })
    );
  }

  createSubscription(subscription: any): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/admin/subscriptions`, subscription, { headers: this.getHeaders() });
  }

  updateSubscription(id: number, subscription: any): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/admin/subscriptions/${id}`, subscription, { headers: this.getHeaders() });
  }

  deleteSubscription(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/admin/subscriptions/${id}`, { headers: this.getHeaders() });
  }

  // ==================== SUBSCRIPTION TYPES ====================

  getSubscriptionTypes(companyType?: string): Observable<SubscriptionType[]> {
    let url = `${this.apiUrl}/admin/subscription-types`;
    if (companyType && companyType !== 'all') {
      url += `?companyType=${companyType}`;
    }
    return this.http.get<SubscriptionType[]>(url, { headers: this.getHeaders() }).pipe(
      map(types => types.map(t => ({
        ...t,
        createdAt: new Date(t.createdAt),
        updatedAt: new Date(t.updatedAt)
      }))),
      catchError(err => {
        console.error('Error fetching subscription types:', err);
        return of([]);
      })
    );
  }

  getSubscriptionType(id: number): Observable<SubscriptionType | undefined> {
    return this.http.get<SubscriptionType>(`${this.apiUrl}/admin/subscription-types/${id}`, { headers: this.getHeaders() }).pipe(
      map(t => ({
        ...t,
        createdAt: new Date(t.createdAt),
        updatedAt: new Date(t.updatedAt)
      })),
      catchError(err => {
        console.error('Error fetching subscription type:', err);
        return of(undefined);
      })
    );
  }

  createSubscriptionType(subscriptionType: Partial<SubscriptionType>): Observable<SubscriptionType> {
    return this.http.post<SubscriptionType>(`${this.apiUrl}/admin/subscription-types`, subscriptionType, { headers: this.getHeaders() });
  }

  updateSubscriptionType(id: number, updates: Partial<SubscriptionType>): Observable<SubscriptionType> {
    return this.http.put<SubscriptionType>(`${this.apiUrl}/admin/subscription-types/${id}`, updates, { headers: this.getHeaders() });
  }

  deleteSubscriptionType(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/admin/subscription-types/${id}`, { headers: this.getHeaders() });
  }

  // ==================== ROLES MANAGEMENT ====================

  getRoles(includeSystem: boolean = true): Observable<Role[]> {
    return this.http.get<Role[]>(`${this.apiUrl}/roles?includeSystem=${includeSystem}`, { headers: this.getHeaders() }).pipe(
      map(roles => roles.map(r => ({
        ...r,
        createdAt: new Date(r.createdAt),
        updatedAt: new Date(r.updatedAt)
      }))),
      catchError(err => {
        console.error('Error fetching roles:', err);
        return of([]);
      })
    );
  }

  getRole(id: number): Observable<Role | undefined> {
    return this.http.get<Role>(`${this.apiUrl}/roles/${id}`, { headers: this.getHeaders() }).pipe(
      map(r => ({
        ...r,
        createdAt: new Date(r.createdAt),
        updatedAt: new Date(r.updatedAt)
      })),
      catchError(err => {
        console.error('Error fetching role:', err);
        return of(undefined);
      })
    );
  }

  createRole(role: { name: string; description?: string; roleType?: string; permissions?: Record<string, any>; isDefault?: boolean }): Observable<Role> {
    return this.http.post<Role>(`${this.apiUrl}/roles`, role, { headers: this.getHeaders() });
  }

  updateRole(id: number, updates: Partial<Role>): Observable<Role> {
    return this.http.put<Role>(`${this.apiUrl}/roles/${id}`, updates, { headers: this.getHeaders() });
  }

  deleteRole(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/roles/${id}`, { headers: this.getHeaders() });
  }

  getCompanyRoles(companyId: number): Observable<Role[]> {
    return this.http.get<Role[]>(`${this.apiUrl}/admin/company/${companyId}/roles`, { headers: this.getHeaders() }).pipe(
      map(roles => roles.map(r => ({
        ...r,
        createdAt: new Date(r.createdAt),
        updatedAt: new Date(r.updatedAt)
      }))),
      catchError(err => {
        console.error('Error fetching company roles:', err);
        return of([]);
      })
    );
  }

  // ==================== SOCIETES MANAGEMENT (ADMIN) ====================

  getSocietes(search?: string, status?: string, page: number = 1, pageSize: number = 20): Observable<{ items: Societe[]; totalCount: number }> {
    let url = `${this.apiUrl}/admin/societes?page=${page}&pageSize=${pageSize}`;
    if (search) url += `&search=${encodeURIComponent(search)}`;
    if (status && status !== 'all') url += `&status=${status}`;
    
    return this.http.get<{ items: Societe[]; totalCount: number }>(url, { headers: this.getHeaders() }).pipe(
      map(response => ({
        ...response,
        items: response.items.map(s => ({
          ...s,
          createdAt: new Date(s.createdAt),
          updatedAt: new Date(s.updatedAt),
          subscriptionStartedAt: new Date(s.subscriptionStartedAt),
          subscriptionExpiresAt: s.subscriptionExpiresAt ? new Date(s.subscriptionExpiresAt) : undefined
        }))
      })),
      catchError(err => {
        console.error('Error fetching societes:', err);
        return of({ items: [], totalCount: 0 });
      })
    );
  }

  getSociete(id: number): Observable<Societe | undefined> {
    return this.http.get<Societe>(`${this.apiUrl}/admin/societes/${id}`, { headers: this.getHeaders() }).pipe(
      map(s => ({
        ...s,
        createdAt: new Date(s.createdAt),
        updatedAt: new Date(s.updatedAt)
      })),
      catchError(err => {
        console.error('Error fetching societe:', err);
        return of(undefined);
      })
    );
  }

  createSociete(societe: {
    name: string;
    type?: string;
    description?: string;
    address?: string;
    city?: string;
    country?: string;
    phone?: string;
    email?: string;
    subscriptionTypeId?: number;
    adminName: string;
    adminEmail: string;
    adminPassword: string;
  }): Observable<Societe> {
    return this.http.post<Societe>(`${this.apiUrl}/admin/societes`, societe, { headers: this.getHeaders() });
  }

  updateSociete(id: number, updates: Partial<Societe>): Observable<Societe> {
    return this.http.put<Societe>(`${this.apiUrl}/admin/societes/${id}`, updates, { headers: this.getHeaders() });
  }

  deleteSociete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/admin/societes/${id}`, { headers: this.getHeaders() });
  }

  suspendSociete(id: number): Observable<Societe> {
    return this.http.post<Societe>(`${this.apiUrl}/admin/societes/${id}/suspend`, {}, { headers: this.getHeaders() });
  }

  activateSociete(id: number): Observable<Societe> {
    return this.http.post<Societe>(`${this.apiUrl}/admin/societes/${id}/activate`, {}, { headers: this.getHeaders() });
  }

  // ==================== PERMISSIONS ====================

  getPermissionTemplate(): Observable<PermissionTemplate> {
    return this.http.get<PermissionTemplate>(`${this.apiUrl}/admin/permissions/template`, { headers: this.getHeaders() });
  }

  getSubscriptionPermissions(subscriptionId: number): Observable<SubscriptionPermissions> {
    return this.http.get<SubscriptionPermissions>(`${this.apiUrl}/admin/permissions/subscription/${subscriptionId}`, { headers: this.getHeaders() });
  }

}

// Permission interfaces
export interface PermissionCategoryMeta {
  name: string;
  icon: string;
  isBase: boolean;
  requiresFeature: string;
}

export interface PermissionTemplateCategory {
  _meta: PermissionCategoryMeta;
  subPermissions: string[];
}

export interface PermissionTemplate {
  [key: string]: PermissionTemplateCategory;
}

export interface SubscriptionPermissions {
  [key: string]: { [subKey: string]: boolean } | { [key: string]: any };
  features: { [key: string]: boolean };
  limits: { [key: string]: number };
}
