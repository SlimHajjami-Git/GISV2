using System.Runtime.Serialization;

namespace GisAPI.Domain.Enums;

public enum VehicleStatus
{
    [EnumMember(Value = "available")] Available,
    [EnumMember(Value = "in_use")] InUse,
    [EnumMember(Value = "maintenance")] Maintenance,
    [EnumMember(Value = "out_of_service")] OutOfService,
    [EnumMember(Value = "retired")] Retired
}


