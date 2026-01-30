using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Roles.Commands.CreateRole;

public class CreateRoleCommandHandler : IRequestHandler<CreateRoleCommand, RoleDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IPermissionService _permissionService;

    public CreateRoleCommandHandler(
        IGisDbContext context, 
        ICurrentTenantService tenantService,
        IPermissionService permissionService)
    {
        _context = context;
        _tenantService = tenantService;
        _permissionService = permissionService;
    }

    public async Task<RoleDto> Handle(CreateRoleCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId 
            ?? throw new DomainException("Société non identifiée");

        // Check if role name already exists for this company
        if (await _context.Roles.AnyAsync(r => r.Name == request.Name && r.SocieteId == companyId, ct))
            throw new ConflictException($"Un rôle avec le nom '{request.Name}' existe déjà");

        // Get the company's subscription type to validate permissions
        var societe = await _context.Societes
            .Include(s => s.SubscriptionType)
            .FirstOrDefaultAsync(s => s.Id == companyId, ct)
            ?? throw new NotFoundException("Société", companyId);

        // Validate role permissions against subscription
        if (societe.SubscriptionType != null && request.Permissions != null)
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

        // Check if trying to create another company_admin role
        if (request.IsCompanyAdmin)
        {
            var existingAdmin = await _context.Roles
                .AnyAsync(r => r.SocieteId == companyId && r.IsCompanyAdmin, ct);
            if (existingAdmin)
                throw new DomainException("Un rôle administrateur existe déjà pour cette société");
        }

        var role = new Role
        {
            Name = request.Name,
            Description = request.Description,
            IsCompanyAdmin = request.IsCompanyAdmin,
            Permissions = request.Permissions,
            SocieteId = companyId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Roles.Add(role);
        await _context.SaveChangesAsync(ct);

        return new RoleDto(
            role.Id,
            role.Name,
            role.Description,
            role.SocieteId,
            role.IsCompanyAdmin,
            role.IsSystemRole,
            role.Permissions,
            0,
            role.CreatedAt,
            role.UpdatedAt
        );
    }
}



