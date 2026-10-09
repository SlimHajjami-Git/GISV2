using GisAPI.Application.Features.Admin.GpsDevices;
using GisAPI.Attributes;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GisAPI.Controllers;

/// <summary>
/// Écran admin « Boîtiers GPS » (09/10/2026) : toutes les fiches, y compris celles sans
/// véhicule qu'aucun écran ne montrait, et la suppression DÉFINITIVE d'un boîtier avec
/// tout ce qui en dépend — véhicule compris — après inventaire chiffré et IMEI retapé.
///
/// La route sœur <c>DELETE /api/admin/gps-devices/{id}</c> (AdminController) ne libère
/// qu'une fiche strictement vide ; celle-ci, sous <c>/with-data</c>, est l'autre bout du
/// spectre. Un DELETE réussi est journalisé par l'audit (suppression_gps-devices) et le
/// handler écrit en plus une ligne détaillée (identité de la fiche, comptages).
/// </summary>
[ApiController]
[Route("api/admin/gps-devices")]
[Authorize]
[RequireAdmin]
public class AdminGpsDevicesController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminGpsDevicesController(IMediator mediator) => _mediator = mediator;

    /// <summary>Toutes les fiches boîtier (société, véhicule rattaché, dernière trame).</summary>
    [HttpGet]
    public async Task<ActionResult<List<AdminGpsDeviceDto>>> List(
        [FromQuery] int? companyId, [FromQuery] bool unassignedOnly = false, [FromQuery] string? search = null, CancellationToken ct = default)
    {
        return Ok(await _mediator.Send(new GetAdminGpsDevicesQuery(companyId, unassignedOnly, search), ct));
    }

    /// <summary>Ce que la suppression emporterait, table par table, et ce qu'elle refuse.</summary>
    [HttpGet("{id:int}/deletion-impact")]
    public async Task<ActionResult<GpsDeviceDeletionImpactDto>> DeletionImpact(int id, CancellationToken ct)
    {
        var impact = await _mediator.Send(new GetGpsDeviceDeletionImpactQuery(id), ct);
        return impact == null ? NotFound(new { message = "Fiche boîtier introuvable." }) : Ok(impact);
    }

    /// <summary>
    /// Suppression définitive du boîtier, de son véhicule et de toutes leurs données.
    /// <paramref name="confirmation"/> = l'IMEI retapé (ou « SUPPRIMER »), tel que
    /// l'inventaire l'annonce dans <c>confirmationToken</c>.
    /// </summary>
    [HttpDelete("{id:int}/with-data")]
    public async Task<ActionResult<DeleteGpsDeviceResult>> DeleteWithData(int id, [FromQuery] string? confirmation, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteGpsDeviceCommand(id, confirmation), ct);
        if (result.NotFound) return NotFound(new { message = result.Message });
        if (!result.Success) return BadRequest(new { message = result.Message });
        return Ok(result);
    }

    /// <summary>IMEI bloqués : leurs trames sont ignorées et leur fiche n'est plus recréée.</summary>
    [HttpGet("blocklist")]
    public async Task<ActionResult<List<GpsDeviceBlocklistDto>>> Blocklist(CancellationToken ct) =>
        Ok(await _mediator.Send(new GetGpsDeviceBlocklistQuery(), ct));

    /// <summary>Déblocage (réinstallation du boîtier).</summary>
    [HttpDelete("blocklist/{imei}")]
    public async Task<ActionResult> Unblock(string imei, CancellationToken ct)
    {
        var found = await _mediator.Send(new UnblockGpsDeviceCommand(imei), ct);
        return found ? Ok(new { message = $"Boîtier IMEI {imei} débloqué." }) : NotFound(new { message = "IMEI absent de la liste de blocage." });
    }
}
