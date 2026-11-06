using GisAPI.Application.Features.Payments;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GisAPI.Controllers;

/// <summary>
/// Console d'administration — page « Paiement ClicToPay » : état de la configuration, paiement de
/// TEST d'un montant libre, vérification d'un retour, liste des tentatives. Sous /api/admin :
/// administrateur système seulement (PermissionMiddleware) ; l'administrateur restreint n'y a pas
/// accès (route absente d'AdminScreenAccess).
/// </summary>
[ApiController]
[Route("api/admin/payments")]
[Authorize]  // System admin check is done via PermissionMiddleware
public class AdminPaymentsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ClicToPayOptions _options;

    public AdminPaymentsController(IMediator mediator, ClicToPayOptions options)
    {
        _mediator = mediator;
        _options = options;
    }

    /// <summary>Ce qui est posé sur ce serveur, sans jamais renvoyer d'identifiant.</summary>
    [HttpGet("clictopay/status")]
    public ActionResult Status() => Ok(new
    {
        configured = _options.IsConfigured,
        testEnvironment = _options.IsTestEnvironment,
        host = Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var uri) ? uri.Host : null,
        clientCheckoutEnabled = _options.ClientCheckoutEnabled,
        pilotCompanyIds = string.IsNullOrWhiteSpace(_options.PilotCompanyIds) ? null : _options.PilotCompanyIds,
        mobilePageView = _options.MobilePageView,
        minAmount = OnlinePaymentProcessor.AdminTestMinAmount,
        maxAmount = OnlinePaymentProcessor.AdminTestMaxAmount
    });

    /// <summary>Ouvre une page ClicToPay pour un paiement de test.</summary>
    [HttpPost("clictopay/test")]
    public async Task<ActionResult> StartTest([FromBody] AdminTestPaymentRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new StartAdminTestPaymentCommand(request.Amount, request.Currency), ct);
        Response.Headers.CacheControl = "no-store";
        return Ok(result);
    }

    /// <summary>Vérifie une tentative (test ou client) auprès de ClicToPay.</summary>
    [HttpPost("{reference}/verify")]
    public async Task<ActionResult> Verify(string reference, CancellationToken ct)
    {
        var result = await _mediator.Send(new VerifyAdminOnlinePaymentCommand(reference), ct);
        Response.Headers.CacheControl = "no-store";
        return Ok(result);
    }

    /// <summary>Dernières tentatives, toutes sociétés (kind = subscription | admin_test, facultatif).</summary>
    [HttpGet]
    public async Task<ActionResult> List([FromQuery] int limit = 50, [FromQuery] string? kind = null, CancellationToken ct = default) =>
        Ok(await _mediator.Send(new GetAdminOnlinePaymentsQuery(limit, kind), ct));
}

/// <summary>Paiement de test : montant en unités normales (1.5 = 1,500 TND), devise TND par défaut.</summary>
public record AdminTestPaymentRequest(decimal Amount, string? Currency);
