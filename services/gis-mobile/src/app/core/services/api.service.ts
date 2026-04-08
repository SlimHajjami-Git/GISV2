import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Vehicle, Geofence, VehicleTrip, DashboardStats, Notification } from '../models/types';
import { AuthService } from './auth.service';

@Injectable({
  providedIn: 'root'
})
export class ApiService {
  private get API(): string {
    return this.authService.API_URL;
  }

  constructor(private http: HttpClient, private authService: AuthService) {}

  // ─── Dashboard ───────────────────────────────────────────
  getDashboardStats(): Observable<DashboardStats> {
    return this.http.get<DashboardStats>(`${this.API}/dashboard/stats`);
  }

  getDashboardKpis(): Observable<any> {
    return this.http.get<any>(`${this.API}/dashboard/kpis`);
  }

  // ─── Vehicles ────────────────────────────────────────────
  getVehicles(): Observable<Vehicle[]> {
    return this.http.get<Vehicle[]>(`${this.API}/vehicles`);
  }

  getVehicle(id: string): Observable<Vehicle> {
    return this.http.get<Vehicle>(`${this.API}/vehicles/${id}`);
  }

  // ─── GPS / Positions ────────────────────────────────────
  getVehiclePositions(): Observable<any[]> {
    return this.http.get<any[]>(`${this.API}/gps/positions/realtime`);
  }

  getVehicleHistory(vehicleId: string, from: string, to: string, maxPoints: number = 5000): Observable<any> {
    const params = new HttpParams()
      .set('from', from)
      .set('to', to)
      .set('maxPoints', maxPoints.toString());
    return this.http.get<any>(`${this.API}/gps/vehicles/${vehicleId}/history`, { params });
  }

  getLastPositions(): Observable<any[]> {
    return this.http.get<any[]>(`${this.API}/gps/positions/latest`);
  }

  // ─── Trips ──────────────────────────────────────────────
  getVehicleTrips(vehicleId: string, from: string, to: string): Observable<VehicleTrip[]> {
    const params = new HttpParams().set('from', from).set('to', to);
    return this.http.get<VehicleTrip[]>(`${this.API}/trips/vehicle/${vehicleId}`, { params });
  }

  // ─── Geofences ──────────────────────────────────────────
  getGeofences(): Observable<Geofence[]> {
    return this.http.get<Geofence[]>(`${this.API}/geofences`);
  }

  // ─── Notifications ──────────────────────────────────────
  getNotifications(page: number = 1, pageSize: number = 20): Observable<any> {
    const params = new HttpParams()
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());
    return this.http.get<any>(`${this.API}/notifications`, { params });
  }

  markNotificationRead(id: number): Observable<any> {
    return this.http.put(`${this.API}/notifications/${id}/read`, {});
  }

  markAllNotificationsRead(): Observable<any> {
    return this.http.put(`${this.API}/notifications/read-all`, {});
  }

  getUnreadCount(): Observable<{ count: number }> {
    return this.http.get<{ count: number }>(`${this.API}/notifications/unread-count`);
  }

  // ─── Alerts ─────────────────────────────────────────────
  getAlerts(from?: string, to?: string): Observable<any[]> {
    let params = new HttpParams();
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    return this.http.get<any[]>(`${this.API}/alerts`, { params });
  }

  // ─── Reports ────────────────────────────────────────────
  getDailyReports(date: string, vehicleIds?: number[]): Observable<any[]> {
    let params = new HttpParams().set('date', date);
    if (vehicleIds?.length) {
      vehicleIds.forEach(id => params = params.append('vehicleIds', id.toString()));
    }
    return this.http.get<any[]>(`${this.API}/reports/daily`, { params });
  }

  getDailyReport(vehicleId: number, date: string): Observable<any> {
    const params = new HttpParams().set('date', date);
    return this.http.get<any>(`${this.API}/reports/daily/${vehicleId}`, { params });
  }

  getMileageReports(startDate: string, endDate: string, vehicleIds?: number[]): Observable<any[]> {
    let params = new HttpParams().set('startDate', startDate).set('endDate', endDate);
    if (vehicleIds?.length) {
      vehicleIds.forEach(id => params = params.append('vehicleIds', id.toString()));
    }
    return this.http.get<any[]>(`${this.API}/reports/mileage`, { params });
  }

  getMileageReport(vehicleId: number, startDate: string, endDate: string): Observable<any> {
    const params = new HttpParams().set('startDate', startDate).set('endDate', endDate);
    return this.http.get<any>(`${this.API}/reports/mileage/${vehicleId}`, { params });
  }

  getMonthlyFleetReport(year?: number, month?: number): Observable<any> {
    let params = new HttpParams();
    if (year) params = params.set('year', year.toString());
    if (month) params = params.set('month', month.toString());
    return this.http.get<any>(`${this.API}/reports/monthly`, { params });
  }

  getTrips(vehicleId?: number, startDate?: string, endDate?: string, limit: number = 50): Observable<any[]> {
    let params = new HttpParams().set('limit', limit.toString());
    if (vehicleId) params = params.set('vehicleId', vehicleId.toString());
    if (startDate) params = params.set('startDate', startDate);
    if (endDate) params = params.set('endDate', endDate);
    return this.http.get<any[]>(`${this.API}/trips`, { params });
  }

  getTripsSummary(startDate: string, endDate: string): Observable<any> {
    const params = new HttpParams().set('startDate', startDate).set('endDate', endDate);
    return this.http.get<any>(`${this.API}/trips/summary`, { params });
  }

  // ─── Maintenance ────────────────────────────────────────
  getMaintenanceAlerts(): Observable<any[]> {
    return this.http.get<any[]>(`${this.API}/maintenance-scheduler/alerts`);
  }

  // ─── User Profile ───────────────────────────────────────
  updateProfile(data: { firstName?: string; lastName?: string; phone?: string }): Observable<any> {
    return this.http.put(`${this.API}/users/profile`, data);
  }

  changePassword(data: { currentPassword: string; newPassword: string }): Observable<any> {
    return this.http.put(`${this.API}/users/change-password`, data);
  }

  // ─── Immobilization ─────────────────────────────────────
  getImmobilizationState(deviceId: number): Observable<any> {
    return this.http.get<any>(`${this.API}/gps/devices/${deviceId}/immobilization`);
  }

  stopVehicle(deviceId: number): Observable<any> {
    return this.http.post<any>(`${this.API}/gps/devices/${deviceId}/stop`, {});
  }

  goVehicle(deviceId: number): Observable<any> {
    return this.http.post<any>(`${this.API}/gps/devices/${deviceId}/go`, {});
  }

  getDeviceCommands(deviceId: number, limit: number = 20): Observable<any[]> {
    return this.http.get<any[]>(`${this.API}/gps/devices/${deviceId}/commands?limit=${limit}`);
  }

  verifyPassword(password: string): Observable<any> {
    return this.http.post<any>(`${this.API}/auth/verify-password`, { password });
  }

  // ─── Immobilization Approval ────────────────────────────
  approveImmobilization(requestId: number): Observable<any> {
    return this.http.post<any>(`${this.API}/gps/immobilization-requests/${requestId}/approve`, {});
  }

  rejectImmobilization(requestId: number): Observable<any> {
    return this.http.post<any>(`${this.API}/gps/immobilization-requests/${requestId}/reject`, {});
  }

  getPendingImmobilizationRequests(): Observable<any[]> {
    return this.http.get<any[]>(`${this.API}/gps/immobilization-requests/pending`);
  }
}
