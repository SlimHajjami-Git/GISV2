using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Companies.Queries.GetCompanyRoles;

public class GetCompanyRolesQueryHandler : IRequestHandler<GetCompanyRolesQuery, List<AdminRoleDto>>
{
    private readonly IGisDbContext _context;

    public GetCompanyRolesQueryHandler(IGisDbContext context) => _context = context;

    public async Task<List<AdminRoleDto>> Handle(GetCompanyRolesQuery request, CancellationToken ct)
    {
        return await _context.Roles
            .Where(r => r.SocieteId == request.CompanyId)
            .Select(r => new AdminRoleDto
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                RoleType = r.Name,
                Permissions = null,
                IsSystem = false,
                IsDefault = false,
                UserCount = _context.Users.Count(u => u.RoleId == r.Id),
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            })
            .ToListAsync(ct);
    }
}
