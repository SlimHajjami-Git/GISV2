using GisAPI.Application.Features.Reports.Common;

namespace GisAPI.Application.Features.DataPort;

/// <summary>
/// Feuille « Dépenses » du port Excel : toutes les dépenses de <c>vehicle_costs</c>
/// qui ne sont pas des entretiens (assurance, amende, vignette, péage…), et clés
/// de dédoublonnage des feuilles Entretiens, Carburant et Dépenses.
///
/// <para>Constat de la campagne de test Calypso GPA (DEF-002) : « Exporter mes
/// données » n'écrivait que les dépenses de type maintenance. L'assurance (625 €)
/// et l'amende (120 €) de la société de test n'apparaissaient dans aucune feuille :
/// un classeur présenté comme « mes données », dont le client se sert comme
/// sauvegarde ou pour quitter le service, perdait 745 € sans prévenir.</para>
///
/// <para>Et l'import ne dédoublonnait que les réparations : réimporter le classeur
/// exporté recréait en silence chaque entretien et chaque plein.</para>
/// </summary>
public static class ExpenseImportRow
{
    // Longueurs maximales des colonnes de vehicle_costs : une seule chaîne trop
    // longue ferait échouer le SaveChanges de TOUT l'import.
    public const int TypeMaxLength = 50;
    public const int DescriptionMaxLength = 500;
    public const int ReceiptNumberMaxLength = 100;

    /// <summary>Type retenu pour une cellule « Type » vide.</summary>
    public const string DefaultType = "autre";

    /// <summary>Plus grand montant que porte une colonne decimal(10,2).</summary>
    public const decimal MaxAmount = RepairImportRow.MaxAmount;

    /// <summary>
    /// Types connus : code stocké, libellé écrit par l'export (celui de l'écran
    /// Dépenses), puis les synonymes rencontrés en base ou saisis à la main.
    /// Un synonyme désigne la même dépense que le code : il se relit sous le code,
    /// et il se dédoublonne avec lui.
    /// </summary>
    private static readonly (string Code, string Label, string[] Synonyms)[] Types =
    {
        ("maintenance", "Entretien", new[] { "entretien" }),
        ("fuel", "Carburant", new[] { "carburant" }),
        ("reparation", "Réparation", new[] { "réparation" }),
        ("repair", "Réparation accident", Array.Empty<string>()),
        ("insurance", "Assurance", new[] { "assurance" }),
        ("insurance_refund", "Remboursement assurance", new[] { "remb. assurance" }),
        // Crédit comme le remboursement d'assurance (VehicleCostCategory), montant positif.
        (VehicleCostCategory.CreditNote, "Avoir fournisseur", new[] { "avoir", "credit note" }),
        ("technical_inspection", "Visite technique", new[] { "visite_technique" }),
        ("tax", "Vignette", new[] { "vignette", "taxe" }),
        ("registration", "Carte grise", Array.Empty<string>()),
        ("transport_permit", "Autorisation transport", Array.Empty<string>()),
        ("peage", "Péage", new[] { "toll" }),
        ("stationnement", "Stationnement", Array.Empty<string>()),
        ("parking", "Parking", Array.Empty<string>()),
        ("amende", "Amende", new[] { "fine" }),
        ("credit", "Crédit / Leasing", Array.Empty<string>()),
        ("achat", "Achat véhicule", Array.Empty<string>()),
        ("lavage", "Lavage", new[] { "wash" }),
        ("autre", "Autre", new[] { "autres", "other" }),
    };

    private static readonly Dictionary<string, (string Code, string Label)> ByKey = BuildIndex();

    /// <summary>
    /// Codes que l'application écrit tels quels dans vehicle_costs.type : ceux du tableau
    /// (écran Dépenses, Sinistres, renouvellement de documents, import) et « toll »,
    /// « fine », « other », catégories du scan de facture et de l'écran Coûts.
    /// </summary>
    private static readonly HashSet<string> WrittenCodes = new(
        Types.Select(t => t.Code).Concat(new[] { "toll", "fine", "other" }), StringComparer.Ordinal);

