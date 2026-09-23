using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Alerts.Commands.ResolveAllAlerts;

public class ResolveAllAlertsCommandHandler : IRequestHandler<ResolveAllAlertsCommand, int>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public ResolveAllAlertsCommandHandler(IGisDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<int> Handle(ResolveAllAlertsCommand request, CancellationToken ct)
    {
        // La LECTURE des alertes est cloisonnée (GetAlertsQueryHandler), pas l'écriture :
        // « tout marquer comme résolu » acquittait les alertes de TOUS les véhicules de
        // la société. Chez un loueur, un locataire effaçait donc la cloche de ses voisins.
        // Même règle que la lecture, TROIS états : null = administrateur, aucun filtre ;
        // liste non vide = ses véhicules ; liste VIDE = il ne résout RIEN.
        var portee = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenant, ct);

        var vehicleIdsQuery = _context.Vehicles
            .Where(v => v.CompanyId == request.CompanyId);

        if (portee is not null)
        {
            List<int> ids = portee;
            vehicleIdsQuery = vehicleIdsQuery.Where(v => ids.Contains(v.Id));
        }

        var vehicleIds = await vehicleIdsQuery
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
