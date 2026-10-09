namespace GisAPI.Domain.Entities;

/// <summary>
/// IMEI d'un boîtier supprimé par l'administration (migration 057, 09/10/2026).
/// L'ingestion consulte cette liste avant de créer ou de mettre à jour une fiche :
/// sans elle, un boîtier supprimé qui émet encore réapparaissait de lui-même (fiche
/// recréée en société 1) ou faisait tomber chaque trame en violation de clé étrangère.
/// Les colonnes *Before gardent ce que portait la fiche : l'audit ne retient que l'id.
/// </summary>
public class GpsDeviceBlocklistEntry
{
    public string DeviceUid { get; set; } = string.Empty;
    public DateTime BlockedAt { get; set; } = DateTime.UtcNow;
    public int? BlockedByUserId { get; set; }
    public int? DeviceIdBefore { get; set; }
    public string? Mat { get; set; }
    public int? CompanyIdBefore { get; set; }
    public string? Reason { get; set; }
}
