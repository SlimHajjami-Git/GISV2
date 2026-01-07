using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class DrivingEvent : TenantEntity
{
    public new long Id { get; set; }
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public int? DriverId { get; set; }
    public Employee? Driver { get; set; }
    public long? TripId { get; set; }
    public Trip? Trip { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = "medium";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Address { get; set; }
    public DateTime Timestamp { get; set; }
    public double? SpeedKph { get; set; }
    public double? SpeedLimitKph { get; set; }
    public double? GForce { get; set; }
    public int? DurationSeconds { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}
