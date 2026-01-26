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
}

export interface VehicleStatsDto {
  currentSpeed: number;
  maxSpeed: number;
  fuelLevel?: number;
  temperature?: number;
  batteryLevel?: number;
  isMoving: boolean;
  isStopped: boolean;
  movingTime: string;   // TimeSpan as ISO string
  stoppedTime: string;  // TimeSpan as ISO string
  lastStopTime?: string;
  lastMoveTime?: string;
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
        companyName: 'Demo Company'
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

  deleteVehicle(id: number): Observable<void> {
    if (this.isMockUser()) {
      this.mockDataService.deleteVehicle(id.toString());
      return of(void 0);
    }
    return this.http.delete<void>(`${this.API_URL}/vehicles/${id}`, { headers: this.getHeaders() });
  }

  getVehicleLocations(): Observable<any[]> {
    return this.getLatestPositions();
  }

  // ==================== EMPLOYEES ====================

  getEmployees(): Observable<any[]> {
    if (this.isMockUser()) {
      return this.mockDataService.getEmployees();
    }
    return this.http.get<any[]>(`${this.API_URL}/employees`, { headers: this.getHeaders() });
  }

  getDrivers(): Observable<any[]> {
    if (this.isMockUser()) {
      return this.mockDataService.getEmployees().pipe(
        map(employees => employees.filter(e => e.role === 'driver'))
      );
    }
    return this.http.get<any[]>(`${this.API_URL}/employees/drivers`, { headers: this.getHeaders() });
  }

  getSupervisors(): Observable<any[]> {
    if (this.isMockUser()) {
      return this.mockDataService.getEmployees().pipe(
        map(employees => employees.filter(e => e.role === 'supervisor'))
      );
    }
    return this.http.get<any[]>(`${this.API_URL}/employees/supervisors`, { headers: this.getHeaders() });
  }

  createEmployee(employee: any): Observable<any> {
    if (this.isMockUser()) {
      return of(this.mockDataService.addEmployee(employee));
    }
    return this.http.post<any>(`${this.API_URL}/employees`, employee, { headers: this.getHeaders() });
  }

  updateEmployee(id: number, employee: any): Observable<void> {
    if (this.isMockUser()) {
      this.mockDataService.updateEmployee({ ...employee, id: id.toString() });
      return of(void 0);
    }
    return this.http.put<void>(`${this.API_URL}/employees/${id}`, employee, { headers: this.getHeaders() });
  }

  deleteEmployee(id: number): Observable<void> {
    if (this.isMockUser()) {
      this.mockDataService.deleteEmployee(id.toString());
      return of(void 0);
    }
    return this.http.delete<void>(`${this.API_URL}/employees/${id}`, { headers: this.getHeaders() });
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
      center: (g.centerLat != null && g.centerLng != null) ? { lat: g.centerLat, lng: g.centerLng } : undefined
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
    if (filters?.startDate) params = params.set('startDate', filters.startDate.toISOString());
    if (filters?.endDate) params = params.set('endDate', filters.endDate.toISOString());
    if (filters?.limit) params = params.set('limit', filters.limit.toString());
    return this.http.get<any[]>(`${this.API_URL}/geofences/events`, { headers: this.getHeaders(), params });
  }

  getGeofenceEventsByGeofence(geofenceId: number, limit = 50): Observable<any[]> {
    const params = new HttpParams().set('limit', limit.toString());
    return this.http.get<any[]>(`${this.API_URL}/geofences/${geofenceId}/events`, { headers: this.getHeaders(), params });
  }

  // ==================== MAINTENANCE ====================

  getMaintenanceRecords(vehicleId?: number): Observable<any[]> {
    if (this.isMockUser()) {
      return this.mockDataService.getMaintenanceRecords();
    }
    let params = new HttpParams();
    if (vehicleId) params = params.set('vehicleId', vehicleId.toString());
    return this.http.get<any[]>(`${this.API_URL}/maintenance`, { headers: this.getHeaders(), params });
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
    if (filters?.startDate) params = params.set('startDate', filters.startDate.toISOString());
    if (filters?.endDate) params = params.set('endDate', filters.endDate.toISOString());
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
    if (startDate) params = params.set('startDate', startDate.toISOString());
    if (endDate) params = params.set('endDate', endDate.toISOString());
    return this.http.get<any>(`${this.API_URL}/costs/summary`, { headers: this.getHeaders(), params });
  }

