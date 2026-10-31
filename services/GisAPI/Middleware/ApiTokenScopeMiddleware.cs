using GisAPI.Application.Common.Security;

namespace GisAPI.Middleware;

/// <summary>
/// Restreint une requête authentifiée par jeton d'API (migration 064) aux seules lectures d'arrêts,
/// sur une période bornée. Placé juste après l'authentification et AVANT tout le reste : les hubs
/// temps réel, les fichiers, /api/auth ou la génération de jetons eux-mêmes ne sont jamais
/// atteignables avec un jeton, quelles que soient les exemptions des middlewares suivants.
///
/// <para>La règle est la même que celle qu'applique le gestionnaire d'authentification AVANT de
/// consommer le jeton : c'est ici que le motif du refus est répondu.</para>
/// </summary>
public class ApiTokenScopeMiddleware
{
    private readonly RequestDelegate _next;

    public ApiTokenScopeMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var isToken = context.User.Identity?.IsAuthenticated == true
            && context.User.FindFirst(ApiTokenAccess.AuthMethodClaim)?.Value == ApiTokenAccess.AuthMethodValue;

        if (!isToken)
        {
            await _next(context);
            return;
        }

        var query = context.Request.Query;
        var refusal = ApiTokenAccess.Evaluate(
            context.Request.Method,
            context.Request.Path.Value ?? "",
            query["startDate"].FirstOrDefault(),
            query["endDate"].FirstOrDefault(),
            query["pageSize"].FirstOrDefault());

        if (refusal is { } r)
        {
            context.Response.StatusCode = r.StatusCode;
            await context.Response.WriteAsJsonAsync(new { message = r.Message });
            return;
        }

        await _next(context);
    }
}
