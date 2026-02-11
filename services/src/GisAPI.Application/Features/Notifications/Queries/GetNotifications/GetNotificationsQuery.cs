using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Notifications.Queries.GetNotifications;

public record GetNotificationsQuery(
    int? PageSize = 20,
    int? Page = 1,
    bool? UnreadOnly = false,
    string? Type = null
) : IQuery<NotificationPageDto>;

public record NotificationPageDto(
    List<NotificationDto> Items,
    int TotalCount,
    int UnreadCount,
    int Page,
    int PageSize
);
