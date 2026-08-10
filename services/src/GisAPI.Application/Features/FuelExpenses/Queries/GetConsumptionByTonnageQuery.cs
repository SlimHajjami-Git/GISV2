using MediatR;

namespace GisAPI.Application.Features.FuelExpenses.Queries;

/// <summary>
/// Comparaison de consommation par tonnage déclaré : regroupe les segments de
/// X km fiables par tonnage et donne moyenne/min/max par groupe, plus l'écart
/// relatif vs le groupe le plus léger.
/// </summary>
public record GetConsumptionByTonnageQuery(
    int VehicleId,
    DateTime? StartDate,
    DateTime? EndDate,
    int SegmentKm = 100) : IRequest<ConsumptionByTonnageReportDto>;

public class GetConsumptionByTonnageQueryHandler
    : IRequestHandler<GetConsumptionByTonnageQuery, ConsumptionByTonnageReportDto>
{
    private readonly IMediator _mediator;

    public GetConsumptionByTonnageQueryHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<ConsumptionByTonnageReportDto> Handle(GetConsumptionByTonnageQuery request, CancellationToken ct)
    {
        // Réutilise le calcul de segments (source de vérité unique), puis agrège.
        var segments = await _mediator.Send(new GetConsumptionSegmentsQuery(
            request.VehicleId, request.StartDate, request.EndDate, request.SegmentKm), ct);

        var groups = segments.Segments
            .Where(s => s.IsReliable && s.DistanceKm > 0)
            .GroupBy(s => s.TonnageT)
            .Select(g =>
            {
                var totalKm = g.Sum(s => s.DistanceKm);
                return new TonnageGroupDto(
                    TonnageT: g.Key,
                    SegmentCount: g.Count(),
                    TotalKm: Math.Round(totalKm, 1),
                    AvgLPer100Km: Math.Round(g.Sum(s => s.FuelLiters) / totalKm * 100m, 2),
                    MinLPer100Km: g.Min(s => s.LPer100Km),
                    MaxLPer100Km: g.Max(s => s.LPer100Km),
                    DeltaVsLightestPercent: null);
            })
            // Tonnages renseignés d'abord (croissant), « non renseigné » en dernier.
            .OrderBy(g => g.TonnageT == null)
            .ThenBy(g => g.TonnageT)
            .ToList();

        // Écart relatif vs le groupe RENSEIGNÉ le plus léger.
        var baseline = groups.FirstOrDefault(g => g.TonnageT != null);
        if (baseline != null && baseline.AvgLPer100Km > 0)
        {
            groups = groups.Select(g => g == baseline ? g : g with
            {
                DeltaVsLightestPercent = Math.Round(
                    (g.AvgLPer100Km - baseline.AvgLPer100Km) / baseline.AvgLPer100Km * 100m, 1)
            }).ToList();
        }

        return new ConsumptionByTonnageReportDto(
            segments.VehicleId, segments.VehicleName, segments.SegmentKm, groups);
    }
}
