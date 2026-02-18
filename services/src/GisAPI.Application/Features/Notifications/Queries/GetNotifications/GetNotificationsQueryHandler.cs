using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Notifications.Queries.GetNotifications;

public class GetNotificationsQueryHandler : IRequestHandler<GetNotificationsQuery, NotificationPageDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetNotificationsQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<NotificationPageDto> Handle(GetNotificationsQuery request, CancellationToken ct)
    {
        var userId = _tenantService.UserId
            ?? throw new GisAPI.Domain.Exceptions.DomainException("Utilisateur non identifié");

        var query = _context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .AsQueryable();

        if (request.UnreadOnly == true)
            query = query.Where(n => !n.IsRead);

        if (!string.IsNullOrEmpty(request.Type))
            query = query.Where(n => n.Type == request.Type);

        var totalCount = await query.CountAsync(ct);
        var unreadCount = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId && !n.IsRead)
            .CountAsync(ct);

        var page = Math.Max(1, request.Page ?? 1);
        var pageSize = Math.Clamp(request.PageSize ?? 20, 1, 100);

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new NotificationDto(
                n.Id,
                n.UserId,
                n.Type,
                n.Title,
                n.Message,
                n.Priority,
                n.Channel,
                n.IsRead,
                n.ReadAt,
                n.ReferenceType,
                n.ReferenceId,
                n.ActionUrl,
                n.Metadata,
                n.CreatedAt
            ))
            .ToListAsync(ct);

        return new NotificationPageDto(items, totalCount, unreadCount, page, pageSize);
    }
}
