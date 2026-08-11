import { Component, OnInit, OnDestroy, ViewChild, ElementRef, NgZone, ChangeDetectorRef, ApplicationRef, HostListener, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService, FuelRecordsResult, FuelRecord, DailyActivityReport, ActivitySegment, MileageReport, DailyMileage, MonthlyFleetReport, MileagePeriodReport, MileagePeriodType, HourlyMileagePeriod, DailyMileagePeriod, MonthlyMileagePeriod, VehicleStopsResult, VehicleStopDto, FleetFuelStatisticsDto, VehicleFuelExpenseDto, FuelTypeDistributionDto, MonthlyFuelTrendDto, MonthlyCostReport, VehicleMonthlyCost, DepartmentCostGroup, FuelAuditReport, FuelLevelPoint, FuelCardFill, FuelConsumptionComparisonReport, ConsumptionSegmentsReport, ConsumptionSegment, ConsumptionByTonnageReport, VehicleLoadPeriod } from '../services/api.service';
import { Subject, forkJoin, of, takeUntil, catchError } from 'rxjs';
import { GeocodingService } from '../services/geocoding.service';
import { AppLayoutComponent } from './shared/app-layout.component';
import { AdminService } from '../admin/services/admin.service';
import { PdfExportService } from '../services/pdf-export.service';
import { ReportStateService } from '../services/report-state.service';
import { PermissionService } from '../services/permission.service';
import { ButtonComponent, CardComponent, DataTableComponent } from './shared/ui';
import { USER_PREF_PIPES } from '../pipes/user-preference-pipes';
import { UserPreferencesService } from '../services/user-preferences.service';
import { Chart, ChartConfiguration, registerables } from 'chart.js';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { marked } from 'marked';
import * as L from 'leaflet';

Chart.register(...registerables);

@Component({
  selector: 'app-reports',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent, ButtonComponent, CardComponent, DataTableComponent, ...USER_PREF_PIPES],
  templateUrl: './reports.component.html',
  styleUrls: ['./reports.component.css']
})
export class ReportsComponent implements OnInit, OnDestroy {
  @Input() embedded = false;
  private destroy$ = new Subject<void>();
  @ViewChild('chartCanvas') chartCanvas?: ElementRef<HTMLCanvasElement>;
  @ViewChild('chartScrollContainer') chartScrollContainer?: ElementRef<HTMLDivElement>;
  @ViewChild('secondaryChartCanvas') secondaryChartCanvas?: ElementRef<HTMLCanvasElement>;
  @ViewChild('kmBarChart') kmBarChartRef?: ElementRef<HTMLCanvasElement>;
  @ViewChild('fuelPieChart') fuelPieChartRef?: ElementRef<HTMLCanvasElement>;
  @ViewChild('maintenanceAreaChart') maintenanceAreaChartRef?: ElementRef<HTMLCanvasElement>;
  @ViewChild('mileagePeriodChart') mileagePeriodChartRef?: ElementRef<HTMLCanvasElement>;
  @ViewChild('comparisonCanvas') comparisonCanvasRef?: ElementRef<HTMLCanvasElement>;
  @ViewChild('comparisonConsoCanvas') comparisonConsoCanvasRef?: ElementRef<HTMLCanvasElement>;
  @ViewChild('consumptionCanvas') consumptionCanvasRef?: ElementRef<HTMLCanvasElement>;
  @ViewChild('mapPopupContainer') mapPopupContainer?: ElementRef<HTMLDivElement>;
  
  // Map popup state
  showMapPopup = false;
  mapPopupData: { latitude: number; longitude: number; vehicle: string; speed: string; limit: string; time: string; address: string; severityLabel: string } | null = null;
  private popupMap?: L.Map;

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
    {
      id: '17',
      name: 'Carburant réel vs GPS',
      type: 'fuel-comparison',
      icon: '🔍',
      description: 'Pleins réels posés sur la courbe du niveau de carburant',
      category: 'costs'
    },
    {
      id: '18',
      name: 'Analyse conso par segments',
      type: 'consumption-analysis',
      icon: '📐',
      description: 'Consommation par tranches de X km, min/max et comparaison par tonnage',
      category: 'costs'
    },
    {
      id: '15',
      name: 'Coûts mensuel par véhicule',
      type: 'monthly-costs',
      icon: '📋',
      description: 'Carburant, entretien, réparation par département',
      category: 'costs'
    },
    {
      id: '16',
      name: 'Consommation carburant mensuel',
      type: 'monthly-fuel',
      icon: '🛢️',
      description: 'Litres consommés par département',
      category: 'costs'
    },
    // Statistics Reports
    {
      id: '9',
      name: 'Rapport mensuel flotte',
      type: 'monthly',
      icon: '📊',
      description: 'KPIs et analyses complètes',
      category: 'stats'
    },
    // AI Reports
    {
      id: '14',
      name: 'Rapport IA Flotte',
      type: 'ai-fleet',
      icon: '🤖',
      description: 'Analyse IA avec TCO et recommandations',
      category: 'ai'
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

  // Filter type checkboxes
  filterByVehicle = true;
  filterByDriver = true;
  filterByDepartment = false;

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
    highRpm: true,
    speedLimitViolation: true
  };
  
  // Driving behavior metric cards
  drivingBehaviorCards: { rpmMax: number; vitesseMax: number; scoreConduite: number; consommation: number | null } = {
    rpmMax: 0, vitesseMax: 0, scoreConduite: 100, consommation: null
  };

  // Incident type definitions with colors
  incidentTypes = [
    { key: 'harshAcceleration', label: 'Accélérations brusques', color: '#FF6B6B', icon: '⚡' },
    { key: 'harshBraking', label: 'Freinages brusques', color: '#4ECDC4', icon: '🛑' },
    { key: 'sharpSteering', label: 'Virages brusques', color: '#45B7D1', icon: '↩️' },
    { key: 'overspeed', label: 'Vitesse > 130 km/h', color: '#FFA07A', icon: '🏎️' },
    { key: 'highRpm', label: 'RPM > 3500', color: '#9B59B6', icon: '⚙️' },
    { key: 'speedLimitViolation', label: 'Dépassement limite', color: '#E74C3C', icon: '🚫' }
  ];
  
  fromDate = '';
  toDate = '';
  todayStr = ''; // Set in initializeMileagePeriodDates() to avoid UTC date shift

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

  // Sorting
  sortColumn: string = '';
  sortDirection: 'asc' | 'desc' = 'asc';

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
  
  // Monthly cost report data
  monthlyCostReport: MonthlyCostReport | null = null;
  monthlyCostReportType: 'costs' | 'fuel' = 'costs';

  // Carburant réel vs GPS : courbe du niveau de réservoir (jauge) avec les pleins
  // réellement facturés posés dessus (single vehicle). Une remontée sans cercle = plein non déclaré.
  comparisonAudit: FuelAuditReport | null = null;

  // Rapport 17, second graphe : consommation mesurée (jauge) vs réelle (factures,
  // méthode plein à plein) par intervalle entre deux pleins consécutifs.
  comparisonConsumption: FuelConsumptionComparisonReport | null = null;

  // Analyse consommation par segments de X km + comparaison par tonnage (single vehicle)
  consumptionReport: ConsumptionSegmentsReport | null = null;
  /** Les tranches sans données exploitables sont masquées par défaut (vue client) —
   *  affichables via une case discrète pour le diagnostic interne. */
  showExcludedSegments = false;
  /** Explication IA de la tranche cliquée (Groq) */
  aiSegment: ConsumptionSegment | null = null;
  aiExplanation = '';
  aiLoading = false;
  consumptionByTonnage: ConsumptionByTonnageReport | null = null;
  loadPeriods: VehicleLoadPeriod[] = [];
  segmentKm = 100;   // taille de tranche paramétrable (km)
  segmentKmPresets = [50, 100, 200];
  /** Sections repliables (fermées par défaut) : déclaration de chargement + détail des tranches. */
  showLoadPeriodsPanel = false;
  showSegmentDetailsPanel = false;
  newLoadPeriod: { startTime: string; endTime: string; tonnageT: number | null; notes: string } = { startTime: '', endTime: '', tonnageT: null, notes: '' };
  loadPeriodError = '';

  // Fuel estimation report data
  fuelEstimationReport: FleetFuelStatisticsDto | null = null;
  fuelEstimationActiveSection = 'summary';
  fuelEstimationSections = [
    { id: 'summary', label: 'Résumé', icon: '📊' },
    { id: 'vehicles', label: 'Par Véhicule', icon: '🚗' },
    { id: 'distribution', label: 'Distribution', icon: '⛽' },
    { id: 'trends', label: 'Tendances', icon: '📈' }
  ];
  
  // AI Fleet Report data
  aiFleetReport: any = null;
  aiFleetLoading = false;
  aiFleetAnalysisHtml: SafeHtml = '';
  aiFleetPeriod = 'month';
  aiFleetQaMessages: { role: string; text: string; html: SafeHtml }[] = [];
  aiFleetQuestionInput = '';
  aiFleetAskLoading = false;
  aiFleetSuggestions = [
    'Quel véhicule devrait être remplacé en priorité ?',
    'Quelles pièces sont à surveiller sur mes véhicules ?',
    'Comment réduire les coûts de maintenance ?',
    'Quel est le coût total de possession par véhicule ?'
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
  private comparisonChart?: Chart;
  private comparisonConsoChart?: Chart;
  private consumptionChart?: Chart;

  constructor(
    private router: Router,
    private apiService: ApiService,
    private geocodingService: GeocodingService,
    private ngZone: NgZone,
    private cdr: ChangeDetectorRef,
    private appRef: ApplicationRef,
    private adminService: AdminService,
    private pdfExportService: PdfExportService,
    private sanitizer: DomSanitizer,
    private reportStateService: ReportStateService,
    private permissionService: PermissionService,
    private userPrefs: UserPreferencesService
  ) {}

  ngOnInit() {
    if (!this.embedded && !this.apiService.isAuthenticated()) {
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
      this.initializeDates();
      this.loadData(() => this.restoreState());
    });
  }

  initializeMileagePeriodDates() {
    const today = new Date();
    const yesterday = new Date(today);
    yesterday.setDate(today.getDate() - 1);
    const weekAgo = new Date(today);
    weekAgo.setDate(today.getDate() - 7);

    // Set todayStr for max date on inputs (local timezone)
    this.todayStr = this.toLocalDate(today);

    // Hourly: default to yesterday
    this.mileagePeriodDate = this.toLocalDate(yesterday);

    // Daily: default to last 7 days
    this.mileagePeriodStartDate = this.toLocalDate(weekAgo);
    this.mileagePeriodEndDate = this.toLocalDate(today);

    // Monthly: default to previous month (current month often has no data yet)
    const prevMonth = new Date(today);
    prevMonth.setMonth(today.getMonth() - 1);
    this.mileagePeriodMonth = prevMonth.getMonth() + 1;
    this.mileagePeriodYear = prevMonth.getFullYear();

    // Initialize custom date range + daily report date
    this.customStartDate = this.toLocalDate(weekAgo);
    this.customEndDate = this.toLocalDate(today);
    this.dailyReportDate = this.toLocalDate(yesterday);
  }

  private getVehiclesObs() {
    return this.embedded ? this.adminService.getVehicles() : this.apiService.getVehicles();
  }

  private getVehicleHistoryObs(vehicleId: number, from?: Date, to?: Date, maxPoints = 3000) {
    return this.embedded
      ? this.adminService.getVehicleHistory(vehicleId, from, to, maxPoints)
      : this.apiService.getVehicleHistory(vehicleId, from, to, maxPoints);
  }

