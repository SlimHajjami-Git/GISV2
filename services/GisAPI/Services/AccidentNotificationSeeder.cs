using GisAPI.Application.Common.Interfaces;
using GisAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// One-shot background task that seeds the in-app notification for the
/// 2026-04-14 accident on the vehicle whose GPS device has <c>id = 118013</c>
/// (real <c>device_uid = 860141076674283</c>, company 4 on prod). Runs once,
/// shortly after API boot, and is idempotent — it only creates a notification
/// for a user if none already exists for
/// (user, type='accident_detected', referenceId=vehicle).
///
/// This exists because the notification for the real accident was never
/// pushed at the time (no server-side detection was in place). The client
/// asked to see the analysis on their next login, so we retroactively fill
/// the bell dropdown for every user of the affected company.
///
/// The seeder routes through <see cref="INotificationService.CreateAndSendAsync"/>
/// so SignalR (web) and FCM (mobile) pushes both fire automatically — if a
/// user is already connected when the API boots, their bell updates live.
///
/// Remove this file (and its registration in <c>Program.cs</c>) once the full
/// server-side accident detection pipeline ships.
/// </summary>
public class AccidentNotificationSeeder : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AccidentNotificationSeeder> _logger;

    /// <summary>
    /// Primary key of the GPS device row that carried the accident.
    /// This is the integer <c>gps_devices.id</c>, not the <c>device_uid</c>.
    /// The client is used to seeing "118013" in their admin UI because it's
    /// the internal row id.
    /// </summary>
    private const int TargetGpsDeviceId = 118013;

    /// <summary>
    /// User-facing label shown in the notification body and on the report
    /// header. Kept separate from the DB lookup so it stays stable even if
    /// the underlying <c>plate_number</c> / <c>name</c> changes.
    /// </summary>
    private const string DisplayLabel = "118013";

    private const string NotificationType = "accident_detected";

    public AccidentNotificationSeeder(
        IServiceProvider serviceProvider,
        ILogger<AccidentNotificationSeeder> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Let the API finish booting and the DB become reachable.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), ct);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        try
        {
            await SeedAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AccidentNotificationSeeder: failed to seed accident notification for gps device id {DeviceId}", TargetGpsDeviceId);
        }
    }

    private async Task SeedAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GisDbContext>();
        var notifService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        // Locate the GPS device by primary key first so we can carry its real
        // device_uid into the notification metadata (the front-end uses it to
        // fetch the real GPS history for the dynamic report).
        // IgnoreQueryFilters bypasses the tenant scope because the seeder
        // runs outside any HTTP request.
        var device = await context.GpsDevices
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == TargetGpsDeviceId, ct);

        if (device == null)
        {
            _logger.LogInformation(
                "AccidentNotificationSeeder: no GPS device with id={DeviceId} found — skipping",
                TargetGpsDeviceId);
            return;
        }

        var vehicle = await context.Vehicles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.GpsDeviceId == device.Id, ct);

        if (vehicle == null)
        {
            _logger.LogInformation(
                "AccidentNotificationSeeder: GPS device {DeviceId} ({DeviceUid}) has no vehicle attached — skipping",
                device.Id, device.DeviceUid);
            return;
        }

        var users = await context.Users
            .IgnoreQueryFilters()
            .Where(u => u.CompanyId == vehicle.CompanyId && u.Status == "active")
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(ct);

        if (users.Count == 0)
        {
            _logger.LogInformation(
                "AccidentNotificationSeeder: no active users in company {CompanyId} — skipping",
                vehicle.CompanyId);
            return;
        }

        var vehicleLabel = !string.IsNullOrWhiteSpace(vehicle.Plate)
            ? vehicle.Plate!
            : (!string.IsNullOrWhiteSpace(vehicle.Name) ? vehicle.Name : DisplayLabel);

        const string title = "Accident détecté sur votre véhicule";
        var message =
            $"Un événement compatible avec un accident grave a été détecté le 14 avril 2026 à 16h02 " +
            $"sur la commune de Jemmal (Monastir) sur le véhicule {vehicleLabel}. " +
            $"Cliquez pour consulter le rapport détaillé.";

        // The URL uses the display label (same as before) — the component
        // reads the :deviceId param for display only and falls back to
        // the real device_uid baked in the component for GPS history fetch.
        var actionUrl = $"/rapport-accident/{DisplayLabel}";

        var metadata = new Dictionary<string, object>
        {
            ["deviceUid"] = device.DeviceUid,     // real uid for history fetch
            ["gpsDeviceId"] = device.Id,          // DB primary key (= 118013)
            ["vehicleId"] = vehicle.Id,
            ["vehicleLabel"] = vehicleLabel,
            ["incidentAt"] = "2026-04-14T16:02:52+01:00",
            ["locationLabel"] = "Jemmal, Monastir",
            ["latitude"] = 35.61365,
            ["longitude"] = 10.74298,
            ["confidence"] = 97
        };

        int created = 0;
        int skipped = 0;

        foreach (var user in users)
        {
            var alreadyExists = await context.Notifications
                .IgnoreQueryFilters()
                .AnyAsync(n =>
                    n.UserId == user.Id &&
                    n.Type == NotificationType &&
                    n.ReferenceType == "vehicle" &&
                    n.ReferenceId == vehicle.Id,
                    ct);

            if (alreadyExists)
            {
                skipped++;
                continue;
            }

            try
            {
                await notifService.CreateAndSendAsync(
                    companyId: vehicle.CompanyId,
                    userId: user.Id,
                    type: NotificationType,
                    title: title,
                    message: message,
                    priority: "critical",
                    referenceType: "vehicle",
                    referenceId: vehicle.Id,
                    actionUrl: actionUrl,
                    metadata: metadata,
                    ct: ct);

                created++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "AccidentNotificationSeeder: failed to create notification for user {UserId} ({Email})",
                    user.Id, user.Email);
            }
        }

        _logger.LogInformation(
            "AccidentNotificationSeeder: done — company {CompanyId}, vehicle {VehicleId} ({Plate}), " +
            "gps device {DeviceId} (uid={DeviceUid}), {Created} created, {Skipped} skipped (already existed)",
            vehicle.CompanyId, vehicle.Id, vehicleLabel, device.Id, device.DeviceUid, created, skipped);
    }
}
