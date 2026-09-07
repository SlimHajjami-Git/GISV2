import { Component, OnInit, OnDestroy, ChangeDetectorRef, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subject, takeUntil, forkJoin, of, catchError } from 'rxjs';
import { ApiService, FuelTypeDto, FuelPriceFullDto, MaintenanceTemplateDto, VehiclePartDto, AcquisitionPaymentDto } from '../services/api.service';
import { PdfExportService, PdfGroup, GroupedPdfReportConfig } from '../services/pdf-export.service';
import { AppLayoutComponent } from './shared/app-layout.component';
import { AppCurrencyPipe } from '../pipes/user-preference-pipes';
import { UserPreferencesService } from '../services/user-preferences.service';
import { AuthService } from '../services/auth.service';

export interface Expense {
  id: string;
  vehicleId: number;
  vehiclePlate: string;
  vehicleName: string;
  category: string;
  label: string;
  quantity: number;
  unitPrice: number;
  totalAmount: number;
  date: Date;
  description?: string;
  createdAt: Date;
  sourceTable?: string;

  // Calypso 7 — when set, this expense was auto-created from an accident
  // timeline (Phase 5 repair or Phase 6 insurance refund). The list shows
  // a small "🚗 Accident #X" badge and the row links back to the report.
  accidentEventId?: number | null;
  /** True when the row is an insurance refund — UI renders amount as a credit (green). */
  isRefund?: boolean;

  /** Justificatif (facture scannée) — image ou PDF servie par /uploads. */
  receiptUrl?: string | null;
  /** Détail de la facture (lignes décortiquées par le scan IA) — affiché dans le panneau. */
  details?: Array<{ label: string; amount: number; category: string }>;

  // ── Échéance d'acquisition (table acquisition_payments, sourceTable
  //    'acquisition_payment', id 'acq_<id>'). Les mensualités de crédit/leasing,
  //    l'apport et l'achat comptant viennent du serveur : elles ne sont plus
  //    recalculées à l'affichage. ──
  acquisitionPaymentId?: number;
  paymentKind?: 'apport' | 'mensualite' | 'achat';
  paymentStatus?: 'planned' | 'paid' | 'skipped';
  /** Date d'échéance (YYYY-MM-DD) — c'est aussi `date`, la date de la ligne. */
  dueDate?: string;
  paidAt?: string | null;
  paidAmount?: number | null;
  /** Montant prévu au contrat (totalAmount = paidAmount ?? plannedAmount). */
  plannedAmount?: number;
  note?: string | null;
  /** Règle serveur : compte en dépense si payée OU planifiée échue. */
  counted?: boolean;
  overdue?: boolean;
  /** Planifiée non échue — affichée seulement avec « Afficher les échéances à venir ». */
  isFuture?: boolean;
}

export interface RepairPart {
  partName: string;
  partReference?: string;
  quantity: number;
  unitPrice: number;
}

