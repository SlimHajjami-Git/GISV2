using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Documents.Queries;

public class GetVehicleExpiriesQueryHandler : IRequestHandler<GetVehicleExpiriesQuery, List<VehicleExpiryDto>>
{
    private readonly IGisDbContext _context;

    public GetVehicleExpiriesQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<List<VehicleExpiryDto>> Handle(GetVehicleExpiriesQuery request, CancellationToken cancellationToken)
    {
        var vehicle = await _context.Vehicles
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId, cancellationToken);

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
