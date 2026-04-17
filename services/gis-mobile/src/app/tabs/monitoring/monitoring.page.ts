import { Component, OnInit, OnDestroy, NgZone, ChangeDetectorRef } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Subscription, interval } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { SignalRService, PositionUpdate, ConnectionState } from '../../core/services/signalr.service';
import * as L from 'leaflet';

@Component({
  selector: 'app-monitoring',
  standalone: false,
  template: `
    <ion-header>
      <ion-toolbar color="primary">
        <ion-title>Monitoring</ion-title>
        <ion-buttons slot="end">
          <ion-button routerLink="/tabs/playback">
            <ion-icon name="play-circle-outline" slot="icon-only"></ion-icon>
          </ion-button>
          <ion-button (click)="centerOnAll()">
            <ion-icon name="locate-outline" slot="icon-only"></ion-icon>
          </ion-button>
        </ion-buttons>
      </ion-toolbar>
    </ion-header>

    <ion-content [fullscreen]="true" class="map-content" scrollY="false">
      <!-- Map Container -->
      <div id="monitoring-map" class="map-container"></div>

      <!-- Floating vehicle count badge -->
      <div class="floating-badge">
        <ion-icon name="car-sport"></ion-icon>
        <span>{{ vehicleCount }} véhicules</span>
      </div>

      <!-- Connection state badge (tap to retry when disconnected) -->
      <div class="conn-badge" [class.connected]="connState === 'Connected'"
           [class.disconnected]="connState !== 'Connected'"
           (click)="onBadgeTap()">
        <ion-icon [name]="connState === 'Connected' ? 'wifi' : 'wifi-outline'"></ion-icon>
        <span>{{ connState === 'Connected' ? 'Temps réel' : (connState === 'Connecting' ? 'Connexion…' : 'Hors ligne') }}</span>
        <span *ngIf="lastUpdateAt" class="last-update">· {{ lastUpdateLabel }}</span>
      </div>

      <!-- Bottom sheet: selected vehicle info -->
      <div class="vehicle-sheet" *ngIf="selectedVehicle" (click)="closeSheet()">
        <div class="sheet-handle"></div>
        <div class="sheet-content">
          <div class="sheet-header">
            <div class="sheet-avatar" [class.moving]="selectedVehicle.isMoving" [class.stopped]="!selectedVehicle.isMoving">
              <ion-icon [name]="selectedVehicle.isMoving ? 'navigate' : 'stop-circle'"></ion-icon>
            </div>
            <div>
              <h3>{{ selectedVehicle.vehicleName }}</h3>
              <p>{{ selectedVehicle.plate }}</p>
            </div>
          </div>
          <div class="sheet-stats">
            <div class="sheet-stat">
              <ion-icon name="speedometer-outline" color="primary"></ion-icon>
              <span>{{ selectedVehicle.speedKph | number:'1.0-0' }} km/h</span>
            </div>
            <div class="sheet-stat">
              <ion-icon [name]="selectedVehicle.ignitionOn ? 'flash' : 'flash-off'" [color]="selectedVehicle.ignitionOn ? 'success' : 'medium'"></ion-icon>
              <span>{{ selectedVehicle.ignitionOn ? 'Contact ON' : 'Contact OFF' }}</span>
            </div>
            <div class="sheet-stat">
              <ion-icon name="compass-outline" color="tertiary"></ion-icon>
              <span>{{ selectedVehicle.courseDeg | number:'1.0-0' }}°</span>
            </div>
          </div>
        </div>
      </div>
    </ion-content>
  `,
  styles: [`
    .map-content { --background: #e5e7eb; }
    .map-container {
      position: absolute;
      top: 0; left: 0; right: 0; bottom: 0;
      z-index: 1;
    }
    /* Both badges sit BELOW the ion-toolbar (56px) + safe-area-inset-top, so
       they're not hidden behind the Android status bar and don't overlap the
       toolbar's locate/playback buttons (which were previously stealing taps). */
    .floating-badge {
      position: absolute;
      top: calc(env(safe-area-inset-top, 0px) + 68px);
      left: 12px;
      z-index: 1000;
      background: var(--ion-card-background, #fff);
      border-radius: 20px;
      padding: 6px 14px;
      display: flex; align-items: center; gap: 6px;
      box-shadow: 0 2px 12px rgba(0,0,0,0.15);
      font-size: 13px; font-weight: 600;
    }
    .floating-badge ion-icon { color: var(--ion-color-primary); font-size: 16px; }
    .conn-badge {
      position: absolute;
      top: calc(env(safe-area-inset-top, 0px) + 68px);
      right: 12px;
      z-index: 1000;
      border-radius: 20px;
      padding: 8px 14px;
      display: flex; align-items: center; gap: 6px;
      box-shadow: 0 2px 12px rgba(0,0,0,0.15);
      font-size: 12px; font-weight: 600;
      color: #fff;
      min-height: 34px;
    }
    .conn-badge.connected { background: #10b981; }
    .conn-badge.disconnected { background: #9ca3af; }
    .conn-badge ion-icon { font-size: 14px; }
    .conn-badge .last-update { font-weight: 400; opacity: 0.85; font-size: 11px; }
    .conn-badge { cursor: pointer; }
    .conn-badge:active { transform: scale(0.96); transition: transform 80ms; }
    .vehicle-sheet {
      position: absolute;
      bottom: 0; left: 0; right: 0;
      z-index: 1000;
      background: var(--ion-card-background, #fff);
      border-radius: 20px 20px 0 0;
      padding: 12px 20px 24px;
      box-shadow: 0 -4px 20px rgba(0,0,0,0.15);
    }
    .sheet-handle {
      width: 40px; height: 4px;
      background: var(--ion-color-light-shade);
      border-radius: 2px;
      margin: 0 auto 12px;
    }
    .sheet-header { display: flex; align-items: center; gap: 12px; margin-bottom: 12px; }
    .sheet-avatar {
      width: 44px; height: 44px; border-radius: 50%;
      display: flex; align-items: center; justify-content: center;
    }
    .sheet-avatar.moving { background: rgba(16,185,129,0.15); color: #10b981; }
    .sheet-avatar.stopped { background: rgba(156,163,175,0.15); color: #9ca3af; }
    .sheet-avatar ion-icon { font-size: 22px; }
    .sheet-header h3 { margin: 0; font-size: 16px; font-weight: 600; }
    .sheet-header p { margin: 2px 0 0; font-size: 13px; color: var(--ion-color-medium); }
    .sheet-stats { display: flex; gap: 16px; }
    .sheet-stat {
      display: flex; align-items: center; gap: 6px;
      font-size: 13px; font-weight: 500;
    }
  `]
})
export class MonitoringPage implements OnInit, OnDestroy {
  private map: L.Map | null = null;
  private markers = new Map<number, L.Marker>();
  private subs: Subscription[] = [];
  private pollSub: Subscription | null = null;
  private clockSub: Subscription | null = null;
  vehicleCount = 0;
  selectedVehicle: PositionUpdate | null = null;
  connState: ConnectionState = 'Disconnected';
  lastUpdateAt: Date | null = null;
  lastUpdateLabel: string = '';
  /** A pending "Localiser" request queued until the map is ready */
  private pendingFocus: { vid: number; lat: number; lng: number } | null = null;

