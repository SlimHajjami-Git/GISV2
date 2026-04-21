using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.AccidentEvents.Commands;

/// <summary>
/// Administrator click-through from the &quot;Fausse alerte&quot; button on the
/// accident decision modal (or the fallback button on
/// <c>/rapport-accident/:id</c>). Marks the detected event as a false
/// positive, which:
///
/// <list type="number">
///   <item><description>Stamps <c>status = 'dismissed'</c> +
///     <c>decided_by_user_id</c> + <c>decided_at</c>.</description></item>
///   <item><description>Broadcasts a &quot;Choc violent détecté — dégâts
///     possibles&quot; notification (<c>type = accident_possible_damage</c>)
///     to every other company admin so they're aware the event was
///     rejected but a hard impact still occurred — drivers are NOT
///     notified and no email is sent, per UX decision.</description></item>
/// </list>
///
/// <para>No tow monitoring is started (the event is deemed benign).</para>
///
/// <para>Idempotent: a second call on an already-decided event is a no-op.
/// Tenant-scoped via <see cref="ICurrentTenantService"/> — a cross-tenant
/// attempt yields <see cref="NotFoundException"/> (HTTP 404).</para>
/// </summary>
public record DismissAccidentCommand(int AccidentEventId) : IRequest<Unit>;

public class DismissAccidentCommandHandler : IRequestHandler<DismissAccidentCommand, Unit>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<DismissAccidentCommandHandler> _logger;

    public DismissAccidentCommandHandler(
        IGisDbContext context,
        ICurrentTenantService tenantService,
        INotificationService notificationService,
        ILogger<DismissAccidentCommandHandler> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<Unit> Handle(DismissAccidentCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");
        var currentUserId = _tenantService.UserId
            ?? throw new DomainException("Utilisateur non identifié");

        var ev = await _context.AccidentEvents
            .FirstOrDefaultAsync(e => e.Id == request.AccidentEventId && e.CompanyId == companyId, ct)
            ?? throw new NotFoundException("AccidentEvent", request.AccidentEventId);

        // Idempotent — already-decided events are never flipped back.
        if (ev.Status != "pending")
        {
            _logger.LogInformation(
                "DismissAccidentCommand: accident {AccidentId} already decided as '{Status}' — no-op",
                ev.Id, ev.Status);
            return Unit.Value;
        }

        ev.Status = "dismissed";
        ev.DecidedByUserId = currentUserId;
        ev.DecidedAt = DateTime.UtcNow;
        ev.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "DismissAccidentCommand: accident {AccidentId} dismissed by user {UserId} (company {CompanyId})",
            ev.Id, currentUserId, companyId);

        await BroadcastPossibleDamageAsync(companyId, currentUserId, ev.Id, ev.VehicleLabel, ev.IncidentAt, ct);

        return Unit.Value;
    }

    private async Task BroadcastPossibleDamageAsync(
        int companyId,
        int decidedByUserId,
        int accidentEventId,
        string? vehicleLabel,
        DateTime incidentAt,
        CancellationToken ct)
    {
        // Admin-only fan-out: drivers stay unaware of false positives to
        // avoid panic pings. The decider is skipped too (they just clicked
        // Fausse alerte, they don't need a reminder).
        var admins = await _context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(u => u.Role)
            .Where(u => u.CompanyId == companyId
                     && u.Id != decidedByUserId
                     && u.Status == "active"
                     && u.Role != null
                     && u.Role.IsCompanyAdmin)
            .Select(u => u.Id)
            .ToListAsync(ct);

        if (admins.Count == 0) return;

        var label = string.IsNullOrWhiteSpace(vehicleLabel) ? "véhicule" : vehicleLabel;
        var localTime = incidentAt.ToString("dd/MM/yyyy 'à' HH'h'mm");
        const string title = "Choc violent détecté — dégâts possibles";
        var message =
            $"Un choc violent a été détecté le {localTime} UTC sur le {label}. " +
            "L'événement a été classé comme fausse alerte par un administrateur, " +
            "mais des dégâts peuvent subsister — pensez à vérifier le véhicule.";
        var actionUrl = $"/rapport-accident/{accidentEventId}";

        var metadata = new Dictionary<string, object>
        {
            ["accidentEventId"] = accidentEventId,
            ["decidedByUserId"] = decidedByUserId,
            ["decision"] = "dismissed",
            // No requiresDecision flag — this is a post-decision echo, not
            // a new modal trigger.
        };

        foreach (var adminId in admins)
        {
            try
            {
                await _notificationService.CreateAndSendAsync(
                    companyId: companyId,
                    userId: adminId,
                    type: "accident_possible_damage",
                    title: title,
                    message: message,
                    priority: "high",
                    referenceType: "accident_event",
                    referenceId: accidentEventId,
                    actionUrl: actionUrl,
                    metadata: metadata,
                    ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "DismissAccidentCommand: failed to notify admin {AdminId} for accident {AccidentId}",
                    adminId, accidentEventId);
            }
        }
    }
}
