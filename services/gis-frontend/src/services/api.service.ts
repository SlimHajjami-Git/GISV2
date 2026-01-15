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
    return this.http.get<any[]>(`${this.API_URL}/geofences`, { headers: this.getHeaders() });
  }

  getGeofence(id: number): Observable<any> {
    if (this.isMockUser()) {
      return this.mockDataService.getGeofences().pipe(
        map(geofences => geofences.find(g => g.id === id.toString()))
      );
    }
    return this.http.get<any>(`${this.API_URL}/geofences/${id}`, { headers: this.getHeaders() });
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
