using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.AccidentEvents.Commands;

/// <summary>
/// Administrator click-through from the accident decision modal (or the
/// fallback button on <c>/rapport-accident/:id</c>). Confirms that the
/// detected event is a real accident, which:
///
/// <list type="number">
///   <item><description>Stamps <c>status = 'confirmed'</c> +
///     <c>decided_by_user_id</c> + <c>decided_at</c>.</description></item>
///   <item><description>Fans an audit notification
///     (&quot;Accident confirmé par X&quot;) to every other company admin so
///     they know the decision was taken and can stop seeing the modal.</description></item>
/// </list>
///
/// <para>Downstream: <c>AccidentTowMonitoringService</c> picks confirmed
/// events up on its next 5-minute cycle and starts watching the vehicle
/// for resumed motion (3 frames &gt; 5 km/h within 14 days → tow notif).</para>
///
/// <para>Idempotent: a second call on an already-decided event is a no-op
/// (returns <see cref="Unit.Value"/> without touching the row), so
/// concurrent clicks from two admins don't collide.</para>
///
/// <para>Tenant isolation: loads the row filtered by
/// <see cref="ICurrentTenantService.CompanyId"/> — confirming an accident
/// that belongs to another company yields
/// <see cref="NotFoundException"/> (surfaced as HTTP 404 by the controller).</para>
/// </summary>
public record ConfirmAccidentCommand(int AccidentEventId) : IRequest<Unit>;

public class ConfirmAccidentCommandHandler : IRequestHandler<ConfirmAccidentCommand, Unit>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<ConfirmAccidentCommandHandler> _logger;

    public ConfirmAccidentCommandHandler(
        IGisDbContext context,
        ICurrentTenantService tenantService,
        INotificationService notificationService,
        ILogger<ConfirmAccidentCommandHandler> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<Unit> Handle(ConfirmAccidentCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");
        var currentUserId = _tenantService.UserId
            ?? throw new DomainException("Utilisateur non identifié");

        // Tenant filter is applied globally on AccidentEvents, but we also
        // pin CompanyId explicitly so an admin of company A can never flip
        // a row from company B even if the filter is ever relaxed.
        var ev = await _context.AccidentEvents
            .FirstOrDefaultAsync(e => e.Id == request.AccidentEventId && e.CompanyId == companyId, ct)
            ?? throw new NotFoundException("AccidentEvent", request.AccidentEventId);

        // Idempotent — a second confirm (or a late confirm on a dismissed
        // event) is a no-op. Guard with a simple status check; no DB write.
        if (ev.Status != "pending")
        {
            _logger.LogInformation(
                "ConfirmAccidentCommand: accident {AccidentId} already decided as '{Status}' by user {DecidedBy} — no-op",
                ev.Id, ev.Status, ev.DecidedByUserId);
            return Unit.Value;
        }

        // Calypso 6 (P9): the workflow now has an intermediate state.
        // Confirming the modal moves the row to 'awaiting_details' (admin
        // said "yes it's a real accident" → PDF will be auto-generated and
        // attached, then the admin must fill the damages form to finalise).
        // The transition to 'confirmed' happens in UpdateAccidentDamagesCommand.
        ev.Status = "awaiting_details";
        ev.DecidedByUserId = currentUserId;
        ev.DecidedAt = DateTime.UtcNow;
        ev.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "ConfirmAccidentCommand: accident {AccidentId} moved to 'awaiting_details' by user {UserId} (company {CompanyId})",
            ev.Id, currentUserId, companyId);

        // Audit notification: tell the other admins the decision is done so
        // their local modal (still listening on SignalR) can stand down.
        await BroadcastAuditNotificationAsync(companyId, currentUserId, ev.Id, ev.VehicleLabel, ct);

        return Unit.Value;
    }

    private async Task BroadcastAuditNotificationAsync(
        int companyId,
        int decidedByUserId,
        int accidentEventId,
        string? vehicleLabel,
        CancellationToken ct)
    {
        // Fetch the actor's name for the notification body.
        var actor = await _context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == decidedByUserId, ct);
        var actorName = actor?.FullName ?? "un administrateur";

        // Fan-out to every OTHER active admin of the company (the decider
        // already knows they confirmed, no point notifying themselves).
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
        var title = "Accident confirmé";
        var message = $"{actorName} a confirmé l'accident sur le {label}.";
        var actionUrl = $"/rapport-accident/{accidentEventId}";

        var metadata = new Dictionary<string, object>
        {
            ["accidentEventId"] = accidentEventId,
            ["decidedByUserId"] = decidedByUserId,
            ["decision"] = "confirmed",
            // No requiresDecision flag — this is an audit echo, the modal
            // must NOT reopen.
        };

        foreach (var adminId in admins)
        {
            try
            {
                await _notificationService.CreateAndSendAsync(
                    companyId: companyId,
                    userId: adminId,
                    type: "accident_decision",
                    title: title,
                    message: message,
                    priority: "normal",
                    referenceType: "accident_event",
                    referenceId: accidentEventId,
                    actionUrl: actionUrl,
                    metadata: metadata,
                    ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "ConfirmAccidentCommand: failed to notify admin {AdminId} for accident {AccidentId}",
                    adminId, accidentEventId);
            }
        }
    }
}
