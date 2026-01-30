using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class SpeedLimitAlert : TenantEntity
{
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    
    public int SpeedLimit { get; set; }
    public int ActualSpeed { get; set; }
    
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Address { get; set; }
    
    public DateTime RecordedAt { get; set; }
    
    public bool IsAcknowledged { get; set; } = false;
    public DateTime? AcknowledgedAt { get; set; }
    public int? AcknowledgedById { get; set; }
    public User? AcknowledgedBy { get; set; }

    // Navigation
    public Societe? Societe { get; set; }
}


