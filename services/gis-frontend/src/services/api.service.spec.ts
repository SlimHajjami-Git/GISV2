import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ApiService, FuelRecordsResult, FuelRecord, FuelReport } from './api.service';

describe('ApiService - Fuel Records', () => {
  let service: ApiService;
  let httpMock: HttpTestingController;

  const mockFuelRecord: FuelRecord = {
    id: 1,
    vehicleId: 1,
    recordedAt: '2025-12-30T10:00:00Z',
    fuelPercent: 75,
    latitude: 36.8065,
    longitude: 10.1815,
    eventType: 'normal',
    isAnomaly: false,
    fuelChange: -5,
    speedKph: 60,
    odometerKm: 15000
  };

  const mockFuelRecordsResult: FuelRecordsResult = {
    items: [mockFuelRecord],
    summary: {
      totalRecords: 1,
      refuelCount: 0,
      anomalyCount: 0,
      totalRefuelLiters: 0,
      averageConsumptionLPer100Km: 8.5
    },
    totalCount: 1,
    page: 1,
    pageSize: 50,
    totalPages: 1
  };

  const mockFuelReport: FuelReport = {
    vehicleId: 1,
    startDate: '2025-12-01T00:00:00Z',
    endDate: '2025-12-31T23:59:59Z',
    totalRecords: 100,
    refuelCount: 5,
    totalRefuelLiters: 250,
    totalRefuelCost: 500,
    anomalyCount: 2,
    theftAlertCount: 1,
    consumptionSpikeCount: 1,
    lowFuelAlertCount: 3,
    averageConsumptionLPer100Km: 8.5,
    refuels: [],
    anomalies: []
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [ApiService]
    });

    service = TestBed.inject(ApiService);
    httpMock = TestBed.inject(HttpTestingController);

    // Mock localStorage for token
    spyOn(localStorage, 'getItem').and.returnValue('mock-token');
  });

  afterEach(() => {
    httpMock.verify();
  });

  describe('getFuelRecords', () => {
    it('should fetch fuel records without filters', () => {
      service.getFuelRecords().subscribe(result => {
        expect(result).toEqual(mockFuelRecordsResult);
        expect(result.items.length).toBe(1);
      });

      const req = httpMock.expectOne(r => r.url.includes('/fuelrecords'));
      expect(req.request.method).toBe('GET');
      req.flush(mockFuelRecordsResult);
    });

    it('should fetch fuel records with vehicleId filter', () => {
      service.getFuelRecords({ vehicleId: 1 }).subscribe(result => {
        expect(result).toEqual(mockFuelRecordsResult);
      });

      const req = httpMock.expectOne(r => 
        r.url.includes('/fuelrecords') && r.params.get('vehicleId') === '1'
      );
      expect(req.request.method).toBe('GET');
      req.flush(mockFuelRecordsResult);
    });

    it('should fetch fuel records with date range filter', () => {
      const startDate = new Date('2025-12-01');
      const endDate = new Date('2025-12-31');

      service.getFuelRecords({ startDate, endDate }).subscribe(result => {
        expect(result).toEqual(mockFuelRecordsResult);
      });

      const req = httpMock.expectOne(r => 
        r.url.includes('/fuelrecords') && 
        r.params.has('startDate') && 
        r.params.has('endDate')
      );
      expect(req.request.method).toBe('GET');
      req.flush(mockFuelRecordsResult);
    });

    it('should fetch fuel records with anomaliesOnly filter', () => {
      service.getFuelRecords({ anomaliesOnly: true }).subscribe(result => {
        expect(result).toEqual(mockFuelRecordsResult);
      });

      const req = httpMock.expectOne(r => 
        r.url.includes('/fuelrecords') && r.params.get('anomaliesOnly') === 'true'
      );
      expect(req.request.method).toBe('GET');
      req.flush(mockFuelRecordsResult);
    });

    it('should fetch fuel records with pagination', () => {
      service.getFuelRecords({ page: 2, pageSize: 25 }).subscribe(result => {
        expect(result).toEqual(mockFuelRecordsResult);
      });

      const req = httpMock.expectOne(r => 
        r.url.includes('/fuelrecords') && 
        r.params.get('page') === '2' && 
        r.params.get('pageSize') === '25'
      );
      expect(req.request.method).toBe('GET');
      req.flush(mockFuelRecordsResult);
    });
  });

  describe('getFuelRecordsByVehicle', () => {
    it('should fetch fuel records for a specific vehicle', () => {
      service.getFuelRecordsByVehicle(1).subscribe(result => {
        expect(result).toEqual(mockFuelRecordsResult);
      });

      const req = httpMock.expectOne(r => r.url.includes('/fuelrecords/vehicle/1'));
      expect(req.request.method).toBe('GET');
      req.flush(mockFuelRecordsResult);
    });

    it('should include date range in vehicle fuel records request', () => {
      const startDate = new Date('2025-12-01');
      const endDate = new Date('2025-12-31');

      service.getFuelRecordsByVehicle(1, startDate, endDate).subscribe(result => {
        expect(result).toEqual(mockFuelRecordsResult);
      });

      const req = httpMock.expectOne(r => 
        r.url.includes('/fuelrecords/vehicle/1') &&
        r.params.has('startDate') &&
        r.params.has('endDate')
      );
      expect(req.request.method).toBe('GET');
      req.flush(mockFuelRecordsResult);
    });
  });

  describe('getFuelRefuels', () => {
    it('should fetch refuel events', () => {
      service.getFuelRefuels().subscribe(result => {
        expect(result).toEqual(mockFuelRecordsResult);
      });

      const req = httpMock.expectOne(r => r.url.includes('/fuelrecords/refuels'));
      expect(req.request.method).toBe('GET');
      req.flush(mockFuelRecordsResult);
    });

    it('should fetch refuel events for a specific vehicle', () => {
      service.getFuelRefuels(1).subscribe(result => {
        expect(result).toEqual(mockFuelRecordsResult);
      });

      const req = httpMock.expectOne(r => 
        r.url.includes('/fuelrecords/refuels') && r.params.get('vehicleId') === '1'
      );
      expect(req.request.method).toBe('GET');
      req.flush(mockFuelRecordsResult);
    });
  });

  describe('getFuelAnomalies', () => {
    it('should fetch fuel anomalies', () => {
      service.getFuelAnomalies().subscribe(result => {
        expect(result).toEqual(mockFuelRecordsResult);
      });

      const req = httpMock.expectOne(r => r.url.includes('/fuelrecords/anomalies'));
      expect(req.request.method).toBe('GET');
      req.flush(mockFuelRecordsResult);
    });

    it('should fetch fuel anomalies for a specific vehicle', () => {
      service.getFuelAnomalies(1).subscribe(result => {
        expect(result).toEqual(mockFuelRecordsResult);
      });

      const req = httpMock.expectOne(r => 
        r.url.includes('/fuelrecords/anomalies') && r.params.get('vehicleId') === '1'
      );
      expect(req.request.method).toBe('GET');
      req.flush(mockFuelRecordsResult);
    });
  });

  describe('getFuelReport', () => {
    it('should fetch fuel report for a vehicle', () => {
      service.getFuelReport(1).subscribe(result => {
        expect(result).toEqual(mockFuelReport);
        expect(result.vehicleId).toBe(1);
        expect(result.refuelCount).toBe(5);
      });

      const req = httpMock.expectOne(r => r.url.includes('/fuelrecords/vehicle/1/report'));
      expect(req.request.method).toBe('GET');
      req.flush(mockFuelReport);
    });

    it('should include date range in fuel report request', () => {
      const startDate = new Date('2025-12-01');
      const endDate = new Date('2025-12-31');

      service.getFuelReport(1, startDate, endDate).subscribe(result => {
        expect(result).toEqual(mockFuelReport);
      });

      const req = httpMock.expectOne(r => 
        r.url.includes('/fuelrecords/vehicle/1/report') &&
        r.params.has('startDate') &&
        r.params.has('endDate')
      );
      expect(req.request.method).toBe('GET');
      req.flush(mockFuelReport);
    });
  });
});

describe('ApiService - Authentication', () => {
  let service: ApiService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [ApiService]
    });

    service = TestBed.inject(ApiService);
  });

  it('should return true when token exists', () => {
    spyOn(localStorage, 'getItem').and.returnValue('valid-token');
    expect(service.isAuthenticated()).toBe(true);
  });

  it('should return false when token does not exist', () => {
    spyOn(localStorage, 'getItem').and.returnValue(null);
    expect(service.isAuthenticated()).toBe(false);
  });
});
