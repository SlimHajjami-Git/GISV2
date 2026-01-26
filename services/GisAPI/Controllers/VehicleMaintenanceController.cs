using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using GisAPI.Application.Features.VehicleMaintenance.Queries;
using GisAPI.Application.Features.VehicleMaintenance.Commands;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/vehicle-maintenance")]
[Authorize]
public class VehicleMaintenanceController : ControllerBase
{
    private readonly IMediator _mediator;

    public VehicleMaintenanceController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get maintenance schedule for all vehicles
    /// </summary>
    [HttpGet]
    public async Task<ActionResult> GetMaintenanceSchedule(
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        var result = await _mediator.Send(new GetVehicleMaintenanceQuery(null, status, page, pageSize));
        return Ok(result);
    }

    /// <summary>
    /// Get maintenance schedule for a specific vehicle
    /// </summary>
    [HttpGet("vehicle/{vehicleId}")]
    public async Task<ActionResult<VehicleMaintenanceStatusDto>> GetVehicleMaintenanceSchedule(int vehicleId)
    {
        var result = await _mediator.Send(new GetVehicleMaintenanceQuery(vehicleId, null, 1, 100));
        var vehicle = result.Items.FirstOrDefault();
        if (vehicle == null)
            return NotFound();
        return Ok(vehicle);
    }

    /// <summary>
    /// Get maintenance alerts (overdue and due)
    /// </summary>
    [HttpGet("alerts")]
    public async Task<ActionResult<List<MaintenanceItemDto>>> GetAlerts()
    {
        var alerts = await _mediator.Send(new GetMaintenanceAlertsQuery());
        return Ok(alerts);
    }

    /// <summary>
    /// Get maintenance statistics
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<MaintenanceStatsDto>> GetStats()
    {
        var stats = await _mediator.Send(new GetMaintenanceStatsQuery());
        return Ok(stats);
    }

    /// <summary>
    /// Assign a maintenance template to a vehicle
    /// </summary>
    [HttpPost("assign")]
    public async Task<ActionResult<int>> AssignTemplate([FromBody] AssignTemplateRequest request)
    {
        var scheduleId = await _mediator.Send(new AssignMaintenanceTemplateCommand(request.VehicleId, request.TemplateId));
        return Ok(new { ScheduleId = scheduleId });
    }

    /// <summary>
    /// Remove a maintenance schedule
    /// </summary>
    [HttpDelete("{scheduleId}")]
    public async Task<ActionResult> RemoveSchedule(int scheduleId)
    {
        var success = await _mediator.Send(new RemoveMaintenanceScheduleCommand(scheduleId));
        if (!success)
            return NotFound();
        return NoContent();
    }

    /// <summary>
    /// Mark a maintenance as done
    /// </summary>
    [HttpPost("mark-done")]
    public async Task<ActionResult<int>> MarkDone([FromBody] MarkDoneRequest request)
    {
        var command = new MarkMaintenanceDoneCommand(
            request.VehicleId,
            request.TemplateId,
            request.Date,
            request.Mileage,
            request.Cost,
            request.SupplierId,
            request.Notes
        );

        var logId = await _mediator.Send(command);
        return Ok(new { LogId = logId, Message = "Maintenance marked as done" });
    }
}

// Request DTOs
public record AssignTemplateRequest(int VehicleId, int TemplateId);

public record MarkDoneRequest(
    int VehicleId,
    int TemplateId,
    DateTime Date,
    int Mileage,
    decimal Cost,
    int? SupplierId,
    string? Notes
);
