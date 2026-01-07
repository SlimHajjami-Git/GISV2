using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly GisDbContext _context;

    public DashboardController(GisDbContext context)
    {
        _context = context;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");

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
        var employees = await _context.Employees
            .Where(e => e.CompanyId == companyId)
            .ToListAsync();

        var totalDrivers = employees.Count(e => e.Role == "driver");
        var activeDrivers = employees.Count(e => e.Role == "driver" && e.Status == "active");

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
}
