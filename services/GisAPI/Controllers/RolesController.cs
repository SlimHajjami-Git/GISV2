using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using GisAPI.Application.Features.Roles.Commands.CreateRole;
using GisAPI.Application.Features.Roles.Commands.UpdateRole;
using GisAPI.Application.Features.Roles.Commands.DeleteRole;
using GisAPI.Application.Features.Roles.Queries.GetRoles;
using GisAPI.Application.Features.Roles.Queries.GetRoleById;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RolesController : ControllerBase
{
    private readonly IMediator _mediator;

    public RolesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Récupère tous les rôles de la société (+ rôles système)
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<RoleDto>>> GetRoles([FromQuery] bool includeSystem = true)
    {
        var roles = await _mediator.Send(new GetRolesQuery(includeSystem));
        return Ok(roles);
    }

    /// <summary>
    /// Récupère un rôle par son ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<RoleDto>> GetRole(int id)
    {
        var role = await _mediator.Send(new GetRoleByIdQuery(id));
        return Ok(role);
    }

    /// <summary>
    /// Crée un nouveau rôle pour la société
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<RoleDto>> CreateRole([FromBody] CreateRoleRequest request)
    {
        var command = new CreateRoleCommand(
            request.Name,
            request.Description,
            request.RoleType ?? "employee",
            request.Permissions,
            request.IsDefault
        );

        var role = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetRole), new { id = role.Id }, role);
    }

    /// <summary>
    /// Met à jour un rôle existant
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<RoleDto>> UpdateRole(int id, [FromBody] UpdateRoleRequest request)
    {
        var command = new UpdateRoleCommand(
            id,
            request.Name,
            request.Description,
            request.RoleType,
            request.Permissions,
            request.IsDefault
        );

        var role = await _mediator.Send(command);
        return Ok(role);
    }

    /// <summary>
    /// Supprime un rôle (si aucun utilisateur n'est affecté)
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteRole(int id)
    {
        await _mediator.Send(new DeleteRoleCommand(id));
        return NoContent();
    }
}

// Request DTOs
public record CreateRoleRequest(
    string Name,
    string? Description,
    string? RoleType,
    Dictionary<string, object>? Permissions,
    bool IsDefault = false
);

public record UpdateRoleRequest(
    string? Name,
    string? Description,
    string? RoleType,
    Dictionary<string, object>? Permissions,
    bool? IsDefault
);
