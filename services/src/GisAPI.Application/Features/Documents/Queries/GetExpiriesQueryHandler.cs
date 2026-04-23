using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Documents.Queries;

public class GetExpiriesQueryHandler : IRequestHandler<GetExpiriesQuery, PaginatedList<VehicleExpiryDto>>
{
    private readonly IGisDbContext _context;
    private static readonly string[] DocumentTypes = { "insurance", "technical_inspection", "tax", "registration", "transport_permit", "driver_permit" };

    public GetExpiriesQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<PaginatedList<VehicleExpiryDto>> Handle(GetExpiriesQuery request, CancellationToken cancellationToken)
    {
        var vehicles = await _context.Vehicles
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (request.VehicleId.HasValue)
        {
            vehicles = vehicles.Where(v => v.Id == request.VehicleId.Value).ToList();
        }

        var expiries = new List<VehicleExpiryDto>();
        var today = DateTime.UtcNow.Date;

        foreach (var vehicle in vehicles)
        {
            var vehicleExpiries = GetVehicleExpiries(vehicle, today);
            expiries.AddRange(vehicleExpiries);
        }

        // Add driver permit expiries. Source of truth is the `drivers` table
        // (decoupled from User since commit d1670d8) — permit fields on User
        // are legacy carry-overs that new code shouldn't rely on.
        if (!request.VehicleId.HasValue)
        {
            var drivers = await _context.Drivers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(d => d.User)
                .Where(d => d.PermitExpiry != null && d.Status == "active")
                .ToListAsync(cancellationToken);

            foreach (var driver in drivers)
            {
                var daysUntil = (int)(driver.PermitExpiry!.Value.Date - today).TotalDays;
                var status = driver.PermitExpiry.Value.Date < today ? "expired"
                    : daysUntil <= 30 ? "expiring_soon"
                    : "ok";

                // driver.AssignedVehicleId is the authoritative side — the
                // inverse leg on vehicles.assigned_driver_id is not always
                // kept in sync by the drivers popup, which only writes the
                // driver-side field.
                var assignedVehicle = driver.AssignedVehicleId.HasValue
                    ? vehicles.FirstOrDefault(v => v.Id == driver.AssignedVehicleId.Value)
                    : null;

                var fullName = driver.User != null
                    ? $"{driver.User.FirstName} {driver.User.LastName}".Trim()
                    : "Conducteur";

                expiries.Add(new VehicleExpiryDto(
                    assignedVehicle?.Id ?? 0,
                    fullName,
                    driver.PermitType != null ? $"Permis {driver.PermitType}" : "Permis",
                    "driver_permit",
                    driver.PermitExpiry,
                    status,
                    daysUntil,
                    null,
                    null,
                    driver.PermitNumber
                ));
            }
        }

        // Filter by document type
        if (!string.IsNullOrWhiteSpace(request.DocumentType))
        {
            expiries = expiries.Where(e => e.DocumentType == request.DocumentType).ToList();
        }

        // Filter by status
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            expiries = expiries.Where(e => e.Status == request.Status).ToList();
        }

        // Get last renewal info from VehicleCosts
        var vehicleIds = expiries.Select(e => e.VehicleId).Distinct().ToList();
        var costs = await _context.VehicleCosts
            .Where(c => vehicleIds.Contains(c.VehicleId) && 
                        (c.Type == "insurance" || c.Type == "tax" || c.Type == "technical_inspection" ||
                         c.Type == "registration" || c.Type == "transport_permit"))
            .OrderByDescending(c => c.Date)
            .ToListAsync(cancellationToken);

        // Enrich with last renewal info
        expiries = expiries.Select(e =>
        {
            var lastRenewal = costs.FirstOrDefault(c => c.VehicleId == e.VehicleId && c.Type == e.DocumentType);
            return e with
            {
                LastRenewalDate = lastRenewal?.Date,
                LastRenewalCost = lastRenewal?.Amount,
                DocumentNumber = lastRenewal?.DocumentNumber ?? e.DocumentNumber
            };
        }).ToList();

        // Sort by urgency (expired first, then expiring soon)
        expiries = expiries
            .OrderBy(e => e.Status == "expired" ? 0 : e.Status == "expiring_soon" ? 1 : 2)
            .ThenBy(e => e.DaysUntilExpiry)
            .ToList();

        var totalCount = expiries.Count;
        var items = expiries
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return new PaginatedList<VehicleExpiryDto>(items, totalCount, request.Page, request.PageSize);
    }

    private List<VehicleExpiryDto> GetVehicleExpiries(Domain.Entities.Vehicle vehicle, DateTime today)
    {
        var expiries = new List<VehicleExpiryDto>();

        AddExpiry(expiries, vehicle, "insurance", vehicle.InsuranceExpiry, today);
        AddExpiry(expiries, vehicle, "technical_inspection", vehicle.TechnicalInspectionExpiry, today);
        AddExpiry(expiries, vehicle, "tax", vehicle.TaxExpiry, today);
        AddExpiry(expiries, vehicle, "registration", vehicle.RegistrationExpiry, today);
        AddExpiry(expiries, vehicle, "transport_permit", vehicle.TransportPermitExpiry, today);

        return expiries;
    }

    private void AddExpiry(List<VehicleExpiryDto> expiries, Domain.Entities.Vehicle vehicle, 
        string type, DateTime? expiryDate, DateTime today)
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

        expiries.Add(new VehicleExpiryDto(
            vehicle.Id,
            vehicle.Name,
            vehicle.Plate,
            type,
            expiryDate,
            status,
            daysUntil == int.MaxValue ? -1 : daysUntil,
            null,
            null,
            null
        ));
    }
}



