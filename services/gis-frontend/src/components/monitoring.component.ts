import { Component, OnInit, OnDestroy, AfterViewInit, ChangeDetectorRef, NgZone, ApplicationRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiService } from '../services/api.service';
import { SignalRService, PositionUpdate } from '../services/signalr.service';
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

  // Panel & Tabs
  isPanelCollapsed = false;
  activeTab: 'details' | 'playback' | 'message' = 'details';

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
  useRoadSnapping = false; // Toggle between road-snapped route and straight lines (disabled by default for progressive drawing)
  pointMarkers: L.CircleMarker[] = []; // Markers for each GPS point
  filteredBirdFlights = 0; // Count of filtered bird flight positions
  
  // Progressive trace drawing
  progressivePolylines: L.Polyline[] = []; // Colored segments for progressive drawing
  traceDrawnUpToIndex = 0; // Track how much of the trace has been drawn
  
  // Live marker visibility during playback
  hiddenLiveMarker: L.Marker | null = null; // Store hidden live marker during playback
  hiddenLiveMarkerId: string | null = null; // Track which marker is hidden

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

  constructor(
    private router: Router,
    private apiService: ApiService,
    private signalRService: SignalRService,
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
      
      // If this vehicle is selected, update the panel
      if (this.selectedVehicle?.id === vehicle.id) {
        this.selectedVehicle = { ...vehicle };
      }
      
      // Force change detection
      this.cdr.detectChanges();
    }
  }

  updateSingleVehicleMarker(vehicle: any) {
    if (!this.map || !this.mapReady || !vehicle.currentLocation) return;

    const markerId = vehicle.id?.toString();
    const existingMarker = this.vehicleMarkers.get(markerId);
    const isMoving = (vehicle.currentSpeed || 0) > 5;
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
            currentSpeed: v.lastPosition?.speedKph || 0
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
        const isMoving = (vehicle.currentSpeed || 0) > 5;
        const icon = this.createVehicleIcon(vehicle, isMoving);

        const marker = L.marker([vehicle.currentLocation.lat, vehicle.currentLocation.lng], { icon })
          .bindPopup(this.createPopupContent(vehicle))
          .on('click', () => this.selectVehicle(vehicle));

        marker.addTo(this.map!);
        this.vehicleMarkers.set(vehicle.id, marker);
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
        color = '#4caf50';
        statusClass = 'moving';
      } else {
        color = '#ff9800';
        statusClass = 'stopped';
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
    return `
      <div class="vehicle-popup">
        <h3>${vehicle.brand} ${vehicle.model}</h3>
        <p><strong>Immatriculation:</strong> ${vehicle.plate}</p>
        <p><strong>Vitesse:</strong> ${vehicle.currentSpeed || 0} km/h</p>
        <p><strong>Statut:</strong> ${vehicle.isOnline ? 'En ligne' : 'Hors ligne'}</p>
      </div>
    `;
  }

  selectVehicle(vehicle: any) {
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
    this.popupPosition = { x: 0, y: 0 }; // Reset position to center
    this.isDragging = false;

    if (this.map && vehicle.currentLocation) {
      this.map.setView([vehicle.currentLocation.lat, vehicle.currentLocation.lng], 12);
    }
  }

  // Hide the live marker of the selected vehicle during playback
  hideLiveMarker(vehicleId: string) {
    if (!this.map) return;
    
    const marker = this.vehicleMarkers.get(vehicleId);
    if (marker) {
      marker.remove();
      this.hiddenLiveMarker = marker;
      this.hiddenLiveMarkerId = vehicleId;
      console.log(`Live marker hidden for vehicle: ${vehicleId}`);
    }
  }

  // Restore the hidden live marker when playback ends
  restoreLiveMarker() {
    if (this.hiddenLiveMarker && this.hiddenLiveMarkerId && this.map) {
      // Re-add the marker to the map
      this.hiddenLiveMarker.addTo(this.map);
      console.log(`Live marker restored for vehicle: ${this.hiddenLiveMarkerId}`);
      
      // Clear the hidden marker references
      this.hiddenLiveMarker = null;
      this.hiddenLiveMarkerId = null;
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
      const popupContent = `
        <div style="font-family: Arial, sans-serif; min-width: 180px;">
          <div style="font-weight: bold; color: #3b82f6; margin-bottom: 8px; border-bottom: 1px solid #e5e7eb; padding-bottom: 4px;">
            Point ${index + 1}
          </div>
          <div style="font-size: 12px; line-height: 1.6;">
            <div><strong>🕐 Heure:</strong> ${time}</div>
            <div><strong>📍 Lat:</strong> ${position.latitude.toFixed(6)}</div>
            <div><strong>📍 Lon:</strong> ${position.longitude.toFixed(6)}</div>
            <div><strong>🚗 Vitesse:</strong> ${(position.speedKph || 0).toFixed(1)} km/h</div>
            <div><strong>⛽ Carburant:</strong> ${position.fuelRaw || 0}%</div>
            <div><strong>🌡️ Température:</strong> ${position.temperatureC != null ? position.temperatureC : 'N/A'}°C</div>
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
    const url = `/api/osrm/route/v1/driving/${coordsStr}?overview=full&geometries=geojson`;

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

    // Create an enhanced vehicle icon with status color and direction indicator
    const vehicleIcon = this.createPlaybackVehicleIcon(statusColor, heading, speed);

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
    
    this.playbackMarker.bindPopup(`
      <div style="font-family: Arial, sans-serif; min-width: 200px;">
        <div style="font-weight: bold; color: ${statusColor}; margin-bottom: 8px; border-bottom: 2px solid ${statusColor}; padding-bottom: 4px;">
          ${statusLabel}
        </div>
        <div style="font-size: 12px; line-height: 1.8;">
          <div><strong>🕐 Heure:</strong> ${time}</div>
          <div><strong>🚗 Vitesse:</strong> ${speed.toFixed(1)} km/h</div>
          <div><strong>🔑 Moteur:</strong> ${ignitionStatus}</div>
          <div><strong>🧭 Direction:</strong> ${heading}°</div>
          <div><strong>📍 Position:</strong> ${position.latitude.toFixed(5)}, ${position.longitude.toFixed(5)}</div>
          ${position.fuelRaw ? `<div><strong>⛽ Carburant:</strong> ${position.fuelRaw}%</div>` : ''}
        </div>
      </div>
    `);
  }

  // Create an enhanced vehicle icon for playback with status color and direction
  createPlaybackVehicleIcon(color: string, heading: number, speed: number): L.DivIcon {
    // Determine if vehicle is moving for animation
    const isMoving = speed > 5;
    const pulseClass = isMoving ? 'pulse-animation' : '';
    
    // SVG vehicle icon (car shape) with rotation based on heading
    const vehicleSvg = `
      <svg width="40" height="40" viewBox="0 0 40 40" xmlns="http://www.w3.org/2000/svg" style="transform: rotate(${heading}deg);">
        <!-- Shadow -->
        <ellipse cx="20" cy="38" rx="12" ry="3" fill="rgba(0,0,0,0.2)"/>
        <!-- Car body -->
        <g transform="translate(20,20)">
          <!-- Main body -->
          <path d="M-8,-15 L8,-15 L10,-8 L10,12 L-10,12 L-10,-8 Z" fill="${color}" stroke="white" stroke-width="2"/>
          <!-- Windshield -->
          <path d="M-6,-12 L6,-12 L7,-6 L-7,-6 Z" fill="rgba(255,255,255,0.6)"/>
          <!-- Headlights -->
          <circle cx="-5" cy="-14" r="2" fill="white"/>
          <circle cx="5" cy="-14" r="2" fill="white"/>
          <!-- Direction indicator (arrow) -->
          <path d="M0,-18 L4,-14 L-4,-14 Z" fill="white"/>
        </g>
      </svg>
    `;

    return L.divIcon({
      html: `
        <div class="playback-vehicle-icon ${pulseClass}" style="position: relative;">
          ${vehicleSvg}
        </div>
      `,
      className: 'custom-vehicle-marker',
      iconSize: [40, 40],
      iconAnchor: [20, 20]
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

    const intervalMs = 1000 / this.playbackSpeed;

    // Run setInterval inside NgZone to ensure Angular change detection triggers
    this.ngZone.runOutsideAngular(() => {
      this.playbackInterval = setInterval(() => {
        // Run state updates inside Angular zone for change detection
        this.ngZone.run(() => {
          if (this.playbackIndex < this.playbackPositions.length - 1) {
            const previousIndex = this.playbackIndex;
            this.playbackIndex++;
            this.playbackProgress = (this.playbackIndex / (this.playbackPositions.length - 1)) * 100;
            
            // Draw progressive trace segment from previous position to current
            this.drawProgressiveSegment(previousIndex, this.playbackIndex);
            
            // Update vehicle marker with status color
            this.updatePlaybackMarker();
            
            // Center map on current position
            const pos = this.playbackPositions[this.playbackIndex];
            if (this.map && pos) {
              this.map.panTo([pos.latitude, pos.longitude]);
            }
            
            // Force change detection to update UI
            this.cdr.detectChanges();
          } else {
            // Playback reached the end - auto-complete
            this.isPlaying = false;
            this.stopPlaybackAnimation();
            
            // Show completion message and offer to end playback
            console.log('Playback completed - trace remains visible until user ends playback');
            this.cdr.detectChanges();
          }
        });
      }, intervalMs);
    });
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

  // Draw a road-snapped segment between two consecutive points using OSRM
  private async drawRoutedSegment(fromPos: any, toPos: any, color: string) {
    if (!this.map) return;
    
    // OSRM API expects lon,lat format
    const coordsStr = `${fromPos.longitude},${fromPos.latitude};${toPos.longitude},${toPos.latitude}`;
    const url = `/api/osrm/route/v1/driving/${coordsStr}?overview=full&geometries=geojson`;

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

    // Restore the live marker that was hidden during playback
    this.restoreLiveMarker();

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

  // Popup drag methods
  startDrag(event: MouseEvent) {
    const popup = (event.target as HTMLElement).closest('.vehicle-popup-panel') as HTMLElement;
    const container = popup?.parentElement;
    if (!popup || !container) return;

    this.isDragging = true;
    const popupRect = popup.getBoundingClientRect();
    const containerRect = container.getBoundingClientRect();

    // Position relative to container
    const currentLeft = popupRect.left - containerRect.left;
    const currentTop = popupRect.top - containerRect.top;

    // Offset from mouse click to popup top-left
    this.dragOffset = {
      x: event.clientX - containerRect.left - currentLeft,
      y: event.clientY - containerRect.top - currentTop
    };

    // Set initial position relative to container
    this.popupPosition = { x: currentLeft, y: currentTop };
  }

  onDrag(event: MouseEvent) {
    if (!this.isDragging) return;
    
    const container = document.querySelector('.map-container');
    if (!container) return;

    const containerRect = container.getBoundingClientRect();
    
    this.popupPosition = {
      x: event.clientX - containerRect.left - this.dragOffset.x,
      y: event.clientY - containerRect.top - this.dragOffset.y
    };
  }

  stopDrag() {
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
    if (vehicle.currentLocation) {
      return `${vehicle.currentLocation.lat.toFixed(4)}, ${vehicle.currentLocation.lng.toFixed(4)}`;
    }
    return 'Location unavailable';
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
