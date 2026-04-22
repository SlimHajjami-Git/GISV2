using MediatR;

namespace GisAPI.Application.Features.Notifications.Events;

/// <summary>
/// Raised by <c>BatteryMonitoringService</c> when a NEMS L device
/// (<c>protocol_type = 'gps_type_1'</c>) reports a sustained low
/// <c>power_voltage</c> reading — a reliable proxy for a dying vehicle
/// battery on a boîtier that has no internal backup cell.
///
/// <para>Dispatched via MediatR so the fan-out handler stays decoupled
/// from the detection loop. One event per device per detection cycle;
/// the <c>BatteryMonitoringService</c> enforces a 24h cooldown through
/// <c>GpsDevice.LastBatteryAlertAt</c>, so a single failing battery
/// doesn't spam the bell icon every 5 minutes.</para>
/// </summary>
public record BatteryAlertNotificationEvent(
    int CompanyId,
    int DeviceId,
    int? VehicleId,
    string? VehicleName,
    int VoltageRaw,
    DateTime DetectedAt
) : INotification;
