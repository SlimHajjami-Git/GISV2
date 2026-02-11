using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Auth.Commands.Login;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Auth.Commands.Register;

public class RegisterCommandHandler : IRequestHandler<RegisterCommand, LoginResponse>
{
    private readonly IGisDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwtService;
    private readonly ILogger<RegisterCommandHandler> _logger;

    public RegisterCommandHandler(
        IGisDbContext context,
        IPasswordHasher passwordHasher,
        IJwtService jwtService,
        ILogger<RegisterCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtService = jwtService;
        _logger = logger;
    }

    public async Task<LoginResponse> Handle(RegisterCommand request, CancellationToken ct)
    {
        if (await _context.Users.AnyAsync(u => u.Email.ToLower() == request.Email.ToLower(), ct))
            throw new ConflictException("Cet email est déjà utilisé");

        // Get default subscription type
        var defaultSubscriptionType = await _context.SubscriptionTypes
            .FirstOrDefaultAsync(st => st.IsActive, ct);

        // Create the company
        var societe = new Societe
        {
            Name = request.CompanyName,
            Type = "transport",
            SubscriptionTypeId = defaultSubscriptionType?.Id,
            Settings = new SocieteSettings()
        };
        _context.Societes.Add(societe);
        await _context.SaveChangesAsync(ct);

        // Create default permissions from subscription
        var defaultPermissions = new Dictionary<string, object>
        {
            ["modules"] = new Dictionary<string, bool>
            {
                ["dashboard"] = true,
                ["monitoring"] = true,
                ["vehicles"] = true,
                ["employees"] = true,
                ["geofences"] = true,
                ["maintenance"] = true,
                ["costs"] = true,
                ["reports"] = true,
                ["settings"] = true,
                ["users"] = true
            }
        };

        // Create company_admin role for this company
        var adminRole = new Role
        {
            Name = "Administrateur",
            Description = "Administrateur de la société avec tous les droits",
            SocieteId = societe.Id,
            IsCompanyAdmin = true,
            Permissions = defaultPermissions
        };
        _context.Roles.Add(adminRole);
        await _context.SaveChangesAsync(ct);

        // Create the admin user
        var user = new User
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email.ToLower(),
            Phone = request.Phone,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            CompanyId = societe.Id,
            RoleId = adminRole.Id,
            Status = "active"
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync(ct);

        // Load the role for JWT generation
        user.Role = adminRole;

        var token = _jwtService.GenerateToken(user);
        var refreshToken = _jwtService.GenerateRefreshToken();

        // Build subscription features
        SubscriptionFeaturesDto? subscriptionFeatures = null;
        if (defaultSubscriptionType != null)
        {
            subscriptionFeatures = new SubscriptionFeaturesDto(
                GpsTracking: defaultSubscriptionType.GpsTracking,
                GpsInstallation: defaultSubscriptionType.GpsInstallation,
                ApiAccess: defaultSubscriptionType.ApiAccess,
                AdvancedReports: defaultSubscriptionType.AdvancedReports,
                RealTimeAlerts: defaultSubscriptionType.RealTimeAlerts,
                HistoryPlayback: defaultSubscriptionType.HistoryPlayback,
                FuelAnalysis: defaultSubscriptionType.FuelAnalysis,
                DrivingBehavior: defaultSubscriptionType.DrivingBehavior,
                ModuleDashboard: defaultSubscriptionType.ModuleDashboard,
                ModuleMonitoring: defaultSubscriptionType.ModuleMonitoring,
                ModuleVehicles: defaultSubscriptionType.ModuleVehicles,
                ModuleEmployees: defaultSubscriptionType.ModuleEmployees,
                ModuleGeofences: defaultSubscriptionType.ModuleGeofences,
                ModuleMaintenance: defaultSubscriptionType.ModuleMaintenance,
                ModuleCosts: defaultSubscriptionType.ModuleCosts,
                ModuleReports: defaultSubscriptionType.ModuleReports,
                ModuleSettings: defaultSubscriptionType.ModuleSettings,
                ModuleUsers: defaultSubscriptionType.ModuleUsers,
                ModuleSuppliers: defaultSubscriptionType.ModuleSuppliers,
                ModuleDocuments: defaultSubscriptionType.ModuleDocuments,
                ModuleAccidents: defaultSubscriptionType.ModuleAccidents,
                ModuleFleetManagement: defaultSubscriptionType.ModuleFleetManagement,
                MaxVehicles: defaultSubscriptionType.MaxVehicles,
                MaxUsers: defaultSubscriptionType.MaxUsers,
                MaxGpsDevices: defaultSubscriptionType.MaxGpsDevices,
                MaxGeofences: defaultSubscriptionType.MaxGeofences,
                HistoryRetentionDays: defaultSubscriptionType.HistoryRetentionDays
            );
        }

        _logger.LogInformation("New company registered: {Company} by {Email}", societe.Name, user.Email);

        return new LoginResponse(
            token,
            refreshToken,
            new UserDto(
                user.Id,
                user.FirstName,
                user.LastName,
                user.Email,
                user.Phone,
                user.PermitNumber,
                user.RoleId,
                adminRole.Name,
                adminRole.IsCompanyAdmin,
                adminRole.IsSystemAdmin,
                user.CompanyId,
                societe.Name,
                adminRole.Permissions,
                subscriptionFeatures
            )
        );
    }
}



