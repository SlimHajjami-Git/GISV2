namespace GisAPI.Domain.Interfaces;

public interface IGeocodingService
{
    Task<string?> ReverseGeocodeAsync(double latitude, double longitude);

    /// <summary>
    /// Reverse-geocodes a coordinate into its structured administrative parts
    /// (commune / governorate / road name) rather than a single formatted
    /// string. Used by the accident detector to stamp a real location on each
    /// event at detection time. Returns <c>null</c> when the lookup fails or
    /// the provider returns no usable address — callers must degrade
    /// gracefully (the accident report shows "—" for unknown fields).
    /// </summary>
    Task<GeocodedLocation?> ReverseGeocodeStructuredAsync(double latitude, double longitude);

    /// <summary>
    /// Adresse lisible ET code postal d'un point, par le même cache que
    /// <see cref="ReverseGeocodeAsync"/> (clé arrondie à ~11 m). <c>null</c> si le
    /// fournisseur ne répond pas ; <see cref="ReverseAddress.Address"/> est <c>null</c>
    /// quand le point n'a aucune adresse connue (jamais un texte « Adresse inconnue »).
    /// </summary>
    Task<ReverseAddress?> ReverseGeocodeAddressAsync(double latitude, double longitude, CancellationToken ct = default);

    (int CacheHits, int CacheMisses, int CacheSize) GetCacheStats();
}

/// <summary>Adresse d'un point et son code postal ; l'un ou l'autre peut manquer.</summary>
public record ReverseAddress(string? Address, string? PostalCode);

/// <summary>
/// Structured reverse-geocoding result. Every field is nullable: a rural crash
/// point may resolve a governorate but no commune or named road.
/// </summary>
public record GeocodedLocation(string? Commune, string? Governorate, string? Road);
