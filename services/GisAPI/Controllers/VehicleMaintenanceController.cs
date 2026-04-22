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
    /// Get maintenance history/logs for a vehicle
    /// </summary>
    [HttpGet("vehicle/{vehicleId}/logs")]
    public async Task<ActionResult<List<MaintenanceLogDto>>> GetMaintenanceLogs(int vehicleId, [FromQuery] int? templateId = null)
    {
        var logs = await _mediator.Send(new GetMaintenanceLogsQuery(vehicleId, templateId));
        return Ok(logs);
    }

    /// <summary>
    /// Get all maintenance logs for reporting (all vehicles, with optional date filter)
    /// </summary>
    [HttpGet("logs")]
    public async Task<ActionResult<List<MaintenanceLogReportDto>>> GetAllMaintenanceLogs(
        [FromQuery] int? vehicleId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var logs = await _mediator.Send(new GetAllMaintenanceLogsQuery(vehicleId, startDate, endDate));
        return Ok(logs);
    }

    /// <summary>
    /// Mark a maintenance as done
    /// </summary>
    [HttpPost("mark-done")]
    public async Task<ActionResult<int>> MarkDone([FromBody] MarkDoneRequest request)
    {
        var dateUtc = request.Date.Kind == DateTimeKind.Utc ? request.Date : DateTime.SpecifyKind(request.Date, DateTimeKind.Utc);
        var command = new MarkMaintenanceDoneCommand(
            request.VehicleId,
            request.TemplateId,
            dateUtc,
            request.Mileage,
            request.Cost,
            request.SupplierId,
            request.Notes,
            LaborCost: request.LaborCost,
            PartsCost: request.PartsCost,
            ApplyFreeBenefit: request.ApplyFreeBenefit ?? true
        );

        var logId = await _mediator.Send(command);
        return Ok(new { LogId = logId, Message = "Maintenance marked as done" });
    }

    /// <summary>
    /// Declare free maintenances on a vehicle (e.g. dealership gift, constructor warranty).
    /// Creates or updates the schedule for (vehicleId, templateId) with a counter of free uses.
    /// </summary>
    [HttpPost("declare-free")]
    public async Task<ActionResult> DeclareFree([FromBody] DeclareFreeRequest request)
    {
        var expiryUtc = request.ExpiryDate.HasValue
            ? (request.ExpiryDate.Value.Kind == DateTimeKind.Utc
                ? request.ExpiryDate.Value
                : DateTime.SpecifyKind(request.ExpiryDate.Value, DateTimeKind.Utc))
            : (DateTime?)null;

        var scheduleId = await _mediator.Send(new DeclareFreeMaintenancesCommand(
            request.VehicleId,
            request.TemplateId,
            request.Count,
            request.Source,
            expiryUtc,
            request.Notes
        ));

        return Ok(new { ScheduleId = scheduleId });
    }

    /// <summary>
    /// Update the free maintenance counters/metadata on an existing schedule.
    /// </summary>
    [HttpPut("{scheduleId}/free")]
    public async Task<ActionResult> UpdateFree(int scheduleId, [FromBody] UpdateFreeRequest request)
    {
        var expiryUtc = request.ExpiryDate.HasValue
            ? (request.ExpiryDate.Value.Kind == DateTimeKind.Utc
                ? request.ExpiryDate.Value
                : DateTime.SpecifyKind(request.ExpiryDate.Value, DateTimeKind.Utc))
            : (DateTime?)null;

        var success = await _mediator.Send(new UpdateFreeMaintenanceCommand(
            scheduleId,
            request.FreeUsesTotal,
            request.FreeUsesRemaining,
            request.Source,
            expiryUtc,
            request.Notes
        ));

        if (!success) return NotFound();
        return NoContent();
    }

    /// <summary>
    /// Clear all free maintenance counters on a schedule (resets to 0).
    /// </summary>
    [HttpDelete("{scheduleId}/free")]
    public async Task<ActionResult> ClearFree(int scheduleId)
    {
        var success = await _mediator.Send(new ClearFreeMaintenanceCommand(scheduleId));
        if (!success) return NotFound();
        return NoContent();
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
    string? Notes,
    bool? ApplyFreeBenefit = null,
    decimal? LaborCost = null,
    decimal? PartsCost = null
);

public record DeclareFreeRequest(
    int VehicleId,
    int TemplateId,
    int Count,
    string? Source,
    DateTime? ExpiryDate,
    string? Notes
);

public record UpdateFreeRequest(
    int FreeUsesTotal,
    int FreeUsesRemaining,
    string? Source,
    DateTime? ExpiryDate,
    string? Notes
);
