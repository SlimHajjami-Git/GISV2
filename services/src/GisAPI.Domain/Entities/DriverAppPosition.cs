namespace GisAPI.Domain.Entities;

/// <summary>
/// Position envoyée par le téléphone du chauffeur PENDANT une tournée en cours
/// (migration 051). Table séparée de <see cref="GpsPosition"/> : les positions du
/// véhicule alimentent le kilométrage, les trajets, les alertes et les rapports —
/// celles du téléphone ne servent qu'au suivi de la tournée (relais quand le boîtier
/// se tait, seule source pour un véhicule sans boîtier).
/// </summary>
public class DriverAppPosition
{
    public long Id { get; set; }
    public int CompanyId { get; set; }
    public int UserId { get; set; }
    public int DriverId { get; set; }
    public int TourId { get; set; }
    /// <summary>Horloge du téléphone, corrigée du décalage mesuré à la réception.</summary>
    public DateTime RecordedAt { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public float? AccuracyM { get; set; }
    public float? SpeedKph { get; set; }
    public float? Heading { get; set; }
    /// <summary>Position simulée signalée par Android : jamais une source de suivi.</summary>
    public bool IsMocked { get; set; }
    public short? BatteryLevel { get; set; }
}
