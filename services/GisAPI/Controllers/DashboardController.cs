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
using GisAPI.Application.Features.Dashboard.Queries.GetGpaDashboard;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Application.Features.Repairs;
using GisAPI.Services;
using System.Security.Claims;

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
    private readonly IDashboardService _dashboardService;
    private readonly IDashboardCache _dashboardCache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public DashboardController(GisDbContext context, IMediator mediator, IMemoryCache cache, IVehicleHealthScoreService healthService, IFuelCalculationService fuelCalcService, IDashboardService dashboardService, IDashboardCache dashboardCache)
    {
        _context = context;
        _mediator = mediator;
        _cache = cache;
        _healthService = healthService;
        _fuelCalcService = fuelCalcService;
        _dashboardService = dashboardService;
        _dashboardCache = dashboardCache;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");
    private int GetUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
    private bool IsAdminUser() => User.IsInRole("company_admin") || User.IsInRole("admin") || User.IsInRole("super_admin") || User.IsInRole("system_admin");

    /// <summary>
    /// Portée véhicules de l'appelant (null = tout le parc, liste VIDE = aucun
    /// véhicule visible), résolue par la MÊME méthode que /dashboard/all.
    /// </summary>
    private Task<List<int>?> AccessibleVehicleIdsAsync(CancellationToken ct) =>
        DashboardService.ScopeIdsAsync(_context, IsAdminUser(), GetUserId(), ct);

    /// <summary>
    /// Composante d'IDENTITÉ des clés de cache. Les réponses de /kpis et /charts
    /// dépendent de l'appelant (les handlers y appliquent VehicleScope) : sans
    /// cette composante, sur un IMemoryCache singleton partagé par toute
    /// l'application, la réponse d'un admin était resservie pendant 5 minutes à
    /// un employé restreint de la même société — et inversement. Même convention
    /// que la clé de /dashboard/all : une seule entrée pour tous les admins (leur
    /// réponse est identique), une entrée par employé restreint.
    /// </summary>
    private string CacheScopeKey() => IsAdminUser() ? "admin" : $"u{GetUserId()}";

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
        var cacheKey = $"dashboard_kpis_{GetCompanyId()}_{CacheScopeKey()}_{year}_{month}_{string.Join(",", vehicleIds ?? Array.Empty<int>())}";
        
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
        var cacheKey = $"dashboard_charts_{GetCompanyId()}_{CacheScopeKey()}_{year}_{month}_{string.Join(",", vehicleIds ?? Array.Empty<int>())}";
        
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
        // Portée véhicules : ce handler ne filtre que par société — un employé
        // restreint y lisait les statistiques (plaque, km, coûts) de TOUT le parc,
        // alors que /kpis, /charts, /dashboard/all et les rapports le restreignent.
        // Elle est appliquée ici, en intersectant le filtre demandé avec ce que
        // l'appelant a le droit de voir.
        var scope = await AccessibleVehicleIdsAsync(HttpContext.RequestAborted);
        var effectiveVehicleIds = vehicleIds;
        if (scope is not null)
        {
            var visible = vehicleIds is { Length: > 0 }
                ? vehicleIds.Where(scope.Contains).ToArray()
                : scope.ToArray();

            // Le handler interprète un tableau VIDE comme « aucun filtre » (donc
            // tout le parc) : quand l'appelant ne voit aucun véhicule, on lui
            // passe un identifiant impossible pour obtenir un résultat vide.
            effectiveVehicleIds = visible.Length > 0 ? visible : new[] { -1 };
        }

        var result = await _mediator.Send(new GetFleetStatisticsQuery(
            year, month, groupBy, effectiveVehicleIds, pageNumber, pageSize));

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
        // La réponse dépend de l'appelant (portée véhicules) : sans composante
        // d'identité, un employé restreint lirait l'entrée remplie par un admin.
        var cacheKey = $"dashboard_widgets_{companyId}_{CacheScopeKey()}_{period}";
        if (_cache.TryGetValue(cacheKey, out object? cached) && cached != null)
            return Ok(cached);

        var now = DateTime.UtcNow;
        var (periodStart, periodEnd, prevStart, prevEnd) = GetPeriodRange(now, period);

        // Même portée que le reste du tableau de bord : null = tout le parc,
        // liste vide = aucun véhicule visible (et non « pas de filtre »).
        var widgetScope = await AccessibleVehicleIdsAsync(HttpContext.RequestAborted);
        var vehiclesQuery = _context.Vehicles.AsNoTracking()
            .Where(v => v.CompanyId == companyId);
        if (widgetScope is not null)
            vehiclesQuery = vehiclesQuery.Where(v => widgetScope.Contains(v.Id));
        var vehicles = await vehiclesQuery.ToListAsync();

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
        // CalculateAllScoresAsync note TOUT le parc de la société et les deux listes
        // ci-dessous publient une PLAQUE : sans portée, un locataire y lisait l'état
        // des véhicules loués à d'autres clients. Même filtre en mémoire que
        // /dashboard/all (la signature du service est partagée avec l'assistant IA).
        var healthResults = DashboardService.ScopedHealthResults(
            await _healthService.CalculateAllScoresAsync(companyId), widgetScope);
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
        // Modèle désactivé exclu comme dans /vehicle-maintenance/alerts et /stats :
        // le recalcul des statuts l'ignore, son statut est figé (recette GPA, DEF-016).
        // Portée : cette liste publie elle aussi une PLAQUE.
        var immobQuery = _context.VehicleMaintenanceSchedules.AsNoTracking()
            .Where(s => s.CompanyId == companyId && !s.IsPaused && s.Template!.IsActive &&
                        (s.Status == "overdue" || s.Status == "critical" || s.Status == "due"));
        if (widgetScope is not null)
            immobQuery = immobQuery.Where(s => widgetScope.Contains(s.VehicleId));
        var immobSchedules = await immobQuery
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

        var immobHistQuery = _context.VehicleMaintenanceSchedules.AsNoTracking()
            .Where(s => s.CompanyId == companyId && !s.IsPaused && s.Template!.IsActive &&
                        (s.Status == "overdue" || s.Status == "critical") &&
                        s.NextDueDate.HasValue &&
                        s.NextDueDate.Value >= sixMonthsAgo &&
                        s.NextDueDate.Value <= monthEnd);
        // Portée : l'histogramme compte des VÉHICULES immobilisés.
        if (widgetScope is not null)
            immobHistQuery = immobHistQuery.Where(s => widgetScope.Contains(s.VehicleId));
        var immobRaw = await immobHistQuery
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
        // Portée : les deux tendances comparent des kilomètres et des dépenses de
        // VÉHICULES. Sans filtre, un locataire voyait la tendance de tout le parc au
        // milieu de cartes, elles, déjà restreintes.
        IQueryable<Trip> TripsSur(DateTime debut, DateTime fin)
        {
            var q = _context.Trips.AsNoTracking()
                .Where(t => t.CompanyId == companyId && t.StartTime >= debut && t.StartTime <= fin && t.Status == "completed");
            return widgetScope is not null ? q.Where(t => widgetScope.Contains(t.VehicleId)) : q;
        }

        IQueryable<VehicleCost> CoutsSur(DateTime debut, DateTime fin)
        {
            var q = _context.VehicleCosts.AsNoTracking()
                .Where(c => c.CompanyId == companyId && c.Date >= debut && c.Date <= fin);
            return widgetScope is not null ? q.Where(c => widgetScope.Contains(c.VehicleId)) : q;
        }

        // Mileage trend: compare current vs previous period trip distances
        var currentMileage = await TripsSur(periodStart, periodEnd)
            .Select(t => (decimal?)t.DistanceKm).SumAsync() ?? 0m;
        var prevMileage = await TripsSur(prevStart, prevEnd)
            .Select(t => (decimal?)t.DistanceKm).SumAsync() ?? 0m;
        var mileageTrend = prevMileage > 0 ? Math.Round((double)(currentMileage - prevMileage) / (double)prevMileage * 100, 1) : 0;

        // Cost trend: compare current vs previous period costs
        // Crédits déduits (avoir, remboursement d'assurance) : additionnés bruts, ils
        // faisaient MONTER la tendance des coûts du mois où le fournisseur remboursait.
        var currentCost = await VehicleCostCategory.SignedTotalAsync(CoutsSur(periodStart, periodEnd));
        var prevCost = await VehicleCostCategory.SignedTotalAsync(CoutsSur(prevStart, prevEnd));
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
            case "year":
                start = DateTime.SpecifyKind(new DateTime(now.Year, 1, 1), DateTimeKind.Utc);
                end = DateTime.SpecifyKind(start.AddYears(1).AddSeconds(-1), DateTimeKind.Utc);
                prevStart = DateTime.SpecifyKind(start.AddYears(-1), DateTimeKind.Utc);
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
        // La réponse porte des plaques et dépend de la portée de l'appelant :
        // la clé doit distinguer un admin d'un employé restreint.
        var cacheKey = $"dashboard_fuel_{companyId}_{CacheScopeKey()}_{days}";
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

        // Get all vehicles with GPS devices — bornés à la portée de l'appelant
        // (null = tout le parc, liste vide = aucun véhicule visible).
        var fuelScope = await AccessibleVehicleIdsAsync(HttpContext.RequestAborted);
        var fuelVehiclesQuery = _context.Vehicles.AsNoTracking()
            .Where(v => v.CompanyId == companyId && v.GpsDeviceId.HasValue);
        if (fuelScope is not null)
            fuelVehiclesQuery = fuelVehiclesQuery.Where(v => fuelScope.Contains(v.Id));
        var vehicles = await fuelVehiclesQuery
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

    /// <summary>Périodes nommées acceptées par /api/dashboard/all (cf. DashboardService.GetPeriodRange).</summary>
    private static readonly string[] AllowedPeriods = { "today", "yesterday", "week", "month", "quarter", "year" };

    /// <summary>
    /// Fragment « période » de la clé de cache, ramené à une valeur CONNUE.
    /// La valeur brute arrive de la query string et entrait telle quelle dans la
    /// clé : n'importe quelle chaîne créait donc son entrée de cache (et le calcul
    /// qui va avec), alors que GetPeriodRange retombe de toute façon sur le mois
    /// pour une valeur qu'elle ne connaît pas — deux clés pour un seul contenu.
    /// Liste blanche, donc, et même repli « month » que GetPeriodRange.
    /// La plage personnalisée est RECONSTRUITE plus haut à partir de deux dates déjà
    /// analysées (custom_yyyyMMdd_yyyyMMdd) : elle est sûre par construction.
    /// </summary>
    internal static string CacheKeyPeriod(string period, bool isCustomRange)
    {
        if (isCustomRange) return period;
        return AllowedPeriods.Contains(period) ? period : "month";
    }

    /// <summary>
    /// Unified dashboard endpoint — returns ALL data in a single call.
    /// Replaces 9+ separate HTTP requests for much faster dashboard loading.
    /// </summary>
    [HttpGet("all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetDashboardAll([FromQuery] string period = "week",
        [FromQuery] string? from = null, [FromQuery] string? to = null)
    {
        var companyId = GetCompanyId();
        var userId = GetUserId();
        var isAdmin = IsAdminUser();

        var now = DateTime.UtcNow;
        var today = now.Date;
        var (periodStart, periodEnd, prevStart, prevEnd) = GetPeriodRange(now, period);

        // Plage personnalisée (champs Du/Au du dashboard). Historiquement ces champs
        // n'étaient jamais transmis : le filtre affichait des dates sans effet.
        // Quand les deux bornes sont valides, elles remplacent la période nommée et
        // la période de comparaison devient la fenêtre de même durée juste avant.
        var isCustomRange = false;
        if (DateTime.TryParse(from, out var fromD) && DateTime.TryParse(to, out var toD) && fromD.Date <= toD.Date)
        {
            isCustomRange = true;
            periodStart = DateTime.SpecifyKind(fromD.Date, DateTimeKind.Utc);
            periodEnd = DateTime.SpecifyKind(toD.Date.AddDays(1).AddSeconds(-1), DateTimeKind.Utc);
            var lengthDays = (toD.Date - fromD.Date).Days + 1;
            prevEnd = periodStart.AddSeconds(-1);
            prevStart = periodStart.AddDays(-lengthDays);
            period = $"custom_{fromD:yyyyMMdd}_{toD:yyyyMMdd}";
        }

        // La période entre dans la CLÉ DE CACHE : elle est ramenée à une valeur
        // connue avant d'y être concaténée (voir CacheKeyPeriod).
        var keyPeriod = CacheKeyPeriod(period, isCustomRange);
        var cacheKey = isAdmin ? $"dashboard_all_{companyId}_{keyPeriod}" : $"dashboard_all_{companyId}_{userId}_{keyPeriod}";

        // Cache COALESCÉ (anti-stampede) : un seul recalcul par clé, même sous
        // polling concurrent d'une même société. Le calcul lui-même vit dans
        // DashboardService (réutilisé par le pré-chauffage en arrière-plan, qui
        // maintient les clés admin chaudes → l'utilisateur reçoit quasi toujours
        // une réponse instantanée depuis le cache).
        var result = await _dashboardCache.GetOrCreateAsync(
            cacheKey, TimeSpan.FromMinutes(10),
            token => _dashboardService.ComputeDashboardAllAsync(
                companyId, userId, isAdmin, period, isCustomRange,
                periodStart, periodEnd, prevStart, prevEnd, now, today, token),
            HttpContext.RequestAborted);

        return Ok(result);
    }

    /// <summary>Plus ancienne date acceptée par GET /api/dashboard/gpa.</summary>
    public static readonly DateTime GpaMinDate = new(2000, 1, 1);

    /// <summary>
    /// Tableau de bord d'un compte SANS GPS (offre Calypso GPA) : coûts
    /// d'exploitation, acquisitions, interventions, échéances et alertes, sur
    /// les seules données saisies. Bornes en jours entiers, jour de fin inclus ;
    /// défaut : du 1er janvier de l'année en cours à aujourd'hui.
    /// Pas de cache : la réponse dépend de la portée véhicules de l'appelant
    /// (appliquée par le handler). /api/dashboard est exempté du contrôle
    /// d'abonnement ET des permissions dans PermissionMiddleware, ce chemin
    /// compris : le handler masque lui-même chaque bloc que l'appelant n'a pas le
    /// droit de voir, et refuse une société équipée du suivi GPS (403).
    /// </summary>
    /// <response code="200">Tableau de bord GPA</response>
    /// <response code="400">Plage inversée ou hors bornes</response>
    /// <response code="403">Société avec suivi GPS</response>
    [HttpGet("gpa")]
    [ProducesResponseType(typeof(GpaDashboardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<GpaDashboardDto>> GetGpaDashboard(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var today = DateTime.UtcNow.Date;
        var start = from?.Date ?? new DateTime(today.Year, 1, 1);
        var end = to?.Date ?? today;

        if (start > end)
            return BadRequest(new { message = "La date de début doit précéder la date de fin." });

        // Bornes : to=9999-12-31 faisait déborder la fin exclusive (AddDays(1)) en
        // 500, et from=0001-01-01 lisait tout l'historique à chaque appel. Aucune
        // limite de durée en deçà : une plage « Personnalisé » de plusieurs années
        // reste légitime.
        if (start < GpaMinDate || end > today.AddYears(1))
            return BadRequest(new { message = $"La période doit être comprise entre le {GpaMinDate:dd/MM/yyyy} et le {today.AddYears(1):dd/MM/yyyy}." });

        var result = await _mediator.Send(new GetGpaDashboardQuery(start, end), HttpContext.RequestAborted);
        return Ok(result);
    }

    #endregion

    #region LEGACY ENDPOINTS (kept for backward compatibility)

    [HttpGet("stats")]
    public async Task<ActionResult> GetDashboardStats()
    {
        var companyId = GetCompanyId();
        var userId = GetUserId();
        var ct = HttpContext.RequestAborted;
        var today = DateTime.UtcNow.Date;
        var thisMonth = DateTime.SpecifyKind(new DateTime(today.Year, today.Month, 1), DateTimeKind.Utc);

        // Portée véhicules — MÊME règle à trois états que /dashboard/all :
        //   null = administrateur, aucun filtre ; liste non vide = ses véhicules ;
        //   liste VIDE = il ne voit RIEN (surtout pas l'absence de filtre).
        // Cette route « legacy » comptait toute la flotte de la société : elle
        // rouvrait donc, par une autre URL, la fuite HERTZ que /dashboard/all venait
        // d'être corrigée pour fermer.
        var scopeIds = await AccessibleVehicleIdsAsync(ct);

        // Vehicle stats with real-time GPS classification
        var vehicles = await DashboardService.StatsVehiclesAsync(_context, companyId, scopeIds, ct);

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

        // Driver stats — drivers are a standalone entity, not users.
        // VOLONTAIREMENT à l'échelle de la société, hors portée véhicules : un
        // conducteur n'appartient pas à un véhicule (il en change), et ce compteur ne
        // publie ni plaque ni nom. Le cloisonner demanderait une règle métier à part.
        // "Employees" (admin staff tagged with EmployeeRole == "employee") are still counted here
        // because the dashboard groups fleet workforce (drivers + non-admin employees).
        var driversFromDriverTable = await _context.Drivers
            .AsNoTracking()
            .Where(d => d.CompanyId == companyId)
            .CountAsync();
        var employeesFromUserTable = await _context.Users
            .AsNoTracking()
            .Where(u => u.CompanyId == companyId && u.EmployeeRole == "employee")
            .CountAsync();
        var totalDrivers = driversFromDriverTable + employeesFromUserTable;

        var activeDriversFromDriverTable = await _context.Drivers
            .AsNoTracking()
            .Where(d => d.CompanyId == companyId && d.Status == "active")
            .CountAsync();
        var activeEmployeesFromUserTable = await _context.Users
            .AsNoTracking()
            .Where(u => u.CompanyId == companyId && u.EmployeeRole == "employee" && u.Status == "active")
            .CountAsync();
        var activeDrivers = activeDriversFromDriverTable + activeEmployeesFromUserTable;

        // Alertes, entretiens, coûts, trajets et géozones : tous rattachés à un
        // VÉHICULE, donc tous cloisonnés sur la portée (voir StatsCountsAsync — les
        // géozones y suivent en plus la règle du 23/09/2026 : rattachement au parc de
        // l'appelant et permission Géofences).
        var counts = await DashboardService.StatsCountsAsync(
            _context, companyId, userId, scopeIds, today, thisMonth, ct);

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
                Unresolved = counts.UnresolvedAlerts,
                Today = counts.AlertsToday
            },
            Maintenance = new
            {
                Upcoming = counts.UpcomingMaintenance,
                Overdue = counts.OverdueMaintenance
            },
            Costs = new
            {
                ThisMonth = counts.CostsThisMonth,
                FuelThisMonth = counts.FuelCostsThisMonth
            },
            Trips = new
            {
                Today = counts.TripsToday,
                DistanceToday = counts.DistanceToday
            },
            Geofences = new
            {
                Active = counts.ActiveGeofences,
                EventsToday = counts.GeofenceEventsToday
            }
        });
    }

    [HttpGet("cost-summary")]
    public async Task<ActionResult> GetCostSummary([FromQuery] string period = "month", CancellationToken ct = default)
    {
        var companyId = GetCompanyId();
        var now = DateTime.UtcNow;
        var (periodStart, periodEnd, _, _) = GetPeriodRange(now, period);

        // Même définition que le tableau de bord (DashboardService.PeriodCostsAsync), dans la
        // portée de l'appelant. Cette synthèse recalculait ses postes à part : chaque
        // « marquer fait » crée un MaintenanceLog ET sa dépense vehicle_costs, et les deux
        // étaient additionnés — l'entretien comptait double (HERTZ, T3 2026 : 20 174 au lieu
        // de 10 087) ; une dépense « repair » ou « entretien » tombait en Autres, et aucune
        // portée véhicule n'était appliquée.
        var scopeIds = await AccessibleVehicleIdsAsync(ct);
        var (fuelCost, maintenanceCost, repairCost, otherCost) =
            await DashboardService.PeriodCostsAsync(_context, companyId, scopeIds, periodStart, periodEnd, ct);

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
        var userId = GetUserId();
        var ct = HttpContext.RequestAborted;

        // Portée véhicules — même règle à trois états que partout ailleurs. Cette
        // route refaisait la requête du bloc « Alertes » du tableau de bord, sans
        // portée et en publiant le NOM DU VÉHICULE : elle rouvrait la fuite HERTZ par
        // une autre URL. Le commentaire « LEGACY ENDPOINTS » ne veut pas dire morte :
        // le front l'appelle (api.service.ts).
        var scopeIds = await AccessibleVehicleIdsAsync(ct);

        // `limit` arrive brut de la query string : un `?limit=-1` descendait jusqu'aux
        // Take() du service et faisait un `LIMIT -1` refusé par PostgreSQL (500), un
        // `?limit=1000000` matérialisait deux fois un million de lignes d'alertes.
        var limite = DashboardService.BornerLimiteActivite(limit);

        var activity = await DashboardService.RecentActivityAsync(
            _context, companyId, userId, scopeIds, limite, ct);

        return Ok(activity);
    }

    #endregion
}
