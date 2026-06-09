using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Auth.Commands.Login;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Users.Commands.UpdateProfile;

public class UpdateProfileCommandHandler : IRequestHandler<UpdateProfileCommand, UserListDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public UpdateProfileCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<UserListDto> Handle(UpdateProfileCommand request, CancellationToken ct)
    {
        var userId = _tenantService.UserId
            ?? throw new DomainException("Utilisateur non identifié");
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");

        // Validate basic inputs
        if (string.IsNullOrWhiteSpace(request.FirstName))
            throw new DomainException("Le prénom est obligatoire");
        if (string.IsNullOrWhiteSpace(request.LastName))
            throw new DomainException("Le nom est obligatoire");
        if (string.IsNullOrWhiteSpace(request.Email))
            throw new DomainException("L'email est obligatoire");

        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId && u.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Utilisateur", userId);

        // Enforce email uniqueness within the tenant
        var normalizedEmail = request.Email.Trim();
        var emailTaken = await _context.Users
            .AnyAsync(u => u.CompanyId == companyId
                        && u.Id != userId
                        && u.Email == normalizedEmail, ct);
        if (emailTaken)
            throw new DomainException("Cet email est déjà utilisé par un autre utilisateur");

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Email = normalizedEmail;
        user.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        // Return a fresh DTO that the frontend can splice into AuthService state
        return new UserListDto(
            user.Id,
            user.FullName,
            user.Email,
            user.Phone,
            user.RoleId,
            user.Role?.Name,
            user.Role?.IsCompanyAdmin ?? false,
            user.Status,
            user.CreatedAt,
            user.LastLoginAt,
            Array.Empty<int>(),
            new UserPermissionsDto(
                user.AccessLevel,
                user.CanMonitoring,
                user.CanVehicles,
                user.CanDrivers,
                user.CanReports,
                user.CanGeofences,
                user.CanMaintenance,
                user.CanCosts,
                user.CanFuel,
                user.CanDocuments,
                user.CanAccidents,
                user.CanUsers,
                user.CanSettings,
                user.CanSuppliers,
                user.CanFleetManagement,
                user.CanTours,
                user.CanPlayback,
                user.CanReportTrips,
                user.CanReportFuel,
                user.CanReportSpeed,
                user.CanReportStops,
                user.CanReportMileage,
                user.CanReportCosts,
                user.CanReportMaintenance,
                user.CanReportDaily,
                user.CanReportMonthly,
                user.CanReportMileagePeriod,
                user.CanReportSpeedInfraction,
                user.CanReportDrivingBehavior,
                user.CanReportMonthlyCosts
            ),
            user.AlertAssurance,
            user.AlertTaxeCirculation,
            user.AlertVisiteTechnique,
            user.AlertEntretien,
            user.DailyReportEmailEnabled
        );
    }
}
