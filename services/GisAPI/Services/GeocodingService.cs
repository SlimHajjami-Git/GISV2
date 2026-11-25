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
        /// <summary>Adresse formatée, null si le point n'en a aucune (repli « Adresse inconnue » à la lecture).</summary>
        public string? Address { get; set; }
        public string? PostalCode { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    private const string UnknownAddress = "Adresse inconnue";

    public GeocodingService(IConfiguration configuration)
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        _nominatimUrl = configuration["Nominatim:Url"] ?? "http://nominatim:8080";
        _maxCacheSize = configuration.GetValue("Nominatim:MaxCacheSize", 10000);
        _cacheDuration = TimeSpan.FromHours(configuration.GetValue("Nominatim:CacheHours", 24));
    }

    public async Task<string?> ReverseGeocodeAsync(double latitude, double longitude)
    {
        var result = await ReverseGeocodeAddressAsync(latitude, longitude);
        return result == null ? null : result.Address ?? UnknownAddress;
    }

    public async Task<ReverseAddress?> ReverseGeocodeAddressAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        // Round to 4 decimal places for cache key (~11m precision)
        var cacheKey = CacheKey(latitude, longitude);

        // Check cache first
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
        {
            Interlocked.Increment(ref _cacheHits);
            return new ReverseAddress(cached.Address, cached.PostalCode);
        }

        Interlocked.Increment(ref _cacheMisses);

        try
        {
            // Culture invariante : en fr-FR, « 36,8 » partirait dans l'URL et Nominatim lirait 36.
            var url = FormattableString.Invariant(
                $"{_nominatimUrl}/reverse?lat={latitude}&lon={longitude}&format=json&zoom=18&addressdetails=1&accept-language=fr");
            var response = await _httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            using var data = JsonDocument.Parse(json);

            var address = FormatAddress(data);
            var postalCode = PostalCode(data);

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

            _cache[cacheKey] = new CacheEntry
            {
                Address = address,
                PostalCode = postalCode,
                ExpiresAt = DateTime.UtcNow.Add(_cacheDuration)
            };

            return new ReverseAddress(address, postalCode);
        }
        catch
        {
            // Délai dépassé, annulation ou réponse illisible : rien en cache, on retentera.
            return null;
        }
    }

    private static string CacheKey(double latitude, double longitude) =>
        FormattableString.Invariant($"{latitude:F4},{longitude:F4}");

    private static string? PostalCode(JsonDocument data) =>
        data.RootElement.TryGetProperty("address", out var address)
        && address.TryGetProperty("postcode", out var postcode)
        && postcode.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(postcode.GetString())
            ? postcode.GetString()!.Trim()
            : null;

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

    /// <summary>Adresse lisible du point, ou null s'il n'en a aucune (l'appelant choisit le libellé de repli).</summary>
    private static string? FormatAddress(JsonDocument data)
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
            var joined = string.Join(",", displayParts).Trim();
            return joined.Length > 0 ? joined : null;
        }

        return null;
    }

    public (int CacheHits, int CacheMisses, int CacheSize) GetCacheStats()
    {
        return (_cacheHits, _cacheMisses, _cache.Count);
    }
}



