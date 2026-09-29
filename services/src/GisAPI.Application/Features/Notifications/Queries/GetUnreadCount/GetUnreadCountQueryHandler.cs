using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using static GisAPI.Application.Common.Security.NotificationAudience;

namespace GisAPI.Application.Features.Notifications.Queries.GetUnreadCount;

public class GetUnreadCountQueryHandler : IRequestHandler<GetUnreadCountQuery, UnreadCountDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetUnreadCountQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<UnreadCountDto> Handle(GetUnreadCountQuery request, CancellationToken ct)
    {
        var userId = _tenantService.UserId
            ?? throw new GisAPI.Domain.Exceptions.DomainException("Utilisateur non identifié");

        // MÊME portée que la liste (GetNotificationsQueryHandler, 29/09/2026) : cette
        // requête alimente la pastille de la cloche. Sans le filtre, elle annoncerait des
        // notifications que la liste n'affiche pas — un compteur qui ne retombe jamais à
        // zéro et que le client ne peut pas vider, ce qui est pire que le défaut d'origine.
        var portee = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, ct);

        var siennes = _context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId && !n.IsRead);

        if (portee != null)
        {
            siennes = siennes.Where(n => n.ReferenceType != ReferenceVehicule
                                      || (n.ReferenceId != null && portee.Contains(n.ReferenceId.Value)));
        }

        var count = await siennes.CountAsync(ct);

        return new UnreadCountDto(count);
    }
}
