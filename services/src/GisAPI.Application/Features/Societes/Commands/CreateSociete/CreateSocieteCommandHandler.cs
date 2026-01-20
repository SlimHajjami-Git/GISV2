using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Societes.Queries.GetSocieteById;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Societes.Commands.CreateSociete;

public class CreateSocieteCommandHandler : IRequestHandler<CreateSocieteCommand, SocieteDetailDto>
{
    private readonly IGisDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPermissionService _permissionService;

    public CreateSocieteCommandHandler(
        IGisDbContext context, 
        IPasswordHasher passwordHasher,
        IPermissionService permissionService)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _permissionService = permissionService;
    }

    public async Task<SocieteDetailDto> Handle(CreateSocieteCommand request, CancellationToken ct)
    {
        // Check if email already exists
        if (await _context.Users.AnyAsync(u => u.Email == request.AdminEmail, ct))
            throw new ConflictException($"L'email '{request.AdminEmail}' est déjà utilisé");

        // Validate subscription type if provided
        if (request.SubscriptionTypeId.HasValue)
        {
            var typeExists = await _context.SubscriptionTypes
                .AnyAsync(st => st.Id == request.SubscriptionTypeId.Value, ct);
            if (!typeExists)
                throw new NotFoundException("SubscriptionType", request.SubscriptionTypeId.Value);
        }

        // Create societe
        var societe = new Societe
        {
            Name = request.Name,
            Type = request.Type,
            Description = request.Description,
            Address = request.Address,
            City = request.City,
            Country = request.Country,
            Phone = request.Phone,
            Email = request.Email,
            SubscriptionTypeId = request.SubscriptionTypeId,
            IsActive = true,
            SubscriptionStatus = "active",
            SubscriptionStartedAt = DateTime.UtcNow,
            Settings = new SocieteSettings()
        };

        _context.Societes.Add(societe);
        await _context.SaveChangesAsync(ct);

        // Get subscription type to determine available permissions
        SubscriptionType? subscriptionType = null;
        if (request.SubscriptionTypeId.HasValue)
        {
            subscriptionType = await _context.SubscriptionTypes
                .FirstOrDefaultAsync(st => st.Id == request.SubscriptionTypeId.Value, ct);
        }

        // Build admin permissions from subscription
        var adminPermissions = subscriptionType != null
            ? _permissionService.GetSubscriptionPermissions(subscriptionType)
            : new Dictionary<string, object> { { "all", true } };

        // Build basic user permissions (view only)
        var userPermissions = new Dictionary<string, object>
        {
            { "dashboard", true },
            { "monitoring", true },
            { "vehicles", new Dictionary<string, object> { { "view", true } } }
        };

        // Create default roles
        var adminRole = new Role
        {
            Name = "Administrateur",
            Description = "Accès complet selon l'abonnement",
            RoleType = "company_admin",
            SocieteId = societe.Id,
            IsSystem = false,
            IsDefault = false,
            Permissions = adminPermissions
        };

        var userRole = new Role
        {
            Name = "Utilisateur",
            Description = "Accès standard (lecture seule)",
            RoleType = "employee",
            SocieteId = societe.Id,
            IsSystem = false,
            IsDefault = true,
            Permissions = userPermissions
        };

        _context.Roles.Add(adminRole);
        _context.Roles.Add(userRole);
        await _context.SaveChangesAsync(ct);

        // Create admin user
        var adminUser = new User
        {
            Name = request.AdminName,
            Email = request.AdminEmail,
            PasswordHash = _passwordHasher.HashPassword(request.AdminPassword),
            Phone = request.Phone,
            CompanyId = societe.Id,
            RoleId = adminRole.Id,
            IsCompanyAdmin = true,
            UserType = "admin",
            Status = "active",
            Roles = new[] { "admin" },
            Permissions = new[] { "all" }
        };

        _context.Users.Add(adminUser);
        await _context.SaveChangesAsync(ct);

        return new SocieteDetailDto(
            societe.Id,
            societe.Name,
            societe.Type,
            societe.Description,
            societe.Address,
            societe.City,
            societe.Country,
            societe.Phone,
            societe.Email,
            societe.LogoUrl,
            societe.TaxId,
            societe.RC,
            societe.IF,
            societe.IsActive,
            societe.SubscriptionStatus,
            societe.BillingCycle,
            societe.SubscriptionStartedAt,
            societe.SubscriptionExpiresAt,
            societe.LastPaymentAt,
            societe.NextPaymentAmount,
            societe.SubscriptionTypeId,
            null,
            new SocieteSettingsDto(
                societe.Settings.Currency,
                societe.Settings.Timezone,
                societe.Settings.Language,
                societe.Settings.DateFormat,
                societe.Settings.DistanceUnit,
                societe.Settings.SpeedUnit,
                societe.Settings.VolumeUnit
            ),
            1, // UsersCount
            0, // VehiclesCount
            0, // GpsDevicesCount
            0, // GeofencesCount
            2, // RolesCount
            societe.CreatedAt,
            societe.UpdatedAt
        );
    }
}
