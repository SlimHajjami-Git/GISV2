import { Injectable, OnDestroy } from '@angular/core';
import { HttpClient, HttpParams, HttpErrorResponse } from '@angular/common/http';
import { Observable, BehaviorSubject, Subject, throwError, of, timer } from 'rxjs';
import { catchError, retry, tap, map, shareReplay, takeUntil, switchMap } from 'rxjs/operators';
import { environment } from '../environments/environment';

export interface VehiclePosition {
  id: number;
  vehicleId: number;
  vehicleName: string;
  plate: string;
  latitude: number;
  longitude: number;
  speedKph: number;
  courseDeg: number;
  ignitionOn: boolean;
  recordedAt: string;
  address?: string;
  fuelRaw?: number;
  odometerKm?: number;
  isOnline: boolean;
}

export interface PositionHistoryItem {
  id: number;
  latitude: number;
  longitude: number;
  speedKph: number;
  courseDeg: number;
  ignitionOn: boolean;
  recordedAt: string;
  address?: string;
  fuelRaw?: number;
  odometerKm?: number;
  temperature?: number;
}

export interface FleetOverview {
  totalVehicles: number;
  onlineVehicles: number;
  movingVehicles: number;
  stoppedVehicles: number;
  offlineVehicles: number;
  lastUpdated: string;
}

export interface VehicleStats {
  vehicleId: number;
  totalDistance: number;
  totalDuration: number;
  maxSpeed: number;
  avgSpeed: number;
  fuelConsumed?: number;
  tripCount: number;
  startDate: string;
  endDate: string;
}

export interface VehicleWithPosition {
  id: number;
  name: string;
  plate: string;
  brand: string;
  model: string;
  type: string;
  status: string;
  hasGPS: boolean;
  isOnline: boolean;
  lastPosition?: {
    latitude: number;
    longitude: number;
    speedKph: number;
    courseDeg: number;
    ignitionOn: boolean;
    recordedAt: string;
    address?: string;
  };
}

interface CacheEntry<T> {
  data: T;
  timestamp: number;
}

@Injectable({
  providedIn: 'root'
})
export class MonitoringApiService implements OnDestroy {
  private readonly destroy$ = new Subject<void>();
  private readonly baseUrl = environment.apiUrl;

  private readonly cache = new Map<string, CacheEntry<unknown>>();
  private readonly CACHE_DURATION = 5000;

  private readonly loadingSubject = new BehaviorSubject<boolean>(false);
  public readonly loading$ = this.loadingSubject.asObservable();

  private readonly errorSubject = new BehaviorSubject<string | null>(null);
  public readonly error$ = this.errorSubject.asObservable();

