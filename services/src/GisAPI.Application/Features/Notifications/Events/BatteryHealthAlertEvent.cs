using MediatR;

namespace GisAPI.Application.Features.Notifications.Events;

/// <summary>
/// Raised by <c>VoltageHealthMonitoringService</c> when one of the four
/// proactive battery-health signals trips on a NEMS L device:
///
/// <list type="bullet">
///   <item><description><c>resting_voltage_decline</c> — average resting
///     voltage over the last 3 days dropped ≥ 0.6 V vs the J-30/J-7
///     baseline AND is currently ≤ 12.4 V (sulfation pattern, days
///     before failure).</description></item>
///   <item><description><c>resting_voltage_critical</c> — recent average
///     resting voltage ≤ 12.0 V (battery already in deep discharge,
///     starting compromised).</description></item>
///   <item><description><c>charging_voltage_low</c> — when ignition is on
///     and the vehicle is moving, average voltage stays below 13.0 V
///     instead of the expected 13.8–14.4 V (alternator/regulator failing,
///     battery not being recharged on each trip).</description></item>
///   <item><description><c>saturated_silence</c> — firmware safety net:
///     <c>power_voltage</c> byte stayed pinned at the saturation value
///     (0x2B / 12.9 V) for 14 consecutive days AND the device has now
///     been silent for 90+ minutes. Catches the case where the firmware
///     plafond hides any pre-death decline signal.</description></item>
/// </list>
///
/// <para>One event per device per signal. The detector enforces a 48h
/// cooldown through <c>GpsDevice.LastVoltageHealthAlertAt</c>; the
/// handler must not re-deduplicate.</para>
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
