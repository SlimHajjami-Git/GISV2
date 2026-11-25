using System.Security.Claims;
using System.Text.Encodings.Web;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GisAPI.Middleware;

/// <summary>
/// Authentifie une requête portant un jeton d'API à usage unique (migration 064) : en-tête
/// <c>X-Api-Token</c>, ou <c>Authorization: Bearer cly_…</c> — ou une clé d'API utilisée directement
/// (en-tête <c>X-Api-Key</c>, ou <c>Bearer clyk_…</c>, voir AuthenticateKeyAsync). L'identité construite reprend les claims
/// d'un jeton de session de l'utilisateur qui l'a généré, pour que le contrôle des droits, l'abonnement
/// et la portée des véhicules s'appliquent tels quels ; elle porte en plus le claim
/// <c>auth_method=api_token</c>, que <see cref="ApiTokenScopeMiddleware"/> utilise pour la restreindre.
///
/// <para><b>Le jeton est consommé ICI, de façon atomique.</b> Un seul <c>UPDATE … WHERE used_at IS
/// NULL AND expires_at &gt; maintenant</c> : de deux requêtes simultanées avec le même jeton, une seule
/// voit une ligne modifiée et passe, l'autre reçoit 401. Il n'est consommé que si l'appel est de ceux
/// qu'un jeton peut faire (<see cref="ApiTokenAccess.Evaluate"/>) : une requête mal formée ou hors
/// périmètre est refusée plus loin par le verrou SANS brûler le jeton.</para>
///
/// <para>Tout est relu en base à chaque appel (jeton utilisé ou expiré, compte désactivé, converti en
/// chauffeur ou promu administrateur système). Un échec ne dit pas pourquoi : inconnu, utilisé et
/// expiré répondent pareil.</para>
/// </summary>
public class ApiTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiToken";

    private readonly IGisDbContext _db;

    public ApiTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IGisDbContext db) : base(options, logger, encoder)
    {
        _db = db;
    }

    /// <summary>Le jeton présenté par la requête, ou null si elle n'en porte pas.</summary>
    public static string? ExtractToken(HttpRequest request)
    {
        var header = request.Headers[ApiTokenAccess.HeaderName].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(header))
            return header.Trim();

        var authorization = request.Headers.Authorization.FirstOrDefault();
        if (authorization != null && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authorization["Bearer ".Length..].Trim();
            if (ApiTokenAccess.LooksLikeToken(token))
                return token;
        }

        return null;
    }

    /// <summary>
    /// La clé d'API présentée pour un appel DIRECT (en-tête X-Api-Key, ou Bearer clyk_…), ou null. La
    /// route d'échange lit elle-même l'en-tête X-Api-Key, anonymement : elle n'est jamais authentifiée ici.
    /// </summary>
    public static string? ExtractKey(HttpRequest request)
    {
        if (IsExchangeRoute(request.Path.Value))
            return null;

        var header = request.Headers[ApiKeyAccess.HeaderName].FirstOrDefault()?.Trim();
        if (ApiKeyAccess.LooksLikeKey(header))
            return header;

        var authorization = request.Headers.Authorization.FirstOrDefault();
        if (authorization != null && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var key = authorization["Bearer ".Length..].Trim();
            if (ApiKeyAccess.LooksLikeKey(key))
                return key;
        }

        return null;
    }

    private static bool IsExchangeRoute(string? path) =>
        string.Equals(path?.TrimEnd('/'), "/api/api-tokens/echange", StringComparison.OrdinalIgnoreCase);

    /// <summary>Refus d'une clé à répondre tel quel au défi (statut et motif).</summary>
    private const string KeyRefusalItem = "api_key_refusal";

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // Une clé refusée dit sa cause, comme à l'échange : celui qui la lit détient déjà la clé,
        // et c'est ce qui permet à l'exploitant du client de réagir (clé expirée, quota…).
        if (Context.Items.TryGetValue(KeyRefusalItem, out var item) && item is ApiTokenAccess.Refusal r)
        {
            Response.StatusCode = r.StatusCode;
            await Response.WriteAsJsonAsync(new { status = r.StatusCode, message = r.Message });
            return;
        }
        await base.HandleChallengeAsync(properties);
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = ExtractToken(Request);
        if (presented == null)
        {
            var key = ExtractKey(Request);
            return key == null ? AuthenticateResult.NoResult() : await AuthenticateKeyAsync(key);
        }

        var hash = ApiTokenAccess.Hash(presented);
        var now = DateTime.UtcNow;

        var token = await _db.ApiTokens.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, Context.RequestAborted);
        if (token == null || !token.IsUsable(now))
            return AuthenticateResult.Fail("Jeton d'API invalide.");

        var user = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == token.UserId && u.CompanyId == token.CompanyId, Context.RequestAborted);
        if (user == null || ApiTokenAccess.AccountRefusal(user,
                await ApiTokenAccess.CompanyApiAccessEnabledAsync(_db, token.CompanyId, Context.RequestAborted)) != null)
            return AuthenticateResult.Fail("Jeton d'API invalide.");

        // Appel qu'un jeton ne peut pas faire : on laisse passer SANS consommer, le verrou
        // (ApiTokenScopeMiddleware) répondra le motif exact et le jeton reste utilisable.
        var query = Request.Query;
        var refusal = ApiTokenAccess.Evaluate(
            Request.Method,
            Request.Path.Value ?? "",
            query["startDate"].FirstOrDefault(),
            query["endDate"].FirstOrDefault(),
            query["pageSize"].FirstOrDefault());

        if (refusal == null)
        {
            var consumed = await _db.ApiTokens.IgnoreQueryFilters()
                .Where(t => t.Id == token.Id && t.UsedAt == null && t.ExpiresAt > now)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.UsedAt, now)
                    .SetProperty(t => t.UpdatedAt, now), Context.RequestAborted);
            if (consumed != 1)
                return AuthenticateResult.Fail("Jeton d'API invalide.");
        }

        return Success(user, new Claim("api_token_id", token.Id.ToString()));
    }

    /// <summary>
    /// Appel DIRECT avec une clé d'API (demande client du 23/11/2026 : une clé utilisable telle quelle
    /// pendant un mois, sans l'échanger contre un jeton à chaque appel). Même identité et même verrou
    /// qu'un jeton (<see cref="ApiTokenScopeMiddleware"/> : lecture des arrêts seule, 31 jours au plus) ;
    /// chaque appel permis est consigné comme un jeton créé et utilisé aussitôt, ce qui garde la trace
    /// (adresse IP, heure) et le plafond de la clé sur 24 heures, échanges et appels directs confondus.
    /// </summary>
    private async Task<AuthenticateResult> AuthenticateKeyAsync(string presented)
    {
        var hash = ApiKeyAccess.Hash(presented);
        var now = DateTime.UtcNow;

        var key = await _db.ApiKeys.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(k => k.KeyHash == hash, Context.RequestAborted);
        if (key == null)
            return RefuseKey(401, ApiKeyAccess.InvalidKeyMessage);
        if (key.RevokedAt != null)
            return RefuseKey(401, ApiKeyAccess.RevokedMessage);
        if (key.ExpiresAt <= now)
            return RefuseKey(401, ApiKeyAccess.ExpiredMessage);

        var user = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == key.UserId && u.CompanyId == key.CompanyId, Context.RequestAborted);
        if (user == null)
            return RefuseKey(403, ApiKeyAccess.OwnerCannotMessage);
        var ownerRefusal = ApiKeyAccess.OwnerRefusal(user,
            await ApiTokenAccess.CompanyApiAccessEnabledAsync(_db, key.CompanyId, Context.RequestAborted));
        if (ownerRefusal != null)
            return RefuseKey(403, ownerRefusal);

        // Appel hors périmètre : identité posée SANS rien compter, le verrou répond le motif exact.
        var query = Request.Query;
        var refusal = ApiTokenAccess.Evaluate(
            Request.Method,
            Request.Path.Value ?? "",
            query["startDate"].FirstOrDefault(),
            query["endDate"].FirstOrDefault(),
            query["pageSize"].FirstOrDefault());

        if (refusal == null)
        {
            var ip = GisAPI.Controllers.AssistantController.ResolveClientIp(Context);
            var (_, row) = ApiTokenAccess.NewToken(key.CompanyId, key.UserId, ip, now, key.Id);
            row.UsedAt = now;
            // Comptage et enregistrement atomiques (ApiKeyQuota) : des appels simultanés ne
            // dépassent plus le plafond.
            if (!await ApiKeyQuota.TryConsumeAsync(_db, key.Id, row, now, Context.RequestAborted))
                return RefuseKey(429, ApiKeyAccess.QuotaMessage);

            await _db.ApiKeys.IgnoreQueryFilters()
                .Where(k => k.Id == key.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(k => k.LastUsedAt, now)
                    .SetProperty(k => k.LastUsedIp, ApiTokenAccess.TruncateIp(ip))
                    .SetProperty(k => k.UpdatedAt, now), Context.RequestAborted);
        }

        return Success(user, new Claim("api_key_id", key.Id.ToString()));
    }

    private AuthenticateResult RefuseKey(int statusCode, string message)
    {
        Context.Items[KeyRefusalItem] = new ApiTokenAccess.Refusal(statusCode, message);
        return AuthenticateResult.Fail(message);
    }

    /// <summary>Identité d'un appel par API : les claims d'une session de l'utilisateur, plus le marqueur du verrou.</summary>
    private static AuthenticateResult Success(User user, Claim origin)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.FullName),
            new("companyId", user.CompanyId.ToString()),
            new("roleId", user.RoleId.ToString()),
            new(JwtClaims.AccountType, user.AccountType),
            new(ApiTokenAccess.AuthMethodClaim, ApiTokenAccess.AuthMethodValue),
            origin
        };
        if (user.Role != null)
        {
            claims.Add(new Claim(ClaimTypes.Role, user.Role.Name));
            if (user.Role.IsCompanyAdmin)
                claims.Add(new Claim(ClaimTypes.Role, "company_admin"));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
