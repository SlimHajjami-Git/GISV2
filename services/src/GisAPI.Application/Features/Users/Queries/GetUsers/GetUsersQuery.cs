using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Users.Queries.GetUsers;

public record GetUsersQuery() : IQuery<List<UserListDto>>;
