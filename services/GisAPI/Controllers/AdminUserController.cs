using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using GisAPI.Application.Features.Admin.Users;
using GisAPI.Application.Features.Admin.Users.Queries.GetAdminUsers;
using GisAPI.Application.Features.Admin.Users.Queries.GetAdminUserById;
using GisAPI.Application.Features.Admin.Users.Queries.GetAdminUserStats;
using GisAPI.Application.Features.Admin.Users.Commands.CreateAdminUser;
using GisAPI.Application.Features.Admin.Users.Commands.UpdateAdminUser;
using GisAPI.Application.Features.Admin.Users.Commands.UpdateAdminUserRole;
using GisAPI.Application.Features.Admin.Users.Commands.ChangeAdminUserStatus;
using GisAPI.Application.Features.Admin.Users.Commands.DeleteAdminUser;
using GisAPI.Application.Features.Admin.Users.Commands.ResetAdminUserPassword;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize]  // System admin check is done via PermissionMiddleware
public class AdminUserController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminUserController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<List<AdminUserDto>>> GetAllUsers(
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] int? companyId = null)
    {
        var users = await _mediator.Send(new GetAdminUsersQuery(search, status, companyId));
        return Ok(users);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AdminUserDto>> GetUser(int id)
    {
        var user = await _mediator.Send(new GetAdminUserByIdQuery(id));
        return Ok(user);
    }

    [HttpPost]
    public async Task<ActionResult<AdminUserDto>> CreateUser([FromBody] CreateAdminUserRequest request)
    {
        var command = new CreateAdminUserCommand(
            request.Name,
            request.Email,
            request.Password,
            request.Phone,
            request.CompanyId,
            request.RoleId,
            request.AssignedVehicleIds
        );
        var user = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<AdminUserDto>> UpdateUser(int id, [FromBody] UpdateAdminUserRequest request)
    {
        var command = new UpdateAdminUserCommand(id, request.Name, request.Email, request.Phone, request.Password);
        var user = await _mediator.Send(command);
        return Ok(user);
    }

    [HttpPut("{id}/role")]
    public async Task<IActionResult> UpdateUserRole(int id, [FromBody] UpdateUserRoleRequest request)
    {
        await _mediator.Send(new UpdateAdminUserRoleCommand(id, request.RoleId));
        return Ok(new { message = "Rôle mis à jour" });
    }

    [HttpPost("{id}/suspend")]
    public async Task<IActionResult> SuspendUser(int id)
    {
        await _mediator.Send(new ChangeAdminUserStatusCommand(id, "suspended"));
        return Ok(new { message = "Utilisateur suspendu" });
    }

    [HttpPost("{id}/activate")]
    public async Task<IActionResult> ActivateUser(int id)
    {
        await _mediator.Send(new ChangeAdminUserStatusCommand(id, "active"));
        return Ok(new { message = "Utilisateur activé" });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        await _mediator.Send(new DeleteAdminUserCommand(id));
        return Ok(new { message = "Utilisateur supprimé" });
    }

    [HttpPost("{id}/reset-password")]
    public async Task<IActionResult> ResetUserPassword(int id, [FromBody] ResetPasswordRequest request)
    {
        await _mediator.Send(new ResetAdminUserPasswordCommand(id, request.NewPassword));
        return Ok(new { message = "Mot de passe réinitialisé" });
    }

    [HttpGet("stats")]
    public async Task<ActionResult<UserStatsDto>> GetUserStats()
    {
        var stats = await _mediator.Send(new GetAdminUserStatsQuery());
        return Ok(stats);
    }

    [HttpGet("available-permissions")]
    public ActionResult<List<PermissionInfo>> GetAvailablePermissions()
    {
        var permissions = new List<PermissionInfo>
        {
            new() { Key = "dashboard", Label = "Tableau de bord", Description = "Accès au tableau de bord principal" },
            new() { Key = "monitoring", Label = "Monitoring", Description = "Suivi en temps réel des véhicules" },
            new() { Key = "vehicles", Label = "Véhicules", Description = "Gestion des véhicules" },
            new() { Key = "employees", Label = "Employés", Description = "Gestion des employés/conducteurs" },
            new() { Key = "gps-devices", Label = "Appareils GPS", Description = "Gestion des appareils GPS" },
            new() { Key = "maintenance", Label = "Maintenance", Description = "Gestion de la maintenance" },
            new() { Key = "costs", Label = "Coûts", Description = "Suivi des coûts" },
            new() { Key = "reports", Label = "Rapports", Description = "Génération de rapports" },
            new() { Key = "geofences", Label = "Géofences", Description = "Gestion des zones géographiques" },
            new() { Key = "notifications", Label = "Notifications", Description = "Gestion des notifications" },
            new() { Key = "settings", Label = "Paramètres", Description = "Configuration du compte" },
            new() { Key = "users", Label = "Utilisateurs", Description = "Gestion des utilisateurs de la société" }
        };

        return Ok(permissions);
    }

    [HttpGet("available-roles")]
    public ActionResult<List<RoleInfo>> GetAvailableRoles()
    {
        var roles = new List<RoleInfo>
        {
            new() { Key = "admin", Label = "Administrateur", Description = "Accès complet à toutes les fonctionnalités" },
            new() { Key = "manager", Label = "Gestionnaire", Description = "Gestion des véhicules et employés" },
            new() { Key = "supervisor", Label = "Superviseur", Description = "Supervision et rapports" },
            new() { Key = "operator", Label = "Opérateur", Description = "Monitoring et suivi basique" },
            new() { Key = "user", Label = "Utilisateur", Description = "Accès en lecture seule" }
        };

        return Ok(roles);
    }
}

// Request DTOs (kept here since they are controller-level concerns)
public class CreateAdminUserRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? CIN { get; set; }
    public int CompanyId { get; set; }
    public int RoleId { get; set; }
    public int[]? AssignedVehicleIds { get; set; }
    public string[]? Roles { get; set; }
    public string[]? Permissions { get; set; }
}

public class UpdateAdminUserRequest
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Password { get; set; }
    public string[]? Roles { get; set; }
    public string[]? Permissions { get; set; }
}

public class UpdatePermissionsRequest
{
    public string[] Permissions { get; set; } = [];
}

public class UpdateUserRoleRequest
{
    public int RoleId { get; set; }
}

public class ResetPasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
}

public class PermissionInfo
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class RoleInfo
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
