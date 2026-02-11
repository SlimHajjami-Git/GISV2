using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Admin.Users.Queries.GetAdminUserById;

public record GetAdminUserByIdQuery(int Id) : IQuery<AdminUserDto>;
