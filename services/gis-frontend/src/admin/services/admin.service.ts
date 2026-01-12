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
  companyId: number;
  companyName: string;
  roles: string[];
  permissions: string[];
  status: 'active' | 'inactive' | 'suspended';
  lastLogin?: Date;
  createdAt: Date;
  isOnline: boolean;
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
  gpsDeviceId?: string;
  gpsImei?: string;
  gpsPhoneNumber?: string;
  assignedDriverId?: number;
  assignedDriverName?: string;
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

  createUser(user: { name: string; email: string; password: string; phone?: string; companyId: number; roles?: string[]; permissions?: string[] }): Observable<SystemUser> {
    return this.http.post<SystemUser>(`${this.apiUrl}/admin/users`, user, { headers: this.getHeaders() });
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
}
