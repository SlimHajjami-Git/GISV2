using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Roles.Commands.CreateRole;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Roles.Queries.GetRoleById;

public class GetRoleByIdQueryHandler : IRequestHandler<GetRoleByIdQuery, RoleDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetRoleByIdQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<RoleDto> Handle(GetRoleByIdQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId;

        var role = await _context.Roles
            .Where(r => r.Id == request.Id && r.SocieteId == companyId)
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
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Role", request.Id);

        return role;
    }
}



