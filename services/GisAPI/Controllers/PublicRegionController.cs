using MaxMind.GeoIP2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace GisAPI.Controllers;

/// <summary>
/// Dit au front quelle vitrine publique présenter : la vitrine européenne ou
/// l'accueil habituel (copilote IA).
///
/// <para><b>Comment :</b> le pays est déduit de l'adresse IP de l'appelant par
/// consultation d'une base LOCALE embarquée dans l'image (DB-IP Country Lite,
/// licence CC-BY 4.0 — attribution : https://db-ip.com). Aucun service tiers
/// n'est appelé, l'adresse n'est ni journalisée ni conservée : elle est lue,
/// convertie en code pays, oubliée. C'est ce caractère transitoire qui rend
/// l'usage conforme au RGPD ; la politique de confidentialité le mentionne.</para>
///
/// <para><b>En cas de doute, l'accueil habituel gagne :</b> base absente,
/// adresse privée (accès direct au pod), pays hors liste — tout retombe sur
/// « default ». Mieux vaut montrer le copilote à un Européen (il a /fr en un
/// clic) que la vitrine européenne à un client tunisien.</para>
/// </summary>
[ApiController]
[Route("api/public")]
public class PublicRegionController : ControllerBase
{
    /// <summary>
    /// Pays servis par la vitrine européenne. Union européenne, AELE et
    /// Royaume-Uni : le périmètre commercial du cahier des charges.
    /// </summary>
    private static readonly HashSet<string> EuropeanCountries = new(StringComparer.OrdinalIgnoreCase)
    {
        "AD", "AT", "BE", "BG", "CH", "CY", "CZ", "DE", "DK", "EE", "ES", "FI",
        "FR", "GB", "GR", "HR", "HU", "IE", "IS", "IT", "LI", "LT", "LU", "LV",
        "MC", "MT", "NL", "NO", "PL", "PT", "RO", "SE", "SI", "SK", "SM"
    };

    // Le lecteur est coûteux à ouvrir et sûr en concurrence : un seul pour
    // toute la vie du processus. Lazy pour que l'API démarre même sans le
    // fichier — l'aiguillage se dégrade, le reste de l'application vit.
    private static readonly Lazy<DatabaseReader?> Reader = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "GeoIp", "dbip-country-lite.mmdb");
        return System.IO.File.Exists(path) ? new DatabaseReader(path) : null;
    });

    [HttpGet("region")]
    [AllowAnonymous]
    public IActionResult GetRegion()
    {
        var ip = ClientAddress();
        string? country = null;

        if (ip is not null && Reader.Value is not null
            && !IPAddress.IsLoopback(ip) && !IsPrivate(ip))
        {
            // TryCountry plutôt que Country : une adresse absente de la base
            // (plage toute neuve, réservée…) est un cas normal, pas une erreur.
            if (Reader.Value.TryCountry(ip, out var response))
                country = response?.Country?.IsoCode;
        }

        var region = country is not null && EuropeanCountries.Contains(country)
            ? "europe"
            : "default";

        // Réponse volontairement anonyme : le pays sert au front pour ses
        // propres choix d'affichage, l'adresse IP ne quitte jamais ce point.
        return Ok(new { region, country });
    }

    /// <summary>
    /// Adresse réelle de l'appelant. Derrière Traefik, elle est dans
    /// X-Forwarded-For (première entrée) ; en accès direct, sur la connexion.
    /// </summary>
    private IPAddress? ClientAddress()
    {
        var forwarded = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',')[0].Trim();
            if (IPAddress.TryParse(first, out var fromHeader)) return fromHeader;
        }
        return HttpContext.Connection.RemoteIpAddress;
    }

    /// <summary>RFC 1918 et lien local : le trafic interne du cluster.</summary>
    private static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var b = ip.GetAddressBytes();
        if (b.Length != 4) return false; // IPv6 public : la base répond.
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254);
    }
}
