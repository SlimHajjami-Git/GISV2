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
                await CheckDocumentExpiry(context, notifService, vehicle, companyId, ct);
                await CheckMaintenanceDue(context, notifService, vehicle, companyId, ct);
                await CheckFuelAnomaly(context, notifService, vehicle, companyId, ct);
            }
        }

        _logger.LogInformation("PredictiveAlertService completed check for {Count} companies", companies.Count);
    }

    private async Task CheckDocumentExpiry(GisDbContext context, INotificationService notifService,
        Vehicle vehicle, int companyId, CancellationToken ct)
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
                    await SendToCompanyAdmins(context, notifService, companyId,
                        "document_expiry", $"Document: {name}", message,
                        priority, "vehicle", vehicle.Id, "/vehicules", ct);
                }
            }
        }
    }

    private async Task CheckMaintenanceDue(GisDbContext context, INotificationService notifService,
        Vehicle vehicle, int companyId, CancellationToken ct)
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
                await SendToCompanyAdmins(context, notifService, companyId,
                    "maintenance_prediction", $"Entretien: {templateName}", message,
                    priority, "vehicle", vehicle.Id, "/entretiens", ct);
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

        if (recentEntries.Count >= 2 && previousEntries.Count >= 2)
        {
            var recentAvg = recentEntries.Average(f => f.Volume);
            var prevAvg = previousEntries.Average(f => f.Volume);

            if (prevAvg > 0)
            {
                var increase = ((recentAvg - prevAvg) / prevAvg) * 100;
                if (increase > 15)
                {
                    var alreadySent = await HasRecentNotification(context, companyId, "fuel_anomaly",
                        vehicle.Id, "fuel", ct);
                    if (!alreadySent)
                    {
                        await SendToCompanyAdmins(context, notifService, companyId,
                            "fuel_anomaly",
                            $"Consommation anormale: {vehicle.Name}",
                            $"Consommation carburant en hausse de {increase:F0}% sur 7 jours pour {vehicle.Name} ({vehicle.Plate}). Moyenne: {recentAvg:F1}L vs {prevAvg:F1}L",
                            "normal", "vehicle", vehicle.Id, "/vehicules", ct);
                    }
                }
            }
        }
    }

    private async Task<bool> HasRecentNotification(GisDbContext context, int companyId,
        string type, int vehicleId, string keyword, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddHours(-CHECK_INTERVAL_HOURS);
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
