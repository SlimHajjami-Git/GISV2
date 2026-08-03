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

/** Ligne de /admin/billing/overview — abonnement à surveiller. */
export interface BillingOverviewItem {
  id: number;
  name: string;
  level: 'warning' | 'danger' | 'blocked';
  reason: 'expiring' | 'grace' | 'expired' | 'suspended' | 'cancelled';
  expiresAt: string | null;
  daysRemaining: number | null;
  graceDaysLeft: number | null;
  unpaid: boolean;
  amountDue: number | null;
  lastPaymentAt: string | null;
  subscriptionStatus: string;
  isActive: boolean;
  autoSuspendEnabled: boolean;
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
  // « pending » : compte né d'une inscription libre, en attente de confirmation
  // de son adresse email. La connexion le refuse tant qu'il n'est pas « active ».
  status: 'active' | 'pending' | 'inactive' | 'suspended';
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
  fuelTankCapacity?: number;
  fuelType?: string;
  companyId: number;
  companyName?: string;
  gpsDeviceId?: number;
  gpsImei?: string;
  gpsMat?: string;
  gpsBrand?: string;
  gpsModel?: string;
  gpsFirmwareVersion?: string;
  gpsFuelSensorMode?: string;
  gpsSimNumber?: string;
  gpsSimOperator?: string;
  gpsInstallationDate?: Date;
  assignedDriverId?: number;
  assignedDriverName?: string;
  createdAt: Date;
  updatedAt: Date;
}

export interface ReplaceDeviceRequest {
  newImei: string;
  newSimNumber?: string;
  newMat?: string;
  newSimOperator?: string;
}

