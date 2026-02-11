using System.Runtime.Serialization;

namespace GisAPI.Domain.Enums;

public enum AlertType
{
    [EnumMember(Value = "overspeed")] Overspeed,
    [EnumMember(Value = "geofence_entry")] GeofenceEntry,
    [EnumMember(Value = "geofence_exit")] GeofenceExit,
    [EnumMember(Value = "low_fuel")] LowFuel,
    [EnumMember(Value = "harsh_braking")] HarshBraking,
    [EnumMember(Value = "harsh_acceleration")] HarshAcceleration,
    [EnumMember(Value = "sharp_turn")] SharpTurn,
    [EnumMember(Value = "ignition_on")] IgnitionOn,
    [EnumMember(Value = "ignition_off")] IgnitionOff,
    [EnumMember(Value = "tow_alert")] TowAlert,
    [EnumMember(Value = "sos")] Sos,
    [EnumMember(Value = "maintenance_due")] MaintenanceDue,
    [EnumMember(Value = "battery_low")] BatteryLow,
    [EnumMember(Value = "other")] Other
}


