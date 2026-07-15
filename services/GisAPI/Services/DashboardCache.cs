using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace GisAPI.Services;

/// <summary>
/// Cache du dashboard avec COALESCING (anti-stampede). Quand une clé expire et
/// que plusieurs requêtes concurrentes de la même société arrivent en même temps
/// (polling), l'ancien code (_cache.TryGetValue → miss → recalcul → Set) lançait
/// N recalculs complets simultanés. Ici, un verrou par clé garantit qu'UN SEUL
/// recalcul s'exécute ; les autres attendent son résultat. Le pré-chauffage
/// (DashboardPrewarmService) utilise Set() pour maintenir les clés chaudes.
/// Singleton (IMemoryCache l'est aussi).
/// </summary>
public interface IDashboardCache
{
    Task<object> GetOrCreateAsync(string key, TimeSpan ttl, Func<CancellationToken, Task<object>> factory, CancellationToken ct);
    void Set(string key, object value, TimeSpan ttl);
}

public class DashboardCache : IDashboardCache
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public DashboardCache(IMemoryCache cache) => _cache = cache;

    public void Set(string key, object value, TimeSpan ttl) => _cache.Set(key, value, ttl);

    public async Task<object> GetOrCreateAsync(string key, TimeSpan ttl, Func<CancellationToken, Task<object>> factory, CancellationToken ct)
    {
        if (_cache.TryGetValue(key, out object? cached) && cached != null)
            return cached;

        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Double-check : une autre requête a peut-être renseigné le cache
            // pendant qu'on attendait le verrou.
            if (_cache.TryGetValue(key, out object? cached2) && cached2 != null)
                return cached2;

            var result = await factory(ct);
            _cache.Set(key, result, ttl);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }
}
