using GisAPI.Application.Features.DataPort;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

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
    ///   <item><c>credit_note</c> (« Avoir fournisseur ») → Autres, en CRÉDIT, même convention : montant
    ///     saisi en positif (écran Dépenses, scan d'un avoir, import Excel) ;</item>
    ///   <item>type ancien synonyme ou libellé de l'un de ces codes (« carburant », « Réparation
    ///     accident », « avoir », « remb. assurance »…) → le poste et le signe de son code ;</item>
    ///   <item>tout le reste → Autres.</item>
    /// </list>
    /// </summary>
    public static (CostCategory Category, int Sign) Classify(string? type) => Normalize(type) switch
    {
        // Codes exacts d'abord : c'est ce qu'écrivent l'application et l'import, soit la
        // quasi-totalité des lignes, et cette règle tourne sur chaque dépense des rapports.
        "fuel" => (CostCategory.Fuel, 1),
        "maintenance" or "entretien" => (CostCategory.Maintenance, 1),
        "repair" or "reparation" or "réparation" => (CostCategory.Repair, 1),
        // Avoir fournisseur (décision du 16/09/2026) : depuis DEF-050 le montant négatif
        // est refusé, l'avoir est donc un crédit explicite comme le remboursement d'assurance.
        "insurance_refund" or CreditNote => (CostCategory.Other, -1),
        // Lignes écrites avant la liste blanche (DEF-040) : « carburant », « avoir »…
        // L'export les écrit sous le libellé de leur code et l'import les relit sous ce
        // code. Classées sur leur seul texte, un plein « carburant » restait en « Autres »
        // et un avoir comptait en dépense, jusqu'au premier aller-retour du classeur.
        _ => ClassifyCode(ExpenseImportRow.StoredType(type))
    };

    /// <summary>Code de l'avoir fournisseur dans <c>vehicle_costs.type</c> (« credit » est déjà « Crédit / Leasing »).</summary>
    public const string CreditNote = "credit_note";

    /// <summary>
    /// Poste d'un code de <see cref="ExpenseImportRow.StoredType"/>, qui ramène synonymes et
    /// libellés à leur code avec la normalisation de l'import (casse, accents) : la table des
    /// synonymes reste unique. <c>StoredType</c> plutôt que <c>TypeFamily</c> : un code déjà
    /// connu (« insurance », « amende »…) y est reconnu sans normalisation Unicode.
    /// </summary>
    private static (CostCategory Category, int Sign) ClassifyCode(string? code) => code switch
    {
        "fuel" => (CostCategory.Fuel, 1),
        "maintenance" => (CostCategory.Maintenance, 1),
        "repair" or "reparation" => (CostCategory.Repair, 1),
        "insurance_refund" or CreditNote => (CostCategory.Other, -1),
        _ => (CostCategory.Other, 1)
    };

    /// <summary>
    /// Montant signé : négatif pour un remboursement d'assurance ou un avoir fournisseur. Un
    /// crédit compte en valeur absolue : avant le refus des montants négatifs (DEF-050), un
    /// avoir pouvait être saisi à −120 ; −1 × −120 en aurait fait une dépense de +120.
    /// </summary>
    public static decimal SignedAmount(string? type, decimal amount) =>
        Classify(type).Sign < 0 ? -Math.Abs(amount) : amount;

    /// <summary>
    /// Total net de dépenses, crédits déduits. La règle (synonymes, accents) n'a pas
    /// d'équivalent SQL : une somme par type côté base, quelques lignes, puis le signe
    /// ici — jamais le détail des dépenses chargé en mémoire.
    /// </summary>
    public static async Task<decimal> SignedTotalAsync(IQueryable<VehicleCost> costs, CancellationToken ct = default)
    {
        // Parts positive et négative à part : un crédit se déduit ligne à ligne en valeur
        // absolue (voir SignedAmount), ce que la somme brute d'un type mélangé ne permet pas.
        // CASE plutôt que Math.Abs, que le fournisseur SQLite des tests ne traduit pas.
        var byType = await costs
            .GroupBy(c => c.Type)
            .Select(g => new
            {
                Type = g.Key,
                Positive = g.Sum(c => c.Amount > 0 ? c.Amount : 0m),
                Negative = g.Sum(c => c.Amount < 0 ? c.Amount : 0m)
            })
            .ToListAsync(ct);
        return byType.Sum(t => Classify(t.Type).Sign < 0
            ? -(t.Positive - t.Negative)
            : t.Positive + t.Negative);
    }

    public static bool IsFuel(string? type) => Classify(type).Category == CostCategory.Fuel;
    public static bool IsMaintenance(string? type) => Classify(type).Category == CostCategory.Maintenance;
    public static bool IsRepair(string? type) => Classify(type).Category == CostCategory.Repair;
}
