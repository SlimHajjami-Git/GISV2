using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Domain.Entities;
using GisAPI.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// Background watcher that creates a bell notification as soon as a vehicle
/// document (assurance / vignette / visite technique) enters its reminder
/// window — i.e. <c>today &gt;= expiry - reminderDays</c>.
///
/// <para><b>Calypso 6 (P8.1)</b> — "Dès la date de notification, on affiche
/// une notification dans la cloche". Before this, the only path to learn
/// an échéance was approaching was the dedicated <c>/documents</c> page;
/// the bell only fired for runtime alerts (vitesse, géofencing, accident).</para>
///
/// <para><b>Detection rule</b> for each vehicle and each of the three doc
/// types (insurance, tax, technical_inspection):</para>
/// <list type="bullet">
///   <item><description>Vehicle has an expiry date for that doc type
///     (skip vehicles where the field is <c>NULL</c>).</description></item>
///   <item><description><c>today &gt;= expiry - reminderDays</c>
///     (using the per-doc-type reminder column on the vehicle, default 30).</description></item>
///   <item><description>The expiry is at most <c>OverdueGraceDays</c> days
///     in the past — beyond that we stop nagging because the user has
///     clearly seen the alert and is dealing with it.</description></item>
///   <item><description>No <c>document_expiry</c> notification with the same
///     <c>(vehicleId, docType, expiryDate)</c> exists yet (dedup so the bell
///     does not refire every cycle for the same approaching deadline).</description></item>
/// </list>
///
/// <para><b>Cycle</b>: <see cref="CycleMinutes"/> minutes — fast enough that
/// users see the bell light up the same day a doc enters its reminder
/// window, slow enough that we do not hammer Postgres with full-fleet
/// scans. The query is cheap (vehicles is in the low thousands per tenant).</para>
///
/// <para><b>Fan-out</b>: publishes a <see cref="DocumentExpiryNotificationEvent"/>
/// via MediatR; <c>DocumentExpiryNotificationHandler</c> resolves the
/// active company admins and calls
/// <see cref="Application.Common.Interfaces.INotificationService.CreateAndSendAsync"/>.
/// Conducteurs are deliberately excluded — only admins can act on
/// renewing documents.</para>
/// </summary>
public class DocumentExpiryMonitoringService : BackgroundService
{
    private const int CycleMinutes = 60;
    private const int StartupDelayMinutes = 3;
    private const int OverdueGraceDays = 60; // stop alerting after 60d overdue

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DocumentExpiryMonitoringService> _logger;

    public DocumentExpiryMonitoringService(
        IServiceProvider serviceProvider,
        ILogger<DocumentExpiryMonitoringService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(StartupDelayMinutes), ct); }
        catch (TaskCanceledException) { return; }

        _logger.LogInformation(
            "DocumentExpiryMonitoringService started (cycle={CycleMin}min, overdueGrace={Grace}d)",
            CycleMinutes, OverdueGraceDays);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "DocumentExpiryMonitoringService cycle failed");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(CycleMinutes), ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GisDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var today = DateTime.UtcNow.Date;
        var graceCutoff = today.AddDays(-OverdueGraceDays);

        // We bypass tenant filters: the watcher must scan every company.
        // The handler still respects tenant scoping when fetching admins.
        var vehicles = await context.Vehicles
            .IgnoreQueryFilters()
            .Where(v =>
                v.InsuranceExpiry.HasValue
                || v.TaxExpiry.HasValue
                || v.TechnicalInspectionExpiry.HasValue)
            .Select(v => new
            {
                v.Id,
                v.CompanyId,
                v.Name,
                v.Plate,
                v.InsuranceExpiry,
                v.TaxExpiry,
                v.TechnicalInspectionExpiry,
                v.InsuranceReminderDays,
                v.TaxReminderDays,
                v.TechnicalInspectionReminderDays,
            })
            .ToListAsync(ct);

        if (vehicles.Count == 0) return;

        // Pre-load existing document_expiry notifications for the candidate
        // vehicles in one query — this is the dedup table.
        var vehicleIds = vehicles.Select(v => v.Id).ToList();
        var existing = await context.Notifications
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(n => n.Type == "document_expiry"
                     && n.ReferenceType == "vehicle"
                     && n.ReferenceId.HasValue
                     && vehicleIds.Contains(n.ReferenceId.Value))
            .Select(n => new { n.ReferenceId, n.Metadata })
            .ToListAsync(ct);

        // Index existing notifications by (vehicleId|docType|expiryDate) so
        // the inner loop below is a simple O(1) lookup.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in existing)
        {
            if (n.ReferenceId == null || n.Metadata == null) continue;
            if (!n.Metadata.TryGetValue("documentType", out var docType)) continue;
            if (!n.Metadata.TryGetValue("expiryDate", out var expiryDate)) continue;
            seen.Add($"{n.ReferenceId}|{docType}|{expiryDate}");
        }

        int candidates = 0;
        int dispatched = 0;

        foreach (var v in vehicles)
        {
            // Build the (docType, label, expiry, reminderDays) tuples once.
            var docs = new List<(string DocType, string Label, DateTime? Expiry, int ReminderDays)>
            {
                ("insurance",            "Assurance",        v.InsuranceExpiry,             v.InsuranceReminderDays),
                ("tax",                  "Vignette",         v.TaxExpiry,                   v.TaxReminderDays),
                ("technical_inspection", "Visite technique", v.TechnicalInspectionExpiry,   v.TechnicalInspectionReminderDays),
            };

            foreach (var doc in docs)
            {
                if (!doc.Expiry.HasValue) continue;
                var expiryDate = doc.Expiry.Value.Date;
                var reminderStart = expiryDate.AddDays(-Math.Max(1, doc.ReminderDays));

                // Skip docs whose reminder window has not opened yet.
                if (today < reminderStart) continue;

                // Stop nagging once the overdue grace period has passed.
                if (expiryDate < graceCutoff) continue;

                candidates++;

                var key = $"{v.Id}|{doc.DocType}|{expiryDate:yyyy-MM-dd}";
                if (seen.Contains(key)) continue;

                var label = !string.IsNullOrWhiteSpace(v.Plate) ? v.Plate :
                            !string.IsNullOrWhiteSpace(v.Name)  ? v.Name  :
                            $"Véhicule #{v.Id}";

                try
                {
                    await mediator.Publish(new DocumentExpiryNotificationEvent(
                        CompanyId: v.CompanyId,
                        VehicleId: v.Id,
                        VehicleLabel: label,
                        DocumentType: doc.DocType,
                        DocumentTypeLabel: doc.Label,
                        ExpiryDate: expiryDate,
                        DaysRemaining: (expiryDate - today).Days
                    ), ct);

                    // Add to the in-memory dedup set so a second doc on the
                    // same vehicle in the same cycle does not double-fire.
                    seen.Add(key);
                    dispatched++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "DocumentExpiry: failed to publish event for vehicle {VehicleId} / {DocType}",
                        v.Id, doc.DocType);
                }
            }
        }

        if (dispatched > 0)
        {
            _logger.LogInformation(
                "DocumentExpiryMonitoringService: dispatched {Dispatched}/{Candidates} reminder(s) this cycle",
                dispatched, candidates);
        }
        else if (candidates > 0)
        {
            _logger.LogDebug(
                "DocumentExpiryMonitoringService: {Candidates} candidate(s) in window, all already notified",
                candidates);
        }
    }
}