export interface ReplaceDeviceResult {
  success: boolean;
  message: string;
  deviceId?: number;
  previousImei?: string;
  releasedDeviceId?: number;
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

export interface EstimateItem {
  id?: number;
  description: string;
  quantity: number;
  unitPrice: number;
  total?: number;
}

export interface Estimate {
  id: number;
  number: string;
  companyId?: number | null;
  companyName?: string | null;
  clientName: string;
  clientEmail?: string | null;
  clientPhone?: string | null;
  clientAddress?: string | null;
  status: 'draft' | 'sent' | 'accepted' | 'rejected';
  issueDate: string;
  validUntil?: string | null;
  discountPercent: number;
  taxPercent: number;
  notes?: string | null;
  items: EstimateItem[];
  subtotal: number;
  discountAmount: number;
  taxAmount: number;
  total: number;
  createdAt: string;
  updatedAt: string;
}

export interface EstimateInput {
  companyId?: number | null;
  clientName: string;
  clientEmail?: string | null;
  clientPhone?: string | null;
  clientAddress?: string | null;
  validUntil?: string | null;
  discountPercent: number;
  taxPercent: number;
  notes?: string | null;
  items: { description: string; quantity: number; unitPrice: number }[];
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
  /** Quota mensuel de scans de factures IA — null = défaut plateforme (20), 0 = désactivé. */
  invoiceScanMonthlyLimit?: number | null;
  /** Scans IA consommés sur le mois calendaire en cours. */
  invoiceScanUsedThisMonth?: number;
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
  // Module permissions
  moduleDashboard?: boolean;
  moduleMonitoring?: boolean;
  moduleVehicles?: boolean;
  moduleEmployees?: boolean;
  moduleGeofences?: boolean;
  moduleMaintenance?: boolean;
  moduleCosts?: boolean;
  moduleReports?: boolean;
  moduleSettings?: boolean;
  moduleUsers?: boolean;
  moduleSuppliers?: boolean;
  moduleDocuments?: boolean;
  moduleAccidents?: boolean;
  moduleFleetManagement?: boolean;
  moduleFuel?: boolean;
  moduleTours?: boolean;
  // Report permissions
  reportTrips?: boolean;
  reportFuel?: boolean;
  reportSpeed?: boolean;
  reportStops?: boolean;
  reportMileage?: boolean;
  reportCosts?: boolean;
  reportMaintenance?: boolean;
  reportDaily?: boolean;
  reportMonthly?: boolean;
  reportMileagePeriod?: boolean;
  reportSpeedInfraction?: boolean;
  reportDrivingBehavior?: boolean;
  reportMonthlyCosts?: boolean;
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
          id: response.user.id?.toString() || '',
          email: response.user.email,
          name: response.user.name,
          role: response.user.roles?.includes('admin') ? 'super_admin' : 'admin',
          permissions: response.user.permissions || ['*'],
          lastLogin: new Date()
        };
        localStorage.setItem('admin_user', JSON.stringify(user));
        localStorage.setItem('admin_token', response.token);
        if (response.refreshToken) {
          localStorage.setItem('admin_refresh_token', response.refreshToken);
        }
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
    // Clear admin-specific storage only (don't touch user session)
    localStorage.removeItem('admin_user');
    localStorage.removeItem('admin_token');
    localStorage.removeItem('admin_refresh_token');
    this.adminUserSubject.next(null);
  }

  isAuthenticated(): boolean {
    return !!localStorage.getItem('admin_token');
  }

  getAdminUser(): AdminUser | null {
    return this.adminUserSubject.value;
  }

  /**
   * Super-admin : génère un jeton "voir en tant que" pour l'utilisateur cible.
   * Authentifié avec le token admin (le backend exige le rôle system_admin).
   */
  impersonate(userId: number): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/auth/impersonate`, { userId }, { headers: this.getHeaders() });
  }

  private getHeaders(): HttpHeaders {
    const token = localStorage.getItem('admin_token');
    return new HttpHeaders({
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${token}`
    });
  }

  // ── Sauvegardes & purge de la base (sys_admin) ──
  getPurgeableTables(): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/admin/database/purgeable-tables`, { headers: this.getHeaders() });
  }
  listBackups(): Observable<any[]> {
    return this.http.get<any[]>(`${this.apiUrl}/admin/database/backups`, { headers: this.getHeaders() });
  }
  getDatabaseStorage(): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/admin/database/storage`, { headers: this.getHeaders() });
  }
  createBackup(): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/admin/database/backups`, {}, { headers: this.getHeaders() });
  }
  deleteBackup(name: string): Observable<any> {
    return this.http.delete<any>(`${this.apiUrl}/admin/database/backups/${encodeURIComponent(name)}`, { headers: this.getHeaders() });
  }
  downloadBackupUrl(name: string): string {
    return `${this.apiUrl}/admin/database/backups/${encodeURIComponent(name)}/download`;
  }
  downloadBackup(name: string): Observable<Blob> {
    return this.http.get(this.downloadBackupUrl(name), { headers: this.getHeaders(), responseType: 'blob' });
  }
  previewPurge(months: number, tables: string[]): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/admin/database/purge/preview`, { months, tables }, { headers: this.getHeaders() });
  }
  runPurge(months: number, tables: string[], confirm: string): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/admin/database/purge`, { months, tables, confirm }, { headers: this.getHeaders() });
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

  /** Sociétés à surveiller (expirent ≤30 j, en grâce = impayées, bloquées, suspendues). */
  getBillingOverview(): Observable<{ count: number; items: BillingOverviewItem[] }> {
    return this.http.get<{ count: number; items: BillingOverviewItem[] }>(
      `${this.apiUrl}/admin/billing/overview`, { headers: this.getHeaders() });
  }

  /** Active/désactive la suspension AUTOMATIQUE à l'expiration pour une société. */
  setAutoSuspend(id: number, enabled: boolean): Observable<{ autoSuspendEnabled: boolean }> {
    return this.http.put<{ autoSuspendEnabled: boolean }>(
      `${this.apiUrl}/admin/company/${id}/auto-suspend`, { enabled }, { headers: this.getHeaders() });
  }

  /** Modifie la date d'échéance de l'abonnement (fiche société). */
  setSubscriptionExpiry(id: number, expiresAt: string): Observable<{ subscriptionExpiresAt: string; subscriptionStatus: string }> {
    return this.http.put<{ subscriptionExpiresAt: string; subscriptionStatus: string }>(
      `${this.apiUrl}/admin/company/${id}/subscription-expiry`, { expiresAt }, { headers: this.getHeaders() });
  }

  /** Marque l'abonnement comme payé : prolonge d'un cycle + enregistre le paiement. */
  markSubscriptionPaid(id: number): Observable<{ newExpirationDate: string; amount: number; billingCycle: string }> {
    return this.http.post<{ newExpirationDate: string; amount: number; billingCycle: string }>(
      `${this.apiUrl}/admin/company/${id}/mark-paid`, {}, { headers: this.getHeaders() });
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

    // Calypso 7 — diagnostic logs : la liste affichait 0 utilisateurs alors
    // que l API renvoyait 17. On veut savoir si c est l API qui renvoie un
    // payload vide ou si c est le frontend qui n affecte pas la liste.
    return this.http.get<SystemUser[]>(url, { headers: this.getHeaders() }).pipe(
      tap(users => console.log('[admin.service] GET /admin/users ->', Array.isArray(users) ? users.length : typeof users, 'item(s)')),
      catchError(err => {
        console.error('[admin.service] Error fetching users:', err?.status, err?.message, err);
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

  /**
   * Calypso 7 — assigne un nouveau RoleId a un utilisateur. Le backend
   * AdminUserController expose PUT /admin/users/:id/role (singular) qui
   * appelle UpdateAdminUserRoleCommand. Avec ce role, l utilisateur recoit
   * automatiquement les permissions definies sur le role (cf. CreateSociete).
   */
  updateUserRoleAssignment(userId: number, roleId: number): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/admin/users/${userId}/role`, { roleId }, { headers: this.getHeaders() });
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

  getVehiclesWithPositions(companyId?: number): Observable<any[]> {
    let url = `${this.apiUrl}/admin/vehicles/with-positions`;
    if (companyId) url += `?companyId=${companyId}`;
    return this.http.get<any[]>(url, { headers: this.getHeaders() }).pipe(
      catchError(err => {
        console.error('Error fetching admin vehicles with positions:', err);
        return of([]);
      })
    );
  }

  getVehicleHistory(vehicleId: number, from?: Date, to?: Date, maxPoints = 5000): Observable<any[]> {
    let url = `${this.apiUrl}/admin/vehicles/${vehicleId}/history`;
    const params: string[] = [];
    if (from) params.push(`from=${from.toISOString()}`);
    if (to) params.push(`to=${to.toISOString()}`);
    params.push(`maxPoints=${maxPoints}`);
    url += '?' + params.join('&');
    return this.http.get<any[]>(url, { headers: this.getHeaders() }).pipe(
      catchError(err => {
        console.error('Error fetching admin vehicle history:', err);
        return of([]);
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

  /**
   * Remplacement du boîtier GPS d'un véhicule (matériel changé sur le terrain).
   * Renomme le boîtier en place — l'historique de positions du véhicule est
   * conservé — et libère la fiche du nouvel IMEI si elle est strictement vide.
   */
  replaceVehicleDevice(vehicleId: number, payload: ReplaceDeviceRequest): Observable<ReplaceDeviceResult> {
    return this.http.post<ReplaceDeviceResult>(
      `${this.apiUrl}/admin/vehicles/${vehicleId}/replace-device`, payload, { headers: this.getHeaders() });
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

  // ── Devis ──
  getEstimates(): Observable<Estimate[]> {
    return this.http.get<Estimate[]>(`${this.apiUrl}/admin/estimates`, { headers: this.getHeaders() }).pipe(
      catchError(err => { console.error('Error fetching estimates:', err); return of([]); })
    );
  }

  getEstimate(id: number): Observable<Estimate> {
    return this.http.get<Estimate>(`${this.apiUrl}/admin/estimates/${id}`, { headers: this.getHeaders() });
  }

  createEstimate(input: EstimateInput): Observable<Estimate> {
    return this.http.post<Estimate>(`${this.apiUrl}/admin/estimates`, input, { headers: this.getHeaders() });
  }

  updateEstimate(id: number, input: EstimateInput): Observable<Estimate> {
    return this.http.put<Estimate>(`${this.apiUrl}/admin/estimates/${id}`, input, { headers: this.getHeaders() });
  }

  updateEstimateStatus(id: number, status: string): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/admin/estimates/${id}/status`, { status }, { headers: this.getHeaders() });
  }

  deleteEstimate(id: number): Observable<any> {
    return this.http.delete<any>(`${this.apiUrl}/admin/estimates/${id}`, { headers: this.getHeaders() });
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

  /** Quota mensuel de scans de factures IA — null = défaut plateforme (20), 0 = désactivé. */
  setScanQuota(id: number, monthlyLimit: number | null): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/admin/societes/${id}/scan-quota`, { monthlyLimit }, { headers: this.getHeaders() });
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

  // ==================== AUTO-RECOVERY ====================

  getAutoRecoveryLog(limit = 100, companyId?: number | null): Observable<any[]> {
    let url = `${this.apiUrl}/admin/auto-recovery?limit=${limit}`;
    if (companyId) url += `&companyId=${companyId}`;
    return this.http.get<any[]>(url, { headers: this.getHeaders() }).pipe(
      catchError(() => of([]))
    );
  }

  getCompanies(): Observable<any[]> {
    return this.getClients();
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
