using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class Trip : TenantEntity
{
    public new long Id { get; set; }
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public int? DriverId { get; set; }
    public User? Driver { get; set; }
    
    public DateTime StartTime { get; set; }
    public double StartLatitude { get; set; }
    public double StartLongitude { get; set; }
    public string? StartAddress { get; set; }
    public int StartMileage { get; set; }
    
    public DateTime? EndTime { get; set; }
    public double? EndLatitude { get; set; }
    public double? EndLongitude { get; set; }
    public string? EndAddress { get; set; }
    public int? EndMileage { get; set; }
    
    public decimal DistanceKm { get; set; }
    public int DurationMinutes { get; set; }
    public decimal? FuelConsumedLiters { get; set; }
    public decimal? AverageSpeedKph { get; set; }
    public decimal? MaxSpeedKph { get; set; }
    public int? IdleTimeMinutes { get; set; }
    public int? HarshBrakingCount { get; set; }
    public int? HarshAccelerationCount { get; set; }
    public int? OverspeedingCount { get; set; }
    public string Status { get; set; } = "in_progress";
    public string? Notes { get; set; }

    public ICollection<TripWaypoint> Waypoints { get; set; } = new List<TripWaypoint>();
}

public class TripWaypoint : Entity
{
    public new long Id { get; set; }
    public long TripId { get; set; }
    public Trip? Trip { get; set; }
    public int SequenceNumber { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime Timestamp { get; set; }
    public double? SpeedKph { get; set; }
    public double? Heading { get; set; }
    public string? Address { get; set; }
    public string? EventType { get; set; }
}
