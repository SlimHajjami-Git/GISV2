using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Models;

public class Trip
{
    [Key]
    public long Id { get; set; }

    public int VehicleId { get; set; }

    [ForeignKey("VehicleId")]
    public Vehicle? Vehicle { get; set; }

    public int? DriverId { get; set; }

    [ForeignKey("DriverId")]
    public Employee? Driver { get; set; }

    public int CompanyId { get; set; }

    [ForeignKey("CompanyId")]
    public Company? Company { get; set; }

    // Start point
    public DateTime StartTime { get; set; }

    public double StartLatitude { get; set; }

    public double StartLongitude { get; set; }

    [MaxLength(500)]
    public string? StartAddress { get; set; }

    public int StartMileage { get; set; }

    // End point
    public DateTime? EndTime { get; set; }

    public double? EndLatitude { get; set; }

    public double? EndLongitude { get; set; }

    [MaxLength(500)]
    public string? EndAddress { get; set; }

    public int? EndMileage { get; set; }

    // Trip metrics
    [Column(TypeName = "decimal(10,2)")]
    public decimal DistanceKm { get; set; }

    public int DurationMinutes { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? FuelConsumedLiters { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? AverageSpeedKph { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? MaxSpeedKph { get; set; }

    public int? IdleTimeMinutes { get; set; }

    public int? HarshBrakingCount { get; set; }

    public int? HarshAccelerationCount { get; set; }

    public int? OverspeedingCount { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = "in_progress"; // in_progress, completed, cancelled

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<TripWaypoint> Waypoints { get; set; } = new List<TripWaypoint>();
}

public class TripWaypoint
{
    [Key]
    public long Id { get; set; }

    public long TripId { get; set; }

    [ForeignKey("TripId")]
    public Trip? Trip { get; set; }

    public int SequenceNumber { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public DateTime Timestamp { get; set; }

    public double? SpeedKph { get; set; }

    public double? Heading { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(50)]
    public string? EventType { get; set; } // stop, speeding, harsh_braking, harsh_acceleration, geofence_entry, geofence_exit
}



