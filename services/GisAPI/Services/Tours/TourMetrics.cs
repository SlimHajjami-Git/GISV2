using GisAPI.Application.Common;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services.Tours;

/// <summary>
/// Métriques RÉELLES d'une tournée qu'on clôt : distance parcourue d'après la trace du
/// boîtier, carburant qui en découle. Une seule définition pour les deux chemins de
/// clôture : le moniteur (arrivée détectée) et le chauffeur (« Je suis arrivé » à la
/// destination).
///
/// Relecture du 21/09/2026 (F21) : seul le moniteur les calculait. Le chauffeur qui
/// touche « Je suis arrivé » avant la détection — le cas normal avec l'application —
/// clôturait la tournée sans distance ni carburant, et une tournée terminée n'est plus
/// relue : « Distance réelle : - » dans le détail et le PDF, statistiques retombées sur
/// l'estimation, alors que la même tournée close par le moniteur 20 s plus tard avait
/// ses chiffres réels.
/// </summary>
public static class TourMetrics
{
    /// <summary>Consommation retenue faute de mesure (gazole, L/100 km) — valeur historique du moniteur.</summary>
    public const decimal EstimatedConsumptionPer100Km = 8.0m;

    /// <summary>Garde de volume : 24 h de trames toutes les 5 s ≈ 17 000 points.</summary>
    private const int MaxTracePoints = 20_000;

    /// <summary>
    /// Renseigne <see cref="Tour.ActualDistanceKm"/> et <see cref="Tour.ActualFuelLiters"/> à
    /// partir des trames valides du boîtier entre le démarrage et l'arrivée à destination
    /// (à défaut la fin de tournée), fenêtre bornée à 24 h. Ne touche à rien sans boîtier,
    /// sans démarrage, avec moins de deux trames, ou quand le véhicule appartient désormais
    /// à une autre société (sa trace n'est plus celle de la tournée).
    /// </summary>
    public static async Task CalculateActualMetricsAsync(Tour tour, IGisDbContext context, CancellationToken ct)
    {
        if (!tour.ActualStartTime.HasValue || tour.Vehicle?.GpsDeviceId == null) return;
        if (!TourPlanning.VehicleBelongsToTourCompany(tour)) return;

        var deviceId = tour.Vehicle.GpsDeviceId.Value;
        var startTime = tour.ActualStartTime.Value;
        // Fin = l'arrivée réelle, pas « maintenant » — et fenêtre bornée comme la tranche
        // de trace du moniteur : une tournée restée en cours plusieurs jours matérialiserait
        // sinon tout l'historique du véhicule en une requête.
        var endTime = tour.Waypoints.FirstOrDefault(w => w.Type == "destination")?.ActualArrivalTime
            ?? tour.ActualEndTime ?? DateTime.UtcNow;
        if (startTime < endTime.AddHours(-24)) startTime = endTime.AddHours(-24);

        var positions = await context.GpsPositions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.DeviceId == deviceId
                && p.RecordedAt >= startTime
                && p.RecordedAt <= endTime
                && p.IsValid)
            .OrderBy(p => p.RecordedAt)
            .Take(MaxTracePoints)
            .Select(p => new { p.Latitude, p.Longitude })
            .ToListAsync(ct);

        if (positions.Count < 2) return;

        double totalDistanceKm = 0;
        for (var i = 1; i < positions.Count; i++)
        {
            totalDistanceKm += GeoMath.HaversineDistance(
                positions[i - 1].Latitude, positions[i - 1].Longitude,
                positions[i].Latitude, positions[i].Longitude) / 1000.0;
        }

        tour.ActualDistanceKm = (decimal)Math.Round(totalDistanceKm, 2);
        tour.ActualFuelLiters = Math.Round(tour.ActualDistanceKm.Value * EstimatedConsumptionPer100Km / 100, 2);
    }
}
