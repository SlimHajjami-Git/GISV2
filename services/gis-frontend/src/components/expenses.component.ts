import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subject, takeUntil, forkJoin } from 'rxjs';
import { ApiService, FuelTypeDto, FuelPriceFullDto, MaintenanceTemplateDto, VehiclePartDto } from '../services/api.service';
import { PdfExportService, PdfGroup, GroupedPdfReportConfig } from '../services/pdf-export.service';
import { AppLayoutComponent } from './shared/app-layout.component';
import { AppCurrencyPipe } from '../pipes/user-preference-pipes';
import { UserPreferencesService } from '../services/user-preferences.service';

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

  constructor(private apiService: ApiService, private cdr: ChangeDetectorRef, private route: ActivatedRoute, private pdfService: PdfExportService, private userPrefs: UserPreferencesService) {}

  ngOnInit(): void {
    // Check for expenseId query param (from notification click)
    const qp = this.route.snapshot.queryParams;
    if (qp['expenseId']) {
      this.pendingExpenseId = qp['expenseId'];
    }
    this.loadAllData();
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
      repairs: this.apiService.getRepairs({ pageSize: 200 })
    }).pipe(takeUntil(this.destroy$)).subscribe({
      next: (result) => {
        const allExpenses: Expense[] = [];
        
        // VehicleCosts — including the auto-synced rows from accident
        // phases 5 & 6 (type='repair' / 'insurance_refund' carry an
        // accidentEventId for the badge and the link to the report).
        (result.costs || []).forEach((c: any) => {
          const isRefund = c.type === 'insurance_refund';
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

        this.expenses = allExpenses;
        this.filterExpenses();
        this.loading = false;

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
    if (this.filterCategory) result = result.filter(e => e.category === this.filterCategory);
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
      vehiclePlate: '', confidence: '', receiptUrl: ''
    };
  }

  onInvoiceFile(event: any): void {
    const file: File | undefined = event?.target?.files?.[0];
    if (event?.target) event.target.value = '';       // allow re-selecting the same file
    if (!file) return;
    this.scanning = true;
    this.apiService.scanInvoice(file).subscribe({
      next: (res: any) => {
        this.scanning = false;
        const x = res?.extraction || {};
        const veh = this.matchVehicleByPlate(x.vehiclePlate);
        const desc = [x.supplierName, x.description].filter((s: string) => !!s).join(' — ');
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
          receiptUrl: res?.receiptUrl || ''
        };
        this.showScanReview = true;
        this.cdr.detectChanges();
      },
      error: (err: any) => {
        this.scanning = false;
        const receiptUrl = err?.error?.receiptUrl || '';
        alert(err?.error?.message || "L'analyse de la facture a échoué. Vous pouvez saisir la dépense manuellement.");
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

  saveScanned(): void {
    if (!this.scan.vehicleId || !this.scan.amount || this.saving) return;
    this.saving = true;
    const data = {
      vehicleId: parseInt(this.scan.vehicleId),
      type: this.scan.category || 'other',
      description: this.scan.description || this.scan.supplierName || null,
      amount: Number(this.scan.amount),
      date: new Date(this.scan.date).toISOString(),
      mileage: null,
      receiptNumber: this.scan.invoiceNumber || null,
      receiptUrl: this.scan.receiptUrl || null
    };
    this.apiService.createCost(data).subscribe({
      next: () => { this.saving = false; this.showScanReview = false; this.loadExpenses(); },
      error: (err) => { this.saving = false; console.error('Error saving scanned cost:', err); alert("L'enregistrement a échoué."); }
    });
  }

  confirmDeleteExpense(expense: Expense): void {
    this.expenseToDelete = expense;
    this.showDeleteConfirm = true;
  }

  cancelDelete(): void {
    this.showDeleteConfirm = false;
    this.expenseToDelete = null;
  }

  deleteExpense(): void {
    if (!this.expenseToDelete) { this.cancelDelete(); return; }
    const [table, id] = this.expenseToDelete.id.split('_');
    let obs;
    switch (table) {
      case 'fuel': obs = this.apiService.deleteFuelEntry(parseInt(id)); break;
      case 'repair': obs = this.apiService.deleteRepair(parseInt(id)); break;
      default: obs = this.apiService.deleteCost(parseInt(id)); break;
    }
    obs.subscribe({
      next: () => { this.loadExpenses(); this.cancelDelete(); },
      error: (err) => { console.error('Error deleting:', err); this.cancelDelete(); }
    });
  }

  openDetailPanel(expense: Expense): void {
    this.detailExpense = expense;
    this.showDetailPanel = true;
  }

  closeDetailPanel(): void {
    this.showDetailPanel = false;
    this.detailExpense = null;
  }

  getTotalAmount(): number { return this.filteredExpenses.reduce((sum, e) => sum + e.totalAmount, 0); }
  getUniqueVehiclesCount(): number { return new Set(this.filteredExpenses.map(e => e.vehicleId)).size; }
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
      'entretien': 'Entretien', 'maintenance': 'Maintenance',
      'reparation': 'Réparation',
      'insurance': 'Assurance', 'assurance': 'Assurance',
      'technical_inspection': 'Visite technique',
      'tax': 'Vignette',
      'registration': 'Carte grise',
      'transport_permit': 'Autorisation transport',
      'peage': 'Péage', 'toll': 'Péage',
      'stationnement': 'Stationnement', 'parking': 'Parking',
      'amende': 'Amende',
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
    return this.filteredExpenses.reduce((sum, e) => sum + (e.isRefund ? -e.totalAmount : e.totalAmount), 0);
  }

  /** Total of insurance refunds in the filtered window (positive number, for display). */
  getRefundTotal(): number {
    return this.filteredExpenses.filter(e => e.isRefund).reduce((sum, e) => sum + e.totalAmount, 0);
  }

  exportPdf(): void {
    // Group filtered expenses by category
    const byCategory = new Map<string, Expense[]>();
    for (const exp of this.filteredExpenses) {
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
        rows: expenses.map(e => ({
          date: e.date ? new Date(e.date).toLocaleDateString('fr-FR') : '-',
          vehicleName: `${e.vehicleName} (${e.vehiclePlate})`,
          label: e.label || e.description || '-',
          amount: this.userPrefs.formatCurrency(e.totalAmount)
        })),
        subtotal: `${expenses.length} entrees - ${this.userPrefs.formatCurrency(total)}`
      });
    });

    const config: GroupedPdfReportConfig = {
      title: 'Depenses par categories',
      subtitle: 'Rapport de depenses',
      dateRange: this.filterMonth ? `Mois: ${this.filterMonth}` : 'Toutes periodes',
      statistics: {
        'Total': this.userPrefs.formatCurrency(this.getTotalAmount()),
        'Entrees': '' + this.filteredExpenses.length,
        'Vehicules': '' + this.getUniqueVehiclesCount()
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
