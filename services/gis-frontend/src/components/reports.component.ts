import { Component, OnInit, ViewChild, ElementRef, NgZone, ChangeDetectorRef, ApplicationRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService, FuelRecordsResult, FuelRecord } from '../services/api.service';
import { GeocodingService } from '../services/geocoding.service';
import { AppLayoutComponent } from './shared/app-layout.component';
import { ButtonComponent, CardComponent, DataTableComponent } from './shared/ui';
import { Chart, ChartConfiguration, registerables } from 'chart.js';

Chart.register(...registerables);

@Component({
  selector: 'app-reports',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent, ButtonComponent, CardComponent, DataTableComponent],
  templateUrl: './reports.component.html',
  styleUrls: ['./reports.component.css']
})
export class ReportsComponent implements OnInit {
  @ViewChild('chartCanvas') chartCanvas?: ElementRef<HTMLCanvasElement>;

  templates = [
    {
      id: '1',
      name: 'Rapport de trajets',
      type: 'trips',
      icon: '🛣️',
      description: 'Analyse détaillée des trajets effectués'
    },
    {
      id: '2',
      name: 'Rapport de carburant',
      type: 'fuel',
      icon: '⛽',
      description: 'Consommation et remplissages'
    },
    {
      id: '3',
      name: 'Rapport de vitesse',
      type: 'speed',
      icon: '🏎️',
      description: 'Analyse des vitesses et excès'
    },
    {
      id: '4',
      name: 'Rapport des arrêts',
      type: 'stops',
      icon: '🅿️',
      description: 'Temps et lieux d\'arrêt'
    },
    {
      id: '5',
      name: 'Rapport de distance',
      type: 'distance',
      icon: '📏',
      description: 'Kilométrage parcouru'
    },
    {
      id: '6',
      name: 'Rapport de coûts',
      type: 'costs',
      icon: '💰',
      description: 'Analyse des dépenses véhicules'
    },
    {
      id: '7',
      name: 'Rapport de maintenance',
      type: 'maintenance',
      icon: '🔧',
      description: 'Historique des maintenances'
    }
  ];

  vehicles: any[] = [];
  selectedTemplate: any = null;
  selectedTemplateId = '';
  selectedVehicleId = '';
  selectedVehicleIds: string[] = [];

  // Period
  periods = [
    { value: 'today', label: 'Today' },
    { value: 'yesterday', label: 'Yesterday' },
    { value: 'week', label: 'Week' },
    { value: 'month', label: 'Month' }
  ];
  selectedPeriod = 'week';
  
  // Interval
  intervalType = 'previous';
  intervalValue = 1;
  intervalUnit = 'months';
  includeCurrent = false;

  fromDate = '';
  toDate = '';

  reportGenerated = false;
  activeTab = 'chart';
  loading = false;

  // Accordion sections
  expandedSections: { [key: string]: boolean } = {
    templates: false,
    result: true
  };

  // Chart legend
  chartLines = {
    all: true,
    speed: true,
    fuel: true,
    consumption: true
  };

  // Pagination
  currentPage = 1;
  pageSize = 50;
  
  chartData: any[] = [];
  tableData: any[] = [];
  statisticsData: any = {};

  private chart?: Chart;

  constructor(
    private router: Router,
    private apiService: ApiService,
    private geocodingService: GeocodingService,
    private ngZone: NgZone,
    private cdr: ChangeDetectorRef,
    private appRef: ApplicationRef
  ) {}

  ngOnInit() {
    if (!this.apiService.isAuthenticated()) {
      this.router.navigate(['/login']);
      return;
    }

    this.ngZone.run(() => {
      this.loadData();
      this.initializeDates();
    });
  }

  loadData() {
    this.apiService.getVehicles().subscribe({
      next: (vehicles) => this.vehicles = vehicles,
      error: (err) => console.error('Error loading vehicles:', err)
    });
  }

  initializeDates() {
    this.selectPeriod('today');
  }

  selectPeriod(period: string) {
    this.selectedPeriod = period;
    const now = new Date();
    const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());

