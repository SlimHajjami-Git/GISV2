using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class Report : TenantEntity
{
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int[]? VehicleIds { get; set; }
    public int[]? DriverIds { get; set; }
    public string Format { get; set; } = "pdf";
    public string Status { get; set; } = "pending";
    public string? FileUrl { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? GeneratedAt { get; set; }
}

public class ReportSchedule : TenantEntity
{
    public int CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ReportType { get; set; } = string.Empty;
    public string Frequency { get; set; } = "weekly";
    public int? DayOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public string TimeOfDay { get; set; } = "08:00";
    public string[] EmailRecipients { get; set; } = [];
    public int[]? VehicleIds { get; set; }
    public int[]? DriverIds { get; set; }
    public string Format { get; set; } = "pdf";
    public bool IsActive { get; set; } = true;
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
}
