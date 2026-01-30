using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FleetManagement.SpeedLimits.Queries;

public class GetSpeedAlertsQueryHandler : IRequestHandler<GetSpeedAlertsQuery, SpeedAlertsResult>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetSpeedAlertsQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<SpeedAlertsResult> Handle(GetSpeedAlertsQuery request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        var query = _context.SpeedLimitAlerts
            .Where(a => a.CompanyId == companyId);

        if (request.VehicleId.HasValue)
            query = query.Where(a => a.VehicleId == request.VehicleId.Value);

        if (request.FromDate.HasValue)
            query = query.Where(a => a.RecordedAt >= request.FromDate.Value);

        if (request.ToDate.HasValue)
            query = query.Where(a => a.RecordedAt <= request.ToDate.Value);

        if (request.IsAcknowledged.HasValue)
            query = query.Where(a => a.IsAcknowledged == request.IsAcknowledged.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var alerts = await query
            .OrderByDescending(a => a.RecordedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(a => new SpeedAlertDto(
                a.Id,
                a.VehicleId,
                a.Vehicle != null ? a.Vehicle.Name : "Unknown",
                a.Vehicle != null ? a.Vehicle.Plate : null,
                a.SpeedLimit,
                a.ActualSpeed,
                a.ActualSpeed - a.SpeedLimit,
                a.Latitude,
                a.Longitude,
                a.Address,
                a.RecordedAt,
                a.IsAcknowledged,
                a.AcknowledgedAt,
                a.AcknowledgedBy != null ? a.AcknowledgedBy.FullName : null
            ))
            .ToListAsync(cancellationToken);

        return new SpeedAlertsResult(alerts, totalCount, request.Page, request.PageSize);
    }
}



