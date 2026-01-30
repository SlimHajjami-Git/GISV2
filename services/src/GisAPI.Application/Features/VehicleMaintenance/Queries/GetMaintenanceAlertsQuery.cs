using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.VehicleMaintenance.Queries;

public record GetMaintenanceAlertsQuery() : IQuery<List<MaintenanceItemDto>>;

public record GetMaintenanceStatsQuery() : IQuery<MaintenanceStatsDto>;



