using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Services;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Documents.Queries;

public class GetExpiryAlertsQueryHandler : IRequestHandler<GetExpiryAlertsQuery, List<VehicleExpiryDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetExpiryAlertsQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<VehicleExpiryDto>> Handle(GetExpiryAlertsQuery request, CancellationToken cancellationToken)
    {
        // Scope by caller's company even for system admins.
        var companyId = _tenantService.CompanyId ?? 0;
        var userId = _tenantService.UserId ?? 0;
        var isAdmin = _tenantService.UserRoles.Any(r => r == "company_admin" || r == "admin" || r == "super_admin" || r == "system_admin");
        var visibleVehicleIds = await VehicleVisibility.GetVisibleVehicleIdsAsync(_context, userId, isAdmin, cancellationToken);

        var vehiclesQuery = _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId);
        if (visibleVehicleIds != null)
            vehiclesQuery = vehiclesQuery.Where(v => visibleVehicleIds.Contains(v.Id));
        var vehicles = await vehiclesQuery.ToListAsync(cancellationToken);

        var today = DateTime.UtcNow.Date;
        var threshold = today.AddDays(request.DaysThreshold);
        var alerts = new List<VehicleExpiryDto>();

        foreach (var vehicle in vehicles)
        {
            CheckAndAddAlert(alerts, vehicle, "insurance", vehicle.InsuranceExpiry, today, threshold);
            CheckAndAddAlert(alerts, vehicle, "technical_inspection", vehicle.TechnicalInspectionExpiry, today, threshold);
            CheckAndAddAlert(alerts, vehicle, "tax", vehicle.TaxExpiry, today, threshold);
            CheckAndAddAlert(alerts, vehicle, "registration", vehicle.RegistrationExpiry, today, threshold);
            CheckAndAddAlert(alerts, vehicle, "transport_permit", vehicle.TransportPermitExpiry, today, threshold);
        }

        return alerts
            .OrderBy(e => e.DaysUntilExpiry)
            .ToList();
    }

    private void CheckAndAddAlert(List<VehicleExpiryDto> alerts, Domain.Entities.Vehicle vehicle,
        string type, DateTime? expiryDate, DateTime today, DateTime threshold)
    {
        if (!expiryDate.HasValue) return;

        // Only include expired or expiring soon
        if (expiryDate.Value.Date > threshold) return;

        var daysUntil = (int)(expiryDate.Value.Date - today).TotalDays;
        var status = daysUntil < 0 ? "expired" : "expiring_soon";

        alerts.Add(new VehicleExpiryDto(
            vehicle.Id,
            vehicle.Name,
            vehicle.Plate,
            type,
            expiryDate,
            status,
            daysUntil,
            null,
            null,
            null
        ));
    }
}



