using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Domain.Common;
using GisAPI.Domain.Entities;
using GisAPI.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// Prévient les admins quand la batterie d'un véhicule équipé NEMS
/// (<c>protocol_type = 'gps_type_1'</c>) est en train de mourir.
///
/// <para><b>Le signal : la tension au démarrage</b> (Slim, 29/09/2026). L'octet
/// « Batterie » (34-36) des trames NEMS mélange deux grandeurs — moteur tournant
/// il porte l'alternateur (13,5 à 14,4 V), qui ne dit rien de la batterie. Seul
/// l'instant du démarrage la montre. <c>BatteryStartReadingService</c> retient
/// cette tension sur le boîtier ; ce service ne fait que la lire et la comparer à
/// <see cref="VoltageScale.NemsBatteryLowWarningV"/>. L'écran du monitoring juge
/// EXACTEMENT la même valeur : un admin qui reçoit la notification retrouve le
/// même chiffre sur la carte.</para>
///
/// <para><b>Ce qui a changé le 29/09/2026.</b> Le service lisait
/// <c>power_voltage</c> (octet 32-34, facteur 0,3) sur les trames moteur coupé
/// des 7 derniers jours. Or le 25/09 cet octet a été prouvé muet : 281 boîtiers
/// sur 288 y renvoyaient la même valeur moteur tournant et moteur éteint. Le
/// service tournait donc à vide — 2 boîtiers signalés sur 308 jugeables au
/// 29/09/2026. Il ne lit plus aucune position : la valeur est déjà sur le boîtier.</para>
///
/// <para><b>Pourquoi 11,5 V et non les 11,9 V du manuel plomb-acide.</b> Mesuré
/// sur la production TN le 29/09/2026, sur les 226 boîtiers ayant démarré dans les
/// 48 h : médiane de la flotte 12,19 V au démarrage, premier quartile 11,72 V. À
/// 11,9 V l'alerte visait 84 boîtiers (37 % du parc) ; à 11,5 V elle en vise 34
/// (15 %). Seuil retenu par Slim.</para>
///
/// <para><b>Ce qu'on ne fait toujours PAS</b> : pas d'alerte « silence prolongé »
/// (un véhicule muet 24 h peut dormir au parking ou être au garage — la cloche
/// « Véhicules hors ligne » couvre déjà ce cas passivement, et les notifications
/// poussées dessus ont produit deux fausses alertes sur des véhicules de location
/// stationnés) ; pas d'alerte alternateur (c'est l'alternateur, pas la batterie).</para>
///
/// <para><b>Temporisation</b> : 48 h via <c>GpsDevice.LastVoltageHealthAlertAt</c>.
/// <b>Diffusion</b> : <see cref="BatteryHealthAlertEvent"/> par MediatR.
/// <b>Filtre véhicule</b> : les véhicules marqués <c>IsImmobilized</c> (hors
/// service, drapeau posé par l'exploitant) sont ignorés.</para>
/// </summary>
public class VoltageHealthMonitoringService : BackgroundService
{
    // Cadence du service. La tension au démarrage ne change qu'au démarrage
    // suivant : une passe par heure suffit largement.
    private const int CycleMinutes = 60;
    private const int StartupDelayMinutes = 3;
    private const int CooldownHours = 48;

    // Au-delà de cet âge, la tension retenue ne dit plus rien de l'état ACTUEL de
    // la batterie : le véhicule n'a pas redémarré depuis, et une notification
    // « batterie en fin de vie » sur une mesure vieille d'une semaine serait au
    // mieux inutile, au pire fausse (batterie déjà remplacée). L'écran, lui,
    // continue de l'afficher datée — c'est une information, pas une alerte.
    private const int MaxReadingAgeDays = 3;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<VoltageHealthMonitoringService> _logger;

