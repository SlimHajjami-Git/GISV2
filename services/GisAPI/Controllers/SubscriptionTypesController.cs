using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using GisAPI.Application.Features.Admin.SubscriptionTypes.Queries.GetSubscriptionTypes;
using GisAPI.Application.Features.Admin.SubscriptionTypes.Commands.CreateSubscriptionType;
using GisAPI.Application.Features.Admin.SubscriptionTypes.Commands.UpdateSubscriptionType;
using GisAPI.Application.Features.Admin.SubscriptionTypes.Commands.DeleteSubscriptionType;

namespace GisAPI.Controllers;

/// <summary>
/// Subscription Types management endpoints - Admin only access
/// Uses CQRS pattern with MediatR
/// </summary>
[ApiController]
[Route("api/admin/subscription-types")]
[Authorize]  // System admin check is done via PermissionMiddleware
public class SubscriptionTypesController : ControllerBase
{
    private readonly IMediator _mediator;

    public SubscriptionTypesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get all subscription types
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<SubscriptionTypeDto>>> GetSubscriptionTypes([FromQuery] string? companyType)
    {
        var result = await _mediator.Send(new GetSubscriptionTypesQuery(companyType));
        return Ok(result);
    }

    /// <summary>
    /// Create a new subscription type
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<SubscriptionTypeDto>> CreateSubscriptionType([FromBody] CreateSubscriptionTypeRequest request)
    {
        try
        {
            var command = new CreateSubscriptionTypeCommand(
                request.Name,
                request.Code,
                request.Description,
                request.TargetCompanyType,
                request.MonthlyPrice,
                request.QuarterlyPrice,
                request.YearlyPrice,
                request.MonthlyDurationDays,
                request.QuarterlyDurationDays,
                request.YearlyDurationDays,
                request.MaxVehicles,
                request.MaxUsers,
                request.MaxGpsDevices,
                request.MaxGeofences,
                request.GpsTracking,
                request.GpsInstallation,
                request.ApiAccess,
                request.AdvancedReports,
                request.RealTimeAlerts,
                request.HistoryPlayback,
                request.FuelAnalysis,
                request.DrivingBehavior,
                request.HistoryRetentionDays,
                request.SortOrder
            );

            var result = await _mediator.Send(command);
            return CreatedAtAction(nameof(GetSubscriptionTypes), new { id = result.Id }, result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Update a subscription type
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<SubscriptionTypeDto>> UpdateSubscriptionType(int id, [FromBody] UpdateSubscriptionTypeRequest request)
    {
        try
        {
            var command = new UpdateSubscriptionTypeCommand(
                id,
                request.Name,
                request.Description,
                request.TargetCompanyType,
                request.MonthlyPrice,
                request.QuarterlyPrice,
                request.YearlyPrice,
                request.MonthlyDurationDays,
                request.QuarterlyDurationDays,
                request.YearlyDurationDays,
                request.MaxVehicles,
                request.MaxUsers,
                request.MaxGpsDevices,
                request.MaxGeofences,
                request.GpsTracking,
                request.GpsInstallation,
                request.ApiAccess,
                request.AdvancedReports,
                request.RealTimeAlerts,
                request.HistoryPlayback,
                request.FuelAnalysis,
                request.DrivingBehavior,
                request.HistoryRetentionDays,
                request.SortOrder,
                request.IsActive
            );

            var result = await _mediator.Send(command);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Delete a subscription type
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteSubscriptionType(int id)
    {
        try
        {
            await _mediator.Send(new DeleteSubscriptionTypeCommand(id));
            return Ok(new { message = "Type d'abonnement supprimé" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

// Request DTOs for controller binding
public class CreateSubscriptionTypeRequest
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? TargetCompanyType { get; set; }
    public decimal MonthlyPrice { get; set; }
    public decimal? QuarterlyPrice { get; set; }
    public decimal? YearlyPrice { get; set; }
    public int? MonthlyDurationDays { get; set; }
    public int? QuarterlyDurationDays { get; set; }
    public int? YearlyDurationDays { get; set; }
    public int? MaxVehicles { get; set; }
    public int? MaxUsers { get; set; }
    public int? MaxGpsDevices { get; set; }
    public int? MaxGeofences { get; set; }
    public bool GpsTracking { get; set; }
    public bool GpsInstallation { get; set; }
    public bool ApiAccess { get; set; }
    public bool AdvancedReports { get; set; }
    public bool? RealTimeAlerts { get; set; }
    public bool? HistoryPlayback { get; set; }
    public bool FuelAnalysis { get; set; }
    public bool DrivingBehavior { get; set; }
    public int? HistoryRetentionDays { get; set; }
    public int? SortOrder { get; set; }
}

public class UpdateSubscriptionTypeRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? TargetCompanyType { get; set; }
    public decimal? MonthlyPrice { get; set; }
    public decimal? QuarterlyPrice { get; set; }
    public decimal? YearlyPrice { get; set; }
    public int? MonthlyDurationDays { get; set; }
    public int? QuarterlyDurationDays { get; set; }
    public int? YearlyDurationDays { get; set; }
    public int? MaxVehicles { get; set; }
    public int? MaxUsers { get; set; }
    public int? MaxGpsDevices { get; set; }
    public int? MaxGeofences { get; set; }
    public bool? GpsTracking { get; set; }
    public bool? GpsInstallation { get; set; }
    public bool? ApiAccess { get; set; }
    public bool? AdvancedReports { get; set; }
    public bool? RealTimeAlerts { get; set; }
    public bool? HistoryPlayback { get; set; }
    public bool? FuelAnalysis { get; set; }
    public bool? DrivingBehavior { get; set; }
    public int? HistoryRetentionDays { get; set; }
    public int? SortOrder { get; set; }
    public bool? IsActive { get; set; }
}
