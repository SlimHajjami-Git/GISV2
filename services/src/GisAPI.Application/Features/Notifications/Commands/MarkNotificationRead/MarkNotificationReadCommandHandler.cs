using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Notifications.Commands.MarkNotificationRead;

public class MarkNotificationReadCommandHandler : IRequestHandler<MarkNotificationReadCommand>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly INotificationService _notificationService;

    public MarkNotificationReadCommandHandler(
        IGisDbContext context,
        ICurrentTenantService tenantService,
        INotificationService notificationService)
    {
        _context = context;
        _tenantService = tenantService;
        _notificationService = notificationService;
    }

    public async Task Handle(MarkNotificationReadCommand request, CancellationToken ct)
    {
        var userId = _tenantService.UserId
            ?? throw new DomainException("Utilisateur non identifié");

        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.Id == request.Id && n.UserId == userId, ct)
            ?? throw new NotFoundException("Notification", request.Id);

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            // Push updated unread count
            var unreadCount = await _context.Notifications
                .CountAsync(n => n.UserId == userId && !n.IsRead, ct);
            await _notificationService.SendUnreadCountAsync(userId, unreadCount);
        }
    }
}
