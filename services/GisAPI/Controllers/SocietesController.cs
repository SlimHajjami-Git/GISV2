using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using GisAPI.Application.Features.Societes.Queries.GetSocietes;
using GisAPI.Application.Features.Societes.Queries.GetSocieteById;
using GisAPI.Application.Features.Societes.Commands.CreateSociete;
using GisAPI.Application.Features.Societes.Commands.UpdateSociete;
using GisAPI.Application.Features.Societes.Commands.DeleteSociete;
using GisAPI.Application.Features.AiCredits;
using GisAPI.Application.Features.Societes.Commands.SetSocieteScanQuota;
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
    /// Fixe le crédit IA MENSUEL de la société, en jetons — il couvre toute l'IA (scans,
    /// assistant, rapports IA) depuis le 22/09/2026. null = défaut plateforme (60 000),
    /// 0 = IA désactivée pour la société. Rend le réglage enregistré et le crédit du mois qui
    /// en découle, ventilation par fonction comprise (barre et détail de la fiche).
    /// </summary>
    [HttpPut("{id}/scan-quota")]
    public async Task<IActionResult> SetScanQuota(int id, [FromBody] SetScanQuotaRequest request)
    {
        var tokens = request.EffectiveMonthlyTokens();
        var credit = await _mediator.Send(new SetSocieteScanQuotaCommand(id, tokens));
        return Ok(new
        {
            invoiceScanMonthlyTokens = tokens,
            budgetTokens = credit.BudgetTokens,
            usedTokens = credit.UsedTokens,
            remainingTokens = credit.RemainingTokens,
            percentUsed = credit.PercentUsed,
            scansThisMonth = credit.ScansThisMonth,
            resetsAt = credit.ResetsAt,
            byFeature = credit.ByFeature
        });
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

/// <summary>
/// Réglage du crédit IA : <see cref="MonthlyTokens"/> en jetons (null = défaut, 0 = désactivé).
/// <see cref="MonthlyLimit"/> est l'ANCIEN champ, en nombre de scans, encore envoyé par une
/// fiche admin restée en cache le temps du déploiement (le pod API part avant le front) :
/// sans lui, « 50 scans » saisis sur l'ancien écran arrivaient comme « aucun jeton précisé »
/// et remettaient la société au défaut. Converti à 3 000 jetons par scan, comme à la lecture,
/// et borné de même au plafond du réglage : une valeur que l'ancien écran acceptait (jusqu'à
/// 100 000 scans) n'est pas refusée au nom d'une limite en jetons qu'il ignorait.
/// </summary>
public record SetScanQuotaRequest(int? MonthlyTokens = null, int? MonthlyLimit = null)
{
    // Un nombre de scans négatif reste négatif, pour être refusé (400) comme avant plutôt
    // que lu comme « 0 = désactivé ».
    public int? EffectiveMonthlyTokens() =>
        MonthlyTokens ?? (MonthlyLimit is int scans
            ? (scans < 0 ? scans : AiCredit.EffectiveBudget(null, scans))
            : null);
}

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
