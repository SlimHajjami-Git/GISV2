namespace GisAPI.Domain.Common;

/// <summary>
/// Pays servis par l'offre commerciale européenne (3 €/4 € par véhicule) :
/// Union européenne, AELE et Royaume-Uni.
///
/// <para>Liste UNIQUE, partagée par tout ce qui doit trancher « européen ou
/// pas » : l'aiguillage public par adresse IP (<c>PublicRegionController</c>)
/// et l'inscription (monnaie EUR, fuseau Europe/Paris). Deux listes qui
/// divergent produiraient un compte facturé en dinars sur la vitrine en
/// euros.</para>
/// </summary>
public static class EuropeanCountries
{
    public static readonly HashSet<string> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AD", "AT", "BE", "BG", "CH", "CY", "CZ", "DE", "DK", "EE", "ES", "FI",
        "FR", "GB", "GR", "HR", "HU", "IE", "IS", "IT", "LI", "LT", "LU", "LV",
        "MC", "MT", "NL", "NO", "PL", "PT", "RO", "SE", "SI", "SK", "SM"
    };

    public static bool Contains(string? countryCode) =>
        !string.IsNullOrWhiteSpace(countryCode) && Codes.Contains(countryCode.Trim());
}