  // Custom icons — four states:
  //   moving  (green)   = speed > 3 km/h (lenient; the server's 10 km/h
  //                       threshold is too strict for city traffic)
  //   idling  (orange)  = ignition ON but not moving (engine running,
  //                       driver waiting / stopped at traffic light)
  //   parked  (red)     = ignition OFF but still transmitting recently
  //                       (<30 min) — vehicle parked with engine off
  //   offline (gray)    = no recent data, device offline / out of coverage
  private movingIcon = L.divIcon({
    className: 'vehicle-marker moving-marker',
    html: '<div style="background:#10b981;width:28px;height:28px;border-radius:50%;display:flex;align-items:center;justify-content:center;border:3px solid #fff;box-shadow:0 2px 8px rgba(0,0,0,0.3);"><svg width="14" height="14" viewBox="0 0 24 24" fill="white"><path d="M12 2L4.5 20.29l.71.71L12 18l6.79 3 .71-.71z"/></svg></div>',
    iconSize: [28, 28],
    iconAnchor: [14, 14]
  });

  private idlingIcon = L.divIcon({
    className: 'vehicle-marker idling-marker',
    html: '<div style="background:#f59e0b;width:28px;height:28px;border-radius:50%;display:flex;align-items:center;justify-content:center;border:3px solid #fff;box-shadow:0 2px 8px rgba(0,0,0,0.3);"><svg width="14" height="14" viewBox="0 0 24 24" fill="white"><circle cx="12" cy="12" r="4"/></svg></div>',
    iconSize: [28, 28],
    iconAnchor: [14, 14]
  });

