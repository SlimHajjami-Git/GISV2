namespace GisAPI.Middleware;

/// <summary>
/// En-têtes de sécurité posés sur TOUTES les réponses de l'API : JSON, fichiers
/// /uploads, erreurs, refus 429, hub SignalR, Swagger en développement.
///
/// Constat de la recette du 16/09/2026 (DEF-051) : les réponses ne portaient que
/// Content-Type, Date et Server. Rien n'empêchait un navigateur de deviner le type
/// d'un fichier servi sous /uploads, ni un site tiers d'encadrer une facture ou un
/// rapport dans une iframe, ni l'URL complète de partir dans le Referer.
///
/// Encadrement : DENY et non SAMEORIGIN, parce qu'aucun écran (web ou mobile)
/// n'affiche un fichier de l'API dans une iframe, un object ou un embed — les
/// pièces s'ouvrent dans un nouvel onglet ou se téléchargent. Si un aperçu intégré
/// apparaît un jour, c'est ici qu'il faudra passer à SAMEORIGIN / 'self'.
///
/// Pas de HSTS ici : Kestrel parle HTTP, le TLS se termine en amont (Traefik).
/// Les en-têtes sont posés AVANT la suite du pipeline pour rester présents sur les
/// réponses d'erreur ; un maillon qui fixe sa propre valeur l'emporte.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        // Équivalent moderne de X-Frame-Options, qui prime dans les navigateurs récents.
        headers.ContentSecurityPolicy = "frame-ancestors 'none'";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        return _next(context);
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
