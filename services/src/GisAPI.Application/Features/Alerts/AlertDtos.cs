namespace GisAPI.Application.Features.Alerts;

public class AlertDto
{
    public int Id { get; set; }
    public int? DeviceId { get; set; }
    public int? VehicleId { get; set; }
    public string? VehicleName { get; set; }
    public string? Plate { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = "medium";
    public string Message { get; set; } = string.Empty;
    public bool Resolved { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int? ResolvedByUserId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTime Timestamp { get; set; }
    public DateTime CreatedAt { get; set; }
}
