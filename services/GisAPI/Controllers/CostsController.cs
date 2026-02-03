using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CostsController : ControllerBase
{
    private readonly GisDbContext _context;

    public CostsController(GisDbContext context)
    {
        _context = context;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");
    private int GetUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

    [HttpGet]
    public async Task<ActionResult> GetCosts(
        [FromQuery] int? vehicleId = null,
        [FromQuery] string? type = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var companyId = GetCompanyId();

        var query = _context.VehicleCosts
            .Where(c => c.CompanyId == companyId)
            .Include(c => c.Vehicle)
            .AsQueryable();

        if (vehicleId.HasValue)
            query = query.Where(c => c.VehicleId == vehicleId);

        if (!string.IsNullOrEmpty(type))
            query = query.Where(c => c.Type == type);

        if (startDate.HasValue)
        {
            var startDateUtc = DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc);
            query = query.Where(c => c.Date >= startDateUtc);
        }

        if (endDate.HasValue)
        {
            var endDateUtc = DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc);
            query = query.Where(c => c.Date <= endDateUtc);
        }

        var costs = await query
            .OrderByDescending(c => c.Date)
            .Select(c => new {
                c.Id,
                c.VehicleId,
                VehicleName = c.Vehicle != null ? c.Vehicle.Name : null,
                VehiclePlate = c.Vehicle != null ? c.Vehicle.Plate : null,
                c.Type,
                c.Description,
                c.Amount,
                c.Date,
                c.Mileage,
                c.ReceiptNumber,
                c.CreatedAt
            })
            .ToListAsync();

        return Ok(costs);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<VehicleCost>> GetCost(int id)
    {
        var companyId = GetCompanyId();

        var cost = await _context.VehicleCosts
            .Where(c => c.Id == id && c.CompanyId == companyId)
            .Include(c => c.Vehicle)
            .FirstOrDefaultAsync();

        if (cost == null)
            return NotFound();

        return Ok(cost);
    }

    [HttpGet("summary")]
    public async Task<ActionResult> GetCostSummary(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var companyId = GetCompanyId();
        var startDateUtc = startDate.HasValue 
            ? DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc) 
            : DateTime.UtcNow.AddMonths(-1);
        var endDateUtc = endDate.HasValue 
            ? DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc) 
            : DateTime.UtcNow;

        var costs = await _context.VehicleCosts
            .Where(c => c.CompanyId == companyId && c.Date >= startDateUtc && c.Date <= endDateUtc)
            .GroupBy(c => c.Type)
            .Select(g => new
            {
                Type = g.Key,
                Total = g.Sum(c => c.Amount),
                Count = g.Count()
            })
            .ToListAsync();

        var totalFuel = await _context.VehicleCosts
            .Where(c => c.CompanyId == companyId && c.Type == "fuel" && c.Date >= startDateUtc && c.Date <= endDateUtc)
            .SumAsync(c => c.Liters ?? 0);

        return Ok(new
        {
            ByType = costs,
            TotalAmount = costs.Sum(c => c.Total),
            TotalFuelLiters = totalFuel,
            Period = new { StartDate = startDateUtc, EndDate = endDateUtc }
        });
    }

    [HttpPost]
    public async Task<ActionResult<VehicleCost>> CreateCost([FromBody] VehicleCost cost)
    {
        var companyId = GetCompanyId();
        var userId = GetUserId();

        cost.CompanyId = companyId;
        cost.CreatedByUserId = userId;
        cost.CreatedAt = DateTime.UtcNow;
        cost.UpdatedAt = DateTime.UtcNow;

        // Calculate total for fuel
        if (cost.Type == "fuel" && cost.Liters.HasValue && cost.PricePerLiter.HasValue)
        {
            cost.Amount = cost.Liters.Value * cost.PricePerLiter.Value;
        }

        _context.VehicleCosts.Add(cost);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetCost), new { id = cost.Id }, cost);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateCost(int id, [FromBody] VehicleCost updated)
    {
        var companyId = GetCompanyId();

        var cost = await _context.VehicleCosts
            .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == companyId);

        if (cost == null)
            return NotFound();

        cost.Type = updated.Type;
        cost.Description = updated.Description;
        cost.Amount = updated.Amount;
        cost.Date = updated.Date;
        cost.Mileage = updated.Mileage;
        cost.ReceiptNumber = updated.ReceiptNumber;
        cost.ReceiptUrl = updated.ReceiptUrl;
        cost.FuelType = updated.FuelType;
        cost.Liters = updated.Liters;
        cost.PricePerLiter = updated.PricePerLiter;

        // Recalculate for fuel
        if (cost.Type == "fuel" && cost.Liters.HasValue && cost.PricePerLiter.HasValue)
        {
            cost.Amount = cost.Liters.Value * cost.PricePerLiter.Value;
        }

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteCost(int id)
    {
        var companyId = GetCompanyId();

        var cost = await _context.VehicleCosts
            .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == companyId);

        if (cost == null)
            return NotFound();

        _context.VehicleCosts.Remove(cost);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
