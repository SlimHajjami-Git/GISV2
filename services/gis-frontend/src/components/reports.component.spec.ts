// marked n'est livré qu'en ESM, que jest ne transforme pas dans node_modules : le charger
// avec le composant fait échouer la suite avant le premier test. Aucun test ici ne rend du
// markdown (rapport IA). Même contournement que reports-ai-cost-breakdown.spec.ts.
jest.mock('marked', () => ({ marked: { parse: (s: string) => s } }));

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { FormsModule } from '@angular/forms';
import { ReportsComponent } from './reports.component';
import { ApiService, PositionDto } from '../services/api.service';
import { of, throwError } from 'rxjs';

describe('ReportsComponent', () => {
  let component: ReportsComponent;
  let fixture: ComponentFixture<ReportsComponent>;
  let apiService: ApiService;

  // Le rapport carburant part de l'historique GPS (fuelRaw / odometerKm) et non
  // plus de /api/fuel-records : trois trames = niveau initial, remplissage
  // (+10 %) puis chute importante (-50 %, marquée anomalie).
  const mockFuelPositions: PositionDto[] = [
    {
      id: 1,
      recordedAt: '2025-12-30T08:00:00Z',
      latitude: 36.8065,
      longitude: 10.1815,
      address: 'Avenue Habib Bourguiba, Tunis',
      fuelRaw: 85,
      odometerKm: 15000,
      speedKph: 60
    },
    {
      id: 2,
      recordedAt: '2025-12-30T10:00:00Z',
      latitude: 36.8100,
      longitude: 10.1850,
      address: 'Station service, La Marsa',
      fuelRaw: 95,
      odometerKm: 15120,
      speedKph: 0
    },
    {
      id: 3,
      recordedAt: '2025-12-30T12:00:00Z',
      latitude: 36.8200,
      longitude: 10.1900,
      address: 'Route de Bizerte, Ariana',
      fuelRaw: 45,
      odometerKm: 15200,
      speedKph: 0
    }
  ];

  beforeEach(async () => {
    // Le service PDF précharge le logo par fetch(), absent de jsdom.
    (globalThis as any).fetch = jest.fn(() => Promise.resolve({ ok: false }));

    await TestBed.configureTestingModule({
      imports: [
        HttpClientTestingModule,
        RouterTestingModule,
        FormsModule,
        ReportsComponent
      ],
      providers: [ApiService]
    }).compileComponents();

    fixture = TestBed.createComponent(ReportsComponent);
    component = fixture.componentInstance;
    apiService = TestBed.inject(ApiService);

    // Mock authentication
    jest.spyOn(apiService, 'isAuthenticated').mockReturnValue(true);
    jest.spyOn(apiService, 'getVehicles').mockReturnValue(of([]));
  });

  describe('Component initialization', () => {
    it('should create the component', () => {
      expect(component).toBeTruthy();
    });

    it('should have default values', () => {
      expect(component.selectedStandardPeriod).toBe('today');
      expect(component.reportGenerated).toBe(false);
      expect(component.loading).toBe(false);
      expect(component.currentPage).toBe(1);
    });

    // Compte du catalogue de rapports : à mettre à jour en même temps que
    // `templates` quand un rapport est ajouté ou retiré du menu.
    it('should have 21 report templates', () => {
      expect(component.templates.length).toBe(21);
    });

    it('should include fuel report template', () => {
      const fuelTemplate = component.templates.find(t => t.type === 'fuel');
      expect(fuelTemplate).toBeTruthy();
      expect(fuelTemplate?.name).toBe('Consommation carburant');
    });
  });

  describe('Template selection', () => {
    it('should select a template', () => {
      const fuelTemplate = component.templates.find(t => t.type === 'fuel');
      component.selectTemplate(fuelTemplate);

      expect(component.selectedTemplate).toEqual(fuelTemplate);
      expect(component.selectedTemplateId).toBe(fuelTemplate?.id);
    });

    it('should update template on change', () => {
      component.selectedTemplateId = '2'; // Fuel report
      component.onTemplateChange();

      expect(component.selectedTemplate?.type).toBe('fuel');
    });
  });

  describe('Period selection', () => {
    it('should set date range for today', () => {
      component.selectStandardPeriod('today');

      expect(component.selectedStandardPeriod).toBe('today');
      expect(component.fromDate).toBeTruthy();
      expect(component.toDate).toBeTruthy();
    });

    it('should set date range for week', () => {
      component.selectStandardPeriod('week');

      expect(component.selectedStandardPeriod).toBe('week');
      const fromDate = new Date(component.fromDate);
      const toDate = new Date(component.toDate);
      const diffDays = (toDate.getTime() - fromDate.getTime()) / (1000 * 60 * 60 * 24);
      expect(diffDays).toBeGreaterThanOrEqual(6);
      expect(diffDays).toBeLessThanOrEqual(8);
    });

    it('should set date range for month', () => {
      component.selectStandardPeriod('month');

      expect(component.selectedStandardPeriod).toBe('month');
    });
  });

  describe('Fuel report execution', () => {
    beforeEach(() => {
      component.selectedTemplateId = '2';
      component.onTemplateChange();
      component.selectStandardPeriod('week');
      // Rapport mono-véhicule : sans véhicule sélectionné, executeReport()
      // s'arrête avant tout appel API.
      component.selectedVehicleId = '1';
    });

    it('should call API when executing fuel report', () => {
      jest.spyOn(apiService, 'getVehicleHistory').mockReturnValue(of(mockFuelPositions));

      component.executeReport();

      expect(apiService.getVehicleHistory).toHaveBeenCalled();
    });

    it('should process fuel data correctly', () => {
      component.processVehicleData(mockFuelPositions);

      expect(component.tableData.length).toBe(3);
      expect(component.chartData.length).toBe(3);
    });

    it('should identify anomaly records', () => {
      component.processVehicleData(mockFuelPositions);

      const anomalyRow = component.tableData.find((r: any) => r.isAnomaly);
      expect(anomalyRow).toBeTruthy();
      expect(anomalyRow.eventType).toContain('Chute importante');
    });

    it('should calculate statistics correctly', () => {
      component.processVehicleData(mockFuelPositions);

      expect(component.statisticsData['⛽ Remplissages']).toBe('1');
      expect(component.statisticsData['⚠️ Alertes']).toBe('1');
      expect(component.statisticsData['📏 Distance parcourue']).toBe('200 km');
    });

    it('should report an error message when the API fails', () => {
      jest.spyOn(apiService, 'getVehicleHistory').mockReturnValue(throwError(() => new Error('API Error')));

      component.executeReport();

      expect(component.statisticsData['Erreur']).toBe('Impossible de charger les données');
      expect(component.tableData.length).toBe(0);
      expect(component.reportGenerated).toBe(true);
      expect(component.loading).toBe(false);
    });
  });

  describe('Pagination', () => {
    beforeEach(() => {
      component.tableData = Array(100).fill({}).map((_, i) => ({ id: i }));
      component.pageSize = 10;
    });

    it('should calculate total pages correctly', () => {
      expect(component.totalPages).toBe(10);
    });

    it('should calculate start item correctly', () => {
      component.currentPage = 1;
      expect(component.startItem).toBe(1);

      component.currentPage = 2;
      expect(component.startItem).toBe(11);
    });

    it('should calculate end item correctly', () => {
      component.currentPage = 1;
      expect(component.endItem).toBe(10);

      component.currentPage = 10;
      expect(component.endItem).toBe(100);
    });

    it('should return correct paginated data', () => {
      component.currentPage = 1;
      expect(component.paginatedData.length).toBe(10);
      expect(component.paginatedData[0].id).toBe(0);

      component.currentPage = 2;
      expect(component.paginatedData[0].id).toBe(10);
    });

    it('should navigate to page', () => {
      component.goToPage(5);
      expect(component.currentPage).toBe(5);
    });

    it('should not navigate to invalid page', () => {
      component.currentPage = 1;
      component.goToPage(0);
      expect(component.currentPage).toBe(1);

      component.goToPage(100);
      expect(component.currentPage).toBe(1);
    });

    it('should reset page on page size change', () => {
      component.currentPage = 5;
      component.onPageSizeChange();
      expect(component.currentPage).toBe(1);
    });
  });

  describe('Clear filters', () => {
    it('should reset all filters', () => {
      component.selectedTemplateId = '2';
      component.selectedTemplate = component.templates[1];
      component.selectedVehicleId = '1';
      component.reportGenerated = true;

      component.clearFilters();

      expect(component.selectedTemplateId).toBe('');
      expect(component.selectedTemplate).toBeNull();
      expect(component.selectedVehicleId).toBe('');
      expect(component.reportGenerated).toBe(false);
    });
  });

  describe('Tab management', () => {
    it('should set active tab', () => {
      component.setActiveTab('statistics');
      expect(component.activeTab).toBe('statistics');

      component.setActiveTab('table');
      expect(component.activeTab).toBe('table');
    });
  });
});
