using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Notifications.Events;

/// <summary>
/// Fans out a <see cref="DocumentExpiryNotificationEvent"/> to every
/// company admin of the tenant. Same admin-only restriction as
/// <see cref="BatteryAlertNotificationHandler"/>: only admins can act on
/// renewing the document — chauffeurs would just see noise.
///
/// <para>The detection loop in <c>DocumentExpiryMonitoringService</c>
/// already enforces a "one notification per (vehicle, docType, expiryDate)"
/// dedup, so this handler can fire without re-deduplication.</para>
/// </summary>
public class DocumentExpiryNotificationHandler : INotificationHandler<DocumentExpiryNotificationEvent>
{
    private readonly INotificationService _notificationService;
    private readonly IGisDbContext _context;
    private readonly ILogger<DocumentExpiryNotificationHandler> _logger;

    public DocumentExpiryNotificationHandler(
        INotificationService notificationService,
        IGisDbContext context,
        ILogger<DocumentExpiryNotificationHandler> logger)
    {
        _notificationService = notificationService;
        _context = context;
        _logger = logger;
    }

    public async Task Handle(DocumentExpiryNotificationEvent e, CancellationToken ct)
    {
        try
        {
            var admins = await _context.Users
                .Include(u => u.Role)
                .Where(u => u.CompanyId == e.CompanyId
                         && u.Status == "active"
                         && u.Role != null
                         && u.Role.IsCompanyAdmin)
                .Select(u => u.Id)
                .ToListAsync(ct);

            if (admins.Count == 0)
            {
                _logger.LogDebug(
                    "DocumentExpiry: no admin to notify for company {CompanyId} (vehicle {VehicleId}, {DocType})",
                    e.CompanyId, e.VehicleId, e.DocumentType);
                return;
            }

            var (title, message, priority) = BuildContent(e);

            var metadata = new Dictionary<string, object>
            {
                ["vehicleId"] = e.VehicleId,
                ["vehicleLabel"] = e.VehicleLabel,
                ["documentType"] = e.DocumentType,
                ["expiryDate"] = e.ExpiryDate.ToString("yyyy-MM-dd"),
                ["daysRemaining"] = e.DaysRemaining,
            };

            foreach (var adminId in admins)
            {
                try
                {
                    await _notificationService.CreateAndSendAsync(
                        companyId: e.CompanyId,
                        userId: adminId,
                        type: "document_expiry",
                        title: title,
                        message: message,
                        priority: priority,
                        referenceType: "vehicle",
                        referenceId: e.VehicleId,
                        actionUrl: "/documents",
                        metadata: metadata,
                        ct: ct);
                }
                catch (Exception exUser)
                {
                    _logger.LogWarning(exUser,
                        "DocumentExpiry: failed to push notification to user {UserId}", adminId);
                }
            }

            _logger.LogInformation(
                "DocumentExpiry: fanned out {DocType} reminder to {Count} admin(s) for {Vehicle} (expiry={Expiry:yyyy-MM-dd}, daysRemaining={Days})",
                e.DocumentTypeLabel, admins.Count, e.VehicleLabel, e.ExpiryDate, e.DaysRemaining);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch DocumentExpiry notification");
        }
    }

    private static (string Title, string Message, string Priority) BuildContent(
        DocumentExpiryNotificationEvent e)
    {
        // Tone scales with urgency: already expired = high (red bell),
        // expiring within a week = elevated, otherwise = normal reminder.
        if (e.DaysRemaining < 0)
        {
            var daysOver = -e.DaysRemaining;
            return (
                $"{e.DocumentTypeLabel} expirée — {e.VehicleLabel}",
                $"L'{e.DocumentTypeLabel.ToLowerInvariant()} du {e.VehicleLabel} a expiré il y a "
                + $"{daysOver} jour{(daysOver > 1 ? "s" : "")} (échéance: {e.ExpiryDate:dd/MM/yyyy}). "
                + "Renouvelez le document dès que possible.",
                "high"
            );
        }

        if (e.DaysRemaining == 0)
        {
            return (
                $"{e.DocumentTypeLabel} expire aujourd'hui — {e.VehicleLabel}",
                $"L'{e.DocumentTypeLabel.ToLowerInvariant()} du {e.VehicleLabel} expire aujourd'hui "
                + $"({e.ExpiryDate:dd/MM/yyyy}). Pensez à le renouveler.",
                "high"
            );
        }

        var priority = e.DaysRemaining <= 7 ? "elevated" : "normal";
        return (
            $"{e.DocumentTypeLabel} à renouveler — {e.VehicleLabel}",
            $"L'{e.DocumentTypeLabel.ToLowerInvariant()} du {e.VehicleLabel} expire dans "
            + $"{e.DaysRemaining} jour{(e.DaysRemaining > 1 ? "s" : "")} "
            + $"({e.ExpiryDate:dd/MM/yyyy}).",
            priority
        );
    }
}
