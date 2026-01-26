using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using GisAPI.Application.Features.AccidentClaims.Queries;
using GisAPI.Application.Features.AccidentClaims.Commands;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/accident-claims")]
[Authorize]
public class AccidentClaimsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AccidentClaimsController(IMediator mediator)
    {
        _mediator = mediator;
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
