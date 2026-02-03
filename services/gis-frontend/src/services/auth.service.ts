import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject, of, tap, map, catchError } from 'rxjs';
import { SubscriptionFeatures } from './permission.service';

export interface AuthUser {
  id: string;
  name: string;
  email: string;
  phone?: string;
  roles: string[];
  permissions: Record<string, any>;  // Changed to object to preserve module permissions
  companyId: string;
  companyName: string;
  isCompanyAdmin: boolean;
  isSystemAdmin: boolean;
  subscriptionFeatures: SubscriptionFeatures | null;
}

export interface AuthResponse {
  token: string;
  refreshToken: string;
  user: {
    id: number;
    firstName: string;
    lastName: string;
    email: string;
    phone?: string;
    roleId: number;
    roleName: string;
    isCompanyAdmin: boolean;
    isSystemAdmin: boolean;
    companyId: number;
    companyName: string;
    permissions: Record<string, any>;
    subscriptionFeatures: SubscriptionFeatures | null;
  };
}

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly API_URL = this.getApiUrl();
  private currentUser$ = new BehaviorSubject<AuthUser | null>(null);

  constructor(private http: HttpClient) {
    this.loadStoredAuth();
  }

  private getApiUrl(): string {
    // Always use relative path - nginx proxies to the backend
    return '/api';
  }

  private loadStoredAuth() {
    const token = localStorage.getItem('auth_token');
    const userData = localStorage.getItem('auth_user');
    if (token && userData) {
      try {
        const parsed = JSON.parse(userData);
        this.currentUser$.next({
          id: parsed.id?.toString() || '',
          name: parsed.name || '',
          email: parsed.email || '',
          phone: parsed.phone,
          roles: parsed.roles || [],
          permissions: parsed.permissions || [],
          companyId: parsed.companyId?.toString() || '',
          companyName: parsed.companyName || '',
          isCompanyAdmin: parsed.isCompanyAdmin ?? false,
          isSystemAdmin: parsed.isSystemAdmin ?? false,
          subscriptionFeatures: parsed.subscriptionFeatures ?? null
        });
      } catch (e) {
        console.error('Error loading stored auth:', e);
      }
    }
  }

  login(email: string, password: string): Observable<AuthUser | null> {
    // Mock user check - admin@test.com with password "admin" (case insensitive)
    if (email === 'admin@test.com' && password.toLowerCase() === 'admin') {
      console.log('AuthService.login - Using mock user');
      const mockUser: AuthUser = {
        id: '1',
        name: 'Admin Test',
        email: 'admin@test.com',
        roles: ['admin'],
        permissions: { all: true },  // Object format for permissions
        companyId: '1',
        companyName: 'Demo Company',
        isCompanyAdmin: true,
        isSystemAdmin: true,
        subscriptionFeatures: {
          gpsTracking: true,
          gpsInstallation: true,
          apiAccess: true,
          advancedReports: true,
          realTimeAlerts: true,
          historyPlayback: true,
          fuelAnalysis: true,
          drivingBehavior: true,
          moduleDashboard: true,
          moduleMonitoring: true,
          moduleVehicles: true,
          moduleEmployees: true,
          moduleGeofences: true,
          moduleMaintenance: true,
          moduleCosts: true,
          moduleReports: true,
          moduleSettings: true,
          moduleUsers: true,
          moduleSuppliers: true,
          moduleDocuments: true,
          moduleAccidents: true,
          moduleFleetManagement: true,
          maxVehicles: 999,
          maxUsers: 999,
          maxGpsDevices: 999,
          maxGeofences: 999,
          historyRetentionDays: 365
        }
      };
      const mockResponse = {
        token: 'mock-jwt-token-for-testing',
        refreshToken: 'mock-refresh-token',
        user: mockUser
      };
      localStorage.setItem('auth_token', mockResponse.token);
      localStorage.setItem('auth_user', JSON.stringify(mockUser));
      this.currentUser$.next(mockUser);
      return of(mockUser);
    }

    const url = `${this.API_URL}/auth/login`;
    console.log('AuthService.login - URL:', url);
    console.log('AuthService.login - Payload:', { email, password: '***' });
    
    return this.http.post<AuthResponse>(url, { email, password }).pipe(
      tap(response => {
        console.log('AuthService.login - Full response:', JSON.stringify(response, null, 2));
        console.log('AuthService.login - subscriptionFeatures from API:', response.user.subscriptionFeatures);
        console.log('AuthService.login - user object keys:', Object.keys(response.user));
      }),
      map(response => {
        const user: AuthUser = {
          id: response.user.id.toString(),
          name: `${response.user.firstName} ${response.user.lastName}`.trim(),
          email: response.user.email,
          phone: response.user.phone,
          roles: [response.user.roleName],
          permissions: response.user.permissions || {},  // Keep original permissions object
          companyId: response.user.companyId.toString(),
          companyName: response.user.companyName,
          isCompanyAdmin: response.user.isCompanyAdmin,
          isSystemAdmin: response.user.isSystemAdmin,
          subscriptionFeatures: response.user.subscriptionFeatures
        };
        console.log('AuthService.login - Mapped subscriptionFeatures:', user.subscriptionFeatures);
        console.log('AuthService.login - User permissions:', user.permissions);
        localStorage.setItem('auth_token', response.token);
        localStorage.setItem('auth_user', JSON.stringify(user));
        this.currentUser$.next(user);
        console.log('AuthService.login - User mapped:', user);
        return user;
      }),
      catchError(err => {
        console.error('AuthService.login - Error:', err);
        return of(null);
      })
    );
  }

  private extractPermissions(permissions: Record<string, any> | null): string[] {
    if (!permissions) return [];
    const result: string[] = [];
    for (const [key, value] of Object.entries(permissions)) {
      if (typeof value === 'boolean' && value) {
        result.push(key);
      } else if (typeof value === 'object' && value !== null) {
        for (const [subKey, subValue] of Object.entries(value)) {
          if (subValue === true) {
            result.push(`${key}.${subKey}`);
          }
        }
      }
    }
    return result;
  }

  register(name: string, email: string, password: string, companyName: string, phone?: string): Observable<AuthUser | null> {
    return this.http.post<AuthResponse>(`${this.API_URL}/auth/register`, { 
      name, email, password, companyName, phone 
    }).pipe(
      tap(response => {
        console.log('AuthService.register - Response received:', response);
      }),
      map(response => {
        const user: AuthUser = {
          id: response.user.id.toString(),
          name: `${response.user.firstName} ${response.user.lastName}`.trim(),
          email: response.user.email,
          phone: response.user.phone,
          roles: [response.user.roleName],
          permissions: response.user.permissions || {},  // Keep original permissions object
          companyId: response.user.companyId.toString(),
          companyName: response.user.companyName,
          isCompanyAdmin: response.user.isCompanyAdmin,
          isSystemAdmin: response.user.isSystemAdmin,
          subscriptionFeatures: response.user.subscriptionFeatures
        };
        localStorage.setItem('auth_token', response.token);
        localStorage.setItem('auth_user', JSON.stringify(user));
        this.currentUser$.next(user);
        return user;
      }),
      catchError(err => {
        console.error('Register failed:', err);
        return of(null);
      })
    );
  }

  logout() {
    // Clear all auth-related storage
    localStorage.removeItem('auth_token');
    localStorage.removeItem('auth_user');
    localStorage.removeItem('admin_token');
    localStorage.removeItem('admin_user');
    this.currentUser$.next(null);
  }

  isAuthenticated(): boolean {
    return !!localStorage.getItem('auth_token');
  }

  getCurrentUser(): Observable<AuthUser | null> {
    return this.currentUser$.asObservable();
  }

  getCurrentUserSync(): AuthUser | null {
    return this.currentUser$.value;
  }

  getToken(): string | null {
    return localStorage.getItem('auth_token');
  }
}
