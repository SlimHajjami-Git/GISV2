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
/// Access to persisted accident report data. Read via <c>GET {id}</c>,
/// decision workflow via <c>POST {id}/confirm</c> and
/// <c>POST {id}/dismiss</c> (both admin-initiated from the blocking
/// modal on the frontend).
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

    /// <summary>
    /// Calypso 6 (P9) — paged list of all accident events for the current
    /// tenant. Default excludes <c>dismissed</c> rows (noise). Supports
    /// optional filters on <c>status</c> and <c>vehicleId</c>.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ListAccidentEventsResult>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] int? vehicleId = null,
        [FromQuery] bool includeDismissed = false,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ListAccidentEventsQuery(page, pageSize, status, vehicleId, includeDismissed),
            ct);
        return Ok(result);
    }

    /// <summary>
    /// Calypso 6 (P9) — finalise the accident: persists the damages form
    /// (description, severity, estimated cost, claim number, internal
    /// notes, manual tow date) and flips <c>status = "confirmed"</c>.
    /// Only valid when the row is currently in <c>awaiting_details</c> or
    /// already <c>confirmed</c> (re-edit). Returns 400 otherwise.
    /// </summary>
    [HttpPatch("{id:int}/damages")]
    public async Task<IActionResult> UpdateDamages(int id, [FromBody] UpdateAccidentDamagesRequest request, CancellationToken ct)
    {
        try
        {
            await _mediator.Send(new UpdateAccidentDamagesCommand(
                AccidentEventId: id,
                Description: request.Description,
                Severity: request.Severity,
                EstimatedCost: request.EstimatedCost,
                ClaimNumber: request.ClaimNumber,
                InternalNotes: request.InternalNotes,
                ManualTowDate: request.ManualTowDate
            ), ct);
            return NoContent();
        }
        catch (NotFoundException) { return NotFound(); }
        catch (DomainException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>
    /// Calypso 6 (P9) — store the PDF accident report attached to an event.
    /// Used both for the auto-generated frontend PDF (uploaded right after
    /// confirm) and for an externally-supplied PDF (insurance expert) on
    /// manual reports. The file is written under
    /// <c>uploads/accident-reports/{id}/{guid}.pdf</c> and the public
    /// URL is persisted on the row.
    /// </summary>
    [HttpPost("{id:int}/upload-pdf")]
    [RequestSizeLimit(20_000_000)] // 20 MB max
    public async Task<ActionResult<object>> UploadPdf(int id, [FromForm] IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Fichier requis" });
        if (file.Length > 20_000_000)
            return BadRequest(new { message = "Fichier trop volumineux (max 20 Mo)" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".pdf")
            return BadRequest(new { message = "Format non supporté — uniquement PDF" });

        // Make sure the row exists and belongs to the caller's tenant before
        // we touch the disk. The Attach command re-checks but we want to
        // fail early on missing/cross-tenant rows without writing files.
        var companyId = _tenantService.CompanyId;
        if (companyId == null) return BadRequest(new { message = "Société non identifiée" });
        var ev = await _context.AccidentEvents
            .FirstOrDefaultAsync(e => e.Id == id && e.CompanyId == companyId.Value, ct);
        if (ev == null) return NotFound();

        var uploadsDir = Path.Combine(_env.ContentRootPath, "uploads", "accident-reports", id.ToString());
        if (!Directory.Exists(uploadsDir)) Directory.CreateDirectory(uploadsDir);

        var uniqueName = $"{Guid.NewGuid()}.pdf";
        var filePath = Path.Combine(uploadsDir, uniqueName);
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream, ct);
        }

        var publicUrl = $"/uploads/accident-reports/{id}/{uniqueName}";
        await _mediator.Send(new AttachAccidentPdfCommand(id, publicUrl), ct);

        _logger.LogInformation(
            "AccidentReportsController.UploadPdf: accident {AccidentId} attached PDF at {Url} ({Size} bytes)",
            id, publicUrl, file.Length);

        return Ok(new { pdfReportUrl = publicUrl });
    }

    /// <summary>
    /// Calypso 6 (P9) — manual accident creation (admin records an accident
    /// the system did not auto-detect). Returns the new id; the caller can
    /// then call <c>POST /:id/upload-pdf</c> to attach an expert report.
    /// </summary>
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

    /// <summary>
    /// Get the persisted accident report by id. Returns 404 if the row
    /// doesn't exist OR belongs to another company (tenant filter).
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<AccidentReportDto>> GetReport(int id, CancellationToken ct)
    {
        var report = await _mediator.Send(new GetAccidentReportQuery(id), ct);
        if (report == null) return NotFound();
        return Ok(report);
    }

    /// <summary>
    /// Confirm the accident — stamps <c>status = 'confirmed'</c>, records
    /// the deciding admin, and fans an audit notification to the other
    /// admins. Idempotent (a second call on an already-decided event is a
    /// no-op). 404 when the event doesn't exist or belongs to another
    /// company.
    /// </summary>
    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> Confirm(int id, CancellationToken ct)
    {
        try
        {
            await _mediator.Send(new ConfirmAccidentCommand(id), ct);
            return NoContent();
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Dismiss the accident as a false positive — stamps
    /// <c>status = 'dismissed'</c> and broadcasts a "Choc violent détecté —
    /// dégâts possibles" notification to the OTHER admins of the company
    /// (no driver, no email). Idempotent. 404 on missing / cross-tenant.
    /// </summary>
    [HttpPost("{id:int}/dismiss")]
    public async Task<IActionResult> Dismiss(int id, CancellationToken ct)
    {
        try
        {
            await _mediator.Send(new DismissAccidentCommand(id), ct);
            return NoContent();
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// <b>Debug-only.</b> Simulates the accident-detection fan-out: inserts a
    /// <c>pending</c> <c>AccidentEvent</c> row in the caller's company and
    /// sends the blocking-modal notification <b>to the caller alone</b>
    /// (UserId = 1). Every other admin of the company is skipped — this
    /// lets one operator walk the full decision workflow (confirm →
    /// tow-monitoring, dismiss, or postpone) end-to-end on prod without
    /// spamming their colleagues.
    ///
    /// <para>Gated to <c>UserId == 1</c>. Anyone else gets 403. Once the
    /// simulated event is created, the caller can confirm / dismiss it via
    /// the normal <c>/confirm</c> / <c>/dismiss</c> endpoints — those
    /// handlers do broadcast to other admins, so use a throwaway test
    /// company or let the team know a simulation is in progress.</para>
    ///
    /// <para>Returns <c>{ accidentEventId }</c> so the frontend can log the
    /// row it just kicked off.</para>
    /// </summary>
    [HttpPost("simulate")]
    public async Task<IActionResult> Simulate(CancellationToken ct)
    {
        var currentUserId = _tenantService.UserId;
        var currentCompanyId = _tenantService.CompanyId;

        // Hard gate — we want this endpoint to be obviously off-limits to
        // anyone other than the primary account. Intentionally not using a
        // role/permission check: this is a manual test hook, not a feature.
        if (currentUserId != 1)
        {
            return Forbid();
        }

        if (currentCompanyId == null)
        {
            return BadRequest(new { error = "Société non identifiée" });
        }

        var now = DateTime.UtcNow;
        var ev = new AccidentEvent
        {
            CompanyId = currentCompanyId.Value,
            DeviceUid = "SIMULATE-USER-1",
            IncidentAt = now,
            // Tunis — close enough for a realistic map pin on the report page.
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

        // Mirror AccidentDetectionService.NotifyCompanyAsync's metadata shape
        // exactly so the AccidentDecisionModalComponent cannot tell the
        // difference between a real accident and the simulated one.
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
            // String "true" — SignalR coerces the dictionary to JSON; the
            // frontend modal filters on metadata.requiresDecision === 'true'.
            ["requiresDecision"] = "true",
        };

        var localTime = now.ToString("dd/MM/yyyy 'à' HH'h'mm");
        var title = "Accident détecté sur votre véhicule";
        var message =
            $"🧪 SIMULATION — événement test déclenché le {localTime} UTC " +
            $"sur le véhicule {ev.VehicleLabel}. Cliquez pour consulter le rapport détaillé.";
        var actionUrl = $"/rapport-accident/{ev.Id}";

        try
        {
            await _notifService.CreateAndSendAsync(
                companyId: currentCompanyId.Value,
                userId: currentUserId.Value,
                type: "accident_detected",
                title: title,
                message: message,
                priority: "critical",
                referenceType: "accident_event",
                referenceId: ev.Id,
                actionUrl: actionUrl,
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
}

// ── Calypso 6 (P9) request DTOs ────────────────────────────────────────────

public record UpdateAccidentDamagesRequest(
    string? Description,
    string? Severity,            // "minor" | "moderate" | "severe" | "total"
    decimal? EstimatedCost,
    string? ClaimNumber,
    string? InternalNotes,
    DateTime? ManualTowDate);

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
