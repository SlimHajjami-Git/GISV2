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

public record GetRepairStatsQuery(
    int? VehicleId = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null
) : IRequest<RepairStatsDto>;

public record RepairsListResult(
    List<RepairDto> Items,
    int TotalCount,
    int Page,
    int PageSize
);
