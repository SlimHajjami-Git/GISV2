namespace GisAPI.Domain.Interfaces;

public interface IGeocodingService
{
    Task<string?> ReverseGeocodeAsync(double latitude, double longitude);
    (int CacheHits, int CacheMisses, int CacheSize) GetCacheStats();
}


