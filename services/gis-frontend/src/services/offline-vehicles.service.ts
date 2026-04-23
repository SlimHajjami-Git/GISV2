import { Injectable, OnDestroy } from '@angular/core';
import { BehaviorSubject, Observable, Subscription, timer } from 'rxjs';
import { MonitoringApiService, VehicleWithPosition } from './monitoring-api.service';
import { SignalRService, PositionUpdate } from './signalr.service';

/**
 * Global store for offline vehicles — shared across the app so the header bell
 * stays in sync with the monitoring page without having to mount that page.
 *
 * Sources of truth (mirrors what monitoring.component.ts does):
 *   1. Initial fetch: GET /api/vehicles/with-positions (isOnline computed server-side)
 *   2. Real-time: SignalR positionUpdate$ → flip a vehicle to online when a frame arrives
 *   3. Background poll every 30s — catches vehicles going offline (timeout-based, no positive signal)
 *   4. "Intensified" poll every 5s while the dropdown is open — the popup stays live
 */
@Injectable({
  providedIn: 'root'
})
export class OfflineVehiclesService implements OnDestroy {
  /** Background polling while the app is running but dropdown is closed */
  private readonly BACKGROUND_POLL_MS = 30000;
  /** Fast polling while the dropdown is open so the user sees instant updates */
  private readonly LIVE_POLL_MS = 5000;

  private subscriptions: Subscription[] = [];
  private pollSubscription: Subscription | null = null;
  private liveSubscription: Subscription | null = null;
  private started = false;

  /** Full list of vehicles currently offline (with last position info) */
  public offlineVehicles$ = new BehaviorSubject<VehicleWithPosition[]>([]);
  /** Derived count — same BehaviorSubject pattern as NotificationService.unreadCount$ */
  public offlineCount$ = new BehaviorSubject<number>(0);

  constructor(
    private monitoringApi: MonitoringApiService,
    private signalR: SignalRService
  ) {}

  ngOnDestroy(): void {
    this.stop();
  }

  /** Call after login (from app-layout ngOnInit). Idempotent. */
  start(): void {
    if (this.started) return;
    this.started = true;

    // 1. Initial load
    this.refresh();

    // 2. Real-time: a position update means the vehicle is online → remove from list
    this.subscriptions.push(
      this.signalR.positionUpdate$.subscribe((update: PositionUpdate) => this.markOnline(update))
    );

    // 3. Background poll (30s) — catches vehicles going offline (no frame = SignalR won't tell us)
    this.pollSubscription = timer(this.BACKGROUND_POLL_MS, this.BACKGROUND_POLL_MS).subscribe(() => this.refresh());
  }

  /** Reset on logout */
  stop(): void {
    this.subscriptions.forEach(s => s.unsubscribe());
    this.subscriptions = [];
    this.pollSubscription?.unsubscribe();
    this.pollSubscription = null;
    this.liveSubscription?.unsubscribe();
    this.liveSubscription = null;
    this.offlineVehicles$.next([]);
    this.offlineCount$.next(0);
    this.started = false;
  }

  /**
   * Switch to fast-polling mode (5s cadence).
   * Call this when the dropdown opens — the user is actively looking at the list
   * so we want it to update as close to real-time as possible.
   */
  intensify(): void {
    if (this.liveSubscription) return; // Already intensified
    this.refresh(); // Immediate refresh
    this.liveSubscription = timer(this.LIVE_POLL_MS, this.LIVE_POLL_MS).subscribe(() => this.refresh());
  }

  /**
   * Switch back to background polling (30s cadence).
   * Call this when the dropdown closes to save HTTP traffic.
   */
  relax(): void {
    this.liveSubscription?.unsubscribe();
    this.liveSubscription = null;
  }

  /** Force a fresh fetch (e.g. when the user opens the dropdown) */
  refresh(): void {
    this.monitoringApi.getVehiclesWithPositions().subscribe({
      next: (vehicles) => {
        const offline = (vehicles || []).filter(v => !v.isOnline);
        // Sort by longest offline first (oldest lastPosition.recordedAt first)
        offline.sort((a, b) => {
          const ta = a.lastPosition?.recordedAt ? new Date(a.lastPosition.recordedAt).getTime() : 0;
          const tb = b.lastPosition?.recordedAt ? new Date(b.lastPosition.recordedAt).getTime() : 0;
          return ta - tb;
        });
        this.offlineVehicles$.next(offline);
        this.offlineCount$.next(offline.length);
      },
      error: (err) => console.error('[OfflineVehiclesService] refresh failed:', err)
    });
  }

  /** Called on each SignalR position update — if the vehicle was offline, pull it out */
  private markOnline(update: PositionUpdate): void {
    const current = this.offlineVehicles$.value;
    const idx = current.findIndex(v => v.id === update.vehicleId);
    if (idx === -1) return; // Was already online, nothing to do

    const next = [...current.slice(0, idx), ...current.slice(idx + 1)];
    this.offlineVehicles$.next(next);
    this.offlineCount$.next(next.length);
  }
}
