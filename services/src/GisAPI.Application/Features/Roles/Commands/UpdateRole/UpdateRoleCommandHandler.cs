using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Roles.Commands.CreateRole;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Roles.Commands.UpdateRole;

public class UpdateRoleCommandHandler : IRequestHandler<UpdateRoleCommand, RoleDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IPermissionService _permissionService;

    public UpdateRoleCommandHandler(
        IGisDbContext context, 
        ICurrentTenantService tenantService,
        IPermissionService permissionService)
    {
        _context = context;
        _tenantService = tenantService;
        _permissionService = permissionService;
    }

    public async Task<RoleDto> Handle(UpdateRoleCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId 
            ?? throw new DomainException("Société non identifiée");

        var role = await _context.Roles
            .Include(r => r.Users)
            .FirstOrDefaultAsync(r => r.Id == request.Id && r.SocieteId == companyId, ct)
            ?? throw new NotFoundException("Role", request.Id);

        if (role.IsSystem)
            throw new DomainException("Impossible de modifier un rôle système");

        // Check name uniqueness if changed
        if (request.Name != null && request.Name != role.Name)
        {
            if (await _context.Roles.AnyAsync(r => r.Name == request.Name && r.SocieteId == companyId && r.Id != request.Id, ct))
                throw new ConflictException($"Un rôle avec le nom '{request.Name}' existe déjà");
            role.Name = request.Name;
        }

        // Validate permissions against subscription if permissions are being updated
        if (request.Permissions != null)
        {
            var societe = await _context.Societes
                .Include(s => s.SubscriptionType)
                .FirstOrDefaultAsync(s => s.Id == companyId, ct)
                ?? throw new NotFoundException("Société", companyId);

            if (societe.SubscriptionType != null)
            {
                var exceedingPerms = _permissionService.GetExceedingPermissions(
                    request.Permissions, 
                    societe.SubscriptionType);
                
                if (exceedingPerms.Count > 0)
                {
                    throw new DomainException(
                        $"Les permissions suivantes dépassent les limites de l'abonnement: {string.Join(", ", exceedingPerms)}");
                }
            }
            role.Permissions = request.Permissions;
        }

        if (request.Description != null) role.Description = request.Description;
        if (request.RoleType != null) role.RoleType = request.RoleType;
        if (request.IsDefault.HasValue) role.IsDefault = request.IsDefault.Value;

        role.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return new RoleDto(
            role.Id,
            role.Name,
            role.Description,
            role.RoleType,
            role.Permissions,
            role.SocieteId,
            role.IsSystem,
            role.IsDefault,
            role.Users.Count,
            role.CreatedAt,
            role.UpdatedAt
        );
    }
}
