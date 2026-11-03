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
/// <c>X-Api-Token</c>, ou <c>Authorization: Bearer cly_…</c>. L'identité construite reprend les claims
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

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = ExtractToken(Request);
        if (presented == null)
            return AuthenticateResult.NoResult();

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

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.FullName),
            new("companyId", user.CompanyId.ToString()),
            new("roleId", user.RoleId.ToString()),
            new(JwtClaims.AccountType, user.AccountType),
            new(ApiTokenAccess.AuthMethodClaim, ApiTokenAccess.AuthMethodValue),
            new("api_token_id", token.Id.ToString())
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
