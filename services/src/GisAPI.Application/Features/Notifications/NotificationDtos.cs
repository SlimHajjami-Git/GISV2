namespace GisAPI.Application.Features.Notifications;

public record NotificationDto(
    long Id,
    int UserId,
    string Type,
    string Title,
    string Message,
    string Priority,
    string Channel,
    bool IsRead,
    DateTime? ReadAt,
    string? ReferenceType,
    int? ReferenceId,
    string? ActionUrl,
    Dictionary<string, object>? Metadata,
    DateTime CreatedAt
);

public record UnreadCountDto(int Count);
