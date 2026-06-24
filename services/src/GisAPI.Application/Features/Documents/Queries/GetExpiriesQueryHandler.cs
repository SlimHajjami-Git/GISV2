using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Models;
using GisAPI.Application.Common.Services;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Documents.Queries;

public class GetExpiriesQueryHandler : IRequestHandler<GetExpiriesQuery, PaginatedList<VehicleExpiryDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private static readonly string[] DocumentTypes = { "insurance", "technical_inspection", "tax", "registration", "transport_permit", "driver_permit" };

    public GetExpiriesQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<PaginatedList<VehicleExpiryDto>> Handle(GetExpiriesQuery request, CancellationToken cancellationToken)
    {
        // Operational page — always scope by the caller's company, even for
        // system admins (admin@belive.tn). The global query filter is bypassed
        // for system roles, so without this explicit filter the échéances page
        // leaked every company's vehicles + drivers.
        var companyId = _tenantService.CompanyId ?? 0;
        var userId = _tenantService.UserId ?? 0;
        var isAdmin = _tenantService.UserRoles.Any(r => r == "company_admin" || r == "admin" || r == "super_admin" || r == "system_admin");
        // Restrict to the user's assigned vehicles (authoritative — even for admins with assignments).
        var visibleVehicleIds = await VehicleVisibility.GetVisibleVehicleIdsAsync(_context, userId, isAdmin, cancellationToken);

        var vehiclesQuery = _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId);
        if (visibleVehicleIds != null)
            vehiclesQuery = vehiclesQuery.Where(v => visibleVehicleIds.Contains(v.Id));
        var vehicles = await vehiclesQuery.ToListAsync(cancellationToken);

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

        // Add driver permit expiries (from the standalone drivers table)
        if (!request.VehicleId.HasValue)
        {
            var driversQuery = _context.Drivers
                .AsNoTracking()
                .Where(d => d.CompanyId == companyId && d.PermitExpiry != null);
            // Restricted users only see permits of drivers assigned to a vehicle they can see.
            if (visibleVehicleIds != null)
                driversQuery = driversQuery.Where(d => d.AssignedVehicleId != null && visibleVehicleIds.Contains(d.AssignedVehicleId.Value));
            var drivers = await driversQuery.ToListAsync(cancellationToken);

            foreach (var driver in drivers)
            {
                var daysUntil = (int)(driver.PermitExpiry!.Value.Date - today).TotalDays;
                var status = driver.PermitExpiry.Value.Date < today ? "expired"
                    : daysUntil <= 30 ? "expiring_soon"
                    : "ok";

                // Find the vehicle assigned to this driver (if any).
                // Source of truth is driver.AssignedVehicleId — vehicles.AssignedDriverId
                // is the reverse leg and not always kept in sync when forms only write the
                // driver-side field (see drivers popup which only edits AssignedVehicleId).
                var assignedVehicle = driver.AssignedVehicleId.HasValue
                    ? vehicles.FirstOrDefault(v => v.Id == driver.AssignedVehicleId.Value)
                    : null;

                expiries.Add(new VehicleExpiryDto(
                    assignedVehicle?.Id ?? 0,
                    driver.FullName,
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



