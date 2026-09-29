using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using static GisAPI.Application.Common.Security.NotificationAudience;

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

        // PORTÉE VÉHICULE À LA LECTURE (29/09/2026). Les producteurs ont été cloisonnés
        // le 16/09 (NotificationAudience) et n'adressent plus une seule ligne hors
        // périmètre — vérifié sur la production : le compte de Kap Pharma n'a rien reçu
        // depuis. Mais les lignes écrites AVANT lui restent adressées, et sa cloche
        // affichait encore 128 alertes de véhicules d'autres locataires de HERTZ ; pour
        // carthage@hertz.tn il y en a 10 353, dont 1 285 non lues. Filtrer ICI les rend
        // invisibles sans effacer une seule ligne, ce qui est réversible, immédiat, et
        // protège en plus d'un futur producteur qui oublierait la règle.
        //
        // Seules les notifications RATTACHÉES À UN VÉHICULE sont filtrées. Celles qui n'en
        // désignent aucun (compte, abonnement, tournée) ne relèvent pas de cette portée et
        // disparaîtraient à tort. Les alertes de géofence portent bien un véhicule, mais
        // dans leur jsonb Metadata, non traduisible en SQL de façon fiable : elles restent
        // visibles, et c'est dit franchement plutôt que masqué.
        var portee = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, ct);

        var siennes = _context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId);

        if (portee != null)
        {
            siennes = siennes.Where(n => n.ReferenceType != ReferenceVehicule
                                      || (n.ReferenceId != null && portee.Contains(n.ReferenceId.Value)));
        }

        var query = siennes;

        if (request.UnreadOnly == true)
            query = query.Where(n => !n.IsRead);

        if (!string.IsNullOrEmpty(request.Type))
            query = query.Where(n => n.Type == request.Type);

        var totalCount = await query.CountAsync(ct);

        // Le compte des non-lues part de la MÊME base filtrée : sinon la pastille
        // annoncerait des notifications que la liste n'affiche pas, et le client
        // chercherait indéfiniment des messages invisibles.
        var unreadCount = await siennes
            .Where(n => !n.IsRead)
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
