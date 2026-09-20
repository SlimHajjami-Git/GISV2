using MediatR;

namespace GisAPI.Application.Features.Repairs.Queries;

public record GetRepairsQuery(
    int? VehicleId = null,
    string? Status = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    int Page = 1,
    int PageSize = 50
) : IRequest<RepairsListResult>;

public record GetRepairByIdQuery(int Id) : IRequest<RepairDto?>;

// Status en DERNIER et optionnel : les appelants existants passent encore
// (vehicleId, fromDate, toDate) par position et continuent de compiler.
// Sans lui, l'écran Réparations filtré sur un statut ne pouvait pas demander ses
// compteurs au serveur — il les calculait sur les lignes chargées, donc faux
// au-delà d'une page.
public record GetRepairStatsQuery(
    int? VehicleId = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    string? Status = null
) : IRequest<RepairStatsDto>;

public record RepairsListResult(
    List<RepairDto> Items,
    int TotalCount,
    int Page,
    int PageSize
);
