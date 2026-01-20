using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

/// <summary>
/// Historique détaillé du carburant pour analyse de consommation et détection d'anomalies
/// </summary>
public class FuelRecord : LongIdTenantEntity
{
    
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    
    public int? DriverId { get; set; }
    public User? Driver { get; set; }
    
    public int? DeviceId { get; set; }
    public GpsDevice? Device { get; set; }
    
    public DateTime RecordedAt { get; set; }
    
    // Fuel levels
    public short FuelPercent { get; set; }              // Niveau % (0-100)
    public decimal? FuelLiters { get; set; }            // Litres (si réservoir calibré)
    public decimal? TankCapacityLiters { get; set; }    // Capacité réservoir
    
    // Consumption metrics
    public decimal? ConsumptionRateLPer100Km { get; set; }  // Consommation instantanée L/100km
    public decimal? AverageConsumptionLPer100Km { get; set; } // Moyenne sur trajet
    
    // Vehicle state
    public long? OdometerKm { get; set; }
    public double? SpeedKph { get; set; }
    public short? Rpm { get; set; }
    public bool? IgnitionOn { get; set; }
    
    // Location
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    
    // Event detection
    public string EventType { get; set; } = "reading";  // reading, refuel, consumption_spike, theft_alert, sensor_error
    public short? FuelChange { get; set; }              // Variation depuis dernière lecture (+ = plein, - = consommation)
    
    // For refuel events
    public decimal? RefuelAmount { get; set; }          // Litres ajoutés
    public decimal? RefuelCost { get; set; }            // Coût du plein
    public string? RefuelStation { get; set; }          // Station service
    
    // Alerts
    public bool IsAnomaly { get; set; }                 // Consommation anormale détectée
    public string? AnomalyReason { get; set; }          // Raison de l'anomalie
    
}

/// <summary>
/// Types d'événements carburant
/// </summary>
public static class FuelEventTypes
{
    public const string Reading = "reading";           // Lecture normale
    public const string Refuel = "refuel";             // Plein de carburant
    public const string ConsumptionSpike = "consumption_spike"; // Pic de consommation
    public const string TheftAlert = "theft_alert";    // Suspicion de vol
    public const string SensorError = "sensor_error";  // Erreur capteur
    public const string LowFuel = "low_fuel";          // Niveau bas
}
