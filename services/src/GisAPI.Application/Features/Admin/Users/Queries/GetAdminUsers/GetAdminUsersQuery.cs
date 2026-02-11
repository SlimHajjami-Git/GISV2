using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Admin.Users.Queries.GetAdminUsers;

public record GetAdminUsersQuery(
    string? Search = null,
    string? Status = null,
    int? CompanyId = null
) : IQuery<List<AdminUserDto>>;
