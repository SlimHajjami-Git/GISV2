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

        var users = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.UserVehicles)
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
                u.UserVehicles.Select(uv => uv.VehicleId).ToArray(),
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

        return users;
    }
}
