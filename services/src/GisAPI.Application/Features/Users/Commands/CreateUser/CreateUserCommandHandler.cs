using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Users.Commands.CreateUser;

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, UserListDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPublisher _publisher;

    public CreateUserCommandHandler(
        IGisDbContext context,
        ICurrentTenantService tenantService,
        IPasswordHasher passwordHasher,
        IPublisher publisher)
    {
        _context = context;
        _tenantService = tenantService;
        _passwordHasher = passwordHasher;
        _publisher = publisher;
    }

    public async Task<UserListDto> Handle(CreateUserCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");
        var currentUserId = _tenantService.UserId
            ?? throw new DomainException("Utilisateur non identifié");

        // Enforce MaxUsers subscription limit
        var company = await _context.Societes
            .Include(s => s.SubscriptionType)
            .FirstOrDefaultAsync(s => s.Id == companyId, ct);

        if (company?.SubscriptionType != null)
        {
            var currentUsers = await _context.Users.CountAsync(u => u.CompanyId == companyId, ct);
            if (currentUsers >= company.SubscriptionType.MaxUsers)
                throw new DomainException(
                    $"Limite d'utilisateurs atteinte ({company.SubscriptionType.MaxUsers} max pour votre abonnement)");
        }

        // Check email uniqueness
        if (await _context.Users.AnyAsync(u => u.Email == request.Email, ct))
            throw new ConflictException("Cet email est déjà utilisé");

        // Auto-select role based on permissions:
        // All permissions checked → Administrateur (company_admin)
        // Any permission unchecked → non-admin role (Chef de parc / Opérateur)
        var allPermissionsGranted = request.CanMonitoring && request.CanVehicles && request.CanDrivers
            && request.CanReports && request.CanGeofences && request.CanMaintenance
            && request.CanCosts && request.CanFuel && request.CanDocuments && request.CanAccidents
            && request.CanUsers && request.CanSettings && request.CanSuppliers
            && request.CanFleetManagement && request.CanTours && request.CanPlayback;

        Role role;
        if (allPermissionsGranted)
        {
            // Assign company admin role
            role = await _context.Roles
                .FirstOrDefaultAsync(r => r.SocieteId == companyId && r.IsCompanyAdmin, ct)
                ?? throw new DomainException("Aucun rôle administrateur trouvé");
        }
        else
        {
            // Assign a non-admin role (prefer the one sent by frontend, else find first non-admin, else auto-create)
            role = await _context.Roles
                .FirstOrDefaultAsync(r => r.Id == request.RoleId && r.SocieteId == companyId && !r.IsCompanyAdmin, ct)
                ?? await _context.Roles
                    .FirstOrDefaultAsync(r => r.SocieteId == companyId && !r.IsCompanyAdmin, ct);

            if (role == null)
            {
                // Auto-create a non-admin role for this company
                role = new Role { Name = "Opérateur", SocieteId = companyId, IsCompanyAdmin = false, IsSystemRole = false };
                _context.Roles.Add(role);
                await _context.SaveChangesAsync(ct);
            }
        }

        // Validate vehicle assignments
        if (request.AssignedVehicleIds is { Length: > 0 })
        {
            var validCount = await _context.Vehicles
                .CountAsync(v => request.AssignedVehicleIds.Contains(v.Id) && v.CompanyId == companyId, ct);
            if (validCount != request.AssignedVehicleIds.Length)
                throw new DomainException("Un ou plusieurs véhicules sont invalides");
        }

        var user = new User
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Phone = request.Phone,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            RoleId = role.Id,
            CompanyId = companyId,
            Status = "active",
            EmployeeRole = request.EmployeeRole,
            PermitNumber = request.PermitNumber,
            PermitType = request.PermitType,
            PermitExpiry = request.PermitExpiry,
            CIN = request.CIN,
            DateOfBirth = request.DateOfBirth,
            HireDate = request.HireDate,
            AccessLevel = request.AccessLevel ?? "user",
            CanMonitoring = request.CanMonitoring,
            CanVehicles = request.CanVehicles,
            CanDrivers = request.CanDrivers,
            CanReports = request.CanReports,
            CanGeofences = request.CanGeofences,
            CanMaintenance = request.CanMaintenance,
            CanCosts = request.CanCosts,
            CanFuel = request.CanFuel,
            CanDocuments = request.CanDocuments,
            CanAccidents = request.CanAccidents,
            CanUsers = request.CanUsers,
            CanSettings = request.CanSettings,
            CanSuppliers = request.CanSuppliers,
            CanFleetManagement = request.CanFleetManagement,
            CanTours = request.CanTours,
            CanPlayback = request.CanPlayback,
            CanReportTrips = request.CanReportTrips,
            CanReportFuel = request.CanReportFuel,
            CanReportSpeed = request.CanReportSpeed,
            CanReportStops = request.CanReportStops,
            CanReportMileage = request.CanReportMileage,
            CanReportCosts = request.CanReportCosts,
            CanReportMaintenance = request.CanReportMaintenance,
            CanReportDaily = request.CanReportDaily,
            CanReportMonthly = request.CanReportMonthly,
            CanReportMileagePeriod = request.CanReportMileagePeriod,
            CanReportSpeedInfraction = request.CanReportSpeedInfraction,
            CanReportDrivingBehavior = request.CanReportDrivingBehavior
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync(ct);

        // Save vehicle assignments
        if (request.AssignedVehicleIds is { Length: > 0 })
        {
            foreach (var vehicleId in request.AssignedVehicleIds)
            {
                _context.UserVehicles.Add(new UserVehicle
                {
                    UserId = user.Id,
                    VehicleId = vehicleId,
                    AssignedAt = DateTime.UtcNow,
                    AssignedById = currentUserId
                });
            }
            await _context.SaveChangesAsync(ct);
        }

        // Notify company admins about the new user
        var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId, ct);
        if (actor != null)
        {
            await _publisher.Publish(new AdminActionNotificationEvent(
                companyId, currentUserId, actor.FullName,
                "user_created", user.FullName, user.Id, "user"
            ), ct);
        }

        return new UserListDto(
            user.Id,
            user.FullName,
            user.Email,
            user.Phone,
            user.RoleId,
            role.Name,
            role.IsCompanyAdmin,
            user.Status,
            user.CreatedAt,
            user.LastLoginAt,
            request.AssignedVehicleIds,
            new GisAPI.Application.Features.Auth.Commands.Login.UserPermissionsDto(
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
                user.CanReportDrivingBehavior
            )
        );
    }
}