    public VoltageHealthMonitoringService(
        IServiceProvider serviceProvider,
        ILogger<VoltageHealthMonitoringService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(StartupDelayMinutes), ct); }
        catch (TaskCanceledException) { return; }

        _logger.LogInformation(
            "VoltageHealthMonitoringService started (cycle={CycleMin}min, cooldown={Cooldown}h, seuil<{Threshold}V au démarrage)",
            CycleMinutes, CooldownHours, VoltageScale.NemsBatteryLowWarningV);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "VoltageHealthMonitoringService cycle failed");
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

        var now = DateTime.UtcNow;
        var cooldownCutoff = now.AddHours(-CooldownHours);
        var readingCutoff = now.AddDays(-MaxReadingAgeDays);

        // Tout le filtrage tient en SQL : plus aucune lecture de gps_positions.
        var candidates = await context.GpsDevices
            .IgnoreQueryFilters()
            .Include(d => d.Vehicle)
            .Where(d => d.ProtocolType == VoltageScale.NemsProtocol
                     && d.Vehicle != null
                     && !d.Vehicle.IsImmobilized
                     && d.BatteryStartMedianRaw != null
                     && d.BatteryStartAt != null
                     && d.BatteryStartAt >= readingCutoff
                     && (d.LastVoltageHealthAlertAt == null
                         || d.LastVoltageHealthAlertAt < cooldownCutoff))
            .ToListAsync(ct);

        if (candidates.Count == 0) return;

        int flagged = 0;
        foreach (var device in candidates)
        {
            try
            {
                var volts = DyingBatteryVolts(device.BatteryStartMedianRaw);
                if (volts == null) continue;

                device.LastVoltageHealthAlertAt = now;
                device.UpdatedAt = now;
                await context.SaveChangesAsync(ct);
                flagged++;

                var measuredAt = device.BatteryStartAt!.Value;

                _logger.LogInformation(
                    "VoltageHealth: batterie faible sur le boîtier {DeviceId} ({Plate}) — médiane {Volts:F1} V, dernier démarrage le {MeasuredAt:u}",
                    device.Id, VehicleLabel(device.Vehicle) ?? "?", volts.Value, measuredAt);

                await mediator.Publish(new BatteryHealthAlertEvent(
                    CompanyId: device.CompanyId,
                    DeviceId: device.Id,
                    VehicleId: device.Vehicle?.Id,
                    VehicleName: VehicleLabel(device.Vehicle),
                    SignalKind: "battery_dead",
                    Severity: "critical",
                    Description:
                        $"Batterie en fin de vie : {volts.Value:F1} V de médiane sur les " +
                        $"{VoltageScale.StartHistoryWindow} derniers démarrages (seuil " +
                        $"{VoltageScale.NemsBatteryLowWarningV:F1} V), dernier le " +
                        $"{measuredAt:dd/MM/yyyy à HH:mm} UTC",
                    VoltageObservedV: VoltageScale.RoundVolts(volts.Value),
                    VoltageBaselineV: null,
                    DetectedAt: now
                ), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "VoltageHealthMonitoringService: failed to evaluate device {DeviceId}",
                    device.Id);
            }
        }

        if (flagged > 0)
        {
            _logger.LogInformation(
                "VoltageHealthMonitoringService: flagged {Flagged}/{Total} device(s) this cycle",
                flagged, candidates.Count);
        }
    }

    /// <summary>
    /// Tension en volts si cette valeur brute désigne une batterie en fin de vie,
    /// <c>null</c> sinon (batterie saine, ou valeur qui n'est pas une tension).
    ///
    /// <para>Le tri de la bande 68-92 (<see cref="VoltageScale.NemsMeaningfulVolts"/>)
    /// écarte l'octet de cap des firmwares R00C30d : hors bande, ce n'est pas une
    /// tension, et une valeur absurde ne doit surtout pas devenir une alerte.</para>
    /// </summary>
    public static double? DyingBatteryVolts(short? batteryStartRaw)
    {
        var volts = VoltageScale.NemsMeaningfulVolts(batteryStartRaw);
        return volts < VoltageScale.NemsBatteryLowWarningV ? volts : null;
    }

    private static string? VehicleLabel(Vehicle? vehicle)
    {
        if (vehicle == null) return null;
        if (!string.IsNullOrWhiteSpace(vehicle.Plate)) return vehicle.Plate;
        if (!string.IsNullOrWhiteSpace(vehicle.Name)) return vehicle.Name;
        return null;
    }
}
