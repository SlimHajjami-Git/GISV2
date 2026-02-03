using GisAPI.Application.Features.VehicleMaintenance.Commands;
using GisAPI.Application.Features.VehicleMaintenance.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/maintenance")]
[Authorize]
public class MaintenanceSchedulerController : ControllerBase
{
    private readonly IMediator _mediator;

    public MaintenanceSchedulerController(IMediator mediator)
    {
        _mediator = mediator;
    }

    #region Alerts & Stats

    /// <summary>
    /// Récupère les entretiens en retard ou dus
    /// </summary>
    [HttpGet("alerts")]
    public async Task<ActionResult<List<MaintenanceItemDto>>> GetAlerts()
    {
        var result = await _mediator.Send(new GetMaintenanceAlertsQuery());
        return Ok(result);
    }

    /// <summary>
    /// Récupère les statistiques globales de maintenance
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<MaintenanceStatsDto>> GetStats()
    {
        var result = await _mediator.Send(new GetMaintenanceStatsQuery());
        return Ok(result);
    }

    /// <summary>
    /// Récupère les notifications de maintenance non acquittées
    /// </summary>
    [HttpGet("notifications")]
    public async Task<ActionResult<List<MaintenanceNotificationDto>>> GetNotifications([FromQuery] bool unacknowledgedOnly = true)
    {
        var result = await _mediator.Send(new GetMaintenanceNotificationsQuery(unacknowledgedOnly));
        return Ok(result);
    }

    /// <summary>
    /// Acquitte une notification de maintenance
    /// </summary>
    [HttpPost("notifications/{id}/acknowledge")]
    public async Task<ActionResult> AcknowledgeNotification(long id)
    {
        var result = await _mediator.Send(new AcknowledgeMaintenanceNotificationCommand((int)id));
        return result ? Ok() : NotFound();
    }

    #endregion

    #region Vehicle Schedules

    /// <summary>
    /// Récupère les programmes de maintenance d'un véhicule
    /// </summary>
    [HttpGet("vehicles/{vehicleId}/schedules")]
    public async Task<ActionResult<List<VehicleScheduleDto>>> GetVehicleSchedules(int vehicleId)
    {
        var result = await _mediator.Send(new GetVehicleMaintenanceSchedulesQuery(vehicleId));
        return Ok(result);
    }

    /// <summary>
    /// Récupère le kilométrage actuel d'un véhicule (GPS ou manuel)
    /// </summary>
    [HttpGet("vehicles/{vehicleId}/mileage")]
    public async Task<ActionResult<VehicleMileageDto>> GetVehicleMileage(int vehicleId)
    {
        var result = await _mediator.Send(new GetCurrentVehicleMileageQuery(vehicleId));
        return Ok(result);
    }

    /// <summary>
    /// Assigne un template de maintenance à un véhicule
    /// </summary>
    [HttpPost("vehicles/{vehicleId}/schedules")]
    public async Task<ActionResult<int>> AssignTemplate(int vehicleId, [FromBody] SchedulerAssignTemplateRequest request)
    {
        var result = await _mediator.Send(new AssignMaintenanceTemplateCommand(vehicleId, request.TemplateId));
        return CreatedAtAction(nameof(GetVehicleSchedules), new { vehicleId }, result);
    }

    /// <summary>
    /// Supprime un programme de maintenance
    /// </summary>
    [HttpDelete("schedules/{scheduleId}")]
    public async Task<ActionResult> RemoveSchedule(int scheduleId)
    {
        var result = await _mediator.Send(new RemoveMaintenanceScheduleCommand(scheduleId));
        return result ? NoContent() : NotFound();
    }

    /// <summary>
    /// Met en pause un programme de maintenance
    /// </summary>
    [HttpPost("schedules/{scheduleId}/pause")]
    public async Task<ActionResult> PauseSchedule(int scheduleId, [FromBody] PauseScheduleRequest? request)
    {
        var result = await _mediator.Send(new PauseMaintenanceScheduleCommand(scheduleId, request?.Reason));
        return result ? Ok() : NotFound();
    }

