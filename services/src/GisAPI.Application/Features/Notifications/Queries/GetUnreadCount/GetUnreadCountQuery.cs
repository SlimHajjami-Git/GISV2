using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Notifications.Queries.GetUnreadCount;

public record GetUnreadCountQuery() : IQuery<UnreadCountDto>;
