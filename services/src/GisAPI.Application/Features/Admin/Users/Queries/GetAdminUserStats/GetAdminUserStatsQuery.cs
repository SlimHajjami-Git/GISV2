using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Admin.Users.Queries.GetAdminUserStats;

public record GetAdminUserStatsQuery() : IQuery<UserStatsDto>;
