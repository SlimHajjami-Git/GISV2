using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Application.Features.ApiTokens.Commands.GenerateApiToken;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.ApiKeys.Commands.ExchangeApiKey;

/// <summary>
/// Échange une clé d'automatisation contre un jeton d'API à usage unique (migration 069). Appelé
/// SANS session, par la tâche planifiée du client : la clé est la seule preuve d'identité.
/// </summary>
/// <param name="Key">La clé présentée (en-tête X-Api-Key), telle quelle.</param>
/// <param name="IpAddress">Adresse de l'appelant, journalisée avec le jeton obtenu.</param>
public record ExchangeApiKeyCommand(string? Key, string? IpAddress = null) : ICommand<ApiKeyExchangeResult>;

/// <summary>
/// Issue de l'échange : le jeton (statut 200) ou le refus (401 clé inconnue, révoquée ou expirée ;
/// 403 compte ou société qui ne peut plus utiliser l'API ; 429 quota). Un refus dit sa cause : celui
/// qui la lit détient déjà la clé, et c'est ce qui permet à l'exploitant du client de réagir.
/// </summary>
public record ApiKeyExchangeResult(int StatusCode, string? Message, ApiTokenCreatedDto? Token)
{
    public static ApiKeyExchangeResult Refused(int statusCode, string message) => new(statusCode, message, null);
}

public class ExchangeApiKeyCommandHandler : IRequestHandler<ExchangeApiKeyCommand, ApiKeyExchangeResult>
{
    private readonly IGisDbContext _context;
    private readonly ILogger<ExchangeApiKeyCommandHandler> _logger;

    public ExchangeApiKeyCommandHandler(IGisDbContext context, ILogger<ExchangeApiKeyCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ApiKeyExchangeResult> Handle(ExchangeApiKeyCommand request, CancellationToken ct)
    {
        var presented = request.Key?.Trim();
        if (!ApiKeyAccess.LooksLikeKey(presented))
            return Refuse(401, ApiKeyAccess.InvalidKeyMessage, null, request.IpAddress);

        var hash = ApiKeyAccess.Hash(presented!);
        // Pas de session : aucun filtre société ne s'applique, la clé désigne elle-même sa société.
        var key = await _context.ApiKeys.IgnoreQueryFilters()
            .FirstOrDefaultAsync(k => k.KeyHash == hash, ct);
        if (key == null)
            return Refuse(401, ApiKeyAccess.InvalidKeyMessage, ApiKeyAccess.DisplayPrefix(presented!), request.IpAddress);

        var now = DateTime.UtcNow;
        if (key.RevokedAt != null)
            return Refuse(401, ApiKeyAccess.RevokedMessage, key.KeyPrefix, request.IpAddress);
        if (key.ExpiresAt <= now)
            return Refuse(401, ApiKeyAccess.ExpiredMessage, key.KeyPrefix, request.IpAddress);

        // La règle des jetons, relue à CHAQUE échange : administrateur toujours actif, société
        // toujours activée par Belive. Sinon la clé tombe d'elle-même, sans action de personne.
        var owner = await _context.Users.IgnoreQueryFilters().AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == key.UserId && u.CompanyId == key.CompanyId, ct);
        if (owner == null)
            return Refuse(403, ApiKeyAccess.OwnerCannotMessage, key.KeyPrefix, request.IpAddress);
        var ownerRefusal = ApiKeyAccess.OwnerRefusal(owner,
            await ApiTokenAccess.CompanyApiAccessEnabledAsync(_context, key.CompanyId, ct));
        if (ownerRefusal != null)
            return Refuse(403, ownerRefusal, key.KeyPrefix, request.IpAddress);

        var since = now.AddHours(-24);
        var exchanges = await _context.ApiTokens.IgnoreQueryFilters()
            .CountAsync(t => t.ApiKeyId == key.Id && t.CreatedAt > since, ct);
        if (exchanges >= ApiKeyAccess.MaxExchangesPer24Hours)
            return Refuse(429, ApiKeyAccess.QuotaMessage, key.KeyPrefix, request.IpAddress);

        // Même plafond qu'à la main : une tâche qui échange sans jamais appeler s'arrête vite.
        var pending = await _context.ApiTokens.IgnoreQueryFilters()
            .CountAsync(t => t.UserId == key.UserId && t.UsedAt == null && t.ExpiresAt > now, ct);
        if (pending >= ApiTokenAccess.MaxPendingPerUser)
            return Refuse(429, ApiKeyAccess.TooManyPendingMessage, key.KeyPrefix, request.IpAddress);

        var (token, row) = ApiTokenAccess.NewToken(key.CompanyId, key.UserId, request.IpAddress, now, key.Id);
        _context.ApiTokens.Add(row);
        key.LastUsedAt = now;
        key.LastUsedIp = ApiTokenAccess.TruncateIp(request.IpAddress);
        key.UpdatedAt = now;
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Clé d'automatisation {KeyPrefix} (société {CompanyId}) échangée contre le jeton {TokenPrefix} depuis {Ip}",
            key.KeyPrefix, key.CompanyId, row.TokenPrefix, request.IpAddress ?? "adresse inconnue");

        return new ApiKeyExchangeResult(200, null,
            new ApiTokenCreatedDto(row.Id, token, row.TokenPrefix, row.ExpiresAt, ApiTokenAccess.LifetimeMinutes * 60));
    }

    private ApiKeyExchangeResult Refuse(int statusCode, string message, string? keyPrefix, string? ip)
    {
        // Le préfixe seul, jamais la clé : le journal ne doit pas devenir une source de clés.
        _logger.LogWarning("Échange de clé d'automatisation refusé ({Status}) pour {KeyPrefix} depuis {Ip} : {Message}",
            statusCode, keyPrefix ?? "clé absente ou mal formée", ip ?? "adresse inconnue", message);
        return ApiKeyExchangeResult.Refused(statusCode, message);
    }
}