  // Red: ignition OFF but the device is still transmitting recently —
  // i.e. the vehicle is parked with the engine off (online but stopped).
  private parkedIcon = L.divIcon({
    className: 'vehicle-marker parked-marker',
    html: '<div style="background:#ef4444;width:28px;height:28px;border-radius:50%;display:flex;align-items:center;justify-content:center;border:3px solid #fff;box-shadow:0 2px 8px rgba(0,0,0,0.3);"><svg width="12" height="12" viewBox="0 0 24 24" fill="white"><rect x="6" y="6" width="12" height="12" rx="2"/></svg></div>',
    iconSize: [28, 28],
    iconAnchor: [14, 14]
  });

  // Gray: no recent data — device is offline / out of coverage.
  private stoppedIcon = L.divIcon({
    className: 'vehicle-marker stopped-marker',
    html: '<div style="background:#9ca3af;width:28px;height:28px;border-radius:50%;display:flex;align-items:center;justify-content:center;border:3px solid #fff;box-shadow:0 2px 8px rgba(0,0,0,0.3);"><svg width="12" height="12" viewBox="0 0 24 24" fill="white"><path d="M1 11.27L12.01 5 23 11.27v1.46L12.01 19 1 12.73zm2 .73l9 5.09 9-5.09-9-5.09z"/></svg></div>',
    iconSize: [28, 28],
    iconAnchor: [14, 14]
  });

  /**
   * Pick the right marker icon for a given position. Four states:
   *   green (moving)  = moving (speed > 3 km/h or server flagged isMoving)
   *   orange (idling) = ignition ON but not moving (engine running, driver waiting)
   *   red (parked)    = ignition OFF but still transmitting recently (<30 min) — parked
   *   gray (offline)  = no recent data, device offline / out of coverage
   */
  private pickIcon(pos: PositionUpdate): L.DivIcon {
    const speed = pos.speedKph ?? 0;
    const moving = pos.isMoving || speed > 3;
    if (moving) return this.movingIcon;

    const recordedMs = pos.recordedAt ? Date.parse(pos.recordedAt) : NaN;
    const ageMs = !isNaN(recordedMs) ? (Date.now() - recordedMs) : Infinity;
    const online = ageMs < 30 * 60 * 1000; // data in last 30 min → still online

    // Ignition on → engine running, driver idling
    if (pos.ignitionOn) return this.idlingIcon;

    // Ignition off but still transmitting → parked / stationary
    if (online) return this.parkedIcon;

    // Too old → device offline
    return this.stoppedIcon;
  }

  constructor(
    private api: ApiService,
    private signalr: SignalRService,
    private route: ActivatedRoute,
    private zone: NgZone,
    private cdr: ChangeDetectorRef
  ) {}

  /**
   * Force an immediate view refresh. NgZone.run() *should* schedule change
   * detection, but on Capacitor's Android WebView we've observed that
   * callbacks coming from SignalR's WebSocket / RxJS timer can fire in a
   * context where Angular's onStable never emits, so the view stays frozen
   * until the user taps. Calling detectChanges() synchronously bypasses
   * that and updates the DOM right away. Wrapped in try/catch because
   * detectChanges throws if the component is being destroyed.
   */
  private tickUI(): void {
    try { this.cdr.detectChanges(); } catch {}
  }

  /**
   * Tap on the connection badge — silently retry the SignalR connection
   * if we're currently disconnected. No-op when already connected.
   */
  async onBadgeTap() {
    if (this.connState === 'Connected') return;
    this.signalr.startConnection();
  }

  /** Focus the map on a specific vehicle position (used by "Localiser" from Vehicles) */
  private focusOnVehicle(vehicleId: number, lat: number, lng: number) {
    if (!this.map) return;
    this.map.setView([lat, lng], 15, { animate: true });
    // If we already have a marker, open its popup; otherwise it'll appear once SignalR/polling catches up
    const marker = this.markers.get(vehicleId);
    if (marker) {
      marker.openTooltip();
      this.selectedVehicle = (marker as any)._posData || null;
    }
  }

