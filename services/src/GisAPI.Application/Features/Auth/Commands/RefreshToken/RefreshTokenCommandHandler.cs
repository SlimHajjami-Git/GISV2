using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Auth.Commands.Login;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Auth.Commands.RefreshToken;

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, LoginResponse>
{
    private readonly IGisDbContext _context;
    private readonly IJwtService _jwtService;
    private readonly ILogger<RefreshTokenCommandHandler> _logger;

    public RefreshTokenCommandHandler(
        IGisDbContext context,
        IJwtService jwtService,
        ILogger<RefreshTokenCommandHandler> logger)
    {
        _context = context;
        _jwtService = jwtService;
        _logger = logger;
    }

    public async Task<LoginResponse> Handle(RefreshTokenCommand request, CancellationToken ct)
    {
        // 1. Validate the expired JWT (check signature, ignore expiry)
        var principal = _jwtService.ValidateExpiredToken(request.Token);
        if (principal == null)
            throw new DomainException("Token invalide");

        var userIdStr = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value;

        if (!int.TryParse(userIdStr, out var userId))
            throw new DomainException("Token invalide");

        // 2. Find the refresh token in DB
        var storedToken = await _context.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken && rt.UserId == userId, ct);

        if (storedToken == null)
            throw new DomainException("Refresh token invalide");

        if (storedToken.IsRevoked)
            throw new DomainException("Refresh token révoqué");

        if (storedToken.IsExpired)
            throw new DomainException("Refresh token expiré");

        // 3. Revoke old refresh token
        storedToken.RevokedAt = DateTime.UtcNow;

        // 4. Load the user with relations
        var user = await _context.Users
            .Include(u => u.Societe)
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user == null || user.Status != "active")
            throw new DomainException("Compte désactivé ou supprimé");

        // Explicitly load SubscriptionType if Societe has one
        if (user.Societe?.SubscriptionTypeId != null)
        {
            user.Societe.SubscriptionType = await _context.SubscriptionTypes
                .FirstOrDefaultAsync(st => st.Id == user.Societe.SubscriptionTypeId, ct);
        }

        // 5. Generate new tokens
        var newJwt = _jwtService.GenerateToken(user);
        var newRefreshTokenStr = _jwtService.GenerateRefreshToken();

        var newRefreshToken = new GisAPI.Domain.Entities.RefreshToken
        {
            Token = newRefreshTokenStr,
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        storedToken.ReplacedByToken = newRefreshTokenStr;
        _context.RefreshTokens.Add(newRefreshToken);
        await _context.SaveChangesAsync(ct);

        // 6. Build subscription features
        SubscriptionFeaturesDto? subscriptionFeatures = null;
        if (user.Societe?.SubscriptionType is { } subType)
        {
            subscriptionFeatures = new SubscriptionFeaturesDto(
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
                ModuleReports: subType.ModuleReports,
                ModuleSettings: subType.ModuleSettings,
                ModuleUsers: subType.ModuleUsers,
                ModuleSuppliers: subType.ModuleSuppliers,
                ModuleDocuments: subType.ModuleDocuments,
                ModuleAccidents: subType.ModuleAccidents,
                ModuleFleetManagement: subType.ModuleFleetManagement,
                MaxVehicles: subType.MaxVehicles,
                MaxUsers: subType.MaxUsers,
                MaxGpsDevices: subType.MaxGpsDevices,
                MaxGeofences: subType.MaxGeofences,
                HistoryRetentionDays: subType.HistoryRetentionDays
            );
        }

        // Load assigned vehicle IDs for non-admin users
        int[]? assignedVehicleIds = null;
        if (user.Role != null && !user.Role.IsCompanyAdmin && !user.Role.IsSystemRole)
        {
            assignedVehicleIds = await _context.UserVehicles
                .Where(uv => uv.UserId == user.Id)
                .Select(uv => uv.VehicleId)
                .ToArrayAsync(ct);
        }

        _logger.LogInformation("Token refreshed for user {Email} (CompanyId: {CompanyId})", user.Email, user.CompanyId);

        return new LoginResponse(
            newJwt,
            newRefreshTokenStr,
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
                user.Role?.Permissions,
                subscriptionFeatures,
                assignedVehicleIds
            )
        );
    }
}
