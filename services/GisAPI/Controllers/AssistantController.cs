using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Controllers;

/// <summary>
/// PUBLIC (pre-login) AI automobile assistant used by the landing page.
///
/// Reuses the existing Groq LLM (<see cref="ILlmService"/>). Because it is
/// exposed anonymously, it is hardened on two fronts:
///   • per-IP + global rate limiting — the "ai-assistant" limiter wired in
///     Program.cs (partitions on <see cref="ResolveClientIp"/>);
///   • strict input caps here — message length, history size and response
///     tokens are all bounded to keep cost and abuse in check.
/// It answers GENERAL automobile questions only; no tenant/vehicle data is
/// ever loaded or exposed (it runs before login).
/// </summary>
[ApiController]
[Route("api/assistant")]
[AllowAnonymous]
public class AssistantController : ControllerBase
{
    private readonly ILlmService _llm;
    private readonly IConfiguration _config;
    private readonly ILogger<AssistantController> _logger;

    public AssistantController(ILlmService llm, IConfiguration config, ILogger<AssistantController> logger)
    {
        _llm = llm;
        _config = config;
        _logger = logger;
    }

    public record AssistantTurn(string? Role, string? Content);
    public record AssistantAskRequest(string? Message, List<AssistantTurn>? History);

    /// <summary>Ask the public automobile assistant a question. No auth required.</summary>
    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AssistantAskRequest request, CancellationToken ct)
    {
        var maxLen     = _config.GetValue<int?>("AiAssistant:MaxMessageLength") ?? 500;
        var maxHistory = _config.GetValue<int?>("AiAssistant:MaxHistoryTurns") ?? 6;
        var maxTokens  = _config.GetValue<int?>("AiAssistant:MaxResponseTokens") ?? 700;

        var message = request?.Message?.Trim();
        if (string.IsNullOrWhiteSpace(message))
            return BadRequest(new { status = 400, message = "La question ne peut pas être vide." });
        if (message.Length > maxLen)
            return BadRequest(new { status = 400, message = $"Question trop longue (max {maxLen} caractères)." });

        // Bounded conversation: keep only the last N turns, clip each turn, and
        // accept only the two valid roles. Anything else is silently dropped so a
        // crafted payload can't inflate the prompt (and therefore the token cost).
        var messages = new List<LlmMessage>();
        if (request?.History is { Count: > 0 })
        {
            foreach (var turn in request.History.TakeLast(maxHistory))
            {
                var role = turn?.Role?.Trim().ToLowerInvariant();
                var content = turn?.Content?.Trim();
                if (string.IsNullOrWhiteSpace(content)) continue;
                if (role != "user" && role != "assistant") continue;
                if (content.Length > maxLen) content = content[..maxLen];
                messages.Add(new LlmMessage(role, content));
            }
        }
        messages.Add(new LlmMessage("user", message));

        try
        {
            var result = await _llm.ChatAsync(SystemPrompt, messages, maxTokens, ct);
            var answer = result.Content?.Trim();
            if (string.IsNullOrWhiteSpace(answer))
                return StatusCode(StatusCodes.Status502BadGateway,
                    new { status = 502, message = "L'assistant n'a pas pu répondre. Réessayez." });
            return Ok(new { answer, tokensUsed = result.TokensUsed });
        }
        catch (OperationCanceledException)
        {
            // Client disconnected / request aborted — let the framework handle it.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Public assistant LLM call failed");
            return StatusCode(StatusCodes.Status502BadGateway,
                new { status = 502, message = "L'assistant est momentanément indisponible. Réessayez dans un instant." });
        }
    }

    /// <summary>
    /// Real client IP behind k3s/traefik: X-Forwarded-For first, socket peer
    /// otherwise. Kept static + public so the rate limiter in Program.cs
    /// partitions on the exact same value the controller would log.
    /// </summary>
    public static string ResolveClientIp(HttpContext ctx)
    {
        var fwd = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(fwd))
            return fwd.Split(',')[0].Trim();
        return ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    // Verbatim string (content flush-left on purpose) — a clean prompt with no
    // leading indentation leaking into the model context.
    private const string SystemPrompt = @"Tu es « l'Assistant Auto Calypso », un expert automobile francophone.
Tu réponds aux questions du grand public sur l'automobile : entretien, pannes,
voyants du tableau de bord, pièces, pneumatiques, batterie, carburant et
consommation, codes défaut OBD, conduite, sécurité et réglementation générale.

Règles :
- Réponds toujours en français, de façon claire, concise et concrète (2 à 6
  phrases). Va droit au but et propose des vérifications simples et actionnables.
- Donne des ordres de grandeur utiles (km, bar, volts, litres…) en précisant que
  le carnet d'entretien et la notice du véhicule font foi.
- Pour tout ce qui touche à la sécurité (freinage, direction, voyant rouge,
  fumée, odeur de carburant…), recommande de s'arrêter et de consulter un
  professionnel sans tarder.
- Tu n'as PAS accès aux données du véhicule de l'utilisateur : tu es la page
  publique, avant connexion. Ne prétends jamais lire son kilométrage, sa position
  ou son historique. Pour un suivi personnalisé, invite-le à se connecter à Calypso.
- Si la question n'a aucun rapport avec l'automobile, décline poliment et rappelle
  que tu es spécialisé dans l'automobile.
- N'invente pas de valeurs précises pour un modèle que tu ne connais pas ; reste
  prudent et propose de vérifier auprès du constructeur.";
}