  createCost(cost: any): Observable<any> {
    if (this.isMockUser()) {
      return of(this.mockDataService.addVehicleCost(cost));
    }
    return this.http.post<any>(`${this.API_URL}/costs`, cost, { headers: this.getHeaders() });
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

  getVehicleHistory(vehicleId: number, from?: Date, to?: Date, limit = 10000): Observable<PositionDto[]> {
    let params = new HttpParams();
    if (from) params = params.set('from', from.toISOString());
    if (to) params = params.set('to', to.toISOString());
    params = params.set('limit', limit.toString());
    return this.http.get<PositionDto[]>(`${this.getMonitoringApiUrl()}/gps/vehicles/${vehicleId}/history`, { headers: this.getHeaders(), params });
  }

  getDeviceHistory(deviceUid: string, from?: Date, to?: Date, limit = 10000): Observable<PositionDto[]> {
    let params = new HttpParams();
    if (from) params = params.set('from', from.toISOString());
    if (to) params = params.set('to', to.toISOString());
    params = params.set('limit', limit.toString());
    return this.http.get<any[]>(`${this.getMonitoringApiUrl()}/gps/devices/${deviceUid}/history`, { headers: this.getHeaders(), params });
  }

  getVehicleGpsStats(vehicleId: number, from?: Date, to?: Date): Observable<any> {
    let params = new HttpParams();
    if (from) params = params.set('from', from.toISOString());
    if (to) params = params.set('to', to.toISOString());
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
    if (filters?.startDate) params = params.set('startDate', filters.startDate.toISOString());
    if (filters?.endDate) params = params.set('endDate', filters.endDate.toISOString());
    if (filters?.limit) params = params.set('limit', filters.limit.toString());
    return this.http.get<any[]>(`${this.API_URL}/trips`, { headers: this.getHeaders(), params });
  }

  // ==================== VEHICLE STOPS ====================

  getVehicleStops(vehicleId: number, startDate?: Date, endDate?: Date, pageSize = 500): Observable<VehicleStopsResult> {
    let params = new HttpParams()
      .set('vehicleId', vehicleId.toString())
      .set('pageSize', pageSize.toString());
    if (startDate) params = params.set('startDate', startDate.toISOString());
    if (endDate) params = params.set('endDate', endDate.toISOString());
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
    if (startDate) params = params.set('startDate', startDate.toISOString());
    if (endDate) params = params.set('endDate', endDate.toISOString());
    return this.http.get<any[]>(`${this.API_URL}/trips/vehicle/${vehicleId}`, { headers: this.getHeaders(), params });
  }

  getTripWaypoints(tripId: number): Observable<any[]> {
    return this.http.get<any[]>(`${this.API_URL}/trips/${tripId}/waypoints`, { headers: this.getHeaders() });
  }

  getTripsSummary(startDate?: Date, endDate?: Date): Observable<any> {
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', startDate.toISOString());
    if (endDate) params = params.set('endDate', endDate.toISOString());
    return this.http.get<any>(`${this.API_URL}/trips/summary`, { headers: this.getHeaders(), params });
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

  upgradeSubscription(subscriptionId: number, months = 1): Observable<any> {
    return this.http.post<any>(`${this.API_URL}/subscriptions/upgrade`, { subscriptionId, months }, { headers: this.getHeaders() });
  }

  // ==================== STATISTICS ====================

  getDailyStatistics(vehicleId?: number, startDate?: Date, endDate?: Date): Observable<any[]> {
    let params = new HttpParams();
    if (vehicleId) params = params.set('vehicleId', vehicleId.toString());
    if (startDate) params = params.set('startDate', startDate.toISOString());
    if (endDate) params = params.set('endDate', endDate.toISOString());
    return this.http.get<any[]>(`${this.API_URL}/statistics/daily`, { headers: this.getHeaders(), params });
  }

  getVehicleStatistics(vehicleId: number, startDate?: Date, endDate?: Date): Observable<any> {
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', startDate.toISOString());
    if (endDate) params = params.set('endDate', endDate.toISOString());
    return this.http.get<any>(`${this.API_URL}/statistics/vehicle/${vehicleId}`, { headers: this.getHeaders(), params });
  }

  getDriverScores(driverId?: number, startDate?: Date, endDate?: Date): Observable<any[]> {
    let params = new HttpParams();
    if (driverId) params = params.set('driverId', driverId.toString());
    if (startDate) params = params.set('startDate', startDate.toISOString());
    if (endDate) params = params.set('endDate', endDate.toISOString());
    return this.http.get<any[]>(`${this.API_URL}/statistics/drivers`, { headers: this.getHeaders(), params });
  }

  getDriverSummary(driverId: number, startDate?: Date, endDate?: Date): Observable<any> {
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', startDate.toISOString());
    if (endDate) params = params.set('endDate', endDate.toISOString());
    return this.http.get<any>(`${this.API_URL}/statistics/drivers/${driverId}/summary`, { headers: this.getHeaders(), params });
  }

  // ==================== USER SETTINGS ====================

  updateUserSettings(settings: any): Observable<void> {
    return this.http.put<void>(`${this.API_URL}/users/me/settings`, settings, { headers: this.getHeaders() });
  }

  getCurrentUserProfile(): Observable<any> {
    return this.http.get<any>(`${this.API_URL}/users/me`, { headers: this.getHeaders() });
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
    if (options.startDate) params = params.set('startDate', options.startDate.toISOString());
    if (options.endDate) params = params.set('endDate', options.endDate.toISOString());
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
    if (startDate) params = params.set('startDate', startDate.toISOString());
    if (endDate) params = params.set('endDate', endDate.toISOString());
    return this.http.get<FuelRecordsResult>(`${this.API_URL}/fuelrecords/vehicle/${vehicleId}`, { headers: this.getHeaders(), params });
  }

  getFuelRefuels(vehicleId?: number, startDate?: Date, endDate?: Date): Observable<FuelRecordsResult> {
    let params = new HttpParams();
    if (vehicleId) params = params.set('vehicleId', vehicleId.toString());
    if (startDate) params = params.set('startDate', startDate.toISOString());
    if (endDate) params = params.set('endDate', endDate.toISOString());
    return this.http.get<FuelRecordsResult>(`${this.API_URL}/fuelrecords/refuels`, { headers: this.getHeaders(), params });
  }

  getFuelAnomalies(vehicleId?: number, startDate?: Date, endDate?: Date): Observable<FuelRecordsResult> {
    let params = new HttpParams();
    if (vehicleId) params = params.set('vehicleId', vehicleId.toString());
    if (startDate) params = params.set('startDate', startDate.toISOString());
    if (endDate) params = params.set('endDate', endDate.toISOString());
    return this.http.get<FuelRecordsResult>(`${this.API_URL}/fuelrecords/anomalies`, { headers: this.getHeaders(), params });
  }

  getFuelReport(vehicleId: number, startDate?: Date, endDate?: Date): Observable<FuelReport> {
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', startDate.toISOString());
    if (endDate) params = params.set('endDate', endDate.toISOString());
    return this.http.get<FuelReport>(`${this.API_URL}/fuelrecords/vehicle/${vehicleId}/report`, { headers: this.getHeaders(), params });
  }

  // ==================== DAILY ACTIVITY REPORTS ====================

  getDailyReport(vehicleId: number, date?: Date, minStopDurationSeconds?: number): Observable<DailyActivityReport> {
    let params = new HttpParams();
    if (date) params = params.set('date', date.toISOString().split('T')[0]);
    if (minStopDurationSeconds) params = params.set('minStopDurationSeconds', minStopDurationSeconds.toString());
    return this.http.get<DailyActivityReport>(`${this.API_URL}/reports/daily/${vehicleId}`, { headers: this.getHeaders(), params });
  }

  getDailyReports(date?: Date, vehicleIds?: number[], minStopDurationSeconds?: number): Observable<DailyActivityReport[]> {
    let params = new HttpParams();
    if (date) params = params.set('date', date.toISOString().split('T')[0]);
    if (vehicleIds?.length) {
      vehicleIds.forEach(id => params = params.append('vehicleIds', id.toString()));
    }
    if (minStopDurationSeconds) params = params.set('minStopDurationSeconds', minStopDurationSeconds.toString());
    return this.http.get<DailyActivityReport[]>(`${this.API_URL}/reports/daily`, { headers: this.getHeaders(), params });
  }

  // ==================== MILEAGE REPORTS ====================

  getMileageReport(vehicleId: number, startDate?: Date, endDate?: Date): Observable<MileageReport> {
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', startDate.toISOString().split('T')[0]);
    if (endDate) params = params.set('endDate', endDate.toISOString().split('T')[0]);
    return this.http.get<MileageReport>(`${this.API_URL}/reports/mileage/${vehicleId}`, { headers: this.getHeaders(), params });
  }

  getMileageReports(startDate?: Date, endDate?: Date, vehicleIds?: number[]): Observable<MileageReport[]> {
    let params = new HttpParams();
    if (startDate) params = params.set('startDate', startDate.toISOString().split('T')[0]);
    if (endDate) params = params.set('endDate', endDate.toISOString().split('T')[0]);
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

  // ==================== MILEAGE PERIOD REPORTS (Hour/Day/Month) ====================

  getMileagePeriodReport(
    vehicleId: number, 
    periodType: 'hour' | 'day' | 'month' = 'day',
    startDate?: Date, 
    endDate?: Date
  ): Observable<MileagePeriodReport> {
    let params = new HttpParams().set('periodType', periodType);
    if (startDate) params = params.set('startDate', startDate.toISOString().split('T')[0]);
    if (endDate) params = params.set('endDate', endDate.toISOString().split('T')[0]);
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

  getRenewalHistory(vehicleId: number): Observable<RenewalHistoryDto[]> {
    return this.http.get<RenewalHistoryDto[]>(`${this.API_URL}/documents/vehicle/${vehicleId}/history`, { headers: this.getHeaders() });
  }

  getExpiryAlerts(daysThreshold = 30): Observable<VehicleExpiryDto[]> {
    const params = new HttpParams().set('daysThreshold', daysThreshold.toString());
    return this.http.get<VehicleExpiryDto[]>(`${this.API_URL}/documents/alerts`, { headers: this.getHeaders(), params });
  }

  // ==================== ACCIDENT CLAIMS ====================

  getAccidentClaims(options?: { searchTerm?: string; status?: string; severity?: string; vehicleId?: number; page?: number; pageSize?: number }): Observable<PaginatedResult<AccidentClaimDto>> {
    let params = new HttpParams();
    if (options?.searchTerm) params = params.set('searchTerm', options.searchTerm);
    if (options?.status) params = params.set('status', options.status);
    if (options?.severity) params = params.set('severity', options.severity);
    if (options?.vehicleId) params = params.set('vehicleId', options.vehicleId.toString());
    if (options?.page) params = params.set('page', options.page.toString());
    if (options?.pageSize) params = params.set('pageSize', options.pageSize.toString());
    return this.http.get<PaginatedResult<AccidentClaimDto>>(`${this.API_URL}/accident-claims`, { headers: this.getHeaders(), params });
  }

  getAccidentClaim(id: number): Observable<AccidentClaimDto> {
    return this.http.get<AccidentClaimDto>(`${this.API_URL}/accident-claims/${id}`, { headers: this.getHeaders() });
  }

  getAccidentClaimStats(): Observable<AccidentClaimStatsDto> {
    return this.http.get<AccidentClaimStatsDto>(`${this.API_URL}/accident-claims/stats`, { headers: this.getHeaders() });
  }

  getVehicleAccidentClaims(vehicleId: number): Observable<PaginatedResult<AccidentClaimDto>> {
    return this.http.get<PaginatedResult<AccidentClaimDto>>(`${this.API_URL}/accident-claims/vehicle/${vehicleId}`, { headers: this.getHeaders() });
  }

  createAccidentClaim(claim: CreateAccidentClaimRequest): Observable<number> {
    return this.http.post<number>(`${this.API_URL}/accident-claims`, claim, { headers: this.getHeaders() });
  }

  updateAccidentClaim(id: number, claim: UpdateAccidentClaimRequest): Observable<void> {
    return this.http.put<void>(`${this.API_URL}/accident-claims/${id}`, claim, { headers: this.getHeaders() });
  }

  deleteAccidentClaim(id: number): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/accident-claims/${id}`, { headers: this.getHeaders() });
  }

  submitAccidentClaim(id: number): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.API_URL}/accident-claims/${id}/submit`, {}, { headers: this.getHeaders() });
  }

  approveAccidentClaim(id: number, approvedAmount: number): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.API_URL}/accident-claims/${id}/approve`, { approvedAmount }, { headers: this.getHeaders() });
  }

  rejectAccidentClaim(id: number, reason?: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.API_URL}/accident-claims/${id}/reject`, { reason }, { headers: this.getHeaders() });
  }

  closeAccidentClaim(id: number): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.API_URL}/accident-claims/${id}/close`, {}, { headers: this.getHeaders() });
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

  markMaintenanceDone(request: MarkMaintenanceDoneRequest): Observable<{ logId: number; message: string }> {
    return this.http.post<{ logId: number; message: string }>(`${this.API_URL}/vehicle-maintenance/mark-done`, request, { headers: this.getHeaders() });
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
  type: 'drive' | 'stop';
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

// ==================== ACCIDENT CLAIMS ====================
export interface AccidentClaimDto {
  id: number;
  claimNumber: string;
  vehicleId: number;
  vehicleName: string;
  vehiclePlate?: string;
  driverId?: number;
  driverName?: string;
  accidentDate: string;
  accidentTime: string;
  location: string;
  latitude?: number;
  longitude?: number;
  description: string;
  severity: string;
  estimatedDamage: number;
  approvedAmount?: number;
  status: string;
  thirdPartyInvolved: boolean;
  policeReportNumber?: string;
  mileageAtAccident?: number;
  damagedZones?: string[];
  createdAt: string;
  updatedAt: string;
  thirdParties: AccidentClaimThirdPartyDto[];
  documents: AccidentClaimDocumentDto[];
}

export interface AccidentClaimThirdPartyDto {
  id: number;
  name?: string;
  phone?: string;
  vehiclePlate?: string;
  vehicleModel?: string;
  insuranceCompany?: string;
  insuranceNumber?: string;
}

export interface AccidentClaimDocumentDto {
  id: number;
  documentType: string;
  fileName: string;
  fileUrl: string;
  fileSize?: number;
  mimeType?: string;
  uploadedAt: string;
}

export interface AccidentClaimStatsDto {
  totalClaims: number;
  draftCount: number;
  submittedCount: number;
  underReviewCount: number;
  approvedCount: number;
  rejectedCount: number;
  closedCount: number;
  totalEstimatedDamage: number;
  totalApprovedAmount: number;
}

export interface CreateAccidentClaimRequest {
  vehicleId: number;
  driverId?: number;
  accidentDate: string;
  accidentTime: string;
  location: string;
  latitude?: number;
  longitude?: number;
  description: string;
  severity: string;
  estimatedDamage: number;
  damagedZones?: string[];
  thirdPartyInvolved?: boolean;
  thirdPartyName?: string;
  thirdPartyPhone?: string;
  thirdPartyVehiclePlate?: string;
  thirdPartyVehicleModel?: string;
  thirdPartyInsurance?: string;
  thirdPartyInsuranceNumber?: string;
  policeReportNumber?: string;
  mileageAtAccident?: number;
  witnesses?: string;
  additionalNotes?: string;
}

export interface UpdateAccidentClaimRequest {
  vehicleId?: number;
  driverId?: number;
  accidentDate?: string;
  accidentTime?: string;
  location?: string;
  latitude?: number;
  longitude?: number;
  description?: string;
  severity?: string;
  estimatedDamage?: number;
  damagedZones?: string[];
  thirdPartyInvolved?: boolean;
  policeReportNumber?: string;
  mileageAtAccident?: number;
  witnesses?: string;
  additionalNotes?: string;
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
}
