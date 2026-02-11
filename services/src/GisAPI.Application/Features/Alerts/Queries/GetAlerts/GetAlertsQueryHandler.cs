using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Alerts.Queries.GetAlerts;

public class GetAlertsQueryHandler : IRequestHandler<GetAlertsQuery, List<AlertDto>>
{
    private readonly IGisDbContext _context;

    public GetAlertsQueryHandler(IGisDbContext context) => _context = context;

    public async Task<List<AlertDto>> Handle(GetAlertsQuery request, CancellationToken ct)
    {
        var vehicleIds = await _context.Vehicles
            .Where(v => v.CompanyId == request.CompanyId)
            .Select(v => v.Id)
            .ToListAsync(ct);

        var query = _context.GpsAlerts
            .Where(a => a.VehicleId.HasValue && vehicleIds.Contains(a.VehicleId.Value))
            .Include(a => a.Vehicle)
            .AsQueryable();

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