  constructor(private http: HttpClient) {}

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    this.cache.clear();
  }

  private getAuthHeaders(): { [key: string]: string } {
    const token = localStorage.getItem('auth_token');
    return token ? { Authorization: `Bearer ${token}` } : {};
  }

  private handleError(error: HttpErrorResponse): Observable<never> {
    let errorMessage = 'An error occurred';

    if (error.status === 0) {
      errorMessage = 'Network error - please check your connection';
    } else if (error.status === 401) {
      errorMessage = 'Authentication failed - please log in again';
    } else if (error.status === 403) {
      errorMessage = 'Access denied';
    } else if (error.status === 404) {
      errorMessage = 'Resource not found';
    } else if (error.status >= 500) {
      errorMessage = 'Server error - please try again later';
    } else if (error.error?.message) {
      errorMessage = error.error.message;
    }

    this.errorSubject.next(errorMessage);
    console.error('Monitoring API Error:', errorMessage, error);

    return throwError(() => new Error(errorMessage));
  }

  private getCached<T>(key: string): T | null {
    const entry = this.cache.get(key) as CacheEntry<T> | undefined;
    if (entry && Date.now() - entry.timestamp < this.CACHE_DURATION) {
      return entry.data;
    }
    this.cache.delete(key);
    return null;
  }

  private setCache<T>(key: string, data: T): void {
    this.cache.set(key, { data, timestamp: Date.now() });
  }

  clearError(): void {
    this.errorSubject.next(null);
  }

  getLatestPositions(): Observable<VehiclePosition[]> {
    const cacheKey = 'latest_positions';
    const cached = this.getCached<VehiclePosition[]>(cacheKey);
    if (cached) {
      return of(cached);
    }

    this.loadingSubject.next(true);

    return this.http.get<VehiclePosition[]>(
      `${this.baseUrl}/gps/positions/latest`,
      { headers: this.getAuthHeaders() }
    ).pipe(
      retry({ count: 2, delay: 1000 }),
      tap(data => {
        this.setCache(cacheKey, data);
        this.loadingSubject.next(false);
        this.clearError();
      }),
      catchError(error => {
        this.loadingSubject.next(false);
        return this.handleError(error);
      }),
      takeUntil(this.destroy$)
    );
  }

  getVehiclePosition(vehicleId: number): Observable<VehiclePosition> {
    return this.http.get<VehiclePosition>(
      `${this.baseUrl}/gps/vehicles/${vehicleId}/position`,
      { headers: this.getAuthHeaders() }
    ).pipe(
      retry({ count: 2, delay: 1000 }),
      catchError(error => this.handleError(error)),
      takeUntil(this.destroy$)
    );
  }

  getVehicleHistory(
    vehicleId: number,
    from?: Date,
    to?: Date,
    limit = 10000
  ): Observable<PositionHistoryItem[]> {
    let params = new HttpParams().set('limit', limit.toString());

    if (from) {
      params = params.set('from', from.toISOString());
    }
    if (to) {
      params = params.set('to', to.toISOString());
    }

    this.loadingSubject.next(true);

    return this.http.get<PositionHistoryItem[]>(
      `${this.baseUrl}/gps/vehicles/${vehicleId}/history`,
      { headers: this.getAuthHeaders(), params }
    ).pipe(
      tap(() => this.loadingSubject.next(false)),
      map(positions => this.filterInvalidPositions(positions)),
      catchError(error => {
        this.loadingSubject.next(false);
        return this.handleError(error);
      }),
      takeUntil(this.destroy$)
    );
  }

  getDeviceHistory(
    deviceUid: string,
    from?: Date,
    to?: Date,
    limit = 10000
  ): Observable<PositionHistoryItem[]> {
    let params = new HttpParams().set('limit', limit.toString());

    if (from) {
      params = params.set('from', from.toISOString());
    }
    if (to) {
      params = params.set('to', to.toISOString());
    }

    return this.http.get<PositionHistoryItem[]>(
      `${this.baseUrl}/gps/devices/${deviceUid}/history`,
      { headers: this.getAuthHeaders(), params }
    ).pipe(
      map(positions => this.filterInvalidPositions(positions)),
      catchError(error => this.handleError(error)),
      takeUntil(this.destroy$)
    );
  }

  getVehicleStats(
    vehicleId: number,
    from?: Date,
    to?: Date
  ): Observable<VehicleStats> {
    let params = new HttpParams();

    if (from) {
      params = params.set('from', from.toISOString());
    }
    if (to) {
      params = params.set('to', to.toISOString());
    }

    return this.http.get<VehicleStats>(
      `${this.baseUrl}/gps/vehicles/${vehicleId}/stats`,
      { headers: this.getAuthHeaders(), params }
    ).pipe(
      catchError(error => this.handleError(error)),
      takeUntil(this.destroy$)
    );
  }

  getFleetOverview(): Observable<FleetOverview> {
    const cacheKey = 'fleet_overview';
    const cached = this.getCached<FleetOverview>(cacheKey);
    if (cached) {
      return of(cached);
    }

    return this.http.get<FleetOverview>(
      `${this.baseUrl}/gps/fleet/overview`,
      { headers: this.getAuthHeaders() }
    ).pipe(
      tap(data => this.setCache(cacheKey, data)),
      catchError(error => this.handleError(error)),
      takeUntil(this.destroy$)
    );
  }

  getVehiclesWithPositions(): Observable<VehicleWithPosition[]> {
    this.loadingSubject.next(true);

    return this.http.get<VehicleWithPosition[]>(
      `${this.baseUrl}/vehicles/with-positions`,
      { headers: this.getAuthHeaders() }
    ).pipe(
      retry({ count: 2, delay: 1000 }),
      tap(() => {
        this.loadingSubject.next(false);
        this.clearError();
      }),
      catchError(error => {
        this.loadingSubject.next(false);
        return this.handleError(error);
      }),
      takeUntil(this.destroy$)
    );
  }

  startPolling(intervalMs = 30000): Observable<VehiclePosition[]> {
    return timer(0, intervalMs).pipe(
      switchMap(() => this.getLatestPositions()),
      takeUntil(this.destroy$)
    );
  }

  private filterInvalidPositions(positions: PositionHistoryItem[]): PositionHistoryItem[] {
    if (!positions || positions.length < 2) {
      return positions || [];
    }

    const filtered: PositionHistoryItem[] = [];
    const MAX_SPEED_KPH = 200;

    for (let i = 0; i < positions.length; i++) {
      const current = positions[i];

      if (i === 0) {
        filtered.push(current);
        continue;
      }

      const previous = filtered[filtered.length - 1];
      const distance = this.calculateDistance(
        previous.latitude, previous.longitude,
        current.latitude, current.longitude
      );

      const timeDiff = (
        new Date(current.recordedAt).getTime() -
        new Date(previous.recordedAt).getTime()
      ) / 1000 / 3600;

      if (timeDiff > 0) {
        const impliedSpeed = distance / timeDiff;
        if (impliedSpeed <= MAX_SPEED_KPH) {
          filtered.push(current);
        }
      } else {
        filtered.push(current);
      }
    }

    return filtered;
  }

  private calculateDistance(
    lat1: number, lon1: number,
    lat2: number, lon2: number
  ): number {
    const R = 6371;
    const dLat = this.toRad(lat2 - lat1);
    const dLon = this.toRad(lon2 - lon1);
    const a =
      Math.sin(dLat / 2) * Math.sin(dLat / 2) +
      Math.cos(this.toRad(lat1)) * Math.cos(this.toRad(lat2)) *
      Math.sin(dLon / 2) * Math.sin(dLon / 2);
    const c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
    return R * c;
  }

  private toRad(deg: number): number {
    return deg * (Math.PI / 180);
  }
}
