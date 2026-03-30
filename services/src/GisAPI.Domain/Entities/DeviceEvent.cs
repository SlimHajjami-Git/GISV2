using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class DeviceEvent : TenantEntity
{
    public new long Id { get; set; }
    public int DeviceId { get; set; }
    public GpsDevice? Device { get; set; }
    public int? VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public string EventType { get; set; } = string.Empty; // restart, gsm_reset, disconnect, tamper_suspected
    public DateTime EventAt { get; set; }
    public int? OfflineDurationSecs { get; set; }
    public double? LastKnownLat { get; set; }
    public double? LastKnownLon { get; set; }
    public string? LastKnownAddress { get; set; }
    public bool WasMoving { get; set; }
    public Dictionary<string, object>? Details { get; set; }
    public bool Acknowledged { get; set; }
    public int? AcknowledgedBy { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
}
