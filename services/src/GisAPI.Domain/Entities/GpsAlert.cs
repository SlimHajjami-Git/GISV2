using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class GpsAlert : AuditableEntity
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
}
