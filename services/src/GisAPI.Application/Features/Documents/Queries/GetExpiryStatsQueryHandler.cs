using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Documents.Queries;

public class GetExpiryStatsQueryHandler : IRequestHandler<GetExpiryStatsQuery, ExpiryStatsDto>
{
    private readonly IGisDbContext _context;

    public GetExpiryStatsQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<ExpiryStatsDto> Handle(GetExpiryStatsQuery request, CancellationToken cancellationToken)
    {
        var vehicles = await _context.Vehicles
            .AsNoTracking()
            .ToListAsync(cancellationToken);

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