    switch (period) {
      case 'today':
        this.fromDate = this.toDateTime(today);
        this.toDate = this.toDateTime(now);
        break;
      case 'yesterday':
        const yesterday = new Date(today);
        yesterday.setDate(today.getDate() - 1);
        this.fromDate = this.toDateTime(yesterday);
        this.toDate = this.toDateTime(today);
        break;
      case 'week':
        const weekAgo = new Date(today);
        weekAgo.setDate(today.getDate() - 7);
        this.fromDate = this.toDateTime(weekAgo);
        this.toDate = this.toDateTime(now);
        break;
      case 'month':
        const monthAgo = new Date(today);
        monthAgo.setMonth(today.getMonth() - 1);
        this.fromDate = this.toDateTime(monthAgo);
        this.toDate = this.toDateTime(now);
        break;
    }
  }

  toDateTime(date: Date): string {
    return date.toISOString().slice(0, 16);
  }

  selectTemplate(template: any) {
    this.selectedTemplate = template;
    this.selectedTemplateId = template.id;
  }

  onTemplateChange() {
    this.selectedTemplate = this.templates.find(t => t.id === this.selectedTemplateId) || null;
  }

  toggleSection(section: string) {
    this.expandedSections[section] = !this.expandedSections[section];
  }

  toggleAllLines() {
    const allChecked = this.chartLines.all;
    this.chartLines.speed = allChecked;
    this.chartLines.fuel = allChecked;
    this.chartLines.consumption = allChecked;
  }

  clearFilters() {
    this.selectedTemplateId = '';
    this.selectedTemplate = null;
    this.selectedVehicleId = '';
    this.selectedPeriod = 'week';
    this.intervalValue = 1;
    this.intervalUnit = 'months';
    this.includeCurrent = false;
    this.reportGenerated = false;
  }

  // Pagination getters
  get totalPages(): number {
    return Math.ceil(this.tableData.length / this.pageSize) || 1;
  }

  get startItem(): number {
    return (this.currentPage - 1) * this.pageSize + 1;
  }

  get endItem(): number {
    return Math.min(this.currentPage * this.pageSize, this.tableData.length);
  }

  get paginatedData(): any[] {
    const start = (this.currentPage - 1) * this.pageSize;
    return this.tableData.slice(start, start + this.pageSize);
  }

  goToPage(page: number) {
    if (page >= 1 && page <= this.totalPages) {
      this.currentPage = page;
    }
  }

  onPageSizeChange() {
    this.currentPage = 1;
  }

  executeReport() {
    console.log('executeReport called', { 
      selectedTemplate: this.selectedTemplate, 
      selectedVehicleId: this.selectedVehicleId,
      fromDate: this.fromDate,
      toDate: this.toDate
    });

    if (!this.selectedTemplate) {
      console.warn('No template selected');
      return;
    }

    if (!this.selectedVehicleId && this.selectedTemplate.type === 'fuel') {
      console.warn('No vehicle selected for fuel report');
      return;
    }

    this.loading = true;
    this.expandedSections['result'] = true;

    const startDate = this.fromDate ? new Date(this.fromDate) : undefined;
    const endDate = this.toDate ? new Date(this.toDate) : undefined;
    const vehicleId = this.selectedVehicleId ? parseInt(this.selectedVehicleId) : undefined;

    // All report types use vehicle history API
    this.executeVehicleReport(vehicleId, startDate, endDate);
  }

  executeVehicleReport(vehicleId?: number, startDate?: Date, endDate?: Date) {
    if (!vehicleId) {
      this.tableData = [];
      this.chartData = [];
      this.statisticsData = { 'Message': 'Veuillez sélectionner un véhicule' };
      this.reportGenerated = true;
      this.loading = false;
      return;
    }

    this.apiService.getVehicleHistory(vehicleId, startDate, endDate).subscribe({
      next: (result) => {
        this.ngZone.run(() => {
          this.processVehicleData(result);
          this.reportGenerated = true;
          this.loading = false;
          this.activeTab = 'chart';
          this.currentPage = 1;
          this.cdr.detectChanges();
          this.appRef.tick();
          setTimeout(() => this.createChart(), 100);
        });
      },
      error: (err) => {
        this.ngZone.run(() => {
          console.error('Error loading vehicle history:', err);
          this.tableData = [];
          this.chartData = [];
          this.statisticsData = { 'Erreur': 'Impossible de charger les données' };
          this.reportGenerated = true;
          this.loading = false;
          this.cdr.detectChanges();
          this.appRef.tick();
        });
      }
    });
  }

  processVehicleData(positions: any[]) {
    const type = this.selectedTemplate?.type || 'fuel';
    const sorted = positions.sort((a: any, b: any) => 
      new Date(a.recordedAt).getTime() - new Date(b.recordedAt).getTime()
    );

    switch (type) {
      case 'fuel':
        this.processFuelReport(sorted);
        break;
      case 'speed':
        this.processSpeedReport(sorted);
        break;
      case 'stops':
        this.processStopsReport(sorted);
        break;
      case 'distance':
        this.processDistanceReport(sorted);
        break;
      default:
        this.processFuelReport(sorted);
    }
  }

  processFuelReport(positions: any[]) {
    // Filter to show only significant fuel changes or periodic readings
    let lastFuel = -1;
    const significantPositions = positions.filter((pos: any, index: number) => {
      const fuel = pos.fuelRaw || 0;
      const isFirst = index === 0;
      const isLast = index === positions.length - 1;
      const hasSignificantChange = Math.abs(fuel - lastFuel) >= 2;
      const isEveryNth = index % 10 === 0; // Show every 10th position
      
      if (isFirst || isLast || hasSignificantChange || isEveryNth) {
        lastFuel = fuel;
        return true;
      }
      return false;
    });

    this.tableData = significantPositions.map((pos: any, index: number) => {
      const prevPos = index > 0 ? significantPositions[index - 1] : null;
      const fuelChange = prevPos ? (pos.fuelRaw || 0) - (prevPos.fuelRaw || 0) : 0;
      
      // Determine event type based on fuel change
      let eventType = 'Lecture';
      let isAnomaly = false;
      if (fuelChange > 5) {
        eventType = '⛽ Remplissage';
      } else if (fuelChange < -15) {
        eventType = '⚠️ Chute importante';
        isAnomaly = true;
      } else if (fuelChange < -5) {
        eventType = '📉 Consommation';
      } else if (fuelChange > 0) {
        eventType = '📈 Augmentation';
      }

      // Use real odometer if available, otherwise calculate distance
      let odometer = '-';
      if (pos.odometerKm && pos.odometerKm > 0) {
        odometer = `${pos.odometerKm.toLocaleString('fr-FR')} km`;
      } else if (prevPos) {
        const d = this.haversineDistance(prevPos.latitude, prevPos.longitude, pos.latitude, pos.longitude);
        if (d > 0.1) {
          odometer = `+${d.toFixed(1)} km`;
        }
      }

      // Use address from API if available, otherwise show coordinates
      const location = pos.address || `${pos.latitude.toFixed(4)}°, ${pos.longitude.toFixed(4)}°`;

      return {
        time: new Date(pos.recordedAt).toLocaleString('fr-FR', {
          day: '2-digit', month: '2-digit', year: 'numeric',
          hour: '2-digit', minute: '2-digit'
        }),
        fuelPercent: `${pos.fuelRaw || 0}%`,
        fuelChange: fuelChange !== 0 ? `${fuelChange > 0 ? '+' : ''}${fuelChange.toFixed(0)}%` : '-',
        eventType,
        location,
        latitude: pos.latitude,
        longitude: pos.longitude,
        speed: pos.speedKph ? `${pos.speedKph.toFixed(0)} km/h` : '0 km/h',
        odometer,
        isAnomaly
      };
    });

    // Fetch addresses asynchronously for positions without address
    this.enrichWithAddresses();

    // Chart data - all positions for smooth graph
    this.chartData = positions.map((pos: any) => ({
      label: new Date(pos.recordedAt).toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' }),
      value: pos.fuelRaw || 0
    }));

    // Statistics
    const fuelValues = positions.map((p: any) => p.fuelRaw || 0).filter((f: number) => f > 0);
    const avgFuel = fuelValues.length > 0 ? fuelValues.reduce((a: number, b: number) => a + b, 0) / fuelValues.length : 0;
    const refuels = this.tableData.filter((r: any) => r.eventType.includes('Remplissage')).length;
    const anomalies = this.tableData.filter((r: any) => r.isAnomaly).length;

    this.statisticsData = {
      'Enregistrements': positions.length.toString(),
      'Niveau moyen': `${avgFuel.toFixed(1)}%`,
      'Niveau min': fuelValues.length > 0 ? `${Math.min(...fuelValues)}%` : 'N/A',
      'Niveau max': fuelValues.length > 0 ? `${Math.max(...fuelValues)}%` : 'N/A',
      'Remplissages': refuels.toString(),
      'Alertes': anomalies.toString()
    };
  }

  processSpeedReport(positions: any[]) {
    const speedData = positions.filter((p: any) => p.speedKph > 0);
    
    this.tableData = speedData.map((pos: any) => ({
      time: new Date(pos.recordedAt).toLocaleString('fr-FR', {
        day: '2-digit', month: '2-digit', year: 'numeric',
        hour: '2-digit', minute: '2-digit'
      }),
      speed: `${(pos.speedKph || 0).toFixed(1)} km/h`,
      location: `${pos.latitude.toFixed(5)}, ${pos.longitude.toFixed(5)}`,
      isAnomaly: pos.speedKph > 90
    }));

    this.chartData = speedData.map((pos: any) => ({
      label: new Date(pos.recordedAt).toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' }),
      value: pos.speedKph || 0
    }));

    const speeds = speedData.map((p: any) => p.speedKph || 0);
    const avgSpeed = speeds.length > 0 ? speeds.reduce((a: number, b: number) => a + b, 0) / speeds.length : 0;
    const violations = speeds.filter((s: number) => s > 90).length;

    this.statisticsData = {
      'Total points': positions.length.toString(),
      'Vitesse moyenne': `${avgSpeed.toFixed(1)} km/h`,
      'Vitesse max': speeds.length > 0 ? `${Math.max(...speeds).toFixed(1)} km/h` : 'N/A',
      'Excès (>90 km/h)': violations.toString()
    };
  }

  processStopsReport(positions: any[]) {
    // Detect stops (speed = 0 or very low)
    const stops: any[] = [];
    let currentStop: any = null;

    positions.forEach((pos: any, index: number) => {
      const isStop = (pos.speedKph || 0) < 2;
      
      if (isStop && !currentStop) {
        currentStop = { start: pos, positions: [pos] };
      } else if (isStop && currentStop) {
        currentStop.positions.push(pos);
      } else if (!isStop && currentStop) {
        currentStop.end = positions[index - 1];
        stops.push(currentStop);
        currentStop = null;
      }
    });

    if (currentStop) {
      currentStop.end = positions[positions.length - 1];
      stops.push(currentStop);
    }

    this.tableData = stops.slice(0, 50).map((stop: any) => {
      const duration = (new Date(stop.end.recordedAt).getTime() - new Date(stop.start.recordedAt).getTime()) / 60000;
      return {
        time: new Date(stop.start.recordedAt).toLocaleString('fr-FR', {
          day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit'
        }),
        duration: `${Math.round(duration)} min`,
        location: `${stop.start.latitude.toFixed(5)}, ${stop.start.longitude.toFixed(5)}`,
        type: 'Arrêt'
      };
    });

    this.chartData = stops.slice(0, 20).map((stop: any, i: number) => ({
      label: `Arrêt ${i + 1}`,
      value: Math.round((new Date(stop.end.recordedAt).getTime() - new Date(stop.start.recordedAt).getTime()) / 60000)
    }));

    const totalDuration = stops.reduce((sum: number, s: any) => 
      sum + (new Date(s.end.recordedAt).getTime() - new Date(s.start.recordedAt).getTime()), 0) / 60000;

    this.statisticsData = {
      'Nombre d\'arrêts': stops.length.toString(),
      'Durée totale': `${Math.round(totalDuration)} min`,
      'Durée moyenne': stops.length > 0 ? `${Math.round(totalDuration / stops.length)} min` : 'N/A'
    };
  }

  processDistanceReport(positions: any[]) {
    // Calculate distance between points using Haversine
    let totalDistance = 0;
    const segments: any[] = [];

    for (let i = 1; i < positions.length; i++) {
      const prev = positions[i - 1];
      const curr = positions[i];
      const dist = this.haversineDistance(prev.latitude, prev.longitude, curr.latitude, curr.longitude);
      totalDistance += dist;
      
      if (i % 10 === 0) {
        segments.push({
          time: new Date(curr.recordedAt).toLocaleString('fr-FR', {
            day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit'
          }),
          distance: `${totalDistance.toFixed(2)} km`,
          speed: `${(curr.speedKph || 0).toFixed(1)} km/h`,
          location: `${curr.latitude.toFixed(5)}, ${curr.longitude.toFixed(5)}`
        });
      }
    }

    this.tableData = segments;
    this.chartData = segments.map((s: any, i: number) => ({
      label: s.time.split(' ')[1] || `Pt ${i}`,
      value: parseFloat(s.distance)
    }));

    this.statisticsData = {
      'Distance totale': `${totalDistance.toFixed(2)} km`,
      'Points GPS': positions.length.toString()
    };
  }

  haversineDistance(lat1: number, lon1: number, lat2: number, lon2: number): number {
    const R = 6371;
    const dLat = (lat2 - lat1) * Math.PI / 180;
    const dLon = (lon2 - lon1) * Math.PI / 180;
    const a = Math.sin(dLat / 2) * Math.sin(dLat / 2) +
              Math.cos(lat1 * Math.PI / 180) * Math.cos(lat2 * Math.PI / 180) *
              Math.sin(dLon / 2) * Math.sin(dLon / 2);
    const c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
    return R * c;
  }

  createChart() {
    if (!this.chartCanvas) return;

    if (this.chart) {
      this.chart.destroy();
    }

    const ctx = this.chartCanvas.nativeElement.getContext('2d');
    if (!ctx) return;

    const type = this.selectedTemplate?.type || 'fuel';
    let config: ChartConfiguration;

    if (type === 'stops') {
      config = {
        type: 'bar',
        data: {
          labels: this.chartData.map(d => d.label),
          datasets: [{
            label: 'Durée (minutes)',
            data: this.chartData.map(d => d.value),
            backgroundColor: [
              'rgba(14, 165, 233, 0.8)',
              'rgba(34, 197, 94, 0.8)',
              'rgba(245, 158, 11, 0.8)',
              'rgba(168, 85, 247, 0.8)',
              'rgba(107, 114, 128, 0.8)'
            ],
            borderWidth: 0
          }]
        },
        options: this.getChartOptions()
      };
    } else if (type === 'distance') {
      config = {
        type: 'bar',
        data: {
          labels: this.chartData.map(d => d.label),
          datasets: [{
            label: 'Distance (km)',
            data: this.chartData.map(d => d.value),
            backgroundColor: 'rgba(14, 165, 233, 0.8)',
            borderColor: 'rgb(14, 165, 233)',
            borderWidth: 1
          }]
        },
        options: this.getChartOptions()
      };
    } else {
      config = {
        type: 'line',
        data: {
          labels: this.chartData.map(d => d.label),
          datasets: [{
            label: this.selectedTemplate.name,
            data: this.chartData.map(d => d.value),
            borderColor: 'rgb(14, 165, 233)',
            backgroundColor: 'rgba(14, 165, 233, 0.1)',
            tension: 0.4,
            fill: true
          }]
        },
        options: this.getChartOptions()
      };
    }

    this.chart = new Chart(ctx, config);
  }

  getChartOptions() {
    return {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: {
          position: 'top' as const,
          labels: {
            color: '#94a3b8',
            font: { size: 12 }
          }
        }
      },
      scales: {
        x: {
          grid: { color: '#1e293b' },
          ticks: { color: '#94a3b8', font: { size: 11 } }
        },
        y: {
          grid: { color: '#1e293b' },
          ticks: { color: '#94a3b8', font: { size: 11 } }
        }
      }
    };
  }

  setActiveTab(tab: string) {
    this.activeTab = tab;
    if (tab === 'chart') {
      setTimeout(() => this.createChart(), 100);
    }
  }

  exportReport(format: string) {
    alert(`Export ${format.toUpperCase()} - fonctionnalité à venir`);
  }

  getStatKeys(): string[] {
    return Object.keys(this.statisticsData);
  }

  /**
   * Enrich table data with addresses from Nominatim for positions without address
   */
  async enrichWithAddresses() {
    // Find positions that need geocoding (no address yet, only coordinates)
    const positionsToGeocode = this.tableData.filter(
      (row: any) => row.latitude && row.longitude && row.location.includes('°')
    );

    if (positionsToGeocode.length === 0) return;

    // Batch geocode coordinates
    const coordinates = positionsToGeocode.map((row: any) => ({
      lat: row.latitude,
      lon: row.longitude
    }));

    try {
      const addressMap = await this.geocodingService.batchReverseGeocode(coordinates);
      
      // Update table data with addresses
      this.ngZone.run(() => {
        this.tableData = this.tableData.map((row: any) => {
          if (row.latitude && row.longitude && row.location.includes('°')) {
            const key = `${row.latitude.toFixed(4)},${row.longitude.toFixed(4)}`;
            const address = addressMap.get(key);
            if (address) {
              return { ...row, location: address };
            }
          }
          return row;
        });
        this.cdr.detectChanges();
      });
    } catch (error) {
      console.error('Error enriching addresses:', error);
    }
  }

  Object = Object;
}
