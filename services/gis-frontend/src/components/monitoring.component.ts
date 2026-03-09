import { Component, OnInit, OnDestroy, AfterViewInit, ChangeDetectorRef, NgZone, ApplicationRef, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiService } from '../services/api.service';
import { SignalRService, PositionUpdate } from '../services/signalr.service';
import { GeocodingService } from '../services/geocoding.service';
import { Vehicle } from '../models/types';
import { AppLayoutComponent } from './shared/app-layout.component';
import { AdminService } from '../admin/services/admin.service';
import { getVehicleIcon } from './shared/vehicle-icons';
import * as L from 'leaflet';
import 'leaflet-routing-machine';

@Component({
  selector: 'app-monitoring',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent],
  templateUrl: './monitoring.component.html',
  styleUrls: ['./monitoring.component.css']
})
export class MonitoringComponent implements OnInit, AfterViewInit, OnDestroy {
  @Input() embedded = false;
  map: L.Map | null = null;
  mapReady = false;
  vehicleMarkers = new Map<string, L.Marker>();

  vehicles: Vehicle[] = [];
  filteredVehicles: Vehicle[] = [];
  selectedVehicle: Vehicle | null = null;
  loading = true;

  searchQuery = '';
  filterStatus: string = 'all';

  viewMode: 'map' | 'list' | 'split' = 'split';
  mapStyle: 'streets' | 'satellite' | 'terrain' = 'streets';
  previousMapStyle: 'streets' | 'satellite' | 'terrain' | null = null; // Store style before playback

  // Panel & Tabs
  isPanelCollapsed = false;
  activeTab: 'details' | 'playback' | 'message' = 'details';
  showInlinePlayback = false;

  // Playback
  playbackFromDate = '';
  playbackToDate = '';
  isPlaybackLoaded = false;
  private monitoringMap: L.Map | null = null; // Saved reference to monitoring map during playback
  private playbackOverlayMap: L.Map | null = null; // Dedicated playback map
  playbackCurrentAddress = 'Chargement...'; // Live address in playback overlay
  private playbackAddressCache = new Map<string, string>(); // Cache for playback addresses
  private playbackAddressThrottle = 0; // Throttle counter for address updates
  isPlaying = false;
  playbackProgress = 0;
  playbackSpeed = 1;
  playbackSpeeds = [0.5, 1, 2, 4, 8];
  playbackPositions: any[] = [];
  playbackRawCount = 0;
  playbackIndex = 0;
  playbackInterval: any = null;
  playbackPolyline: L.Polyline | null = null;
  playbackMarker: L.Marker | null = null;
  playbackLoading = false;
  playbackVehicleId: number | null = null; // Track which vehicle's playback is loaded
  routingControl: any = null;
  useRoadSnapping = true; // Valhalla road snapping enabled by default (replaces OSRM)
  private playbackZoomLevel: number = 15; // Store zoom level during playback
  pointMarkers: L.CircleMarker[] = []; // Markers for each GPS point
  filteredBirdFlights = 0; // Count of filtered bird flight positions

  // Stationary stop markers (placed during animation when ignition is OFF)
  stationaryMarkers: L.Marker[] = [];
  private stopMarkerCount = 0;
  
  // Smooth animation properties
  private animationFrameId: number | null = null;
  private animationStartTime: number = 0;
  private animationFromPos: { lat: number; lng: number } | null = null;
  private animationToPos: { lat: number; lng: number } | null = null;
  private segmentDuration: number = 1000; // Base duration per segment in ms
  private isAnimatingSegment: boolean = false;
  smoothFollowCamera: boolean = true; // Enable smooth camera following
  
  // Valhalla route animation
  private currentRouteCoords: L.LatLng[] = []; // Coordinates of current Valhalla route segment
  private routeAnimationIndex: number = 0; // Current position in route animation
  
  // Progressive trace drawing
  progressivePolylines: L.Polyline[] = []; // Legacy (kept for cleanup)
  traceDrawnUpToIndex = 0;
  private ghostPolyline: L.Polyline | null = null; // Full route preview (faded)
  private progressPolyline: L.Polyline | null = null; // Growing colored trace
  private progressCoords: L.LatLng[] = []; // Accumulated coords for progress line
  private _iconFrameCount: number = 0;
  private _traceFrameCount: number = 0;
  
  // Ignition-off anchor position: when ignition is off, all positions use this anchor
  private ignitionOffAnchor: { latitude: number; longitude: number } | null = null;
  
  // Stopped anchor position: when ignition is on but speed < 3 km/h, anchor to prevent GPS drift
  private stoppedAnchor: { latitude: number; longitude: number } | null = null;
  
  // Valhalla matched route for the entire trace (batch matched)
  private matchedRouteCoords: L.LatLng[] = [];
  private matchedRouteIndex: number = 0;
  private segmentBoundaries: number[] = []; // roadPath indices where each GPS point maps to
  
  // Live marker visibility during playback
  hiddenLiveMarkers: Map<string, L.Marker> = new Map(); // Store ALL hidden live markers during playback

  // Remote commands
  remoteCommandLoading = false;
  remoteCommandMessage = '';
  remoteCommandSuccess = false;

  // Message
  driverMessage = '';

  // Popup drag
  isDragging = false;
  popupPosition = { x: 0, y: 0 };
  dragOffset = { x: 0, y: 0 };

  refreshInterval: any;
  stalenessInterval: any;
  signalRSubscription: Subscription | null = null;
  connectionStateSubscription: Subscription | null = null;
  connectionStatus = 'Disconnected';
  private loadDataController: AbortController | null = null;

  // Dedup: track last processed recordedAt per vehicle to skip duplicate SignalR messages
  private lastProcessedPosition = new Map<string, string>();

  stats = {
    total: 0,
    online: 0,
    moving: 0,
    stopped: 0,
    offline: 0
  };

  showLayersMenu = false;

  // Address cache for reverse geocoding
  private addressCache = new Map<string, string>();

  constructor(
    private router: Router,
    private apiService: ApiService,
    private signalRService: SignalRService,
    private geocodingService: GeocodingService,
    private cdr: ChangeDetectorRef,
    private ngZone: NgZone,
    private appRef: ApplicationRef,
    private adminService: AdminService
  ) {}

  ngOnInit() {
    if (!this.embedded && !this.apiService.isAuthenticated()) {
      this.router.navigate(['/login']);
      return;
    }

    // Initialize state
    this.vehicles = [];
    this.filteredVehicles = [];
    this.loading = true;

    // Load data immediately - use zone.run to ensure Angular change detection
    this.ngZone.run(() => {
      this.loadData();
      this.initSignalR();
      this.startAutoRefresh();
      this.startStalenessCheck();
    });
  }

  ngAfterViewInit() {
    // Initialize map immediately without delay
    this.initializeMap();
  }

  ngOnDestroy() {
    if (this.refreshInterval) {
      clearInterval(this.refreshInterval);
    }
    if (this.stalenessInterval) {
      clearInterval(this.stalenessInterval);
    }
    if (this.signalRSubscription) {
      this.signalRSubscription.unsubscribe();
    }
    if (this.connectionStateSubscription) {
      this.connectionStateSubscription.unsubscribe();
    }
    if (this.loadDataController) {
      this.loadDataController.abort();
    }
    // Clean up overlay map if active
    if (this.playbackOverlayMap) {
      this.playbackOverlayMap.remove();
      this.playbackOverlayMap = null;
    }
    // Restore monitoring map ref before removing
    if (this.monitoringMap) {
      this.map = this.monitoringMap;
      this.monitoringMap = null;
    }
    if (this.map) {
      this.map.remove();
    }
  }

  async initSignalR() {
    // Subscribe to connection state (stored for cleanup to prevent memory leak)
    this.connectionStateSubscription = this.signalRService.connectionState$.subscribe(state => {
      this.connectionStatus = state;
      console.log('SignalR connection state:', state);
    });

    // Subscribe to position updates
    this.signalRSubscription = this.signalRService.positionUpdate$.subscribe(
      (update: PositionUpdate) => this.handlePositionUpdate(update)
    );

    // Start connection
    await this.signalRService.startConnection();
  }

  handlePositionUpdate(update: PositionUpdate) {
    // Frontend dedup: skip if same vehicle+recordedAt already processed
    // (Redis + RabbitMQ consumers both broadcast the same GPS frame)
    const dedupKey = `${update.vehicleId}`;
    const lastRecorded = this.lastProcessedPosition.get(dedupKey);
    if (lastRecorded === update.recordedAt) {
      return; // Exact duplicate, skip
    }
    this.lastProcessedPosition.set(dedupKey, update.recordedAt);

    this.ngZone.run(() => {
      // Find the vehicle and update its position
      const vehicleIndex = this.vehicles.findIndex(
        v => v.id?.toString() === update.vehicleId?.toString()
      );

      if (vehicleIndex !== -1) {
        const vehicle = this.vehicles[vehicleIndex] as any;
        
        // Update vehicle data from SignalR push
        vehicle.currentLocation = {
          lat: update.latitude,
          lng: update.longitude
        };
        // Speed is already rounded and set to 0 if ignition off by backend
        vehicle.currentSpeed = update.speedKph || 0;
        vehicle.isOnline = true;
        vehicle.isMoving = update.isMoving;
        vehicle.ignitionOn = update.ignitionOn;
        (vehicle as any).lastCommunication = update.timestamp;

        // Keep stats object in sync with live data (including fuel/temp/battery)
        if (vehicle.stats) {
          vehicle.stats = {
            ...vehicle.stats,
            currentSpeed: update.speedKph || 0,
            isMoving: update.isMoving,
            isStopped: !update.isMoving,
            ...(update.fuelRaw != null ? { fuelLevel: update.fuelRaw } : {}),
            ...(update.batteryPercent != null ? { batteryLevel: update.batteryPercent } : {}),
            ...(update.batteryVoltage != null ? { batteryVoltage: update.batteryVoltage } : {}),
            ...(update.temperatureC != null ? { temperature: update.temperatureC } : {})
          };
        }

        // Update the marker on the map with smooth animation
        this.updateSingleVehicleMarker(vehicle);
        
        // Update stats
        this.updateStats();
        
        // Update filtered vehicles to reflect changes in sidebar
        this.updateFilteredVehicle(vehicle);
        
        // If this vehicle is selected, update the panel
        if (this.selectedVehicle?.id === vehicle.id) {
          this.selectedVehicle = { ...vehicle };
        }
        
        // Force change detection
        this.cdr.detectChanges();
      }
    });
  }

  // Update a single vehicle in filteredVehicles without re-filtering all
  private updateFilteredVehicle(vehicle: any) {
    const index = this.filteredVehicles.findIndex(v => v.id === vehicle.id);
    if (index !== -1) {
      // Create new array reference to trigger Angular change detection
      this.filteredVehicles = [
        ...this.filteredVehicles.slice(0, index),
        vehicle,
        ...this.filteredVehicles.slice(index + 1)
      ];
    }
  }

  // Track active marker animations to cancel them on new updates
  private markerAnimations = new Map<string, number>();

  updateSingleVehicleMarker(vehicle: any) {
    if (!this.map || !this.mapReady || !vehicle.currentLocation) return;

    const markerId = vehicle.id?.toString();
    const existingMarker = this.vehicleMarkers.get(markerId);
    const isMoving = vehicle.isMoving ?? (vehicle.currentSpeed || 0) > 3;
    const icon = this.createVehicleIcon(vehicle, isMoving);
    const newLatLng = L.latLng(vehicle.currentLocation.lat, vehicle.currentLocation.lng);

    if (existingMarker) {
      // Cancel any ongoing animation for this marker
      const existingAnim = this.markerAnimations.get(markerId);
      if (existingAnim) cancelAnimationFrame(existingAnim);

      const oldLatLng = existingMarker.getLatLng();
      const distance = oldLatLng.distanceTo(newLatLng);

      existingMarker.setIcon(icon);
      existingMarker.setPopupContent(this.createPopupContent(vehicle));

      // Smooth animate if distance is reasonable (< 5km, > 1m)
      if (distance > 1 && distance < 5000) {
        const duration = Math.min(1500, Math.max(500, distance * 2)); // 500ms-1500ms
        const startTime = performance.now();
        const startLat = oldLatLng.lat;
        const startLng = oldLatLng.lng;
        const dLat = newLatLng.lat - startLat;
        const dLng = newLatLng.lng - startLng;

        const animate = (now: number) => {
          const elapsed = now - startTime;
          const t = Math.min(elapsed / duration, 1);
          // Ease-out cubic for smooth deceleration
          const eased = 1 - Math.pow(1 - t, 3);
          
          existingMarker.setLatLng([
            startLat + dLat * eased,
            startLng + dLng * eased
          ]);

          if (t < 1) {
            this.markerAnimations.set(markerId, requestAnimationFrame(animate));
          } else {
            this.markerAnimations.delete(markerId);
          }
        };
        this.markerAnimations.set(markerId, requestAnimationFrame(animate));
      } else {
        // Jump directly for very small or very large distances
        existingMarker.setLatLng(newLatLng);
      }
    } else {
      // Create new marker
      const marker = L.marker(newLatLng, { icon })
        .bindPopup(this.createPopupContent(vehicle))
        .on('click', () => this.selectVehicle(vehicle));
      marker.addTo(this.map!);
      this.vehicleMarkers.set(markerId, marker);
    }
  }

  initializeMap() {
    // Use setTimeout to ensure DOM is ready
    setTimeout(() => {
      if (!this.map) {
        this.map = L.map('tracking-map').setView([36.8065, 10.1815], 8);

        const mapUrls: Record<string, string> = {
          streets: 'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png',
          satellite: 'https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}',
          terrain: 'https://{s}.tile.opentopomap.org/{z}/{x}/{y}.png'
        };

        L.tileLayer(mapUrls[this.mapStyle], {
          attribution: '© OpenStreetMap contributors',
          maxZoom: 19
        }).addTo(this.map);

        this.mapReady = true;

        // Update markers if data is already loaded
        if (this.vehicles.length > 0) {
          this.updateVehicleMarkers();
        }
      }
    }, 50);
  }

  loadData() {
    this.loading = true;
    this.cdr.detectChanges();

    // Use admin API when embedded (admin context), otherwise regular API
    const vehiclesObs = this.embedded
      ? this.adminService.getVehiclesWithPositions()
      : this.apiService.getVehiclesWithPositions();
    vehiclesObs.subscribe({
      next: (vehicles) => {
        // Run inside Angular zone to ensure change detection triggers
        this.ngZone.run(() => {
          console.log('Vehicles loaded:', vehicles.length);
          
          const mappedVehicles = vehicles.map(v => ({
            ...v,
            registration_number: v.plate,
            currentLocation: v.lastPosition ? {
              lat: v.lastPosition.latitude,
              lng: v.lastPosition.longitude
            } : undefined,
            currentSpeed: v.lastPosition?.speedKph || 0,
            // Use isMoving from stats, fallback to speed calculation
            isMoving: v.stats?.isMoving ?? ((v.lastPosition?.speedKph || 0) > 5 && v.lastPosition?.ignitionOn),
            ignitionOn: v.lastPosition?.ignitionOn ?? false,
            // Address from database
            lastAddress: v.lastPosition?.address || null,
            lastRecordedAt: v.lastPosition?.recordedAt || null,
            // Vehicle mileage
            odometerKm: v.lastPosition?.odometerKm ?? v.mileage ?? null
          }));
          
          // Assign to trigger change detection
          this.vehicles = [...mappedVehicles];
          this.loading = false;
          
          // Apply filters and update UI
          this.doApplyFilters();
          this.updateStats();
          
          // Force change detection
          this.cdr.detectChanges();
          
          // Update map markers after a micro-task to ensure DOM is updated
          Promise.resolve().then(() => {
            this.updateVehicleMarkers();
          });
        });
      },
      error: (err) => {
        this.ngZone.run(() => {
          console.error('Error loading vehicles:', err);
          this.loading = false;
          this.cdr.detectChanges();
        });
      }
    });
  }

