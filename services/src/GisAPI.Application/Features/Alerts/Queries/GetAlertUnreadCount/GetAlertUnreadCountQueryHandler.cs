using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Alerts.Queries.GetAlertUnreadCount;

public class GetAlertUnreadCountQueryHandler : IRequestHandler<GetAlertUnreadCountQuery, int>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public GetAlertUnreadCountQueryHandler(IGisDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<int> Handle(GetAlertUnreadCountQuery request, CancellationToken ct)
    {
        // Même portée que GetAlertsQuery : sans ce filtre le badge annoncerait des
        // alertes que l'utilisateur ne peut pas ouvrir (compteur du parc entier
        // face à une liste restreinte), et le nombre lui-même renseignerait sur
        // l'activité de véhicules auxquels il n'a pas accès.
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenant, ct);

        var vehicleQuery = _context.Vehicles
            .Where(v => v.CompanyId == request.CompanyId);

        if (scope is not null)
            vehicleQuery = vehicleQuery.Where(v => scope.Contains(v.Id));

        var vehicleIds = await vehicleQuery
            .Select(v => v.Id)
            .ToListAsync(ct);

        return await _context.GpsAlerts
            .Where(a => a.VehicleId.HasValue &&
                        vehicleIds.Contains(a.VehicleId.Value) &&
                        !a.Resolved)
            .CountAsync(ct);
    }
}
