using MediatR;

namespace GisAPI.Application.Features.Notifications.Events;

/// <summary>
/// Émis par <c>VoltageHealthMonitoringService</c> quand la batterie d'un véhicule
/// équipé NEMS est en fin de vie.
///
/// <para>Un seul signal, <c>battery_dead</c> : la tension relevée au DERNIER
/// DÉMARRAGE du véhicule (<c>gps_devices.battery_start_raw</c>, écrite par
/// <c>BatteryStartReadingService</c>) est passée sous
/// <c>VoltageScale.NemsBatteryLowWarningV</c>. C'est la valeur que le monitoring
/// affiche : l'admin retrouve le même chiffre sur sa carte.</para>
///
/// <para>Les signaux retirés en cours de route, et pourquoi :
/// <c>saturated_silence</c> (un véhicule muet dort souvent au parking — deux
/// fausses alertes sur des véhicules de location stationnés),
/// <c>charging_voltage_low</c> (c'est l'alternateur, pas la batterie),
/// <c>resting_voltage_decline</c> et <c>resting_voltage_critical</c> (calculés sur
/// l'octet 32-34, prouvé muet le 25/09/2026).</para>
///
/// <para>Un événement par boîtier. Le détecteur applique une temporisation de 48 h
/// via <c>GpsDevice.LastVoltageHealthAlertAt</c> ; le handler ne dédoublonne pas.</para>
/// </summary>
public record BatteryHealthAlertEvent(
    int CompanyId,
    int DeviceId,
    int? VehicleId,
    string? VehicleName,
    string SignalKind,
    string Severity,
    string Description,
    double? VoltageObservedV,
    double? VoltageBaselineV,
    DateTime DetectedAt
) : INotification;