  updateVehicleMarkers() {
    // If map is not ready yet, retry after a short delay
    if (!this.map || !this.mapReady) {
      setTimeout(() => this.updateVehicleMarkers(), 100);
      return;
    }

    this.vehicleMarkers.forEach(marker => marker.remove());
    this.vehicleMarkers.clear();

    this.filteredVehicles.forEach((vehicle: any) => {
      if (vehicle.currentLocation) {
        const isMoving = vehicle.isMoving ?? (vehicle.currentSpeed || 0) > 3;
        const icon = this.createVehicleIcon(vehicle, isMoving);

        const marker = L.marker([vehicle.currentLocation.lat, vehicle.currentLocation.lng], { icon })
          .bindPopup(this.createPopupContent(vehicle))
          .on('click', () => this.selectVehicle(vehicle));

        marker.addTo(this.map!);
        this.vehicleMarkers.set(vehicle.id?.toString(), marker);
      }
    });

    if (this.vehicleMarkers.size > 0 && !this.selectedVehicle) {
      const group = new L.FeatureGroup(Array.from(this.vehicleMarkers.values()));
      this.map.fitBounds(group.getBounds().pad(0.1));
    }
  }

  createVehicleIcon(vehicle: any, isMoving: boolean): L.DivIcon {
    const vehicleType = vehicle.type || vehicle.vehicleType || this.getVehicleType(vehicle);
    let color = '#9e9e9e'; // Gray: offline
    let statusClass = 'offline';

    if (vehicle.isOnline) {
      if (!vehicle.ignitionOn) {
        // RED: Ignition OFF (parked)
        color = '#ef4444';
        statusClass = 'parked';
      } else if (isMoving || (vehicle.currentSpeed || 0) > 5) {
        // GREEN: Moving (ignition ON + speed > 5)
        color = '#4caf50';
        statusClass = 'moving';
      } else {
        // ORANGE: Idle (ignition ON + speed <= 5)
        color = '#ff9800';
        statusClass = 'stopped';
      }
    }

    const heading = (vehicle as any).lastPosition?.courseDeg || 0;
    const plate = vehicle.plate || (vehicle as any).registration_number || '';

    // Pin-style marker: gradient top circle + plate band + color triangle
    const iconHtml = `
      <div style="display:flex;flex-direction:column;align-items:center;filter:drop-shadow(0 2px 4px rgba(0,0,0,0.35));">
        <div style="width:42px;height:42px;border-radius:50%;background:linear-gradient(135deg,#38bdf8,#6366f1);display:flex;align-items:center;justify-content:center;border:2.5px solid #fff;">
          <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M5 17h14v-5l-2-5H7L5 12z"/><circle cx="7.5" cy="17.5" r="1.5"/><circle cx="16.5" cy="17.5" r="1.5"/>
          </svg>
        </div>
        ${plate ? `<div style="font-size:8.5px;font-weight:700;color:#334155;background:#f1f5f9;padding:1px 6px;border-radius:2px;white-space:nowrap;margin-top:-3px;line-height:1.4;text-align:center;max-width:80px;overflow:hidden;text-overflow:ellipsis;border:1px solid #e2e8f0;">${plate}</div>` : ''}
        <div style="width:0;height:0;border-left:10px solid transparent;border-right:10px solid transparent;border-top:14px solid ${color};margin-top:-1px;"></div>
      </div>
    `;

    return L.divIcon({
      html: iconHtml,
      className: 'custom-vehicle-marker',
      iconSize: [80, 72],
      iconAnchor: [40, 72]
    });
  }

  createPopupContent(vehicle: any): string {
    const isOnline = vehicle.isOnline;
    const ignitionOn = vehicle.ignitionOn;
    const speed = vehicle.currentSpeed || 0;
    const isMoving = speed > 5;
    
    let statusColor = '#9e9e9e';
    let statusText = 'Hors ligne';
    
    if (isOnline) {
      if (!ignitionOn) {
        statusColor = '#ef4444';
        statusText = 'Stationné';
      } else if (isMoving) {
        statusColor = '#22c55e';
        statusText = 'En mouvement';
      } else {
        statusColor = '#f59e0b';
        statusText = 'Au ralenti';
      }
    }

    const plate = vehicle.plate || 'N/A';
    const fuelLevel = vehicle.stats?.fuelLevel;
    const heading = (vehicle as any).lastPosition?.courseDeg || 0;
    const odometer = vehicle.odometerKm || vehicle.mileage;
    const address = vehicle.lastAddress || 'Position en cours...';
    const ignitionColor = ignitionOn ? '#22c55e' : '#ef4444';
    const fuelColor = fuelLevel != null && fuelLevel < 20 ? '#ef4444' : '#f59e0b';
    
    return `
      <div style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; width: 270px; margin: -14px -20px;">
        <div style="background: linear-gradient(135deg, ${statusColor} 0%, ${statusColor}cc 100%); padding: 12px 16px; border-radius: 8px 8px 0 0; display: flex; align-items: center; gap: 12px;">
          <div style="width: 38px; height: 38px; background: rgba(255,255,255,0.2); border-radius: 10px; display: flex; align-items: center; justify-content: center; flex-shrink: 0;">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="2"><rect x="1" y="3" width="15" height="13" rx="2"/><path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-1"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/></svg>
          </div>
          <div style="flex: 1; min-width: 0;">
            <div style="font-weight: 700; font-size: 14px; color: #fff; white-space: nowrap; overflow: hidden; text-overflow: ellipsis;">${plate}</div>
            <div style="font-size: 11px; color: rgba(255,255,255,0.9); margin-top: 1px;">${statusText}</div>
          </div>
          <div style="text-align: right; flex-shrink: 0;">
            <div style="font-size: 26px; font-weight: 800; color: #fff; line-height: 1;">${speed}</div>
            <div style="font-size: 9px; color: rgba(255,255,255,0.85); text-transform: uppercase; letter-spacing: 0.5px;">km/h</div>
          </div>
        </div>
        <div style="display: flex; background: #fff; border-bottom: 1px solid #f1f5f9;">
          <div style="flex: 1; padding: 10px 0; text-align: center; border-right: 1px solid #f1f5f9;">
            <div style="display: flex; align-items: center; justify-content: center; gap: 4px;">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="${ignitionColor}" stroke-width="2"><circle cx="12" cy="12" r="3"/><path d="M12 1v2m0 18v2m-9-11h2m18 0h2m-3.5-7.5-1.4 1.4M6.3 17.7l-1.4 1.4m0-14.2 1.4 1.4m11.4 11.4 1.4 1.4"/></svg>
              <span style="font-size: 12px; font-weight: 600; color: ${ignitionColor};">${ignitionOn ? 'Allumé' : 'Éteint'}</span>
            </div>
            <div style="font-size: 9px; color: #94a3b8; margin-top: 2px;">Moteur</div>
          </div>
          <div style="flex: 1; padding: 10px 0; text-align: center; border-right: 1px solid #f1f5f9;">
            <div style="display: flex; align-items: center; justify-content: center; gap: 4px;">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="${fuelColor}" stroke-width="2"><path d="M3 22V6a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v16"/><path d="M15 11h3.5a2 2 0 0 1 2 2v3a1.5 1.5 0 0 0 3 0v-7l-3-3"/></svg>
              <span style="font-size: 12px; font-weight: 600; color: ${fuelLevel != null && fuelLevel < 20 ? '#ef4444' : '#1e293b'};">${fuelLevel != null ? fuelLevel + '%' : 'N/A'}</span>
            </div>
            <div style="font-size: 9px; color: #94a3b8; margin-top: 2px;">Carburant</div>
          </div>
          <div style="flex: 1; padding: 10px 0; text-align: center;">
            <div style="font-size: 12px; font-weight: 600; color: #1e293b;">${heading}°</div>
            <div style="font-size: 9px; color: #94a3b8; margin-top: 2px;">Direction</div>
          </div>
        </div>
        <div style="padding: 8px 14px; background: #f8fafc; border-bottom: 1px solid #f1f5f9; display: flex; align-items: flex-start; gap: 6px;">
          <svg width="12" height="12" viewBox="0 0 24 24" fill="${statusColor}" stroke="none" style="flex-shrink: 0; margin-top: 2px;"><path d="M12 2C8.13 2 5 5.13 5 9c0 5.25 7 13 7 13s7-7.75 7-13c0-3.87-3.13-7-7-7zm0 9.5c-1.38 0-2.5-1.12-2.5-2.5s1.12-2.5 2.5-2.5 2.5 1.12 2.5 2.5-1.12 2.5-2.5 2.5z"/></svg>
          <span style="font-size: 11px; color: #64748b; line-height: 1.4;">${address}</span>
        </div>
        <div style="padding: 7px 14px; background: #f8fafc; border-radius: 0 0 8px 8px; display: flex; justify-content: space-between; align-items: center;">
          <span style="font-size: 10px; color: #94a3b8;">${odometer ? Number(odometer).toLocaleString() + ' km' : ''}</span>
          <span style="font-size: 10px; color: #94a3b8; font-family: 'SF Mono', Monaco, monospace;">${vehicle.currentLocation ? vehicle.currentLocation.lat.toFixed(5) + ', ' + vehicle.currentLocation.lng.toFixed(5) : ''}</span>
        </div>
      </div>
    `;
  }

  selectVehicle(vehicle: any) {
    // Toggle behavior: if same vehicle clicked, collapse panel
    if (this.selectedVehicle?.id === vehicle.id) {
      this.selectedVehicle = null;
      this.showInlinePlayback = false;
      return;
    }

    const isNewVehicle = this.selectedVehicle?.id !== vehicle.id;
    
    // If switching vehicles during active playback, stop and clear everything
    if (isNewVehicle && (this.isPlaybackLoaded || this.isPlaying)) {
      console.log('Vehicle switch during playback - stopping current playback');
      this.stopPlaybackAnimation();
      this.restoreLiveMarker(); // Restore hidden live marker of previous vehicle
      this.clearPlayback();
    }
    
    this.selectedVehicle = vehicle;
    this.activeTab = 'details';
    this.showInlinePlayback = false; // Reset playback panel on vehicle switch
    this.popupPosition = { x: 0, y: 0 }; // Reset position to center
    this.isDragging = false;

    // Default playback dates to last 24 hours
    const now = new Date();
    const yesterday = new Date(now.getTime() - 24 * 60 * 60 * 1000);
    this.playbackFromDate = this.toLocalDateTimeString(yesterday);
    this.playbackToDate = this.toLocalDateTimeString(now);

    if (this.map && vehicle.currentLocation) {
      this.map.setView([vehicle.currentLocation.lat, vehicle.currentLocation.lng], 12);
    }
  }

  // Monitoring indicator helpers
  isFuelLow(vehicle: any): boolean {
    const stats = this.getVehicleStats(vehicle);
    return stats?.fuelLevel != null && stats.fuelLevel < 20;
  }

  isBatteryLow(vehicle: any): boolean {
    const stats = this.getVehicleStats(vehicle);
    return stats?.batteryLevel != null && stats.batteryLevel < 20;
  }

  isTemperatureHigh(vehicle: any): boolean {
    if (!vehicle.ignitionOn) return false;
    const stats = this.getVehicleStats(vehicle);
    return stats?.temperature != null && stats.temperature >= 105;
  }

  isOverSpeeding(vehicle: any): boolean {
    return (vehicle.currentSpeed || 0) > 130;
  }

  toggleInlinePlayback(vehicle: any) {
    if (this.showInlinePlayback && this.selectedVehicle?.id === vehicle.id) {
      this.showInlinePlayback = false;
    } else {
      this.showInlinePlayback = true;
      // Initialize dates if not set
      if (!this.playbackFromDate || !this.playbackToDate) {
        const now = new Date();
        const yesterday = new Date(now.getTime() - 24 * 60 * 60 * 1000);
        this.playbackFromDate = this.toLocalDateTimeString(yesterday);
        this.playbackToDate = this.toLocalDateTimeString(now);
      }
    }
  }

  // Hide the live marker of the selected vehicle during playback
  hideLiveMarker(vehicleId: string | number) {
    if (!this.map) return;
    
    const idStr = vehicleId.toString();
    
    // Hide ALL live markers during playback to avoid confusion
    this.vehicleMarkers.forEach((marker, key) => {
      marker.remove();
      this.hiddenLiveMarkers.set(key, marker);
    });
    console.log(`All ${this.hiddenLiveMarkers.size} live markers hidden for playback`);
  }

  // Restore ALL hidden live markers when playback ends
  restoreLiveMarker() {
    if (this.hiddenLiveMarkers.size > 0 && this.map) {
      this.hiddenLiveMarkers.forEach((marker, key) => {
        marker.addTo(this.map!);
      });
      console.log(`Restored ${this.hiddenLiveMarkers.size} live markers`);
      this.hiddenLiveMarkers.clear();
    }
  }

  applyFilters() {
    this.doApplyFilters();
    this.cdr.detectChanges();
    this.updateVehicleMarkers();
  }

  private doApplyFilters() {
    let filtered = [...this.vehicles];

    if (this.searchQuery) {
      const query = this.searchQuery.toLowerCase();
      filtered = filtered.filter((v: any) =>
        v.plate?.toLowerCase().includes(query) ||
        v.brand?.toLowerCase().includes(query) ||
        v.model?.toLowerCase().includes(query)
      );
    }

    if (this.filterStatus !== 'all') {
      if (this.filterStatus === 'online') {
        filtered = filtered.filter((v: any) => v.isOnline);
      } else if (this.filterStatus === 'moving') {
        filtered = filtered.filter((v: any) => v.isOnline && v.ignitionOn && (v.currentSpeed || 0) > 5);
      } else if (this.filterStatus === 'idle') {
        filtered = filtered.filter((v: any) => v.isOnline && v.ignitionOn && (v.currentSpeed || 0) <= 5);
      } else if (this.filterStatus === 'parked') {
        filtered = filtered.filter((v: any) => v.isOnline && !v.ignitionOn);
      } else if (this.filterStatus === 'offline') {
        filtered = filtered.filter((v: any) => !v.isOnline);
      }
    }

    // Create new array reference to trigger change detection
    this.filteredVehicles = [...filtered];
    console.log('Filtered vehicles:', this.filteredVehicles.length);
  }

  onSearchChange(query: string) {
    this.searchQuery = query;
    this.applyFilters();
  }

  onFilterStatusChange(status: string) {
    this.filterStatus = status;
    this.applyFilters();
  }

  updateStats() {
    const vehicles: any[] = this.vehicles;
    const online = vehicles.filter(v => v.isOnline);

    this.stats = {
      total: vehicles.length,
      online: online.length,
      moving: online.filter(v => (v.currentSpeed || 0) > 5).length,
      stopped: online.filter(v => (v.currentSpeed || 0) <= 5).length,
      offline: vehicles.length - online.length
    };
  }

  changeMapStyle(style: 'streets' | 'satellite' | 'terrain') {
    this.mapStyle = style;
    this.showLayersMenu = false;

    // During playback overlay: just swap tiles, don't destroy map
    if (this.playbackOverlayMap && this.map === this.playbackOverlayMap) {
      this.applyTileLayer(this.playbackOverlayMap);
      return;
    }

    if (this.map) {
      this.map.remove();
      this.map = null;
      this.mapReady = false;
      this.initializeMap();
    }
  }

  changeViewMode(mode: 'map' | 'list' | 'split') {
    this.viewMode = mode;
    if (mode === 'map' && this.map) {
      setTimeout(() => {
        this.map?.invalidateSize();
      }, 100);
    }
  }

  startAutoRefresh() {
    // ALWAYS poll periodically as a safety net — even when SignalR is connected,
    // frames can be lost in the pipeline (Redis PubSub disconnect, RabbitMQ backlog, etc.)
    // When connected: every 2 min (catch-up). When disconnected: every 30s (primary source).
    this.refreshInterval = setInterval(() => {
      if (this.connectionStatus !== 'Connected') {
        this.loadData();
      } else {
        // Light catch-up: only refresh if at least one vehicle may be stale
        const now = Date.now();
        const hasStale = this.vehicles.some((v: any) => {
          if (!v.isMoving && !v.ignitionOn) return false; // Parked vehicles are fine
          const last = v.lastCommunication || v.lastRecordedAt;
          if (!last) return true; // No data at all
          return (now - new Date(last).getTime()) > 120000; // >2 min without update
        });
        if (hasStale) {
          this.loadData();
        }
      }
    }, 30000); // Check every 30 seconds
  }

