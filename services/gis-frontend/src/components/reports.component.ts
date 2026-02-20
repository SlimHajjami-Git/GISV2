import { Component, OnInit, OnDestroy, ViewChild, ElementRef, NgZone, ChangeDetectorRef, ApplicationRef, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService, FuelRecordsResult, FuelRecord, DailyActivityReport, ActivitySegment, MileageReport, DailyMileage, MonthlyFleetReport, MileagePeriodReport, MileagePeriodType, HourlyMileagePeriod, DailyMileagePeriod, MonthlyMileagePeriod, VehicleStopsResult, VehicleStopDto, FleetFuelStatisticsDto, VehicleFuelExpenseDto, FuelTypeDistributionDto, MonthlyFuelTrendDto } from '../services/api.service';
import { Subject, takeUntil } from 'rxjs';
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
export class ReportsComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  @ViewChild('chartCanvas') chartCanvas?: ElementRef<HTMLCanvasElement>;
  @ViewChild('secondaryChartCanvas') secondaryChartCanvas?: ElementRef<HTMLCanvasElement>;
  @ViewChild('kmBarChart') kmBarChartRef?: ElementRef<HTMLCanvasElement>;
  @ViewChild('fuelPieChart') fuelPieChartRef?: ElementRef<HTMLCanvasElement>;
  @ViewChild('maintenanceAreaChart') maintenanceAreaChartRef?: ElementRef<HTMLCanvasElement>;
  @ViewChild('mileagePeriodChart') mileagePeriodChartRef?: ElementRef<HTMLCanvasElement>;
  
  // Chart instances for monthly report
  private kmBarChart?: Chart;
  private fuelPieChart?: Chart;
  private maintenanceAreaChart?: Chart;
  private mileagePeriodChart?: Chart;
  
  // Chart color palette
  chartColors = [
    '#3B82F6', '#10B981', '#F59E0B', '#EF4444', '#8B5CF6',
    '#EC4899', '#06B6D4', '#84CC16', '#F97316', '#6366F1'
  ];

  templates = [
    // GPS Analysis Reports
    {
      id: '1',
      name: 'Rapport de trajets',
      type: 'trips',
      icon: '🛣️',
      description: 'Analyse détaillée des trajets',
      category: 'gps'
    },
    {
      id: '4',
      name: 'Rapport des arrêts',
      type: 'stops',
      icon: '🅿️',
      description: 'Temps et lieux d\'arrêt',
      category: 'gps'
    },
    {
      id: '5',
      name: 'Rapport kilométrique',
      type: 'mileage',
      icon: '📏',
      description: 'Kilométrage journalier détaillé',
      category: 'gps'
    },
    {
      id: '10',
      name: 'Kilométrage par période',
      type: 'mileage-period',
      icon: '📈',
      description: 'Analyse par heure/jour/mois',
      category: 'gps'
    },
    {
      id: '8',
      name: 'Rapport journalier',
      type: 'daily',
      icon: '�',
      description: 'Activité journalière complète',
      category: 'gps'
    },
    {
      id: '3',
      name: 'Rapport de vitesse',
      type: 'speed',
      icon: '🏎️',
      description: 'Analyse des vitesses',
      category: 'gps'
    },
    {
      id: '11',
      name: 'Infractions vitesse',
      type: 'speed-infraction',
      icon: '⚠️',
      description: 'Dépassements de limite',
      category: 'gps'
    },
    {
      id: '12',
      name: 'Comportement conduite',
      type: 'driving-behavior',
      icon: '�',
      description: 'Freinages, accélérations brusques',
      category: 'gps'
    },
    // Cost Reports
    {
      id: '2',
      name: 'Consommation carburant',
      type: 'fuel',
      icon: '⛽',
      description: 'Analyse de la consommation',
      category: 'costs'
    },
    {
      id: '6',
      name: 'Réparations véhicules',
      type: 'costs',
      icon: '🔩',
      description: 'Historique des réparations',
      category: 'costs'
    },
    {
      id: '7',
      name: 'Coûts maintenance',
      type: 'maintenance',
      icon: '🔧',
      description: 'Historique des maintenances',
      category: 'costs'
    },
    {
      id: '13',
      name: 'Estimation coûts carburant',
      type: 'fuel-estimation',
      icon: '💰',
      description: 'Estimation basée sur GPS et prix',
      category: 'costs'
    },
    // Statistics Reports
    {
      id: '9',
      name: 'Rapport mensuel flotte',
      type: 'monthly',
      icon: '�',
      description: 'KPIs et analyses complètes',
      category: 'stats'
    }
  ];

  // Dropdown state
  showTemplateDropdown = false;

  vehicles: any[] = [];
  drivers: any[] = [];
  departments: any[] = [];
  selectedTemplate: any = null;
  selectedTemplateId = '';
  selectedVehicleId = '';
  selectedDriverId = '';
  selectedDepartmentId = '';
  selectedVehicleIds: string[] = [];

  // Mileage Period Report options
  mileagePeriodTypes = [
    { value: 'hour', label: 'Par heure (24h)' },
    { value: 'day', label: 'Par jour' },
    { value: 'month', label: 'Par mois' }
  ];
  selectedMileagePeriodType: MileagePeriodType = 'day';
  mileagePeriodReport: MileagePeriodReport | null = null;
  
  // Mileage Period specific dates
  mileagePeriodDate = '';           // For hourly report (single date)
  mileagePeriodStartDate = '';      // For daily report (range start)
  mileagePeriodEndDate = '';        // For daily report (range end)
  mileagePeriodMonth = new Date().getMonth() + 1;  // For monthly report
  mileagePeriodYear = new Date().getFullYear();    // For monthly report
  availableYears: number[] = [];

  // Period filters for standard reports
  standardPeriods = [
    { value: 'today', label: 'Aujourd\'hui' },
    { value: 'week', label: 'Semaine' },
    { value: 'month', label: 'Mois' },
    { value: 'custom', label: 'Personnalisé' }
  ];
  selectedStandardPeriod = 'today';
  
  // Custom date range (when Personnalisé is selected)
  customStartDate = '';
  customEndDate = '';
  
  // Single date for daily report
  dailyReportDate = '';
  
  // Periods for costs/maintenance (longer ranges)
  costPeriods = [
    { value: 'month', label: 'Ce mois' },
    { value: 'quarter', label: 'Trimestre' },
    { value: 'year', label: 'Année' },
    { value: 'custom', label: 'Personnalisé' }
  ];
  selectedCostPeriod = 'month';
  
  // Speed limit for speed-infraction report
  speedLimit = 90;
  
  // Incident type filters (checkboxes)
  drivingBehaviorFilters: { [key: string]: boolean } = {
    harshAcceleration: true,
    harshBraking: true,
    sharpSteering: true,
    overspeed: true,
    highRpm: true
  };
  
  // Incident type definitions with colors
  incidentTypes = [
    { key: 'harshAcceleration', label: 'Accélérations brusques', color: '#FF6B6B', icon: '⚡' },
    { key: 'harshBraking', label: 'Freinages brusques', color: '#4ECDC4', icon: '🛑' },
    { key: 'sharpSteering', label: 'Virages brusques', color: '#45B7D1', icon: '↩️' },
    { key: 'overspeed', label: 'Vitesse > 130 km/h', color: '#FFA07A', icon: '🏎️' },
    { key: 'highRpm', label: 'RPM > 3500', color: '#9B59B6', icon: '⚙️' }
  ];
  
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
  secondaryChartData: any[] = [];
  tableData: any[] = [];
  statisticsData: any = {};
  
  // Daily report data
  dailyReport: DailyActivityReport | null = null;
  
  // Mileage report data
  mileageReport: MileageReport | null = null;
  
  // Monthly fleet report data
  monthlyReport: MonthlyFleetReport | null = null;
  monthlyActiveSection = 'summary';
  
  // Fuel estimation report data
  fuelEstimationReport: FleetFuelStatisticsDto | null = null;
  fuelEstimationActiveSection = 'summary';
  fuelEstimationSections = [
    { id: 'summary', label: 'Résumé', icon: '📊' },
    { id: 'vehicles', label: 'Par Véhicule', icon: '🚗' },
    { id: 'distribution', label: 'Distribution', icon: '⛽' },
    { id: 'trends', label: 'Tendances', icon: '📈' }
  ];
  
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
  private secondaryChart?: Chart;

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

    // Initialize mileage period report dates
    this.availableYears = [currentYear, currentYear - 1, currentYear - 2];
    this.initializeMileagePeriodDates();

    this.ngZone.run(() => {
      this.loadData();
      this.initializeDates();
    });
  }

  initializeMileagePeriodDates() {
    const today = new Date();
    const yesterday = new Date(today);
    yesterday.setDate(today.getDate() - 1);
    const weekAgo = new Date(today);
    weekAgo.setDate(today.getDate() - 7);
    
    // Hourly: default to yesterday
    this.mileagePeriodDate = yesterday.toISOString().split('T')[0];
    
    // Daily: default to last 7 days
    this.mileagePeriodStartDate = weekAgo.toISOString().split('T')[0];
    this.mileagePeriodEndDate = today.toISOString().split('T')[0];
    
    // Monthly: default to current month/year
    this.mileagePeriodMonth = today.getMonth() + 1;
    this.mileagePeriodYear = today.getFullYear();
    
    
    // Initialize custom date range + daily report date
    this.customStartDate = weekAgo.toISOString().split('T')[0];
    this.customEndDate = today.toISOString().split('T')[0];
    this.dailyReportDate = yesterday.toISOString().split('T')[0];
  }

  loadData() {
    this.apiService.getVehicles().pipe(takeUntil(this.destroy$)).subscribe({
      next: (vehicles) => this.vehicles = vehicles,
      error: (err) => console.error('Error loading vehicles:', err)
    });
    this.apiService.getDrivers().pipe(takeUntil(this.destroy$)).subscribe({
      next: (drivers) => this.drivers = drivers,
      error: (err) => console.error('Error loading drivers:', err)
    });
    this.apiService.getDepartments().pipe(takeUntil(this.destroy$)).subscribe({
      next: (departments) => this.departments = departments,
      error: (err) => console.error('Error loading departments:', err)
    });
  }

  onDriverFilterChange() {
    if (this.selectedDriverId) {
      const driver = this.drivers.find(d => String(d.id) === String(this.selectedDriverId));
      if (driver?.assignedVehicleId) {
        this.selectedVehicleId = String(driver.assignedVehicleId);
      }
    }
  }

  onDepartmentFilterChange() {
    if (this.selectedDepartmentId) {
      this.selectedVehicleId = '';
      this.selectedDriverId = '';
    }
  }

  getFilteredVehicles(): any[] {
    if (!this.selectedDepartmentId) return this.vehicles;
    return this.vehicles.filter(v => String(v.departmentId) === String(this.selectedDepartmentId));
  }

  initializeDates() {
    this.selectStandardPeriod('today');
  }

  selectStandardPeriod(period: string) {
    this.selectedStandardPeriod = period;
    const now = new Date();
    const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());

    switch (period) {
      case 'today':
        this.fromDate = this.toDateTime(today);
        this.toDate = this.toDateTime(now);
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
      case 'custom':
        // Dates are set via customStartDate / customEndDate inputs
        if (this.customStartDate && this.customEndDate) {
          this.fromDate = this.toDateTime(new Date(this.customStartDate));
          this.toDate = this.toDateTime(new Date(this.customEndDate + 'T23:59:59'));
        }
        break;
    }
  }

  selectCostPeriod(period: string) {
    this.selectedCostPeriod = period;
    const now = new Date();
    const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());

    switch (period) {
      case 'month':
        const monthStart = new Date(today.getFullYear(), today.getMonth(), 1);
        this.fromDate = this.toDateTime(monthStart);
        this.toDate = this.toDateTime(now);
        break;
      case 'quarter':
        const qStart = new Date(today.getFullYear(), Math.floor(today.getMonth() / 3) * 3, 1);
        this.fromDate = this.toDateTime(qStart);
        this.toDate = this.toDateTime(now);
        break;
      case 'year':
        const yearStart = new Date(today.getFullYear(), 0, 1);
        this.fromDate = this.toDateTime(yearStart);
        this.toDate = this.toDateTime(now);
        break;
      case 'custom':
        if (this.customStartDate && this.customEndDate) {
          this.fromDate = this.toDateTime(new Date(this.customStartDate));
          this.toDate = this.toDateTime(new Date(this.customEndDate + 'T23:59:59'));
        }
        break;
    }
  }


  toDateTime(date: Date): string {
    // Format as local time (not UTC) - DB stores local time
    const y = date.getFullYear();
    const m = String(date.getMonth() + 1).padStart(2, '0');
    const d = String(date.getDate()).padStart(2, '0');
    const h = String(date.getHours()).padStart(2, '0');
    const min = String(date.getMinutes()).padStart(2, '0');
    return `${y}-${m}-${d}T${h}:${min}`;
  }

  selectTemplate(template: any) {
    this.destroyAllCharts();
    this.selectedTemplate = template;
    this.selectedTemplateId = template.id;
    this.reportGenerated = false;
    this.tableData = [];
    this.chartData = [];
    this.secondaryChartData = [];
    this.statisticsData = {};
  }

  onTemplateChange() {
    this.destroyAllCharts();
    this.selectedTemplate = this.templates.find(t => t.id === this.selectedTemplateId) || null;
    this.reportGenerated = false;
    this.tableData = [];
    this.chartData = [];
    this.secondaryChartData = [];
    this.statisticsData = {};
  }

  // Dropdown methods
  toggleTemplateDropdown(event: Event) {
    event.stopPropagation();
    this.showTemplateDropdown = !this.showTemplateDropdown;
  }

  selectTemplateFromDropdown(template: any) {
    this.destroyAllCharts();
    this.selectedTemplate = template;
    this.selectedTemplateId = template.id;
    this.showTemplateDropdown = false;
    this.reportGenerated = false;
    this.tableData = [];
    this.chartData = [];
    this.secondaryChartData = [];
    this.statisticsData = {};
  }

  getTemplatesByCategory(category: string): any[] {
    return this.templates.filter(t => (t as any).category === category);
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: Event) {
    this.showTemplateDropdown = false;
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
    this.destroyAllCharts();
    this.selectedTemplateId = '';
    this.selectedTemplate = null;
    this.selectedVehicleId = '';
    this.selectedDriverId = '';
    this.selectedDepartmentId = '';
    this.selectedStandardPeriod = 'today';
    this.selectedCostPeriod = 'month';
    this.customStartDate = '';
    this.customEndDate = '';
    this.dailyReportDate = '';
    this.reportGenerated = false;
    this.tableData = [];
    this.chartData = [];
    this.secondaryChartData = [];
    this.statisticsData = {};
  }

  destroyAllCharts() {
    if (this.chart) { this.chart.destroy(); this.chart = undefined; }
    if (this.secondaryChart) { this.secondaryChart.destroy(); this.secondaryChart = undefined; }
    if (this.mileagePeriodChart) { this.mileagePeriodChart.destroy(); this.mileagePeriodChart = undefined; }
    if (this.kmBarChart) { this.kmBarChart.destroy(); this.kmBarChart = undefined; }
    if (this.fuelPieChart) { this.fuelPieChart.destroy(); this.fuelPieChart = undefined; }
    if (this.maintenanceAreaChart) { this.maintenanceAreaChart.destroy(); this.maintenanceAreaChart = undefined; }
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

    // Reports that require a single vehicle
    const singleVehicleReports = ['fuel', 'daily', 'mileage'];
    if (!this.selectedVehicleId && singleVehicleReports.includes(this.selectedTemplate.type)) {
      console.warn('No vehicle selected for single-vehicle report');
      this.tableData = [];
      this.chartData = [];
      this.statisticsData = { 'Erreur': 'Veuillez sélectionner un véhicule pour ce type de rapport' };
      this.reportGenerated = true;
      this.loading = false;
      return;
    }

    this.loading = true;
    this.expandedSections['result'] = true;
    this.dailyReport = null;
    this.mileageReport = null;
    this.monthlyReport = null;
    this.mileagePeriodReport = null;
    this.fuelEstimationReport = null;

    let startDate = this.fromDate ? new Date(this.fromDate) : undefined;
    let endDate = this.toDate ? new Date(this.toDate) : undefined;
    const vehicleId = this.selectedVehicleId ? parseInt(this.selectedVehicleId) : undefined;

    // For custom date range, recompute dates from inputs
    if (this.selectedStandardPeriod === 'custom' && this.customStartDate && this.customEndDate) {
      startDate = new Date(this.customStartDate + 'T00:00:00');
      endDate = new Date(this.customEndDate + 'T23:59:59');
    }

    // For cost/maintenance reports, compute dates from cost period
    if (this.selectedTemplate.type === 'costs' || this.selectedTemplate.type === 'maintenance') {
      this.selectCostPeriod(this.selectedCostPeriod);
      startDate = this.fromDate ? new Date(this.fromDate) : undefined;
      endDate = this.toDate ? new Date(this.toDate) : undefined;
    }

    // Handle daily report: use single date picker
    if (this.selectedTemplate.type === 'daily') {
      const reportDate = this.dailyReportDate ? new Date(this.dailyReportDate) : undefined;
      this.executeDailyReport(vehicleId!, reportDate);
      return;
    }

    // Handle mileage report separately
    if (this.selectedTemplate.type === 'mileage') {
      this.executeMileageReport(vehicleId!, startDate, endDate);
      return;
    }

    // Handle mileage period report (Hour/Day/Month)
    if (this.selectedTemplate.type === 'mileage-period') {
      if (vehicleId) {
        this.executeMileagePeriodReport(vehicleId, startDate, endDate);
      } else {
        this.executeMileagePeriodReportAllVehicles(startDate, endDate);
      }
      return;
    }

    // Handle monthly fleet report inline
    if (this.selectedTemplate.type === 'monthly') {
      this.executeMonthlyReport();
      return;
    }

    // Handle stops report using VehicleStops API
    if (this.selectedTemplate.type === 'stops') {
      if (vehicleId) {
        this.executeStopsReport(vehicleId, startDate, endDate);
      } else {
        this.executeStopsReportAllVehicles(startDate, endDate);
      }
      return;
    }

    // Handle speed infraction report
    if (this.selectedTemplate.type === 'speed-infraction') {
      this.executeSpeedInfractionReport(startDate, endDate);
      return;
    }

    // Handle driving behavior report
    if (this.selectedTemplate.type === 'driving-behavior') {
      this.executeDrivingBehaviorReport(startDate, endDate);
      return;
    }

    // Handle fuel estimation report
    if (this.selectedTemplate.type === 'fuel-estimation') {
      this.executeFuelEstimationReport(vehicleId, startDate, endDate);
      return;
    }

    // Handle costs report
    if (this.selectedTemplate.type === 'costs') {
      this.executeCostsReport(vehicleId, startDate, endDate);
      return;
    }

    // Handle maintenance report
    if (this.selectedTemplate.type === 'maintenance') {
      this.executeMaintenanceReport(vehicleId, startDate, endDate);
      return;
    }

    // All other report types use vehicle history API
    if (vehicleId) {
      this.executeVehicleReport(vehicleId, startDate, endDate);
    } else {
      this.executeVehicleReportAllVehicles(startDate, endDate);
    }
  }

  executeVehicleReportAllVehicles(startDate?: Date, endDate?: Date) {
    console.log('executeVehicleReportAllVehicles called');
    const allPositions: any[] = [];
    let completedRequests = 0;
    const totalVehicles = this.vehicles.length;

    if (totalVehicles === 0) {
      this.ngZone.run(() => {
        this.tableData = [];
        this.chartData = [];
        this.statisticsData = { 'Information': 'Aucun véhicule disponible' };
        this.reportGenerated = true;
        this.loading = false;
        this.cdr.detectChanges();
      });
      return;
    }

    this.vehicles.forEach(vehicle => {
      this.apiService.getVehicleHistory(vehicle.id, startDate, endDate, 5000).pipe(takeUntil(this.destroy$)).subscribe({
        next: (positions) => {
          const positionsWithVehicle = positions.map(p => ({
            ...p,
            vehicleId: vehicle.id,
            vehicleName: vehicle.name || vehicle.brand + ' ' + vehicle.model,
            vehiclePlate: vehicle.plate
          }));
          allPositions.push(...positionsWithVehicle);
          completedRequests++;

          if (completedRequests === totalVehicles) {
            this.ngZone.run(() => {
              this.processVehicleDataAllVehicles(allPositions);
              this.reportGenerated = true;
              this.loading = false;
              this.activeTab = 'table';
              this.currentPage = 1;
              this.cdr.detectChanges();
              this.appRef.tick();
              setTimeout(() => this.createChart(), 100);
            });
          }
        },
        error: () => {
          completedRequests++;
          if (completedRequests === totalVehicles) {
            this.ngZone.run(() => {
              this.processVehicleDataAllVehicles(allPositions);
              this.reportGenerated = true;
              this.loading = false;
              this.activeTab = 'table';
              this.currentPage = 1;
              this.cdr.detectChanges();
              this.appRef.tick();
              setTimeout(() => this.createChart(), 100);
            });
          }
        }
      });
    });
  }

  processVehicleDataAllVehicles(positions: any[]) {
    if (!positions.length) {
      this.tableData = [];
      this.chartData = [];
      this.statisticsData = { 'Information': 'Aucune donnée pour cette période' };
      return;
    }

    // Sort by time
    positions.sort((a, b) => new Date(a.recordedAt).getTime() - new Date(b.recordedAt).getTime());

    const type = this.selectedTemplate?.type || 'trips';

    if (type === 'trips') {
      this.processTripReportAllVehicles(positions);
    } else if (type === 'speed') {
      this.processSpeedReportAllVehicles(positions);
    } else {
      // Default: just show positions
      this.tableData = positions.slice(0, 100).map(p => ({
        vehicleName: p.vehicleName,
        time: this.formatDateTime(p.recordedAt),
        speed: `${(p.speedKph || 0).toFixed(0)} km/h`,
        address: p.address || `${p.latitude.toFixed(5)}, ${p.longitude.toFixed(5)}`
      }));
      this.statisticsData = {
        'Véhicules': new Set(positions.map(p => p.vehicleId)).size.toString(),
        'Points GPS': positions.length.toString()
      };
    }
  }

  processTripReportAllVehicles(positions: any[]) {
    // Group by vehicle
    const byVehicle = new Map<number, any[]>();
    positions.forEach(p => {
      const list = byVehicle.get(p.vehicleId) || [];
      list.push(p);
      byVehicle.set(p.vehicleId, list);
    });

    const allTrips: any[] = [];
    let tripNumber = 0;

    byVehicle.forEach((vehiclePositions, vehicleId) => {
      const vehicleName = vehiclePositions[0]?.vehicleName || 'Véhicule';
      
      // Detect trips for this vehicle - require actual movement
      let currentTrip: any = null;
      
      vehiclePositions.forEach((pos, i) => {
        const isIgnitionOn = pos.ignitionOn === true;
        const isMoving = (pos.speedKph || 0) > 2; // Must be moving to count as trip
        const isTripPosition = isIgnitionOn && isMoving;
        
        if (isTripPosition && !currentTrip) {
          currentTrip = { start: pos, end: pos, positions: [pos], vehicleName, vehicleId, hasMovement: true };
        } else if (isTripPosition && currentTrip) {
          currentTrip.end = pos;
          currentTrip.positions.push(pos);
        } else if (!isTripPosition && currentTrip) {
          // Only save if there was actual movement
          if (currentTrip.hasMovement && currentTrip.positions.length > 1) {
            tripNumber++;
            currentTrip.tripNumber = tripNumber;
            allTrips.push(currentTrip);
          }
          currentTrip = null;
        }
      });
      
      if (currentTrip && currentTrip.positions.length > 1 && currentTrip.hasMovement) {
        tripNumber++;
        currentTrip.tripNumber = tripNumber;
        allTrips.push(currentTrip);
      }
    });

    const formatDuration = (minutes: number): string => {
      if (minutes >= 60) {
        const h = Math.floor(minutes / 60);
        const m = Math.round(minutes % 60);
        return m > 0 ? `${h}h ${m}min` : `${h}h`;
      }
      return `${Math.round(minutes)}min`;
    };

    // Calculate distances and prepare table data
    const tripResults = allTrips.map(trip => {
      const startTime = new Date(trip.start.recordedAt);
      const endTime = new Date(trip.end.recordedAt);
      const durationMin = (endTime.getTime() - startTime.getTime()) / 60000;
      
      // Try odometer first for accurate distance
      let distanceKm = 0;
      if (trip.end.odometerKm && trip.start.odometerKm && trip.end.odometerKm > trip.start.odometerKm) {
        distanceKm = trip.end.odometerKm - trip.start.odometerKm;
      } else {
        // Fallback to haversine calculation
        for (let i = 1; i < trip.positions.length; i++) {
          distanceKm += this.haversineDistance(
            trip.positions[i-1].latitude, trip.positions[i-1].longitude,
            trip.positions[i].latitude, trip.positions[i].longitude
          );
        }
      }
      
      // If still 0, estimate from average speed
      if (distanceKm < 0.1 && durationMin > 1) {
        const speeds = trip.positions.map((p: any) => p.speedKph || 0).filter((s: number) => s > 0);
        if (speeds.length > 0) {
          const avgSpeedFromPositions = speeds.reduce((a: number, b: number) => a + b, 0) / speeds.length;
          distanceKm = avgSpeedFromPositions * (durationMin / 60);
        }
      }
      
      const avgSpeed = durationMin > 0 && distanceKm > 0 ? distanceKm / (durationMin / 60) : 0;
      const maxSpeed = Math.max(...trip.positions.map((p: any) => p.speedKph || 0));

      return {
        vehicleName: trip.vehicleName,
        vehicleId: trip.vehicleId,
        isTrip: true,
        tripNumber: trip.tripNumber,
        startTime: this.formatDateTime(trip.start.recordedAt),
        endTime: this.formatDateTime(trip.end.recordedAt),
        duration: formatDuration(durationMin),
        durationMin,
        distance: `${distanceKm.toFixed(1)} km`,
        distanceKm,
        avgSpeed: `${avgSpeed.toFixed(0)} km/h`,
        maxSpeed: `${maxSpeed.toFixed(0)} km/h`,
        startAddress: trip.start.address || `${trip.start.latitude.toFixed(4)}°`,
        endAddress: trip.end.address || `${trip.end.latitude.toFixed(4)}°`
      };
    });

    this.tableData = tripResults.sort((a, b) => {
      const timeA = new Date(a.startTime.split('/').reverse().join('-')).getTime();
      const timeB = new Date(b.startTime.split('/').reverse().join('-')).getTime();
      return timeB - timeA;
    });
    const totalDistance = tripResults.reduce((sum, t) => sum + t.distanceKm, 0);
    const totalDurationMin = tripResults.reduce((sum, t) => sum + t.durationMin, 0);

    // Chart 1: Distance by vehicle (bar chart)
    const distanceByVehicle = new Map<string, number>();
    tripResults.forEach(t => {
      const current = distanceByVehicle.get(t.vehicleName) || 0;
      distanceByVehicle.set(t.vehicleName, current + t.distanceKm);
    });

    this.chartData = Array.from(distanceByVehicle.entries())
      .sort((a, b) => b[1] - a[1])
      .slice(0, 15)
      .map(([name, distance], i) => ({
        label: name,
        value: Math.round(distance * 10) / 10,
        color: this.chartColors[i % this.chartColors.length]
      }));

    this.statisticsData = {
      '🚗 Véhicules': byVehicle.size.toString(),
      '🛣️ Nombre de trajets': allTrips.length.toString(),
      '📏 Distance totale': `${totalDistance.toFixed(1)} km`,
      '⏱️ Temps total': formatDuration(totalDurationMin),
      '⌀ Distance/trajet': `${(totalDistance / allTrips.length || 0).toFixed(1)} km`
    };

    this.secondaryChartData = [];
  }

  processSpeedReportAllVehicles(positions: any[]) {
    // Filter only positions with speed > 50 km/h
    const highSpeedPositions = positions.filter(p => (p.speedKph || 0) > 50);
    
    if (highSpeedPositions.length === 0) {
      this.tableData = [];
      this.chartData = [];
      this.statisticsData = { 'Information': 'Aucune donnée de vitesse > 50 km/h pour cette période' };
      return;
    }

    // Build vehicle speed limit map from this.vehicles
    const vehicleLimitMap = new Map<number, number>();
    this.vehicles.forEach(v => {
      vehicleLimitMap.set(v.id, v.speedLimit || 120);
    });

    // Speed ranges (only > 50 km/h)
    const ranges = [
      { label: '50-70 km/h', min: 50, max: 70, color: '#EAB308' },
      { label: '70-90 km/h', min: 70, max: 90, color: '#F97316' },
      { label: '90-110 km/h', min: 90, max: 110, color: '#EF4444' },
      { label: '>110 km/h', min: 110, max: 999, color: '#DC2626' }
    ];

    const rangeCounts = ranges.map(r => ({
      ...r,
      count: highSpeedPositions.filter(p => (p.speedKph || 0) >= r.min && (p.speedKph || 0) < r.max).length
    }));

    this.chartData = rangeCounts.filter(r => r.count > 0).map(r => ({
      label: r.label,
      value: r.count,
      color: r.color,
      percentage: ((r.count / highSpeedPositions.length) * 100).toFixed(1)
    }));

    // Sort by speed descending to show highest first
    const sorted = [...highSpeedPositions].sort((a, b) => (b.speedKph || 0) - (a.speedKph || 0));
    
    // Take top 100 highest speed records
    const topRecords = sorted.slice(0, 100);
    
    this.tableData = topRecords.map(p => {
      const speed = p.speedKph || 0;
      const vehicleLimit = vehicleLimitMap.get(p.vehicleId) || 120;
      const exceedsLimit = speed > vehicleLimit;
      const excess = exceedsLimit ? speed - vehicleLimit : 0;
      
      return {
        vehicleName: p.vehicleName,
        time: this.formatDateTime(p.recordedAt),
        speed: `${speed.toFixed(0)} km/h`,
        speedValue: speed,
        vehicleLimit: `${vehicleLimit} km/h`,
        vehicleLimitValue: vehicleLimit,
        excess: exceedsLimit ? `+${excess.toFixed(0)} km/h` : '-',
        excessValue: excess,
        exceedsLimit: exceedsLimit,
        address: p.address || `${p.latitude.toFixed(5)}, ${p.longitude.toFixed(5)}`,
        latitude: p.latitude,
        longitude: p.longitude
      };
    });

    // Count infractions (exceeding vehicle limit)
    const infractions = highSpeedPositions.filter(p => {
      const vehicleLimit = vehicleLimitMap.get(p.vehicleId) || 120;
      return (p.speedKph || 0) > vehicleLimit;
    });

    // Secondary chart: infractions by vehicle
    const infractionsByVehicle: { [key: string]: number } = {};
    infractions.forEach(p => {
      const name = p.vehicleName || `Véhicule ${p.vehicleId}`;
      infractionsByVehicle[name] = (infractionsByVehicle[name] || 0) + 1;
    });
    
    this.secondaryChartData = Object.entries(infractionsByVehicle)
      .sort((a, b) => b[1] - a[1])
      .slice(0, 10)
      .map(([label, value]) => ({ label, value, color: '#EF4444' }));

    const speeds = highSpeedPositions.map(p => p.speedKph || 0);
    const avgSpeed = speeds.reduce((a, b) => a + b, 0) / speeds.length;
    const maxSpeed = Math.max(...speeds);

    this.statisticsData = {
      'Points > 50 km/h': highSpeedPositions.length.toString(),
      'Véhicules': new Set(highSpeedPositions.map(p => p.vehicleId)).size.toString(),
      'Vitesse moy': `${avgSpeed.toFixed(0)} km/h`,
      'Vitesse max': `${maxSpeed.toFixed(0)} km/h`,
      '🔴 Dépassements limite': infractions.length.toString(),
      'Véhicules en infraction': Object.keys(infractionsByVehicle).length.toString()
    };
  }

  executeStopsReport(vehicleId: number, startDate?: Date, endDate?: Date) {
    console.log('executeStopsReport called with:', { vehicleId, startDate, endDate });
    this.apiService.getVehicleStops(vehicleId, startDate, endDate).pipe(takeUntil(this.destroy$)).subscribe({
      next: (result) => {
        this.ngZone.run(() => {
          this.processStopsFromApi(result);
          this.reportGenerated = true;
          this.loading = false;
          this.activeTab = 'table';
          this.currentPage = 1;
          this.cdr.detectChanges();
          this.appRef.tick();
          setTimeout(() => this.createChart(), 100);
        });
      },
      error: (err) => {
        this.ngZone.run(() => {
          console.error('Error loading stops report:', err);
          this.tableData = [];
          this.chartData = [];
          this.statisticsData = { 'Erreur': 'Impossible de charger le rapport des arrêts' };
          this.reportGenerated = true;
          this.loading = false;
          this.cdr.detectChanges();
          this.appRef.tick();
        });
      }
    });
  }

  executeStopsReportAllVehicles(startDate?: Date, endDate?: Date) {
    console.log('executeStopsReportAllVehicles called with:', { startDate, endDate });
    const allStops: any[] = [];
    let completedRequests = 0;
    const totalVehicles = this.vehicles.length;

    if (totalVehicles === 0) {
      this.ngZone.run(() => {
        this.tableData = [];
        this.chartData = [];
        this.statisticsData = { 'Information': 'Aucun véhicule disponible' };
        this.reportGenerated = true;
        this.loading = false;
        this.cdr.detectChanges();
      });
      return;
    }

    this.vehicles.forEach(vehicle => {
      this.apiService.getVehicleStops(vehicle.id, startDate, endDate).pipe(takeUntil(this.destroy$)).subscribe({
        next: (result) => {
          // Add vehicle info to each stop
          const stopsWithVehicle = result.items.map(stop => ({
            ...stop,
            vehicleName: vehicle.name || vehicle.brand + ' ' + vehicle.model,
            vehiclePlate: vehicle.plate
          }));
          allStops.push(...stopsWithVehicle);
          completedRequests++;

          if (completedRequests === totalVehicles) {
            this.ngZone.run(() => {
              this.processStopsFromApiAllVehicles(allStops);
              this.reportGenerated = true;
              this.loading = false;
              this.activeTab = 'table';
              this.currentPage = 1;
              this.cdr.detectChanges();
              this.appRef.tick();
              setTimeout(() => this.createChart(), 100);
            });
          }
        },
        error: () => {
          completedRequests++;
          if (completedRequests === totalVehicles) {
            this.ngZone.run(() => {
              this.processStopsFromApiAllVehicles(allStops);
              this.reportGenerated = true;
              this.loading = false;
              this.activeTab = 'table';
              this.currentPage = 1;
              this.cdr.detectChanges();
              this.appRef.tick();
              setTimeout(() => this.createChart(), 100);
            });
          }
        }
      });
    });
  }

  processStopsFromApiAllVehicles(stops: any[]) {
    if (!stops || stops.length === 0) {
      this.tableData = [];
      this.chartData = [];
      this.statisticsData = { 'Information': 'Aucun arrêt trouvé pour cette période' };
      return;
    }

    // Filter out short "Circulation" stops (ignition ON, < 10 min) — traffic lights, brief pauses
    // Only keep real stops: ignition OFF (parked) or long ignition-ON pauses (> 10 min)
    const meaningfulStops = stops.filter((stop: any) => {
      if (stop.ignitionOff) return true; // Always keep real parked stops
      return stop.durationSeconds >= 600; // Keep circulation stops only if >= 10 min
    });

    // Sort by start time descending (most recent first)
    meaningfulStops.sort((a: any, b: any) => new Date(b.startTime).getTime() - new Date(a.startTime).getTime());

    const formatDuration = (seconds: number): string => {
      const minutes = seconds / 60;
      if (minutes >= 60) {
        const hours = Math.floor(minutes / 60);
        const mins = Math.round(minutes % 60);
        return mins > 0 ? `${hours}h ${mins}min` : `${hours}h`;
      }
      return `${Math.round(minutes)}min`;
    };

    this.tableData = meaningfulStops.map((stop: any) => {
      const stopTypeCode = stop.ignitionOff ? 'A' : 'C';
      const stopTypeLabel = stop.ignitionOff ? '🅿️ Arrêt' : '🚦 Ralenti prolongé';
      const durationMinutes = stop.durationSeconds / 60;

      return {
        vehicleName: stop.vehicleName || stop.vehiclePlate,
        time: this.formatDateTime(stop.startTime),
        endTime: stop.endTime ? this.formatDateTime(stop.endTime) : '-',
        duration: formatDuration(stop.durationSeconds),
        durationSeconds: stop.durationSeconds,
        address: stop.address || `${stop.latitude.toFixed(5)}, ${stop.longitude.toFixed(5)}`,
        typeCode: stopTypeCode,
        typeLabel: stopTypeLabel,
        ignitionOff: stop.ignitionOff,
        isLongStop: durationMinutes > 30,
        geofenceName: stop.geofenceName
      };
    });

    // Chart data - by type
    const typeA = this.tableData.filter((s: any) => s.typeCode === 'A');
    const typeC = this.tableData.filter((s: any) => s.typeCode === 'C');
    const totalTypeASeconds = typeA.reduce((sum: number, s: any) => sum + s.durationSeconds, 0);
    const totalTypeCSeconds = typeC.reduce((sum: number, s: any) => sum + s.durationSeconds, 0);

    this.chartData = [
      { label: '🅿️ Arrêts (A)', value: Math.round(totalTypeASeconds / 60), count: typeA.length, color: '#3B82F6' },
      { label: '🚦 Circulation (C)', value: Math.round(totalTypeCSeconds / 60), count: typeC.length, color: '#F59E0B' }
    ];

    // Statistics
    const totalDurationSeconds = stops.reduce((sum: number, s: any) => sum + s.durationSeconds, 0);
    const vehiclesWithStops = new Set(stops.map((s: any) => s.vehicleId || s.vehicleName)).size;

    this.statisticsData = {
      'Véhicules': vehiclesWithStops.toString(),
      'Total arrêts': stops.length.toString(),
      '🅿️ Arrêts (A)': `${typeA.length} (${formatDuration(totalTypeASeconds)})`,
      '🚦 Circulation (C)': `${typeC.length} (${formatDuration(totalTypeCSeconds)})`,
      'Durée totale': formatDuration(totalDurationSeconds)
    };

    // Secondary chart: Stops by vehicle
    const stopsByVehicle = new Map<string, number>();
    this.tableData.forEach((s: any) => {
      const current = stopsByVehicle.get(s.vehicleName) || 0;
      stopsByVehicle.set(s.vehicleName, current + 1);
    });

    this.secondaryChartData = Array.from(stopsByVehicle.entries())
      .sort((a, b) => b[1] - a[1])
      .slice(0, 10)
      .map(([name, count]) => ({ label: name, value: count }));
  }

  executeSpeedInfractionReport(start?: Date, end?: Date) {
    const now = new Date();
    const startDate = start || new Date(now.getFullYear(), now.getMonth(), now.getDate(), 0, 0, 0);
    const endDate = end || now;
    const selectedVehicleId = this.selectedVehicleId ? parseInt(this.selectedVehicleId) : null;
    
    // Fetch vehicles — filter by selected vehicle if one is chosen
    this.apiService.getVehicles().pipe(takeUntil(this.destroy$)).subscribe({
      next: (vehicles) => {
        let targetVehicles = vehicles;
        if (selectedVehicleId) {
          targetVehicles = vehicles.filter(v => v.id === selectedVehicleId);
        }

        const allInfractions: any[] = [];
        let completedRequests = 0;
        const totalVehicles = targetVehicles.length;
        
        if (totalVehicles === 0) {
          this.ngZone.run(() => {
            this.processSpeedInfractionReport([]);
            this.reportGenerated = true;
            this.loading = false;
            this.activeTab = 'table';
            this.currentPage = 1;
            this.cdr.detectChanges();
          });
          return;
        }
        
        targetVehicles.forEach(vehicle => {
          // Use custom limit if set, otherwise use vehicle's configured limit from DB (default 120)
          const vehicleSpeedLimit = this.speedLimit 
            ? this.speedLimit 
            : (vehicle.speedLimit || 120);
          
          this.apiService.getVehicleHistory(vehicle.id, startDate, endDate, 10000).pipe(takeUntil(this.destroy$)).subscribe({
            next: (positions) => {
              const infractions = positions
                .filter((p: any) => (p.speedKph || 0) > vehicleSpeedLimit)
                .map((p: any) => ({
                  vehicleId: vehicle.id,
                  vehicleName: vehicle.name || vehicle.plate,
                  vehiclePlate: vehicle.plate,
                  time: p.recordedAt,
                  latitude: p.latitude,
                  longitude: p.longitude,
                  address: p.address,
                  speed: p.speedKph || 0,
                  limit: vehicleSpeedLimit,
                  configuredLimit: vehicle.speedLimit || 120,
                  excess: (p.speedKph || 0) - vehicleSpeedLimit
                }));
              allInfractions.push(...infractions);
              completedRequests++;
              
              if (completedRequests === totalVehicles) {
                this.ngZone.run(() => {
                  this.processSpeedInfractionReport(allInfractions);
                  this.reportGenerated = true;
                  this.loading = false;
                  this.activeTab = 'table';
                  this.currentPage = 1;
                  this.cdr.detectChanges();
                  this.appRef.tick();
                  setTimeout(() => this.createChart(), 100);
                });
              }
            },
            error: () => {
              completedRequests++;
              if (completedRequests === totalVehicles) {
                this.ngZone.run(() => {
                  this.processSpeedInfractionReport(allInfractions);
                  this.reportGenerated = true;
                  this.loading = false;
                  this.activeTab = 'table';
                  this.currentPage = 1;
                  this.cdr.detectChanges();
                  this.appRef.tick();
                  setTimeout(() => this.createChart(), 100);
                });
              }
            }
          });
        });
      },
      error: (err) => {
        console.error('Error fetching vehicles:', err);
        this.ngZone.run(() => {
          this.tableData = [];
          this.chartData = [];
          this.statisticsData = { 'Erreur': 'Impossible de charger les véhicules' };
          this.reportGenerated = true;
          this.loading = false;
          this.cdr.detectChanges();
        });
      }
    });
  }

  processSpeedInfractionReport(infractions: any[]) {
    // Sort by time descending
    infractions.sort((a, b) => new Date(b.time).getTime() - new Date(a.time).getTime());
    
    if (infractions.length === 0) {
      this.tableData = [];
      this.chartData = [];
      this.statisticsData = { 'Information': `Aucune infraction au-dessus de ${this.speedLimit} km/h` };
      return;
    }
    
    // Process table data
    this.tableData = infractions.map(inf => ({
      vehicle: inf.vehicleName || inf.vehiclePlate,
      time: this.formatDateTime(inf.time),
      address: inf.address || `${inf.latitude.toFixed(5)}, ${inf.longitude.toFixed(5)}`,
      latitude: inf.latitude,
      longitude: inf.longitude,
      speed: `${inf.speed.toFixed(1)} km/h`,
      limit: `${inf.limit} km/h`,
      excess: `+${inf.excess.toFixed(1)} km/h`,
      excessValue: inf.excess,
      isAnomaly: inf.excess > 30
    }));
    
    // Fetch addresses for rows without one
    this.enrichSpeedInfractionAddresses();
    
    // Chart data - group by vehicle
    const byVehicle: { [key: string]: number } = {};
    infractions.forEach(inf => {
      const name = inf.vehicleName || inf.vehiclePlate;
      byVehicle[name] = (byVehicle[name] || 0) + 1;
    });
    
    // If single vehicle selected, show infractions by hour instead
    const vehicleCount = Object.keys(byVehicle).length;
    if (vehicleCount <= 1) {
      // Time distribution: infractions by hour of day
      const byHour: { [key: number]: number } = {};
      infractions.forEach(inf => {
        const hour = new Date(inf.time).getHours();
        byHour[hour] = (byHour[hour] || 0) + 1;
      });
      this.chartData = Array.from({ length: 24 }, (_, h) => ({
        label: `${h.toString().padStart(2, '0')}h`,
        value: byHour[h] || 0,
        color: (byHour[h] || 0) > 3 ? '#EF4444' : (byHour[h] || 0) > 0 ? '#F59E0B' : '#334155'
      }));
    } else {
      this.chartData = Object.entries(byVehicle)
        .sort((a, b) => b[1] - a[1])
        .slice(0, 10)
        .map(([label, value]) => ({ label, value }));
    }
    
    // Secondary chart data - severity distribution
    const light = infractions.filter(i => i.excess <= 15).length;
    const medium = infractions.filter(i => i.excess > 15 && i.excess <= 30).length;
    const severe = infractions.filter(i => i.excess > 30).length;
    this.secondaryChartData = [
      { label: '🟢 Léger (≤15 km/h)', value: light, color: '#22C55E' },
      { label: '🟡 Modéré (15-30 km/h)', value: medium, color: '#F59E0B' },
      { label: '🔴 Grave (>30 km/h)', value: severe, color: '#EF4444' }
    ].filter(d => d.value > 0);
    
    // Statistics
    const maxSpeed = Math.max(...infractions.map(i => i.speed));
    const avgExcess = infractions.reduce((sum, i) => sum + i.excess, 0) / infractions.length;
    const severeCount = infractions.filter(i => i.excess > 30).length;
    const lightPct = ((light / infractions.length) * 100).toFixed(0);
    const mediumPct = ((medium / infractions.length) * 100).toFixed(0);
    const severePct = ((severeCount / infractions.length) * 100).toFixed(0);
    
    this.statisticsData = {
      '⚠️ Total infractions': infractions.length.toString(),
      '🏎️ Vitesse max': `${maxSpeed.toFixed(1)} km/h`,
      '📊 Excès moyen': `+${avgExcess.toFixed(1)} km/h`,
      '🟢 Léger (≤15)': `${light} (${lightPct}%)`,
      '🟡 Modéré (15-30)': `${medium} (${mediumPct}%)`,
      '🔴 Grave (>30)': `${severeCount} (${severePct}%)`,
      '🚗 Véhicules concernés': vehicleCount.toString()
    };
  }

  enrichSpeedInfractionAddresses() {
    this.tableData.forEach((row: any, index: number) => {
      if (row.address?.includes(',') && row.latitude && row.longitude) {
        this.geocodingService.reverseGeocode(row.latitude, row.longitude).pipe(takeUntil(this.destroy$)).subscribe({
          next: (address) => {
            if (address) {
              this.ngZone.run(() => {
                this.tableData[index] = { ...this.tableData[index], address };
                this.cdr.detectChanges();
              });
            }
          }
        });
      }
    });
  }

  executeDrivingBehaviorReport(start?: Date, end?: Date) {
    const now = new Date();
    const startDate = start || new Date(now.getFullYear(), now.getMonth(), now.getDate(), 0, 0, 0);
    const endDate = end || now;
    const selectedVehicleId = this.selectedVehicleId ? parseInt(this.selectedVehicleId) : null;
    
    // Fetch vehicles — filter by selected vehicle if one is chosen
    this.apiService.getVehicles().pipe(takeUntil(this.destroy$)).subscribe({
      next: (vehicles) => {
        let targetVehicles = vehicles;
        if (selectedVehicleId) {
          targetVehicles = vehicles.filter(v => v.id === selectedVehicleId);
        }

        const allIncidents: any[] = [];
        let completedRequests = 0;
        const totalVehicles = targetVehicles.length;
        
        if (totalVehicles === 0) {
          this.ngZone.run(() => {
            this.processDrivingBehaviorReport([]);
            this.reportGenerated = true;
            this.loading = false;
            this.activeTab = 'table';
            this.currentPage = 1;
            this.cdr.detectChanges();
          });
          return;
        }
        
        targetVehicles.forEach(vehicle => {
          this.apiService.getVehicleHistory(vehicle.id, startDate, endDate, 10000).pipe(takeUntil(this.destroy$)).subscribe({
            next: (positions) => {
              const incidents = this.detectDrivingIncidents(positions, vehicle);
              allIncidents.push(...incidents);
              completedRequests++;
              
              if (completedRequests === totalVehicles) {
                this.ngZone.run(() => {
                  this.processDrivingBehaviorReport(allIncidents);
                  this.reportGenerated = true;
                  this.loading = false;
                  this.activeTab = 'table';
                  this.currentPage = 1;
                  this.cdr.detectChanges();
                  this.appRef.tick();
                  setTimeout(() => this.createChart(), 100);
                });
              }
            },
            error: () => {
              completedRequests++;
              if (completedRequests === totalVehicles) {
                this.ngZone.run(() => {
                  this.processDrivingBehaviorReport(allIncidents);
                  this.reportGenerated = true;
                  this.loading = false;
                  this.activeTab = 'table';
                  this.currentPage = 1;
                  this.cdr.detectChanges();
                  this.appRef.tick();
                  setTimeout(() => this.createChart(), 100);
                });
              }
            }
          });
        });
      },
      error: (err) => {
        console.error('Error fetching vehicles:', err);
        this.ngZone.run(() => {
          this.tableData = [];
          this.chartData = [];
          this.statisticsData = { 'Erreur': 'Impossible de charger les véhicules' };
          this.reportGenerated = true;
          this.loading = false;
          this.cdr.detectChanges();
        });
      }
    });
  }

  detectDrivingIncidents(positions: any[], vehicle: any): any[] {
    const rawIncidents: any[] = [];
    
    for (let i = 1; i < positions.length; i++) {
      const prev = positions[i - 1];
      const curr = positions[i];
      
      const timeDiff = (new Date(curr.recordedAt).getTime() - new Date(prev.recordedAt).getTime()) / 1000;
      if (timeDiff <= 0 || timeDiff > 300) continue; // Skip if time gap is invalid or too large
      
      const speedDiff = (curr.speedKph || 0) - (prev.speedKph || 0);
      // speedDiff is km/h, timeDiff is seconds → divide by 3.6 to get m/s²
      const acceleration = speedDiff / timeDiff / 3.6;
      
      // Detect harsh acceleration (> 3 m/s²)
      if (this.drivingBehaviorFilters['harshAcceleration'] && acceleration > 3) {
        rawIncidents.push({
          type: 'harshAcceleration',
          vehicleId: vehicle.id,
          vehicleName: vehicle.name || vehicle.plate,
          time: curr.recordedAt,
          latitude: curr.latitude,
          longitude: curr.longitude,
          address: curr.address,
          value: acceleration,
          valueFormatted: `+${acceleration.toFixed(1)} m/s²`,
          severity: acceleration > 5 ? 'high' : acceleration > 4 ? 'medium' : 'low'
        });
      }
      
      // Detect harsh braking (< -3 m/s²)
      if (this.drivingBehaviorFilters['harshBraking'] && acceleration < -3) {
        rawIncidents.push({
          type: 'harshBraking',
          vehicleId: vehicle.id,
          vehicleName: vehicle.name || vehicle.plate,
          time: curr.recordedAt,
          latitude: curr.latitude,
          longitude: curr.longitude,
          address: curr.address,
          value: Math.abs(acceleration),
          valueFormatted: `${acceleration.toFixed(1)} m/s²`,
          severity: acceleration < -5 ? 'high' : acceleration < -4 ? 'medium' : 'low'
        });
      }
      
      // Detect sharp steering (heading change > 45° in short time)
      if (this.drivingBehaviorFilters['sharpSteering'] && prev.heading !== undefined && curr.heading !== undefined) {
        let headingDiff = Math.abs(curr.heading - prev.heading);
        if (headingDiff > 180) headingDiff = 360 - headingDiff;
        
        if (headingDiff > 45 && (curr.speedKph || 0) > 20) {
          rawIncidents.push({
            type: 'sharpSteering',
            vehicleId: vehicle.id,
            vehicleName: vehicle.name || vehicle.plate,
            time: curr.recordedAt,
            latitude: curr.latitude,
            longitude: curr.longitude,
            address: curr.address,
            value: headingDiff,
            valueFormatted: `${headingDiff.toFixed(0)}°`,
            severity: headingDiff > 90 ? 'high' : headingDiff > 60 ? 'medium' : 'low'
          });
        }
      }
      
      // Detect overspeed (> 130 km/h)
      if (this.drivingBehaviorFilters['overspeed'] && (curr.speedKph || 0) > 130) {
        rawIncidents.push({
          type: 'overspeed',
          vehicleId: vehicle.id,
          vehicleName: vehicle.name || vehicle.plate,
          time: curr.recordedAt,
          latitude: curr.latitude,
          longitude: curr.longitude,
          address: curr.address,
          value: curr.speedKph,
          valueFormatted: `${curr.speedKph.toFixed(0)} km/h`,
          severity: curr.speedKph > 160 ? 'high' : curr.speedKph > 145 ? 'medium' : 'low'
        });
      }
      
      // Detect high RPM (> 3500)
      if (this.drivingBehaviorFilters['highRpm'] && curr.rpm && curr.rpm > 3500) {
        rawIncidents.push({
          type: 'highRpm',
          vehicleId: vehicle.id,
          vehicleName: vehicle.name || vehicle.plate,
          time: curr.recordedAt,
          latitude: curr.latitude,
          longitude: curr.longitude,
          address: curr.address,
          value: curr.rpm,
          valueFormatted: `${curr.rpm} RPM`,
          severity: curr.rpm > 5000 ? 'high' : curr.rpm > 4000 ? 'medium' : 'low'
        });
      }
    }
    
    // Deduplicate: for same vehicle + same type + same minute, keep only highest value
    const severityRank: any = { 'high': 3, 'medium': 2, 'low': 1 };
    const deduped = new Map<string, any>();
    for (const inc of rawIncidents) {
      const t = new Date(inc.time);
      const minuteKey = `${inc.vehicleId}_${inc.type}_${t.getFullYear()}-${t.getMonth()}-${t.getDate()}-${t.getHours()}-${t.getMinutes()}`;
      const existing = deduped.get(minuteKey);
      if (!existing || inc.value > existing.value) {
        deduped.set(minuteKey, inc);
      }
    }
    
    return Array.from(deduped.values());
  }

  processDrivingBehaviorReport(incidents: any[]) {
    // Sort by time descending
    incidents.sort((a, b) => new Date(b.time).getTime() - new Date(a.time).getTime());
    
    if (incidents.length === 0) {
      this.tableData = [];
      this.chartData = [];
      this.statisticsData = { 'Information': 'Aucun incident de conduite détecté' };
      return;
    }
    
    // Get incident type info
    const getIncidentInfo = (type: string) => {
      return this.incidentTypes.find(i => i.key === type) || { label: type, color: '#888', icon: '❓' };
    };
    
    // Process table data
    this.tableData = incidents.map(inc => {
      const info = getIncidentInfo(inc.type);
      return {
        vehicle: inc.vehicleName,
        time: new Date(inc.time).toLocaleString('fr-FR', {
          day: '2-digit', month: '2-digit', year: 'numeric',
          hour: '2-digit', minute: '2-digit'
        }),
        incidentType: info.label,
        incidentIcon: info.icon,
        incidentColor: info.color,
        address: inc.address || `${inc.latitude.toFixed(5)}, ${inc.longitude.toFixed(5)}`,
        latitude: inc.latitude,
        longitude: inc.longitude,
        value: inc.valueFormatted,
        severity: inc.severity,
        severityLabel: inc.severity === 'high' ? '🔴 Grave' : inc.severity === 'medium' ? '🟡 Modéré' : '🟢 Léger'
      };
    });
    
    // Fetch addresses for rows without one
    this.enrichDrivingBehaviorAddresses();
    
    // Group by incident type with colors
    const byType: { [key: string]: { count: number; color: string; icon: string } } = {};
    incidents.forEach(inc => {
      const info = getIncidentInfo(inc.type);
      if (!byType[info.label]) byType[info.label] = { count: 0, color: info.color, icon: info.icon };
      byType[info.label].count++;
    });
    
    // Group by vehicle
    const byVehicle: { [key: string]: number } = {};
    incidents.forEach(inc => {
      byVehicle[inc.vehicleName] = (byVehicle[inc.vehicleName] || 0) + 1;
    });
    const vehicleCount = Object.keys(byVehicle).length;
    
    // Primary chart: by incident type (always useful)
    this.chartData = Object.entries(byType)
      .sort((a, b) => b[1].count - a[1].count)
      .map(([label, data]) => ({ label: `${data.icon} ${label}`, value: data.count, color: data.color }));
    
    // Severity stats
    const bySeverity = {
      high: incidents.filter(i => i.severity === 'high').length,
      medium: incidents.filter(i => i.severity === 'medium').length,
      low: incidents.filter(i => i.severity === 'low').length
    };
    
    // Secondary chart: severity distribution (doughnut)
    this.secondaryChartData = [
      { label: '🟢 Léger', value: bySeverity.low, color: '#22C55E' },
      { label: '🟡 Modéré', value: bySeverity.medium, color: '#F59E0B' },
      { label: '🔴 Grave', value: bySeverity.high, color: '#EF4444' }
    ].filter(d => d.value > 0);
    
    // Statistics with percentages
    const highPct = ((bySeverity.high / incidents.length) * 100).toFixed(0);
    const mediumPct = ((bySeverity.medium / incidents.length) * 100).toFixed(0);
    const lowPct = ((bySeverity.low / incidents.length) * 100).toFixed(0);
    
    // Find worst incident type
    const worstType = Object.entries(byType).sort((a, b) => b[1].count - a[1].count)[0];
    
    this.statisticsData = {
      '🚨 Total incidents': incidents.length.toString(),
      '🔴 Graves': `${bySeverity.high} (${highPct}%)`,
      '🟡 Modérés': `${bySeverity.medium} (${mediumPct}%)`,
      '🟢 Légers': `${bySeverity.low} (${lowPct}%)`,
      '⚠️ Type dominant': worstType ? `${worstType[0]} (${worstType[1].count})` : '-',
      '🚗 Véhicules': vehicleCount.toString()
    };
  }

  enrichDrivingBehaviorAddresses() {
    this.tableData.forEach((row: any, index: number) => {
      if (row.address?.includes(',') && row.latitude && row.longitude) {
        this.geocodingService.reverseGeocode(row.latitude, row.longitude).pipe(takeUntil(this.destroy$)).subscribe({
          next: (address) => {
            if (address) {
              this.ngZone.run(() => {
                this.tableData[index] = { ...this.tableData[index], address };
                this.cdr.detectChanges();
              });
            }
          }
        });
      }
    });
  }

  executeDailyReport(vehicleId: number, date?: Date) {
    // Default to J-1 (yesterday) if no date provided
    const reportDate = date || (() => {
      const yesterday = new Date();
      yesterday.setDate(yesterday.getDate() - 1);
      return yesterday;
    })();
    
    this.apiService.getDailyReport(vehicleId, reportDate).pipe(takeUntil(this.destroy$)).subscribe({
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
          setTimeout(() => this.createChart(), 100);
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
    const reportDateFormatted = new Date(report.reportDate).toLocaleDateString('fr-FR', { 
      weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' 
    });

    if (!report.hasActivity) {
      this.tableData = [];
      this.chartData = [];
      this.statisticsData = {
        'Véhicule': report.vehicleName,
        'Date': reportDateFormatted,
        'Information': 'Aucune activité enregistrée pour cette journée'
      };
      return;
    }

    // Build comprehensive daily timeline with all events
    const events: any[] = [];
    let eventNumber = 0;

    // Add first start event
    if (report.firstStart) {
      eventNumber++;
      events.push({
        eventNumber,
        time: this.formatDateTime(report.firstStart.timestamp),
        type: 'start',
        typeIcon: '🔑',
        typeLabel: 'Démarrage',
        description: 'Premier démarrage de la journée',
        address: report.firstStart.address || `${report.firstStart.latitude.toFixed(4)}°, ${report.firstStart.longitude.toFixed(4)}°`,
        latitude: report.firstStart.latitude,
        longitude: report.firstStart.longitude,
        duration: '-',
        distance: '-',
        speed: '-'
      });
    }

    // Process all activities — insert implicit stops between trips if there's a gap
    let driveNumber = 0;
    let stopNumber = 0;
    let lastEndTime: string | null = null;
    let lastEndLocation: any = null;

    report.activities.forEach((activity: ActivitySegment, idx: number) => {
      // Check for gap between previous activity end and this activity start
      if (lastEndTime && activity.startTime) {
        const gapMs = new Date(activity.startTime).getTime() - new Date(lastEndTime).getTime();
        const gapSeconds = Math.floor(gapMs / 1000);
        if (gapSeconds > 120) { // Gap > 2 minutes → insert implicit stop
          stopNumber++;
          eventNumber++;
          const gapFormatted = gapSeconds >= 3600
            ? `${Math.floor(gapSeconds / 3600)}h ${Math.floor((gapSeconds % 3600) / 60)}m`
            : `${Math.floor(gapSeconds / 60)}m`;
          const stopAddr = lastEndLocation?.address || 'Lieu inconnu';
          events.push({
            eventNumber,
            time: `${this.formatDateTime(lastEndTime)} → ${this.formatDateTime(activity.startTime)}`,
            type: 'stop',
            typeIcon: '🅿️',
            typeLabel: `Arrêt ${stopNumber}`,
            description: stopAddr,
            address: stopAddr,
            latitude: lastEndLocation?.latitude || 0,
            longitude: lastEndLocation?.longitude || 0,
            duration: gapFormatted,
            distance: '-',
            speed: '-',
            durationSeconds: gapSeconds,
            _sortTime: new Date(lastEndTime).getTime()
          });
        }
      }

      eventNumber++;
      if (activity.type === 'drive') {
        driveNumber++;
        events.push({
          eventNumber,
          time: `${this.formatDateTime(activity.startTime)} → ${activity.endTime ? this.formatDateTime(activity.endTime) : '...'}`,
          type: 'drive',
          typeIcon: '🚗',
          typeLabel: `Trajet ${driveNumber}`,
          description: `${activity.startLocation.address || '?'} → ${activity.endLocation?.address || '?'}`,
          address: activity.endLocation?.address || activity.startLocation.address,
          latitude: activity.endLocation?.latitude || activity.startLocation.latitude,
          longitude: activity.endLocation?.longitude || activity.startLocation.longitude,
          duration: activity.durationFormatted,
          distance: `${(activity.distanceKm || 0).toFixed(1)} km`,
          speed: `max ${activity.maxSpeedKph || 0} km/h`,
          distanceKm: activity.distanceKm || 0,
          durationSeconds: activity.durationSeconds,
          _sortTime: new Date(activity.startTime).getTime()
        });
        lastEndTime = activity.endTime || activity.startTime;
        lastEndLocation = activity.endLocation || activity.startLocation;
      } else {
        stopNumber++;
        events.push({
          eventNumber,
          time: `${this.formatDateTime(activity.startTime)} → ${activity.endTime ? this.formatDateTime(activity.endTime) : '...'}`,
          type: 'stop',
          typeIcon: '🅿️',
          typeLabel: `Arrêt ${stopNumber}`,
          description: activity.startLocation.address || 'Lieu inconnu',
          address: activity.startLocation.address || `${activity.startLocation.latitude.toFixed(4)}°, ${activity.startLocation.longitude.toFixed(4)}°`,
          latitude: activity.startLocation.latitude,
          longitude: activity.startLocation.longitude,
          duration: activity.durationFormatted,
          distance: '-',
          speed: '-',
          durationSeconds: activity.durationSeconds,
          _sortTime: new Date(activity.startTime).getTime()
        });
        lastEndTime = activity.endTime || activity.startTime;
        lastEndLocation = activity.startLocation;
      }
    });

    // Insert fuel events into the timeline at correct chronological position
    if (report.fuelEvents?.length) {
      report.fuelEvents.forEach((fe: any) => {
        eventNumber++;
        const fuelLabels: Record<string, string> = {
          'refuel': 'Remplissage carburant',
          'theft_alert': 'Alerte vol carburant'
        };
        const fuelIcons: Record<string, string> = {
          'refuel': '⛽',
          'theft_alert': '🚨'
        };
        const changeText = fe.fuelChange ? (fe.fuelChange > 0 ? `+${fe.fuelChange}%` : `${fe.fuelChange}%`) : '';
        const amountText = fe.refuelAmount ? `${fe.refuelAmount} L` : '';
        const desc = [amountText, changeText, fe.refuelStation].filter(Boolean).join(' - ');

        events.push({
          eventNumber,
          time: this.formatDateTime(fe.timestamp),
          type: 'fuel',
          typeIcon: fuelIcons[fe.eventType] || '⛽',
          typeLabel: fuelLabels[fe.eventType] || fe.eventType,
          description: desc || `Niveau: ${fe.fuelPercent}%`,
          address: fe.address || `${fe.latitude.toFixed(4)}°, ${fe.longitude.toFixed(4)}°`,
          latitude: fe.latitude,
          longitude: fe.longitude,
          duration: '-',
          distance: '-',
          speed: `${fe.fuelPercent}%`,
          _sortTime: new Date(fe.timestamp).getTime()
        });
      });
    }

    // Add last position event if different from last activity
    if (report.lastPosition) {
      eventNumber++;
      events.push({
        eventNumber,
        time: this.formatDateTime(report.lastPosition.timestamp),
        type: report.lastPosition.ignitionOn ? 'position' : 'end',
        typeIcon: report.lastPosition.ignitionOn ? '📍' : '🔒',
        typeLabel: report.lastPosition.ignitionOn ? 'Dernière position' : 'Fin de journée',
        description: report.lastPosition.ignitionOn ? 'Véhicule en mouvement' : 'Moteur éteint',
        address: report.lastPosition.address || `${report.lastPosition.latitude.toFixed(4)}°, ${report.lastPosition.longitude.toFixed(4)}°`,
        latitude: report.lastPosition.latitude,
        longitude: report.lastPosition.longitude,
        duration: '-',
        distance: '-',
        speed: '-'
      });
    }

    // Sort all events chronologically (fuel events may be out of order)
    events.sort((a: any, b: any) => {
      const timeA = a._sortTime || this.parseEventTime(a.time);
      const timeB = b._sortTime || this.parseEventTime(b.time);
      return timeA - timeB;
    });
    // Re-number after sort
    events.forEach((e: any, idx: number) => e.eventNumber = idx + 1);

    this.tableData = events;

    // Enrich addresses
    this.enrichDailyReportAddresses();

    // Chart data - Timeline bar chart showing activity durations
    const driveEvents = events.filter(e => e.type === 'drive');
    const stopEvents = events.filter(e => e.type === 'stop');

    // Primary chart: driving vs stopped time (donut)
    const totalDriveSeconds = driveEvents.reduce((s: number, e: any) => s + (e.durationSeconds || 0), 0);
    const totalStopSeconds = stopEvents.reduce((s: number, e: any) => s + (e.durationSeconds || 0), 0);
    const totalSec = totalDriveSeconds + totalStopSeconds;
    const pctDrive = totalSec > 0 ? Math.round(totalDriveSeconds / totalSec * 100) : 0;
    const pctStop = totalSec > 0 ? 100 - pctDrive : 0;

    this.chartData = [
      { label: `Conduite ${pctDrive}%`, value: Math.round(totalDriveSeconds / 60), color: '#3B82F6' },
      { label: `Arrêts ${pctStop}%`, value: Math.round(totalStopSeconds / 60), color: '#8B5CF6' }
    ];

    // Secondary chart: per-trip distance bar chart
    this.secondaryChartData = driveEvents.map((e: any) => ({
      label: e.typeLabel,
      value: e.distanceKm || 0,
      color: '#3B82F6'
    }));

    // Statistics
    const stats: Record<string, string> = {
      '🚗 Véhicule': `${report.vehicleName}${report.plate ? ' (' + report.plate + ')' : ''}`,
      '📅 Date': reportDateFormatted,
      '🔑 Premier démarrage': report.firstStart ? this.formatDateTime(report.firstStart.timestamp) : 'N/A',
      '🔒 Dernière position': report.lastPosition ? this.formatDateTime(report.lastPosition.timestamp) : 'N/A',
      '⏱️ Temps conduite': report.summary.totalDrivingFormatted,
      '⏸️ Temps arrêt': report.summary.totalStoppedFormatted,
      '📏 Distance': `${report.summary.totalDistanceKm} km`,
      '📊 Trajets/Arrêts': `${report.summary.driveCount} / ${report.summary.stopCount}`,
      '🏎️ Vitesse max': `${report.summary.maxSpeedKph} km/h`,
      '⌀ Vitesse moy': `${report.summary.avgSpeedKph} km/h`
    };

    // Add fuel info if available
    if (report.summary.fuelStartPercent != null || report.summary.fuelEndPercent != null) {
      stats['⛽ Carburant début'] = report.summary.fuelStartPercent != null ? `${report.summary.fuelStartPercent}%` : 'N/A';
      stats['⛽ Carburant fin'] = report.summary.fuelEndPercent != null ? `${report.summary.fuelEndPercent}%` : 'N/A';
    }
    if (report.summary.fuelRefillCount > 0) {
      stats['⛽ Remplissages'] = `${report.summary.fuelRefillCount}`;
      if (report.summary.totalFuelRefillLiters) {
        stats['⛽ Total rempli'] = `${report.summary.totalFuelRefillLiters.toFixed(1)} L`;
      }
    }

    this.statisticsData = stats;
  }

  enrichDailyReportAddresses() {
    this.tableData.forEach((row: any, index: number) => {
      if (row.address?.includes('°') && row.latitude && row.longitude) {
        this.geocodingService.reverseGeocode(row.latitude, row.longitude).pipe(takeUntil(this.destroy$)).subscribe({
          next: (addr) => {
            if (addr) {
              this.ngZone.run(() => {
                this.tableData[index] = { ...this.tableData[index], address: addr, description: addr };
                this.cdr.detectChanges();
              });
            }
          }
        });
      }
    });
  }

  formatTime(dateStr: string): string {
    return new Date(dateStr).toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
  }

  formatDateTime(dateStr: string): string {
    const d = new Date(dateStr);
    const day = d.getDate().toString().padStart(2, '0');
    const month = (d.getMonth() + 1).toString().padStart(2, '0');
    const year = d.getFullYear().toString().slice(-2);
    const hours = d.getHours().toString().padStart(2, '0');
    const minutes = d.getMinutes().toString().padStart(2, '0');
    return `${day}/${month}/${year} ${hours}:${minutes}`;
  }

  parseEventTime(timeStr: string): number {
    // Parse "DD/MM/YY HH:MM" or "DD/MM/YY HH:MM → DD/MM/YY HH:MM" format
    const firstPart = timeStr.split('→')[0].trim();
    const parts = firstPart.match(/(\d{2})\/(\d{2})\/(\d{2})\s+(\d{2}):(\d{2})/);
    if (parts) {
      const [, day, month, year, hours, minutes] = parts;
      return new Date(2000 + parseInt(year), parseInt(month) - 1, parseInt(day), parseInt(hours), parseInt(minutes)).getTime();
    }
    return new Date(firstPart).getTime() || 0;
  }

  formatDate(dateStr: string): string {
    const d = new Date(dateStr);
    const day = d.getDate().toString().padStart(2, '0');
    const month = (d.getMonth() + 1).toString().padStart(2, '0');
    const year = d.getFullYear().toString().slice(-2);
    return `${day}/${month}/${year}`;
  }

  executeMileageReport(vehicleId: number, startDate?: Date, endDate?: Date) {
    const start = startDate || new Date(Date.now() - 30 * 24 * 60 * 60 * 1000); // Default: last 30 days
    const end = endDate || new Date();
    
    this.apiService.getMileageReport(vehicleId, start, end).pipe(takeUntil(this.destroy$)).subscribe({
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

    // Convert daily breakdown to table data - FILTER OUT days with no trips
    const daysWithActivity = report.dailyBreakdown.filter((day: DailyMileage) => 
      day.tripCount > 0 || day.distanceKm > 0
    );

    this.tableData = daysWithActivity.map((day: DailyMileage) => ({
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
          title: { display: true, text: '📏 Distance journalière', font: { size: 14, weight: 'bold' } },
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

    // Create secondary cumulative chart
    this.createSecondaryChart();
  }

  // ==================== MILEAGE PERIOD REPORT (Hour/Day/Month) ====================

  executeMileagePeriodReport(vehicleId: number, startDate?: Date, endDate?: Date) {
    let start: Date;
    let end: Date;
    
    // Determine dates based on period type
    switch (this.selectedMileagePeriodType) {
      case 'hour':
        // For hourly report, use single date
        start = this.mileagePeriodDate ? new Date(this.mileagePeriodDate) : new Date();
        end = start;
        break;
      case 'day':
        // For daily report, use date range
        start = this.mileagePeriodStartDate ? new Date(this.mileagePeriodStartDate) : new Date();
        end = this.mileagePeriodEndDate ? new Date(this.mileagePeriodEndDate) : new Date();
        break;
      case 'month':
        // For monthly report, use first day of selected month to last day
        start = new Date(this.mileagePeriodYear, this.mileagePeriodMonth - 1, 1);
        end = new Date(this.mileagePeriodYear, this.mileagePeriodMonth, 0); // Last day of month
        break;
      default:
        start = startDate || new Date();
        end = endDate || new Date();
    }
    
    this.apiService.getMileagePeriodReport(vehicleId, this.selectedMileagePeriodType, start, end).pipe(takeUntil(this.destroy$)).subscribe({
      next: (report) => {
        this.ngZone.run(() => {
          this.mileagePeriodReport = report;
          this.processMileagePeriodReport(report);
          this.reportGenerated = true;
          this.loading = false;
          this.activeTab = 'table';
          this.currentPage = 1;
          this.cdr.detectChanges();
          this.appRef.tick();
          setTimeout(() => this.createMileagePeriodChart(), 100);
        });
      },
      error: (err) => {
        this.ngZone.run(() => {
          console.error('Error loading mileage period report:', err);
          this.mileagePeriodReport = null;
          this.tableData = [];
          this.chartData = [];
          this.statisticsData = { 'Erreur': 'Impossible de charger le rapport kilométrique par période' };
          this.reportGenerated = true;
          this.loading = false;
          this.cdr.detectChanges();
          this.appRef.tick();
        });
      }
    });
  }

  executeMileagePeriodReportAllVehicles(startDate?: Date, endDate?: Date) {
    let start: Date;
    let end: Date;
    
    switch (this.selectedMileagePeriodType) {
      case 'hour':
        start = this.mileagePeriodDate ? new Date(this.mileagePeriodDate) : new Date();
        end = start;
        break;
      case 'day':
        start = this.mileagePeriodStartDate ? new Date(this.mileagePeriodStartDate) : new Date();
        end = this.mileagePeriodEndDate ? new Date(this.mileagePeriodEndDate) : new Date();
        break;
      case 'month':
        start = new Date(this.mileagePeriodYear, this.mileagePeriodMonth - 1, 1);
        end = new Date(this.mileagePeriodYear, this.mileagePeriodMonth, 0);
        break;
      default:
        start = startDate || new Date();
        end = endDate || new Date();
    }

    const allResults: any[] = [];
    let completedRequests = 0;
    const totalVehicles = this.vehicles.length;

    if (totalVehicles === 0) {
      this.ngZone.run(() => {
        this.tableData = [];
        this.chartData = [];
        this.statisticsData = { 'Information': 'Aucun véhicule disponible' };
        this.reportGenerated = true;
        this.loading = false;
        this.cdr.detectChanges();
      });
      return;
    }

    this.vehicles.forEach(vehicle => {
      this.apiService.getMileagePeriodReport(vehicle.id, this.selectedMileagePeriodType, start, end).pipe(takeUntil(this.destroy$)).subscribe({
        next: (report) => {
          if (report.hasData) {
            allResults.push({
              vehicleName: vehicle.name || vehicle.plate,
              vehiclePlate: vehicle.plate,
              totalDistance: report.totalDistanceKm,
              totalTrips: report.totalTripCount,
              totalDriving: report.totalDrivingMinutes,
              totalDrivingFormatted: report.totalDrivingFormatted,
              avgDistance: report.averageDistanceKm,
              maxDistance: report.maxDistanceKm
            });
          }
          completedRequests++;

          if (completedRequests === totalVehicles) {
            this.ngZone.run(() => {
              this.processMileagePeriodAllVehicles(allResults, start, end);
              this.reportGenerated = true;
              this.loading = false;
              this.activeTab = 'table';
              this.currentPage = 1;
              this.cdr.detectChanges();
              this.appRef.tick();
              setTimeout(() => this.createChart(), 100);
            });
          }
        },
        error: () => {
          completedRequests++;
          if (completedRequests === totalVehicles) {
            this.ngZone.run(() => {
              this.processMileagePeriodAllVehicles(allResults, start, end);
              this.reportGenerated = true;
              this.loading = false;
              this.activeTab = 'table';
              this.currentPage = 1;
              this.cdr.detectChanges();
              this.appRef.tick();
              setTimeout(() => this.createChart(), 100);
            });
          }
        }
      });
    });
  }

  processMileagePeriodAllVehicles(results: any[], start: Date, end: Date) {
    results.sort((a, b) => b.totalDistance - a.totalDistance);

    this.tableData = results.map(r => ({
      vehicle: `${r.vehicleName}${r.vehiclePlate ? ' (' + r.vehiclePlate + ')' : ''}`,
      distance: `${r.totalDistance.toFixed(1)} km`,
      distanceValue: r.totalDistance,
      tripCount: r.totalTrips,
      drivingTime: r.totalDrivingFormatted,
      avgDistance: `${r.avgDistance.toFixed(1)} km`,
      maxDistance: `${r.maxDistance.toFixed(1)} km`
    }));

    this.chartData = results.map(r => ({
      label: r.vehicleName,
      value: r.totalDistance
    }));

    const totalDist = results.reduce((s, r) => s + r.totalDistance, 0);
    const totalTrips = results.reduce((s, r) => s + r.totalTrips, 0);

    this.statisticsData = {
      'Période': `${start.toLocaleDateString('fr-FR')} - ${end.toLocaleDateString('fr-FR')}`,
      'Type': this.getMileagePeriodTypeLabel(this.selectedMileagePeriodType),
      'Véhicules': results.length.toString(),
      'Distance totale flotte': `${totalDist.toFixed(1)} km`,
      'Nombre total de trajets': totalTrips.toString(),
      'Moyenne par véhicule': results.length > 0 ? `${(totalDist / results.length).toFixed(1)} km` : '0 km'
    };
  }

  processMileagePeriodReport(report: MileagePeriodReport) {
    console.log('Processing mileage period report:', report);
    console.log('Report hasData:', report.hasData);
    console.log('Report periodType:', report.periodType);
    
    if (!report.hasData) {
      this.tableData = [];
      this.chartData = [];
      this.statisticsData = {
        'Véhicule': report.vehicleName,
        'Période': `${new Date(report.startDate).toLocaleDateString('fr-FR')} - ${new Date(report.endDate).toLocaleDateString('fr-FR')}`,
        'Type': this.getMileagePeriodTypeLabel(report.periodType),
        'Information': 'Aucune donnée disponible pour cette période'
      };
      return;
    }

    // Normalize periodType to lowercase for comparison
    const periodType = (report.periodType || '').toString().toLowerCase() as MileagePeriodType;
    console.log('Normalized periodType:', periodType);

    // Process based on period type
    switch (periodType) {
      case 'hour':
        console.log('Processing hourly data:', report.hourlyBreakdown?.length, 'items');
        this.tableData = (report.hourlyBreakdown || []).map((h: HourlyMileagePeriod) => ({
          period: h.hourLabel,
          distance: `${h.distanceKm.toFixed(1)} km`,
          distanceValue: h.distanceKm,
          tripCount: h.tripCount,
          drivingTime: this.formatMinutes(h.drivingMinutes),
          avgSpeed: `${h.avgSpeedKph.toFixed(1)} km/h`,
          maxSpeed: `${h.maxSpeedKph.toFixed(1)} km/h`
        }));
        break;
      case 'day':
        console.log('Processing daily data:', report.dailyBreakdown?.length, 'items');
        this.tableData = (report.dailyBreakdown || []).reverse().map((d: DailyMileagePeriod) => ({
          period: d.dateLabel,
          dayOfWeek: d.dayOfWeek,
          distance: `${d.distanceKm.toFixed(1)} km`,
          distanceValue: d.distanceKm,
          tripCount: d.tripCount,
          drivingTime: this.formatMinutes(d.drivingMinutes),
          avgSpeed: `${d.avgSpeedKph.toFixed(1)} km/h`,
          maxSpeed: `${d.maxSpeedKph.toFixed(1)} km/h`
        }));
        break;
      case 'month':
        console.log('Processing monthly data:', report.monthlyBreakdown?.length, 'items');
        this.tableData = (report.monthlyBreakdown || []).reverse().map((m: MonthlyMileagePeriod) => ({
          period: m.monthLabel,
          distance: `${m.distanceKm.toFixed(1)} km`,
          distanceValue: m.distanceKm,
          avgDaily: `${m.averageDailyKm.toFixed(1)} km/jour`,
          tripCount: m.tripCount,
          drivingTime: this.formatMinutes(m.drivingMinutes),
          activeDays: `${m.daysWithActivity}/${m.totalDays}`
        }));
        break;
      default:
        console.warn('Unknown periodType:', periodType);
        this.tableData = [];
    }

    console.log('tableData after processing:', this.tableData.length, 'items');

    // Chart data from report
    this.chartData = report.chartData.map(d => ({
      label: d.label,
      value: d.value,
      tooltip: d.tooltip
    }));

    // Statistics
    this.statisticsData = {
      'Véhicule': `${report.vehicleName}${report.plate ? ' (' + report.plate + ')' : ''}`,
      'Période': `${new Date(report.startDate).toLocaleDateString('fr-FR')} - ${new Date(report.endDate).toLocaleDateString('fr-FR')}`,
      'Type de rapport': this.getMileagePeriodTypeLabel(report.periodType),
      'Distance totale': `${report.totalDistanceKm.toFixed(1)} km`,
      'Moyenne': `${report.averageDistanceKm.toFixed(1)} km`,
      'Maximum': `${report.maxDistanceKm.toFixed(1)} km`,
      'Minimum': `${report.minDistanceKm.toFixed(1)} km`,
      'Nombre de trajets': report.totalTripCount.toString(),
      'Temps de conduite': report.totalDrivingFormatted
    };
  }

  getMileagePeriodTypeLabel(type: MileagePeriodType): string {
    const labels: Record<MileagePeriodType, string> = {
      'hour': 'Par heure (24h)',
      'day': 'Par jour',
      'month': 'Par mois'
    };
    return labels[type] || type;
  }

  createMileagePeriodChart() {
    const canvas = this.mileagePeriodChartRef?.nativeElement || this.chartCanvas?.nativeElement;
    if (!canvas || !this.chartData.length) return;

    if (this.mileagePeriodChart) {
      this.mileagePeriodChart.destroy();
    }
    if (this.chart) {
      this.chart.destroy();
    }

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const periodType = this.mileagePeriodReport?.periodType || 'day';
    const chartConfig = this.getMileagePeriodChartConfig(periodType);

    this.mileagePeriodChart = new Chart(ctx, chartConfig);
    this.chart = this.mileagePeriodChart;
  }

  getMileagePeriodChartConfig(periodType: MileagePeriodType): ChartConfiguration {
    const labels = this.chartData.map(d => d.label);
    const data = this.chartData.map(d => d.value);
    
    const xAxisLabel = periodType === 'hour' ? 'Heure' : periodType === 'day' ? 'Date' : 'Mois';
    const chartType = periodType === 'hour' ? 'line' : 'bar';
    
    return {
      type: chartType as any,
      data: {
        labels,
        datasets: [{
          label: 'Distance (km)',
          data,
          backgroundColor: periodType === 'hour' 
            ? 'rgba(16, 185, 129, 0.2)' 
            : this.chartColors.map(c => c + 'CC'),
          borderColor: periodType === 'hour' 
            ? 'rgba(16, 185, 129, 1)' 
            : this.chartColors,
          borderWidth: periodType === 'hour' ? 2 : 1,
          borderRadius: periodType === 'hour' ? 0 : 6,
          fill: periodType === 'hour',
          tension: 0.3,
          pointRadius: periodType === 'hour' ? 4 : 0,
          pointHoverRadius: periodType === 'hour' ? 6 : 0
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        indexAxis: 'x',
        plugins: {
          legend: {
            display: true,
            position: 'top'
          },
          tooltip: {
            callbacks: {
              label: (context) => {
                const value = context.parsed.y ?? context.parsed.x ?? 0;
                return `${value.toFixed(1)} km`;
              },
              afterLabel: (context) => {
                const dataPoint = this.chartData[context.dataIndex];
                return dataPoint?.tooltip || '';
              }
            }
          },
          title: {
            display: true,
            text: `Kilométrage par ${xAxisLabel.toLowerCase()}`,
            font: { size: 14 }
          }
        },
        scales: {
          y: {
            beginAtZero: true,
            title: {
              display: true,
              text: 'Distance (km)'
            },
            grid: {
              color: 'rgba(0, 0, 0, 0.05)'
            }
          },
          x: {
            title: {
              display: true,
              text: xAxisLabel
            },
            grid: {
              display: false
            }
          }
        }
      }
    };
  }

  // ==================== MONTHLY FLEET REPORT ====================

  executeMonthlyReport() {
    this.apiService.getMonthlyFleetReport(this.selectedMonthlyYear, this.selectedMonthlyMonth).pipe(takeUntil(this.destroy$)).subscribe({
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

    this.apiService.getVehicleHistory(vehicleId, startDate, endDate).pipe(takeUntil(this.destroy$)).subscribe({
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
    // Only show rows where fuel level CHANGES (no duplicate consecutive readings)
    // Track mileage between fuel level changes (not between individual readings)
    const fuelChanges: any[] = [];
    let lastFuelLevel = -1;
    let lastChangeOdometer = 0; // Odometer at last FUEL CHANGE (not intermediate readings)

    // First pass: identify positions where fuel level changes
    positions.forEach((pos: any, index: number) => {
      const fuel = pos.fuelRaw ?? 0;
      const odometer = pos.odometerKm || 0;
      const isFirst = index === 0;
      const isLast = index === positions.length - 1;

      // Only process if fuel level changed, or first/last entry
      if (isFirst || fuel !== lastFuelLevel || isLast) {
        // Skip duplicate: last entry has same fuel as previous change
        if (!isFirst && fuel === lastFuelLevel && isLast) {
          return;
        }

        const fuelDelta = lastFuelLevel >= 0 ? fuel - lastFuelLevel : 0;
        // km between this fuel change and the previous fuel change
        const kmDelta = (lastChangeOdometer > 0 && odometer > 0) ? odometer - lastChangeOdometer : 0;

        let eventType = 'Lecture';
        let isAnomaly = false;
        if (fuelDelta > 5) {
          eventType = '⛽ Remplissage';
        } else if (fuelDelta < -15) {
          eventType = '⚠️ Chute importante';
          isAnomaly = true;
        } else if (fuelDelta < -5) {
          eventType = '📉 Consommation';
        } else if (fuelDelta > 0 && !isFirst) {
          eventType = '📈 Augmentation';
        }

        const location = pos.address || `${pos.latitude.toFixed(4)}°, ${pos.longitude.toFixed(4)}°`;

        fuelChanges.push({
          time: new Date(pos.recordedAt).toLocaleString('fr-FR', {
            day: '2-digit', month: '2-digit', year: 'numeric',
            hour: '2-digit', minute: '2-digit'
          }),
          fuelPercent: `${fuel}%`,
          fuelChange: (fuelDelta !== 0 && !isFirst) ? `${fuelDelta > 0 ? '+' : ''}${fuelDelta}%` : '-',
          eventType,
          location,
          latitude: pos.latitude,
          longitude: pos.longitude,
          mileage: kmDelta > 0 ? `${kmDelta.toLocaleString('fr-FR')} km` : '-',
          odometer: odometer > 0 ? `${odometer.toLocaleString('fr-FR')} km` : '-',
          isAnomaly
        });

        lastFuelLevel = fuel;
        if (odometer > 0) lastChangeOdometer = odometer;
      }
      // Positions with same fuel level are simply skipped (no odometer update)
    });

    // Reverse to show most recent first
    fuelChanges.reverse();
    this.tableData = fuelChanges;

    // Fetch addresses asynchronously for positions without address
    this.enrichWithAddresses();

    // Chart data - all positions for smooth fuel level graph
    this.chartData = positions.map((pos: any) => ({
      label: new Date(pos.recordedAt).toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' }),
      value: pos.fuelRaw || 0
    }));

    // Statistics
    const fuelValues = positions.map((p: any) => p.fuelRaw ?? 0).filter((f: number) => f > 0);
    const avgFuel = fuelValues.length > 0 ? fuelValues.reduce((a: number, b: number) => a + b, 0) / fuelValues.length : 0;
    const firstOdo = positions.find((p: any) => p.odometerKm > 0)?.odometerKm || 0;
    const lastOdo = [...positions].reverse().find((p: any) => p.odometerKm > 0)?.odometerKm || 0;
    const totalKm = (firstOdo > 0 && lastOdo > 0) ? lastOdo - firstOdo : 0;
    const fuelStart = fuelValues.length > 0 ? fuelValues[0] : 0;
    const fuelEnd = fuelValues.length > 0 ? fuelValues[fuelValues.length - 1] : 0;
    const refuels = this.tableData.filter((r: any) => r.eventType.includes('Remplissage')).length;
    const anomalies = this.tableData.filter((r: any) => r.isAnomaly).length;

    this.statisticsData = {
      '⛽ Niveau début': `${fuelStart}%`,
      '⛽ Niveau fin': `${fuelEnd}%`,
      '📉 Variation': `${fuelEnd - fuelStart > 0 ? '+' : ''}${fuelEnd - fuelStart}%`,
      '📏 Distance parcourue': totalKm > 0 ? `${totalKm.toLocaleString('fr-FR')} km` : 'N/A',
      '⛽ Remplissages': refuels.toString(),
      '⚠️ Alertes': anomalies.toString(),
      '📊 Niveau moyen': `${avgFuel.toFixed(1)}%`,
      '📈 Niveau max': fuelValues.length > 0 ? `${Math.max(...fuelValues)}%` : 'N/A',
      '📉 Niveau min': fuelValues.length > 0 ? `${Math.min(...fuelValues)}%` : 'N/A'
    };
  }

  processSpeedReport(positions: any[]) {
    // Get the selected vehicle info for name and speed limit
    const selectedVehicle = this.vehicles.find(v => v.id === parseInt(this.selectedVehicleId));
    const vehicleName = selectedVehicle 
      ? (selectedVehicle.name || `${selectedVehicle.brand} ${selectedVehicle.model}`)
      : 'Véhicule';
    const vehicleLimit = selectedVehicle?.speedLimit || 120;

    // Filter only meaningful speed data (> 5 km/h to exclude GPS noise)
    const movingPositions = positions.filter((p: any) => (p.speedKph || 0) > 5);
    
    if (movingPositions.length === 0) {
      this.tableData = [];
      this.chartData = [];
      this.statisticsData = { 'Information': 'Aucune donnée de vitesse pour cette période' };
      return;
    }

    // Find all positions exceeding the vehicle speed limit
    const infractions = movingPositions.filter((p: any) => (p.speedKph || 0) > vehicleLimit);

    // Speed ranges for chart (only moving positions)
    const speedRanges = [
      { min: 5, max: 30, label: '5-30 km/h', color: '#22C55E' },
      { min: 30, max: 50, label: '30-50 km/h', color: '#84CC16' },
      { min: 50, max: 70, label: '50-70 km/h', color: '#EAB308' },
      { min: 70, max: 90, label: '70-90 km/h', color: '#F97316' },
      { min: 90, max: 110, label: '90-110 km/h', color: '#EF4444' },
      { min: 110, max: 999, label: '>110 km/h', color: '#DC2626' }
    ];

    const rangeCounts = speedRanges.map(range => ({
      ...range,
      count: movingPositions.filter((p: any) => (p.speedKph || 0) >= range.min && (p.speedKph || 0) < range.max).length
    }));

    // Table: show highest speeds first (top 200)
    const sorted = [...movingPositions].sort((a, b) => (b.speedKph || 0) - (a.speedKph || 0));
    const topRecords = sorted.slice(0, 200);

    this.tableData = topRecords.map((pos: any) => {
      const speed = pos.speedKph || 0;
      const exceedsLimit = speed > vehicleLimit;
      const excess = exceedsLimit ? speed - vehicleLimit : 0;
      return {
        vehicleName: vehicleName,
        time: this.formatDateTime(pos.recordedAt),
        speed: `${speed.toFixed(0)} km/h`,
        speedValue: speed,
        vehicleLimit: `${vehicleLimit} km/h`,
        vehicleLimitValue: vehicleLimit,
        excess: exceedsLimit ? `+${excess.toFixed(0)} km/h` : '-',
        excessValue: excess,
        exceedsLimit: exceedsLimit,
        address: pos.address || `${pos.latitude?.toFixed(5) || 0}, ${pos.longitude?.toFixed(5) || 0}`,
        latitude: pos.latitude,
        longitude: pos.longitude
      };
    });

    // Enrich with addresses
    this.tableData.forEach((row: any, index: number) => {
      if (row.address?.includes(',') && row.latitude && row.longitude) {
        this.geocodingService.reverseGeocode(row.latitude, row.longitude).pipe(takeUntil(this.destroy$)).subscribe({
          next: (addr) => {
            if (addr) {
              this.ngZone.run(() => {
                this.tableData[index] = { ...this.tableData[index], address: addr };
                this.cdr.detectChanges();
              });
            }
          }
        });
      }
    });

    // Chart data - Speed distribution
    this.chartData = rangeCounts.filter(r => r.count > 0).map(range => ({
      label: range.label,
      value: range.count,
      color: range.color,
      percentage: ((range.count / movingPositions.length) * 100).toFixed(1)
    }));

    // Statistics
    const speeds = movingPositions.map((p: any) => p.speedKph || 0);
    const avgSpeed = speeds.reduce((a: number, b: number) => a + b, 0) / speeds.length;
    const maxSpeed = Math.max(...speeds);
    const highSpeedCount = speeds.filter((s: number) => s > 90).length;
    const highSpeedPct = ((highSpeedCount / speeds.length) * 100).toFixed(1);

    this.statisticsData = {
      '🚗 Véhicule': `${vehicleName} (limite: ${vehicleLimit} km/h)`,
      'Points analysés': movingPositions.length.toString(),
      'Vitesse moyenne': `${avgSpeed.toFixed(0)} km/h`,
      'Vitesse max': `${maxSpeed.toFixed(0)} km/h`,
      '🔴 Dépassements limite': `${infractions.length} (${vehicleLimit} km/h)`,
      '⚠️ >90 km/h': `${highSpeedCount} (${highSpeedPct}%)`
    };
  }

  enrichSpeedAddresses() {
    this.tableData.slice(0, 20).forEach((row: any, index: number) => {
      if (row.location?.includes(',') && row.latitude && row.longitude) {
        this.geocodingService.reverseGeocode(row.latitude, row.longitude).pipe(takeUntil(this.destroy$)).subscribe({
          next: (address) => {
            if (address) {
              this.ngZone.run(() => {
                this.tableData[index] = { ...this.tableData[index], location: address };
                this.cdr.detectChanges();
              });
            }
          }
        });
      }
    });
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

    // Merge consecutive stops that are close in time (< 3 min gap)
    const mergedStops: any[] = [];
    for (const stop of stops) {
      if (mergedStops.length > 0) {
        const prev = mergedStops[mergedStops.length - 1];
        const gapMs = new Date(stop.start.recordedAt).getTime() - new Date(prev.end.recordedAt).getTime();
        if (gapMs < 3 * 60 * 1000) { // < 3 min gap → merge
          prev.end = stop.end;
          prev.positions = [...prev.positions, ...stop.positions];
          continue;
        }
      }
      mergedStops.push(stop);
    }

    // Filter stops < 2 min (GPS noise)
    const filteredStops = mergedStops.filter((stop: any) => {
      const durationMs = new Date(stop.end.recordedAt).getTime() - new Date(stop.start.recordedAt).getTime();
      return durationMs >= 2 * 60 * 1000;
    });

    // Sort stops most recent first
    filteredStops.sort((a: any, b: any) => new Date(b.start.recordedAt).getTime() - new Date(a.start.recordedAt).getTime());

    this.tableData = filteredStops.slice(0, 50).map((stop: any) => {
      const durationMs = new Date(stop.end.recordedAt).getTime() - new Date(stop.start.recordedAt).getTime();
      const durationMinutes = durationMs / 60000;
      
      // Format duration as "Xh Ymin" or "Ymin"
      let formattedDuration: string;
      if (durationMinutes >= 60) {
        const hours = Math.floor(durationMinutes / 60);
        const mins = Math.round(durationMinutes % 60);
        formattedDuration = mins > 0 ? `${hours}h ${mins}min` : `${hours}h`;
      } else {
        formattedDuration = `${Math.round(durationMinutes)}min`;
      }
      
      return {
        time: this.formatDateTime(stop.start.recordedAt),
        duration: formattedDuration,
        address: stop.start.address || 'Chargement...',
        latitude: stop.start.latitude,
        longitude: stop.start.longitude,
        type: durationMinutes > 30 ? '🅿️ Arrêt prolongé' : '⏸️ Arrêt'
      };
    });

    // Fetch addresses asynchronously for stops without address
    this.enrichStopsWithAddresses();

    this.chartData = filteredStops.slice(0, 20).map((stop: any, i: number) => ({
      label: `Arrêt ${i + 1}`,
      value: Math.round((new Date(stop.end.recordedAt).getTime() - new Date(stop.start.recordedAt).getTime()) / 60000)
    }));

    const totalDuration = filteredStops.reduce((sum: number, s: any) => 
      sum + (new Date(s.end.recordedAt).getTime() - new Date(s.start.recordedAt).getTime()), 0) / 60000;

    // Format total duration
    let totalFormatted: string;
    if (totalDuration >= 60) {
      const hours = Math.floor(totalDuration / 60);
      const mins = Math.round(totalDuration % 60);
      totalFormatted = mins > 0 ? `${hours}h ${mins}min` : `${hours}h`;
    } else {
      totalFormatted = `${Math.round(totalDuration)}min`;
    }

    // Format average duration
    let avgFormatted = 'N/A';
    if (filteredStops.length > 0) {
      const avgMinutes = totalDuration / filteredStops.length;
      if (avgMinutes >= 60) {
        const hours = Math.floor(avgMinutes / 60);
        const mins = Math.round(avgMinutes % 60);
        avgFormatted = mins > 0 ? `${hours}h ${mins}min` : `${hours}h`;
      } else {
        avgFormatted = `${Math.round(avgMinutes)}min`;
      }
    }

    this.statisticsData = {
      'Nombre d\'arrêts': filteredStops.length.toString(),
      'Durée totale': totalFormatted,
      'Durée moyenne': avgFormatted
    };
  }

  enrichStopsWithAddresses() {
    // Fetch addresses for stops that don't have one
    this.tableData.forEach((row: any, index: number) => {
      if (row.address === 'Chargement...' && row.latitude && row.longitude) {
        this.geocodingService.reverseGeocode(row.latitude, row.longitude).pipe(takeUntil(this.destroy$)).subscribe({
          next: (address) => {
            this.ngZone.run(() => {
              this.tableData[index] = { ...this.tableData[index], address: address || `${row.latitude.toFixed(5)}, ${row.longitude.toFixed(5)}` };
              this.cdr.detectChanges();
            });
          },
          error: () => {
            this.ngZone.run(() => {
              this.tableData[index] = { ...this.tableData[index], address: `${row.latitude.toFixed(5)}, ${row.longitude.toFixed(5)}` };
              this.cdr.detectChanges();
            });
          }
        });
      }
    });
  }

  processStopsFromApi(result: VehicleStopsResult) {
    const stops = result.items;
    
    if (!stops || stops.length === 0) {
      this.tableData = [];
      this.chartData = [];
      this.statisticsData = { 'Information': 'Aucun arrêt trouvé pour cette période' };
      return;
    }

    // Format duration helper
    const formatDuration = (seconds: number): string => {
      const minutes = seconds / 60;
      if (minutes >= 60) {
        const hours = Math.floor(minutes / 60);
        const mins = Math.round(minutes % 60);
        return mins > 0 ? `${hours}h ${mins}min` : `${hours}h`;
      }
      return `${Math.round(minutes)}min`;
    };

    // Process stops into table data with type classification
    // A = Arrêt (ignition_off = true) - Vehicle is parked
    // C = Circulation (ignition_on = true, speed = 0) - Idling in traffic
    this.tableData = stops.map((stop: VehicleStopDto) => {
      const durationMinutes = stop.durationSeconds / 60;
      // Determine stop type: A = Arrêt (ignition off), C = Circulation (idling)
      const stopTypeCode = stop.ignitionOff ? 'A' : 'C';
      const stopTypeLabel = stop.ignitionOff 
        ? '🅿️ Arrêt' 
        : '🚦 Circulation';
      const stopTypeDescription = stop.ignitionOff
        ? 'Moteur éteint'
        : 'Moteur allumé, véhicule à l\'arrêt';

      return {
        time: this.formatDateTime(stop.startTime),
        endTime: stop.endTime ? this.formatDateTime(stop.endTime) : '-',
        duration: formatDuration(stop.durationSeconds),
        durationSeconds: stop.durationSeconds,
        address: stop.address || `${stop.latitude.toFixed(5)}, ${stop.longitude.toFixed(5)}`,
        latitude: stop.latitude,
        longitude: stop.longitude,
        typeCode: stopTypeCode,
        typeLabel: stopTypeLabel,
        typeDescription: stopTypeDescription,
        ignitionOff: stop.ignitionOff,
        isLongStop: durationMinutes > 30,
        geofenceName: stop.geofenceName,
        fuelStart: stop.fuelLevelStart,
        fuelEnd: stop.fuelLevelEnd,
        fuelConsumed: stop.fuelConsumed
      };
    });

    // Fetch addresses for stops without one
    this.enrichStopsWithAddresses();

    // Chart data - Gantt-style timeline showing stops
    // Group by type for pie chart
    const typeA = this.tableData.filter((s: any) => s.typeCode === 'A');
    const typeC = this.tableData.filter((s: any) => s.typeCode === 'C');
    
    const totalTypeASeconds = typeA.reduce((sum: number, s: any) => sum + s.durationSeconds, 0);
    const totalTypeCSeconds = typeC.reduce((sum: number, s: any) => sum + s.durationSeconds, 0);

    const totalSeconds = totalTypeASeconds + totalTypeCSeconds;
    const pctA = totalSeconds > 0 ? Math.round(totalTypeASeconds / totalSeconds * 100) : 0;
    const pctC = totalSeconds > 0 ? 100 - pctA : 0;

    this.chartData = [
      { label: `Arrêts (A) ${pctA}%`, value: Math.round(totalTypeASeconds / 60), count: typeA.length, color: '#3B82F6' },
      { label: `Circulation (C) ${pctC}%`, value: Math.round(totalTypeCSeconds / 60), count: typeC.length, color: '#F59E0B' }
    ];

    // Secondary chart: duration range breakdown (matching Calypso bar chart)
    const durationRanges = [
      { label: '0-5 min', min: 0, max: 300, color: '#3B82F6' },
      { label: '5-15 min', min: 300, max: 900, color: '#6366F1' },
      { label: '15-30 min', min: 900, max: 1800, color: '#8B5CF6' },
      { label: '30-60 min', min: 1800, max: 3600, color: '#F59E0B' },
      { label: '>60 min', min: 3600, max: Infinity, color: '#EF4444' }
    ];
    this.secondaryChartData = durationRanges.map(r => ({
      label: r.label,
      value: this.tableData.filter((s: any) => s.durationSeconds >= r.min && s.durationSeconds < r.max).length,
      color: r.color
    }));

    // Statistics
    const totalDurationSeconds = stops.reduce((sum, s) => sum + s.durationSeconds, 0);
    const avgDurationSeconds = stops.length > 0 ? totalDurationSeconds / stops.length : 0;
    const maxDurationSeconds = stops.length > 0 ? Math.max(...stops.map(s => s.durationSeconds)) : 0;
    const minDurationSeconds = stops.length > 0 ? Math.min(...stops.map(s => s.durationSeconds)) : 0;
    const longStops = this.tableData.filter((s: any) => s.isLongStop).length;

    this.statisticsData = {
      'Total arrêts': stops.length.toString(),
      'Durée moy. arrêt': formatDuration(avgDurationSeconds),
      'Max. arrêt': formatDuration(maxDurationSeconds),
      'Min. arrêt': formatDuration(minDurationSeconds),
      'Temps total circulation': formatDuration(totalTypeCSeconds),
      'Temps total arrêt': formatDuration(totalTypeASeconds),
      '🅿️ Arrêts (A)': typeA.length.toString(),
      '🚦 Ralenti (C)': typeC.length.toString()
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
          time: this.formatDateTime(curr.recordedAt),
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

    // Detect trips based on ignition_on transitions
    // Trip starts when ignition_on = true, ends when ignition_on = false
    const segments: { type: 'trip' | 'stop'; start: any; end: any; positions: any[]; distanceKm: number }[] = [];
    let currentSegment: { type: 'trip' | 'stop'; start: any; end: any; positions: any[]; distanceKm: number } | null = null;

    for (let i = 0; i < positions.length; i++) {
      const pos = positions[i];
      const isIgnitionOn = pos.ignitionOn === true;
      const isMoving = (pos.speedKph || 0) > 2;
      const isTripPosition = isIgnitionOn && isMoving; // Must be moving to count as trip

      if (!currentSegment) {
        currentSegment = {
          type: isTripPosition ? 'trip' : 'stop',
          start: pos,
          end: pos,
          positions: [pos],
          distanceKm: 0
        };
        continue;
      }

      const prev = positions[i - 1];
      const wasMoving = (prev.speedKph || 0) > 2 && prev.ignitionOn === true;

      // Calculate distance
      const dist = this.haversineDistance(prev.latitude, prev.longitude, pos.latitude, pos.longitude);
      if (!Number.isNaN(dist) && dist < 50) { // Filter unrealistic jumps
        currentSegment.distanceKm += dist;
      }

      // Detect transition: stopped moving = end of trip
      if (wasMoving && !isTripPosition) {
        currentSegment.end = prev;
        segments.push(currentSegment);
        currentSegment = {
          type: 'stop',
          start: pos,
          end: pos,
          positions: [pos],
          distanceKm: 0
        };
      }
      // Detect transition: started moving = start of new trip
      else if (!wasMoving && isTripPosition) {
        currentSegment.end = prev;
        segments.push(currentSegment);
        currentSegment = {
          type: 'trip',
          start: pos,
          end: pos,
          positions: [pos],
          distanceKm: 0
        };
      }
      // Continue current segment
      else {
        currentSegment.end = pos;
        currentSegment.positions.push(pos);
      }
    }

    if (currentSegment) {
      segments.push(currentSegment);
    }

    // === MERGE PHASE ===
    // 1. Merge trip-shortStop-trip into one continuous trip
    //    (handles traffic lights, brief pauses < 3 min)
    // 2. Merge consecutive stops into one stop
    const MIN_STOP_BREAK_MINUTES = 3; // Stop must be >= 3 min to split a trip
    let merged: typeof segments = [];
    
    for (let i = 0; i < segments.length; i++) {
      const seg = segments[i];
      
      // Check if this is a short stop between two trips → absorb into previous trip
      if (seg.type === 'stop' && merged.length > 0 && i + 1 < segments.length) {
        const prevSeg = merged[merged.length - 1];
        const nextSeg = segments[i + 1];
        const stopDurationMs = new Date(seg.end.recordedAt).getTime() - new Date(seg.start.recordedAt).getTime();
        const stopDurationMin = stopDurationMs / 60000;
        
        if (prevSeg.type === 'trip' && nextSeg.type === 'trip' && stopDurationMin < MIN_STOP_BREAK_MINUTES) {
          // Absorb stop + next trip into previous trip
          prevSeg.end = nextSeg.end;
          prevSeg.positions = [...prevSeg.positions, ...seg.positions, ...nextSeg.positions];
          prevSeg.distanceKm += seg.distanceKm + nextSeg.distanceKm;
          i++; // Skip next trip (already merged)
          continue;
        }
      }
      
      // Merge consecutive stops
      if (seg.type === 'stop' && merged.length > 0 && merged[merged.length - 1].type === 'stop') {
        const prevStop = merged[merged.length - 1];
        prevStop.end = seg.end;
        prevStop.positions = [...prevStop.positions, ...seg.positions];
        prevStop.distanceKm += seg.distanceKm;
        continue;
      }
      
      merged.push(seg);
    }

    // Filter meaningful segments
    const meaningfulSegments = merged.filter(seg => {
      const durationMs = new Date(seg.end.recordedAt).getTime() - new Date(seg.start.recordedAt).getTime();
      const durationMin = durationMs / 60000;
      if (seg.type === 'trip') {
        return durationMin >= 1 || seg.distanceKm >= 0.1;
      }
      return durationMin >= 2; // Show stops >= 2 min (filters GPS noise)
    });

    if (!meaningfulSegments.length) {
      this.tableData = [];
      this.chartData = [];
      this.statisticsData = { 'Information': 'Aucun trajet significatif détecté' };
      return;
    }

    // Format duration helper
    const formatDuration = (minutes: number): string => {
      if (minutes >= 60) {
        const h = Math.floor(minutes / 60);
        const m = Math.round(minutes % 60);
        return m > 0 ? `${h}h ${m}min` : `${h}h`;
      }
      return `${Math.round(minutes)}min`;
    };

    // Reverse to show most recent first
    meaningfulSegments.reverse();

    // Build table data with alternating trips and stops
    const totalTrips = meaningfulSegments.filter(s => s.type === 'trip').length;
    let tripNumber = totalTrips + 1;
    this.tableData = meaningfulSegments.map((seg, index) => {
      const startTime = new Date(seg.start.recordedAt);
      const endTime = new Date(seg.end.recordedAt);
      const durationMin = (endTime.getTime() - startTime.getTime()) / 60000;

      if (seg.type === 'trip') {
        tripNumber--;
        let distanceKm = seg.distanceKm;
        
        // Try odometer first
        if (seg.start.odometerKm && seg.end.odometerKm && seg.end.odometerKm >= seg.start.odometerKm) {
          distanceKm = seg.end.odometerKm - seg.start.odometerKm;
        }
        
        // If still 0, estimate from average speed
        if (distanceKm < 0.1 && durationMin > 1) {
          const speeds = seg.positions.map((p: any) => p.speedKph || 0).filter((s: number) => s > 0);
          if (speeds.length > 0) {
            const avgSpeedFromPositions = speeds.reduce((a: number, b: number) => a + b, 0) / speeds.length;
            distanceKm = avgSpeedFromPositions * (durationMin / 60);
          }
        }
        
        const avgSpeed = durationMin > 0 && distanceKm > 0 ? distanceKm / (durationMin / 60) : 0;
        const maxSpeed = Math.max(...seg.positions.map(p => p.speedKph || 0));

        return {
          isTrip: true,
          tripNumber,
          startTime: this.formatDateTime(seg.start.recordedAt),
          endTime: this.formatDateTime(seg.end.recordedAt),
          duration: formatDuration(durationMin),
          durationMin,
          distance: `${distanceKm.toFixed(1)} km`,
          distanceKm,
          avgSpeed: `${avgSpeed.toFixed(0)} km/h`,
          maxSpeed: `${maxSpeed.toFixed(0)} km/h`,
          startAddress: seg.start.address || `${seg.start.latitude.toFixed(4)}°, ${seg.start.longitude.toFixed(4)}°`,
          endAddress: seg.end.address || `${seg.end.latitude.toFixed(4)}°, ${seg.end.longitude.toFixed(4)}°`,
          startLat: seg.start.latitude,
          startLng: seg.start.longitude,
          endLat: seg.end.latitude,
          endLng: seg.end.longitude
        };
      } else {
        return {
          isTrip: false,
          isStop: true,
          startTime: this.formatDateTime(seg.start.recordedAt),
          endTime: this.formatDateTime(seg.end.recordedAt),
          duration: formatDuration(durationMin),
          durationMin,
          address: seg.start.address || `${seg.start.latitude.toFixed(4)}°, ${seg.start.longitude.toFixed(4)}°`,
          latitude: seg.start.latitude,
          longitude: seg.start.longitude
        };
      }
    });

    // Enrich addresses
    this.enrichTripAddresses();

    // Chart data - Timeline chart showing trips and stops
    const trips = this.tableData.filter((d: any) => d.isTrip);
    const stops = this.tableData.filter((d: any) => d.isStop);
    
    this.chartData = trips.map((t: any) => ({
      label: `Trajet ${t.tripNumber}`,
      value: t.distanceKm,
      duration: t.durationMin
    }));

    // Statistics
    const totalDistance = trips.reduce((sum: number, t: any) => sum + t.distanceKm, 0);
    const totalDrivingMin = trips.reduce((sum: number, t: any) => sum + t.durationMin, 0);
    const totalStopMin = stops.reduce((sum: number, s: any) => sum + s.durationMin, 0);
    const avgSpeed = totalDrivingMin > 0 ? totalDistance / (totalDrivingMin / 60) : 0;

    this.statisticsData = {
      'Nombre de trajets': trips.length.toString(),
      'Distance totale': `${totalDistance.toFixed(1)} km`,
      'Temps de conduite': formatDuration(totalDrivingMin),
      'Temps d\'arrêt': formatDuration(totalStopMin),
      'Vitesse moyenne': `${avgSpeed.toFixed(0)} km/h`
    };
  }

  enrichTripAddresses() {
    this.tableData.forEach((row: any, index: number) => {
      if (row.isTrip) {
        if (row.startAddress?.includes('°')) {
          this.geocodingService.reverseGeocode(row.startLat, row.startLng).pipe(takeUntil(this.destroy$)).subscribe({
            next: (addr) => {
              if (addr) {
                this.ngZone.run(() => {
                  this.tableData[index] = { ...this.tableData[index], startAddress: addr };
                  this.cdr.detectChanges();
                });
              }
            }
          });
        }
        if (row.endAddress?.includes('°')) {
          this.geocodingService.reverseGeocode(row.endLat, row.endLng).pipe(takeUntil(this.destroy$)).subscribe({
            next: (addr) => {
              if (addr) {
                this.ngZone.run(() => {
                  this.tableData[index] = { ...this.tableData[index], endAddress: addr };
                  this.cdr.detectChanges();
                });
              }
            }
          });
        }
      } else if (row.isStop && row.address?.includes('°')) {
        this.geocodingService.reverseGeocode(row.latitude, row.longitude).pipe(takeUntil(this.destroy$)).subscribe({
          next: (addr) => {
            if (addr) {
              this.ngZone.run(() => {
                this.tableData[index] = { ...this.tableData[index], address: addr };
                this.cdr.detectChanges();
              });
            }
          }
        });
      }
    });
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

    if (type === 'trips') {
      // Combined bar + line chart: Distance bars + Average speed line
      const distances = this.chartData.map(d => d.value);
      const durations = this.chartData.map(d => d.duration || 0);
      const avgSpeeds = this.chartData.map((d, i) => durations[i] > 0 ? parseFloat((distances[i] / (durations[i] / 60)).toFixed(1)) : 0);
      
      config = {
        type: 'bar',
        data: {
          labels: this.chartData.map(d => d.label),
          datasets: [
            {
              type: 'bar',
              label: 'Distance (km)',
              data: distances,
              backgroundColor: this.chartColors.map(c => c + 'CC'),
              borderColor: this.chartColors,
              borderWidth: 1,
              borderRadius: 6,
              yAxisID: 'y'
            },
            {
              type: 'line',
              label: 'Vitesse moy. (km/h)',
              data: avgSpeeds,
              borderColor: '#F59E0B',
              backgroundColor: 'rgba(245, 158, 11, 0.2)',
              borderWidth: 3,
              pointRadius: 5,
              pointBackgroundColor: '#F59E0B',
              tension: 0.3,
              yAxisID: 'y1'
            }
          ]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          interaction: { mode: 'index', intersect: false },
          plugins: {
            legend: { position: 'top', labels: { usePointStyle: true, font: { size: 11 } } },
            title: { display: true, text: '📊 Distance et Vitesse moyenne par trajet', font: { size: 14, weight: 'bold' } },
            tooltip: {
              callbacks: {
                afterBody: (context: any) => {
                  const idx = context[0]?.dataIndex;
                  if (idx !== undefined) {
                    const dur = durations[idx];
                    return dur ? `⏱️ Durée: ${Math.round(dur)} min` : '';
                  }
                  return '';
                }
              }
            }
          },
          scales: {
            y: { type: 'linear', position: 'left', title: { display: true, text: 'Distance (km)' }, beginAtZero: true },
            y1: { type: 'linear', position: 'right', title: { display: true, text: 'Vitesse (km/h)' }, grid: { drawOnChartArea: false }, beginAtZero: true }
          }
        }
      };
    } else if (type === 'stops') {
      // Dual charts: Donut for type distribution + Horizontal bar for duration
      const totalMinutes = this.chartData.reduce((sum, d) => sum + d.value, 0);
      config = {
        type: 'doughnut',
        data: {
          labels: this.chartData.map(d => d.label),
          datasets: [{
            data: this.chartData.map(d => d.value),
            backgroundColor: ['#3B82F6', '#F59E0B', '#10B981', '#EF4444'],
            borderWidth: 3,
            borderColor: '#1e293b',
            hoverOffset: 10
          }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          plugins: {
            legend: { position: 'bottom', labels: { usePointStyle: true, padding: 15, font: { size: 12 } } },
            title: { display: true, text: '🅿️ Répartition du temps d\'arrêt', font: { size: 14, weight: 'bold' } },
            tooltip: {
              callbacks: {
                label: (context: any) => {
                  const item = this.chartData[context.dataIndex];
                  const pct = totalMinutes > 0 ? ((item.value / totalMinutes) * 100).toFixed(1) : 0;
                  return [`${item.value} min (${pct}%)`, `${item.count || 0} arrêts`];
                }
              }
            }
          }
        }
      } as ChartConfiguration;
    } else if (type === 'speed') {
      // Gradient bar chart with danger zones highlighted
      const gradientColors = this.chartData.map(d => d.color || '#3B82F6');
      config = {
        type: 'bar',
        data: {
          labels: this.chartData.map(d => d.label),
          datasets: [{
            label: 'Points GPS',
            data: this.chartData.map(d => d.value),
            backgroundColor: gradientColors,
            borderColor: gradientColors.map(c => c),
            borderWidth: 2,
            borderRadius: 8,
            borderSkipped: false
          }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          plugins: {
            legend: { display: false },
            title: { display: true, text: '🏎️ Distribution des vitesses', font: { size: 14, weight: 'bold' } },
            tooltip: {
              callbacks: {
                afterLabel: (context: any) => {
                  const item = this.chartData[context.dataIndex];
                  return item?.percentage ? `📊 ${item.percentage}% du temps` : '';
                }
              }
            }
          },
          scales: {
            y: { beginAtZero: true, title: { display: true, text: 'Nombre de points' }, grid: { color: 'rgba(255,255,255,0.1)' } },
            x: { grid: { display: false } }
          }
        }
      };
    } else if (type === 'speed-infraction') {
      // Detect if single vehicle (hourly chart) or multi-vehicle (bar chart)
      const hasHourlyData = this.chartData.length === 24 && this.chartData[0]?.label?.includes('h');
      if (hasHourlyData) {
        // Single vehicle: infractions by hour of day
        config = {
          type: 'bar',
          data: {
            labels: this.chartData.map(d => d.label),
            datasets: [{
              label: 'Infractions',
              data: this.chartData.map(d => d.value),
              backgroundColor: this.chartData.map(d => d.color || '#F59E0B'),
              borderColor: this.chartData.map(d => d.value > 3 ? '#EF4444' : d.value > 0 ? '#F59E0B' : '#334155'),
              borderWidth: 2,
              borderRadius: 4
            }]
          },
          options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
              legend: { display: false },
              title: { display: true, text: '⏰ Infractions par heure de la journée', font: { size: 14, weight: 'bold' } },
              tooltip: {
                callbacks: {
                  label: (context: any) => `${context.raw} infraction${context.raw > 1 ? 's' : ''}`
                }
              }
            },
            scales: {
              y: { beginAtZero: true, title: { display: true, text: 'Nombre d\'infractions' }, ticks: { stepSize: 1 } },
              x: { grid: { display: false }, title: { display: true, text: 'Heure' } }
            }
          }
        };
      } else {
        // Multi-vehicle: infractions by vehicle (horizontal bar)
        config = {
          type: 'bar',
          data: {
            labels: this.chartData.map(d => d.label),
            datasets: [{
              label: 'Infractions',
              data: this.chartData.map(d => d.value),
              backgroundColor: this.chartData.map((_, i) => {
                const colors = ['#EF4444', '#F97316', '#F59E0B', '#EAB308', '#84CC16', '#22C55E', '#14B8A6', '#06B6D4', '#3B82F6', '#8B5CF6'];
                return colors[i % colors.length] + 'CC';
              }),
              borderColor: '#EF4444',
              borderWidth: 2,
              borderRadius: 8
            }]
          },
          options: {
            responsive: true,
            maintainAspectRatio: false,
            indexAxis: 'y',
            plugins: {
              legend: { display: false },
              title: { display: true, text: '⚠️ Infractions de vitesse par véhicule', font: { size: 14, weight: 'bold' } }
            },
            scales: {
              x: { beginAtZero: true, title: { display: true, text: 'Nombre d\'infractions' } },
              y: { grid: { display: false } }
            }
          }
        };
      }
    } else if (type === 'driving-behavior') {
      // Horizontal bar chart with incident type colors
      const incidentColors = this.chartData.map(d => (d.color || '#3B82F6') + 'CC');
      const incidentBorders = this.chartData.map(d => d.color || '#3B82F6');
      config = {
        type: 'bar',
        data: {
          labels: this.chartData.map(d => d.label),
          datasets: [{
            label: 'Nombre d\'incidents',
            data: this.chartData.map(d => d.value),
            backgroundColor: incidentColors,
            borderColor: incidentBorders,
            borderWidth: 2,
            borderRadius: 8
          }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          indexAxis: 'y',
          plugins: {
            legend: { display: false },
            title: { display: true, text: '🚨 Incidents par type de comportement', font: { size: 14, weight: 'bold' } },
            tooltip: {
              callbacks: {
                label: (context: any) => {
                  const total = this.chartData.reduce((s, d) => s + d.value, 0);
                  const pct = total > 0 ? ((context.raw / total) * 100).toFixed(1) : 0;
                  return `${context.raw} incidents (${pct}%)`;
                }
              }
            }
          },
          scales: {
            x: { beginAtZero: true, title: { display: true, text: 'Nombre d\'incidents' }, ticks: { stepSize: 1 } },
            y: { grid: { display: false } }
          }
        }
      };
    } else if (type === 'fuel') {
      // Area chart with gradient for fuel level
      config = {
        type: 'line',
        data: {
          labels: this.chartData.map(d => d.label),
          datasets: [{
            label: 'Niveau carburant (%)',
            data: this.chartData.map(d => d.value),
            borderColor: '#10B981',
            backgroundColor: (context: any) => {
              const chart = context.chart;
              const { ctx, chartArea } = chart;
              if (!chartArea) return 'rgba(16, 185, 129, 0.3)';
              const gradient = ctx.createLinearGradient(0, chartArea.bottom, 0, chartArea.top);
              gradient.addColorStop(0, 'rgba(239, 68, 68, 0.3)');
              gradient.addColorStop(0.3, 'rgba(245, 158, 11, 0.3)');
              gradient.addColorStop(0.7, 'rgba(16, 185, 129, 0.3)');
              gradient.addColorStop(1, 'rgba(16, 185, 129, 0.5)');
              return gradient;
            },
            borderWidth: 2,
            tension: 0.4,
            fill: true,
            pointRadius: 0,
            pointHoverRadius: 6
          }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          interaction: { mode: 'index', intersect: false },
          plugins: {
            legend: { display: true, position: 'top' },
            title: { display: true, text: '⛽ Évolution du niveau de carburant', font: { size: 14, weight: 'bold' } }
          },
          scales: {
            y: { beginAtZero: true, max: 100, title: { display: true, text: 'Niveau (%)' }, grid: { color: 'rgba(255,255,255,0.1)' } },
            x: { grid: { display: false }, ticks: { maxTicksLimit: 12 } }
          }
        }
      };
    } else if (type === 'daily') {
      // Donut chart: driving vs stopped time (Calypso style)
      config = {
        type: 'doughnut',
        data: {
          labels: this.chartData.map(d => d.label),
          datasets: [{
            data: this.chartData.map(d => d.value),
            backgroundColor: this.chartData.map(d => d.color || '#3B82F6'),
            borderWidth: 3,
            borderColor: '#1e293b',
            hoverOffset: 10
          }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          plugins: {
            legend: { position: 'bottom' as const, labels: { usePointStyle: true, padding: 12, font: { size: 11 } } },
            title: { display: true, text: 'Répartition conduite / arrêts', font: { size: 14, weight: 'bold' } }
          }
        }
      };
    } else if (type === 'costs') {
      // Doughnut for repairs by status
      config = {
        type: 'doughnut',
        data: {
          labels: this.chartData.map(d => d.label),
          datasets: [{
            data: this.chartData.map(d => d.value),
            backgroundColor: this.chartData.map(d => d.color || '#3B82F6'),
            borderWidth: 3,
            borderColor: '#1e293b',
            hoverOffset: 10
          }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          plugins: {
            legend: { position: 'bottom', labels: { usePointStyle: true, padding: 15, font: { size: 12 } } },
            title: { display: true, text: '🔩 Réparations par statut', font: { size: 14, weight: 'bold' } }
          }
        }
      } as ChartConfiguration;
    } else if (type === 'maintenance') {
      // Doughnut for maintenance by type
      config = {
        type: 'doughnut',
        data: {
          labels: this.chartData.map(d => d.label),
          datasets: [{
            data: this.chartData.map(d => d.value),
            backgroundColor: this.chartData.map(d => d.color || '#3B82F6'),
            borderWidth: 3,
            borderColor: '#1e293b',
            hoverOffset: 10
          }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          plugins: {
            legend: { position: 'bottom', labels: { usePointStyle: true, padding: 15, font: { size: 12 } } },
            title: { display: true, text: '🔧 Maintenances par type', font: { size: 14, weight: 'bold' } }
          }
        }
      } as ChartConfiguration;
    } else if (type === 'distance') {
      // Cumulative area chart for distance
      let cumulative = 0;
      const cumulativeData = this.chartData.map(d => { cumulative += d.value; return cumulative; });
      config = {
        type: 'line',
        data: {
          labels: this.chartData.map(d => d.label),
          datasets: [
            {
              type: 'bar',
              label: 'Distance segment (km)',
              data: this.chartData.map(d => d.value),
              backgroundColor: 'rgba(59, 130, 246, 0.6)',
              borderRadius: 4,
              yAxisID: 'y'
            },
            {
              type: 'line',
              label: 'Distance cumulée (km)',
              data: cumulativeData,
              borderColor: '#10B981',
              backgroundColor: 'rgba(16, 185, 129, 0.1)',
              borderWidth: 3,
              fill: true,
              tension: 0.3,
              yAxisID: 'y1'
            }
          ]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          plugins: {
            title: { display: true, text: '📏 Progression de la distance', font: { size: 14, weight: 'bold' } }
          },
          scales: {
            y: { type: 'linear', position: 'left', title: { display: true, text: 'Distance (km)' } },
            y1: { type: 'linear', position: 'right', title: { display: true, text: 'Cumulé (km)' }, grid: { drawOnChartArea: false } }
          }
        }
      };
    } else {
      // Default enhanced line chart
      config = {
        type: 'line',
        data: {
          labels: this.chartData.map(d => d.label),
          datasets: [{
            label: this.selectedTemplate?.name || 'Valeur',
            data: this.chartData.map(d => d.value),
            borderColor: '#3B82F6',
            backgroundColor: 'rgba(59, 130, 246, 0.15)',
            tension: 0.4,
            fill: true,
            pointRadius: 3,
            pointHoverRadius: 6,
            borderWidth: 2
          }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          plugins: {
            legend: { position: 'top' },
            title: { display: true, text: this.selectedTemplate?.name || 'Données', font: { size: 14, weight: 'bold' } }
          },
          scales: {
            y: { beginAtZero: true, grid: { color: 'rgba(255,255,255,0.1)' } },
            x: { grid: { display: false } }
          }
        }
      };
    }

    this.chart = new Chart(ctx, config);

    // Create secondary chart if data exists
    this.createSecondaryChart();
  }

  createSecondaryChart() {
    if (!this.secondaryChartCanvas) return;

    if (this.secondaryChart) {
      this.secondaryChart.destroy();
    }

    const ctx = this.secondaryChartCanvas.nativeElement.getContext('2d');
    if (!ctx) return;

    const type = this.selectedTemplate?.type || 'fuel';

    if (type === 'trips' && this.secondaryChartData?.length) {
      // Duration distribution pie chart with enhanced styling
      this.secondaryChart = new Chart(ctx, {
        type: 'doughnut',
        data: {
          labels: this.secondaryChartData.map(d => d.label),
          datasets: [{
            data: this.secondaryChartData.map(d => d.value),
            backgroundColor: this.secondaryChartData.map(d => d.color),
            borderWidth: 3,
            borderColor: '#1e293b',
            hoverOffset: 8
          }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          plugins: {
            legend: { position: 'bottom' as const, labels: { usePointStyle: true, padding: 10, font: { size: 10 } } },
            title: { display: true, text: '⏱️ Répartition par durée de trajet', font: { size: 12, weight: 'bold' } }
          }
        }
      });
    } else if (type === 'stops' && this.secondaryChartData?.length) {
      // Duration range bar chart (Calypso style)
      this.secondaryChart = new Chart(ctx, {
        type: 'bar',
        data: {
          labels: this.secondaryChartData.map(d => d.label),
          datasets: [{
            label: 'Nombre d\'arrêts',
            data: this.secondaryChartData.map(d => d.value),
            backgroundColor: this.secondaryChartData.map((d: any) => d.color || '#3B82F6'),
            borderColor: this.secondaryChartData.map((d: any) => d.color || '#3B82F6'),
            borderWidth: 1,
            borderRadius: 6
          }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          plugins: {
            legend: { display: false },
            title: { display: true, text: 'Répartition par durée d\'arrêt', font: { size: 12, weight: 'bold' } }
          },
          scales: {
            x: { beginAtZero: true, title: { display: true, text: 'Nombre d\'arrêts' } }
          }
        }
      });
    } else if (type === 'speed' && this.secondaryChartData?.length) {
      // Speed timeline with gradient zones
      this.secondaryChart = new Chart(ctx, {
        type: 'line',
        data: {
          labels: this.secondaryChartData.map(d => d.label),
          datasets: [{
            label: 'Vitesse (km/h)',
            data: this.secondaryChartData.map(d => d.value),
            borderColor: '#3B82F6',
            backgroundColor: (context: any) => {
              const chart = context.chart;
              const { ctx: chartCtx, chartArea } = chart;
              if (!chartArea) return 'rgba(59, 130, 246, 0.15)';
              const gradient = chartCtx.createLinearGradient(0, chartArea.bottom, 0, chartArea.top);
              gradient.addColorStop(0, 'rgba(34, 197, 94, 0.2)');
              gradient.addColorStop(0.5, 'rgba(245, 158, 11, 0.2)');
              gradient.addColorStop(1, 'rgba(239, 68, 68, 0.3)');
              return gradient;
            },
            fill: true,
            tension: 0.3,
            pointRadius: 1,
            pointHoverRadius: 4,
            borderWidth: 2
          }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          plugins: {
            legend: { display: false },
            title: { display: true, text: '📈 Évolution de la vitesse dans le temps', font: { size: 12, weight: 'bold' } }
          },
          scales: {
            y: { beginAtZero: true, title: { display: true, text: 'km/h' }, grid: { color: 'rgba(255,255,255,0.1)' } },
            x: { grid: { display: false }, ticks: { maxTicksLimit: 10 } }
          }
        }
      });
    } else if (type === 'speed-infraction') {
      // Severity distribution for infractions
      const severityData = this.getSeverityDistribution();
      if (severityData.length > 0) {
        this.secondaryChart = new Chart(ctx, {
          type: 'doughnut',
          data: {
            labels: severityData.map(d => d.label),
            datasets: [{
              data: severityData.map(d => d.value),
              backgroundColor: ['#22C55E', '#F59E0B', '#EF4444'],
              borderWidth: 2,
              borderColor: '#1e293b'
            }]
          },
          options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
              legend: { position: 'bottom' as const, labels: { usePointStyle: true } },
              title: { display: true, text: '🎯 Gravité des infractions', font: { size: 12, weight: 'bold' } }
            }
          }
        });
      }
    } else if (type === 'driving-behavior') {
      // Severity breakdown for driving behavior
      const severityData = this.getBehaviorSeverityData();
      if (severityData.length > 0) {
        this.secondaryChart = new Chart(ctx, {
          type: 'bar',
          data: {
            labels: ['Léger', 'Modéré', 'Grave'],
            datasets: [{
              label: 'Incidents',
              data: severityData,
              backgroundColor: ['#22C55E', '#F59E0B', '#EF4444'],
              borderRadius: 6
            }]
          },
          options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
              legend: { display: false },
              title: { display: true, text: '📊 Répartition par sévérité', font: { size: 12, weight: 'bold' } }
            },
            scales: {
              y: { beginAtZero: true }
            }
          }
        });
      }
    } else if (type === 'fuel') {
      // Fuel events distribution
      const eventData = this.getFuelEventsData();
      if (eventData.length > 0) {
        this.secondaryChart = new Chart(ctx, {
          type: 'bar',
          data: {
            labels: eventData.map(d => d.label),
            datasets: [{
              label: 'Événements',
              data: eventData.map(d => d.value),
              backgroundColor: eventData.map(d => d.color),
              borderRadius: 6
            }]
          },
          options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
              legend: { display: false },
              title: { display: true, text: '📋 Types d\'événements carburant', font: { size: 12, weight: 'bold' } }
            }
          }
        });
      }
    } else if (type === 'costs' && this.secondaryChartData?.length) {
      // Costs by vehicle horizontal bar chart
      this.secondaryChart = new Chart(ctx, {
        type: 'bar',
        data: {
          labels: this.secondaryChartData.map(d => d.label),
          datasets: [{
            label: 'Coût (TND)',
            data: this.secondaryChartData.map(d => d.value),
            backgroundColor: this.secondaryChartData.map((_, i) => this.chartColors[i % this.chartColors.length] + 'CC'),
            borderColor: this.secondaryChartData.map((_, i) => this.chartColors[i % this.chartColors.length]),
            borderWidth: 1,
            borderRadius: 6
          }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          indexAxis: 'y' as const,
          plugins: {
            legend: { display: false },
            title: { display: true, text: '💰 Coûts par véhicule', font: { size: 12, weight: 'bold' } }
          },
          scales: {
            x: { beginAtZero: true, title: { display: true, text: 'Coût (TND)' } }
          }
        }
      });
    } else if (type === 'maintenance' && this.secondaryChartData?.length) {
      // Maintenance costs by vehicle horizontal bar chart
      this.secondaryChart = new Chart(ctx, {
        type: 'bar',
        data: {
          labels: this.secondaryChartData.map(d => d.label),
          datasets: [{
            label: 'Coût (TND)',
            data: this.secondaryChartData.map(d => d.value),
            backgroundColor: this.secondaryChartData.map((_, i) => this.chartColors[i % this.chartColors.length] + 'CC'),
            borderColor: this.secondaryChartData.map((_, i) => this.chartColors[i % this.chartColors.length]),
            borderWidth: 1,
            borderRadius: 6
          }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          indexAxis: 'y' as const,
          plugins: {
            legend: { display: false },
            title: { display: true, text: '🔧 Coûts maintenance par véhicule', font: { size: 12, weight: 'bold' } }
          },
          scales: {
            x: { beginAtZero: true, title: { display: true, text: 'Coût (TND)' } }
          }
        }
      });
    } else if (type === 'daily' && this.secondaryChartData?.length) {
      // Daily report secondary: per-trip distance bar chart
      this.secondaryChart = new Chart(ctx, {
        type: 'bar',
        data: {
          labels: this.secondaryChartData.map(d => d.label),
          datasets: [{
            label: 'Distance (km)',
            data: this.secondaryChartData.map(d => d.value),
            backgroundColor: '#3B82F6CC',
            borderColor: '#3B82F6',
            borderWidth: 1,
            borderRadius: 6
          }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          plugins: {
            legend: { display: false },
            title: { display: true, text: 'Distance par trajet (km)', font: { size: 12, weight: 'bold' } }
          },
          scales: {
            y: { beginAtZero: true, title: { display: true, text: 'km' } },
            x: { grid: { display: false } }
          }
        }
      });
    } else if (type === 'mileage' && this.chartData?.length) {
      // Mileage secondary: Cumulative distance line
      let cumulative = 0;
      const cumulativeData = this.chartData.map(d => { cumulative += d.value; return cumulative; });
      this.secondaryChart = new Chart(ctx, {
        type: 'line',
        data: {
          labels: this.chartData.map(d => d.label),
          datasets: [{
            label: 'Distance cumulée (km)',
            data: cumulativeData,
            borderColor: '#10B981',
            backgroundColor: 'rgba(16, 185, 129, 0.15)',
            borderWidth: 3,
            fill: true,
            tension: 0.3,
            pointRadius: 3,
            pointBackgroundColor: '#10B981'
          }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          plugins: {
            legend: { display: true, position: 'top' as const },
            title: { display: true, text: '📈 Distance cumulée', font: { size: 12, weight: 'bold' } },
            tooltip: {
              callbacks: {
                label: (context: any) => `${(context.parsed.y ?? 0).toFixed(1)} km`
              }
            }
          },
          scales: {
            y: { beginAtZero: true, title: { display: true, text: 'km cumulés' } },
            x: { grid: { display: false } }
          }
        }
      });
    }
  }

  // Helper methods for secondary charts
  getSeverityDistribution(): { label: string; value: number }[] {
    const light = this.tableData.filter((r: any) => r.excessValue && r.excessValue <= 15).length;
    const medium = this.tableData.filter((r: any) => r.excessValue && r.excessValue > 15 && r.excessValue <= 30).length;
    const severe = this.tableData.filter((r: any) => r.excessValue && r.excessValue > 30).length;
    if (light + medium + severe === 0) return [];
    return [
      { label: '🟢 Léger (≤15 km/h)', value: light },
      { label: '🟡 Modéré (15-30 km/h)', value: medium },
      { label: '🔴 Grave (>30 km/h)', value: severe }
    ];
  }

  getBehaviorSeverityData(): number[] {
    const low = this.tableData.filter((r: any) => r.severity === 'low').length;
    const medium = this.tableData.filter((r: any) => r.severity === 'medium').length;
    const high = this.tableData.filter((r: any) => r.severity === 'high').length;
    return [low, medium, high];
  }

  getFuelEventsData(): { label: string; value: number; color: string }[] {
    const refills = this.tableData.filter((r: any) => r.eventType?.includes('Remplissage')).length;
    const consumption = this.tableData.filter((r: any) => r.eventType?.includes('Consommation')).length;
    const anomalies = this.tableData.filter((r: any) => r.isAnomaly).length;
    const readings = this.tableData.length - refills - consumption - anomalies;
    return [
      { label: '⛽ Remplissages', value: refills, color: '#22C55E' },
      { label: '📉 Consommation', value: consumption, color: '#3B82F6' },
      { label: '⚠️ Anomalies', value: anomalies, color: '#EF4444' },
      { label: '📊 Lectures', value: readings, color: '#94A3B8' }
    ].filter(d => d.value > 0);
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

  // ==================== FUEL ESTIMATION REPORT ====================
  
  executeFuelEstimationReport(vehicleId?: number, startDate?: Date, endDate?: Date) {
    this.loading = true;
    this.fuelEstimationReport = null;
    this.fuelEstimationActiveSection = 'summary';
    
    const startDateStr = startDate ? startDate.toISOString() : undefined;
    const endDateStr = endDate ? endDate.toISOString() : undefined;
    
    this.apiService.getFuelExpenseStatistics(startDateStr, endDateStr, vehicleId).subscribe({
      next: (report) => {
        this.ngZone.run(() => {
          this.fuelEstimationReport = report;
          this.reportGenerated = true;
          this.loading = false;
          
          // Prepare table data for vehicle expenses
          this.tableData = report.vehicleExpenses.map(v => ({
            vehicleId: v.vehicleId,
            vehicleName: v.vehicleName,
            plate: v.plate || '-',
            fuelType: v.fuelType || '-',
            totalDistance: v.totalDistanceKm,
            totalFuel: v.totalFuelConsumedLiters,
            totalCost: v.totalFuelCost,
            avgConsumption: v.averageConsumptionPer100Km,
            deviation: v.deviationFromFleetAverage
          }));
          
          // Prepare statistics
          this.statisticsData = {
            'Véhicules analysés': report.vehicleCount.toString(),
            'Distance totale': `${report.totalFleetDistanceKm.toLocaleString('fr-FR')} km`,
            'Carburant consommé': `${report.totalFleetFuelConsumedLiters.toFixed(1)} L`,
            'Coût total estimé': `${report.totalFleetFuelCost.toFixed(2)} TND`,
            'Consommation moyenne': `${report.fleetAverageConsumptionPer100Km.toFixed(2)} L/100km`,
            'Écart-type': `${report.fleetStandardDeviation.toFixed(2)} L/100km`
          };
          
          // Chart data - fuel type distribution
          this.chartData = report.fuelTypeDistribution.map(d => ({
            label: d.fuelType,
            value: d.totalCost,
            percentage: d.percentage,
            vehicleCount: d.vehicleCount
          }));
          
          this.cdr.detectChanges();
          this.appRef.tick();
          
          // Create distribution chart
          setTimeout(() => this.createFuelEstimationChart(), 100);
        });
      },
      error: (err) => {
        console.error('Error loading fuel estimation report:', err);
        this.ngZone.run(() => {
          this.loading = false;
          this.reportGenerated = true;
          this.statisticsData = { 'Erreur': 'Impossible de charger le rapport d\'estimation carburant' };
          this.cdr.detectChanges();
        });
      }
    });
  }
  
  createFuelEstimationChart() {
    if (!this.fuelEstimationReport) return;
    
    // Destroy existing charts
    if (this.chart) { this.chart.destroy(); this.chart = undefined; }
    if (this.secondaryChart) { this.secondaryChart.destroy(); this.secondaryChart = undefined; }
    
    const expenses = this.fuelEstimationReport.vehicleExpenses || [];
    if (expenses.length === 0) return;

    // Sort by cost descending for better readability
    const sorted = [...expenses].sort((a, b) => b.totalFuelCost - a.totalFuelCost);
    const labels = sorted.map(v => v.vehicleName.length > 12 ? v.vehicleName.substring(0, 12) + '…' : v.vehicleName);

    // ===== CHART 1: Per-vehicle Consumption (L) vs Cost (TND) with L/100km line =====
    const canvas = this.chartCanvas?.nativeElement;
    if (canvas) {
      const ctx = canvas.getContext('2d');
      if (ctx) {
        this.chart = new Chart(ctx, {
          type: 'bar',
          data: {
            labels,
            datasets: [
              {
                label: 'Coût (TND)',
                data: sorted.map(v => v.totalFuelCost),
                backgroundColor: 'rgba(22, 163, 74, 0.75)',
                borderColor: '#16a34a',
                borderWidth: 1,
                borderRadius: 4,
                yAxisID: 'y',
                order: 2
              },
              {
                label: 'Consommation (L)',
                data: sorted.map(v => v.totalFuelConsumedLiters),
                backgroundColor: 'rgba(59, 130, 246, 0.75)',
                borderColor: '#3B82F6',
                borderWidth: 1,
                borderRadius: 4,
                yAxisID: 'y',
                order: 3
              },
              {
                label: 'L/100km',
                data: sorted.map(v => v.averageConsumptionPer100Km),
                type: 'line' as any,
                borderColor: '#f59e0b',
                backgroundColor: 'rgba(245, 158, 11, 0.15)',
                borderWidth: 2,
                pointRadius: 4,
                pointBackgroundColor: '#f59e0b',
                fill: true,
                tension: 0.3,
                yAxisID: 'y1',
                order: 1
              }
            ]
          },
          options: {
            responsive: true,
            maintainAspectRatio: false,
            interaction: { mode: 'index', intersect: false },
            plugins: {
              legend: { position: 'top', labels: { usePointStyle: true, font: { size: 11 } } },
              title: {
                display: true,
                text: 'Consommation & Coût par véhicule',
                font: { size: 14, weight: 'bold' }
              },
              tooltip: {
                callbacks: {
                  afterBody: (items: any[]) => {
                    const idx = items[0]?.dataIndex;
                    if (idx !== undefined) {
                      const v = sorted[idx];
                      return `Distance: ${v.totalDistanceKm.toLocaleString('fr-FR')} km\nType: ${v.fuelType || '-'}`;
                    }
                    return '';
                  }
                }
              }
            },
            scales: {
              x: { ticks: { font: { size: 10 }, maxRotation: 45 } },
              y: {
                type: 'linear',
                position: 'left',
                title: { display: true, text: 'Litres / TND' },
                beginAtZero: true
              },
              y1: {
                type: 'linear',
                position: 'right',
                title: { display: true, text: 'L/100km' },
                beginAtZero: true,
                grid: { drawOnChartArea: false }
              }
            }
          }
        });
      }
    }

    // ===== CHART 2: Fuel type cost distribution (doughnut) =====
    const canvas2 = this.secondaryChartCanvas?.nativeElement;
    if (canvas2 && this.fuelEstimationReport.fuelTypeDistribution.length > 0) {
      const ctx2 = canvas2.getContext('2d');
      if (ctx2) {
        this.secondaryChart = new Chart(ctx2, {
          type: 'doughnut',
          data: {
            labels: this.fuelEstimationReport.fuelTypeDistribution.map(d => `${d.fuelType} (${d.vehicleCount} véh.)`),
            datasets: [{
              data: this.fuelEstimationReport.fuelTypeDistribution.map(d => d.totalCost),
              backgroundColor: ['#3B82F6', '#10B981', '#F59E0B', '#EF4444', '#8B5CF6'],
              borderWidth: 2,
              borderColor: '#fff'
            }]
          },
          options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
              legend: { position: 'right', labels: { font: { size: 11 } } },
              title: {
                display: true,
                text: 'Répartition coûts par type de carburant',
                font: { size: 14, weight: 'bold' }
              },
              tooltip: {
                callbacks: {
                  label: (context) => {
                    const value = context.raw as number;
                    const total = this.fuelEstimationReport!.totalFleetFuelCost;
                    const pct = total > 0 ? ((value / total) * 100).toFixed(1) : '0';
                    return `${context.label}: ${value.toFixed(2)} TND (${pct}%)`;
                  }
                }
              }
            }
          }
        });
      }
    }
  }
  
  createFuelTrendsChart() {
    if (!this.fuelEstimationReport || !this.secondaryChartCanvas) return;
    
    // Destroy existing secondary chart
    if (this.secondaryChart) {
      this.secondaryChart.destroy();
    }
    
    const canvas = this.secondaryChartCanvas.nativeElement;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    
    const trends = this.fuelEstimationReport.monthlyTrends;
    if (trends.length === 0) return;
    
    this.secondaryChart = new Chart(ctx, {
      type: 'bar',
      data: {
        labels: trends.map(t => t.monthName),
        datasets: [
          {
            label: 'Coût (TND)',
            data: trends.map(t => t.totalCost),
            backgroundColor: '#3B82F6',
            borderRadius: 4,
            yAxisID: 'y'
          },
          {
            label: 'Consommation (L)',
            data: trends.map(t => t.totalFuelConsumed),
            backgroundColor: '#10B981',
            borderRadius: 4,
            yAxisID: 'y1'
          }
        ]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { position: 'top' },
          title: {
            display: true,
            text: 'Tendances mensuelles',
            font: { size: 14, weight: 'bold' }
          }
        },
        scales: {
          y: {
            type: 'linear',
            position: 'left',
            title: { display: true, text: 'Coût (TND)' }
          },
          y1: {
            type: 'linear',
            position: 'right',
            title: { display: true, text: 'Litres' },
            grid: { drawOnChartArea: false }
          }
        }
      }
    });
  }
  
  selectFuelEstimationSection(sectionId: string) {
    this.fuelEstimationActiveSection = sectionId;
    
    // Recreate appropriate chart based on section
    setTimeout(() => {
      if (sectionId === 'distribution') {
        this.createFuelEstimationChart();
      } else if (sectionId === 'trends') {
        this.createFuelTrendsChart();
      }
    }, 100);
  }
  
  getEfficiencyClass(deviation: number): string {
    if (deviation <= -10) return 'efficiency-excellent';
    if (deviation <= -2) return 'efficiency-good';
    if (deviation <= 2) return 'efficiency-average';
    if (deviation <= 10) return 'efficiency-poor';
    return 'efficiency-bad';
  }
  
  getEfficiencyLabel(deviation: number): string {
    if (deviation <= -10) return 'Excellent';
    if (deviation <= -2) return 'Bon';
    if (deviation <= 2) return 'Moyen';
    if (deviation <= 10) return 'Médiocre';
    return 'Mauvais';
  }

  getTotalRefuelLiters(refuels: any[]): number {
    return refuels.reduce((sum: number, r: any) => sum + (r.fuelAddedLiters || 0), 0);
  }

  getRefuelTooltip(refuels: any[]): string {
    return refuels.map((r: any) => {
      const date = new Date(r.timestamp);
      const dateStr = date.toLocaleDateString('fr-FR') + ' ' + date.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
      return `${dateStr}: +${r.fuelAddedLiters.toFixed(1)} L (~${(r.estimatedCost || 0).toFixed(2)} TND)`;
    }).join('\n');
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

  // ==================== REPAIRS REPORT (Coûts véhicules) ====================
  
  executeCostsReport(vehicleId?: number, startDate?: Date, endDate?: Date) {
    this.loading = true;
    
    const options: any = {};
    if (vehicleId) options.vehicleId = vehicleId;
    if (startDate) options.fromDate = startDate.toISOString().split('T')[0];
    if (endDate) options.toDate = endDate.toISOString().split('T')[0];
    
    this.apiService.getRepairs(options).subscribe({
      next: (result) => {
        this.ngZone.run(() => {
          this.processRepairsReport(result.items || []);
          this.reportGenerated = true;
          this.loading = false;
          this.activeTab = 'table';
          this.currentPage = 1;
          this.cdr.detectChanges();
          setTimeout(() => this.createChart(), 100);
        });
      },
      error: (error) => {
        console.error('Error loading repairs:', error);
        this.ngZone.run(() => {
          this.tableData = [];
          this.chartData = [];
          this.statisticsData = { 'Erreur': 'Impossible de charger les réparations' };
          this.reportGenerated = true;
          this.loading = false;
          this.cdr.detectChanges();
        });
      }
    });
  }

  processRepairsReport(repairs: any[]) {
    if (!repairs || repairs.length === 0) {
      this.tableData = [];
      this.chartData = [];
      this.statisticsData = { 'Information': 'Aucune réparation enregistrée pour cette période' };
      return;
    }

    // Sort by date descending
    repairs.sort((a, b) => new Date(b.repairDate).getTime() - new Date(a.repairDate).getTime());

    // Build vehicle name map
    const vehicleMap = new Map<number, string>();
    this.vehicles.forEach(v => vehicleMap.set(v.id, v.name || v.plateNumber || `Véhicule ${v.id}`));

    // Process table data
    this.tableData = repairs.map(repair => {
      const vehicleName = repair.vehicleName || vehicleMap.get(repair.vehicleId) || `Véhicule ${repair.vehicleId}`;
      return {
        vehicleName: vehicleName,
        vehicleId: repair.vehicleId,
        date: this.formatDateTime(repair.repairDate),
        reference: repair.reference || '-',
        description: repair.description || '-',
        supplierName: repair.supplierName || '-',
        laborCost: repair.laborCost || 0,
        laborCostFormatted: this.formatCurrency(repair.laborCost || 0),
        partsCost: repair.partsCost || 0,
        partsCostFormatted: this.formatCurrency(repair.partsCost || 0),
        totalCost: repair.totalCost || 0,
        totalCostFormatted: this.formatCurrency(repair.totalCost || 0),
        status: this.getRepairStatusLabel(repair.status),
        statusKey: repair.status,
        mileage: repair.mileageAtRepair ? `${repair.mileageAtRepair.toLocaleString('fr-FR')} km` : '-',
        invoiceNumber: repair.invoiceNumber || '-'
      };
    });

    // Chart data - group by status
    const byStatus: { [key: string]: number } = {};
    repairs.forEach(repair => {
      const statusLabel = this.getRepairStatusLabel(repair.status);
      byStatus[statusLabel] = (byStatus[statusLabel] || 0) + 1;
    });

    this.chartData = Object.entries(byStatus)
      .sort((a, b) => b[1] - a[1])
      .map(([label, value], index) => ({
        label,
        value,
        color: this.chartColors[index % this.chartColors.length]
      }));

    // Secondary chart - costs by vehicle
    const costsByVehicle: { [key: string]: number } = {};
    repairs.forEach(repair => {
      const vehicleName = repair.vehicleName || vehicleMap.get(repair.vehicleId) || `Véhicule ${repair.vehicleId}`;
      costsByVehicle[vehicleName] = (costsByVehicle[vehicleName] || 0) + (repair.totalCost || 0);
    });

    this.secondaryChartData = Object.entries(costsByVehicle)
      .sort((a, b) => b[1] - a[1])
      .slice(0, 10)
      .map(([label, value], index) => ({
        label,
        value,
        color: this.chartColors[index % this.chartColors.length]
      }));

    // Statistics
    const totalCost = repairs.reduce((sum, r) => sum + (r.totalCost || 0), 0);
    const totalLaborCost = repairs.reduce((sum, r) => sum + (r.laborCost || 0), 0);
    const totalPartsCost = repairs.reduce((sum, r) => sum + (r.partsCost || 0), 0);
    const completedCount = repairs.filter(r => r.status === 'completed' || r.status === 'done').length;
    const pendingCount = repairs.filter(r => r.status === 'pending' || r.status === 'in_progress').length;

    this.statisticsData = {
      'Total réparations': repairs.length.toString(),
      'Coût total': this.formatCurrency(totalCost),
      '🔧 Main d\'oeuvre': this.formatCurrency(totalLaborCost),
      '📦 Pièces': this.formatCurrency(totalPartsCost),
      '✅ Complétées': completedCount.toString(),
      '⏳ En cours': pendingCount.toString(),
      'Véhicules': new Set(repairs.map(r => r.vehicleId)).size.toString()
    };
  }

  getRepairStatusLabel(status: string): string {
    const statuses: { [key: string]: string } = {
      'pending': '⏳ En attente',
      'in_progress': '🔄 En cours',
      'completed': '✅ Complétée',
      'done': '✅ Complétée',
      'cancelled': '❌ Annulée'
    };
    return statuses[status] || status || '⏳ En attente';
  }

  // ==================== MAINTENANCE REPORT ====================
  
  executeMaintenanceReport(vehicleId?: number, startDate?: Date, endDate?: Date) {
    this.loading = true;
    
    this.apiService.getMaintenanceRecords(vehicleId).subscribe({
      next: (records) => {
        // Filter by date if provided
        let filteredRecords = records;
        if (startDate || endDate) {
          filteredRecords = records.filter(r => {
            const recordDate = new Date(r.date || r.scheduledDate || r.createdAt);
            if (startDate && recordDate < startDate) return false;
            if (endDate && recordDate > endDate) return false;
            return true;
          });
        }
        
        this.ngZone.run(() => {
          this.processMaintenanceReport(filteredRecords);
          this.reportGenerated = true;
          this.loading = false;
          this.activeTab = 'table';
          this.currentPage = 1;
          this.cdr.detectChanges();
          setTimeout(() => this.createChart(), 100);
        });
      },
      error: (error) => {
        console.error('Error loading maintenance records:', error);
        this.ngZone.run(() => {
          this.tableData = [];
          this.chartData = [];
          this.statisticsData = { 'Erreur': 'Impossible de charger les maintenances' };
          this.reportGenerated = true;
          this.loading = false;
          this.cdr.detectChanges();
        });
      }
    });
  }

  processMaintenanceReport(records: any[]) {
    if (!records || records.length === 0) {
      this.tableData = [];
      this.chartData = [];
      this.statisticsData = { 'Information': 'Aucune maintenance enregistrée pour cette période' };
      return;
    }

    // Sort by date descending
    records.sort((a, b) => {
      const dateA = new Date(a.date || a.scheduledDate || a.createdAt);
      const dateB = new Date(b.date || b.scheduledDate || b.createdAt);
      return dateB.getTime() - dateA.getTime();
    });

    // Build vehicle name map
    const vehicleMap = new Map<number, string>();
    this.vehicles.forEach(v => vehicleMap.set(v.id, v.name || v.plateNumber || `Véhicule ${v.id}`));

    // Process table data
    this.tableData = records.map(record => {
      const vehicleName = vehicleMap.get(record.vehicleId) || `Véhicule ${record.vehicleId}`;
      const date = record.date || record.scheduledDate || record.createdAt;
      return {
        vehicleName: vehicleName,
        vehicleId: record.vehicleId,
        date: this.formatDateTime(date),
        type: record.type || record.maintenanceType || 'Général',
        description: record.description || record.notes || '-',
        status: this.getMaintenanceStatusLabel(record.status),
        statusKey: record.status,
        cost: record.cost || record.totalCost || 0,
        costFormatted: this.formatCurrency(record.cost || record.totalCost || 0),
        mileage: record.mileage ? `${record.mileage.toLocaleString('fr-FR')} km` : '-'
      };
    });

    // Chart data - group by type
    const byType: { [key: string]: number } = {};
    records.forEach(record => {
      const type = record.type || record.maintenanceType || 'Général';
      byType[type] = (byType[type] || 0) + 1;
    });

    this.chartData = Object.entries(byType)
      .sort((a, b) => b[1] - a[1])
      .map(([label, value], index) => ({
        label,
        value,
        color: this.chartColors[index % this.chartColors.length]
      }));

    // Secondary chart - costs by vehicle
    const costsByVehicle: { [key: string]: number } = {};
    records.forEach(record => {
      const vehicleName = vehicleMap.get(record.vehicleId) || `Véhicule ${record.vehicleId}`;
      costsByVehicle[vehicleName] = (costsByVehicle[vehicleName] || 0) + (record.cost || record.totalCost || 0);
    });

    this.secondaryChartData = Object.entries(costsByVehicle)
      .sort((a, b) => b[1] - a[1])
      .slice(0, 10)
      .map(([label, value], index) => ({
        label,
        value,
        color: this.chartColors[index % this.chartColors.length]
      }));

    // Statistics
    const totalCost = records.reduce((sum, r) => sum + (r.cost || r.totalCost || 0), 0);
    const completedCount = records.filter(r => r.status === 'completed' || r.status === 'done').length;
    const scheduledCount = records.filter(r => r.status === 'scheduled' || r.status === 'pending').length;
    const overdueCount = records.filter(r => r.status === 'overdue').length;

    this.statisticsData = {
      'Total maintenances': records.length.toString(),
      'Coût total': this.formatCurrency(totalCost),
      '✅ Complétées': completedCount.toString(),
      '📅 Planifiées': scheduledCount.toString(),
      '⚠️ En retard': overdueCount.toString(),
      'Véhicules': new Set(records.map(r => r.vehicleId)).size.toString()
    };
  }

  getMaintenanceStatusLabel(status: string): string {
    const statuses: { [key: string]: string } = {
      'completed': '✅ Complétée',
      'done': '✅ Complétée',
      'scheduled': '📅 Planifiée',
      'pending': '⏳ En attente',
      'in_progress': '🔄 En cours',
      'overdue': '⚠️ En retard',
      'cancelled': '❌ Annulée'
    };
    return statuses[status] || status || '⏳ En attente';
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
