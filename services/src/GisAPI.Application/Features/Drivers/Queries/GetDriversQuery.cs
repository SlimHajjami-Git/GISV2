using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Drivers.Queries;

public record GetDriversQuery() : IRequest<List<DriverDto>>;

public class GetDriversQueryHandler : IRequestHandler<GetDriversQuery, List<DriverDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetDriversQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<DriverDto>> Handle(GetDriversQuery request, CancellationToken ct)
    {
        // Écran OPÉRATIONNEL : on borne toujours à la société de l'appelant, y
        // compris pour un administrateur système. Le filtre global de
        // multi-tenance (GisDbContext) est volontairement contourné pour les
        // rôles système — sans ce filtre explicite, la page « Chauffeurs »
        // affichait donc les chauffeurs de TOUTES les sociétés. Le service de
        // tenant était déjà injecté ici mais n'était pas utilisé.
        // Même correctif que GetExpiriesQueryHandler.
        var companyId = _tenantService.CompanyId ?? 0;

        // Driver is a standalone entity with native fields — no Include on User.
        // Driver.AssignedVehicle is NOT a navigation (see Driver.cs) because it
        // would collide with Vehicle.AssignedDriver in EF's relationship discovery.
        // We join on AssignedVehicleId manually to project vehicle name/plate.
        return await (from d in _context.Drivers
                      where d.CompanyId == companyId
                      join v in _context.Vehicles
                          on d.AssignedVehicleId equals v.Id into vehicleJoin
                      from vehicle in vehicleJoin.DefaultIfEmpty()
                      orderby d.LastName, d.FirstName
                      select new DriverDto(
                          d.Id,
                          d.FirstName,
                          d.LastName,
                          d.Email,
                          d.Phone,
                          d.PermitNumber,
                          d.PermitType,
                          d.PermitExpiry,
                          d.CIN,
                          d.DateOfBirth,
                          d.HireDate,
                          d.AssignedVehicleId,
                          vehicle != null ? vehicle.Name : null,
                          vehicle != null ? vehicle.Plate : null,
                          d.Status,
                          d.CreatedAt
                      ))
                      .ToListAsync(ct);
    }
}
