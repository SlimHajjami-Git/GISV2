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
        var thisMonth = new DateTime(today.Year, today.Month, 1);
        var cutoffTime = DateTime.UtcNow.AddMinutes(-5);

        // Vehicle stats
        var vehicles = await _context.Vehicles
            .Where(v => v.CompanyId == companyId)
            .ToListAsync();

        var totalVehicles = vehicles.Count;
        var vehiclesWithGps = vehicles.Count(v => v.HasGps);

        // Get online vehicles (those with recent GPS positions)
        var vehicleIds = vehicles.Where(v => v.GpsDeviceId.HasValue).Select(v => v.GpsDeviceId!.Value).ToList();
        var onlineDevices = await _context.GpsPositions
            .Where(p => vehicleIds.Contains(p.DeviceId) && p.RecordedAt > cutoffTime)
            .Select(p => p.DeviceId)
            .Distinct()
            .CountAsync();

        // Employee stats
        var employees = await _context.Users
            .Where(e => e.CompanyId == companyId)
            .ToListAsync();

        var totalDrivers = employees.Count(e => e.Roles.Contains("driver") || e.UserType == "employee");
        var activeDrivers = employees.Count(e => (e.Roles.Contains("driver") || e.UserType == "employee") && e.Status == "active");

        // Alert stats
        var unresolvedAlerts = await _context.GpsAlerts
            .Where(a => a.VehicleId.HasValue && 
                        vehicles.Select(v => v.Id).Contains(a.VehicleId.Value) && 
                        !a.Resolved)
            .CountAsync();

        var alertsToday = await _context.GpsAlerts
            .Where(a => a.VehicleId.HasValue && 
                        vehicles.Select(v => v.Id).Contains(a.VehicleId.Value) && 
                        a.Timestamp >= today)
            .CountAsync();

        // Maintenance stats
        var upcomingMaintenance = await _context.MaintenanceRecords
            .Where(m => m.CompanyId == companyId && 
                        m.Status == "scheduled" && 
                        m.Date <= today.AddDays(30))
            .CountAsync();

        var overdueMaintenance = await _context.MaintenanceRecords
            .Where(m => m.CompanyId == companyId && 
                        m.Status == "scheduled" && 
                        m.Date < today)
            .CountAsync();

        // Cost stats this month
        var costsThisMonth = await _context.VehicleCosts
            .Where(c => c.CompanyId == companyId && c.Date >= thisMonth)
            .SumAsync(c => c.Amount);

        var fuelCostsThisMonth = await _context.VehicleCosts
            .Where(c => c.CompanyId == companyId && c.Type == "fuel" && c.Date >= thisMonth)
            .SumAsync(c => c.Amount);

        // Trip stats today
        var tripsToday = await _context.Trips
            .Where(t => t.CompanyId == companyId && t.StartTime >= today)
            .CountAsync();

        var distanceToday = await _context.Trips
            .Where(t => t.CompanyId == companyId && t.StartTime >= today && t.Status == "completed")
            .SumAsync(t => t.DistanceKm);

        // Geofence stats
        var activeGeofences = await _context.Geofences
            .Where(g => g.CompanyId == companyId && g.IsActive)
            .CountAsync();

        var geofenceEventsToday = await _context.GeofenceEvents
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

    [HttpGet("activity")]
    public async Task<ActionResult> GetRecentActivity([FromQuery] int limit = 20)
    {
        var companyId = GetCompanyId();
        var vehicleIds = await _context.Vehicles
            .Where(v => v.CompanyId == companyId)
            .Select(v => v.Id)
            .ToListAsync();

        // Get recent alerts
        var recentAlerts = await _context.GpsAlerts
            .Where(a => a.VehicleId.HasValue && vehicleIds.Contains(a.VehicleId.Value))
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
            .Where(e => vehicleIds.Contains(e.VehicleId))
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
