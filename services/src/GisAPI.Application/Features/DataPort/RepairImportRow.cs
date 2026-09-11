using GisAPI.Application.Features.Repairs.Handlers;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Domain.Entities;

namespace GisAPI.Application.Features.DataPort;

/// <summary>
/// Résultat de l'analyse d'une ligne de la feuille « Réparations » de l'import Excel.
/// <see cref="Error"/> non nul = ligne écartée ; <see cref="Notes"/> = remarques à
/// remonter au client sans rejeter la ligne.
/// </summary>
public sealed record RepairImportAnalysis(
    decimal LaborCost,
    decimal PartsCost,
    decimal TotalCost,
    string Status,
    string? RepairType,
    string? Error,
    IReadOnlyList<string> Notes)
{
    public bool IsValid => Error is null;

    /// <summary>
    /// Une réparation annulée ne décrit aucun passage à l'atelier : son relevé ne
    /// fait pas avancer le compteur (même règle que <c>OdometerReadings</c>).
    /// </summary>
    public bool AdvancesMileage => Status != RepairImportRow.Cancelled;
}

/// <summary>
/// Analyse PURE d'une ligne « Réparations » de l'import Excel (montants, statut,
/// type), extraite du contrôleur pour être testée sans base.
///
/// <para>Constat de recette du 11/09/2026 : le port Excel ne connaissait que trois
/// feuilles (Véhicules, Entretiens, Carburant) ; les réparations — pourtant
/// saisies depuis les écrans Dépenses et Réparations — n'étaient ni exportées ni
/// importables. Le client qui démarre son parc sans boîtier ne pouvait donc pas
/// reprendre son historique d'atelier.</para>
///
/// <para>Règles de montants :</para>
/// <list type="bullet">
///   <item>Total seul (main d'œuvre et pièces vides ou à zéro) → tout en main
///     d'œuvre. C'est la forme des réparations réelles de la société 14 : un
///     montant global, sans détail.</item>
///   <item>Un seul des deux postes vide → il est déduit du total saisi.</item>
///   <item>Sinon total = main d'œuvre + pièces, avec une remarque si le total
///     saisi en diffère de plus d'un centime.</item>
/// </list>
/// </summary>
public static class RepairImportRow
{
    public const string Pending = "pending";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";

    /// <summary>Libellé de la ligne de pièce technique créée à l'import.</summary>
    public const string ImportedPartName = "Pièces (import Excel)";

    // Longueurs maximales des colonnes (MaxLength de l'entité Repair) : une seule
    // chaîne trop longue ferait échouer le SaveChanges de TOUT l'import.
    public const int DescriptionMaxLength = 500;
    public const int InvoiceNumberMaxLength = 100;
    public const int ReferenceMaxLength = 100;

    /// <summary>Plus grand montant que porte une colonne decimal(10,2).</summary>
    public const decimal MaxAmount = 99_999_999.99m;

    private const decimal Tolerance = 0.01m;

    private static readonly HashSet<string> CompletedWords = new(StringComparer.Ordinal)
    { "completed", "complete", "done", "termine", "terminee", "fait", "faite", "realise", "realisee" };
    private static readonly HashSet<string> InProgressWords = new(StringComparer.Ordinal)
    { "in progress", "en cours", "encours" };
    private static readonly HashSet<string> PendingWords = new(StringComparer.Ordinal)
    { "pending", "en attente", "attente", "a faire" };
    private static readonly HashSet<string> CancelledWords = new(StringComparer.Ordinal)
    { "cancelled", "canceled", "annule", "annulee" };

    /// <summary>
    /// Analyse les montants, le statut et le type d'une ligne.
    /// Les montants sont ceux lus dans les cellules (null = cellule vide ou illisible).
    /// </summary>
    public static RepairImportAnalysis Analyze(
        decimal? labor, decimal? parts, decimal? total, string? status, string? repairType)
    {
        var notes = new List<string>();

        var (resolvedStatus, statusNote) = ParseStatus(status);
        if (statusNote != null) notes.Add(statusNote);

        var (type, typeNote) = ParseType(repairType);
        if (typeNote != null) notes.Add(typeNote);

        if (labor < 0 || parts < 0 || total < 0)
            return Rejected(resolvedStatus, type, notes, "montant négatif.");

        decimal l, p;
        var laborZero = (labor ?? 0) == 0;
        var partsZero = (parts ?? 0) == 0;

        if (laborZero && partsZero && total is decimal totalOnly && totalOnly > 0)
        {
            // Total seul : le montant global passe en main d'œuvre.
            l = totalOnly;
            p = 0;
        }
        else if (labor is null && parts is decimal knownParts && total is decimal t1 && t1 >= knownParts)
        {
            // Main d'œuvre vide : c'est ce qui reste du total une fois les pièces payées.
            l = t1 - knownParts;
            p = knownParts;
        }
        else if (parts is null && labor is decimal knownLabor && total is decimal t2 && t2 >= knownLabor)
        {
            // Pièces vides : même raisonnement dans l'autre sens.
            l = knownLabor;
            p = t2 - knownLabor;
        }
        else
        {
            l = labor ?? 0;
            p = parts ?? 0;
            var sum = l + p;
            if (total is decimal entered && Math.Abs(entered - sum) > Tolerance)
                notes.Add($"total saisi ({entered:N2}) différent de main d'œuvre + pièces ({sum:N2}) : {sum:N2} retenu.");
        }

        l = Math.Round(l, 2, MidpointRounding.AwayFromZero);
        p = Math.Round(p, 2, MidpointRounding.AwayFromZero);
        var t = l + p;

        if (l > MaxAmount || p > MaxAmount || t > MaxAmount)
            return Rejected(resolvedStatus, type, notes, "montant trop élevé.");

        return new RepairImportAnalysis(l, p, t, resolvedStatus, type, null, notes);
    }

