using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TripsController : ControllerBase
{
    private readonly GisDbContext _context;

    public TripsController(GisDbContext context)
    {
        _context = context;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");

    [HttpGet]
    public async Task<ActionResult<List<Trip>>> GetTrips(
        [FromQuery] int? vehicleId = null,
        [FromQuery] int? driverId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int limit = 100)
    {
        var companyId = GetCompanyId();

        var query = _context.Trips
            .Where(t => t.CompanyId == companyId)
            .Include(t => t.Vehicle)
            .Include(t => t.Driver)
            .AsQueryable();

        if (vehicleId.HasValue)
            query = query.Where(t => t.VehicleId == vehicleId);

        if (driverId.HasValue)
            query = query.Where(t => t.DriverId == driverId);

        if (startDate.HasValue)
            query = query.Where(t => t.StartTime >= startDate);

        if (endDate.HasValue)
            query = query.Where(t => t.EndTime <= endDate);

        var trips = await query
            .OrderByDescending(t => t.StartTime)
            .Take(limit)
            .ToListAsync();

        return Ok(trips);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Trip>> GetTrip(long id)
    {
        var companyId = GetCompanyId();

        var trip = await _context.Trips
            .Where(t => t.Id == id && t.CompanyId == companyId)
            .Include(t => t.Vehicle)
            .Include(t => t.Driver)
            .Include(t => t.Waypoints.OrderBy(w => w.SequenceNumber))
            .FirstOrDefaultAsync();

        if (trip == null)
            return NotFound();

        return Ok(trip);
    }

    [HttpGet("vehicle/{vehicleId}")]
    public async Task<ActionResult<List<Trip>>> GetVehicleTrips(
        int vehicleId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int limit = 50)
    {
        var companyId = GetCompanyId();

        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == vehicleId && v.CompanyId == companyId);

        if (vehicle == null)
            return NotFound();

        var query = _context.Trips
            .Where(t => t.VehicleId == vehicleId)
            .Include(t => t.Driver)
            .AsQueryable();

        if (startDate.HasValue)
            query = query.Where(t => t.StartTime >= startDate);

        if (endDate.HasValue)
            query = query.Where(t => t.EndTime <= endDate);

        var trips = await query
            .OrderByDescending(t => t.StartTime)
            .Take(limit)
            .ToListAsync();

        return Ok(trips);
    }

    [HttpGet("{id}/waypoints")]
    public async Task<ActionResult<List<TripWaypoint>>> GetTripWaypoints(long id)
    {
        var companyId = GetCompanyId();

        var trip = await _context.Trips
            .FirstOrDefaultAsync(t => t.Id == id && t.CompanyId == companyId);

        if (trip == null)
            return NotFound();

        var waypoints = await _context.TripWaypoints
            .Where(w => w.TripId == id)
            .OrderBy(w => w.SequenceNumber)
            .ToListAsync();

        return Ok(waypoints);
    }

    [HttpGet("summary")]
    public async Task<ActionResult> GetTripsSummary(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var companyId = GetCompanyId();
        startDate ??= DateTime.UtcNow.AddMonths(-1);
        endDate ??= DateTime.UtcNow;

        var trips = await _context.Trips
            .Where(t => t.CompanyId == companyId && 
                        t.StartTime >= startDate && 
                        t.EndTime <= endDate &&
                        t.Status == "completed")
            .ToListAsync();

        var summary = new
        {
            TotalTrips = trips.Count,
            TotalDistanceKm = trips.Sum(t => t.DistanceKm),
            TotalDurationMinutes = trips.Sum(t => t.DurationMinutes),
            TotalFuelConsumed = trips.Sum(t => t.FuelConsumedLiters ?? 0),
            AverageSpeedKph = trips.Average(t => t.AverageSpeedKph ?? 0),
            MaxSpeedKph = trips.Max(t => t.MaxSpeedKph ?? 0),
            TotalHarshBraking = trips.Sum(t => t.HarshBrakingCount ?? 0),
            TotalHarshAcceleration = trips.Sum(t => t.HarshAccelerationCount ?? 0),
            TotalOverspeeding = trips.Sum(t => t.OverspeedingCount ?? 0),
            Period = new { StartDate = startDate, EndDate = endDate }
        };

        return Ok(summary);
    }
}
