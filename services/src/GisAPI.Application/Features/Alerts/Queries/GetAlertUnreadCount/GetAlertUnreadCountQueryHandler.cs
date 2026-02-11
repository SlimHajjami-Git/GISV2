using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Alerts.Queries.GetAlertUnreadCount;

public class GetAlertUnreadCountQueryHandler : IRequestHandler<GetAlertUnreadCountQuery, int>
{
    private readonly IGisDbContext _context;

    public GetAlertUnreadCountQueryHandler(IGisDbContext context) => _context = context;

    public async Task<int> Handle(GetAlertUnreadCountQuery request, CancellationToken ct)
    {
        var vehicleIds = await _context.Vehicles
            .Where(v => v.CompanyId == request.CompanyId)
            .Select(v => v.Id)
            .ToListAsync(ct);

        return await _context.GpsAlerts
            .Where(a => a.VehicleId.HasValue &&
                        vehicleIds.Contains(a.VehicleId.Value) &&
                        !a.Resolved)
            .CountAsync(ct);
    }
}
