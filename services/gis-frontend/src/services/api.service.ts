import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Observable, BehaviorSubject, tap, of, map } from 'rxjs';
import { MockDataService } from './mock-data.service';
import { environment } from '../environments/environment';

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  name: string;
  email: string;
  password: string;
  companyName: string;
  phone?: string;
}

export interface AuthResponse {
  token: string;
  refreshToken: string;
  user: UserDto;
}

export interface UserDto {
  id: number;
  name: string;
  email: string;
  phone?: string;
  roles: string[];
  permissions: string[];
  companyId: number;
  companyName: string;
  userType: string;
  isCompanyAdmin: boolean;
  roleId?: number;
  roleName?: string;
  modulePermissions?: Record<string, boolean>;
  reportPermissions?: Record<string, boolean>;
  currency?: string;
}

export interface PositionDto {
  id: number;
  latitude: number;
  longitude: number;
  speedKph?: number;
  courseDeg?: number;
  ignitionOn?: boolean;
  recordedAt: string;
  address?: string;
  fuelRaw?: number;
  odometerKm?: number;
  isRealTime?: boolean;
  temperatureC?: number;
  batteryLevel?: number;
  /** Decoded volts (PowerVoltage byte * 0.3) — preferred on monitoring. */
  batteryVoltage?: number;
  /** Accelerometer (MEMS) raw values clamped to [-128 ; 127]. Used by the
   * accident report to reconstruct second-shock, sustained tilt (rollover)
   * and tow-loading from the same history call. */
  memsX?: number;
  memsY?: number;
  memsZ?: number;
}

export interface VehicleStatsDto {
  currentSpeed: number;
  maxSpeed: number;
  fuelLevel?: number;
  temperature?: number;
  batteryLevel?: number;
  /** Battery in volts — populated alongside batteryLevel for the monitoring readout. */
  batteryVoltage?: number;
  isMoving: boolean;
  isStopped: boolean;
  movingTime: string;   // TimeSpan as ISO string
  stoppedTime: string;  // TimeSpan as ISO string
  lastStopTime?: string;
  lastMoveTime?: string;
  /** Timestamp of the last frame with ignition_on=true. After this point the engine has been off. */
  engineOffSince?: string;
}

export interface VehicleWithPositionDto {
  id: number;
  name: string;
  type: string;
  brand?: string;
  model?: string;
  plate?: string;
  status: string;
  hasGps: boolean;
  deviceUid?: string;
  lastCommunication?: string;
  isOnline: boolean;
  lastPosition?: PositionDto;
  stats?: VehicleStatsDto;
  /** Sticky 7-day battery-health alert flag (VoltageHealthMonitoringService). */
  hasBatteryHealthAlert?: boolean;
  /**
   * Operator-toggled immobilisation. When true, every automatic alert
   * service skips this vehicle. Surfaced on the monitoring page as a
   * clear badge so the operator knows alerts are muted.
   */
  isImmobilized?: boolean;
  immobilizationReason?: string;
  immobilizationStartedAt?: string;
}


@Injectable({
  providedIn: 'root'
})
export class ApiService {
  private readonly API_URL = this.getApiUrl();
  private currentUser$ = new BehaviorSubject<UserDto | null>(null);
  private useMockData = false;

  constructor(private http: HttpClient, private mockDataService: MockDataService) {
    this.loadStoredUser();
  }

  private isMockUser(): boolean {
    const user = this.currentUser$.value;
    return user?.email === 'admin@test.com' || this.useMockData;
  }

  private getApiUrl(): string {
    return '/api';
  }

  private getMonitoringApiUrl(): string {
    return environment.apiUrl;
  }

  private loadStoredUser() {
    const token = localStorage.getItem('auth_token');
    const user = localStorage.getItem('auth_user');
    if (token && user) {
      const parsedUser = JSON.parse(user);
      this.currentUser$.next(parsedUser);
      // Enable mock data if admin@test.com
      if (parsedUser.email === 'admin@test.com') {
        this.useMockData = true;
        this.mockDataService.login(parsedUser.email, '');
      }
    }
  }

  private getHeaders(): HttpHeaders {
    const token = localStorage.getItem('auth_token');
    return new HttpHeaders({
      'Content-Type': 'application/json',
      ...(token ? { 'Authorization': `Bearer ${token}` } : {})
    });
  }

  // ==================== AUTH ====================

  login(request: LoginRequest): Observable<AuthResponse> {
    // Check if this is the mock user (admin@test.com / Admin) - case insensitive password
    if (request.email === 'admin@test.com' && request.password.toLowerCase() === 'admin') {
      this.useMockData = true;
      this.mockDataService.login(request.email, request.password);
      
      // Return mock auth response
      const mockUser: UserDto = {
        id: 1,
        name: 'Admin Test',
        email: 'admin@test.com',
        roles: ['admin'],
        permissions: ['all'],
        companyId: 1,
        companyName: 'Demo Company',
        userType: 'company_admin',
        isCompanyAdmin: true,
        roleId: 1,
        roleName: 'Administrateur',
        modulePermissions: {
          dashboard: true, monitoring: true, vehicles: true, employees: true,
          geofences: true, maintenance: true, costs: true, reports: true,
          settings: true, users: true, suppliers: true, documents: true,
          accidents: true, fleet_management: true
        },
        reportPermissions: {
          trips: true, fuel: true, speed: true, stops: true, mileage: true,
          costs: true, maintenance: true, daily: true, monthly: true,
          mileage_period: true, speed_infraction: true, driving_behavior: true
        }
      };
      const mockResponse: AuthResponse = {
        token: 'mock-jwt-token-for-testing',
        refreshToken: 'mock-refresh-token',
        user: mockUser
      };
      
      localStorage.setItem('auth_token', mockResponse.token);
      localStorage.setItem('auth_user', JSON.stringify(mockResponse.user));
      this.currentUser$.next(mockResponse.user);
      
      return of(mockResponse);
    }
    
    // Real API login for other users
    this.useMockData = false;
    return this.http.post<AuthResponse>(`${this.API_URL}/auth/login`, request).pipe(
      tap(response => {
        localStorage.setItem('auth_token', response.token);
        localStorage.setItem('auth_user', JSON.stringify(response.user));
        this.currentUser$.next(response.user);
      })
    );
  }

