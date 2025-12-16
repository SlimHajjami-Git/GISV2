import { Component, OnInit, OnDestroy, signal, AfterViewInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { CompanyService } from '../../core/services/company.service';
import { VehicleService } from '../../core/services/vehicle.service';
import { GpsService } from '../../core/services/gps.service';
import { MonitoringService } from '../../core/services/monitoring.service';
import { Vehicle } from '../../core/models/monitoring.types';
import * as L from 'leaflet';

@Component({
  selector: 'app-monitoring',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './monitoring.component.html',
  styleUrls: ['./monitoring.component.css']
})
export class MonitoringComponent implements OnInit, AfterViewInit, OnDestroy {
  map: L.Map | null = null;
  vehicleMarkers = new Map<string, L.Marker>();

  vehicles = signal<Vehicle[]>([]);
  filteredVehicles = signal<Vehicle[]>([]);
  selectedVehicle = signal<Vehicle | null>(null);
  loading = signal(true);

  searchQuery = '';
  filterStatus: string = 'all';
  filterGroup: string = 'all';

  viewMode: 'map' | 'list' | 'split' = 'split';
  mapStyle: 'streets' | 'satellite' | 'terrain' = 'streets';

  refreshInterval: any;

  stats = signal({
    total: 0,
    online: 0,
    moving: 0,
    stopped: 0,
    offline: 0
  });

  constructor(
    private companyService: CompanyService,
    private vehicleService: VehicleService,
    private gpsService: GpsService,
    private monitoringService: MonitoringService
  ) {}

  async ngOnInit() {
    await this.loadData();
    this.startAutoRefresh();
  }

  ngAfterViewInit() {
    this.initializeMap();
  }

  ngOnDestroy() {
    if (this.refreshInterval) {
      clearInterval(this.refreshInterval);
    }
    if (this.map) {
      this.map.remove();
    }
  }

  initializeMap() {
    setTimeout(() => {
      if (!this.map) {
        this.map = L.map('tracking-map').setView([48.8566, 2.3522], 6);

        const mapUrls: Record<string, string> = {
          streets: 'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png',
          satellite: 'https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}',
          terrain: 'https://{s}.tile.opentopomap.org/{z}/{x}/{y}.png'
        };

        L.tileLayer(mapUrls[this.mapStyle], {
          attribution: '© OpenStreetMap contributors',
          maxZoom: 19
        }).addTo(this.map);

        this.updateVehicleMarkers();
      }
    }, 100);
  }

  async loadData() {
    const company = this.companyService.currentCompany();
    if (!company) return;

    try {
      this.loading.set(true);
      const vehicles = await this.vehicleService.getVehicles(company.id);

      const vehiclesWithLocation = await Promise.all(
        vehicles.map(async (vehicle) => {
          if (vehicle.gps_device_id) {
            const location = await this.gpsService.getLatestTracking(vehicle.id);
            return {
              ...vehicle,
              currentLocation: location ? {
                lat: location.latitude,
                lng: location.longitude
              } : undefined,
              currentSpeed: location?.speed || 0,
              isOnline: location ? this.isRecentLocation(location.timestamp) : false
            };
          }
          return { ...vehicle, isOnline: false };
        })
      );

      this.vehicles.set(vehiclesWithLocation);
      this.applyFilters();
      this.updateStats();
      this.updateVehicleMarkers();
    } catch (error) {
      console.error('Error loading monitoring data:', error);
    } finally {
      this.loading.set(false);
    }
  }

  isRecentLocation(timestamp: string): boolean {
    const locationTime = new Date(timestamp).getTime();
    const now = Date.now();
    return (now - locationTime) < 5 * 60 * 1000;
  }

  updateVehicleMarkers() {
    if (!this.map) return;

    this.vehicleMarkers.forEach(marker => marker.remove());
    this.vehicleMarkers.clear();

    const bounds: L.LatLngBounds[] = [];

    this.filteredVehicles().forEach(vehicle => {
      if (vehicle.currentLocation) {
        const isMoving = (vehicle.currentSpeed || 0) > 5;
        const icon = this.createVehicleIcon(vehicle, isMoving);

        const marker = L.marker([vehicle.currentLocation.lat, vehicle.currentLocation.lng], { icon })
          .bindPopup(this.createPopupContent(vehicle))
          .on('click', () => this.selectVehicle(vehicle));

        marker.addTo(this.map!);
        this.vehicleMarkers.set(vehicle.id, marker);

        bounds.push(L.latLngBounds([vehicle.currentLocation.lat, vehicle.currentLocation.lng], [vehicle.currentLocation.lat, vehicle.currentLocation.lng]));
      }
    });

    if (bounds.length > 0 && !this.selectedVehicle()) {
      const group = new L.FeatureGroup(Array.from(this.vehicleMarkers.values()));
      this.map.fitBounds(group.getBounds().pad(0.1));
    }
  }

  createVehicleIcon(vehicle: Vehicle, isMoving: boolean): L.DivIcon {
    const color = isMoving ? '#48bb78' : '#f56565';
    const iconHtml = `
      <div class="vehicle-marker ${isMoving ? 'moving' : 'stopped'}" style="background-color: ${color};">
        <div class="marker-icon">🚗</div>
      </div>
    `;

    return L.divIcon({
      html: iconHtml,
      className: 'custom-vehicle-marker',
      iconSize: [40, 40],
      iconAnchor: [20, 20]
    });
  }

  createPopupContent(vehicle: Vehicle): string {
    return `
      <div class="vehicle-popup">
        <h3>${vehicle.brand} ${vehicle.model}</h3>
        <p><strong>Immatriculation:</strong> ${vehicle.registration_number}</p>
        <p><strong>Vitesse:</strong> ${vehicle.currentSpeed || 0} km/h</p>
        <p><strong>Statut:</strong> ${vehicle.isOnline ? 'En ligne' : 'Hors ligne'}</p>
      </div>
    `;
  }

  selectVehicle(vehicle: Vehicle) {
    this.selectedVehicle.set(vehicle);

    if (this.map && vehicle.currentLocation) {
      this.map.setView([vehicle.currentLocation.lat, vehicle.currentLocation.lng], 15);
    }
  }

  applyFilters() {
    let filtered = this.vehicles();

    if (this.searchQuery) {
      const query = this.searchQuery.toLowerCase();
      filtered = filtered.filter(v =>
        v.registration_number?.toLowerCase().includes(query) ||
        v.brand?.toLowerCase().includes(query) ||
        v.model?.toLowerCase().includes(query)
      );
    }

    if (this.filterStatus !== 'all') {
      if (this.filterStatus === 'online') {
        filtered = filtered.filter(v => v.isOnline);
      } else if (this.filterStatus === 'moving') {
        filtered = filtered.filter(v => v.isOnline && (v.currentSpeed || 0) > 5);
      } else if (this.filterStatus === 'stopped') {
        filtered = filtered.filter(v => v.isOnline && (v.currentSpeed || 0) <= 5);
      } else if (this.filterStatus === 'offline') {
        filtered = filtered.filter(v => !v.isOnline);
      }
    }

    this.filteredVehicles.set(filtered);
    this.updateVehicleMarkers();
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
    const vehicles = this.vehicles();
    const online = vehicles.filter(v => v.isOnline);

    this.stats.set({
      total: vehicles.length,
      online: online.length,
      moving: online.filter(v => (v.currentSpeed || 0) > 5).length,
      stopped: online.filter(v => (v.currentSpeed || 0) <= 5).length,
      offline: vehicles.length - online.length
    });
  }

  changeMapStyle(style: 'streets' | 'satellite' | 'terrain') {
    this.mapStyle = style;
    if (this.map) {
      this.map.remove();
      this.map = null;
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
    this.refreshInterval = setInterval(() => {
      this.loadData();
    }, 30000);
  }

  async refresh() {
    await this.loadData();
  }

  getVehicleStatusClass(vehicle: Vehicle): string {
    if (!vehicle.isOnline) return 'status-offline';
    if ((vehicle.currentSpeed || 0) > 5) return 'status-moving';
    return 'status-stopped';
  }

  getVehicleStatusLabel(vehicle: Vehicle): string {
    if (!vehicle.isOnline) return 'Hors ligne';
    if ((vehicle.currentSpeed || 0) > 5) return 'En mouvement';
    return 'Arrêté';
  }
}
