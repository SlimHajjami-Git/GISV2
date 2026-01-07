using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class DriverAssignment : TenantEntity
{
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public int DriverId { get; set; }
    public Employee? Driver { get; set; }
    public int? AssignedByUserId { get; set; }
    public User? AssignedByUser { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Status { get; set; } = "active";
    public string AssignmentType { get; set; } = "permanent";
    public string? Notes { get; set; }
    public int? StartMileage { get; set; }
    public int? EndMileage { get; set; }
}

public class DriverScore : TenantEntity
{
    public int DriverId { get; set; }
    public Employee? Driver { get; set; }
    public DateOnly Date { get; set; }
    public int OverallScore { get; set; }
    public int SpeedingScore { get; set; }
    public int BrakingScore { get; set; }
    public int AccelerationScore { get; set; }
    public int IdlingScore { get; set; }
    public int FuelEfficiencyScore { get; set; }
    public int SpeedingEvents { get; set; }
    public int HarshBrakingEvents { get; set; }
    public int HarshAccelerationEvents { get; set; }
    public int IdlingEvents { get; set; }
    public decimal DistanceKm { get; set; }
    public int DrivingTimeMinutes { get; set; }
}
