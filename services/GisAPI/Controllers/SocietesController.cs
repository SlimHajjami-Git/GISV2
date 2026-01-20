using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using GisAPI.Application.Features.Societes.Queries.GetSocietes;
using GisAPI.Application.Features.Societes.Queries.GetSocieteById;
using GisAPI.Application.Features.Societes.Commands.CreateSociete;
using GisAPI.Application.Features.Societes.Commands.UpdateSociete;
using GisAPI.Application.Features.Societes.Commands.DeleteSociete;
using GisAPI.Domain.Entities;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/admin/[controller]")]
[Authorize]
public class SocietesController : ControllerBase
{
    private readonly IMediator _mediator;

    public SocietesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Liste toutes les sociétés (admin only)
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<SocietesListResponse>> GetSocietes(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new GetSocietesQuery(search, status, page, pageSize));
        return Ok(result);
    }

    /// <summary>
    /// Récupère une société par son ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<SocieteDetailDto>> GetSociete(int id)
    {
        var societe = await _mediator.Send(new GetSocieteByIdQuery(id));
        return Ok(societe);
    }

    /// <summary>
    /// Crée une nouvelle société avec son admin
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<SocieteDetailDto>> CreateSociete([FromBody] CreateSocieteRequest request)
    {
        var command = new CreateSocieteCommand(
            request.Name,
            request.Type ?? "transport",
            request.Description,
            request.Address,
            request.City,
            request.Country ?? "TN",
            request.Phone,
            request.Email,
            request.SubscriptionTypeId,
            request.AdminName,
            request.AdminEmail,
            request.AdminPassword
        );

        var societe = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetSociete), new { id = societe.Id }, societe);
    }

    /// <summary>
    /// Met à jour une société
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<SocieteDetailDto>> UpdateSociete(int id, [FromBody] UpdateSocieteRequest request)
    {
        var command = new UpdateSocieteCommand(
            id,
            request.Name,
            request.Type,
            request.Description,
            request.Address,
            request.City,
            request.Country,
            request.Phone,
            request.Email,
            request.LogoUrl,
            request.TaxId,
            request.RC,
            request.IF,
            request.IsActive,
            request.SubscriptionStatus,
            request.BillingCycle,
            request.SubscriptionTypeId,
            request.Settings
        );

        var societe = await _mediator.Send(command);
        return Ok(societe);
    }

    /// <summary>
    /// Supprime une société (si vide)
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteSociete(int id)
    {
        await _mediator.Send(new DeleteSocieteCommand(id));
        return NoContent();
    }

    /// <summary>
    /// Suspend une société
    /// </summary>
    [HttpPost("{id}/suspend")]
    public async Task<ActionResult<SocieteDetailDto>> SuspendSociete(int id)
    {
        var command = new UpdateSocieteCommand(id, null, null, null, null, null, null, null, null, null, null, null, null, false, "suspended", null, null, null);
        var societe = await _mediator.Send(command);
        return Ok(societe);
    }

    /// <summary>
    /// Réactive une société
    /// </summary>
    [HttpPost("{id}/activate")]
    public async Task<ActionResult<SocieteDetailDto>> ActivateSociete(int id)
    {
        var command = new UpdateSocieteCommand(id, null, null, null, null, null, null, null, null, null, null, null, null, true, "active", null, null, null);
        var societe = await _mediator.Send(command);
        return Ok(societe);
    }
}

// Request DTOs
public record CreateSocieteRequest(
    string Name,
    string? Type,
    string? Description,
    string? Address,
    string? City,
    string? Country,
    string? Phone,
    string? Email,
    int? SubscriptionTypeId,
    string AdminName,
    string AdminEmail,
    string AdminPassword
);

public record UpdateSocieteRequest(
    string? Name,
    string? Type,
    string? Description,
    string? Address,
    string? City,
    string? Country,
    string? Phone,
    string? Email,
    string? LogoUrl,
    string? TaxId,
    string? RC,
    string? IF,
    bool? IsActive,
    string? SubscriptionStatus,
    string? BillingCycle,
    int? SubscriptionTypeId,
    SocieteSettings? Settings
);
