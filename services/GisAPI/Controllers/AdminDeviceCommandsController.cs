using GisAPI.Application.Features.Admin.DeviceCommands;
using GisAPI.Attributes;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GisAPI.Controllers;

/// <summary>
/// Écran admin « Commandes boîtiers » : envoi d'une commande AJ+ (configuration)
/// à un, plusieurs ou tous les boîtiers d'une société, via le socket tenu par
/// l'ingest Rust. Chaque envoi laisse une ligne device_commands (audit).
///
/// La coupure moteur (AJ+STOP) est refusée ici par <see cref="DeviceCommandSafety"/> :
/// elle ne passe que par le circuit d'immobilisation et son approbation.
/// </summary>
[ApiController]
[Route("api/admin/device-commands")]
[Authorize]
[RequireAdmin]
public class AdminDeviceCommandsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminDeviceCommandsController(IMediator mediator) => _mediator = mediator;

    /// <summary>Les boîtiers d'une société, avec leur état de connexion et leur compatibilité AJ+.</summary>
    [HttpGet("targets")]
    public async Task<ActionResult<List<AdminDeviceCommandTargetDto>>> GetTargets([FromQuery] int companyId, CancellationToken ct)
    {
        if (companyId <= 0) return BadRequest(new { message = "companyId requis." });
        return Ok(await _mediator.Send(new GetAdminDeviceCommandTargetsQuery(companyId), ct));
    }

    /// <summary>Envoie la commande. 200 avec le détail par boîtier ; 400 si le texte est refusé ou la sélection vide.</summary>
    [HttpPost("send")]
    public async Task<ActionResult<AdminDeviceCommandResult>> Send([FromBody] SendAdminDeviceCommandRequest request, CancellationToken ct)
    {
        if (request.CompanyId <= 0) return BadRequest(new { message = "companyId requis." });

        var result = await _mediator.Send(new SendAdminDeviceCommandCommand(
            request.CompanyId,
            request.CommandText ?? string.Empty,
            request.DeviceIds,
            request.AllFleet), ct);

        return result.Accepted ? Ok(result) : BadRequest(result);
    }

    /// <summary>Historique (source « admin » par défaut ; source=* pour tout voir).</summary>
    [HttpGet("history")]
    public async Task<ActionResult<List<AdminDeviceCommandHistoryDto>>> GetHistory(
        [FromQuery] int? companyId, [FromQuery] int limit = 100, [FromQuery] string? source = "admin", CancellationToken ct = default)
    {
        return Ok(await _mediator.Send(new GetAdminDeviceCommandHistoryQuery(companyId, limit, source), ct));
    }
}

public class SendAdminDeviceCommandRequest
{
    public int CompanyId { get; set; }
    public string? CommandText { get; set; }
    public List<int>? DeviceIds { get; set; }
    public bool AllFleet { get; set; }
}
