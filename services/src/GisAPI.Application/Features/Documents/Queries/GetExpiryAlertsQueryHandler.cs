using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
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

        // + portée par utilisateur : voir VehicleScope.
        var accessibleIds = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, cancellationToken);

        var vehicleQuery = _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId);

        if (accessibleIds is not null)
            vehicleQuery = vehicleQuery.Where(v => accessibleIds.Contains(v.Id));

        var vehicles = await vehicleQuery.ToListAsync(cancellationToken);

        var today = DateTime.UtcNow.Date;
        var alerts = new List<VehicleExpiryDto>();

        // Les cinq échéances viennent de la liste partagée avec le tableau de bord GPA.
        foreach (var vehicle in vehicles)
        {
            foreach (var (type, expiry) in VehicleDocumentExpiries.Of(vehicle))
                CheckAndAddAlert(alerts, vehicle, type, expiry, today, request.DaysThreshold);
        }

        // Permis de conducteur : même périmètre que la liste des échéances et
        // les compteurs, mêmes seuils que les documents véhicule. Sans eux, un
        // permis expiré était compté mais jamais signalé.
        var drivers = await DriverPermitExpiries.LoadAsync(_context, companyId, accessibleIds, cancellationToken);
        foreach (var driver in drivers)
        {
            var daysUntil = ExpiryCalendar.DaysUntil(driver.PermitExpiry!.Value, today);
            if (daysUntil > request.DaysThreshold) continue;

            alerts.Add(DriverPermitExpiries.ToDto(driver, vehicles, AlertStatus(daysUntil), daysUntil));
        }

        return alerts
            .OrderBy(e => e.DaysUntilExpiry)
            .ToList();
    }

    private static string AlertStatus(int daysUntil) =>
        daysUntil < 0 ? ExpiryCalendar.Expired : ExpiryCalendar.ExpiringSoon;

    private void CheckAndAddAlert(List<VehicleExpiryDto> alerts, Domain.Entities.Vehicle vehicle,
        string type, DateTime? expiryDate, DateTime today, int daysThreshold)
    {
        if (!expiryDate.HasValue) return;

        // Only include expired or expiring soon (jours calendaires, voir ExpiryCalendar)
        var daysUntil = ExpiryCalendar.DaysUntil(expiryDate.Value, today);
        if (daysUntil > daysThreshold) return;

        alerts.Add(new VehicleExpiryDto(
            vehicle.Id,
            vehicle.Name,
            vehicle.Plate,
            type,
            ExpiryCalendar.Day(expiryDate),
            AlertStatus(daysUntil),
            daysUntil,
            null,
            null,
            null
        ));
    }
}



