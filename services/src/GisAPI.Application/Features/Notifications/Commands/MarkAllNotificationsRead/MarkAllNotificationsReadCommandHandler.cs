using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Notifications.Commands.MarkAllNotificationsRead;

public class MarkAllNotificationsReadCommandHandler : IRequestHandler<MarkAllNotificationsReadCommand>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly INotificationService _notificationService;

    public MarkAllNotificationsReadCommandHandler(
        IGisDbContext context,
        ICurrentTenantService tenantService,
        INotificationService notificationService)
    {
        _context = context;
        _tenantService = tenantService;
        _notificationService = notificationService;
    }

    public async Task Handle(MarkAllNotificationsReadCommand request, CancellationToken ct)
    {
        var userId = _tenantService.UserId
            ?? throw new DomainException("Utilisateur non identifié");

        var now = DateTime.UtcNow;
        var unread = await _context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync(ct);

        foreach (var n in unread)
        {
            n.IsRead = true;
            n.ReadAt = now;
        }

        await _context.SaveChangesAsync(ct);
        await _notificationService.SendUnreadCountAsync(userId, 0);
    }
}
