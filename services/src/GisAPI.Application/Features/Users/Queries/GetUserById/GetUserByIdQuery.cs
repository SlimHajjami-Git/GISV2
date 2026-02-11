using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Users.Queries.GetUserById;

public record GetUserByIdQuery(int Id) : IQuery<UserListDto>;
