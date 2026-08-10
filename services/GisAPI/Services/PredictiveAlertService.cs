using GisAPI.Application.Common.Interfaces;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

public class PredictiveAlertService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PredictiveAlertService> _logger;
    private const int CHECK_INTERVAL_HOURS = 6;

    public PredictiveAlertService(IServiceProvider serviceProvider, ILogger<PredictiveAlertService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Wait for the app to fully start before running checks
        await Task.Delay(TimeSpan.FromMinutes(2), ct);
        _logger.LogInformation("PredictiveAlertService started");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunPredictiveChecks(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PredictiveAlertService");
            }

            await Task.Delay(TimeSpan.FromHours(CHECK_INTERVAL_HOURS), ct);
        }
    }

    private async Task RunPredictiveChecks(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GisDbContext>();
        var notifService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var alertDispatcher = scope.ServiceProvider.GetRequiredService<IAlertEmailDispatcher>();

        var companies = await context.Societes
            .AsNoTracking()
            .Select(s => s.Id)
            .ToListAsync(ct);

        foreach (var companyId in companies)
        {
            var vehicles = await context.Vehicles
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(v => v.CompanyId == companyId)
                .ToListAsync(ct);

            foreach (var vehicle in vehicles)
            {
                await CheckDocumentExpiry(context, notifService, alertDispatcher, vehicle, companyId, ct);
                await CheckMaintenanceDue(context, notifService, alertDispatcher, vehicle, companyId, ct);
                await CheckFuelAnomaly(context, notifService, vehicle, companyId, ct);
            }

            // Driver permit expiries — not attached to any vehicle, so run once per company.
            await CheckDriverPermitExpiry(context, notifService, companyId, ct);
        }

        _logger.LogInformation("PredictiveAlertService completed check for {Count} companies", companies.Count);
    }

    /// <summary>
    /// Maps a document name (as displayed in the UI) to the <c>alert_emails.alert_type</c>
    /// key used by <see cref="IAlertEmailDispatcher"/>. Documents not mapped here
    /// (Carte grise, Permis de transport) fall through to the admin-only notification.
    /// </summary>
    private static string? MapDocumentNameToAlertType(string name) => name switch
    {
        "Assurance" => "assurance",
        "Vignette" => "taxe_circulation",
        "Contrôle technique" => "visite_technique",
        _ => null
    };

    private async Task CheckDocumentExpiry(GisDbContext context, INotificationService notifService,
        IAlertEmailDispatcher alertDispatcher, Vehicle vehicle, int companyId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var in15Days = now.AddDays(15);
        var in30Days = now.AddDays(30);

        var docs = new List<(string Name, DateTime? Expiry)>
        {
            ("Assurance", vehicle.InsuranceExpiry),
            ("Contrôle technique", vehicle.TechnicalInspectionExpiry),
            ("Vignette", vehicle.TaxExpiry),
            ("Carte grise", vehicle.RegistrationExpiry),
            ("Permis de transport", vehicle.TransportPermitExpiry)
        };

        foreach (var (name, expiry) in docs)
        {
            if (!expiry.HasValue) continue;

            string? message = null;
            string priority = "normal";

            if (expiry.Value < now)
            {
                message = $"{name} expiré(e) pour {vehicle.Name} ({vehicle.Plate}) depuis le {expiry.Value:dd/MM/yyyy}";
                priority = "urgent";
            }
            else if (expiry.Value < in15Days)
            {
                var days = (int)(expiry.Value - now).TotalDays;
                message = $"{name} expire dans {days} jour(s) pour {vehicle.Name} ({vehicle.Plate})";
                priority = "urgent";
            }
            else if (expiry.Value < in30Days)
            {
                var days = (int)(expiry.Value - now).TotalDays;
                message = $"{name} expire dans {days} jour(s) pour {vehicle.Name} ({vehicle.Plate})";
                priority = "normal";
            }

            if (message != null)
            {
                var alreadySent = await HasRecentNotification(context, companyId, "document_expiry",
                    vehicle.Id, name, ct);
                if (!alreadySent)
                {
                    var title = $"Document: {name}";

                    // In-app notification for company admins (unchanged)
                    await SendToCompanyAdmins(context, notifService, companyId,
                        "document_expiry", title, message,
                        priority, "vehicle", vehicle.Id, "/echeances", ct);

                    // Email fan-out to configured alert_emails recipients
                    // (Assurance / Vignette / Contrôle technique only — other docs
                    // don't have a dedicated alert_emails slot and stay admin-only)
                    var alertType = MapDocumentNameToAlertType(name);
                    if (alertType != null)
                    {
                        try
                        {
                            await alertDispatcher.DispatchAsync(
                                companyId, alertType, title, message, "/echeances", ct);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex,
                                "Failed to dispatch alert email for company {CompanyId} type {AlertType}",
                                companyId, alertType);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Notifies company admins when a driver's driving-licence (permis conducteur)
    /// is expired, or expires within 15 / 30 days. Admin-only in-app notification —
    /// there's no dedicated <c>alert_emails</c> type for this yet.
    /// </summary>
    private async Task CheckDriverPermitExpiry(GisDbContext context, INotificationService notifService,
        int companyId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var in15Days = now.AddDays(15);
        var in30Days = now.AddDays(30);

        var drivers = await context.Drivers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(d => d.CompanyId == companyId
                     && d.PermitExpiry != null
                     && d.Status == "active")
            .ToListAsync(ct);

        foreach (var driver in drivers)
        {
            var expiry = driver.PermitExpiry!.Value;
            string? message = null;
            string priority = "normal";
            var fullName = $"{driver.FirstName} {driver.LastName}".Trim();

            if (expiry < now)
            {
                message = $"Permis conducteur expiré pour {fullName} depuis le {expiry:dd/MM/yyyy}";
                priority = "urgent";
            }
            else if (expiry < in15Days)
            {
                var days = Math.Max(0, (int)(expiry - now).TotalDays);
                message = $"Permis conducteur expire dans {days} jour(s) pour {fullName}";
                priority = "urgent";
            }
            else if (expiry < in30Days)
            {
                var days = (int)(expiry - now).TotalDays;
                message = $"Permis conducteur expire dans {days} jour(s) pour {fullName}";
                priority = "normal";
            }

            if (message == null) continue;

            // Dedup on (companyId, type, driver.Id) — same pattern as vehicle docs but keyed
            // on driver so two drivers in the same company don't race-condition each other.
            var alreadySent = await HasRecentNotification(context, companyId,
                "driver_permit_expiry", driver.Id, "driver_permit", ct);
            if (alreadySent) continue;

            await SendToCompanyAdmins(context, notifService, companyId,
                "driver_permit_expiry",
                $"Permis conducteur: {fullName}",
                message,
                priority, "driver", driver.Id, "/documents?type=driver_permit", ct);
        }
    }

    private async Task CheckMaintenanceDue(GisDbContext context, INotificationService notifService,
        IAlertEmailDispatcher alertDispatcher, Vehicle vehicle, int companyId, CancellationToken ct)
    {
        var schedules = await context.VehicleMaintenanceSchedules
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(s => s.Template)
            .Where(s => s.VehicleId == vehicle.Id && (s.Status == "due" || s.Status == "overdue" || s.Status == "critical"))
            .ToListAsync(ct);

        foreach (var schedule in schedules)
        {
            var templateName = schedule.Template?.Name ?? "Entretien";
            string message;
            string priority;

            if (schedule.Status == "critical" || schedule.Status == "overdue")
            {
                priority = "urgent";
                if (schedule.NextDueKm.HasValue && vehicle.Mileage > schedule.NextDueKm.Value)
                {
                    var overKm = vehicle.Mileage - schedule.NextDueKm.Value;
                    message = $"{templateName} en retard de {overKm:N0} km pour {vehicle.Name} ({vehicle.Plate})";
                }
                else
                {
                    message = $"{templateName} en retard pour {vehicle.Name} ({vehicle.Plate})";
                }
            }
            else
            {
                priority = "normal";
                if (schedule.NextDueKm.HasValue)
                {
                    var remainingKm = schedule.NextDueKm.Value - vehicle.Mileage;
                    message = $"{templateName} à prévoir dans {remainingKm:N0} km pour {vehicle.Name} ({vehicle.Plate})";
                }
                else if (schedule.NextDueDate.HasValue)
                {
                    var days = (int)(schedule.NextDueDate.Value - DateTime.UtcNow).TotalDays;
                    message = $"{templateName} à prévoir dans {days} jour(s) pour {vehicle.Name} ({vehicle.Plate})";
                }
                else
                {
                    message = $"{templateName} à effectuer pour {vehicle.Name} ({vehicle.Plate})";
                }
            }

            var alreadySent = await HasRecentNotification(context, companyId, "maintenance_prediction",
                vehicle.Id, templateName, ct);
            if (!alreadySent)
            {
                var title = $"Entretien: {templateName}";

                // In-app notification for company admins — route to the real "entretien programmable" page
                await SendToCompanyAdmins(context, notifService, companyId,
                    "maintenance_prediction", title, message,
                    priority, "vehicle", vehicle.Id, "/entretien-programmable", ct);

                // Email fan-out to alert_emails with alertType = "entretien"
                try
                {
                    await alertDispatcher.DispatchAsync(
                        companyId, "entretien", title, message, "/entretien-programmable", ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to dispatch entretien alert email for company {CompanyId}",
                        companyId);
                }
            }
        }
    }

    private async Task CheckFuelAnomaly(GisDbContext context, INotificationService notifService,
        Vehicle vehicle, int companyId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var last7Days = now.AddDays(-7);
        var prev7Days = now.AddDays(-14);

        var recentEntries = await context.FuelEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(f => f.VehicleId == vehicle.Id && f.InvoiceDate >= last7Days)
            .ToListAsync(ct);

        var previousEntries = await context.FuelEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(f => f.VehicleId == vehicle.Id && f.InvoiceDate >= prev7Days && f.InvoiceDate < last7Days)
            .ToListAsync(ct);

        if (recentEntries.Count < 2 || previousEntries.Count < 2)
            return;

        // La taille moyenne d'un plein ne dit RIEN de la consommation : elle
        // dépend du moment où le chauffeur s'arrête (2 gros pleins vs 3 petits
        // = fausse "hausse" à consommation identique — cas Scania 001 du
        // 08-10/08, alertes en boucle après la simple saisie des factures).
        // On compare des L/100km : litres facturés ÷ km odomètre par fenêtre.
        if (!vehicle.GpsDeviceId.HasValue)
            return;

        var recentKm = await WindowKmAsync(context, vehicle.GpsDeviceId.Value, last7Days, now, ct);
        var prevKm = await WindowKmAsync(context, vehicle.GpsDeviceId.Value, prev7Days, last7Days, ct);
        if (recentKm < 100 || prevKm < 100)
            return; // pas assez de roulage pour une comparaison honnête

        var recentRate = (double)recentEntries.Sum(f => f.Volume) / recentKm * 100.0;
        var prevRate = (double)previousEntries.Sum(f => f.Volume) / prevKm * 100.0;
        if (prevRate <= 0)
            return;

        var increase = (recentRate - prevRate) / prevRate * 100.0;
        if (increase > 20)
        {
            // Fenêtres de 7 jours glissantes => re-notifier toutes les 6 h ne
            // fait que répéter la même information : anti-doublon de 7 jours.
            var alreadySent = await HasRecentNotification(context, companyId, "fuel_anomaly",
                vehicle.Id, "fuel", ct, TimeSpan.FromDays(7));
            if (!alreadySent)
            {
                await SendToCompanyAdmins(context, notifService, companyId,
                    "fuel_anomaly",
                    $"Consommation anormale: {vehicle.Name}",
                    $"Consommation carburant en hausse de {increase:F0}% sur 7 jours pour {vehicle.Name} ({vehicle.Plate}). {recentRate:F1} L/100km vs {prevRate:F1} L/100km la semaine précédente.",
                    "normal", "vehicle", vehicle.Id, "/vehicules", ct);
            }
        }
    }

    /// <summary>Km parcourus sur la fenêtre d'après l'odomètre du boîtier (0 si indisponible).</summary>
    private static async Task<double> WindowKmAsync(GisDbContext context, int deviceId,
        DateTime from, DateTime to, CancellationToken ct)
    {
        var odo = await context.GpsPositions
            .AsNoTracking()
            .Where(p => p.DeviceId == deviceId && p.RecordedAt >= from && p.RecordedAt < to
                        && p.OdometerKm != null && p.OdometerKm > 0)
            .GroupBy(p => 1)
            .Select(g => new { Min = g.Min(p => p.OdometerKm), Max = g.Max(p => p.OdometerKm) })
            .FirstOrDefaultAsync(ct);
        return odo?.Max == null || odo.Min == null ? 0 : (double)(odo.Max.Value - odo.Min.Value);
    }

    private async Task<bool> HasRecentNotification(GisDbContext context, int companyId,
        string type, int vehicleId, string keyword, CancellationToken ct, TimeSpan? dedupWindow = null)
    {
        var cutoff = DateTime.UtcNow - (dedupWindow ?? TimeSpan.FromHours(CHECK_INTERVAL_HOURS));
        return await context.Notifications
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(n => n.CompanyId == companyId
                && n.Type == type
                && n.ReferenceId == vehicleId
                && n.CreatedAt >= cutoff, ct);
    }

    private async Task SendToCompanyAdmins(GisDbContext context, INotificationService notifService,
        int companyId, string type, string title, string message,
        string priority, string referenceType, int referenceId, string actionUrl, CancellationToken ct)
    {
        var adminUsers = await context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(u => u.Role)
            .Where(u => u.CompanyId == companyId && u.Status == "active"
                && u.Role != null && u.Role.IsCompanyAdmin)
            .Select(u => u.Id)
            .ToListAsync(ct);

        foreach (var userId in adminUsers)
        {
            await notifService.CreateAndSendAsync(
                companyId, userId, type, title, message,
                priority, referenceType, referenceId, actionUrl, ct: ct);
        }
    }
}
