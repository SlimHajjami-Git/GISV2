using MediatR;

namespace GisAPI.Application.Features.Admin.Companies.Queries.GetCompanyRoles;

public record GetCompanyRolesQuery(int CompanyId) : IRequest<List<AdminRoleDto>>;