  loadData(onVehiclesReady?: () => void) {
    this.getVehiclesObs().pipe(takeUntil(this.destroy$)).subscribe({
      next: (vehicles) => { this.vehicles = vehicles; if (onVehiclesReady) onVehiclesReady(); },
      error: (err) => console.error('Error loading vehicles:', err)
    });
    this.apiService.getDrivers().pipe(takeUntil(this.destroy$)).subscribe({
      next: (drivers) => this.drivers = drivers,
      error: (err) => console.error('Error loading drivers:', err)
    });
    this.apiService.getDepartments().pipe(takeUntil(this.destroy$)).subscribe({
      next: (departments) => { this.departments = departments; this.cdr.detectChanges(); },
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

  onFilterTypeChange() {
    // Clear filters for unchecked types
    if (!this.filterByVehicle) this.selectedVehicleId = '';
    if (!this.filterByDriver) this.selectedDriverId = '';
    if (!this.filterByDepartment) this.selectedDepartmentId = '';
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

  // Auto-execute wrappers — triggered by (change) / (click) in template
  onVehicleChange() {
    if (this.selectedTemplateId && !this.loading) {
      this.executeReport();
    }
  }

  onStandardPeriodChange(period: string) {
    this.selectStandardPeriod(period);
    if (this.selectedTemplateId && !this.loading) {
      this.executeReport();
    }
  }

  onCostPeriodChange(period: string) {
    this.selectCostPeriod(period);
    if (this.selectedTemplateId && !this.loading) {
      this.executeReport();
    }
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
        const thirtyDaysAgo = new Date(today);
        thirtyDaysAgo.setDate(today.getDate() - 30);
        this.fromDate = this.toDateTime(thirtyDaysAgo);
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

  /** Returns YYYY-MM-DD in local timezone (never UTC) */
  toLocalDate(date: Date): string {
    const y = date.getFullYear();
    const m = String(date.getMonth() + 1).padStart(2, '0');
    const d = String(date.getDate()).padStart(2, '0');
    return `${y}-${m}-${d}`;
  }

  /** Chart container min-width capped to avoid exceeding browser canvas limits.
   *  Calypso 7 — pour le rapport Carburant l utilisateur veut voir tout le
   *  graphe d un coup, sans defiler horizontalement. La courbe etant lisse
   *  (line chart sur evolution dans le temps) elle reste lisible meme tassee.
   *  On force donc 'auto' pour ce template pour que le canvas se cale a la
   *  largeur du conteneur. Pour les autres rapports (souvent des bar charts
   *  avec beaucoup de categories) le comportement actuel reste le bon. */
  getChartMinWidth(): string {
    if (this.selectedTemplate?.type === 'fuel') return 'auto';
    if (this.chartData.length <= 20) return 'auto';
    return Math.min(this.chartData.length * 40, 5000) + 'px';
  }

  /** True quand le rapport courant doit afficher les boutons de scroll
   *  horizontal autour du graphe. Faux pour Carburant (fit-to-width). */
  showChartScrollButtons(): boolean {
    if (this.selectedTemplate?.type === 'fuel') return false;
    return this.chartData.length > 20;
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
    // Fuel comparison / segment analysis are meaningless on "today" (fuel and km accrue over time) -> default to month.
    if (this.selectedTemplate?.type === 'fuel-comparison' || this.selectedTemplate?.type === 'consumption-analysis') this.selectStandardPeriod('month');
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
    if (template?.type === 'fuel-comparison' || template?.type === 'consumption-analysis') this.selectStandardPeriod('month');
    this.reportGenerated = false;
    this.tableData = [];
    this.chartData = [];
    this.secondaryChartData = [];
    this.statisticsData = {};
  }

  getTemplatesByCategory(category: string): any[] {
    return this.templates.filter(t => {
      if ((t as any).category !== category) return false;
      // Map template type to report permission key (handle hyphen vs underscore)
      const reportKey = (t as any).type?.replace(/-/g, '_');
      return this.permissionService.hasReportAccess(reportKey);
    });
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
    this.reportStateService.clear();
  }

  destroyAllCharts() {
    if (this.chart) { this.chart.destroy(); this.chart = undefined; }
    if (this.secondaryChart) { this.secondaryChart.destroy(); this.secondaryChart = undefined; }
    if (this.mileagePeriodChart) { this.mileagePeriodChart.destroy(); this.mileagePeriodChart = undefined; }
    if (this.kmBarChart) { this.kmBarChart.destroy(); this.kmBarChart = undefined; }
    if (this.fuelPieChart) { this.fuelPieChart.destroy(); this.fuelPieChart = undefined; }
    if (this.maintenanceAreaChart) { this.maintenanceAreaChart.destroy(); this.maintenanceAreaChart = undefined; }
    if (this.comparisonChart) { this.comparisonChart.destroy(); this.comparisonChart = undefined; }
    if (this.comparisonConsoChart) { this.comparisonConsoChart.destroy(); this.comparisonConsoChart = undefined; }
    if (this.consumptionChart) { this.consumptionChart.destroy(); this.consumptionChart = undefined; }
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

  sortBy(column: string) {
    if (this.sortColumn === column) {
      this.sortDirection = this.sortDirection === 'asc' ? 'desc' : 'asc';
    } else {
      this.sortColumn = column;
      this.sortDirection = 'asc';
    }
    const dir = this.sortDirection === 'asc' ? 1 : -1;
    // Columns that have a dedicated numeric sort key (e.g. 'period' → '_periodSort')
    const sortKeyColumn = `_${column}Sort`;

    // Bloc B3 (correction Calypso 7) : on retire les pseudo-lignes « day-header »
    // avant le tri. Sinon elles n'ont pas de _xxxSort et finissent toutes à la
    // fin, ce qui casse le tri visible sur la colonne Date du tableau Trajets.
    const dataRows = this.tableData.filter((r: any) => !r.isDayHeader);
    const sorted = [...dataRows].sort((a: any, b: any) => {
      // Use dedicated sort key if available (for dates/periods stored as display strings)
      let valA = a[sortKeyColumn] !== undefined ? a[sortKeyColumn] : a[column];
      let valB = b[sortKeyColumn] !== undefined ? b[sortKeyColumn] : b[column];
      // Handle null/undefined
      if (valA == null && valB == null) return 0;
      if (valA == null) return 1;
      if (valB == null) return -1;
      // Numeric sort for known numeric fields
      if (typeof valA === 'number' && typeof valB === 'number') {
        return (valA - valB) * dir;
      }
      // Try parsing as number (for formatted strings with numeric sortKey)
      const numA = parseFloat(valA);
      const numB = parseFloat(valB);
      if (!isNaN(numA) && !isNaN(numB)) {
        return (numA - numB) * dir;
      }
      // String sort
      return String(valA).localeCompare(String(valB), 'fr') * dir;
    });

    // Si la colonne triée est temporelle ET que le tableau utilisait
    // initialement un groupement par jour (trips/stops), on régénère les
    // entêtes de jour suivant le nouvel ordre. Sinon on rend le tableau plat,
    // car des entêtes répétés au milieu d'un tri par distance/vitesse n'ont
    // aucun sens.
    const dateColumns = ['startTime', 'endTime', 'time', 'date'];
    const hadDayHeaders = this.tableData.some((r: any) => r.isDayHeader);
    if (hadDayHeaders && dateColumns.includes(column)) {
      this.tableData = this.regroupRowsByDay(sorted);
    } else {
      this.tableData = sorted;
    }
    this.currentPage = 1;
    this.cdr.detectChanges();
  }

  /**
   * Regenerate "📅 day" header rows around an already-sorted list of trip /
   * stop rows. Used by sortBy() so day grouping survives a re-sort on a
   * date column (Bloc B3 — correction Calypso 7).
   */
  private regroupRowsByDay(rows: any[]): any[] {
    const grouped: any[] = [];
    let currentDay = '';
    const type = this.selectedTemplate?.type;
    for (const row of rows) {
      // Both trips (startTime) and stops (time) put the date as the first
      // space-separated token of the formatted string.
      const stamp = row.startTime || row.time || '';
      const dayKey = typeof stamp === 'string' ? stamp.split(' ')[0] : '';
      if (dayKey && dayKey !== currentDay) {
        currentDay = dayKey;
        if (type === 'trips') {
          const dayTrips = rows.filter((r: any) => r.startTime?.startsWith?.(dayKey) && r.isTrip);
          const tripNums = dayTrips.map((r: any) => r.tripNumber).filter((n: any) => n != null);
          const dayLabel = tripNums.length > 0
            ? `📅 ${dayKey} — Trajets ${Math.min(...tripNums)} à ${Math.max(...tripNums)}`
            : `📅 ${dayKey}`;
          grouped.push({ isDayHeader: true, dayLabel, _sortKey: dayKey });
        } else if (type === 'stops') {
          const dayStops = rows.filter((r: any) => r.time?.startsWith?.(dayKey));
          grouped.push({ isDayHeader: true, dayLabel: `📅 ${dayKey} — ${dayStops.length} arrêt(s)`, _sortKey: dayKey });
        } else {
          grouped.push({ isDayHeader: true, dayLabel: `📅 ${dayKey}`, _sortKey: dayKey });
        }
      }
      grouped.push(row);
    }
    return grouped;
  }

  getSortIcon(column: string): string {
    if (this.sortColumn !== column) return '↕';
    return this.sortDirection === 'asc' ? '▲' : '▼';
  }

  scrollChart(direction: 'left' | 'right') {
    const container = this.chartScrollContainer?.nativeElement;
    if (!container) return;
    const scrollAmount = container.clientWidth * 0.6;
    container.scrollBy({ left: direction === 'right' ? scrollAmount : -scrollAmount, behavior: 'smooth' });
  }

  executeReport() {
    // Reset sort state for new report
    this.sortColumn = '';
    this.sortDirection = 'asc';

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
    const singleVehicleReports = ['fuel', 'daily', 'consumption-analysis', 'fuel-comparison'];
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
    this.monthlyCostReport = null;
    this.monthlyReport = null;
    this.mileagePeriodReport = null;
    this.fuelEstimationReport = null;
    this.comparisonAudit = null;
    this.comparisonConsumption = null;
    this.consumptionReport = null;
    this.consumptionByTonnage = null;
    if (this.comparisonChart) { this.comparisonChart.destroy(); this.comparisonChart = undefined; }
    if (this.comparisonConsoChart) { this.comparisonConsoChart.destroy(); this.comparisonConsoChart = undefined; }
    if (this.consumptionChart) { this.consumptionChart.destroy(); this.consumptionChart = undefined; }

    // Re-compute dates from the selected period to ensure fresh timestamps
    if (this.selectedStandardPeriod !== 'custom') {
      this.selectStandardPeriod(this.selectedStandardPeriod);
    }
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
      if (vehicleId) {
        this.executeMileageReport(vehicleId, startDate, endDate);
      } else {
        this.executeMileageReportAllVehicles(startDate, endDate);
      }
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

    // Handle monthly cost reports (costs + fuel consumption)
    if (this.selectedTemplate.type === 'monthly-costs' || this.selectedTemplate.type === 'monthly-fuel') {
      this.monthlyCostReportType = this.selectedTemplate.type === 'monthly-costs' ? 'costs' : 'fuel';
      this.executeMonthlyCostReport();
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

    // Handle trips report using backend API
    if (this.selectedTemplate.type === 'trips') {
      if (vehicleId) {
        this.executeTripsReport(vehicleId, startDate, endDate);
      } else {
        this.executeTripsReportAllVehicles(startDate, endDate);
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

    // Handle AI fleet report
    if (this.selectedTemplate.type === 'ai-fleet') {
      this.executeAiFleetReport();
      return;
    }

    // Handle fuel comparison (pleins réels vs consommation mesurée) report — single vehicle, guarded above
    if (this.selectedTemplate.type === 'fuel-comparison') {
      this.executeFuelComparisonReport(vehicleId!, startDate, endDate);
      return;
    }

    // Handle consumption analysis by segments (single vehicle, guarded above)
    if (this.selectedTemplate.type === 'consumption-analysis') {
      this.executeConsumptionAnalysisReport(vehicleId!, startDate, endDate);
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
      this.getVehicleHistoryObs(vehicle.id, startDate, endDate, 10000).pipe(takeUntil(this.destroy$)).subscribe({
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
      // Trips report is now handled by backend API via executeTripsReport
      return;
    } else if (type === 'speed') {
      this.processSpeedReportAllVehicles(positions);
    } else {
      // Default: just show positions
      this.tableData = positions.slice(0, 100).map(p => ({
        vehicleName: p.vehicleName,
        time: this.formatDateTime(p.recordedAt),
        _timeSort: new Date(p.recordedAt).getTime(),
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

    const SPEED_THRESHOLD = 3;
    const allTrips: any[] = [];
    let tripNumber = 0;

    byVehicle.forEach((vehiclePositions, vehicleId) => {
      const vehicleName = vehiclePositions[0]?.vehicleName || 'Véhicule';
      const hasIgnitionData = vehiclePositions.some((p: any) => p.ignitionOn === true || p.ignitionOn === false);
      
      let idx = 0;
      while (idx < vehiclePositions.length) {
        const pos = vehiclePositions[idx];
        const ignitionOn = hasIgnitionData ? (pos.ignitionOn === true) : ((pos.speedKph || 0) > SPEED_THRESHOLD);
        const isMoving = (pos.speedKph || 0) > SPEED_THRESHOLD;

        if (ignitionOn && isMoving) {
          const drivePositions: any[] = [pos];
          let driveDistanceKm = 0;
          idx++;
          let consecutiveSlowPoints = 0;

          while (idx < vehiclePositions.length) {
            const nextPos = vehiclePositions[idx];
            const nextSpeed = nextPos.speedKph || 0;
            const nextIgnition = hasIgnitionData ? (nextPos.ignitionOn === true) : (nextSpeed > SPEED_THRESHOLD);

            if (nextSpeed > SPEED_THRESHOLD) {
              consecutiveSlowPoints = 0;
              const prev = drivePositions[drivePositions.length - 1];
              const dist = this.haversineDistance(prev.latitude, prev.longitude, nextPos.latitude, nextPos.longitude);
              if (!Number.isNaN(dist) && dist > 0.01 && dist < 5) {
                driveDistanceKm += dist;
              }
              drivePositions.push(nextPos);
              idx++;
            } else if (nextIgnition && nextSpeed > 0 && consecutiveSlowPoints < 5) {
              consecutiveSlowPoints++;
              drivePositions.push(nextPos);
              idx++;
            } else if (nextIgnition && nextSpeed === 0 && consecutiveSlowPoints < 3) {
              consecutiveSlowPoints++;
              drivePositions.push(nextPos);
              idx++;
            } else {
              break;
            }
          }

          if (drivePositions.length > 1) {
            const speeds = drivePositions.map((p: any) => p.speedKph || 0).filter((s: number) => s > 0);
            const avgSpeed = speeds.length > 0 ? speeds.reduce((a: number, b: number) => a + b, 0) / speeds.length : 0;

            if (driveDistanceKm >= 0.2 && avgSpeed >= 5) {
              tripNumber++;
              allTrips.push({
                start: drivePositions[0],
                end: drivePositions[drivePositions.length - 1],
                positions: drivePositions,
                distanceKm: driveDistanceKm,
                vehicleName,
                vehicleId,
                tripNumber
              });
            }
          }
        } else {
          idx++;
        }
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
      
      let distanceKm = trip.distanceKm;
      
      // Try odometer first, with sanity check
      if (trip.end.odometerKm && trip.start.odometerKm && trip.end.odometerKm > trip.start.odometerKm) {
        let odometerDist = trip.end.odometerKm - trip.start.odometerKm;
        if (distanceKm > 0 && odometerDist > distanceKm * 500) {
          odometerDist = odometerDist / 1000;
        }
        const maxReasonable = Math.max((durationMin / 60) * 200, 5);
        if (odometerDist <= maxReasonable) {
          distanceKm = odometerDist;
        }
      }
      
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

    // Enrich addresses
    this.enrichAllAddresses();

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
      '⌀ Distance/trajet': `${(totalDistance / (allTrips.length || 1)).toFixed(1)} km`
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
        _timeSort: new Date(p.recordedAt).getTime(),
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

    // Enrich addresses
    this.enrichAllAddresses();

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
      'Vitesse max': `${maxSpeed.toFixed(0)} km/h`,
      '🔴 Dépassements limite': infractions.length.toString(),
      'Véhicules en infraction': Object.keys(infractionsByVehicle).length.toString()
    };
  }

  executeStopsReport(vehicleId: number, startDate?: Date, endDate?: Date) {
    this.apiService.getStopsReport(vehicleId, startDate, endDate).pipe(takeUntil(this.destroy$)).subscribe({
      next: (report: any) => {
        this.ngZone.run(() => {
          this.processStopsFromBackend([report]);
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
    this.apiService.getStopsReportAll(startDate, endDate).pipe(takeUntil(this.destroy$)).subscribe({
      next: (reports: any[]) => {
        this.ngZone.run(() => {
          this.processStopsFromBackend(reports);
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

  executeTripsReport(vehicleId: number, startDate?: Date, endDate?: Date) {
    this.apiService.getTripsReport(vehicleId, startDate, endDate).pipe(takeUntil(this.destroy$)).subscribe({
      next: (report: any) => {
        this.ngZone.run(() => {
          this.processTripsFromBackend([report], startDate, endDate);
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
          console.error('Error loading trips report:', err);
          this.tableData = [];
          this.chartData = [];
          this.statisticsData = { 'Erreur': 'Impossible de charger le rapport des trajets' };
          this.reportGenerated = true;
          this.loading = false;
          this.cdr.detectChanges();
          this.appRef.tick();
        });
      }
    });
  }

  executeTripsReportAllVehicles(startDate?: Date, endDate?: Date) {
    this.apiService.getTripsReportAll(startDate, endDate).pipe(takeUntil(this.destroy$)).subscribe({
      next: (reports: any[]) => {
        this.ngZone.run(() => {
          this.processTripsFromBackend(reports, startDate, endDate);
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
          console.error('Error loading trips report:', err);
          this.tableData = [];
          this.chartData = [];
          this.statisticsData = { 'Erreur': 'Impossible de charger le rapport des trajets' };
          this.reportGenerated = true;
          this.loading = false;
          this.cdr.detectChanges();
          this.appRef.tick();
        });
      }
    });
  }

  executeSpeedInfractionReport(start?: Date, end?: Date) {
    const now = new Date();
    const startDate = start || new Date(now.getFullYear(), now.getMonth(), now.getDate(), 0, 0, 0);
    const endDate = end || now;
    const selectedVehicleId = this.selectedVehicleId ? parseInt(this.selectedVehicleId) : null;
    
    // Fetch vehicles — filter by selected vehicle if one is chosen
    this.getVehiclesObs().pipe(takeUntil(this.destroy$)).subscribe({
      next: (vehicles: any[]) => {
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
          
          this.getVehicleHistoryObs(vehicle.id, startDate, endDate, 10000).pipe(takeUntil(this.destroy$)).subscribe({
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
    
    // Process table data with % based severity
    this.tableData = infractions.map(inf => {
      const excessPct = (inf.excess / inf.limit) * 100;
      let severityLevel: string;
      let severityLabel: string;
      let severityOrder: number;
      if (excessPct > 20) {
        severityLevel = 'grave';
        severityLabel = '🔴 Grave';
        severityOrder = 3;
      } else if (excessPct > 10) {
        severityLevel = 'modere';
        severityLabel = '🟡 Modéré';
        severityOrder = 2;
      } else {
        severityLevel = 'leger';
        severityLabel = '🟢 Léger';
        severityOrder = 1;
      }
      return {
        vehicle: inf.vehicleName || inf.vehiclePlate,
        time: this.formatDateTime(inf.time),
        _timeSort: new Date(inf.time).getTime(),
        address: inf.address || `${inf.latitude.toFixed(5)}, ${inf.longitude.toFixed(5)}`,
        latitude: inf.latitude,
        longitude: inf.longitude,
        speed: `${inf.speed.toFixed(1)} km/h`,
        speedValue: inf.speed,
        limit: `${inf.limit} km/h`,
        limitValue: inf.limit,
        excessValue: inf.excess,
        excessPct,
        severityLevel,
        severityLabel,
        severityOrder
      };
    });
    
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
    
    // Secondary chart data - severity distribution (% based)
    const light = this.tableData.filter((r: any) => r.severityLevel === 'leger').length;
    const medium = this.tableData.filter((r: any) => r.severityLevel === 'modere').length;
    const severe = this.tableData.filter((r: any) => r.severityLevel === 'grave').length;
    this.secondaryChartData = [
      { label: '🟢 Léger (+1-10%)', value: light, color: '#22C55E' },
      { label: '🟡 Modéré (+11-20%)', value: medium, color: '#F59E0B' },
      { label: '🔴 Grave (+21%+)', value: severe, color: '#EF4444' }
    ].filter(d => d.value > 0);
    
    // Statistics
    const maxSpeed = Math.max(...infractions.map(i => i.speed));
    const avgExcess = infractions.reduce((sum, i) => sum + i.excess, 0) / infractions.length;
    const lightPct = ((light / infractions.length) * 100).toFixed(0);
    const mediumPct = ((medium / infractions.length) * 100).toFixed(0);
    const severePct = ((severe / infractions.length) * 100).toFixed(0);
    
    this.statisticsData = {
      '⚠️ Total infractions': infractions.length.toString(),
      '🏎️ Vitesse max': `${maxSpeed.toFixed(1)} km/h`,
      '📊 Excès moyen': `+${avgExcess.toFixed(1)} km/h`,
      '🟢 Léger (+1-10%)': `${light} (${lightPct}%)`,
      '🟡 Modéré (+11-20%)': `${medium} (${mediumPct}%)`,
      '🔴 Grave (+21%+)': `${severe} (${severePct}%)`,
      '🚗 Véhicules concernés': vehicleCount.toString()
    };
  }

  viewInfractionOnMap(row: any) {
    if (row.latitude && row.longitude) {
      this.mapPopupData = {
        latitude: row.latitude,
        longitude: row.longitude,
        vehicle: row.vehicle || '',
        speed: row.speed || '',
        limit: row.limit || '',
        time: row.time || '',
        address: row.address || '',
        severityLabel: row.severityLabel || ''
      };
      this.showMapPopup = true;
      this.cdr.detectChanges();

      // Initialize Leaflet map after DOM renders
      setTimeout(() => this.initPopupMap(), 50);
    }
  }

  closeMapPopup() {
    this.showMapPopup = false;
    this.mapPopupData = null;
    if (this.popupMap) {
      this.popupMap.remove();
      this.popupMap = undefined;
    }
  }

  private initPopupMap() {
    if (!this.mapPopupContainer?.nativeElement || !this.mapPopupData) return;

    // Clean up previous map
    if (this.popupMap) {
      this.popupMap.remove();
      this.popupMap = undefined;
    }

    const { latitude, longitude } = this.mapPopupData;

    this.popupMap = L.map(this.mapPopupContainer.nativeElement, {
      center: [latitude, longitude],
      zoom: 17,
      zoomControl: true,
      attributionControl: false
    });

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19
    }).addTo(this.popupMap);

    // Custom red marker for infraction location
    const icon = L.divIcon({
      className: 'infraction-marker',
      html: `<div style="background:#EF4444;width:32px;height:32px;border-radius:50%;border:3px solid #fff;box-shadow:0 2px 8px rgba(0,0,0,0.3);display:flex;align-items:center;justify-content:center;color:#fff;font-size:16px;font-weight:bold;">⚠️</div>`,
      iconSize: [32, 32],
      iconAnchor: [16, 16]
    });

    const marker = L.marker([latitude, longitude], { icon }).addTo(this.popupMap);
    marker.bindPopup(
      `<div style="font-family:system-ui;min-width:180px;">
        <div style="font-weight:700;font-size:14px;margin-bottom:6px;">${this.mapPopupData.vehicle}</div>
        <div style="color:#EF4444;font-weight:700;font-size:16px;">${this.mapPopupData.speed}</div>
        <div style="color:#64748b;font-size:12px;margin-top:4px;">Limite: ${this.mapPopupData.limit}</div>
        <div style="color:#64748b;font-size:12px;margin-top:2px;">${this.mapPopupData.time}</div>
        <div style="color:#334155;font-size:12px;margin-top:4px;">${this.mapPopupData.address}</div>
      </div>`
    ).openPopup();

    // Force map resize
    setTimeout(() => this.popupMap?.invalidateSize(), 100);
  }

  enrichSpeedInfractionAddresses() {
    this.enrichAllAddresses();
  }

  executeDrivingBehaviorReport(start?: Date, end?: Date) {
    const now = new Date();
    const startDate = start || new Date(now.getFullYear(), now.getMonth(), now.getDate(), 0, 0, 0);
    const endDate = end || now;
    const selectedVehicleId = this.selectedVehicleId ? parseInt(this.selectedVehicleId) : null;
    
    // Fetch vehicles — filter by selected vehicle if one is chosen
    this.getVehiclesObs().pipe(takeUntil(this.destroy$)).subscribe({
      next: (vehicles: any[]) => {
        let targetVehicles = vehicles;
        if (selectedVehicleId) {
          targetVehicles = vehicles.filter(v => v.id === selectedVehicleId);
        }

        const allIncidents: any[] = [];
        let maxSpeedKph = 0;
        let maxRpm = 0;
        const allFuelRates: number[] = [];
        let completedRequests = 0;
        const totalVehicles = targetVehicles.length;
        
        if (totalVehicles === 0) {
          this.ngZone.run(() => {
            this.processDrivingBehaviorReport([], 0, 0);
            this.reportGenerated = true;
            this.loading = false;
            this.activeTab = 'table';
            this.currentPage = 1;
            this.cdr.detectChanges();
          });
          return;
        }
        
        targetVehicles.forEach(vehicle => {
          this.getVehicleHistoryObs(vehicle.id, startDate, endDate, 10000).pipe(takeUntil(this.destroy$)).subscribe({
            next: (positions) => {
              const incidents = this.detectDrivingIncidents(positions, vehicle);
              allIncidents.push(...incidents);
              // Track max speed, RPM, and fuel consumption across all positions
              let prevFuel: number | null = null;
              let prevOdo: number | null = null;
              positions.forEach((p: any) => {
                if ((p.speedKph || 0) > maxSpeedKph) maxSpeedKph = p.speedKph || 0;
                if ((p.rpm || 0) > maxRpm) maxRpm = p.rpm || 0;
                if (p.fuelRateLPer100Km != null && p.fuelRateLPer100Km > 0 && p.fuelRateLPer100Km < 100) {
                  allFuelRates.push(p.fuelRateLPer100Km);
                }
                // Estimate consumption from fuel level delta when direct rate unavailable
                const fuelPct = p.fuelRaw ?? p.fuelPercent;
                const odo = p.odometerKm ?? p.totalOdometerKm;
                if (fuelPct != null && odo != null && prevFuel != null && prevOdo != null) {
                  const dFuel = prevFuel - fuelPct;
                  const dOdo = odo - prevOdo;
                  if (dFuel > 0 && dOdo > 1) {
                    const tankCapacity = 60; // liters estimate
                    const liters = (dFuel / 100) * tankCapacity;
                    const rate = (liters / dOdo) * 100;
                    if (rate > 0 && rate < 50) allFuelRates.push(rate);
                  }
                }
                if (fuelPct != null) prevFuel = fuelPct;
                if (odo != null) prevOdo = odo;
              });
              completedRequests++;
              
              if (completedRequests === totalVehicles) {
                this.ngZone.run(() => {
                  this.processDrivingBehaviorReport(allIncidents, maxSpeedKph, maxRpm, allFuelRates);
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
                  this.processDrivingBehaviorReport(allIncidents, maxSpeedKph, maxRpm, allFuelRates);
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

  getSpeedLimitFromAddress(address: string): number {
    if (!address) return 90;
    const addr = address.toLowerCase();
    // Autoroute → 120 km/h (explicit "autoroute" keyword only, not loose A+digit patterns)
    if (addr.includes('autoroute')) {
      return 120;
    }
    // Route nationale / route principale / GP → 100 km/h
    if (addr.includes('route nationale') || /\brn\s?\d+\b/.test(addr) || /\bgp\s?\d+\b/.test(addr) || addr.includes('route principale')) {
      return 100;
    }
    // Route régionale (RR) → 80 km/h
    if (addr.includes('route régionale') || addr.includes('route regionale') || /\brr\s?\d+\b/.test(addr)) {
      return 80;
    }
    // Route locale (RL) → 60 km/h
    if (/\brl\s?\d+\b/.test(addr) || addr.includes('route locale')) {
      return 60;
    }
    // MC (chemin municipal) → 50 km/h — only match explicit MC codes, not generic "rue"/"avenue"
    if (/\bmc\s?\d+\b/.test(addr) || addr.includes('chemin municipal') || addr.includes('chemin communal')) {
      return 50;
    }
    // Default: hors agglomération → 90 km/h
    return 90;
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
      
      // Detect speed limit violation based on road type from address
      if (this.drivingBehaviorFilters['speedLimitViolation'] && curr.address && (curr.speedKph || 0) > 0) {
        const speedLimit = this.getSpeedLimitFromAddress(curr.address);
        const speed = curr.speedKph || 0;
        const excess = speed - speedLimit;
        if (excess > 0) {
          rawIncidents.push({
            type: 'speedLimitViolation',
            vehicleId: vehicle.id,
            vehicleName: vehicle.name || vehicle.plate,
            time: curr.recordedAt,
            latitude: curr.latitude,
            longitude: curr.longitude,
            address: curr.address,
            value: speed,
            valueFormatted: `${Math.round(speed)} km/h (limite ${speedLimit})`,
            severity: excess > 40 ? 'high' : excess > 20 ? 'medium' : 'low'
          });
        }
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

  processDrivingBehaviorReport(incidents: any[], maxSpeedKph: number = 0, maxRpm: number = 0, fuelRates: number[] = []) {
    // Sort by time descending
    incidents.sort((a, b) => new Date(b.time).getTime() - new Date(a.time).getTime());
    
    // Compute driving behavior metric cards
    // Score: start at 100, deduct per incident severity
    let score = 100;
    incidents.forEach(inc => {
      if (inc.severity === 'high') score -= 5;
      else if (inc.severity === 'medium') score -= 3;
      else score -= 1;
    });
    const avgFuel = fuelRates.length > 0
      ? Math.round((fuelRates.reduce((a, b) => a + b, 0) / fuelRates.length) * 10) / 10
      : null;
    this.drivingBehaviorCards = {
      rpmMax: Math.round(maxRpm),
      vitesseMax: Math.round(maxSpeedKph),
      scoreConduite: Math.max(0, Math.min(100, score)),
      consommation: avgFuel
    };

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
        _timeSort: new Date(inc.time).getTime(),
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
    
    // Build per-type count entries
    const typeCountEntries: { [key: string]: string } = {};
    for (const [label, data] of Object.entries(byType)) {
      typeCountEntries[`${data.icon} ${label}`] = data.count.toString();
    }

    this.statisticsData = {
      '🚨 Total incidents': incidents.length.toString(),
      '🏁 Vitesse max': maxSpeedKph > 0 ? `${Math.round(maxSpeedKph)} km/h` : 'N/A',
      '🔧 RPM max': maxRpm > 0 ? `${Math.round(maxRpm)} tr/min` : 'N/A',
      ...typeCountEntries,
      '🔴 Graves': `${bySeverity.high} (${highPct}%)`,
      '🟡 Modérés': `${bySeverity.medium} (${mediumPct}%)`,
      '🟢 Légers': `${bySeverity.low} (${lowPct}%)`,
      '⚠️ Type dominant': worstType ? `${worstType[0]} (${worstType[1].count})` : '-',
      '🚗 Véhicules': vehicleCount.toString()
    };
  }

  enrichDrivingBehaviorAddresses() {
    this.enrichAllAddresses();
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
        if (gapSeconds > 0) { // Any gap between activities → insert implicit stop
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
      } else if (activity.type === 'pause') {
        events.push({
          eventNumber,
          time: `${this.formatDateTime(activity.startTime)} → ${activity.endTime ? this.formatDateTime(activity.endTime) : '...'}`,
          type: 'pause',
          typeIcon: '⏸️',
          typeLabel: 'Pause',
          description: activity.startLocation.address || 'Manoeuvre / stationnement',
          address: activity.startLocation.address || `${activity.startLocation.latitude.toFixed(4)}°, ${activity.startLocation.longitude.toFixed(4)}°`,
          latitude: activity.startLocation.latitude,
          longitude: activity.startLocation.longitude,
          duration: activity.durationFormatted,
          distance: activity.distanceKm ? `${activity.distanceKm.toFixed(1)} km` : '-',
          speed: '-',
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

    // Merge consecutive stops after sorting (implicit stops may be adjacent to backend stops)
    // Also merge consecutive pauses
    const mergedEvents: any[] = [];
    for (const evt of events) {
      const prevEvt = mergedEvents.length > 0 ? mergedEvents[mergedEvents.length - 1] : null;
      if (prevEvt && (
        (evt.type === 'stop' && prevEvt.type === 'stop') ||
        (evt.type === 'pause' && prevEvt.type === 'pause')
      )) {
        // Extend previous event to cover this one
        const prevEnd = prevEvt._sortTime + (prevEvt.durationSeconds || 0) * 1000;
        const currEnd = evt._sortTime + (evt.durationSeconds || 0) * 1000;
        const mergedEnd = Math.max(prevEnd, currEnd);
        prevEvt.durationSeconds = Math.round((mergedEnd - prevEvt._sortTime) / 1000);
        const totalSecs = prevEvt.durationSeconds;
        prevEvt.duration = totalSecs >= 3600
          ? `${Math.floor(totalSecs / 3600)}h ${Math.floor((totalSecs % 3600) / 60)}m`
          : `${Math.floor(totalSecs / 60)}m`;
        // Update time range display
        const endDate = new Date(prevEvt._sortTime + totalSecs * 1000);
        const startStr = prevEvt.time.split(' → ')[0];
        prevEvt.time = `${startStr} → ${this.formatDateTime(this.toDateTime(endDate))}`;
        continue;
      }
      mergedEvents.push(evt);
    }

    // Re-number after merge
    mergedEvents.forEach((e: any, idx: number) => e.eventNumber = idx + 1);
    // Re-number stop/drive labels
    let dn = 0, sn = 0;
    mergedEvents.forEach((e: any) => {
      if (e.type === 'drive') { dn++; e.typeLabel = `Trajet ${dn}`; }
      if (e.type === 'stop') { sn++; e.typeLabel = `Arrêt ${sn}`; }
    });

    this.tableData = mergedEvents;

    // Enrich addresses
    this.enrichDailyReportAddresses();

    // Chart data - Timeline bar chart showing activity durations
    const driveEvents = mergedEvents.filter(e => e.type === 'drive');
    const stopEvents = mergedEvents.filter(e => e.type === 'stop');

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
      '🏎️ Vitesse max': `${report.summary.maxSpeedKph} km/h`
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
    this.enrichAllAddresses();
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
    // Also filter to only include days within the requested date range
    const fromDateStr = this.fromDate ? this.fromDate.split('T')[0] : '';
    const toDateStr = this.toDate ? this.toDate.split('T')[0] : '';
    const daysWithActivity = report.dailyBreakdown.filter((day: DailyMileage) => {
      if (day.tripCount === 0 && day.distanceKm === 0) return false;
      if (fromDateStr) {
        const dayDate = this.toLocalDate(new Date(day.date));
        if (dayDate < fromDateStr) return false;
        if (toDateStr && dayDate > toDateStr) return false;
      }
      return true;
    });

    // Sort: most recent first
    daysWithActivity.sort((a: DailyMileage, b: DailyMileage) => new Date(b.date).getTime() - new Date(a.date).getTime());

    this.tableData = daysWithActivity.map((day: DailyMileage) => ({
      date: new Date(day.date).toLocaleDateString('fr-FR', { weekday: 'short', day: '2-digit', month: '2-digit', year: '2-digit' }),
      _dateSort: new Date(day.date).getTime(),
      dayOfWeek: day.dayOfWeek,
      distance: `${day.distanceKm.toFixed(1)} km`,
      distanceValue: day.distanceKm,
      tripCount: day.tripCount,
      drivingTime: this.formatMinutes(day.drivingMinutes),
      _drivingTimeSecondsSort: day.drivingMinutes,
      maxSpeed: `${day.maxSpeedKph.toFixed(1)} km/h`,
      _maxSpeedValueSort: day.maxSpeedKph,
      odometer: day.endOdometerKm ? `${day.endOdometerKm.toFixed(0)} km` : '-'
    }));

    // Chart data - daily distances (oldest left → newest right)
    const chartDays = [...daysWithActivity].sort((a: DailyMileage, b: DailyMileage) => new Date(a.date).getTime() - new Date(b.date).getTime());
    this.chartData = chartDays.map((day: DailyMileage) => ({
      label: new Date(day.date).toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit', year: '2-digit' }),
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

  executeMileageReportAllVehicles(startDate?: Date, endDate?: Date) {
    const start = startDate || new Date(Date.now() - 30 * 24 * 60 * 60 * 1000);
    const end = endDate || new Date();

    this.apiService.getMileageReports(start, end).pipe(takeUntil(this.destroy$)).subscribe({
      next: (reports) => {
        this.ngZone.run(() => {
          this.processMileageReportAllVehicles(reports, start, end);
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
          console.error('Error loading mileage reports:', err);
          this.tableData = [];
          this.chartData = [];
          this.statisticsData = { 'Erreur': 'Impossible de charger le rapport kilométrique' };
          this.reportGenerated = true;
          this.loading = false;
          this.cdr.detectChanges();
        });
      }
    });
  }

  processMileageReportAllVehicles(reports: any[], start: Date, end: Date) {
    const validReports = reports.filter((r: any) => r.hasData && r.summary);

    if (validReports.length === 0) {
      this.tableData = [];
      this.chartData = [];
      this.statisticsData = {
        'Période': `${start.toLocaleDateString('fr-FR')} - ${end.toLocaleDateString('fr-FR')}`,
        'Information': 'Aucune donnée kilométrique pour cette période'
      };
      return;
    }

    // Sort by total distance descending
    validReports.sort((a: any, b: any) => b.summary.totalDistanceKm - a.summary.totalDistanceKm);

    this.tableData = validReports.map((r: any) => ({
      vehicleName: r.vehicleName || r.plate,
      plate: r.plate || '-',
      driver: r.driverName || '-',
      distance: `${r.summary.totalDistanceKm.toFixed(1)} km`,
      distanceValue: r.summary.totalDistanceKm,
      tripCount: r.summary.totalTripCount,
      drivingTime: r.summary.totalDrivingFormatted || this.formatMinutes(r.summary.totalDrivingMinutes || 0),
      maxSpeed: `${r.summary.maxSpeedKph.toFixed(1)} km/h`,
      avgDaily: `${r.summary.averageDailyKm.toFixed(1)} km/j`,
      activeDays: `${r.summary.daysWithActivity}/${r.summary.totalDays}`
    }));

    // Chart: top 10 vehicles by distance
    this.chartData = validReports.slice(0, 10).map((r: any) => ({
      label: r.vehicleName || r.plate,
      value: r.summary.totalDistanceKm
    }));

    // Aggregate statistics
    const totalDist = validReports.reduce((sum: number, r: any) => sum + r.summary.totalDistanceKm, 0);
    const totalTrips = validReports.reduce((sum: number, r: any) => sum + r.summary.totalTripCount, 0);
    const maxDist = Math.max(...validReports.map((r: any) => r.summary.totalDistanceKm));
    const maxVehicle = validReports.find((r: any) => r.summary.totalDistanceKm === maxDist);

    this.statisticsData = {
      'Période': `${start.toLocaleDateString('fr-FR')} - ${end.toLocaleDateString('fr-FR')}`,
      'Véhicules': validReports.length.toString(),
      'Distance totale flotte': `${totalDist.toFixed(1)} km`,
      'Nombre total de trajets': totalTrips.toString(),
      'Moyenne par véhicule': `${(totalDist / validReports.length).toFixed(1)} km`,
      'Véhicule le plus actif': maxVehicle ? `${maxVehicle.vehicleName} (${maxDist.toFixed(1)} km)` : '-'
    };
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
          legend: { display: false },
          title: { display: true, text: '📏 Distance journalière', font: { size: 14, weight: 'bold' } },
          // No dataset label needed
          tooltip: {
            callbacks: {
              label: (context) => `${(context.parsed.y ?? 0).toFixed(1)} km`
            }
          }
        },
        scales: {
          y: {
            beginAtZero: true,
            title: { display: true, text: 'Distance (Km)' }
          },
          x: {
            title: { display: true, text: 'Jour' }
          }
        }
      }
    });

    // Create secondary cumulative chart
    this.createSecondaryChart();
  }

  // ==================== MILEAGE PERIOD REPORT (Hour/Day/Month) ====================

  onMileagePeriodTypeChange() {
    // Ensure date fields have sensible defaults when switching period type
    if (this.selectedMileagePeriodType === 'hour' && !this.mileagePeriodDate) {
      const yesterday = new Date();
      yesterday.setDate(yesterday.getDate() - 1);
      this.mileagePeriodDate = this.toLocalDate(yesterday);
    }
    if (this.selectedMileagePeriodType === 'day') {
      if (!this.mileagePeriodStartDate || !this.mileagePeriodEndDate) {
        const today = new Date();
        const weekAgo = new Date(today.getTime() - 7 * 86400000);
        this.mileagePeriodStartDate = this.toLocalDate(weekAgo);
        this.mileagePeriodEndDate = this.toLocalDate(today);
      }
    }
    // Always re-execute when changing period type (even on first run)
    this.executeReport();
  }

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
        // Calypso 8 — pour le rapport "Par mois", on couvre toute l annee
        // selectionnee (1er janv -> 31 dec) pour que le backend retourne
        // 12 barres mensuelles dans le graphe.
        start = new Date(this.mileagePeriodYear, 0, 1);
        end = new Date(this.mileagePeriodYear, 11, 31);
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
        // Calypso 8 — annee complete pour 12 barres mensuelles
        start = new Date(this.mileagePeriodYear, 0, 1);
        end = new Date(this.mileagePeriodYear, 11, 31);
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
            // Compute speed from breakdown data
            const breakdown = report.hourlyBreakdown?.length ? report.hourlyBreakdown
              : report.dailyBreakdown?.length ? report.dailyBreakdown
              : report.monthlyBreakdown || [];
            const maxSpd = breakdown.length > 0 ? Math.max(...breakdown.map((b: any) => b.maxSpeedKph || 0)) : 0;
            const avgSpds = breakdown.filter((b: any) => b.avgSpeedKph > 0).map((b: any) => b.avgSpeedKph);
            const avgSpd = avgSpds.length > 0 ? avgSpds.reduce((a: number, b: number) => a + b, 0) / avgSpds.length : 0;

            allResults.push({
              vehicleName: vehicle.name || vehicle.plate,
              vehiclePlate: vehicle.plate,
              totalDistance: report.totalDistanceKm,
              totalTrips: report.totalTripCount,
              totalDriving: report.totalDrivingMinutes,
              totalDrivingFormatted: report.totalDrivingFormatted,
              avgDistance: report.averageDistanceKm,
              maxDistance: report.maxDistanceKm,
              maxSpeedKph: maxSpd,
              avgSpeedKph: avgSpd
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
      period: r.vehiclePlate || r.vehicleName,
      distance: `${r.totalDistance.toFixed(1)} km`,
      distanceValue: r.totalDistance,
      tripCount: r.totalTrips,
      drivingTime: r.totalDrivingFormatted,
      avgSpeed: `${r.avgSpeedKph.toFixed(1)} km/h`,
      maxSpeed: `${r.maxSpeedKph.toFixed(1)} km/h`
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

    // Process based on period type
    switch (periodType) {
      case 'hour':
        // Filter out hours with 0 km (no activity)
        this.tableData = (report.hourlyBreakdown || [])
          .filter((h: HourlyMileagePeriod) => h.distanceKm > 0)
          .map((h: HourlyMileagePeriod) => ({
            period: h.hourLabel || `${String(h.hour).padStart(2, '0')}:00 - ${String(h.hour + 1).padStart(2, '0')}:00`,
            _periodSort: h.hour,
            distance: `${h.distanceKm.toFixed(1)} km`,
            distanceValue: h.distanceKm,
            tripCount: h.tripCount,
            drivingTime: this.formatMinutes(h.drivingMinutes),
            _drivingTimeSecondsSort: h.drivingMinutes,
            maxSpeed: `${h.maxSpeedKph.toFixed(1)} km/h`,
            _maxSpeedValueSort: h.maxSpeedKph
          }));
        break;
      case 'day':
        // Filter out days with 0 km
        this.tableData = (report.dailyBreakdown || []).reverse()
          .filter((d: DailyMileagePeriod) => d.distanceKm > 0)
          .map((d: DailyMileagePeriod) => ({
            period: d.dateLabel,
            _periodSort: new Date(d.date).getTime(),
            dayOfWeek: d.dayOfWeek,
            distance: `${d.distanceKm.toFixed(1)} km`,
            distanceValue: d.distanceKm,
            tripCount: d.tripCount,
            drivingTime: this.formatMinutes(d.drivingMinutes),
            _drivingTimeSecondsSort: d.drivingMinutes,
            maxSpeed: `${d.maxSpeedKph.toFixed(1)} km/h`,
            _maxSpeedValueSort: d.maxSpeedKph
          }));
        break;
      case 'month':
        // Filter out months with 0 km
        this.tableData = (report.monthlyBreakdown || []).reverse()
          .filter((m: MonthlyMileagePeriod) => m.distanceKm > 0)
          .map((m: MonthlyMileagePeriod) => ({
            period: m.monthLabel,
            _periodSort: m.year * 100 + m.month,
            distance: `${m.distanceKm.toFixed(1)} km`,
            distanceValue: m.distanceKm,
            avgDaily: `${m.averageDailyKm.toFixed(1)} km/jour`,
            activeDays: `${m.daysWithActivity}`,
            tripCount: m.tripCount,
            drivingTime: this.formatMinutes(m.drivingMinutes),
            _drivingTimeSecondsSort: m.drivingMinutes
          }));
        break;
      default:
        this.tableData = [];
    }

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

    const periodType = this.selectedMileagePeriodType || (this.mileagePeriodReport?.periodType || 'day').toString().toLowerCase() as MileagePeriodType;
    const chartConfig = this.getMileagePeriodChartConfig(periodType);

    this.mileagePeriodChart = new Chart(ctx, chartConfig);
    this.chart = this.mileagePeriodChart;
  }

  getMileagePeriodChartConfig(periodType: MileagePeriodType): ChartConfiguration {
    const labels = this.chartData.map(d => d.label);
    const data = this.chartData.map(d => d.value);
    
    const pt = periodType.toString().toLowerCase();
    const xAxisLabel = pt === 'hour' ? 'Heure' : pt === 'day' ? 'Jour' : 'Mois';
    const chartType = pt === 'hour' ? 'line' : 'bar';
    
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
          legend: { display: false },
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
    if (this.selectedTemplate?.type === 'monthly-costs' || this.selectedTemplate?.type === 'monthly-fuel') {
      this.executeMonthlyCostReport();
    }
  }

  // ==================== MONTHLY COST / FUEL REPORT ====================

  executeMonthlyCostReport() {
    const deptId = this.selectedDepartmentId ? parseInt(this.selectedDepartmentId) : undefined;
    this.apiService.getMonthlyCostReport(this.selectedMonthlyYear, this.selectedMonthlyMonth, deptId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (report) => {
          this.ngZone.run(() => {
            this.monthlyCostReport = report;
            this.processMonthlyCostReport(report);
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
            console.error('Error loading monthly cost report:', err);
            this.monthlyCostReport = null;
            this.tableData = [];
            this.chartData = [];
            this.statisticsData = { 'Erreur': 'Impossible de charger le rapport mensuel des coûts' };
            this.reportGenerated = true;
            this.loading = false;
            this.cdr.detectChanges();
            this.appRef.tick();
          });
        }
      });
  }

  processMonthlyCostReport(report: MonthlyCostReport) {
    if (this.monthlyCostReportType === 'costs') {
      this.statisticsData = {
        'Période': report.reportPeriod,
        'KM Total': this.formatNumber(report.totalKm) + ' km',
        'Carburant': this.formatCurrency(report.totalFuelCostDzd),
        'Entretien': this.formatCurrency(report.totalMaintenanceCostDzd),
        'Réparation': this.formatCurrency(report.totalRepairCostDzd),
        'Coût Total': this.formatCurrency(report.totalCostDzd)
      };
    } else {
      this.statisticsData = {
        'Période': report.reportPeriod,
        'KM Total': this.formatNumber(report.totalKm) + ' km',
        'Litres Total': this.formatNumber(report.totalFuelLiters) + ' L',
        'Coût Carburant': this.formatCurrency(report.totalFuelCostDzd),
        'Consommation Moyenne': report.totalKm > 0
          ? ((report.totalFuelLiters / report.totalKm) * 100).toFixed(2) + ' L/100km'
          : 'N/A'
      };
    }

    // Build flat table data for rendering
    this.tableData = report.vehicles.map(v => ({
      ...v,
      type: this.monthlyCostReportType
    }));
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
              label: (ctx) => `${ctx.dataset.label}: ${(ctx.parsed.y ?? 0).toLocaleString('fr-FR', { minimumFractionDigits: 2 })} ${this.getCurrencyCode()}`
            }
          }
        },
        scales: {
          x: { title: { display: true, text: 'Période' } },
          y: { 
            beginAtZero: true, 
            stacked: true,
            title: { display: true, text: `Coût maintenance (${this.getCurrencyCode()})` },
            ticks: { callback: (value) => `${value.toLocaleString('fr-FR')} ${this.getCurrencyCode()}` }
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
          legend: { display: false }
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

  getCurrencyCode(): string {
    // Calypso 9 — single source of truth is UserPreferencesService so the
    // currency here matches the appCurrency pipe used elsewhere in the app.
    return this.userPrefs.current.currency;
  }

  formatCurrency(value: number): string {
    return this.userPrefs.formatCurrency(value);
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

    this.getVehicleHistoryObs(vehicleId, startDate, endDate, 10000).pipe(takeUntil(this.destroy$)).subscribe({
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
        // Stops report is now handled by backend API via executeStopsReport
        break;
      case 'distance':
        this.processDistanceReport(sorted);
        break;
      case 'trips':
        // Trips report is now handled by backend API via executeTripsReport
        break;
      default:
        this.processFuelReport(sorted);
    }
  }

  processFuelReport(positions: any[]) {
    // === PRE-FILTER: Only keep positions with valid fuel data (0-100) ===
    // Positions without fuelRaw break the spike filter's triplet detection
    const fuelPositions = positions.filter((p: any) => p.fuelRaw != null && p.fuelRaw >= 0 && p.fuelRaw <= 100);

    if (fuelPositions.length === 0) {
      this.tableData = [];
      this.chartData = [];
      this.statisticsData = { 'Information': 'Aucune donnée carburant disponible pour ce véhicule' };
      return;
    }

    // === BINARY OSCILLATION DETECTION ===
    // Some vehicles have broken sensors that only report 0 and 100 (or max).
    // Detect: if >60% of readings are at extremes (0-2 or 98-100) AND there are
    // frequent large swings, the sensor is unreliable.
    const extremeCount = fuelPositions.filter((p: any) => p.fuelRaw <= 2 || p.fuelRaw >= 98).length;
    const extremeRatio = extremeCount / fuelPositions.length;
    let largeSwings = 0;
    for (let i = 1; i < fuelPositions.length; i++) {
      if (Math.abs(fuelPositions[i].fuelRaw - fuelPositions[i - 1].fuelRaw) > 50) largeSwings++;
    }
    const swingRatio = fuelPositions.length > 1 ? largeSwings / (fuelPositions.length - 1) : 0;

    if (extremeRatio > 0.6 && swingRatio > 0.1) {
      console.warn(`Fuel report: unreliable sensor detected (${(extremeRatio * 100).toFixed(0)}% extreme, ${largeSwings} large swings)`);
      this.tableData = [];
      this.chartData = [];
      this.statisticsData = {
        '⚠️ Capteur carburant': 'Données non fiables',
        'Diagnostic': `${(extremeRatio * 100).toFixed(0)}% des lectures sont à 0% ou 100% avec ${largeSwings} oscillations`,
        'Recommandation': 'Vérifier le branchement du capteur carburant ou le mode capteur du boîtier GPS'
      };
      return;
    }

    // === SPIKE FILTER: Remove isolated bad fuel readings ===
    // Runs on fuel-only positions (no null gaps to break triplet detection)
    const spikeIndices = new Set<number>();
    
    for (let i = 1; i < fuelPositions.length - 1; i++) {
      const prevFuel = fuelPositions[i - 1].fuelRaw;
      const currFuel = fuelPositions[i].fuelRaw;
      const nextFuel = fuelPositions[i + 1].fuelRaw;
      
      const dropFromPrev = Math.abs(currFuel - prevFuel);
      const recoveryToNext = Math.abs(nextFuel - prevFuel);
      
      // Spike: big change (>10%) from previous, and next reading returns close to previous (<5% diff)
      if (dropFromPrev > 10 && recoveryToNext <= 5) {
        spikeIndices.add(i);
      }
    }
    
    // Also handle consecutive spikes (e.g., 40 → 20 → 15 → 40)
    for (let i = 1; i < fuelPositions.length - 2; i++) {
      if (spikeIndices.has(i)) continue;
      const prevFuel = fuelPositions[i - 1].fuelRaw;
      const currFuel = fuelPositions[i].fuelRaw;
      const afterNextFuel = fuelPositions[i + 2].fuelRaw;
      
      const dropFromPrev = Math.abs(currFuel - prevFuel);
      const recoveryToAfterNext = Math.abs(afterNextFuel - prevFuel);
      
      if (dropFromPrev > 10 && recoveryToAfterNext <= 5) {
        spikeIndices.add(i);
        spikeIndices.add(i + 1);
      }
    }
    
    const cleanPositions = fuelPositions.filter((_: any, idx: number) => !spikeIndices.has(idx));
    
    if (spikeIndices.size > 0) {
      console.log(`Fuel report: filtered ${spikeIndices.size} spike reading(s) from ${fuelPositions.length} fuel positions`);
    }

    // Only show rows where fuel level CHANGES (no duplicate consecutive readings)
    // Track mileage between fuel level changes (not between individual readings)
    const fuelChanges: any[] = [];
    let lastFuelLevel = -1;
    let lastChangeOdometer = 0; // Odometer at last FUEL CHANGE (not intermediate readings)

    // First pass: identify positions where fuel level changes
    cleanPositions.forEach((pos: any, index: number) => {
      const fuel = pos.fuelRaw ?? 0;
      const odometer = pos.odometerKm || 0;
      const isFirst = index === 0;
      const isLast = index === cleanPositions.length - 1;

      // Only process if fuel level changed, or first/last entry
      if (isFirst || fuel !== lastFuelLevel || isLast) {
        // Skip duplicate: last entry has same fuel as previous change
        if (!isFirst && fuel === lastFuelLevel && isLast) {
          return;
        }

        const fuelDelta = lastFuelLevel >= 0 ? fuel - lastFuelLevel : 0;

        // Skip sensor noise: small positive changes (+1 to +4%) are not real
        if (fuelDelta > 0 && fuelDelta < 5 && !isFirst) {
          return; // Don't display, don't update lastFuelLevel
        }

        // km between this fuel change and the previous fuel change
        const kmDelta = (lastChangeOdometer > 0 && odometer > 0) ? odometer - lastChangeOdometer : 0;

        let eventType = 'Lecture';
        let isAnomaly = false;
        if (fuelDelta >= 10) {
          eventType = '⛽ Remplissage';
        } else if (fuelDelta < -15) {
          eventType = '⚠️ Chute importante';
          isAnomaly = true;
        } else if (fuelDelta < -5) {
          eventType = '📉 Consommation';
        } else if (fuelDelta >= 5 && !isFirst) {
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
          odometer: odometer > 0 ? `${Math.round(odometer)} km` : '-',
          isAnomaly
        });

        lastFuelLevel = fuel;
        if (odometer > 0) lastChangeOdometer = odometer;
      }
      // Positions with same fuel level are simply skipped (no odometer update)
    });

    // Calypso 8 — bug rapporte (page 1 PDF) : "Dates en double" sur le rapport
    // carburant. Cause : deux lectures dans la meme minute mais a des secondes
    // differentes (ex: 14:14:30 puis 14:14:45) avec un fuel% legerement
    // different (bruit capteur, jitter du raw 0-255) passaient toutes les deux
    // le filtre fuel !== lastFuelLevel et generaient deux lignes affichees au
    // meme timestamp "DD/MM/YYYY HH:MM". L utilisateur voyait des "doublons".
    //
    // Fix : dedupe par minute, on garde la derniere entree (chronologiquement
    // la plus recente) de chaque minute. Bonus : nettoie aussi le bruit
    // intra-minute en consolidant la valeur finale.
    const dedupedByMinute = new Map<string, any>();
    for (const fc of fuelChanges) {
      // fc.time est deja au format "DD/MM/YYYY HH:MM" — c est notre cle de minute
      // L iteration prend la derniere valeur car cleanPositions est trie par recordedAt asc
      dedupedByMinute.set(fc.time, fc);
    }
    const dedupedFuelChanges = Array.from(dedupedByMinute.values());

    // Reverse to show most recent first
    dedupedFuelChanges.reverse();
    this.tableData = dedupedFuelChanges;

    // Fetch addresses asynchronously for positions without address
    this.enrichAllAddresses();

    // Calypso 7 — Chart data garde TOUS les points bruts pour conserver
    // les sauts (remplissages) et les pentes (consommation reelle). Le
    // probleme historique (labels axe X clusterises sur les jours actifs)
    // est resolu cote rendu dans createChart : on passe l axe X en
    // type='linear' avec timestamps, donc Chart.js distribue les labels
    // uniformement par TEMPS au lieu de par INDEX. La courbe elle-meme
    // garde tous les details d origine.
    //
    // Downsample defensif si > 300 points (canvas peut ramer au dela).
    const maxChartPoints = 300;
    let chartPositions = cleanPositions;
    if (cleanPositions.length > maxChartPoints) {
      const step = Math.ceil(cleanPositions.length / maxChartPoints);
      chartPositions = cleanPositions.filter((_: any, i: number) => i % step === 0 || i === cleanPositions.length - 1);
    }
    this.chartData = chartPositions.map((pos: any) => ({
      label: new Date(pos.recordedAt).toLocaleString('fr-FR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }),
      value: pos.fuelRaw || 0,
      ts: new Date(pos.recordedAt).getTime()
    }));

    // Statistics - use cleaned positions to avoid spike pollution
    const fuelValues = cleanPositions.map((p: any) => p.fuelRaw ?? 0).filter((f: number) => f > 0);
    const avgFuel = fuelValues.length > 0 ? fuelValues.reduce((a: number, b: number) => a + b, 0) / fuelValues.length : 0;
    const firstOdo = cleanPositions.find((p: any) => p.odometerKm > 0)?.odometerKm || 0;
    const lastOdo = [...cleanPositions].reverse().find((p: any) => p.odometerKm > 0)?.odometerKm || 0;
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
        _timeSort: new Date(pos.recordedAt).getTime(),
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
    this.enrichAllAddresses();

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

  /**
   * Process stops report from backend API response.
   * Backend returns data grouped by day with ignition_on == false periods.
   */
  processStopsFromBackend(reports: any[]) {
    if (!reports || reports.length === 0) {
      this.tableData = [];
      this.chartData = [];
      this.statisticsData = { 'Information': 'Aucun arrêt trouvé pour cette période' };
      return;
    }

    // Format duration helper
    const formatDuration = (seconds: number): string => {
      if (seconds < 60) return `${seconds}s`;
      const minutes = seconds / 60;
      if (minutes >= 60) {
        const hours = Math.floor(minutes / 60);
        const mins = Math.round(minutes % 60);
        return mins > 0 ? `${hours}h ${mins}min` : `${hours}h`;
      }
      return `${Math.round(minutes)}min`;
    };

    // Flatten all stops from all reports (multi-vehicle support)
    // Cap stop endTime at 23:59:59 of the start day if it crosses midnight
    const allStops: any[] = [];
    for (const report of reports) {
      if (!report.days) continue;
      for (const day of report.days) {
        for (const stop of day.stops) {
          let endTime = stop.endTime;
          let durationSeconds = stop.durationSeconds;
          let durationFormatted = stop.durationFormatted;

          const startDate = new Date(stop.startTime);
          const endDate = new Date(stop.endTime);

          // If stop crosses midnight, cap at 23:59:59 of the start day
          if (startDate.toDateString() !== endDate.toDateString()) {
            const endOfDay = new Date(startDate);
            endOfDay.setHours(23, 59, 59, 0);
            endTime = this.toDateTime(endOfDay);
            durationSeconds = Math.round((endOfDay.getTime() - startDate.getTime()) / 1000);
            durationFormatted = null; // Force recalculation via formatDuration
          }

          allStops.push({
            ...stop,
            endTime,
            durationSeconds,
            durationFormatted,
            dayDate: day.date,
            vehicleName: report.vehicleName,
            vehiclePlate: report.plate
          });
        }
      }
    }

    // Build table data — most recent first
    allStops.sort((a, b) => new Date(b.startTime).getTime() - new Date(a.startTime).getTime());

    this.tableData = allStops.map((stop: any) => {
      const durationMinutes = stop.durationSeconds / 60;
      return {
        time: this.formatDateTime(stop.startTime),
        _timeSort: new Date(stop.startTime).getTime(),
        endTime: this.formatDateTime(stop.endTime),
        _endTimeSort: new Date(stop.endTime).getTime(),
        duration: stop.durationFormatted || formatDuration(stop.durationSeconds),
        durationSeconds: stop.durationSeconds,
        address: stop.address || `${stop.latitude?.toFixed(5)}, ${stop.longitude?.toFixed(5)}`,
        latitude: stop.latitude,
        longitude: stop.longitude,
        typeCode: 'A',
        typeLabel: '🅿️ Arrêt',
        ignitionOff: true,
        isLongStop: durationMinutes > 30,
        vehicleName: stop.vehicleName,
        vehiclePlate: stop.vehiclePlate,
        dayDate: stop.dayDate
      };
    });

    // Enrich addresses that are still coordinates
    this.enrichAllAddresses();

    // Chart: duration range breakdown — include count for tooltip
    const durationRanges = [
      { label: '0-5 min', min: 0, max: 300, color: '#3B82F6' },
      { label: '5-15 min', min: 300, max: 900, color: '#6366F1' },
      { label: '15-30 min', min: 900, max: 1800, color: '#8B5CF6' },
      { label: '30-60 min', min: 1800, max: 3600, color: '#F59E0B' },
      { label: '>60 min', min: 3600, max: Infinity, color: '#EF4444' }
    ];
    this.chartData = durationRanges.map(r => {
      const stops = allStops.filter(s => s.durationSeconds >= r.min && s.durationSeconds < r.max);
      const totalMin = Math.round(stops.reduce((sum: number, s: any) => sum + s.durationSeconds, 0) / 60);
      return {
        label: r.label,
        value: totalMin,
        count: stops.length,
        color: r.color
      };
    });

    // Secondary chart: stops per day
    const stopsByDay = new Map<string, number>();
    for (const stop of allStops) {
      const d = new Date(stop.startTime);
      const dayKey = d.toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit', year: '2-digit' });
      stopsByDay.set(dayKey, (stopsByDay.get(dayKey) || 0) + 1);
    }
    this.secondaryChartData = Array.from(stopsByDay.entries())
      .sort((a, b) => a[0].localeCompare(b[0]))
      .map(([day, count]) => ({ label: day, value: count, color: '#3B82F6' }));

    // Group stops by day — insert day header rows
    const grouped: any[] = [];
    let currentDay = '';
    for (const row of this.tableData) {
      const dayKey = row.time ? row.time.split(' ')[0] : '';
      if (dayKey && dayKey !== currentDay) {
        currentDay = dayKey;
        const dayStops = this.tableData.filter((r: any) => r.time?.startsWith(dayKey));
        grouped.push({ isDayHeader: true, dayLabel: `📅 ${dayKey} — ${dayStops.length} arrêt(s)`, _sortKey: dayKey });
      }
      grouped.push(row);
    }
    this.tableData = grouped;

    // Statistics from backend summary (single vehicle) or computed (multi)
    if (reports.length === 1 && reports[0].summary) {
      const s = reports[0].summary;
      this.statisticsData = {
        'Total arrêts': s.totalStops.toString(),
        'Temps total arrêt': s.totalStopFormatted,
        'Durée moy.': s.avgStopFormatted,
        'Max. arrêt': s.maxStopFormatted,
        'Min. arrêt': s.minStopFormatted
      };
    } else {
      const durations = allStops.map(s => s.durationSeconds);
      const totalSec = durations.reduce((sum: number, d: number) => sum + d, 0);
      const avgSec = durations.length > 0 ? Math.round(totalSec / durations.length) : 0;
      const maxSec = durations.length > 0 ? Math.max(...durations) : 0;
      const minSec = durations.length > 0 ? Math.min(...durations) : 0;
      this.statisticsData = {
        'Total arrêts': allStops.length.toString(),
        'Temps total arrêt': formatDuration(totalSec),
        'Durée moy.': formatDuration(avgSec),
        'Max. arrêt': formatDuration(maxSec),
        'Min. arrêt': formatDuration(minSec),
        'Véhicules': new Set(allStops.map(s => s.vehicleName)).size.toString()
      };
    }
  }

  enrichStopsWithAddresses() {
    this.enrichAllAddresses();
  }

  processDistanceReport(positions: any[]) {
    // Calculate distance between points using Haversine with jump filter
    let totalDistance = 0;
    let filteredPoints = 0;
    const segments: any[] = [];

    for (let i = 1; i < positions.length; i++) {
      const prev = positions[i - 1];
      const curr = positions[i];
      const dist = this.haversineDistance(prev.latitude, prev.longitude, curr.latitude, curr.longitude);
      
      // Filter out unrealistic GPS jumps (> 5 km between consecutive points)
      // Also check speed-based max: if time gap known, cap at 200 km/h
      const timeDiffMs = new Date(curr.recordedAt).getTime() - new Date(prev.recordedAt).getTime();
      const timeDiffH = timeDiffMs / 3600000;
      const maxReasonableDist = timeDiffH > 0 ? Math.max(5, timeDiffH * 200) : 5;
      
      if (dist < maxReasonableDist) {
        totalDistance += dist;
      } else {
        filteredPoints++;
      }
      
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
    if (filteredPoints > 0) {
      this.statisticsData['Points filtrés (sauts GPS)'] = filteredPoints.toString();
    }
  }

  /**
   * Process trips report from backend API response.
   * Backend returns data grouped by day with ignition_on == true periods.
   *
   * Bloc B (correction Calypso 7): pour fixer les bugs B1/B2/B4, le graphique
   * couvre maintenant toujours la plage demandée par l'utilisateur. Les jours
   * sans trajet sont représentés par une barre à 0 km au lieu d'être omis,
   * ce qui évite les sauts visuels (« 26/01 → 1/02 directement ») et la
   * confusion entre l'intervalle filtré et l'intervalle réellement affiché.
   */
  processTripsFromBackend(reports: any[], rangeStart?: Date, rangeEnd?: Date) {
    if (!reports || reports.length === 0) {
      this.tableData = [];
      this.chartData = [];
      this.statisticsData = { 'Information': 'Aucun trajet trouvé pour cette période' };
      return;
    }

    // Format duration helper (seconds → "Xh Ymin" / "Xmin" / "Xs")
    const formatDuration = (seconds: number): string => {
      if (seconds < 60) return `${seconds}s`;
      const minutes = seconds / 60;
      if (minutes >= 60) {
        const hours = Math.floor(minutes / 60);
        const mins = Math.round(minutes % 60);
        return mins > 0 ? `${hours}h ${mins}min` : `${hours}h`;
      }
      return `${Math.round(minutes)}min`;
    };

    // Format minutes → "Xh Ymin" (used by chart tooltip)
    const formatMinutesPretty = (minutes: number): string => {
      if (!minutes || minutes < 1) return '0min';
      const total = Math.round(minutes);
      if (total < 60) return `${total}min`;
      const hours = Math.floor(total / 60);
      const mins = total % 60;
      return mins > 0 ? `${hours}h ${mins}min` : `${hours}h`;
    };

    // Flatten all trips from all reports (multi-vehicle support)
    const allTrips: any[] = [];
    for (const report of reports) {
      if (!report.days) continue;
      for (const day of report.days) {
        for (const trip of day.trips) {
          allTrips.push({
            ...trip,
            dayDate: day.date,
            vehicleName: report.vehicleName,
            vehiclePlate: report.plate
          });
        }
      }
    }

    // Build table data — most recent first
    allTrips.sort((a, b) => new Date(b.startTime).getTime() - new Date(a.startTime).getTime());

    let tripNumber = allTrips.length + 1;
    this.tableData = allTrips.map((trip: any) => {
      tripNumber--;
      const durationMin = trip.durationSeconds / 60;
      return {
        isTrip: true,
        tripNumber,
        startTime: this.formatDateTime(trip.startTime),
        _startTimeSort: new Date(trip.startTime).getTime(),
        endTime: this.formatDateTime(trip.endTime),
        _endTimeSort: new Date(trip.endTime).getTime(),
        duration: trip.durationFormatted || formatDuration(trip.durationSeconds),
        durationSeconds: trip.durationSeconds,
        durationMin,
        distance: `${this.formatNumber(trip.distanceKm, 1)} km`,
        distanceValue: trip.distanceKm,
        distanceKm: trip.distanceKm,
        maxSpeed: `${this.formatNumber(trip.maxSpeedKph, 0)} km/h`,
        maxSpeedValue: trip.maxSpeedKph,
        startAddress: trip.startAddress || `${trip.startLatitude?.toFixed(5)}, ${trip.startLongitude?.toFixed(5)}`,
        endAddress: trip.endAddress || `${trip.endLatitude?.toFixed(5)}, ${trip.endLongitude?.toFixed(5)}`,
        startLat: trip.startLatitude,
        startLng: trip.startLongitude,
        endLat: trip.endLatitude,
        endLng: trip.endLongitude,
        vehicleName: trip.vehicleName,
        vehiclePlate: trip.vehiclePlate
      };
    });

    // Enrich addresses
    this.enrichAllAddresses();

    // Chart data - distance per trip (oldest left → newest right)
    const tripRows = [...this.tableData].filter((t: any) => t.isTrip).reverse();

    // Compute the range covered by data alone (fallback when no filter is given)
    const tripDays = tripRows
      .map((t: any) => t._startTimeSort ? new Date(t._startTimeSort) : null)
      .filter((d): d is Date => d !== null);
    const dataMinDay = tripDays.length > 0
      ? new Date(Math.min(...tripDays.map(d => d.getTime())))
      : null;
    const dataMaxDay = tripDays.length > 0
      ? new Date(Math.max(...tripDays.map(d => d.getTime())))
      : null;

    // Pick the chart's effective range: prefer the user filter, fallback to data range
    const effectiveStart = rangeStart || dataMinDay;
    const effectiveEnd = rangeEnd || dataMaxDay;
    const rangeSpansMultipleDays = effectiveStart && effectiveEnd
      && this.toLocalDate(effectiveStart) !== this.toLocalDate(effectiveEnd);

    // Use daily aggregation if the filter spans more than a single day, OR if the
    // dataset is large. Below, daily aggregation always emits one bar per day in
    // the requested range — including 0-km days — so the X axis matches the
    // user's date filter (B1 + B4).
    const useDailyAggregation = rangeSpansMultipleDays || tripRows.length > 30;

    if (useDailyAggregation) {
      const dailyMap = new Map<string, { distance: number; trips: number; duration: number }>();
      for (const t of tripRows) {
        const rawTs = t._startTimeSort;
        const dayKey = rawTs ? this.toLocalDate(new Date(rawTs)) : null;
        if (!dayKey) continue;
        const entry = dailyMap.get(dayKey) || { distance: 0, trips: 0, duration: 0 };
        entry.distance += t.distanceKm || 0;
        entry.trips += 1;
        entry.duration += t.durationMin || 0;
        dailyMap.set(dayKey, entry);
      }

      // Build the day axis from the effective range to ensure we always span the
      // user's filter (no gaps, no truncation).
      const axisDays: string[] = [];
      if (effectiveStart && effectiveEnd) {
        const cursor = new Date(effectiveStart.getFullYear(), effectiveStart.getMonth(), effectiveStart.getDate());
        const stop = new Date(effectiveEnd.getFullYear(), effectiveEnd.getMonth(), effectiveEnd.getDate());
        while (cursor.getTime() <= stop.getTime()) {
          axisDays.push(this.toLocalDate(cursor));
          cursor.setDate(cursor.getDate() + 1);
        }
      } else {
        // No range info — fall back to populated days only
        axisDays.push(...[...dailyMap.keys()].sort());
      }

      // Hard cap to keep the chart usable (~120 bars max).
      // If the range is huge, fall back to populated days only.
      const cappedDays = axisDays.length > 120
        ? [...dailyMap.keys()].sort()
        : axisDays;

      this.chartData = cappedDays.map(day => {
        const data = dailyMap.get(day) || { distance: 0, trips: 0, duration: 0 };
        return {
          label: new Date(day).toLocaleDateString('fr-FR', { day: '2-digit', month: 'short' }),
          value: Math.round(data.distance * 10) / 10,
          trips: data.trips,
          duration: data.duration,
          durationFormatted: formatMinutesPretty(data.duration),
          _isDailyAggregation: true,
          _dayKey: day
        };
      });
    } else {
      this.chartData = tripRows.map((t: any) => ({
        label: `T${t.tripNumber}`,
        value: t.distanceKm,
        duration: t.durationMin,
        durationFormatted: formatMinutesPretty(t.durationMin || 0),
        _isDailyAggregation: false
      }));
    }

    // Statistics from backend summary (single vehicle) or computed (multi).
    // Calypso 7 (correction client) : format FR « xxx xxx,yy » avec espace
    // toutes les 3 chiffres et virgule decimale (Intl 'fr-FR').
    if (reports.length === 1 && reports[0].summary) {
      const s = reports[0].summary;
      this.statisticsData = {
        'Nombre de trajets': this.formatNumber(s.totalTrips, 0),
        'Distance totale': `${this.formatNumber(s.totalDistanceKm, 2)} km`,
        'Temps de conduite': s.totalTripFormatted,
        'Vitesse max': `${this.formatNumber(s.maxSpeedKph, 1)} km/h`
      };
    } else {
      const totalDistance = allTrips.reduce((sum: number, t: any) => sum + t.distanceKm, 0);
      const totalDrivingMin = allTrips.reduce((sum: number, t: any) => sum + t.durationSeconds / 60, 0);
      const maxSpeedAll = allTrips.length > 0 ? Math.max(...allTrips.map((t: any) => t.maxSpeedKph)) : 0;
      this.statisticsData = {
        'Nombre de trajets': this.formatNumber(allTrips.length, 0),
        'Distance totale': `${this.formatNumber(totalDistance, 2)} km`,
        'Temps de conduite': formatDuration(Math.round(totalDrivingMin * 60)),
        'Vitesse max': `${this.formatNumber(maxSpeedAll, 0)} km/h`,
        'Véhicules': new Set(allTrips.map((t: any) => t.vehicleName)).size.toString()
      };
    }

    // Group trips by day — insert day header rows
    const grouped: any[] = [];
    let currentDay = '';
    for (const row of this.tableData) {
      const dayKey = row.startTime ? row.startTime.split(' ')[0] : '';
      if (dayKey && dayKey !== currentDay) {
        currentDay = dayKey;
        const dayTrips = this.tableData.filter((r: any) => r.startTime?.startsWith(dayKey));
        const tripNums = dayTrips.filter((r: any) => r.isTrip).map((r: any) => r.tripNumber);
        const dayLabel = tripNums.length > 0 ? `📅 ${dayKey} — Trajets ${Math.min(...tripNums)} à ${Math.max(...tripNums)}` : `📅 ${dayKey}`;
        grouped.push({ isDayHeader: true, dayLabel, _sortKey: dayKey });
      }
      grouped.push(row);
    }
    this.tableData = grouped;
  }

  enrichTripAddresses() {
    this.enrichAllAddresses();
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
      const isDailyAgg = this.chartData.length > 0 && this.chartData[0]._isDailyAggregation;
      const distances = this.chartData.map(d => d.value);
      const labels = this.chartData.map(d => d.label);

      // Local helper: minutes → "Xh Ymin" (matches processTripsFromBackend)
      const fmtMin = (minutes: number): string => {
        if (!minutes || minutes < 1) return '0min';
        const total = Math.round(minutes);
        if (total < 60) return `${total}min`;
        const hours = Math.floor(total / 60);
        const mins = total % 60;
        return mins > 0 ? `${hours}h ${mins}min` : `${hours}h`;
      };

      if (isDailyAgg) {
        // Daily aggregation: clean area chart with trip count on secondary axis
        const tripCounts = this.chartData.map(d => d.trips || 0);
        config = {
          type: 'bar',
          data: {
            labels,
            datasets: [
              {
                type: 'bar',
                label: 'Distance (km)',
                data: distances,
                backgroundColor: 'rgba(59, 130, 246, 0.7)',
                borderColor: 'rgba(59, 130, 246, 1)',
                borderWidth: 1,
                borderRadius: 4,
                yAxisID: 'y'
              },
              {
                type: 'line',
                label: 'Trajets',
                data: tripCounts,
                borderColor: '#F59E0B',
                backgroundColor: 'rgba(245, 158, 11, 0.1)',
                borderWidth: 2,
                pointRadius: 3,
                pointBackgroundColor: '#F59E0B',
                tension: 0.3,
                fill: false,
                yAxisID: 'y1'
              }
            ]
          },
          options: {
            responsive: true,
            maintainAspectRatio: false,
            interaction: { mode: 'index', intersect: false },
            plugins: {
              legend: { display: true, position: 'top', labels: { usePointStyle: true, padding: 16, font: { size: 12 } } },
              title: { display: true, text: 'Distance et trajets par jour', font: { size: 14, weight: 'bold' }, padding: { bottom: 16 } },
              tooltip: {
                callbacks: {
                  afterBody: (context: any) => {
                    const idx = context[0]?.dataIndex;
                    if (idx !== undefined) {
                      const d = this.chartData[idx];
                      const dur = d?.duration || 0;
                      return dur > 0 ? `⏱️ Conduite: ${fmtMin(dur)}` : '';
                    }
                    return '';
                  }
                }
              }
            },
            scales: {
              y: { type: 'linear', position: 'left', title: { display: true, text: 'Distance (km)' }, beginAtZero: true, grid: { color: 'rgba(0,0,0,0.06)' } },
              y1: { type: 'linear', position: 'right', title: { display: true, text: 'Nb trajets' }, beginAtZero: true, grid: { drawOnChartArea: false } },
              x: { grid: { display: false } }
            }
          }
        };
      } else {
        // Few trips: individual bar chart with consistent color
        const durations = this.chartData.map(d => d.duration || 0);
        config = {
          type: 'bar',
          data: {
            labels,
            datasets: [
              {
                type: 'bar',
                label: 'Distance (km)',
                data: distances,
                backgroundColor: 'rgba(59, 130, 246, 0.7)',
                borderColor: 'rgba(59, 130, 246, 1)',
                borderWidth: 1,
                borderRadius: 6
              }
            ]
          },
          options: {
            responsive: true,
            maintainAspectRatio: false,
            interaction: { mode: 'index', intersect: false },
            plugins: {
              legend: { display: false },
              title: { display: true, text: 'Distance par trajet', font: { size: 14, weight: 'bold' } },
              tooltip: {
                callbacks: {
                  afterBody: (context: any) => {
                    const idx = context[0]?.dataIndex;
                    if (idx !== undefined) {
                      const dur = durations[idx];
                      return dur > 0 ? `⏱️ Durée: ${fmtMin(dur)}` : '';
                    }
                    return '';
                  }
                }
              }
            },
            scales: {
              y: { type: 'linear', position: 'left', title: { display: true, text: 'Distance (km)' }, beginAtZero: true },
              x: { grid: { display: false } }
            }
          }
        };
      }
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
            legend: { display: false },
            title: { display: true, text: '🅿️ Répartition du temps d\'arrêt', font: { size: 14, weight: 'bold' } },
            tooltip: {
              callbacks: {
                label: (context: any) => {
                  const item = this.chartData[context.dataIndex];
                  const totalVal = this.chartData.reduce((s: number, d: any) => s + d.value, 0);
                  const pct = totalVal > 0 ? ((item.value / totalVal) * 100).toFixed(1) : 0;
                  return [`${item.value} min (${pct}%)`, `${item.count} arrêt(s)`];
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
      // Calypso 7 — Echelle X lineaire en timestamp pour distribuer les
      // labels uniformement par TEMPS (pas par index data). On garde donc
      // tous les points bruts (la courbe reste realiste : sauts de
      // remplissage + pentes de consommation). Les ticks sont formates
      // en date a la volee. tension=0 pour ne pas lisser les remplissages
      // en courbe douce — un fill-up est instantane, doit apparaitre
      // comme un saut.
      const fuelSpanMs = this.chartData.length > 1
        ? (this.chartData[this.chartData.length - 1].ts - this.chartData[0].ts)
        : 0;
      const HOUR_MS = 3_600_000;
      const DAY_MS = 24 * HOUR_MS;
      const tickFmt: Intl.DateTimeFormatOptions = fuelSpanMs <= 36 * HOUR_MS
        ? { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }
        : fuelSpanMs <= 90 * DAY_MS
          ? { day: '2-digit', month: '2-digit' }
          : { day: '2-digit', month: '2-digit', year: '2-digit' };

      // Un remplissage est INSTANTANÉ : quand le niveau bondit entre deux
      // points consécutifs (≥ 10 points de %), on insère un point synthétique
      // (même instant, ancien niveau) pour dessiner un palier puis une montée
      // VERTICALE au moment observé. Sans ça, si le boîtier était muet pendant
      // le plein (contact coupé), la courbe reliait l'avant (ex: 28 %) à
      // l'après (95 %) par une pente trompeuse étalée sur tout le trou de
      // données — parfois plusieurs jours.
      const fuelPts: { x: number; y: number }[] = [];
      for (let k = 0; k < this.chartData.length; k++) {
        const d = this.chartData[k];
        const prev = k > 0 ? this.chartData[k - 1] : null;
        if (prev && (d.value - prev.value) >= 10) fuelPts.push({ x: d.ts, y: prev.value });
        fuelPts.push({ x: d.ts, y: d.value });
      }

      config = {
        type: 'line',
        data: {
          datasets: [{
            label: 'Niveau carburant (%)',
            data: fuelPts,
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
            tension: 0,
            fill: true,
            pointRadius: 0,
            pointHoverRadius: 6
          }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          interaction: { mode: 'nearest', axis: 'x', intersect: false },
          plugins: {
            legend: { display: false },
            title: { display: true, text: '⛽ Évolution du niveau de carburant', font: { size: 14, weight: 'bold' } },
            tooltip: {
              callbacks: {
                title: (items: any[]) => {
                  if (!items?.length) return '';
                  const ts = items[0].parsed?.x;
                  return ts ? new Date(ts).toLocaleString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '';
                }
              }
            }
          },
          scales: {
            y: { beginAtZero: true, max: 100, title: { display: true, text: 'Niveau (%)' }, grid: { color: 'rgba(255,255,255,0.1)' } },
            x: {
              type: 'linear',
              // Épingle l'axe sur la plage réelle des données : sans ça,
              // l'axe linéaire arrondit son max bien au-delà du dernier point
              // (gros timestamps), laissant un vide à droite — la courbe
              // paraissait alors décalée par rapport à l'axe X.
              bounds: 'data',
              // Accès optionnel : si chartData est vide (rapport ouvert avant
              // chargement, ou véhicule sans données), undefined => Chart.js
              // auto-scale, pas de crash.
              min: this.chartData[0]?.ts,
              max: this.chartData[this.chartData.length - 1]?.ts,
              grid: { display: false },
              ticks: {
                maxTicksLimit: 12,
                callback: (val: any) => new Date(val).toLocaleString('fr-FR', tickFmt)
              }
            }
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
            legend: { display: false },
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
            legend: { display: false },
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
            legend: { display: false },
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
            legend: { display: false },
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
            legend: { display: false },
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
            y: { beginAtZero: true, title: { display: true, text: 'Nombre d\'arrêts' } },
            x: { grid: { display: false } }
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
              legend: { display: false },
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
              y: { beginAtZero: true, title: { display: true, text: 'Nombre d\'incidents' }, ticks: { stepSize: 1 } },
              x: { grid: { display: false } }
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
            label: `Coût (${this.getCurrencyCode()})`,
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
            x: { beginAtZero: true, title: { display: true, text: `Coût (${this.getCurrencyCode()})` } }
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
            label: `Coût (${this.getCurrencyCode()})`,
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
            x: { beginAtZero: true, title: { display: true, text: `Coût (${this.getCurrencyCode()})` } }
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
            legend: { display: false },
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
  // Bloc B5 (correction Calypso 7) : on filtre directement sur le champ
  // `severityLevel` que processSpeedInfractionReport calcule à partir du
  // pourcentage d'excès (≤10 % léger, 10-20 % modéré, >20 % grave). Avant ce
  // patch, le donut utilisait des seuils km/h (≤20, 20-40, >40) totalement
  // décorrélés de ceux du tableau, d'où l'incohérence rapportée par le client
  // (« le tableau dit Grave mais le donut est tout vert »).
  getSeverityDistribution(): { label: string; value: number }[] {
    const light = this.tableData.filter((r: any) => r.severityLevel === 'leger').length;
    const medium = this.tableData.filter((r: any) => r.severityLevel === 'modere').length;
    const severe = this.tableData.filter((r: any) => r.severityLevel === 'grave').length;
    if (light + medium + severe === 0) return [];
    return [
      { label: '🟢 Léger (+1-10%)', value: light },
      { label: '🟡 Modéré (+11-20%)', value: medium },
      { label: '🔴 Grave (+21%+)', value: severe }
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
    if (!this.selectedTemplate || !this.reportGenerated) return;

    const type = this.selectedTemplate.type;
    const vehicleName = this.selectedVehicleId
      ? (this.vehicles.find((v: any) => v.id == this.selectedVehicleId)?.name ||
         this.vehicles.find((v: any) => v.id == this.selectedVehicleId)?.plate || 'Véhicule')
      : 'Tous les véhicules';

    let dateRange = '';
    if (this.fromDate && this.toDate) {
      const f = new Date(this.fromDate).toLocaleDateString('fr-FR');
      const t = new Date(this.toDate).toLocaleDateString('fr-FR');
      dateRange = `${f} - ${t}`;
    } else if (this.dailyReportDate) {
      dateRange = new Date(this.dailyReportDate).toLocaleDateString('fr-FR');
    }

    // Monthly report special handling
    if (type === 'monthly' && this.monthlyReport) {
      this.exportMonthlyReportPdf();
      return;
    }

    // Fuel estimation special handling
    if (type === 'fuel-estimation' && this.fuelEstimationReport) {
      this.exportFuelEstimationPdf(vehicleName, dateRange);
      return;
    }

    const allVehicles = !this.selectedVehicleId;
    const options: any = { allVehicles };
    if (type === 'mileage-period') options.periodType = this.selectedMileagePeriodType;

    const columns = this.pdfExportService.getColumnsForReport(type, options);
    const formatters = this.pdfExportService.getFormattersForReport(type);

    // Calypso 7 (correction client) : pour le rapport trajets on retire
    // les pseudo-lignes day-header et les separateurs « Arret » ; le PDF
    // ne contient que les vrais trajets, plus lisible.
    const pdfData = type === 'trips'
      ? this.tableData.filter((r: any) => r && r.isTrip === true)
      : this.tableData.filter((r: any) => r && r.isDayHeader !== true);

    this.pdfExportService.exportReport({
      title: this.selectedTemplate.name,
      vehicleName,
      dateRange,
      statistics: this.statisticsData,
      columns,
      data: pdfData,
      formatters
    });
  }

  private exportMonthlyReportPdf() {
    if (!this.monthlyReport) return;
    const r = this.monthlyReport;
    const columns = [
      { header: 'Véhicule', dataKey: 'vehicleName' },
      { header: 'Distance', dataKey: 'distance' },
      { header: 'Trajets', dataKey: 'trips' },
      { header: 'Temps conduite', dataKey: 'drivingTime' },
      { header: 'Vit. max', dataKey: 'maxSpeed' },
      { header: 'Arrêts', dataKey: 'stops' },
      { header: 'Score', dataKey: 'score' }
    ];
    const data = (r as any).vehicleReports?.map((v: any) => ({
      vehicleName: v.vehicleName || v.plate || '-',
      distance: `${(v.totalDistanceKm || 0).toFixed(1)} km`,
      trips: v.totalTrips || 0,
      drivingTime: this.formatDuration(v.totalDrivingTimeSeconds || 0),
      maxSpeed: `${(v.maxSpeed || 0).toFixed(0)} km/h`,
      stops: v.totalStops || 0,
      score: `${(v.drivingScore || 0).toFixed(0)}/100`
    })) || [];

    const monthLabel = this.monthlyMonths.find((m: any) => m.value === this.selectedMonthlyMonth)?.label || '';
    this.pdfExportService.exportReport({
      title: 'Rapport mensuel flotte',
      subtitle: `${monthLabel} ${this.selectedMonthlyYear}`,
      dateRange: `${monthLabel} ${this.selectedMonthlyYear}`,
      statistics: {
        'Véhicules': String((r as any).vehicleReports?.length || 0),
        'Distance totale': `${((r as any).totalDistanceKm || 0).toFixed(1)} km`,
        'Trajets': String((r as any).totalTrips || 0)
      },
      columns,
      data
    });
  }

  private exportFuelEstimationPdf(vehicleName: string, dateRange: string) {
    const report = this.fuelEstimationReport;
    if (!report) return;
    const columns = [
      { header: 'Véhicule', dataKey: 'vehicleName' },
      { header: 'Plaque', dataKey: 'plate' },
      { header: 'Distance (km)', dataKey: 'distance' },
      { header: 'Conso. est. (L)', dataKey: 'fuel' },
      { header: `Coût est. (${this.getCurrencyCode()})`, dataKey: 'cost' },
      { header: 'L/100km', dataKey: 'consumption' }
    ];
    const data = (report as any).vehicles?.map((v: any) => ({
      vehicleName: v.name || '-',
      plate: v.plate || '-',
      distance: (v.distanceKm || 0).toFixed(1),
      fuel: (v.estimatedFuelLiters || 0).toFixed(1),
      cost: (v.estimatedCostDa || 0).toFixed(0),
      consumption: (v.consumptionPer100km || 0).toFixed(1)
    })) || [];

    this.pdfExportService.exportReport({
      title: 'Estimation coûts carburant',
      vehicleName,
      dateRange,
      statistics: this.statisticsData,
      columns,
      data
    });
  }

  private formatDuration(seconds: number): string {
    if (!seconds || seconds <= 0) return '-';
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    if (h > 0) return `${h}h ${m}min`;
    return `${m}min`;
  }

  // ==================== CARBURANT RÉEL VS GPS (pleins facturés posés sur la courbe de niveau) ====================

  /** Rapport 17 : courbe du niveau de réservoir mesuré par la jauge, avec un cercle
   *  posé SUR la courbe à chaque plein réellement facturé. L'œil vérifie directement :
   *  une remontée sans cercle = plein non déclaré ; un cercle sans remontée = facture suspecte. */
  executeFuelComparisonReport(vehicleId: number, startDate?: Date, endDate?: Date) {
    this.loading = true;
    this.comparisonAudit = null;
    this.comparisonConsumption = null;
    if (this.comparisonChart) { this.comparisonChart.destroy(); this.comparisonChart = undefined; }
    if (this.comparisonConsoChart) { this.comparisonConsoChart.destroy(); this.comparisonConsoChart = undefined; }

    const startDateStr = startDate ? this.toDateTime(startDate) : undefined;
    const endDateStr = endDate ? this.toDateTime(endDate) : undefined;

    forkJoin({
      audit: this.apiService.getFuelAuditReport(vehicleId, startDateStr, endDateStr),
      // La comparaison plein à plein ne doit jamais faire tomber la courbe de niveau :
      // en cas d'erreur sur ce second appel, on continue sans le second graphe.
      conso: this.apiService.getConsumptionComparison(vehicleId, startDateStr, endDateStr)
        .pipe(catchError(() => of(null)))
    }).pipe(takeUntil(this.destroy$)).subscribe({
        next: ({ audit, conso }) => {
          this.ngZone.run(() => {
            // Tableau « Historique des pleins réels » du plus récent au plus ancien —
            // le graphe, lui, replace chaque plein par sa date, l'ordre lui est égal.
            audit.cardFills = (audit.cardFills || [])
              .slice()
              .sort((a, b) => new Date(b.date).getTime() - new Date(a.date).getTime());
            this.comparisonAudit = audit;
            this.comparisonConsumption = conso;
            this.reportGenerated = true;
            this.loading = false;
            this.cdr.detectChanges();
            // Canvases are *ngIf-gated (comparisonAudit + hasSensor, intervalles) — defer draw until they're in the DOM
            setTimeout(() => { this.drawComparisonChart(); this.drawComparisonConsoChart(); }, 120);
          });
        },
        error: (err) => {
          console.error('Error loading fuel comparison report:', err);
          this.ngZone.run(() => {
            this.loading = false;
            this.reportGenerated = true;
            this.statisticsData = { 'Erreur': 'Impossible de charger la comparaison consommation / pleins réels' };
            this.cdr.detectChanges();
          });
        }
      });
  }

  /** Chip « Écart » du second graphe : vert quand l'écart réel/mesuré reste ≤ 10 % en valeur absolue. */
  comparisonDeltaOk(): boolean {
    const d = this.comparisonConsumption?.deltaPercent;
    return d != null && Math.abs(d) <= 10;
  }

  /** Litres facturés : somme des volumes des pleins réels sur la période. */
  comparisonBilledLiters(): number {
    return (this.comparisonAudit?.cardFills || []).reduce((sum, f) => sum + (Number(f.liters) || 0), 0);
  }

  /** Même règle anti-bruit que le rapport carburant (cf. préparation du graphe
   *  « Évolution du niveau ») : les petites hausses de +1 à +4 pts sont du
   *  ballottement, pas du carburant ajouté — ignorées SANS déplacer la
   *  référence. Les baisses restent (la consommation descend par pas de 1 pt)
   *  et les vraies remontées (≥ +5) passent. */
  private denoiseLevelSeries(series: FuelLevelPoint[]): FuelLevelPoint[] {
    const out: FuelLevelPoint[] = [];
    let ref: number | null = null;
    for (const pt of series) {
      if (ref === null) { out.push(pt); ref = pt.percent; continue; }
      const delta = pt.percent - ref;
      if (delta === 0) continue;              // pas de changement : inutile
      if (delta > 0 && delta < 5) continue;   // bruit +1..+4 : ignoré
      out.push(pt);
      ref = pt.percent;
    }
    return out;
  }

  /** Rééchantillonnage sur une grille TEMPORELLE uniforme (~1200 pas, pas ≥ 15 min).
   *  Indispensable : sur un axe catégorie, des points espacés irrégulièrement
   *  déforment le temps (les journées denses en trames s'étirent, les journées à
   *  l'arrêt se compressent) — la courbe ne ressemblait plus à celle de la page
   *  carburant. Report de la dernière valeur connue entre deux trames ; null
   *  avant la première donnée. */
  private resampleLevelSeries(series: FuelLevelPoint[], startIso: string, endIso: string):
    { grid: { t: string; percent: number | null; liters: number | null }[]; t0: number; stepMs: number } {
    if (!series.length) return { grid: [], t0: 0, stepMs: 1 };
    const firstMs = new Date(series[0].t).getTime();
    const lastMs = new Date(series[series.length - 1].t).getTime();
    const startMs = new Date(startIso).getTime();
    const endMs = new Date(endIso).getTime();
    const t0 = Math.min(isNaN(startMs) ? firstMs : startMs, firstMs);
    const t1 = Math.max(isNaN(endMs) ? lastMs : endMs, lastMs);
    const span = Math.max(t1 - t0, 60_000);
    const stepMs = Math.max(15 * 60_000, Math.ceil(span / 1200 / 60_000) * 60_000);
    const grid: { t: string; percent: number | null; liters: number | null }[] = [];
    let si = 0;
    let last: FuelLevelPoint | null = null;
    for (let t = t0; t <= t1; t += stepMs) {
      while (si < series.length && new Date(series[si].t).getTime() <= t) { last = series[si]; si++; }
      grid.push({ t: new Date(t).toISOString(), percent: last?.percent ?? null, liters: last?.liters ?? null });
    }
    return { grid, t0, stepMs };
  }

  /** Libellés X pour une série horodatée : mêmes marqueurs de jour que
   *  buildSegmentDayLabels (« dd/MM » au premier point de chaque journée, '' sinon,
   *  éclaircis à ≤ 10), appliqués à des timestamps bruts plutôt qu'à des tranches. */
  private buildTimestampDayLabels(timestamps: string[]): string[] {
    const p = (n: number) => String(n).padStart(2, '0');
    const labels: string[] = [];
    let prevDay = '';
    for (const t of timestamps) {
      const d = new Date(t);
      const day = `${p(d.getDate())}/${p(d.getMonth() + 1)}`;
      if (day !== prevDay) { labels.push(day); prevDay = day; }
      else labels.push('');
    }
    // Au-delà de ~10 jours marqués, n'en garder qu'un sur N pour respirer.
    const dayPositions = labels.map((l, i) => l ? i : -1).filter(i => i >= 0);
    if (dayPositions.length > 10) {
      const step = Math.ceil(dayPositions.length / 10);
      dayPositions.forEach((pos, k) => { if (k % step !== 0) labels[pos] = ''; });
    }
    return labels;
  }

  /** Graphe du rapport 17 : la courbe du niveau de réservoir (%) sur la période,
   *  avec un cercle ambre posé SUR la courbe à chaque plein facturé. Chaque remontée
   *  de jauge doit avoir son cercle ; une remontée orpheline = plein non déclaré. */
  drawComparisonChart() {
    if (this.comparisonChart) {
      this.comparisonChart.destroy();
      this.comparisonChart = undefined;
    }

    const canvas = this.comparisonCanvasRef?.nativeElement;
    if (!canvas) return;

    const audit = this.comparisonAudit;
    if (!audit?.hasSensor) return;

    const { grid: series, t0, stepMs } = this.resampleLevelSeries(
      this.denoiseLevelSeries(audit.levelSeries || []), audit.startDate, audit.endDate);
    if (!series.length) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const p = (n: number) => String(n).padStart(2, '0');
    const fmt0 = (n: number | null | undefined) =>
      (n ?? 0).toLocaleString('fr-FR', { maximumFractionDigits: 0 });
    const fmtDT = (iso: string) => {
      const d = new Date(iso);
      return `${p(d.getDate())}/${p(d.getMonth() + 1)} ${p(d.getHours())}:${p(d.getMinutes())}`;
    };

    const labels = this.buildTimestampDayLabels(series.map(pt => pt.t));

    // Cercles : le backend a DÉJÀ rapproché chaque facture d'une remontée de
    // jauge (fillChecks.matchedRefillDate) — le cercle se pose AU SOMMET de la
    // remontée appariée, pas à midi du jour de facture (qui tombait dans le
    // creux d'avant-plein). Sans appariement : repli sur midi. Tableau creux
    // aligné sur les labels (un scatter {x} sur axe catégorie part à gauche).
    const checks = audit.fillChecks || [];
    const gridIndexFor = (ms: number) =>
      Math.min(series.length - 1, Math.max(0, Math.round((ms - t0) / stepMs)));
    const nearestDrawn = (idx: number): number => {
      if (series[idx]?.percent != null) return idx;
      for (let d = 1; d < series.length; d++) {
        if (idx + d < series.length && series[idx + d].percent != null) return idx + d;
        if (idx - d >= 0 && series[idx - d].percent != null) return idx - d;
      }
      return idx;
    };

    const fillValues: (number | null)[] = new Array(series.length).fill(null);
    const fillAt: (FuelCardFill | null)[] = new Array(series.length).fill(null);
    for (const f of (audit.cardFills || [])) {
      const chk = checks.find(c => c.fillDate
        && Math.abs(new Date(c.fillDate).getTime() - new Date(f.date).getTime()) < 3_600_000
        && Math.abs((c.billedLiters ?? 0) - f.liters) < 0.5);
      let whenMs: number;
      if (chk?.matchedRefillDate) {
        whenMs = new Date(chk.matchedRefillDate).getTime() + stepMs; // juste APRÈS le saut = plateau haut
      } else {
        const d = new Date(f.date);
        whenMs = new Date(d.getFullYear(), d.getMonth(), d.getDate(), 12).getTime();
      }
      let idx = nearestDrawn(gridIndexFor(whenMs));
      // Aimant au maximum local (±3 pas ≈ ±2 h) : entre l'arrondi de grille et
      // l'horodatage au DÉBUT du plein, le cercle pouvait tomber au pied de la
      // remontée — il doit la coiffer.
      let bestIdx = idx;
      for (let d = -3; d <= 3; d++) {
        const j = idx + d;
        if (j >= 0 && j < series.length && series[j].percent != null &&
            (series[bestIdx].percent == null || series[j].percent! > series[bestIdx].percent!)) {
          bestIdx = j;
        }
      }
      idx = bestIdx;
      while (idx < series.length && fillAt[idx] !== null) idx++; // 2 pleins même point → décale d'un cran
      if (idx >= series.length) idx = series.length - 1;
      fillValues[idx] = series[idx].percent;
      fillAt[idx] = f;
    }
    const fillsCount = (audit.cardFills || []).length;

    const fillsDatasetLabel = 'Pleins réels';
    const datasets: any[] = [
      {
        label: 'Niveau',
        data: series.map(pt => pt.percent),
        borderColor: '#10b981',
        borderWidth: 2,
        backgroundColor: 'rgba(16,185,129,.10)',
        fill: 'origin',
        pointRadius: 0,
        pointHitRadius: 6,
        tension: .15,
        order: 1
      }
    ];
    if (fillsCount > 0) {
      datasets.push({
        label: fillsDatasetLabel,
        data: fillValues,          // aligné index-à-index sur les labels
        showLine: false,
        spanGaps: false,
        pointStyle: 'circle',
        pointRadius: 9,
        pointHoverRadius: 11,
        backgroundColor: 'rgba(245,158,11,.18)',
        borderColor: '#d97706',
        pointBorderWidth: 3,
        order: 0
      });
    }

    // Annotation « ⛽ N L » au-dessus de chaque cercle tant qu'ils restent lisibles (≤ 12).
    const fillsDatasetIndex = datasets.length - 1;
    const drawFillLabels = fillsCount > 0 && fillsCount <= 12;
    const annotationsPlugin = {
      id: 'comparisonAnnotations',
      afterDatasetsDraw: (chart: any) => {
        if (!drawFillLabels) return;
        const c = chart.ctx as CanvasRenderingContext2D;
        const fillMeta = chart.getDatasetMeta(fillsDatasetIndex);
        c.save();
        c.font = '11px sans-serif';
        c.textAlign = 'center';
        c.textBaseline = 'bottom';
        c.fillStyle = '#b45309';
        fillMeta?.data?.forEach((pt: any, i: number) => {
          const f = fillAt[i];
          if (!f || !pt || pt.skip || isNaN(pt.y)) return;
          c.fillText(`⛽ ${fmt0(f.liters)} L`, pt.x, pt.y - 12);
        });
        c.restore();
      }
    };

    this.comparisonChart = new Chart(ctx, {
      type: 'line',
      data: { labels, datasets },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { display: false },
          tooltip: {
            backgroundColor: 'rgba(15,23,42,.92)',
            padding: 10,
            cornerRadius: 8,
            displayColors: false,
            callbacks: {
              title: (items) => {
                const it = items[0];
                if (!it) return '';
                if ((it.dataset as any).label === fillsDatasetLabel) {
                  const f = fillAt[it.dataIndex];
                  if (!f) return '';
                  const d = new Date(f.date);
                  return `Plein réel — ${p(d.getDate())}/${p(d.getMonth() + 1)}`;
                }
                return '';
              },
              label: (c) => {
                if ((c.dataset as any).label === fillsDatasetLabel) {
                  const f = fillAt[c.dataIndex];
                  if (!f) return '';
                  const amount = (f.cost ?? 0).toLocaleString('fr-FR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
                  const lines = [`${fmt0(f.liters)} L · ${amount} ${this.getCurrencyCode()}`];
                  if (f.station) lines.push(f.station);
                  return lines;
                }
                const pt = series[c.dataIndex];
                if (!pt || pt.percent == null) return '';
                return `${fmtDT(pt.t)} — ${fmt0(pt.percent)} % (${fmt0(pt.liters)} L)`;
              }
            }
          }
        },
        scales: {
          x: {
            grid: { display: false },
            // autoSkip OFF : les libellés vides font l'éclaircissage nous-mêmes,
            // Chart.js ne doit pas supprimer un marqueur de jour au hasard.
            ticks: { color: '#94a3b8', font: { size: 11 }, maxRotation: 0, autoSkip: false }
          },
          y: {
            min: 0,
            max: 100,
            border: { display: false },
            grid: { color: 'rgba(148,163,184,.14)' },
            ticks: { color: '#94a3b8', font: { size: 11 } }
          }
        }
      },
      plugins: [annotationsPlugin]
    });
  }

  /** Second graphe du rapport 17 : consommation mesurée par la jauge vs consommation
   *  réelle déduite des factures (méthode plein à plein), par intervalle entre deux
   *  pleins consécutifs. Barres bleues = jauge (grisées quand la fenêtre est polluée
   *  par le capteur), barres ambre = litres facturés rapportés aux mêmes kilomètres. */
  drawComparisonConsoChart() {
    if (this.comparisonConsoChart) {
      this.comparisonConsoChart.destroy();
      this.comparisonConsoChart = undefined;
    }

    const canvas = this.comparisonConsoCanvasRef?.nativeElement;
    if (!canvas) return;

    const report = this.comparisonConsumption;
    const intervals = report?.intervals || [];
    if (!intervals.length) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const p = (n: number) => String(n).padStart(2, '0');
    const fmtDM = (iso: string) => {
      const d = new Date(iso);
      return `${p(d.getDate())}/${p(d.getMonth() + 1)}`;
    };
    const fmt1 = (n: number | null | undefined) =>
      (n ?? 0).toLocaleString('fr-FR', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
    const fmt0 = (n: number | null | undefined) =>
      (n ?? 0).toLocaleString('fr-FR', { maximumFractionDigits: 0 });

    // Un groupe de barres par intervalle, étiqueté par le plein qui le FERME.
    const labels = intervals.map(iv => `→ ${fmtDM(iv.end)}`);

    // Barres « mesurée » : bleues quand la fenêtre est fiable, grisées sinon
    // (la valeur reste affichée si elle existe — le gris signale le doute).
    const measuredColors = intervals.map(iv =>
      iv.measuredReliable && iv.measuredLPer100 != null ? 'rgba(59,130,246,.65)' : 'rgba(148,163,175,.35)');

    const measuredLabel = 'Mesurée (jauge)';
    const realLabel = 'Réelle (factures)';
    const datasets: any[] = [
      {
        label: measuredLabel,
        data: intervals.map(iv => iv.measuredLPer100),   // null → pas de barre
        backgroundColor: measuredColors,
        borderWidth: 0,
        borderRadius: 5,
        barPercentage: .7,
        categoryPercentage: .75
      },
      {
        label: realLabel,
        data: intervals.map(iv => iv.realLPer100),
        backgroundColor: 'rgba(245,158,11,.75)',
        borderColor: '#d97706',
        borderWidth: 1,
        borderRadius: 5,
        barPercentage: .7,
        categoryPercentage: .75
      }
    ];

    // Deux lignes de moyenne en pointillés (modèle : ligne « moyenne » de
    // drawConsumptionChart) — libellés à droite, décalés de part et d'autre de
    // leur ligne pour ne pas se chevaucher quand les moyennes sont proches.
    const avgMeasured = report!.avgMeasuredLPer100;
    const avgReal = report!.avgRealLPer100;
    const avgLinesPlugin = {
      id: 'consoComparisonAvgLines',
      afterDatasetsDraw: (chart: any) => {
        const yScale = chart.scales?.['y'];
        if (!yScale) return;
        const c = chart.ctx as CanvasRenderingContext2D;
        const area = chart.chartArea;
        const drawAvgLine = (value: number, color: string, label: string, labelAbove: boolean) => {
          const y = yScale.getPixelForValue(value);
          c.save();
          c.strokeStyle = color;
          c.lineWidth = 1.5;
          c.setLineDash([5, 5]);
          c.beginPath();
          c.moveTo(area.left, y);
          c.lineTo(area.right, y);
          c.stroke();
          c.setLineDash([]);
          c.font = '11px sans-serif';
          c.fillStyle = color;
          c.textAlign = 'right';
          c.textBaseline = labelAbove ? 'bottom' : 'top';
          c.fillText(label, area.right - 4, labelAbove ? y - 3 : y + 3);
          c.restore();
        };
        if (avgMeasured != null) drawAvgLine(avgMeasured, 'rgba(59,130,246,.9)', `mesurée ${fmt1(avgMeasured)}`, true);
        if (avgReal != null) drawAvgLine(avgReal, 'rgba(245,158,11,.95)', `réelle ${fmt1(avgReal)}`, false);
      }
    };

    this.comparisonConsoChart = new Chart(ctx, {
      type: 'bar',
      data: { labels, datasets },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          // Légende indispensable ici : deux séries à distinguer.
          legend: {
            display: true,
            position: 'top',
            labels: { usePointStyle: true, boxWidth: 8, color: '#64748b', font: { size: 11 } }
          },
          tooltip: {
            backgroundColor: 'rgba(15,23,42,.92)',
            padding: 10,
            cornerRadius: 8,
            displayColors: false,
            callbacks: {
              title: (items) => {
                const iv = intervals[items[0]?.dataIndex];
                return iv ? `Du ${fmtDM(iv.start)} au ${fmtDM(iv.end)}` : '';
              },
              label: (c) => {
                const iv = intervals[c.dataIndex];
                if (!iv) return '';
                if ((c.dataset as any).label === measuredLabel) {
                  if (!iv.measuredReliable || iv.measuredLPer100 == null) {
                    return 'Mesure non exploitable sur cette fenêtre';
                  }
                  return `Mesurée : ${fmt1(iv.measuredLPer100)} L/100 km (${fmt0(iv.measuredLiters)} L sur ${fmt0(iv.km)} km)`;
                }
                return `Réelle : ${fmt1(iv.realLPer100)} L/100 km (${fmt0(iv.realLiters)} L facturés)`;
              }
            }
          }
        },
        scales: {
          x: {
            grid: { display: false },
            ticks: { color: '#94a3b8', font: { size: 11 }, maxRotation: 0 }
          },
          y: {
            beginAtZero: true,
            grace: '10%',
            border: { display: false },
            grid: { color: 'rgba(148,163,184,.14)' },
            ticks: { color: '#94a3b8', font: { size: 11 } }
          }
        }
      },
      plugins: [avgLinesPlugin]
    });
  }

  // ==================== ANALYSE CONSO PAR SEGMENTS (tranches de X km + tonnage) ====================

  executeConsumptionAnalysisReport(vehicleId: number, startDate?: Date, endDate?: Date) {
    this.loading = true;
    this.consumptionReport = null;
    this.consumptionByTonnage = null;
    this.loadPeriodError = '';
    this.closeAiPanel();
    if (this.consumptionChart) { this.consumptionChart.destroy(); this.consumptionChart = undefined; }

    // Clamp de la taille de tranche (l'input number peut renvoyer une chaîne vide)
    const segmentKm = Math.min(Math.max(Number(this.segmentKm) || 100, 10), 1000);
    this.segmentKm = segmentKm;

    const startDateStr = startDate ? this.toDateTime(startDate) : undefined;
    const endDateStr = endDate ? this.toDateTime(endDate) : undefined;

    forkJoin({
      segments: this.apiService.getConsumptionSegments(vehicleId, startDateStr, endDateStr, segmentKm),
      byTonnage: this.apiService.getConsumptionByTonnage(vehicleId, startDateStr, endDateStr, segmentKm),
      loadPeriods: this.apiService.getVehicleLoadPeriods(vehicleId)
    }).pipe(takeUntil(this.destroy$)).subscribe({
      next: ({ segments, byTonnage, loadPeriods }) => {
        this.ngZone.run(() => {
          this.consumptionReport = segments;
          this.consumptionByTonnage = byTonnage;
          this.loadPeriods = loadPeriods || [];
          this.reportGenerated = true;
          this.loading = false;
          this.cdr.detectChanges();
          // Canvas is *ngIf-gated on consumptionReport + hasSensor — defer draw until it's in the DOM
          setTimeout(() => this.drawConsumptionChart(), 120);
        });
      },
      error: (err) => {
        console.error('Error loading consumption analysis report:', err);
        this.ngZone.run(() => {
          this.loading = false;
          this.reportGenerated = true;
          this.statisticsData = { 'Erreur': 'Impossible de charger l\'analyse de consommation par segments' };
          this.cdr.detectChanges();
        });
      }
    });
  }

  /** Tranches affichées : en vue client (par défaut) les tranches sans données
   *  exploitables sont masquées — le résumé, lui, les excluait déjà des stats. */
  visibleConsumptionSegments(): ConsumptionSegment[] {
    const segs = this.consumptionReport?.segments || [];
    return this.showExcludedSegments ? segs : segs.filter(s => s.isReliable);
  }

  reliableConsumptionKm(): number {
    return (this.consumptionReport?.segments || [])
      .filter(s => s.isReliable)
      .reduce((sum, s) => sum + s.distanceKm, 0);
  }

  /** Preset de taille de tranche (chips 50 / 100 / 200 km) : applique et relance le rapport. */
  setSegmentKmPreset(km: number) {
    if (this.loading) return;
    this.segmentKm = km;
    this.executeReport();
  }

  /** Date de début formatée (« le 04/08 à 08:12 ») de la tranche min/max fiable — '' si introuvable. */
  private segmentStartLabel(index: number | null | undefined): string {
    if (index == null) return '';
    const seg = this.consumptionReport?.segments?.find(s => s.index === index);
    if (!seg) return '';
    const d = new Date(seg.startTime);
    const p = (n: number) => String(n).padStart(2, '0');
    return `le ${p(d.getDate())}/${p(d.getMonth() + 1)} à ${p(d.getHours())}:${p(d.getMinutes())}`;
  }

  minSegmentDate(): string { return this.segmentStartLabel(this.consumptionReport?.summary?.minSegmentIndex); }
  maxSegmentDate(): string { return this.segmentStartLabel(this.consumptionReport?.summary?.maxSegmentIndex); }

  /** Clic sur une barre ou une ligne du détail : demande à l'IA d'expliquer la tranche. */
  explainSegment(seg: ConsumptionSegment) {
    const report = this.consumptionReport;
    const vehicleId = this.selectedVehicleId ? parseInt(this.selectedVehicleId) : undefined;
    if (!report || !vehicleId) return;

    this.aiSegment = seg;
    this.aiExplanation = '';
    this.aiLoading = true;
    this.cdr.detectChanges();

    this.apiService.explainConsumptionSegment({
      vehicleId,
      startTime: seg.startTime,
      endTime: seg.endTime,
      distanceKm: seg.distanceKm,
      fuelLiters: seg.fuelLiters,
      lPer100Km: seg.lPer100Km,
      tonnageT: seg.tonnageT,
      isReliable: seg.isReliable,
      exclusionReason: seg.exclusionReason,
      segmentKm: report.segmentKm,
      periodAvgLPer100Km: report.summary?.avgLPer100Km ?? null,
      periodMinLPer100Km: report.summary?.minLPer100Km ?? null,
      periodMaxLPer100Km: report.summary?.maxLPer100Km ?? null
    }).pipe(takeUntil(this.destroy$)).subscribe({
      next: (res) => this.ngZone.run(() => {
        this.aiExplanation = res.explanation;
        this.aiLoading = false;
        this.cdr.detectChanges();
      }),
      error: () => this.ngZone.run(() => {
        this.aiExplanation = 'Analyse IA momentanément indisponible.';
        this.aiLoading = false;
        this.cdr.detectChanges();
      })
    });
  }

  closeAiPanel() {
    this.aiSegment = null;
    this.aiExplanation = '';
    this.aiLoading = false;
  }

  /** Libellés X partagés par les graphes « par tranches » (rapports 17 et 18) :
   *  uniquement les débuts de journée — les heures ne servent qu'à l'échelle ≤ 48 h.
   *  Éclaircissage manuel + autoSkip OFF obligatoire : laisser Chart.js sauter des
   *  libellés supprime des marqueurs de jour au hasard et laisse des heures
   *  orphelines illisibles. */
  private buildSegmentDayLabels(segments: ConsumptionSegment[]): string[] {
    const p = (n: number) => String(n).padStart(2, '0');
    const spanMs = new Date(segments[segments.length - 1].startTime).getTime()
                 - new Date(segments[0].startTime).getTime();
    const labels: string[] = [];
    if (spanMs <= 48 * 3600 * 1000) {
      for (let i = 0; i < segments.length; i++) {
        const d = new Date(segments[i].startTime);
        const prev = i > 0 ? new Date(segments[i - 1].startTime) : null;
        const sameDay = prev != null && prev.getDate() === d.getDate() && prev.getMonth() === d.getMonth();
        labels.push(sameDay ? `${p(d.getHours())}h` : `${p(d.getDate())}/${p(d.getMonth() + 1)} ${p(d.getHours())}h`);
      }
    } else {
      let prevDay = '';
      for (const s of segments) {
        const d = new Date(s.startTime);
        const day = `${p(d.getDate())}/${p(d.getMonth() + 1)}`;
        if (day !== prevDay) { labels.push(day); prevDay = day; }
        else labels.push('');
      }
      // Au-delà de ~10 jours marqués, n'en garder qu'un sur N pour respirer.
      const dayPositions = labels.map((l, i) => l ? i : -1).filter(i => i >= 0);
      if (dayPositions.length > 10) {
        const step = Math.ceil(dayPositions.length / 10);
        dayPositions.forEach((pos, k) => { if (k % step !== 0) labels[pos] = ''; });
      }
    }
    return labels;
  }

  drawConsumptionChart() {
    if (this.consumptionChart) {
      this.consumptionChart.destroy();
      this.consumptionChart = undefined;
    }

    const canvas = this.consumptionCanvasRef?.nativeElement;
    if (!canvas) return;

    const report = this.consumptionReport;
    if (!report?.hasSensor) return;

    const segments = this.visibleConsumptionSegments();
    if (!segments.length) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const summary = report.summary;
    const p = (n: number) => String(n).padStart(2, '0');
    const labels = this.buildSegmentDayLabels(segments);

    const isMin = (s: ConsumptionSegment) => s.isReliable && summary?.minSegmentIndex != null && s.index === summary.minSegmentIndex;
    const isMax = (s: ConsumptionSegment) => s.isReliable && summary?.maxSegmentIndex != null && s.index === summary.maxSegmentIndex;

    const colors = segments.map(s => {
      if (!s.isReliable) return 'rgba(148,163,175,.35)';   // exclu (visible seulement en mode diagnostic)
      if (isMin(s)) return '#22c55e';                       // meilleure tranche
      if (isMax(s)) return '#ef4444';                       // pire tranche
      return 'rgba(59,130,246,.55)';
    });
    const hoverColors = segments.map(s => {
      if (!s.isReliable) return 'rgba(148,163,175,.35)';
      if (isMin(s)) return '#22c55e';
      if (isMax(s)) return '#ef4444';
      return 'rgba(59,130,246,.8)';
    });

    const fmt1 = (n: number | null | undefined) =>
      (n ?? 0).toLocaleString('fr-FR', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
    const fmt0 = (n: number | null | undefined) =>
      (n ?? 0).toLocaleString('fr-FR', { maximumFractionDigits: 0 });
    const fmtT = (n: number | null | undefined) =>
      (n ?? 0).toLocaleString('fr-FR', { maximumFractionDigits: 1 });
    const fmtDT = (iso: string) => {
      const d = new Date(iso);
      return `${p(d.getDate())}/${p(d.getMonth() + 1)} ${p(d.getHours())}:${p(d.getMinutes())}`;
    };

    const datasets: any[] = [
      {
        label: 'L/100 km',
        data: segments.map(s => s.lPer100Km),
        backgroundColor: colors,
        hoverBackgroundColor: hoverColors,
        borderWidth: 0,
        borderRadius: 6,
        barPercentage: .62,
        categoryPercentage: .78,
        order: 2
      }
    ];

    if (summary?.avgLPer100Km != null) {
      datasets.push({
        label: 'Moyenne',
        data: segments.map(() => summary.avgLPer100Km),
        type: 'line' as any,
        borderColor: 'rgba(245,158,11,.9)',
        backgroundColor: 'transparent',
        borderDash: [5, 5],
        borderWidth: 1.5,
        pointRadius: 0,
        pointHoverRadius: 0,
        fill: false,
        order: 1
      });
    }

    // Annotations discrètes : valeur au-dessus des barres min/max + « moyenne X » sur la ligne.
    const minIdx = segments.findIndex(isMin);
    const maxIdx = segments.findIndex(isMax);
    const avg = summary?.avgLPer100Km;
    const annotationsPlugin = {
      id: 'consoAnnotations',
      afterDatasetsDraw: (chart: any) => {
        const c = chart.ctx as CanvasRenderingContext2D;
        const meta = chart.getDatasetMeta(0);
        c.save();
        c.font = '11px sans-serif';
        c.textAlign = 'center';
        c.textBaseline = 'bottom';
        const drawBarValue = (i: number, color: string) => {
          const bar = meta?.data?.[i];
          if (!bar) return;
          c.fillStyle = color;
          c.fillText(fmt1(segments[i].lPer100Km), bar.x, bar.y - 4);
        };
        if (minIdx >= 0) drawBarValue(minIdx, '#16a34a');
        if (maxIdx >= 0 && maxIdx !== minIdx) drawBarValue(maxIdx, '#dc2626');
        if (avg != null && chart.scales?.['y']) {
          const y = chart.scales['y'].getPixelForValue(avg);
          c.fillStyle = 'rgba(245,158,11,.95)';
          c.textAlign = 'right';
          c.fillText(`moyenne ${fmt1(avg)}`, chart.chartArea.right - 4, y - 4);
        }
        c.restore();
      }
    };

    this.consumptionChart = new Chart(ctx, {
      type: 'bar',
      data: { labels, datasets },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        // Clic sur une barre → explication IA de la tranche (hors zone Angular)
        onClick: (_evt, elements) => {
          if (!elements?.length) return;
          const s = segments[elements[0].index];
          if (s) this.ngZone.run(() => this.explainSegment(s));
        },
        onHover: (evt, elements) => {
          const target = evt.native?.target as HTMLElement | null;
          if (target) target.style.cursor = elements?.length ? 'pointer' : 'default';
        },
        plugins: {
          legend: { display: false },
          tooltip: {
            backgroundColor: 'rgba(15,23,42,.92)',
            padding: 10,
            cornerRadius: 8,
            displayColors: false,
            filter: (item) => item.datasetIndex === 0,
            callbacks: {
              title: (items) => {
                const s = segments[items[0]?.dataIndex];
                return s ? `Du ${fmtDT(s.startTime)} au ${fmtDT(s.endTime)}` : '';
              },
              label: (c) => {
                const s = segments[c.dataIndex];
                if (!s) return '';
                const lines = [
                  `${fmt1(s.lPer100Km)} L/100 km`,
                  `${fmt0(s.distanceKm)} km parcourus · ${fmt0(s.fuelLiters)} L consommés`,
                  `Chargement : ${s.tonnageT != null ? fmtT(s.tonnageT) + ' t' : 'non renseigné'}`
                ];
                if (s.exclusionReason) lines.push('Données non exploitables sur cette tranche');
                return lines;
              }
            }
          }
        },
        scales: {
          x: {
            grid: { display: false },
            // autoSkip OFF : les libellés vides font l'éclaircissage nous-mêmes,
            // Chart.js ne doit pas supprimer un marqueur de jour au hasard.
            ticks: { color: '#94a3b8', font: { size: 11 }, maxRotation: 0, autoSkip: false }
          },
          y: {
            beginAtZero: true,
            grace: '10%',
            border: { display: false },
            grid: { color: 'rgba(148,163,184,.14)' },
            ticks: { color: '#94a3b8', font: { size: 11 } }
          }
        }
      },
      plugins: [annotationsPlugin]
    });
  }

  /** Ajoute une période de chargement (tonnage) puis relance le rapport :
   *  les segments héritent du nouveau tonnage et les tableaux se rafraîchissent. */
  addLoadPeriod() {
    this.loadPeriodError = '';
    const vehicleId = this.selectedVehicleId ? parseInt(this.selectedVehicleId) : undefined;
    if (!vehicleId) return;

    if (this.newLoadPeriod.tonnageT == null || this.newLoadPeriod.tonnageT === ('' as any) || !this.newLoadPeriod.startTime) {
      this.loadPeriodError = 'Le tonnage et la date de début sont obligatoires.';
      return;
    }

    // Les inputs datetime-local renvoient déjà "YYYY-MM-DDTHH:mm" (heure locale),
    // le même format que toDateTime() — on transmet tel quel, jamais en UTC.
    const body = {
      vehicleId,
      startTime: this.newLoadPeriod.startTime,
      endTime: this.newLoadPeriod.endTime || null,
      tonnageT: Number(this.newLoadPeriod.tonnageT),
      notes: this.newLoadPeriod.notes?.trim() || null
    };

    this.apiService.createVehicleLoadPeriod(body).subscribe({
      next: () => {
        this.ngZone.run(() => {
          this.newLoadPeriod = { startTime: '', endTime: '', tonnageT: null, notes: '' };
          this.cdr.detectChanges();
        });
        // Relance complète : recharge périodes + segments + comparaison par tonnage
        this.executeReport();
      },
      error: (err) => {
        console.error('Error creating load period:', err);
        this.ngZone.run(() => {
          // 400 = message métier du backend (ex : chevauchement de périodes)
          this.loadPeriodError = err?.error?.message || 'Impossible d\'ajouter la période de chargement.';
          this.cdr.detectChanges();
        });
      }
    });
  }

  deleteLoadPeriod(id: number) {
    if (!confirm('Supprimer cette période de chargement ?')) return;
    this.loadPeriodError = '';
    this.apiService.deleteVehicleLoadPeriod(id).subscribe({
      next: () => {
        // Relance complète : recharge périodes + segments + comparaison par tonnage
        this.executeReport();
      },
      error: (err) => {
        console.error('Error deleting load period:', err);
        this.ngZone.run(() => {
          this.loadPeriodError = err?.error?.message || 'Impossible de supprimer la période de chargement.';
          this.cdr.detectChanges();
        });
      }
    });
  }

  executeFuelEstimationReport(vehicleId?: number, startDate?: Date, endDate?: Date) {
    this.loading = true;
    this.fuelEstimationReport = null;
    this.fuelEstimationActiveSection = 'summary';
    
    const startDateStr = startDate ? this.toDateTime(startDate) : undefined;
    const endDateStr = endDate ? this.toDateTime(endDate) : undefined;
    
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
            'Distance totale': `${Math.round(report.totalFleetDistanceKm)} km`,
            'Carburant consommé': `${report.totalFleetFuelConsumedLiters.toFixed(1)} L`,
            'Coût total estimé': `${Math.round(report.totalFleetFuelCost)} ${this.getCurrencyCode()}`,
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

    // ===== CHART 1: Per-vehicle Consumption (L) vs Cost (active currency) with L/100km line =====
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
                label: `Coût (${this.getCurrencyCode()})`,
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
              legend: { display: false },
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
                title: { display: true, text: `Litres / ${this.getCurrencyCode()}` },
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
              legend: { display: false },
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
                    return `${context.label}: ${value.toFixed(2)} ${this.getCurrencyCode()} (${pct}%)`;
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
            label: `Coût (${this.getCurrencyCode()})`,
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
          legend: { display: false },
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
            title: { display: true, text: `Coût (${this.getCurrencyCode()})` }
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
    // Le fuel detector côté serveur peut découper un seul remplissage
    // physique en plusieurs "jumps" successifs si la jauge se stabilise
    // par paliers (réservoir qui se remplit, vapeur, etc.). Avant
    // l'affichage on regroupe les refuels par minute calendaire :
    // toutes les entrées tombant sur le même YYYY-MM-DD HH:mm sont
    // fusionnées en une seule ligne, litres et coût additionnés.
    // Évite les "doublons" visuels du genre :
    //   21/04/2026 10:48: +35.2 L
    //   21/04/2026 10:48: +4.0 L
    //   21/04/2026 10:48: +8.4 L
    // → désormais une seule ligne 21/04/2026 10:48: +47.6 L.
    const grouped = new Map<string, { date: Date; liters: number; cost: number }>();
    for (const r of refuels) {
      const date = new Date(r.timestamp);
      const key = date.toISOString().slice(0, 16); // YYYY-MM-DDTHH:mm
      const existing = grouped.get(key);
      if (existing) {
        existing.liters += (r.fuelAddedLiters || 0);
        existing.cost += (r.estimatedCost || 0);
      } else {
        grouped.set(key, {
          date,
          liters: r.fuelAddedLiters || 0,
          cost: r.estimatedCost || 0
        });
      }
    }
    return Array.from(grouped.values())
      .sort((a, b) => a.date.getTime() - b.date.getTime())
      .map(g => {
        const dateStr = g.date.toLocaleDateString('fr-FR') + ' ' +
          g.date.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
        return `${dateStr}: +${g.liters.toFixed(1)} L (~${g.cost.toFixed(2)} ${this.getCurrencyCode()})`;
      })
      .join('\n');
  }

  getStatKeys(): string[] {
    return Object.keys(this.statisticsData);
  }

  /**
   * Universal address enrichment for ALL reports.
   * Detects and geocodes coordinate-based fields: location, address, startAddress, endAddress.
   */
  async enrichAllAddresses() {
    const needsGeocode = (val: string | undefined | null): boolean => {
      if (!val) return false;
      // Detect: coordinates with °, 'Chargement...', or pure numeric (no letters = raw coordinates)
      return val.includes('°') || val === 'Chargement...' || !/[a-zA-Z\u00C0-\u024F\u0600-\u06FF]/.test(val);
    };

    // Apply geocoding results to tableData, return count of unresolved fields
    const applyResults = (addressMap: Map<string, string>): number => {
      let unresolved = 0;
      this.ngZone.run(() => {
        this.tableData = this.tableData.map((row: any) => {
          const updated = { ...row };
          if (needsGeocode(row.location) && row.latitude && row.longitude) {
            const key = `${row.latitude.toFixed(4)},${row.longitude.toFixed(4)}`;
            const addr = addressMap.get(key);
            if (addr) { updated.location = addr; } else { updated.location = `${row.latitude.toFixed(5)}, ${row.longitude.toFixed(5)}`; unresolved++; }
          }
          if (needsGeocode(row.address) && row.latitude && row.longitude) {
            const key = `${row.latitude.toFixed(4)},${row.longitude.toFixed(4)}`;
            const addr = addressMap.get(key);
            if (addr) {
              updated.address = addr;
              if (row.description && row.description === row.address) updated.description = addr;
            } else { updated.address = `${row.latitude.toFixed(5)}, ${row.longitude.toFixed(5)}`; unresolved++; }
          }
          if (needsGeocode(row.startAddress) && row.startLat && row.startLng) {
            const key = `${row.startLat.toFixed(4)},${row.startLng.toFixed(4)}`;
            const addr = addressMap.get(key);
            if (addr) { updated.startAddress = addr; } else { updated.startAddress = `${row.startLat.toFixed(5)}, ${row.startLng.toFixed(5)}`; unresolved++; }
          }
          if (needsGeocode(row.endAddress) && row.endLat && row.endLng) {
            const key = `${row.endLat.toFixed(4)},${row.endLng.toFixed(4)}`;
            const addr = addressMap.get(key);
            if (addr) { updated.endAddress = addr; } else { updated.endAddress = `${row.endLat.toFixed(5)}, ${row.endLng.toFixed(5)}`; unresolved++; }
          }
          return updated;
        });
        this.cdr.detectChanges();
      });
      return unresolved;
    };

    // Collect coordinates needing geocoding from current tableData
    const collectCoords = (): { lat: number; lon: number }[] => {
      const coords: { lat: number; lon: number }[] = [];
      this.tableData.forEach((row: any) => {
        if (needsGeocode(row.location) && row.latitude && row.longitude)
          coords.push({ lat: row.latitude, lon: row.longitude });
        if (needsGeocode(row.address) && row.latitude && row.longitude)
          coords.push({ lat: row.latitude, lon: row.longitude });
        if (needsGeocode(row.startAddress) && row.startLat && row.startLng)
          coords.push({ lat: row.startLat, lon: row.startLng });
        if (needsGeocode(row.endAddress) && row.endLat && row.endLng)
          coords.push({ lat: row.endLat, lon: row.endLng });
      });
      return coords;
    };

    const coordsToGeocode = collectCoords();
    if (coordsToGeocode.length === 0) return;

    // --- Pass 1: initial geocoding ---
    try {
      const addressMap = await this.geocodingService.batchReverseGeocode(coordsToGeocode);
      const unresolved = applyResults(addressMap);

      // --- Pass 2 & 3: retry unresolved after delay ---
      if (unresolved > 0) {
        const retryDelays = [3000, 8000]; // 3s then 8s
        for (const delay of retryDelays) {
          await new Promise(resolve => setTimeout(resolve, delay));
          // Re-collect only coords still needing geocoding
          const retryCoords = collectCoords();
          if (retryCoords.length === 0) break;
          // Clear cache for these coords so Nominatim is re-queried
          this.geocodingService.clearCacheForCoords(retryCoords);
          const retryMap = await this.geocodingService.batchReverseGeocode(retryCoords);
          const stillUnresolved = applyResults(retryMap);
          if (stillUnresolved === 0) break;
        }
      }
    } catch (error) {
      console.error('Error enriching addresses:', error);
      // Fallback: replace 'Chargement...' with coordinates
      this.ngZone.run(() => {
        this.tableData = this.tableData.map((row: any) => {
          const updated = { ...row };
          if (row.address === 'Chargement...' && row.latitude && row.longitude)
            updated.address = `${row.latitude.toFixed(5)}, ${row.longitude.toFixed(5)}`;
          if (row.location === 'Chargement...' && row.latitude && row.longitude)
            updated.location = `${row.latitude.toFixed(5)}, ${row.longitude.toFixed(5)}`;
          if (row.startAddress === 'Chargement...' && row.startLat && row.startLng)
            updated.startAddress = `${row.startLat.toFixed(5)}, ${row.startLng.toFixed(5)}`;
          if (row.endAddress === 'Chargement...' && row.endLat && row.endLng)
            updated.endAddress = `${row.endLat.toFixed(5)}, ${row.endLng.toFixed(5)}`;
          return updated;
        });
        this.cdr.detectChanges();
      });
    }
  }

  Object = Object;

  // ==================== REPAIRS REPORT (Coûts véhicules) ====================
  
  executeCostsReport(vehicleId?: number, startDate?: Date, endDate?: Date) {
    this.loading = true;
    
    const options: any = {};
    if (vehicleId) options.vehicleId = vehicleId;
    if (startDate) options.fromDate = this.toLocalDate(startDate);
    if (endDate) options.toDate = this.toLocalDate(endDate);
    
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
    
    this.apiService.getMaintenanceRecords(vehicleId, startDate, endDate).subscribe({
      next: (records) => {
        this.ngZone.run(() => {
          this.processMaintenanceReport(records);
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

    // Sort by date descending (backend field: doneDate)
    records.sort((a, b) => {
      const dateA = new Date(a.doneDate || a.date || a.createdAt);
      const dateB = new Date(b.doneDate || b.date || b.createdAt);
      return dateB.getTime() - dateA.getTime();
    });

    // Process table data — uses MaintenanceLogReportDto fields from backend
    this.tableData = records.map(record => {
      const cost = record.actualCost || record.totalCost || 0;
      // Only DONE maintenances carry a real cost; planned ones must not show one.
      const isDone = record.status === 'completed' || record.status === 'done';
      return {
        vehicleName: record.vehicleName || record.plate || `Véhicule ${record.vehicleId}`,
        vehicleId: record.vehicleId,
        date: this.formatDateTime(record.doneDate || record.date),
        type: record.templateName || record.category || record.type || 'Général',
        description: record.notes || record.description || '-',
        status: this.getMaintenanceStatusLabel(record.status),
        statusKey: record.status,
        cost: isDone ? cost : 0,
        costFormatted: isDone ? this.formatCurrency(cost) : '—',
        mileage: record.doneKm ? `${record.doneKm.toLocaleString('fr-FR')} km` : '-',
        mileageValue: record.doneKm || 0,
        supplierName: record.supplierName || '-'
      };
    });

    // Chart data - group by type (templateName)
    const byType: { [key: string]: number } = {};
    records.forEach(record => {
      const type = record.templateName || record.category || 'Général';
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
      const vehicleName = record.vehicleName || `Véhicule ${record.vehicleId}`;
      costsByVehicle[vehicleName] = (costsByVehicle[vehicleName] || 0) + (record.actualCost || 0);
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
    const totalCost = records.reduce((sum: number, r: any) => sum + (r.actualCost || 0), 0);

    this.statisticsData = {
      'Total maintenances': records.length.toString(),
      'Coût total': this.formatCurrency(totalCost),
      'Véhicules': new Set(records.map((r: any) => r.vehicleId)).size.toString()
    };
  }

  getMaintenanceStatusLabel(status: string): string {
    const statuses: { [key: string]: string } = {
      'completed': '✅ Complétée',
      'done': '✅ Complétée',
      'scheduled': '📅 Planifiée',
      'pending': '⏳ En attente',
      'in_progress': '🔄 En cours',
      'overdue': '🔴 En retard',
      'due': '🟡 À prévoir',
      'upcoming': '🟡 À prévoir',
      'critical': '🟠 Critique',
      'ok': '🟢 OK',
      'cancelled': '❌ Annulée'
    };
    return statuses[status] || status || '⏳ En attente';
  }

  // ==================== AI FLEET REPORT ====================

  executeAiFleetReport() {
    this.loading = true;
    this.aiFleetLoading = true;
    this.aiFleetReport = null;
    this.aiFleetQaMessages = [];
    this.aiFleetQuestionInput = '';

    this.apiService.generateFleetReport(this.aiFleetPeriod).pipe(takeUntil(this.destroy$)).subscribe({
      next: (data: any) => {
        this.ngZone.run(() => {
          this.aiFleetReport = data;
          this.aiFleetAnalysisHtml = this.renderMarkdown(data.aiAnalysis || '');
          this.reportGenerated = true;
          this.loading = false;
          this.aiFleetLoading = false;
          this.activeTab = 'statistics';
          this.cdr.detectChanges();
        });
      },
      error: (err: any) => {
        this.ngZone.run(() => {
          this.statisticsData = { 'Erreur': err.error?.message || 'Erreur lors de la génération du rapport IA' };
          this.reportGenerated = true;
          this.loading = false;
          this.aiFleetLoading = false;
          this.cdr.detectChanges();
        });
      }
    });
  }

  askAiFleetQuestion(question: string) {
    if (!question?.trim() || this.aiFleetAskLoading) return;
    this.aiFleetAskLoading = true;
    this.aiFleetQuestionInput = '';
    this.aiFleetQaMessages.push({ role: 'user', text: question, html: question as any });

    const context = this.aiFleetReport?.aiAnalysis || '';
    this.apiService.askFleetReport(question, context).pipe(takeUntil(this.destroy$)).subscribe({
      next: (data: any) => {
        this.ngZone.run(() => {
          this.aiFleetQaMessages.push({ role: 'ai', text: data.answer, html: this.renderMarkdown(data.answer) });
          this.aiFleetAskLoading = false;
          this.cdr.detectChanges();
          setTimeout(() => {
            const el = document.querySelector('.ai-qa-messages');
            if (el) el.scrollTop = el.scrollHeight;
          }, 100);
        });
      },
      error: (err: any) => {
        this.ngZone.run(() => {
          this.aiFleetQaMessages.push({ role: 'ai', text: 'Erreur: ' + (err.error?.message || 'Service indisponible'), html: ('Erreur: ' + (err.error?.message || 'Service indisponible')) as any });
          this.aiFleetAskLoading = false;
          this.cdr.detectChanges();
        });
      }
    });
  }

  getAiHealthColor(level: string): string {
    const colors: any = { excellent: '#22c55e', good: '#3b82f6', fair: '#f59e0b', poor: '#f97316', critical: '#ef4444' };
    return colors[level] || '#94a3b8';
  }

  getAiScoreColor(score: number): string {
    if (score >= 80) return '#22c55e';
    if (score >= 60) return '#3b82f6';
    if (score >= 40) return '#f59e0b';
    return '#ef4444';
  }

  getAiBarPct(value: number, max: number): number {
    return max > 0 ? (value / max) * 100 : 0;
  }

  get aiMaxFuel(): number {
    return Math.max(...(this.aiFleetReport?.charts?.topFuelConsumers?.map((f: any) => f.value) || [1]), 1);
  }

  get aiMaxMileage(): number {
    return Math.max(...(this.aiFleetReport?.charts?.mileageByVehicle?.map((m: any) => m.value) || [1]), 1);
  }

  private renderMarkdown(text: string): SafeHtml {
    try {
      const html = marked.parse(text, { async: false }) as string;
      return this.sanitizer.bypassSecurityTrustHtml(html);
    } catch {
      return text;
    }
  }

  private saveState(): void {
    this.reportStateService.save({
      selectedTemplateId: this.selectedTemplateId,
      selectedVehicleId: this.selectedVehicleId,
      selectedDriverId: this.selectedDriverId,
      selectedDepartmentId: this.selectedDepartmentId,
      selectedVehicleIds: this.selectedVehicleIds,
      filterByVehicle: this.filterByVehicle,
      filterByDriver: this.filterByDriver,
      filterByDepartment: this.filterByDepartment,
      selectedStandardPeriod: this.selectedStandardPeriod,
      selectedCostPeriod: this.selectedCostPeriod,
      customStartDate: this.customStartDate,
      customEndDate: this.customEndDate,
      dailyReportDate: this.dailyReportDate,
      speedLimit: this.speedLimit,
      selectedMileagePeriodType: this.selectedMileagePeriodType,
      mileagePeriodDate: this.mileagePeriodDate,
      mileagePeriodStartDate: this.mileagePeriodStartDate,
      mileagePeriodEndDate: this.mileagePeriodEndDate,
      mileagePeriodMonth: this.mileagePeriodMonth,
      mileagePeriodYear: this.mileagePeriodYear,
      selectedMonthlyYear: this.selectedMonthlyYear,
      selectedMonthlyMonth: this.selectedMonthlyMonth,
      reportGenerated: this.reportGenerated,
      activeTab: this.activeTab,
      expandedSections: { ...this.expandedSections },
      drivingBehaviorFilters: { ...this.drivingBehaviorFilters },
      aiFleetPeriod: this.aiFleetPeriod
    });
  }

  private restoreState(): void {
    const state = this.reportStateService.restore();
    if (!state || !state.selectedTemplateId) return;

    this.selectedTemplateId = state.selectedTemplateId;
    this.selectedTemplate = this.templates.find(t => t.id === this.selectedTemplateId) || null;
    this.selectedVehicleId = state.selectedVehicleId;
    this.selectedDriverId = state.selectedDriverId;
    this.selectedDepartmentId = state.selectedDepartmentId;
    this.selectedVehicleIds = state.selectedVehicleIds || [];
    this.filterByVehicle = state.filterByVehicle;
    this.filterByDriver = state.filterByDriver;
    this.filterByDepartment = state.filterByDepartment;
    this.selectedStandardPeriod = state.selectedStandardPeriod;
    this.selectedCostPeriod = state.selectedCostPeriod;
    this.customStartDate = state.customStartDate;
    this.customEndDate = state.customEndDate;
    this.dailyReportDate = state.dailyReportDate;
    this.speedLimit = state.speedLimit;
    this.selectedMileagePeriodType = state.selectedMileagePeriodType as any;
    this.mileagePeriodDate = state.mileagePeriodDate;
    this.mileagePeriodStartDate = state.mileagePeriodStartDate;
    this.mileagePeriodEndDate = state.mileagePeriodEndDate;
    this.mileagePeriodMonth = state.mileagePeriodMonth;
    this.mileagePeriodYear = state.mileagePeriodYear;
    this.selectedMonthlyYear = state.selectedMonthlyYear;
    this.selectedMonthlyMonth = state.selectedMonthlyMonth;
    this.activeTab = state.activeTab;
    this.expandedSections = state.expandedSections || this.expandedSections;
    this.drivingBehaviorFilters = state.drivingBehaviorFilters || this.drivingBehaviorFilters;
    this.aiFleetPeriod = state.aiFleetPeriod;

    // Re-compute fromDate/toDate from the restored period
    if (this.selectedStandardPeriod !== 'custom') {
      this.selectStandardPeriod(this.selectedStandardPeriod);
    } else if (this.customStartDate && this.customEndDate) {
      this.fromDate = this.toDateTime(new Date(this.customStartDate + 'T00:00:00'));
      this.toDate = this.toDateTime(new Date(this.customEndDate + 'T23:59:59'));
    }

    // Auto re-execute the report if one was previously generated
    if (state.reportGenerated && this.selectedTemplate) {
      this.cdr.detectChanges();
      // Small delay to let Angular digest the restored bindings
      setTimeout(() => {
        if (this.selectedTemplate) this.executeReport();
      }, 200);
    }
  }

  ngOnDestroy() {
    this.saveState();
    if (this.popupMap) {
      this.popupMap.remove();
      this.popupMap = undefined;
    }
    this.destroy$.next();
    this.destroy$.complete();
  }
}
