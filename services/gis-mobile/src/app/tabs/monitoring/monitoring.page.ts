import { Component, OnInit, OnDestroy, NgZone, ChangeDetectorRef } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription, interval } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { SignalRService, PositionUpdate, ConnectionState } from '../../core/services/signalr.service';
import { PositionShareService, ShareChannel, hasKnownPosition } from '../../core/services/position-share.service';
import {
  VEHICLE_STATE_ORDER, VehicleMotionState, VehicleStateStyle, STATE_TEXT_ON_COLOR,
  motionState, stateMarkerHtml, stateStyle
} from '../../core/vehicle-state.util';
import * as L from 'leaflet';

/** Taille des marqueurs de véhicule (bordure blanche comprise). */
const MARKER_SIZE = 32;

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

      <!-- Légende des couleurs d'état (masquée quand la fiche d'un véhicule s'ouvre) -->
      <app-vehicle-state-legend class="map-legend" *ngIf="!selectedVehicle"></app-vehicle-state-legend>

      <!-- Backdrop: tap outside to close -->
      <div class="sheet-backdrop" *ngIf="selectedVehicle" (click)="closeSheet()"></div>

      <!-- Bottom sheet: selected vehicle info -->
      <div class="vehicle-sheet" *ngIf="selectedVehicle">
        <div class="sheet-handle" (click)="closeSheet()"></div>
        <div class="sheet-content">
          <div class="sheet-header">
            <div class="sheet-avatar" [ngClass]="vehicleState(selectedVehicle)"
                 [style.background]="stateOf(selectedVehicle).tint" [style.color]="stateOf(selectedVehicle).color">
              <ion-icon [name]="vehicleStateIcon(selectedVehicle)" aria-hidden="true"></ion-icon>
            </div>
            <div class="sheet-title">
              <h3>{{ selectedVehicle.vehicleName }}</h3>
              <p>{{ selectedVehicle.plate || '—' }}</p>
            </div>
            <span class="status-pill" [ngClass]="vehicleState(selectedVehicle)"
                  [style.background]="stateOf(selectedVehicle).color" [style.color]="textOnState">{{ stateLabel(selectedVehicle) }}</span>
          </div>
          <div class="sheet-stats">
            <div class="sheet-stat speed-stat">
              <ion-icon name="speedometer-outline" color="primary"></ion-icon>
              <!-- Sous « Déconnecté », la vitesse est celle de la dernière trame (parfois vieille de
                   plusieurs jours) : l'afficher contredisait la pastille grise. -->
              <span>{{ vehicleState(selectedVehicle) === 'offline' ? '—' : (selectedVehicle.speedKph | number:'1.0-0') + ' km/h' }}</span>
            </div>
            <div class="sheet-stat">
              <!-- Contact mis en couleur de l'application : le vert est réservé à « En route ». -->
              <ion-icon [name]="selectedVehicle.ignitionOn ? 'flash' : 'flash-off'" [color]="selectedVehicle.ignitionOn ? 'primary' : 'medium'"></ion-icon>
              <span>{{ selectedVehicle.ignitionOn ? 'Contact ON' : 'Contact OFF' }}</span>
            </div>
            <div class="sheet-stat">
              <ion-icon name="compass-outline" color="tertiary"></ion-icon>
              <span>{{ selectedVehicle.courseDeg | number:'1.0-0' }}°</span>
            </div>
          </div>
          <div class="sheet-rows">
            <div class="sheet-row" *ngIf="selectedVehicle.driverName">
              <ion-icon name="person-outline"></ion-icon>
              <span>{{ selectedVehicle.driverName }}</span>
            </div>
            <div class="sheet-row">
              <ion-icon name="location-outline"></ion-icon>
              <span>{{ selectedVehicle.address || 'Position en cours…' }}</span>
            </div>
            <div class="sheet-row">
              <ion-icon name="time-outline"></ion-icon>
              <span>Dernière comm. : {{ selectedLastComm() || '—' }}</span>
            </div>
          </div>
          <ion-button expand="block" fill="outline" size="small" class="locate-btn" (click)="recenterSelected($event)">
            <ion-icon name="locate-outline" slot="start"></ion-icon>
            Localiser sur la carte
          </ion-button>
          <app-position-share-bar
            [disabled]="!canShareSelected()"
            (share)="shareSelectedPosition($event)"></app-position-share-bar>
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
    /* Connexion de l'APPLICATION, pas un état de véhicule : couleur de l'application et
       gris foncé, pour ne pas se lire « en route » (vert) ou « déconnecté » (gris clair)
       à côté des marqueurs — et un texte blanc enfin lisible (contraste >= 4,5:1). */
    .conn-badge.connected { background: var(--ion-color-primary, #1a56db); }
    .conn-badge.disconnected { background: #4b5563; }
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
    .sheet-avatar ion-icon { font-size: 22px; }
    /* En bas à gauche, sous les badges du haut : ne recouvre ni la barre d'outils ni la fiche. */
    .map-legend {
      position: absolute;
      left: 12px;
      bottom: calc(env(safe-area-inset-bottom, 0px) + 12px);
      z-index: 1000;
      max-width: calc(100% - 24px);
    }
    .sheet-header h3 { margin: 0; font-size: 16px; font-weight: 600; }
    .sheet-header p { margin: 2px 0 0; font-size: 13px; color: var(--ion-color-medium); }
    .sheet-stats { display: flex; gap: 16px; }
    .sheet-stat {
      display: flex; align-items: center; gap: 6px;
      font-size: 13px; font-weight: 500;
    }
    .sheet-backdrop {
      position: absolute;
      inset: 0;
      z-index: 999;
      background: rgba(0,0,0,0.25);
    }
    .sheet-handle { cursor: pointer; }
    .sheet-title { flex: 1; min-width: 0; }
    .sheet-title h3 { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    /* Couleurs d'état liées depuis vehicle-state.util (aucune couleur d'état recodée ici). */
    .status-pill {
      font-size: 11px; font-weight: 700; padding: 4px 10px; border-radius: 12px;
      white-space: nowrap;
    }
    .sheet-rows { margin-top: 12px; display: flex; flex-direction: column; gap: 8px; }
    .sheet-row {
      display: flex; align-items: center; gap: 8px;
      font-size: 13px; color: var(--ion-color-medium-shade, #555);
    }
    .sheet-row ion-icon { font-size: 16px; color: var(--ion-color-medium); flex: none; }
    .sheet-row span { line-height: 1.35; }
    .locate-btn { margin-top: 14px; }
  `]
})
export class MonitoringPage implements OnInit, OnDestroy {
  private map: L.Map | null = null;
  private markers = new Map<number, L.Marker>();
  private subs: Subscription[] = [];
  private pollSub: Subscription | null = null;
  private clockSub: Subscription | null = null;
  vehicleCount = 0;
  /** Texte sur la couleur d'un état : le blanc y était illisible (contraste < 3:1). */
  readonly textOnState = STATE_TEXT_ON_COLOR;
  selectedVehicle: PositionUpdate | null = null;
  connState: ConnectionState = 'Disconnected';
  lastUpdateAt: Date | null = null;
  lastUpdateLabel: string = '';
  /** A pending "Localiser" request queued until the map is ready */
  private pendingFocus: { vid: number; lat: number; lng: number } | null = null;
  /** Vrai tant qu'un focus (Localiser / deep link) est en cours: empêche le
   *  fitBounds flotte entière de loadPositions() d'écraser le zoom cible. */
  private suppressAutoCenter = false;

  /**
   * Un marqueur par état — vert en route, orange au ralenti, rouge à l'arrêt,
   * gris déconnecté — fabriqué par vehicle-state.util : la légende, la liste
   * des véhicules et le replay montrent ainsi exactement les mêmes pastilles.
   * Chaque état a aussi sa FORME (flèche, pause, carré, nuage barré) pour qui
   * distingue mal les couleurs.
   */
  private readonly stateIcons: Record<VehicleMotionState, L.DivIcon> = VEHICLE_STATE_ORDER.reduce((acc, state) => {
    acc[state] = L.divIcon({
      className: `vehicle-marker ${state}-marker`,
      html: stateMarkerHtml(state, { size: MARKER_SIZE }),
      iconSize: [MARKER_SIZE, MARKER_SIZE],
      iconAnchor: [MARKER_SIZE / 2, MARKER_SIZE / 2]
    });
    return acc;
  }, {} as Record<VehicleMotionState, L.DivIcon>);

  /**
   * Icône du marqueur — délégué à motionState, qui teste la FRAÎCHEUR AVANT la
   * vitesse. L'ancien code court-circuitait sur « moving » dès que la dernière
   * trame portait une vitesse > 3 km/h : un boîtier muet depuis 11 jours restait
   * donc affiché vert « en mouvement » avec sa vitesse fantôme, alors même que
   * la fiche indiquait « Dernière comm. : il y a 11 j ».
   */
  private pickIcon(pos: PositionUpdate, nowMs: number = Date.now()): L.DivIcon {
    return this.stateIcons[motionState(pos, nowMs)];
  }

  /**
   * Repasse chaque marqueur à l'état de sa dernière trame À L'INSTANT PRÉSENT. L'icône
   * n'était recalculée qu'à l'arrivée d'une trame de CE véhicule, et le repli REST de 30 s
   * ne tourne que hors connexion : un boîtier qui se taisait en roulant restait vert
   * « En route » indéfiniment, alors que la fiche (recalculée chaque seconde) affichait déjà
   * « Déconnecté ». Les DivIcon étant partagés par état, on ne touche au DOM que si l'état
   * a changé.
   */
  refreshMarkerStates(nowMs: number = Date.now()): void {
    this.markers.forEach(marker => {
      const pos: PositionUpdate | undefined = (marker as any)._posData;
      if (!pos) return;
      const icon = this.pickIcon(pos, nowMs);
      if (marker.options.icon !== icon) marker.setIcon(icon);
    });
  }

  constructor(
    private api: ApiService,
    private signalr: SignalRService,
    private route: ActivatedRoute,
    private router: Router,
    private zone: NgZone,
    private cdr: ChangeDetectorRef,
    private positionShare: PositionShareService
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

  /** Focus the map on a specific vehicle (Localiser / deep link QR).
   *  Returns true when the vehicle's marker existed (focus complet:
   *  zoom + tooltip + bottom-sheet), false when only the coords zoom
   *  could be applied. Prefer the marker's LIVE position over the
   *  (possibly stale) coords carried by the link. */
  private focusOnVehicle(vehicleId: number, lat: number, lng: number): boolean {
    if (!this.map) return false;
    const marker = this.markers.get(vehicleId);
    const target = marker ? marker.getLatLng() : L.latLng(lat, lng);
    this.map.setView(target, 15, { animate: true });
    if (marker) {
      marker.openTooltip();
      this.selectedVehicle = (marker as any)._posData || null;
      this.tickUI();
      return true;
    }
    return false;
  }

  /** Apply queued focus request (Localiser / deep link) once the map is ready.
   *  Keeps pendingFocus alive while the marker doesn't exist yet so
   *  loadPositions() can complete the focus once markers are created. */
  private applyPendingFocus() {
    if (!this.pendingFocus || !this.map) return;
    const { vid, lat, lng } = this.pendingFocus;
    if (this.focusOnVehicle(vid, lat, lng)) {
      this.pendingFocus = null;
      this.clearFocusParams();
    }
  }

  /** Purge vehicleId/lat/lng de l'URL une fois le focus consommé — sinon
   *  chaque retour sur l'onglet re-zoomerait sur les coordonnées figées
   *  du lien (snapshot relu dans ionViewDidEnter). */
  private clearFocusParams() {
    this.router.navigate([], { relativeTo: this.route, queryParams: {}, replaceUrl: true });
  }

  ngOnInit() {
    // Ensure SignalR is connected (may already be started by dashboard)
    this.signalr.startConnection();

    // Wrap SignalR callbacks in NgZone — on mobile WebView, the SignalR
    // WebSocket callbacks sometimes fire outside Angular's zone which means
    // Angular bindings (connState badge, vehicleCount, selectedVehicle)
    // never re-render even though the Leaflet markers move.
    this.subs.push(
      this.signalr.positionBatch$.subscribe(batch => {
        this.zone.run(() => {
          // Leaflet marker moves are outside Angular CD (cheap); we run ONE
          // change-detection pass per ~300ms batch instead of one per frame.
          for (const pos of batch) this.updateMarker(pos);
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
          this.suppressAutoCenter = true;
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
      this.suppressAutoCenter = true;
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

    // Retour sur l'onglet après une absence : les marqueurs ont pu vieillir sans trame.
    this.refreshMarkerStates();

    // Tick the "last update" label every 1 s while visible. detectChanges()
    // forces the DOM to reflect the new label even when Zone.js change
    // detection doesn't auto-fire in the Capacitor WebView.
    // Même horloge pour les marqueurs : ils passent au gris à la même seconde que la fiche.
    if (!this.clockSub) {
      this.clockSub = interval(1000).subscribe(() => {
        this.zone.run(() => {
          this.refreshLastUpdateLabel();
          this.refreshMarkerStates();
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
            timestamp: p.lastPosition.recordedAt || '',
            address: p.lastPosition.address || p.lastAddress || '',
            driverName: p.driverName || ''
          };
          this.updateMarker(pos);
        });
        this.vehicleCount = this.markers.size;
        if (this.markers.size > 0) {
          this.lastUpdateAt = new Date();
          this.refreshLastUpdateLabel();
        }
        if (this.pendingFocus) {
          // Les markers existent maintenant: compléter le focus deep-link
          // (zoom + tooltip + bottom-sheet) au lieu d'écraser le zoom cible
          // par un fitBounds flotte entière.
          this.applyPendingFocus();
          if (this.pendingFocus) {
            // Véhicule introuvable dans la flotte (ex: QR d'un autre
            // serveur): le zoom coordonnées reste appliqué, on abandonne.
            this.pendingFocus = null;
            this.clearFocusParams();
          }
        } else if (!this.suppressAutoCenter) {
          this.centerOnAll();
        }
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
      // Keep the open detail sheet live if it's showing this vehicle.
      if (this.selectedVehicle && this.selectedVehicle.vehicleId === pos.vehicleId) {
        this.selectedVehicle = pos;
      }
    } else {
      const marker = L.marker(latlng, { icon })
        .addTo(this.map)
        .bindTooltip(pos.vehicleName, { direction: 'top', offset: [0, -16] });

      marker.on('click', () => {
        // Run inside Angular's zone + force a tick: the Capacitor Android
        // WebView otherwise doesn't re-render the detail sheet, so the user
        // only saw the marker tooltip (the name) and no details.
        this.zone.run(() => {
          this.selectedVehicle = (marker as any)._posData || pos;
          this.tickUI();
        });
      });

      (marker as any)._posData = pos;
      this.markers.set(pos.vehicleId, marker);
    }
    this.vehicleCount = this.markers.size;
  }

  centerOnAll() {
    if (!this.map || this.markers.size === 0) return;
    // Recentrage explicite (bouton toolbar) ou auto: le focus deep-link est
    // terminé, les prochains loadPositions() peuvent de nouveau auto-centrer.
    this.suppressAutoCenter = false;
    const bounds = L.latLngBounds([]);
    this.markers.forEach(m => bounds.extend(m.getLatLng()));
    this.map.fitBounds(bounds, { padding: [40, 40], maxZoom: 14 });
  }

  closeSheet() {
    this.selectedVehicle = null;
  }

  /** État affiché (pastille du panneau) — même source que les marqueurs. */
  vehicleState(pos: PositionUpdate | null): VehicleMotionState {
    if (!pos) return 'offline';
    return motionState(pos);
  }

  /** Couleur, libellé et icône de l'état : jamais recodés ici (vehicle-state.util). */
  stateOf(pos: PositionUpdate | null): VehicleStateStyle {
    return stateStyle(this.vehicleState(pos));
  }

  stateLabel(pos: PositionUpdate | null): string {
    return this.stateOf(pos).label;
  }

  vehicleStateIcon(pos: PositionUpdate | null): string {
    return this.stateOf(pos).icon;
  }

  /** Relative "last communication" label for the selected vehicle. */
  selectedLastComm(): string {
    const r = this.selectedVehicle?.recordedAt;
    if (!r) return '';
    const ms = Date.parse(r);
    if (isNaN(ms)) return '';
    const delta = Math.max(0, Math.floor((Date.now() - ms) / 1000));
    if (delta < 60) return `il y a ${delta}s`;
    if (delta < 3600) return `il y a ${Math.floor(delta / 60)} min`;
    if (delta < 86400) return `il y a ${Math.floor(delta / 3600)} h`;
    return `il y a ${Math.floor(delta / 86400)} j`;
  }

  /** Recenter the map on the selected vehicle (Localiser button). */
  recenterSelected(ev?: Event) {
    ev?.stopPropagation();
    if (this.map && this.selectedVehicle) {
      this.map.setView([this.selectedVehicle.latitude, this.selectedVehicle.longitude], 15, { animate: true });
    }
  }

  /** Les boutons de partage ne sont actifs qu'avec une position exploitable. */
  canShareSelected(): boolean {
    const v = this.selectedVehicle;
    return !!v && hasKnownPosition(v.latitude, v.longitude);
  }

  /** Partage la position du véhicule sélectionné (WhatsApp, Messenger, SMS ou
   *  feuille de partage). Message et liens : PositionShareService, commun à la
   *  fiche véhicule. */
  async shareSelectedPosition(channel: ShareChannel) {
    const v = this.selectedVehicle;
    if (!v || !this.canShareSelected()) return;
    await this.positionShare.share(channel, {
      label: v.plate || v.vehicleName,
      latitude: v.latitude,
      longitude: v.longitude,
      address: v.address,
      recordedAt: v.recordedAt
    });
  }
}
