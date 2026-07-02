using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Services;

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
    private readonly ICarAdvisorService _advisor;
    private readonly IConfiguration _config;
    private readonly ILogger<AssistantController> _logger;

    public AssistantController(ILlmService llm, ICarAdvisorService advisor, IConfiguration config, ILogger<AssistantController> logger)
    {
        _llm = llm;
        _advisor = advisor;
        _config = config;
        _logger = logger;
    }

    public record AssistantTurn(string? Role, string? Content);
    public record AssistantAskRequest(string? Message, List<AssistantTurn>? History);

    /// <summary>Ask the public automobile assistant a question. No auth required.</summary>
    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AssistantAskRequest request, CancellationToken ct)
    {
        var (messages, error) = BuildConversation(request);
        if (messages == null)
            return BadRequest(new { status = 400, message = error });

        try
        {
            // Function calling: the model can query the "Argus tunisien" tools
            // (prices, defects, parts, resale) before answering. Bounded rounds
            // keep the worst-case cost of one HTTP request predictable.
            var result = await _llm.ChatWithToolsAsync(
                SystemPrompt, messages, _advisor.Tools, _advisor.ExecuteToolAsync, MaxTokens, MaxToolRounds, ct);
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
    /// Streamed variant (Server-Sent Events): emits <c>data: {"delta":"…"}</c>
    /// fragments as the model produces the answer (tool rounds stay silent),
    /// then <c>data: {"done":true}</c> — or <c>data: {"error":"…"}</c> on
    /// failure. Same caps and rate limits as /ask.
    /// </summary>
    [HttpPost("ask/stream")]
    public async Task AskStream([FromBody] AssistantAskRequest request, CancellationToken ct)
    {
        var (messages, error) = BuildConversation(request);
        if (messages == null)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { status = 400, message = error }, ct);
            return;
        }

        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache";
        // nginx honors this per-response and disables proxy buffering — without
        // it the SSE would be held back until the proxy buffer fills.
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            var result = await _llm.ChatStreamWithToolsAsync(
                SystemPrompt, messages, _advisor.Tools, _advisor.ExecuteToolAsync,
                delta => WriteEventAsync(new { delta }, ct),
                MaxTokens, MaxToolRounds, ct);

            await WriteEventAsync(new { done = true, tokensUsed = result.TokensUsed }, ct);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — nothing to write to.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Public assistant LLM stream failed");
            try
            {
                await WriteEventAsync(new
                {
                    error = "L'assistant est momentanément indisponible. Réessayez dans un instant."
                }, ct);
            }
            catch (OperationCanceledException) { /* client gone mid-error */ }
        }
    }

    private async Task WriteEventAsync(object payload, CancellationToken ct)
    {
        await Response.WriteAsync($"data: {System.Text.Json.JsonSerializer.Serialize(payload)}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    private int MaxTokens => _config.GetValue<int?>("AiAssistant:MaxResponseTokens") ?? 700;
    private int MaxToolRounds => _config.GetValue<int?>("AiAssistant:MaxToolRounds") ?? 4;

    /// <summary>
    /// Shared validation + prompt assembly for /ask and /ask/stream. Bounded
    /// conversation: last N turns only, clipped lengths, only user/assistant
    /// roles — a crafted payload can't inflate the prompt (token cost).
    /// Returns (null, error) when the request is invalid.
    /// </summary>
    private (List<LlmMessage>? Messages, string? Error) BuildConversation(AssistantAskRequest? request)
    {
        var maxLen     = _config.GetValue<int?>("AiAssistant:MaxMessageLength") ?? 500;
        var maxHistory = _config.GetValue<int?>("AiAssistant:MaxHistoryTurns") ?? 6;

        var message = request?.Message?.Trim();
        if (string.IsNullOrWhiteSpace(message))
            return (null, "La question ne peut pas être vide.");
        if (message.Length > maxLen)
            return (null, $"Question trop longue (max {maxLen} caractères).");

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
        return (messages, null);
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
    private const string SystemPrompt = @"Tu es « l'Assistant Auto Calypso », expert automobile francophone ET conseiller
d'achat spécialisé dans le MARCHÉ TUNISIEN. Tu réponds aux questions du grand
public : entretien, pannes, voyants, pièces, pneus, batterie, carburant, codes
OBD, conduite, sécurité — et surtout l'ACHAT de voitures neuves ou d'occasion.

OUTILS (base « Argus » Calypso du marché tunisien) :
- Dès qu'une question touche à l'achat, un prix, un budget, une comparaison, la
  cote, la revente, les défauts d'un modèle ou le prix des pièces → UTILISE les
  outils AVANT de répondre (recommend_cars dès qu'un budget est cité,
  get_market_price pour évaluer une annonce, get_car_details avant de conseiller
  un modèle précis, estimate_resale pour la revente).
- RÈGLE ABSOLUE SUR LES PRIX : tout chiffre en dinars (TND) doit provenir des
  outils. Si un modèle n'est pas couvert (found=false), dis-le honnêtement et
  donne uniquement des conseils généraux SANS chiffres. N'invente JAMAIS un prix.
- Précise toujours que les prix sont indicatifs.
- Si aucun modèle ne rentre dans le budget mais que l'outil renvoie
  « alternativesAuDessusDuBudget », PROPOSE ces alternatives (« à partir de
  X TND vous avez… ») au lieu d'un simple refus.
- Si on te demande d'où viennent tes informations/prix : elles viennent de la
  base de connaissances Calypso du marché automobile tunisien (annonces et
  prix constructeurs, mise à jour régulière) — réponds-le directement, sans
  appeler d'outil et sans citer de noms d'outils techniques.

CONSEIL D'ACHAT — méthode :
1. Si le besoin est vague, pose 2-3 questions max (budget en TND, usage
   ville/route/famille/utilitaire, essence ou diesel, neuf ou occasion).
2. Recommande 2-3 modèles avec pour chacun : prix (outil), points forts,
   défauts connus à contrôler, et un mot sur la revente.
3. Pour une occasion : liste les points de contrôle spécifiques au modèle
   (outil get_car_details) et les questions à poser au vendeur.
4. Pense « Tunisie » : chevaux fiscaux (vignette/assurance, « voiture
   populaire » = 4 CV), disponibilité des pièces, réseau de réparation, climat
   chaud et poussiéreux (clim, filtration, refroidissement), routes dégradées
   (amortisseurs), régime FCR pour les Tunisiens de l'étranger.

RÈGLES GÉNÉRALES :
- Toujours en français, clair, concret et structuré (listes courtes bienvenues).
- Ordres de grandeur utiles (km, bar, volts, TND) ; le carnet d'entretien fait foi.
- Sécurité (freins, direction, voyant rouge, fumée…) → recommander l'arrêt et un
  professionnel sans tarder.
- Tu n'as PAS accès aux données du véhicule de l'utilisateur (page publique,
  avant connexion). Pour un suivi personnalisé, invite-le à se connecter à Calypso.
- Hors automobile → décline poliment, tu es spécialisé auto.";
}
