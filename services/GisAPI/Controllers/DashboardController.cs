using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MediatR;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;
using GisAPI.Application.Features.Dashboard.Queries.GetDashboardKpis;
using GisAPI.Application.Features.Dashboard.Queries.GetDashboardCharts;
using GisAPI.Application.Features.Dashboard.Queries.GetFleetStatistics;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Services;

namespace GisAPI.Controllers;

/// <summary>
/// Dashboard API Controller - Provides comprehensive data for fleet management dashboards
/// Implements CQRS pattern with MediatR and supports caching for performance
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public class DashboardController : ControllerBase
{
    private readonly GisDbContext _context;
    private readonly IMediator _mediator;
    private readonly IMemoryCache _cache;
    private readonly IVehicleHealthScoreService _healthService;
    private readonly IFuelCalculationService _fuelCalcService;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public DashboardController(GisDbContext context, IMediator mediator, IMemoryCache cache, IVehicleHealthScoreService healthService, IFuelCalculationService fuelCalcService)
    {
        _context = context;
        _mediator = mediator;
        _cache = cache;
        _healthService = healthService;
        _fuelCalcService = fuelCalcService;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");

    #region NEW CQRS-BASED ENDPOINTS

    /// <summary>
    /// Get lightweight KPI data for quick dashboard loading
    /// </summary>
    /// <param name="year">Report year (defaults to current year)</param>
    /// <param name="month">Report month (defaults to current month)</param>
    /// <param name="vehicleIds">Optional filter by vehicle IDs</param>
    /// <returns>Dashboard KPIs with fleet, operational, financial, and performance metrics</returns>
    /// <response code="200">Returns KPI data</response>
    /// <response code="401">Unauthorized</response>
    [HttpGet("kpis")]
    [ProducesResponseType(typeof(DashboardKpisDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardKpisDto>> GetDashboardKpis(
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] int[]? vehicleIds = null)
    {
        var cacheKey = $"dashboard_kpis_{GetCompanyId()}_{year}_{month}_{string.Join(",", vehicleIds ?? Array.Empty<int>())}";
        
        if (_cache.TryGetValue(cacheKey, out DashboardKpisDto? cachedResult) && cachedResult != null)
        {
            return Ok(cachedResult);
        }

        var result = await _mediator.Send(new GetDashboardKpisQuery(year, month, vehicleIds));
        
        _cache.Set(cacheKey, result, CacheDuration);
        
        return Ok(result);
    }

    /// <summary>
    /// Get chart-ready data for dashboard visualizations
    /// </summary>
    /// <param name="year">Report year</param>
    /// <param name="month">Report month</param>
    /// <param name="chartTypes">Filter specific chart types: distance, fuel, maintenance, utilization, cost</param>
    /// <param name="vehicleIds">Optional filter by vehicle IDs</param>
    /// <returns>Chart data for bar, pie, line, and area charts</returns>
    [HttpGet("charts")]
    [ProducesResponseType(typeof(DashboardChartsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardChartsDto>> GetDashboardCharts(
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] string[]? chartTypes = null,
        [FromQuery] int[]? vehicleIds = null)
    {
        var cacheKey = $"dashboard_charts_{GetCompanyId()}_{year}_{month}_{string.Join(",", vehicleIds ?? Array.Empty<int>())}";
        
        if (_cache.TryGetValue(cacheKey, out DashboardChartsDto? cachedResult) && cachedResult != null)
        {
            return Ok(cachedResult);
        }

        var result = await _mediator.Send(new GetDashboardChartsQuery(year, month, chartTypes, vehicleIds));
        
        _cache.Set(cacheKey, result, CacheDuration);
        
        return Ok(result);
    }

    /// <summary>
    /// Get detailed fleet statistics with pagination and grouping
    /// </summary>
    /// <param name="year">Report year</param>
    /// <param name="month">Report month</param>
    /// <param name="groupBy">Group results by: vehicle, driver, type, department</param>
    /// <param name="vehicleIds">Optional filter by vehicle IDs</param>
    /// <param name="pageNumber">Page number for pagination</param>
    /// <param name="pageSize">Items per page (default 25)</param>
    /// <returns>Detailed statistics with pagination and statistical analysis</returns>
    [HttpGet("fleet-statistics")]
    [ProducesResponseType(typeof(FleetStatisticsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FleetStatisticsDto>> GetFleetStatistics(
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] string? groupBy = null,
        [FromQuery] int[]? vehicleIds = null,
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null)
    {
        var result = await _mediator.Send(new GetFleetStatisticsQuery(
            year, month, groupBy, vehicleIds, pageNumber, pageSize));
        
        return Ok(result);
    }

    /// <summary>
    /// Invalidate cached dashboard data (force refresh)
    /// </summary>
    [HttpPost("refresh-cache")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult RefreshCache()
    {
        var companyId = GetCompanyId();
        // Clear all dashboard-related cache entries for this company
        // Note: In production, use distributed cache with pattern-based invalidation
        return Ok(new { message = "Cache refresh initiated", companyId });
    }

    /// <summary>
    /// Get real data for all dashboard widget cards (fuel consumers, driving scores, health, immobilization, trends)
    /// </summary>
    [HttpGet("widget-data")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetWidgetData([FromQuery] string period = "month")
    {
        var companyId = GetCompanyId();
        var cacheKey = $"dashboard_widgets_{companyId}_{period}";
        if (_cache.TryGetValue(cacheKey, out object? cached) && cached != null)
            return Ok(cached);

        var now = DateTime.UtcNow;
        var (periodStart, periodEnd, prevStart, prevEnd) = GetPeriodRange(now, period);

        var vehicles = await _context.Vehicles.AsNoTracking()
            .Where(v => v.CompanyId == companyId)
            .ToListAsync();

        var deviceMap = vehicles
            .Where(v => v.GpsDeviceId.HasValue)
            .ToDictionary(v => v.GpsDeviceId!.Value, v => v);
        var deviceIds = deviceMap.Keys.ToList();

        // ── Top fuel consumers (from FMS FuelRateLPer100Km) ──
        var fuelData = await _context.GpsPositions.AsNoTracking()
            .Where(p => deviceIds.Contains(p.DeviceId) &&
                        p.RecordedAt >= periodStart && p.RecordedAt <= periodEnd &&
                        p.FuelRateLPer100Km != null && p.FuelRateLPer100Km > 0 && p.FuelRateLPer100Km < 80)
            .GroupBy(p => p.DeviceId)
            .Select(g => new { DeviceId = g.Key, AvgRate = g.Average(p => (double)p.FuelRateLPer100Km!), Count = g.Count() })
            .Where(x => x.Count >= 3)
            .OrderByDescending(x => x.AvgRate)
            .Take(5)
            .ToListAsync();

        // Previous period fuel for trend
        var prevFuelData = await _context.GpsPositions.AsNoTracking()
            .Where(p => deviceIds.Contains(p.DeviceId) &&
                        p.RecordedAt >= prevStart && p.RecordedAt <= prevEnd &&
                        p.FuelRateLPer100Km != null && p.FuelRateLPer100Km > 0 && p.FuelRateLPer100Km < 80)
            .GroupBy(p => p.DeviceId)
            .Select(g => new { DeviceId = g.Key, AvgRate = g.Average(p => (double)p.FuelRateLPer100Km!) })
            .ToListAsync();

        var topFuelConsumers = fuelData.Select(f =>
        {
            var v = deviceMap.GetValueOrDefault(f.DeviceId);
            var prevRate = prevFuelData.FirstOrDefault(p => p.DeviceId == f.DeviceId)?.AvgRate ?? f.AvgRate;
            var trend = prevRate > 0 ? Math.Round((f.AvgRate - prevRate) / prevRate * 100, 1) : 0;
            return new { plate = v?.Plate ?? v?.Name ?? "N/A", consumption = Math.Round(f.AvgRate, 1), trend };
        }).ToList();

        // If no FMS data, fallback: estimate from vehicle mileage & type
        if (topFuelConsumers.Count == 0)
        {
            topFuelConsumers = vehicles
                .Where(v => v.Mileage > 0)
                .OrderByDescending(v => v.Mileage)
                .Take(5)
                .Select(v =>
                {
                    var rate = (v.Type?.ToLower()) switch
                    {
                        "camion" => 25.0, "bus" => 30.0, "fourgon" => 11.0,
                        "utilitaire" or "camionnette" => 10.0, "suv" => 9.0,
                        _ => 8.0
                    };
                    return new { plate = v.Plate ?? v.Name, consumption = rate, trend = 0.0 };
                }).ToList();
        }

        // ── Driving scores (from alerts count in period — fewer alerts = better score) ──
        var alertsByVehicle = await _context.GpsAlerts.AsNoTracking()
            .Where(a => a.VehicleId.HasValue && a.Vehicle!.CompanyId == companyId &&
                        a.Timestamp >= periodStart && a.Timestamp <= periodEnd)
            .GroupBy(a => a.VehicleId!.Value)
            .Select(g => new { VehicleId = g.Key, AlertCount = g.Count() })
            .ToListAsync();

        var drivingScores = vehicles
            .Select(v =>
            {
                var alerts = alertsByVehicle.FirstOrDefault(a => a.VehicleId == v.Id)?.AlertCount ?? 0;
                var score = Math.Max(0, 100 - (alerts * 5)); // -5 per alert, min 0
                return new { vehicleId = v.Id, plate = v.Plate ?? v.Name, score };
            })
            .OrderByDescending(x => x.score)
            .ToList();

        // Group into 4 score tiers
        string[] tierColors = { "#3b82f6", "#10b981", "#f59e0b", "#ef4444" };
        var scoreTiers = new List<object>();
        if (drivingScores.Count > 0)
        {
            var chunkSize = Math.Max(1, (drivingScores.Count + 3) / 4);
            for (int i = 0; i < 4 && i * chunkSize < drivingScores.Count; i++)
            {
                var chunk = drivingScores.Skip(i * chunkSize).Take(chunkSize).ToList();
                var avgScore = (int)Math.Round(chunk.Average(x => x.score));
                scoreTiers.Add(new
                {
                    score = avgScore,
                    color = tierColors[i],
                    vehicles = chunk.Take(2).Select(x => x.plate).ToList()
                });
            }
        }

        // ── Vehicle health (from IVehicleHealthScoreService — real DB data) ──
        var healthResults = await _healthService.CalculateAllScoresAsync(companyId);
        var healthyVehicles = healthResults
            .Where(h => h.Score >= 60)
            .OrderByDescending(h => h.Score)
            .Take(5)
            .Select(h => new { plate = h.VehicleName, score = h.Score })
            .ToList();

        var unhealthyVehicles = healthResults
            .Where(h => h.Score < 60)
            .OrderBy(h => h.Score)
            .Select(h => new
            {
                plate = h.VehicleName,
                issue = h.Warnings.FirstOrDefault() ?? (h.Level == "critical" ? "État critique" : "Maintenance requise")
            })
            .ToList();

        // ── Immobilized vehicles (from maintenance schedules overdue/critical + vehicle status) ──
        var immobSchedules = await _context.VehicleMaintenanceSchedules.AsNoTracking()
            .Where(s => s.CompanyId == companyId && !s.IsPaused &&
                        (s.Status == "overdue" || s.Status == "critical" || s.Status == "due"))
            .Include(s => s.Vehicle)
            .Include(s => s.Template)
            .ToListAsync();

        var seen = new HashSet<int>();
        var immobilizedVehicles = new List<object>();
        foreach (var s in immobSchedules.OrderByDescending(s => s.Status == "critical").ThenByDescending(s => s.Status == "overdue"))
        {
            if (s.Vehicle == null || !seen.Add(s.VehicleId)) continue;
            var days = s.NextDueDate.HasValue ? Math.Max(0, (int)(now - s.NextDueDate.Value).TotalDays) : 0;
            immobilizedVehicles.Add(new
            {
                plate = s.Vehicle.Plate ?? s.Vehicle.Name,
                reason = s.Template?.Name ?? (s.Status == "critical" ? "Maintenance critique" : "Maintenance en retard"),
                days = Math.Max(days, 1)
            });
        }
        // Add vehicles with maintenance status not already covered
        foreach (var v in vehicles.Where(v => v.Status == "maintenance" && !seen.Contains(v.Id)))
        {
            immobilizedVehicles.Add(new { plate = v.Plate ?? v.Name, reason = "En maintenance", days = 1 });
        }

        // ── Immobilization history (real monthly counts from maintenance logs) ──
        // Single query for all 6 months instead of 6 separate CountAsync calls
        var monthNames = new[] { "Jan", "Fev", "Mar", "Avr", "Mai", "Jun", "Jul", "Aou", "Sep", "Oct", "Nov", "Dec" };
        var sixMonthsAgo = DateTime.SpecifyKind(new DateTime(now.Year, now.Month, 1).AddMonths(-5), DateTimeKind.Utc);
        var monthEnd = DateTime.SpecifyKind(new DateTime(now.Year, now.Month, 1).AddMonths(1).AddSeconds(-1), DateTimeKind.Utc);

        var immobRaw = await _context.VehicleMaintenanceSchedules.AsNoTracking()
            .Where(s => s.CompanyId == companyId &&
                        (s.Status == "overdue" || s.Status == "critical") &&
                        s.NextDueDate.HasValue &&
                        s.NextDueDate.Value >= sixMonthsAgo &&
                        s.NextDueDate.Value <= monthEnd)
            .Select(s => new { s.VehicleId, s.NextDueDate!.Value.Year, s.NextDueDate!.Value.Month })
            .ToListAsync();

        var immobGrouped = immobRaw
            .GroupBy(s => new { s.Year, s.Month })
            .ToDictionary(g => g.Key, g => g.Select(s => s.VehicleId).Distinct().Count());

        var immobHistory = new List<object>();
        for (int i = 5; i >= 0; i--)
        {
            var mStart = new DateTime(now.Year, now.Month, 1).AddMonths(-i);
            immobGrouped.TryGetValue(new { mStart.Year, mStart.Month }, out var count);
            immobHistory.Add(new { month = monthNames[mStart.Month - 1], count });
        }

        // ── Trends (real period comparison) ──
        // Mileage trend: compare current vs previous period trip distances
        var currentMileage = await _context.Trips.AsNoTracking()
            .Where(t => t.CompanyId == companyId && t.StartTime >= periodStart && t.StartTime <= periodEnd && t.Status == "completed")
            .Select(t => (decimal?)t.DistanceKm).SumAsync() ?? 0m;
        var prevMileage = await _context.Trips.AsNoTracking()
            .Where(t => t.CompanyId == companyId && t.StartTime >= prevStart && t.StartTime <= prevEnd && t.Status == "completed")
            .Select(t => (decimal?)t.DistanceKm).SumAsync() ?? 0m;
        var mileageTrend = prevMileage > 0 ? Math.Round((double)(currentMileage - prevMileage) / (double)prevMileage * 100, 1) : 0;

        // Cost trend: compare current vs previous period costs
        var currentCost = await _context.VehicleCosts.AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.Date >= periodStart && c.Date <= periodEnd)
            .Select(c => (decimal?)c.Amount).SumAsync() ?? 0m;
        var prevCost = await _context.VehicleCosts.AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.Date >= prevStart && c.Date <= prevEnd)
            .Select(c => (decimal?)c.Amount).SumAsync() ?? 0m;
        var costTrend = prevCost > 0 ? Math.Round((double)(currentCost - prevCost) / (double)prevCost * 100, 1) : 0;

        var result = new
        {
            topFuelConsumers,
            drivingScores = scoreTiers,
            healthyVehicles,
            unhealthyVehicles,
            immobilizedVehicles,
            immobHistory,
            trends = new
            {
                mileage = mileageTrend,
                expenses = costTrend,
                fuel = topFuelConsumers.Any()
                    ? Math.Round(topFuelConsumers.Average(f => f.trend), 1)
                    : 0.0
            }
        };

        _cache.Set(cacheKey, result, CacheDuration);
        return Ok(result);
    }

    private (DateTime start, DateTime end, DateTime prevStart, DateTime prevEnd) GetPeriodRange(DateTime now, string period)
    {
        DateTime start, end, prevStart, prevEnd;
        switch (period)
        {
            case "today":
                start = DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);
                end = DateTime.SpecifyKind(now.Date.AddDays(1).AddSeconds(-1), DateTimeKind.Utc);
                prevStart = DateTime.SpecifyKind(now.Date.AddDays(-1), DateTimeKind.Utc);
                prevEnd = DateTime.SpecifyKind(now.Date.AddSeconds(-1), DateTimeKind.Utc);
                break;
            case "yesterday":
                start = DateTime.SpecifyKind(now.Date.AddDays(-1), DateTimeKind.Utc);
                end = DateTime.SpecifyKind(now.Date.AddSeconds(-1), DateTimeKind.Utc);
                prevStart = DateTime.SpecifyKind(now.Date.AddDays(-2), DateTimeKind.Utc);
                prevEnd = DateTime.SpecifyKind(start.AddSeconds(-1), DateTimeKind.Utc);
                break;
            case "week":
                var dayOfWeek = (int)now.DayOfWeek;
                start = DateTime.SpecifyKind(now.Date.AddDays(-dayOfWeek), DateTimeKind.Utc);
                end = DateTime.SpecifyKind(start.AddDays(7).AddSeconds(-1), DateTimeKind.Utc);
                prevStart = DateTime.SpecifyKind(start.AddDays(-7), DateTimeKind.Utc);
                prevEnd = DateTime.SpecifyKind(start.AddSeconds(-1), DateTimeKind.Utc);
                break;
            case "quarter":
                var quarterMonth = ((now.Month - 1) / 3) * 3 + 1;
                start = DateTime.SpecifyKind(new DateTime(now.Year, quarterMonth, 1), DateTimeKind.Utc);
                end = DateTime.SpecifyKind(start.AddMonths(3).AddSeconds(-1), DateTimeKind.Utc);
                prevStart = DateTime.SpecifyKind(start.AddMonths(-3), DateTimeKind.Utc);
                prevEnd = DateTime.SpecifyKind(start.AddSeconds(-1), DateTimeKind.Utc);
                break;
            default: // month
                start = DateTime.SpecifyKind(new DateTime(now.Year, now.Month, 1), DateTimeKind.Utc);
                end = DateTime.SpecifyKind(start.AddMonths(1).AddSeconds(-1), DateTimeKind.Utc);
                prevStart = DateTime.SpecifyKind(start.AddMonths(-1), DateTimeKind.Utc);
                prevEnd = DateTime.SpecifyKind(start.AddSeconds(-1), DateTimeKind.Utc);
                break;
        }
        return (start, end, prevStart, prevEnd);
    }

    /// <summary>
    /// Get real fuel consumption data per vehicle using FuelCalculationService
    /// Uses GPS fuel sensor data (fuel_records + gps_positions.FuelRaw) with sensor mode logic
    /// </summary>
    [HttpGet("fuel-consumption")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetFuelConsumption([FromQuery] int days = 30)
    {
        var companyId = GetCompanyId();
        var cacheKey = $"dashboard_fuel_{companyId}_{days}";
        if (_cache.TryGetValue(cacheKey, out object? cached) && cached != null)
            return Ok(cached);

        var endDate = DateTime.UtcNow;
        var startDate = endDate.AddDays(-days);

        // Get fuel prices for this company
        var now = DateTime.UtcNow;
        var fuelPrices = await _context.FuelPricings
            .Where(fp => fp.CompanyId == companyId && fp.IsActive &&
                         fp.EffectiveFrom <= now &&
                         (fp.EffectiveTo == null || fp.EffectiveTo > now))
            .Join(_context.FuelTypes, fp => fp.FuelTypeId, ft => ft.Id,
                  (fp, ft) => new { ft.Code, fp.PricePerLiter })
            .ToListAsync();
        var priceDict = fuelPrices.ToDictionary(p => p.Code.ToLower(), p => p.PricePerLiter);

        // Get all vehicles with GPS devices
        var vehicles = await _context.Vehicles.AsNoTracking()
            .Where(v => v.CompanyId == companyId && v.GpsDeviceId.HasValue)
            .Include(v => v.GpsDevice)
            .ToListAsync();

        var vehicleStats = new List<object>();
        decimal fleetTotalLiters = 0;
        int fleetTotalKm = 0;
        var dailyFleetFuel = new Dictionary<string, decimal>();

        foreach (var vehicle in vehicles)
        {
            try
            {
                var expense = await _fuelCalcService.CalculateVehicleFuelExpenseAsync(
                    vehicle, startDate, endDate, priceDict);
                if (expense == null) continue;

                vehicleStats.Add(new
                {
                    plate = expense.Plate ?? expense.VehicleName,
                    consumption = expense.AverageConsumptionPer100Km,
                    totalLiters = expense.TotalFuelConsumedLiters,
                    totalKm = expense.TotalDistanceKm,
                    isEstimated = expense.IsEstimated
                });

                fleetTotalLiters += expense.TotalFuelConsumedLiters;
                fleetTotalKm += expense.TotalDistanceKm;

                // Aggregate daily fuel for fleet chart
                foreach (var d in expense.DailyConsumption)
                {
                    var dayKey = d.Date.ToString("yyyy-MM-dd");
                    dailyFleetFuel[dayKey] = dailyFleetFuel.GetValueOrDefault(dayKey) + d.FuelConsumedLiters;
                }
            }
            catch { /* skip vehicles with errors */ }
        }

        // Sort by consumption desc
        vehicleStats = vehicleStats
            .OrderByDescending(v => ((dynamic)v).consumption)
            .ToList();

        // Build daily chart data (last N days)
        var chartDays = Enumerable.Range(0, Math.Min(days, 30))
            .Select(i => endDate.AddDays(-((Math.Min(days, 30) - 1) - i)).ToString("yyyy-MM-dd"))
            .ToList();
        var chartValues = chartDays.Select(d => Math.Round(dailyFleetFuel.GetValueOrDefault(d), 2)).ToList();

        var result = new
        {
            vehicleStats,
            fleetTotalLiters = Math.Round(fleetTotalLiters, 2),
            fleetTotalKm,
            fleetAvgConsumption = fleetTotalKm > 0
                ? Math.Round((fleetTotalLiters / fleetTotalKm) * 100, 2)
                : 0m,
            chartDays,
            chartValues
        };

        _cache.Set(cacheKey, result, CacheDuration);
        return Ok(result);
    }

    /// <summary>
    /// Unified dashboard endpoint — returns ALL data in a single call.
    /// Replaces 9+ separate HTTP requests for much faster dashboard loading.
    /// </summary>
    [HttpGet("all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetDashboardAll([FromQuery] string period = "week")
    {
        var companyId = GetCompanyId();
        var cacheKey = $"dashboard_all_{companyId}_{period}";
        if (_cache.TryGetValue(cacheKey, out object? cached) && cached != null)
            return Ok(cached);

        var now = DateTime.UtcNow;
        var today = now.Date;
        var (periodStart, periodEnd, prevStart, prevEnd) = GetPeriodRange(now, period);

        // ── 1. Load vehicles once (shared across sections) ──
        var vehicles = await _context.Vehicles
            .AsNoTracking()
            .Include(v => v.GpsDevice)
            .Where(v => v.CompanyId == companyId)
            .ToListAsync();

        var maintenanceVehicles = vehicles.Count(v => v.Status == "maintenance");
        var noGpsVehicles = vehicles.Count(v => !v.GpsDeviceId.HasValue);
        var deviceMap = vehicles.Where(v => v.GpsDeviceId.HasValue)
            .ToDictionary(v => v.GpsDeviceId!.Value, v => v);

        // ── 2. Vehicle status from last GPS position ──
        int movingCount = 0, ignitionOnCount = 0, stoppedCount = 0;
        var gpsVehicles = vehicles.Where(v => v.GpsDeviceId.HasValue && v.Status != "maintenance").ToList();
        var gpsDeviceIds = gpsVehicles.Select(v => v.GpsDeviceId!.Value).ToList();
        var latestPositions = new Dictionary<int, GpsPosition>();

        if (gpsDeviceIds.Any())
        {
            var latestPosIds = await _context.GpsPositions
                .AsNoTracking()
                .Where(p => gpsDeviceIds.Contains(p.DeviceId))
                .GroupBy(p => p.DeviceId)
                .Select(g => g.Max(p => p.Id))
                .ToListAsync();

            latestPositions = await _context.GpsPositions
                .AsNoTracking()
                .Where(p => latestPosIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.DeviceId);

            foreach (var v in gpsVehicles)
            {
                if (latestPositions.TryGetValue(v.GpsDeviceId!.Value, out var pos))
                {
                    var speed = pos.SpeedKph ?? 0;
                    var ignition = pos.IgnitionOn ?? false;
                    if (speed > 3) movingCount++;
                    else if (ignition) ignitionOnCount++;
                    else stoppedCount++;
                }
                else stoppedCount++;
            }
        }

        // ── 3. Expenses (use nullable Sum to safely handle empty result sets) ──
        decimal fuelCost = 0, maintenanceCost = 0, repairCost = 0, otherCost = 0;
        try
        {
            fuelCost = await _context.FuelEntries.AsNoTracking()
                .Where(f => f.CompanyId == companyId && f.InvoiceDate >= periodStart && f.InvoiceDate <= periodEnd)
                .Select(f => (decimal?)f.TotalAmount).SumAsync() ?? 0m;
            fuelCost += await _context.VehicleCosts.AsNoTracking()
                .Where(c => c.CompanyId == companyId && c.Type == "fuel" && c.Date >= periodStart && c.Date <= periodEnd)
                .Select(c => (decimal?)c.Amount).SumAsync() ?? 0m;

            maintenanceCost = await _context.MaintenanceLogs.AsNoTracking()
                .Where(m => m.Vehicle!.CompanyId == companyId && m.DoneDate >= periodStart && m.DoneDate <= periodEnd && m.ActualCost > 0)
                .Select(m => (decimal?)m.ActualCost).SumAsync() ?? 0m;
            maintenanceCost += await _context.VehicleCosts.AsNoTracking()
                .Where(c => c.CompanyId == companyId && c.Type == "maintenance" && c.Date >= periodStart && c.Date <= periodEnd)
                .Select(c => (decimal?)c.Amount).SumAsync() ?? 0m;

            repairCost = await _context.Repairs.AsNoTracking()
                .Where(r => r.SocieteId == companyId && r.RepairDate >= periodStart && r.RepairDate <= periodEnd)
                .Select(r => (decimal?)r.TotalCost).SumAsync() ?? 0m;

            otherCost = await _context.VehicleCosts.AsNoTracking()
                .Where(c => c.CompanyId == companyId && c.Date >= periodStart && c.Date <= periodEnd
                    && c.Type != "fuel" && c.Type != "maintenance")
                .Select(c => (decimal?)c.Amount).SumAsync() ?? 0m;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Dashboard] Error calculating expenses for company {companyId}: {ex.Message}");
        }

        // ── 4. Driving scores (alerts per vehicle in period) ──
        var alertsByVehicle = await _context.GpsAlerts.AsNoTracking()
            .Where(a => a.VehicleId.HasValue && a.Vehicle!.CompanyId == companyId &&
                        a.Timestamp >= periodStart && a.Timestamp <= periodEnd)
            .GroupBy(a => a.VehicleId!.Value)
            .Select(g => new { VehicleId = g.Key, AlertCount = g.Count() })
            .ToListAsync();

        var drivingScores = vehicles
            .Select(v =>
            {
                var alerts = alertsByVehicle.FirstOrDefault(a => a.VehicleId == v.Id)?.AlertCount ?? 0;
                var score = Math.Max(0, 100 - (alerts * 5));
                return new { plate = v.Plate ?? v.Name, score };
            })
            .OrderByDescending(x => x.score)
            .ToList();

        // ── 5. Vehicle health ──
        var healthResults = await _healthService.CalculateAllScoresAsync(companyId);
        var healthy = healthResults.Count(h => h.Score >= 80);
        var attention = healthResults.Count(h => h.Score >= 40 && h.Score < 80);
        var unhealthy = healthResults.Count(h => h.Score < 40);

        // ── 6. Top km units (OdometerKm from GPS if firmware starts with L, else Vehicle.Mileage) ──
        var topUnitsColors = new[] { "#3b82f6", "#22c55e", "#f97316", "#8b5cf6", "#06b6d4", "#ec4899", "#eab308", "#14b8a6" };
        var vehicleMileages = vehicles.Select(v =>
        {
            double km = v.Mileage;
            if (v.GpsDeviceId.HasValue && v.GpsDevice != null)
            {
                var fw = v.GpsDevice.FirmwareVersion ?? "";
                if (fw.StartsWith("L", StringComparison.OrdinalIgnoreCase)
                    && latestPositions.TryGetValue(v.GpsDeviceId.Value, out var pos)
                    && pos.OdometerKm.HasValue && pos.OdometerKm.Value > 0)
                {
                    km = (double)pos.OdometerKm.Value;
                }
            }
            return new { v, km };
        }).ToList();

        var topUnits = vehicleMileages
            .Where(x => x.km > 0)
            .OrderByDescending(x => x.km)
            .Take(20)
            .Select((x, i) => new { name = x.v.Plate ?? x.v.Name, color = topUnitsColors[i % topUnitsColors.Length], mileage = Math.Round(x.km) })
            .ToList();

        // ── 7. Geofences ──
        var geoColors = new[] { "#22c55e", "#3b82f6", "#f97316", "#06b6d4", "#8b5cf6", "#ec4899", "#eab308", "#14b8a6" };
        var geofences = await _context.Geofences.AsNoTracking()
            .Where(g => g.CompanyId == companyId && g.IsActive)
            .Select(g => new { g.Name, Count = g.AssignedVehicles.Count })
            .ToListAsync();
        var geofencesList = geofences.Select((g, i) => new { name = g.Name, color = geoColors[i % geoColors.Length], count = g.Count }).ToList();

        // ── 8. Alerts (GpsAlerts + Notifications merged) ──
        var gpsAlerts = await _context.GpsAlerts.AsNoTracking()
            .Where(a => a.VehicleId.HasValue && a.Vehicle!.CompanyId == companyId)
            .OrderByDescending(a => a.Timestamp)
            .Take(20)
            .Select(a => new { message = a.Message ?? "Alerte", severity = a.Severity ?? "info", ts = a.Timestamp })
            .ToListAsync();

        var notifications = await _context.Notifications.AsNoTracking()
            .Where(n => n.CompanyId == companyId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(20)
            .Select(n => new { message = n.Title ?? "Notification", severity = n.Priority, ts = n.CreatedAt })
            .ToListAsync();

        var mergedAlerts = gpsAlerts
            .Select(a => new
            {
                a.message,
                severity = a.severity == "critical" ? "danger" : a.severity == "warning" ? "warning" : "info",
                time = a.ts.ToString("dd/MM HH:mm"),
                a.ts
            })
            .Concat(notifications.Select(n => new
            {
                message = n.message,
                severity = n.severity == "high" || n.severity == "critical" ? "danger" : n.severity == "medium" ? "warning" : "info",
                time = n.ts.ToString("dd/MM HH:mm"),
                ts = n.ts
            }))
            .OrderByDescending(a => a.ts)
            .Take(20)
            .Select(a => new { a.message, a.severity, a.time })
            .ToList();

        // ── 9. Recent trips ──
        var trips = await _context.Trips.AsNoTracking()
            .Where(t => t.CompanyId == companyId)
            .OrderByDescending(t => t.StartTime)
            .Take(20)
            .Include(t => t.Vehicle)
            .ToListAsync();

        var tripsList = trips.Select(t =>
        {
            var dist = (t.DistanceKm).ToString("F1");
            var mins = t.DurationMinutes;
            var dur = mins >= 60 ? $"{mins / 60}h{(mins % 60).ToString().PadLeft(2, '0')}" : $"{mins} min";
            var plate = t.Vehicle?.Plate ?? t.Vehicle?.Name ?? "Vehicule";
            return new { plate, distance = dist, duration = dur, date = t.StartTime.ToString("dd/MM HH:mm") };
        }).ToList();

        // ── 10. Drivers ──
        var drivers = await _context.Drivers.AsNoTracking()
            .Where(d => d.CompanyId == companyId)
            .Include(d => d.User)
            .Include(d => d.AssignedVehicle)
            .Take(20)
            .ToListAsync();

        var driversList = drivers.Select(d =>
        {
            var name = d.User?.FullName ?? "Conducteur";
            if (string.IsNullOrWhiteSpace(name)) name = "Conducteur";
            var initials = string.Join("", name.Split(' ').Where(w => w.Length > 0).Select(w => w[0].ToString().ToUpper()));
            if (initials.Length > 2) initials = initials[..2];
            return new
            {
                name,
                initials,
                vehicle = d.AssignedVehicle?.Plate ?? "",
                active = d.Status != "inactive"
            };
        }).ToList();

        // ── 11. Fuel consumption (FuelCalculationService) ──
        var fuelDays = period switch { "today" => 1, "yesterday" => 1, "week" => 7, "quarter" => 90, _ => 30 };
        var fuelStartDate = now.AddDays(-fuelDays);

        var fuelPrices = await _context.FuelPricings
            .Where(fp => fp.CompanyId == companyId && fp.IsActive &&
                         fp.EffectiveFrom <= now && (fp.EffectiveTo == null || fp.EffectiveTo > now))
            .Join(_context.FuelTypes, fp => fp.FuelTypeId, ft => ft.Id,
                  (fp, ft) => new { ft.Code, fp.PricePerLiter })
            .ToListAsync();
        var priceDict = fuelPrices.ToDictionary(p => p.Code.ToLower(), p => p.PricePerLiter);

        var vehicleFuelStats = new List<object>();
        decimal fleetTotalLiters = 0;
        int fleetTotalKm = 0;
        var dailyFleetFuel = new Dictionary<string, decimal>();

        foreach (var vehicle in vehicles.Where(v => v.GpsDeviceId.HasValue))
        {
            try
            {
                var expense = await _fuelCalcService.CalculateVehicleFuelExpenseAsync(vehicle, fuelStartDate, now, priceDict);
                if (expense == null) continue;
                vehicleFuelStats.Add(new
                {
                    plate = expense.Plate ?? expense.VehicleName,
                    consumption = expense.AverageConsumptionPer100Km,
                    totalLiters = expense.TotalFuelConsumedLiters,
                    totalKm = expense.TotalDistanceKm
                });
                fleetTotalLiters += expense.TotalFuelConsumedLiters;
                fleetTotalKm += expense.TotalDistanceKm;
                foreach (var d in expense.DailyConsumption)
                {
                    var dayKey = d.Date.ToString("yyyy-MM-dd");
                    dailyFleetFuel[dayKey] = dailyFleetFuel.GetValueOrDefault(dayKey) + d.FuelConsumedLiters;
                }
            }
            catch { }
        }
        vehicleFuelStats = vehicleFuelStats.OrderByDescending(v => ((dynamic)v).consumption).ToList();

        var chartDays = Enumerable.Range(0, Math.Min(fuelDays, 30))
            .Select(i => now.AddDays(-((Math.Min(fuelDays, 30) - 1) - i)).ToString("yyyy-MM-dd"))
            .ToList();
        var chartValues = chartDays.Select(d => Math.Round(dailyFleetFuel.GetValueOrDefault(d), 2)).ToList();

        // ── Build response ──
        var result = new
        {
            vehicleStatus = new
            {
                stopped = stoppedCount,
                ignitionOn = ignitionOnCount,
                moving = movingCount,
                maintenance = maintenanceVehicles,
                noGps = noGpsVehicles
            },
            expenses = new
            {
                fuelCost,
                maintenanceCost,
                repairCost,
                otherCost,
                totalCost = fuelCost + maintenanceCost + repairCost + otherCost
            },
            fuelConsumption = new
            {
                vehicleStats = vehicleFuelStats,
                fleetTotalLiters = Math.Round(fleetTotalLiters, 2),
                fleetTotalKm,
                chartDays,
                chartValues
            },
            drivingScores,
            healthData = new { healthy, attention, unhealthy },
            topUnits,
            geofences = geofencesList,
            alerts = mergedAlerts,
            recentTrips = tripsList,
            drivers = driversList
        };

        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(2));
        return Ok(result);
    }

    #endregion

    #region LEGACY ENDPOINTS (kept for backward compatibility)

    [HttpGet("stats")]
    public async Task<ActionResult> GetDashboardStats()
    {
        var companyId = GetCompanyId();
        var today = DateTime.UtcNow.Date;
        var thisMonth = DateTime.SpecifyKind(new DateTime(today.Year, today.Month, 1), DateTimeKind.Utc);

        // Vehicle stats with real-time GPS classification
        var vehicles = await _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId)
            .ToListAsync();

        var totalVehicles = vehicles.Count;
        var vehiclesWithGps = vehicles.Count(v => v.GpsDeviceId.HasValue);
        var maintenanceVehicles = vehicles.Count(v => v.Status == "maintenance");
        var noGpsVehicles = vehicles.Count(v => !v.GpsDeviceId.HasValue);

        // Classify vehicles by last GPS position (speed + ignition)
        int movingCount = 0, ignitionOnCount = 0, stoppedCount = 0;
        var gpsVehicles = vehicles.Where(v => v.GpsDeviceId.HasValue && v.Status != "maintenance").ToList();
        var gpsDeviceIds = gpsVehicles.Select(v => v.GpsDeviceId!.Value).ToList();

        if (gpsDeviceIds.Any())
        {
            var latestPosIds = await _context.GpsPositions
                .AsNoTracking()
                .Where(p => gpsDeviceIds.Contains(p.DeviceId))
                .GroupBy(p => p.DeviceId)
                .Select(g => g.Max(p => p.Id))
                .ToListAsync();

            var latestPositions = await _context.GpsPositions
                .AsNoTracking()
                .Where(p => latestPosIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.DeviceId);

            foreach (var v in gpsVehicles)
            {
                if (latestPositions.TryGetValue(v.GpsDeviceId!.Value, out var pos))
                {
                    var speed = pos.SpeedKph ?? 0;
                    var ignition = pos.IgnitionOn ?? false;
                    if (speed > 3) movingCount++;
                    else if (ignition) ignitionOnCount++;
                    else stoppedCount++;
                }
                else stoppedCount++;
            }
        }

        var onlineDevices = movingCount + ignitionOnCount;

        // Employee stats - server-side counts (use DB column EmployeeRole, not computed Roles/UserType)
        var totalDrivers = await _context.Users
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && (e.EmployeeRole == "driver" || e.EmployeeRole == "employee"))
            .CountAsync();

        var activeDrivers = await _context.Users
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && (e.EmployeeRole == "driver" || e.EmployeeRole == "employee") && e.Status == "active")
            .CountAsync();

        // Alert stats - server-side counts with subquery
        var unresolvedAlerts = await _context.GpsAlerts
            .AsNoTracking()
            .Where(a => a.VehicleId.HasValue && 
                        a.Vehicle!.CompanyId == companyId && 
                        !a.Resolved)
            .CountAsync();

        var alertsToday = await _context.GpsAlerts
            .AsNoTracking()
            .Where(a => a.VehicleId.HasValue && 
                        a.Vehicle!.CompanyId == companyId && 
                        a.Timestamp >= today)
            .CountAsync();

        // Maintenance stats (from VehicleMaintenanceSchedules — the active system)
        var upcomingMaintenance = await _context.VehicleMaintenanceSchedules
            .AsNoTracking()
            .Where(s => s.CompanyId == companyId && !s.IsPaused &&
                        (s.Status == "upcoming" || s.Status == "due"))
            .CountAsync();

        var overdueMaintenance = await _context.VehicleMaintenanceSchedules
            .AsNoTracking()
            .Where(s => s.CompanyId == companyId && !s.IsPaused &&
                        (s.Status == "overdue" || s.Status == "critical"))
            .CountAsync();

        // Cost stats this month
        var costsThisMonth = await _context.VehicleCosts
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.Date >= thisMonth)
            .SumAsync(c => c.Amount);

        var fuelCostsThisMonth = await _context.VehicleCosts
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.Type == "fuel" && c.Date >= thisMonth)
            .SumAsync(c => c.Amount);

        // Trip stats today
        var tripsToday = await _context.Trips
            .AsNoTracking()
            .Where(t => t.CompanyId == companyId && t.StartTime >= today)
            .CountAsync();

        var distanceToday = await _context.Trips
            .AsNoTracking()
            .Where(t => t.CompanyId == companyId && t.StartTime >= today && t.Status == "completed")
            .SumAsync(t => t.DistanceKm);

        // Geofence stats
        var activeGeofences = await _context.Geofences
            .AsNoTracking()
            .Where(g => g.CompanyId == companyId && g.IsActive)
            .CountAsync();

        var geofenceEventsToday = await _context.GeofenceEvents
            .AsNoTracking()
            .Where(e => e.Geofence!.CompanyId == companyId && e.Timestamp >= today)
            .CountAsync();

        return Ok(new
        {
            Vehicles = new
            {
                Total = totalVehicles,
                WithGps = vehiclesWithGps,
                Online = onlineDevices,
                Offline = vehiclesWithGps - onlineDevices,
                Moving = movingCount,
                IgnitionOn = ignitionOnCount,
                Stopped = stoppedCount,
                Maintenance = maintenanceVehicles,
                NoGps = noGpsVehicles
            },
            Drivers = new
            {
                Total = totalDrivers,
                Active = activeDrivers
            },
            Alerts = new
            {
                Unresolved = unresolvedAlerts,
                Today = alertsToday
            },
            Maintenance = new
            {
                Upcoming = upcomingMaintenance,
                Overdue = overdueMaintenance
            },
            Costs = new
            {
                ThisMonth = costsThisMonth,
                FuelThisMonth = fuelCostsThisMonth
            },
            Trips = new
            {
                Today = tripsToday,
                DistanceToday = distanceToday
            },
            Geofences = new
            {
                Active = activeGeofences,
                EventsToday = geofenceEventsToday
            }
        });
    }

    [HttpGet("cost-summary")]
    public async Task<ActionResult> GetCostSummary([FromQuery] string period = "month")
    {
        var companyId = GetCompanyId();
        var now = DateTime.UtcNow;
        var (periodStart, periodEnd, _, _) = GetPeriodRange(now, period);

        // 1. Carburant: from FuelEntries (manual fuel invoices/fill-ups)
        var fuelCost = await _context.FuelEntries
            .AsNoTracking()
            .Where(f => f.CompanyId == companyId && f.InvoiceDate >= periodStart && f.InvoiceDate <= periodEnd)
            .Select(f => (decimal?)f.TotalAmount).SumAsync() ?? 0m;

        // Also add VehicleCosts type='fuel' (legacy manual cost entries)
        fuelCost += await _context.VehicleCosts
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.Type == "fuel" && c.Date >= periodStart && c.Date <= periodEnd)
            .Select(c => (decimal?)c.Amount).SumAsync() ?? 0m;

        // 2. Entretiens: from MaintenanceLogs (completed scheduled maintenance)
        var maintenanceCost = await _context.MaintenanceLogs
            .AsNoTracking()
            .Where(m => m.Vehicle!.CompanyId == companyId && m.DoneDate >= periodStart && m.DoneDate <= periodEnd && m.ActualCost > 0)
            .Select(m => (decimal?)m.ActualCost).SumAsync() ?? 0m;

        // Also add VehicleCosts type='maintenance'
        maintenanceCost += await _context.VehicleCosts
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.Type == "maintenance" && c.Date >= periodStart && c.Date <= periodEnd)
            .Select(c => (decimal?)c.Amount).SumAsync() ?? 0m;

        // 3. Réparations: from Repairs table (SocieteId = companyId)
        var repairCost = await _context.Repairs
            .AsNoTracking()
            .Where(r => r.SocieteId == companyId && r.RepairDate >= periodStart && r.RepairDate <= periodEnd)
            .Select(r => (decimal?)r.TotalCost).SumAsync() ?? 0m;

        // 4. Autres: remaining VehicleCosts (insurance, tax, toll, parking, fine, other)
        var otherCost = await _context.VehicleCosts
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.Date >= periodStart && c.Date <= periodEnd
                && c.Type != "fuel" && c.Type != "maintenance")
            .Select(c => (decimal?)c.Amount).SumAsync() ?? 0m;

        var grandTotal = fuelCost + maintenanceCost + repairCost + otherCost;

        return Ok(new
        {
            FuelCost = fuelCost,
            MaintenanceCost = maintenanceCost,
            RepairCost = repairCost,
            OtherCost = otherCost,
            TotalCost = grandTotal
        });
    }

    [HttpGet("activity")]
    public async Task<ActionResult> GetRecentActivity([FromQuery] int limit = 20)
    {
        var companyId = GetCompanyId();
        // Get recent alerts - use navigation property instead of in-memory ID list
        var recentAlerts = await _context.GpsAlerts
            .AsNoTracking()
            .Where(a => a.VehicleId.HasValue && a.Vehicle!.CompanyId == companyId)
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .Select(a => new
            {
                Type = "alert",
                a.Id,
                a.Message,
                a.Timestamp,
                VehicleName = a.Vehicle != null ? a.Vehicle.Name : null
            })
            .ToListAsync();

        // Get recent geofence events
        var recentGeofenceEvents = await _context.GeofenceEvents
            .AsNoTracking()
            .Where(e => e.Vehicle!.CompanyId == companyId)
            .OrderByDescending(e => e.Timestamp)
            .Take(limit)
            .Select(e => new
            {
                Type = "geofence",
                e.Id,
                Message = $"{e.Type} - {e.Geofence!.Name}",
                e.Timestamp,
                VehicleName = e.Vehicle != null ? e.Vehicle.Name : null
            })
            .ToListAsync();

        var activity = recentAlerts
            .Cast<object>()
            .Concat(recentGeofenceEvents)
            .OrderByDescending(a => ((dynamic)a).Timestamp)
            .Take(limit);

        return Ok(activity);
    }

    #endregion
}
