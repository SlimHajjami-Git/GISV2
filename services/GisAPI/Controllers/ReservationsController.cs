using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using GisAPI.Application.Features.Reservations;
using GisAPI.Application.Features.Reservations.Commands.SetVehicleRentalStatus;
using GisAPI.Application.Features.Reservations.Commands.CreateReservation;
using GisAPI.Application.Features.Reservations.Commands.CompleteReservation;
using GisAPI.Application.Features.Reservations.Commands.CancelReservation;
using GisAPI.Application.Features.Reservations.Queries.GetReservations;
using GisAPI.Application.Features.Reservations.Queries.GetAvailableVehicles;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReservationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ReservationsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<List<ReservationDto>>> GetReservations(
        [FromQuery] int? vehicleId,
        [FromQuery] int? driverId,
        [FromQuery] string? status,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        var result = await _mediator.Send(new GetReservationsQuery(vehicleId, driverId, status, from, to));
        return Ok(result);
    }

    [HttpGet("available-vehicles")]
    public async Task<ActionResult<List<AvailableVehicleDto>>> GetAvailableVehicles()
    {
        var result = await _mediator.Send(new GetAvailableVehiclesQuery());
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ReservationDto>> CreateReservation([FromBody] CreateReservationRequest request)
    {
        var result = await _mediator.Send(new CreateReservationCommand(
            request.VehicleId,
            request.AssignedDriverId,
            request.Purpose,
            request.Destination,
            request.EstimatedKm,
            request.Notes
        ));
        return CreatedAtAction(nameof(GetReservations), new { vehicleId = request.VehicleId }, result);
    }

    [HttpPut("{id}/complete")]
    public async Task<ActionResult<ReservationDto>> CompleteReservation(int id, [FromBody] CompleteReservationRequest? request)
    {
        var result = await _mediator.Send(new CompleteReservationCommand(id, request?.Notes));
        return Ok(result);
    }

    [HttpPut("{id}/cancel")]
    public async Task<ActionResult<ReservationDto>> CancelReservation(int id, [FromBody] CancelReservationRequest? request)
    {
        var result = await _mediator.Send(new CancelReservationCommand(id, request?.Reason));
        return Ok(result);
    }

    [HttpPut("~/api/vehicles/{vehicleId}/rental-status")]
    public async Task<ActionResult<VehicleRentalStatusDto>> SetRentalStatus(int vehicleId, [FromBody] SetRentalStatusRequest request)
    {
        var result = await _mediator.Send(new SetVehicleRentalStatusCommand(vehicleId, request.IsRented));
        return Ok(result);
    }
}

public record CreateReservationRequest(
    int VehicleId,
    int? AssignedDriverId,
    string? Purpose,
    string? Destination,
    int? EstimatedKm,
    string? Notes
);

public record CompleteReservationRequest(string? Notes);
public record CancelReservationRequest(string? Reason);
public record SetRentalStatusRequest(bool IsRented);
