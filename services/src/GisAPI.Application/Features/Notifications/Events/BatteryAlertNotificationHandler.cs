using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Notifications.Events;

/// <summary>
/// Fans out a low-battery alert to every <b>company admin</b> of the
/// tenant that owns the failing device — same pattern as
/// <see cref="PowerCutNotificationHandler"/>, but restricted to admins
/// because only they can act on it (drive the vehicle to the garage,
/// schedule a battery swap, …). Conducteurs would just see noise.
///
/// <para>The detector (<c>BatteryMonitoringService</c>) already enforces
/// the 24h cooldown through <c>GpsDevice.LastBatteryAlertAt</c>, so this
/// handler can fire without any re-deduplication logic.</para>
/// </summary>
public class BatteryAlertNotificationHandler : INotificationHandler<BatteryAlertNotificationEvent>
{
    private readonly INotificationService _notificationService;
    private readonly IGisDbContext _context;
    private readonly ILogger<BatteryAlertNotificationHandler> _logger;

    public BatteryAlertNotificationHandler(
        INotificationService notificationService,
        IGisDbContext context,
        ILogger<BatteryAlertNotificationHandler> logger)
    {
        _notificationService = notificationService;
        _context = context;
        _logger = logger;
    }

    public async Task Handle(BatteryAlertNotificationEvent e, CancellationToken ct)
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
                    "BatteryAlert: no admin to notify for company {CompanyId} (device {DeviceId})",
                    e.CompanyId, e.DeviceId);
                return;
            }

            var vehicleLabel = string.IsNullOrWhiteSpace(e.VehicleName)
                ? $"Véhicule #{e.VehicleId}"
                : e.VehicleName!;

            var title = $"Batterie faible — {vehicleLabel}";
            var message =
                $"La tension batterie du {vehicleLabel} est anormalement basse " +
                $"(valeur brute {e.VoltageRaw}). Pensez à vérifier / remplacer la batterie " +
                "avant qu'elle ne lâche complètement.";

            var metadata = new Dictionary<string, object>
            {
                ["deviceId"] = e.DeviceId,
                ["voltageRaw"] = e.VoltageRaw,
                ["detectedAt"] = e.DetectedAt.ToString("O"),
            };

            foreach (var adminId in admins)
            {
                try
                {
                    await _notificationService.CreateAndSendAsync(
                        companyId: e.CompanyId,
                        userId: adminId,
                        type: "low_voltage",
                        title: title,
                        message: message,
                        priority: "normal",
                        referenceType: "vehicle",
                        referenceId: e.VehicleId,
                        actionUrl: "/vehicules",
                        metadata: metadata,
                        ct: ct);
                }
                catch (Exception exUser)
                {
                    // Per-admin failures do not break the fan-out — the
                    // 24h cooldown is already stamped by the caller, we
                    // don't want one bad token/SignalR connection to
                    // deny the rest of the team.
                    _logger.LogWarning(exUser,
                        "BatteryAlert: failed to push notification to user {UserId}", adminId);
                }
            }

            _logger.LogInformation(
                "BatteryAlert: fanned out low-voltage notification to {Count} admin(s) for {Vehicle} (raw={Raw})",
                admins.Count, vehicleLabel, e.VoltageRaw);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch BatteryAlert notification");
        }
    }
}
