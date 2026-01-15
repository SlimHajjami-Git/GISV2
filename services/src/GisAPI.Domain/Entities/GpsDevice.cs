using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class GpsDevice : TenantEntity
{
    public string DeviceUid { get; set; } = string.Empty;
    public string? Mat { get; set; }
    public string? Label { get; set; }
    public string? SimNumber { get; set; }
    public string? SimOperator { get; set; }
    public string? Model { get; set; }
    public string? Brand { get; set; }
    public string? ProtocolType { get; set; }
    public string? FirmwareVersion { get; set; }
    public DateTime? InstallationDate { get; set; }
    public string Status { get; set; } = "unassigned";
    public DateTime? LastCommunication { get; set; }
    public int? BatteryLevel { get; set; }
    public int? SignalStrength { get; set; }

    public Company? Company { get; set; }
    public Vehicle? Vehicle { get; set; }
    
    public ICollection<GpsPosition> Positions { get; set; } = new List<GpsPosition>();
    public ICollection<GpsAlert> Alerts { get; set; } = new List<GpsAlert>();
}

public class GpsPosition : Entity
{
    public new long Id { get; set; }
    public int DeviceId { get; set; }
    public GpsDevice? Device { get; set; }
    public DateTime RecordedAt { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? EventKey { get; set; }
    public double? SpeedKph { get; set; }
    public double? CourseDeg { get; set; }
    public double? AltitudeM { get; set; }
    public bool? IgnitionOn { get; set; }
    public int? FuelRaw { get; set; }
    public int? PowerVoltage { get; set; }
    public int? Satellites { get; set; }
    public bool IsValid { get; set; }
    public bool IsRealTime { get; set; }
    public string? Address { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // MEMS (Accéléromètre) - Valeurs brutes (-128 à 127)
    public short? MemsX { get; set; }
    public short? MemsY { get; set; }
    public short? MemsZ { get; set; }
    
    // FMS Data (Fleet Management System)
    public short? TemperatureC { get; set; }      // Température moteur (°C, valeur - 40)
    public long? OdometerKm { get; set; }         // Kilométrage total (support > 2M km)
    public short? Rpm { get; set; }               // Tours/minute moteur
    public decimal? FuelRateLPer100Km { get; set; } // Consommation instantanée L/100km
    
    // Protocol info
    public byte? SendFlag { get; set; }           // Type d'événement (0-11)
    public byte? ProtocolVersion { get; set; }    // Version protocole AAP (1,2,3)
}

public class GpsAlert : Entity
{
    public int? DeviceId { get; set; }
    public GpsDevice? Device { get; set; }
    public int? VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = "medium";
    public string Message { get; set; } = string.Empty;
    public bool Resolved { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int? ResolvedByUserId { get; set; }
    public User? ResolvedByUser { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
