using System.Runtime.Serialization;

namespace GisAPI.Domain.Enums;

public enum MaintenanceType
{
    [EnumMember(Value = "scheduled")] Scheduled,
    [EnumMember(Value = "unscheduled")] Unscheduled,
    [EnumMember(Value = "emergency")] Emergency,
    [EnumMember(Value = "preventive")] Preventive,
    [EnumMember(Value = "corrective")] Corrective,
    [EnumMember(Value = "repair")] Repair,
    [EnumMember(Value = "inspection")] Inspection,
    [EnumMember(Value = "tire_change")] TireChange,
    [EnumMember(Value = "oil_change")] OilChange,
    [EnumMember(Value = "other")] Other
}


