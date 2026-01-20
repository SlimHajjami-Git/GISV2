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
        public DateTime ExpiresAt { get; set; }
    }

    public GeocodingService(IConfiguration configuration)
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        _nominatimUrl = configuration["Nominatim:Url"] ?? "http://nominatim:8080";
        _maxCacheSize = configuration.GetValue("Nominatim:MaxCacheSize", 10000);
        _cacheDuration = TimeSpan.FromHours(configuration.GetValue("Nominatim:CacheHours", 24));
    }

    public async Task<string?> ReverseGeocodeAsync(double latitude, double longitude)
    {
        // Round to 4 decimal places for cache key (~11m precision)
        var cacheKey = $"{latitude:F4},{longitude:F4}";

        // Check cache first
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
        {
            Interlocked.Increment(ref _cacheHits);
            return cached.Address;
        }

        Interlocked.Increment(ref _cacheMisses);

        try
        {
            var url = $"{_nominatimUrl}/reverse?lat={latitude}&lon={longitude}&format=json&zoom=18";
            var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonDocument.Parse(json);
            
            var address = FormatAddress(data);
            
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
                ExpiresAt = DateTime.UtcNow.Add(_cacheDuration)
            };

            return address;
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

        return "Adresse inconnue";
    }

    public (int CacheHits, int CacheMisses, int CacheSize) GetCacheStats()
    {
        return (_cacheHits, _cacheMisses, _cache.Count);
    }
}
