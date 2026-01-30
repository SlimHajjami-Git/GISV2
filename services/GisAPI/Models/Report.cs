using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Models;

public class Report
{
    [Key]
    public int Id { get; set; }

    public int CompanyId { get; set; }

    [ForeignKey("CompanyId")]
    public Company? Company { get; set; }

    public int? CreatedByUserId { get; set; }

    [ForeignKey("CreatedByUserId")]
    public User? CreatedByUser { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty; // trip, fuel, maintenance, alert, geofence, driver_behavior, summary

    [MaxLength(500)]
    public string? Description { get; set; }

    // Report parameters
    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    // Filter by vehicles (null = all)
    public int[]? VehicleIds { get; set; }

    // Filter by drivers (null = all)
    public int[]? DriverIds { get; set; }

    [MaxLength(20)]
    public string Format { get; set; } = "pdf"; // pdf, excel, csv

    [MaxLength(20)]
    public string Status { get; set; } = "pending"; // pending, generating, completed, failed

    [MaxLength(500)]
    public string? FileUrl { get; set; }

    public long? FileSizeBytes { get; set; }

    [MaxLength(1000)]
    public string? ErrorMessage { get; set; }

    public DateTime? GeneratedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ReportSchedule
{
    [Key]
    public int Id { get; set; }

    public int CompanyId { get; set; }

    [ForeignKey("CompanyId")]
    public Company? Company { get; set; }

    public int CreatedByUserId { get; set; }

    [ForeignKey("CreatedByUserId")]
    public User? CreatedByUser { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string ReportType { get; set; } = string.Empty;

    // Schedule frequency
    [Required]
    [MaxLength(20)]
    public string Frequency { get; set; } = "weekly"; // daily, weekly, monthly

    // For weekly: 0=Sunday, 1=Monday, etc.
    public int? DayOfWeek { get; set; }

    // For monthly: 1-31
    public int? DayOfMonth { get; set; }

    // Time to generate (HH:mm)
    [MaxLength(5)]
    public string TimeOfDay { get; set; } = "08:00";

    // Recipients
    public string[] EmailRecipients { get; set; } = Array.Empty<string>();

    // Filter parameters
    public int[]? VehicleIds { get; set; }

    public int[]? DriverIds { get; set; }

    [MaxLength(20)]
    public string Format { get; set; } = "pdf";

    public bool IsActive { get; set; } = true;

    public DateTime? LastRunAt { get; set; }

    public DateTime? NextRunAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}



