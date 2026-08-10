using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

/// <summary>
/// Période de chargement déclarée d'un véhicule (tonnage transporté).
/// Sert à comparer la consommation selon la charge : les segments de
/// consommation dont le milieu tombe dans [StartTime, EndTime] héritent
/// du tonnage de la période. EndTime null = période encore en cours.
/// </summary>
public class VehicleLoadPeriod : TenantEntity
{
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    /// <summary>Tonnage transporté en tonnes (0 = à vide)</summary>
    public decimal TonnageT { get; set; }

    public string? Notes { get; set; }
}