  /** Apply queued focus request (from Localiser deep-link) once the map is ready */
  private applyPendingFocus() {
    if (!this.pendingFocus || !this.map) return;
    const { vid, lat, lng } = this.pendingFocus;
    this.focusOnVehicle(vid, lat, lng);
    this.pendingFocus = null;
  }

  ngOnInit() {
    // Ensure SignalR is connected (may already be started by dashboard)
    this.signalr.startConnection();

    // Wrap SignalR callbacks in NgZone — on mobile WebView, the SignalR
    // WebSocket callbacks sometimes fire outside Angular's zone which means
    // Angular bindings (connState badge, vehicleCount, selectedVehicle)
    // never re-render even though the Leaflet markers move.
    this.subs.push(
      this.signalr.positionUpdate$.subscribe(pos => {
        this.zone.run(() => {
          this.updateMarker(pos);
          this.lastUpdateAt = new Date();
          this.refreshLastUpdateLabel();
          this.tickUI();
        });
      })
    );

    this.subs.push(
      this.signalr.connectionState$.subscribe(state => {
        this.zone.run(() => {
          this.connState = state;
          this.tickUI();
        });
      })
    );

    // Listen for queryParams changes.
    // Ionic tabs reuse the component, so snapshot in ionViewDidEnter can be
    // stale on repeat navigations. Observable fires every time queryParams change.
    this.subs.push(
      this.route.queryParams.subscribe(params => {
        const lat = parseFloat(params['lat']);
        const lng = parseFloat(params['lng']);
        const vid = parseInt(params['vehicleId'], 10);
        if (!isNaN(lat) && !isNaN(lng) && !isNaN(vid)) {
          // Remember so ionViewDidEnter can apply once map is ready
          this.pendingFocus = { vid, lat, lng };
          // If map is already up, focus immediately
          if (this.map) {
            setTimeout(() => this.applyPendingFocus(), 150);
          }
        }
      })
    );
  }

  /** Build the "il y a Xs" label for the connection badge. */
  private refreshLastUpdateLabel() {
    if (!this.lastUpdateAt) { this.lastUpdateLabel = ''; return; }
    const delta = Math.max(0, Math.floor((Date.now() - this.lastUpdateAt.getTime()) / 1000));
    if (delta < 5) this.lastUpdateLabel = 'à l\'instant';
    else if (delta < 60) this.lastUpdateLabel = `il y a ${delta}s`;
    else if (delta < 3600) this.lastUpdateLabel = `il y a ${Math.floor(delta / 60)} min`;
    else this.lastUpdateLabel = `il y a ${Math.floor(delta / 3600)}h`;
  }

  // Ionic lifecycle hook — fires every time tab becomes visible.
  // More reliable than ngAfterViewInit for Leaflet maps in tabbed Ionic apps.
  ionViewDidEnter() {
    if (!this.map) {
      // First visit: initialize the map now that the tab is actually on screen
      this.initMap();
    } else {
      // Subsequent visits: Leaflet needs invalidateSize() after tab switch
      // otherwise tiles render at wrong dimensions
      setTimeout(() => this.map?.invalidateSize(), 100);
    }

    // Belt-and-braces: also read queryParams from the route snapshot on
    // every tab entry. The Observable subscription in ngOnInit already
    // catches new navigations, but if something about Ionic tab reuse
    // swallows the re-fire, this guarantees we still pick up the
    // Localiser target.
    const qp = this.route.snapshot.queryParamMap;
    const lat = parseFloat(qp.get('lat') || '');
    const lng = parseFloat(qp.get('lng') || '');
    const vid = parseInt(qp.get('vehicleId') || '', 10);
    if (!isNaN(lat) && !isNaN(lng) && !isNaN(vid)) {
      this.pendingFocus = { vid, lat, lng };
    }

    // If a Localiser navigation left a pending focus, apply it once map is ready
    if (this.pendingFocus) {
      // Delay so map init + invalidateSize have settled
      setTimeout(() => this.applyPendingFocus(), 300);
    }

    // Fallback polling: refresh positions every 30s in case SignalR is down.
    // (Stops when we leave the tab to avoid wasting battery.)
    if (!this.pollSub) {
      this.pollSub = interval(30000).subscribe(() => {
        if (this.connState !== 'Connected') {
          this.loadPositions();
        }
      });
    }

    // Tick the "last update" label every 1 s while visible. detectChanges()
    // forces the DOM to reflect the new label even when Zone.js change
    // detection doesn't auto-fire in the Capacitor WebView.
    if (!this.clockSub) {
      this.clockSub = interval(1000).subscribe(() => {
        this.zone.run(() => {
          this.refreshLastUpdateLabel();
          this.tickUI();
        });
      });
    }
  }

