using GisAPI.Application.Features.ApiKeys.Commands.CreateApiKey;
using GisAPI.Application.Features.ApiKeys.Commands.RevokeApiKey;
using GisAPI.Application.Features.ApiKeys.Queries.GetApiKeys;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GisAPI.Controllers;

/// <summary>
/// Clés d'automatisation de l'API (migration 069), gérées par l'administrateur de la société depuis
/// Profil > Accès API. Une clé ne s'utilise PAS ici : elle s'échange contre un jeton à usage unique
/// par POST /api/api-tokens/echange. Un jeton d'API ne peut pas appeler ces routes
/// (ApiTokenScopeMiddleware), et la création, qui vérifie un mot de passe, est plafonnée par adresse IP
/// comme la génération d'un jeton (RateLimitPolicies).
/// </summary>
[ApiController]
[Route("api/api-keys")]
[Authorize]
public class ApiKeysController : ControllerBase
{
    private readonly IMediator _mediator;

    public ApiKeysController(IMediator mediator) => _mediator = mediator;

    /// <summary>Les clés de la société (sans leur secret) et les règles qui s'y appliquent.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiKeyListDto>> List() =>
        Ok(await _mediator.Send(new GetApiKeysQuery()));

    /// <summary>
    /// Crée une clé après ressaisie de l'e-mail et du mot de passe. La réponse est la SEULE occasion
    /// de lire la clé en clair.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApiKeyCreatedDto>> Create([FromBody] CreateApiKeyRequest body)
    {
        var created = await _mediator.Send(new CreateApiKeyCommand(
            body.Label,
            body.Email ?? string.Empty,
            body.Password ?? string.Empty,
            AssistantController.ResolveClientIp(HttpContext)));
        Response.Headers.CacheControl = "no-store";
        return Ok(created);
    }

    /// <summary>Révoque une clé : elle ne s'échange plus jamais. Sans effet sur une clé déjà révoquée.</summary>
    [HttpPost("{id:int}/revocation")]
    public async Task<IActionResult> Revoke(int id)
    {
        await _mediator.Send(new RevokeApiKeyCommand(id, AssistantController.ResolveClientIp(HttpContext)));
        return NoContent();
    }
}

public record CreateApiKeyRequest(string? Label, string? Email, string? Password);
