# Dashboard API Documentation

## Overview

The Dashboard API provides comprehensive data for the fleet management dashboard using **CQRS (Command Query Responsibility Segregation)** architecture with **MediatR** for request handling.

## Base URL

```
https://api.example.com/api
```

## Authentication

All endpoints require JWT Bearer token authentication.

```http
Authorization: Bearer <token>
```

## API Endpoints

### 1. Dashboard KPIs

Lightweight endpoint for quick dashboard loading with key performance indicators.

```http
GET /api/dashboard/kpis
```

#### Query Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| year | int | No | Report year (defaults to current) |
| month | int | No | Report month (defaults to current) |
| vehicleIds | int[] | No | Filter by specific vehicle IDs |

#### Response

```json
{
  "generatedAt": "2026-01-20T15:00:00Z",
  "period": "January 2026",
  "fleet": {
    "totalVehicles": 25,
    "activeVehicles": 22,
    "inactiveVehicles": 2,
    "inMaintenance": 1,
    "availabilityRate": 96.0,
    "utilizationRate": 88.0
  },
  "operations": {
    "totalDistanceKm": 45230.5,
    "totalTrips": 1250,
    "totalDrivingHours": 890.5,
    "avgDailyDistanceKm": 1459.7,
    "avgTripsPerVehicle": 50.0,
    "activeDrivers": 18
  },
  "financial": {
    "totalOperationalCost": 9046.10,
    "fuelCost": 6784.58,
    "maintenanceCost": 2261.53,
    "costPerKm": 0.20,
    "costPerVehicle": 361.84,
    "fuelCostPerKm": 0.15
  },
  "performance": {
    "fuelEfficiencyKmPerLiter": 12.5,
    "avgConsumptionPer100Km": 8.0,
    "driverPerformanceScore": 85.0,
    "safetyIncidents": 0,
    "onTimeDeliveryRate": 95.0,
    "idleTimePercentage": 12.3
  },
  "trends": {
    "distance": {
      "currentValue": 45230.5,
      "previousValue": 42100.0,
      "changePercent": 7.4,
      "direction": "up",
      "isPositive": true
    }
  }
}
```

---

### 2. Dashboard Charts

Chart-ready data for visualizations (bar, pie, line, area charts).

```http
GET /api/dashboard/charts
```

#### Query Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| year | int | No | Report year |
| month | int | No | Report month |
| chartTypes | string[] | No | Filter: distance, fuel, maintenance, utilization, cost |
| vehicleIds | int[] | No | Filter by vehicle IDs |

#### Response

```json
{
  "generatedAt": "2026-01-20T15:00:00Z",
  "period": "January 2026",
  "distanceByVehicle": {
    "title": "Kilomètres par véhicule",
    "xAxisLabel": "Véhicule",
    "yAxisLabel": "Distance (km)",
    "unit": "km",
    "data": [
      { "label": "Opel Corsa", "value": 2450.5, "color": "#3B82F6", "id": 1 },
      { "label": "Ford Transit", "value": 1890.2, "color": "#10B981", "id": 2 }
    ]
  },
  "fuelDistribution": {
    "title": "Répartition consommation carburant",
    "unit": "L",
    "total": 3618.44,
    "slices": [
      { "label": "Opel Corsa", "value": 196.04, "percentage": 5.4, "color": "#3B82F6", "id": 1 }
    ]
  },
  "dailyDistanceTrend": {
    "title": "Tendance distance journalière",
    "xAxisLabel": "Date",
    "yAxisLabel": "Distance (km)",
    "labels": ["01/01", "02/01", "03/01"],
    "series": [
      {
        "name": "Distance (km)",
        "color": "#3B82F6",
        "values": [1250.5, 1380.2, 1120.8],
        "fill": true
      }
    ]
  }
}
```

---

### 3. Fleet Statistics

Detailed statistics with pagination and statistical analysis.

```http
GET /api/dashboard/fleet-statistics
```

#### Query Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| year | int | No | Report year |
| month | int | No | Report month |
| groupBy | string | No | Group by: vehicle, driver, type, department |
| vehicleIds | int[] | No | Filter by vehicle IDs |
| pageNumber | int | No | Page number (default: 1) |
| pageSize | int | No | Items per page (default: 25) |

#### Response

```json
{
  "generatedAt": "2026-01-20T15:00:00Z",
  "period": "January 2026",
  "groupedBy": "vehicle",
  "summary": {
    "totalRecords": 25,
    "totalDistanceKm": 45230.5,
    "totalFuelLiters": 3618.44,
    "totalCost": 9046.10,
    "totalTrips": 1250,
    "totalHours": 890.5,
    "avgUtilizationRate": 88.0,
    "avgEfficiency": 12.5
  },
  "vehicleStats": [
    {
      "vehicleId": 1,
      "vehicleName": "Opel Corsa",
      "plate": "A-12345-MA",
      "vehicleType": "car",
      "driverName": "Ahmed Ben Ali",
      "totalDistanceKm": 2450.5,
      "avgDailyDistanceKm": 79.05,
      "maxDailyDistanceKm": 145.2,
      "utilizationRate": 92.5,
      "operatingDays": 28,
      "idleDays": 3,
      "totalDrivingHours": 65.2,
      "totalFuelLiters": 196.04,
      "avgConsumptionPer100Km": 8.0,
      "fuelEfficiencyKmPerLiter": 12.5,
      "fuelVariancePercent": -2.5,
      "totalCost": 490.13,
      "fuelCost": 367.58,
      "maintenanceCost": 122.55,
      "costPerKm": 0.20,
      "costVariancePercent": -1.2,
      "totalTrips": 85,
      "avgSpeedKph": 45.2,
      "maxSpeedKph": 120.5,
      "safetyIncidents": 0,
      "distanceRank": 1,
      "efficiencyRank": 3,
      "costRank": 2
    }
  ],
  "pagination": {
    "currentPage": 1,
    "pageSize": 25,
    "totalPages": 1,
    "totalRecords": 25,
    "hasPrevious": false,
    "hasNext": false
  },
  "analysis": {
    "distanceMean": 1809.22,
    "distanceMedian": 1650.0,
    "distanceStdDev": 450.5,
    "distanceMin": 520.0,
    "distanceMax": 2450.5,
    "fuelMean": 144.74,
    "fuelMedian": 132.0,
    "fuelStdDev": 36.04,
    "costMean": 361.84,
    "costMedian": 330.0,
    "costStdDev": 90.12,
    "highDistanceOutliers": [1, 5],
    "highFuelOutliers": [3],
    "highCostOutliers": []
  }
}
```

