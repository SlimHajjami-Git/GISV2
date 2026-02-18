using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MediatR;
using GisAPI.Application.Features.AccidentClaims.Queries;
using GisAPI.Application.Features.AccidentClaims.Commands;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/accident-claims")]
[Authorize]
public class AccidentClaimsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IGisDbContext _context;
    private readonly IWebHostEnvironment _env;

    public AccidentClaimsController(IMediator mediator, IGisDbContext context, IWebHostEnvironment env)
    {
        _mediator = mediator;
        _context = context;
        _env = env;
    }

    /// <summary>
    /// Get all accident claims with optional filtering
    /// </summary>
    [HttpGet]
    public async Task<ActionResult> GetAccidentClaims(
        [FromQuery] string? searchTerm = null,
        [FromQuery] string? status = null,
        [FromQuery] string? severity = null,
        [FromQuery] int? vehicleId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _mediator.Send(new GetAccidentClaimsQuery(searchTerm, status, severity, vehicleId, page, pageSize));
        return Ok(result);
    }

    /// <summary>
    /// Get accident claim by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<AccidentClaimDto>> GetAccidentClaim(int id)
    {
        var claim = await _mediator.Send(new GetAccidentClaimByIdQuery(id));
        if (claim == null)
            return NotFound();
        return Ok(claim);
    }

    /// <summary>
    /// Get accident claims statistics
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<AccidentClaimStatsDto>> GetStats()
    {
        var stats = await _mediator.Send(new GetAccidentClaimStatsQuery());
        return Ok(stats);
    }

    /// <summary>
    /// Get accident claims for a specific vehicle
    /// </summary>
    [HttpGet("vehicle/{vehicleId}")]
    public async Task<ActionResult> GetVehicleAccidentClaims(int vehicleId)
    {
        var result = await _mediator.Send(new GetAccidentClaimsQuery(null, null, null, vehicleId, 1, 100));
        return Ok(result);
    }

    /// <summary>
    /// Create a new accident claim
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<int>> CreateAccidentClaim([FromBody] CreateAccidentClaimRequest request)
    {
        var command = new CreateAccidentClaimCommand(
            request.VehicleId,
            request.DriverId,
            request.AccidentDate,
            request.AccidentTime,
            request.Location,
            request.Latitude,
            request.Longitude,
            request.Description,
            request.Severity,
            request.EstimatedDamage,
            request.DamagedZones,
            request.ThirdPartyInvolved,
            request.ThirdPartyName,
            request.ThirdPartyPhone,
            request.ThirdPartyVehiclePlate,
            request.ThirdPartyVehicleModel,
            request.ThirdPartyInsurance,
            request.ThirdPartyInsuranceNumber,
            request.PoliceReportNumber,
            request.MileageAtAccident,
            request.Witnesses,
            request.AdditionalNotes
        );

        var claimId = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetAccidentClaim), new { id = claimId }, claimId);
    }

    /// <summary>
    /// Update an existing accident claim (draft only)
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateAccidentClaim(int id, [FromBody] UpdateAccidentClaimRequest request)
    {
        var command = new UpdateAccidentClaimCommand(
            id,
            request.VehicleId,
            request.DriverId,
            request.AccidentDate,
            request.AccidentTime,
            request.Location,
            request.Latitude,
            request.Longitude,
            request.Description,
            request.Severity,
            request.EstimatedDamage,
            request.DamagedZones,
            request.ThirdPartyInvolved,
            request.PoliceReportNumber,
            request.MileageAtAccident,
            request.Witnesses,
            request.AdditionalNotes
        );

        var success = await _mediator.Send(command);
        if (!success)
            return NotFound();
        return NoContent();
    }

    /// <summary>
    /// Delete an accident claim (draft only)
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteAccidentClaim(int id)
    {
        var success = await _mediator.Send(new DeleteAccidentClaimCommand(id));
        if (!success)
            return NotFound();
        return NoContent();
    }

    /// <summary>
    /// Submit an accident claim for review
    /// </summary>
    [HttpPost("{id}/submit")]
    public async Task<ActionResult> SubmitAccidentClaim(int id)
    {
        var success = await _mediator.Send(new SubmitAccidentClaimCommand(id));
        if (!success)
            return NotFound();
        return Ok(new { Message = "Claim submitted for review" });
    }

    /// <summary>
    /// Approve an accident claim (admin)
    /// </summary>
    [HttpPost("{id}/approve")]
    public async Task<ActionResult> ApproveAccidentClaim(int id, [FromBody] ApproveClaimRequest request)
    {
        var success = await _mediator.Send(new ApproveAccidentClaimCommand(id, request.ApprovedAmount));
        if (!success)
            return NotFound();
        return Ok(new { Message = "Claim approved" });
    }

    /// <summary>
    /// Reject an accident claim (admin)
    /// </summary>
    [HttpPost("{id}/reject")]
    public async Task<ActionResult> RejectAccidentClaim(int id, [FromBody] RejectClaimRequest? request)
    {
        var success = await _mediator.Send(new RejectAccidentClaimCommand(id, request?.Reason));
        if (!success)
            return NotFound();
        return Ok(new { Message = "Claim rejected" });
    }

    /// <summary>
    /// Close an approved accident claim
    /// </summary>
    [HttpPost("{id}/close")]
    public async Task<ActionResult> CloseAccidentClaim(int id)
    {
        var success = await _mediator.Send(new CloseAccidentClaimCommand(id));
        if (!success)
            return NotFound();
        return Ok(new { Message = "Claim closed" });
    }
    /// <summary>
    /// Upload documents/photos to an accident claim
    /// </summary>
    [HttpPost("{id}/documents")]
    [RequestSizeLimit(50_000_000)] // 50MB max
    public async Task<ActionResult> UploadDocuments(int id, [FromForm] List<IFormFile> files, [FromForm] string? documentType)
    {
        var claim = await _context.AccidentClaims.FirstOrDefaultAsync(c => c.Id == id);
        if (claim == null) return NotFound();

        var uploadsDir = Path.Combine(_env.ContentRootPath, "uploads", "claims", id.ToString());
        if (!Directory.Exists(uploadsDir)) Directory.CreateDirectory(uploadsDir);

        var uploadedDocs = new List<object>();
        foreach (var file in files)
        {
            if (file.Length == 0) continue;
            if (file.Length > 10_000_000) continue; // Skip files > 10MB

            var ext = Path.GetExtension(file.FileName).ToLower();
            var allowedExts = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf", ".doc", ".docx" };
            if (!allowedExts.Contains(ext)) continue;

            var uniqueName = $"{Guid.NewGuid()}{ext}";
            var filePath = Path.Combine(uploadsDir, uniqueName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var doc = new AccidentClaimDocument
            {
                ClaimId = id,
                DocumentType = documentType ?? (file.ContentType.StartsWith("image/") ? "photo" : "document"),
                FileName = file.FileName,
                FileUrl = $"/uploads/claims/{id}/{uniqueName}",
                FileSize = (int)file.Length,
                MimeType = file.ContentType,
                UploadedAt = DateTime.UtcNow
            };
            _context.AccidentClaimDocuments.Add(doc);
            uploadedDocs.Add(new { doc.Id, doc.FileName, doc.FileUrl, doc.FileSize, doc.MimeType, doc.DocumentType });
        }

        await _context.SaveChangesAsync(CancellationToken.None);
        return Ok(uploadedDocs);
    }

    /// <summary>
    /// Get all documents for an accident claim
    /// </summary>
    [HttpGet("{id}/documents")]
    public async Task<ActionResult> GetDocuments(int id)
    {
        var claim = await _context.AccidentClaims.FirstOrDefaultAsync(c => c.Id == id);
        if (claim == null) return NotFound();

        var docs = await _context.AccidentClaimDocuments
            .Where(d => d.ClaimId == id)
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => new { d.Id, d.DocumentType, d.FileName, d.FileUrl, d.FileSize, d.MimeType, d.UploadedAt })
            .ToListAsync();
        return Ok(docs);
    }

    /// <summary>
    /// Delete a document from an accident claim
    /// </summary>
    [HttpDelete("{claimId}/documents/{documentId}")]
    public async Task<ActionResult> DeleteDocument(int claimId, int documentId)
    {
        var claim = await _context.AccidentClaims.FirstOrDefaultAsync(c => c.Id == claimId);
        if (claim == null) return NotFound();

        var doc = await _context.AccidentClaimDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId && d.ClaimId == claimId);
        if (doc == null) return NotFound();

        // Delete physical file
        var filePath = Path.Combine(_env.ContentRootPath, doc.FileUrl.TrimStart('/'));
        if (System.IO.File.Exists(filePath))
            System.IO.File.Delete(filePath);

        _context.AccidentClaimDocuments.Remove(doc);
        await _context.SaveChangesAsync(CancellationToken.None);
        return NoContent();
    }
}

