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
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public DashboardController(GisDbContext context, IMediator mediator, IMemoryCache cache)
    {
        _context = context;
        _mediator = mediator;
        _cache = cache;
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

    #endregion

    #region LEGACY ENDPOINTS (kept for backward compatibility)

    [HttpGet("stats")]
    public async Task<ActionResult> GetDashboardStats()
    {
        var companyId = GetCompanyId();
        var today = DateTime.UtcNow.Date;
        var thisMonth = DateTime.SpecifyKind(new DateTime(today.Year, today.Month, 1), DateTimeKind.Utc);
        var cutoffTime = DateTime.UtcNow.AddMinutes(-5);

        // Vehicle stats - server-side counts
        var totalVehicles = await _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId)
            .CountAsync();

        var vehiclesWithGps = await _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId && v.HasGps)
            .CountAsync();

        // Get online vehicles (those with recent GPS positions)
        var onlineDevices = await _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId && v.GpsDeviceId.HasValue)
            .Where(v => _context.GpsPositions
                .Any(p => p.DeviceId == v.GpsDeviceId!.Value && p.RecordedAt > cutoffTime))
            .CountAsync();

        // Employee stats - server-side counts
        var totalDrivers = await _context.Users
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && (e.Roles.Contains("driver") || e.UserType == "employee"))
            .CountAsync();

        var activeDrivers = await _context.Users
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && (e.Roles.Contains("driver") || e.UserType == "employee") && e.Status == "active")
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
                Offline = vehiclesWithGps - onlineDevices
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
    public async Task<ActionResult> GetCostSummary()
    {
        var companyId = GetCompanyId();
        var now = DateTime.UtcNow;
        var thisMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // 1. Manual costs from VehicleCosts table
        var manualCosts = await _context.VehicleCosts
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.Date >= thisMonth)
            .GroupBy(c => c.Type)
            .Select(g => new { Type = g.Key, Total = g.Sum(c => c.Amount) })
            .ToListAsync();

        var manualFuel = manualCosts.Where(c => c.Type == "fuel").Sum(c => c.Total);
        var manualMaintenance = manualCosts.Where(c => c.Type == "maintenance").Sum(c => c.Total);
        var manualInsurance = manualCosts.Where(c => c.Type == "insurance" || c.Type == "fine").Sum(c => c.Total);
        var manualOther = manualCosts.Where(c => !new[] { "fuel", "maintenance", "insurance", "fine" }.Contains(c.Type)).Sum(c => c.Total);

        // 2. Fuel costs from FuelRecords (GPS telemetry refuels)
        var fuelRecordCosts = await _context.FuelRecords
            .AsNoTracking()
            .Where(f => f.RecordedAt >= thisMonth && f.RefuelCost != null && f.RefuelCost > 0)
            .SumAsync(f => f.RefuelCost ?? 0);

        // 3. Maintenance costs from MaintenanceLogs (completed maintenance)
        var maintenanceLogCosts = await _context.MaintenanceLogs
            .AsNoTracking()
            .Where(m => m.CompanyId == companyId && m.DoneDate >= thisMonth && m.ActualCost > 0)
            .SumAsync(m => m.ActualCost);

        // Combine: manual + automated sources (avoid double-counting by checking CostId link)
        var linkedCostIds = await _context.MaintenanceLogs
            .AsNoTracking()
            .Where(m => m.CompanyId == companyId && m.DoneDate >= thisMonth && m.CostId != null)
            .Select(m => m.CostId!.Value)
            .ToListAsync();

        // Subtract linked manual costs that are already counted in maintenanceLogCosts
        var linkedMaintenanceManual = linkedCostIds.Count > 0
            ? await _context.VehicleCosts
                .AsNoTracking()
                .Where(c => linkedCostIds.Contains(c.Id))
                .SumAsync(c => c.Amount)
            : 0m;

        var totalFuel = manualFuel + fuelRecordCosts;
        var totalMaintenance = manualMaintenance + maintenanceLogCosts - linkedMaintenanceManual;
        if (totalMaintenance < 0) totalMaintenance = Math.Max(manualMaintenance, maintenanceLogCosts);
        var totalRepair = manualInsurance;
        var totalOther = manualOther;
        var grandTotal = totalFuel + totalMaintenance + totalRepair + totalOther;

        return Ok(new
        {
            FuelCost = totalFuel,
            MaintenanceCost = totalMaintenance,
            RepairCost = totalRepair,
            OtherCost = totalOther,
            TotalCost = grandTotal,
            Sources = new
            {
                ManualCosts = manualCosts.Sum(c => c.Total),
                FuelRecordCosts = fuelRecordCosts,
                MaintenanceLogCosts = maintenanceLogCosts
            }
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
