using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace GisAPI.Middleware;

public static class ApiTokenAuthenticationExtensions
{
    /// <summary>Nom du schéma par défaut : il aiguille vers le jeton d'API ou vers le jeton de session.</summary>
    public const string SmartScheme = "Smart";

    /// <summary>
    /// Ajoute le schéma « Smart » et celui du jeton d'API (migration 064). À appeler sur
    /// <c>AddAuthentication(SmartScheme)</c>, avant <c>AddJwtBearer</c> : une requête qui porte un
    /// jeton d'API (en-tête X-Api-Token, ou Bearer cly_…) ou une clé d'API (en-tête X-Api-Key, ou
    /// Bearer clyk_…, hors route d'échange) va au gestionnaire d'API, toutes les autres au jeton de
    /// session, exactement comme avant. Partagé avec le test qui démarre un vrai serveur.
    /// </summary>
    public static AuthenticationBuilder AddApiTokenRouting(this AuthenticationBuilder builder) =>
        builder
            .AddPolicyScheme(SmartScheme, SmartScheme, options =>
            {
                options.ForwardDefaultSelector = ctx =>
                    ApiTokenAuthenticationHandler.ExtractToken(ctx.Request) != null
                    || ApiTokenAuthenticationHandler.ExtractKey(ctx.Request) != null
                        ? ApiTokenAuthenticationHandler.SchemeName
                        : JwtBearerDefaults.AuthenticationScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(
                ApiTokenAuthenticationHandler.SchemeName, _ => { });
}