    /// <summary>
    /// Type à enregistrer pour une catégorie reçue par POST/PUT /api/costs (DEF-040),
    /// null si elle est inconnue. Un code écrit par l'application est gardé tel quel ;
    /// un synonyme ou un libellé connu (« carburant », « Entretien », « Carte grise »)
    /// est ramené à son code. Enregistré tel quel, « carburant » tombait en « Autres »
    /// dans les rapports, qui ne reconnaissent que « fuel ».
    /// </summary>
    public static string? StoredType(string? type)
    {
        var raw = (type ?? string.Empty).Trim();
        var code = raw.ToLowerInvariant();
        if (WrittenCodes.Contains(code)) return code;
        return raw.Length > 0 && ByKey.TryGetValue(RepairImportRow.NormalizeKey(raw), out var known)
            ? known.Code
            : null;
    }

    private static Dictionary<string, (string Code, string Label)> BuildIndex()
    {
        var index = new Dictionary<string, (string Code, string Label)>(StringComparer.Ordinal);
        foreach (var (code, label, synonyms) in Types)
            foreach (var word in synonyms.Append(code).Append(label))
                index.TryAdd(RepairImportRow.NormalizeKey(word), (code, label));
        return index;
    }

    /// <summary>
    /// Même partage que la feuille Entretiens de l'export (types maintenance et
    /// entretien, au mot près) : chaque dépense tombe dans UNE feuille, jamais deux.
    /// </summary>
    public static bool IsMaintenance(string? type) => type is "maintenance" or "entretien";

    /// <summary>Libellé écrit dans la colonne « Type » ; un type inconnu est écrit tel quel.</summary>
    public static string TypeLabel(string? type)
    {
        var raw = (type ?? string.Empty).Trim();
        return ByKey.TryGetValue(RepairImportRow.NormalizeKey(raw), out var known) ? known.Label : raw;
    }

    /// <summary>
    /// Type lu dans la cellule → code stocké. Libellé, code ou synonyme connus,
    /// casse et accents ignorés ; vide → « autre ». Un type inconnu est gardé tel
    /// quel avec une remarque : l'export écrit les codes qu'il ne connaît pas, et
    /// les remplacer par « autre » changerait la dépense au premier aller-retour.
    /// </summary>
    public static (string Type, string? Note) ParseType(string? value)
    {
        var raw = (value ?? string.Empty).Trim();
        if (raw.Length == 0) return (DefaultType, null);
        if (ByKey.TryGetValue(RepairImportRow.NormalizeKey(raw), out var known)) return (known.Code, null);

        var kept = RepairImportRow.Truncate(raw, TypeMaxLength)!;
        return (kept, $"type « {raw} » non reconnu, importé tel quel.");
    }

    /// <summary>Code de référence d'un type : ses synonymes y sont ramenés.</summary>
    public static string TypeFamily(string? type)
    {
        var raw = (type ?? string.Empty).Trim();
        return ByKey.TryGetValue(RepairImportRow.NormalizeKey(raw), out var known)
            ? known.Code
            : raw.ToLowerInvariant();
    }

    /// <summary>
    /// Clé naturelle d'une dépense (véhicule, jour, famille de type, montant,
    /// description sans casse ni accents). Partagée par les feuilles Entretiens et
    /// Dépenses : une ligne déjà en base — ou répétée dans le fichier — est ignorée.
    /// L'intitulé « Entretien » vaut une description vide : c'est ce que l'import
    /// enregistre pour un entretien sans intitulé, et réimporter le même fichier
    /// doit le reconnaître.
    /// </summary>
    public static string NaturalKey(int vehicleId, DateTime date, string? type, decimal amount, string? description)
    {
        var family = TypeFamily(type);
        var text = RepairImportRow.NormalizeKey(description);
        if (family == "maintenance" && text == "entretien") text = string.Empty;

        return FormattableString.Invariant(
            $"{vehicleId}|{date:yyyy-MM-dd}|{family}|{Math.Round(amount, 2, MidpointRounding.AwayFromZero):0.00}|{text}");
    }

    /// <summary>
    /// Clé naturelle d'un plein (véhicule, jour, volume, montant, compteur) : le même
    /// ticket réimporté est reconnu, deux pleins distincts du même jour ne le sont pas
    /// (leur compteur ou leur volume diffère).
    /// </summary>
    public static string FuelNaturalKey(int vehicleId, DateTime date, decimal volume, decimal totalAmount, long? odometerKm) =>
        FormattableString.Invariant(
            $"{vehicleId}|{date:yyyy-MM-dd}|{Math.Round(volume, 2, MidpointRounding.AwayFromZero):0.00}|{Math.Round(totalAmount, 2, MidpointRounding.AwayFromZero):0.00}|{(odometerKm is > 0 ? odometerKm.Value : 0)}");
}
