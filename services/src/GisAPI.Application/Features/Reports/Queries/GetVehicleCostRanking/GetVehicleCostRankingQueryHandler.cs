using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Application.Features.Reports.Queries.GetOperatingCostReport;
using GisAPI.Domain.Interfaces;
using MediatR;

namespace GisAPI.Application.Features.Reports.Queries.GetVehicleCostRanking;

public class GetVehicleCostRankingQueryHandler : IRequestHandler<GetVehicleCostRankingQuery, OperatingCostReportDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetVehicleCostRankingQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<OperatingCostReportDto> Handle(GetVehicleCostRankingQuery request, CancellationToken ct)
    {
        var startUtc = OperatingCostAggregator.StartUtc(request.StartDate);
        var endExclusiveUtc = OperatingCostAggregator.EndExclusiveUtc(request.EndDate);

        var data = await OperatingCostAggregator.LoadAsync(
            _context, _tenantService, startUtc, endExclusiveUtc, vehicleId: null, request.DepartmentId, ct);

        var top = request.Top <= 0 ? 10 : request.Top;
        return OperatingCostReportBuilder.Build(data, startUtc, endExclusiveUtc.AddDays(-1), top);
    }
}
