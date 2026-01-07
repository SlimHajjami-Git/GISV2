import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { FormsModule } from '@angular/forms';
import { ReportsComponent } from './reports.component';
import { ApiService, FuelRecordsResult, FuelRecord } from '../services/api.service';
import { of, throwError } from 'rxjs';

describe('ReportsComponent', () => {
  let component: ReportsComponent;
  let fixture: ComponentFixture<ReportsComponent>;
  let apiService: ApiService;

  const mockFuelRecords: FuelRecord[] = [
    {
      id: 1,
      vehicleId: 1,
      recordedAt: '2025-12-30T08:00:00Z',
      fuelPercent: 85,
      fuelChange: -5,
      latitude: 36.8065,
      longitude: 10.1815,
      eventType: 'normal',
      isAnomaly: false,
      speedKph: 60,
      odometerKm: 15000
    },
    {
      id: 2,
      vehicleId: 1,
      recordedAt: '2025-12-30T10:00:00Z',
      fuelPercent: 100,
      fuelChange: 15,
      latitude: 36.8100,
      longitude: 10.1850,
      eventType: 'refuel',
      isAnomaly: false,
      refuelAmount: 45,
      speedKph: 0,
      odometerKm: 15120
    },
    {
      id: 3,
      vehicleId: 1,
      recordedAt: '2025-12-30T12:00:00Z',
      fuelPercent: 50,
      fuelChange: -50,
      latitude: 36.8200,
      longitude: 10.1900,
      eventType: 'theft_alert',
      isAnomaly: true,
      anomalyReason: 'Sudden fuel drop detected',
      speedKph: 0,
      odometerKm: 15200
    }
  ];

  const mockFuelRecordsResult: FuelRecordsResult = {
    items: mockFuelRecords,
    summary: {
      totalRecords: 3,
      refuelCount: 1,
      anomalyCount: 1,
      totalRefuelLiters: 45,
      averageConsumptionLPer100Km: 8.5
    },
    totalCount: 3,
    page: 1,
    pageSize: 50,
    totalPages: 1
  };

  beforeEach(async () => {
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
    spyOn(apiService, 'isAuthenticated').and.returnValue(true);
    spyOn(apiService, 'getVehicles').and.returnValue(of([]));
  });

  describe('Component initialization', () => {
    it('should create the component', () => {
      expect(component).toBeTruthy();
    });

    it('should have default values', () => {
      expect(component.selectedPeriod).toBe('week');
      expect(component.reportGenerated).toBe(false);
      expect(component.loading).toBe(false);
      expect(component.currentPage).toBe(1);
    });

    it('should have 7 report templates', () => {
      expect(component.templates.length).toBe(7);
    });

    it('should include fuel report template', () => {
      const fuelTemplate = component.templates.find(t => t.type === 'fuel');
      expect(fuelTemplate).toBeTruthy();
      expect(fuelTemplate?.name).toBe('Rapport de carburant');
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
      component.selectPeriod('today');

      expect(component.selectedPeriod).toBe('today');
      expect(component.fromDate).toBeTruthy();
      expect(component.toDate).toBeTruthy();
    });

    it('should set date range for week', () => {
      component.selectPeriod('week');

      expect(component.selectedPeriod).toBe('week');
      const fromDate = new Date(component.fromDate);
      const toDate = new Date(component.toDate);
      const diffDays = (toDate.getTime() - fromDate.getTime()) / (1000 * 60 * 60 * 24);
      expect(diffDays).toBeGreaterThanOrEqual(6);
      expect(diffDays).toBeLessThanOrEqual(8);
    });

    it('should set date range for month', () => {
      component.selectPeriod('month');

      expect(component.selectedPeriod).toBe('month');
    });
  });

  describe('Fuel report execution', () => {
    beforeEach(() => {
      component.selectedTemplateId = '2';
      component.onTemplateChange();
      component.selectPeriod('week');
    });

    it('should call API when executing fuel report', () => {
      spyOn(apiService, 'getFuelRecords').and.returnValue(of(mockFuelRecordsResult));

      component.executeReport();

      expect(apiService.getFuelRecords).toHaveBeenCalled();
    });

    it('should process fuel data correctly', () => {
      component.processFuelData(mockFuelRecordsResult);

      expect(component.tableData.length).toBe(3);
      expect(component.chartData.length).toBe(3);
    });

    it('should identify anomaly records', () => {
      component.processFuelData(mockFuelRecordsResult);

      const anomalyRow = component.tableData.find((r: any) => r.isAnomaly);
      expect(anomalyRow).toBeTruthy();
      expect(anomalyRow.eventType).toContain('Vol suspecté');
    });

    it('should calculate statistics correctly', () => {
      component.processFuelData(mockFuelRecordsResult);

      expect(component.statisticsData['Total enregistrements']).toBe('3');
      expect(component.statisticsData['Remplissages']).toBe('1');
      expect(component.statisticsData['Anomalies']).toBe('1');
    });

    it('should handle API error with fallback to mock data', () => {
      spyOn(apiService, 'getFuelRecords').and.returnValue(throwError(() => new Error('API Error')));
      spyOn(component, 'generateFuelData');

      component.executeReport();

      // Wait for async operation
      setTimeout(() => {
        expect(component.generateFuelData).toHaveBeenCalled();
      }, 100);
    });
  });

  describe('Event type translation', () => {
    it('should translate normal event type', () => {
      expect(component.translateEventType('normal')).toBe('Normal');
    });

    it('should translate refuel event type', () => {
      expect(component.translateEventType('refuel')).toBe('Remplissage');
    });

    it('should translate theft_alert event type', () => {
      expect(component.translateEventType('theft_alert')).toBe('⚠️ Vol suspecté');
    });

    it('should translate consumption_spike event type', () => {
      expect(component.translateEventType('consumption_spike')).toBe('⚠️ Pic consommation');
    });

    it('should translate low_fuel event type', () => {
      expect(component.translateEventType('low_fuel')).toBe('⚠️ Niveau bas');
    });

    it('should return original for unknown event type', () => {
      expect(component.translateEventType('unknown_type')).toBe('unknown_type');
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
