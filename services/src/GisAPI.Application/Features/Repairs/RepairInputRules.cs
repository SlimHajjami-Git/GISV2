using System.Globalization;
using GisAPI.Domain.Exceptions;

namespace GisAPI.Application.Features.Repairs;

/// <summary>
/// Contrôles de saisie d'une réparation, communs à la création, à la modification
/// complète et au changement de statut (recette GPA du 16/09/2026).
///
///   • DEF-043 : le statut était recopié sans contrôle. « bidule » était stocké, les
///     compteurs de l'écran ne se recoupaient plus (en attente + terminées ≠ total)
///     et une réparation annulée repassée à une valeur libre était recomptée dans
///     les coûts.
///   • DEF-044 : main-d'œuvre, prix unitaire négatifs et quantité nulle étaient
///     acceptés ; le total négatif était DÉDUIT du poste « Réparations » des rapports.
///
/// Refus = <see cref="DomainException"/> (400 { message }) : le message est affiché
/// tel quel par les formulaires Réparations et Dépenses.
/// </summary>
public static class RepairInputRules
{
    public const string Pending = "pending";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";

    /// <summary>Plus grand montant que porte une colonne decimal(10,2) : au-delà, 500 à l'écriture.</summary>
    public const decimal MaxAmount = 99_999_999.99m;

    /// <summary>
    /// Les quatre statuts réellement écrits : écran Réparations (pending, in_progress,
    /// completed), import Excel (les mêmes + cancelled) et lectures des rapports, qui
    /// excluent « cancelled ».
    /// </summary>
    private static readonly (string Value, string Label)[] Statuses =
    {
        (Pending, "En attente"),
        (InProgress, "En cours"),
        (Completed, "Terminée"),
        (Cancelled, "Annulée"),
    };

    /// <summary>
    /// Statut reçu → valeur stockée en minuscules. La casse et les espaces autour sont
    /// ignorés : « Cancelled » existe dans des données anciennes et doit rester modifiable.
    /// </summary>
    public static string NormalizeStatus(string? status)
    {
        var key = (status ?? string.Empty).Trim().ToLowerInvariant();
        foreach (var (value, _) in Statuses)
        {
            if (value == key) return value;
        }

        var allowed = string.Join(", ", Statuses.Select(s => $"{s.Value} ({s.Label})"));
        var received = string.IsNullOrWhiteSpace(status) ? "vide" : $"« {status.Trim()} »";
        throw new DomainException($"Statut de réparation {received} invalide. Valeurs acceptées : {allowed}.");
    }

    /// <summary>
    /// Comparaison sans casse ni espaces : des statuts anciens (« Cancelled ») restent en base,
    /// les rapports les comparent déjà ainsi (OperatingCostAggregator).
    /// </summary>
    public static bool HasStatus(string? status, string expected) =>
        string.Equals(status?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Main-d'œuvre et prix unitaires ≥ 0, quantités > 0, totaux dans la capacité de la
    /// colonne. Le refus nomme le champ et, pour une pièce, son numéro de ligne et sa
    /// désignation : la saisie fautive se retrouve sans deviner.
    /// </summary>
    public static void EnsureAmounts(decimal laborCost, IReadOnlyList<CreateRepairPartRequest>? parts)
    {
        if (laborCost < 0)
            throw new DomainException("Main-d'œuvre : le montant ne peut pas être négatif.");
        if (laborCost > MaxAmount)
            throw new DomainException($"Main-d'œuvre : le montant ne peut pas dépasser {Amount(MaxAmount)}.");

        decimal partsCost = 0;
        var lines = parts ?? Array.Empty<CreateRepairPartRequest>();
        for (var i = 0; i < lines.Count; i++)
        {
            var part = lines[i];
            if (part is null)
                throw new DomainException($"{PartLabel(i, null)} : ligne vide.");
            if (part.Quantity <= 0)
                throw new DomainException($"{PartLabel(i, part.PartName)} : la quantité doit être un nombre entier supérieur à zéro.");
            if (part.UnitPrice < 0)
                throw new DomainException($"{PartLabel(i, part.PartName)} : le prix unitaire ne peut pas être négatif.");
            if (part.UnitPrice > MaxAmount || part.Quantity * part.UnitPrice > MaxAmount)
                throw new DomainException($"{PartLabel(i, part.PartName)} : le montant ne peut pas dépasser {Amount(MaxAmount)}.");

            partsCost += part.Quantity * part.UnitPrice;
        }

        if (partsCost > MaxAmount || laborCost + partsCost > MaxAmount)
            throw new DomainException($"Coût total : le montant ne peut pas dépasser {Amount(MaxAmount)}.");
    }

    private static string PartLabel(int index, string? partName) =>
        string.IsNullOrWhiteSpace(partName)
            ? $"Pièce n° {index + 1}"
            : $"Pièce n° {index + 1} (« {partName.Trim()} »)";

    private static string Amount(decimal value) =>
        value.ToString("N2", CultureInfo.GetCultureInfo("fr-FR"));
}