  startStalenessCheck() {
    // Every 30 seconds, check if any vehicle's last position is stale
    // If a vehicle was "moving" but no update for 2+ minutes → mark stopped
    // NOTE: Online/offline status is determined by the backend, not here.
    // Parked vehicles may only send frames every 30 minutes (throttled).
    this.stalenessInterval = setInterval(() => {
      const now = new Date().getTime();
      let changed = false;

      this.vehicles.forEach((vehicle: any) => {
        const lastUpdate = vehicle.lastCommunication || vehicle.lastRecordedAt;
        if (!lastUpdate) return;

        const lastTime = new Date(lastUpdate).getTime();
        const ageMs = now - lastTime;
        const ageMinutes = ageMs / 60000;

        if (ageMinutes > 2 && vehicle.isMoving) {
          // Was moving but no update for 2+ minutes → mark stopped
          vehicle.isMoving = false;
          vehicle.currentSpeed = 0;
          changed = true;
        }
      });

      if (changed) {
        this.ngZone.run(() => {
          this.updateStats();
          this.doApplyFilters();
          this.cdr.detectChanges();
          // Update markers to reflect new status colors
          this.vehicles.forEach((v: any) => {
            if (this.vehicleMarkers.has(v.id?.toString())) {
              this.updateSingleVehicleMarker(v);
            }
          });
        });
      }
    }, 30000); // Check every 30 seconds
  }

  refresh() {
    this.loadData();
  }

  getVehicleStatusClass(vehicle: any): string {
    if (!vehicle.isOnline) return 'status-offline';
    if ((vehicle.currentSpeed || 0) > 5) return 'status-moving';
    return 'status-stopped';
  }

  getVehicleStatusLabel(vehicle: any): string {
    if (!vehicle.isOnline) return 'Hors ligne';
    if ((vehicle.currentSpeed || 0) > 5) return 'En mouvement';
    return 'Arrêté';
  }

  // TrackBy function for better ngFor performance
  trackByVehicleId(index: number, vehicle: any): string {
    return vehicle.id;
  }

  navigate(path: string) {
    this.router.navigate([path]);
  }

  // Panel toggle
  togglePanel() {
    this.isPanelCollapsed = !this.isPanelCollapsed;
    setTimeout(() => {
      if (this.map) {
        this.map.invalidateSize();
      }
    }, 300);
  }

  // Get driver initial for avatar
  getDriverInitial(vehicle: any): string {
    if (vehicle.assignedDriver) {
      return vehicle.assignedDriver.charAt(0).toUpperCase();
    }
    return '?';
  }

  // Playback methods
  loadPlaybackRoute() {
    if (!this.selectedVehicle || !this.playbackFromDate || !this.playbackToDate) {
      alert('Veuillez sélectionner un véhicule et une plage de dates');
      return;
    }

    this.playbackLoading = true;
    this.clearPlayback();

    const fromDate = new Date(this.playbackFromDate);
    const toDate = new Date(this.playbackToDate);
    const vehicleId = parseInt(this.selectedVehicle.id, 10);
    const fromTime = fromDate.getTime();
    const toTime = toDate.getTime();

    // Store the vehicle ID for this playback to ensure data isolation
    this.playbackVehicleId = vehicleId;

    this.apiService.getVehicleHistory(vehicleId, fromDate, toDate, 3000, false).subscribe({
      next: (positions) => {
        // Verify this is still the correct vehicle (user might have switched)
        if (this.playbackVehicleId !== vehicleId) {
          console.log('Vehicle changed during load, discarding results');
          return;
        }
        
        this.playbackRawCount = positions.length;
        
        console.log(`Playback: Received ${positions.length} positions from API`);

        // Note: isRealTime filter removed - all GPS positions are valid for playback
        // The recordedAt timestamp is the GPS device timestamp
        // Backend already filters by date range, so no additional filtering needed here
        const filteredPositions = positions.filter((position: any) => {
          // Just validate that recordedAt exists and is valid
          const recordedAt = new Date(position.recordedAt).getTime();
          return !isNaN(recordedAt);
        });

        // Positions are now returned as a direct array
        this.playbackPositions = [...filteredPositions].sort((a: any, b: any) => 
          new Date(a.recordedAt).getTime() - new Date(b.recordedAt).getTime()
        );
        
        if (this.playbackPositions.length === 0) {
          alert('Aucune position trouvée pour cette période');
          this.playbackLoading = false;
          return;
        }

        this.playbackIndex = 0;
        this.playbackProgress = 0;

        // Pre-populate address cache from positions that already have addresses
        this.playbackAddressCache.clear();
        this.playbackPositions.forEach((p: any) => {
          if (p.address && !p.address.includes('°')) {
            const key = `${p.latitude.toFixed(4)},${p.longitude.toFixed(4)}`;
            this.playbackAddressCache.set(key, p.address);
          }
        });

        // Save monitoring map and show overlay
        this.monitoringMap = this.map;
        this.isPlaybackLoaded = true;
        this.playbackCurrentAddress = 'Chargement...';
        this.cdr.detectChanges();

        // Initialize overlay map after DOM renders
        setTimeout(() => {
          this.initPlaybackOverlayMap();

          // Pre-process: consolidate rapid ignition ON/OFF toggles
          this.smoothIgnitionData();

          // Process route: batch road correction via Valhalla
          this.processPlaybackRoute().then(() => {
            this.playbackLoading = false;
            this.drawPlaybackRoute();
            this.updatePlaybackMarker();
            this.updatePlaybackAddress();
            this.cdr.detectChanges();
            console.log(`[Playback] Loaded for vehicle ${vehicleId}: ${this.playbackPositions.length} GPS points -> ${this.matchedRouteCoords.length} road points`);
          }).catch((err: Error) => {
            console.error('[Playback] Batch road correction FAILED, using raw GPS:', err);
            this.matchedRouteCoords = this.playbackPositions.map(p => L.latLng(p.latitude, p.longitude));
            this.segmentBoundaries = [];
            this.matchedRouteIndex = 0;
            this.playbackLoading = false;
            this.drawPlaybackRoute();
            this.updatePlaybackMarker();
            this.updatePlaybackAddress();
            this.cdr.detectChanges();
          });
        }, 100);
      },
      error: (err) => {
        console.error('Error loading playback data:', err);
        alert('Erreur lors du chargement de l\'historique');
        this.playbackLoading = false;
        this.cdr.detectChanges();
      }
    });
  }

  drawPlaybackRoute() {
    if (!this.map || this.playbackPositions.length === 0) return;

    // Remove existing route elements
    this.clearRouteDisplay();
    this.traceDrawnUpToIndex = 0;
    this.progressCoords = [];

    // Center map on start position (no ghost route preview)
    const firstPos = this.playbackPositions[0];
    const firstLatLng = this.getSnappedLatLng(0);
    const firstLatLngArr = Array.isArray(firstLatLng) ? firstLatLng : [firstPos.latitude, firstPos.longitude];
    this.map.setView(firstLatLngArr as L.LatLngExpression, 15);

    // Create progress polyline that grows as the vehicle moves
    if (this.progressPolyline) { this.progressPolyline.remove(); }
    this.progressPolyline = L.polyline([], {
      color: '#6366f1',
      weight: 3,
      opacity: 0.8,
      dashArray: undefined
    }).addTo(this.map);

    // Add start marker
    const startPos = this.playbackPositions[0];
    const startLatLng = this.getSnappedLatLng(0);
    L.circleMarker(startLatLng as L.LatLngExpression, {
      radius: 8, fillColor: '#22c55e', color: '#fff', weight: 3, fillOpacity: 1
    }).addTo(this.map).bindTooltip('Départ', { permanent: false });

    // Point markers and end marker will be added progressively during animation

    // Stop marker counter reset
    this.stopMarkerCount = 0;
  }

