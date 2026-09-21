import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable, BehaviorSubject, of, map, catchError, from, switchMap, firstValueFrom } from 'rxjs';
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

/**
 * Type de compte renvoyé par le serveur (users.account_type, migration 050) :
 * « staff » = gestionnaire ordinaire (onglets classiques), « driver » = chauffeur
 * (uniquement « Mes tournées » ; le serveur répond 403 partout ailleurs).
 */
export type AccountType = 'staff' | 'driver';

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
  accountType: AccountType;
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
    accountType?: string;
  };
}

/**
 * En-tête par lequel l'application se déclare au serveur (AuthController.ClientHeader).
 * OBLIGATOIRE sur /auth/login : sans lui, un compte chauffeur est refusé avec
 * « Ce compte est réservé à l'application mobile Calypso… ».
 */
export const CALYPSO_CLIENT_HEADER = 'X-Calypso-Client';
export const CALYPSO_CLIENT_MOBILE = 'mobile';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private apiUrl = environment.apiUrl;
  private currentUser$ = new BehaviorSubject<AuthUser | null>(null);
  private token: string | null = null;
  private refreshTokenValue: string | null = null;
  private _ready: Promise<void>;
  /** Rafraîchissement en cours (partagé pour ne pas en lancer deux). */
  private restoreInFlight: Promise<boolean> | null = null;
  /** Le dernier rafraîchissement a échoué faute de réseau (et non parce que le jeton est révoqué). */
  private lastRefreshWasNetworkError = false;

  constructor(private http: HttpClient) {
    this._ready = this.loadStoredAuth();
  }

  /** Resolves when stored tokens have been loaded from Preferences */
  get ready(): Promise<void> {
    return this._ready;
  }

  get API_URL(): string {
    return this.apiUrl;
  }

  private get clientHeaders(): HttpHeaders {
    return new HttpHeaders({ [CALYPSO_CLIENT_HEADER]: CALYPSO_CLIENT_MOBILE });
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
        const parsed = JSON.parse(userData) as AuthUser;
        // Sessions stockées avant l'arrivée des comptes chauffeur : gestionnaire.
        if (parsed.accountType !== 'driver') parsed.accountType = 'staff';
        this.currentUser$.next(parsed);
      }
    } catch (e) {
      console.error('Error loading stored auth:', e);
    }
  }

  /** Réponse serveur → utilisateur de l'application (même forme au login et au refresh). */
  private mapUser(response: AuthResponse): AuthUser {
    const u = response.user;
    return {
      id: u.id?.toString() || '',
      name: `${u.firstName} ${u.lastName}`.trim(),
      email: u.email,
      phone: u.phone,
      roles: [u.roleName],
      permissions: u.permissions || {},
      companyId: u.companyId.toString(),
      companyName: u.companyName,
      companyType: u.companyType || '',
      isCompanyAdmin: u.isCompanyAdmin,
      isSystemAdmin: u.isSystemAdmin,
      subscriptionFeatures: u.subscriptionFeatures,
      assignedVehicleIds: u.assignedVehicleIds ?? null,
      userPermissions: u.userPermissions ?? null,
      accountType: u.accountType === 'driver' ? 'driver' : 'staff'
    };
  }

  login(email: string, password: string): Observable<AuthUser | null> {
    // L'application se déclare DEUX fois (le serveur accepte l'un ou l'autre) : en-tête
    // X-Calypso-Client ET champ « client » du corps — un proxy peut retirer un en-tête
    // qu'il ne connaît pas, et un compte chauffeur serait alors refusé.
    const body = { email, password, client: CALYPSO_CLIENT_MOBILE };
    return this.http.post<AuthResponse>(`${this.apiUrl}/auth/login`, body, { headers: this.clientHeaders }).pipe(
      switchMap(response => {
        const user = this.mapUser(response);
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
        throw err;
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

  /** Un jeton de rafraîchissement est stocké (la session peut être restaurée sans mot de passe). */
  hasRefreshToken(): boolean {
    return !!this.token && !!this.refreshTokenValue;
  }

  /** Compte chauffeur : uniquement « Mes tournées », jamais SignalR ni les autres routes. */
  isDriver(): boolean {
    return this.currentUser$.value?.accountType === 'driver';
  }

  /**
   * Session utilisable ? Vrai si le jeton d'accès est valide ; sinon, s'il existe un
   * jeton de rafraîchissement, tente un rafraîchissement SILENCIEUX (un chauffeur ne
   * ressaisit pas son mot de passe tous les jours sur un téléphone de service).
   * Échec réseau : la session stockée est conservée et considérée utilisable —
   * l'intercepteur rafraîchira au premier 401 quand le réseau reviendra. Refus du
   * serveur (jeton révoqué, compte désactivé) : déconnexion.
   */
  restoreSession(): Promise<boolean> {
    if (!this.restoreInFlight) {
      this.restoreInFlight = this.doRestoreSession().finally(() => { this.restoreInFlight = null; });
    }
    return this.restoreInFlight;
  }

  private async doRestoreSession(): Promise<boolean> {
    await this.ready;
    if (this.isAuthenticated()) return true;
    if (!this.hasRefreshToken()) return false;

    const refreshed = await firstValueFrom(this.refreshAccessToken());
    if (refreshed) return true;
    return this.lastRefreshWasNetworkError && !!this.currentUser$.value;
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
    }, { headers: this.clientHeaders }).pipe(
      switchMap(response => {
        const user = this.mapUser(response);
        this.token = response.token;
        this.refreshTokenValue = response.refreshToken;
        this.lastRefreshWasNetworkError = false;

        return from(this.saveAuth(response.token, response.refreshToken, user)).pipe(
          map(() => {
            this.currentUser$.next(user);
            return response;
          })
        );
      }),
      catchError(err => {
        console.error('Token refresh failed:', err);
        // Pas de réseau (status 0) : le jeton n'est pas refusé, il n'a pas pu être
        // présenté. Garder la session pour réessayer plus tard au lieu de déconnecter
        // un chauffeur en zone blanche.
        this.lastRefreshWasNetworkError = err?.status === 0;
        if (!this.lastRefreshWasNetworkError) {
          this.logout();
        }
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
