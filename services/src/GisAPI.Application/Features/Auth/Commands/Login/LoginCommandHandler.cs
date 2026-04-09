using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Auth.Commands.Login;

public class LoginCommandHandler : IRequestHandler<LoginCommand, LoginResponse>
{
    private readonly IGisDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwtService;
    private readonly ILogger<LoginCommandHandler> _logger;

    public LoginCommandHandler(
        IGisDbContext context,
        IPasswordHasher passwordHasher,
        IJwtService jwtService,
        ILogger<LoginCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtService = jwtService;
        _logger = logger;
    }

    public async Task<LoginResponse> Handle(LoginCommand request, CancellationToken ct)
    {
        var user = await _context.Users
            .Include(u => u.Societe)
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower(), ct);

        if (user == null)
            throw new DomainException("Email ou mot de passe incorrect");

        if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
            throw new DomainException("Email ou mot de passe incorrect");

        if (user.Status != "active")
            throw new DomainException("Compte désactivé");

        // Explicitly load SubscriptionType if Societe has one
        if (user.Societe?.SubscriptionTypeId != null)
        {
            user.Societe.SubscriptionType = await _context.SubscriptionTypes
                .FirstOrDefaultAsync(st => st.Id == user.Societe.SubscriptionTypeId, ct);
        }

        user.LastLoginAt = DateTime.UtcNow;

        var token = _jwtService.GenerateToken(user);
        var refreshTokenStr = _jwtService.GenerateRefreshToken();

        // Revoke any existing active refresh tokens for this user
        var existingTokens = _context.RefreshTokens
            .Where(rt => rt.UserId == user.Id && rt.RevokedAt == null && rt.ExpiresAt > DateTime.UtcNow);
        foreach (var existing in existingTokens)
            existing.RevokedAt = DateTime.UtcNow;

        // Save new refresh token to DB
        var refreshTokenEntity = new GisAPI.Domain.Entities.RefreshToken
        {
            Token = refreshTokenStr,
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        _context.RefreshTokens.Add(refreshTokenEntity);
        await _context.SaveChangesAsync(ct);

        // Build subscription features
        var subscriptionFeatures = BuildSubscriptionFeatures(user.Societe?.SubscriptionType);

        // Load assigned vehicle IDs for non-admin users
        int[]? assignedVehicleIds = null;
        if (user.Role != null && !user.Role.IsCompanyAdmin && !user.Role.IsSystemRole)
        {
            assignedVehicleIds = await _context.UserVehicles
                .Where(uv => uv.UserId == user.Id)
                .Select(uv => uv.VehicleId)
                .ToArrayAsync(ct);
        }

        _logger.LogInformation("User {Email} logged in (CompanyId: {CompanyId})", user.Email, user.CompanyId);

        var userPermissions = new UserPermissionsDto(
            AccessLevel: user.AccessLevel,
            CanMonitoring: user.CanMonitoring,
            CanVehicles: user.CanVehicles,
            CanDrivers: user.CanDrivers,
            CanReports: user.CanReports,
            CanGeofences: user.CanGeofences,
            CanMaintenance: user.CanMaintenance,
            CanCosts: user.CanCosts,
            CanFuel: user.CanFuel,
            CanDocuments: user.CanDocuments,
            CanAccidents: user.CanAccidents,
            CanUsers: user.CanUsers,
            CanSettings: user.CanSettings,
            CanSuppliers: user.CanSuppliers,
            CanFleetManagement: user.CanFleetManagement,
            CanTours: user.CanTours,
            CanPlayback: user.CanPlayback
        );

        return new LoginResponse(
            token,
            refreshTokenStr,
            new UserDto(
                user.Id,
                user.FirstName,
                user.LastName,
                user.Email,
                user.Phone,
                user.PermitNumber,
                user.RoleId,
                user.Role?.Name ?? "",
                user.Role?.IsCompanyAdmin ?? false,
                user.Role?.IsSystemAdmin ?? false,
                user.CompanyId,
                user.Societe?.Name ?? "",
                user.Societe?.Type,
                user.Role?.Permissions,
                subscriptionFeatures,
                assignedVehicleIds,
                userPermissions
            )
        );
    }

    private static SubscriptionFeaturesDto? BuildSubscriptionFeatures(
        GisAPI.Domain.Entities.SubscriptionType? subType)
    {
        if (subType == null) return null;

        return new SubscriptionFeaturesDto(
            GpsTracking: subType.GpsTracking,
            GpsInstallation: subType.GpsInstallation,
            ApiAccess: subType.ApiAccess,
            AdvancedReports: subType.AdvancedReports,
            RealTimeAlerts: subType.RealTimeAlerts,
            HistoryPlayback: subType.HistoryPlayback,
            FuelAnalysis: subType.FuelAnalysis,
            DrivingBehavior: subType.DrivingBehavior,
            ModuleDashboard: subType.ModuleDashboard,
            ModuleMonitoring: subType.ModuleMonitoring,
            ModuleVehicles: subType.ModuleVehicles,
            ModuleEmployees: subType.ModuleEmployees,
            ModuleGeofences: subType.ModuleGeofences,
            ModuleMaintenance: subType.ModuleMaintenance,
            ModuleCosts: subType.ModuleCosts,
            ModuleFuel: subType.ModuleFuel,
            ModuleReports: subType.ModuleReports,
            ModuleSettings: subType.ModuleSettings,
            ModuleUsers: subType.ModuleUsers,
            ModuleSuppliers: subType.ModuleSuppliers,
            ModuleDocuments: subType.ModuleDocuments,
            ModuleAccidents: subType.ModuleAccidents,
            ModuleFleetManagement: subType.ModuleFleetManagement,
            ModuleTours: subType.ModuleTours,
            ReportTrips: subType.ReportTrips,
            ReportFuel: subType.ReportFuel,
            ReportSpeed: subType.ReportSpeed,
            ReportStops: subType.ReportStops,
            ReportMileage: subType.ReportMileage,
            ReportCosts: subType.ReportCosts,
            ReportMaintenance: subType.ReportMaintenance,
            ReportDaily: subType.ReportDaily,
            ReportMonthly: subType.ReportMonthly,
            ReportMileagePeriod: subType.ReportMileagePeriod,
            ReportSpeedInfraction: subType.ReportSpeedInfraction,
            ReportDrivingBehavior: subType.ReportDrivingBehavior,
            MaxVehicles: subType.MaxVehicles,
            MaxUsers: subType.MaxUsers,
            MaxGpsDevices: subType.MaxGpsDevices,
            MaxGeofences: subType.MaxGeofences,
            HistoryRetentionDays: subType.HistoryRetentionDays
        );
    }
}



