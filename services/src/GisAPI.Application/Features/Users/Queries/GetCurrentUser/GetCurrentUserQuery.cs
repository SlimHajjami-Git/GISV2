using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Users.Queries.GetCurrentUser;

public record GetCurrentUserQuery(int UserId) : IQuery<UserListDto>;
