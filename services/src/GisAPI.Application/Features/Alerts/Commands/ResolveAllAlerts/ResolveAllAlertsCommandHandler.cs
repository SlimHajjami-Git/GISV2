using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Alerts.Commands.ResolveAllAlerts;

public class ResolveAllAlertsCommandHandler : IRequestHandler<ResolveAllAlertsCommand, int>
{
    private readonly IGisDbContext _context;

    public ResolveAllAlertsCommandHandler(IGisDbContext context) => _context = context;

    public async Task<int> Handle(ResolveAllAlertsCommand request, CancellationToken ct)
    {
        var vehicleIds = await _context.Vehicles
            .Where(v => v.CompanyId == request.CompanyId)
            .Select(v => v.Id)
            .ToListAsync(ct);

        var unresolvedAlerts = await _context.GpsAlerts
            .Where(a => a.VehicleId.HasValue &&
                        vehicleIds.Contains(a.VehicleId.Value) &&
                        !a.Resolved)
            .ToListAsync(ct);

        foreach (var alert in unresolvedAlerts)
        {
            alert.Resolved = true;
            alert.ResolvedAt = DateTime.UtcNow;
            alert.ResolvedByUserId = request.UserId;
        }

        await _context.SaveChangesAsync(ct);
        return unresolvedAlerts.Count;
    }
}
