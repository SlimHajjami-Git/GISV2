using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.AccidentEvents.Commands;
using GisAPI.Application.Features.AccidentEvents.Queries;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Controllers;

/// <summary>
/// Calypso 7 — single entry point for the unified accident lifecycle.
/// Replaces the old <c>AccidentClaimsController</c> (deleted) — manual
/// claim data was migrated into <c>accident_events</c> by the
/// <c>UnifyAccidentEventLifecycle</c> migration.
///
/// <para>Endpoints map to the timeline phases:</para>
/// <list type="bullet">
///   <item><c>GET    /                          </c> — paged list</item>
///   <item><c>GET    /:id                       </c> — full timeline DTO</item>
///   <item><c>POST   /:id/confirm               </c> — Phase 2 trigger (status → confirmed)</item>
///   <item><c>POST   /:id/dismiss               </c> — false alarm</item>
///   <item><c>POST   /manual                    </c> — create a manually-declared accident</item>
///   <item><c>PATCH  /:id/initial-damages       </c> — Phase 2 form fields</item>
///   <item><c>PATCH  /:id/expert                </c> — Phase 3</item>
///   <item><c>PATCH  /:id/mechanic-quote        </c> — Phase 4</item>
///   <item><c>PATCH  /:id/repair                </c> — Phase 5 (auto-syncs VehicleCost)</item>
///   <item><c>PATCH  /:id/claim                 </c> — Phase 6 (auto-syncs VehicleCost as refund)</item>
///   <item><c>POST   /:id/third-parties         </c> — add a third party</item>
///   <item><c>DELETE /:id/third-parties/:tpId   </c> — remove a third party</item>
///   <item><c>POST   /:id/upload-pdf            </c> — auto-generated PDF (Phase 2)</item>
///   <item><c>POST   /:id/documents             </c> — multi-file upload typed by phase</item>
///   <item><c>DELETE /:id/documents/:docId      </c> — remove a document</item>
///   <item><c>POST   /simulate                  </c> — debug-only, user 1</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/accident-reports")]
[Authorize]
public class AccidentReportsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly GisDbContext _context;
    private readonly INotificationService _notifService;
    private readonly ICurrentTenantService _tenantService;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AccidentReportsController> _logger;

    public AccidentReportsController(
        IMediator mediator,
        GisDbContext context,
        INotificationService notifService,
        ICurrentTenantService tenantService,
        IWebHostEnvironment env,
        ILogger<AccidentReportsController> logger)
    {
        _mediator = mediator;
        _context = context;
        _notifService = notifService;
        _tenantService = tenantService;
        _env = env;
        _logger = logger;
    }

    // ── Read endpoints ──────────────────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<ListAccidentEventsResult>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] int? vehicleId = null,
        [FromQuery] string? origin = null,
        [FromQuery] bool includeDismissed = false,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ListAccidentEventsQuery(page, pageSize, status, vehicleId, origin, includeDismissed),
            ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AccidentReportDto>> GetReport(int id, CancellationToken ct)
    {
        var report = await _mediator.Send(new GetAccidentReportQuery(id), ct);
        if (report == null) return NotFound();
        return Ok(report);
    }

    // ── Confirmation / dismissal ───────────────────────────────────────────

    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> Confirm(int id, CancellationToken ct)
    {
        try { await _mediator.Send(new ConfirmAccidentCommand(id), ct); return NoContent(); }
        catch (NotFoundException) { return NotFound(); }
    }

    [HttpPost("{id:int}/dismiss")]
    public async Task<IActionResult> Dismiss(int id, CancellationToken ct)
    {
        try { await _mediator.Send(new DismissAccidentCommand(id), ct); return NoContent(); }
        catch (NotFoundException) { return NotFound(); }
    }

    [HttpPost("manual")]
    public async Task<ActionResult<object>> CreateManual([FromBody] CreateManualAccidentRequest request, CancellationToken ct)
    {
        try
        {
            var id = await _mediator.Send(new CreateManualAccidentCommand(
                VehicleId: request.VehicleId,
                IncidentAt: request.IncidentAt,
                Latitude: request.Latitude,
                Longitude: request.Longitude,
                LocationCommune: request.LocationCommune,
                LocationGovernorate: request.LocationGovernorate,
                Description: request.Description,
                Severity: request.Severity,
                EstimatedCost: request.EstimatedCost,
                ClaimNumber: request.ClaimNumber,
                InternalNotes: request.InternalNotes
            ), ct);
            return Ok(new { accidentEventId = id });
        }
        catch (NotFoundException) { return NotFound(); }
        catch (DomainException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ── Phase commands ─────────────────────────────────────────────────────

    [HttpPatch("{id:int}/initial-damages")]
    public async Task<IActionResult> UpdateInitialDamages(int id, [FromBody] UpdateInitialDamagesRequest req, CancellationToken ct)
    {
        try
        {
            await _mediator.Send(new UpdateInitialDamagesCommand(id, req.Description, req.Severity,
                req.DamagedZones, req.PoliceReportNumber, req.MileageAtAccident,
                req.WeatherConditions, req.RoadConditions), ct);
            return NoContent();
        }
        catch (NotFoundException) { return NotFound(); }
        catch (DomainException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPatch("{id:int}/expert")]
    public async Task<IActionResult> RegisterExpert(int id, [FromBody] RegisterExpertAssessmentRequest req, CancellationToken ct)
    {
        try
        {
            await _mediator.Send(new RegisterExpertAssessmentCommand(id, req.VisitedAt, req.ExpertName,
                req.ExpertCompany, req.Assessment, req.EstimatedAmount), ct);
            return NoContent();
        }
        catch (NotFoundException) { return NotFound(); }
        catch (DomainException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPatch("{id:int}/mechanic-quote")]
    public async Task<IActionResult> RegisterMechanicQuote(int id, [FromBody] RegisterMechanicQuoteRequest req, CancellationToken ct)
    {
        try
        {
            await _mediator.Send(new RegisterMechanicQuoteCommand(id, req.QuoteAt, req.MechanicName, req.QuotedAmount), ct);
            return NoContent();
        }
        catch (NotFoundException) { return NotFound(); }
        catch (DomainException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPatch("{id:int}/repair")]
    public async Task<IActionResult> RegisterRepair(int id, [FromBody] RegisterRepairRequest req, CancellationToken ct)
    {
        try
        {
            await _mediator.Send(new RegisterRepairCommand(id, req.StartedAt, req.CompletedAt, req.ActualCost), ct);
            return NoContent();
        }
        catch (NotFoundException) { return NotFound(); }
        catch (DomainException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPatch("{id:int}/claim")]
    public async Task<IActionResult> RegisterClaim(int id, [FromBody] RegisterClaimRequest req, CancellationToken ct)
    {
        try
        {
            await _mediator.Send(new RegisterClaimCommand(id, req.ClaimNumber, req.SubmittedAt,
                req.ApprovedAmount, req.Status, req.ThirdPartyInvolved), ct);
            return NoContent();
        }
        catch (NotFoundException) { return NotFound(); }
        catch (DomainException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ── Third parties ──────────────────────────────────────────────────────

    [HttpPost("{id:int}/third-parties")]
    public async Task<ActionResult<object>> AddThirdParty(int id, [FromBody] AddThirdPartyRequest req, CancellationToken ct)
    {
        try
        {
            var tpId = await _mediator.Send(new AddThirdPartyCommand(id, req.Name, req.Phone,
                req.VehiclePlate, req.VehicleModel, req.InsuranceCompany, req.InsuranceNumber, req.InsuranceExpiry), ct);
            return Ok(new { thirdPartyId = tpId });
        }
        catch (NotFoundException) { return NotFound(); }
        catch (DomainException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("{id:int}/third-parties/{tpId:int}")]
    public async Task<IActionResult> DeleteThirdParty(int id, int tpId, CancellationToken ct)
    {
        try { await _mediator.Send(new DeleteThirdPartyCommand(id, tpId), ct); return NoContent(); }
        catch (NotFoundException) { return NotFound(); }
        catch (DomainException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ── Documents ──────────────────────────────────────────────────────────

    /// <summary>
    /// Auto-generated PDF (Phase 2) — single PDF per accident, stored as
    /// <c>pdf_report_url</c> on the row. Replaces any previously attached
    /// auto-PDF.
    /// </summary>
    [HttpPost("{id:int}/upload-pdf")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<object>> UploadPdf(int id, [FromForm] IFormFile file, CancellationToken ct)
    {
        var (ev, error) = await ValidateUploadAsync(id, file, allowedExt: ".pdf", maxSize: 20_000_000, ct);
        if (error != null) return error;

        var (publicUrl, _) = await SaveFileAsync(id, file!, ".pdf", ct);
        await _mediator.Send(new AttachAccidentPdfCommand(id, publicUrl), ct);
        return Ok(new { pdfReportUrl = publicUrl });
    }

    /// <summary>
    /// Multi-file upload typed by phase — accepts pdf / images / docs.
    /// Payload: <c>file</c> (the file) + <c>documentType</c> (one of
    /// <c>expert_report | mechanic_quote | repair_invoice | insurance_response | police_report | photo | other</c>).
    /// </summary>
    [HttpPost("{id:int}/documents")]
    [RequestSizeLimit(50_000_000)]
    public async Task<ActionResult<object>> AddDocument(int id, [FromForm] IFormFile file, [FromForm] string? documentType, CancellationToken ct)
    {
        var (_, error) = await ValidateUploadAsync(id, file, allowedExt: null, maxSize: 50_000_000, ct);
        if (error != null) return error;

        var ext = Path.GetExtension(file!.FileName).ToLowerInvariant();
        var allowed = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".gif", ".webp", ".doc", ".docx" };
        if (!allowed.Contains(ext))
            return BadRequest(new { message = $"Format non supporté ({ext}). Accepté: PDF, images, Word." });

        var (publicUrl, _) = await SaveFileAsync(id, file, ext, ct);
        var docId = await _mediator.Send(new AddAccidentDocumentCommand(
            AccidentEventId: id,
            DocumentType: documentType ?? "other",
            FileName: file.FileName,
            FileUrl: publicUrl,
            FileSize: (int)file.Length,
            MimeType: file.ContentType
        ), ct);

        return Ok(new { documentId = docId, fileUrl = publicUrl });
    }

    [HttpDelete("{id:int}/documents/{docId:int}")]
    public async Task<IActionResult> DeleteDocument(int id, int docId, CancellationToken ct)
    {
        try { await _mediator.Send(new DeleteAccidentDocumentCommand(id, docId), ct); return NoContent(); }
        catch (NotFoundException) { return NotFound(); }
        catch (DomainException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ── Simulate (debug-only) ─────────────────────────────────────────────

    [HttpPost("simulate")]
    public async Task<IActionResult> Simulate(CancellationToken ct)
    {
        var currentUserId = _tenantService.UserId;
        var currentCompanyId = _tenantService.CompanyId;
        if (currentUserId != 1) return Forbid();
        if (currentCompanyId == null) return BadRequest(new { error = "Société non identifiée" });

        var now = DateTime.UtcNow;
        var ev = new AccidentEvent
        {
            CompanyId = currentCompanyId.Value,
            Origin = "auto",
            DeviceUid = "SIMULATE-USER-1",
            IncidentAt = now,
            Latitude = 36.8188,
            Longitude = 10.1657,
            Confidence = 95,
            VehicleLabel = "🧪 Simulation test",
            SynthesisText = "Événement simulé manuellement (bouton de test).",
            Status = "pending",
        };

        _context.AccidentEvents.Add(ev);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AccidentReportsController.Simulate: created simulated accident {AccidentId} for user {UserId} in company {CompanyId}",
            ev.Id, currentUserId.Value, currentCompanyId.Value);

        var metadata = new Dictionary<string, object>
        {
            ["accidentEventId"] = ev.Id,
            ["vehicleId"] = 0,
            ["vehicleLabel"] = ev.VehicleLabel!,
            ["incidentAt"] = now.ToString("O"),
            ["latitude"] = ev.Latitude,
            ["longitude"] = ev.Longitude,
            ["magnitude"] = 8000,
            ["speedBeforeKph"] = 65,
            ["requiresDecision"] = "true",
        };

        var localTime = now.ToString("dd/MM/yyyy 'à' HH'h'mm");
        try
        {
            await _notifService.CreateAndSendAsync(
                companyId: currentCompanyId.Value,
                userId: currentUserId.Value,
                type: "accident_detected",
                title: "Accident détecté sur votre véhicule",
                message: $"🧪 SIMULATION — événement test déclenché le {localTime} UTC sur le véhicule {ev.VehicleLabel}.",
                priority: "critical",
                referenceType: "accident_event",
                referenceId: ev.Id,
                actionUrl: $"/rapport-accident/{ev.Id}",
                metadata: metadata,
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "AccidentReportsController.Simulate: notification fan-out failed for accident {AccidentId}",
                ev.Id);
        }

        return Ok(new { accidentEventId = ev.Id });
    }

    // ── File upload helpers ───────────────────────────────────────────────

    private async Task<(AccidentEvent? Ev, ActionResult? Error)> ValidateUploadAsync(
        int id, IFormFile? file, string? allowedExt, long maxSize, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return (null, BadRequest(new { message = "Fichier requis" }));
        if (file.Length > maxSize)
            return (null, BadRequest(new { message = $"Fichier trop volumineux (max {maxSize / 1_000_000} Mo)" }));
        if (allowedExt != null)
        {
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != allowedExt) return (null, BadRequest(new { message = $"Format non supporté — uniquement {allowedExt}" }));
        }
        var companyId = _tenantService.CompanyId;
        if (companyId == null) return (null, BadRequest(new { message = "Société non identifiée" }));
        var ev = await _context.AccidentEvents
            .FirstOrDefaultAsync(e => e.Id == id && e.CompanyId == companyId.Value, ct);
        if (ev == null) return (null, NotFound());
        return (ev, null);
    }

    private async Task<(string PublicUrl, string DiskPath)> SaveFileAsync(
        int accidentId, IFormFile file, string ext, CancellationToken ct)
    {
        var uploadsDir = Path.Combine(_env.ContentRootPath, "uploads", "accident-reports", accidentId.ToString());
        if (!Directory.Exists(uploadsDir)) Directory.CreateDirectory(uploadsDir);
        var uniqueName = $"{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(uploadsDir, uniqueName);
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream, ct);
        }
        return ($"/uploads/accident-reports/{accidentId}/{uniqueName}", filePath);
    }
}

// ── Request DTOs ────────────────────────────────────────────────────────────

public record CreateManualAccidentRequest(
    int VehicleId,
    DateTime IncidentAt,
    double? Latitude,
    double? Longitude,
    string? LocationCommune,
    string? LocationGovernorate,
    string? Description,
    string? Severity,
    decimal? EstimatedCost,
    string? ClaimNumber,
    string? InternalNotes);

public record UpdateInitialDamagesRequest(
    string? Description,
    string? Severity,
    List<string>? DamagedZones,
    string? PoliceReportNumber,
    int? MileageAtAccident,
    string? WeatherConditions,
    string? RoadConditions);

public record RegisterExpertAssessmentRequest(
    DateTime? VisitedAt,
    string? ExpertName,
    string? ExpertCompany,
    string? Assessment,
    decimal? EstimatedAmount);

public record RegisterMechanicQuoteRequest(
    DateTime? QuoteAt,
    string? MechanicName,
    decimal? QuotedAmount);

public record RegisterRepairRequest(
    DateTime? StartedAt,
    DateTime? CompletedAt,
    decimal? ActualCost);

public record RegisterClaimRequest(
    string? ClaimNumber,
    DateTime? SubmittedAt,
    decimal? ApprovedAmount,
    string? Status,
    bool? ThirdPartyInvolved);

public record AddThirdPartyRequest(
    string? Name,
    string? Phone,
    string? VehiclePlate,
    string? VehicleModel,
    string? InsuranceCompany,
    string? InsuranceNumber,
    DateTime? InsuranceExpiry);
