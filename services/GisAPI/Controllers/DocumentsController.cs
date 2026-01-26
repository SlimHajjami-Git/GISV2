using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using GisAPI.Application.Features.Documents.Queries;
using GisAPI.Application.Features.Documents.Commands;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DocumentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public DocumentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get all document expiries across the fleet
    /// </summary>
    [HttpGet("expiries")]
    public async Task<ActionResult> GetExpiries(
        [FromQuery] string? documentType = null,
        [FromQuery] string? status = null,
        [FromQuery] int? vehicleId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        var result = await _mediator.Send(new GetExpiriesQuery(documentType, status, vehicleId, page, pageSize));
        return Ok(result);
    }

    /// <summary>
    /// Get expiry statistics (counts by status)
    /// </summary>
    [HttpGet("expiries/stats")]
    public async Task<ActionResult<ExpiryStatsDto>> GetExpiryStats()
    {
        var stats = await _mediator.Send(new GetExpiryStatsQuery());
        return Ok(stats);
    }

    /// <summary>
    /// Get expiries for a specific vehicle
    /// </summary>
    [HttpGet("vehicle/{vehicleId}/expiries")]
    public async Task<ActionResult<List<VehicleExpiryDto>>> GetVehicleExpiries(int vehicleId)
    {
        var expiries = await _mediator.Send(new GetVehicleExpiriesQuery(vehicleId));
        return Ok(expiries);
    }

    /// <summary>
    /// Renew a document for a vehicle
    /// </summary>
    [HttpPost("vehicle/{vehicleId}/renew")]
    public async Task<ActionResult<int>> RenewDocument(int vehicleId, [FromBody] RenewDocumentRequest request)
    {
        if (vehicleId != request.VehicleId)
            return BadRequest("Vehicle ID mismatch");

        var command = new RenewDocumentCommand(
            request.VehicleId,
            request.DocumentType,
            request.Amount,
            request.PaymentDate,
            request.NewExpiryDate,
            request.DocumentNumber,
            request.Provider,
            request.Notes,
            request.DocumentUrl
        );

        var costId = await _mediator.Send(command);
        return Ok(new { CostId = costId, Message = "Document renewed successfully" });
    }

    /// <summary>
    /// Get renewal history for a vehicle
    /// </summary>
    [HttpGet("vehicle/{vehicleId}/history")]
    public async Task<ActionResult<List<RenewalHistoryDto>>> GetRenewalHistory(int vehicleId)
    {
        var history = await _mediator.Send(new GetRenewalHistoryQuery(vehicleId));
        return Ok(history);
    }

    /// <summary>
    /// Get documents that need attention (expired or expiring soon)
    /// </summary>
    [HttpGet("alerts")]
    public async Task<ActionResult<List<VehicleExpiryDto>>> GetExpiryAlerts([FromQuery] int daysThreshold = 30)
    {
        var alerts = await _mediator.Send(new GetExpiryAlertsQuery(daysThreshold));
        return Ok(alerts);
    }
}

// Request DTO
public record RenewDocumentRequest(
    int VehicleId,
    string DocumentType,
    decimal Amount,
    DateTime PaymentDate,
    DateTime NewExpiryDate,
    string? DocumentNumber,
    string? Provider,
    string? Notes,
    string? DocumentUrl
);
