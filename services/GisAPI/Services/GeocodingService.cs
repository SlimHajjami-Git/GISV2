using System.Collections.Concurrent;
using System.Text.Json;
using GisAPI.Domain.Interfaces;

namespace GisAPI.Services;

public class GeocodingService : IGeocodingService
{
    private readonly HttpClient _httpClient;
    private readonly string _nominatimUrl;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly int _maxCacheSize;
    private readonly TimeSpan _cacheDuration;
    private int _cacheHits;
    private int _cacheMisses;

    private class CacheEntry
    {
        public string Address { get; set; } = "";
        public string? PostalCode { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    /// <summary>Libellé de <see cref="FormatAddress"/> quand Nominatim ne rend rien d'exploitable.</summary>
    internal const string UnknownAddress = "Adresse inconnue";

    public GeocodingService(IConfiguration configuration)
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        _nominatimUrl = configuration["Nominatim:Url"] ?? "http://nominatim:8080";
        _maxCacheSize = configuration.GetValue("Nominatim:MaxCacheSize", 10000);
        _cacheDuration = TimeSpan.FromHours(configuration.GetValue("Nominatim:CacheHours", 24));
    }

    public async Task<string?> ReverseGeocodeAsync(double latitude, double longitude)
        => (await LookupAsync(latitude, longitude, CancellationToken.None))?.Address;

    public async Task<GeocodedAddress?> ReverseGeocodeWithPostcodeAsync(
        double latitude, double longitude, CancellationToken ct = default)
    {
        var entry = await LookupAsync(latitude, longitude, ct);

        // « Adresse inconnue » est un libellé d'écran : un client de l'API reçoit null, pas ce texte.
        if (entry is null || string.IsNullOrWhiteSpace(entry.Address) || entry.Address == UnknownAddress)
            return null;

        return new GeocodedAddress(entry.Address, entry.PostalCode);
    }

    /// <summary>
    /// Recherche partagée par l'écran (adresse seule) et l'API (adresse + code postal) : un seul appel
    /// à Nominatim et une seule entrée de cache par point arrondi à ~11 m. Un échec n'est pas mis en cache.
    /// </summary>
    private async Task<CacheEntry?> LookupAsync(double latitude, double longitude, CancellationToken ct)
    {
        // Round to 4 decimal places for cache key (~11m precision). Culture invariante : sous une
        // culture française, « 36,8059,10,1610 » rendait la clé ambiguë et l'URL fausse.
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var cacheKey = string.Create(inv, $"{latitude:F4},{longitude:F4}");

        // Check cache first
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
        {
            Interlocked.Increment(ref _cacheHits);
            return cached;
        }

        Interlocked.Increment(ref _cacheMisses);

        try
        {
            var url = string.Create(inv,
                $"{_nominatimUrl}/reverse?lat={latitude}&lon={longitude}&format=json&zoom=18&accept-language=fr");
            using var response = await _httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            using var data = JsonDocument.Parse(json);

            var entry = new CacheEntry
            {
                Address = FormatAddress(data),
                PostalCode = ExtractPostcode(data),
                ExpiresAt = DateTime.UtcNow.Add(_cacheDuration)
            };

            // Add to cache (evict old entries if needed)
            if (_cache.Count >= _maxCacheSize)
            {
                // Simple eviction: remove expired entries
                var expiredKeys = _cache
                    .Where(kv => kv.Value.ExpiresAt <= DateTime.UtcNow)
                    .Select(kv => kv.Key)
                    .Take(1000)
                    .ToList();

                foreach (var key in expiredKeys)
                    _cache.TryRemove(key, out _);
            }

            // Borne mémoire : cache encore plein après la purge (beaucoup de points distincts en
            // moins de 24 h, par exemple un appel API sur 31 jours) => le résultat est rendu sans
            // être gardé, au lieu de faire grossir le cache sans limite.
            if (_cache.Count < _maxCacheSize)
                _cache[cacheKey] = entry;

            return entry;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Code postal de la réponse Nominatim (<c>address.postcode</c>), ou <c>null</c>.</summary>
    internal static string? ExtractPostcode(JsonDocument data)
    {
        if (data.RootElement.ValueKind == JsonValueKind.Object
            && data.RootElement.TryGetProperty("address", out var address)
            && address.ValueKind == JsonValueKind.Object
            && address.TryGetProperty("postcode", out var postcode)
            && postcode.ValueKind == JsonValueKind.String)
        {
            var value = postcode.GetString()?.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
        return null;
    }

    /// <summary>
    /// Structured reverse geocoding — pulls commune (city/town/village/…),
    /// governorate (Nominatim <c>state</c>) and the road name out of the raw
    /// Nominatim response. Accidents are rare so this path is intentionally
    /// un-cached and always hits the provider for the freshest address.
    /// </summary>
    public async Task<GeocodedLocation?> ReverseGeocodeStructuredAsync(double latitude, double longitude)
    {
        try
        {
            var url = $"{_nominatimUrl}/reverse?lat={latitude}&lon={longitude}&format=json&zoom=18&accept-language=fr";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            using var data = JsonDocument.Parse(json);
            var root = data.RootElement;

            if (!root.TryGetProperty("address", out var address))
                return null;

            string? Get(string prop) =>
                address.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString()
                    : null;

            // Commune: prefer the most specific populated place.
            var commune = Get("city") ?? Get("town") ?? Get("village")
                       ?? Get("municipality") ?? Get("suburb") ?? Get("county");

            // In Tunisia (and most countries) the governorate maps to "state".
            var governorate = Get("state") ?? Get("region");

            var road = Get("road");

            if (string.IsNullOrWhiteSpace(commune)
             && string.IsNullOrWhiteSpace(governorate)
             && string.IsNullOrWhiteSpace(road))
                return null;

            return new GeocodedLocation(commune, governorate, road);
        }
        catch
        {
            return null;
        }
    }

    private string FormatAddress(JsonDocument data)
    {
        var root = data.RootElement;
        var parts = new List<string>();

        if (root.TryGetProperty("address", out var address))
        {
            // Street with number
            if (address.TryGetProperty("road", out var road))
            {
                var roadStr = road.GetString() ?? "";
                if (address.TryGetProperty("house_number", out var number))
                {
                    parts.Add($"{number.GetString()} {roadStr}");
                }
                else
                {
                    parts.Add(roadStr);
                }
            }

            // City/Town/Village
            string? locality = null;
            if (address.TryGetProperty("city", out var city))
                locality = city.GetString();
            else if (address.TryGetProperty("town", out var town))
                locality = town.GetString();
            else if (address.TryGetProperty("village", out var village))
                locality = village.GetString();
            else if (address.TryGetProperty("suburb", out var suburb))
                locality = suburb.GetString();

            if (!string.IsNullOrEmpty(locality))
                parts.Add(locality);
        }

        if (parts.Count > 0)
            return string.Join(", ", parts);

        // Fallback to display_name
        if (root.TryGetProperty("display_name", out var displayName))
        {
            var display = displayName.GetString() ?? "";
            var displayParts = display.Split(',').Take(3);
            return string.Join(",", displayParts).Trim();
        }

        return UnknownAddress;
    }

    public (int CacheHits, int CacheMisses, int CacheSize) GetCacheStats()
    {
        return (_cacheHits, _cacheMisses, _cache.Count);
    }
}



