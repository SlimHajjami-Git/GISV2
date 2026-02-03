using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Auth.Commands.Login;

public class LoginCommandHandler : IRequestHandler<LoginCommand, LoginResponse>
{
    private readonly IGisDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwtService;

    public LoginCommandHandler(
        IGisDbContext context,
        IPasswordHasher passwordHasher,
        IJwtService jwtService)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtService = jwtService;
    }

    public async Task<LoginResponse> Handle(LoginCommand request, CancellationToken ct)
    {
        var user = await _context.Users
            .Include(u => u.Societe)
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower(), ct);

        if (user == null)
            throw new NotFoundException("User", request.Email);
        
        // Debug: Log user found
        Console.WriteLine($"[Login] Found user: {user.Email}, CompanyId: {user.CompanyId}, SubscriptionTypeId: {user.Societe?.SubscriptionTypeId}");
        
        // Explicitly load SubscriptionType if Societe has one
        if (user.Societe?.SubscriptionTypeId != null)
        {
            user.Societe.SubscriptionType = await _context.SubscriptionTypes
                .FirstOrDefaultAsync(st => st.Id == user.Societe.SubscriptionTypeId, ct);
            Console.WriteLine($"[Login] Loaded SubscriptionType: {user.Societe.SubscriptionType?.Name ?? "NULL"}");
        }

        if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
            throw new DomainException("Invalid credentials");

        user.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        var token = _jwtService.GenerateToken(user);
        var refreshToken = _jwtService.GenerateRefreshToken();

        // Build subscription features from company's subscription type
        SubscriptionFeaturesDto? subscriptionFeatures = null;
        var subType = user.Societe?.SubscriptionType;
        
        // Debug logging
        Console.WriteLine($"[Login] User: {user.Email}, CompanyId: {user.CompanyId}");
        Console.WriteLine($"[Login] Societe: {user.Societe?.Name ?? "NULL"}, SubscriptionTypeId: {user.Societe?.SubscriptionTypeId}");
        Console.WriteLine($"[Login] SubscriptionType: {subType?.Name ?? "NULL"}, Id: {subType?.Id}");
        if (subType != null)
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
                user.Role?.Name ?? "",
                user.Role?.IsCompanyAdmin ?? false,
                user.Role?.IsSystemAdmin ?? false,
                user.CompanyId,
                user.Societe?.Name ?? "",
                user.Role?.Permissions,
                subscriptionFeatures
            )
        );
    }
}



