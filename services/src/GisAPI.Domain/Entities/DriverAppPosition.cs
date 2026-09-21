namespace GisAPI.Domain.Entities;

/// <summary>
/// Position envoyée par le téléphone du chauffeur PENDANT une tournée en cours, ou
/// mesurée au plus 2 min après sa clôture (dernier lot de l'application), migration 051.
/// Un seul point par compte et par instant (index unique user_id, recorded_at : un lot
/// renvoyé après une réponse perdue n'est pas doublé). Table séparée de <see cref="GpsPosition"/> : les positions du
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