@Component({
  selector: 'app-expenses',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, AppLayoutComponent, AppCurrencyPipe],
  templateUrl: './expenses.component.html',
  styleUrls: ['./expenses.component.css']
})
export class ExpensesComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();

  // Data
  vehicles: any[] = [];
  expenses: Expense[] = [];
  filteredExpenses: Expense[] = [];
  fuelTypes: FuelTypeDto[] = [];
  fuelPrices: FuelPriceFullDto[] = [];
  maintenanceTemplates: MaintenanceTemplateDto[] = [];
  suppliers: any[] = [];
  partsCatalog: VehiclePartDto[] = [];
  partSuggestions: VehiclePartDto[] = [];
  activePartIndex: number = -1;

  // Filters
  searchQuery = '';
  filterVehicleId = '';
  filterCategory = '';
  filterMonth = '';
  /** Case « Afficher les échéances à venir » → GET /acquisition-payments?includeFuture=true. */
  showFuturePayments = false;

  // ── Échéances d'acquisition : menu ⋯, modale « Marquer payée », quittance ──
  /** id de la ligne dont le menu ⋯ est ouvert (une seule à la fois). */
  menuExpenseId: string | null = null;
  payModal = {
    open: false,
    expense: null as Expense | null,
    date: '',
    amount: 0,
    note: '',
    saving: false,
    error: ''
  };
  /** Ligne visée par le prochain fichier choisi dans l'input caché #receiptInput. */
  private receiptTarget: Expense | null = null;
  /** id d'échéance dont la quittance est en cours d'envoi (spinner / bouton grisé). */
  uploadingReceiptId: number | null = null;
  /** id d'échéance dont le statut est en cours de changement. */
  paymentBusyId: number | null = null;

  // Form state
  showAddForm = false;
  editingExpense: Expense | null = null;
  selectedCategory = '';

  // Generic form
  formVehicleId = '';
  formDate = new Date().toISOString().split('T')[0];
  formDescription = '';
  formAmount = 0;

  // Carburant form
  formFuelTypeId = '';
  formVolume = 0;
  formPricePerLiter = 0;
  formStation = '';
  formOdometerKm: number | null = null;

  // Entretien form
  formTemplateId = '';
  formMileageAtService = 0;
  formLaborCost = 0;
  formPartsCost = 0;
  formSupplierId = '';
  showCreateTemplate = false;
  newTemplateName = '';
  newTemplateCategory = 'vidange';

  // Réparation form
  formRepairReference = '';
  repairParts: RepairPart[] = [{ partName: '', partReference: '', quantity: 1, unitPrice: 0 }];

  // Assurance form
  insuranceProviders: any[] = [];
  vehicleContracts: any[] = [];
  formInsuranceProviderId = '';
  formContractId = '';
  showCreateInsurance = false;
  newInsuranceName = '';
  newInsurancePhone = '';

  // Delete
  showDeleteConfirm = false;
  expenseToDelete: Expense | null = null;

  // Detail slide-in
  showDetailPanel = false;
  detailExpense: Expense | null = null;
  private pendingExpenseId: string | null = null;

  loading = false;

  // ── Invoice scan (IA) — extract → review → save as a generic cost ──────────
  scanning = false;
  saving = false;
  showScanReview = false;
  scan: {
    vehicleId: string; category: string; date: string; amount: number;
    supplierName: string; invoiceNumber: string; description: string;
    vehiclePlate: string; confidence: string; receiptUrl: string;
    /** Lignes de la facture (détail extrait par l'IA, éditable). Enregistrées
     *  AVEC la dépense (details_json) — une facture reste UNE seule dépense,
     *  le détail décortiqué s'affiche dans le panneau de la dépense. */
    items: Array<{ label: string; amount: number; category: string }>;
  } = this.emptyScan();
  readonly scanCategories = [
    { value: 'fuel', label: 'Carburant' },
    { value: 'maintenance', label: 'Entretien' },
    { value: 'repair', label: 'Réparation' },
    { value: 'insurance', label: 'Assurance' },
    { value: 'tax', label: 'Vignette' },
    { value: 'toll', label: 'Péage' },
    { value: 'parking', label: 'Stationnement' },
    { value: 'fine', label: 'Amende' },
    { value: 'other', label: 'Autre' },
  ];

  get currencyCode(): string { return this.userPrefs.current.currency || 'TND'; }

  // Invoice scan is gated to a single pilot user during testing.
  get canScanInvoice(): boolean {
    return (this.authService.getCurrentUserSync()?.email || '').toLowerCase() === 'admin@belive.tn';
  }

  /** Quota mensuel de scans IA de la société (null tant que non chargé). */
  scanQuota: { used: number; limit: number; remaining: number } | null = null;

  private loadScanQuota(): void {
    if (!this.canScanInvoice) return;
    this.apiService.getScanQuota().pipe(takeUntil(this.destroy$)).subscribe({
      next: (q) => { this.scanQuota = q; this.cdr.detectChanges(); },
      error: () => { /* quota indisponible → bouton reste utilisable, le serveur tranche */ }
    });
  }

  constructor(private apiService: ApiService, private cdr: ChangeDetectorRef, private route: ActivatedRoute, private pdfService: PdfExportService, private userPrefs: UserPreferencesService, private authService: AuthService) {}

  ngOnInit(): void {
    // Check for expenseId query param (from notification click)
    const qp = this.route.snapshot.queryParams;
    if (qp['expenseId']) {
      this.pendingExpenseId = qp['expenseId'];
    }
    this.loadAllData();
    this.loadScanQuota();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private loadAllData(): void {
    this.loading = true;
    forkJoin({
      vehicles: this.apiService.getVehicles(),
      fuelTypes: this.apiService.getFuelTypes(),
      fuelPrices: this.apiService.getCurrentActiveFuelPrices(),
      templates: this.apiService.getMaintenanceTemplates({ isActive: true, pageSize: 100 }),
      suppliers: this.apiService.getSuppliers(),
      parts: this.apiService.getAllParts()
    }).pipe(takeUntil(this.destroy$)).subscribe({
      next: (result) => {
        this.vehicles = result.vehicles;
        this.fuelTypes = result.fuelTypes;
        this.fuelPrices = result.fuelPrices;
        this.maintenanceTemplates = result.templates.items || [];
        this.suppliers = result.suppliers?.items || [];
        this.insuranceProviders = this.suppliers.filter((s: any) => s.type === 'insurance');
        this.partsCatalog = result.parts || [];
        this.loadExpenses();
      },
      error: (err) => {
        console.error('Error loading data:', err);
        this.loading = false;
      }
    });
  }

  private loadExpenses(): void {
    forkJoin({
      costs: this.apiService.getCosts(),
      fuelEntries: this.apiService.getFuelEntries({ pageSize: 200 }),
      repairs: this.apiService.getRepairs({ pageSize: 200 }),
      // Échéances d'acquisition (mensualités, apport, achat comptant) lues en
      // base : sans includeFuture le serveur ne renvoie que ce qui compte ou a
      // compté en dépense (payées, planifiées échues, ignorées échues).
      // Un échec de cet appel ne doit pas vider les autres dépenses.
      acquisitionPayments: this.apiService.getAcquisitionPayments({ includeFuture: this.showFuturePayments }).pipe(
        catchError((err) => { console.error('Error loading acquisition payments:', err); return of([] as AcquisitionPaymentDto[]); })
      )
    }).pipe(takeUntil(this.destroy$)).subscribe({
      next: (result) => {
        const allExpenses: Expense[] = [];
        
        // VehicleCosts — including the auto-synced rows from accident
        // phases 5 & 6 (type='repair' / 'insurance_refund' carry an
        // accidentEventId for the badge and the link to the report).
        (result.costs || []).forEach((c: any) => {
          const isRefund = c.type === 'insurance_refund';
          // Détail de la facture (scan IA) — JSON défensif : une valeur
          // corrompue ne doit jamais casser la liste.
          let details: Expense['details'];
          if (c.detailsJson) {
            try {
              const parsed = JSON.parse(c.detailsJson);
              if (Array.isArray(parsed)) {
                details = parsed
                  .filter((it: any) => it && (it.label || it.amount))
                  .map((it: any) => ({
                    label: String(it.label || ''),
                    amount: Number(it.amount) || 0,
                    category: String(it.category || 'other')
                  }));
              }
            } catch { /* détail illisible → ignoré */ }
          }
          allExpenses.push({
            id: 'cost_' + c.id,
            vehicleId: c.vehicleId,
            vehiclePlate: c.vehiclePlate || '',
            vehicleName: c.vehicleName || '',
            category: c.type || 'autre',
            label: c.description || c.type,
            quantity: 1,
            unitPrice: c.amount,
            totalAmount: c.amount,
            date: new Date(c.date),
            description: c.description,
            createdAt: new Date(c.createdAt || c.date),
            sourceTable: 'costs',
            accidentEventId: c.accidentEventId ?? null,
            isRefund,
            receiptUrl: c.receiptUrl || null,
            details: details?.length ? details : undefined,
          });
        });

        // FuelEntries
        ((result.fuelEntries as any)?.items || []).forEach((f: any) => {
          allExpenses.push({
            id: 'fuel_' + f.id,
            vehicleId: f.vehicleId || 0,
            vehiclePlate: f.vehiclePlate || '',
            vehicleName: '',
            category: 'carburant',
            label: (f.fuelTypeName || 'Carburant') + ' - ' + (f.stationName || ''),
            quantity: f.volume,
            unitPrice: f.pricePerLiter,
            totalAmount: f.totalAmount,
            date: new Date(f.invoiceDate),
            description: f.notes,
            createdAt: new Date(f.createdAt || f.invoiceDate),
            sourceTable: 'fuelentries'
          });
        });

        // Repairs
        ((result.repairs as any)?.items || []).forEach((r: any) => {
          allExpenses.push({
            id: 'repair_' + r.id,
            vehicleId: r.vehicleId,
            vehiclePlate: r.vehiclePlate || '',
            vehicleName: r.vehicleName || '',
            category: 'reparation',
            label: r.description || r.reference,
            quantity: 1,
            unitPrice: r.totalCost,
            totalAmount: r.totalCost,
            date: new Date(r.repairDate),
            description: r.notes,
            createdAt: new Date(r.createdAt || r.repairDate),
            sourceTable: 'repairs'
          });
        });

        // Échéances d'acquisition (mensualités de crédit/leasing, apport, achat
        // comptant) — lignes de la table acquisition_payments, générées par le
        // serveur depuis le contrat du véhicule. La date de la ligne est la
        // date d'échéance (le filtre mois regroupe donc par mois de contrat) ;
        // le montant affiché est le montant réellement payé s'il est connu.
        // Recette client 26/08/2026 (« douze échéances payées, zéro dépense
        // affichée ») et 04/09/2026 (apport introuvable) : les deux univers
        // sont couverts par la même table.
        (result.acquisitionPayments || []).forEach((p) => {
          const due = new Date(String(p.dueDate).slice(0, 10) + 'T00:00:00');
          if (isNaN(due.getTime())) return;
          const isFuture = p.status === 'planned' && !p.counted;
          allExpenses.push({
            id: 'acq_' + p.id,
            vehicleId: p.vehicleId,
            vehiclePlate: p.vehiclePlate || '',
            vehicleName: p.vehicleName || '',
            category: p.kind === 'achat' ? 'achat' : 'credit',
            label: this.acquisitionLabel(p),
            quantity: 1,
            unitPrice: p.amount,
            totalAmount: p.paidAmount ?? p.amount,
            date: due,
            description: this.acquisitionDescription(p),
            createdAt: p.paidAt ? new Date(p.paidAt) : due,
            sourceTable: 'acquisition_payment',
            receiptUrl: p.receiptUrl || null,
            acquisitionPaymentId: p.id,
            paymentKind: p.kind,
            paymentStatus: p.status,
            dueDate: String(p.dueDate).slice(0, 10),
            paidAt: p.paidAt ?? null,
            paidAmount: p.paidAmount ?? null,
            plannedAmount: p.amount,
            note: p.note ?? null,
            counted: !!p.counted,
            overdue: !!p.overdue,
            isFuture
          });
        });

        this.expenses = allExpenses;
        this.filterExpenses();
        this.loading = false;

        // Panneau de détail ouvert pendant un rechargement (statut changé,
        // quittance jointe) : le rafraîchir avec la ligne à jour — ou le
        // fermer si la ligne a disparu (échéance ignorée → toujours listée ;
        // ligne supprimée → plus là).
        if (this.detailExpense) {
          const fresh = this.expenses.find(e => e.id === this.detailExpense!.id);
          if (fresh) this.detailExpense = fresh; else this.closeDetailPanel();
        }

        // Auto-open detail if navigated from notification
        if (this.pendingExpenseId) {
          const found = this.expenses.find(e => e.id === this.pendingExpenseId);
          if (found) {
            this.openDetailPanel(found);
          }
          this.pendingExpenseId = null;
        }

        this.cdr.detectChanges();
      },
      error: (err) => {
        console.error('Error loading expenses:', err);
        this.expenses = [];
        this.filterExpenses();
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  filterExpenses(): void {
    let result = [...this.expenses];
    if (this.searchQuery) {
      const q = this.searchQuery.toLowerCase();
      result = result.filter(e => e.label.toLowerCase().includes(q) || e.vehiclePlate.toLowerCase().includes(q));
    }
    if (this.filterVehicleId) result = result.filter(e => e.vehicleId === parseInt(this.filterVehicleId));
    if (this.filterCategory) {
      // « entretien » et « maintenance » sont un seul univers metier : le
      // tableau de bord les agrege sous « Entretien », mais la depense creee
      // par le module maintenance porte le code « maintenance » — filtrer sur
      // l un sans l autre affichait une page vide (recette du 28/08/2026).
      const wanted = this.filterCategory === 'entretien'
        ? ['entretien', 'maintenance']
        : [this.filterCategory];
      result = result.filter(e => wanted.includes(e.category));
    }
    if (this.filterMonth) {
      const [year, month] = this.filterMonth.split('-').map(Number);
      result = result.filter(e => { const d = new Date(e.date); return d.getFullYear() === year && d.getMonth() + 1 === month; });
    }
    result.sort((a, b) => new Date(b.date).getTime() - new Date(a.date).getTime());
    this.filteredExpenses = result;
  }

  openAddForm(): void {
    this.resetForm();
    this.showAddForm = true;
  }

  closeAddForm(): void {
    this.showAddForm = false;
    this.editingExpense = null;
  }

  resetForm(): void {
    this.selectedCategory = '';
    this.formVehicleId = '';
    this.formDate = new Date().toISOString().split('T')[0];
    this.formDescription = '';
    this.formAmount = 0;
    this.formFuelTypeId = '';
    this.formVolume = 0;
    this.formPricePerLiter = 0;
    this.formStation = '';
    this.formOdometerKm = null;
    this.formTemplateId = '';
    this.formMileageAtService = 0;
    this.formLaborCost = 0;
    this.formPartsCost = 0;
    this.formSupplierId = '';
    this.formRepairReference = '';
    this.repairParts = [{ partName: '', partReference: '', quantity: 1, unitPrice: 0 }];
    this.showCreateTemplate = false;
    this.editingExpense = null;
  }

  onCategoryChange(): void {
    if (this.selectedCategory === 'carburant' && this.fuelTypes.length > 0) {
      this.formFuelTypeId = this.fuelTypes[0].id.toString();
      this.updatePriceFromFuelType();
    }
    if (this.selectedCategory === 'insurance' && this.formVehicleId) {
      this.loadVehicleContracts();
    }
  }

  loadVehicleContracts(): void {
    if (!this.formVehicleId) return;
    this.apiService.getContracts({ vehicleId: parseInt(this.formVehicleId), type: 'insurance' }).subscribe({
      next: (result: any) => this.vehicleContracts = result.items || result || [],
      error: () => this.vehicleContracts = []
    });
  }

  createInsuranceProvider(): void {
    if (!this.newInsuranceName.trim()) return;
    this.apiService.createSupplier({
      name: this.newInsuranceName,
      type: 'insurance',
      phone: this.newInsurancePhone,
      address: '',
      city: '',
      postalCode: '',
      email: '',
      contactName: '',
      isActive: true
    }).subscribe({
      next: (newProvider: any) => {
        this.insuranceProviders.push(newProvider);
        this.formInsuranceProviderId = newProvider.id?.toString() || '';
        this.showCreateInsurance = false;
        this.newInsuranceName = '';
        this.newInsurancePhone = '';
      },
      error: (err) => console.error('Error creating insurance provider:', err)
    });
  }

  onFuelTypeChange(): void {
    this.updatePriceFromFuelType();
  }

  updatePriceFromFuelType(): void {
    const price = this.fuelPrices.find(p => p.fuelTypeId === parseInt(this.formFuelTypeId));
    if (price) this.formPricePerLiter = price.pricePerLiter;
  }

  calculateFuelTotal(): number {
    return this.formVolume * this.formPricePerLiter;
  }

  calculateMaintenanceTotal(): number {
    return this.formLaborCost + this.formPartsCost;
  }

  calculateRepairTotal(): number {
    const partsTotal = this.repairParts.reduce((sum, p) => sum + (p.quantity * p.unitPrice), 0);
    return this.formLaborCost + partsTotal;
  }

  addRepairPart(): void {
    this.repairParts.push({ partName: '', partReference: '', quantity: 1, unitPrice: 0 });
  }

  removeRepairPart(index: number): void {
    if (this.repairParts.length > 1) this.repairParts.splice(index, 1);
  }

  onPartNameInput(index: number): void {
    this.activePartIndex = index;
    const query = this.repairParts[index].partName.toLowerCase().trim();
    if (query.length < 2) {
      this.partSuggestions = [];
      return;
    }
    this.partSuggestions = this.partsCatalog.filter(p => 
      p.name.toLowerCase().includes(query)
    ).slice(0, 8);
  }

  selectPartSuggestion(part: VehiclePartDto): void {
    if (this.activePartIndex >= 0 && this.activePartIndex < this.repairParts.length) {
      this.repairParts[this.activePartIndex].partName = part.name;
      this.repairParts[this.activePartIndex].partReference = part.partNumber || '';
    }
    this.partSuggestions = [];
    this.activePartIndex = -1;
  }

  hidePartSuggestions(): void {
    setTimeout(() => {
      this.partSuggestions = [];
      this.activePartIndex = -1;
    }, 200);
  }

  private saveNewPartToCatalog(partName: string): void {
    if (!partName.trim()) return;
    const exists = this.partsCatalog.some(p => p.name.toLowerCase() === partName.toLowerCase());
    if (!exists) {
      this.apiService.createPart({ categoryId: 1, name: partName.trim() }).subscribe({
        next: (newPart) => this.partsCatalog.push(newPart),
        error: (err) => console.error('Error saving part to catalog:', err)
      });
    }
  }

  isFormValid(): boolean {
    if (!this.formVehicleId || !this.selectedCategory || !this.formDate) return false;
    switch (this.selectedCategory) {
      case 'carburant': return this.formFuelTypeId !== '' && this.formVolume > 0 && this.formPricePerLiter > 0;
      case 'entretien': return (this.formTemplateId !== '' || this.showCreateTemplate) && this.calculateMaintenanceTotal() > 0;
      case 'reparation': return this.repairParts.some(p => p.partName && p.unitPrice > 0) || this.formLaborCost > 0;
      default: return this.formAmount > 0;
    }
  }

  saveExpense(): void {
    if (!this.isFormValid()) return;
    switch (this.selectedCategory) {
      case 'carburant': this.saveFuelEntry(); break;
      case 'entretien': this.saveMaintenanceRecord(); break;
      case 'reparation': this.saveRepair(); break;
      default: this.saveCost(); break;
    }
  }

  private saveFuelEntry(): void {
    const vehicle = this.vehicles.find(v => v.id === parseInt(this.formVehicleId));
    const data = {
      vehicleId: parseInt(this.formVehicleId),
      vehiclePlate: vehicle?.plate || '',
      fuelTypeId: parseInt(this.formFuelTypeId),
      volume: this.formVolume,
      pricePerLiter: this.formPricePerLiter,
      totalAmount: this.calculateFuelTotal(),
      invoiceDate: this.formDate,
      stationName: this.formStation,
      odometerKm: this.formOdometerKm || undefined,
      notes: this.formDescription
    };
    this.apiService.createFuelEntry(data).subscribe({
      next: () => { this.loadExpenses(); this.closeAddForm(); },
      error: (err) => console.error('Error creating fuel entry:', err)
    });
  }

  private saveMaintenanceRecord(): void {
    if (this.showCreateTemplate && this.newTemplateName) {
      this.apiService.createMaintenanceTemplate({
        name: this.newTemplateName,
        category: this.newTemplateCategory,
        description: '',
        intervalKm: 10000,
        intervalMonths: 12,
        estimatedCost: this.calculateMaintenanceTotal(),
        isActive: true
      }).subscribe({
        next: (id) => { this.formTemplateId = id.toString(); this.createMaintenanceFromTemplate(); },
        error: (err) => console.error('Error creating template:', err)
      });
    } else {
      this.createMaintenanceFromTemplate();
    }
  }

  private createMaintenanceFromTemplate(): void {
    const data = {
      vehicleId: parseInt(this.formVehicleId),
      type: 'scheduled',
      description: this.formDescription || this.maintenanceTemplates.find(t => t.id === parseInt(this.formTemplateId))?.name || 'Entretien',
      mileageAtService: this.formMileageAtService,
      date: this.formDate,
      status: 'completed',
      laborCost: this.formLaborCost,
      partsCost: this.formPartsCost,
      totalCost: this.calculateMaintenanceTotal(),
      supplierId: this.formSupplierId ? parseInt(this.formSupplierId) : null
    };
    this.apiService.createMaintenanceRecord(data).subscribe({
      next: () => { this.loadExpenses(); this.closeAddForm(); },
      error: (err) => console.error('Error creating maintenance:', err)
    });
  }

  private saveRepair(): void {
    const parts = this.repairParts.filter(p => p.partName && p.unitPrice > 0).map(p => ({
      partName: p.partName,
      partReference: p.partReference || undefined,
      quantity: p.quantity,
      unitPrice: p.unitPrice
    }));
    // Save new parts to catalog for future reuse
    parts.forEach(p => this.saveNewPartToCatalog(p.partName));
    
    const data = {
      vehicleId: parseInt(this.formVehicleId),
      supplierId: this.formSupplierId ? parseInt(this.formSupplierId) : undefined,
      description: this.formDescription,
      repairDate: this.formDate,
      mileageAtRepair: this.formMileageAtService || undefined,
      laborCost: this.formLaborCost,
      invoiceNumber: this.formRepairReference || undefined,
      notes: undefined,
      parts: parts
    };
    this.apiService.createRepair(data).subscribe({
      next: () => { this.loadExpenses(); this.closeAddForm(); },
      error: (err) => console.error('Error creating repair:', err)
    });
  }

  private saveCost(): void {
    const data = {
      vehicleId: parseInt(this.formVehicleId),
      type: this.selectedCategory,
      description: this.formDescription,
      amount: this.formAmount,
      date: new Date(this.formDate).toISOString(),
      mileage: null,
      receiptNumber: null
    };
    this.apiService.createCost(data).subscribe({
      next: () => { this.loadExpenses(); this.closeAddForm(); },
      error: (err) => console.error('Error creating cost:', err)
    });
  }

  // ── Invoice scan (IA) ──────────────────────────────────────────────────────
  private emptyScan() {
    return {
      vehicleId: '', category: 'other', date: new Date().toISOString().split('T')[0],
      amount: 0, supplierName: '', invoiceNumber: '', description: '',
      vehiclePlate: '', confidence: '', receiptUrl: '',
      items: [] as Array<{ label: string; amount: number; category: string }>
    };
  }

  /** Prépare la photo avant upload : rotation EXIF appliquée, côté max 2000 px,
   *  ré-encodage JPEG qualité 0,85. Une photo de téléphone (4000×3000, ~6 Mo)
   *  devient ~500 Ko — upload bien plus rapide et lecture IA plus fiable.
   *  Les PDF et les petites images passent tels quels ; en cas d'échec de
   *  décodage (navigateur ancien, format exotique) on renvoie l'original. */
  private async prepareInvoiceImage(file: File): Promise<File> {
    if (!file.type.startsWith('image/') || file.type === 'image/gif') return file;
    const MAX_SIDE = 2000;
    try {
      const bmp = await createImageBitmap(file, { imageOrientation: 'from-image' } as ImageBitmapOptions);
      const scale = Math.min(1, MAX_SIDE / Math.max(bmp.width, bmp.height));
      if (scale === 1 && file.size < 1_500_000) { bmp.close(); return file; }
      const w = Math.max(1, Math.round(bmp.width * scale));
      const h = Math.max(1, Math.round(bmp.height * scale));
      const canvas = document.createElement('canvas');
      canvas.width = w; canvas.height = h;
      const ctx = canvas.getContext('2d');
      if (!ctx) { bmp.close(); return file; }
      ctx.drawImage(bmp, 0, 0, w, h);
      bmp.close();
      const blob = await new Promise<Blob | null>(res => canvas.toBlob(res, 'image/jpeg', 0.85));
      if (!blob || blob.size >= file.size) return file;   // pas de gain → original
      return new File([blob], file.name.replace(/\.[^.]+$/, '') + '.jpg', { type: 'image/jpeg' });
    } catch {
      return file;
    }
  }

  async onInvoiceFile(event: any): Promise<void> {
    const file: File | undefined = event?.target?.files?.[0];
    if (event?.target) event.target.value = '';       // allow re-selecting the same file
    if (!file) return;
    this.scanning = true;
    this.cdr.detectChanges();
    const prepared = await this.prepareInvoiceImage(file);
    this.apiService.scanInvoice(prepared).subscribe({
      next: (res: any) => {
        this.scanning = false;
        if (res?.quota) this.scanQuota = res.quota;   // compteur mis à jour par le serveur
        const x = res?.extraction || {};
        const veh = this.matchVehicleByPlate(x.vehiclePlate);
        const desc = [x.supplierName, x.description].filter((s: string) => !!s).join(' — ');
        const items = (Array.isArray(x.items) ? x.items : [])
          .slice(0, 30)
          .map((it: any) => ({
            label: it?.label || '',
            amount: typeof it?.amount === 'number' ? it.amount : 0,
            category: it?.category || x.category || 'other'
          }))
          .filter((it: any) => it.label || it.amount);
        this.scan = {
          vehicleId: veh ? String(veh.id) : '',
          category: x.category || 'other',
          date: x.date || new Date().toISOString().split('T')[0],
          amount: x.amountTTC ?? x.amountHT ?? 0,
          supplierName: x.supplierName || '',
          invoiceNumber: x.invoiceNumber || '',
          description: desc,
          vehiclePlate: x.vehiclePlate || '',
          confidence: x.confidence || '',
          receiptUrl: res?.receiptUrl || '',
          items
        };
        this.showScanReview = true;
        this.cdr.detectChanges();
      },
      error: (err: any) => {
        this.scanning = false;
        const receiptUrl = err?.error?.receiptUrl || '';
        // 413 = rejected by the proxy for size before reaching the API — the
        // generic message would mislead the user into retrying the same file.
        const msg = err?.status === 413
          ? 'Fichier trop volumineux (maximum 12 Mo). Réduisez la taille ou envoyez une photo compressée.'
          : (err?.error?.message || "L'analyse de la facture a échoué. Vous pouvez saisir la dépense manuellement.");
        alert(msg);
        // The file may still be stored — let the user fill the fields by hand.
        if (receiptUrl) {
          this.scan = { ...this.emptyScan(), confidence: 'low', receiptUrl };
          this.showScanReview = true;
          this.cdr.detectChanges();
        }
      }
    });
  }

  private matchVehicleByPlate(plate?: string): any {
    if (!plate) return null;
    const norm = (s: string) => (s || '').toLowerCase().replace(/[^a-z0-9]/g, '');
    const target = norm(plate);
    if (!target) return null;
    return this.vehicles.find(v => norm(v.plate) === target)
        || this.vehicles.find(v => norm(v.plate) && (norm(v.plate).includes(target) || target.includes(norm(v.plate))))
        || null;
  }

  closeScanReview(): void { this.showScanReview = false; }

  isPdf(url: string): boolean { return (url || '').toLowerCase().endsWith('.pdf'); }

  scanConfidenceLabel(): string {
    return ({ high: 'élevée', medium: 'moyenne', low: 'faible' } as Record<string, string>)[this.scan.confidence] || this.scan.confidence;
  }

  // ── Détail de la facture (lignes) ──────────────────────────────────────────
  scanItemsSum(): number {
    return this.scan.items.reduce((s, it) => s + (Number(it.amount) || 0), 0);
  }

  /** Somme des lignes ≠ total facture (tolérance 0,01) → avertissement visuel. */
  scanSumMismatch(): boolean {
    if (!this.scan.items.length || !this.scan.amount) return false;
    return Math.abs(this.scanItemsSum() - Number(this.scan.amount)) > 0.01;
  }

  addScanItem(): void {
    this.scan.items.push({ label: '', amount: 0, category: this.scan.category || 'other' });
  }

  removeScanItem(i: number): void { this.scan.items.splice(i, 1); }

  canSaveScan(): boolean {
    return !!this.scan.vehicleId && !!this.scan.amount && !this.saving;
  }

  /** Libellé FR d'une catégorie de ligne (les lignes utilisent les catégories du scan). */
  lineCategoryLabel(cat: string): string {
    return this.scanCategories.find(c => c.value === cat)?.label || this.getCategoryLabel(cat);
  }

  /** Somme des lignes décortiquées d'une dépense (panneau de détail). */
  detailsSum(e: Expense): number {
    return (e.details || []).reduce((s, it) => s + (Number(it.amount) || 0), 0);
  }

  saveScanned(): void {
    if (!this.canSaveScan()) return;
    this.saving = true;

    // Une facture = UNE dépense. Le détail (lignes) part avec elle en JSON et
    // s'affiche dans le panneau de la dépense — pas de lignes multiples dans
    // la liste.
    const cleanItems = this.scan.items
      .filter(it => (it.label || '').trim() || Number(it.amount) > 0)
      .map(it => ({ label: (it.label || '').trim(), amount: Number(it.amount) || 0, category: it.category || 'other' }));

    const data = {
      vehicleId: parseInt(this.scan.vehicleId),
      type: this.scan.category || 'other',
      description: this.scan.description || this.scan.supplierName || null,
      amount: Number(this.scan.amount),
      date: new Date(this.scan.date).toISOString(),
      mileage: null,
      receiptNumber: this.scan.invoiceNumber || null,
      receiptUrl: this.scan.receiptUrl || null,
      detailsJson: cleanItems.length ? JSON.stringify(cleanItems) : null
    };
    this.apiService.createCost(data).subscribe({
      next: () => { this.saving = false; this.showScanReview = false; this.loadExpenses(); },
      error: (err) => { this.saving = false; console.error('Error saving scanned cost:', err); alert("L'enregistrement a échoué."); }
    });
  }

  confirmDeleteExpense(expense: Expense): void {
    // Une échéance d'acquisition ne se supprime pas : elle s'ignore (menu ⋯)
    // et c'est le contrat du véhicule qui la fait exister ou disparaître.
    if (this.isAcquisition(expense)) return;
    this.expenseToDelete = expense;
    this.showDeleteConfirm = true;
  }

  cancelDelete(): void {
    this.showDeleteConfirm = false;
    this.expenseToDelete = null;
  }

  deleteExpense(): void {
    if (!this.expenseToDelete) { this.cancelDelete(); return; }
    // Garde explicite : l'ancien découpage 'leasing_12_3' → table 'leasing'
    // tombait dans la branche par défaut → deleteCost(12), c'est-à-dire la
    // suppression d'un vehicle_costs sans rapport. Seuls les préfixes connus
    // suppriment ; 'acq_' (et tout inconnu) ne fait rien.
    if (this.isAcquisition(this.expenseToDelete)) { this.cancelDelete(); return; }
    const [table, id] = this.expenseToDelete.id.split('_');
    const numericId = parseInt(id);
    if (isNaN(numericId)) { this.cancelDelete(); return; }
    let obs;
    switch (table) {
      case 'fuel': obs = this.apiService.deleteFuelEntry(numericId); break;
      case 'repair': obs = this.apiService.deleteRepair(numericId); break;
      case 'cost': obs = this.apiService.deleteCost(numericId); break;
      default:
        console.warn('deleteExpense: préfixe inconnu, suppression refusée —', this.expenseToDelete.id);
        this.cancelDelete();
        return;
    }
    obs.subscribe({
      next: () => { this.loadExpenses(); this.cancelDelete(); },
      error: (err) => { console.error('Error deleting:', err); this.cancelDelete(); }
    });
  }

  openDetailPanel(expense: Expense): void {
    this.closeRowMenu();
    this.detailExpense = expense;
    this.showDetailPanel = true;
  }

  closeDetailPanel(): void {
    this.showDetailPanel = false;
    this.detailExpense = null;
  }

  // ── Échéances d'acquisition ────────────────────────────────────────────────

  isAcquisition(e: Expense | null | undefined): boolean {
    return e?.sourceTable === 'acquisition_payment';
  }

  /**
   * Une ligne entre dans les totaux (barre de stats, export PDF) si c'est une
   * dépense ordinaire, ou une échéance que le serveur dit « counted » (payée,
   * ou planifiée et échue). Les échéances ignorées et à venir sont exclues.
   */
  countsInTotals(e: Expense): boolean {
    return !this.isAcquisition(e) || !!e.counted;
  }

  /** Lignes filtrées qui comptent dans les totaux. */
  private countedExpenses(): Expense[] {
    return this.filteredExpenses.filter(e => this.countsInTotals(e));
  }

  private acquisitionLabel(p: AcquisitionPaymentDto): string {
    switch (p.kind) {
      case 'mensualite': return `Mensualité crédit/leasing ${p.seq}/${p.total || p.seq}`;
      case 'apport': return 'Apport crédit/leasing';
      default: return 'Achat véhicule';
    }
  }

  private acquisitionDescription(p: AcquisitionPaymentDto): string {
    switch (p.kind) {
      case 'mensualite': return 'Échéance générée depuis le contrat du véhicule';
      case 'apport': return 'Apport initial du contrat de financement';
      default: return 'Prix d\'achat du véhicule';
    }
  }

  /** JJ/MM (badge de ligne) ou JJ/MM/AAAA (panneau, PDF) d'une date ISO ou YYYY-MM-DD. */
  private shortDate(value: string | null | undefined, withYear = false): string {
    if (!value) return '';
    const d = value.length === 10 ? new Date(value + 'T00:00:00') : new Date(value);
    if (isNaN(d.getTime())) return '';
    return d.toLocaleDateString('fr-FR', withYear
      ? { day: '2-digit', month: '2-digit', year: 'numeric' }
      : { day: '2-digit', month: '2-digit' });
  }

  /**
   * Badge de statut d'une échéance : « Payée (auto) » (planifiée échue —
   * présomption calendaire, comptée), « Payée le JJ/MM » (paiement confirmé),
   * « À venir » (planifiée non échue, hors totaux), « Ignorée » (hors totaux).
   */
  paymentBadge(e: Expense, withYear = false): { cls: string; text: string } | null {
    if (!this.isAcquisition(e)) return null;
    switch (e.paymentStatus) {
      case 'paid': {
        const when = this.shortDate(e.paidAt, withYear);
        return { cls: 'paid', text: when ? `Payée le ${when}` : 'Payée' };
      }
      case 'skipped': return { cls: 'skipped', text: 'Ignorée' };
      default: return e.counted ? { cls: 'auto', text: 'Payée (auto)' } : { cls: 'upcoming', text: 'À venir' };
    }
  }

  /** Libellé complet du statut (panneau de détail, export PDF). */
  paymentStatusText(e: Expense): string {
    return this.paymentBadge(e, true)?.text || '';
  }

  onToggleFuturePayments(): void {
    this.closeRowMenu();
    this.loading = true;
    this.loadExpenses();
  }

  toggleRowMenu(e: Expense, event: Event): void {
    event.stopPropagation();
    this.menuExpenseId = this.menuExpenseId === e.id ? null : e.id;
  }

  closeRowMenu(): void {
    this.menuExpenseId = null;
  }

  /** Un clic n'importe où ailleurs referme le menu ⋯ (le bouton stoppe la propagation). */
  @HostListener('document:click')
  onDocumentClick(): void {
    if (this.menuExpenseId) this.menuExpenseId = null;
  }

  /** Jour LOCAL au format YYYY-MM-DD : toISOString() donnerait la veille entre minuit et 1 h en TN/DZ. */
  private todayLocal(): string {
    const d = new Date();
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  }

  openMarkPaid(e: Expense): void {
    if (!this.isAcquisition(e) || !e.acquisitionPaymentId) return;
    this.closeRowMenu();
    const today = this.todayLocal();
    this.payModal = {
      open: true,
      expense: e,
      // Défaut : la date d'échéance (le cas courant est « payée à la date prévue »),
      // sans jamais proposer une date future.
      date: e.dueDate && e.dueDate <= today ? e.dueDate : today,
      amount: Number(e.paidAmount ?? e.plannedAmount ?? e.totalAmount) || 0,
      note: e.note || '',
      saving: false,
      error: ''
    };
  }

  closeMarkPaid(): void {
    if (this.payModal.saving) return;
    this.payModal = { ...this.payModal, open: false, expense: null, error: '' };
  }

  /**
   * Un champ « Montant payé » vidé met `amount` à null, et `Number(null)` vaut 0 :
   * sans ce contrôle, on enregistrerait un paiement de 0 et l'échéance sortirait
   * du coût total. Le montant doit être réellement saisi.
   */
  hasPayAmount(): boolean {
    const a = this.payModal.amount as unknown;
    if (a === null || a === undefined || String(a).trim() === '') return false;
    const n = Number(a);
    return !isNaN(n) && n >= 0;
  }

  canSaveMarkPaid(): boolean {
    return !!this.payModal.expense && !!this.payModal.date && this.hasPayAmount() && !this.payModal.saving;
  }

  /** Montant saisi ≠ montant prévu au contrat (tolérance 0,001) → rappel visuel. */
  payAmountDiffers(): boolean {
    const planned = this.payModal.expense?.plannedAmount;
    if (planned == null) return false;
    return Math.abs(Number(this.payModal.amount) - planned) > 0.001;
  }

  saveMarkPaid(): void {
    if (!this.canSaveMarkPaid()) return;
    const e = this.payModal.expense!;
    if (!e.acquisitionPaymentId) return;
    this.payModal.saving = true;
    this.payModal.error = '';
    // Midi local → la date calendaire choisie survit à la conversion UTC
    // (minuit local basculerait la veille pour tout fuseau à l'est de Greenwich).
    const paidAt = new Date(this.payModal.date + 'T12:00:00').toISOString();
    const note = (this.payModal.note || '').trim();
    this.apiService.updateAcquisitionPayment(e.acquisitionPaymentId, {
      status: 'paid',
      paidAt,
      // Montant omis si rien n'est saisi : le serveur retombe alors sur le
      // montant prévu au contrat, au lieu d'enregistrer un paiement de 0.
      ...(this.hasPayAmount() ? { paidAmount: Number(this.payModal.amount) } : {}),
      // Chaîne vide = note effacée (null signifierait « ne pas y toucher »).
      note: note.slice(0, 500)
    }).pipe(takeUntil(this.destroy$)).subscribe({
      next: () => {
        this.payModal = { ...this.payModal, open: false, expense: null, saving: false };
        this.loadExpenses();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.payModal.saving = false;
        this.payModal.error = err?.error?.message || "L'enregistrement du paiement a échoué. Réessayez.";
        this.cdr.detectChanges();
      }
    });
  }

  /** Ignorer (« skipped ») ou rétablir (« planned ») une échéance — réversible, pas de confirmation. */
  setPaymentStatus(e: Expense, status: 'planned' | 'skipped'): void {
    if (!this.isAcquisition(e) || !e.acquisitionPaymentId || this.paymentBusyId) return;
    this.closeRowMenu();
    this.paymentBusyId = e.acquisitionPaymentId;
    this.apiService.updateAcquisitionPayment(e.acquisitionPaymentId, { status })
      .pipe(takeUntil(this.destroy$)).subscribe({
        next: () => {
          this.paymentBusyId = null;
          this.loadExpenses();
          this.cdr.detectChanges();
        },
        error: (err) => {
          this.paymentBusyId = null;
          console.error('Error updating acquisition payment:', err);
          alert(err?.error?.message || 'La mise à jour de l\'échéance a échoué.');
          this.cdr.detectChanges();
        }
      });
  }

  /** Mémorise la ligne visée ; l'appelant déclenche ensuite `receiptInput.click()`. */
  startReceiptUpload(e: Expense): void {
    this.receiptTarget = this.isAcquisition(e) ? e : null;
    this.closeRowMenu();
  }

  async onReceiptFile(event: any): Promise<void> {
    const file: File | undefined = event?.target?.files?.[0];
    if (event?.target) event.target.value = '';       // permet de rechoisir le même fichier
    const target = this.receiptTarget;
    this.receiptTarget = null;
    if (!file || !target?.acquisitionPaymentId) return;
    const id = target.acquisitionPaymentId;
    this.uploadingReceiptId = id;
    this.cdr.detectChanges();
    // Même préparation que le scan de facture : rotation EXIF, 2000 px max,
    // JPEG 0,85 — une photo de téléphone passe de ~6 Mo à ~500 Ko.
    const prepared = await this.prepareInvoiceImage(file);
    this.apiService.uploadAcquisitionPaymentReceipt(id, prepared).pipe(takeUntil(this.destroy$)).subscribe({
      next: (res) => {
        this.uploadingReceiptId = null;
        // Mise à jour immédiate de la ligne (et du panneau s'il est ouvert dessus)
        // avant le rechargement complet.
        const row = this.expenses.find(x => x.acquisitionPaymentId === id);
        if (row) row.receiptUrl = res?.receiptUrl || row.receiptUrl;
        if (this.detailExpense?.acquisitionPaymentId === id) this.detailExpense.receiptUrl = res?.receiptUrl || this.detailExpense.receiptUrl;
        this.loadExpenses();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.uploadingReceiptId = null;
        const msg = err?.status === 413
          ? 'Fichier trop volumineux (maximum 12 Mo). Réduisez la taille ou envoyez une photo compressée.'
          : (err?.error?.message || "L'envoi de la quittance a échoué.");
        alert(msg);
        this.cdr.detectChanges();
      }
    });
  }

  getTotalAmount(): number { return this.countedExpenses().reduce((sum, e) => sum + e.totalAmount, 0); }
  /** Nombre de lignes réellement comptées (même base que le Total et que l'export). */
  getCountedCount(): number { return this.countedExpenses().length; }
  /** Lignes affichées mais non comptées (échéances à venir ou ignorées). */
  getNotCountedCount(): number { return this.filteredExpenses.length - this.getCountedCount(); }
  getUniqueVehiclesCount(): number { return new Set(this.countedExpenses().map(e => e.vehicleId)).size; }
  getAverageAmount(): number { const c = this.getUniqueVehiclesCount(); return c > 0 ? this.getTotalAmount() / c : 0; }

  getCategoryIcon(category: string): string {
    const icons: Record<string, string> = { 'carburant': '', 'entretien': '', 'reparation': '', 'insurance': '', 'assurance': '', 'peage': '', 'stationnement': '', 'amende': '', 'autre': '' };
    return icons[category] || '';
  }

  getCategoryLabel(category: string): string {
    // Labels are aligned with RenewDocumentCommandHandler.GetDocumentTypeLabel
    // on the backend so the chip / filter / detail panel always show the
    // same wording as the rest of the app (notif bell, document renewal
    // popup, etc.).
    const labels: Record<string, string> = {
      'carburant': 'Carburant', 'fuel': 'Carburant',
      'entretien': 'Entretien', 'maintenance': 'Entretien',
      'reparation': 'Réparation',
      'insurance': 'Assurance', 'assurance': 'Assurance',
      'technical_inspection': 'Visite technique',
      'tax': 'Vignette',
      'registration': 'Carte grise',
      'transport_permit': 'Autorisation transport',
      'peage': 'Péage', 'toll': 'Péage',
      'stationnement': 'Stationnement', 'parking': 'Parking',
      'amende': 'Amende',
      'credit': 'Crédit / Leasing',
      'achat': 'Achat véhicule',
      'autre': 'Autre',
      // Calypso 7 — accident-driven categories.
      'repair': 'Réparation accident',
      'insurance_refund': 'Remb. assurance',
    };
    return labels[category] || category;
  }

  /**
   * Calypso 7 — net total = expenses minus insurance refunds. Surfaced
   * next to the gross total so the admin sees their actual out-of-pocket
   * cost after insurance settlements.
   */
  getNetTotal(): number {
    return this.countedExpenses().reduce((sum, e) => sum + (e.isRefund ? -e.totalAmount : e.totalAmount), 0);
  }

  /** Total of insurance refunds in the filtered window (positive number, for display). */
  getRefundTotal(): number {
    return this.countedExpenses().filter(e => e.isRefund).reduce((sum, e) => sum + e.totalAmount, 0);
  }

  exportPdf(): void {
    // Group filtered expenses by category — only rows that count (acquisition
    // payments that are skipped or not yet due are left out, like the totals).
    const exported = this.countedExpenses();
    const byCategory = new Map<string, Expense[]>();
    for (const exp of exported) {
      const cat = exp.category || 'autre';
      if (!byCategory.has(cat)) byCategory.set(cat, []);
      byCategory.get(cat)!.push(exp);
    }

    const groups: PdfGroup[] = [];
    byCategory.forEach((expenses, cat) => {
      const total = expenses.reduce((s, e) => s + e.totalAmount, 0);
      groups.push({
        groupLabel: this.getCategoryLabel(cat),
        groupSubtitle: `${expenses.length} entree(s)`,
        rows: expenses.map(e => {
          const status = this.isAcquisition(e) ? this.paymentStatusText(e) : '';
          return {
            date: e.date ? new Date(e.date).toLocaleDateString('fr-FR') : '-',
            vehicleName: `${e.vehicleName} (${e.vehiclePlate})`,
            label: (e.label || e.description || '-') + (status ? ` — ${status}` : ''),
            amount: this.userPrefs.formatCurrency(e.totalAmount)
          };
        }),
        subtotal: `${expenses.length} entrees - ${this.userPrefs.formatCurrency(total)}`
      });
    });

    const config: GroupedPdfReportConfig = {
      title: 'Depenses par categories',
      subtitle: 'Rapport de depenses',
      dateRange: this.filterMonth ? `Mois: ${this.filterMonth}` : 'Toutes periodes',
      statistics: {
        'Total': this.userPrefs.formatCurrency(this.getTotalAmount()),
        'Entrees': '' + exported.length,
        'Vehicules': '' + new Set(exported.map(e => e.vehicleId)).size
      },
      columns: [
        { header: 'Date', dataKey: 'date' },
        { header: 'Vehicule', dataKey: 'vehicleName' },
        { header: 'Description', dataKey: 'label' },
        { header: 'Montant', dataKey: 'amount' }
      ],
      groups,
      grandTotal: `Total general: ${this.userPrefs.formatCurrency(this.getTotalAmount())}`
    };

    this.pdfService.exportGroupedReport(config);
  }
}
