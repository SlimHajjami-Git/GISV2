using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Models;

[Table("vehicles")]
public class Vehicle
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    [Column("type")]
    public string Type { get; set; } = "camion"; // camion, citadine, suv, utilitaire, other

    [MaxLength(50)]
    [Column("brand")]
    public string? Brand { get; set; }

    [MaxLength(50)]
    [Column("model")]
    public string? Model { get; set; }

    [MaxLength(20)]
    [Column("plate_number")]
    public string? Plate { get; set; }

    [Column("year")]
    public int? Year { get; set; }

    [MaxLength(30)]
    [Column("color")]
    public string? Color { get; set; }

    [MaxLength(20)]
    [Column("status")]
    public string Status { get; set; } = "available"; // available, in_use, maintenance

    [Column("has_gps")]
    public bool HasGps { get; set; }

    [Column("mileage")]
    public int Mileage { get; set; }

    [Column("rental_mileage")]
    public int? RentalMileage { get; set; }

    // Foreign keys
    [Column("company_id")]
    public int CompanyId { get; set; }

    [ForeignKey("CompanyId")]
    public Company? Company { get; set; }

    [Column("assigned_driver_id")]
    public int? AssignedDriverId { get; set; }

    [ForeignKey("AssignedDriverId")]
    public Employee? AssignedDriver { get; set; }

    [Column("assigned_supervisor_id")]
    public int? AssignedSupervisorId { get; set; }

    [ForeignKey("AssignedSupervisorId")]
    public Employee? AssignedSupervisor { get; set; }

    [Column("gps_device_id")]
    public int? GpsDeviceId { get; set; }

    [ForeignKey("GpsDeviceId")]
    public GpsDevice? GpsDevice { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Legacy columns from old schema (mapped for compatibility with Rust)
    [Column("driver_name")]
    public string? DriverName { get; set; }

    [Column("driver_phone")]
    public string? DriverPhone { get; set; }

    // Navigation
    public ICollection<VehicleDocument> Documents { get; set; } = new List<VehicleDocument>();
    public ICollection<MaintenanceRecord> MaintenanceRecords { get; set; } = new List<MaintenanceRecord>();
    public ICollection<VehicleCost> Costs { get; set; } = new List<VehicleCost>();
}

public class VehicleDocument
{
    [Key]
    public int Id { get; set; }

    public int VehicleId { get; set; }

    [ForeignKey("VehicleId")]
    public Vehicle? Vehicle { get; set; }

    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty; // registration, insurance, customs, maintenance

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public DateTime? ExpiryDate { get; set; }

    [MaxLength(500)]
    public string? FileUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
