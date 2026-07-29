using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Documents.Queries;

public class GetVehicleExpiriesQueryHandler : IRequestHandler<GetVehicleExpiriesQuery, List<VehicleExpiryDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetVehicleExpiriesQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<VehicleExpiryDto>> Handle(GetVehicleExpiriesQuery request, CancellationToken cancellationToken)
    {
        // Scope by caller's company even for system admins.
        var companyId = _tenantService.CompanyId ?? 0;

        // Accès direct par identifiant : c'est le cas le plus exploitable de la
        // famille (il suffit d'incrémenter l'id dans l'URL). Un utilisateur
        // restreint doit se voir refuser un véhicule hors de sa portée, même
        // s'il appartient à sa société.
        var accessibleIds = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, cancellationToken);
        if (accessibleIds is not null && !accessibleIds.Contains(request.VehicleId))
            return new List<VehicleExpiryDto>();

        var vehicle = await _context.Vehicles
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.CompanyId == companyId, cancellationToken);

        if (vehicle == null)
            return new List<VehicleExpiryDto>();

        var today = DateTime.UtcNow.Date;
        var expiries = new List<VehicleExpiryDto>();

        // Get last renewal info from VehicleCosts
        var costs = await _context.VehicleCosts
            .Where(c => c.VehicleId == request.VehicleId &&
                        (c.Type == "insurance" || c.Type == "tax" || c.Type == "technical_inspection" ||
                         c.Type == "registration" || c.Type == "transport_permit"))
            .OrderByDescending(c => c.Date)
            .ToListAsync(cancellationToken);

        AddExpiry(expiries, vehicle, "insurance", vehicle.InsuranceExpiry, today, costs);
        AddExpiry(expiries, vehicle, "technical_inspection", vehicle.TechnicalInspectionExpiry, today, costs);
        AddExpiry(expiries, vehicle, "tax", vehicle.TaxExpiry, today, costs);
        AddExpiry(expiries, vehicle, "registration", vehicle.RegistrationExpiry, today, costs);
        AddExpiry(expiries, vehicle, "transport_permit", vehicle.TransportPermitExpiry, today, costs);

        return expiries.OrderBy(e => e.DaysUntilExpiry).ToList();
    }

    private void AddExpiry(List<VehicleExpiryDto> expiries, Domain.Entities.Vehicle vehicle,
        string type, DateTime? expiryDate, DateTime today, List<Domain.Entities.VehicleCost> costs)
    {
        var daysUntil = expiryDate.HasValue
            ? (int)(expiryDate.Value.Date - today).TotalDays
            : int.MaxValue;

        var status = expiryDate switch
        {
            null => "unknown",
            _ when expiryDate.Value.Date < today => "expired",
            _ when daysUntil <= 30 => "expiring_soon",
            _ => "ok"
        };

        var lastRenewal = costs.FirstOrDefault(c => c.Type == type);

        expiries.Add(new VehicleExpiryDto(
            vehicle.Id,
            vehicle.Name,
            vehicle.Plate,
            type,
            expiryDate,
            status,
            daysUntil == int.MaxValue ? -1 : daysUntil,
            lastRenewal?.Date,
            lastRenewal?.Amount,
            lastRenewal?.DocumentNumber
        ));
    }
}



