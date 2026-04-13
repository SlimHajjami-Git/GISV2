using GisAPI.Application.Common.Interfaces;
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

        var activeReservationVehicleIds = await _context.Reservations
            .Where(r => r.CompanyId == companyId && r.Status == "in_progress")
            .Select(r => r.VehicleId)
            .ToListAsync(ct);

        var vehicles = await _context.Vehicles
            .Where(v => v.CompanyId == companyId)
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
