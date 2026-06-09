using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using MediatR;
using GisAPI.DTOs;
using GisAPI.Application.Features.Users;
using GisAPI.Application.Features.Users.Commands.CreateUser;
using GisAPI.Application.Features.Users.Commands.UpdateUser;
using GisAPI.Application.Features.Users.Commands.UpdateProfile;
using GisAPI.Application.Features.Users.Commands.ChangeMyPassword;
using GisAPI.Application.Features.Users.Commands.DeleteUser;
using GisAPI.Application.Features.Users.Queries.GetUsers;
using GisAPI.Application.Features.Users.Queries.GetUserById;
using GisAPI.Application.Features.Users.Queries.GetCurrentUser;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    private int GetUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

    [HttpGet]
    public async Task<ActionResult<List<UserListDto>>> GetUsers()
    {
        var users = await _mediator.Send(new GetUsersQuery());
        return Ok(users);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<UserListDto>> GetUser(int id)
    {
        var user = await _mediator.Send(new GetUserByIdQuery(id));
        return Ok(user);
    }

    [HttpPost]
    public async Task<ActionResult<UserListDto>> CreateUser([FromBody] CreateUserRequest request)
    {
        var command = new CreateUserCommand(
            request.FirstName,
            request.LastName,
            request.Email,
            request.Phone,
            request.Password,
            request.RoleId,
            request.AssignedVehicleIds,
            request.EmployeeRole,
            request.PermitNumber,
            request.PermitType,
            request.PermitExpiry,
            request.CIN,
            request.DateOfBirth,
            request.HireDate,
            request.AccessLevel,
            request.CanMonitoring,
            request.CanVehicles,
            request.CanDrivers,
            request.CanReports,
            request.CanGeofences,
            request.CanMaintenance,
            request.CanCosts,
            request.CanDocuments,
            request.CanAccidents,
            request.CanUsers,
            request.CanSettings,
            request.CanSuppliers,
            request.CanFleetManagement,
            request.CanFuel,
            request.CanTours,
            request.CanPlayback,
            request.CanReportTrips,
            request.CanReportFuel,
            request.CanReportSpeed,
            request.CanReportStops,
            request.CanReportMileage,
            request.CanReportCosts,
            request.CanReportMaintenance,
            request.CanReportDaily,
            request.CanReportMonthly,
            request.CanReportMileagePeriod,
            request.CanReportSpeedInfraction,
            request.CanReportDrivingBehavior
        );

        var user = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserRequest request)
    {
        var command = new UpdateUserCommand(
            id,
            request.FirstName,
            request.LastName,
            request.Email,
            request.Phone,
            request.RoleId,
            request.Status,
            request.AssignedVehicleIds,
            CanMonitoring: request.CanMonitoring,
            CanVehicles: request.CanVehicles,
            CanDrivers: request.CanDrivers,
            CanReports: request.CanReports,
            CanGeofences: request.CanGeofences,
            CanMaintenance: request.CanMaintenance,
            CanCosts: request.CanCosts,
            CanDocuments: request.CanDocuments,
            CanAccidents: request.CanAccidents,
            CanUsers: request.CanUsers,
            CanSettings: request.CanSettings,
            CanSuppliers: request.CanSuppliers,
            CanFleetManagement: request.CanFleetManagement,
            CanFuel: request.CanFuel,
            CanTours: request.CanTours,
            CanPlayback: request.CanPlayback,
            CanReportTrips: request.CanReportTrips,
            CanReportFuel: request.CanReportFuel,
            CanReportSpeed: request.CanReportSpeed,
            CanReportStops: request.CanReportStops,
            CanReportMileage: request.CanReportMileage,
            CanReportCosts: request.CanReportCosts,
            CanReportMaintenance: request.CanReportMaintenance,
            CanReportDaily: request.CanReportDaily,
            CanReportMonthly: request.CanReportMonthly,
            CanReportMileagePeriod: request.CanReportMileagePeriod,
            CanReportSpeedInfraction: request.CanReportSpeedInfraction,
            CanReportDrivingBehavior: request.CanReportDrivingBehavior
        );

        await _mediator.Send(command);
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        await _mediator.Send(new DeleteUserCommand(id));
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserListDto>> GetCurrentUser()
    {
        var user = await _mediator.Send(new GetCurrentUserQuery(GetUserId()));
        return Ok(user);
    }

    [HttpPut("me")]
    public async Task<ActionResult<UserListDto>> UpdateMyProfile([FromBody] UpdateProfileRequest request)
    {
        var command = new UpdateProfileCommand(
            request.FirstName,
            request.LastName,
            request.Email,
            request.Phone,
            request.DailyReportEmailEnabled
        );
        var user = await _mediator.Send(command);
        return Ok(user);
    }

    [HttpPut("me/password")]
    public async Task<IActionResult> ChangeMyPassword([FromBody] ChangeMyPasswordRequest request)
    {
        await _mediator.Send(new ChangeMyPasswordCommand(
            request.CurrentPassword ?? string.Empty,
            request.NewPassword ?? string.Empty));
        return NoContent();
    }
}

public record UpdateProfileRequest(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    bool DailyReportEmailEnabled = false
);

public record ChangeMyPasswordRequest(
    string CurrentPassword,
    string NewPassword
);
