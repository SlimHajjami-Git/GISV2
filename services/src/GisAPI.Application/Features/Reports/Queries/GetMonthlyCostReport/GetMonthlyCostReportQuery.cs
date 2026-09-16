using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Reports.Queries.GetMonthlyCostReport;

public record GetMonthlyCostReportQuery(
    int Year,
    int Month,
    int? DepartmentId = null
) : IQuery<MonthlyCostReportDto>;

// ==================== REPORT DTOs ====================

/// <summary>
/// Ratios au kilomètre d'un GROUPE de véhicules (société, département). Seuls les
/// véhicules dont la distance est mesurée et non nulle y entrent, au numérateur
/// COMME au dénominateur : la règle de la moyenne du parc de « Coût d'exploitation
/// réel ». L'écran divisait le coût de TOUS les véhicules par les seuls kilomètres
/// mesurés, si bien que chaque véhicule « non mesuré » gonflait le coût au km et la
/// consommation de son département (recette du 13/09/2026).
/// Null quand le groupe ne compte aucun kilomètre mesuré.
/// </summary>
public abstract class MonthlyCostGroupRatiosDto
{
    public decimal? CostPerKm { get; set; }
    public decimal? FuelPer100Km { get; set; }
    public decimal? MaintenanceRepairPer100Km { get; set; }
    public decimal? ConsumptionPer100Km { get; set; }
    public decimal? ConsumptionPrPer100Km { get; set; }

    public void ComputeRatios(IReadOnlyCollection<VehicleMonthlyCostDto> vehicles)
    {
        var mesures = vehicles.Where(v => v.Km > 0).ToList();
        var km = mesures.Sum(v => v.Km!.Value);
        // Mois précédent : sa propre sélection, un véhicule peut être mesuré un mois et pas l'autre.
        var mesuresPr = vehicles.Where(v => v.KmPr > 0).ToList();
        var kmPr = mesuresPr.Sum(v => v.KmPr!.Value);

        CostPerKm = Ratio(mesures.Sum(v => v.TotalCostDzd), km, 1);
        FuelPer100Km = Ratio(mesures.Sum(v => v.FuelCostDzd), km, 100);
        MaintenanceRepairPer100Km = Ratio(mesures.Sum(v => v.MaintenanceCostDzd + v.RepairCostDzd), km, 100);
        ConsumptionPer100Km = Ratio(mesures.Sum(v => v.FuelLiters), km, 100);
        ConsumptionPrPer100Km = Ratio(mesuresPr.Sum(v => v.FuelLitersPr), kmPr, 100);
    }

    private static decimal? Ratio(decimal numerateur, decimal km, decimal facteur) =>
        km > 0 ? Math.Round(numerateur / km * facteur, 2) : null;
}

public class MonthlyCostReportDto : MonthlyCostGroupRatiosDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = string.Empty;
    public string ReportPeriod { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }

    // Summary totals
    public decimal TotalKm { get; set; }
    public decimal TotalFuelCostDzd { get; set; }
    public decimal TotalFuelLiters { get; set; }
    public decimal TotalMaintenanceCostDzd { get; set; }
    public decimal TotalRepairCostDzd { get; set; }
    public decimal TotalOtherCostDzd { get; set; }
    public decimal TotalCostDzd { get; set; }

    // Totaux du mois PRECEDENT. Sans eux, la ligne « Total » du tableau
    // recopiait le mois courant dans les colonnes de comparaison : elle
    // contredisait ses propres lignes vehicule, qui affichent bien deux
    // mois differents.
    public decimal TotalKmPr { get; set; }
    public decimal TotalFuelLitersPr { get; set; }

    // Per-vehicle detail rows grouped by department
    public List<DepartmentCostGroupDto> Departments { get; set; } = new();

    // Flat list of all vehicle rows (for table display)
    public List<VehicleMonthlyCostDto> Vehicles { get; set; } = new();

    /// <summary>
    /// Vue « Consommation carburant mensuel » (route monthly-fuel, recette du 11/09/2026) :
    /// ce rapport partage la requête de « Coûts mensuel par véhicule » mais porte sa propre
    /// case. Les postes hors carburant sont mis à zéro et le total devient le carburant
    /// seul, à tous les niveaux (société, département, véhicule) — l'écran carburant ne lit
    /// que km, litres et coût carburant. Les listes de véhicules partagent leurs instances :
    /// l'opération est idempotente.
    /// </summary>
    public MonthlyCostReportDto ToFuelOnly()
    {
        TotalMaintenanceCostDzd = 0;
        TotalRepairCostDzd = 0;
        TotalOtherCostDzd = 0;
        TotalCostDzd = TotalFuelCostDzd;
        foreach (var d in Departments)
        {
            d.TotalMaintenanceCostDzd = 0;
            d.TotalRepairCostDzd = 0;
            d.TotalOtherCostDzd = 0;
            d.TotalCostDzd = d.TotalFuelCostDzd;
            foreach (var v in d.Vehicles) v.StripNonFuelCosts();
            d.ComputeRatios(d.Vehicles);
        }
        foreach (var v in Vehicles) v.StripNonFuelCosts();
        ComputeRatios(Vehicles);
        return this;
    }
}

