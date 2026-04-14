using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Users.Queries.GetUserById;

public class GetUserByIdQueryHandler : IRequestHandler<GetUserByIdQuery, UserListDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetUserByIdQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<UserListDto> Handle(GetUserByIdQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");

        var user = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.UserVehicles)
            .Where(u => u.Id == request.Id && u.CompanyId == companyId)
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
                u.AlertEntretien
            ))
            .FirstOrDefaultAsync(ct);

        if (user == null)
            throw new NotFoundException("Utilisateur", request.Id);

        return user;
    }
}
