using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StatisticsController : ControllerBase
{
    private readonly GisDbContext _context;

    public StatisticsController(GisDbContext context)
    {
        _context = context;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");

    [HttpGet("daily")]
    public async Task<ActionResult<List<DailyStatistics>>> GetDailyStatistics(
        [FromQuery] int? vehicleId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var companyId = GetCompanyId();
        startDate ??= DateTime.UtcNow.AddDays(-30);
        endDate ??= DateTime.UtcNow;

        var query = _context.DailyStatistics
            .Where(s => s.CompanyId == companyId && 
                        s.Date >= DateOnly.FromDateTime(startDate.Value) && 
                        s.Date <= DateOnly.FromDateTime(endDate.Value))
            .Include(s => s.Vehicle)
            .AsQueryable();

        if (vehicleId.HasValue)
            query = query.Where(s => s.VehicleId == vehicleId);

        var stats = await query
            .OrderByDescending(s => s.Date)
            .ToListAsync();

        return Ok(stats);
    }

    [HttpGet("vehicle/{vehicleId}")]
    public async Task<ActionResult> GetVehicleStatistics(
        int vehicleId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var companyId = GetCompanyId();
        startDate ??= DateTime.UtcNow.AddDays(-30);
        endDate ??= DateTime.UtcNow;

        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == vehicleId && v.CompanyId == companyId);

        if (vehicle == null)
            return NotFound();

        var dailyStats = await _context.DailyStatistics
            .Where(s => s.VehicleId == vehicleId && 
                        s.Date >= DateOnly.FromDateTime(startDate.Value) && 
                        s.Date <= DateOnly.FromDateTime(endDate.Value))
            .OrderBy(s => s.Date)
            .ToListAsync();

        var summary = new
        {
            Vehicle = new { vehicle.Id, vehicle.Name, vehicle.Plate },
            Period = new { StartDate = startDate, EndDate = endDate },
            TotalDistanceKm = dailyStats.Sum(s => s.DistanceKm),
            TotalDrivingMinutes = dailyStats.Sum(s => s.DrivingTimeMinutes),
            TotalIdleMinutes = dailyStats.Sum(s => s.IdleTimeMinutes),
            TotalTrips = dailyStats.Sum(s => s.TripCount),
            AverageSpeedKph = dailyStats.Average(s => s.AverageSpeedKph ?? 0),
            MaxSpeedKph = dailyStats.Max(s => s.MaxSpeedKph ?? 0),
            TotalFuelConsumed = dailyStats.Sum(s => s.FuelConsumedLiters ?? 0),
            AverageFuelEfficiency = dailyStats.Average(s => s.FuelEfficiencyKmPerLiter ?? 0),
            TotalFuelCost = dailyStats.Sum(s => s.FuelCost ?? 0),
            TotalHarshBraking = dailyStats.Sum(s => s.HarshBrakingCount),
            TotalHarshAcceleration = dailyStats.Sum(s => s.HarshAccelerationCount),
            TotalOverspeeding = dailyStats.Sum(s => s.OverspeedingEvents),
            TotalAlerts = dailyStats.Sum(s => s.AlertCount),
            AverageDriverScore = dailyStats.Average(s => s.DriverScore ?? 0),
            DailyData = dailyStats
        };

        return Ok(summary);
    }

    [HttpGet("drivers")]
    public async Task<ActionResult<List<DriverScore>>> GetDriverScores(
        [FromQuery] int? driverId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var companyId = GetCompanyId();
        startDate ??= DateTime.UtcNow.AddDays(-30);
        endDate ??= DateTime.UtcNow;

        var query = _context.DriverScores
            .Where(s => s.CompanyId == companyId && 
                        s.Date >= DateOnly.FromDateTime(startDate.Value) && 
                        s.Date <= DateOnly.FromDateTime(endDate.Value))
            .Include(s => s.Driver)
            .AsQueryable();

        if (driverId.HasValue)
            query = query.Where(s => s.DriverId == driverId);

        var scores = await query
            .OrderByDescending(s => s.Date)
            .ToListAsync();

        return Ok(scores);
    }

    [HttpGet("drivers/{driverId}/summary")]
    public async Task<ActionResult> GetDriverSummary(
        int driverId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var companyId = GetCompanyId();
        startDate ??= DateTime.UtcNow.AddDays(-30);
        endDate ??= DateTime.UtcNow;

        var driver = await _context.Employees
            .FirstOrDefaultAsync(e => e.Id == driverId && e.CompanyId == companyId);

        if (driver == null)
            return NotFound();

        var scores = await _context.DriverScores
            .Where(s => s.DriverId == driverId && 
                        s.Date >= DateOnly.FromDateTime(startDate.Value) && 
                        s.Date <= DateOnly.FromDateTime(endDate.Value))
            .ToListAsync();

        var summary = new
        {
            Driver = new { driver.Id, driver.Name, driver.Email },
            Period = new { StartDate = startDate, EndDate = endDate },
            AverageOverallScore = scores.Average(s => s.OverallScore),
            AverageSpeedingScore = scores.Average(s => s.SpeedingScore),
            AverageBrakingScore = scores.Average(s => s.BrakingScore),
            AverageAccelerationScore = scores.Average(s => s.AccelerationScore),
            TotalDistanceKm = scores.Sum(s => s.DistanceKm),
            TotalDrivingMinutes = scores.Sum(s => s.DrivingTimeMinutes),
            TotalSpeedingEvents = scores.Sum(s => s.SpeedingEvents),
            TotalHarshBrakingEvents = scores.Sum(s => s.HarshBrakingEvents),
            TotalHarshAccelerationEvents = scores.Sum(s => s.HarshAccelerationEvents),
            DailyScores = scores.OrderBy(s => s.Date)
        };

        return Ok(summary);
    }
}