  // Place a stop marker at the given position during animation
  // Called inline from animateToNextPoint when ignition OFF is detected
  private placeStopMarker(lat: number, lng: number, startTime: Date, endTime: Date, fuel: number | null) {
    if (!this.map) return;
    this.stopMarkerCount++;
    const stopNum = this.stopMarkerCount;

    const durationMs = endTime.getTime() - startTime.getTime();
    const durationMin = Math.round(durationMs / 60000);
    const durationLabel = durationMin >= 60
      ? `${Math.floor(durationMin / 60)}h${durationMin % 60 > 0 ? (durationMin % 60) + 'min' : ''}`
      : `${durationMin}min`;

    const durationH = Math.floor(durationMs / 3600000);
    const durationM = Math.round((durationMs % 3600000) / 60000);
    const durationFull = durationH > 0 ? `${durationH}h ${durationM}min` : `${durationM} min`;

    const bgColor = '#ef4444';
    const borderColor = '#dc2626';

    const icon = L.divIcon({
      html: `
        <div style="
          position:relative;
          display:flex;align-items:center;justify-content:center;
          width:36px;height:36px;
          background:${bgColor};
          border:3px solid #fff;
          border-radius:50%;
          box-shadow:0 2px 8px rgba(0,0,0,0.35);
          cursor:pointer;
          transition:transform 0.2s;
        "
        onmouseenter="this.style.transform='scale(1.25)'"
        onmouseleave="this.style.transform='scale(1)'"
        >
          <span style="font-size:16px;line-height:1;">🅿️</span>
        </div>
        <div style="
          position:absolute;
          bottom:-18px;left:50%;transform:translateX(-50%);
          background:${bgColor};
          color:#fff;
          font-size:9px;font-weight:700;
          padding:1px 5px;border-radius:6px;
          white-space:nowrap;
          box-shadow:0 1px 3px rgba(0,0,0,0.3);
        ">${durationLabel}</div>
      `,
      className: 'stationary-stop-marker',
      iconSize: [36, 50],
      iconAnchor: [18, 18]
    });

    const marker = L.marker([lat, lng], { icon, zIndexOffset: 500 }).addTo(this.map);

    // Hover tooltip
    const startTimeStr = startTime.toLocaleString('fr-FR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
    const endTimeStr = endTime.toLocaleString('fr-FR', { hour: '2-digit', minute: '2-digit' });
    marker.bindTooltip(`Moteur éteint — ${durationLabel} (${startTimeStr} → ${endTimeStr})`, {
      direction: 'top', offset: [0, -24], className: 'stationary-tooltip'
    });

    // Click popup
    const fuelDisplay = fuel != null ? `${fuel}%` : 'N/A';
    const startFull = startTime.toLocaleString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit' });
    const endFull = endTime.toLocaleString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit' });

    const cacheKey = `${lat.toFixed(4)},${lng.toFixed(4)}`;
    const cachedAddr = this.playbackAddressCache.get(cacheKey) || '';

    marker.bindPopup(`
      <div style="font-family:'Inter',-apple-system,sans-serif;min-width:260px;padding:0;margin:-14px -20px;">
        <div style="background:linear-gradient(135deg,${bgColor},${borderColor});padding:10px 14px;border-radius:8px 8px 0 0;display:flex;align-items:center;gap:8px;">
          <span style="font-size:20px;">🅿️</span>
          <div>
            <div style="font-weight:700;font-size:13px;color:#fff;">Stationnement</div>
            <div style="font-size:11px;color:rgba(255,255,255,0.85);">Arrêt #${stopNum}</div>
          </div>
        </div>
        ${cachedAddr ? `<div style="padding:8px 14px;background:#fff;border-bottom:1px solid #e5e7eb;font-size:11px;color:#6366f1;font-weight:500;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;">📍 ${cachedAddr}</div>` : ''}
        <div style="background:#fff;padding:12px 14px;">
          <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px;">
            <div style="background:#f8fafc;padding:8px 10px;border-radius:8px;border:1px solid #e2e8f0;">
              <div style="font-size:9px;color:#94a3b8;text-transform:uppercase;letter-spacing:0.5px;margin-bottom:2px;">Durée</div>
              <div style="font-size:16px;font-weight:700;color:#1e293b;">${durationFull}</div>
            </div>
            <div style="background:#f8fafc;padding:8px 10px;border-radius:8px;border:1px solid #e2e8f0;">
              <div style="font-size:9px;color:#94a3b8;text-transform:uppercase;letter-spacing:0.5px;margin-bottom:2px;">Carburant</div>
              <div style="font-size:14px;font-weight:700;color:#f59e0b;">${fuelDisplay}</div>
            </div>
          </div>
          <div style="margin-top:10px;display:grid;grid-template-columns:1fr 1fr;gap:10px;">
            <div style="background:#f0fdf4;padding:8px 10px;border-radius:8px;border:1px solid #bbf7d0;">
              <div style="font-size:9px;color:#16a34a;text-transform:uppercase;letter-spacing:0.5px;margin-bottom:2px;">Début</div>
              <div style="font-size:11px;font-weight:600;color:#15803d;">${startFull}</div>
            </div>
            <div style="background:#fef2f2;padding:8px 10px;border-radius:8px;border:1px solid #fecaca;">
              <div style="font-size:9px;color:#dc2626;text-transform:uppercase;letter-spacing:0.5px;margin-bottom:2px;">Fin</div>
              <div style="font-size:11px;font-weight:600;color:#b91c1c;">${endFull}</div>
            </div>
          </div>
        </div>
        <div style="padding:6px 14px 8px;background:#f8fafc;font-size:10px;color:#94a3b8;font-family:monospace;text-align:center;border-radius:0 0 8px 8px;">
          ${lat.toFixed(6)}, ${lng.toFixed(6)}
        </div>
      </div>
    `, { maxWidth: 320 });

    // Lazy geocode on popup open
    if (!cachedAddr) {
      marker.on('popupopen', () => {
        this.geocodingService.reverseGeocode(lat, lng).subscribe({
          next: (addr) => {
            if (addr && !addr.includes('°')) {
              this.playbackAddressCache.set(cacheKey, addr);
              marker.closePopup();
              setTimeout(() => {
                const content = marker.getPopup()?.getContent() as string;
                if (content && !content.includes(addr)) {
                  marker.setPopupContent(content.replace(
                    '</div>\n        <div style="background:#fff;padding:12px 14px;">',
                    `</div>\n        <div style="padding:8px 14px;background:#fff;border-bottom:1px solid #e5e7eb;font-size:11px;color:#6366f1;font-weight:500;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;">📍 ${addr}</div>\n        <div style="background:#fff;padding:12px 14px;">`
                  ));
                }
                marker.openPopup();
              }, 100);
            }
          }
        });
      }, { once: true } as any);
    }

    this.stationaryMarkers.push(marker);
    console.log(`[Playback] Stop marker #${stopNum} placed at ${lat.toFixed(5)},${lng.toFixed(5)} — ${durationLabel}`);
  }

  /**
   * Pre-process ignition data to consolidate rapid ON/OFF toggles.
   * GPS devices often send noisy ignition signals when parked, creating
   * hundreds of micro-stops (1-2 points OFF, then 1-2 ON, then OFF again).
   * This merges them into proper consolidated stop periods.
   * Must run BEFORE processPlaybackRoute() so routing sees clean data.
   */
  private smoothIgnitionData() {
    const positions = this.playbackPositions;
    if (positions.length < 5) return;

    const MAX_MERGE_DISTANCE_M = 100; // Max distance between OFF positions to merge
    const MAX_GAP_POINTS = 5; // Max ON points between two OFF periods to merge
    const MIN_STOP_DURATION_MS = 60 * 1000; // 1 minute minimum for a real stop

    // Step 1: Find all ignition OFF periods
    let offPeriods: { start: number; end: number }[] = [];
    let i = 0;
    while (i < positions.length) {
      if (positions[i].ignitionOn === false) {
        const start = i;
        while (i < positions.length && positions[i].ignitionOn === false) i++;
        offPeriods.push({ start, end: i - 1 });
      } else {
        i++;
      }
    }

    if (offPeriods.length < 2) {
      // Even with single period, filter if too short
      if (offPeriods.length === 1) {
        const p = offPeriods[0];
        const dur = new Date(positions[p.end].recordedAt).getTime() - new Date(positions[p.start].recordedAt).getTime();
        if (dur < MIN_STOP_DURATION_MS && p.end - p.start <= 1) {
          for (let j = p.start; j <= p.end; j++) positions[j].ignitionOn = true;
        }
      }
      return;
    }

    // Step 2: Iteratively merge nearby OFF periods at the same location
    let changed = true;
    while (changed) {
      changed = false;
      const merged: { start: number; end: number }[] = [offPeriods[0]];

      for (let k = 1; k < offPeriods.length; k++) {
        const prev = merged[merged.length - 1];
        const curr = offPeriods[k];
        const gapPoints = curr.start - prev.end - 1;

        if (gapPoints <= MAX_GAP_POINTS) {
          const prevPos = positions[prev.start];
          const currPos = positions[curr.start];
          const dist = this.calculateDistance(
            prevPos.latitude, prevPos.longitude,
            currPos.latitude, currPos.longitude
          );

          if (dist < MAX_MERGE_DISTANCE_M) {
            prev.end = curr.end;
            changed = true;
            continue;
          }
        }
        merged.push({ start: curr.start, end: curr.end });
      }
      offPeriods = merged;
    }

    // Step 3: Apply merged periods — mark all points in each period as OFF
    for (const period of offPeriods) {
      for (let j = period.start; j <= period.end; j++) {
        positions[j].ignitionOn = false;
      }
    }

    // Step 4: Remove very short OFF periods (< 1 min AND < 2 points) as noise
    for (const period of offPeriods) {
      const dur = new Date(positions[period.end].recordedAt).getTime() -
                  new Date(positions[period.start].recordedAt).getTime();
      if (dur < MIN_STOP_DURATION_MS && period.end - period.start <= 1) {
        for (let j = period.start; j <= period.end; j++) {
          positions[j].ignitionOn = true;
        }
      }
    }

    // Log result
    const finalOffPeriods: { start: number; end: number }[] = [];
    i = 0;
    while (i < positions.length) {
      if (positions[i].ignitionOn === false) {
        const start = i;
        while (i < positions.length && positions[i].ignitionOn === false) i++;
        finalOffPeriods.push({ start, end: i - 1 });
      } else {
        i++;
      }
    }
    console.log(`[Playback] Ignition smoothing: ${offPeriods.length} raw OFF periods → ${finalOffPeriods.length} consolidated stops`);
  }

  // Place stop markers for ALL ignition OFF periods at once (used by skipToEnd)
  private placeAllStopMarkers() {
    if (!this.map || this.playbackPositions.length < 2) return;
    let i = 0;
    while (i < this.playbackPositions.length) {
      const pos = this.playbackPositions[i];
      if (pos.ignitionOn === false) {
        const startIdx = i;
        while (i < this.playbackPositions.length - 1 && this.playbackPositions[i + 1].ignitionOn === false) {
          i++;
        }
        const startTime = new Date(this.playbackPositions[startIdx].recordedAt);
        const endTime = new Date(this.playbackPositions[i].recordedAt);
        let fuel: number | null = null;
        for (let j = startIdx; j <= i; j++) {
          if (this.playbackPositions[j].fuelRaw != null) fuel = this.playbackPositions[j].fuelRaw;
        }
        this.placeStopMarker(
          this.playbackPositions[startIdx].latitude,
          this.playbackPositions[startIdx].longitude,
          startTime, endTime, fuel
        );
      }
      i++;
    }
  }

  addPointMarkers() {
    if (!this.map) return;

    this.playbackPositions.forEach((position, index) => {
      const marker = L.circleMarker([position.latitude, position.longitude], {
        radius: 5,
        fillColor: '#3b82f6',
        color: '#ffffff',
        weight: 2,
        opacity: 1,
        fillOpacity: 0.8
      }).addTo(this.map!);

      // Format date/time
      const time = new Date(position.recordedAt).toLocaleString('fr-FR', {
        day: '2-digit',
        month: '2-digit',
        year: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit'
      });

      // Create popup content with details
      const speed = position.speedKph || 0;
      const isMoving = speed > 3;
      const speedColor = speed > 80 ? '#ef4444' : speed > 50 ? '#f59e0b' : '#10b981';
      
      const popupContent = `
        <div style="
          font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
          min-width: 200px;
          padding: 0;
          margin: -14px -20px;
        ">
          <!-- Header -->
          <div style="
            background: linear-gradient(135deg, #3b82f6 0%, #1d4ed8 100%);
            padding: 10px 14px;
            border-radius: 8px 8px 0 0;
            display: flex;
            align-items: center;
            justify-content: space-between;
          ">
            <div style="display: flex; align-items: center; gap: 8px;">
              <div style="
                width: 24px;
                height: 24px;
                background: rgba(255,255,255,0.2);
                border-radius: 50%;
                display: flex;
                align-items: center;
                justify-content: center;
                font-size: 11px;
                font-weight: 700;
                color: #fff;
              ">${index + 1}</div>
              <span style="font-weight: 500; font-size: 12px; color: #fff;">Point de trace</span>
            </div>
            <span style="font-size: 11px; color: rgba(255,255,255,0.8);">${time.split(' ')[1] || ''}</span>
          </div>
          
          <!-- Speed highlight -->
          <div style="
            background: #fff;
            padding: 12px 14px;
            display: flex;
            align-items: center;
            gap: 12px;
            border-bottom: 1px solid #e5e7eb;
          ">
            <div style="
              width: 44px;
              height: 44px;
              background: ${speedColor}15;
              border-radius: 10px;
              display: flex;
              align-items: center;
              justify-content: center;
              border: 2px solid ${speedColor};
            ">
              <span style="font-size: 16px; font-weight: 700; color: ${speedColor};">${speed.toFixed(0)}</span>
            </div>
            <div>
              <div style="font-size: 11px; color: #64748b; text-transform: uppercase; letter-spacing: 0.5px;">Vitesse</div>
              <div style="font-size: 14px; font-weight: 600; color: #1e293b;">${speed.toFixed(1)} km/h</div>
            </div>
          </div>
          
          <!-- Details grid -->
          <div style="padding: 10px 14px; background: #f8fafc; border-radius: 0 0 8px 8px;">
            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 8px; font-size: 11px;">
              <div style="background: #fff; padding: 8px 10px; border-radius: 6px; border: 1px solid #e2e8f0;">
                <div style="color: #64748b; margin-bottom: 2px;">⛽ Carburant</div>
                <div style="font-weight: 600; color: #1e293b;">${position.fuelRaw || 0}%</div>
              </div>
              <div style="background: #fff; padding: 8px 10px; border-radius: 6px; border: 1px solid #e2e8f0;">
                <div style="color: #64748b; margin-bottom: 2px;">🌡️ Température</div>
                <div style="font-weight: 600; color: #1e293b;">${position.temperatureC != null ? position.temperatureC + '°C' : 'N/A'}</div>
              </div>
            </div>
            <div id="playback-addr-${index}" style="
              margin-top: 8px;
              padding: 8px 10px;
              background: #fff;
              border-radius: 6px;
              border: 1px solid #e2e8f0;
              font-size: 11px;
              color: #475569;
              display: flex;
              align-items: flex-start;
              gap: 6px;
            ">
              <span style="flex-shrink:0;">📍</span>
              <span class="addr-text">${position.address && !position.address.includes('°') ? position.address : 'Chargement...'}</span>
            </div>
            <div style="
              margin-top: 4px;
              padding: 4px 10px;
              font-size: 9px;
              color: #94a3b8;
              font-family: 'SF Mono', Monaco, monospace;
              text-align: center;
            ">
              ${position.latitude.toFixed(6)}, ${position.longitude.toFixed(6)}
            </div>
          </div>
        </div>
      `;

      marker.bindPopup(popupContent);

      // Lazy geocode on popup open if no address
      if (!position.address || position.address.includes('°')) {
        marker.on('popupopen', () => {
          const cacheKey = `${position.latitude.toFixed(4)},${position.longitude.toFixed(4)}`;
          const addrEl = document.querySelector(`#playback-addr-${index} .addr-text`);
          if (!addrEl) return;

          if (this.playbackAddressCache.has(cacheKey)) {
            const cached = this.playbackAddressCache.get(cacheKey)!;
            if (!cached.includes('°')) addrEl.textContent = cached;
            return;
          }

          this.geocodingService.reverseGeocode(position.latitude, position.longitude).subscribe({
            next: (addr) => {
              if (addr && !addr.includes('°')) {
                this.playbackAddressCache.set(cacheKey, addr);
                const el = document.querySelector(`#playback-addr-${index} .addr-text`);
                if (el) el.textContent = addr;
              } else {
                const el = document.querySelector(`#playback-addr-${index} .addr-text`);
                if (el) el.textContent = `${position.latitude.toFixed(5)}, ${position.longitude.toFixed(5)}`;
              }
            }
          });
        }, { once: true } as any);
      }

      this.pointMarkers.push(marker);
    });
  }

  // Add a small dot at the current position during playback animation
  addSinglePointMarker(index: number) {
    if (!this.map || index < 0 || index >= this.playbackPositions.length) return;
    
    const position = this.playbackPositions[index];
    const latLng = this.getSnappedLatLng(index);
    const speed = position.speedKph || 0;
    const dateStr = new Date(position.recordedAt).toLocaleString('fr-FR', {
      day: '2-digit', month: '2-digit', year: 'numeric',
      hour: '2-digit', minute: '2-digit', second: '2-digit'
    });
    const cacheKey = `${position.latitude.toFixed(4)},${position.longitude.toFixed(4)}`;
    // Priority: backend address > cache > coordinates (with lazy geocode)
    let address = position.address || this.playbackAddressCache?.get(cacheKey) || '';

    const marker = L.circleMarker(latLng as L.LatLngExpression, {
      radius: 4,
      fillColor: '#6366f1',
      color: '#ffffff',
      weight: 1.5,
      opacity: 1,
      fillOpacity: 0.9
    }).addTo(this.map);

    if (address && !address.includes('°')) {
      marker.bindTooltip(`${dateStr} — ${address} — ${speed.toFixed(0)} km/h`, {
        direction: 'top', offset: [0, -6]
      });
    } else {
      // Show coordinates initially, geocode lazily on first hover
      marker.bindTooltip(`${dateStr} — ${position.latitude.toFixed(4)}°, ${position.longitude.toFixed(4)}° — ${speed.toFixed(0)} km/h`, {
        direction: 'top', offset: [0, -6]
      });
      marker.on('tooltipopen', () => {
        if (this.playbackAddressCache.has(cacheKey)) {
          const cached = this.playbackAddressCache.get(cacheKey)!;
          if (!cached.includes('°')) {
            marker.setTooltipContent(`${dateStr} — ${cached} — ${speed.toFixed(0)} km/h`);
          }
          return;
        }
        this.geocodingService.reverseGeocode(position.latitude, position.longitude).subscribe({
          next: (addr) => {
            if (addr && !addr.includes('°')) {
              this.playbackAddressCache.set(cacheKey, addr);
              marker.setTooltipContent(`${dateStr} — ${addr} — ${speed.toFixed(0)} km/h`);
            }
          }
        });
      }, { once: true } as any);
    }

    this.pointMarkers.push(marker);
  }

  clearRouteDisplay() {
    if (this.playbackPolyline) {
      this.playbackPolyline.remove();
      this.playbackPolyline = null;
    }
    if (this.routingControl) {
      this.map?.removeControl(this.routingControl);
      this.routingControl = null;
    }
    // Clear ghost and progress polylines
    if (this.ghostPolyline) { this.ghostPolyline.remove(); this.ghostPolyline = null; }
    if (this.progressPolyline) { this.progressPolyline.remove(); this.progressPolyline = null; }
    this.progressCoords = [];
    // Clear legacy progressive polylines
    this.progressivePolylines.forEach(polyline => polyline.remove());
    this.progressivePolylines = [];
    this.traceDrawnUpToIndex = 0;
    // Clear point markers
    this.pointMarkers.forEach(marker => marker.remove());
    this.pointMarkers = [];
    // Clear stationary stop markers
    this.stationaryMarkers.forEach(marker => marker.remove());
    this.stationaryMarkers = [];
  }

  drawStraightPath(coords: L.LatLng[]) {
    if (!this.map) return;
    
    this.playbackPolyline = L.polyline(coords, {
      color: '#3b82f6',
      weight: 4,
      opacity: 0.8
    }).addTo(this.map);

    this.map.fitBounds(this.playbackPolyline.getBounds().pad(0.1));
  }

  async drawRoutedPath(coords: L.LatLng[]) {
    if (!this.map || coords.length < 2) return;

    // Filter out points that are too close together (< 15m) to avoid erratic routing
    // This is especially important for stationary vehicles with GPS jitter
    const filteredCoords: L.LatLng[] = [coords[0]];
    for (let i = 1; i < coords.length; i++) {
      const lastKept = filteredCoords[filteredCoords.length - 1];
      const distance = this.calculateDistance(lastKept.lat, lastKept.lng, coords[i].lat, coords[i].lng);
      if (distance >= 15) {
        filteredCoords.push(coords[i]);
      }
    }
    // Always include the last point if it was filtered out
    if (filteredCoords.length > 0 && filteredCoords[filteredCoords.length - 1] !== coords[coords.length - 1]) {
      const lastKept = filteredCoords[filteredCoords.length - 1];
      const lastOriginal = coords[coords.length - 1];
      const distance = this.calculateDistance(lastKept.lat, lastKept.lng, lastOriginal.lat, lastOriginal.lng);
      if (distance >= 5) { // Lower threshold for final point
        filteredCoords.push(lastOriginal);
      }
    }

    console.log(`Filtered ${coords.length} points to ${filteredCoords.length} for Valhalla routing`);

    // If too few points after filtering, just draw straight line
    if (filteredCoords.length < 2) {
      this.drawStraightPath(coords);
      return;
    }

    // Use Valhalla for road snapping (better map-matching than OSRM)
    const points = filteredCoords.map(c => ({
      lat: c.lat,
      lon: c.lng,
      timestamp: null
    }));

    try {
      const response = await fetch('/api/routing/snap', {
        method: 'POST',
        headers: { 
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${localStorage.getItem('auth_token')}`
        },
        body: JSON.stringify({ points })
      });
      
      if (!response.ok) {
        throw new Error(`Valhalla API error: ${response.status}`);
      }

      const data = await response.json();
      
      if (data.points && data.points.length > 0) {
        // Use snapped coordinates from Valhalla
        const routeCoords: L.LatLng[] = data.points.map(
          (p: any) => L.latLng(p.snappedLat, p.snappedLon)
        );

        this.playbackPolyline = L.polyline(routeCoords, {
          color: '#3b82f6',
          weight: 5,
          opacity: 0.8
        }).addTo(this.map!);

        this.map!.fitBounds(this.playbackPolyline.getBounds().pad(0.1));
        console.log('Valhalla road-snapped route drawn successfully with', routeCoords.length, 'points');
      } else {
        throw new Error('Invalid Valhalla response: no points returned');
      }
    } catch (error) {
      console.error('[Playback] Road snapping FAILED, falling back to straight lines:', error);
      this.drawStraightPath(coords);
    }
  }


  // Get snapped road position for a given playback index (uses segmentBoundaries)
  private getSnappedLatLng(index: number): L.LatLngExpression {
    // Anchored: return fixed position to prevent GPS drift while parked/stopped
    if (this.ignitionOffAnchor) {
      return [this.ignitionOffAnchor.latitude, this.ignitionOffAnchor.longitude];
    }
    if (this.stoppedAnchor) {
      return [this.stoppedAnchor.latitude, this.stoppedAnchor.longitude];
    }
    // Primary: use segment boundaries mapping
    if (this.matchedRouteCoords.length > 0 && this.segmentBoundaries.length > 0 && index < this.segmentBoundaries.length) {
      const roadIdx = this.segmentBoundaries[index];
      if (roadIdx !== undefined && roadIdx < this.matchedRouteCoords.length) {
        const snapped = this.matchedRouteCoords[roadIdx];
        if (snapped && !isNaN(snapped.lat) && !isNaN(snapped.lng)) {
          return [snapped.lat, snapped.lng];
        }
      }
    }
    // Fallback: find nearest point on matchedRouteCoords
    const pos = this.playbackPositions[index];
    if (!pos) return [0, 0];
    if (this.matchedRouteCoords.length > 0) {
      let bestDist = Infinity;
      let bestCoord = this.matchedRouteCoords[0];
      for (let i = 0; i < this.matchedRouteCoords.length; i++) {
        const c = this.matchedRouteCoords[i];
        const dLat = c.lat - pos.latitude;
        const dLng = c.lng - pos.longitude;
        const dist = dLat * dLat + dLng * dLng;
        if (dist < bestDist) {
          bestDist = dist;
          bestCoord = c;
        }
      }
      if (bestCoord && !isNaN(bestCoord.lat) && !isNaN(bestCoord.lng)) {
        return [bestCoord.lat, bestCoord.lng];
      }
    }
    return [pos.latitude, pos.longitude];
  }

  updatePlaybackMarker() {
    if (!this.map || this.playbackPositions.length === 0) return;

    const position = this.playbackPositions[this.playbackIndex];
    if (!position) return;

    // Snap marker to Valhalla road-matched coordinates when available
    const latLng = this.getSnappedLatLng(this.playbackIndex);
    const statusColor = this.getStatusColor(position);
    const speed = position.speedKph || 0;
    const heading = position.courseDeg || 0;
    
    // Get vehicle type and name from selected vehicle
    const vehicleType = this.selectedVehicle?.type || (this.selectedVehicle as any)?.vehicleType || 'car';
    const vehicleName = this.selectedVehicle?.plate || (this.selectedVehicle as any)?.name || '';

    // Create an enhanced vehicle icon with status color, direction indicator, type and name
    const vehicleIcon = this.createPlaybackVehicleIcon(statusColor, heading, speed, vehicleType, vehicleName);

    if (this.playbackMarker) {
      this.playbackMarker.setLatLng(latLng);
      this.playbackMarker.setIcon(vehicleIcon);
    } else {
      this.playbackMarker = L.marker(latLng, { 
        icon: vehicleIcon,
        zIndexOffset: 1000 // Keep vehicle on top
      }).addTo(this.map);
    }

    // Build popup with detailed position info including cached address
    const time = new Date(position.recordedAt).toLocaleString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit' });
    const statusLabel = this.getPlaybackStatusLabel();
    const ignitionStatus = position.ignitionOn ? 'Allumé' : 'Éteint';
    const ignitionColor = position.ignitionOn ? '#10b981' : '#ef4444';
    const fuelDisplay = position.fuelRaw != null ? `${position.fuelRaw}%` : 'N/A';
    const fuelColor = position.fuelRaw != null ? '#f59e0b' : '#94a3b8';
    const addrKey = `${position.latitude.toFixed(4)},${position.longitude.toFixed(4)}`;
    const cachedAddr = this.playbackAddressCache.get(addrKey) || '';
    
    this.playbackMarker.bindPopup(`
      <div style="font-family:'Inter',-apple-system,sans-serif;min-width:240px;padding:0;margin:-14px -20px;">
        <div style="background:linear-gradient(135deg,${statusColor},${statusColor}dd);padding:10px 14px;border-radius:8px 8px 0 0;display:flex;align-items:center;justify-content:space-between;">
          <span style="font-weight:600;font-size:12px;color:#fff;">${statusLabel}</span>
          <span style="background:rgba(255,255,255,0.2);padding:3px 8px;border-radius:10px;font-size:10px;font-weight:600;color:#fff;">${time}</span>
        </div>
        ${cachedAddr ? `<div style="padding:8px 14px;background:#fff;border-bottom:1px solid #e5e7eb;font-size:11px;color:#6366f1;font-weight:500;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;">📍 ${cachedAddr}</div>` : ''}
        <div style="background:#fff;padding:12px 14px;display:grid;grid-template-columns:1fr 1fr 1fr;gap:8px;text-align:center;">
          <div><div style="font-size:22px;font-weight:700;color:#1e293b;">${speed.toFixed(0)}</div><div style="font-size:9px;color:#94a3b8;text-transform:uppercase;">km/h</div></div>
          <div><div style="font-size:14px;font-weight:700;color:${ignitionColor};">${ignitionStatus}</div><div style="font-size:9px;color:#94a3b8;text-transform:uppercase;">Moteur</div></div>
          <div><div style="font-size:14px;font-weight:700;color:${fuelColor};">${fuelDisplay}</div><div style="font-size:9px;color:#94a3b8;text-transform:uppercase;">Carburant</div></div>
        </div>
        <div style="padding:6px 14px 8px;background:#f8fafc;font-size:10px;color:#94a3b8;font-family:monospace;text-align:center;border-radius:0 0 8px 8px;">
          ${position.latitude.toFixed(6)}, ${position.longitude.toFixed(6)} · ${heading}°
        </div>
      </div>
    `);

    // Throttled address update for the live info panel (every 5th marker update)
    this.playbackAddressThrottle++;
    if (this.playbackAddressThrottle >= 5) {
      this.playbackAddressThrottle = 0;
      this.updatePlaybackAddress();
    }
  }

  // Create a clean directional arrow icon for playback
  createPlaybackVehicleIcon(color: string, heading: number, speed: number, vehicleType?: string, vehicleName?: string): L.DivIcon {
    const isMoving = speed > 3;
    // Clean arrow: solid colored triangle pointing up, rotated by heading
    return L.divIcon({
      html: `
        <div style="transform: rotate(${heading}deg); transition: transform 0.3s ease;">
          <svg width="28" height="28" viewBox="0 0 28 28" xmlns="http://www.w3.org/2000/svg" style="filter: drop-shadow(0 2px 4px rgba(0,0,0,0.35));">
            <polygon points="14,2 24,24 14,19 4,24" fill="${color}" stroke="#fff" stroke-width="2" stroke-linejoin="round"/>
            ${isMoving ? '' : '<circle cx="14" cy="15" r="3" fill="#fff" opacity="0.6"/>'}
          </svg>
        </div>
      `,
      className: 'pb-arrow-marker',
      iconSize: [28, 28],
      iconAnchor: [14, 14]
    });
  }

  togglePlayback() {
    if (!this.isPlaybackLoaded) return;

    this.isPlaying = !this.isPlaying;

    if (this.isPlaying) {
      this.startPlaybackAnimation();
    } else {
      this.stopPlaybackAnimation();
    }
  }

  startPlaybackAnimation() {
    if (this.playbackInterval) {
      clearInterval(this.playbackInterval);
    }
    if (this.animationFrameId) {
      cancelAnimationFrame(this.animationFrameId);
      this.animationFrameId = null;
    }

    // Start smooth animation to next point
    this.animateToNextPoint().catch(e => console.error('[Playback] start error:', e));
  }

  // Smooth animation using requestAnimationFrame — uses pre-computed matchedRouteCoords (no network calls)
  // Time-based: 1 second real time = 1 minute GPS time (adjusted by playbackSpeed)
  private async animateToNextPoint() {
    if (!this.isPlaying || this.playbackIndex >= this.playbackPositions.length - 1) {
      this.ngZone.run(() => {
        this.isPlaying = false;
        this.isAnimatingSegment = false;
        // Add end marker when playback finishes
        if (this.map && this.playbackPositions.length > 1) {
          const endIdx = this.playbackPositions.length - 1;
          const endLatLng = this.getSnappedLatLng(endIdx);
          L.circleMarker(endLatLng as L.LatLngExpression, {
            radius: 8, fillColor: '#ef4444', color: '#fff', weight: 3, fillOpacity: 1
          }).addTo(this.map).bindTooltip('Arrivée', { permanent: false });
        }
        this.cdr.detectChanges();
      });
      return;
    }

    try {
      // === IGNITION OFF: skip stopped period, place stop marker at this position ===
      const fromPos = this.playbackPositions[this.playbackIndex];
      if (fromPos.ignitionOn === false) {
        // Scan forward to find where ignition turns back ON
        let endIdx = this.playbackIndex;
        while (endIdx < this.playbackPositions.length - 1 && this.playbackPositions[endIdx + 1].ignitionOn === false) {
          endIdx++;
        }
        const startTime = new Date(fromPos.recordedAt);
        const endTime = new Date(this.playbackPositions[endIdx].recordedAt);
        // Get latest fuel in the stop range
        let fuel: number | null = null;
        for (let j = this.playbackIndex; j <= endIdx; j++) {
          if (this.playbackPositions[j].fuelRaw != null) fuel = this.playbackPositions[j].fuelRaw;
        }
        // Place stop marker at the RAW GPS position where ignition turned off
        this.placeStopMarker(fromPos.latitude, fromPos.longitude, startTime, endTime, fuel);

        const skipTo = Math.min(endIdx + 1, this.playbackPositions.length - 1);
        console.log(`[Playback] Ignition OFF: index ${this.playbackIndex} → ${skipTo}`);
        this.ngZone.run(() => {
          this.playbackIndex = skipTo;
          this.playbackProgress = (this.playbackIndex / (this.playbackPositions.length - 1)) * 100;
          this.updatePlaybackMarker();
          this.updatePlaybackAddress();
          this.cdr.detectChanges();
          // Brief pause then continue
          setTimeout(() => {
            this.animateToNextPoint().catch(e => console.error('[Playback] post-stop error:', e));
          }, Math.max(200, Math.min(800, 500 / this.playbackSpeed)));
        });
        return;
      }
      const toPos = this.playbackPositions[this.playbackIndex + 1];

      // Drop a small dot at the current position
      this.addSinglePointMarker(this.playbackIndex);

      // Calculate time-based duration: 1 real second = 1 GPS minute
      const fromTime = new Date(fromPos.recordedAt).getTime();
      const toTime = new Date(toPos.recordedAt).getTime();
      const gpsTimeDiffMs = Math.abs(toTime - fromTime);
      const gpsTimeDiffMinutes = gpsTimeDiffMs / 60000;
      // 1 minute GPS = 1 second real time, capped to avoid very long waits
      const baseDurationMs = Math.max(50, Math.min(5000, gpsTimeDiffMinutes * 1000));
      const durationMs = baseDurationMs / this.playbackSpeed;

      // === ANCHOR LOGIC: prevent GPS drift animation while parked/stopped ===
      if (fromPos.ignitionOn === false) {
        if (!this.ignitionOffAnchor) {
          this.ignitionOffAnchor = { latitude: fromPos.latitude, longitude: fromPos.longitude };
        }
        this.stoppedAnchor = null;
      } else if ((fromPos.speedKph || 0) < 3 && (toPos.speedKph || 0) < 3) {
        if (!this.stoppedAnchor) {
          this.stoppedAnchor = { latitude: fromPos.latitude, longitude: fromPos.longitude };
        }
        this.ignitionOffAnchor = null;
      } else {
        this.ignitionOffAnchor = null;
        this.stoppedAnchor = null;
      }

      // ===== GET ROAD SEGMENT from pre-computed matchedRouteCoords =====
      this.currentRouteCoords = this.getPrecomputedSegment(this.playbackIndex);
      this.routeAnimationIndex = 0;

      // Calculate segment distance
      let totalDistance = 0;
      for (let i = 1; i < this.currentRouteCoords.length; i++) {
        const prev = this.currentRouteCoords[i - 1];
        const curr = this.currentRouteCoords[i];
        if (prev && curr && !isNaN(prev.lat) && !isNaN(curr.lat)) {
          totalDistance += this.calculateDistance(prev.lat, prev.lng, curr.lat, curr.lng);
        }
      }

      // Vehicle is stationary: no movement OR anchored (ignition off / stopped with no speed)
      const isAnchored = !!(this.ignitionOffAnchor || this.stoppedAnchor);
      if (totalDistance < 1 || isAnchored) {
        this.updatePlaybackMarker();
        // Use shorter wait when anchored to skip through parked periods faster
        const waitMs = isAnchored 
          ? Math.max(30, Math.min(500, durationMs)) 
          : durationMs;
        this.ngZone.run(() => {
          setTimeout(() => {
            // Don't extend trace when anchored (vehicle isn't actually moving)
            if (!isAnchored) {
              this.appendProgressTrace(this.playbackIndex, this.playbackIndex + 1);
            }
            this.playbackIndex++;
            this.playbackProgress = (this.playbackIndex / (this.playbackPositions.length - 1)) * 100;
            this.updatePlaybackMarker();
            this.cdr.detectChanges();
            this.animateToNextPoint().catch(e => console.error('[Playback] stationary error:', e));
          }, waitMs);
        });
        return;
      }

      // ===== ANIMATE along pre-computed road segment =====
      this.segmentDuration = durationMs;
      this.animationStartTime = performance.now();
      this.isAnimatingSegment = true;
      this.animateFrame();

    } catch (err) {
      console.error('[Playback] animateToNextPoint error:', err);
      this.ngZone.run(() => {
        this.appendProgressTrace(this.playbackIndex, this.playbackIndex + 1);
        this.playbackIndex++;
        this.playbackProgress = (this.playbackIndex / (this.playbackPositions.length - 1)) * 100;
        this.updatePlaybackMarker();
        this.cdr.detectChanges();
        setTimeout(() => this.animateToNextPoint().catch(e => console.error('[Playback] recovery error:', e)), 30 / this.playbackSpeed);
      });
    }
  }

  // Get road segment between GPS point[index] and GPS point[index+1] from pre-computed data
  private getPrecomputedSegment(index: number): L.LatLng[] {
    if (this.matchedRouteCoords.length > 0 && this.segmentBoundaries.length > 0 && index < this.segmentBoundaries.length - 1) {
      const startIdx = this.segmentBoundaries[index];
      const endIdx = this.segmentBoundaries[index + 1];
      if (startIdx !== undefined && endIdx !== undefined && endIdx > startIdx) {
        const segment = this.matchedRouteCoords.slice(startIdx, endIdx + 1);
        if (segment.length >= 2) return segment;
      }
    }
    // Fallback: straight line between raw GPS points
    const from = this.playbackPositions[index];
    const to = this.playbackPositions[index + 1];
    if (!from || !to) return [];
    return [L.latLng(from.latitude, from.longitude), L.latLng(to.latitude, to.longitude)];
  }

  // Rebuild progressCoords up to a given GPS index (used when rewinding)
  private rebuildProgressTrace(upToGpsIndex: number) {
    this.progressCoords = [];
    if (!this.progressPolyline) return;
    if (upToGpsIndex <= 0) {
      this.progressPolyline.setLatLngs([]);
      return;
    }
    if (this.matchedRouteCoords.length > 0 && this.segmentBoundaries.length > 0) {
      const endRoad = this.segmentBoundaries[Math.min(upToGpsIndex, this.segmentBoundaries.length - 1)];
      if (endRoad !== undefined) {
        for (let i = 0; i <= endRoad; i++) {
          const coord = this.matchedRouteCoords[i];
          if (coord && !isNaN(coord.lat) && !isNaN(coord.lng)) {
            this.progressCoords.push(coord);
          }
        }
        this.progressPolyline.setLatLngs(this.progressCoords);
        return;
      }
    }
    // Fallback: use raw GPS positions
    for (let i = 0; i <= Math.min(upToGpsIndex, this.playbackPositions.length - 1); i++) {
      const pos = this.playbackPositions[i];
      if (pos) this.progressCoords.push(L.latLng(pos.latitude, pos.longitude));
    }
    this.progressPolyline.setLatLngs(this.progressCoords);
  }

  // Append road coords to the growing progress polyline
  private appendProgressTrace(fromGpsIndex: number, toGpsIndex: number) {
    if (!this.progressPolyline || !this.map) return;
    if (this.matchedRouteCoords.length > 0 && this.segmentBoundaries.length > 0) {
      const startRoad = this.segmentBoundaries[fromGpsIndex];
      const endRoad = this.segmentBoundaries[Math.min(toGpsIndex, this.segmentBoundaries.length - 1)];
      if (startRoad !== undefined && endRoad !== undefined && endRoad >= startRoad) {
        for (let i = startRoad; i <= endRoad; i++) {
          const coord = this.matchedRouteCoords[i];
          if (coord && !isNaN(coord.lat) && !isNaN(coord.lng)) {
            this.progressCoords.push(coord);
          }
        }
        this.progressPolyline.setLatLngs(this.progressCoords);
        return;
      }
    }
    // Fallback
    const from = this.playbackPositions[fromGpsIndex];
    const to = this.playbackPositions[Math.min(toGpsIndex, this.playbackPositions.length - 1)];
    if (from) this.progressCoords.push(L.latLng(from.latitude, from.longitude));
    if (to) this.progressCoords.push(L.latLng(to.latitude, to.longitude));
    this.progressPolyline.setLatLngs(this.progressCoords);
  }

  // Fetch Valhalla match for multiple GPS points (batch approach for better accuracy)
  // This matches the entire trace at once so Valhalla understands the full trajectory
  private async fetchValhallaMatchBatch(positions: any[]): Promise<L.LatLng[]> {
    if (positions.length < 2) {
      return positions.map(p => L.latLng(p.latitude, p.longitude));
    }
    
    // Valhalla can handle larger batches than OSRM, use 500 points per batch
    const BATCH_SIZE = 500;
    const allCoords: L.LatLng[] = [];
    
    for (let i = 0; i < positions.length; i += BATCH_SIZE - 1) {
      // Overlap by 1 point to ensure continuity between batches
      const batch = positions.slice(i, i + BATCH_SIZE);
      if (batch.length < 2) break;
      
      try {
        const points = batch.map(p => ({
          lat: p.latitude,
          lon: p.longitude,
          timestamp: p.recordedAt ? new Date(p.recordedAt).getTime() : null
        }));
        
        const response = await fetch('/api/routing/snap', {
          method: 'POST',
          headers: { 
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${localStorage.getItem('auth_token')}`
          },
          body: JSON.stringify({ points })
        });
        
        if (!response.ok) throw new Error(`Valhalla Match error: ${response.status}`);
        
        const data = await response.json();
        
        if (data.points && data.points.length > 0) {
          const coords = data.points.map(
            (p: any) => L.latLng(p.snappedLat, p.snappedLon)
          );
          // Skip first point if not first batch to avoid duplicates
          const startIdx = (i > 0 && allCoords.length > 0) ? 1 : 0;
          allCoords.push(...coords.slice(startIdx));
        }
      } catch (error) {
        console.error(`[Playback] Valhalla Match batch FAILED at index ${i}, using raw points:`, error);
        // Fallback: add raw GPS points for this batch
        const rawCoords = batch.map(p => L.latLng(p.latitude, p.longitude));
        const startIdx = (i > 0 && allCoords.length > 0) ? 1 : 0;
        allCoords.push(...rawCoords.slice(startIdx));
      }
    }
    
    if (allCoords.length === 0) {
      return positions.map(p => L.latLng(p.latitude, p.longitude));
    }
    
    console.log(`Valhalla Batch Match: ${positions.length} GPS points -> ${allCoords.length} matched coords`);
    return allCoords;
  }

