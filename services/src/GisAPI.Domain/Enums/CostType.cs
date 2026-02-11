using System.Runtime.Serialization;

namespace GisAPI.Domain.Enums;

public enum CostType
{
    [EnumMember(Value = "fuel")] Fuel,
    [EnumMember(Value = "maintenance")] Maintenance,
    [EnumMember(Value = "insurance")] Insurance,
    [EnumMember(Value = "tax")] Tax,
    [EnumMember(Value = "toll")] Toll,
    [EnumMember(Value = "fine")] Fine,
    [EnumMember(Value = "parking")] Parking,
    [EnumMember(Value = "tire")] Tire,
    [EnumMember(Value = "registration")] Registration,
    [EnumMember(Value = "technical_inspection")] TechnicalInspection,
    [EnumMember(Value = "transport_permit")] TransportPermit,
    [EnumMember(Value = "other")] Other
}


