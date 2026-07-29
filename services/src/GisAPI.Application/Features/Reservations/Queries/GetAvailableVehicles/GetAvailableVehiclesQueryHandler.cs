using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Reservations.Queries.GetAvailableVehicles;

public class GetAvailableVehiclesQueryHandler : IRequestHandler<GetAvailableVehiclesQuery, List<AvailableVehicleDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetAvailableVehiclesQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<AvailableVehicleDto>> Handle(GetAvailableVehiclesQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");

        // Portée véhicules : le filtre société ne suffisait pas — un employé restreint à
        // quelques véhicules se voyait proposer tout le parc à la réservation. null => admin société.
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, ct);

        var reservationsQuery = _context.Reservations
            .Where(r => r.CompanyId == companyId && r.Status == "in_progress");
        if (scope is not null)
            reservationsQuery = reservationsQuery.Where(r => scope.Contains(r.VehicleId));

        var activeReservationVehicleIds = await reservationsQuery
            .Select(r => r.VehicleId)
            .ToListAsync(ct);

        var vehiclesQuery = _context.Vehicles.Where(v => v.CompanyId == companyId);
        if (scope is not null)
            vehiclesQuery = vehiclesQuery.Where(v => scope.Contains(v.Id));

        var vehicles = await vehiclesQuery
            .OrderBy(v => v.Name)
            .ToListAsync(ct);

        return vehicles.Select(v => new AvailableVehicleDto(
            v.Id,
            v.Name,
            v.Plate,
            v.Mileage,
            v.HasGps,
            v.IsRented,
            activeReservationVehicleIds.Contains(v.Id)
        )).ToList();
    }
}
