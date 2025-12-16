import { Component, OnInit, OnDestroy, AfterViewInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MockDataService } from '../services/mock-data.service';
import { Vehicle } from '../models/types';
import { AppLayoutComponent } from './shared/app-layout.component';
import { StatCardComponent, ButtonComponent, CardComponent } from './shared/ui';
import * as L from 'leaflet';

@Component({
  selector: 'app-monitoring',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent, StatCardComponent, ButtonComponent, CardComponent],
  templateUrl: './monitoring.component.html',
  styleUrls: ['./monitoring.component.css']
})
export class MonitoringComponent implements OnInit, AfterViewInit, OnDestroy {
  map: L.Map | null = null;
  vehicleMarkers = new Map<string, L.Marker>();

  vehicles: Vehicle[] = [];
  filteredVehicles: Vehicle[] = [];
  selectedVehicle: Vehicle | null = null;
  loading = true;

  searchQuery = '';
  filterStatus: string = 'all';

  viewMode: 'map' | 'list' | 'split' = 'split';
  mapStyle: 'streets' | 'satellite' | 'terrain' = 'streets';

  refreshInterval: any;

  stats = {
    total: 0,
    online: 0,
    moving: 0,
    stopped: 0,
    offline: 0
  };

  constructor(
    private router: Router,
    private dataService: MockDataService
  ) {}

  ngOnInit() {
    if (!this.dataService.isAuthenticated()) {
      this.router.navigate(['/login']);
      return;
    }

    this.loadData();
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

  loadData() {
    const company = this.dataService.getCurrentCompany();
    if (!company) return;

    this.loading = true;

    this.vehicles = this.dataService.getVehiclesByCompany(company.id).map(v => ({
      ...v,
      registration_number: v.plate,
      currentLocation: v.hasGPS ? this.generateRandomLocation() : undefined,
      currentSpeed: v.hasGPS ? Math.floor(Math.random() * 100) : 0,
      isOnline: v.hasGPS
    }));

    this.applyFilters();
    this.updateStats();
    this.updateVehicleMarkers();
    this.loading = false;
  }

  generateRandomLocation() {
    return {
      lat: 48.8566 + (Math.random() - 0.5) * 0.5,
      lng: 2.3522 + (Math.random() - 0.5) * 0.5
    };
  }

  updateVehicleMarkers() {
    if (!this.map) return;

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
    this.selectedVehicle = vehicle;

    if (this.map && vehicle.currentLocation) {
      this.map.setView([vehicle.currentLocation.lat, vehicle.currentLocation.lng], 15);
    }
  }

  applyFilters() {
    let filtered = this.vehicles;

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

    this.filteredVehicles = filtered;
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

  navigate(path: string) {
    this.router.navigate([path]);
  }
}