public class DepartmentCostGroupDto : MonthlyCostGroupRatiosDto
{
    public int? DepartmentId { get; set; }
    public string DepartmentName { get; set; } = "Non assigné";

    // Department subtotals
    public decimal TotalKm { get; set; }
    public decimal TotalFuelCostDzd { get; set; }
    public decimal TotalFuelLiters { get; set; }
    public decimal TotalMaintenanceCostDzd { get; set; }
    public decimal TotalRepairCostDzd { get; set; }
    public decimal TotalOtherCostDzd { get; set; }
    public decimal TotalCostDzd { get; set; }

    // Totaux du mois PRECEDENT. Sans eux, la ligne « Total » du tableau
    // recopiait le mois courant dans les colonnes de comparaison : elle
    // contredisait ses propres lignes vehicule, qui affichent bien deux
    // mois differents.
    public decimal TotalKmPr { get; set; }
    public decimal TotalFuelLitersPr { get; set; }

    public List<VehicleMonthlyCostDto> Vehicles { get; set; } = new();
}

public class VehicleMonthlyCostDto
{
    public int VehicleId { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public string? Plate { get; set; }
    public string? DriverName { get; set; }
    public int? DepartmentId { get; set; }
    public string DepartmentName { get; set; } = "Non assigné";

    // Mileage
    /// <summary>
    /// Kilométrage du mois ; NULL quand il n'est pas mesurable. Un véhicule sans
    /// boîtier dont le mois ne contient qu'un seul relevé compteur (sa vidange,
    /// par exemple) n'a pas de distance : « 0 km » se lisait comme une mesure et
    /// tirait à zéro la consommation et le coût au km de véhicules qui avaient
    /// bien roulé — 700 km pour GL-694-PN en septembre (recette du 13/09/2026).
    /// </summary>
    public decimal? Km { get; set; }
    /// <summary>
    /// "gps" : compteur du boîtier ; "odometer" : relevés saisis (pleins,
    /// entretiens, réparations, dépenses) ; "none" : rien de mesurable ce mois.
    /// </summary>
    public string KmSource { get; set; } = "none";
    // For fuel consumption report: KM PR = KM from GPS between fuel entries
    /// <summary>Kilométrage du mois PRÉCÉDENT ; null s'il n'est pas mesurable.</summary>
    public decimal? KmPr { get; set; }

    // Costs in DZD
    public decimal FuelCostDzd { get; set; }
    public decimal MaintenanceCostDzd { get; set; }
    public decimal RepairCostDzd { get; set; }
    // Assurance, vignette, visite technique, carte grise, peage, reparation-accident…
    public decimal OtherCostDzd { get; set; }
    public decimal TotalCostDzd { get; set; }

    // Fuel consumption (liters)
    public decimal FuelLiters { get; set; }
    // Liters from GPS-period (between refuels)
    public decimal FuelLitersPr { get; set; }

    // Computed ratios — NULL sans distance mesurée : un ratio « 0 » se lit comme
    // une mesure alors qu'il ne dit que l'absence de dénominateur.
    public decimal? CostPerKm { get; set; }                // TotalCost / Km
    public decimal? FuelPer100Km { get; set; }             // (FuelCostDzd / Km) * 100
    public decimal? MaintenanceRepairPer100Km { get; set; }// ((Maint+Repair) / Km) * 100

    // Fuel consumption ratios
    public decimal? ConsumptionPer100Km { get; set; }      // (FuelLiters / Km) * 100
    public decimal? ConsumptionPrPer100Km { get; set; }    // (FuelLitersPr / KmPr) * 100

    /// <summary>Ne garde que le carburant (voir MonthlyCostReportDto.ToFuelOnly).</summary>
    public void StripNonFuelCosts()
    {
        MaintenanceCostDzd = 0;
        RepairCostDzd = 0;
        OtherCostDzd = 0;
        TotalCostDzd = FuelCostDzd;
        CostPerKm = Km > 0 ? FuelCostDzd / Km.Value : null;
        MaintenanceRepairPer100Km = Km > 0 ? 0 : null;
    }
}
