using System.Globalization;
using System.Text;

namespace GisAPI.Application.Features.Reports.Common;

/// <summary>
/// Type d'intervention d'une réparation, pour la répartition « par type » du
/// rapport « Fréquence des réparations ».
///
/// La table <c>repairs</c> n'a porté aucun type jusqu'à la migration 043
/// (colonne <c>repair_type</c>, nullable, jamais renseignée sur l'existant).
/// Pour que le donut ne soit pas vide sur les lignes historiques, le type est
/// DÉDUIT de la description par mots-clés quand la colonne est vide ; le DTO
/// signale alors <c>typeInferred = true</c> pour que l'écran affiche « (déduit) ».
/// </summary>
public static class RepairTypeClassifier
{
    public const string Electrique = "electrique";
    public const string Mecanique = "mecanique";
    public const string Freinage = "freinage";
    public const string Pneumatique = "pneumatique";
    public const string Carrosserie = "carrosserie";
    public const string Autre = "autre";

    public static readonly IReadOnlyList<string> KnownTypes = new[]
    {
        Electrique, Mecanique, Freinage, Pneumatique, Carrosserie, Autre
    };

    // Ordre d'évaluation = ordre du contrat : une description « plaquettes de
    // frein + roue » tombe en pneumatique (premier groupe qui matche).
    // « feu » est cherché avec une espace finale pour ne pas attraper « feuille »
    // ; la description normalisée est entourée d'espaces pour qu'un mot en fin de
    // chaîne matche aussi.
    private static readonly (string Type, string[] Keywords)[] Rules =
    {
        (Pneumatique, new[] { "pneu", "crevaison", "jante", "roue" }),
        (Freinage,    new[] { "frein", "plaquette", "disque", "etrier" }),
        (Electrique,  new[] { "ampoule", "feu ", "phare", "batterie", "electr", "alternateur", "demarreur",
                              "sonde", "capteur", "fusible", "faisceau", "lampe" }),
        (Mecanique,   new[] { "moteur", "injection", "courroie", "embrayage", "vidange", "distribution", "boite",
                              "amortisseur", "echappement", "radiateur", "joint", "huile", "filtre" }),
        (Carrosserie, new[] { "carrosserie", "pare-choc", "tole", "peinture", "rayure", "vitre", "pare-brise",
                              "retroviseur", "essuie", "portiere" }),
    };

    public static string Label(string type) => type switch
    {
        Electrique => "Électrique",
        Mecanique => "Mécanique",
        Freinage => "Freinage",
        Pneumatique => "Pneumatique",
        Carrosserie => "Carrosserie",
        Autre => "Autres",
        _ => string.IsNullOrWhiteSpace(type) ? "Autres" : char.ToUpperInvariant(type[0]) + type[1..]
    };

    /// <summary>
    /// Type retenu et indicateur « déduit de la description ».
    /// Un <paramref name="repairType"/> renseigné prime toujours (saisi par
    /// l'exploitant) ; sinon les mots-clés ; sinon <see cref="Autre"/>.
    /// </summary>
    public static (string Type, bool Inferred) Classify(string? repairType, string? description)
    {
        var explicitType = Normalize(repairType).Trim();
        if (explicitType.Length > 0)
            return (explicitType, false);

        var text = Normalize(description);
        if (text.Length == 0)
            return (Autre, true);

        var padded = $" {text} ";
        foreach (var (type, keywords) in Rules)
        {
            if (keywords.Any(k => padded.Contains(k, StringComparison.Ordinal)))
                return (type, true);
        }

        return (Autre, true);
    }

    /// <summary>Minuscules, sans accents, espaces multiples réduites.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsWhiteSpace(ch) ? ' ' : char.ToLowerInvariant(ch));
        }

        var collapsed = sb.ToString().Normalize(NormalizationForm.FormC);
        while (collapsed.Contains("  ", StringComparison.Ordinal))
            collapsed = collapsed.Replace("  ", " ", StringComparison.Ordinal);
        return collapsed.Trim();
    }
}
