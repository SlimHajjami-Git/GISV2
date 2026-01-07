using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Common;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class POIController : ControllerBase
{
    private readonly GisDbContext _context;

    public POIController(GisDbContext context)
    {
        _context = context;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");

    // ==================== POINTS OF INTEREST ====================

    [HttpGet]
    public async Task<ActionResult<List<PoiDto>>> GetPOIs(
        [FromQuery] string? category = null,
        [FromQuery] string? tag = null,
        [FromQuery] bool? isActive = null)
    {
        var companyId = GetCompanyId();

        var query = _context.PointsOfInterest.Where(p => p.CompanyId == companyId);

        if (!string.IsNullOrEmpty(category))
            query = query.Where(p => p.Category == category);

        if (isActive.HasValue)
            query = query.Where(p => p.IsActive == isActive);

        var pois = await query
            .OrderBy(p => p.Name)
            .Select(p => new PoiDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Category = p.Category,
                SubCategory = p.SubCategory,
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                Radius = p.Radius,
                Address = p.Address,
                City = p.City,
                Phone = p.Phone,
                Email = p.Email,
                Website = p.Website,
                ContactName = p.ContactName,
                ExternalId = p.ExternalId,
                Color = p.Color,
                Icon = p.Icon,
                AlertOnArrival = p.AlertOnArrival,
                AlertOnDeparture = p.AlertOnDeparture,
                ExpectedStayMinutes = p.ExpectedStayMinutes,
                NotificationCooldownMinutes = p.NotificationCooldownMinutes,
                Tags = p.Tags,
                IsActive = p.IsActive,
                VisitCount = p.VisitCount,
                LastVisitAt = p.LastVisitAt,
                FuelBrand = p.FuelBrand,
                HasDiesel = p.HasDiesel,
                HasGasoline = p.HasGasoline,
                HasElectricCharging = p.HasElectricCharging,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            })
            .ToListAsync();

        // Filter by tag if specified (JSONB contains)
        if (!string.IsNullOrEmpty(tag))
        {
            pois = pois.Where(p => p.Tags != null && p.Tags.Contains(tag)).ToList();
        }

        return Ok(pois);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PoiDto>> GetPOI(int id)
    {
        var companyId = GetCompanyId();

        var poi = await _context.PointsOfInterest
            .Where(p => p.Id == id && p.CompanyId == companyId)
            .Select(p => new PoiDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Category = p.Category,
                SubCategory = p.SubCategory,
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                Radius = p.Radius,
                Address = p.Address,
                City = p.City,
                Phone = p.Phone,
                Email = p.Email,
                Website = p.Website,
                ContactName = p.ContactName,
                ExternalId = p.ExternalId,
                Color = p.Color,
                Icon = p.Icon,
                AlertOnArrival = p.AlertOnArrival,
                AlertOnDeparture = p.AlertOnDeparture,
                ExpectedStayMinutes = p.ExpectedStayMinutes,
                NotificationCooldownMinutes = p.NotificationCooldownMinutes,
                Tags = p.Tags,
                IsActive = p.IsActive,
                VisitCount = p.VisitCount,
                LastVisitAt = p.LastVisitAt,
                FuelBrand = p.FuelBrand,
                HasDiesel = p.HasDiesel,
                HasGasoline = p.HasGasoline,
                HasElectricCharging = p.HasElectricCharging,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            })
            .FirstOrDefaultAsync();

        if (poi == null)
            return NotFound();

        return Ok(poi);
    }

    [HttpPost]
    public async Task<ActionResult<PointOfInterest>> CreatePOI([FromBody] PointOfInterest poi)
    {
        var companyId = GetCompanyId();
        poi.CompanyId = companyId;
        poi.CreatedAt = DateTime.UtcNow;
        poi.UpdatedAt = DateTime.UtcNow;

        _context.PointsOfInterest.Add(poi);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetPOI), new { id = poi.Id }, poi);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> UpdatePOI(int id, [FromBody] PointOfInterest updated)
    {
        var companyId = GetCompanyId();

        var poi = await _context.PointsOfInterest
            .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId);

        if (poi == null)
            return NotFound();

        poi.Name = updated.Name;
        poi.Description = updated.Description;
        poi.Category = updated.Category;
        poi.SubCategory = updated.SubCategory;
        poi.Latitude = updated.Latitude;
        poi.Longitude = updated.Longitude;
        poi.Radius = updated.Radius;
        poi.Address = updated.Address;
        poi.City = updated.City;
        poi.Phone = updated.Phone;
        poi.Email = updated.Email;
        poi.Website = updated.Website;
        poi.ContactName = updated.ContactName;
        poi.ExternalId = updated.ExternalId;
        poi.Hours = updated.Hours;
        poi.Color = updated.Color;
        poi.Icon = updated.Icon;
        poi.AlertOnArrival = updated.AlertOnArrival;
        poi.AlertOnDeparture = updated.AlertOnDeparture;
        poi.ExpectedStayMinutes = updated.ExpectedStayMinutes;
        poi.NotificationCooldownMinutes = updated.NotificationCooldownMinutes;
        poi.Tags = updated.Tags;
        poi.IsActive = updated.IsActive;
        poi.FuelBrand = updated.FuelBrand;
        poi.HasDiesel = updated.HasDiesel;
        poi.HasGasoline = updated.HasGasoline;
        poi.HasElectricCharging = updated.HasElectricCharging;
        poi.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeletePOI(int id)
    {
        var companyId = GetCompanyId();

        var poi = await _context.PointsOfInterest
            .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId);

        if (poi == null)
            return NotFound();

        _context.PointsOfInterest.Remove(poi);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // ==================== POI VISITS ====================

    [HttpGet("{id}/visits")]
    public async Task<ActionResult<List<PoiVisitDto>>> GetPOIVisits(
        int id,
        [FromQuery] int limit = 50,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var companyId = GetCompanyId();

        var poi = await _context.PointsOfInterest
            .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId);

        if (poi == null)
            return NotFound();

        var query = _context.PoiVisits.Where(v => v.PoiId == id);

        if (from.HasValue)
            query = query.Where(v => v.ArrivalAt >= from.Value);

        if (to.HasValue)
            query = query.Where(v => v.ArrivalAt <= to.Value);

        var visits = await query
            .Include(v => v.Vehicle)
            .OrderByDescending(v => v.ArrivalAt)
            .Take(limit)
            .Select(v => new PoiVisitDto
            {
                Id = v.Id,
                PoiId = v.PoiId,
                VehicleId = v.VehicleId,
                VehicleName = v.Vehicle != null ? (v.Vehicle.Plate ?? v.Vehicle.Name) : null,
                ArrivalAt = v.ArrivalAt,
                DepartureAt = v.DepartureAt,
                DurationMinutes = v.DurationMinutes,
                ArrivalLat = v.ArrivalLat,
                ArrivalLng = v.ArrivalLng,
                DepartureLat = v.DepartureLat,
                DepartureLng = v.DepartureLng,
                Notes = v.Notes,
                IsNotified = v.IsNotified
            })
            .ToListAsync();

        return Ok(visits);
    }

    [HttpGet("visits/vehicle/{vehicleId}")]
    public async Task<ActionResult<List<PoiVisitDto>>> GetVehicleVisits(
        int vehicleId,
        [FromQuery] int limit = 50,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var companyId = GetCompanyId();

        var query = _context.PoiVisits
            .Where(v => v.VehicleId == vehicleId && v.CompanyId == companyId);

        if (from.HasValue)
            query = query.Where(v => v.ArrivalAt >= from.Value);

        if (to.HasValue)
            query = query.Where(v => v.ArrivalAt <= to.Value);

        var visits = await query
            .Include(v => v.Poi)
            .Include(v => v.Vehicle)
            .OrderByDescending(v => v.ArrivalAt)
            .Take(limit)
            .Select(v => new PoiVisitDto
            {
                Id = v.Id,
                PoiId = v.PoiId,
                PoiName = v.Poi != null ? v.Poi.Name : null,
                VehicleId = v.VehicleId,
                VehicleName = v.Vehicle != null ? (v.Vehicle.Plate ?? v.Vehicle.Name) : null,
                ArrivalAt = v.ArrivalAt,
                DepartureAt = v.DepartureAt,
                DurationMinutes = v.DurationMinutes,
                ArrivalLat = v.ArrivalLat,
                ArrivalLng = v.ArrivalLng,
                DepartureLat = v.DepartureLat,
                DepartureLng = v.DepartureLng,
                Notes = v.Notes,
                IsNotified = v.IsNotified
            })
            .ToListAsync();

        return Ok(visits);
    }

    // ==================== CATEGORIES ====================

    [HttpGet("categories")]
    public ActionResult GetCategories()
    {
        var categories = new[]
        {
            new { Value = "fuel_station", Label = "Station essence", Icon = "fuel" },
            new { Value = "garage", Label = "Garage", Icon = "wrench" },
            new { Value = "parking", Label = "Parking", Icon = "parking-circle" },
            new { Value = "warehouse", Label = "Entrepôt", Icon = "warehouse" },
            new { Value = "client", Label = "Client", Icon = "building-2" },
            new { Value = "supplier", Label = "Fournisseur", Icon = "truck" },
            new { Value = "checkpoint", Label = "Point de contrôle", Icon = "shield-check" },
            new { Value = "rest_area", Label = "Aire de repos", Icon = "coffee" },
            new { Value = "toll", Label = "Péage", Icon = "receipt" },
            new { Value = "border", Label = "Frontière", Icon = "flag" },
            new { Value = "other", Label = "Autre", Icon = "map-pin" }
        };

        return Ok(categories);
    }

    // ==================== STATISTICS ====================

    [HttpGet("stats")]
    public async Task<ActionResult> GetPOIStats([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
    {
        var companyId = GetCompanyId();
        from ??= DateTime.UtcNow.AddDays(-30);
        to ??= DateTime.UtcNow;

        var totalPOIs = await _context.PointsOfInterest
            .CountAsync(p => p.CompanyId == companyId);

        var activePOIs = await _context.PointsOfInterest
            .CountAsync(p => p.CompanyId == companyId && p.IsActive);

        var totalVisits = await _context.PoiVisits
            .CountAsync(v => v.CompanyId == companyId && v.ArrivalAt >= from && v.ArrivalAt <= to);

        var categoryStats = await _context.PointsOfInterest
            .Where(p => p.CompanyId == companyId)
            .GroupBy(p => p.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToListAsync();

        var topVisited = await _context.PointsOfInterest
            .Where(p => p.CompanyId == companyId)
            .OrderByDescending(p => p.VisitCount)
            .Take(5)
            .Select(p => new { p.Id, p.Name, p.Category, p.VisitCount })
            .ToListAsync();

        return Ok(new
        {
            TotalPOIs = totalPOIs,
            ActivePOIs = activePOIs,
            TotalVisits = totalVisits,
            ByCategory = categoryStats,
            TopVisited = topVisited,
            Period = new { From = from, To = to }
        });
    }
}

// ==================== DTOs ====================

public class PoiDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? SubCategory { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Radius { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? ContactName { get; set; }
    public string? ExternalId { get; set; }
    public string Color { get; set; } = "#3b82f6";
    public string? Icon { get; set; }
    public bool AlertOnArrival { get; set; }
    public bool AlertOnDeparture { get; set; }
    public int? ExpectedStayMinutes { get; set; }
    public int NotificationCooldownMinutes { get; set; }
    public string[]? Tags { get; set; }
    public bool IsActive { get; set; }
    public int VisitCount { get; set; }
    public DateTime? LastVisitAt { get; set; }
    public string? FuelBrand { get; set; }
    public bool? HasDiesel { get; set; }
    public bool? HasGasoline { get; set; }
    public bool? HasElectricCharging { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class PoiVisitDto
{
    public int Id { get; set; }
    public int PoiId { get; set; }
    public string? PoiName { get; set; }
    public int VehicleId { get; set; }
    public string? VehicleName { get; set; }
    public DateTime ArrivalAt { get; set; }
    public DateTime? DepartureAt { get; set; }
    public int? DurationMinutes { get; set; }
    public double ArrivalLat { get; set; }
    public double ArrivalLng { get; set; }
    public double? DepartureLat { get; set; }
    public double? DepartureLng { get; set; }
    public string? Notes { get; set; }
    public bool IsNotified { get; set; }
}
