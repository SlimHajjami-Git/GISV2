using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FuelExpenses.Queries;

/// <summary>
/// Explication IA d'une tranche de consommation : reconstitue le profil de
/// conduite de la tranche depuis les trames GPS (vitesses, ralenti, arrêts,
/// dénivelé, régime moteur), y joint le tonnage déclaré et le contexte du
/// rapport, et demande à Groq les causes probables. Le front envoie les
/// chiffres de la tranche tels qu'affichés — le serveur ne s'en sert que
/// comme contexte textuel, toutes les données GPS sont relues sous scope.
/// </summary>
public record ExplainConsumptionSegmentQuery(
    int VehicleId,
    DateTime StartTime,
    DateTime EndTime,
    decimal DistanceKm,
    decimal FuelLiters,
    decimal LPer100Km,
    decimal? TonnageT,
    bool IsReliable,
    string? ExclusionReason,
    int SegmentKm,
    decimal? PeriodAvgLPer100Km,
    decimal? PeriodMinLPer100Km,
    decimal? PeriodMaxLPer100Km,
    // Renseignés quand l'explication porte sur un intervalle plein-à-plein du
    // rapport Réel vs GPS : l'IA doit alors adresser l'écart facture/mesure.
    decimal? RealLiters = null,
    decimal? RealLPer100Km = null) : IRequest<ExplainSegmentResultDto>;

public record ExplainSegmentResultDto(string Explanation, bool FromCache);

