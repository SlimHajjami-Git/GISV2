using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GisAPI.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Middleware;

/// <summary>
/// Fichiers /uploads (factures scannées, justificatifs, photos et pièces de sinistre) : ils se
/// lisaient sans connexion, par qui avait le lien (campagne de tests GPA du 06/10/2026, erreur
/// n°18). Les écrans les affichent par de simples liens (&lt;img src&gt;, &lt;a href&gt;), et le
/// navigateur n'y joint pas le jeton, rangé dans le stockage local. La connexion pose donc un
/// cookie HttpOnly limité au chemin /uploads, qui porte le même jeton ; JwtBearer le lit pour
/// ces seules adresses (Program.cs), et ce middleware vérifie la société propriétaire.
/// </summary>
public static class UploadsAccess
{
    /// <summary>Cookie de l'espace client.</summary>
    public const string CookieClient = "calypso_fichiers";

    /// <summary>
    /// Cookie d'un administrateur système : distinct, pour qu'une session client ouverte dans le
    /// même navigateur n'écrase pas l'accès de l'espace /admin (et inversement).
    /// </summary>
    public const string CookieAdmin = "calypso_fichiers_admin";

    /// <summary>Jeton à valider pour une adresse /uploads : le cookie administrateur d'abord.</summary>
    public static string? JetonDuCookie(HttpRequest request) =>
        new[] { request.Cookies[CookieAdmin], request.Cookies[CookieClient] }
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

    /// <summary>Pose le cookie du jeton (connexion, rafraîchissement, reprise d'une session ouverte).</summary>
    public static void Poser(HttpRequest request, HttpResponse response, string? jeton)
    {
        if (string.IsNullOrWhiteSpace(jeton)) return;
        JwtSecurityToken lu;
        try { lu = new JwtSecurityTokenHandler().ReadJwtToken(jeton); }
        catch (ArgumentException) { return; }

        var admin = lu.Claims.Any(c => (c.Type == ClaimTypes.Role || c.Type == "role") && c.Value == "system_admin");
        response.Cookies.Append(admin ? CookieAdmin : CookieClient, jeton, Options(request, lu.ValidTo));
    }

    /// <summary>Déconnexion : le cookie de l'espace de l'appelant disparaît avec la session.</summary>
    public static void Effacer(HttpRequest request, HttpResponse response, ClaimsPrincipal appelant)
    {
        var nom = appelant.IsInRole("system_admin") ? CookieAdmin : CookieClient;
        response.Cookies.Delete(nom, Options(request, null));
    }

    private static CookieOptions Options(HttpRequest request, DateTime? expire) => new()
    {
        HttpOnly = true,
        // Derrière le proxy de production, l'API voit du HTTP : le schéma vient de l'en-tête.
        Secure = request.IsHttps
            || string.Equals(request.Headers["X-Forwarded-Proto"].FirstOrDefault(), "https", StringComparison.OrdinalIgnoreCase),
        SameSite = SameSiteMode.Strict,
        Path = "/uploads",
        Expires = expire.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(expire.Value, DateTimeKind.Utc)) : null
    };

    /// <summary>
    /// Société propriétaire du fichier, lue dans son chemin : « invoices/{société}/… »,
    /// « acquisition-receipts/{société}/… », « accident-reports/{sinistre}/… » (société du
    /// sinistre). Null pour un autre dossier : il reste réservé aux comptes connectés.
    /// </summary>
    public static async Task<int?> ProprietaireAsync(PathString chemin, IGisDbContext db, CancellationToken ct)
    {
        var segments = (chemin.Value ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        // segments[0] = « uploads »
        if (segments.Length < 4 || !int.TryParse(segments[2], out var id)) return null;

        return segments[1].ToLowerInvariant() switch
        {
            "invoices" or "acquisition-receipts" => id,
            "accident-reports" => await db.AccidentEvents.IgnoreQueryFilters().AsNoTracking()
                .Where(a => a.Id == id).Select(a => (int?)a.CompanyId).FirstOrDefaultAsync(ct) ?? -1,
            _ => null
        };
    }

    public static async Task<bool> PeutLireAsync(ClaimsPrincipal user, PathString chemin, IGisDbContext db, CancellationToken ct)
    {
        if (user.Identity?.IsAuthenticated != true) return false;
        if (user.IsInRole("system_admin")) return true;
        var proprietaire = await ProprietaireAsync(chemin, db, ct);
        if (proprietaire is null) return true;
        return int.TryParse(user.FindFirst("companyId")?.Value, out var societe) && societe == proprietaire;
    }
}

/// <summary>Garde des fichiers /uploads : 401 sans session, 404 pour le fichier d'une autre société.</summary>
public class UploadsAccessMiddleware
{
    private readonly RequestDelegate _next;

    public UploadsAccessMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IGisDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        // 404 et non 403 : un compte d'une autre société n'apprend pas que le fichier existe.
        if (!await UploadsAccess.PeutLireAsync(context.User, context.Request.Path, db, context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await _next(context);
    }
}
