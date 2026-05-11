using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Notifications.Events;

/// <summary>
/// Fans out a battery-health alert to every <b>company admin</b> of the
/// tenant that owns the failing device — same restriction as
/// <see cref="BatteryAlertNotificationHandler"/>: only admins can act on
/// it (drive to the garage, schedule a swap), conducteurs would just see
/// noise.
///
/// <para>The detector (<c>VoltageHealthMonitoringService</c>) already
/// stamps <c>GpsDevice.LastVoltageHealthAlertAt</c> before publishing,
/// so this handler doesn't dedupe — one publish = one fan-out.</para>
///
/// <para>The notification <c>type</c> is <c>battery_health</c> with a
/// signal-kind discriminator in metadata so the frontend can render an
/// icon variant per cause without duplicating the persistence path.</para>
/// </summary>
public class BatteryHealthAlertHandler : INotificationHandler<BatteryHealthAlertEvent>
{
    private readonly INotificationService _notificationService;
    private readonly IGisDbContext _context;
    private readonly ILogger<BatteryHealthAlertHandler> _logger;

    public BatteryHealthAlertHandler(
        INotificationService notificationService,
        IGisDbContext context,
        ILogger<BatteryHealthAlertHandler> logger)
    {
        _notificationService = notificationService;
        _context = context;
        _logger = logger;
    }

    public async Task Handle(BatteryHealthAlertEvent e, CancellationToken ct)
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
                    "BatteryHealth: no admin to notify for company {CompanyId} (device {DeviceId})",
                    e.CompanyId, e.DeviceId);
                return;
            }

            var vehicleLabel = string.IsNullOrWhiteSpace(e.VehicleName)
                ? $"Véhicule #{e.VehicleId}"
                : e.VehicleName!;

            var (title, message) = BuildCopy(e, vehicleLabel);

            var metadata = new Dictionary<string, object>
            {
                ["deviceId"] = e.DeviceId,
                ["signalKind"] = e.SignalKind,
                ["severity"] = e.Severity,
                ["detectedAt"] = e.DetectedAt.ToString("O"),
            };
            if (e.VoltageObservedV.HasValue) metadata["voltageObservedV"] = e.VoltageObservedV.Value;
            if (e.VoltageBaselineV.HasValue) metadata["voltageBaselineV"] = e.VoltageBaselineV.Value;

            foreach (var adminId in admins)
            {
                try
                {
                    await _notificationService.CreateAndSendAsync(
                        companyId: e.CompanyId,
                        userId: adminId,
                        type: "battery_health",
                        title: title,
                        message: message,
                        priority: e.Severity == "critical" ? "high" : "normal",
                        referenceType: "vehicle",
                        referenceId: e.VehicleId,
                        actionUrl: "/vehicules",
                        metadata: metadata,
                        ct: ct);
                }
                catch (Exception exUser)
                {
                    // Per-admin failures must not break the fan-out — the
                    // 48h cooldown is already stamped by the caller, we
                    // don't want a single bad token to deny the rest of
                    // the team.
                    _logger.LogWarning(exUser,
                        "BatteryHealth: failed to push notification to user {UserId}", adminId);
                }
            }

            _logger.LogInformation(
                "BatteryHealth: fanned out {Signal} ({Severity}) to {Count} admin(s) for {Vehicle}",
                e.SignalKind, e.Severity, admins.Count, vehicleLabel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch BatteryHealth notification");
        }
    }

    /// <summary>
    /// Per-signal user-facing wording. Kept here (and not in the detector)
    /// so the detection service stays focused on numeric thresholds and
    /// the i18n lives next to the notification path.
    /// </summary>
    private static (string Title, string Message) BuildCopy(BatteryHealthAlertEvent e, string vehicleLabel)
    {
        var observed = e.VoltageObservedV.HasValue ? $"{e.VoltageObservedV.Value:F1} V" : "—";
        var baseline = e.VoltageBaselineV.HasValue ? $"{e.VoltageBaselineV.Value:F1} V" : "—";

        return e.SignalKind switch
        {
            "battery_dead" =>
                ($"Batterie morte — {vehicleLabel}",
                 $"La batterie du {vehicleLabel} chute régulièrement sous 11.9 V au repos " +
                 $"(médiane {observed}). À ce niveau, la batterie ne tient plus la charge et le " +
                 "démarrage à froid est compromis — remplacement à prévoir rapidement."),

            _ =>
                ($"Anomalie batterie — {vehicleLabel}",
                 $"Anomalie de tension détectée sur le {vehicleLabel} (signal: {e.SignalKind}, valeur " +
                 $"observée: {observed}).")
        };
    }
}