public class ExplainConsumptionSegmentQueryHandler
    : IRequestHandler<ExplainConsumptionSegmentQuery, ExplainSegmentResultDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ILlmService _llm;

    // Cache mémoire process-local : le même clic répété ne doit pas re-payer
    // un appel LLM. TTL 15 min, purge opportuniste.
    private static readonly ConcurrentDictionary<string, (DateTime At, string Text)> Cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);

    public ExplainConsumptionSegmentQueryHandler(
        IGisDbContext context,
        ICurrentTenantService tenantService,
        ILlmService llm)
    {
        _context = context;
        _tenantService = tenantService;
        _llm = llm;
    }

    public async Task<ExplainSegmentResultDto> Handle(ExplainConsumptionSegmentQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        // Même portée que le reste du rapport : pas d'analyse IA d'un véhicule
        // hors périmètre via un simple appel direct à l'API.
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, ct);
        var vehicleQuery = _context.Vehicles
            .Include(v => v.GpsDevice)
            .Where(v => v.Id == request.VehicleId && v.CompanyId == companyId);
        if (scope is not null)
            vehicleQuery = vehicleQuery.Where(v => scope.Contains(v.Id));

        var vehicle = await vehicleQuery.FirstOrDefaultAsync(ct);
        if (vehicle?.GpsDeviceId == null)
            return new ExplainSegmentResultDto("Analyse indisponible pour ce véhicule.", false);

        var cacheKey = $"{vehicle.Id}|{request.StartTime.Ticks}|{request.EndTime.Ticks}|{request.TonnageT}|{request.LPer100Km}|{request.RealLPer100Km}";
        if (Cache.TryGetValue(cacheKey, out var hit) && DateTime.UtcNow - hit.At < CacheTtl)
            return new ExplainSegmentResultDto(hit.Text, true);

        // ── Profil de conduite de la tranche depuis les trames brutes ──
        var frames = await _context.GpsPositions
            .AsNoTracking()
            .Where(p => p.DeviceId == vehicle.GpsDeviceId.Value
                        && p.RecordedAt >= request.StartTime && p.RecordedAt <= request.EndTime)
            .OrderBy(p => p.RecordedAt)
            .Select(p => new { p.SpeedKph, p.IgnitionOn, p.AltitudeM, p.Rpm })
            .ToListAsync(ct);

        var moving = frames.Where(f => (f.SpeedKph ?? 0) >= 5).ToList();
        var maxSpeed = frames.Count > 0 ? frames.Max(f => f.SpeedKph ?? 0) : 0;
        var avgMoving = moving.Count > 0 ? moving.Average(f => f.SpeedKph ?? 0) : 0;
        var pctFast = moving.Count > 0
            ? 100.0 * moving.Count(f => (f.SpeedKph ?? 0) > 90) / moving.Count : 0;
        // Contact allumé = ~1 trame/min sur nos boîtiers → le nombre de trames
        // à l'arrêt moteur tournant approxime les minutes de ralenti.
        var idleMinutes = frames.Count(f => f.IgnitionOn == true && (f.SpeedKph ?? 0) < 2);

        double climbM = 0;
        double? prevAlt = null;
        foreach (var f in frames)
        {
            if (f.AltitudeM.HasValue && f.AltitudeM.Value != 0)
            {
                if (prevAlt.HasValue)
                {
                    var delta = f.AltitudeM.Value - prevAlt.Value;
                    if (delta > 0 && delta < 100) climbM += delta; // garde anti-bruit GPS
                }
                prevAlt = f.AltitudeM.Value;
            }
        }

        var rpms = frames.Where(f => f.Rpm is > 0).Select(f => (int)f.Rpm!.Value).ToList();

        var stops = await _context.VehicleStops
            .AsNoTracking()
            .Where(s => s.VehicleId == vehicle.Id
                        && s.StartTime >= request.StartTime && s.StartTime < request.EndTime)
            .Select(s => s.DurationSeconds)
            .ToListAsync(ct);

        // ── Dossier envoyé au modèle ──
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        var sb = new StringBuilder();
        sb.AppendLine($"Véhicule : {vehicle.Name} ({vehicle.Type}, {vehicle.FuelType}, réservoir {vehicle.FuelTankCapacity} L)");
        sb.AppendLine($"Tranche analysée : du {request.StartTime:dd/MM HH:mm} au {request.EndTime:dd/MM HH:mm} — {request.DistanceKm.ToString("0.#", fr)} km parcourus");
        sb.AppendLine($"Consommation de la tranche : {request.LPer100Km.ToString("0.#", fr)} L/100 km ({request.FuelLiters.ToString("0.#", fr)} L)");
        if (request.PeriodAvgLPer100Km.HasValue)
            sb.AppendLine($"Références du véhicule sur la période : moyenne {request.PeriodAvgLPer100Km.Value.ToString("0.#", fr)}, min {request.PeriodMinLPer100Km?.ToString("0.#", fr)}, max {request.PeriodMaxLPer100Km?.ToString("0.#", fr)} L/100 km");
        sb.AppendLine(request.TonnageT.HasValue
            ? $"Chargement déclaré : {request.TonnageT.Value.ToString("0.#", fr)} tonnes"
            : "Chargement déclaré : aucun (inconnu)");
        if (request.RealLiters.HasValue && request.RealLPer100Km.HasValue)
            sb.AppendLine($"Carburant réellement FACTURÉ sur ce même intervalle (méthode plein à plein) : {request.RealLiters.Value.ToString("0.#", fr)} L, soit {request.RealLPer100Km.Value.ToString("0.#", fr)} L/100 km");
        if (frames.Count >= 5)
        {
            sb.AppendLine($"Profil de conduite mesuré : vitesse moyenne en roulage {avgMoving:0} km/h, pointe {maxSpeed:0} km/h, {pctFast:0} % du roulage au-dessus de 90 km/h");
            sb.AppendLine($"Ralenti moteur (à l'arrêt, moteur tournant) : ≈ {idleMinutes} min");
            sb.AppendLine($"Arrêts : {stops.Count} (total {TimeSpan.FromSeconds(stops.Sum()).TotalMinutes:0} min)");
            if (climbM > 30) sb.AppendLine($"Dénivelé positif cumulé : ≈ {climbM:0} m");
            if (rpms.Count > 0) sb.AppendLine($"Régime moteur : moyenne {rpms.Average():0} tr/min, max {rpms.Max()} tr/min");
        }
        else
        {
            sb.AppendLine("Profil de conduite : données GPS insuffisantes sur la tranche.");
        }
        if (!request.IsReliable)
            sb.AppendLine("Attention : cette tranche est écartée des statistiques car ses données de mesure de carburant ne sont pas exploitables.");

        var system =
            "Tu es un analyste expert en gestion de flotte. On te donne le dossier d'une tranche de " +
            "consommation d'un véhicule. Réponds en français, pour un gestionnaire de flotte non technicien. " +
            "Structure imposée : « Causes probables : » suivi de 2 à 3 puces courtes classées de la plus à la " +
            "moins vraisemblable, PUIS « Recommandation : » une seule phrase actionnable. " +
            "Appuie-toi UNIQUEMENT sur les chiffres fournis — n'invente aucun fait. Compare toujours la tranche " +
            "aux références du véhicule plutôt qu'à des normes générales. Si des litres FACTURÉS sont fournis pour " +
            "l'intervalle, commence par dire si mesure et facture concordent (écart < 10 % = normal), et en cas " +
            "d'écart notable propose les causes possibles : niveau du réservoir différent aux bornes, plein non " +
            "complet, données de mesure partielles, ou carburant payé non versé dans ce réservoir. " +
            "Si le chargement est inconnu, mentionne " +
            "que le déclarer affinerait l'analyse. Si les données de mesure sont signalées non exploitables, " +
            "explique calmement que la tranche ne doit pas être interprétée et pourquoi elle est écartée des " +
            "statistiques — sans jamais parler de capteur ou GPS défaillant. Maximum 130 mots. Pas de titre, pas de gras.";

        string explanation;
        try
        {
            var response = await _llm.ChatAsync(system, new List<LlmMessage> { new("user", sb.ToString()) }, 500, ct);
            explanation = response.Content?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(explanation))
                return new ExplainSegmentResultDto("Analyse IA momentanément indisponible.", false);
        }
        catch
        {
            // Clé absente / quota / réseau : le rapport reste utilisable sans IA.
            return new ExplainSegmentResultDto("Analyse IA momentanément indisponible.", false);
        }

        // Purge opportuniste puis insertion.
        foreach (var kv in Cache.Where(kv => DateTime.UtcNow - kv.Value.At > CacheTtl).ToList())
            Cache.TryRemove(kv.Key, out _);
        Cache[cacheKey] = (DateTime.UtcNow, explanation);

        return new ExplainSegmentResultDto(explanation, false);
    }
}