---

### 4. Monthly Fleet Report

Comprehensive monthly report with all analytics.

```http
GET /api/reports/monthly
```

#### Query Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| year | int | No | Report year |
| month | int | No | Report month |
| vehicleTypeFilter | int | No | Filter by vehicle type |
| departmentFilter | int | No | Filter by department |
| vehicleIds | int[] | No | Filter by vehicle IDs |

---

### 5. Cache Refresh

Force refresh of cached dashboard data.

```http
POST /api/dashboard/refresh-cache
```

---

## CQRS Architecture

### Query Structure

```
GisAPI.Application/
├── Features/
│   ├── Dashboard/
│   │   └── Queries/
│   │       ├── GetDashboardKpis/
│   │       │   ├── GetDashboardKpisQuery.cs
│   │       │   └── GetDashboardKpisQueryHandler.cs
│   │       ├── GetDashboardCharts/
│   │       │   ├── GetDashboardChartsQuery.cs
│   │       │   └── GetDashboardChartsQueryHandler.cs
│   │       └── GetFleetStatistics/
│   │           ├── GetFleetStatisticsQuery.cs
│   │           └── GetFleetStatisticsQueryHandler.cs
│   └── Reports/
│       └── Queries/
│           └── GetMonthlyFleetReport/
│               ├── GetMonthlyFleetReportQuery.cs
│               └── GetMonthlyFleetReportQueryHandler.cs
```

### Handler Pattern

```csharp
public class GetDashboardKpisQueryHandler : IQueryHandler<GetDashboardKpisQuery, DashboardKpisDto>
{
    private readonly IGisDbContext _context;
    private readonly ITenantService _tenantService;

    public async Task<DashboardKpisDto> Handle(
        GetDashboardKpisQuery request, 
        CancellationToken cancellationToken)
    {
        // Implementation
    }
}
```

---

## Caching Strategy

- **Duration**: 5 minutes for KPIs and Charts
- **Cache Key Format**: `dashboard_{type}_{companyId}_{year}_{month}_{vehicleIds}`
- **Invalidation**: POST `/api/dashboard/refresh-cache`

---

## Error Handling

### HTTP Status Codes

| Code | Description |
|------|-------------|
| 200 | Success |
| 400 | Bad Request - Invalid parameters |
| 401 | Unauthorized - Missing or invalid token |
| 403 | Forbidden - Insufficient permissions |
| 404 | Not Found - Resource not found |
| 500 | Internal Server Error |

### Error Response Format

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "Invalid year parameter",
  "traceId": "00-abc123..."
}
```

---

## Rate Limiting

- **Limit**: 100 requests per minute per user
- **Headers**: `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset`

---

## Frontend Integration

### TypeScript Interfaces

```typescript
interface DashboardKpisDto {
  generatedAt: string;
  period: string;
  fleet: FleetKpisDto;
  operations: OperationalKpisDto;
  financial: FinancialKpisDto;
  performance: PerformanceKpisDto;
  trends: TrendIndicatorsDto;
}

interface DashboardChartsDto {
  generatedAt: string;
  period: string;
  distanceByVehicle: BarChartDataDto;
  fuelDistribution: PieChartDataDto;
  maintenanceTrend: AreaChartDataDto;
  dailyDistanceTrend: LineChartDataDto;
  utilizationTrend: LineChartDataDto;
  costBreakdown: PieChartDataDto;
  vehicleStatusChart: BarChartDataDto;
  topVehicles: BarChartDataDto;
}
```

### API Service Example

```typescript
@Injectable({ providedIn: 'root' })
export class DashboardApiService {
  private baseUrl = '/api/dashboard';

  constructor(private http: HttpClient) {}

  getKpis(year?: number, month?: number): Observable<DashboardKpisDto> {
    const params = new HttpParams()
      .set('year', year?.toString() || '')
      .set('month', month?.toString() || '');
    return this.http.get<DashboardKpisDto>(`${this.baseUrl}/kpis`, { params });
  }

  getCharts(year?: number, month?: number): Observable<DashboardChartsDto> {
    const params = new HttpParams()
      .set('year', year?.toString() || '')
      .set('month', month?.toString() || '');
    return this.http.get<DashboardChartsDto>(`${this.baseUrl}/charts`, { params });
  }
}
```

---

## Performance Considerations

1. **Lightweight KPIs endpoint** - Use for initial dashboard load
2. **Charts loaded separately** - Defer chart data loading
3. **Pagination** - Use for large fleet statistics
4. **Caching** - 5-minute cache reduces database load
5. **Tenant isolation** - All queries filtered by company ID
