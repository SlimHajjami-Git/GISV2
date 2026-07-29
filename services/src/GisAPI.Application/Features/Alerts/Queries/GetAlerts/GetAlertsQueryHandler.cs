using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Alerts.Queries.GetAlerts;

public class GetAlertsQueryHandler : IRequestHandler<GetAlertsQuery, List<AlertDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public GetAlertsQueryHandler(IGisDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<List<AlertDto>> Handle(GetAlertsQuery request, CancellationToken ct)
    {
        // Fuite constatée : le filtre ne portait que sur la société, si bien qu'un
        // employé restreint à quelques véhicules recevait les alertes de TOUT le parc
        // (position GPS, nom et plaque inclus). On restreint donc aux véhicules qui
        // lui sont réellement affectés ; un administrateur (scope == null) voit tout.
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenant, ct);

        var vehicleIds = await _context.Vehicles
            .Where(v => v.CompanyId == request.CompanyId)
            .Select(v => v.Id)
            .ToListAsync(ct);

        var query = _context.GpsAlerts
            .Where(a => a.VehicleId.HasValue && vehicleIds.Contains(a.VehicleId.Value))
            .Include(a => a.Vehicle)
            .AsQueryable();

        if (scope is not null)
            query = query.Where(a => a.VehicleId.HasValue && scope.Contains(a.VehicleId.Value));

        if (request.Resolved.HasValue)
            query = query.Where(a => a.Resolved == request.Resolved.Value);

        if (!string.IsNullOrEmpty(request.Type))
            query = query.Where(a => a.Type == request.Type);

        return await query
            .OrderByDescending(a => a.Timestamp)
            .Take(request.Limit)
            .Select(a => new AlertDto
            {
                Id = a.Id,
                DeviceId = a.DeviceId,
                VehicleId = a.VehicleId,
                VehicleName = a.Vehicle != null ? a.Vehicle.Name : null,
                Plate = a.Vehicle != null ? a.Vehicle.Plate : null,
                Type = a.Type,
                Severity = a.Severity,
                Message = a.Message,
                Resolved = a.Resolved,
                ResolvedAt = a.ResolvedAt,
                ResolvedByUserId = a.ResolvedByUserId,
                Latitude = a.Latitude,
                Longitude = a.Longitude,
                Timestamp = a.Timestamp,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync(ct);
    }
}
