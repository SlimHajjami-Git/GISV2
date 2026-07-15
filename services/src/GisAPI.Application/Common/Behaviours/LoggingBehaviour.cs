using System.Diagnostics;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Common.Behaviours;

/// <summary>
/// Journalisation légère du pipeline MediatR.
///
/// L'ancienne version journalisait le CORPS ENTIER de chaque requête ET réponse
/// (<c>{@Request}</c> / <c>{@Response}</c>) au niveau Information pour CHAQUE
/// message — y compris les ~20 <c>BroadcastPositionCommand</c> par seconde de la
/// télémétrie GPS. Résultat : ~45 000 lignes de log en 2 minutes, un flux stdout
/// saturé, et des requêtes utilisateur qui BLOQUAIENT derrière l'écriture des
/// logs (ex. /api/users à 2,3 s alors que sa requête SQL faisait 0,4 ms). Le pod
/// n'était pas CPU-bound : c'était de l'I/O de journalisation.
///
/// Désormais :
///  - les commandes à haute fréquence (télémétrie) ne sont PAS journalisées ;
///  - on log une seule ligne concise par message (nom + user + durée), sans
///    sérialiser le corps ;
///  - les corps (requête/réponse) ne partent qu'en Debug, et seulement si activé.
/// </summary>
public class LoggingBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehaviour<TRequest, TResponse>> _logger;
    private readonly ICurrentTenantService _tenantService;

    // Messages à très haut débit : jamais journalisés (ils noieraient les logs
    // et ralentiraient tout le reste). La télémétrie a ses propres compteurs.
    private static readonly HashSet<string> HighFrequencyRequests = new(StringComparer.Ordinal)
    {
        "BroadcastPositionCommand",
        "BroadcastPositionCommandHandler",
    };

    public LoggingBehaviour(ILogger<LoggingBehaviour<TRequest, TResponse>> logger, ICurrentTenantService tenantService)
    {
        _logger = logger;
        _tenantService = tenantService;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var requestName = typeof(TRequest).Name;

        if (HighFrequencyRequests.Contains(requestName))
            return await next();

        // Corps complets uniquement en Debug (désactivé en prod par défaut).
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("MediatR ▶ {Name} {@Request}", requestName, request);

        var sw = Stopwatch.StartNew();
        var response = await next();
        sw.Stop();

        _logger.LogInformation("MediatR {Name} UserId={UserId} CompanyId={CompanyId} {Elapsed}ms",
            requestName, _tenantService.UserId, _tenantService.CompanyId, sw.ElapsedMilliseconds);

        return response;
    }
}
