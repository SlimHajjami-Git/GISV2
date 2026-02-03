import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../environments/environment';

// Interfaces
export interface MaintenanceAlertDto {
  id: number;
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
  total: number;
  overdue: number;
  due: number;
  upcoming: number;
  ok: number;
}

export interface VehicleScheduleDto {
  id: number;
  templateId: number;
  templateName: string;
  category: string;
  priority: string;
  lastDoneDate?: string;
  lastDoneKm?: number;
  nextDueDate?: string;
  nextDueKm?: number;
  status: string;
  isPaused: boolean;
  pausedReason?: string;
  customIntervalKm?: number;
  customIntervalMonths?: number;
  kmRemaining?: number;
  daysRemaining?: number;
  estimatedCost?: number;
}

export interface MaintenanceNotificationDto {
  id: number;
  scheduleId: number;
  vehicleId: number;
  vehicleName: string;
  vehiclePlate?: string;
  maintenanceName: string;
  notificationType: string;
  triggerReason: string;
  kmRemaining?: number;
  daysRemaining?: number;
  createdAt: string;
  acknowledgedAt?: string;
}

export interface TemplatePartDto {
  id: number;
  partName: string;
  partNumber?: string;
  quantity: number;
  unit: string;
  estimatedUnitCost?: number;
  isRequired: boolean;
  preferredSupplierId?: number;
  supplierName?: string;
}

export interface VehicleMileageDto {
  vehicleId: number;
  currentKm: number;
  source: string;
  lastGpsUpdate?: string;
}

export interface MaintenanceTemplateDto {
  id: number;
  name: string;
  description?: string;
  category: string;
  priority: string;
  intervalKm?: number;
  intervalMonths?: number;
  warningKm: number;
  warningDays: number;
  estimatedCost?: number;
  estimatedDurationMinutes?: number;
  icon: string;
  partsCount?: number;
}

export interface MarkDoneRequest {
  vehicleId: number;
  templateId: number;
  date: string;
  mileage: number;
  cost: number;
  supplierId?: number;
  notes?: string;
  technicianName?: string;
  workOrderNumber?: string;
  laborCost?: number;
  partsCost?: number;
  partsReplaced?: PartReplacedRequest[];
  qualityRating?: number;
}

export interface PartReplacedRequest {
  name: string;
  partNumber?: string;
  quantity: number;
  unitCost: number;
}

export interface AddPartRequest {
  partName: string;
  partNumber?: string;
  quantity: number;
  estimatedUnitCost?: number;
  isRequired: boolean;
  preferredSupplierId?: number;
}

@Injectable({
  providedIn: 'root'
})
export class MaintenanceService {
  private readonly API_URL = environment.apiUrl || 'http://localhost:5000';

  constructor(private http: HttpClient) {}

  // ========== Alerts & Stats ==========

  getAlerts(): Observable<MaintenanceAlertDto[]> {
    return this.http.get<MaintenanceAlertDto[]>(`${this.API_URL}/maintenance/alerts`);
  }

  getStats(): Observable<MaintenanceStatsDto> {
    return this.http.get<MaintenanceStatsDto>(`${this.API_URL}/maintenance/stats`);
  }

  getNotifications(unacknowledgedOnly = true): Observable<MaintenanceNotificationDto[]> {
    return this.http.get<MaintenanceNotificationDto[]>(
      `${this.API_URL}/maintenance/notifications?unacknowledgedOnly=${unacknowledgedOnly}`
    );
  }

  acknowledgeNotification(id: number): Observable<void> {
    return this.http.post<void>(`${this.API_URL}/maintenance/notifications/${id}/acknowledge`, {});
  }

  // ========== Templates ==========

  getTemplates(): Observable<MaintenanceTemplateDto[]> {
    return this.http.get<{ items: MaintenanceTemplateDto[], totalCount: number }>(`${this.API_URL}/maintenance-templates`).pipe(
      map(response => response.items || [])
    );
  }

  getTemplateParts(templateId: number): Observable<TemplatePartDto[]> {
    return this.http.get<TemplatePartDto[]>(`${this.API_URL}/maintenance/templates/${templateId}/parts`);
  }

  addTemplatePart(templateId: number, request: AddPartRequest): Observable<number> {
    return this.http.post<number>(`${this.API_URL}/maintenance/templates/${templateId}/parts`, request);
  }

  removePart(partId: number): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/maintenance/parts/${partId}`);
  }

  // ========== Vehicle Schedules ==========

  getVehicleSchedules(vehicleId: number): Observable<VehicleScheduleDto[]> {
    return this.http.get<VehicleScheduleDto[]>(`${this.API_URL}/maintenance/vehicles/${vehicleId}/schedules`);
  }

  getVehicleMileage(vehicleId: number): Observable<VehicleMileageDto> {
    return this.http.get<VehicleMileageDto>(`${this.API_URL}/maintenance/vehicles/${vehicleId}/mileage`);
  }

  assignTemplate(vehicleId: number, templateId: number): Observable<number> {
    return this.http.post<number>(`${this.API_URL}/maintenance/vehicles/${vehicleId}/schedules`, { templateId });
  }

  removeSchedule(scheduleId: number): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/maintenance/schedules/${scheduleId}`);
  }

  pauseSchedule(scheduleId: number, reason?: string): Observable<void> {
    return this.http.post<void>(`${this.API_URL}/maintenance/schedules/${scheduleId}/pause`, { reason });
  }

  resumeSchedule(scheduleId: number): Observable<void> {
    return this.http.post<void>(`${this.API_URL}/maintenance/schedules/${scheduleId}/resume`, {});
  }

  updateScheduleIntervals(scheduleId: number, customIntervalKm?: number, customIntervalMonths?: number, notes?: string): Observable<void> {
    return this.http.put<void>(`${this.API_URL}/maintenance/schedules/${scheduleId}/intervals`, {
      customIntervalKm,
      customIntervalMonths,
      notes
    });
  }

  markAsDone(scheduleId: number, request: MarkDoneRequest): Observable<number> {
    return this.http.post<number>(`${this.API_URL}/maintenance/schedules/${scheduleId}/done`, request);
  }

  // ========== Helpers ==========

  getStatusColor(status: string): string {
    switch (status) {
      case 'ok': return 'green';
      case 'upcoming': return 'yellow';
      case 'due': return 'orange';
      case 'critical': return 'red';
      case 'overdue': return 'red';
      default: return 'gray';
    }
  }

  getStatusLabel(status: string): string {
    switch (status) {
      case 'ok': return 'OK';
      case 'upcoming': return 'À venir';
      case 'due': return 'Dû';
      case 'critical': return 'Critique';
      case 'overdue': return 'En retard';
      default: return status;
    }
  }

  getStatusIcon(status: string): string {
    switch (status) {
      case 'ok': return '✅';
      case 'upcoming': return '🔔';
      case 'due': return '⚠️';
      case 'critical': return '🚨';
      case 'overdue': return '❌';
      default: return '❓';
    }
  }

  getPriorityColor(priority: string): string {
    switch (priority) {
      case 'low': return 'gray';
      case 'medium': return 'blue';
      case 'high': return 'orange';
      case 'critical': return 'red';
      default: return 'gray';
    }
  }

  formatKm(km: number | undefined): string {
    if (km === undefined || km === null) return '-';
    return km.toLocaleString('fr-FR') + ' km';
  }

  formatDays(days: number | undefined): string {
    if (days === undefined || days === null) return '-';
    if (days < 0) return `${Math.abs(days)} j en retard`;
    if (days === 0) return "Aujourd'hui";
    if (days === 1) return 'Demain';
    return `${days} jours`;
  }
}
