using System.Runtime.Serialization;

namespace GisAPI.Domain.Enums;

public enum AlertSeverity
{
    [EnumMember(Value = "low")] Low,
    [EnumMember(Value = "medium")] Medium,
    [EnumMember(Value = "high")] High,
    [EnumMember(Value = "critical")] Critical
}


