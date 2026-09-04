using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Reports.Queries.GetOperatingCostReport;

namespace GisAPI.Application.Features.Reports.Queries.GetVehicleCostRanking;

/// <summary>
/// R3 — Top N des véhicules les plus coûteux au km
/// (GET /api/reports/costs/ranking). Même DTO que R1 : les KPI portent sur tout
/// le parc, seule la liste <c>Vehicles</c> est tronquée.
/// </summary>
public record GetVehicleCostRankingQuery(
    DateTime StartDate,
    DateTime EndDate,
    int Top = 10,
    int? DepartmentId = null
) : IQuery<OperatingCostReportDto>;
