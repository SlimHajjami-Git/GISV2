using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Documents.Queries;

public class GetExpiryStatsQueryHandler : IRequestHandler<GetExpiryStatsQuery, ExpiryStatsDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetExpiryStatsQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<ExpiryStatsDto> Handle(GetExpiryStatsQuery request, CancellationToken cancellationToken)
    {
        // Scope by caller's company even for system admins (see GetExpiriesQueryHandler).
        var companyId = _tenantService.CompanyId ?? 0;

        // + portée par utilisateur : sans cela les compteurs (expirés, à venir)
        // annonçaient des totaux calculés sur tout le parc à un employé qui
        // n'avait accès qu'à deux véhicules.
        var accessibleIds = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, cancellationToken);

        var vehicleQuery = _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId);

        if (accessibleIds is not null)
            vehicleQuery = vehicleQuery.Where(v => accessibleIds.Contains(v.Id));

        var vehicles = await vehicleQuery.ToListAsync(cancellationToken);

        var today = DateTime.UtcNow.Date;
        var expiredCount = 0;
        var expiringSoonCount = 0;
        var okCount = 0;
        var totalCount = 0;

        foreach (var vehicle in vehicles)
        {
            CountExpiry(vehicle.InsuranceExpiry, today, ref expiredCount, ref expiringSoonCount, ref okCount, ref totalCount);
            CountExpiry(vehicle.TechnicalInspectionExpiry, today, ref expiredCount, ref expiringSoonCount, ref okCount, ref totalCount);
            CountExpiry(vehicle.TaxExpiry, today, ref expiredCount, ref expiringSoonCount, ref okCount, ref totalCount);
            CountExpiry(vehicle.RegistrationExpiry, today, ref expiredCount, ref expiringSoonCount, ref okCount, ref totalCount);
            CountExpiry(vehicle.TransportPermitExpiry, today, ref expiredCount, ref expiringSoonCount, ref okCount, ref totalCount);
        }

        // Include driver permit expiries in stats (from the standalone drivers table)
        var driverQuery = _context.Drivers
            .AsNoTracking()
            .Where(d => d.CompanyId == companyId && d.PermitExpiry != null);

        if (accessibleIds is not null)
            driverQuery = driverQuery.Where(d => d.AssignedVehicleId != null
                                              && accessibleIds.Contains(d.AssignedVehicleId.Value));

        var driverPermitExpiries = await driverQuery
            .Select(d => d.PermitExpiry)
            .ToListAsync(cancellationToken);

        foreach (var permitExpiry in driverPermitExpiries)
        {
            CountExpiry(permitExpiry, today, ref expiredCount, ref expiringSoonCount, ref okCount, ref totalCount);
        }

        return new ExpiryStatsDto(expiredCount, expiringSoonCount, okCount, totalCount);
    }

    private void CountExpiry(DateTime? expiryDate, DateTime today, 
        ref int expired, ref int expiringSoon, ref int ok, ref int total)
    {
        if (!expiryDate.HasValue) return;

        total++;
        var daysUntil = (expiryDate.Value.Date - today).TotalDays;

        if (daysUntil < 0)
            expired++;
        else if (daysUntil <= 30)
            expiringSoon++;
        else
            ok++;
    }
}



