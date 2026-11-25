using System.Globalization;
using GisAPI.Domain.Interfaces;

namespace GisAPI.Application.Features.Reports.Queries.GetStopsReport;

/// <summary>
/// Adresse et code postal des arrêts, calculés par le serveur pour les appels par clé ou jeton
/// d'API (demande client du 24/11/2026 : « address: null » sur /api/reports/stops).
///
/// <para>L'écran Calypso géocode lui-même ses arrêts (reports.component, enrichAllAddresses) ;
/// une application tierce, elle, ne recevait que des coordonnées. Le géocodage passe par le
/// Nominatim du cluster (17 ms par point mesurées sur TN, code postal présent 24 fois sur 25) et par
/// le cache de <see cref="IGeocodingService"/> (clé arrondie à ~11 m, partagé avec l'écran).</para>
///
/// <para>Bornes, pour qu'une période de 31 jours sur toute une flotte reste une réponse rapide :
/// les points sont dédoublonnés avant tout appel (un véhicule revient sans cesse au même dépôt),
/// <see cref="MaxConcurrency"/> appels en parallèle au plus, et <see cref="Budget"/> de temps au
/// total. Un point non résolu dans le temps imparti garde une adresse nulle — la réponse part
/// quand même, l'appel suivant le trouvera en cache.</para>
/// </summary>
public static class StopAddresses
{
    public const int MaxConcurrency = 4;
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(20);

    /// <summary>Renseigne Address et PostalCode de chaque arrêt de ces rapports.</summary>
    public static Task EnrichAsync(IGeocodingService geocoding, IEnumerable<StopsReportDto> reports, CancellationToken ct) =>
        EnrichStopsAsync(geocoding, reports.SelectMany(r => r.Days).SelectMany(d => d.Stops), Budget, ct);

    /// <summary>Renseigne Address et PostalCode de ces arrêts, en <paramref name="budget"/> au plus.</summary>
    public static async Task EnrichStopsAsync(
        IGeocodingService geocoding, IEnumerable<StopEntryDto> stops, TimeSpan budget, CancellationToken ct)
    {
        var byPoint = stops
            .Where(s => s.Latitude != 0 || s.Longitude != 0)
            .GroupBy(s => PointKey(s.Latitude, s.Longitude))
            .ToList();
        if (byPoint.Count == 0)
            return;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(budget);
        using var gate = new SemaphoreSlim(MaxConcurrency);

        var tasks = byPoint.Select(async group =>
        {
            try
            {
                await gate.WaitAsync(deadline.Token);
            }
            catch (OperationCanceledException)
            {
                return; // budget épuisé avant même d'appeler : adresse laissée nulle
            }

            try
            {
                var first = group.First();
                var found = await geocoding.ReverseGeocodeAddressAsync(first.Latitude, first.Longitude, deadline.Token);
                if (found == null)
                    return;
                foreach (var stop in group)
                {
                    stop.Address = found.Address;
                    stop.PostalCode = found.PostalCode;
                }
            }
            catch (OperationCanceledException)
            {
                // budget épuisé pendant l'appel : adresse laissée nulle
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>Même arrondi que le cache du géocodage (~11 m) : deux arrêts au même endroit, un seul appel.</summary>
    private static string PointKey(double latitude, double longitude) =>
        string.Create(CultureInfo.InvariantCulture, $"{latitude:F4},{longitude:F4}");
}
