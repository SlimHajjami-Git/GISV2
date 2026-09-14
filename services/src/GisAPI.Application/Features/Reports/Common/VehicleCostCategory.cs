namespace GisAPI.Application.Features.Reports.Common;

/// <summary>Seau d'une dépense <c>vehicle_costs</c> dans les écrans de coûts.</summary>
public enum CostCategory
{
    Fuel,
    Maintenance,
    Repair,
    Other
}

/// <summary>
/// Ventilation d'une dépense <c>vehicle_costs</c> selon son type : UNE seule
/// règle pour le tableau de bord GPS (<c>DashboardService.PeriodCostsAsync</c>),
/// l'agrégateur des rapports de coûts et du tableau de bord GPA
/// (<see cref="OperatingCostAggregator"/>) et « Coûts mensuels par véhicule ».
///
/// <para>Constat du 14/09/2026 : l'agrégateur venait de ranger « repair » en
/// Réparations et de SOUSTRAIRE « insurance_refund », mais les deux autres calculs
/// les comptaient toujours en « Autres », en positif. La même dépense de
/// 1 143 400 (société 1, avril 2026) était « Réparations » dans un écran et
/// « Autres » dans le voisin, et un remboursement R aurait écarté les totaux de 2R.</para>
/// </summary>
public static class VehicleCostCategory
{
    /// <summary>Type normalisé : espaces retirés, minuscules (la saisie varie selon l'écran source).</summary>
    public static string Normalize(string? type) => (type ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// Seau et signe d'une dépense :
    /// <list type="bullet">
    ///   <item><c>fuel</c> → Carburant ;</item>
    ///   <item><c>maintenance</c> / <c>entretien</c> → Entretiens ;</item>
    ///   <item><c>repair</c> / <c>reparation</c> / <c>réparation</c> → Réparations (catégorie proposée
    ///     par l'écran Dépenses et le scan de facture) ;</item>
    ///   <item><c>insurance_refund</c> → Autres, en CRÉDIT (le module Sinistres l'enregistre en montant positif) ;</item>
    ///   <item>tout le reste → Autres.</item>
    /// </list>
    /// </summary>
    public static (CostCategory Category, int Sign) Classify(string? type) => Normalize(type) switch
    {
        "fuel" => (CostCategory.Fuel, 1),
        "maintenance" or "entretien" => (CostCategory.Maintenance, 1),
        "repair" or "reparation" or "réparation" => (CostCategory.Repair, 1),
        "insurance_refund" => (CostCategory.Other, -1),
        _ => (CostCategory.Other, 1)
    };

    /// <summary>Montant signé : négatif pour un remboursement d'assurance.</summary>
    public static decimal SignedAmount(string? type, decimal amount) => Classify(type).Sign * amount;

    public static bool IsFuel(string? type) => Classify(type).Category == CostCategory.Fuel;
    public static bool IsMaintenance(string? type) => Classify(type).Category == CostCategory.Maintenance;
    public static bool IsRepair(string? type) => Classify(type).Category == CostCategory.Repair;
}
