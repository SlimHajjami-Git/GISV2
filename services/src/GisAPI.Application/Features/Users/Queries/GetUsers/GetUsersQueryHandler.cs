using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Users.Queries.GetUsers;

public class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, List<UserListDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetUsersQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<UserListDto>> Handle(GetUsersQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new GisAPI.Domain.Exceptions.DomainException("Société non identifiée");

        // Affectations véhicule par utilisateur — récupérées SÉPARÉMENT (petite
        // requête plate), PAS via .Include(UserVehicles).
        //
        // L'ancienne version incluait la collection UserVehicles DANS la requête
        // users : EF générait un JOIN qui MULTIPLIAIT chaque utilisateur par son
        // nombre de véhicules assignés. Chez HERTZ les admins sont assignés à
        // ~219 véhicules → des centaines de lignes, chacune traînant societe
        // (JSON settings) + subscription_type (60 colonnes + JSONB) à
        // matérialiser côté .NET. Résultat : /api/users à 2,3 s pour 4 users
        // (la requête SQL équivalente plate fait 0,4 ms). Une requête dédiée
        // sur user_vehicles supprime le cartésien.
        var vehiclesByUser = (await _context.UserVehicles
                .AsNoTracking()
                .Where(uv => uv.User != null && uv.User.CompanyId == companyId)
                .Select(uv => new { uv.UserId, uv.VehicleId })
                .ToListAsync(ct))
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.VehicleId).ToArray());

        var users = await _context.Users
            .AsNoTracking()
            .Where(u => u.CompanyId == companyId)
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .Select(u => new UserListDto(
                u.Id,
                u.FullName,
                u.Email,
                u.Phone,
                u.RoleId,
                u.Role != null ? u.Role.Name : null,
                u.Role != null && u.Role.IsCompanyAdmin,
                u.Status,
                u.CreatedAt,
                u.LastLoginAt,
                null,   // AssignedVehicleIds : renseigné après la requête (voir plus bas)
                new GisAPI.Application.Features.Auth.Commands.Login.UserPermissionsDto(
                    u.AccessLevel,
                    u.CanMonitoring,
                    u.CanVehicles,
                    u.CanDrivers,
                    u.CanReports,
                    u.CanGeofences,
                    u.CanMaintenance,
                    u.CanCosts,
                    u.CanFuel,
                    u.CanDocuments,
                    u.CanAccidents,
                    u.CanUsers,
                    u.CanSettings,
                    u.CanSuppliers,
                    u.CanFleetManagement,
                    u.CanTours,
                    u.CanPlayback,
                    u.CanReportTrips,
                    u.CanReportFuel,
                    u.CanReportSpeed,
                    u.CanReportStops,
                    u.CanReportMileage,
                    u.CanReportCosts,
                    u.CanReportMaintenance,
                    u.CanReportDaily,
                    u.CanReportMonthly,
                    u.CanReportMileagePeriod,
                    u.CanReportSpeedInfraction,
                    u.CanReportDrivingBehavior,
                    u.CanReportMonthlyCosts
                ),
                u.AlertAssurance,
                u.AlertTaxeCirculation,
                u.AlertVisiteTechnique,
                u.AlertEntretien,
                u.DailyReportEmailEnabled
            ))
            .ToListAsync(ct);

        // Rattache les IDs de véhicules assignés (requête séparée ci-dessus).
        for (int i = 0; i < users.Count; i++)
        {
            if (vehiclesByUser.TryGetValue(users[i].Id, out var ids))
                users[i] = users[i] with { AssignedVehicleIds = ids };
            else
                users[i] = users[i] with { AssignedVehicleIds = System.Array.Empty<int>() };
        }

        return users;
    }
}