  register(request: RegisterRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.API_URL}/auth/register`, request).pipe(
      tap(response => {
        localStorage.setItem('auth_token', response.token);
        localStorage.setItem('auth_user', JSON.stringify(response.user));
        this.currentUser$.next(response.user);
      })
    );
  }

  logout() {
    // Best-effort: notify the backend so it records the logout in the audit trail and
    // revokes refresh tokens. Fire-and-forget — never block or fail the client-side logout.
    const token = localStorage.getItem('auth_token');
    if (token && token !== 'mock-jwt-token-for-testing') {
      this.http.post(`${this.API_URL}/auth/logout`, {}, { headers: this.getHeaders() })
        .subscribe({ next: () => {}, error: () => {} });
    }
    localStorage.removeItem('auth_token');
    localStorage.removeItem('auth_user');
    this.currentUser$.next(null);
    this.useMockData = false;
    this.mockDataService.logout();
  }

  isAuthenticated(): boolean {
    return !!localStorage.getItem('auth_token');
  }

  getCurrentUser(): Observable<UserDto | null> {
    return this.currentUser$.asObservable();
  }

  getCurrentUserSync(): UserDto | null {
    return this.currentUser$.value;
  }

  // ==================== ROLES ====================

  getRoles(includeSystem = true): Observable<any[]> {
    const params = new HttpParams().set('includeSystem', includeSystem.toString());
    return this.http.get<any[]>(`${this.API_URL}/roles`, { headers: this.getHeaders(), params });
  }

  getRole(id: number): Observable<any> {
    return this.http.get<any>(`${this.API_URL}/roles/${id}`, { headers: this.getHeaders() });
  }

  createRole(role: { name: string; description?: string; isCompanyAdmin?: boolean; permissions?: Record<string, any> }): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/roles`, role, { headers: this.getHeaders() });
  }

  updateRole(id: number, role: { name?: string; description?: string; isCompanyAdmin?: boolean; permissions?: Record<string, any> }): Observable<any> {
    return this.http.put<any>(`${this.API_URL}/roles/${id}`, role, { headers: this.getHeaders() });
  }

  deleteRole(id: number): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/roles/${id}`, { headers: this.getHeaders() });
  }

  // ==================== USERS ====================

  getUsers(): Observable<any[]> {
    return this.http.get<any[]>(`${this.API_URL}/users`, { headers: this.getHeaders() });
  }

  getUser(id: number): Observable<any> {
    return this.http.get<any>(`${this.API_URL}/users/${id}`, { headers: this.getHeaders() });
  }

  createUser(user: any): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/users`, user, { headers: this.getHeaders() });
  }

  updateUser(id: number, user: any): Observable<void> {
    return this.http.put<void>(`${this.API_URL}/users/${id}`, user, { headers: this.getHeaders() });
  }

  deleteUser(id: number): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/users/${id}`, { headers: this.getHeaders() });
  }

  // ==================== VEHICLES ====================

  getVehicles(): Observable<any[]> {
    if (this.isMockUser()) {
      return this.mockDataService.getVehicles();
    }
    return this.http.get<any[]>(`${this.API_URL}/vehicles`, { headers: this.getHeaders() });
  }

  getVehiclesWithPositions(): Observable<any[]> {
    return this.http.get<any[]>(`${this.getMonitoringApiUrl()}/vehicles/with-positions`, { headers: this.getHeaders() });
  }

  getVehicle(id: number): Observable<any> {
    if (this.isMockUser()) {
      return this.mockDataService.getVehicles().pipe(
        map(vehicles => vehicles.find(v => v.id === id.toString()))
      );
    }
    return this.http.get<any>(`${this.API_URL}/vehicles/${id}`, { headers: this.getHeaders() });
  }

  createVehicle(vehicle: any): Observable<any> {
    if (this.isMockUser()) {
      return of(this.mockDataService.addVehicle(vehicle));
    }
    return this.http.post<any>(`${this.API_URL}/vehicles`, vehicle, { headers: this.getHeaders() });
  }

  updateVehicle(id: number, vehicle: any): Observable<void> {
    if (this.isMockUser()) {
      this.mockDataService.updateVehicle({ ...vehicle, id: id.toString() });
      return of(void 0);
    }
    return this.http.put<void>(`${this.API_URL}/vehicles/${id}`, vehicle, { headers: this.getHeaders() });
  }

  patchVehicle(id: number, patch: any): Observable<void> {
    // Partial update — sends only the fields provided, preserves others
    if (this.isMockUser()) {
      this.mockDataService.updateVehicle({ ...patch, id: id.toString() });
      return of(void 0);
    }
    return this.http.patch<void>(`${this.API_URL}/vehicles/${id}`, patch, { headers: this.getHeaders() });
  }

  // Brands & Models
  getBrands(): Observable<any[]> {
    return this.http.get<any[]>(`${this.API_URL}/brands`, { headers: this.getHeaders() });
  }

  getBrandModels(brandId: number): Observable<any[]> {
    return this.http.get<any[]>(`${this.API_URL}/brands/${brandId}/models`, { headers: this.getHeaders() });
  }

  deleteVehicle(id: number): Observable<void> {
    if (this.isMockUser()) {
      this.mockDataService.deleteVehicle(id.toString());
      return of(void 0);
    }
    return this.http.delete<void>(`${this.API_URL}/vehicles/${id}`, { headers: this.getHeaders() });
  }

  syncVehicleMileage(vehicleId: number): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/vehicles/${vehicleId}/sync-mileage`, {}, { headers: this.getHeaders() });
  }

  /**
   * Activate the "immobilisation" flag on a vehicle. While the flag is
   * active, every automatic alert service (accident, speed, battery,
   * geofence) is skipped server-side for this vehicle. Use when the
   * vehicle is at the mechanic, in long-term parking, or has had its
   * boîtier removed.
   */
  immobilizeVehicle(vehicleId: number, reason: string | null): Observable<any> {
    return this.http.post<any>(
      `${this.API_URL}/vehicles/${vehicleId}/immobilize`,
      { reason },
      { headers: this.getHeaders() }
    );
  }

  /** Clear the immobilisation flag — re-enables every alert service for the vehicle. */
  clearVehicleImmobilization(vehicleId: number): Observable<any> {
    return this.http.delete<any>(
      `${this.API_URL}/vehicles/${vehicleId}/immobilize`,
      { headers: this.getHeaders() }
    );
  }

  // ==================== ALERT EMAILS ====================

  getAlertEmails(alertType?: string): Observable<any[]> {
    let url = `${this.API_URL}/alertemails`;
    if (alertType) url += `?alertType=${alertType}`;
    return this.http.get<any[]>(url, { headers: this.getHeaders() });
  }

  createAlertEmail(data: { email: string; alertType: string }): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/alertemails`, data, { headers: this.getHeaders() });
  }

  updateAlertEmail(id: number, data: { email: string; alertType: string }): Observable<void> {
    return this.http.put<void>(`${this.API_URL}/alertemails/${id}`, data, { headers: this.getHeaders() });
  }

  deleteAlertEmail(id: number): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/alertemails/${id}`, { headers: this.getHeaders() });
  }

  testAlertEmail(id: number): Observable<{ sent: boolean }> {
    return this.http.post<{ sent: boolean }>(`${this.API_URL}/alertemails/${id}/test`, {}, { headers: this.getHeaders() });
  }

  // ==================== RESERVATIONS / EMPRUNTS ====================

  getReservations(params?: { vehicleId?: number; driverId?: number; status?: string; from?: string; to?: string }): Observable<any[]> {
    let url = `${this.API_URL}/reservations`;
    const queryParams: string[] = [];
    if (params?.vehicleId) queryParams.push(`vehicleId=${params.vehicleId}`);
    if (params?.driverId) queryParams.push(`driverId=${params.driverId}`);
    if (params?.status) queryParams.push(`status=${params.status}`);
    if (params?.from) queryParams.push(`from=${params.from}`);
    if (params?.to) queryParams.push(`to=${params.to}`);
    if (queryParams.length) url += '?' + queryParams.join('&');
    return this.http.get<any[]>(url, { headers: this.getHeaders() });
  }

  getAvailableVehiclesForBorrowing(): Observable<any[]> {
    return this.http.get<any[]>(`${this.API_URL}/reservations/available-vehicles`, { headers: this.getHeaders() });
  }

  createReservation(data: { vehicleId: number; assignedDriverId?: number; purpose?: string; destination?: string; estimatedKm?: number; notes?: string }): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/reservations`, data, { headers: this.getHeaders() });
  }

  completeReservation(id: number, notes?: string): Observable<any> {
    return this.http.put<any>(`${this.API_URL}/reservations/${id}/complete`, { notes }, { headers: this.getHeaders() });
  }

  cancelReservation(id: number, reason?: string): Observable<any> {
    return this.http.put<any>(`${this.API_URL}/reservations/${id}/cancel`, { reason }, { headers: this.getHeaders() });
  }

  setVehicleRentalStatus(vehicleId: number, isRented: boolean): Observable<any> {
    return this.http.put<any>(`${this.API_URL}/vehicles/${vehicleId}/rental-status`, { isRented }, { headers: this.getHeaders() });
  }

  getVehicleLocations(): Observable<any[]> {
    return this.getLatestPositions();
  }

  // ==================== DEPARTMENTS ====================

  getDepartments(): Observable<any[]> {
    if (this.isMockUser()) {
      return of([]);
    }
    return this.http.get<any[]>(`${this.API_URL}/fleet/departments`, { headers: this.getHeaders() });
  }

  // ==================== DRIVERS ====================

  getDrivers(): Observable<any[]> {
    if (this.isMockUser()) {
      return this.mockDataService.getEmployees().pipe(
        map(employees => employees.filter(e => e.employeeRole === 'driver'))
      );
    }
    return this.http.get<any[]>(`${this.API_URL}/drivers`, { headers: this.getHeaders() });
  }

  createDriver(driver: any): Observable<any> {
    if (this.isMockUser()) {
      return of(driver);
    }
    return this.http.post<any>(`${this.API_URL}/drivers`, driver, { headers: this.getHeaders() });
  }

  updateDriver(id: number, driver: any): Observable<void> {
    if (this.isMockUser()) {
      return of(void 0);
    }
    return this.http.put<void>(`${this.API_URL}/drivers/${id}`, { id, ...driver }, { headers: this.getHeaders() });
  }

  deleteDriver(id: number): Observable<void> {
    if (this.isMockUser()) {
      return of(void 0);
    }
    return this.http.delete<void>(`${this.API_URL}/drivers/${id}`, { headers: this.getHeaders() });
  }

  getCompanyUsers(): Observable<any[]> {
    if (this.isMockUser()) {
      return of([]);
    }
    return this.http.get<any[]>(`${this.API_URL}/users`, { headers: this.getHeaders() });
  }

  // ==================== GEOFENCES ====================

  getGeofences(): Observable<any[]> {
    if (this.isMockUser()) {
      return this.mockDataService.getGeofences();
    }
    return this.http.get<any[]>(`${this.API_URL}/geofences`, { headers: this.getHeaders() }).pipe(
      map(geofences => geofences.map(g => this.transformGeofence(g)))
    );
  }

  getGeofence(id: number): Observable<any> {
    if (this.isMockUser()) {
      return this.mockDataService.getGeofences().pipe(
        map(geofences => geofences.find(g => g.id === id.toString()))
      );
    }
    return this.http.get<any>(`${this.API_URL}/geofences/${id}`, { headers: this.getHeaders() }).pipe(
      map(g => this.transformGeofence(g))
    );
  }

  private transformGeofence(g: any): any {
    return {
      ...g,
      center: (g.centerLat != null && g.centerLng != null) ? { lat: g.centerLat, lng: g.centerLng } : undefined,
      // Calypso 6 (P4): backend returns AssignedVehicleIds as int[] but the
      // frontend Vehicle.id is a string. Normalize here so includes/equality
      // work consistently across the geofence editor (the previous mismatch
      // caused the "monitored vehicles" checkboxes to all appear unchecked).
      assignedVehicleIds: Array.isArray(g.assignedVehicleIds)
        ? g.assignedVehicleIds.map((id: any) => String(id))
        : []
    };
  }

  createGeofence(geofence: any): Observable<any> {
    if (this.isMockUser()) {
      return of(this.mockDataService.addGeofence(geofence));
    }
    return this.http.post<any>(`${this.API_URL}/geofences`, geofence, { headers: this.getHeaders() });
  }

  updateGeofence(id: number, geofence: any): Observable<void> {
    if (this.isMockUser()) {
      this.mockDataService.updateGeofence(id.toString(), geofence);
      return of(void 0);
    }
    return this.http.put<void>(`${this.API_URL}/geofences/${id}`, geofence, { headers: this.getHeaders() });
  }

  deleteGeofence(id: number): Observable<void> {
    if (this.isMockUser()) {
      this.mockDataService.deleteGeofence(id.toString());
      return of(void 0);
    }
    return this.http.delete<void>(`${this.API_URL}/geofences/${id}`, { headers: this.getHeaders() });
  }

  assignGeofenceVehicles(id: number, vehicleIds: number[]): Observable<void> {
    if (this.isMockUser()) {
      return of(void 0);
    }
    return this.http.post<void>(`${this.API_URL}/geofences/${id}/vehicles`, vehicleIds, { headers: this.getHeaders() });
  }

  // ==================== GEOFENCE GROUPS ====================

  getGeofenceGroups(): Observable<any[]> {
    if (this.isMockUser()) {
      return of([]);
    }
    return this.http.get<any[]>(`${this.API_URL}/geofences/groups`, { headers: this.getHeaders() });
  }

  getGeofenceGroup(id: number): Observable<any> {
    return this.http.get<any>(`${this.API_URL}/geofences/groups/${id}`, { headers: this.getHeaders() });
  }

  createGeofenceGroup(group: any): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/geofences/groups`, group, { headers: this.getHeaders() });
  }

  updateGeofenceGroup(id: number, group: any): Observable<void> {
    return this.http.put<void>(`${this.API_URL}/geofences/groups/${id}`, group, { headers: this.getHeaders() });
  }

  deleteGeofenceGroup(id: number): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/geofences/groups/${id}`, { headers: this.getHeaders() });
  }

  // ==================== GEOFENCE EVENTS ====================

  getGeofenceEvents(filters?: { geofenceId?: number; vehicleId?: number; startDate?: Date; endDate?: Date; limit?: number }): Observable<any[]> {
    if (this.isMockUser()) {
      return of([]);
    }
    let params = new HttpParams();
    if (filters?.geofenceId) params = params.set('geofenceId', filters.geofenceId.toString());
    if (filters?.vehicleId) params = params.set('vehicleId', filters.vehicleId.toString());
    if (filters?.startDate) params = params.set('startDate', this.toLocalIso(filters.startDate));
    if (filters?.endDate) params = params.set('endDate', this.toLocalIso(filters.endDate));
    if (filters?.limit) params = params.set('limit', filters.limit.toString());
    return this.http.get<any[]>(`${this.API_URL}/geofences/events`, { headers: this.getHeaders(), params });
  }

  getGeofenceEventsByGeofence(geofenceId: number, limit = 50): Observable<any[]> {
    const params = new HttpParams().set('limit', limit.toString());
    return this.http.get<any[]>(`${this.API_URL}/geofences/${geofenceId}/events`, { headers: this.getHeaders(), params });
  }

  // ==================== MAINTENANCE ====================

  getMaintenanceRecords(vehicleId?: number, startDate?: Date, endDate?: Date): Observable<any[]> {
    if (this.isMockUser()) {
      return this.mockDataService.getMaintenanceRecords();
    }
    let params = new HttpParams();
    if (vehicleId) params = params.set('vehicleId', vehicleId.toString());
    if (startDate) params = params.set('startDate', this.toLocalIso(startDate).split('T')[0]);
    if (endDate) params = params.set('endDate', this.toLocalIso(endDate).split('T')[0]);
    return this.http.get<any[]>(`${this.API_URL}/vehicle-maintenance/logs`, { headers: this.getHeaders(), params });
  }

  getUpcomingMaintenance(): Observable<any[]> {
    if (this.isMockUser()) {
      return this.mockDataService.getMaintenanceRecords().pipe(
        map(records => records.filter(r => r.status === 'scheduled'))
      );
    }
    return this.http.get<any[]>(`${this.API_URL}/maintenance/upcoming`, { headers: this.getHeaders() });
  }

  createMaintenanceRecord(record: any): Observable<any> {
    if (this.isMockUser()) {
      return of(this.mockDataService.addMaintenanceRecord(record));
    }
    return this.http.post<any>(`${this.API_URL}/maintenance`, record, { headers: this.getHeaders() });
  }

  updateMaintenanceRecord(id: number, record: any): Observable<void> {
    if (this.isMockUser()) {
      this.mockDataService.updateMaintenanceRecord({ ...record, id: id.toString() });
      return of(void 0);
    }
    return this.http.put<void>(`${this.API_URL}/maintenance/${id}`, record, { headers: this.getHeaders() });
  }

  deleteMaintenanceRecord(id: number): Observable<void> {
    if (this.isMockUser()) {
      this.mockDataService.deleteMaintenanceRecord(id.toString());
      return of(void 0);
    }
    return this.http.delete<void>(`${this.API_URL}/maintenance/${id}`, { headers: this.getHeaders() });
  }

  // ==================== COSTS ====================

  getCosts(filters?: { vehicleId?: number; type?: string; startDate?: Date; endDate?: Date }): Observable<any[]> {
    if (this.isMockUser()) {
      return this.mockDataService.getVehicleCosts();
    }
    let params = new HttpParams();
    if (filters?.vehicleId) params = params.set('vehicleId', filters.vehicleId.toString());
    if (filters?.type) params = params.set('type', filters.type);
    if (filters?.startDate) params = params.set('startDate', this.toLocalIso(filters.startDate));
    if (filters?.endDate) params = params.set('endDate', this.toLocalIso(filters.endDate));
    return this.http.get<any[]>(`${this.API_URL}/costs`, { headers: this.getHeaders(), params });
  }

  getCostSummary(startDate?: Date, endDate?: Date): Observable<any> {
    if (this.isMockUser()) {
      return this.mockDataService.getVehicleCosts().pipe(
        map(costs => {
          const total = costs.reduce((sum, c) => sum + c.amount, 0);
          const fuel = costs.filter(c => c.type === 'fuel').reduce((sum, c) => sum + c.amount, 0);
          const maintenance = costs.filter(c => c.type === 'maintenance').reduce((sum, c) => sum + c.amount, 0);
          return { total, fuel, maintenance, other: total - fuel - maintenance };
        })
      );
    }
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', this.toLocalIso(startDate));
    if (endDate) params = params.set('endDate', this.toLocalIso(endDate));
    return this.http.get<any>(`${this.API_URL}/costs/summary`, { headers: this.getHeaders(), params });
  }

  createCost(cost: any): Observable<any> {
    if (this.isMockUser()) {
      return of(this.mockDataService.addVehicleCost(cost));
    }
    return this.http.post<any>(`${this.API_URL}/costs`, cost, { headers: this.getHeaders() });
  }

  /**
   * Scan an invoice (image/PDF) with AI. Returns { extraction, receiptUrl } for
   * user review — nothing is saved. The confirmed data is saved via createCost.
   */
  scanInvoice(file: File): Observable<any> {
    const formData = new FormData();
    formData.append('file', file, file.name);
    // Let the browser set multipart boundary — must NOT send our JSON Content-Type.
    const headers = this.getHeaders().delete('Content-Type');
    return this.http.post<any>(`${this.API_URL}/costs/scan-invoice`, formData, { headers });
  }

  /** Quota mensuel de scans IA de la société — { used, limit, remaining }. */
  getScanQuota(): Observable<{ used: number; limit: number; remaining: number }> {
    return this.http.get<{ used: number; limit: number; remaining: number }>(
      `${this.API_URL}/costs/scan-quota`, { headers: this.getHeaders() });
  }

  updateCost(id: number, cost: any): Observable<void> {
    if (this.isMockUser()) {
      return of(void 0);
    }
    return this.http.put<void>(`${this.API_URL}/costs/${id}`, cost, { headers: this.getHeaders() });
  }

  deleteCost(id: number): Observable<void> {
    if (this.isMockUser()) {
      this.mockDataService.deleteVehicleCost(id.toString());
      return of(void 0);
    }
    return this.http.delete<void>(`${this.API_URL}/costs/${id}`, { headers: this.getHeaders() });
  }

  // ==================== GPS TRACKING (Real-time) ====================

  getLatestPositions(): Observable<any[]> {
    return this.http.get<any[]>(`${this.getMonitoringApiUrl()}/gps/positions/latest`, { headers: this.getHeaders() });
  }

  getVehiclePosition(vehicleId: number): Observable<any> {
    return this.http.get<any>(`${this.getMonitoringApiUrl()}/gps/vehicles/${vehicleId}/position`, { headers: this.getHeaders() });
  }

  // ==================== REMOTE IMMOBILIZATION ====================

  getImmobilizationState(deviceId: number): Observable<any> {
    return this.http.get<any>(`${this.getMonitoringApiUrl()}/gps/devices/${deviceId}/immobilization`, { headers: this.getHeaders() });
  }

  stopVehicle(deviceId: number): Observable<any> {
    return this.http.post<any>(`${this.getMonitoringApiUrl()}/gps/devices/${deviceId}/stop`, {}, { headers: this.getHeaders() });
  }

  goVehicle(deviceId: number): Observable<any> {
    return this.http.post<any>(`${this.getMonitoringApiUrl()}/gps/devices/${deviceId}/go`, {}, { headers: this.getHeaders() });
  }

  getDeviceCommandHistory(deviceId: number, limit = 20): Observable<any[]> {
    return this.http.get<any[]>(`${this.getMonitoringApiUrl()}/gps/devices/${deviceId}/commands?limit=${limit}`, { headers: this.getHeaders() });
  }

  getVehicleHistory(vehicleId: number, from?: Date, to?: Date, maxPoints = 3000, filterDrift = true): Observable<PositionDto[]> {
    // Même correctif que getDeviceHistory ci-dessous : toLocalIso() émettait
    // l'heure locale SANS marqueur de fuseau, que le backend interprète en UTC
    // → fenêtre décalée de l'offset du navigateur (+2 h ici). Fatal pour les
    // fenêtres précises (replay d'une tournée d'1 h). ISO avec « Z » explicite.
    let params = new HttpParams();
    if (from) params = params.set('from', from.toISOString());
    if (to) params = params.set('to', to.toISOString());
    params = params.set('maxPoints', maxPoints.toString());
    if (!filterDrift) params = params.set('filterDrift', 'false');
    return this.http.get<any>(`${this.getMonitoringApiUrl()}/gps/vehicles/${vehicleId}/history`, { headers: this.getHeaders(), params }).pipe(
      map(resp => Array.isArray(resp) ? resp : resp?.positions ?? [])
    );
  }

  getDeviceHistory(deviceUid: string, from?: Date, to?: Date, maxPoints = 3000): Observable<PositionDto[]> {
    // BUGFIX: toLocalIso() emits the browser-local time WITHOUT a timezone
    // marker. The backend parses that string as UTC, so for any user not on
    // UTC the requested window was shifted by their TZ offset (e.g. Europe
    // browsers in summer asked for [+02h] off the real interval, silently
    // missing the data they wanted). Send ISO with explicit "Z" instead.
    let params = new HttpParams();
    if (from) params = params.set('from', from.toISOString());
    if (to) params = params.set('to', to.toISOString());
    params = params.set('maxPoints', maxPoints.toString());
    return this.http.get<any>(`${this.getMonitoringApiUrl()}/gps/devices/${deviceUid}/history`, { headers: this.getHeaders(), params }).pipe(
      map(resp => Array.isArray(resp) ? resp : resp?.positions ?? [])
    );
  }

  private toLocalIso(date: Date): string {
    const y = date.getFullYear();
    const m = String(date.getMonth() + 1).padStart(2, '0');
    const d = String(date.getDate()).padStart(2, '0');
    const h = String(date.getHours()).padStart(2, '0');
    const min = String(date.getMinutes()).padStart(2, '0');
    const sec = String(date.getSeconds()).padStart(2, '0');
    return `${y}-${m}-${d}T${h}:${min}:${sec}`;
  }

  getVehicleGpsStats(vehicleId: number, from?: Date, to?: Date): Observable<any> {
    let params = new HttpParams();
    if (from) params = params.set('from', this.toLocalIso(from));
    if (to) params = params.set('to', this.toLocalIso(to));
    return this.http.get<any>(`${this.getMonitoringApiUrl()}/gps/vehicles/${vehicleId}/stats`, { headers: this.getHeaders(), params });
  }

  getFleetOverview(): Observable<any> {
    return this.http.get<any>(`${this.getMonitoringApiUrl()}/gps/fleet/overview`, { headers: this.getHeaders() });
  }

  // ==================== GPS DEVICES ====================

  getGpsDevices(): Observable<any[]> {
    if (this.isMockUser()) {
      return this.mockDataService.getGPSDevices();
    }
    return this.http.get<any[]>(`${this.API_URL}/gpsdevices`, { headers: this.getHeaders() });
  }

  getUnassignedDevices(): Observable<any[]> {
    if (this.isMockUser()) {
      return this.mockDataService.getGPSDevices().pipe(
        map(devices => devices.filter(d => !d.vehicleId))
      );
    }
    return this.http.get<any[]>(`${this.API_URL}/gps/devices/available`, { headers: this.getHeaders() });
  }

  getAvailableGpsDevices(): Observable<any[]> {
    return this.http.get<any[]>(`${this.API_URL}/gps/devices/available`, { headers: this.getHeaders() });
  }

  getAllGpsDevices(): Observable<any[]> {
    return this.http.get<any[]>(`${this.API_URL}/gps/devices`, { headers: this.getHeaders() });
  }

  createGpsDevice(device: any): Observable<any> {
    if (this.isMockUser()) {
      return of(device);
    }
    return this.http.post<any>(`${this.API_URL}/gpsdevices`, device, { headers: this.getHeaders() });
  }

  assignDeviceToVehicle(deviceId: number, vehicleId: number): Observable<any> {
    if (this.isMockUser()) {
      return of({ success: true });
    }
    return this.http.post<any>(`${this.API_URL}/gpsdevices/${deviceId}/assign/${vehicleId}`, {}, { headers: this.getHeaders() });
  }

  unassignDevice(deviceId: number): Observable<any> {
    if (this.isMockUser()) {
      return of({ success: true });
    }
    return this.http.post<any>(`${this.API_URL}/gpsdevices/${deviceId}/unassign`, {}, { headers: this.getHeaders() });
  }

  getDevicePositions(deviceId: number, limit = 100): Observable<any[]> {
    if (this.isMockUser()) {
      return this.mockDataService.getGPSLocations();
    }
    const params = new HttpParams().set('limit', limit.toString());
    return this.http.get<any[]>(`${this.API_URL}/gpsdevices/${deviceId}/positions`, { headers: this.getHeaders(), params });
  }

  // ==================== ALERTS ====================

  getAlerts(resolved?: boolean, type?: string, limit = 50): Observable<any[]> {
    if (this.isMockUser()) {
      return this.mockDataService.getGPSAlerts();
    }
    let params = new HttpParams().set('limit', limit.toString());
    if (resolved !== undefined) params = params.set('resolved', resolved.toString());
    if (type) params = params.set('type', type);
    return this.http.get<any[]>(`${this.API_URL}/alerts`, { headers: this.getHeaders(), params });
  }

  getUnreadAlertCount(): Observable<number> {
    if (this.isMockUser()) {
      return this.mockDataService.getGPSAlerts().pipe(
        map(alerts => alerts.filter(a => !a.resolved).length)
      );
    }
    return this.http.get<number>(`${this.API_URL}/alerts/unread-count`, { headers: this.getHeaders() });
  }

  resolveAlert(id: number): Observable<void> {
    if (this.isMockUser()) {
      return of(void 0);
    }
    return this.http.post<void>(`${this.API_URL}/alerts/${id}/resolve`, {}, { headers: this.getHeaders() });
  }

  resolveAllAlerts(): Observable<any> {
    if (this.isMockUser()) {
      return of({ success: true });
    }
    return this.http.post<any>(`${this.API_URL}/alerts/resolve-all`, {}, { headers: this.getHeaders() });
  }

  // ==================== TRIPS ====================

  getTrips(filters?: { vehicleId?: number; driverId?: number; startDate?: Date; endDate?: Date; limit?: number }): Observable<any[]> {
    if (this.isMockUser()) {
      return of([]);
    }
    let params = new HttpParams();
    if (filters?.vehicleId) params = params.set('vehicleId', filters.vehicleId.toString());
    if (filters?.driverId) params = params.set('driverId', filters.driverId.toString());
    if (filters?.startDate) params = params.set('startDate', this.toLocalIso(filters.startDate));
    if (filters?.endDate) params = params.set('endDate', this.toLocalIso(filters.endDate));
    if (filters?.limit) params = params.set('limit', filters.limit.toString());
    return this.http.get<any[]>(`${this.API_URL}/trips`, { headers: this.getHeaders(), params });
  }

  // ==================== VEHICLE STOPS ====================

  getVehicleStops(vehicleId: number, startDate?: Date, endDate?: Date, pageSize = 500): Observable<VehicleStopsResult> {
    let params = new HttpParams()
      .set('vehicleId', vehicleId.toString())
      .set('pageSize', pageSize.toString());
    if (startDate) params = params.set('startDate', this.toLocalIso(startDate));
    if (endDate) params = params.set('endDate', this.toLocalIso(endDate));
    return this.http.get<VehicleStopsResult>(`${this.API_URL}/vehiclestops`, { headers: this.getHeaders(), params });
  }

  getTrip(id: number): Observable<any> {
    return this.http.get<any>(`${this.API_URL}/trips/${id}`, { headers: this.getHeaders() });
  }

  getVehicleTrips(vehicleId: number, startDate?: Date, endDate?: Date): Observable<any[]> {
    if (this.isMockUser()) {
      return of([]);
    }
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', this.toLocalIso(startDate));
    if (endDate) params = params.set('endDate', this.toLocalIso(endDate));
    return this.http.get<any[]>(`${this.API_URL}/trips/vehicle/${vehicleId}`, { headers: this.getHeaders(), params });
  }

  getTripWaypoints(tripId: number): Observable<any[]> {
    return this.http.get<any[]>(`${this.API_URL}/trips/${tripId}/waypoints`, { headers: this.getHeaders() });
  }

  getTripsSummary(startDate?: Date, endDate?: Date): Observable<any> {
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', this.toLocalIso(startDate));
    if (endDate) params = params.set('endDate', this.toLocalIso(endDate));
    return this.http.get<any>(`${this.API_URL}/trips/summary`, { headers: this.getHeaders(), params });
  }

  // ==================== TOURS ====================

  getTours(filters?: { status?: string; vehicleId?: number; driverId?: number; from?: string; to?: string; page?: number; pageSize?: number }): Observable<any> {
    let params = new HttpParams();
    if (filters?.status) params = params.set('status', filters.status);
    if (filters?.vehicleId) params = params.set('vehicleId', filters.vehicleId.toString());
    if (filters?.driverId) params = params.set('driverId', filters.driverId.toString());
    if (filters?.from) params = params.set('from', filters.from);
    if (filters?.to) params = params.set('to', filters.to);
    if (filters?.page) params = params.set('page', filters.page.toString());
    if (filters?.pageSize) params = params.set('pageSize', filters.pageSize.toString());
    return this.http.get<any>(`${this.API_URL}/tours`, { headers: this.getHeaders(), params });
  }

  getTour(id: number): Observable<any> {
    return this.http.get<any>(`${this.API_URL}/tours/${id}`, { headers: this.getHeaders() });
  }

  createTour(tour: any): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/tours`, tour, { headers: this.getHeaders() });
  }

  updateTour(id: number, tour: any): Observable<any> {
    return this.http.put<any>(`${this.API_URL}/tours/${id}`, tour, { headers: this.getHeaders() });
  }

  deleteTour(id: number): Observable<any> {
    return this.http.delete<any>(`${this.API_URL}/tours/${id}`, { headers: this.getHeaders() });
  }

  startTour(id: number): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/tours/${id}/start`, {}, { headers: this.getHeaders() });
  }

  completeTour(id: number, data?: any): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/tours/${id}/complete`, data || {}, { headers: this.getHeaders() });
  }

  cancelTour(id: number): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/tours/${id}/cancel`, {}, { headers: this.getHeaders() });
  }

  completeWaypoint(tourId: number, waypointId: number): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/tours/${tourId}/waypoints/${waypointId}/complete`, {}, { headers: this.getHeaders() });
  }

  addTourPause(tourId: number, data: any): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/tours/${tourId}/pauses`, data, { headers: this.getHeaders() });
  }

  endTourPause(tourId: number, pauseId: number): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/tours/${tourId}/pauses/${pauseId}/end`, {}, { headers: this.getHeaders() });
  }

  estimateRoute(data: any): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/tours/estimate`, data, { headers: this.getHeaders() });
  }

  getTourStats(): Observable<any> {
    return this.http.get<any>(`${this.API_URL}/tours/stats`, { headers: this.getHeaders() });
  }

  getTourTracking(id: number): Observable<any> {
    return this.http.get<any>(`${this.API_URL}/tours/${id}/tracking`, { headers: this.getHeaders() });
  }

  // ==================== REPORTS ====================

  getReports(limit = 50): Observable<any[]> {
    if (this.isMockUser()) {
      return of([]);
    }
    const params = new HttpParams().set('limit', limit.toString());
    return this.http.get<any[]>(`${this.API_URL}/reports`, { headers: this.getHeaders(), params });
  }

  getReport(id: number): Observable<any> {
    return this.http.get<any>(`${this.API_URL}/reports/${id}`, { headers: this.getHeaders() });
  }

  createReport(report: any): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/reports`, report, { headers: this.getHeaders() });
  }

  generateReport(id: number): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/reports/${id}/generate`, {}, { headers: this.getHeaders() });
  }

  generateFleetReport(period: string = 'month', question?: string): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/ai-chat/fleet-report`, { period, question }, { headers: this.getHeaders() });
  }

  askFleetReport(question: string, reportContext?: string): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/ai-chat/fleet-report/ask`, { question, reportContext }, { headers: this.getHeaders() });
  }

  deleteReport(id: number): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/reports/${id}`, { headers: this.getHeaders() });
  }

  getReportSchedules(): Observable<any[]> {
    return this.http.get<any[]>(`${this.API_URL}/reports/schedules`, { headers: this.getHeaders() });
  }

  createReportSchedule(schedule: any): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/reports/schedules`, schedule, { headers: this.getHeaders() });
  }

  updateReportSchedule(id: number, schedule: any): Observable<void> {
    return this.http.put<void>(`${this.API_URL}/reports/schedules/${id}`, schedule, { headers: this.getHeaders() });
  }

  deleteReportSchedule(id: number): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/reports/schedules/${id}`, { headers: this.getHeaders() });
  }

  // ==================== NOTIFICATIONS ====================

  getNotifications(isRead?: boolean, type?: string, limit = 50): Observable<any[]> {
    if (this.isMockUser()) {
      return of([]);
    }
    let params = new HttpParams().set('limit', limit.toString());
    if (isRead !== undefined) params = params.set('isRead', isRead.toString());
    if (type) params = params.set('type', type);
    return this.http.get<any[]>(`${this.API_URL}/notifications`, { headers: this.getHeaders(), params });
  }

  getUnreadNotificationCount(): Observable<number> {
    if (this.isMockUser()) {
      return of(0);
    }
    return this.http.get<number>(`${this.API_URL}/notifications/unread-count`, { headers: this.getHeaders() });
  }

  markNotificationAsRead(id: number): Observable<void> {
    return this.http.post<void>(`${this.API_URL}/notifications/${id}/read`, {}, { headers: this.getHeaders() });
  }

  markAllNotificationsAsRead(): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/notifications/read-all`, {}, { headers: this.getHeaders() });
  }

  deleteNotification(id: number): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/notifications/${id}`, { headers: this.getHeaders() });
  }

  clearNotifications(onlyRead = true): Observable<any> {
    const params = new HttpParams().set('onlyRead', onlyRead.toString());
    return this.http.delete<any>(`${this.API_URL}/notifications/clear`, { headers: this.getHeaders(), params });
  }

  // ==================== DASHBOARD ====================

  getDashboardStats(): Observable<any> {
    if (this.isMockUser()) {
      return of({
        Vehicles: { Total: 10, WithGps: 8, Online: 5, Offline: 3 },
        Drivers: { Total: 15, Active: 12 },
        Alerts: { Unresolved: 3, Today: 5 },
        Maintenance: { Upcoming: 2, Overdue: 1 },
        Costs: { ThisMonth: 5000, FuelThisMonth: 3000 },
        Trips: { Today: 12, DistanceToday: 450 },
        Geofences: { Active: 5, EventsToday: 8 }
      });
    }
    return this.http.get<any>(`${this.API_URL}/dashboard/stats`, { headers: this.getHeaders() });
  }

  getDashboardAll(period: string = 'week', from?: string, to?: string): Observable<any> {
    if (this.isMockUser()) {
      return of({
        vehicleStatus: { stopped: 0, ignitionOn: 0, moving: 0, maintenance: 0, noGps: 0 },
        expenses: { fuelCost: 0, maintenanceCost: 0, repairCost: 0, otherCost: 0, acquisitionCost: 0, totalCost: 0 },
        fuelConsumption: { vehicleStats: [], fleetTotalLiters: 0, fleetTotalKm: 0, chartDays: [], chartValues: [] },
        drivingScores: [], healthData: { healthy: 0, attention: 0, unhealthy: 0 },
        topUnits: [], geofences: [], alerts: [], recentTrips: [], drivers: []
      });
    }
    let params = new HttpParams().set('period', period);
    if (from && to) params = params.set('from', from).set('to', to);
    return this.http.get<any>(`${this.API_URL}/dashboard/all`, { headers: this.getHeaders(), params });
  }

  getDashboardCostSummary(period: string = 'month'): Observable<any> {
    if (this.isMockUser()) {
      return of({ fuelCost: 0, maintenanceCost: 0, repairCost: 0, otherCost: 0, totalCost: 0 });
    }
    const params = new HttpParams().set('period', period);
    return this.http.get<any>(`${this.API_URL}/dashboard/cost-summary`, { headers: this.getHeaders(), params });
  }

  getDashboardFuelConsumption(days: number = 30): Observable<any> {
    if (this.isMockUser()) {
      return of({ vehicleStats: [], fleetTotalLiters: 0, fleetTotalKm: 0, fleetAvgConsumption: 0, chartDays: [], chartValues: [] });
    }
    const params = new HttpParams().set('days', days.toString());
    return this.http.get<any>(`${this.API_URL}/dashboard/fuel-consumption`, { headers: this.getHeaders(), params });
  }

  getDashboardWidgetData(period: string = 'month'): Observable<any> {
    if (this.isMockUser()) {
      return of({ topFuelConsumers: [], drivingScores: [], healthyVehicles: [], unhealthyVehicles: [], immobilizedVehicles: [], immobHistory: [], trends: { mileage: 0, expenses: 0, fuel: 0 } });
    }
    const params = new HttpParams().set('period', period);
    return this.http.get<any>(`${this.API_URL}/dashboard/widget-data`, { headers: this.getHeaders(), params });
  }

  getDashboardActivity(limit = 20): Observable<any[]> {
    if (this.isMockUser()) {
      return of([]);
    }
    const params = new HttpParams().set('limit', limit.toString());
    return this.http.get<any[]>(`${this.API_URL}/dashboard/activity`, { headers: this.getHeaders(), params });
  }

  // ==================== NEW DASHBOARD API (CQRS) ====================

  /**
   * Get lightweight KPI data for quick dashboard loading
   */
  getDashboardKpis(year?: number, month?: number, vehicleIds?: number[]): Observable<DashboardKpis> {
    let params = new HttpParams();
    if (year) params = params.set('year', year.toString());
    if (month) params = params.set('month', month.toString());
    if (vehicleIds?.length) {
      vehicleIds.forEach(id => params = params.append('vehicleIds', id.toString()));
    }
    return this.http.get<DashboardKpis>(`${this.API_URL}/dashboard/kpis`, { headers: this.getHeaders(), params });
  }

  /**
   * Get chart-ready data for dashboard visualizations
   */
  getDashboardCharts(year?: number, month?: number, vehicleIds?: number[]): Observable<DashboardCharts> {
    let params = new HttpParams();
    if (year) params = params.set('year', year.toString());
    if (month) params = params.set('month', month.toString());
    if (vehicleIds?.length) {
      vehicleIds.forEach(id => params = params.append('vehicleIds', id.toString()));
    }
    return this.http.get<DashboardCharts>(`${this.API_URL}/dashboard/charts`, { headers: this.getHeaders(), params });
  }

  /**
   * Get detailed fleet statistics with pagination
   */
  getFleetStatistics(options?: {
    year?: number;
    month?: number;
    groupBy?: string;
    vehicleIds?: number[];
    pageNumber?: number;
    pageSize?: number;
  }): Observable<FleetStatistics> {
    let params = new HttpParams();
    if (options?.year) params = params.set('year', options.year.toString());
    if (options?.month) params = params.set('month', options.month.toString());
    if (options?.groupBy) params = params.set('groupBy', options.groupBy);
    if (options?.pageNumber) params = params.set('pageNumber', options.pageNumber.toString());
    if (options?.pageSize) params = params.set('pageSize', options.pageSize.toString());
    if (options?.vehicleIds?.length) {
      options.vehicleIds.forEach(id => params = params.append('vehicleIds', id.toString()));
    }
    return this.http.get<FleetStatistics>(`${this.API_URL}/dashboard/fleet-statistics`, { headers: this.getHeaders(), params });
  }

  /**
   * Refresh dashboard cache
   */
  refreshDashboardCache(): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/dashboard/refresh-cache`, {}, { headers: this.getHeaders() });
  }

  // ==================== SUBSCRIPTIONS ====================

  getSubscriptions(): Observable<any[]> {
    return this.http.get<any[]>(`${this.API_URL}/subscriptions`, { headers: this.getHeaders() });
  }

  getCurrentSubscription(): Observable<any> {
    if (this.isMockUser()) {
      return of({
        Subscription: { id: 1, name: 'Demo Plan', type: 'parc_gps', maxVehicles: 50 },
        Usage: {
          Vehicles: { Current: 10, Max: 50 },
          Users: { Current: 5, Max: 10 },
          Devices: { Current: 8, Max: 50 },
          Geofences: { Current: 5, Max: 20 }
        }
      });
    }
    return this.http.get<any>(`${this.API_URL}/subscriptions/current`, { headers: this.getHeaders() });
  }

  // ── Commandes d'abonnement (achat en libre-service, offre GPA) ──
  //
  // Le client COMMANDE ; la plateforme confirme une fois le règlement reçu hors
  // application. AUCUN montant ne part de l'écran : il est calculé côté serveur
  // depuis le plan et le cycle — un montant venu du client serait un prix
  // libre-service.

  createSubscriptionOrder(subscriptionTypeId: number, billingCycle: 'monthly' | 'quarterly' | 'semiannual' | 'yearly'): Observable<SubscriptionOrder> {
    return this.http.post<SubscriptionOrder>(`${this.API_URL}/subscriptions/orders`,
      { subscriptionTypeId, billingCycle }, { headers: this.getHeaders() });
  }

  getMySubscriptionOrders(): Observable<SubscriptionOrder[]> {
    return this.http.get<SubscriptionOrder[]>(`${this.API_URL}/subscriptions/orders/mine`, { headers: this.getHeaders() });
  }

  cancelSubscriptionOrder(orderId: number): Observable<any> {
    return this.http.delete<any>(`${this.API_URL}/subscriptions/orders/${orderId}`, { headers: this.getHeaders() });
  }

  // upgradeSubscription() supprimée en même temps que l'endpoint serveur
  // POST /api/subscriptions/upgrade : il changeait le plan et repoussait
  // l'expiration sans contrôle de rôle ni preuve de paiement. Elle n'était
  // appelée nulle part, et envoyait de toute façon `subscriptionId` là où le
  // serveur attendait `SubscriptionTypeId`. Aucun écran ne doit prolonger un
  // abonnement : cela passe par l'encaissement côté plateforme.

  // ==================== STATISTICS ====================

  getDailyStatistics(vehicleId?: number, startDate?: Date, endDate?: Date): Observable<any[]> {
    let params = new HttpParams();
    if (vehicleId) params = params.set('vehicleId', vehicleId.toString());
    if (startDate) params = params.set('startDate', this.toLocalIso(startDate));
    if (endDate) params = params.set('endDate', this.toLocalIso(endDate));
    return this.http.get<any[]>(`${this.API_URL}/statistics/daily`, { headers: this.getHeaders(), params });
  }

  getVehicleStatistics(vehicleId: number, startDate?: Date, endDate?: Date): Observable<any> {
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', this.toLocalIso(startDate));
    if (endDate) params = params.set('endDate', this.toLocalIso(endDate));
    return this.http.get<any>(`${this.API_URL}/statistics/vehicle/${vehicleId}`, { headers: this.getHeaders(), params });
  }

  getDriverScores(driverId?: number, startDate?: Date, endDate?: Date): Observable<any[]> {
    let params = new HttpParams();
    if (driverId) params = params.set('driverId', driverId.toString());
    if (startDate) params = params.set('startDate', this.toLocalIso(startDate));
    if (endDate) params = params.set('endDate', this.toLocalIso(endDate));
    return this.http.get<any[]>(`${this.API_URL}/statistics/drivers`, { headers: this.getHeaders(), params });
  }

  getDriverSummary(driverId: number, startDate?: Date, endDate?: Date): Observable<any> {
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', this.toLocalIso(startDate));
    if (endDate) params = params.set('endDate', this.toLocalIso(endDate));
    return this.http.get<any>(`${this.API_URL}/statistics/drivers/${driverId}/summary`, { headers: this.getHeaders(), params });
  }

  // ==================== USER SETTINGS ====================

  updateUserSettings(settings: any): Observable<void> {
    return this.http.put<void>(`${this.API_URL}/users/me/settings`, settings, { headers: this.getHeaders() });
  }

  getCurrentUserProfile(): Observable<any> {
    return this.http.get<any>(`${this.API_URL}/users/me`, { headers: this.getHeaders() });
  }

  updateMyProfile(payload: { firstName: string; lastName: string; email: string; phone?: string | null }): Observable<any> {
    return this.http.put<any>(`${this.API_URL}/users/me`, payload, { headers: this.getHeaders() });
  }

  // ── Import / export des données (Excel) — DataPortController ──
  exportDataset(): Observable<Blob> {
    return this.http.get(`${this.API_URL}/dataport/export`, { headers: this.getHeaders(), responseType: 'blob' });
  }
  downloadImportTemplate(): Observable<Blob> {
    return this.http.get(`${this.API_URL}/dataport/template`, { headers: this.getHeaders(), responseType: 'blob' });
  }
  importDataset(file: File): Observable<any> {
    const form = new FormData();
    form.append('file', file);
    // Pas de headers ici : l'intercepteur pose le jeton, et le navigateur pose
    // lui-même le Content-Type multipart avec le bon boundary (un Content-Type
    // manuel casserait l'upload du fichier).
    return this.http.post(`${this.API_URL}/dataport/import`, form);
  }

  changePassword(payload: { currentPassword: string; newPassword: string }): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/auth/change-password`, payload, { headers: this.getHeaders() });
  }

  /**
   * Backwards-compat alias for {@link changePassword}. Older callers (profile page,
   * Calypso I spec) call this name and hit `PUT /api/users/me/password`. Both URLs
   * are wired to the same CQRS handler on the backend.
   */
  changeMyPassword(payload: { currentPassword: string; newPassword: string }): Observable<void> {
    return this.http.put<void>(`${this.API_URL}/users/me/password`, payload, { headers: this.getHeaders() });
  }

  // ==================== FUEL RECORDS ====================

  getFuelRecords(options: {
    vehicleId?: number;
    startDate?: Date;
    endDate?: Date;
    eventType?: string;
    anomaliesOnly?: boolean;
    page?: number;
    pageSize?: number;
  } = {}): Observable<FuelRecordsResult> {
    let params = new HttpParams();
    if (options.vehicleId) params = params.set('vehicleId', options.vehicleId.toString());
    if (options.startDate) params = params.set('startDate', this.toLocalIso(options.startDate));
    if (options.endDate) params = params.set('endDate', this.toLocalIso(options.endDate));
    if (options.eventType) params = params.set('eventType', options.eventType);
    if (options.anomaliesOnly !== undefined) params = params.set('anomaliesOnly', options.anomaliesOnly.toString());
    if (options.page) params = params.set('page', options.page.toString());
    if (options.pageSize) params = params.set('pageSize', options.pageSize.toString());
    return this.http.get<FuelRecordsResult>(`${this.API_URL}/fuelrecords`, { headers: this.getHeaders(), params });
  }

  getFuelRecordsByVehicle(vehicleId: number, startDate?: Date, endDate?: Date, page = 1, pageSize = 50): Observable<FuelRecordsResult> {
    let params = new HttpParams()
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());
    if (startDate) params = params.set('startDate', this.toLocalIso(startDate));
    if (endDate) params = params.set('endDate', this.toLocalIso(endDate));
    return this.http.get<FuelRecordsResult>(`${this.API_URL}/fuelrecords/vehicle/${vehicleId}`, { headers: this.getHeaders(), params });
  }

  getFuelRefuels(vehicleId?: number, startDate?: Date, endDate?: Date): Observable<FuelRecordsResult> {
    let params = new HttpParams();
    if (vehicleId) params = params.set('vehicleId', vehicleId.toString());
    if (startDate) params = params.set('startDate', this.toLocalIso(startDate));
    if (endDate) params = params.set('endDate', this.toLocalIso(endDate));
    return this.http.get<FuelRecordsResult>(`${this.API_URL}/fuelrecords/refuels`, { headers: this.getHeaders(), params });
  }

  getFuelAnomalies(vehicleId?: number, startDate?: Date, endDate?: Date): Observable<FuelRecordsResult> {
    let params = new HttpParams();
    if (vehicleId) params = params.set('vehicleId', vehicleId.toString());
    if (startDate) params = params.set('startDate', this.toLocalIso(startDate));
    if (endDate) params = params.set('endDate', this.toLocalIso(endDate));
    return this.http.get<FuelRecordsResult>(`${this.API_URL}/fuelrecords/anomalies`, { headers: this.getHeaders(), params });
  }

  getFuelReport(vehicleId: number, startDate?: Date, endDate?: Date): Observable<FuelReport> {
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', this.toLocalIso(startDate));
    if (endDate) params = params.set('endDate', this.toLocalIso(endDate));
    return this.http.get<FuelReport>(`${this.API_URL}/fuelrecords/vehicle/${vehicleId}/report`, { headers: this.getHeaders(), params });
  }

  // ==================== DAILY ACTIVITY REPORTS ====================

  getDailyReport(vehicleId: number, date?: Date, minStopDurationSeconds?: number): Observable<DailyActivityReport> {
    let params = new HttpParams();
    if (date) params = params.set('date', this.toLocalIso(date).split('T')[0]);
    if (minStopDurationSeconds) params = params.set('minStopDurationSeconds', minStopDurationSeconds.toString());
    return this.http.get<DailyActivityReport>(`${this.API_URL}/reports/daily/${vehicleId}`, { headers: this.getHeaders(), params });
  }

  getDailyReports(date?: Date, vehicleIds?: number[], minStopDurationSeconds?: number): Observable<DailyActivityReport[]> {
    let params = new HttpParams();
    if (date) params = params.set('date', this.toLocalIso(date).split('T')[0]);
    if (vehicleIds?.length) {
      vehicleIds.forEach(id => params = params.append('vehicleIds', id.toString()));
    }
    if (minStopDurationSeconds) params = params.set('minStopDurationSeconds', minStopDurationSeconds.toString());
    return this.http.get<DailyActivityReport[]>(`${this.API_URL}/reports/daily`, { headers: this.getHeaders(), params });
  }

  /** Télécharge le rapport journalier de la flotte en PDF (même rendu que l'email), sans passer par l'email. */
  downloadDailyReportPdf(date?: Date, vehicleIds?: number[], minStopDurationSeconds?: number): Observable<Blob> {
    let params = new HttpParams();
    if (date) params = params.set('date', this.toLocalIso(date).split('T')[0]);
    if (vehicleIds?.length) {
      vehicleIds.forEach(id => params = params.append('vehicleIds', id.toString()));
    }
    if (minStopDurationSeconds) params = params.set('minStopDurationSeconds', minStopDurationSeconds.toString());
    return this.http.get(`${this.API_URL}/reports/daily-fleet-report/pdf`, { headers: this.getHeaders(), params, responseType: 'blob' });
  }

  // ==================== STOPS REPORTS ====================

  getStopsReport(vehicleId: number, startDate?: Date, endDate?: Date): Observable<any> {
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', this.toLocalIso(startDate).split('T')[0]);
    if (endDate) params = params.set('endDate', this.toLocalIso(endDate).split('T')[0]);
    return this.http.get<any>(`${this.API_URL}/reports/stops/${vehicleId}`, { headers: this.getHeaders(), params });
  }

  getStopsReportAll(startDate?: Date, endDate?: Date, vehicleIds?: number[]): Observable<any[]> {
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', this.toLocalIso(startDate).split('T')[0]);
    if (endDate) params = params.set('endDate', this.toLocalIso(endDate).split('T')[0]);
    if (vehicleIds?.length) {
      vehicleIds.forEach(id => params = params.append('vehicleIds', id.toString()));
    }
    return this.http.get<any[]>(`${this.API_URL}/reports/stops`, { headers: this.getHeaders(), params });
  }

  // ==================== TRIPS REPORTS ====================

  getTripsReport(vehicleId: number, startDate?: Date, endDate?: Date): Observable<any> {
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', this.toLocalIso(startDate).split('T')[0]);
    if (endDate) params = params.set('endDate', this.toLocalIso(endDate).split('T')[0]);
    return this.http.get<any>(`${this.API_URL}/reports/trips/${vehicleId}`, { headers: this.getHeaders(), params });
  }

  getTripsReportAll(startDate?: Date, endDate?: Date, vehicleIds?: number[]): Observable<any[]> {
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', this.toLocalIso(startDate).split('T')[0]);
    if (endDate) params = params.set('endDate', this.toLocalIso(endDate).split('T')[0]);
    if (vehicleIds?.length) {
      vehicleIds.forEach(id => params = params.append('vehicleIds', id.toString()));
    }
    return this.http.get<any[]>(`${this.API_URL}/reports/trips`, { headers: this.getHeaders(), params });
  }

  // ==================== MILEAGE REPORTS ====================

  getMileageReport(vehicleId: number, startDate?: Date, endDate?: Date): Observable<MileageReport> {
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', this.toLocalIso(startDate).split('T')[0]);
    if (endDate) params = params.set('endDate', this.toLocalIso(endDate).split('T')[0]);
    return this.http.get<MileageReport>(`${this.API_URL}/reports/mileage/${vehicleId}`, { headers: this.getHeaders(), params });
  }

  getMileageReports(startDate?: Date, endDate?: Date, vehicleIds?: number[]): Observable<MileageReport[]> {
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', this.toLocalIso(startDate).split('T')[0]);
    if (endDate) params = params.set('endDate', this.toLocalIso(endDate).split('T')[0]);
    if (vehicleIds?.length) {
      vehicleIds.forEach(id => params = params.append('vehicleIds', id.toString()));
    }
    return this.http.get<MileageReport[]>(`${this.API_URL}/reports/mileage`, { headers: this.getHeaders(), params });
  }

  // ==================== MONTHLY FLEET REPORTS ====================

  getMonthlyFleetReport(year?: number, month?: number, vehicleIds?: number[]): Observable<MonthlyFleetReport> {
    let params = new HttpParams();
    if (year) params = params.set('year', year.toString());
    if (month) params = params.set('month', month.toString());
    if (vehicleIds?.length) {
      vehicleIds.forEach(id => params = params.append('vehicleIds', id.toString()));
    }
    return this.http.get<MonthlyFleetReport>(`${this.API_URL}/reports/monthly`, { headers: this.getHeaders(), params });
  }

  // ==================== MONTHLY COST REPORT ====================

  getMonthlyCostReport(year?: number, month?: number, departmentId?: number): Observable<MonthlyCostReport> {
    let params = new HttpParams();
    if (year) params = params.set('year', year.toString());
    if (month) params = params.set('month', month.toString());
    if (departmentId) params = params.set('departmentId', departmentId.toString());
    return this.http.get<MonthlyCostReport>(`${this.API_URL}/reports/monthly-costs`, { headers: this.getHeaders(), params });
  }

  // ==================== MILEAGE PERIOD REPORTS (Hour/Day/Month) ====================

  getMileagePeriodReport(
    vehicleId: number, 
    periodType: 'hour' | 'day' | 'month' = 'day',
    startDate?: Date, 
    endDate?: Date
  ): Observable<MileagePeriodReport> {
    let params = new HttpParams().set('periodType', periodType);
    if (startDate) params = params.set('startDate', this.toLocalIso(startDate).split('T')[0]);
    if (endDate) params = params.set('endDate', this.toLocalIso(endDate).split('T')[0]);
    return this.http.get<MileagePeriodReport>(`${this.API_URL}/reports/mileage-period/${vehicleId}`, { headers: this.getHeaders(), params });
  }

  // ==================== SUPPLIERS / GARAGES ====================

  getSuppliers(options?: { searchTerm?: string; type?: string; isActive?: boolean; page?: number; pageSize?: number }): Observable<PaginatedResult<SupplierDto>> {
    let params = new HttpParams();
    if (options?.searchTerm) params = params.set('searchTerm', options.searchTerm);
    if (options?.type) params = params.set('type', options.type);
    if (options?.isActive !== undefined) params = params.set('isActive', options.isActive.toString());
    if (options?.page) params = params.set('page', options.page.toString());
    if (options?.pageSize) params = params.set('pageSize', options.pageSize.toString());
    return this.http.get<PaginatedResult<SupplierDto>>(`${this.API_URL}/suppliers`, { headers: this.getHeaders(), params });
  }

  getSupplier(id: number): Observable<SupplierDto> {
    return this.http.get<SupplierDto>(`${this.API_URL}/suppliers/${id}`, { headers: this.getHeaders() });
  }

  getSupplierStats(): Observable<SupplierStatsDto> {
    return this.http.get<SupplierStatsDto>(`${this.API_URL}/suppliers/stats`, { headers: this.getHeaders() });
  }

  getGarages(options?: { searchTerm?: string; isActive?: boolean; page?: number; pageSize?: number }): Observable<PaginatedResult<SupplierDto>> {
    let params = new HttpParams();
    if (options?.searchTerm) params = params.set('searchTerm', options.searchTerm);
    if (options?.isActive !== undefined) params = params.set('isActive', options.isActive.toString());
    if (options?.page) params = params.set('page', options.page.toString());
    if (options?.pageSize) params = params.set('pageSize', options.pageSize.toString());
    return this.http.get<PaginatedResult<SupplierDto>>(`${this.API_URL}/suppliers/garages`, { headers: this.getHeaders(), params });
  }

  createSupplier(supplier: CreateSupplierRequest): Observable<number> {
    return this.http.post<number>(`${this.API_URL}/suppliers`, supplier, { headers: this.getHeaders() });
  }

  updateSupplier(id: number, supplier: UpdateSupplierRequest): Observable<void> {
    return this.http.put<void>(`${this.API_URL}/suppliers/${id}`, supplier, { headers: this.getHeaders() });
  }

  deleteSupplier(id: number): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/suppliers/${id}`, { headers: this.getHeaders() });
  }

  getSupplierServices(id: number): Observable<string[]> {
    return this.http.get<string[]>(`${this.API_URL}/suppliers/${id}/services`, { headers: this.getHeaders() });
  }

  updateSupplierServices(id: number, services: string[]): Observable<void> {
    return this.http.put<void>(`${this.API_URL}/suppliers/${id}/services`, { services }, { headers: this.getHeaders() });
  }

  // ==================== CONTRACTS ====================

  getContracts(options?: { vehicleId?: number; type?: string; status?: string; page?: number; pageSize?: number }): Observable<any> {
    let params = new HttpParams();
    if (options?.vehicleId) params = params.set('vehicleId', options.vehicleId.toString());
    if (options?.type) params = params.set('type', options.type);
    if (options?.status) params = params.set('status', options.status);
    if (options?.page) params = params.set('page', options.page.toString());
    if (options?.pageSize) params = params.set('pageSize', options.pageSize.toString());
    return this.http.get<any>(`${this.API_URL}/contracts`, { headers: this.getHeaders(), params });
  }

  // ==================== DOCUMENTS / EXPIRIES ====================

  getDocumentExpiries(options?: { documentType?: string; status?: string; vehicleId?: number; page?: number; pageSize?: number }): Observable<PaginatedResult<VehicleExpiryDto>> {
    let params = new HttpParams();
    if (options?.documentType) params = params.set('documentType', options.documentType);
    if (options?.status) params = params.set('status', options.status);
    if (options?.vehicleId) params = params.set('vehicleId', options.vehicleId.toString());
    if (options?.page) params = params.set('page', options.page.toString());
    if (options?.pageSize) params = params.set('pageSize', options.pageSize.toString());
    return this.http.get<PaginatedResult<VehicleExpiryDto>>(`${this.API_URL}/documents/expiries`, { headers: this.getHeaders(), params });
  }

  getExpiryStats(): Observable<ExpiryStatsDto> {
    return this.http.get<ExpiryStatsDto>(`${this.API_URL}/documents/expiries/stats`, { headers: this.getHeaders() });
  }

  getVehicleExpiries(vehicleId: number): Observable<VehicleExpiryDto[]> {
    return this.http.get<VehicleExpiryDto[]>(`${this.API_URL}/documents/vehicle/${vehicleId}/expiries`, { headers: this.getHeaders() });
  }

  renewDocument(vehicleId: number, request: RenewDocumentRequest): Observable<{ costId: number; message: string }> {
    return this.http.post<{ costId: number; message: string }>(`${this.API_URL}/documents/vehicle/${vehicleId}/renew`, request, { headers: this.getHeaders() });
  }

  // Corriger la date d'une échéance (bouton « Modifier ») sans renouvellement ni dépense.
  updateDocumentExpiry(vehicleId: number, documentType: string, expiryDate: string): Observable<any> {
    return this.http.put(`${this.API_URL}/documents/vehicle/${vehicleId}/expiry`, { documentType, expiryDate }, { headers: this.getHeaders() });
  }

  getRenewalHistory(vehicleId: number): Observable<RenewalHistoryDto[]> {
    return this.http.get<RenewalHistoryDto[]>(`${this.API_URL}/documents/vehicle/${vehicleId}/history`, { headers: this.getHeaders() });
  }

  getExpiryAlerts(daysThreshold = 30): Observable<VehicleExpiryDto[]> {
    const params = new HttpParams().set('daysThreshold', daysThreshold.toString());
    return this.http.get<VehicleExpiryDto[]>(`${this.API_URL}/documents/alerts`, { headers: this.getHeaders(), params });
  }

  // ==================== ACCIDENT REPORTS ====================
  //
  // Calypso 7 — single unified accident timeline. The old AccidentClaim
  // methods were removed; the AccidentEvent endpoints below cover the
  // full lifecycle (detection → confirmation → expert → mechanic →
  // repair → insurance) with phase-specific PATCH endpoints.

  getAccidentReport(id: number): Observable<AccidentReportDto> {
    return this.http.get<AccidentReportDto>(`${this.API_URL}/accident-reports/${id}`, { headers: this.getHeaders() });
  }

  /**
   * Admin click-through from the accident decision modal (or the
   * /rapport-accident fallback button). Idempotent — a repeat call on an
   * already-decided event resolves silently.
   */
  confirmAccident(id: number): Observable<void> {
    return this.http.post<void>(`${this.API_URL}/accident-reports/${id}/confirm`, null, { headers: this.getHeaders() });
  }

  /**
   * Admin "Fausse alerte" click — flips the event to dismissed and lets
   * the backend broadcast the "Choc violent — dégâts possibles" admin
   * notification.
   */
  dismissAccident(id: number): Observable<void> {
    return this.http.post<void>(`${this.API_URL}/accident-reports/${id}/dismiss`, null, { headers: this.getHeaders() });
  }

  /**
   * Debug-only: inserts a pending accident row in the caller's company and
   * sends the blocking-modal notification ONLY to user id=1. Server returns
   * 403 for anyone else. Used by the hidden "Tester accident" button in the
   * header to walk the decision flow on prod without waiting for a real crash.
   */
  simulateAccident(): Observable<{ accidentEventId: number }> {
    return this.http.post<{ accidentEventId: number }>(`${this.API_URL}/accident-reports/simulate`, null, { headers: this.getHeaders() });
  }

  /**
   * Calypso 6 (P9) — paged list of accident events for the /accident-reports
   * admin page. Excludes dismissed rows by default.
   */
  listAccidentEvents(options?: {
    page?: number;
    pageSize?: number;
    status?: string;
    vehicleId?: number;
    includeDismissed?: boolean;
  }): Observable<ListAccidentEventsResult> {
    let params = new HttpParams();
    if (options?.page) params = params.set('page', options.page.toString());
    if (options?.pageSize) params = params.set('pageSize', options.pageSize.toString());
    if (options?.status) params = params.set('status', options.status);
    if (options?.vehicleId) params = params.set('vehicleId', options.vehicleId.toString());
    if (options?.includeDismissed) params = params.set('includeDismissed', 'true');
    return this.http.get<ListAccidentEventsResult>(`${this.API_URL}/accident-reports`, { headers: this.getHeaders(), params });
  }

  // ── Standalone tow detection (/remorquages) ──────────────────────────────

  /** Paged list of detected tows (engine-off + speed + displacement). */
  getTowEvents(options?: {
    page?: number;
    pageSize?: number;
    status?: 'active' | 'ended';
    vehicleId?: number;
    acknowledged?: boolean;
  }): Observable<TowEventsResult> {
    let params = new HttpParams();
    if (options?.page) params = params.set('page', options.page.toString());
    if (options?.pageSize) params = params.set('pageSize', options.pageSize.toString());
    if (options?.status) params = params.set('status', options.status);
    if (options?.vehicleId) params = params.set('vehicleId', options.vehicleId.toString());
    if (options?.acknowledged !== undefined) params = params.set('acknowledged', String(options.acknowledged));
    return this.http.get<TowEventsResult>(`${this.API_URL}/towing`, { headers: this.getHeaders(), params });
  }

  /** Number of unacknowledged tows (nav badge). */
  getTowUnacknowledgedCount(): Observable<{ count: number }> {
    return this.http.get<{ count: number }>(`${this.API_URL}/towing/unacknowledged-count`, { headers: this.getHeaders() });
  }

  /** Mark a tow event as reviewed. */
  acknowledgeTowEvent(id: number): Observable<void> {
    return this.http.post<void>(`${this.API_URL}/towing/${id}/acknowledge`, null, { headers: this.getHeaders() });
  }

  // ── Calypso 7 — phase commands ────────────────────────────────────────
  //
  // Each phase is editable independently and over multiple days. The
  // backend only accepts these on a row whose status is 'confirmed'.

  /** Phase 2 — initial visible damages (description, severity, zones, weather, …). */
  updateAccidentInitialDamages(id: number, payload: UpdateInitialDamagesRequest): Observable<void> {
    return this.http.patch<void>(`${this.API_URL}/accident-reports/${id}/initial-damages`, payload, { headers: this.getHeaders() });
  }

  /** Phase 3 — insurance expert visit: name, company, assessment, estimated amount. */
  registerAccidentExpertAssessment(id: number, payload: RegisterExpertAssessmentRequest): Observable<void> {
    return this.http.patch<void>(`${this.API_URL}/accident-reports/${id}/expert`, payload, { headers: this.getHeaders() });
  }

  /** Phase 4 — mechanic / garage quote. */
  registerAccidentMechanicQuote(id: number, payload: RegisterMechanicQuoteRequest): Observable<void> {
    return this.http.patch<void>(`${this.API_URL}/accident-reports/${id}/mechanic-quote`, payload, { headers: this.getHeaders() });
  }

  /** Phase 5 — repair tracking. Server auto-creates a VehicleCost row. */
  registerAccidentRepair(id: number, payload: RegisterRepairRequest): Observable<void> {
    return this.http.patch<void>(`${this.API_URL}/accident-reports/${id}/repair`, payload, { headers: this.getHeaders() });
  }

  /** Phase 6 — insurance claim follow-up. Server auto-creates a refund VehicleCost row. */
  registerAccidentClaim(id: number, payload: RegisterClaimRequest): Observable<void> {
    return this.http.patch<void>(`${this.API_URL}/accident-reports/${id}/claim`, payload, { headers: this.getHeaders() });
  }

  /** Add a third party (other vehicle / driver involved). */
  addAccidentThirdParty(id: number, payload: AddThirdPartyRequest): Observable<{ thirdPartyId: number }> {
    return this.http.post<{ thirdPartyId: number }>(`${this.API_URL}/accident-reports/${id}/third-parties`, payload, { headers: this.getHeaders() });
  }

  /** Remove a third party. */
  deleteAccidentThirdParty(id: number, thirdPartyId: number): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/accident-reports/${id}/third-parties/${thirdPartyId}`, { headers: this.getHeaders() });
  }

  /**
   * Multi-file upload typed by phase (expert_report, mechanic_quote,
   * repair_invoice, insurance_response, police_report, photo, other).
   */
  uploadAccidentDocument(id: number, file: File, documentType: string): Observable<{ documentId: number; fileUrl: string }> {
    const formData = new FormData();
    formData.append('file', file, file.name);
    formData.append('documentType', documentType);
    const headers = this.getHeaders().delete('Content-Type');
    return this.http.post<{ documentId: number; fileUrl: string }>(`${this.API_URL}/accident-reports/${id}/documents`, formData, { headers });
  }

  /** Remove a previously-uploaded accident document. */
  deleteAccidentDocument(id: number, documentId: number): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/accident-reports/${id}/documents/${documentId}`, { headers: this.getHeaders() });
  }

  /**
   * Calypso 6 (P9) — uploads the PDF report for an accident event.
   * Used both for the auto-generated jsPDF blob (right after Confirm in
   * the modal) and for an externally-supplied PDF (insurance expert).
   * Multipart/form-data with field name "file".
   */
  uploadAccidentReportPdf(id: number, file: File | Blob, fileName?: string): Observable<{ pdfReportUrl: string }> {
    const formData = new FormData();
    formData.append('file', file, fileName ?? 'accident-report.pdf');
    // NB: do NOT set Content-Type manually — Angular sets the multipart
    // boundary automatically. We just need the auth token.
    const headers = this.getHeaders().delete('Content-Type');
    return this.http.post<{ pdfReportUrl: string }>(`${this.API_URL}/accident-reports/${id}/upload-pdf`, formData, { headers });
  }

  /**
   * Calypso 6 (P9) — creates an accident the system did not auto-detect.
   * The admin fills the form (vehicle, date, optional location, damages)
   * and the row lands as status='confirmed'. The caller can then attach
   * an external PDF via uploadAccidentReportPdf().
   */
  createManualAccident(payload: CreateManualAccidentRequest): Observable<{ accidentEventId: number }> {
    return this.http.post<{ accidentEventId: number }>(`${this.API_URL}/accident-reports/manual`, payload, { headers: this.getHeaders() });
  }

  // ==================== MAINTENANCE TEMPLATES ====================

  getMaintenanceTemplates(options?: { category?: string; isActive?: boolean; page?: number; pageSize?: number }): Observable<PaginatedResult<MaintenanceTemplateDto>> {
    let params = new HttpParams();
    if (options?.category) params = params.set('category', options.category);
    if (options?.isActive !== undefined) params = params.set('isActive', options.isActive.toString());
    if (options?.page) params = params.set('page', options.page.toString());
    if (options?.pageSize) params = params.set('pageSize', options.pageSize.toString());
    return this.http.get<PaginatedResult<MaintenanceTemplateDto>>(`${this.API_URL}/maintenance-templates`, { headers: this.getHeaders(), params });
  }

  getMaintenanceTemplate(id: number): Observable<MaintenanceTemplateDto> {
    return this.http.get<MaintenanceTemplateDto>(`${this.API_URL}/maintenance-templates/${id}`, { headers: this.getHeaders() });
  }

  getMaintenanceCategories(): Observable<string[]> {
    return this.http.get<string[]>(`${this.API_URL}/maintenance-templates/categories`, { headers: this.getHeaders() });
  }

  createMaintenanceTemplate(template: CreateMaintenanceTemplateRequest): Observable<number> {
    return this.http.post<number>(`${this.API_URL}/maintenance-templates`, template, { headers: this.getHeaders() });
  }

  updateMaintenanceTemplate(id: number, template: UpdateMaintenanceTemplateRequest): Observable<void> {
    return this.http.put<void>(`${this.API_URL}/maintenance-templates/${id}`, template, { headers: this.getHeaders() });
  }

  deleteMaintenanceTemplate(id: number): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/maintenance-templates/${id}`, { headers: this.getHeaders() });
  }

  // ==================== VEHICLE MAINTENANCE SCHEDULE ====================

  getVehicleMaintenanceSchedule(options?: { status?: string; page?: number; pageSize?: number }): Observable<PaginatedResult<VehicleMaintenanceStatusDto>> {
    let params = new HttpParams();
    if (options?.status) params = params.set('status', options.status);
    if (options?.page) params = params.set('page', options.page.toString());
    if (options?.pageSize) params = params.set('pageSize', options.pageSize.toString());
    return this.http.get<PaginatedResult<VehicleMaintenanceStatusDto>>(`${this.API_URL}/vehicle-maintenance`, { headers: this.getHeaders(), params });
  }

  getVehicleMaintenanceStatus(vehicleId: number): Observable<VehicleMaintenanceStatusDto> {
    return this.http.get<VehicleMaintenanceStatusDto>(`${this.API_URL}/vehicle-maintenance/vehicle/${vehicleId}`, { headers: this.getHeaders() });
  }

  getMaintenanceAlerts(): Observable<MaintenanceItemDto[]> {
    return this.http.get<MaintenanceItemDto[]>(`${this.API_URL}/vehicle-maintenance/alerts`, { headers: this.getHeaders() });
  }

  getMaintenanceStats(): Observable<MaintenanceStatsDto> {
    return this.http.get<MaintenanceStatsDto>(`${this.API_URL}/vehicle-maintenance/stats`, { headers: this.getHeaders() });
  }

  assignMaintenanceTemplate(vehicleId: number, templateId: number): Observable<{ scheduleId: number }> {
    return this.http.post<{ scheduleId: number }>(`${this.API_URL}/vehicle-maintenance/assign`, { vehicleId, templateId }, { headers: this.getHeaders() });
  }

  removeMaintenanceSchedule(scheduleId: number): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/vehicle-maintenance/${scheduleId}`, { headers: this.getHeaders() });
  }

  /**
   * Calypso 7 (P-maint-ter): force a schedule to re-snap its NextDueKm
   * against the current mileage (GPS odometer → manual → trips fallback).
   * Used to repair schedules created on a vehicle whose tracker had no
   * FMS odometer wired at assignment time — the original NextDueKm was
   * anchored on vehicle.Mileage = 0 and would never trigger correctly.
   */
  rebaseMaintenanceSchedule(scheduleId: number): Observable<{ success: boolean }> {
    return this.http.post<{ success: boolean }>(
      `${this.API_URL}/vehicle-maintenance/${scheduleId}/rebase`, {},
      { headers: this.getHeaders() }
    );
  }

  markMaintenanceDone(request: MarkMaintenanceDoneRequest): Observable<{ logId: number; message: string }> {
    return this.http.post<{ logId: number; message: string }>(`${this.API_URL}/vehicle-maintenance/mark-done`, request, { headers: this.getHeaders() });
  }

  getMaintenanceLogs(vehicleId: number, templateId?: number): Observable<any[]> {
    let params = new HttpParams();
    if (templateId) params = params.set('templateId', templateId.toString());
    return this.http.get<any[]>(`${this.API_URL}/vehicle-maintenance/vehicle/${vehicleId}/logs`, { headers: this.getHeaders(), params });
  }

  // ==================== FREE MAINTENANCE BENEFITS ====================

  declareFreeMaintenances(request: DeclareFreeMaintenancesRequest): Observable<{ scheduleId: number }> {
    return this.http.post<{ scheduleId: number }>(
      `${this.API_URL}/vehicle-maintenance/declare-free`,
      request,
      { headers: this.getHeaders() }
    );
  }

  updateFreeMaintenance(scheduleId: number, request: UpdateFreeMaintenanceRequest): Observable<void> {
    return this.http.put<void>(
      `${this.API_URL}/vehicle-maintenance/${scheduleId}/free`,
      request,
      { headers: this.getHeaders() }
    );
  }

  clearFreeMaintenance(scheduleId: number): Observable<void> {
    return this.http.delete<void>(
      `${this.API_URL}/vehicle-maintenance/${scheduleId}/free`,
      { headers: this.getHeaders() }
    );
  }

  // ==================== REPAIRS ====================

  getRepairs(options?: { vehicleId?: number; status?: string; fromDate?: string; toDate?: string; page?: number; pageSize?: number }): Observable<RepairsListResult> {
    let params = new HttpParams();
    if (options?.vehicleId) params = params.set('vehicleId', options.vehicleId.toString());
    if (options?.status) params = params.set('status', options.status);
    if (options?.fromDate) params = params.set('fromDate', options.fromDate);
    if (options?.toDate) params = params.set('toDate', options.toDate);
    if (options?.page) params = params.set('page', options.page.toString());
    if (options?.pageSize) params = params.set('pageSize', options.pageSize.toString());
    return this.http.get<RepairsListResult>(`${this.API_URL}/repairs`, { headers: this.getHeaders(), params });
  }

  getRepair(id: number): Observable<RepairDto> {
    return this.http.get<RepairDto>(`${this.API_URL}/repairs/${id}`, { headers: this.getHeaders() });
  }

  getRepairStats(vehicleId?: number, fromDate?: string, toDate?: string): Observable<RepairStatsDto> {
    let params = new HttpParams();
    if (vehicleId) params = params.set('vehicleId', vehicleId.toString());
    if (fromDate) params = params.set('fromDate', fromDate);
    if (toDate) params = params.set('toDate', toDate);
    return this.http.get<RepairStatsDto>(`${this.API_URL}/repairs/stats`, { headers: this.getHeaders(), params });
  }

  createRepair(repair: CreateRepairRequest): Observable<{ id: number }> {
    return this.http.post<{ id: number }>(`${this.API_URL}/repairs`, repair, { headers: this.getHeaders() });
  }

  updateRepair(id: number, repair: UpdateRepairRequest): Observable<void> {
    return this.http.put<void>(`${this.API_URL}/repairs/${id}`, repair, { headers: this.getHeaders() });
  }

  deleteRepair(id: number): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/repairs/${id}`, { headers: this.getHeaders() });
  }

  updateRepairStatus(id: number, status: string): Observable<void> {
    return this.http.patch<void>(`${this.API_URL}/repairs/${id}/status`, { status }, { headers: this.getHeaders() });
  }

  // ==================== FUEL EXPENSES ====================

  getFuelExpenseStatistics(startDate?: string, endDate?: string, vehicleId?: number, fuelType?: string): Observable<FleetFuelStatisticsDto> {
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', startDate);
    if (endDate) params = params.set('endDate', endDate);
    if (vehicleId) params = params.set('vehicleId', vehicleId.toString());
    if (fuelType) params = params.set('fuelType', fuelType);
    return this.http.get<FleetFuelStatisticsDto>(`${this.API_URL}/fuelexpenses/statistics`, { headers: this.getHeaders(), params });
  }

  getVehicleFuelExpense(vehicleId: number, startDate?: string, endDate?: string): Observable<VehicleFuelExpenseDto> {
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', startDate);
    if (endDate) params = params.set('endDate', endDate);
    return this.http.get<VehicleFuelExpenseDto>(`${this.API_URL}/fuelexpenses/vehicle/${vehicleId}`, { headers: this.getHeaders(), params });
  }

  getCurrentFuelPrices(): Observable<FuelPriceDto[]> {
    return this.http.get<FuelPriceDto[]>(`${this.API_URL}/fuelexpenses/prices`, { headers: this.getHeaders() });
  }

  /**
   * GPS-independent fuel consumption ("Carburant réel"): L/100km, coût/km and
   * distance from manually entered fill-ups (full-to-full). Works for vehicles
   * WITHOUT a GPS box — for the "Gestion sans GPS" clients.
   */
  getRealFuelConsumption(startDate?: string, endDate?: string, vehicleId?: number): Observable<any> {
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', startDate);
    if (endDate) params = params.set('endDate', endDate);
    if (vehicleId) params = params.set('vehicleId', vehicleId.toString());
    return this.http.get<any>(`${this.API_URL}/fuelexpenses/real-consumption`, { headers: this.getHeaders(), params });
  }

  // Anti-fraud: real fuel (card-billed) vs GPS-consumed comparison
  getFuelComparisonReport(startDate?: string, endDate?: string, vehicleId?: number): Observable<FuelComparisonReport> {
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', startDate);
    if (endDate) params = params.set('endDate', endDate);
    if (vehicleId) params = params.set('vehicleId', vehicleId.toString());
    return this.http.get<FuelComparisonReport>(`${this.API_URL}/fuelexpenses/comparison`, { headers: this.getHeaders(), params });
  }

  // Per-vehicle fuel audit: level curve + per-fill verification (card fills vs GPS-detected refills)
  getFuelAuditReport(vehicleId: number, startDate?: string, endDate?: string): Observable<FuelAuditReport> {
    let params = new HttpParams();
    params = params.set('vehicleId', vehicleId.toString());
    if (startDate) params = params.set('startDate', startDate);
    if (endDate) params = params.set('endDate', endDate);
    return this.http.get<FuelAuditReport>(`${this.API_URL}/fuelexpenses/vehicle-audit`, { headers: this.getHeaders(), params });
  }

  // ==================== CONSUMPTION ANALYSIS (segments de X km + tonnage) ====================

  /** Consumption per X-km segment (min/max, reliability) for a single vehicle. */
  getConsumptionSegments(vehicleId: number, startDate?: string, endDate?: string, segmentKm: number = 100): Observable<ConsumptionSegmentsReport> {
    let params = new HttpParams();
    params = params.set('vehicleId', vehicleId.toString());
    if (startDate) params = params.set('startDate', startDate);
    if (endDate) params = params.set('endDate', endDate);
    params = params.set('segmentKm', segmentKm.toString());
    return this.http.get<ConsumptionSegmentsReport>(`${this.API_URL}/consumption-analysis/segments`, { headers: this.getHeaders(), params });
  }

  /** Consommation mesurée (jauge) vs réelle (factures, méthode plein à plein) par intervalle entre deux pleins consécutifs. */
  getConsumptionComparison(vehicleId: number, startDate?: string, endDate?: string): Observable<FuelConsumptionComparisonReport> {
    let params = new HttpParams();
    params = params.set('vehicleId', vehicleId.toString());
    if (startDate) params = params.set('startDate', startDate);
    if (endDate) params = params.set('endDate', endDate);
    return this.http.get<FuelConsumptionComparisonReport>(`${this.API_URL}/consumption-analysis/consumption-comparison`, { headers: this.getHeaders(), params });
  }

  /** Consumption grouped by declared tonnage (segments inherit the load periods). */
  getConsumptionByTonnage(vehicleId: number, startDate?: string, endDate?: string, segmentKm: number = 100): Observable<ConsumptionByTonnageReport> {
    let params = new HttpParams();
    params = params.set('vehicleId', vehicleId.toString());
    if (startDate) params = params.set('startDate', startDate);
    if (endDate) params = params.set('endDate', endDate);
    params = params.set('segmentKm', segmentKm.toString());
    return this.http.get<ConsumptionByTonnageReport>(`${this.API_URL}/consumption-analysis/by-tonnage`, { headers: this.getHeaders(), params });
  }

  /** Declared load (tonnage) periods of a vehicle. */
  getVehicleLoadPeriods(vehicleId: number): Observable<VehicleLoadPeriod[]> {
    let params = new HttpParams();
    params = params.set('vehicleId', vehicleId.toString());
    return this.http.get<VehicleLoadPeriod[]>(`${this.API_URL}/consumption-analysis/load-periods`, { headers: this.getHeaders(), params });
  }

  createVehicleLoadPeriod(body: { vehicleId: number; startTime: string; endTime: string | null; tonnageT: number; notes: string | null }): Observable<number> {
    return this.http.post<number>(`${this.API_URL}/consumption-analysis/load-periods`, body, { headers: this.getHeaders() });
  }

  updateVehicleLoadPeriod(id: number, body: { id: number; startTime: string; endTime: string | null; tonnageT: number; notes: string | null }): Observable<any> {
    return this.http.put<any>(`${this.API_URL}/consumption-analysis/load-periods/${id}`, body, { headers: this.getHeaders() });
  }

  deleteVehicleLoadPeriod(id: number): Observable<any> {
    return this.http.delete<any>(`${this.API_URL}/consumption-analysis/load-periods/${id}`, { headers: this.getHeaders() });
  }

  /** AI explanation of one consumption segment (Groq) — server caches 15 min. */
  explainConsumptionSegment(body: {
    vehicleId: number; startTime: string; endTime: string;
    distanceKm: number; fuelLiters: number; lPer100Km: number;
    tonnageT: number | null; isReliable: boolean; exclusionReason: string | null;
    segmentKm: number; periodAvgLPer100Km: number | null;
    periodMinLPer100Km: number | null; periodMaxLPer100Km: number | null;
  }): Observable<ExplainSegmentResult> {
    return this.http.post<ExplainSegmentResult>(`${this.API_URL}/consumption-analysis/explain-segment`, body, { headers: this.getHeaders() });
  }

  /** Send a one-off preview of the daily fleet report to the current user (test, no 06:00 wait). */
  sendDailyReportTest(): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/reports/daily-fleet-report/test`, {}, { headers: this.getHeaders() });
  }

  /** Admin-only (admin@belive.tn): send the daily fleet report to the fixed test address karim.hajjami@gmail.com. */
  sendDailyReportToOwner(): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/reports/daily-fleet-report/test-to-owner`, {}, { headers: this.getHeaders() });
  }

  /** Enable/disable the daily fleet report email for the current user (persisted server-side). */
  setDailyReportEmailPref(enabled: boolean): Observable<any> {
    return this.http.put<any>(`${this.API_URL}/reports/daily-fleet-report/preference`, { enabled }, { headers: this.getHeaders() });
  }

  // ==================== FUEL PRICES MANAGEMENT ====================

  getFuelPrices(options: { fuelTypeId?: number; isActive?: boolean; page?: number; pageSize?: number } = {}): Observable<PaginatedFuelPricesResult> {
    let params = new HttpParams();
    if (options.fuelTypeId) params = params.set('fuelTypeId', options.fuelTypeId.toString());
    if (options.isActive !== undefined) params = params.set('isActive', options.isActive.toString());
    if (options.page) params = params.set('page', options.page.toString());
    if (options.pageSize) params = params.set('pageSize', options.pageSize.toString());
    return this.http.get<PaginatedFuelPricesResult>(`${this.API_URL}/fuelprices`, { headers: this.getHeaders(), params });
  }

  getCurrentActiveFuelPrices(): Observable<FuelPriceFullDto[]> {
    return this.http.get<FuelPriceFullDto[]>(`${this.API_URL}/fuelprices/current`, { headers: this.getHeaders() });
  }

  getFuelTypes(): Observable<FuelTypeDto[]> {
    return this.http.get<FuelTypeDto[]>(`${this.API_URL}/fuelprices/types`, { headers: this.getHeaders() });
  }

  createFuelPrice(request: CreateFuelPriceRequest): Observable<{ id: number }> {
    return this.http.post<{ id: number }>(`${this.API_URL}/fuelprices`, request, { headers: this.getHeaders() });
  }

  updateFuelPrice(id: number, request: UpdateFuelPriceRequest): Observable<void> {
    return this.http.put<void>(`${this.API_URL}/fuelprices/${id}`, request, { headers: this.getHeaders() });
  }

  deleteFuelPrice(id: number): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/fuelprices/${id}`, { headers: this.getHeaders() });
  }

  importFuelPricesFromExcel(file: File): Observable<FuelPriceImportResult> {
    const formData = new FormData();
    formData.append('file', file);
    const token = localStorage.getItem('auth_token');
    return this.http.post<FuelPriceImportResult>(`${this.API_URL}/fuelprices/import`, formData, { 
      headers: new HttpHeaders({ 'Authorization': `Bearer ${token}` })
    });
  }

  downloadFuelPriceTemplate(): Observable<Blob> {
    return this.http.get(`${this.API_URL}/fuelprices/import/template`, { 
      headers: this.getHeaders(), 
      responseType: 'blob' 
    });
  }

  // ==================== FUEL ENTRIES MANAGEMENT ====================

  getFuelEntries(options: { fuelTypeId?: number; vehiclePlate?: string; vehicleId?: number; startDate?: string; endDate?: string; page?: number; pageSize?: number } = {}): Observable<PaginatedFuelEntriesResult> {
    let params = new HttpParams();
    if (options.fuelTypeId) params = params.set('fuelTypeId', options.fuelTypeId.toString());
    if (options.vehiclePlate) params = params.set('vehiclePlate', options.vehiclePlate);
    if (options.vehicleId) params = params.set('vehicleId', options.vehicleId.toString());
    if (options.startDate) params = params.set('startDate', options.startDate);
    if (options.endDate) params = params.set('endDate', options.endDate);
    if (options.page) params = params.set('page', options.page.toString());
    if (options.pageSize) params = params.set('pageSize', options.pageSize.toString());
    return this.http.get<PaginatedFuelEntriesResult>(`${this.API_URL}/fuelentries`, { headers: this.getHeaders(), params });
  }

  createFuelEntry(request: CreateFuelEntryRequest): Observable<{ id: number }> {
    return this.http.post<{ id: number }>(`${this.API_URL}/fuelentries`, request, { headers: this.getHeaders() });
  }

  bulkCreateFuelEntries(requests: CreateFuelEntryRequest[]): Observable<{ total: number; success: number; failed: number; results: any[] }> {
    return this.http.post<any>(`${this.API_URL}/fuelentries/bulk`, requests, { headers: this.getHeaders() });
  }

  deleteFuelEntry(id: number): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/fuelentries/${id}`, { headers: this.getHeaders() });
  }

  // ==================== DEVICE EVENTS (SECURITY) ====================

  getDeviceEvents(params: { eventType?: string; vehicleId?: number; from?: string; to?: string; acknowledged?: boolean; page?: number; pageSize?: number }): Observable<DeviceEventsResult> {
    let queryParams = new HttpParams();
    if (params.eventType) queryParams = queryParams.set('eventType', params.eventType);
    if (params.vehicleId) queryParams = queryParams.set('vehicleId', params.vehicleId.toString());
    if (params.from) queryParams = queryParams.set('from', params.from);
    if (params.to) queryParams = queryParams.set('to', params.to);
    if (params.acknowledged !== undefined) queryParams = queryParams.set('acknowledged', params.acknowledged.toString());
    if (params.page) queryParams = queryParams.set('page', params.page.toString());
    if (params.pageSize) queryParams = queryParams.set('pageSize', params.pageSize.toString());
    return this.http.get<DeviceEventsResult>(`${this.API_URL}/device-events`, { headers: this.getHeaders(), params: queryParams });
  }

  acknowledgeDeviceEvent(id: number): Observable<void> {
    return this.http.post<void>(`${this.API_URL}/device-events/${id}/acknowledge`, {}, { headers: this.getHeaders() });
  }

  // ==================== PARTS CATALOG ====================

  getPartCategories(): Observable<PartCategoryDto[]> {
    return this.http.get<PartCategoryDto[]>(`${this.API_URL}/parts/categories`, { headers: this.getHeaders() });
  }

  getAllParts(): Observable<VehiclePartDto[]> {
    return this.http.get<VehiclePartDto[]>(`${this.API_URL}/parts/parts`, { headers: this.getHeaders() });
  }

  createPart(request: { categoryId: number; name: string; description?: string; partNumber?: string }): Observable<VehiclePartDto> {
    return this.http.post<VehiclePartDto>(`${this.API_URL}/parts/parts`, request, { headers: this.getHeaders() });
  }

  // ==================== CHAT ====================

  getChatUsers(): Observable<any[]> {
    return this.http.get<any[]>(`${this.API_URL}/chat/users`, { headers: this.getHeaders() });
  }

  getChatMessages(otherUserId: number, limit = 50, beforeId?: number): Observable<any[]> {
    let params = `limit=${limit}`;
    if (beforeId) params += `&beforeId=${beforeId}`;
    return this.http.get<any[]>(`${this.API_URL}/chat/messages/${otherUserId}?${params}`, { headers: this.getHeaders() });
  }

  sendChatMessage(receiverId: number, content: string): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/chat/messages`, { receiverId, content }, { headers: this.getHeaders() });
  }

  markChatMessagesRead(otherUserId: number): Observable<any> {
    return this.http.put<any>(`${this.API_URL}/chat/messages/${otherUserId}/read`, {}, { headers: this.getHeaders() });
  }

  getChatUnreadCount(): Observable<{ count: number }> {
    return this.http.get<{ count: number }>(`${this.API_URL}/chat/unread-count`, { headers: this.getHeaders() });
  }

  // ==================== AI DIAGNOSTIC CHAT ====================

  getAiChatVehicles(): Observable<any[]> {
    return this.http.get<any[]>(`${this.API_URL}/ai-chat/vehicles`, { headers: this.getHeaders() });
  }

  sendAiChatMessage(vehicleId: number, message: string): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/ai-chat/send`, { vehicleId, message }, { headers: this.getHeaders() });
  }

  getAiChatHistory(vehicleId: number, limit = 50): Observable<any[]> {
    return this.http.get<any[]>(`${this.API_URL}/ai-chat/history/${vehicleId}?limit=${limit}`, { headers: this.getHeaders() });
  }

  clearAiChatHistory(vehicleId: number): Observable<any> {
    return this.http.delete<any>(`${this.API_URL}/ai-chat/history/${vehicleId}`, { headers: this.getHeaders() });
  }

  getVehicleHealthScore(vehicleId: number): Observable<any> {
    return this.http.get<any>(`${this.API_URL}/ai-chat/health-score/${vehicleId}`, { headers: this.getHeaders() });
  }

  getAllHealthScores(): Observable<any[]> {
    return this.http.get<any[]>(`${this.API_URL}/ai-chat/health-scores`, { headers: this.getHeaders() });
  }

  compareVehicles(vehicleIds: number[], question?: string): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/ai-chat/compare`, { vehicleIds, question }, { headers: this.getHeaders() });
  }

  generateAiReport(vehicleId: number): Observable<any> {
    return this.http.get<any>(`${this.API_URL}/ai-chat/report/${vehicleId}`, { headers: this.getHeaders() });
  }
}

// ==================== FUEL RECORDS INTERFACES ====================

export interface FuelRecord {
  id: number;
  vehicleId: number;
  driverId?: number;
  deviceId?: number;
  recordedAt: string;
  fuelPercent: number;
  fuelLiters?: number;
  tankCapacityLiters?: number;
  consumptionRateLPer100Km?: number;
  averageConsumptionLPer100Km?: number;
  odometerKm?: number;
  speedKph?: number;
  rpm?: number;
  ignitionOn?: boolean;
  latitude: number;
  longitude: number;
  eventType: string;
  fuelChange?: number;
  refuelAmount?: number;
  refuelCost?: number;
  refuelStation?: string;
  isAnomaly: boolean;
  anomalyReason?: string;
}

export interface FuelRecordsSummary {
  totalRecords: number;
  refuelCount: number;
  anomalyCount: number;
  totalRefuelLiters?: number;
  averageConsumptionLPer100Km?: number;
}

export interface FuelRecordsResult {
  items: FuelRecord[];
  summary: FuelRecordsSummary;
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface RefuelEvent {
  id: number;
  recordedAt: string;
  fuelPercent: number;
  refuelAmount?: number;
  refuelCost?: number;
  refuelStation?: string;
  odometerKm?: number;
  latitude: number;
  longitude: number;
}

export interface AnomalyEvent {
  id: number;
  recordedAt: string;
  eventType: string;
  fuelPercent: number;
  fuelChange?: number;
  anomalyReason?: string;
  latitude: number;
  longitude: number;
}

export interface FuelReport {
  vehicleId: number;
  startDate?: string;
  endDate?: string;
  totalRecords: number;
  refuelCount: number;
  totalRefuelLiters: number;
  totalRefuelCost: number;
  anomalyCount: number;
  theftAlertCount: number;
  consumptionSpikeCount: number;
  lowFuelAlertCount: number;
  averageConsumptionLPer100Km?: number;
  refuels: RefuelEvent[];
  anomalies: AnomalyEvent[];
}

// ==================== DAILY ACTIVITY REPORT INTERFACES ====================

export interface DailyActivityReport {
  vehicleId: number;
  vehicleName: string;
  plate?: string;
  driverName?: string;
  reportDate: string;
  hasActivity: boolean;
  firstStart?: DailyStartEvent;
  lastPosition?: DailyEndEvent;
  activities: ActivitySegment[];
  fuelEvents: FuelEvent[];
  summary: DailySummary;
}

export interface DailyStartEvent {
  timestamp: string;
  latitude: number;
  longitude: number;
  address?: string;
}

export interface DailyEndEvent {
  timestamp: string;
  latitude: number;
  longitude: number;
  address?: string;
  ignitionOn: boolean;
}

export interface ActivitySegment {
  type: 'drive' | 'stop' | 'pause';
  sequenceNumber: number;
  startTime: string;
  endTime?: string;
  durationSeconds: number;
  durationFormatted: string;
  startLocation: LocationInfo;
  endLocation?: LocationInfo;
  distanceKm?: number;
  avgSpeedKph?: number;
  maxSpeedKph?: number;
}

export interface LocationInfo {
  latitude: number;
  longitude: number;
  address?: string;
}

export interface DailySummary {
  totalActiveSeconds: number;
  totalDrivingSeconds: number;
  totalStoppedSeconds: number;
  totalActiveFormatted: string;
  totalDrivingFormatted: string;
  totalStoppedFormatted: string;
  totalDistanceKm: number;
  stopCount: number;
  driveCount: number;
  maxSpeedKph: number;
  avgSpeedKph: number;
  positionCount: number;
  fuelRefillCount: number;
  totalFuelRefillLiters?: number;
  fuelStartPercent?: number;
  fuelEndPercent?: number;
}

export interface FuelEvent {
  timestamp: string;
  eventType: string;
  fuelPercent: number;
  fuelLiters?: number;
  fuelChange?: number;
  refuelAmount?: number;
  latitude: number;
  longitude: number;
  address?: string;
  refuelStation?: string;
}

// ==================== MILEAGE REPORT INTERFACES ====================

export interface MileageReport {
  vehicleId: number;
  vehicleName: string;
  plate?: string;
  driverName?: string;
  vehicleType?: string;
  startDate: string;
  endDate: string;
  hasData: boolean;
  startOdometerKm?: number;
  endOdometerKm?: number;
  odometerDifferenceKm?: number;
  totalDistanceKm: number;
  averageDailyKm: number;
  dailyBreakdown: DailyMileage[];
  weeklyBreakdown: WeeklyMileage[];
  monthlyBreakdown: MonthlyMileage[];
  previousPeriodComparison?: PeriodComparison;
  summary: MileageSummary;
}

export interface DailyMileage {
  date: string;
  dayOfWeek: string;
  distanceKm: number;
  startOdometerKm?: number;
  endOdometerKm?: number;
  tripCount: number;
  drivingMinutes: number;
  maxSpeedKph: number;
  avgSpeedKph: number;
}

export interface WeeklyMileage {
  weekNumber: number;
  weekStart: string;
  weekEnd: string;
  distanceKm: number;
  averageDailyKm: number;
  tripCount: number;
  drivingMinutes: number;
}

export interface MonthlyMileage {
  year: number;
  month: number;
  monthName: string;
  distanceKm: number;
  averageDailyKm: number;
  tripCount: number;
  daysWithActivity: number;
}

export interface PeriodComparison {
  previousPeriodDistanceKm: number;
  currentPeriodDistanceKm: number;
  differenceKm: number;
  percentageChange: number;
  trend: 'increase' | 'decrease' | 'stable';
}

export interface MileageSummary {
  totalDistanceKm: number;
  averageDailyKm: number;
  maxDailyKm: number;
  minDailyKm: number;
  maxDailyDate?: string;
  minDailyDate?: string;
  totalTripCount: number;
  totalDrivingMinutes: number;
  totalDrivingFormatted: string;
  maxSpeedKph: number;
  avgSpeedKph: number;
  daysWithActivity: number;
  totalDays: number;
  activityPercentage: number;
}

// ==================== MILEAGE PERIOD REPORT INTERFACES (Hour/Day/Month) ====================

export type MileagePeriodType = 'hour' | 'day' | 'month';

export interface MileagePeriodReport {
  vehicleId: number;
  vehicleName: string;
  plate?: string;
  driverName?: string;
  vehicleType?: string;
  startDate: string;
  endDate: string;
  periodType: MileagePeriodType;
  hasData: boolean;
  totalDistanceKm: number;
  averageDistanceKm: number;
  maxDistanceKm: number;
  minDistanceKm: number;
  totalTripCount: number;
  totalDrivingMinutes: number;
  totalDrivingFormatted: string;
  hourlyBreakdown: HourlyMileagePeriod[];
  dailyBreakdown: DailyMileagePeriod[];
  monthlyBreakdown: MonthlyMileagePeriod[];
  chartData: ChartDataPointPeriod[];
}

export interface HourlyMileagePeriod {
  hour: number;
  hourLabel: string;
  distanceKm: number;
  tripCount: number;
  drivingMinutes: number;
  maxSpeedKph: number;
  avgSpeedKph: number;
}

export interface DailyMileagePeriod {
  date: string;
  dateLabel: string;
  dayOfWeek: string;
  distanceKm: number;
  tripCount: number;
  drivingMinutes: number;
  maxSpeedKph: number;
  avgSpeedKph: number;
}

export interface MonthlyMileagePeriod {
  year: number;
  month: number;
  monthLabel: string;
  distanceKm: number;
  averageDailyKm: number;
  tripCount: number;
  drivingMinutes: number;
  daysWithActivity: number;
  totalDays: number;
}

export interface ChartDataPointPeriod {
  label: string;
  value: number;
  tooltip?: string;
}

// ==================== MONTHLY FLEET REPORT INTERFACES ====================

export interface MonthlyFleetReport {
  year: number;
  month: number;
  monthName: string;
  generatedAt: string;
  reportPeriod: string;
  executiveSummary: ExecutiveSummary;
  fleetOverview: FleetOverview;
  utilization: VehicleUtilization;
  fuelAnalytics: FuelAnalytics;
  maintenance: MaintenanceAnalytics;
  driverPerformance: DriverPerformance;
  efficiency: OperationalEfficiency;
  costAnalysis: CostAnalysis;
  monthOverMonth: FleetPeriodComparison;
  yearOverYear?: FleetPeriodComparison;
  alerts: FleetAlert[];
  keyPerformanceIndicators: Kpi[];
  charts: ChartDataCollection;
}

// ==================== MONTHLY COST REPORT ====================

export interface MonthlyCostReport {
  year: number;
  month: number;
  monthName: string;
  reportPeriod: string;
  generatedAt: string;
  totalKm: number;
  totalFuelCostDzd: number;
  totalFuelLiters: number;
  totalMaintenanceCostDzd: number;
  totalRepairCostDzd: number;
  totalCostDzd: number;
  departments: DepartmentCostGroup[];
  vehicles: VehicleMonthlyCost[];
}

export interface DepartmentCostGroup {
  departmentId: number | null;
  departmentName: string;
  totalKm: number;
  totalFuelCostDzd: number;
  totalFuelLiters: number;
  totalMaintenanceCostDzd: number;
  totalRepairCostDzd: number;
  totalCostDzd: number;
  vehicles: VehicleMonthlyCost[];
}

export interface VehicleMonthlyCost {
  vehicleId: number;
  vehicleName: string;
  plate: string | null;
  driverName: string | null;
  departmentId: number | null;
  departmentName: string;
  km: number;
  kmPr: number;
  fuelCostDzd: number;
  maintenanceCostDzd: number;
  repairCostDzd: number;
  totalCostDzd: number;
  fuelLiters: number;
  fuelLitersPr: number;
  costPerKm: number;
  fuelPer100Km: number;
  maintenanceRepairPer100Km: number;
  consumptionPer100Km: number;
  consumptionPrPer100Km: number;
}

export interface ExecutiveSummary {
  totalVehicles: number;
  activeVehicles: number;
  totalDistanceKm: number;
  totalFuelConsumedLiters: number;
  totalOperationalCost: number;
  fleetUtilizationRate: number;
  averageFuelEfficiency: number;
  totalTrips: number;
  totalDrivingHours: number;
  keyInsights: string[];
  recommendations: string[];
}

export interface FleetOverview {
  totalVehicles: number;
  activeVehicles: number;
  inactiveVehicles: number;
  inMaintenanceVehicles: number;
  byType: VehicleTypeSummary[];
  byStatus: VehicleStatusSummary[];
  byDepartment: DepartmentSummary[];
}

export interface VehicleTypeSummary {
  type: string;
  count: number;
  percentage: number;
  totalDistanceKm: number;
  avgDistanceKm: number;
}

export interface VehicleStatusSummary {
  status: string;
  count: number;
  percentage: number;
}

export interface DepartmentSummary {
  department: string;
  vehicleCount: number;
  totalDistanceKm: number;
  totalCost: number;
}

export interface VehicleUtilization {
  overallUtilizationRate: number;
  averageDailyUsageHours: number;
  averageDailyDistanceKm: number;
  totalOperatingDays: number;
  totalIdleDays: number;
  dailyTrend: DailyUtilization[];
  byVehicle: VehicleUtilizationDetail[];
  statistics: StatisticalMetrics;
}

export interface DailyUtilization {
  date: string;
  utilizationRate: number;
  activeVehicles: number;
  totalDistanceKm: number;
  totalTrips: number;
}

export interface VehicleUtilizationDetail {
  vehicleId: number;
  vehicleName: string;
  plate?: string;
  utilizationRate: number;
  totalDistanceKm: number;
  totalTrips: number;
  operatingDays: number;
  avgDailyKm: number;
}

export interface FuelAnalytics {
  totalFuelConsumedLiters: number;
  totalFuelCost: number;
  averageConsumptionPer100Km: number;
  averageFuelEfficiencyKmPerLiter: number;
  dailyTrend: DailyFuelConsumption[];
  byVehicle: VehicleFuelConsumption[];
  refuelEvents: FuelEvent[];
  anomalies: FuelAnomaly[];
  statistics: StatisticalMetrics;
}

export interface DailyFuelConsumption {
  date: string;
  consumptionLiters: number;
  distanceKm: number;
  efficiencyKmPerLiter: number;
}

export interface VehicleFuelConsumption {
  vehicleId: number;
  vehicleName: string;
  totalConsumedLiters: number;
  totalDistanceKm: number;
  efficiencyKmPerLiter: number;
  consumptionPer100Km: number;
  efficiencyRating: string;
}

export interface FuelEvent {
  timestamp: string;
  vehicleId: number;
  vehicleName: string;
  amountLiters: number;
  cost?: number;
  location: string;
}

export interface FuelAnomaly {
  detectedAt: string;
  vehicleId: number;
  vehicleName: string;
  anomalyType: string;
  description: string;
  severity: string;
}

export interface MaintenanceAnalytics {
  totalMaintenanceEvents: number;
  totalMaintenanceCost: number;
  scheduledMaintenances: number;
  unscheduledMaintenances: number;
  avgMaintenanceCostPerVehicle: number;
  byType: MaintenanceTypeBreakdown[];
  byVehicle: VehicleMaintenance[];
  recentEvents: MaintenanceEvent[];
  upcoming: UpcomingMaintenance[];
}

export interface MaintenanceTypeBreakdown {
  type: string;
  count: number;
  totalCost: number;
  percentage: number;
}

export interface VehicleMaintenance {
  vehicleId: number;
  vehicleName: string;
  maintenanceCount: number;
  totalCost: number;
  lastMaintenanceDate?: string;
}

export interface MaintenanceEvent {
  id: number;
  vehicleId: number;
  vehicleName: string;
  type: string;
  date: string;
  cost: number;
  description: string;
}

export interface UpcomingMaintenance {
  vehicleId: number;
  vehicleName: string;
  maintenanceType: string;
  dueDate: string;
  daysUntilDue: number;
}

export interface DriverPerformance {
  totalDrivers: number;
  activeDrivers: number;
  averagePerformanceScore: number;
  driverMetrics: DriverMetrics[];
  topPerformers: DriverRanking[];
  needsImprovement: DriverRanking[];
  eventsSummary: DrivingEventSummary[];
  statistics: StatisticalMetrics;
}

export interface DriverMetrics {
  driverId: number;
  driverName: string;
  totalDistanceKm: number;
  totalTrips: number;
  avgSpeedKph: number;
  harshBrakingEvents: number;
  harshAccelerationEvents: number;
  speedingEvents: number;
  fuelEfficiency: number;
  performanceScore: number;
  rating: string;
}

export interface DriverRanking {
  rank: number;
  driverId: number;
  driverName: string;
  score: number;
  trend: string;
}

export interface DrivingEventSummary {
  eventType: string;
  totalCount: number;
  uniqueDrivers: number;
  avgPerDriver: number;
}

export interface OperationalEfficiency {
  overallEfficiencyScore: number;
  fleetAvailabilityRate: number;
  onTimeDeliveryRate: number;
  idleTimePercentage: number;
  averageRouteEfficiency: number;
  dailyTrend: DailyEfficiency[];
  metrics: EfficiencyMetric[];
}

export interface DailyEfficiency {
  date: string;
  efficiencyScore: number;
  availabilityRate: number;
  idleTimePercent: number;
}

export interface EfficiencyMetric {
  name: string;
  value: number;
  target: number;
  variance: number;
  status: string;
}

export interface CostAnalysis {
  totalOperationalCost: number;
  fuelCost: number;
  maintenanceCost: number;
  insuranceCost: number;
  otherCosts: number;
  costPerKm: number;
  costPerVehicle: number;
  byCategory: CostBreakdown[];
  dailyTrend: DailyCost[];
  byVehicle: VehicleCost[];
}

export interface CostBreakdown {
  category: string;
  amount: number;
  percentage: number;
}

export interface DailyCost {
  date: string;
  totalCost: number;
  fuelCost: number;
  maintenanceCost: number;
}

export interface VehicleCost {
  vehicleId: number;
  vehicleName: string;
  totalCost: number;
  fuelCost: number;
  maintenanceCost: number;
  costPerKm: number;
}

export interface FleetPeriodComparison {
  comparisonPeriod: string;
  distance: ComparisonMetric;
  fuelConsumption: ComparisonMetric;
  cost: ComparisonMetric;
  utilization: ComparisonMetric;
  efficiency: ComparisonMetric;
  trips: ComparisonMetric;
}

export interface ComparisonMetric {
  metricName: string;
  currentValue: number;
  previousValue: number;
  change: number;
  changePercent: number;
  trend: string;
  isPositiveTrend: boolean;
}

export interface FleetAlert {
  id: string;
  type: string;
  severity: string;
  title: string;
  description: string;
  detectedAt: string;
  vehicleId?: number;
  vehicleName?: string;
  recommendedAction: string;
}

export interface Kpi {
  name: string;
  category: string;
  value: number;
  target: number;
  variance: number;
  variancePercent: number;
  unit: string;
  status: string;
  trend: string;
}

export interface StatisticalMetrics {
  mean: number;
  median: number;
  standardDeviation: number;
  variance: number;
  min: number;
  max: number;
  range: number;
  percentile25: number;
  percentile75: number;
  interquartileRange: number;
}

export interface ChartDataCollection {
  utilizationByVehicleType: ChartData;
  maintenanceCostByType: ChartData;
  distanceByDepartment: ChartData;
  fuelConsumptionTrend: MultiSeriesChartData;
  driverPerformanceTrend: MultiSeriesChartData;
  efficiencyTrend: MultiSeriesChartData;
  dailyDistanceTrend: MultiSeriesChartData;
  fleetComposition: ChartData;
  costDistribution: ChartData;
  maintenanceTypeBreakdown: ChartData;
  vehicleStatusDistribution: ChartData;
  departmentComparison: ChartData;
  vehiclePerformanceRanking: ChartData;
  driverRanking: ChartData;
}

export interface ChartData {
  title: string;
  type: string;
  labels: string[];
  values: number[];
  unit?: string;
  colors?: string[];
}

export interface MultiSeriesChartData {
  title: string;
  type: string;
  labels: string[];
  series: ChartSeries[];
  xAxisLabel?: string;
  yAxisLabel?: string;
}

export interface ChartSeries {
  name: string;
  data: number[];
  color?: string;
}

// ==================== NEW DASHBOARD API INTERFACES ====================

export interface DashboardKpis {
  generatedAt: string;
  period: string;
  fleet: FleetKpis;
  operations: OperationalKpis;
  financial: FinancialKpis;
  performance: PerformanceKpis;
  trends: TrendIndicators;
}

export interface FleetKpis {
  totalVehicles: number;
  activeVehicles: number;
  inactiveVehicles: number;
  inMaintenance: number;
  availabilityRate: number;
  utilizationRate: number;
}

export interface OperationalKpis {
  totalDistanceKm: number;
  totalTrips: number;
  totalDrivingHours: number;
  avgDailyDistanceKm: number;
  avgTripsPerVehicle: number;
  activeDrivers: number;
}

export interface FinancialKpis {
  totalOperationalCost: number;
  fuelCost: number;
  maintenanceCost: number;
  costPerKm: number;
  costPerVehicle: number;
  fuelCostPerKm: number;
}

export interface PerformanceKpis {
  fuelEfficiencyKmPerLiter: number;
  avgConsumptionPer100Km: number;
  driverPerformanceScore: number;
  safetyIncidents: number;
  onTimeDeliveryRate: number;
  idleTimePercentage: number;
}

export interface TrendIndicators {
  distance: Trend;
  fuelConsumption: Trend;
  cost: Trend;
  utilization: Trend;
  efficiency: Trend;
}

export interface Trend {
  currentValue: number;
  previousValue: number;
  changePercent: number;
  direction: 'up' | 'down' | 'stable';
  isPositive: boolean;
}

export interface DashboardCharts {
  generatedAt: string;
  period: string;
  distanceByVehicle: BarChartData;
  fuelDistribution: PieChartData;
  maintenanceTrend: AreaChartData;
  dailyDistanceTrend: LineChartData;
  utilizationTrend: LineChartData;
  costBreakdown: PieChartData;
  vehicleStatusChart: BarChartData;
  topVehicles: BarChartData;
}

export interface BarChartData {
  title: string;
  xAxisLabel: string;
  yAxisLabel: string;
  unit: string;
  data: BarChartItem[];
}

export interface BarChartItem {
  label: string;
  value: number;
  color: string;
  id?: number;
}

export interface PieChartData {
  title: string;
  unit: string;
  total: number;
  slices: PieChartSlice[];
}

export interface PieChartSlice {
  label: string;
  value: number;
  percentage: number;
  color: string;
  id?: number;
}

export interface LineChartData {
  title: string;
  xAxisLabel: string;
  yAxisLabel: string;
  labels: string[];
  series: LineChartSeriesData[];
}

export interface LineChartSeriesData {
  name: string;
  color: string;
  values: number[];
  fill: boolean;
}

export interface AreaChartData {
  title: string;
  xAxisLabel: string;
  yAxisLabel: string;
  unit: string;
  labels: string[];
  series: AreaChartSeriesData[];
}

export interface AreaChartSeriesData {
  name: string;
  color: string;
  backgroundColor: string;
  values: number[];
  vehicleId?: number;
}

export interface FleetStatistics {
  generatedAt: string;
  period: string;
  groupedBy: string;
  summary: FleetSummary;
  vehicleStats: VehicleStatistics[];
  pagination: Pagination;
  analysis: StatisticalAnalysis;
}

export interface FleetSummary {
  totalRecords: number;
  totalDistanceKm: number;
  totalFuelLiters: number;
  totalCost: number;
  totalTrips: number;
  totalHours: number;
  avgUtilizationRate: number;
  avgEfficiency: number;
}

export interface VehicleStatistics {
  vehicleId: number;
  vehicleName: string;
  plate?: string;
  vehicleType?: string;
  department?: string;
  driverName?: string;
  totalDistanceKm: number;
  avgDailyDistanceKm: number;
  maxDailyDistanceKm: number;
  utilizationRate: number;
  operatingDays: number;
  idleDays: number;
  totalDrivingHours: number;
  totalFuelLiters: number;
  avgConsumptionPer100Km: number;
  fuelEfficiencyKmPerLiter: number;
  fuelVariancePercent: number;
  totalCost: number;
  fuelCost: number;
  maintenanceCost: number;
  costPerKm: number;
  costVariancePercent: number;
  totalTrips: number;
  avgSpeedKph: number;
  maxSpeedKph: number;
  safetyIncidents: number;
  distanceRank: number;
  efficiencyRank: number;
  costRank: number;
}

export interface Pagination {
  currentPage: number;
  pageSize: number;
  totalPages: number;
  totalRecords: number;
  hasPrevious: boolean;
  hasNext: boolean;
}

export interface StatisticalAnalysis {
  distanceMean: number;
  distanceMedian: number;
  distanceStdDev: number;
  distanceMin: number;
  distanceMax: number;
  fuelMean: number;
  fuelMedian: number;
  fuelStdDev: number;
  costMean: number;
  costMedian: number;
  costStdDev: number;
  highDistanceOutliers: number[];
  highFuelOutliers: number[];
  highCostOutliers: number[];
}

// ==================== VEHICLE STOPS ====================
export interface VehicleStopDto {
  id: number;
  vehicleId: number;
  vehicleName?: string;
  vehiclePlate?: string;
  driverId?: number;
  driverName?: string;
  startTime: string;
  endTime?: string;
  durationSeconds: number;
  latitude: number;
  longitude: number;
  address?: string;
  stopType: string;
  ignitionOff: boolean;
  isAuthorized: boolean;
  fuelLevelStart?: number;
  fuelLevelEnd?: number;
  fuelConsumed?: number;
  insideGeofence: boolean;
  geofenceName?: string;
  notes?: string;
}

export interface VehicleStopsResult {
  items: VehicleStopDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

// ==================== GENERIC PAGINATED RESULT ====================
export interface PaginatedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

// ==================== SUPPLIERS / GARAGES ====================
export interface SupplierDto {
  id: number;
  name: string;
  type: string;
  address?: string;
  city?: string;
  postalCode?: string;
  contactName?: string;
  phone?: string;
  email?: string;
  website?: string;
  taxId?: string;
  bankAccount?: string;
  paymentTerms?: string;
  discountPercent?: number;
  rating?: number;
  notes?: string;
  isActive: boolean;
  services: string[];
  createdAt: string;
  updatedAt: string;
}

export interface SupplierStatsDto {
  totalSuppliers: number;
  activeSuppliers: number;
  byType: { [key: string]: number };
}

export interface CreateSupplierRequest {
  name: string;
  type: string;
  address?: string;
  city?: string;
  postalCode?: string;
  contactName?: string;
  phone?: string;
  email?: string;
  website?: string;
  taxId?: string;
  bankAccount?: string;
  paymentTerms?: string;
  discountPercent?: number;
  rating?: number;
  notes?: string;
  isActive?: boolean;
  services?: string[];
}

export interface UpdateSupplierRequest {
  name?: string;
  type?: string;
  address?: string;
  city?: string;
  postalCode?: string;
  contactName?: string;
  phone?: string;
  email?: string;
  website?: string;
  taxId?: string;
  bankAccount?: string;
  paymentTerms?: string;
  discountPercent?: number;
  rating?: number;
  notes?: string;
  isActive?: boolean;
  services?: string[];
}

// ==================== DOCUMENTS / EXPIRIES ====================
export interface VehicleExpiryDto {
  vehicleId: number;
  vehicleName: string;
  vehiclePlate?: string;
  documentType: string;
  expiryDate?: string;
  status: string;
  daysUntilExpiry: number;
  lastRenewalDate?: string;
  lastRenewalCost?: number;
  documentNumber?: string;
}

export interface ExpiryStatsDto {
  expiredCount: number;
  expiringSoonCount: number;
  okCount: number;
  totalCount: number;
}

export interface RenewDocumentRequest {
  vehicleId: number;
  documentType: string;
  amount: number;
  paymentDate: string;
  newExpiryDate: string;
  documentNumber?: string;
  provider?: string;
  notes?: string;
  documentUrl?: string;
}

export interface RenewalHistoryDto {
  id: number;
  documentType: string;
  amount: number;
  paymentDate: string;
  expiryDate?: string;
  documentNumber?: string;
  provider?: string;
  notes?: string;
  documentUrl?: string;
}

// Calypso 7 — AccidentClaim* DTOs deleted; the unified AccidentReportDto
// below replaces them.

// ==================== ACCIDENT REPORTS (Calypso 7) ====================
// Mirrors services/src/GisAPI.Application/Features/AccidentEvents/Queries/
// GetAccidentReportQuery.cs — full timeline (6 phases).

export type AccidentSeverity = 'minor' | 'moderate' | 'severe' | 'total';
export type AccidentStatus = 'pending' | 'confirmed' | 'dismissed';
export type AccidentClaimStatus = 'pending' | 'approved' | 'partial' | 'rejected' | 'closed';
export type AccidentOrigin = 'auto' | 'manual';

export interface AccidentReportDto {
  id: number;
  companyId: number;
  origin: AccidentOrigin;
  vehicleId: number | null;
  gpsDeviceId: number | null;
  driverId: number | null;
  deviceUid: string;
  incidentAt: string;
  latitude: number;
  longitude: number;
  referenceCode: string | null;
  vehicleLabel: string | null;
  locationCommune: string | null;
  locationGovernorate: string | null;
  locationRoadType: string | null;

  // Phase 1 — Detection
  synthesisText: string | null;
  confidence: number;
  story: AccidentReportStoryEventDto[] | null;
  reasons: AccidentReportReasonDto[] | null;
  indicators: AccidentReportIndicatorDto[] | null;
  weatherConditions: string | null;
  roadConditions: string | null;
  policeReportNumber: string | null;
  mileageAtAccident: number | null;

  // Phase 2 — Confirmation
  status: AccidentStatus;
  decidedByUserId: number | null;
  decidedByName: string | null;
  decidedAt: string | null;
  initialDescription: string | null;
  initialSeverity: AccidentSeverity | null;
  damagedZones: string[] | null;

  // Phase 3 — Expert
  expertVisitedAt: string | null;
  expertName: string | null;
  expertCompany: string | null;
  expertAssessment: string | null;
  expertEstimatedAmount: number | null;

  // Phase 4 — Mechanic quote
  mechanicQuoteAt: string | null;
  mechanicName: string | null;
  mechanicQuotedAmount: number | null;

  // Phase 5 — Repair
  repairStartedAt: string | null;
  repairCompletedAt: string | null;
  actualRepairCost: number | null;
  towDetectedAt: string | null;

  // Phase 6 — Insurance settlement
  claimNumber: string | null;
  claimSubmittedAt: string | null;
  claimApprovedAmount: number | null;
  claimStatus: AccidentClaimStatus | null;
  thirdPartyInvolved: boolean;

  // Misc
  witnesses: string | null;
  additionalNotes: string | null;
  pdfReportUrl: string | null;

  // Children
  documents: AccidentReportDocumentDto[];
  thirdParties: AccidentReportThirdPartyDto[];
}

export interface AccidentReportDocumentDto {
  id: number;
  documentType: string;
  fileName: string;
  fileUrl: string;
  fileSize: number | null;
  mimeType: string | null;
  uploadedAt: string;
}

export interface AccidentReportThirdPartyDto {
  id: number;
  name: string | null;
  phone: string | null;
  vehiclePlate: string | null;
  vehicleModel: string | null;
  insuranceCompany: string | null;
  insuranceNumber: string | null;
  /** Calypso 8 — Date d'expiration de la police d'assurance du tiers. */
  insuranceExpiry: string | null;
}

export interface AccidentEventListItemDto {
  id: number;
  origin: AccidentOrigin;
  vehicleId: number | null;
  vehicleLabel: string | null;
  incidentAt: string;
  latitude: number;
  longitude: number;
  locationCommune: string | null;
  locationGovernorate: string | null;
  confidence: number;
  status: AccidentStatus;
  decidedAt: string | null;
  decidedByName: string | null;
  towDetectedAt: string | null;
  pdfReportUrl: string | null;
  initialSeverity: AccidentSeverity | null;
  expertVisitedAt: string | null;
  expertEstimatedAmount: number | null;
  mechanicQuotedAmount: number | null;
  repairCompletedAt: string | null;
  actualRepairCost: number | null;
  claimNumber: string | null;
  claimStatus: AccidentClaimStatus | null;
  claimApprovedAmount: number | null;
  currentPhase: 'detection' | 'confirmed' | 'expertise' | 'quote' | 'repair' | 'claim' | 'closed' | 'dismissed';
}

export interface ListAccidentEventsResult {
  items: AccidentEventListItemDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

// ── Standalone tow detection (/remorquages) ──────────────────────────────

export interface TowEventDto {
  id: number;
  vehicleId: number;
  vehicleName: string | null;
  vehiclePlate: string | null;
  deviceUid: string | null;
  startedAt: string;
  lastSeenAt: string;
  endedAt: string | null;
  startLat: number;
  startLon: number;
  lastLat: number | null;
  lastLon: number | null;
  startAddress: string | null;
  maxSpeedKph: number;
  distanceMeters: number;
  frameCount: number;
  status: 'active' | 'ended';
  acknowledged: boolean;
  acknowledgedAt: string | null;
}

export interface TowEventsResult {
  items: TowEventDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

// ── Phase request payloads ───────────────────────────────────────────────

export interface UpdateInitialDamagesRequest {
  description?: string | null;
  severity?: AccidentSeverity | null;
  damagedZones?: string[] | null;
  policeReportNumber?: string | null;
  mileageAtAccident?: number | null;
  weatherConditions?: string | null;
  roadConditions?: string | null;
}

export interface RegisterExpertAssessmentRequest {
  visitedAt?: string | null;
  expertName?: string | null;
  expertCompany?: string | null;
  assessment?: string | null;
  estimatedAmount?: number | null;
}

export interface RegisterMechanicQuoteRequest {
  quoteAt?: string | null;
  mechanicName?: string | null;
  quotedAmount?: number | null;
}

export interface RegisterRepairRequest {
  startedAt?: string | null;
  completedAt?: string | null;
  actualCost?: number | null;
}

export interface RegisterClaimRequest {
  claimNumber?: string | null;
  submittedAt?: string | null;
  approvedAmount?: number | null;
  status?: AccidentClaimStatus | null;
  thirdPartyInvolved?: boolean | null;
}

export interface AddThirdPartyRequest {
  name?: string | null;
  phone?: string | null;
  vehiclePlate?: string | null;
  vehicleModel?: string | null;
  insuranceCompany?: string | null;
  insuranceNumber?: string | null;
  /** Calypso 8 — Date d'expiration de la police d'assurance du tiers. */
  insuranceExpiry?: string | null;
}

export interface CreateManualAccidentRequest {
  vehicleId: number;
  incidentAt: string;
  latitude?: number | null;
  longitude?: number | null;
  locationCommune?: string | null;
  locationGovernorate?: string | null;
  description?: string | null;
  severity?: AccidentSeverity | null;
  estimatedCost?: number | null;
  claimNumber?: string | null;
  internalNotes?: string | null;
}

export interface AccidentReportStoryEventDto {
  time: string;
  title: string;
  body: string;
  severity: string;
}

export interface AccidentReportReasonDto {
  title: string;
  text: string;
}

export interface AccidentReportIndicatorDto {
  label: string;
  value: string;
  hint?: string | null;
}

// ==================== MAINTENANCE TEMPLATES ====================
export interface MaintenanceTemplateDto {
  id: number;
  name: string;
  description?: string;
  category: string;
  priority: string;
  intervalKm?: number;
  intervalMonths?: number;
  estimatedCost?: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
  warningKm?: number;
  warningDays?: number;
  criticalKm?: number;
  criticalDays?: number;
}

export interface CreateMaintenanceTemplateRequest {
  name: string;
  description?: string;
  category: string;
  priority?: string;
  intervalKm?: number;
  intervalMonths?: number;
  estimatedCost?: number;
  isActive?: boolean;
  warningKm?: number;
  warningDays?: number;
  criticalKm?: number;
  criticalDays?: number;
}

export interface UpdateMaintenanceTemplateRequest {
  name?: string;
  description?: string;
  category?: string;
  priority?: string;
  intervalKm?: number;
  intervalMonths?: number;
  estimatedCost?: number;
  isActive?: boolean;
  warningKm?: number;
  warningDays?: number;
  criticalKm?: number;
  criticalDays?: number;
}

// ==================== VEHICLE MAINTENANCE SCHEDULE ====================
export interface VehicleMaintenanceStatusDto {
  vehicleId: number;
  vehicleName: string;
  vehiclePlate?: string;
  currentMileage: number;
  maintenanceItems: MaintenanceItemDto[];
}

export interface MaintenanceItemDto {
  scheduleId: number;
  templateId: number;
  templateName: string;
  category: string;
  priority: string;
  lastDoneDate?: string;
  lastDoneKm?: number;
  nextDueDate?: string;
  nextDueKm?: number;
  status: string;
  kmUntilDue?: number;
  daysUntilDue?: number;
  // Free maintenance benefits
  freeUsesTotal?: number;
  freeUsesRemaining?: number;
  freeSource?: string;
  freeExpiryDate?: string;
  freeNotes?: string;
}

export interface MaintenanceStatsDto {
  totalSchedules: number;
  overdueCount: number;
  dueCount: number;
  upcomingCount: number;
  okCount: number;
}

export interface MarkMaintenanceDoneRequest {
  vehicleId: number;
  templateId: number;
  date: string;
  mileage: number;
  cost: number;
  supplierId?: number;
  notes?: string;
  applyFreeBenefit?: boolean;
}

export interface DeclareFreeMaintenancesRequest {
  vehicleId: number;
  templateId: number;
  count: number;
  source?: string;
  expiryDate?: string;
  notes?: string;
}

export interface UpdateFreeMaintenanceRequest {
  freeUsesTotal: number;
  freeUsesRemaining: number;
  source?: string;
  expiryDate?: string;
  notes?: string;
}

// ==================== REPAIRS ====================
export interface RepairsListResult {
  items: RepairDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface RepairDto {
  id: number;
  vehicleId: number;
  vehicleName?: string;
  vehiclePlate?: string;
  supplierId?: number;
  supplierName?: string;
  reference?: string;
  description?: string;
  repairDate: string;
  mileageAtRepair?: number;
  laborCost: number;
  partsCost: number;
  totalCost: number;
  status: string;
  invoiceNumber?: string;
  notes?: string;
  parts: RepairPartDto[];
  createdAt?: string;
  updatedAt?: string;
}

export interface RepairPartDto {
  id?: number;
  partName: string;
  partReference?: string;
  quantity: number;
  unitPrice: number;
  subtotal: number;
  notes?: string;
}

export interface RepairStatsDto {
  totalRepairs: number;
  pendingRepairs: number;
  completedRepairs: number;
  totalCost: number;
  averageCost: number;
  totalLaborCost: number;
  totalPartsCost: number;
}

export interface CreateRepairRequest {
  vehicleId: number;
  supplierId?: number;
  description?: string;
  repairDate: string;
  mileageAtRepair?: number;
  laborCost: number;
  invoiceNumber?: string;
  notes?: string;
  parts: CreateRepairPartRequest[];
}

export interface CreateRepairPartRequest {
  partName: string;
  partReference?: string;
  quantity: number;
  unitPrice: number;
  notes?: string;
}

export interface UpdateRepairRequest {
  vehicleId: number;
  supplierId?: number;
  description?: string;
  repairDate: string;
  mileageAtRepair?: number;
  laborCost: number;
  status: string;
  invoiceNumber?: string;
  notes?: string;
  parts: CreateRepairPartRequest[];
}

// ==================== FUEL EXPENSES ====================
export interface FleetFuelStatisticsDto {
  totalFleetFuelCost: number;
  totalFleetFuelConsumedLiters: number;
  fleetAverageConsumptionPer100Km: number;
  fleetStandardDeviation: number;
  totalFleetDistanceKm: number;
  vehicleCount: number;
  fuelTypeDistribution: FuelTypeDistributionDto[];
  monthlyTrends: MonthlyFuelTrendDto[];
  vehicleExpenses: VehicleFuelExpenseDto[];
}

export interface FuelComparisonRow {
  vehicleId: number;
  vehicleName: string;
  plate: string | null;
  realLiters: number;   // billed via fuel card (/carburant)
  realCost: number;
  gpsLiters: number;    // consumed per the GPS device
  gpsSource: 'capteur' | 'estime' | 'aucun';  // capteur = real sensor (NEMS L); estime = distance estimate; aucun = no GPS
  distanceKm: number;
  diffLiters: number;   // realLiters - gpsLiters (positive = billed more than consumed)
  diffPercent: number;
}

export interface FuelComparisonReport {
  startDate: string;
  endDate: string;
  totalRealLiters: number;
  totalRealCost: number;
  totalGpsLiters: number;
  vehicleCount: number;
  sensorCount: number;
  rows: FuelComparisonRow[];
}

// ---- Per-vehicle fuel audit (level curve + per-fill verification) ----
export interface FuelLevelPoint {
  t: string;        // ISO timestamp
  percent: number;  // tank fill %
  liters: number;   // litres in tank
}

export interface FuelCardFill {
  date: string;             // ISO
  liters: number;
  cost: number;
  station: string | null;
}

export interface FuelDetectedRefill {
  t: string;      // ISO
  liters: number;
}

export interface FuelFillCheck {
  fillDate: string | null;          // ISO — billed card fill date (null = undeclared refill)
  billedLiters: number;
  matchedRefillDate: string | null; // ISO of matched GPS refill, or null
  detectedLiters: number | null;    // litres detected at the matched refill, or null
  gapHours: number | null;          // hours between billed fill and detected refill
  verdict: 'confirme' | 'ecart' | 'non_detecte' | 'non_declare' | 'volume_non_saisi';
  // Fourchette honnête : la jauge mesure des points, pas des litres —
  // detectedLiters n'est que le centre de [low, high]
  detectedLitersLow: number | null;
  detectedLitersHigh: number | null;
  deltaPoints: number | null;       // montée de jauge brute, en points de %
}

export interface FuelAuditReport {
  vehicleId: number;
  vehicleName: string;
  plate: string | null;
  hasSensor: boolean;
  tankCapacity: number;
  startDate: string;
  endDate: string;
  levelSeries: FuelLevelPoint[];
  cardFills: FuelCardFill[];
  detectedRefills: FuelDetectedRefill[];
  fillChecks: FuelFillCheck[];
  confirmedCount: number;
  notDetectedCount: number;
  undeclaredCount: number;
  // Synthèse : litres facturés vs litres entrés dans la cuve (vus par la jauge)
  totalBilledLiters: number;
  totalDetectedLiters: number;
  undeclaredLiters: number;
  coveragePercent: number | null;
  estimatedUndeclaredCost: number | null;
  // Étalonnage points→litres appris des pleins facturés
  isCalibrated: boolean;
  calibrationPointCount: number;
  effectiveTankLiters: number | null;
}

// ---- Analyse consommation par segments de X km + comparaison par tonnage ----
export interface ConsumptionSegment {
  index: number;
  startTime: string;   // ISO
  endTime: string;     // ISO
  distanceKm: number;
  fuelLiters: number;
  lPer100Km: number;
  tonnageT: number | null;          // tonnage hérité des périodes de chargement déclarées
  isReliable: boolean;
  exclusionReason: string | null;   // pourquoi le segment est exclu des stats
}

export interface ConsumptionSegmentsSummary {
  totalKm: number;
  totalLiters: number;
  avgLPer100Km: number | null;
  minLPer100Km: number | null;
  minSegmentIndex: number | null;
  maxLPer100Km: number | null;
  maxSegmentIndex: number | null;
  reliableSegments: number;
  excludedSegments: number;
}

export interface ConsumptionSegmentsReport {
  vehicleId: number;
  vehicleName: string;
  segmentKm: number;
  hasSensor: boolean;
  litersPerPoint: number;
  isCalibrated: boolean;
  segments: ConsumptionSegment[];
  summary: ConsumptionSegmentsSummary;
}

export interface TonnageGroup {
  tonnageT: number | null;   // null = segments sans tonnage déclaré
  segmentCount: number;
  totalKm: number;
  avgLPer100Km: number;
  minLPer100Km: number;
  maxLPer100Km: number;
  deltaVsLightestPercent: number | null;
}

export interface ConsumptionByTonnageReport {
  vehicleId: number;
  vehicleName: string;
  segmentKm: number;
  groups: TonnageGroup[];
}

export interface VehicleLoadPeriod {
  id: number;
  vehicleId: number;
  startTime: string;        // ISO
  endTime: string | null;   // null = en cours
  tonnageT: number;
  notes: string | null;
}

// ---- Consommation mesurée (jauge) vs réelle (factures, méthode plein à plein) ----
export interface ConsumptionComparisonInterval {
  start: string;                    // ISO — date du plein qui OUVRE l'intervalle
  end: string;                      // ISO — date du plein qui le FERME
  km: number;
  realLiters: number;               // litres facturés au plein de fin
  realLPer100: number;
  measuredLiters: number | null;    // ratchet jauge étalonné sur la même fenêtre
  measuredLPer100: number | null;
  measuredReliable: boolean;        // false = fenêtre polluée par le capteur
}

export interface FuelConsumptionComparisonReport {
  vehicleId: number;
  hasSensor: boolean;
  intervals: ConsumptionComparisonInterval[];
  avgRealLPer100: number | null;      // moyenne pondérée km
  avgMeasuredLPer100: number | null;  // moyenne pondérée km (intervalles fiables)
  deltaPercent: number | null;        // (réel − mesuré) / mesuré × 100
}

export interface ExplainSegmentResult {
  explanation: string;
  fromCache: boolean;
}

export interface VehicleFuelExpenseDto {
  vehicleId: number;
  vehicleName: string;
  plate?: string;
  fuelType?: string;
  fuelTankCapacity?: number;
  totalFuelConsumedLiters: number;
  totalFuelCost: number;
  averageConsumptionPer100Km: number;
  deviationFromFleetAverage: number;
  totalDistanceKm: number;
  isEstimated: boolean;
  dailyConsumption: DailyFuelConsumptionDto[];
  refuels: FuelRefillEventDto[];
}

export interface FuelRefillEventDto {
  timestamp: string;
  vehicleId: number;
  fuelAddedLiters: number;
  estimatedCost?: number;
  latitude?: number;
  longitude?: number;
}

export interface DailyFuelConsumptionDto {
  date: string;
  fuelConsumedLiters: number;
  fuelCost: number;
  distanceKm: number;
  consumptionPer100Km: number;
}

export interface FuelTypeDistributionDto {
  fuelType: string;
  vehicleCount: number;
  totalFuelConsumed: number;
  totalCost: number;
  percentage: number;
}

export interface MonthlyFuelTrendDto {
  year: number;
  month: number;
  monthName: string;
  totalFuelConsumed: number;
  totalCost: number;
  averageConsumption: number;
}

export interface FuelPriceDto {
  fuelTypeId: number;
  fuelTypeCode: string;
  fuelTypeName: string;
  pricePerLiter: number;
  effectiveFrom: string;
}

// ==================== FUEL PRICES MANAGEMENT INTERFACES ====================

export interface FuelPriceFullDto {
  id: number;
  fuelTypeId: number;
  fuelTypeCode: string;
  fuelTypeName: string;
  pricePerLiter: number;
  effectiveFrom: string;
  effectiveTo?: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface FuelTypeDto {
  id: number;
  code: string;
  name: string;
  isSystem: boolean;
}

export interface PaginatedFuelPricesResult {
  items: FuelPriceFullDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface CreateFuelPriceRequest {
  fuelTypeId: number;
  pricePerLiter: number;
  effectiveFrom: string;
  effectiveTo?: string;
}

export interface UpdateFuelPriceRequest {
  fuelTypeId: number;
  pricePerLiter: number;
  effectiveFrom: string;
  effectiveTo?: string;
  isActive: boolean;
}

export interface FuelPriceImportResult {
  totalRows: number;
  successfulImports: number;
  failedImports: number;
  errors: string[];
}

// ==================== FUEL ENTRIES INTERFACES ====================

export interface FuelEntryDto {
  id: number;
  vehicleId?: number;
  vehiclePlate: string;
  fuelTypeId: number;
  fuelTypeCode: string;
  fuelTypeName: string;
  volume: number;
  pricePerLiter: number;
  totalAmount: number;
  invoiceDate: string;
  stationName?: string;
  invoiceNumber?: string;
  notes?: string;
  driverId?: number;
  driverName?: string;
  odometerKm?: number;
  createdAt: string;
}

export interface CreateFuelEntryRequest {
  vehiclePlate: string;
  fuelTypeId: number;
  volume: number;
  pricePerLiter: number;
  // Optional override — when the operator only has the total on the
  // ticket (and not a volume × price breakdown) they can send the total
  // directly. The backend uses it when > 0 and falls back to
  // volume × pricePerLiter otherwise.
  totalAmount?: number;
  invoiceDate: string;
  stationName?: string;
  invoiceNumber?: string;
  notes?: string;
  driverId?: number;
  odometerKm?: number;
}

/** Une commande d'abonnement du libre-service, telle que le serveur la renvoie. */
export interface SubscriptionOrder {
  id: number;
  companyId: number;
  companyName: string;
  subscriptionTypeId: number;
  planName: string;
  planCode: string;
  // semiannual = 6 mois, cycle vendable au même titre que les autres.
  billingCycle: 'monthly' | 'quarterly' | 'semiannual' | 'yearly';
  amount: number;
  // cancelled = annulée par le client ; rejected = refusée par la plateforme (motif dans note).
  status: 'pending' | 'confirmed' | 'cancelled' | 'rejected';
  note?: string;
  createdAt: string;
  processedAt?: string;
}

export interface PaginatedFuelEntriesResult {
  items: FuelEntryDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

// ==================== DEVICE EVENTS INTERFACES ====================

export interface DeviceEventDto {
  id: number;
  deviceId: number;
  vehicleId?: number;
  vehicleName?: string;
  eventType: string;
  eventAt: string;
  offlineDurationSecs?: number;
  lastKnownLat?: number;
  lastKnownLon?: number;
  lastKnownAddress?: string;
  wasMoving: boolean;
  acknowledged: boolean;
  acknowledgedBy?: number;
  acknowledgedAt?: string;
}

export interface DeviceEventsResult {
  items: DeviceEventDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

// ==================== PARTS CATALOG INTERFACES ====================

export interface PartCategoryDto {
  id: number;
  name: string;
  description?: string;
  icon?: string;
  partsCount: number;
}

export interface VehiclePartDto {
  id: number;
  categoryId: number;
  categoryName?: string;
  name: string;
  description?: string;
  partNumber?: string;
}
