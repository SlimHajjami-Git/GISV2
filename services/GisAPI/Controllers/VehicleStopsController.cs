using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using GisAPI.Application.Features.VehicleStops.Queries.GetVehicleStops;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class VehicleStopsController : ControllerBase
{
    private readonly IMediator _mediator;

    public VehicleStopsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get vehicle stops with filtering and pagination
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<VehicleStopsResultDto>> GetVehicleStops(
        [FromQuery] int? vehicleId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] string? stopType = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _mediator.Send(new GetVehicleStopsQuery(
            vehicleId, startDate, endDate, stopType, page, pageSize));
        return Ok(result);
    }

    /// <summary>
    /// Get stops for a specific vehicle
    /// </summary>
    [HttpGet("vehicle/{vehicleId}")]
    public async Task<ActionResult<VehicleStopsResultDto>> GetStopsByVehicle(
        int vehicleId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _mediator.Send(new GetVehicleStopsQuery(
            vehicleId, startDate, endDate, null, page, pageSize));
        return Ok(result);
    }

    /// <summary>
    /// Get stop statistics for a vehicle
    /// </summary>
    [HttpGet("vehicle/{vehicleId}/stats")]
    public async Task<ActionResult<VehicleStopStatsDto>> GetStopStats(
        int vehicleId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var result = await _mediator.Send(new GetVehicleStopsQuery(
            vehicleId, startDate, endDate, null, 1, 10000));

        var stops = result.Items;
        var stats = new VehicleStopStatsDto(
            TotalStops: stops.Count,
            TotalDurationSeconds: stops.Sum(s => s.DurationSeconds),
            AverageDurationSeconds: stops.Count > 0 ? (int)stops.Average(s => s.DurationSeconds) : 0,
            ParkingCount: stops.Count(s => s.StopType == "parking"),
            TrafficCount: stops.Count(s => s.StopType == "traffic"),
            DeliveryCount: stops.Count(s => s.StopType == "delivery"),
            UnauthorizedCount: stops.Count(s => !s.IsAuthorized),
            TotalFuelConsumed: stops.Where(s => s.FuelConsumed.HasValue).Sum(s => s.FuelConsumed!.Value)
        );

        return Ok(stats);
    }
}

public record VehicleStopStatsDto(
    int TotalStops,
    int TotalDurationSeconds,
    int AverageDurationSeconds,
    int ParkingCount,
    int TrafficCount,
    int DeliveryCount,
    int UnauthorizedCount,
    int TotalFuelConsumed
);
