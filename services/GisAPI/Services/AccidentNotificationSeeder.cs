using System.Text.Json;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
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

        // Upsert the persisted accident_events row. Idempotent: if a row
        // already exists for (vehicle, incident_at), we reuse its id instead
        // of creating a duplicate.
        var accidentEventId = await EnsureAccidentEventAsync(
            context, vehicle.CompanyId, vehicle.Id, device.Id, device.DeviceUid, vehicleLabel, ct);

        const string title = "Accident détecté sur votre véhicule";
        var message =
            $"Un événement compatible avec un accident grave a été détecté le 14 avril 2026 à 16h02 " +
            $"sur la commune de Jemmal (Monastir) sur le véhicule {vehicleLabel}. " +
            $"Cliquez pour consulter le rapport détaillé.";

        // The URL now points at the persisted accident_events row id. The
        // frontend reads :accidentId from the route and pulls the full report
        // from /api/accident-reports/{id}.
        var actionUrl = $"/rapport-accident/{accidentEventId}";

        var metadata = new Dictionary<string, object>
        {
            ["accidentEventId"] = accidentEventId,
            ["deviceUid"] = device.DeviceUid,
            ["gpsDeviceId"] = device.Id,
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
            "gps device {DeviceId} (uid={DeviceUid}), accident_event {AccidentEventId}, " +
            "{Created} created, {Skipped} skipped (already existed)",
            vehicle.CompanyId, vehicle.Id, vehicleLabel, device.Id, device.DeviceUid,
            accidentEventId, created, skipped);
    }

    /// <summary>
    /// Idempotently creates the <c>accident_events</c> row backing the report
    /// page, or returns the existing row's id. The pre-generated narrative
    /// (story / reasons / indicators / synthesis) is stored as JSON so the
    /// frontend no longer needs to hardcode it.
    /// </summary>
    private async Task<int> EnsureAccidentEventAsync(
        GisDbContext context,
        int companyId,
        int vehicleId,
        int gpsDeviceId,
        string deviceUid,
        string vehicleLabel,
        CancellationToken ct)
    {
        var incidentAt = DateTime.SpecifyKind(
            new DateTime(2026, 4, 14, 16, 2, 52), DateTimeKind.Utc);

        var existing = await context.AccidentEvents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                e => e.VehicleId == vehicleId && e.IncidentAt == incidentAt, ct);

        if (existing != null) return existing.Id;

        var story = new[]
        {
            new { time = "15h 58", title = "Conduite normale",
                body = "Le véhicule circule à 88 km/h sur une route secondaire de la région de Jemmal. Aucun comportement inhabituel n'est détecté dans les minutes précédentes. La conduite est régulière et stable.",
                severity = "normal" },
            new { time = "16h 01", title = "Légère décélération",
                body = "Le véhicule ralentit progressivement de 85 à 77 km/h en l'espace d'environ une minute. Rien d'anormal à ce stade.",
                severity = "normal" },
            new { time = "16h 02", title = "Chute brutale de la vitesse et choc violent",
                body = "La vitesse passe brutalement de 77 à 16 km/h en quelques secondes. Ce profil de ralentissement ne correspond pas à un freinage normal. Au même instant, les capteurs de mouvement du véhicule enregistrent un choc d'une violence exceptionnelle, dans toutes les directions simultanément.",
                severity = "critical" },
            new { time = "16h 03", title = "Second choc d'intensité équivalente",
                body = "Un second impact est enregistré, de même intensité que le premier. Le véhicule roule désormais à seulement 2 km/h.",
                severity = "critical" },
            new { time = "16h 04", title = "Arrêt complet du véhicule",
                body = "Le véhicule est totalement immobilisé. Plus aucun déplacement n'est détecté à partir de ce moment.",
                severity = "warning" },
            new { time = "16h 04 → 16h 07", title = "Position anormalement inclinée",
                body = "Pendant quatre minutes consécutives, les capteurs indiquent que le véhicule se trouve dans une position fortement inclinée. Une inclinaison de cette ampleur, maintenue à l'arrêt, est caractéristique d'un véhicule qui s'est retrouvé sur le flanc ou retourné.",
                severity = "warning" },
            new { time = "16h 08", title = "Coupure du contact",
                body = "Le contact du véhicule est coupé. Il est très probable qu'à ce moment, le conducteur, la protection civile ou un tiers soit déjà intervenu sur place.",
                severity = "neutral" },
            new { time = "17h 13", title = "Mouvement compatible avec un chargement sur dépanneuse",
                body = "Un mouvement spécifique est détecté sur le véhicule, compatible avec son chargement sur un plateau de dépanneuse. Après cet épisode, le véhicule reste immobile.",
                severity = "neutral" },
        };

        var reasons = new[]
        {
            new { title = "Une chute brutale et incontrôlée de la vitesse",
                text = "Le véhicule est passé de 88 à 0 km/h sur une durée très courte. Le profil de décélération ne correspond pas à un freinage volontaire mais à un arrêt subi." },
            new { title = "Un choc d'une violence exceptionnelle",
                text = "Les capteurs de mouvement ont enregistré une intensité plus de deux fois supérieure à tout ce qui avait été observé auparavant sur ce véhicule, toutes situations confondues." },
            new { title = "Une immobilisation totale immédiatement après",
                text = "À partir de 16h04, le véhicule est resté strictement à l'arrêt pendant plus de cinq minutes, sans reprise de mouvement, ce qui exclut un simple ralentissement ou un freinage normal." },
            new { title = "Une position fortement inclinée pendant quatre minutes",
                text = "Un véhicule qui repose normalement sur ses roues ne présente pas d'inclinaison soutenue. La valeur mesurée ici ne peut s'expliquer que par un véhicule qui s'est retrouvé couché sur le flanc ou retourné." },
        };

        var indicators = new[]
        {
            new { label = "Heure de l'impact", value = "16h 02 min 52 s", hint = "Heure locale de Tunis" },
            new { label = "Vitesse avant l'impact", value = "88 km/h", hint = "Mesurée 4 minutes avant" },
            new { label = "Vitesse au moment de l'impact", value = "16 km/h", hint = "Chute brutale et non-maîtrisée" },
            new { label = "Temps jusqu'à l'arrêt complet", value = "5 min 6 s", hint = "Entre 15h58 et 16h04" },
            new { label = "Durée d'inclinaison anormale", value = "4 minutes", hint = "Forte suspicion de retournement" },
            new { label = "Coupure du contact", value = "16h 08", hint = "Soit 6 minutes après l'impact" },
        };

        var jsonOpts = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        var ev = new AccidentEvent
        {
            CompanyId = companyId,
            VehicleId = vehicleId,
            GpsDeviceId = gpsDeviceId,
            DeviceUid = deviceUid,
            IncidentAt = incidentAt,
            Latitude = 35.61365,
            Longitude = 10.74298,
            ReferenceCode = $"2026-04-14-{vehicleLabel.Replace(' ', '-')}",
            VehicleLabel = vehicleLabel,
            LocationCommune = "Jemmal",
            LocationGovernorate = "Monastir",
            LocationRoadType = "Route secondaire interurbaine",
            SynthesisText = "choc violent ayant vraisemblablement entraîné un retournement du véhicule",
            Confidence = 97,
            StoryJson = JsonSerializer.Serialize(story, jsonOpts),
            ReasonsJson = JsonSerializer.Serialize(reasons, jsonOpts),
            IndicatorsJson = JsonSerializer.Serialize(indicators, jsonOpts),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        context.AccidentEvents.Add(ev);
        await context.SaveChangesAsync(ct);
        return ev.Id;
    }
}
