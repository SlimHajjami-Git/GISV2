import { Component, OnInit, OnDestroy, AfterViewInit, ChangeDetectorRef, NgZone, ApplicationRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiService } from '../services/api.service';
import { SignalRService, PositionUpdate } from '../services/signalr.service';
import { GeocodingService } from '../services/geocoding.service';
import { Vehicle } from '../models/types';
import { AppLayoutComponent } from './shared/app-layout.component';
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
  useRoadSnapping = true; // OSRM road snapping enabled by default
  pointMarkers: L.CircleMarker[] = []; // Markers for each GPS point
  filteredBirdFlights = 0; // Count of filtered bird flight positions
  
  // Smooth animation properties
  private animationFrameId: number | null = null;
  private animationStartTime: number = 0;
  private animationFromPos: { lat: number; lng: number } | null = null;
  private animationToPos: { lat: number; lng: number } | null = null;
  private segmentDuration: number = 1000; // Base duration per segment in ms
  private isAnimatingSegment: boolean = false;
  smoothFollowCamera: boolean = true; // Enable smooth camera following
  
  // OSRM route animation
  private currentRouteCoords: L.LatLng[] = []; // Coordinates of current OSRM route segment
  private routeAnimationIndex: number = 0; // Current position in route animation
  
  // Progressive trace drawing
  progressivePolylines: L.Polyline[] = []; // Colored segments for progressive drawing
  traceDrawnUpToIndex = 0; // Track how much of the trace has been drawn
  
  // Live marker visibility during playback
  hiddenLiveMarkers: Map<string, L.Marker> = new Map(); // Store ALL hidden live markers during playback

  // Message
  driverMessage = '';

  // Popup drag
  isDragging = false;
  popupPosition = { x: 0, y: 0 };
  dragOffset = { x: 0, y: 0 };

  refreshInterval: any;
  signalRSubscription: Subscription | null = null;
  connectionStatus = 'Disconnected';

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
    private appRef: ApplicationRef
  ) {}

  ngOnInit() {
    if (!this.apiService.isAuthenticated()) {
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
    if (this.signalRSubscription) {
      this.signalRSubscription.unsubscribe();
    }
    this.signalRService.stopConnection();
    if (this.map) {
      this.map.remove();
    }
  }

  async initSignalR() {
    // Subscribe to connection state
    this.signalRService.connectionState$.subscribe(state => {
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
    console.log('Real-time position update:', update);
    
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

      // Update the marker on the map
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

  updateSingleVehicleMarker(vehicle: any) {
    if (!this.map || !this.mapReady || !vehicle.currentLocation) return;

    const markerId = vehicle.id?.toString();
    const existingMarker = this.vehicleMarkers.get(markerId);
    // Use vehicle.isMoving from SignalR if available, otherwise calculate from speed
    const isMoving = vehicle.isMoving ?? (vehicle.currentSpeed || 0) > 3;
    const icon = this.createVehicleIcon(vehicle, isMoving);
    const newLatLng = L.latLng(vehicle.currentLocation.lat, vehicle.currentLocation.lng);

    if (existingMarker) {
      // Animate marker movement
      existingMarker.setLatLng(newLatLng);
      existingMarker.setIcon(icon);
      existingMarker.setPopupContent(this.createPopupContent(vehicle));
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

    // Single API call to get vehicles with their GPS positions
    this.apiService.getVehiclesWithPositions().subscribe({
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
            odometerKm: v.lastPosition?.odometerKm || null
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
    let color = '#9e9e9e';
    let statusClass = 'offline';

    if (vehicle.isOnline) {
      if (isMoving) {
        // Moving (ignition ON + speed > 5) = GREEN
        color = '#4caf50';
        statusClass = 'moving';
      } else if (vehicle.ignitionOn) {
        // Stopped with engine ON = ORANGE
        color = '#ff9800';
        statusClass = 'stopped';
      } else {
        // Parked (ignition OFF) = RED
        color = '#ef4444';
        statusClass = 'parked';
      }
    }

    const iconSvg = getVehicleIcon(vehicleType);

    const iconHtml = `
      <div class="vehicle-marker vehicle-marker--${statusClass}" style="background-color: ${color};">
        <div class="marker-icon">${iconSvg}</div>
      </div>
    `;

    return L.divIcon({
      html: iconHtml,
      className: 'custom-vehicle-marker',
      iconSize: [40, 40],
      iconAnchor: [20, 20]
    });
  }

  createPopupContent(vehicle: any): string {
    const isOnline = vehicle.isOnline;
    const statusColor = isOnline ? '#10b981' : '#6b7280';
    const statusBg = isOnline ? 'rgba(16, 185, 129, 0.1)' : 'rgba(107, 114, 128, 0.1)';
    const statusText = isOnline ? 'En ligne' : 'Hors ligne';
    const speed = vehicle.currentSpeed || 0;
    const isMoving = speed > 3;
    
    return `
      <div style="
        font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
        min-width: 220px;
        padding: 0;
        margin: -14px -20px;
      ">
        <!-- Header with gradient -->
        <div style="
          background: linear-gradient(135deg, #1e3a5f 0%, #0f2744 100%);
          padding: 14px 16px;
          border-radius: 8px 8px 0 0;
          display: flex;
          align-items: center;
          gap: 12px;
        ">
          <div style="
            width: 40px;
            height: 40px;
            background: rgba(255,255,255,0.15);
            border-radius: 10px;
            display: flex;
            align-items: center;
            justify-content: center;
          ">
            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="2">
              <rect x="1" y="3" width="15" height="13" rx="2"/>
              <path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-1"/>
              <circle cx="5.5" cy="18.5" r="2.5"/>
              <circle cx="18.5" cy="18.5" r="2.5"/>
            </svg>
          </div>
          <div style="flex: 1;">
            <div style="font-weight: 600; font-size: 14px; color: #fff; margin-bottom: 2px;">
              ${vehicle.name || vehicle.brand + ' ' + vehicle.model}
            </div>
            <div style="font-size: 12px; color: rgba(255,255,255,0.7); font-family: monospace;">
              ${vehicle.plate || 'N/A'}
            </div>
          </div>
        </div>
        
        <!-- Status bar -->
        <div style="
          display: flex;
          align-items: center;
          justify-content: space-between;
          padding: 10px 16px;
          background: ${statusBg};
          border-bottom: 1px solid #e5e7eb;
        ">
          <div style="display: flex; align-items: center; gap: 6px;">
            <div style="
              width: 8px;
              height: 8px;
              background: ${statusColor};
              border-radius: 50%;
              ${isOnline ? 'box-shadow: 0 0 0 3px rgba(16, 185, 129, 0.2);' : ''}
            "></div>
            <span style="font-size: 12px; font-weight: 500; color: ${statusColor};">${statusText}</span>
          </div>
          ${isMoving ? `
            <div style="
              display: flex;
              align-items: center;
              gap: 4px;
              padding: 3px 8px;
              background: rgba(59, 130, 246, 0.1);
              border-radius: 12px;
            ">
              <svg width="12" height="12" viewBox="0 0 24 24" fill="#3b82f6" stroke="none">
                <path d="M12 2L4.5 20.3l.7.7 6.8-3 6.8 3 .7-.7L12 2z"/>
              </svg>
              <span style="font-size: 11px; font-weight: 600; color: #3b82f6;">En mouvement</span>
            </div>
          ` : ''}
        </div>
        
        <!-- Info grid -->
        <div style="padding: 12px 16px 14px; background: #fff; border-radius: 0 0 8px 8px;">
          <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 10px;">
            <div style="
              background: #f8fafc;
              padding: 10px 12px;
              border-radius: 8px;
              border: 1px solid #e2e8f0;
            ">
              <div style="font-size: 10px; color: #64748b; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 4px;">Vitesse</div>
              <div style="font-size: 18px; font-weight: 700; color: #1e293b;">${speed}<span style="font-size: 11px; font-weight: 500; color: #64748b;"> km/h</span></div>
            </div>
            <div style="
              background: #f8fafc;
              padding: 10px 12px;
              border-radius: 8px;
              border: 1px solid #e2e8f0;
            ">
              <div style="font-size: 10px; color: #64748b; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 4px;">Kilométrage</div>
              <div style="font-size: 14px; font-weight: 600; color: #1e293b;">${vehicle.odometerKm ? (vehicle.odometerKm).toLocaleString() + ' km' : 'N/A'}</div>
            </div>
          </div>
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
    const stats = this.getVehicleStats(vehicle);
    return stats?.temperature != null && stats.temperature >= 90;
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
        this.playbackFromDate = yesterday.toISOString().slice(0, 16);
        this.playbackToDate = now.toISOString().slice(0, 16);
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
        filtered = filtered.filter((v: any) => v.isOnline && (v.currentSpeed || 0) > 5);
      } else if (this.filterStatus === 'stopped') {
        filtered = filtered.filter((v: any) => v.isOnline && (v.currentSpeed || 0) <= 5);
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
    // Auto-refresh is now a fallback for SignalR disconnection
    // SignalR push handles real-time updates with adaptive intervals:
    // - Moving vehicles: 30 seconds
    // - Stopped vehicles: 2 minutes  
    // - Parked vehicles (ignition off): 10 minutes
    // This polling is just a safety net every 5 minutes
    this.refreshInterval = setInterval(() => {
      if (this.connectionStatus !== 'Connected') {
        // Only poll when SignalR is not connected
        this.loadData();
      }
    }, 300000); // 5 minutes fallback
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

    this.apiService.getVehicleHistory(vehicleId, fromDate, toDate).subscribe({
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

        this.isPlaybackLoaded = true;
        this.playbackIndex = 0;
        this.playbackProgress = 0;
        this.playbackLoading = false;

        // Switch to satellite map for better playback visualization (like Wialon)
        if (this.mapStyle !== 'satellite') {
          this.previousMapStyle = this.mapStyle; // Store current style to restore later
          this.changeMapStyle('satellite');
        }

        // Hide the live marker of the selected vehicle during playback
        this.hideLiveMarker(vehicleId.toString());

        // Draw the complete route polyline
        this.drawPlaybackRoute();
        
        // Position the playback marker at start
        this.updatePlaybackMarker();
        
        // Force change detection to update UI with new point count
        this.cdr.detectChanges();
        
        console.log(`Playback loaded for vehicle ${vehicleId}: ${this.playbackPositions.length} points`);
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
    
    // Reset progressive drawing index
    this.traceDrawnUpToIndex = 0;

    // Center map on starting position and set appropriate zoom
    const startPos = this.playbackPositions[0];
    this.map.setView([startPos.latitude, startPos.longitude], 15);
    
    // Don't draw full trace - it will be drawn progressively during playback
    // Just show the vehicle icon at the starting position
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
            <div style="
              margin-top: 8px;
              padding: 6px 10px;
              background: #fff;
              border-radius: 4px;
              border: 1px solid #e2e8f0;
              font-size: 10px;
              color: #64748b;
              font-family: 'SF Mono', Monaco, monospace;
              text-align: center;
            ">
              ${position.latitude.toFixed(6)}, ${position.longitude.toFixed(6)}
            </div>
          </div>
        </div>
      `;

      marker.bindPopup(popupContent);
      this.pointMarkers.push(marker);
    });
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
    // Clear progressive polylines
    this.progressivePolylines.forEach(polyline => polyline.remove());
    this.progressivePolylines = [];
    this.traceDrawnUpToIndex = 0;
    // Clear point markers
    this.pointMarkers.forEach(marker => marker.remove());
    this.pointMarkers = [];
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

    // Use local OSRM server (self-hosted) for road snapping
    // Format: lon,lat;lon,lat;...
    const coordsStr = coords.map(c => `${c.lng},${c.lat}`).join(';');
    
    // Calculate bearings for each point to constrain OSRM to correct road direction
    const bearings = coords.map((c, i) => {
      if (i < coords.length - 1) {
        const bearing = Math.round(this.calculateBearing(c.lat, c.lng, coords[i + 1].lat, coords[i + 1].lng));
        return `${bearing},45`;
      } else {
        // Last point: use bearing from previous point
        const bearing = Math.round(this.calculateBearing(coords[i - 1].lat, coords[i - 1].lng, c.lat, c.lng));
        return `${bearing},45`;
      }
    }).join(';');
    
    const url = `/api/osrm/route/v1/driving/${coordsStr}?overview=full&geometries=geojson&bearings=${bearings}`;

    try {
      const response = await fetch(url);
      
      if (!response.ok) {
        throw new Error(`OSRM API error: ${response.status}`);
      }

      const data = await response.json();
      
      if (data.code === 'Ok' && data.routes && data.routes[0] && data.routes[0].geometry) {
        // Convert GeoJSON coordinates to Leaflet LatLng
        const routeCoords: L.LatLng[] = data.routes[0].geometry.coordinates.map(
          (c: number[]) => L.latLng(c[1], c[0])
        );

        this.playbackPolyline = L.polyline(routeCoords, {
          color: '#3b82f6',
          weight: 5,
          opacity: 0.8
        }).addTo(this.map!);

        this.map!.fitBounds(this.playbackPolyline.getBounds().pad(0.1));
        console.log('OSRM road-snapped route drawn successfully with', routeCoords.length, 'points');
      } else {
        throw new Error('Invalid OSRM response: ' + (data.code || 'unknown'));
      }
    } catch (error) {
      console.warn('Road snapping failed, falling back to straight lines:', error);
      this.drawStraightPath(coords);
    }
  }

  toggleRoadSnapping() {
    this.useRoadSnapping = !this.useRoadSnapping;
    if (this.isPlaybackLoaded) {
      this.drawPlaybackRoute();
    }
  }

  updatePlaybackMarker() {
    if (!this.map || this.playbackPositions.length === 0) return;

    const position = this.playbackPositions[this.playbackIndex];
    if (!position) return;

    const latLng: L.LatLngExpression = [position.latitude, position.longitude];
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

    // Update popup with detailed position info
    const time = new Date(position.recordedAt).toLocaleString('fr-FR');
    const statusLabel = this.getPlaybackStatusLabel();
    const ignitionStatus = position.ignitionOn ? 'Allumé' : 'Éteint';
    
    const ignitionColor = position.ignitionOn ? '#10b981' : '#ef4444';
    const ignitionBg = position.ignitionOn ? 'rgba(16, 185, 129, 0.1)' : 'rgba(239, 68, 68, 0.1)';
    
    this.playbackMarker.bindPopup(`
      <div style="
        font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
        min-width: 260px;
        padding: 0;
        margin: -14px -20px;
      ">
        <!-- Header -->
        <div style="
          background: linear-gradient(135deg, ${statusColor} 0%, ${statusColor}dd 100%);
          padding: 12px 16px;
          border-radius: 8px 8px 0 0;
          display: flex;
          align-items: center;
          justify-content: space-between;
        ">
          <div style="display: flex; align-items: center; gap: 8px;">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="2">
              <polygon points="5 3 19 12 5 21 5 3"/>
            </svg>
            <span style="font-weight: 600; font-size: 13px; color: #fff;">${statusLabel}</span>
          </div>
          <div style="
            background: rgba(255,255,255,0.2);
            padding: 4px 10px;
            border-radius: 12px;
            font-size: 11px;
            font-weight: 600;
            color: #fff;
          ">${time.split(' ')[1] || time}</div>
        </div>
        
        <!-- Speed display -->
        <div style="
          background: #fff;
          padding: 16px;
          display: flex;
          align-items: center;
          justify-content: center;
          gap: 16px;
          border-bottom: 1px solid #e5e7eb;
        ">
          <div style="text-align: center;">
            <div style="font-size: 32px; font-weight: 700; color: #1e293b; line-height: 1;">${speed.toFixed(0)}</div>
            <div style="font-size: 11px; color: #64748b; text-transform: uppercase; letter-spacing: 0.5px;">km/h</div>
          </div>
          <div style="width: 1px; height: 40px; background: #e5e7eb;"></div>
          <div style="text-align: center;">
            <div style="font-size: 24px; font-weight: 600; color: #1e293b; line-height: 1;">${heading}°</div>
            <div style="font-size: 11px; color: #64748b; text-transform: uppercase; letter-spacing: 0.5px;">Direction</div>
          </div>
        </div>
        
        <!-- Info grid -->
        <div style="padding: 12px 16px; background: #f8fafc;">
          <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 8px;">
            <!-- Ignition -->
            <div style="
              background: ${ignitionBg};
              padding: 10px 12px;
              border-radius: 8px;
              display: flex;
              align-items: center;
              gap: 8px;
            ">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="${ignitionColor}" stroke-width="2">
                <path d="M15 7h3a2 2 0 0 1 2 2v6a2 2 0 0 1-2 2h-3"/>
                <path d="M9 17H6a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2h3"/>
                <circle cx="12" cy="12" r="3"/>
              </svg>
              <div>
                <div style="font-size: 10px; color: #64748b;">Moteur</div>
                <div style="font-size: 12px; font-weight: 600; color: ${ignitionColor};">${ignitionStatus}</div>
              </div>
            </div>
            
            <!-- Fuel -->
            ${position.fuelRaw ? `
            <div style="
              background: rgba(245, 158, 11, 0.1);
              padding: 10px 12px;
              border-radius: 8px;
              display: flex;
              align-items: center;
              gap: 8px;
            ">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#f59e0b" stroke-width="2">
                <path d="M3 22V6a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v16"/>
                <path d="M15 11h3.5a2 2 0 0 1 2 2v3a1.5 1.5 0 0 0 3 0v-7l-3-3"/>
                <path d="M5 10h8"/>
              </svg>
              <div>
                <div style="font-size: 10px; color: #64748b;">Carburant</div>
                <div style="font-size: 12px; font-weight: 600; color: #f59e0b;">${position.fuelRaw}%</div>
              </div>
            </div>
            ` : `
            <div style="
              background: rgba(100, 116, 139, 0.1);
              padding: 10px 12px;
              border-radius: 8px;
              display: flex;
              align-items: center;
              gap: 8px;
            ">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#64748b" stroke-width="2">
                <circle cx="12" cy="12" r="10"/>
                <line x1="12" y1="8" x2="12" y2="12"/>
                <line x1="12" y1="16" x2="12.01" y2="16"/>
              </svg>
              <div>
                <div style="font-size: 10px; color: #64748b;">Carburant</div>
                <div style="font-size: 12px; font-weight: 600; color: #64748b;">N/A</div>
              </div>
            </div>
            `}
          </div>
          
          <!-- Coordinates -->
          <div style="
            margin-top: 8px;
            padding: 8px 10px;
            background: #fff;
            border-radius: 6px;
            border: 1px solid #e2e8f0;
            font-size: 11px;
            color: #64748b;
            font-family: 'SF Mono', Monaco, monospace;
            text-align: center;
          ">
            📍 ${position.latitude.toFixed(6)}, ${position.longitude.toFixed(6)}
          </div>
        </div>
      </div>
    `);
  }

  // Create an enhanced vehicle icon for playback with status color and direction
  createPlaybackVehicleIcon(color: string, heading: number, speed: number, vehicleType?: string, vehicleName?: string): L.DivIcon {
    // Determine if vehicle is moving for animation
    const isMoving = speed > 5;
    const pulseClass = isMoving ? 'pulse-animation' : '';
    
    // Get SVG icon based on vehicle type
    const vehicleSvg = this.getVehicleTypeSvg(vehicleType || 'car', color, heading);
    
    // Truncate vehicle name for display
    const displayName = vehicleName ? (vehicleName.length > 15 ? vehicleName.substring(0, 12) + '...' : vehicleName) : '';

    return L.divIcon({
      html: `
        <div class="playback-vehicle-icon ${pulseClass}" style="position: relative; display: flex; flex-direction: column; align-items: center;">
          ${vehicleSvg}
          ${displayName ? `<div class="vehicle-name-label" style="
            font-size: 10px;
            font-weight: 600;
            color: #333;
            background: rgba(255,255,255,0.9);
            padding: 2px 6px;
            border-radius: 3px;
            white-space: nowrap;
            margin-top: 2px;
            box-shadow: 0 1px 3px rgba(0,0,0,0.2);
            border: 1px solid ${color};
          ">${displayName}</div>` : ''}
        </div>
      `,
      className: 'custom-vehicle-marker',
      iconSize: [50, 60],
      iconAnchor: [25, 25]
    });
  }

  // Get SVG icon based on vehicle type
  getVehicleTypeSvg(type: string, color: string, heading: number): string {
    const normalizedType = (type || 'car').toLowerCase();
    
    // Truck icon - like in the reference image (red truck)
    if (normalizedType.includes('truck') || normalizedType.includes('camion') || normalizedType.includes('poids')) {
      return `
        <svg width="36" height="36" viewBox="0 0 36 36" xmlns="http://www.w3.org/2000/svg" style="transform: rotate(${heading}deg); filter: drop-shadow(0 2px 3px rgba(0,0,0,0.3));">
          <!-- Truck body -->
          <rect x="4" y="8" width="28" height="16" rx="2" fill="${color}" stroke="white" stroke-width="1.5"/>
          <!-- Cab -->
          <rect x="22" y="4" width="10" height="12" rx="1" fill="${color}" stroke="white" stroke-width="1.5"/>
          <!-- Windshield -->
          <rect x="24" y="6" width="6" height="5" rx="1" fill="rgba(200,230,255,0.8)"/>
          <!-- Cargo area lines -->
          <line x1="8" y1="10" x2="8" y2="22" stroke="white" stroke-width="1" opacity="0.6"/>
          <line x1="14" y1="10" x2="14" y2="22" stroke="white" stroke-width="1" opacity="0.6"/>
          <!-- Wheels -->
          <circle cx="10" cy="26" r="3" fill="#333" stroke="white" stroke-width="1"/>
          <circle cx="26" cy="26" r="3" fill="#333" stroke="white" stroke-width="1"/>
          <!-- Direction indicator -->
          <polygon points="18,2 21,6 15,6" fill="white"/>
        </svg>
      `;
    }
    
    // Van/Fourgon icon
    if (normalizedType.includes('van') || normalizedType.includes('fourgon') || normalizedType.includes('utilitaire')) {
      return `
        <svg width="36" height="36" viewBox="0 0 36 36" xmlns="http://www.w3.org/2000/svg" style="transform: rotate(${heading}deg); filter: drop-shadow(0 2px 3px rgba(0,0,0,0.3));">
          <!-- Van body -->
          <path d="M6,10 L6,24 L30,24 L30,10 L22,10 L20,6 L10,6 L6,10 Z" fill="${color}" stroke="white" stroke-width="1.5"/>
          <!-- Windshield -->
          <path d="M20,8 L22,12 L30,12 L30,8 Z" fill="rgba(200,230,255,0.8)"/>
          <!-- Side windows -->
          <rect x="8" y="11" width="5" height="4" rx="1" fill="rgba(200,230,255,0.6)"/>
          <rect x="15" y="11" width="5" height="4" rx="1" fill="rgba(200,230,255,0.6)"/>
          <!-- Wheels -->
          <circle cx="11" cy="26" r="3" fill="#333" stroke="white" stroke-width="1"/>
          <circle cx="25" cy="26" r="3" fill="#333" stroke="white" stroke-width="1"/>
          <!-- Direction indicator -->
          <polygon points="18,2 21,6 15,6" fill="white"/>
        </svg>
      `;
    }
    
    // Bus icon
    if (normalizedType.includes('bus') || normalizedType.includes('autobus')) {
      return `
        <svg width="36" height="36" viewBox="0 0 36 36" xmlns="http://www.w3.org/2000/svg" style="transform: rotate(${heading}deg); filter: drop-shadow(0 2px 3px rgba(0,0,0,0.3));">
          <!-- Bus body -->
          <rect x="4" y="6" width="28" height="20" rx="3" fill="${color}" stroke="white" stroke-width="1.5"/>
          <!-- Windows row -->
          <rect x="7" y="9" width="4" height="6" rx="1" fill="rgba(200,230,255,0.8)"/>
          <rect x="13" y="9" width="4" height="6" rx="1" fill="rgba(200,230,255,0.8)"/>
          <rect x="19" y="9" width="4" height="6" rx="1" fill="rgba(200,230,255,0.8)"/>
          <rect x="25" y="9" width="4" height="6" rx="1" fill="rgba(200,230,255,0.8)"/>
          <!-- Wheels -->
          <circle cx="10" cy="28" r="3" fill="#333" stroke="white" stroke-width="1"/>
          <circle cx="26" cy="28" r="3" fill="#333" stroke="white" stroke-width="1"/>
          <!-- Direction indicator -->
          <polygon points="18,2 21,5 15,5" fill="white"/>
        </svg>
      `;
    }
    
    // Motorcycle icon
    if (normalizedType.includes('moto') || normalizedType.includes('scooter') || normalizedType.includes('bike')) {
      return `
        <svg width="36" height="36" viewBox="0 0 36 36" xmlns="http://www.w3.org/2000/svg" style="transform: rotate(${heading}deg); filter: drop-shadow(0 2px 3px rgba(0,0,0,0.3));">
          <!-- Body -->
          <ellipse cx="18" cy="18" rx="8" ry="5" fill="${color}" stroke="white" stroke-width="1.5"/>
          <!-- Handlebar -->
          <line x1="14" y1="10" x2="22" y2="10" stroke="${color}" stroke-width="3" stroke-linecap="round"/>
          <!-- Wheels -->
          <circle cx="18" cy="26" r="4" fill="#333" stroke="white" stroke-width="1"/>
          <circle cx="18" cy="8" r="2" fill="#333" stroke="white" stroke-width="1"/>
          <!-- Direction indicator -->
          <polygon points="18,2 20,5 16,5" fill="white"/>
        </svg>
      `;
    }
    
    // Default: Car icon
    return `
      <svg width="36" height="36" viewBox="0 0 36 36" xmlns="http://www.w3.org/2000/svg" style="transform: rotate(${heading}deg); filter: drop-shadow(0 2px 3px rgba(0,0,0,0.3));">
        <!-- Car body -->
        <path d="M8,14 L8,24 L28,24 L28,14 L24,14 L22,8 L14,8 L12,14 Z" fill="${color}" stroke="white" stroke-width="1.5"/>
        <!-- Windshield -->
        <path d="M14,10 L22,10 L23,13 L13,13 Z" fill="rgba(200,230,255,0.8)"/>
        <!-- Rear window -->
        <path d="M13,15 L23,15 L23,18 L13,18 Z" fill="rgba(200,230,255,0.6)"/>
        <!-- Headlights -->
        <circle cx="12" cy="11" r="1.5" fill="white"/>
        <circle cx="24" cy="11" r="1.5" fill="white"/>
        <!-- Wheels -->
        <circle cx="12" cy="26" r="3" fill="#333" stroke="white" stroke-width="1"/>
        <circle cx="24" cy="26" r="3" fill="#333" stroke="white" stroke-width="1"/>
        <!-- Direction indicator -->
        <polygon points="18,4 21,8 15,8" fill="white"/>
      </svg>
    `;
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
    this.animateToNextPoint();
  }

  // Smooth animation using requestAnimationFrame
  private async animateToNextPoint() {
    if (!this.isPlaying || this.playbackIndex >= this.playbackPositions.length - 1) {
      // Playback complete
      this.ngZone.run(() => {
        this.isPlaying = false;
        this.isAnimatingSegment = false;
        console.log('Playback completed - trace remains visible until user ends playback');
        this.cdr.detectChanges();
      });
      return;
    }

    const fromPos = this.playbackPositions[this.playbackIndex];
    const toPos = this.playbackPositions[this.playbackIndex + 1];
    
    // Fetch OSRM route for smooth road-following animation
    this.currentRouteCoords = await this.fetchOSRMRoute(fromPos, toPos);
    this.routeAnimationIndex = 0;
    
    // Calculate total route distance for animation duration
    let totalDistance = 0;
    for (let i = 1; i < this.currentRouteCoords.length; i++) {
      const prev = this.currentRouteCoords[i - 1];
      const curr = this.currentRouteCoords[i];
      totalDistance += this.calculateDistance(prev.lat, prev.lng, curr.lat, curr.lng);
    }
    
    // Adaptive duration based on route distance
    // Base: 300ms per 100m at speed 1x (faster for smoother feel)
    const baseDuration = Math.max(300, Math.min(3000, (totalDistance / 100) * 300));
    const duration = baseDuration / this.playbackSpeed;
    
    this.animationStartTime = performance.now();
    this.segmentDuration = duration;
    this.isAnimatingSegment = true;

    // Start the animation loop
    this.animateFrame();
  }

  // Fetch OSRM route between two GPS points
  private async fetchOSRMRoute(fromPos: any, toPos: any): Promise<L.LatLng[]> {
    try {
      // Get bearing - returns null if vehicle stationary or distance too short
      const bearing = this.getBearing(fromPos, toPos);
      
      const coordsStr = `${fromPos.longitude},${fromPos.latitude};${toPos.longitude},${toPos.latitude}`;
      // Only add bearings parameter if we have a valid bearing
      const bearingsParam = bearing !== null ? `&bearings=${bearing},45;${bearing},45` : '';
      const url = `/api/osrm/route/v1/driving/${coordsStr}?overview=full&geometries=geojson${bearingsParam}`;
      
      const response = await fetch(url);
      if (!response.ok) throw new Error(`OSRM error: ${response.status}`);
      
      const data = await response.json();
      
      if (data.code === 'Ok' && data.routes?.[0]?.geometry?.coordinates) {
        return data.routes[0].geometry.coordinates.map(
          (c: number[]) => L.latLng(c[1], c[0])
        );
      }
    } catch (error) {
      console.warn('OSRM route fetch failed, using straight line:', error);
    }
    
    // Fallback to straight line if OSRM fails
    return [
      L.latLng(fromPos.latitude, fromPos.longitude),
      L.latLng(toPos.latitude, toPos.longitude)
    ];
  }

  // Animation frame loop for smooth interpolation along OSRM route
  private animateFrame() {
    if (!this.isPlaying || this.currentRouteCoords.length === 0) {
      this.isAnimatingSegment = false;
      return;
    }

    const elapsed = performance.now() - this.animationStartTime;
    const progress = Math.min(1, elapsed / this.segmentDuration);
    
    // Easing function for smooth movement
    const easedProgress = this.easeInOutCubic(progress);
    
    // Calculate position along the route based on progress
    const position = this.getPositionAlongRoute(easedProgress);
    
    // Skip if position is invalid
    if (!position) {
      // Continue to next frame anyway
      this.animationFrameId = requestAnimationFrame(() => this.animateFrame());
      return;
    }
    
    const { lat, lng, heading } = position;

    // Update marker position smoothly
    if (this.playbackMarker) {
      this.playbackMarker.setLatLng([lat, lng]);
      
      // Update marker rotation based on heading
      const currentPos = this.playbackPositions[this.playbackIndex];
      const speed = currentPos?.speedKph || 0;
      const statusColor = this.getStatusColor(currentPos);
      const vehicleType = this.selectedVehicle?.type || 'car';
      const vehicleName = this.selectedVehicle?.plate || '';
      const icon = this.createPlaybackVehicleIcon(statusColor, heading, speed, vehicleType, vehicleName);
      this.playbackMarker.setIcon(icon);
    }

    // Smooth camera follow
    if (this.map && this.smoothFollowCamera) {
      this.map.panTo([lat, lng], {
        animate: true,
        duration: 0.15,
        easeLinearity: 0.25
      });
    }

    if (progress < 1) {
      // Continue animation
      this.animationFrameId = requestAnimationFrame(() => this.animateFrame());
    } else {
      // Segment complete - move to next point
      this.ngZone.run(() => {
        const previousIndex = this.playbackIndex;
        this.playbackIndex++;
        this.playbackProgress = (this.playbackIndex / (this.playbackPositions.length - 1)) * 100;
        
        // Draw the trace segment now that animation is complete
        this.drawProgressiveSegment(previousIndex, this.playbackIndex);
        
        // Update marker icon (for status color changes)
        this.updatePlaybackMarker();
        
        this.cdr.detectChanges();
        
        // Continue to next segment
        this.isAnimatingSegment = false;
        this.animateToNextPoint();
      });
    }
  }

  // Get interpolated position along the OSRM route
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
    
    // Determine color based on vehicle status at the destination point
    const color = this.getStatusColor(toPos);
    
    // Use OSRM for road snapping if enabled, otherwise draw straight line
    if (this.useRoadSnapping) {
      this.drawRoutedSegment(fromPos, toPos, color);
    } else {
      this.drawStraightSegment(fromPos, toPos, color);
    }
    
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
        weight: 5,
        opacity: 0.9,
        lineCap: 'round',
        lineJoin: 'round'
      }
    ).addTo(this.map);

    this.progressivePolylines.push(segment);
    
    // Add a small point marker at the destination
    const pointMarker = L.circleMarker([toPos.latitude, toPos.longitude], {
      radius: 4,
      fillColor: color,
      color: '#ffffff',
      weight: 2,
      opacity: 1,
      fillOpacity: 0.9
    }).addTo(this.map);
    
    this.pointMarkers.push(pointMarker);
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

  // Draw a road-snapped segment between two consecutive points using OSRM
  private async drawRoutedSegment(fromPos: any, toPos: any, color: string) {
    if (!this.map) return;
    
    // Get bearing - returns null if vehicle stationary or distance too short
    const bearing = this.getBearing(fromPos, toPos);
    
    // OSRM API expects lon,lat format
    const coordsStr = `${fromPos.longitude},${fromPos.latitude};${toPos.longitude},${toPos.latitude}`;
    // Only add bearings parameter if we have a valid bearing
    const bearingsParam = bearing !== null ? `&bearings=${bearing},45;${bearing},45` : '';
    const url = `/api/osrm/route/v1/driving/${coordsStr}?overview=full&geometries=geojson${bearingsParam}`;

    try {
      const response = await fetch(url);
      
      if (!response.ok) {
        throw new Error(`OSRM API error: ${response.status}`);
      }

      const data = await response.json();
      
      if (data.code === 'Ok' && data.routes && data.routes[0] && data.routes[0].geometry) {
        // Convert GeoJSON coordinates to Leaflet LatLng array
        const routeCoords: L.LatLngExpression[] = data.routes[0].geometry.coordinates.map(
          (c: number[]) => [c[1], c[0]] as L.LatLngExpression
        );

        const segment = L.polyline(routeCoords, {
          color: color,
          weight: 5,
          opacity: 0.9,
          lineCap: 'round',
          lineJoin: 'round'
        }).addTo(this.map!);

        this.progressivePolylines.push(segment);
        
        // Add a small point marker at the destination
        const pointMarker = L.circleMarker([toPos.latitude, toPos.longitude], {
          radius: 4,
          fillColor: color,
          color: '#ffffff',
          weight: 2,
          opacity: 1,
          fillOpacity: 0.9
        }).addTo(this.map!);
        
        this.pointMarkers.push(pointMarker);
      } else {
        throw new Error('Invalid OSRM response');
      }
    } catch (error) {
      // Fallback to straight line if OSRM fails
      console.warn('OSRM segment routing failed, using straight line:', error);
      this.drawStraightSegment(fromPos, toPos, color);
    }
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
    
    // Clear all progressive polylines and point markers
    this.progressivePolylines.forEach(polyline => polyline.remove());
    this.progressivePolylines = [];
    this.pointMarkers.forEach(marker => marker.remove());
    this.pointMarkers = [];
    this.traceDrawnUpToIndex = 0;
    
    this.updatePlaybackMarker();

    // Center on start position
    if (this.map && this.playbackPositions.length > 0) {
      const startPos = this.playbackPositions[0];
      this.map.setView([startPos.latitude, startPos.longitude], 15);
    }
    
    this.cdr.detectChanges();
  }

  skipToEnd() {
    this.stopPlaybackAnimation();
    this.isPlaying = false;
    
    // Draw all remaining segments
    for (let i = this.traceDrawnUpToIndex; i < this.playbackPositions.length - 1; i++) {
      this.drawProgressiveSegment(i, i + 1);
    }
    
    this.playbackIndex = this.playbackPositions.length - 1;
    this.playbackProgress = 100;
    this.updatePlaybackMarker();

    // Center on end position
    if (this.map && this.playbackPositions.length > 0) {
      const endPos = this.playbackPositions[this.playbackPositions.length - 1];
      this.map.setView([endPos.latitude, endPos.longitude], 14);
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
    this.playbackProgress = progress;
    this.playbackIndex = Math.floor((progress / 100) * (this.playbackPositions.length - 1));
    this.updatePlaybackMarker();
    
    // Center map on current position
    const pos = this.playbackPositions[this.playbackIndex];
    if (this.map && pos) {
      this.map.panTo([pos.latitude, pos.longitude]);
    }
    
    this.cdr.detectChanges();
  }

  previousPoint() {
    if (this.playbackIndex > 0) {
      this.stopPlaybackAnimation();
      this.isPlaying = false;
      this.playbackIndex--;
      this.playbackProgress = (this.playbackIndex / (this.playbackPositions.length - 1)) * 100;
      this.updatePlaybackMarker();
      
      const pos = this.playbackPositions[this.playbackIndex];
      if (this.map && pos) {
        this.map.panTo([pos.latitude, pos.longitude]);
      }
      
      this.cdr.detectChanges();
    }
  }

  nextPoint() {
    if (this.playbackIndex < this.playbackPositions.length - 1) {
      this.stopPlaybackAnimation();
      this.isPlaying = false;
      this.playbackIndex++;
      this.playbackProgress = (this.playbackIndex / (this.playbackPositions.length - 1)) * 100;
      this.updatePlaybackMarker();
      
      const pos = this.playbackPositions[this.playbackIndex];
      if (this.map && pos) {
        this.map.panTo([pos.latitude, pos.longitude]);
      }
      
      this.cdr.detectChanges();
    }
  }

  // End playback - called when user clicks "Terminer" button
  endPlayback() {
    console.log('Ending playback - restoring live view');
    this.clearPlayback();
    
    // Center map on the live vehicle position
    if (this.map && this.selectedVehicle?.currentLocation) {
      this.map.setView([this.selectedVehicle.currentLocation.lat, this.selectedVehicle.currentLocation.lng], 14);
    }
  }

  clearPlayback() {
    this.stopPlaybackAnimation();
    this.isPlaybackLoaded = false;
    this.isPlaying = false;
    this.playbackPositions = [];
    this.playbackRawCount = 0;
    this.playbackIndex = 0;
    this.playbackProgress = 0;
    this.playbackVehicleId = null; // Reset vehicle tracking
    this.filteredBirdFlights = 0;
    
    // Reset smooth animation state
    this.animationFromPos = null;
    this.animationToPos = null;
    this.isAnimatingSegment = false;
    this.currentRouteCoords = [];
    this.routeAnimationIndex = 0;

    // Restore the live marker that was hidden during playback
    this.restoreLiveMarker();

    // Restore the original map style (before playback switched to satellite)
    if (this.previousMapStyle && this.mapStyle !== this.previousMapStyle) {
      this.changeMapStyle(this.previousMapStyle);
      this.previousMapStyle = null;
    }

    // Clear route display (polyline and routing control)
    this.clearRouteDisplay();
    
    if (this.playbackMarker) {
      this.playbackMarker.remove();
      this.playbackMarker = null;
    }
    
    // Force UI update after clearing
    this.cdr.detectChanges();
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
    if (!vehicle.isOnline) return 'offline';
    if ((vehicle.currentSpeed || 0) > 5) return 'moving';
    return 'stopped';
  }

  getStatusTag(vehicle: any): string {
    if (!vehicle.isOnline) return 'Not available';
    return 'Allowed';
  }

  getStatusColorClass(vehicle: any): string {
    if (!vehicle.isOnline) return 'status-offline';
    if ((vehicle.currentSpeed || 0) > 5) return 'status-moving';
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
