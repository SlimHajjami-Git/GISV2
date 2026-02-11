using System.Runtime.Serialization;

namespace GisAPI.Domain.Enums;

public enum MaintenanceStatus
{
    [EnumMember(Value = "scheduled")] Scheduled,
    [EnumMember(Value = "in_progress")] InProgress,
    [EnumMember(Value = "completed")] Completed,
    [EnumMember(Value = "cancelled")] Cancelled
}