  ionViewWillLeave() {
    this.pollSub?.unsubscribe();
    this.pollSub = null;
    this.clockSub?.unsubscribe();
    this.clockSub = null;
  }

  ngOnDestroy() {
    this.subs.forEach(s => s.unsubscribe());
    this.pollSub?.unsubscribe();
    this.clockSub?.unsubscribe();
    if (this.map) {
      this.map.remove();
      this.map = null;
    }
  }

  private initMap() {
    const container = document.getElementById('monitoring-map');
    if (!container) {
      // Tab DOM not ready yet — retry shortly
      setTimeout(() => this.initMap(), 150);
      return;
    }

    this.map = L.map('monitoring-map', {
      center: [34.0, 9.0], // Tunisia center
      zoom: 7,
      zoomControl: false,
      attributionControl: false
    });

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19
    }).addTo(this.map);

    // Ensure Leaflet measures the container AFTER Ionic finishes laying out the tab
    setTimeout(() => this.map?.invalidateSize(), 150);

    // Load initial positions
    this.loadPositions();
  }

  private loadPositions() {
    this.api.getLastPositions().subscribe({
      next: (positions) => {
        const list = Array.isArray(positions) ? positions : [];
        list.filter((p: any) => p.lastPosition).forEach((p: any) => {
          const pos: PositionUpdate = {
            deviceId: p.deviceId || 0,
            deviceUid: p.deviceUid || '',
            vehicleId: p.vehicleId,
            vehicleName: p.vehicleName || 'Véhicule',
            plate: p.plate || '',
            latitude: p.lastPosition.latitude,
            longitude: p.lastPosition.longitude,
            speedKph: p.lastPosition.speedKph || 0,
            courseDeg: p.lastPosition.courseDeg || 0,
            ignitionOn: p.lastPosition.ignitionOn ?? false,
            // Match the server's definition: ignition on AND speed > 3 km/h
            // (pickIcon will further classify idling vs parked).
            isMoving: (p.lastPosition.ignitionOn ?? false) && (p.lastPosition.speedKph || 0) > 3,
            recordedAt: p.lastPosition.recordedAt || '',
            timestamp: p.lastPosition.recordedAt || ''
          };
          this.updateMarker(pos);
        });
        this.vehicleCount = this.markers.size;
        if (this.markers.size > 0) {
          this.lastUpdateAt = new Date();
          this.refreshLastUpdateLabel();
        }
        this.centerOnAll();
        this.tickUI();
      }
    });
  }

  private updateMarker(pos: PositionUpdate) {
    if (!this.map || !pos.latitude || !pos.longitude) return;

    const icon = this.pickIcon(pos);
    const latlng = L.latLng(pos.latitude, pos.longitude);

    if (this.markers.has(pos.vehicleId)) {
      const marker = this.markers.get(pos.vehicleId)!;
      marker.setLatLng(latlng);
      marker.setIcon(icon);
      (marker as any)._posData = pos;
    } else {
      const marker = L.marker(latlng, { icon })
        .addTo(this.map)
        .bindTooltip(pos.vehicleName, { direction: 'top', offset: [0, -16] });

      marker.on('click', () => {
        this.selectedVehicle = (marker as any)._posData || pos;
      });

      (marker as any)._posData = pos;
      this.markers.set(pos.vehicleId, marker);
    }
    this.vehicleCount = this.markers.size;
  }

  centerOnAll() {
    if (!this.map || this.markers.size === 0) return;
    const bounds = L.latLngBounds([]);
    this.markers.forEach(m => bounds.extend(m.getLatLng()));
    this.map.fitBounds(bounds, { padding: [40, 40], maxZoom: 14 });
  }

  closeSheet() {
    this.selectedVehicle = null;
  }
}
