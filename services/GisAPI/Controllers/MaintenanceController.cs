using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Data;
using GisAPI.Models;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MaintenanceController : ControllerBase
{
    private readonly GisDbContext _context;

    public MaintenanceController(GisDbContext context)
    {
        _context = context;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");

    [HttpGet]
    public async Task<ActionResult<List<MaintenanceRecord>>> GetMaintenanceRecords([FromQuery] int? vehicleId = null)
    {
        var companyId = GetCompanyId();

        var query = _context.MaintenanceRecords
            .Where(m => m.CompanyId == companyId)
            .Include(m => m.Vehicle)
            .Include(m => m.Parts)
            .AsQueryable();

        if (vehicleId.HasValue)
            query = query.Where(m => m.VehicleId == vehicleId);

        var records = await query
            .OrderByDescending(m => m.Date)
            .ToListAsync();

        return Ok(records);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<MaintenanceRecord>> GetMaintenanceRecord(int id)
    {
        var companyId = GetCompanyId();

        var record = await _context.MaintenanceRecords
            .Where(m => m.Id == id && m.CompanyId == companyId)
            .Include(m => m.Vehicle)
            .Include(m => m.Parts)
            .FirstOrDefaultAsync();

        if (record == null)
            return NotFound();

        return Ok(record);
    }

    [HttpGet("upcoming")]
    public async Task<ActionResult<List<MaintenanceRecord>>> GetUpcomingMaintenance()
    {
        var companyId = GetCompanyId();
        var nextMonth = DateTime.UtcNow.AddMonths(1);

        var records = await _context.MaintenanceRecords
            .Where(m => m.CompanyId == companyId && 
                        m.Status == "scheduled" && 
                        m.Date <= nextMonth)
            .Include(m => m.Vehicle)
            .OrderBy(m => m.Date)
            .ToListAsync();

        return Ok(records);
    }

    [HttpPost]
    public async Task<ActionResult<MaintenanceRecord>> CreateMaintenanceRecord([FromBody] MaintenanceRecord record)
    {
        var companyId = GetCompanyId();
        record.CompanyId = companyId;
        record.CreatedAt = DateTime.UtcNow;
        record.TotalCost = record.LaborCost + record.PartsCost;

        _context.MaintenanceRecords.Add(record);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetMaintenanceRecord), new { id = record.Id }, record);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateMaintenanceRecord(int id, [FromBody] MaintenanceRecord updated)
    {
        var companyId = GetCompanyId();

        var record = await _context.MaintenanceRecords
            .FirstOrDefaultAsync(m => m.Id == id && m.CompanyId == companyId);

        if (record == null)
            return NotFound();

        record.Type = updated.Type;
        record.Description = updated.Description;
        record.MileageAtService = updated.MileageAtService;
        record.Date = updated.Date;
        record.NextServiceDate = updated.NextServiceDate;
        record.NextServiceMileage = updated.NextServiceMileage;
        record.Status = updated.Status;
        record.LaborCost = updated.LaborCost;
        record.PartsCost = updated.PartsCost;
        record.TotalCost = updated.LaborCost + updated.PartsCost;
        record.ServiceProvider = updated.ServiceProvider;
        record.ProviderContact = updated.ProviderContact;
        record.InvoiceNumber = updated.InvoiceNumber;
        record.InvoiceUrl = updated.InvoiceUrl;
        record.Notes = updated.Notes;
        record.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteMaintenanceRecord(int id)
    {
        var companyId = GetCompanyId();

        var record = await _context.MaintenanceRecords
            .FirstOrDefaultAsync(m => m.Id == id && m.CompanyId == companyId);

        if (record == null)
            return NotFound();

        _context.MaintenanceRecords.Remove(record);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("{id}/parts")]
    public async Task<ActionResult> AddPart(int id, [FromBody] MaintenancePart part)
    {
        var companyId = GetCompanyId();

        var record = await _context.MaintenanceRecords
            .FirstOrDefaultAsync(m => m.Id == id && m.CompanyId == companyId);

        if (record == null)
            return NotFound();

        part.MaintenanceRecordId = id;
        part.TotalCost = part.UnitCost * part.Quantity;

        _context.MaintenanceParts.Add(part);
        
        record.PartsCost += part.TotalCost;
        record.TotalCost = record.LaborCost + record.PartsCost;

        await _context.SaveChangesAsync();

        return Ok(part);
    }
}
