using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using GisAPI.Application.Features.FuelRecords.Queries.GetFuelRecords;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FuelRecordsController : ControllerBase
{
    private readonly IMediator _mediator;

    public FuelRecordsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get fuel records with filtering and pagination
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<FuelRecordsResultDto>> GetFuelRecords(
        [FromQuery] int? vehicleId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] string? eventType = null,
        [FromQuery] bool? anomaliesOnly = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _mediator.Send(new GetFuelRecordsQuery(
            vehicleId, startDate, endDate, eventType, anomaliesOnly, page, pageSize));
        return Ok(result);
    }

    /// <summary>
    /// Get fuel records for a specific vehicle
    /// </summary>
    [HttpGet("vehicle/{vehicleId}")]
    public async Task<ActionResult<FuelRecordsResultDto>> GetFuelRecordsByVehicle(
        int vehicleId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _mediator.Send(new GetFuelRecordsQuery(
            vehicleId, startDate, endDate, null, null, page, pageSize));
        return Ok(result);
    }

    /// <summary>
    /// Get refuel events only
    /// </summary>
    [HttpGet("refuels")]
    public async Task<ActionResult<FuelRecordsResultDto>> GetRefuels(
        [FromQuery] int? vehicleId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _mediator.Send(new GetFuelRecordsQuery(
            vehicleId, startDate, endDate, "refuel", null, page, pageSize));
        return Ok(result);
    }

    /// <summary>
    /// Get anomalies (theft alerts, consumption spikes)
    /// </summary>
    [HttpGet("anomalies")]
    public async Task<ActionResult<FuelRecordsResultDto>> GetAnomalies(
        [FromQuery] int? vehicleId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _mediator.Send(new GetFuelRecordsQuery(
            vehicleId, startDate, endDate, null, true, page, pageSize));
        return Ok(result);
    }

    /// <summary>
    /// Get fuel consumption report for a vehicle
    /// </summary>
    [HttpGet("vehicle/{vehicleId}/report")]
    public async Task<ActionResult<FuelReportDto>> GetFuelReport(
        int vehicleId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var result = await _mediator.Send(new GetFuelRecordsQuery(
            vehicleId, startDate, endDate, null, null, 1, 10000));

        var records = result.Items;
        var refuels = records.Where(r => r.EventType == "refuel").ToList();
        var anomalies = records.Where(r => r.IsAnomaly).ToList();

        var report = new FuelReportDto(
            VehicleId: vehicleId,
            StartDate: startDate,
            EndDate: endDate,
            TotalRecords: records.Count,
            RefuelCount: refuels.Count,
            TotalRefuelLiters: refuels.Sum(r => r.RefuelAmount ?? 0),
            TotalRefuelCost: refuels.Sum(r => r.RefuelCost ?? 0),
            AnomalyCount: anomalies.Count,
            TheftAlertCount: anomalies.Count(a => a.EventType == "theft_alert"),
            ConsumptionSpikeCount: anomalies.Count(a => a.EventType == "consumption_spike"),
            LowFuelAlertCount: records.Count(r => r.EventType == "low_fuel"),
            AverageConsumptionLPer100Km: result.Summary?.AverageConsumptionLPer100Km,
            Refuels: refuels.Select(r => new RefuelEventDto(
                r.Id,
                r.RecordedAt,
                r.FuelPercent,
                r.RefuelAmount,
                r.RefuelCost,
                r.RefuelStation,
                r.OdometerKm,
                r.Latitude,
                r.Longitude
            )).ToList(),
            Anomalies: anomalies.Select(a => new AnomalyEventDto(
                a.Id,
                a.RecordedAt,
                a.EventType,
                a.FuelPercent,
                a.FuelChange,
                a.AnomalyReason,
                a.Latitude,
                a.Longitude
            )).ToList()
        );

        return Ok(report);
    }
}

public record FuelReportDto(
    int VehicleId,
    DateTime? StartDate,
    DateTime? EndDate,
    int TotalRecords,
    int RefuelCount,
    decimal TotalRefuelLiters,
    decimal TotalRefuelCost,
    int AnomalyCount,
    int TheftAlertCount,
    int ConsumptionSpikeCount,
    int LowFuelAlertCount,
    double? AverageConsumptionLPer100Km,
    List<RefuelEventDto> Refuels,
    List<AnomalyEventDto> Anomalies
);

public record RefuelEventDto(
    long Id,
    DateTime RecordedAt,
    short FuelPercent,
    decimal? RefuelAmount,
    decimal? RefuelCost,
    string? RefuelStation,
    long? OdometerKm,
    double Latitude,
    double Longitude
);

public record AnomalyEventDto(
    long Id,
    DateTime RecordedAt,
    string EventType,
    short FuelPercent,
    short? FuelChange,
    string? AnomalyReason,
    double Latitude,
    double Longitude
);
