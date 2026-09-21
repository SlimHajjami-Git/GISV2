import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable, BehaviorSubject, map, catchError, from, switchMap, firstValueFrom } from 'rxjs';
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
  /** Restauration de session en cours (partagée entre les gardes lancées en parallèle). */
  private restoreInFlight: Promise<boolean> | null = null;
  /**
   * LE rafraîchissement du jeton en vol, unique pour toute l'application : restoreSession,
   * l'intercepteur (refresh proactif, rattrapage d'un 401) et la bascule d'espace passent
   * tous par lui. Relecture du 21/09/2026 (constat 1) : l'intercepteur avait son propre
   * drapeau ; deux POST /auth/refresh partaient avec le MÊME jeton de rafraîchissement,
   * le serveur le révoque au premier usage, le second recevait 401 et effaçait la session
   * que le premier venait de restaurer (chauffeur déconnecté, suivi arrêté).
   */
  private refreshInFlight: Promise<AuthResponse | null> | null = null;

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

  /** Identifiant du compte connecté (propriétaire des files hors ligne), null sans session. */
  currentUserId(): string | null {
    return this.currentUser$.value?.id || null;
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
   * Échec passager (pas de réseau, 5xx pendant un déploiement, délai, 429) : la session
   * stockée est conservée et considérée utilisable — l'intercepteur rafraîchira au
   * premier 401 quand le serveur répondra. Refus explicite du jeton (401/403 : révoqué,
   * compte désactivé) : la session a été effacée, on rend faux.
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

    const refreshed = await this.refreshShared();
    if (refreshed) return true;
    // Un refus explicite a effacé la session (plus de jeton) ; sinon elle reste utilisable.
    return this.hasRefreshToken() && !!this.currentUser$.value;
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

  /**
   * Rafraîchit le jeton d'accès. UN SEUL appel réseau à la fois pour toute l'application :
   * un appel concurrent reçoit le résultat du rafraîchissement déjà en vol. Rend null en
   * cas d'échec, jamais d'erreur.
   *
   * La session n'est effacée QUE sur un refus explicite (401/403) du jeton de
   * rafraîchissement ENCORE EN PLACE. Jamais sur 0 (réseau), 408, 429 ni 5xx (rollout de
   * l'API, Traefik 502/503) : on déconnecterait un chauffeur en pleine tournée pour une
   * panne passagère (constats 5 et 12). Jamais non plus sur l'échec d'un jeton déjà
   * remplacé entre-temps (nouvelle connexion) : ce refus ne concerne plus la session.
   */
  refreshAccessToken(): Observable<AuthResponse | null> {
    return from(this.refreshShared());
  }

  /** Le rafraîchissement en vol, s'il y en a un : l'intercepteur y fait attendre les requêtes. */
  get pendingRefresh(): Promise<AuthResponse | null> | null {
    return this.refreshInFlight;
  }

  /** 401/403 sur /auth/refresh : le serveur refuse ce jeton (révoqué, compte désactivé). */
  static isExplicitRefusal(err: any): boolean {
    return err?.status === 401 || err?.status === 403;
  }

  private refreshShared(): Promise<AuthResponse | null> {
    if (this.refreshInFlight) return this.refreshInFlight;
    if (!this.token || !this.refreshTokenValue) return Promise.resolve(null);
    this.refreshInFlight = this.doRefresh(this.token, this.refreshTokenValue)
      .finally(() => { this.refreshInFlight = null; });
    return this.refreshInFlight;
  }

  private async doRefresh(token: string, sentRefresh: string): Promise<AuthResponse | null> {
    let response: AuthResponse;
    try {
      response = await firstValueFrom(this.http.post<AuthResponse>(`${this.apiUrl}/auth/refresh`, {
        token,
        refreshToken: sentRefresh
      }, { headers: this.clientHeaders }));
    } catch (err: any) {
      console.error('Token refresh failed:', err);
      if (AuthService.isExplicitRefusal(err) && this.refreshTokenValue === sentRefresh) {
        await this.logout();
      }
      return null;
    }
    // Déconnexion ou autre connexion pendant l'appel : ne pas ressusciter l'ancienne session.
    if (this.refreshTokenValue !== sentRefresh) return null;

    const user = this.mapUser(response);
    this.token = response.token;
    this.refreshTokenValue = response.refreshToken;
    await this.saveAuth(response.token, response.refreshToken, user);
    this.currentUser$.next(user);
    return response;
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
