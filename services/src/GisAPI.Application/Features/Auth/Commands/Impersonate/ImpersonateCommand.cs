using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Auth.Commands.Login;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Auth.Commands.Impersonate;

/// <summary>
/// "Voir en tant que" : un super-administrateur plateforme (system_admin) obtient un
/// jeton au périmètre d'un utilisateur cible (sa société, ses permissions/features),
/// afin de voir l'application exactement comme lui. Le jeton porte un claim
/// <c>impersonated_by</c> d'audit. Réutilise la construction de réponse du login pour
/// renvoyer un payload identique.
/// </summary>
public record ImpersonateCommand(int TargetUserId) : IRequest<LoginResponse>;

public class ImpersonateCommandHandler : IRequestHandler<ImpersonateCommand, LoginResponse>
{
    private readonly IGisDbContext _context;
    private readonly IJwtService _jwtService;
    private readonly ICurrentTenantService _tenant;
    private readonly ILogger<ImpersonateCommandHandler> _logger;

    public ImpersonateCommandHandler(
        IGisDbContext context,
        IJwtService jwtService,
        ICurrentTenantService tenant,
        ILogger<ImpersonateCommandHandler> logger)
    {
        _context = context;
        _jwtService = jwtService;
        _tenant = tenant;
        _logger = logger;
    }

    public async Task<LoginResponse> Handle(ImpersonateCommand request, CancellationToken ct)
    {
        // Réservé au super-administrateur plateforme.
        if (!_tenant.IsSystemAdmin)
            throw new DomainException("Réservé au super-administrateur.");

        // Cible chargée SANS filtre tenant (l'admin impersonate à travers les sociétés).
        var user = await _context.Users
            .IgnoreQueryFilters()
            .Include(u => u.Societe)
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == request.TargetUserId, ct);

        if (user == null)
            throw new DomainException("Utilisateur introuvable.");

        // On n'impersonate pas un autre super-administrateur plateforme.
        if (user.Role?.IsSystemRole == true)
            throw new DomainException("Impossible de prendre le POV d'un super-administrateur.");

        if (user.Societe?.SubscriptionTypeId != null)
        {
            user.Societe.SubscriptionType = await _context.SubscriptionTypes
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(st => st.Id == user.Societe.SubscriptionTypeId, ct);
        }

        var impersonatedBy = _tenant.UserEmail ?? "system_admin";
        var token = _jwtService.GenerateToken(user, impersonatedBy);
        var refreshTokenStr = _jwtService.GenerateRefreshToken();

        // Nouveau refresh token pour la session d'impersonation, SANS révoquer ceux du
        // vrai utilisateur (sa session réelle ne doit pas être déconnectée). Durée courte.
        _context.RefreshTokens.Add(new GisAPI.Domain.Entities.RefreshToken
        {
            Token = refreshTokenStr,
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(ct);

        // Audit : qui a pris le POV de qui (best-effort).
        try
        {
            _context.AuditLogs.Add(new GisAPI.Domain.Entities.AuditLog
            {
                UserId = _tenant.UserId ?? 0,
                CompanyId = user.CompanyId,
                Action = "impersonate",
                EntityType = "User",
                EntityId = user.Id,
                EntityName = user.Email,
                Description = $"{impersonatedBy} a pris le POV de {user.Email} (société {user.Societe?.Name})",
                Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write impersonation audit log");
        }

        _logger.LogInformation(
            "{Admin} impersonating user {Email} (CompanyId {CompanyId})",
            impersonatedBy, user.Email, user.CompanyId);

        var subscriptionFeatures = LoginCommandHandler.BuildSubscriptionFeatures(user.Societe?.SubscriptionType);

        int[]? assignedVehicleIds = null;
        if (user.Role != null && !user.Role.IsCompanyAdmin && !user.Role.IsSystemRole)
        {
            assignedVehicleIds = await _context.UserVehicles
                .IgnoreQueryFilters()
                .Where(uv => uv.UserId == user.Id)
                .Select(uv => uv.VehicleId)
                .ToArrayAsync(ct);
        }

        var userPermissions = LoginCommandHandler.BuildUserPermissions(user);

        return new LoginResponse(
            token,
            refreshTokenStr,
            LoginCommandHandler.BuildUserDto(user, subscriptionFeatures, assignedVehicleIds, userPermissions)
        );
    }
}
