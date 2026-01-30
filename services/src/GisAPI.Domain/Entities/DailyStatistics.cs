using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class DailyStatistics : TenantEntity
{
    public new long Id { get; set; }
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public DateOnly Date { get; set; }
    
    public decimal DistanceKm { get; set; }
    public int DrivingTimeMinutes { get; set; }
    public int IdleTimeMinutes { get; set; }
    public int StoppedTimeMinutes { get; set; }
    public int TripCount { get; set; }
    
    public decimal? AverageSpeedKph { get; set; }
    public decimal? MaxSpeedKph { get; set; }
    public int OverspeedingEvents { get; set; }
    public int OverspeedingMinutes { get; set; }
    
    public decimal? FuelConsumedLiters { get; set; }
    public decimal? FuelEfficiencyKmPerLiter { get; set; }
    public decimal? FuelCost { get; set; }
    
    public int HarshBrakingCount { get; set; }
    public int HarshAccelerationCount { get; set; }
    public int SharpTurnCount { get; set; }
    
    public int StartMileage { get; set; }
    public int EndMileage { get; set; }
    public int? EngineOnTimeMinutes { get; set; }
    public int? EngineOffTimeMinutes { get; set; }
    
    public int AlertCount { get; set; }
    public int GeofenceEventsCount { get; set; }
    public int? DriverScore { get; set; }
}


