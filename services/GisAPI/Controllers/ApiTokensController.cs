using GisAPI.Application.Common.Security;
using GisAPI.Application.Features.ApiKeys.Commands.ExchangeApiKey;
using GisAPI.Application.Features.ApiTokens.Commands.GenerateApiToken;
using GisAPI.Application.Features.ApiTokens.Queries.GetMyApiTokens;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GisAPI.Controllers;

/// <summary>
/// Jetons d'API à usage unique (migration 064) de l'utilisateur connecté. Un jeton ne peut pas
/// appeler cette route : ApiTokenScopeMiddleware le restreint aux arrêts, donc un jeton volé ne sert
/// pas à en fabriquer d'autres. Les essais sont plafonnés par adresse IP (RateLimitPolicies), comme
/// la connexion, puisque cette route vérifie un mot de passe.
/// </summary>
[ApiController]
[Route("api/api-tokens")]
[Authorize]
public class ApiTokensController : ControllerBase
{
    private readonly IMediator _mediator;

    public ApiTokensController(IMediator mediator) => _mediator = mediator;

    /// <summary>Les derniers jetons de l'utilisateur (sans leur secret).</summary>
    [HttpGet]
    public async Task<ActionResult<List<ApiTokenDto>>> List() =>
        Ok(await _mediator.Send(new GetMyApiTokensQuery()));

    /// <summary>
    /// L'accès API est-il ouvert à ce compte (administrateur de la société, société activée par
    /// Belive) ? Le profil n'affiche la section « Accès API » que dans ce cas.
    /// </summary>
    [HttpGet("acces")]
    public async Task<ActionResult<ApiAccessDto>> Access() =>
        Ok(await _mediator.Send(new GetMyApiAccessQuery()));

    /// <summary>
    /// Génère un jeton après ressaisie de l'e-mail et du mot de passe. La réponse est la SEULE
    /// occasion de lire le jeton en clair.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApiTokenCreatedDto>> Generate([FromBody] GenerateApiTokenRequest body)
    {
        var created = await _mediator.Send(new GenerateApiTokenCommand(
            body.Email ?? string.Empty,
            body.Password ?? string.Empty,
            AssistantController.ResolveClientIp(HttpContext)));
        Response.Headers.CacheControl = "no-store";
        return Ok(created);
    }

    /// <summary>
    /// Échange une clé d'automatisation (en-tête X-Api-Key, migration 069) contre un jeton à usage
    /// unique, SANS session : c'est l'appel que fait la tâche planifiée du client avant chaque lecture
    /// des arrêts. La clé ne sert qu'ici ; le jeton obtenu est identique à un jeton généré à la main.
    /// Plafonné par adresse IP (RateLimitPolicies) et par clé (100 échanges par 24 heures).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("echange")]
    public async Task<IActionResult> Exchange()
    {
        var result = await _mediator.Send(new ExchangeApiKeyCommand(
            Request.Headers[ApiKeyAccess.HeaderName].FirstOrDefault(),
            AssistantController.ResolveClientIp(HttpContext)));
        Response.Headers.CacheControl = "no-store";
        return result.Token != null
            ? Ok(result.Token)
            : StatusCode(result.StatusCode, new { status = result.StatusCode, message = result.Message });
    }
}

public record GenerateApiTokenRequest(string? Email, string? Password);
