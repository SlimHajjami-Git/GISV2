import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject, of, tap, map, catchError } from 'rxjs';
import { UserPreferencesService } from './user-preferences.service';
import { SubscriptionFeatures } from './permission.service';

export interface UserPermissions {
  accessLevel: string;
  canMonitoring: boolean;
  canVehicles: boolean;
  canDrivers: boolean;
  canReports: boolean;
  canGeofences: boolean;
  canMaintenance: boolean;
  canCosts: boolean;
  canFuel: boolean;
  canDocuments: boolean;
  canAccidents: boolean;
  canUsers: boolean;
  canSettings: boolean;
  canSuppliers: boolean;
  canFleetManagement: boolean;
  canTours: boolean;
  canPlayback: boolean;
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
  /** Account currency (server-side: Societe.Settings.Currency, else deployment default). */
  currency?: string;
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
    currency?: string;
  };
}

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly API_URL = this.getApiUrl();
  private currentUser$ = new BehaviorSubject<AuthUser | null>(null);

  constructor(private http: HttpClient, private userPrefs: UserPreferencesService) {
    this.expireSessionOnlyIfBrowserClosed();
    this.loadStoredAuth();
    this.startAliveHeartbeat();
  }

  /**
   * « Se souvenir de moi » décoché → session de NAVIGATION seulement.
   * Les jetons restent en localStorage (trop de lecteurs directs pour changer
   * de stockage sans risque) ; à la place, chaque onglet authentifié émet un
   * battement (app_alive_at). Au démarrage, si la session est marquée
   * « session_only » et qu'aucun onglet n'a battu depuis > 90 s, le navigateur
   * a été fermé entre-temps → session purgée. Multi-onglets sûr : un onglet
   * ouvert maintient le battement pour les autres.
   */
  private setSessionPersistence(remember: boolean): void {
    if (remember) localStorage.removeItem('session_only');
    else localStorage.setItem('session_only', '1');
    localStorage.setItem('app_alive_at', String(Date.now()));
  }

  private startAliveHeartbeat(): void {
    setInterval(() => {
      if (localStorage.getItem('auth_token')) {
        localStorage.setItem('app_alive_at', String(Date.now()));
      }
    }, 15_000);
  }

  private expireSessionOnlyIfBrowserClosed(): void {
    if (localStorage.getItem('session_only') !== '1') return;
    const last = parseInt(localStorage.getItem('app_alive_at') || '0', 10);
    if (last && Date.now() - last > 90_000) {
      localStorage.removeItem('auth_token');
      localStorage.removeItem('refresh_token');
      localStorage.removeItem('auth_user');
      localStorage.removeItem('session_only');
    }
  }

  /**
   * The currency shown across the app follows the ACCOUNT (server-side
   * Societe.Settings.Currency, else the deployment default), not the browser.
   * Seeding it into UserPreferencesService on login / session-restore makes
   * every screen consistent and overrides any stale localStorage value
   * (e.g. an old "TND" left over before a deployment was switched to DZD).
   */
  private applyAccountCurrency(currency: string | null | undefined): void {
    if (currency) this.userPrefs.update({ currency: currency as any });
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
          companyType: parsed.companyType || '',
          isCompanyAdmin: parsed.isCompanyAdmin ?? false,
          isSystemAdmin: parsed.isSystemAdmin ?? false,
          subscriptionFeatures: parsed.subscriptionFeatures ?? null,
          assignedVehicleIds: parsed.assignedVehicleIds ?? null,
          userPermissions: parsed.userPermissions ?? null,
          currency: parsed.currency
        });
        this.applyAccountCurrency(parsed.currency);
      } catch (e) {
        console.error('Error loading stored auth:', e);
      }
    }
  }

  login(email: string, password: string, rememberMe: boolean = true): Observable<AuthUser | null> {
    // Mock user check - admin@test.com with password "admin" (case insensitive)
    if (email === 'admin@test.com' && password.toLowerCase() === 'admin') {
      console.log('AuthService.login - Using mock user');
      const mockUser: AuthUser = {
        id: '1',
        name: 'Admin Test',
        email: 'admin@test.com',
        roles: ['admin'],
        permissions: { all: true },
        companyId: '1',
        companyName: 'Demo Company',
        companyType: 'transport',
        isCompanyAdmin: true,
        isSystemAdmin: true,
        assignedVehicleIds: null,
        userPermissions: {
          accessLevel: 'admin',
          canMonitoring: true, canVehicles: true, canDrivers: true,
          canReports: true, canGeofences: true, canMaintenance: true,
          canCosts: true, canFuel: true, canDocuments: true, canAccidents: true,
          canUsers: true, canSettings: true, canSuppliers: true,
          canFleetManagement: true,
          canTours: true,
          canPlayback: true
        },
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
          moduleFuel: true,
          moduleReports: true,
          moduleSettings: true,
          moduleUsers: true,
          moduleSuppliers: true,
          moduleDocuments: true,
          moduleAccidents: true,
          moduleFleetManagement: true,
          moduleTours: true,
          reportTrips: true,
          reportFuel: true,
          reportSpeed: true,
          reportStops: true,
          reportMileage: true,
          reportCosts: true,
          reportMaintenance: true,
          reportDaily: true,
          reportMonthly: true,
          reportMileagePeriod: true,
          reportSpeedInfraction: true,
          reportDrivingBehavior: true,
          reportMonthlyCosts: true,
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
      localStorage.setItem('refresh_token', mockResponse.refreshToken);
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
          userPermissions: response.user.userPermissions ?? null,
          currency: response.user.currency
        };
        console.log('AuthService.login - Mapped subscriptionFeatures:', user.subscriptionFeatures);
        console.log('AuthService.login - User permissions:', user.userPermissions);
        localStorage.setItem('auth_token', response.token);
        localStorage.setItem('refresh_token', response.refreshToken);
        localStorage.setItem('auth_user', JSON.stringify(user));
        this.setSessionPersistence(rememberMe);
        this.currentUser$.next(user);
        this.applyAccountCurrency(user.currency);
        console.log('AuthService.login - User mapped:', user);
        return user;
      }),
      catchError(err => {
        console.error('AuthService.login - Error:', err);
        return of(null);
      })
    );
  }

  /**
   * Inscription libre : crée la société du visiteur et son compte, puis ouvre la
   * session dans la foulée.
   *
   * L'API renvoie exactement la même charge utile que la connexion : on passe donc
   * par le MÊME mappage (applySession) plutôt que d'en écrire un second, qui
   * finirait par diverger — c'est déjà arrivé avec l'impersonation, qui oubliait
   * la devise.
   *
   * Renvoie null en cas d'échec, l'appelant lisant le message porté par l'erreur.
   */
  registerCompany(payload: {
    firstName: string;
    lastName: string;
    email: string;
    password: string;
    accountType: 'particulier' | 'societe';
    companyName?: string;
    phone?: string;
  }): Observable<{ email: string; message: string; emailSent: boolean }> {
    return this.http.post<{ email: string; message: string; emailSent: boolean }>(
      `${this.API_URL}/auth/register`, payload);
  }

  /** Confirme l'adresse email depuis le lien reçu, et active le compte. */
  confirmEmail(token: string): Observable<{ success: boolean; message: string; alreadyConfirmed: boolean }> {
    return this.http.post<{ success: boolean; message: string; alreadyConfirmed: boolean }>(
      `${this.API_URL}/auth/confirm-email`, { token });
  }

  /** Redemande un lien de confirmation. La réponse est neutre par construction. */
  resendConfirmation(email: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(
      `${this.API_URL}/auth/resend-confirmation`, { email });
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

  logout() {
    // Clear all auth-related storage
    localStorage.removeItem('auth_token');
    localStorage.removeItem('refresh_token');
    localStorage.removeItem('auth_user');
    localStorage.removeItem('admin_token');
    localStorage.removeItem('admin_user');
    localStorage.removeItem('impersonating_active');
    localStorage.removeItem('session_only');
    this.currentUser$.next(null);
  }

  isAuthenticated(): boolean {
    const token = localStorage.getItem('auth_token');
    if (!token) return false;
    // Mock token doesn't expire
    if (token === 'mock-jwt-token-for-testing') return true;
    return !this.isTokenExpired(token);
  }

  isTokenExpiringSoon(): boolean {
    const token = localStorage.getItem('auth_token');
    if (!token || token === 'mock-jwt-token-for-testing') return false;
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      const exp = payload.exp * 1000;
      // Consider "expiring soon" if less than 5 minutes remain
      return (exp - Date.now()) < 5 * 60 * 1000;
    } catch {
      return true;
    }
  }

  private isTokenExpired(token: string): boolean {
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      return payload.exp * 1000 < Date.now();
    } catch {
      return true;
    }
  }

  getCurrentUser(): Observable<AuthUser | null> {
    return this.currentUser$.asObservable();
  }

  getCurrentUserSync(): AuthUser | null {
    return this.currentUser$.value;
  }

  /**
   * Merge updates into the current auth user and persist to localStorage.
   * Used after self-service actions like profile updates so the whole app
   * (sidebar, toolbar, guards) sees the new values without a full re-login.
   */
  patchCurrentUser(updates: Partial<AuthUser>): void {
    const current = this.currentUser$.value;
    if (!current) return;
    const merged: AuthUser = { ...current, ...updates };
    localStorage.setItem('auth_user', JSON.stringify(merged));
    this.currentUser$.next(merged);
  }

  // ─────────── Impersonation ("voir en tant que", super-admin) ───────────
  // NB : l'espace /admin utilise admin_token/admin_user pour SA propre session. On n'y
  // touche donc PAS ici ; la session "voir en tant que" vit dans les clés client
  // (auth_token/auth_user) + un simple flag pour l'identifier.

  /** True si une session "voir en tant que" est active dans l'app client. */
  isImpersonating(): boolean {
    return localStorage.getItem('impersonating_active') === '1';
  }

  /**
   * Démarre une session client au périmètre de l'utilisateur cible, à partir de la
   * réponse de POST /auth/impersonate (appelée par l'espace admin avec son token admin).
   */
  beginImpersonation(response: AuthResponse): AuthUser {
    const user = this.mapAuthResponse(response);
    localStorage.setItem('auth_token', response.token);
    localStorage.setItem('refresh_token', response.refreshToken);
    localStorage.setItem('auth_user', JSON.stringify(user));
    localStorage.setItem('impersonating_active', '1');
    this.currentUser$.next(user);
    return user;
  }

  /** Termine le "voir en tant que" : efface la session client impersonée (le retour se fait vers /admin). */
  stopImpersonation(): void {
    localStorage.removeItem('impersonating_active');
    localStorage.removeItem('auth_token');
    localStorage.removeItem('refresh_token');
    localStorage.removeItem('auth_user');
    this.currentUser$.next(null);
  }

  /** Mappe la réponse d'auth (login/impersonate) vers AuthUser. */
  private mapAuthResponse(response: AuthResponse): AuthUser {
    return {
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
  }

  getToken(): string | null {
    return localStorage.getItem('auth_token');
  }

  getRefreshToken(): string | null {
    return localStorage.getItem('refresh_token');
  }

  refreshAccessToken(): Observable<AuthResponse | null> {
    const token = localStorage.getItem('auth_token');
    const refreshToken = localStorage.getItem('refresh_token');
    if (!token || !refreshToken) return of(null);

    return this.http.post<AuthResponse>(`${this.API_URL}/auth/refresh`, { token, refreshToken }).pipe(
      tap(response => {
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
          userPermissions: response.user.userPermissions ?? null,
          currency: response.user.currency
        };
        localStorage.setItem('auth_token', response.token);
        localStorage.setItem('refresh_token', response.refreshToken);
        localStorage.setItem('auth_user', JSON.stringify(user));
        this.currentUser$.next(user);
        this.applyAccountCurrency(user.currency);
        console.log('AuthService - Token refreshed successfully');
      }),
      catchError(err => {
        console.error('AuthService - Token refresh failed:', err);
        this.logout();
        return of(null);
      })
    );
  }
}
