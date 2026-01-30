using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

/// <summary>
/// Représente un arrêt de véhicule détecté (contact coupé ou véhicule immobile)
/// </summary>
public class VehicleStop : LongIdTenantEntity
{
    
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    
    public int? DriverId { get; set; }
    public User? Driver { get; set; }
    
    public int? DeviceId { get; set; }
    public GpsDevice? Device { get; set; }
    
    // Timing
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int DurationSeconds { get; set; }
    
    // Location
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Address { get; set; }
    
    // Stop classification
    public string StopType { get; set; } = "unknown";  // parking, traffic, delivery, refuel, rest, unknown
    public bool IgnitionOff { get; set; }
    public bool IsAuthorized { get; set; } = true;     // Pour marquer les arrêts non autorisés
    
    // Vehicle state at stop
    public int? StartMileage { get; set; }
    public int? EndMileage { get; set; }
    public int? FuelLevelStart { get; set; }           // % carburant au début
    public int? FuelLevelEnd { get; set; }             // % carburant à la fin
    
    // Geofence info
    public int? GeofenceId { get; set; }
    public Geofence? Geofence { get; set; }
    public bool InsideGeofence { get; set; }
    
    // Metadata
    public string? Notes { get; set; }
}


