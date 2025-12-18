import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Observable, BehaviorSubject, tap, of, map } from 'rxjs';
import { MockDataService } from './mock-data.service';

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
    // Always use relative path - nginx proxies to backend
    return '/api';
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
    if (this.isMockUser()) {
      return this.mockDataService.getGPSLocations();
    }
    return this.http.get<any[]>(`${this.API_URL}/vehicles/locations`, { headers: this.getHeaders() });
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
    return this.http.get<any[]>(`${this.API_URL}/gpsdevices/unassigned`, { headers: this.getHeaders() });
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
}
