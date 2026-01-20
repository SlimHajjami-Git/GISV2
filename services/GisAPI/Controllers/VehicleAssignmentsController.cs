using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using GisAPI.Application.Features.VehicleAssignments.Commands.AssignVehicle;
using GisAPI.Application.Features.VehicleAssignments.Commands.UnassignVehicle;
using GisAPI.Application.Features.VehicleAssignments.Queries.GetVehicleAssignments;
using GisAPI.Application.Features.VehicleAssignments.Queries.GetUserVehicles;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class VehicleAssignmentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public VehicleAssignmentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Liste les attributions de véhicules
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<VehicleAssignmentDto>>> GetAssignments(
        [FromQuery] int? vehicleId,
        [FromQuery] int? userId)
    {
        var assignments = await _mediator.Send(new GetVehicleAssignmentsQuery(vehicleId, userId));
        return Ok(assignments);
    }

    /// <summary>
    /// Liste les véhicules accessibles à l'utilisateur courant
    /// </summary>
    [HttpGet("my-vehicles")]
    public async Task<ActionResult<List<UserVehicleDto>>> GetMyVehicles()
    {
        var vehicles = await _mediator.Send(new GetUserVehiclesQuery());
        return Ok(vehicles);
    }

    /// <summary>
    /// Attribue un véhicule à un utilisateur
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<VehicleAssignmentDto>> AssignVehicle([FromBody] AssignVehicleRequest request)
    {
        var assignment = await _mediator.Send(new AssignVehicleCommand(
            request.VehicleId,
            request.UserId,
            request.Notes
        ));
        return CreatedAtAction(nameof(GetAssignments), new { vehicleId = request.VehicleId }, assignment);
    }

    /// <summary>
    /// Retire l'attribution d'un véhicule
    /// </summary>
    [HttpDelete("{vehicleId}/user/{userId}")]
    public async Task<IActionResult> UnassignVehicle(int vehicleId, int userId)
    {
        await _mediator.Send(new UnassignVehicleCommand(vehicleId, userId));
        return NoContent();
    }
}

public record AssignVehicleRequest(int VehicleId, int UserId, string? Notes);