    /// <summary>
    /// Reprend un programme de maintenance
    /// </summary>
    [HttpPost("schedules/{scheduleId}/resume")]
    public async Task<ActionResult> ResumeSchedule(int scheduleId)
    {
        var result = await _mediator.Send(new ResumeMaintenanceScheduleCommand(scheduleId));
        return result ? Ok() : NotFound();
    }

    /// <summary>
    /// Met à jour les intervalles personnalisés d'un programme
    /// </summary>
    [HttpPut("schedules/{scheduleId}/intervals")]
    public async Task<ActionResult> UpdateScheduleIntervals(int scheduleId, [FromBody] UpdateIntervalsRequest request)
    {
        var result = await _mediator.Send(new UpdateScheduleIntervalsCommand(
            scheduleId,
            request.CustomIntervalKm,
            request.CustomIntervalMonths,
            request.Notes
        ));
        return result ? Ok() : NotFound();
    }

    /// <summary>
    /// Marque un entretien comme réalisé
    /// </summary>
    [HttpPost("schedules/{scheduleId}/done")]
    public async Task<ActionResult<int>> MarkAsDone(int scheduleId, [FromBody] SchedulerMarkDoneRequest request)
    {
        var partsReplaced = request.PartsReplaced?.Select(p => 
            new ReplacedPartDto(p.Name, p.PartNumber, p.Quantity, p.UnitCost)).ToList();

        var result = await _mediator.Send(new MarkMaintenanceDoneCommand(
            request.VehicleId,
            request.TemplateId,
            request.Date,
            request.Mileage,
            request.Cost,
            request.SupplierId,
            request.Notes,
            request.TechnicianName,
            request.WorkOrderNumber,
            request.LaborCost,
            request.PartsCost,
            partsReplaced,
            request.QualityRating
        ));

        return Ok(result);
    }

    #endregion

    #region Template Parts

    /// <summary>
    /// Récupère les pièces d'un template
    /// </summary>
    [HttpGet("templates/{templateId}/parts")]
    public async Task<ActionResult<List<TemplatePartDto>>> GetTemplateParts(int templateId)
    {
        var result = await _mediator.Send(new GetTemplatePartsQuery(templateId));
        return Ok(result);
    }

    /// <summary>
    /// Ajoute une pièce à un template
    /// </summary>
    [HttpPost("templates/{templateId}/parts")]
    public async Task<ActionResult<int>> AddTemplatePart(int templateId, [FromBody] AddPartRequest request)
    {
        var result = await _mediator.Send(new AddTemplatePartCommand(
            templateId,
            request.PartName,
            request.PartNumber,
            request.Quantity,
            request.EstimatedUnitCost,
            request.IsRequired,
            request.PreferredSupplierId
        ));
        return CreatedAtAction(nameof(GetTemplateParts), new { templateId }, result);
    }

    /// <summary>
    /// Supprime une pièce d'un template
    /// </summary>
    [HttpDelete("parts/{partId}")]
    public async Task<ActionResult> RemovePart(int partId)
    {
        var result = await _mediator.Send(new RemoveTemplatePartCommand(partId));
        return result ? NoContent() : NotFound();
    }

    #endregion
}

#region Request DTOs

public record SchedulerAssignTemplateRequest(int TemplateId);

public record PauseScheduleRequest(string? Reason);

public record UpdateIntervalsRequest(
    int? CustomIntervalKm,
    int? CustomIntervalMonths,
    string? Notes
);

public record SchedulerMarkDoneRequest(
    int VehicleId,
    int TemplateId,
    DateTime Date,
    int Mileage,
    decimal Cost,
    int? SupplierId,
    string? Notes,
    string? TechnicianName,
    string? WorkOrderNumber,
    decimal? LaborCost,
    decimal? PartsCost,
    List<SchedulerPartReplacedRequest>? PartsReplaced,
    int? QualityRating
);

public record SchedulerPartReplacedRequest(
    string Name,
    string? PartNumber,
    int Quantity,
    decimal UnitCost
);

public record AddPartRequest(
    string PartName,
    string? PartNumber,
    int Quantity,
    decimal? EstimatedUnitCost,
    bool IsRequired,
    int? PreferredSupplierId
);

#endregion
