using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Models;
using GisAPI.Application.Common.Security;
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

        // Portée par utilisateur : un employé restreint à quelques véhicules ne
        // doit voir que LEURS échéances. Le filtre société seul laissait fuiter
        // tout le parc ici, alors que le monitoring et le playback filtraient
        // bien — d'où la réclamation client.
        var accessibleIds = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, cancellationToken);

        var vehicleQuery = _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId);

        if (accessibleIds is not null)
            vehicleQuery = vehicleQuery.Where(v => accessibleIds.Contains(v.Id));

        var vehicles = await vehicleQuery.ToListAsync(cancellationToken);

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
            // Même portée pour les permis : un utilisateur restreint ne voit que
            // les chauffeurs affectés aux véhicules qu'il a le droit de voir.
            var drivers = await DriverPermitExpiries.LoadAsync(_context, companyId, accessibleIds, cancellationToken);

            foreach (var driver in drivers)
            {
                expiries.Add(DriverPermitExpiries.ToDto(
                    driver,
                    vehicles,
                    ExpiryCalendar.Status(driver.PermitExpiry, today),
                    ExpiryCalendar.DaysUntil(driver.PermitExpiry!.Value, today)));
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

        // Sort by urgency: expirés, bientôt, à jour, puis non renseignées en fin.
        expiries = expiries
            .OrderBy(e => ExpiryCalendar.StatusRank(e.Status))
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
        // Jours calendaires (ExpiryCalendar) : l'heure stockée ne décale ni le
        // compte ni la date affichée.
        expiries.Add(new VehicleExpiryDto(
            vehicle.Id,
            vehicle.Name,
            vehicle.Plate,
            type,
            ExpiryCalendar.Day(expiryDate),
            ExpiryCalendar.Status(expiryDate, today),
            expiryDate.HasValue ? ExpiryCalendar.DaysUntil(expiryDate.Value, today) : -1,
            null,
            null,
            null
        ));
    }
}



