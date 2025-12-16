import { Component, OnInit, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MockDataService } from '../services/mock-data.service';
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
    private dataService: MockDataService
  ) {}

  ngOnInit() {
    if (!this.dataService.isAuthenticated()) {
      this.router.navigate(['/login']);
      return;
    }

    this.loadData();
    this.initializeDates();
  }

  loadData() {
    const company = this.dataService.getCurrentCompany();
    if (!company) return;
    this.vehicles = this.dataService.getVehiclesByCompany(company.id);
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
    if (!this.selectedTemplate) {
      alert('Veuillez sélectionner un template de rapport');
      return;
    }

    this.loading = true;
    this.expandedSections['result'] = true;

    setTimeout(() => {
      this.reportGenerated = true;
      this.generateFuelData();
      this.loading = false;
      this.activeTab = 'chart';
      this.currentPage = 1;

      setTimeout(() => {
        this.createChart();
      }, 100);
    }, 1500);
  }

  generateMockData() {
    const type = this.selectedTemplate.type;

    switch (type) {
      case 'fuel':
        this.chartData = [
          { label: '08:00', value: 85 },
          { label: '09:00', value: 82 },
          { label: '10:00', value: 78 },
          { label: '11:00', value: 75 },
          { label: '12:00', value: 72 },
          { label: '13:00', value: 100 },
          { label: '14:00', value: 95 },
          { label: '15:00', value: 90 },
          { label: '16:00', value: 85 },
          { label: '17:00', value: 78 }
        ];
        this.tableData = [
          { time: '08:00', level: '85%', consumption: '12.5 L', status: 'Normal' },
          { time: '10:00', level: '78%', consumption: '18.2 L', status: 'Élevée' },
          { time: '12:30', level: '72%', consumption: '15.8 L', status: 'Normal' },
          { time: '13:00', level: '100%', consumption: '45 L', status: 'Remplissage' },
          { time: '16:00', level: '85%', consumption: '20.5 L', status: 'Élevée' }
        ];
        this.statisticsData = {
          totalConsumption: '112.0 L',
          avgConsumption: '8.5 L/100km',
          totalRefills: '1',
          refillVolume: '45 L'
        };
        break;

      case 'speed':
        this.chartData = [
          { label: '08:00', value: 55 },
          { label: '09:00', value: 75 },
          { label: '10:00', value: 95 },
          { label: '11:00', value: 68 },
          { label: '12:00', value: 50 },
          { label: '14:00', value: 65 },
          { label: '15:00', value: 125 },
          { label: '16:00', value: 85 },
          { label: '17:00', value: 70 }
        ];
        this.tableData = [
          { time: '10:15', speed: '95 km/h', limit: '90 km/h', excess: '5 km/h', duration: '5 min' },
          { time: '15:30', speed: '125 km/h', limit: '90 km/h', excess: '35 km/h', duration: '2 min' },
          { time: '16:45', speed: '105 km/h', limit: '90 km/h', excess: '15 km/h', duration: '3 min' }
        ];
        this.statisticsData = {
          avgSpeed: '72 km/h',
          maxSpeed: '125 km/h',
          violations: '3',
          totalExcessTime: '10 min'
        };
        break;

      case 'stops':
        this.chartData = [
          { label: 'Dépôt', value: 150 },
          { label: 'Clients', value: 95 },
          { label: 'Pause', value: 60 },
          { label: 'Ravitaillement', value: 10 },
          { label: 'Autres', value: 35 }
        ];
        this.tableData = [
          { time: '08:15', location: 'Dépôt Principal', duration: '45 min', type: 'Départ' },
          { time: '10:30', location: 'Client ABC', duration: '15 min', type: 'Livraison' },
          { time: '12:00', location: 'Pause déjeuner', duration: '60 min', type: 'Pause' },
          { time: '14:20', location: 'Client XYZ', duration: '25 min', type: 'Livraison' },
          { time: '16:45', location: 'Station service', duration: '10 min', type: 'Carburant' }
        ];
        this.statisticsData = {
          totalStops: '6',
          totalDuration: '5h 30min',
          avgStopDuration: '55 min',
          longestStop: '2h 30min'
        };
        break;

      case 'distance':
        this.chartData = [
          { label: 'Lundi', value: 290 },
          { label: 'Mardi', value: 315 },
          { label: 'Mercredi', value: 275 },
          { label: 'Jeudi', value: 340 },
          { label: 'Vendredi', value: 295 },
          { label: 'Samedi', value: 180 },
          { label: 'Dimanche', value: 95 }
        ];
        this.tableData = [
          { date: 'Lundi 11/12', distance: '290 km', trips: '8', avgSpeed: '65 km/h' },
          { date: 'Mardi 12/12', distance: '315 km', trips: '9', avgSpeed: '72 km/h' },
          { date: 'Mercredi 13/12', distance: '275 km', trips: '7', avgSpeed: '68 km/h' },
          { date: 'Jeudi 14/12', distance: '340 km', trips: '10', avgSpeed: '75 km/h' },
          { date: 'Vendredi 15/12', distance: '295 km', trips: '8', avgSpeed: '70 km/h' }
        ];
        this.statisticsData = {
          totalDistance: '1,790 km',
          avgDaily: '255 km',
          maxDaily: '340 km',
          totalTrips: '42'
        };
        break;

      default:
        this.chartData = [
          { label: '08:00', value: 75 },
          { label: '10:00', value: 72 },
          { label: '12:00', value: 68 },
          { label: '14:00', value: 80 },
          { label: '16:00', value: 78 },
          { label: '18:00', value: 82 }
        ];
        this.tableData = [
          { time: '10:30', event: 'Freinage brusque', count: '8', severity: 'Moyen' },
          { time: '11:15', event: 'Accélération rapide', count: '12', severity: 'Élevé' },
          { time: '14:45', event: 'Virage serré', count: '5', severity: 'Faible' }
        ];
        this.statisticsData = {
          score: '72/100',
          harshBraking: '8',
          rapidAccel: '12',
          sharpTurns: '5'
        };
    }
  }

  generateFuelData() {
    // Generate realistic fuel filling data
    const locations = [
      'DN2, Bălcăuți, Suceava, Romania',
      'DN2, Siret, Suceava, Romania, 3.71 km from Siret',
      'DN2, Cordun, Neamț, Romania, 3.87 km from Roman',
      'DN2, Fântâna Mare, Suceava, Romania, 1.04 km from Fântâna Mare',
      'DN2, Sascut, Bacău, Romania, 1.97 km from Schineni'
    ];
    
    this.tableData = [];
    const baseDate = new Date('2024-12-01T00:00:00');
    
    for (let i = 0; i < 20; i++) {
      const date = new Date(baseDate.getTime() + i * 3 * 60 * 60 * 1000);
      const filled = Math.floor(Math.random() * 30) + 10;
      const initialLevel = Math.floor(Math.random() * 300) + 400;
      
      this.tableData.push({
        location: locations[i % locations.length],
        filled: `${filled} l`,
        initialLevel: `${initialLevel} l`,
        finalLevel: `${initialLevel + filled} l`,
        sensor: 'FLS',
        driver: 'Alex Black',
        time: date.toLocaleString('fr-FR', { 
          year: 'numeric', month: '2-digit', day: '2-digit',
          hour: '2-digit', minute: '2-digit'
        })
      });
    }

    // Chart data - fuel level over time
    this.chartData = [];
    for (let i = 0; i < 24; i++) {
      const hour = 14 + Math.floor(i / 2);
      const minute = (i % 2) * 30;
      const fuelLevel = 50 + Math.sin(i * 0.5) * 20 + Math.random() * 10;
      this.chartData.push({
        label: `${hour}:${minute.toString().padStart(2, '0')}`,
        value: Math.round(fuelLevel)
      });
    }

    this.statisticsData = {
      totalFilled: '298 l',
      avgFilling: '22.3 l',
      totalFillings: '15',
      avgInterval: '4.2 h'
    };
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

  Object = Object;
}