    /// <summary>
    /// Ligne de pièce à rattacher à la réparation importée, ou null s'il n'y a pas
    /// de pièces. Sans elle, la première modification à l'écran remettrait le coût
    /// des pièces à zéro : UpdateRepairCommandHandler le recalcule à partir des
    /// lignes de pièces envoyées.
    /// </summary>
    public static RepairPart? PartLine(RepairImportAnalysis analysis)
    {
        if (analysis.PartsCost <= 0) return null;
        return new RepairPart
        {
            PartName = ImportedPartName,
            Quantity = 1,
            UnitPrice = analysis.PartsCost,
            Subtotal = analysis.PartsCost
        };
    }

    /// <summary>
    /// Statut saisi (français ou anglais, casse et accents ignorés) → valeur stockée.
    /// Vide → « completed », le défaut de la saisie à l'écran ; inconnu → « completed »
    /// avec une remarque.
    /// </summary>
    public static (string Status, string? Note) ParseStatus(string? value)
    {
        var key = NormalizeKey(value).Replace('_', ' ').Replace('-', ' ');
        while (key.Contains("  ", StringComparison.Ordinal))
            key = key.Replace("  ", " ", StringComparison.Ordinal);
        key = key.Trim();

        if (key.Length == 0 || CompletedWords.Contains(key)) return (Completed, null);
        if (InProgressWords.Contains(key)) return (InProgress, null);
        if (PendingWords.Contains(key)) return (Pending, null);
        if (CancelledWords.Contains(key)) return (Cancelled, null);
        return (Completed, $"statut « {value!.Trim()} » inconnu, « Terminée » retenu.");
    }

    /// <summary>Libellé français d'un statut, écrit par l'export et relu par l'import.</summary>
    public static string StatusLabel(string? status) => (status ?? "").Trim().ToLowerInvariant() switch
    {
        Pending => "En attente",
        InProgress => "En cours",
        Completed => "Terminée",
        Cancelled => "Annulée",
        _ => (status ?? "").Trim()
    };

    /// <summary>
    /// Type saisi → l'un des six types connus, même règle que la saisie à l'écran
    /// (vide → null, hors liste → « autre »). « Autres », libellé écrit par l'export,
    /// est relu comme « autre » sans remarque.
    /// </summary>
    public static (string? Type, string? Note) ParseType(string? value)
    {
        var key = NormalizeKey(value);
        if (key.Length == 0) return (null, null);
        if (key == "autres") return (RepairTypeClassifier.Autre, null);

        var type = CreateRepairCommandHandler.NormalizeRepairType(value);
        if (type == RepairTypeClassifier.Autre && key != RepairTypeClassifier.Autre)
            return (type, $"type « {value!.Trim()} » inconnu, « Autres » retenu.");
        return (type, null);
    }

    /// <summary>
    /// Clé naturelle d'une réparation (véhicule, jour, total, description sans casse
    /// ni accents) : une ligne déjà présente en base — ou deux fois dans le fichier —
    /// est ignorée même sans référence, ce qui rend idempotente la boucle
    /// exporter → compléter → réimporter.
    /// </summary>
    public static string NaturalKey(int vehicleId, DateTime date, decimal totalCost, string? description) =>
        FormattableString.Invariant(
            $"{vehicleId}|{date:yyyy-MM-dd}|{Math.Round(totalCost, 2, MidpointRounding.AwayFromZero):0.00}|{NormalizeKey(description)}");

    /// <summary>Référence interne, même format que la saisie à l'écran (REP-aaaamm-nnnn).</summary>
    public static string GeneratedReference(DateTime nowUtc, int sequence) =>
        FormattableString.Invariant($"REP-{nowUtc:yyyyMM}-{sequence:D4}");

    /// <summary>Minuscules, sans accents, espaces réduites (noms de fournisseur, de feuille…).</summary>
    public static string NormalizeKey(string? value) => RepairTypeClassifier.Normalize(value);

    /// <summary>Coupe à maxLength sans jamais séparer une paire de substitution (emoji) :
    /// une moitié orpheline est refusée par PostgreSQL et ferait échouer tout l'import.</summary>
    public static string? Truncate(string? value, int maxLength)
    {
        if (value is null || value.Length <= maxLength) return value;
        var cut = char.IsHighSurrogate(value[maxLength - 1]) ? maxLength - 1 : maxLength;
        return value[..cut];
    }

    private static RepairImportAnalysis Rejected(string status, string? type, List<string> notes, string error) =>
        new(0, 0, 0, status, type, error, notes);
}
