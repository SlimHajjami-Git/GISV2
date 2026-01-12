using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Models;

public class GpsDevice
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string DeviceUid { get; set; } = string.Empty; // IMEI

    [MaxLength(50)]
    public string? Mat { get; set; } // MAT (identifiant logique GPS, distinct du matricule véhicule)

    [MaxLength(100)]
    public string? Label { get; set; }

    [MaxLength(20)]
    public string? SimNumber { get; set; }

    [MaxLength(50)]
    public string? SimOperator { get; set; } // orange, inwi, maroc_telecom, other

    [MaxLength(50)]
    public string? Model { get; set; }

    [MaxLength(50)]
    public string? Brand { get; set; }

    [MaxLength(50)]
    public string? ProtocolType { get; set; }

    [MaxLength(50)]
    public string? FirmwareVersion { get; set; }

    public DateTime? InstallationDate { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = "unassigned"; // active, inactive, maintenance, unassigned

    public DateTime? LastCommunication { get; set; }

    public int? BatteryLevel { get; set; }

    public int? SignalStrength { get; set; }

    public bool? ExternalPowerConnected { get; set; }

    public double? LastLatitude { get; set; }

    public double? LastLongitude { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal? TotalDistance { get; set; } // Total km traveled

    public int CompanyId { get; set; }

    [ForeignKey("CompanyId")]
    public Company? Company { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Vehicle? Vehicle { get; set; }
    public ICollection<GpsPosition> Positions { get; set; } = new List<GpsPosition>();
    public ICollection<GpsAlert> Alerts { get; set; } = new List<GpsAlert>();
}

public class GpsPosition
{
    [Key]
    public long Id { get; set; }

    public int DeviceId { get; set; }

    [ForeignKey("DeviceId")]
    public GpsDevice? Device { get; set; }

    public DateTime RecordedAt { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public double? SpeedKph { get; set; }

    public double? CourseDeg { get; set; }

    public double? AltitudeM { get; set; }

    public bool? IgnitionOn { get; set; }

    public int? FuelRaw { get; set; }

    public long? OdometerKm { get; set; }

    public int? PowerVoltage { get; set; }

    public int? Satellites { get; set; }

    public bool IsValid { get; set; }

    public bool IsRealTime { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object>? Metadata { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class GpsAlert
{
    [Key]
    public int Id { get; set; }

    public int? DeviceId { get; set; }

    [ForeignKey("DeviceId")]
    public GpsDevice? Device { get; set; }

    public int? VehicleId { get; set; }

    [ForeignKey("VehicleId")]
    public Vehicle? Vehicle { get; set; }

    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty; // speeding, stopped, geofence, battery, sos, other

    [MaxLength(50)]
    public string Severity { get; set; } = "medium"; // low, medium, high, critical

    [Required]
    [MaxLength(500)]
    public string Message { get; set; } = string.Empty;

    public bool Resolved { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public int? ResolvedByUserId { get; set; }

    [ForeignKey("ResolvedByUserId")]
    public User? ResolvedByUser { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
