using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Roles.Commands.CreateRole;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Roles.Queries.GetRoles;

public class GetRolesQueryHandler : IRequestHandler<GetRolesQuery, List<RoleDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetRolesQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<RoleDto>> Handle(GetRolesQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId;

        var roles = await _context.Roles
            .Where(r => r.SocieteId == companyId)
            .OrderByDescending(r => r.IsCompanyAdmin)
            .ThenBy(r => r.Name)
            .Select(r => new RoleDto(
                r.Id,
                r.Name,
                r.Description,
                r.SocieteId,
                r.IsCompanyAdmin,
                r.IsSystemRole,
                r.Permissions,
                r.Users.Count,
                r.CreatedAt,
                r.UpdatedAt
            ))
            .ToListAsync(ct);

        return roles;
    }
}



