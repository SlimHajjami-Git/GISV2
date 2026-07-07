using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;
using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Application.Services;
using MediatR;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CostsController : ControllerBase
{
    private readonly GisDbContext _context;
    private readonly IPublisher _publisher;
    private readonly IInvoiceExtractionService _invoiceExtraction;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<CostsController> _logger;

    public CostsController(GisDbContext context, IPublisher publisher,
        IInvoiceExtractionService invoiceExtraction, IWebHostEnvironment env, ILogger<CostsController> logger)
    {
        _context = context;
        _publisher = publisher;
        _invoiceExtraction = invoiceExtraction;
        _env = env;
        _logger = logger;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");
    private int GetUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

    /// <summary>
    /// Scan an invoice (image or PDF) with AI and return the EXTRACTED fields for
    /// the user to review — nothing is saved to costs here. The invoice file is
    /// stored and its URL returned so the confirmed cost can reference it
    /// (receiptUrl). The user saves via the normal POST /api/costs afterwards.
    /// </summary>
    [HttpPost("scan-invoice")]
    [RequestSizeLimit(12_000_000)]
    public async Task<IActionResult> ScanInvoice(IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Aucun fichier reçu." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".pdf" };
        if (!allowed.Contains(ext))
            return BadRequest(new { message = "Format non supporté. Envoyez une image (JPG/PNG) ou un PDF." });

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        // Persist the invoice so the created cost can point at it (receiptUrl).
        var companyId = GetCompanyId();
        var dir = Path.Combine(_env.ContentRootPath, "uploads", "invoices", companyId.ToString());
        Directory.CreateDirectory(dir);
        var storedName = $"{Guid.NewGuid():N}{ext}";
        await System.IO.File.WriteAllBytesAsync(Path.Combine(dir, storedName), bytes, ct);
        var receiptUrl = $"/uploads/invoices/{companyId}/{storedName}";

        try
        {
            var extraction = await _invoiceExtraction.ExtractAsync(bytes, file.ContentType ?? "", file.FileName, ct);
            return Ok(new { extraction, receiptUrl });
        }
        catch (InvalidOperationException ex)
        {
            // Unsupported / scanned-PDF-with-no-text — the file is still stored.
            return BadRequest(new { message = ex.Message, receiptUrl });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invoice extraction failed");
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                message = "L'analyse de la facture a échoué (service IA momentanément indisponible). Le fichier est enregistré, vous pouvez saisir les champs manuellement.",
                receiptUrl
            });
        }
    }

    [HttpGet]
    public async Task<ActionResult> GetCosts(
        [FromQuery] int? vehicleId = null,
        [FromQuery] string? type = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var companyId = GetCompanyId();

        var query = _context.VehicleCosts
            .AsNoTracking()
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
                c.CreatedAt,
                // Calypso 7 — link to the accident timeline that produced
                // this cost (Phase 5 repair / Phase 6 insurance refund).
                c.AccidentEventId,
            })
            .ToListAsync();

        return Ok(costs);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<VehicleCost>> GetCost(int id)
    {
        var companyId = GetCompanyId();

        var cost = await _context.VehicleCosts
            .AsNoTracking()
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
            .AsNoTracking()
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
            .AsNoTracking()
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

        // Send notification to company admins
        try
        {
            var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            var vehicle = await _context.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == cost.VehicleId && v.CompanyId == companyId);
            if (actor != null)
            {
                var entityName = !string.IsNullOrEmpty(cost.Description)
                    ? cost.Description
                    : vehicle?.Name ?? vehicle?.Plate ?? "Véhicule";

                await _publisher.Publish(new AdminActionNotificationEvent(
                    companyId, userId, actor.FullName,
                    "cost_created", entityName, cost.Id, "cost"
                ));
            }
        }
        catch { /* Don't fail the request if notification fails */ }

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