// Request DTOs
public record CreateAccidentClaimRequest(
    int VehicleId,
    int? DriverId,
    DateTime AccidentDate,
    string AccidentTime,
    string Location,
    double? Latitude,
    double? Longitude,
    string Description,
    string Severity,
    decimal EstimatedDamage,
    string[]? DamagedZones,
    bool ThirdPartyInvolved = false,
    string? ThirdPartyName = null,
    string? ThirdPartyPhone = null,
    string? ThirdPartyVehiclePlate = null,
    string? ThirdPartyVehicleModel = null,
    string? ThirdPartyInsurance = null,
    string? ThirdPartyInsuranceNumber = null,
    string? PoliceReportNumber = null,
    int? MileageAtAccident = null,
    string? Witnesses = null,
    string? AdditionalNotes = null
);

public record UpdateAccidentClaimRequest(
    int? VehicleId,
    int? DriverId,
    DateTime? AccidentDate,
    string? AccidentTime,
    string? Location,
    double? Latitude,
    double? Longitude,
    string? Description,
    string? Severity,
    decimal? EstimatedDamage,
    string[]? DamagedZones,
    bool? ThirdPartyInvolved,
    string? PoliceReportNumber,
    int? MileageAtAccident,
    string? Witnesses,
    string? AdditionalNotes
);

public record ApproveClaimRequest(decimal ApprovedAmount);

public record RejectClaimRequest(string? Reason);
