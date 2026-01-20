import { Component, OnInit, ViewChild, ElementRef, NgZone, ChangeDetectorRef, ApplicationRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService, FuelRecordsResult, FuelRecord, DailyActivityReport, ActivitySegment, MileageReport, DailyMileage, MonthlyFleetReport } from '../services/api.service';
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
  @ViewChild('kmBarChart') kmBarChartRef?: ElementRef<HTMLCanvasElement>;
  @ViewChild('fuelPieChart') fuelPieChartRef?: ElementRef<HTMLCanvasElement>;
  @ViewChild('maintenanceAreaChart') maintenanceAreaChartRef?: ElementRef<HTMLCanvasElement>;
  
  // Chart instances for monthly report
  private kmBarChart?: Chart;
  private fuelPieChart?: Chart;
  private maintenanceAreaChart?: Chart;
  
  // Chart color palette
  chartColors = [
    '#3B82F6', '#10B981', '#F59E0B', '#EF4444', '#8B5CF6',
    '#EC4899', '#06B6D4', '#84CC16', '#F97316', '#6366F1'
  ];

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
      name: 'Rapport kilométrique',
      type: 'mileage',
      icon: '📏',
      description: 'Kilométrage détaillé avec analyses journalières, hebdomadaires et mensuelles'
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
    },
    {
      id: '8',
      name: 'Rapport journalier',
      type: 'daily',
      icon: '📅',
      description: 'Activité journalière détaillée (démarrages, arrêts, trajets)'
    },
    {
      id: '9',
      name: 'Rapport mensuel flotte',
      type: 'monthly',
      icon: '📊',
      description: 'Rapport complet avec KPIs, graphiques et analyses statistiques'
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
  
  // Daily report data
  dailyReport: DailyActivityReport | null = null;
  
  // Mileage report data
  mileageReport: MileageReport | null = null;
  
  // Monthly fleet report data
  monthlyReport: MonthlyFleetReport | null = null;
  monthlyActiveSection = 'summary';
  monthlySections = [
    { id: 'summary', label: 'Résumé', icon: '📊' },
    { id: 'fleet', label: 'Flotte', icon: '🚗' },
    { id: 'utilization', label: 'Utilisation', icon: '📈' },
    { id: 'fuel', label: 'Carburant', icon: '⛽' },
    { id: 'drivers', label: 'Conducteurs', icon: '👤' },
    { id: 'costs', label: 'Coûts', icon: '💰' },
    { id: 'kpis', label: 'KPIs', icon: '🎯' }
  ];
  selectedMonthlyYear: number = new Date().getFullYear();
  selectedMonthlyMonth: number = new Date().getMonth(); // Previous month
  monthlyYears: number[] = [];
  monthlyMonths = [
    { value: 1, label: 'Janvier' }, { value: 2, label: 'Février' }, { value: 3, label: 'Mars' },
    { value: 4, label: 'Avril' }, { value: 5, label: 'Mai' }, { value: 6, label: 'Juin' },
    { value: 7, label: 'Juillet' }, { value: 8, label: 'Août' }, { value: 9, label: 'Septembre' },
    { value: 10, label: 'Octobre' }, { value: 11, label: 'Novembre' }, { value: 12, label: 'Décembre' }
  ];

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

    // Initialize monthly report years (last 3 years)
    const currentYear = new Date().getFullYear();
    this.monthlyYears = [currentYear, currentYear - 1, currentYear - 2];
    // Default to previous month
    const lastMonth = new Date();
    lastMonth.setMonth(lastMonth.getMonth() - 1);
    this.selectedMonthlyYear = lastMonth.getFullYear();
    this.selectedMonthlyMonth = lastMonth.getMonth() + 1;

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

    if (!this.selectedVehicleId && (this.selectedTemplate.type === 'fuel' || this.selectedTemplate.type === 'daily' || this.selectedTemplate.type === 'mileage')) {
      console.warn('No vehicle selected');
      return;
    }

    this.loading = true;
    this.expandedSections['result'] = true;
    this.dailyReport = null;
    this.mileageReport = null;
    this.monthlyReport = null;

    const startDate = this.fromDate ? new Date(this.fromDate) : undefined;
    const endDate = this.toDate ? new Date(this.toDate) : undefined;
    const vehicleId = this.selectedVehicleId ? parseInt(this.selectedVehicleId) : undefined;

    // Handle daily report separately
    if (this.selectedTemplate.type === 'daily') {
      this.executeDailyReport(vehicleId!, startDate);
      return;
    }

    // Handle mileage report separately
    if (this.selectedTemplate.type === 'mileage') {
      this.executeMileageReport(vehicleId!, startDate, endDate);
      return;
    }

    // Handle monthly fleet report inline
    if (this.selectedTemplate.type === 'monthly') {
      this.executeMonthlyReport();
      return;
    }

    // All other report types use vehicle history API
    this.executeVehicleReport(vehicleId, startDate, endDate);
  }

  executeDailyReport(vehicleId: number, date?: Date) {
    const reportDate = date || new Date();
    
    this.apiService.getDailyReport(vehicleId, reportDate).subscribe({
      next: (report) => {
        this.ngZone.run(() => {
          this.dailyReport = report;
          this.processDailyReport(report);
          this.reportGenerated = true;
          this.loading = false;
          this.activeTab = 'table';
          this.currentPage = 1;
          this.cdr.detectChanges();
          this.appRef.tick();
        });
      },
      error: (err) => {
        this.ngZone.run(() => {
          console.error('Error loading daily report:', err);
          this.dailyReport = null;
          this.tableData = [];
          this.chartData = [];
          this.statisticsData = { 'Erreur': 'Impossible de charger le rapport journalier' };
          this.reportGenerated = true;
          this.loading = false;
          this.cdr.detectChanges();
          this.appRef.tick();
        });
      }
    });
  }

  processDailyReport(report: DailyActivityReport) {
    if (!report.hasActivity) {
      this.tableData = [];
      this.chartData = [];
      this.statisticsData = {
        'Véhicule': report.vehicleName,
        'Date': new Date(report.reportDate).toLocaleDateString('fr-FR'),
        'Information': 'Aucune activité enregistrée pour cette journée'
      };
      return;
    }

    // Convert activities to table data
    this.tableData = report.activities.map((activity: ActivitySegment) => ({
      time: `${this.formatTime(activity.startTime)}${activity.endTime ? ' → ' + this.formatTime(activity.endTime) : ''}`,
      type: activity.type === 'drive' ? '🚗 Trajet' : '🅿️ Arrêt',
      duration: activity.durationFormatted,
      details: activity.type === 'drive' 
        ? `${activity.distanceKm} km | ⌀ ${activity.avgSpeedKph} km/h | max ${activity.maxSpeedKph} km/h`
        : `${activity.startLocation.address || 'Adresse inconnue'}`,
      location: activity.type === 'drive'
        ? `${activity.startLocation.address || '?'} → ${activity.endLocation?.address || '?'}`
        : activity.startLocation.address || `${activity.startLocation.latitude.toFixed(4)}°, ${activity.startLocation.longitude.toFixed(4)}°`,
      isDrive: activity.type === 'drive'
    }));

    // Chart data - show duration of each activity
    this.chartData = report.activities.map((activity: ActivitySegment, index: number) => ({
      label: `${activity.type === 'drive' ? 'Trajet' : 'Arrêt'} ${activity.sequenceNumber}`,
      value: Math.round(activity.durationSeconds / 60) // duration in minutes
    }));

    // Statistics
    this.statisticsData = {
      'Véhicule': `${report.vehicleName}${report.plate ? ' (' + report.plate + ')' : ''}`,
      'Date': new Date(report.reportDate).toLocaleDateString('fr-FR', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }),
      'Premier démarrage': report.firstStart ? this.formatTime(report.firstStart.timestamp) : 'N/A',
      'Temps de conduite': report.summary.totalDrivingFormatted,
      'Temps d\'arrêt': report.summary.totalStoppedFormatted,
      'Distance totale': `${report.summary.totalDistanceKm} km`,
      'Nombre de trajets': report.summary.driveCount.toString(),
      'Nombre d\'arrêts': report.summary.stopCount.toString(),
      'Vitesse max': `${report.summary.maxSpeedKph} km/h`,
      'Vitesse moyenne': `${report.summary.avgSpeedKph} km/h`
    };
  }

  formatTime(dateStr: string): string {
    return new Date(dateStr).toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
  }

  executeMileageReport(vehicleId: number, startDate?: Date, endDate?: Date) {
    const start = startDate || new Date(Date.now() - 30 * 24 * 60 * 60 * 1000); // Default: last 30 days
    const end = endDate || new Date();
    
    this.apiService.getMileageReport(vehicleId, start, end).subscribe({
      next: (report) => {
        this.ngZone.run(() => {
          this.mileageReport = report;
          this.processMileageReport(report);
          this.reportGenerated = true;
          this.loading = false;
          this.activeTab = 'table';
          this.currentPage = 1;
          this.cdr.detectChanges();
          this.appRef.tick();
          setTimeout(() => this.createMileageChart(), 100);
        });
      },
      error: (err) => {
        this.ngZone.run(() => {
          console.error('Error loading mileage report:', err);
          this.mileageReport = null;
          this.tableData = [];
          this.chartData = [];
          this.statisticsData = { 'Erreur': 'Impossible de charger le rapport kilométrique' };
          this.reportGenerated = true;
          this.loading = false;
          this.cdr.detectChanges();
          this.appRef.tick();
        });
      }
    });
  }

  processMileageReport(report: MileageReport) {
    if (!report.hasData) {
      this.tableData = [];
      this.chartData = [];
      this.statisticsData = {
        'Véhicule': report.vehicleName,
        'Période': `${new Date(report.startDate).toLocaleDateString('fr-FR')} - ${new Date(report.endDate).toLocaleDateString('fr-FR')}`,
        'Information': 'Aucune donnée disponible pour cette période'
      };
      return;
    }

    // Convert daily breakdown to table data
    this.tableData = report.dailyBreakdown.map((day: DailyMileage) => ({
      date: new Date(day.date).toLocaleDateString('fr-FR', { weekday: 'short', day: '2-digit', month: '2-digit' }),
      dayOfWeek: day.dayOfWeek,
      distance: `${day.distanceKm.toFixed(1)} km`,
      distanceValue: day.distanceKm,
      tripCount: day.tripCount,
      drivingTime: this.formatMinutes(day.drivingMinutes),
      avgSpeed: `${day.avgSpeedKph.toFixed(1)} km/h`,
      maxSpeed: `${day.maxSpeedKph.toFixed(1)} km/h`,
      odometer: day.endOdometerKm ? `${day.endOdometerKm.toFixed(0)} km` : '-'
    }));

    // Chart data - daily distances
    this.chartData = report.dailyBreakdown.map((day: DailyMileage) => ({
      label: new Date(day.date).toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit' }),
      value: day.distanceKm
    }));

    // Statistics
    this.statisticsData = {
      'Véhicule': `${report.vehicleName}${report.plate ? ' (' + report.plate + ')' : ''}`,
      'Période': `${new Date(report.startDate).toLocaleDateString('fr-FR')} - ${new Date(report.endDate).toLocaleDateString('fr-FR')}`,
      'Distance totale': `${report.summary.totalDistanceKm.toFixed(1)} km`,
      'Moyenne journalière': `${report.summary.averageDailyKm.toFixed(1)} km`,
      'Max journalier': report.summary.maxDailyDate 
        ? `${report.summary.maxDailyKm.toFixed(1)} km (${new Date(report.summary.maxDailyDate).toLocaleDateString('fr-FR')})`
        : `${report.summary.maxDailyKm.toFixed(1)} km`,
      'Nombre de trajets': report.summary.totalTripCount.toString(),
      'Temps de conduite': report.summary.totalDrivingFormatted,
      'Vitesse max': `${report.summary.maxSpeedKph.toFixed(1)} km/h`,
      'Vitesse moyenne': `${report.summary.avgSpeedKph.toFixed(1)} km/h`,
      'Jours actifs': `${report.summary.daysWithActivity}/${report.summary.totalDays} (${report.summary.activityPercentage.toFixed(0)}%)`
    };

    // Add comparison if available
    if (report.previousPeriodComparison) {
      const comp = report.previousPeriodComparison;
      const trendIcon = comp.trend === 'increase' ? '📈' : comp.trend === 'decrease' ? '📉' : '➡️';
      const sign = comp.differenceKm >= 0 ? '+' : '';
      this.statisticsData['Évolution vs période précédente'] = 
        `${trendIcon} ${sign}${comp.differenceKm.toFixed(1)} km (${sign}${comp.percentageChange.toFixed(1)}%)`;
    }

    // Add odometer if available
    if (report.startOdometerKm && report.endOdometerKm) {
      this.statisticsData['Compteur début'] = `${report.startOdometerKm.toFixed(0)} km`;
      this.statisticsData['Compteur fin'] = `${report.endOdometerKm.toFixed(0)} km`;
    }
  }

  formatMinutes(minutes: number): string {
    if (minutes < 60) return `${minutes}m`;
    const hours = Math.floor(minutes / 60);
    const mins = minutes % 60;
    return mins > 0 ? `${hours}h ${mins}m` : `${hours}h`;
  }

  createMileageChart() {
    if (!this.chartCanvas?.nativeElement || !this.chartData.length) return;

    if (this.chart) {
      this.chart.destroy();
    }

    const ctx = this.chartCanvas.nativeElement.getContext('2d');
    if (!ctx) return;

    this.chart = new Chart(ctx, {
      type: 'bar',
      data: {
        labels: this.chartData.map(d => d.label),
        datasets: [{
          label: 'Distance (km)',
          data: this.chartData.map(d => d.value),
          backgroundColor: 'rgba(59, 130, 246, 0.7)',
          borderColor: 'rgba(59, 130, 246, 1)',
          borderWidth: 1,
          borderRadius: 4
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: {
            display: true,
            position: 'top'
          },
          tooltip: {
            callbacks: {
              label: (context) => `${(context.parsed.y ?? 0).toFixed(1)} km`
            }
          }
        },
        scales: {
          y: {
            beginAtZero: true,
            title: {
              display: true,
              text: 'Distance (km)'
            }
          },
          x: {
            title: {
              display: true,
              text: 'Date'
            }
          }
        }
      }
    });
  }

  // ==================== MONTHLY FLEET REPORT ====================

  executeMonthlyReport() {
    this.apiService.getMonthlyFleetReport(this.selectedMonthlyYear, this.selectedMonthlyMonth).subscribe({
      next: (report) => {
        this.ngZone.run(() => {
          this.monthlyReport = report;
          this.processMonthlyReport(report);
          this.reportGenerated = true;
          this.loading = false;
          this.activeTab = 'table';
          this.monthlyActiveSection = 'summary';
          this.cdr.detectChanges();
          this.appRef.tick();
          setTimeout(() => this.createMonthlyCharts(), 100);
        });
      },
      error: (err) => {
        this.ngZone.run(() => {
          console.error('Error loading monthly report:', err);
          this.monthlyReport = null;
          this.tableData = [];
          this.chartData = [];
          this.statisticsData = { 'Erreur': 'Impossible de charger le rapport mensuel' };
          this.reportGenerated = true;
          this.loading = false;
          this.cdr.detectChanges();
          this.appRef.tick();
        });
      }
    });
  }

  onMonthlyPeriodChange() {
    if (this.selectedTemplate?.type === 'monthly') {
      this.executeMonthlyReport();
    }
  }

  setMonthlySection(sectionId: string) {
    this.monthlyActiveSection = sectionId;
    setTimeout(() => this.createMonthlyCharts(), 100);
  }

  processMonthlyReport(report: MonthlyFleetReport) {
    // Build statistics from executive summary
    this.statisticsData = {
      'Période': report.reportPeriod,
      'Véhicules totaux': report.executiveSummary.totalVehicles.toString(),
      'Véhicules actifs': report.executiveSummary.activeVehicles.toString(),
      'Distance totale': `${this.formatNumber(report.executiveSummary.totalDistanceKm)} km`,
      'Carburant consommé': `${this.formatNumber(report.executiveSummary.totalFuelConsumedLiters)} L`,
      'Coût total': this.formatCurrency(report.executiveSummary.totalOperationalCost),
      'Taux utilisation': `${report.executiveSummary.fleetUtilizationRate.toFixed(1)}%`,
      'Trajets': report.executiveSummary.totalTrips.toString()
    };

    // Calculate fleet averages for variance calculations
    const fleetAvgConsumption = report.fuelAnalytics.averageConsumptionPer100Km;
    const totalMaintenanceCost = report.maintenance.totalMaintenanceCost;
    const avgMaintenanceCost = report.utilization.byVehicle.length > 0 
      ? totalMaintenanceCost / report.utilization.byVehicle.length : 0;

    // Build enhanced table data with fuel consumption, variance, and cost metrics
    this.tableData = report.utilization.byVehicle.map((v, index) => {
      // Get fuel data for this vehicle
      const fuelData = report.fuelAnalytics.byVehicle?.find(f => f.vehicleId === v.vehicleId);
      const consumption = fuelData?.consumptionPer100Km || 0;
      const consumptionVariance = fleetAvgConsumption > 0 
        ? ((consumption - fleetAvgConsumption) / fleetAvgConsumption) * 100 : 0;
      
      // Get maintenance data for this vehicle
      const maintenanceData = report.maintenance.byVehicle?.find(m => m.vehicleId === v.vehicleId);
      const maintenanceCost = maintenanceData?.totalCost || 0;
      const costVariance = avgMaintenanceCost > 0 
        ? ((maintenanceCost - avgMaintenanceCost) / avgMaintenanceCost) * 100 : 0;

      return {
        vehicleId: v.vehicleId,
        vehicleName: v.vehicleName,
        plate: v.plate || '-',
        utilizationRate: v.utilizationRate,
        utilizationRateFormatted: `${v.utilizationRate.toFixed(1)}%`,
        totalDistanceKm: v.totalDistanceKm,
        totalDistanceFormatted: `${this.formatNumber(v.totalDistanceKm)} km`,
        totalTrips: v.totalTrips,
        operatingDays: v.operatingDays,
        avgDailyKm: `${this.formatNumber(v.avgDailyKm)} km`,
        // Enhanced columns
        fuelConsumption: consumption,
        fuelConsumptionFormatted: `${consumption.toFixed(1)} L/100km`,
        consumptionVariance: consumptionVariance,
        consumptionVarianceFormatted: `${consumptionVariance >= 0 ? '+' : ''}${consumptionVariance.toFixed(1)}%`,
        maintenanceCost: maintenanceCost,
        maintenanceCostFormatted: this.formatCurrency(maintenanceCost),
        costVariance: costVariance,
        costVarianceFormatted: `${costVariance >= 0 ? '+' : ''}${costVariance.toFixed(1)}%`,
        colorIndex: index
      };
    });

    // Build chart data from daily trend
    this.chartData = report.utilization.dailyTrend.map(d => ({
      label: new Date(d.date).toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit' }),
      value: d.totalDistanceKm
    }));
  }

  createMonthlyCharts() {
    if (!this.monthlyReport) return;

    // Destroy existing charts
    this.destroyMonthlyCharts();

    // Create all charts with slight delay to ensure DOM is ready
    setTimeout(() => {
      this.createKmBarChart();
      this.createFuelPieChart();
      this.createMaintenanceAreaChart();
      this.createMainLineChart();
    }, 100);
  }

  destroyMonthlyCharts() {
    if (this.chart) { this.chart.destroy(); this.chart = undefined; }
    if (this.kmBarChart) { this.kmBarChart.destroy(); this.kmBarChart = undefined; }
    if (this.fuelPieChart) { this.fuelPieChart.destroy(); this.fuelPieChart = undefined; }
    if (this.maintenanceAreaChart) { this.maintenanceAreaChart.destroy(); this.maintenanceAreaChart = undefined; }
  }

  // Bar Chart: Kilometers per vehicle
  createKmBarChart() {
    if (!this.kmBarChartRef?.nativeElement || !this.monthlyReport) return;
    
    const ctx = this.kmBarChartRef.nativeElement.getContext('2d');
    if (!ctx) return;

    const vehicleData = this.monthlyReport.utilization.byVehicle;
    
    this.kmBarChart = new Chart(ctx, {
      type: 'bar',
      data: {
        labels: vehicleData.map(v => v.vehicleName),
        datasets: [{
          label: 'Kilomètres parcourus',
          data: vehicleData.map(v => v.totalDistanceKm),
          backgroundColor: vehicleData.map((_, i) => this.chartColors[i % this.chartColors.length]),
          borderColor: vehicleData.map((_, i) => this.chartColors[i % this.chartColors.length]),
          borderWidth: 1,
          borderRadius: 6
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { display: false },
          tooltip: {
            callbacks: {
              label: (ctx) => `${(ctx.parsed.y ?? 0).toLocaleString('fr-FR')} km`
            }
          }
        },
        scales: {
          x: { 
            title: { display: true, text: 'Véhicule' },
            ticks: { maxRotation: 45, minRotation: 45 }
          },
          y: { 
            beginAtZero: true, 
            title: { display: true, text: 'Distance (km)' },
            ticks: { callback: (value) => `${value.toLocaleString('fr-FR')}` }
          }
        }
      }
    });
  }

  // Pie Chart: Fuel consumption distribution
  createFuelPieChart() {
    if (!this.fuelPieChartRef?.nativeElement || !this.monthlyReport) return;
    
    const ctx = this.fuelPieChartRef.nativeElement.getContext('2d');
    if (!ctx) return;

    const fuelData = this.monthlyReport.fuelAnalytics.byVehicle || [];
    const totalFuel = fuelData.reduce((sum, v) => sum + (v.totalConsumedLiters || 0), 0);
    
    this.fuelPieChart = new Chart(ctx, {
      type: 'pie',
      data: {
        labels: fuelData.map(v => v.vehicleName),
        datasets: [{
          data: fuelData.map(v => v.totalConsumedLiters || 0),
          backgroundColor: fuelData.map((_, i) => this.chartColors[i % this.chartColors.length]),
          borderColor: '#ffffff',
          borderWidth: 2
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { 
            display: true, 
            position: 'right',
            labels: { 
              usePointStyle: true,
              padding: 15,
              font: { size: 11 }
            }
          },
          tooltip: {
            callbacks: {
              label: (ctx) => {
                const value = ctx.parsed;
                const percentage = totalFuel > 0 ? ((value / totalFuel) * 100).toFixed(1) : 0;
                return `${ctx.label}: ${value.toLocaleString('fr-FR')} L (${percentage}%)`;
              }
            }
          }
        }
      }
    });
  }

  // Area Chart: Maintenance costs over time
  createMaintenanceAreaChart() {
    if (!this.maintenanceAreaChartRef?.nativeElement || !this.monthlyReport) return;
    
    const ctx = this.maintenanceAreaChartRef.nativeElement.getContext('2d');
    if (!ctx) return;

    // Use daily trend or generate mock maintenance data based on report
    const maintenanceByVehicle = this.monthlyReport.maintenance.byVehicle || [];
    const daysInMonth = new Date(this.monthlyReport.year, this.monthlyReport.month, 0).getDate();
    
    // Generate labels for each week of the month
    const weekLabels = ['Semaine 1', 'Semaine 2', 'Semaine 3', 'Semaine 4'];
    
    // Create datasets for each vehicle with maintenance costs distributed over weeks
    const datasets = maintenanceByVehicle.slice(0, 5).map((vehicle, index) => {
      const weeklyData = [0, 0, 0, 0];
      // Distribute maintenance cost across weeks (simulated breakdown)
      const totalCost = vehicle.totalCost || 0;
      weeklyData[0] = totalCost * 0.25;
      weeklyData[1] = totalCost * 0.30;
      weeklyData[2] = totalCost * 0.20;
      weeklyData[3] = totalCost * 0.25;
      
      return {
        label: vehicle.vehicleName,
        data: weeklyData,
        backgroundColor: this.hexToRgba(this.chartColors[index % this.chartColors.length], 0.3),
        borderColor: this.chartColors[index % this.chartColors.length],
        borderWidth: 2,
        fill: true,
        tension: 0.4
      };
    });

    this.maintenanceAreaChart = new Chart(ctx, {
      type: 'line',
      data: {
        labels: weekLabels,
        datasets: datasets
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { 
            display: true, 
            position: 'top',
            labels: { usePointStyle: true }
          },
          tooltip: {
            mode: 'index',
            intersect: false,
            callbacks: {
              label: (ctx) => `${ctx.dataset.label}: ${(ctx.parsed.y ?? 0).toLocaleString('fr-FR', { minimumFractionDigits: 2 })} TND`
            }
          }
        },
        scales: {
          x: { title: { display: true, text: 'Période' } },
          y: { 
            beginAtZero: true, 
            stacked: true,
            title: { display: true, text: 'Coût maintenance (TND)' },
            ticks: { callback: (value) => `${value.toLocaleString('fr-FR')} TND` }
          }
        },
        interaction: {
          mode: 'nearest',
          axis: 'x',
          intersect: false
        }
      }
    });
  }

  // Main line chart (distance + utilization trends)
  createMainLineChart() {
    if (!this.chartCanvas?.nativeElement || !this.monthlyReport) return;

    const ctx = this.chartCanvas.nativeElement.getContext('2d');
    if (!ctx) return;

    const chartData = this.monthlyReport.utilization.dailyTrend;
    
    this.chart = new Chart(ctx, {
      type: 'line',
      data: {
        labels: chartData.map(d => new Date(d.date).toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit' })),
        datasets: [{
          label: 'Distance (km)',
          data: chartData.map(d => d.totalDistanceKm),
          borderColor: '#3B82F6',
          backgroundColor: 'rgba(59, 130, 246, 0.1)',
          fill: true,
          tension: 0.4
        }, {
          label: 'Utilisation (%)',
          data: chartData.map(d => d.utilizationRate),
          borderColor: '#10B981',
          backgroundColor: 'rgba(16, 185, 129, 0.1)',
          fill: false,
          tension: 0.4,
          yAxisID: 'y1'
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { display: true, position: 'top' }
        },
        scales: {
          y: { beginAtZero: true, title: { display: true, text: 'Distance (km)' } },
          y1: { beginAtZero: true, position: 'right', max: 100, title: { display: true, text: 'Utilisation (%)' }, grid: { drawOnChartArea: false } }
        }
      }
    });
  }

  hexToRgba(hex: string, alpha: number): string {
    const r = parseInt(hex.slice(1, 3), 16);
    const g = parseInt(hex.slice(3, 5), 16);
    const b = parseInt(hex.slice(5, 7), 16);
    return `rgba(${r}, ${g}, ${b}, ${alpha})`;
  }

  formatNumber(value: number, decimals = 0): string {
    return value.toLocaleString('fr-FR', { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
  }

  formatCurrency(value: number): string {
    return value.toLocaleString('fr-FR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' TND';
  }

  getKpiStatusClass(status: string): string {
    return status === 'OnTarget' ? 'kpi-success' : status === 'Above' ? 'kpi-warning' : 'kpi-danger';
  }

  getAlertSeverityClass(severity: string): string {
    switch (severity.toLowerCase()) {
      case 'critical': return 'alert-critical';
      case 'warning': return 'alert-warning';
      default: return 'alert-info';
    }
  }

  getTrendIcon(trend: string): string {
    return trend === 'increase' ? '📈' : trend === 'decrease' ? '📉' : '➡️';
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
      case 'trips':
        this.processTripReport(sorted);
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
          location: `${curr.latitude.toFixed(5)}, ${curr.longitude.toFixed(5)}`,
          kilometrage: `${totalDistance.toFixed(2)} km`
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

  processTripReport(positions: any[]) {
    if (!positions.length) {
      this.tableData = [];
      this.chartData = [];
      this.statisticsData = { 'Information': 'Aucune donnée pour cette période' };
      return;
    }

    const gapMinutes = 10;
    const trips: {
      start: any;
      end: any;
      distanceCalculated: number;
    }[] = [];

    let currentTrip: {
      start: any;
      end: any;
      distanceCalculated: number;
    } | null = null;

    const resetTrip = (pos: any) => ({
      start: pos,
      end: pos,
      distanceCalculated: 0
    });

    for (let i = 0; i < positions.length; i++) {
      const pos = positions[i];
      if (!currentTrip) {
        currentTrip = resetTrip(pos);
        continue;
      }

      const prev = positions[i - 1];
      currentTrip.end = pos;

      const dist = this.haversineDistance(prev.latitude, prev.longitude, pos.latitude, pos.longitude);
      if (!Number.isNaN(dist)) {
        currentTrip.distanceCalculated += dist;
      }

      const gap = (new Date(pos.recordedAt).getTime() - new Date(prev.recordedAt).getTime()) / 60000;
      const ignitionDrop = prev.ignitionOn && pos.ignitionOn === false;
      const extendedStop = (prev.speedKph || 0) < 2 && (pos.speedKph || 0) < 2 && gap >= 5;

      if (gap >= gapMinutes || ignitionDrop || extendedStop) {
        trips.push(currentTrip);
        currentTrip = resetTrip(pos);
      }
    }

    if (currentTrip) {
      trips.push(currentTrip);
    }

    const meaningfulTrips = trips.filter(trip => {
      const durationMinutes = (new Date(trip.end.recordedAt).getTime() - new Date(trip.start.recordedAt).getTime()) / 60000;
      return durationMinutes >= 2 || trip.distanceCalculated >= 0.2;
    });

    if (!meaningfulTrips.length) {
      this.tableData = [];
      this.chartData = [];
      this.statisticsData = { 'Information': 'Aucun trajet significatif détecté' };
      return;
    }

    const summaries = meaningfulTrips.map((trip, index) => {
      const startTime = new Date(trip.start.recordedAt);
      const endTime = new Date(trip.end.recordedAt);
      const durationMinutes = Math.max(1, (endTime.getTime() - startTime.getTime()) / 60000);

      let distanceKm = trip.distanceCalculated;
      if (trip.start.odometerKm && trip.end.odometerKm && trip.end.odometerKm >= trip.start.odometerKm) {
        distanceKm = trip.end.odometerKm - trip.start.odometerKm;
      }

      const avgSpeed = durationMinutes > 0 ? distanceKm / (durationMinutes / 60) : 0;
      const startLocation = trip.start.address || `${trip.start.latitude.toFixed(4)}°, ${trip.start.longitude.toFixed(4)}°`;
      const endLocation = trip.end.address || `${trip.end.latitude.toFixed(4)}°, ${trip.end.longitude.toFixed(4)}°`;

      const kilometrageLabel = trip.end.odometerKm
        ? `${Number(trip.end.odometerKm).toLocaleString('fr-FR')} km`
        : `+${distanceKm.toFixed(2)} km`;

      return {
        row: {
          time: `${startTime.toLocaleString('fr-FR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })} → ${endTime.toLocaleString('fr-FR', { hour: '2-digit', minute: '2-digit' })}`,
          distance: `${distanceKm.toFixed(2)} km`,
          trips: `${Math.round(durationMinutes)} min | ${avgSpeed.toFixed(1)} km/h`,
          location: `${startLocation} → ${endLocation}`,
          kilometrage: kilometrageLabel
        },
        distanceKm,
        durationMinutes,
        label: `Trajet ${index + 1}`
      };
    });

    this.tableData = summaries.map(summary => summary.row);
    this.chartData = summaries.map(summary => ({
      label: summary.label,
      value: summary.distanceKm
    }));

    const totalDistance = summaries.reduce((sum, s) => sum + s.distanceKm, 0);
    const totalDuration = summaries.reduce((sum, s) => sum + s.durationMinutes, 0);
    const avgSpeed = totalDuration > 0 ? totalDistance / (totalDuration / 60) : 0;

    this.statisticsData = {
      'Nombre de trajets': summaries.length.toString(),
      'Distance totale': `${totalDistance.toFixed(2)} km`,
      'Durée totale': `${Math.round(totalDuration)} min`,
      'Vitesse moyenne': `${avgSpeed.toFixed(1)} km/h`
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
