using System.Globalization;
using GisAPI.Domain.Interfaces;

namespace GisAPI.Application.Features.Reports.Queries.GetStopsReport;

/// <summary>
/// Adresse et code postal des arrêts renvoyés à un client de l'API (plainte du 25/11/2026 :
/// « la liste donne des adresses null, il n'y a pas de code postal »).
///
/// <para>L'écran Calypso calcule l'adresse dans le navigateur (<c>enrichAllAddresses</c>) ; un appel
/// par clé ou jeton recevait donc <c>address: null</c>. Ce complément n'est appliqué QU'aux appels
/// d'API : l'écran n'attend pas des recherches qu'il refait de toute façon.</para>
///
/// <para>Bornes, parce que Nominatim est partagé avec tout le reste (détection d'accidents, écran) et
/// qu'un appel peut couvrir 31 jours de toute une flotte : au plus <see cref="MaxLookupsPerCall"/>
/// points distincts (arrondis à ~11 m, comme le cache), <see cref="Parallelism"/> recherches à la fois,
/// et <see cref="TimeBudget"/> au total. Au-delà, l'adresse reste <c>null</c> sans erreur ; un nouvel
/// appel la retrouve, les points déjà résolus étant en cache.</para>
/// </summary>
public static class StopAddressEnricher
{
    public const int MaxLookupsPerCall = 500;
    public const int Parallelism = 4;
    public static readonly TimeSpan TimeBudget = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Remplit <see cref="StopEntryDto.Address"/> et <see cref="StopEntryDto.PostalCode"/> des arrêts
    /// sans adresse. Renvoie le nombre d'arrêts complétés. Une recherche qui échoue laisse l'arrêt
    /// tel quel ; seule l'annulation de <paramref name="ct"/> (client parti) remonte.
    /// </summary>
    public static async Task<int> EnrichAsync(
        IEnumerable<StopsReportDto> reports,
        IGeocodingService geocoder,
        CancellationToken ct,
        int maxLookups = MaxLookupsPerCall,
        TimeSpan? budget = null)
    {
        var stops = reports
            .SelectMany(r => r.Days)
            .SelectMany(d => d.Stops)
            .Where(s => s.Address is null && HasPosition(s))
            .ToList();

        if (stops.Count == 0 || maxLookups <= 0)
            return 0;

        // Un même lieu (dépôt, client habituel) revient souvent : une seule recherche par point.
        var byPoint = stops
            .GroupBy(Key)
            .Take(maxLookups)
            .ToList();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(budget ?? TimeBudget);
        using var gate = new SemaphoreSlim(Parallelism);

        var lookups = byPoint.Select(async group =>
        {
            var first = group.First();
            try
            {
                await gate.WaitAsync(deadline.Token);
            }
            catch (OperationCanceledException)
            {
                return (group, (GeocodedAddress?)null);
            }

            try
            {
                return (group, await geocoder.ReverseGeocodeWithPostcodeAsync(first.Latitude, first.Longitude, deadline.Token));
            }
            catch (Exception)
            {
                return (group, (GeocodedAddress?)null);
            }
            finally
            {
                gate.Release();
            }
        }).ToList();

        var results = await Task.WhenAll(lookups);
        ct.ThrowIfCancellationRequested();

        var filled = 0;
        foreach (var (group, found) in results)
        {
            if (found is null) continue;
            foreach (var stop in group)
            {
                stop.Address = found.Address;
                stop.PostalCode = found.PostalCode;
                filled++;
            }
        }
        return filled;
    }

    // Position absente du boîtier (0,0) : rien à chercher, et Nominatim répondrait « océan ».
    private static bool HasPosition(StopEntryDto s) =>
        !(s.Latitude == 0 && s.Longitude == 0)
        && !double.IsNaN(s.Latitude) && !double.IsNaN(s.Longitude);

    private static string Key(StopEntryDto s) =>
        string.Create(CultureInfo.InvariantCulture, $"{s.Latitude:F4},{s.Longitude:F4}");
}
