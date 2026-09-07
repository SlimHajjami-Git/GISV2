using GisAPI.Application.Features.AcquisitionPayments;
using GisAPI.Domain.Interfaces;
using GisAPI.Services;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GisAPI.Controllers;

/// <summary>
/// Échéances d'acquisition persistées (apport, mensualités de crédit/leasing,
/// achat comptant) — recette client du 07/09/2026. Contrôleur fin : la
/// visibilité (VehicleScope), la génération paresseuse et les règles de statut
/// vivent dans les handlers MediatR. Droits : mêmes drapeaux que /api/costs
/// (PermissionMiddleware).
/// </summary>
[ApiController]
[Route("api/acquisition-payments")]
[Authorize]
public class AcquisitionPaymentsController : ControllerBase
{
    private const string ReceiptsFolder = "acquisition-receipts";
    // Mêmes formats que CostsController.ScanInvoice.
    private static readonly string[] AllowedReceiptExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".pdf" };

    private readonly IMediator _mediator;
    private readonly ICurrentTenantService _tenantService;
    private readonly IWebHostEnvironment _env;
    private readonly IDashboardCache _dashboardCache;
    private readonly ILogger<AcquisitionPaymentsController> _logger;

    public AcquisitionPaymentsController(IMediator mediator, ICurrentTenantService tenantService,
        IWebHostEnvironment env, IDashboardCache dashboardCache, ILogger<AcquisitionPaymentsController> logger)
    {
        _mediator = mediator;
        _tenantService = tenantService;
        _env = env;
        _dashboardCache = dashboardCache;
        _logger = logger;
    }

    /// <summary>
    /// Échéances visibles par l'appelant. Sans <c>includeFuture</c> : payées +
    /// planifiées/ignorées dont la date est atteinte (écran Dépenses) ; avec :
    /// tout l'échéancier (fiche véhicule). Tri : date d'échéance puis seq, décroissants.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<AcquisitionPaymentDto>>> Get(
        [FromQuery] int? vehicleId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] bool includeFuture = false,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetAcquisitionPaymentsQuery(vehicleId, startDate, endDate, includeFuture), ct);
        return Ok(result);
    }

    /// <summary>Changement de statut (planned | paid | skipped), date/montant réglés, note.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<AcquisitionPaymentDto>> Update(int id, [FromBody] UpdateAcquisitionPaymentRequest body, CancellationToken ct)
    {
        var dto = await _mediator.Send(
            new UpdateAcquisitionPaymentCommand(id, body.Status, body.PaidAt, body.PaidAmount, body.Note), ct);

        // Le « Coût total » du tableau de bord somme cette table : on évince le
        // cache de la société plutôt que d'attendre les 10 min de TTL.
        if (_tenantService.CompanyId is { } companyId)
            _dashboardCache.InvalidateCompany(companyId);

        return Ok(dto);
    }

    /// <summary>
    /// Joint une quittance à l'échéance. Stockage sous
    /// uploads/acquisition-receipts/{companyId}/ — PAS sous uploads/invoices, où
    /// InvoiceOrphanCleanupService supprime tout fichier non référencé par une
    /// dépense. Remplace (et supprime) la quittance précédente si elle vivait
    /// dans le même dossier.
    /// </summary>
    [HttpPost("{id:int}/receipt")]
    [RequestSizeLimit(12_000_000)]
    public async Task<IActionResult> UploadReceipt(int id, IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Aucun fichier reçu." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedReceiptExtensions.Contains(ext))
            return BadRequest(new { message = "Format non supporté. Envoyez une image (JPG/PNG) ou un PDF." });

        var companyId = _tenantService.CompanyId;
        if (companyId == null)
            return BadRequest(new { message = "Société non identifiée" });

        var dir = Path.Combine(_env.ContentRootPath, "uploads", ReceiptsFolder, companyId.Value.ToString());
        Directory.CreateDirectory(dir);
        var storedName = $"{Guid.NewGuid():N}{ext}";
        var diskPath = Path.Combine(dir, storedName);
        await using (var stream = new FileStream(diskPath, FileMode.Create))
        {
            await file.CopyToAsync(stream, ct);
        }
        var urlPrefix = $"/uploads/{ReceiptsFolder}/{companyId.Value}/";
        var receiptUrl = urlPrefix + storedName;

        string? previous;
        try
        {
            previous = await _mediator.Send(new SetAcquisitionPaymentReceiptCommand(id, receiptUrl), ct);
        }
        catch
        {
            // Ligne introuvable / hors société : on ne laisse pas de fichier orphelin.
            TryDelete(diskPath);
            throw;
        }

        if (!string.IsNullOrEmpty(previous) && previous.StartsWith(urlPrefix, StringComparison.Ordinal))
            TryDelete(Path.Combine(dir, Path.GetFileName(previous)));

        return Ok(new { receiptUrl });
    }

    private void TryDelete(string path)
    {
        try
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Quittance d'échéance : suppression impossible de {Path}", path);
        }
    }
}

/// <summary>Corps de PUT /api/acquisition-payments/{id}.</summary>
public record UpdateAcquisitionPaymentRequest(
    string Status,
    DateTime? PaidAt = null,
    decimal? PaidAmount = null,
    string? Note = null);
