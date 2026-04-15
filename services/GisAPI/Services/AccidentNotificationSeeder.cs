using GisAPI.Application.Common.Interfaces;
using GisAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// One-shot background task that seeds the in-app notification for the
/// 2026-04-14 accident on vehicle 118013. Runs once, shortly after API boot,
/// and is idempotent — it only creates a notification for a user if none
/// already exists for (user, type='accident_detected', referenceId=vehicle).
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

    private const string TargetDeviceUid = "118013";
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
            _logger.LogError(ex, "AccidentNotificationSeeder: failed to seed accident notification for device {DeviceUid}", TargetDeviceUid);
        }
    }

    private async Task SeedAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GisDbContext>();
        var notifService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        // Locate the vehicle via its GPS device UID. IgnoreQueryFilters bypasses
        // the tenant scope because the seeder runs outside any HTTP request.
        var vehicle = await context.Vehicles
            .IgnoreQueryFilters()
            .Include(v => v.GpsDevice)
            .FirstOrDefaultAsync(v => v.GpsDevice != null && v.GpsDevice.DeviceUid == TargetDeviceUid, ct);

        if (vehicle == null)
        {
            _logger.LogInformation(
                "AccidentNotificationSeeder: no vehicle with GPS device_uid={DeviceUid} found — skipping",
                TargetDeviceUid);
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
            : (!string.IsNullOrWhiteSpace(vehicle.Name) ? vehicle.Name : TargetDeviceUid);

        const string title = "Accident détecté sur votre véhicule";
        var message =
            $"Un événement compatible avec un accident grave a été détecté le 14 avril 2026 à 16h02 " +
            $"sur la commune de Jemmal (Monastir) sur le véhicule {vehicleLabel}. " +
            $"Cliquez pour consulter le rapport détaillé.";

        var actionUrl = $"/rapport-accident/{TargetDeviceUid}";

        var metadata = new Dictionary<string, object>
        {
            ["deviceUid"] = TargetDeviceUid,
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
            "{Created} created, {Skipped} skipped (already existed)",
            vehicle.CompanyId, vehicle.Id, vehicleLabel, created, skipped);
    }
}
