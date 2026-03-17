import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject, of, tap, map, catchError, from, switchMap } from 'rxjs';
import { Preferences } from '@capacitor/preferences';
import { environment } from '../../../environments/environment';

export interface UserPermissions {
  accessLevel: string;
  canMonitoring: boolean;
  canVehicles: boolean;
  canDrivers: boolean;
  canReports: boolean;
  canGeofences: boolean;
  canMaintenance: boolean;
  canCosts: boolean;
  canDocuments: boolean;
  canAccidents: boolean;
  canUsers: boolean;
  canSettings: boolean;
  canSuppliers: boolean;
  canFleetManagement: boolean;
}

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

export interface AuthUser {
  id: string;
  name: string;
  email: string;
  phone?: string;
  roles: string[];
  permissions: Record<string, any>;
  companyId: string;
  companyName: string;
  companyType?: string;
  isCompanyAdmin: boolean;
  isSystemAdmin: boolean;
  subscriptionFeatures: SubscriptionFeatures | null;
  assignedVehicleIds: number[] | null;
  userPermissions: UserPermissions | null;
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
    companyType?: string;
    permissions: Record<string, any>;
    subscriptionFeatures: SubscriptionFeatures | null;
    assignedVehicleIds: number[] | null;
    userPermissions: UserPermissions | null;
  };
}

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private apiUrl = environment.apiUrl;
  private currentUser$ = new BehaviorSubject<AuthUser | null>(null);
  private token: string | null = null;
  private refreshTokenValue: string | null = null;

  constructor(private http: HttpClient) {
    this.loadStoredAuth();
  }

  get API_URL(): string {
    return this.apiUrl;
  }

  async setServerUrl(url: string) {
    const baseUrl = url.replace(/\/+$/, '');
    this.apiUrl = `${baseUrl}/api`;
    await Preferences.set({ key: 'server_url', value: baseUrl });
  }

  async getServerUrl(): Promise<string> {
    const { value } = await Preferences.get({ key: 'server_url' });
    return value || environment.apiUrl.replace('/api', '');
  }

  getSignalrUrl(): string {
    return this.apiUrl.replace('/api', '/hubs/gps');
  }

  private async loadStoredAuth() {
    try {
      const { value: serverUrl } = await Preferences.get({ key: 'server_url' });
      if (serverUrl) {
        this.apiUrl = `${serverUrl}/api`;
      }

      const { value: token } = await Preferences.get({ key: 'auth_token' });
      const { value: userData } = await Preferences.get({ key: 'auth_user' });
      const { value: refreshToken } = await Preferences.get({ key: 'refresh_token' });

      this.token = token;
      this.refreshTokenValue = refreshToken;

      if (token && userData) {
        const parsed = JSON.parse(userData);
        this.currentUser$.next(parsed);
      }
    } catch (e) {
      console.error('Error loading stored auth:', e);
    }
  }

  login(email: string, password: string): Observable<AuthUser | null> {
    return this.http.post<AuthResponse>(`${this.apiUrl}/auth/login`, { email, password }).pipe(
      switchMap(response => {
        const user: AuthUser = {
          id: response.user.id?.toString() || '',
          name: `${response.user.firstName} ${response.user.lastName}`.trim(),
          email: response.user.email,
          phone: response.user.phone,
          roles: [response.user.roleName],
          permissions: response.user.permissions || {},
          companyId: response.user.companyId.toString(),
          companyName: response.user.companyName,
          companyType: response.user.companyType || '',
          isCompanyAdmin: response.user.isCompanyAdmin,
          isSystemAdmin: response.user.isSystemAdmin,
          subscriptionFeatures: response.user.subscriptionFeatures,
          assignedVehicleIds: response.user.assignedVehicleIds ?? null,
          userPermissions: response.user.userPermissions ?? null
        };

        this.token = response.token;
        this.refreshTokenValue = response.refreshToken;

        return from(this.saveAuth(response.token, response.refreshToken, user)).pipe(
          map(() => {
            this.currentUser$.next(user);
            return user;
          })
        );
      }),
      catchError(err => {
        console.error('Login failed:', err);
        return of(null);
      })
    );
  }

  private async saveAuth(token: string, refreshToken: string, user: AuthUser) {
    await Preferences.set({ key: 'auth_token', value: token });
    await Preferences.set({ key: 'refresh_token', value: refreshToken });
    await Preferences.set({ key: 'auth_user', value: JSON.stringify(user) });
  }

  async logout() {
    this.token = null;
    this.refreshTokenValue = null;
    await Preferences.remove({ key: 'auth_token' });
    await Preferences.remove({ key: 'refresh_token' });
    await Preferences.remove({ key: 'auth_user' });
    this.currentUser$.next(null);
  }

  isAuthenticated(): boolean {
    if (!this.token) return false;
    return !this.isTokenExpired(this.token);
  }

  private isTokenExpired(token: string): boolean {
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      return payload.exp * 1000 < Date.now();
    } catch {
      return true;
    }
  }

  isTokenExpiringSoon(): boolean {
    if (!this.token) return false;
    try {
      const payload = JSON.parse(atob(this.token.split('.')[1]));
      const exp = payload.exp * 1000;
      return (exp - Date.now()) < 5 * 60 * 1000;
    } catch {
      return true;
    }
  }

  refreshAccessToken(): Observable<AuthResponse | null> {
    if (!this.token || !this.refreshTokenValue) return of(null);

    return this.http.post<AuthResponse>(`${this.apiUrl}/auth/refresh`, {
      token: this.token,
      refreshToken: this.refreshTokenValue
    }).pipe(
      switchMap(response => {
        const user: AuthUser = {
          id: response.user.id?.toString() || '',
          name: `${response.user.firstName} ${response.user.lastName}`.trim(),
          email: response.user.email,
          phone: response.user.phone,
          roles: [response.user.roleName],
          permissions: response.user.permissions || {},
          companyId: response.user.companyId.toString(),
          companyName: response.user.companyName,
          isCompanyAdmin: response.user.isCompanyAdmin,
          isSystemAdmin: response.user.isSystemAdmin,
          subscriptionFeatures: response.user.subscriptionFeatures,
          assignedVehicleIds: response.user.assignedVehicleIds ?? null,
          userPermissions: response.user.userPermissions ?? null
        };

        this.token = response.token;
        this.refreshTokenValue = response.refreshToken;

        return from(this.saveAuth(response.token, response.refreshToken, user)).pipe(
          map(() => {
            this.currentUser$.next(user);
            return response;
          })
        );
      }),
      catchError(err => {
        console.error('Token refresh failed:', err);
        this.logout();
        return of(null);
      })
    );
  }

  getCurrentUser(): Observable<AuthUser | null> {
    return this.currentUser$.asObservable();
  }

  getCurrentUserSync(): AuthUser | null {
    return this.currentUser$.value;
  }

  getToken(): string | null {
    return this.token;
  }
}
