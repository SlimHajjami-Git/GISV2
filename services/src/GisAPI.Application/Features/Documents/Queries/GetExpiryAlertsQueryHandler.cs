using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Documents.Queries;

public class GetExpiryAlertsQueryHandler : IRequestHandler<GetExpiryAlertsQuery, List<VehicleExpiryDto>>
{
    private readonly IGisDbContext _context;

    public GetExpiryAlertsQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<List<VehicleExpiryDto>> Handle(GetExpiryAlertsQuery request, CancellationToken cancellationToken)
    {
        var vehicles = await _context.Vehicles
            .AsNoTracking()
            .ToListAsync(cancellationToken);

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
