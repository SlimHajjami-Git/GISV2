using GisAPI.Application.Features.DataPort;

namespace GisAPI.Application.Features.Costs;

/// <summary>
/// Contrôles d'une dépense saisie par POST/PUT /api/costs (écran Dépenses, scan de
/// facture IA, écran Coûts).
///
/// <para>Campagne de test GPA : l'API acceptait n'importe quelle catégorie
/// (DEF-040 : « xyz » créait une catégorie fantôme, affichée brute à l'écran et
/// comptée en « Autres ») et n'importe quel montant (DEF-050 : -50 était enregistré
/// et jouait comme un crédit silencieux dans les totaux de l'écran Dépenses, du
/// tableau de bord et des rapports). Le crédit prévu est la catégorie
/// « insurance_refund » à montant POSITIF, créée par le dossier sinistre et que les
/// agrégats soustraient explicitement : aucun écrivain légitime n'envoie de montant
/// négatif.</para>
/// </summary>
public static class VehicleCostRules
{
    /// <summary>Plus grand montant que porte la colonne amount, decimal(10,2).</summary>
    public const decimal MaxAmount = ExpenseImportRow.MaxAmount;

    /// <summary>
    /// Catégorie à enregistrer (<see cref="ExpenseImportRow.StoredType"/>) : code écrit
    /// par l'application, synonyme ramené à son code ; null si elle est inconnue.
    /// </summary>
    public static string? StoredType(string? type) => ExpenseImportRow.StoredType(type);

    /// <summary>Motif de refus d'une catégorie hors liste.</summary>
    public static string UnknownTypeMessage(string? type) =>
        string.IsNullOrWhiteSpace(type)
            ? "Choisissez une catégorie de dépense."
            : $"Catégorie de dépense inconnue : « {type.Trim()} ». Choisissez une catégorie de la liste.";

    /// <summary>Motif de refus du montant, null s'il est acceptable.</summary>
    public static string? AmountError(decimal amount)
    {
        if (amount <= 0)
            return "Le montant doit être supérieur à zéro. Un remboursement d'assurance se saisit en positif " +
                   "dans le dossier sinistre.";
        if (amount > MaxAmount)
            return "Montant trop élevé : 99 999 999,99 au maximum.";
        return null;
    }
}