  // Process playback route: let Valhalla calculate the ROAD ROUTE between GPS points
  // Only route when: ignition ON + distance > 20m (vehicle actually moving on road)
  // Otherwise: raw GPS coords (parked, close points, ignition off)
  // Consecutive moving points are grouped into trips → one Valhalla call per trip
  private async processPlaybackRoute(): Promise<void> {
    const totalPositions = this.playbackPositions.length;
    if (totalPositions < 2) {
      this.matchedRouteCoords = this.playbackPositions.map(p => L.latLng(p.latitude, p.longitude));
      this.segmentBoundaries = [];
      this.matchedRouteIndex = 0;
      return;
    }

    // Step 1: Classify each transition (i → i+1) as "route" or "raw"
    const transitions: boolean[] = []; // true = should route via Valhalla
    for (let i = 0; i < totalPositions - 1; i++) {
      const from = this.playbackPositions[i];
      const to = this.playbackPositions[i + 1];
      const ignitionOn = to.ignitionOn !== false;
      const dist = this.calculateDistance(from.latitude, from.longitude, to.latitude, to.longitude);
      transitions.push(ignitionOn && dist > 20);
    }

    // Step 2: Group consecutive "route" transitions into trips
    // A trip is a sequence of GPS points connected by "route" transitions
    const allRoadPath: L.LatLng[] = [];
    const allSegmentBoundaries: number[] = [];
    let routedTrips = 0;
    let rawTransitions = 0;
    let i = 0;

    while (i < totalPositions) {
      // Add current point's boundary
      allSegmentBoundaries.push(allRoadPath.length);

      if (i >= totalPositions - 1) {
        // Last point — just add it
        const pos = this.playbackPositions[i];
        allRoadPath.push(L.latLng(pos.latitude, pos.longitude));
        break;
      }

      if (!transitions[i]) {
        // Raw transition: add current point as-is, move to next
        const pos = this.playbackPositions[i];
        allRoadPath.push(L.latLng(pos.latitude, pos.longitude));
        rawTransitions++;
        i++;
        continue;
      }

      // Start of a routed trip: collect consecutive "route" transitions
      const tripStart = i;
      while (i < totalPositions - 1 && transitions[i]) {
        i++;
      }
      const tripEnd = i; // inclusive — last point of this trip

      // Trip waypoints: GPS points from tripStart to tripEnd
      const waypoints = [];
      for (let j = tripStart; j <= tripEnd; j++) {
        const p = this.playbackPositions[j];
        waypoints.push({
          lat: p.latitude,
          lon: p.longitude,
          timestamp: p.recordedAt ? new Date(p.recordedAt).getTime() : null,
          speed: p.speedKph || 0,
          heading: p.courseDeg || null,
          ignitionOn: p.ignitionOn
        });
      }

      try {
        const response = await fetch('/api/routing/process', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${localStorage.getItem('auth_token')}`
          },
          body: JSON.stringify({ points: waypoints, enableRoadSnapping: true })
        });

        if (!response.ok) throw new Error(`API error: ${response.status}`);
        const data = await response.json();

        if (data.roadPath && data.roadPath.length >= 2 && data.segmentBoundaries) {
          const tripRoadCoords: L.LatLng[] = data.roadPath.map((p: any) => L.latLng(p.lat, p.lon));
          const tripBoundaries: number[] = data.segmentBoundaries;
          const roadOffset = allRoadPath.length;

          allRoadPath.push(...tripRoadCoords);

          // First waypoint boundary already added above (allSegmentBoundaries.push)
          // Update it to point to correct road offset
          allSegmentBoundaries[allSegmentBoundaries.length - 1] = roadOffset + (tripBoundaries[0] || 0);

          // Add boundaries for remaining waypoints in this trip
          for (let j = 1; j <= tripEnd - tripStart; j++) {
            const localBound = j < tripBoundaries.length ? tripBoundaries[j] : tripRoadCoords.length - 1;
            allSegmentBoundaries.push(roadOffset + localBound);
          }

          routedTrips++;
        } else {
          // Valhalla failed — add raw GPS for this trip
          this.addTripRawFallback(tripStart, tripEnd, allRoadPath, allSegmentBoundaries);
        }
      } catch (error) {
        console.error(`[Playback] Trip routing FAILED (points ${tripStart}-${tripEnd}):`, error);
        this.addTripRawFallback(tripStart, tripEnd, allRoadPath, allSegmentBoundaries);
      }

      // Skip past tripEnd — it was already included in the trip's boundaries
      i++;
      continue;
    }

    // Ensure exactly one boundary per GPS point
    while (allSegmentBoundaries.length < totalPositions) {
      allSegmentBoundaries.push(Math.max(0, allRoadPath.length - 1));
    }
    if (allSegmentBoundaries.length > totalPositions) {
      allSegmentBoundaries.length = totalPositions;
    }

    if (allRoadPath.length >= 2) {
      this.matchedRouteCoords = allRoadPath;
      this.segmentBoundaries = allSegmentBoundaries;
      this.matchedRouteIndex = 0;
      console.log(`[Playback] Route built: ${totalPositions} GPS points -> ${allRoadPath.length} road coords (${routedTrips} routed trips, ${rawTransitions} raw transitions)`);
    } else {
      console.warn('[Playback] Route processing failed, using raw GPS');
      this.matchedRouteCoords = this.playbackPositions.map(p => L.latLng(p.latitude, p.longitude));
      this.segmentBoundaries = [];
      this.matchedRouteIndex = 0;
    }
  }

  // Fallback: add raw GPS points for a trip that failed Valhalla routing
  private addTripRawFallback(tripStart: number, tripEnd: number, allRoadPath: L.LatLng[], allSegmentBoundaries: number[]) {
    // First point's boundary was already added by the caller, update it
    const pos0 = this.playbackPositions[tripStart];
    allRoadPath.push(L.latLng(pos0.latitude, pos0.longitude));
    allSegmentBoundaries[allSegmentBoundaries.length - 1] = allRoadPath.length - 1;

    for (let j = tripStart + 1; j <= tripEnd; j++) {
      const pos = this.playbackPositions[j];
      allRoadPath.push(L.latLng(pos.latitude, pos.longitude));
      allSegmentBoundaries.push(allRoadPath.length - 1);
    }
  }

  // Fetch route between two GPS points for animation (uses road path from segmentBoundaries)
  private async fetchValhallaRoute(fromPos: any, toPos: any): Promise<L.LatLng[]> {
    if (this.matchedRouteCoords.length > 0 && this.segmentBoundaries.length > 0) {
      const gpsIndex = this.playbackIndex;
      if (gpsIndex < this.segmentBoundaries.length - 1) {
        const startIdx = this.segmentBoundaries[gpsIndex];
        const endIdx = this.segmentBoundaries[gpsIndex + 1];
        if (endIdx > startIdx) {
          const segment = this.matchedRouteCoords.slice(startIdx, endIdx + 1);
          if (segment.length >= 2) return segment;
        }
      }
    }
    // Fallback: straight line between raw GPS points
    const from = this.playbackPositions[this.playbackIndex];
    const to = this.playbackPositions[this.playbackIndex + 1];
    if (!from || !to) return [];
    return [L.latLng(from.latitude, from.longitude), L.latLng(to.latitude, to.longitude)];
  }

  // Helper: get snapped position as L.LatLng object
  private getSnappedLatLngAsLatLng(index: number): L.LatLng {
    if (this.matchedRouteCoords.length > 0 && this.segmentBoundaries.length > 0 && index < this.segmentBoundaries.length) {
      const roadIdx = this.segmentBoundaries[index];
      if (roadIdx !== undefined && roadIdx < this.matchedRouteCoords.length) {
        const snapped = this.matchedRouteCoords[roadIdx];
        if (snapped && !isNaN(snapped.lat) && !isNaN(snapped.lng)) return snapped;
      }
    }
    const pos = this.playbackPositions[index];
    if (!pos) return L.latLng(0, 0);
    return L.latLng(pos.latitude, pos.longitude);
  }

  // Animation frame loop for smooth interpolation along pre-computed road path
  private animateFrame() {
    if (!this.isPlaying) {
      this.isAnimatingSegment = false;
      return;
    }
    
    if (!this.currentRouteCoords || this.currentRouteCoords.length === 0) {
      this.isAnimatingSegment = false;
      this.ngZone.run(() => {
        this.playbackIndex++;
        this.playbackProgress = (this.playbackIndex / (this.playbackPositions.length - 1)) * 100;
        this.cdr.detectChanges();
        this.animateToNextPoint().catch(e => console.error('[Playback] empty route skip:', e));
      });
      return;
    }

    const elapsed = performance.now() - this.animationStartTime;
    const progress = Math.min(1, elapsed / this.segmentDuration);
    const easedProgress = this.easeInOutCubic(progress);
    const position = this.getPositionAlongRoute(easedProgress);
    
    if (!position) {
      this.animationFrameId = requestAnimationFrame(() => this.animateFrame());
      return;
    }
    
    const { lat, lng, heading } = position;

    // Update vehicle marker position (always smooth)
    if (this.playbackMarker) {
      this.playbackMarker.setLatLng([lat, lng]);
      // Only recreate icon every 6th frame to avoid DOM thrashing
      this._iconFrameCount++;
      if (this._iconFrameCount >= 6) {
        this._iconFrameCount = 0;
        const currentPos = this.playbackPositions[this.playbackIndex];
        const spd = currentPos?.speedKph || 0;
        const col = this.getStatusColor(currentPos);
        this.playbackMarker.setIcon(this.createPlaybackVehicleIcon(col, heading, spd));
      }
    }

    // Smooth camera follow (throttled with icon updates)
    if (this.map && this.smoothFollowCamera && this._iconFrameCount === 0) {
      this.map.panTo([lat, lng], { animate: true, duration: 0.3, easeLinearity: 0.25 });
    }

    // Update progress polyline in real-time during animation
    if (this.progressPolyline && this.currentRouteCoords && this.currentRouteCoords.length > 0) {
      const routeIdx = Math.min(Math.floor(easedProgress * (this.currentRouteCoords.length - 1)), this.currentRouteCoords.length - 1);
      const currentTraceCoords = [...this.progressCoords];
      for (let i = 0; i <= routeIdx; i++) {
        const c = this.currentRouteCoords[i];
        if (c && !isNaN(c.lat) && !isNaN(c.lng)) {
          currentTraceCoords.push(c);
        }
      }
      currentTraceCoords.push(L.latLng(lat, lng));
      this.progressPolyline.setLatLngs(currentTraceCoords);
    }

    if (progress < 1) {
      this.animationFrameId = requestAnimationFrame(() => this.animateFrame());
    } else {
      // Segment complete — append exact road segment and advance
      this.appendProgressTrace(this.playbackIndex, this.playbackIndex + 1);
      this.ngZone.run(() => {
        this.playbackIndex++;
        this.playbackProgress = (this.playbackIndex / (this.playbackPositions.length - 1)) * 100;
        this.updatePlaybackMarker();
        this.cdr.detectChanges();
        this.isAnimatingSegment = false;
        this.animateToNextPoint().catch(e => console.error('[Playback] next segment error:', e));
      });
    }
  }

  // Get interpolated position along the Valhalla route
  private getPositionAlongRoute(progress: number): { lat: number; lng: number; heading: number } | null {
    // Validate route coords exist
    if (!this.currentRouteCoords || this.currentRouteCoords.length === 0) {
      return null;
    }
    
    // If only one point, return it
    if (this.currentRouteCoords.length === 1) {
      const pos = this.currentRouteCoords[0];
      if (!pos || isNaN(pos.lat) || isNaN(pos.lng)) return null;
      return { lat: pos.lat, lng: pos.lng, heading: 0 };
    }

    // Calculate total route length
    let totalLength = 0;
    const segmentLengths: number[] = [];
    
    for (let i = 1; i < this.currentRouteCoords.length; i++) {
      const prev = this.currentRouteCoords[i - 1];
      const curr = this.currentRouteCoords[i];
      if (!prev || !curr || isNaN(prev.lat) || isNaN(curr.lat)) continue;
      const len = this.calculateDistance(prev.lat, prev.lng, curr.lat, curr.lng);
      segmentLengths.push(len);
      totalLength += len;
    }

    // Handle zero-length route
    if (totalLength === 0 || segmentLengths.length === 0) {
      const pos = this.currentRouteCoords[0];
      if (!pos || isNaN(pos.lat) || isNaN(pos.lng)) return null;
      return { lat: pos.lat, lng: pos.lng, heading: 0 };
    }

    // Find position at progress along route
    const targetDistance = totalLength * progress;
    let accumulatedDistance = 0;
    
    for (let i = 0; i < segmentLengths.length; i++) {
      if (accumulatedDistance + segmentLengths[i] >= targetDistance) {
        // Interpolate within this segment
        const segmentLen = segmentLengths[i];
        const segmentProgress = segmentLen > 0 ? (targetDistance - accumulatedDistance) / segmentLen : 0;
        const from = this.currentRouteCoords[i];
        const to = this.currentRouteCoords[i + 1];
        
        if (!from || !to) continue;
        
        const lat = from.lat + (to.lat - from.lat) * segmentProgress;
        const lng = from.lng + (to.lng - from.lng) * segmentProgress;
        
        if (isNaN(lat) || isNaN(lng)) continue;
        
        // Calculate heading
        const heading = this.calculateHeading(from.lat, from.lng, to.lat, to.lng);
        
        return { lat, lng, heading: isNaN(heading) ? 0 : heading };
      }
      accumulatedDistance += segmentLengths[i];
    }

    // Return last position if we somehow exceeded
    const lastPos = this.currentRouteCoords[this.currentRouteCoords.length - 1];
    const prevPos = this.currentRouteCoords[this.currentRouteCoords.length - 2];
    if (!lastPos || isNaN(lastPos.lat) || isNaN(lastPos.lng)) return null;
    
    const heading = prevPos ? this.calculateHeading(prevPos.lat, prevPos.lng, lastPos.lat, lastPos.lng) : 0;
    return { lat: lastPos.lat, lng: lastPos.lng, heading: isNaN(heading) ? 0 : heading };
  }

  // Calculate heading/bearing between two points in degrees
  private calculateHeading(lat1: number, lon1: number, lat2: number, lon2: number): number {
    const dLon = (lon2 - lon1) * Math.PI / 180;
    const lat1Rad = lat1 * Math.PI / 180;
    const lat2Rad = lat2 * Math.PI / 180;
    
    const y = Math.sin(dLon) * Math.cos(lat2Rad);
    const x = Math.cos(lat1Rad) * Math.sin(lat2Rad) - 
              Math.sin(lat1Rad) * Math.cos(lat2Rad) * Math.cos(dLon);
    
    let heading = Math.atan2(y, x) * 180 / Math.PI;
    heading = (heading + 360) % 360; // Normalize to 0-360
    
    return heading;
  }

  // Easing function for natural movement
  private easeInOutCubic(t: number): number {
    return t < 0.5 
      ? 4 * t * t * t 
      : 1 - Math.pow(-2 * t + 2, 3) / 2;
  }

  // Calculate distance between two coordinates in meters
  private calculateDistance(lat1: number, lon1: number, lat2: number, lon2: number): number {
    const R = 6371000; // Earth radius in meters
    const dLat = (lat2 - lat1) * Math.PI / 180;
    const dLon = (lon2 - lon1) * Math.PI / 180;
    const a = Math.sin(dLat / 2) * Math.sin(dLat / 2) +
              Math.cos(lat1 * Math.PI / 180) * Math.cos(lat2 * Math.PI / 180) *
              Math.sin(dLon / 2) * Math.sin(dLon / 2);
    const c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
    return R * c;
  }

  // Draw a single segment of the trace with color based on vehicle status
  drawProgressiveSegment(fromIndex: number, toIndex: number) {
    if (!this.map || fromIndex < 0 || toIndex >= this.playbackPositions.length) return;

    const fromPos = this.playbackPositions[fromIndex];
    const toPos = this.playbackPositions[toIndex];
    
    // Skip drawing when vehicle is stationary (speed < 3 km/h or ignition off)
    // This prevents "point clouds" from GPS drift when parked/stopped
    const speed = toPos.speedKph || 0;
    const ignitionOn = toPos.ignitionOn !== false;
    if (speed < 3 || !ignitionOn) {
      this.traceDrawnUpToIndex = toIndex;
      return;
    }
    
    // Determine color based on vehicle status at the destination point
    const color = this.getStatusColor(toPos);
    
    // Always use road snapping for accurate route display
    this.drawRoutedSegment(fromPos, toPos, color);
    
    this.traceDrawnUpToIndex = toIndex;
  }

  // Draw a straight line segment between two points
  private drawStraightSegment(fromPos: any, toPos: any, color: string) {
    if (!this.map) return;
    
    const segment = L.polyline(
      [
        [fromPos.latitude, fromPos.longitude],
        [toPos.latitude, toPos.longitude]
      ],
      {
        color: color,
        weight: 4,
        opacity: 0.9,
        dashArray: '10, 8',
        lineCap: 'round',
        lineJoin: 'round'
      }
    ).addTo(this.map);

    this.progressivePolylines.push(segment);
  }

  // Calculate bearing between two points in degrees (0-360)
  private calculateBearing(lat1: number, lon1: number, lat2: number, lon2: number): number {
    const dLon = (lon2 - lon1) * Math.PI / 180;
    const lat1Rad = lat1 * Math.PI / 180;
    const lat2Rad = lat2 * Math.PI / 180;
    
    const y = Math.sin(dLon) * Math.cos(lat2Rad);
    const x = Math.cos(lat1Rad) * Math.sin(lat2Rad) - 
              Math.sin(lat1Rad) * Math.cos(lat2Rad) * Math.cos(dLon);
    
    return (Math.atan2(y, x) * 180 / Math.PI + 360) % 360;
  }

  // Get bearing from position - returns null if vehicle is stationary or distance too short
  private getBearing(fromPos: any, toPos: any): number | null {
    // Check if vehicle is moving (speed > 2 km/h to filter GPS noise)
    const speed = fromPos.speedKph || fromPos.speed || 0;
    if (speed < 2) {
      return null; // Don't use bearing for stationary vehicles
    }
    
    // Check distance - if < 20m, bearing is unreliable due to GPS noise
    const distance = this.calculateDistance(
      fromPos.latitude, fromPos.longitude,
      toPos.latitude, toPos.longitude
    );
    if (distance < 20) {
      return null; // Distance too short for reliable bearing
    }
    
    // Use courseDeg from database if available and valid (> 0)
    if (fromPos.courseDeg && fromPos.courseDeg > 0) {
      return Math.round(fromPos.courseDeg);
    }
    
    // Fallback: calculate bearing from coordinates
    return Math.round(this.calculateBearing(
      fromPos.latitude, fromPos.longitude,
      toPos.latitude, toPos.longitude
    ));
  }

  // Draw a road-snapped segment using segmentBoundaries for exact GPS-to-road mapping
  private drawRoutedSegment(fromPos: any, toPos: any, color: string) {
    if (!this.map) return;
    
    if (this.matchedRouteCoords.length > 0 && this.segmentBoundaries.length > 0) {
      const fromIndex = this.playbackPositions.indexOf(fromPos);
      const gpsIndex = fromIndex >= 0 ? fromIndex : this.playbackIndex - 1;
      
      if (gpsIndex >= 0 && gpsIndex < this.segmentBoundaries.length - 1) {
        const startIdx = this.segmentBoundaries[gpsIndex];
        const endIdx = this.segmentBoundaries[gpsIndex + 1];
        
        const segmentCoords = this.matchedRouteCoords.slice(startIdx, endIdx + 1);
        
        if (segmentCoords.length >= 2) {
          const segment = L.polyline(segmentCoords, {
            color: color,
            weight: 4,
            opacity: 0.9,
            dashArray: '10, 8',
            lineCap: 'round',
            lineJoin: 'round'
          }).addTo(this.map!);
          
          this.progressivePolylines.push(segment);
          return;
        }
      }
    }
    
    // Fallback: draw straight line using snapped coordinates (not raw GPS)
    if (!this.map) return;
    const fromIdx = this.playbackPositions.indexOf(fromPos);
    const toIdx = this.playbackPositions.indexOf(toPos);
    const fromSnapped = this.getSnappedLatLngAsLatLng(fromIdx >= 0 ? fromIdx : this.playbackIndex - 1);
    const toSnapped = this.getSnappedLatLngAsLatLng(toIdx >= 0 ? toIdx : this.playbackIndex);
    const segment = L.polyline([fromSnapped, toSnapped], {
      color: color,
      weight: 4,
      opacity: 0.9,
      dashArray: '10, 8',
      lineCap: 'round',
      lineJoin: 'round'
    }).addTo(this.map!);
    this.progressivePolylines.push(segment);
  }
  
  // Find the closest point index in matchedRouteCoords starting from a given index
  private findClosestMatchedIndex(targetLatLng: L.LatLng, startFrom: number): number {
    let closestIdx = startFrom;
    let closestDist = Infinity;
    
    // Search within a reasonable range (not the entire array for performance)
    const searchEnd = Math.min(startFrom + 200, this.matchedRouteCoords.length);
    
    for (let i = startFrom; i < searchEnd; i++) {
      const dist = targetLatLng.distanceTo(this.matchedRouteCoords[i]);
      if (dist < closestDist) {
        closestDist = dist;
        closestIdx = i;
      }
      // Stop if we're getting further away (we passed the closest point)
      if (dist > closestDist * 2 && closestDist < 100) break;
    }
    
    return closestIdx;
  }

  // Get color based on vehicle status: green=moving, orange=stopped in traffic, red=parked
  getStatusColor(position: any): string {
    const speed = position.speedKph || 0;
    const ignitionOn = position.ignitionOn;
    
    if (!ignitionOn) {
      // Engine off = parked (red)
      return '#ef4444';
    } else if (speed > 5) {
      // Engine on + moving = moving (green)
      return '#22c55e';
    } else {
      // Engine on + not moving = stopped in traffic (orange)
      return '#f97316';
    }
  }

  // Get status class for CSS styling
  getPlaybackStatusClass(): string {
    if (this.playbackPositions.length === 0 || this.playbackIndex >= this.playbackPositions.length) {
      return '';
    }
    const pos = this.playbackPositions[this.playbackIndex];
    const speed = pos?.speedKph || 0;
    const ignitionOn = pos?.ignitionOn;
    
    if (!ignitionOn) return 'status-parked';
    if (speed > 5) return 'status-moving';
    return 'status-traffic';
  }

  // Get status label for display
  getPlaybackStatusLabel(): string {
    if (this.playbackPositions.length === 0 || this.playbackIndex >= this.playbackPositions.length) {
      return '';
    }
    const pos = this.playbackPositions[this.playbackIndex];
    const speed = pos?.speedKph || 0;
    const ignitionOn = pos?.ignitionOn;
    
    if (!ignitionOn) return '🔴 Stationné';
    if (speed > 5) return '🟢 En mouvement';
    return '🟠 Arrêt trafic';
  }

  stopPlaybackAnimation() {
    if (this.playbackInterval) {
      clearInterval(this.playbackInterval);
      this.playbackInterval = null;
    }
    if (this.animationFrameId) {
      cancelAnimationFrame(this.animationFrameId);
      this.animationFrameId = null;
    }
    this.isAnimatingSegment = false;
  }

  resetPlayback() {
    this.stopPlaybackAnimation();
    this.isPlaying = false;
    this.playbackIndex = 0;
    this.playbackProgress = 0;
    
    // Reset anchors
    this.ignitionOffAnchor = null;
    this.stoppedAnchor = null;
    this.matchedRouteCoords = [];
    this.matchedRouteIndex = 0;
    
    // Clear all progressive polylines and point markers
    this.progressivePolylines.forEach(polyline => polyline.remove());
    this.progressivePolylines = [];
    this.pointMarkers.forEach(marker => marker.remove());
    this.pointMarkers = [];
    this.traceDrawnUpToIndex = 0;
    
    this.updatePlaybackMarker();

    // Clear stationary markers from map
    this.stationaryMarkers.forEach(m => m.remove());
    this.stationaryMarkers = [];
    this.stopMarkerCount = 0;

    // Center on start position (snapped) - maintain current zoom level
    if (this.map && this.playbackPositions.length > 0) {
      this.map.setView(this.getSnappedLatLng(0), this.playbackZoomLevel);
    }
    
    this.cdr.detectChanges();
  }

  skipToEnd() {
    this.stopPlaybackAnimation();
    this.isPlaying = false;
    
    this.playbackIndex = this.playbackPositions.length - 1;
    this.playbackProgress = 100;
    
    // Draw the full route using road-snapped coords (single polyline, not per-segment)
    this.rebuildProgressTrace(this.playbackIndex);
    this.traceDrawnUpToIndex = this.playbackIndex;
    this.updatePlaybackMarker();

    // Place stop markers for all ignition OFF periods when skipping to end
    this.placeAllStopMarkers();

    // Add end marker
    if (this.map && this.playbackPositions.length > 1) {
      const endIdx = this.playbackPositions.length - 1;
      const endLatLng = this.getSnappedLatLng(endIdx);
      L.circleMarker(endLatLng as L.LatLngExpression, {
        radius: 8, fillColor: '#ef4444', color: '#fff', weight: 3, fillOpacity: 1
      }).addTo(this.map).bindTooltip('Arrivée', { permanent: false });
    }

    // Center on end position (snapped) - maintain current zoom level
    if (this.map && this.playbackPositions.length > 0) {
      this.map.setView(this.getSnappedLatLng(this.playbackPositions.length - 1), this.playbackZoomLevel);
    }
    
    this.cdr.detectChanges();
  }

  onPlaybackSpeedChange(speed: number) {
    this.playbackSpeed = speed;
    if (this.isPlaying) {
      this.stopPlaybackAnimation();
      this.startPlaybackAnimation();
    }
  }

  onPlaybackProgressChange(progress: number) {
    this.stopPlaybackAnimation();
    this.isPlaying = false;
    this.ignitionOffAnchor = null;
    this.stoppedAnchor = null;
    this.playbackProgress = progress;
    const newIndex = Math.floor((progress / 100) * (this.playbackPositions.length - 1));
    // Rebuild trace when rewinding to avoid stray connecting lines
    if (newIndex < this.playbackIndex) {
      this.playbackIndex = newIndex;
      this.rebuildProgressTrace(this.playbackIndex);
    } else {
      this.playbackIndex = newIndex;
    }
    this.updatePlaybackMarker();
    
    // Center map on current snapped position
    if (this.map) {
      this.map.panTo(this.getSnappedLatLng(this.playbackIndex));
    }
    
    this.cdr.detectChanges();
  }

  previousPoint() {
    if (this.playbackIndex > 0) {
      this.stopPlaybackAnimation();
      this.isPlaying = false;
      this.ignitionOffAnchor = null;
      this.stoppedAnchor = null;
      this.playbackIndex--;
      this.playbackProgress = (this.playbackIndex / (this.playbackPositions.length - 1)) * 100;
      // Rebuild trace to trim back the polyline
      this.rebuildProgressTrace(this.playbackIndex);
      this.updatePlaybackMarker();
      
      if (this.map) {
        this.map.panTo(this.getSnappedLatLng(this.playbackIndex));
      }
      
      this.cdr.detectChanges();
    }
  }

  nextPoint() {
    if (this.playbackIndex < this.playbackPositions.length - 1) {
      this.stopPlaybackAnimation();
      this.isPlaying = false;
      this.ignitionOffAnchor = null;
      this.stoppedAnchor = null;
      this.playbackIndex++;
      this.playbackProgress = (this.playbackIndex / (this.playbackPositions.length - 1)) * 100;
      this.updatePlaybackMarker();
      
      if (this.map) {
        this.map.panTo(this.getSnappedLatLng(this.playbackIndex));
      }
      
      this.cdr.detectChanges();
    }
  }

  // ═══════ PLAYBACK OVERLAY MAP ═══════

  initPlaybackOverlayMap() {
    const mapEl = document.getElementById('playback-map');
    if (!mapEl) { console.error('[Playback] playback-map element not found'); return; }

    const startPos = this.playbackPositions[0];
    const center: L.LatLngExpression = startPos ? [startPos.latitude, startPos.longitude] : [36.8, 10.18];

    this.playbackOverlayMap = L.map(mapEl, {
      center,
      zoom: 15,
      zoomControl: false,
      attributionControl: false
    });

    // Apply current map style
    this.applyTileLayer(this.playbackOverlayMap);

    // Swap this.map to overlay map so all existing playback methods work
    this.map = this.playbackOverlayMap;
  }

  private applyTileLayer(targetMap: L.Map) {
    // Remove existing tile layers
    targetMap.eachLayer((layer: L.Layer) => {
      if (layer instanceof L.TileLayer) { targetMap.removeLayer(layer); }
    });

    let tileUrl = 'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png';
    if (this.mapStyle === 'satellite') {
      tileUrl = 'https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}';
    } else if (this.mapStyle === 'terrain') {
      tileUrl = 'https://{s}.tile.opentopomap.org/{z}/{x}/{y}.png';
    }
    L.tileLayer(tileUrl, { maxZoom: 19 }).addTo(targetMap);
  }

  // ═══════ PLAYBACK LIVE INFO HELPERS ═══════

  updatePlaybackAddress() {
    if (this.playbackPositions.length === 0) return;
    const pos = this.playbackPositions[this.playbackIndex];
    if (!pos) return;

    const cacheKey = `${pos.latitude.toFixed(4)},${pos.longitude.toFixed(4)}`;
    if (this.playbackAddressCache.has(cacheKey)) {
      this.playbackCurrentAddress = this.playbackAddressCache.get(cacheKey)!;
      return;
    }

    this.geocodingService.reverseGeocode(pos.latitude, pos.longitude).subscribe({
      next: (address) => {
        this.playbackAddressCache.set(cacheKey, address);
        // Only update if still on same position
        const currentPos = this.playbackPositions[this.playbackIndex];
        if (currentPos && `${currentPos.latitude.toFixed(4)},${currentPos.longitude.toFixed(4)}` === cacheKey) {
          this.playbackCurrentAddress = address;
          this.cdr.detectChanges();
        }
      }
    });
  }

  getPlaybackCurrentTime(): string {
    const pos = this.playbackPositions[this.playbackIndex];
    if (!pos) return '';
    return new Date(pos.recordedAt).toLocaleString('fr-FR', {
      day: '2-digit', month: '2-digit', year: 'numeric',
      hour: '2-digit', minute: '2-digit', second: '2-digit'
    });
  }

  getPlaybackCurrentSpeed(): string {
    const pos = this.playbackPositions[this.playbackIndex];
    return pos ? Math.round(pos.speedKph || 0).toString() : '0';
  }

  getPlaybackSpeedColor(): string {
    const pos = this.playbackPositions[this.playbackIndex];
    const speed = pos?.speedKph || 0;
    if (speed > 80) return '#ef4444';
    if (speed > 50) return '#f59e0b';
    if (speed > 5) return '#22c55e';
    return '#64748b';
  }

  getPlaybackIgnitionLabel(): string {
    const pos = this.playbackPositions[this.playbackIndex];
    return pos?.ignitionOn ? 'ON' : 'OFF';
  }

  getPlaybackIgnitionColor(): string {
    const pos = this.playbackPositions[this.playbackIndex];
    return pos?.ignitionOn ? '#22c55e' : '#ef4444';
  }

  getPlaybackFuel(): string {
    const pos = this.playbackPositions[this.playbackIndex];
    return pos?.fuelRaw != null ? `${pos.fuelRaw}%` : 'N/A';
  }

  formatPlaybackDate(dateStr: string): string {
    if (!dateStr) return '';
    const d = new Date(dateStr);
    if (isNaN(d.getTime())) return dateStr;
    return d.toLocaleString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
  }

  toLocalDateTimeString(date: Date): string {
    const y = date.getFullYear();
    const m = String(date.getMonth() + 1).padStart(2, '0');
    const d = String(date.getDate()).padStart(2, '0');
    const h = String(date.getHours()).padStart(2, '0');
    const min = String(date.getMinutes()).padStart(2, '0');
    return `${y}-${m}-${d}T${h}:${min}`;
  }

  getPlaybackTotalDuration(): string {
    if (this.playbackPositions.length < 2) return '0min';
    const first = new Date(this.playbackPositions[0].recordedAt).getTime();
    const last = new Date(this.playbackPositions[this.playbackPositions.length - 1].recordedAt).getTime();
    const diffMin = Math.round((last - first) / 60000);
    if (diffMin < 60) return `${diffMin}min`;
    const h = Math.floor(diffMin / 60);
    const m = diffMin % 60;
    return `${h}h${m > 0 ? m + 'min' : ''}`;
  }

  getPlaybackTotalKm(): string {
    if (this.playbackPositions.length < 2) return '0';
    let totalKm = 0;
    for (let i = 1; i < this.playbackPositions.length; i++) {
      const prev = this.playbackPositions[i - 1];
      const curr = this.playbackPositions[i];
      if (prev.ignitionOn === false) continue;
      totalKm += this.calculateDistance(prev.latitude, prev.longitude, curr.latitude, curr.longitude);
    }
    return totalKm < 10 ? totalKm.toFixed(1) : Math.round(totalKm).toString();
  }

  // End playback - called when user clicks "Retour au suivi" button
  endPlayback() {
    console.log('Ending playback - restoring live view');
    
    // Save current zoom level before clearing
    const currentZoom = this.map?.getZoom() || this.playbackZoomLevel;
    
    this.clearPlayback();
    
    // Center map on the live vehicle position - maintain zoom level
    if (this.map && this.selectedVehicle?.currentLocation) {
      this.map.setView([this.selectedVehicle.currentLocation.lat, this.selectedVehicle.currentLocation.lng], currentZoom);
    }
  }

  clearPlayback() {
    this.stopPlaybackAnimation();
    this.isPlaying = false;
    this.playbackRawCount = 0;
    this.playbackVehicleId = null;
    this.filteredBirdFlights = 0;
    
    // Reset smooth animation state
    this.animationFromPos = null;
    this.animationToPos = null;
    this.isAnimatingSegment = false;
    this.currentRouteCoords = [];
    this.routeAnimationIndex = 0;
    
    // Reset anchors
    this.ignitionOffAnchor = null;
    this.stoppedAnchor = null;
    this.matchedRouteCoords = [];
    this.matchedRouteIndex = 0;

    // Reset stop marker counter
    this.stopMarkerCount = 0;

    // Clear route display on overlay map before destroying it
    this.clearRouteDisplay();
    
    if (this.playbackMarker) {
      this.playbackMarker.remove();
      this.playbackMarker = null;
    }

    // Destroy overlay map and restore monitoring map
    if (this.playbackOverlayMap) {
      this.playbackOverlayMap.remove();
      this.playbackOverlayMap = null;
    }
    if (this.monitoringMap) {
      this.map = this.monitoringMap;
      this.monitoringMap = null;
    }

    // Now hide overlay and reset state
    this.isPlaybackLoaded = false;
    this.playbackPositions = [];
    this.playbackIndex = 0;
    this.playbackProgress = 0;
    this.playbackCurrentAddress = '';
    
    // Force UI update after clearing
    this.cdr.detectChanges();
  }

  // Remote vehicle commands (stop/resume engine)
  sendRemoteCommand(vehicle: Vehicle, command: 'stop' | 'resume') {
    if (this.remoteCommandLoading) return;
    
    const actionLabel = command === 'stop' ? "ARRÊTER" : "REMETTRE EN MARCHE";
    const password = prompt(`Pour ${actionLabel} le moteur du véhicule "${vehicle.name || vehicle.plate}", veuillez entrer votre mot de passe :`);
    if (!password) return;

    this.remoteCommandLoading = true;
    this.remoteCommandMessage = '';
    this.remoteCommandSuccess = false;

    this.apiService.sendVehicleCommand(parseInt(vehicle.id), command).subscribe({
      next: (response) => {
        this.remoteCommandLoading = false;
        this.remoteCommandSuccess = response.success;
        this.remoteCommandMessage = response.message;
        this.cdr.detectChanges();
        setTimeout(() => { this.remoteCommandMessage = ''; this.cdr.detectChanges(); }, 8000);
      },
      error: (err) => {
        this.remoteCommandLoading = false;
        this.remoteCommandSuccess = false;
        this.remoteCommandMessage = err.error?.message || 'Erreur lors de l\'envoi de la commande';
        this.cdr.detectChanges();
        setTimeout(() => { this.remoteCommandMessage = ''; this.cdr.detectChanges(); }, 8000);
      }
    });
  }

  // Message
  sendMessageToDriver() {
    if (this.driverMessage.trim()) {
      alert(`Message envoyé: ${this.driverMessage}`);
      this.driverMessage = '';
    }
  }

  // Panel drag methods for vehicle-info-panel
  startDragPanel(event: MouseEvent) {
    // Only start drag from header
    const target = event.target as HTMLElement;
    if (!target.closest('.drag-handle') && !target.closest('.info-panel-header')) return;
    
    const panel = (event.target as HTMLElement).closest('.vehicle-info-panel') as HTMLElement;
    const container = document.querySelector('.map-area') as HTMLElement;
    if (!panel || !container) return;

    this.isDragging = true;
    const panelRect = panel.getBoundingClientRect();
    const containerRect = container.getBoundingClientRect();

    // Offset from mouse click to panel top-left
    this.dragOffset = {
      x: event.clientX - panelRect.left,
      y: event.clientY - panelRect.top
    };

    // Set initial position
    this.popupPosition = { 
      x: panelRect.left - containerRect.left, 
      y: panelRect.top - containerRect.top 
    };
    
    event.preventDefault();
  }

  onDragPanel(event: MouseEvent) {
    if (!this.isDragging) return;
    
    const container = document.querySelector('.map-area') as HTMLElement;
    if (!container) return;

    const containerRect = container.getBoundingClientRect();
    
    // Calculate new position
    let newX = event.clientX - containerRect.left - this.dragOffset.x;
    let newY = event.clientY - containerRect.top - this.dragOffset.y;
    
    // Constrain within container bounds
    const panelWidth = 380;
    const panelHeight = 300;
    newX = Math.max(0, Math.min(newX, containerRect.width - panelWidth));
    newY = Math.max(0, Math.min(newY, containerRect.height - panelHeight));
    
    this.popupPosition = { x: newX, y: newY };
  }

  stopDragPanel() {
    this.isDragging = false;
  }

  getVehicleType(vehicle: any): string {
    const type = (vehicle.type || '').toLowerCase();
    const brand = (vehicle.brand || '').toLowerCase();
    const model = (vehicle.model || '').toLowerCase();

    if (type.includes('truck') || type.includes('camion') || brand.includes('man') ||
        brand.includes('scania') || brand.includes('volvo') || model.includes('actros') ||
        model.includes('tge')) {
      return 'truck';
    }
    if (type.includes('van') || type.includes('fourgon') || type.includes('utilitaire') ||
        model.includes('sprinter') || model.includes('transit') || model.includes('vito')) {
      return 'van';
    }
    return 'car';
  }

  getVehicleTypeClass(vehicle: any): string {
    const type = this.getVehicleType(vehicle);
    const status = this.getVehicleStatusState(vehicle);
    return `${type}-${status}`;
  }

  private getVehicleStatusState(vehicle: any): string {
    // Gray: No data for 30+ minutes
    if (!vehicle.isOnline) return 'offline';
    // Red: Ignition OFF (parked)
    if (!vehicle.ignitionOn) return 'parked';
    // Green: Ignition ON + speed > 5
    if ((vehicle.currentSpeed || 0) > 5) return 'moving';
    // Orange: Ignition ON + speed <= 5 (idle)
    return 'stopped';
  }

  getStatusTag(vehicle: any): string {
    // Replace "allowed/not allowed" with "online/offline"
    if (!vehicle.isOnline) return 'Hors ligne';
    if (!vehicle.ignitionOn) return 'Stationné';
    if ((vehicle.currentSpeed || 0) > 5) return 'En marche';
    return 'Au ralenti';
  }

  getStatusColorClass(vehicle: any): string {
    // Gray: offline (no data 30+ min)
    if (!vehicle.isOnline) return 'status-offline';
    // Red: ignition OFF
    if (!vehicle.ignitionOn) return 'status-parked';
    // Green: moving (ignition ON + speed > 5)
    if ((vehicle.currentSpeed || 0) > 5) return 'status-moving';
    // Orange: idle (ignition ON + speed <= 5)
    return 'status-stopped';
  }

  isVehicleMoving(vehicle: any): boolean {
    return vehicle.isOnline && (vehicle.currentSpeed || 0) > 5;
  }

  getLastUpdateTime(vehicle: any): string {
    const lastComm = (vehicle as any).lastCommunication;
    if (lastComm) {
      const date = new Date(lastComm);
      const now = new Date();
      const diffMs = now.getTime() - date.getTime();
      const diffSec = Math.floor(diffMs / 1000);

      if (diffSec < 60) {
        return `${diffSec} s ago, ${date.toLocaleDateString('en-GB')} ${date.toLocaleTimeString('en-GB')}`;
      }
      const diffMin = Math.floor(diffSec / 60);
      if (diffMin < 60) {
        return `${diffMin} min ago, ${date.toLocaleDateString('en-GB')} ${date.toLocaleTimeString('en-GB')}`;
      }
      return date.toLocaleDateString('en-GB') + ' ' + date.toLocaleTimeString('en-GB');
    }
    return 'Unknown';
  }

  getVehicleAddress(vehicle: any): string {
    if (!vehicle.currentLocation) {
      return 'Position non disponible';
    }
    
    const lat = vehicle.currentLocation.lat;
    const lng = vehicle.currentLocation.lng;
    const cacheKey = `${lat.toFixed(4)},${lng.toFixed(4)}`;
    
    // Return cached address if available
    if (this.addressCache.has(cacheKey)) {
      return this.addressCache.get(cacheKey)!;
    }
    
    // Set temporary placeholder and fetch address
    this.addressCache.set(cacheKey, `${lat.toFixed(4)}°, ${lng.toFixed(4)}°`);
    
    // Async reverse geocoding
    this.geocodingService.reverseGeocode(lat, lng).subscribe({
      next: (address) => {
        this.addressCache.set(cacheKey, address);
        this.cdr.detectChanges();
      },
      error: () => {
        // Keep coordinates as fallback
      }
    });
    
    return this.addressCache.get(cacheKey)!;
  }

  getCurrentTime(): string {
    const now = new Date();
    const offset = now.getTimezoneOffset();
    const offsetHours = Math.abs(Math.floor(offset / 60));
    const sign = offset <= 0 ? '+' : '-';
    return `${now.toLocaleTimeString('en-GB')} (${sign}${offsetHours.toString().padStart(2, '0')})`;
  }

  toggleLayersMenu() {
    this.showLayersMenu = !this.showLayersMenu;
  }

  zoomIn() {
    if (this.map) {
      this.map.zoomIn();
    }
  }

  zoomOut() {
    if (this.map) {
      this.map.zoomOut();
    }
  }

  centerOnVehicles() {
    if (this.map && this.vehicleMarkers.size > 0) {
      const group = new L.FeatureGroup(Array.from(this.vehicleMarkers.values()));
      this.map.fitBounds(group.getBounds().pad(0.1));
    }
  }

  centerOnVehicle(vehicle: any) {
    if (this.map && vehicle?.currentLocation) {
      this.map.setView([vehicle.currentLocation.latitude, vehicle.currentLocation.longitude], 16);
    }
  }

  getVehicleStats(vehicle: any): any {
    return vehicle?.stats || null;
  }

  formatDuration(duration: string | null | undefined): string {
    if (!duration) return '0h 0m';
    
    // Parse ISO 8601 duration or TimeSpan format (e.g., "01:30:00" or "PT1H30M")
    if (duration.includes(':')) {
      const parts = duration.split(':');
      const hours = parseInt(parts[0], 10) || 0;
      const minutes = parseInt(parts[1], 10) || 0;
      return `${hours}h ${minutes}m`;
    }
    
    // Handle .NET TimeSpan serialized as total time
    const match = duration.match(/(\d+)\.?(\d{2})?:?(\d{2})?:?(\d{2})?/);
    if (match) {
      const hours = parseInt(match[1], 10) || 0;
      const minutes = parseInt(match[2], 10) || 0;
      return `${hours}h ${minutes}m`;
    }
    
    return '0h 0m';
  }
}
