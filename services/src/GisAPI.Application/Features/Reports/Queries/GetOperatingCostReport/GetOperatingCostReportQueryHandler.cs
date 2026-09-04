using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Domain.Interfaces;
using MediatR;

namespace GisAPI.Application.Features.Reports.Queries.GetOperatingCostReport;

/// <summary>
/// R1 — coût d'exploitation par véhicule : carburant + entretiens + réparations +
/// autres dépenses, rapportés au kilométrage (compteur des pleins sans boîtier,
/// trajets GPS avec boîtier). Voir <see cref="OperatingCostAggregator"/>.
/// </summary>
public class GetOperatingCostReportQueryHandler : IRequestHandler<GetOperatingCostReportQuery, OperatingCostReportDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetOperatingCostReportQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<OperatingCostReportDto> Handle(GetOperatingCostReportQuery request, CancellationToken ct)
    {
        var startUtc = OperatingCostAggregator.StartUtc(request.StartDate);
        var endExclusiveUtc = OperatingCostAggregator.EndExclusiveUtc(request.EndDate);

        var data = await OperatingCostAggregator.LoadAsync(
            _context, _tenantService, startUtc, endExclusiveUtc, request.VehicleId, request.DepartmentId, ct);

        return OperatingCostReportBuilder.Build(data, startUtc, endExclusiveUtc.AddDays(-1));
    }
}
