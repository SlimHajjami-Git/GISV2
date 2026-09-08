using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Application.Services;
using MediatR;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CostsController : ControllerBase
{
    // IGisDbContext (et non le GisDbContext concret) : c'est l'abstraction que
    // toute la couche Application utilise déjà, et elle rend ce contrôleur
    // instanciable dans les tests — sans cela la portée véhicules ne pouvait
    // être vérifiée qu'en RECOPIANT la requête dans le test, qui restait donc
    // vert même quand le filtre disparaissait du contrôleur (constat du
    // 09/09/2026, cf. CostsScreenScopeTests).
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly IPublisher _publisher;
    private readonly IInvoiceExtractionService _invoiceExtraction;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<CostsController> _logger;

    public CostsController(IGisDbContext context, ICurrentTenantService tenant, IPublisher publisher,
        IInvoiceExtractionService invoiceExtraction, IWebHostEnvironment env, ILogger<CostsController> logger)
    {
        _context = context;
        _tenant = tenant;
        _publisher = publisher;
        _invoiceExtraction = invoiceExtraction;
        _env = env;
        _logger = logger;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");
    private int GetUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

    /// <summary>
    /// Charge UNE dépense de la société courante en respectant la portée
    /// véhicules de l'appelant (null = admin, tout le parc ; liste vide =
    /// aucun véhicule visible donc aucune dépense).
    ///
    /// Utilisé par la lecture ET par les mutations. Jusqu'au 09/09/2026 seule
    /// la lecture était cloisonnée : un employé restreint au véhicule #7
    /// recevait bien un 404 sur GET /api/costs/812 (dépense du véhicule #9)
    /// mais son DELETE /api/costs/812 renvoyait 204 et SUPPRIMAIT la ligne —
    /// un IDOR. Retourne null quand la dépense n'existe pas, appartient à une
    /// autre société, ou porte sur un véhicule hors portée : les trois cas
    /// répondent 404, pour ne jamais révéler l'existence de la ligne.
    /// </summary>
    private async Task<VehicleCost?> FindScopedCostAsync(
        int id, CancellationToken ct, bool tracked = true, bool includeVehicle = false)
    {
        var companyId = GetCompanyId();
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenant, ct);

        var query = _context.VehicleCosts.AsQueryable();
        if (!tracked)
            query = query.AsNoTracking();
        if (includeVehicle)
            query = query.Include(c => c.Vehicle);

        query = query.Where(c => c.Id == id && c.CompanyId == companyId);

        if (scope is not null)
            query = query.Where(c => scope.Contains(c.VehicleId));

        return await query.FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Le véhicule visé par une création de dépense doit appartenir à la
    /// société ET rester dans la portée de l'appelant — sinon un employé
    /// restreint pourrait imputer une dépense à un véhicule qu'il ne voit
    /// même pas dans sa liste.
    /// </summary>
    private async Task<bool> CanBookOnVehicleAsync(int vehicleId, CancellationToken ct)
    {
        var companyId = GetCompanyId();
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenant, ct);

        if (scope is not null && !scope.Contains(vehicleId))
            return false;

        return await _context.Vehicles
            .AsNoTracking()
            .AnyAsync(v => v.Id == vehicleId && v.CompanyId == companyId, ct);
    }

    /// <summary>Default monthly AI invoice-scan quota when the société has no
    /// explicit limit. The sys admin can raise/lower it per société from the
    /// admin company page (0 = feature disabled).</summary>
    private const int DefaultScanMonthlyLimit = 20;

    private async Task<(int Limit, int Used)> GetScanQuotaAsync(int companyId, CancellationToken ct)
    {
        var limit = await _context.Societes
            .AsNoTracking()
            .Where(s => s.Id == companyId)
            .Select(s => s.InvoiceScanMonthlyLimit)
            .FirstOrDefaultAsync(ct) ?? DefaultScanMonthlyLimit;

        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var used = await _context.InvoiceScanLogs
            .AsNoTracking()
            .CountAsync(l => l.CompanyId == companyId && l.CreatedAt >= monthStart, ct);

        return (limit, used);
    }

    /// <summary>Current company's monthly scan quota — drives the counter shown
    /// next to the "Scanner une facture" button.</summary>
    [HttpGet("scan-quota")]
    public async Task<ActionResult> GetScanQuota(CancellationToken ct)
    {
        var (limit, used) = await GetScanQuotaAsync(GetCompanyId(), ct);
        return Ok(new { used, limit, remaining = Math.Max(0, limit - used) });
    }

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

        // Monthly quota per société — checked BEFORE storing anything or paying
        // for a Groq call. Only successful scans count against the quota.
        var companyId = GetCompanyId();
        var (limit, used) = await GetScanQuotaAsync(companyId, ct);
        if (limit <= 0)
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Le scan de factures IA n'est pas activé pour votre société.",
                used, limit
            });
        if (used >= limit)
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                message = $"Quota mensuel de scans atteint ({used}/{limit}). Contactez votre administrateur pour augmenter la limite.",
                used, limit
            });

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        // Persist the invoice so the created cost can point at it (receiptUrl).
        var dir = Path.Combine(_env.ContentRootPath, "uploads", "invoices", companyId.ToString());
        Directory.CreateDirectory(dir);
        var storedName = $"{Guid.NewGuid():N}{ext}";
        await System.IO.File.WriteAllBytesAsync(Path.Combine(dir, storedName), bytes, ct);
        var receiptUrl = $"/uploads/invoices/{companyId}/{storedName}";

        try
        {
            var result = await _invoiceExtraction.ExtractAsync(bytes, file.ContentType ?? "", file.FileName, ct);

            // Count the scan against the monthly quota + audit token consumption.
            _context.InvoiceScanLogs.Add(new InvoiceScanLog
            {
                CompanyId = companyId,
                UserId = GetUserId(),
                TokensUsed = result.TokensUsed
            });
            await _context.SaveChangesAsync(ct);

            return Ok(new
            {
                extraction = result.Extraction,
                receiptUrl,
                quota = new { used = used + 1, limit, remaining = Math.Max(0, limit - used - 1) }
            });
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
        [FromQuery] DateTime? endDate = null,
        CancellationToken ct = default)
    {
        var companyId = GetCompanyId();

        // Portée véhicules : chaque dépense expose le véhicule (nom + MATRICULE),
        // le montant et la facture. Fuite constatée le 09/09/2026 : l'écran
        // Dépenses ne filtrait que sur la société, si bien qu'un employé restreint
        // à un seul véhicule y voyait les 36 dépenses de tout le parc alors que les
        // pleins, les échéances et les rapports, eux, lui en montraient 8.
        // null = admin (tout le parc) ; liste vide = aucun véhicule visible.
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenant, ct);

        var query = _context.VehicleCosts
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId)
            .Include(c => c.Vehicle)
            .AsQueryable();

        if (scope is not null)
            query = query.Where(c => scope.Contains(c.VehicleId));

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
                // Justificatif (facture scannée) + détail des lignes — rendus
                // dans le panneau de détail de la dépense.
                c.ReceiptUrl,
                c.DetailsJson,
                c.CreatedAt,
                // Calypso 7 — link to the accident timeline that produced
                // this cost (Phase 5 repair / Phase 6 insurance refund).
                c.AccidentEventId,
            })
            .ToListAsync(ct);

        return Ok(costs);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<VehicleCost>> GetCost(int id, CancellationToken ct = default)
    {
        var cost = await FindScopedCostAsync(id, ct, tracked: false, includeVehicle: true);

        if (cost == null)
            return NotFound();

        return Ok(cost);
    }

    [HttpGet("summary")]
    public async Task<ActionResult> GetCostSummary(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        CancellationToken ct = default)
    {
        var companyId = GetCompanyId();
        var startDateUtc = startDate.HasValue
            ? DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc)
            : DateTime.UtcNow.AddMonths(-1);
        var endDateUtc = endDate.HasValue
            ? DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc)
            : DateTime.UtcNow;

        // Même portée que la liste : le total affiché en tête de l'écran Dépenses
        // doit porter sur les mêmes lignes que celles listées en dessous.
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenant, ct);

        var scoped = _context.VehicleCosts
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.Date >= startDateUtc && c.Date <= endDateUtc);

        if (scope is not null)
            scoped = scoped.Where(c => scope.Contains(c.VehicleId));

        var costs = await scoped
            .GroupBy(c => c.Type)
            .Select(g => new
            {
                Type = g.Key,
                Total = g.Sum(c => c.Amount),
                Count = g.Count()
            })
            .ToListAsync(ct);

        var totalFuel = await scoped
            .Where(c => c.Type == "fuel")
            .SumAsync(c => c.Liters ?? 0, ct);

        return Ok(new
        {
            ByType = costs,
            TotalAmount = costs.Sum(c => c.Total),
            TotalFuelLiters = totalFuel,
            Period = new { StartDate = startDateUtc, EndDate = endDateUtc }
        });
    }

    [HttpPost]
    public async Task<ActionResult<VehicleCost>> CreateCost([FromBody] VehicleCost cost, CancellationToken ct = default)
    {
        var companyId = GetCompanyId();
        var userId = GetUserId();

        // Portée véhicules en ÉCRITURE : le véhicule visé doit exister dans la
        // société ET être visible par l'appelant. Sans ce contrôle, un employé
        // restreint au véhicule #7 pouvait imputer une dépense au véhicule #9
        // (ou à un véhicule d'une autre société) alors qu'il ne le voit nulle
        // part. Même réponse 404 dans les deux cas : ne rien révéler.
        if (!await CanBookOnVehicleAsync(cost.VehicleId, ct))
            return NotFound(new { message = "Véhicule introuvable." });

        cost.CompanyId = companyId;
        cost.CreatedByUserId = userId;
        cost.CreatedAt = DateTime.UtcNow;
        cost.UpdatedAt = DateTime.UtcNow;

        // Invoice breakdown: bound the stored JSON (30 short lines max ≈ 4 KB)
        // so a hostile client can't inflate the row.
        if (cost.DetailsJson is { Length: > 8000 })
            cost.DetailsJson = null;

        // Calculate total for fuel
        if (cost.Type == "fuel" && cost.Liters.HasValue && cost.PricePerLiter.HasValue)
        {
            cost.Amount = cost.Liters.Value * cost.PricePerLiter.Value;
        }

        _context.VehicleCosts.Add(cost);
        await _context.SaveChangesAsync(ct);

        // Send notification to company admins
        try
        {
            var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
            var vehicle = await _context.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == cost.VehicleId && v.CompanyId == companyId, ct);
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
    public async Task<ActionResult> UpdateCost(int id, [FromBody] VehicleCost updated, CancellationToken ct = default)
    {
        // Même portée que la lecture : ce que l'appelant ne peut pas voir, il ne
        // peut pas le modifier. Le véhicule porteur n'est volontairement PAS
        // réaffectable ici (cost.VehicleId n'est jamais écrasé par le payload),
        // sinon une dépense pourrait être poussée hors de la portée.
        var cost = await FindScopedCostAsync(id, ct);

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

        await _context.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteCost(int id, CancellationToken ct = default)
    {
        // Portée véhicules : la suppression suit exactement la même règle que
        // l'affichage. C'était la fuite en écriture du 09/09/2026 — la ligne
        // était chargée sur le seul couple (Id, CompanyId).
        var cost = await FindScopedCostAsync(id, ct);

        if (cost == null)
            return NotFound();

        // Un entretien saisi depuis l'écran crée une dépense (cette VehicleCost)
        // ET un journal d'entretien (MaintenanceLog) qui la référence par CostId.
        // Supprimer la seule dépense laissait le journal ORPHELIN, que le tableau
        // de bord continuait de compter — d'où un entretien « fantôme » persistant
        // (recette client du 25/08/2026). On supprime donc le journal lié.
        var linkedLogs = await _context.MaintenanceLogs
            .Where(m => m.CostId == cost.Id)
            .ToListAsync(ct);
        if (linkedLogs.Count > 0)
            _context.MaintenanceLogs.RemoveRange(linkedLogs);

        _context.VehicleCosts.Remove(cost);
        await _context.SaveChangesAsync(ct);

        return NoContent();
    }
}
