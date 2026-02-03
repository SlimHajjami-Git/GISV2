using GisAPI.Application.Features.Repairs;
using GisAPI.Application.Features.Repairs.Commands;
using GisAPI.Application.Features.Repairs.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/repairs")]
[Authorize]
public class RepairsController : ControllerBase
{
    private readonly IMediator _mediator;

    public RepairsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get all repairs with optional filters
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<RepairsListResult>> GetRepairs(
        [FromQuery] int? vehicleId = null,
        [FromQuery] string? status = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _mediator.Send(new GetRepairsQuery(vehicleId, status, fromDate, toDate, page, pageSize));
        return Ok(result);
    }

    /// <summary>
    /// Get repair by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<RepairDto>> GetRepair(int id)
    {
        var result = await _mediator.Send(new GetRepairByIdQuery(id));
        if (result == null)
            return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Get repair statistics
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<RepairStatsDto>> GetStats(
        [FromQuery] int? vehicleId = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null)
    {
        var result = await _mediator.Send(new GetRepairStatsQuery(vehicleId, fromDate, toDate));
        return Ok(result);
    }

    /// <summary>
    /// Create a new repair
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<int>> CreateRepair([FromBody] CreateRepairRequest request)
    {
        var command = new CreateRepairCommand(
            request.VehicleId,
            request.SupplierId,
            request.Description,
            request.RepairDate,
            request.MileageAtRepair,
            request.LaborCost,
            request.InvoiceNumber,
            request.Notes,
            request.Parts
        );

        var id = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetRepair), new { id }, new { Id = id });
    }

    /// <summary>
    /// Update an existing repair
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateRepair(int id, [FromBody] UpdateRepairRequest request)
    {
        var command = new UpdateRepairCommand(
            id,
            request.VehicleId,
            request.SupplierId,
            request.Description,
            request.RepairDate,
            request.MileageAtRepair,
            request.LaborCost,
            request.Status,
            request.InvoiceNumber,
            request.Notes,
            request.Parts
        );

        var success = await _mediator.Send(command);
        if (!success)
            return NotFound();
        return NoContent();
    }

    /// <summary>
    /// Delete a repair
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteRepair(int id)
    {
        var success = await _mediator.Send(new DeleteRepairCommand(id));
        if (!success)
            return NotFound();
        return NoContent();
    }

    /// <summary>
    /// Update repair status
    /// </summary>
    [HttpPatch("{id}/status")]
    public async Task<ActionResult> UpdateStatus(int id, [FromBody] UpdateStatusRequest request)
    {
        var success = await _mediator.Send(new UpdateRepairStatusCommand(id, request.Status));
        if (!success)
            return NotFound();
        return NoContent();
    }
}

// Request DTOs
public record CreateRepairRequest(
    int VehicleId,
    int? SupplierId,
    string? Description,
    DateTime RepairDate,
    int? MileageAtRepair,
    decimal LaborCost,
    string? InvoiceNumber,
    string? Notes,
    List<CreateRepairPartRequest> Parts
);

public record UpdateRepairRequest(
    int VehicleId,
    int? SupplierId,
    string? Description,
    DateTime RepairDate,
    int? MileageAtRepair,
    decimal LaborCost,
    string Status,
    string? InvoiceNumber,
    string? Notes,
    List<CreateRepairPartRequest> Parts
);

public record UpdateStatusRequest(string Status);
