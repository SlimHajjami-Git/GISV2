using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.AiCredits;

namespace GisAPI.Controllers;

/// <summary>
/// Crédit IA MENSUEL de la société de l'appelant (22/09/2026, demande de Slim : « le quota
/// inclut l'utilisation de l'IA ») : un seul crédit en jetons pour toute l'IA — scan de
/// factures, assistant IA, comparaison et rapport véhicule, rapport IA flotte et ses
/// questions, explication de consommation, récit d'accident.
///
/// <para>Ouvert à tout utilisateur AUTHENTIFIÉ de la société, sans case de module : chaque
/// écran d'IA (assistant, rapport flotte, scan) affiche la barre, et chacun reste gardé par
/// SON droit. Aucune règle de PermissionMiddleware ne vise « /api/ai-credit » : le chemin
/// passe les contrôles de société (utilisateur supprimé refusé), aucun droit de module
/// n'est exigé ; un compte chauffeur, lui, est refusé par la garde du haut (liste blanche
/// /api/driver-app).</para>
/// </summary>
[ApiController]
[Route("api/ai-credit")]
[Authorize]
public class AiCreditController : ControllerBase
{
    private readonly IGisDbContext _context;

    public AiCreditController(IGisDbContext context)
    {
        _context = context;
    }

    private int GetCompanyId() => int.TryParse(User.FindFirst("companyId")?.Value, out var id) ? id : 0;

    /// <summary>
    /// { enabled, budgetTokens, usedTokens, remainingTokens, percentUsed, scansThisMonth,
    ///   estimatedScansLeft, resetsAt, byFeature } — même objet que GET /api/costs/scan-quota
    /// et que le « credit » joint aux réponses et aux refus des appels d'IA.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<AiCreditStatus>> Get(CancellationToken ct)
    {
        var credit = await AiCredit.LoadAsync(_context, GetCompanyId(), DateTime.UtcNow, ct);
        return Ok(credit);
    }
}
