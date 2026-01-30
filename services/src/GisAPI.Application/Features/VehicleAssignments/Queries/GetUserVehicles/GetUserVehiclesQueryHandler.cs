using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.VehicleAssignments.Queries.GetUserVehicles;

public class GetUserVehiclesQueryHandler : IRequestHandler<GetUserVehiclesQuery, List<UserVehicleDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetUserVehiclesQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<UserVehicleDto>> Handle(GetUserVehiclesQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId;
        var userId = request.UserId ?? _tenantService.UserId;

        // Si l'utilisateur est admin ou chef de société, retourner tous les véhicules de la société
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        
        var isAdmin = user?.Role?.IsCompanyAdmin ?? false;
        if (user != null && isAdmin)
        {
            // Retourner tous les véhicules de la société
            return await _context.Vehicles
                .Where(v => v.CompanyId == companyId)
                .Select(v => new UserVehicleDto(
                    v.Id,
                    v.Name,
                    v.Plate,
                    v.Type,
                    v.Brand,
                    v.Model,
                    v.GpsDeviceId,
                    v.GpsDeviceId != null,
                    v.CreatedAt
                ))
                .ToListAsync(ct);
        }

        // Sinon, retourner uniquement les véhicules attribués
        return await _context.VehicleAssignments
            .Include(a => a.Vehicle)
            .Where(a => a.UserId == userId && a.IsActive && a.Vehicle!.CompanyId == companyId)
            .Select(a => new UserVehicleDto(
                a.Vehicle!.Id,
                a.Vehicle.Name,
                a.Vehicle.Plate,
                a.Vehicle.Type,
                a.Vehicle.Brand,
                a.Vehicle.Model,
                a.Vehicle.GpsDeviceId,
                a.Vehicle.GpsDeviceId != null,
                a.AssignedAt
            ))
            .ToListAsync(ct);
    }
}



