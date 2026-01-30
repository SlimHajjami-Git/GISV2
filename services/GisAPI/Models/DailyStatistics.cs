using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Models;

public class DailyStatistics
{
    [Key]
    public long Id { get; set; }

    public int VehicleId { get; set; }

    [ForeignKey("VehicleId")]
    public Vehicle? Vehicle { get; set; }

    public int CompanyId { get; set; }

    [ForeignKey("CompanyId")]
    public Company? Company { get; set; }

    public DateOnly Date { get; set; }

    // Distance & Time
    [Column(TypeName = "decimal(10,2)")]
    public decimal DistanceKm { get; set; }

    public int DrivingTimeMinutes { get; set; }

    public int IdleTimeMinutes { get; set; }

    public int StoppedTimeMinutes { get; set; }

    public int TripCount { get; set; }

    // Speed
    [Column(TypeName = "decimal(10,2)")]
    public decimal? AverageSpeedKph { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? MaxSpeedKph { get; set; }

    public int OverspeedingEvents { get; set; }

    public int OverspeedingMinutes { get; set; }

    // Fuel
    [Column(TypeName = "decimal(10,2)")]
    public decimal? FuelConsumedLiters { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? FuelEfficiencyKmPerLiter { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? FuelCost { get; set; }

    // Driving behavior
    public int HarshBrakingCount { get; set; }

    public int HarshAccelerationCount { get; set; }

    public int SharpTurnCount { get; set; }

    // Mileage
    public int StartMileage { get; set; }

    public int EndMileage { get; set; }

    // Engine
    public int? EngineOnTimeMinutes { get; set; }

    public int? EngineOffTimeMinutes { get; set; }

    // Alerts
    public int AlertCount { get; set; }

    public int GeofenceEventsCount { get; set; }

    // Driver score (0-100)
    public int? DriverScore { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}



