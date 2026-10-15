using System.Globalization;
using System.Text;

namespace GisAPI.Application.Features.VehicleMaintenance;

/// <summary>
/// « Même type d'entretien » entre deux modèles aux noms différents (campagne de tests GPA du
/// 06/10/2026, erreur n°17) : « Vidange + filtre à huile » et « Vidange Moteur » affectés au
/// même véhicule faisaient deux échéances, et la vidange faite sur l'une laissait l'autre
/// « en retard ». La catégorie ne suffit pas (« Bougie » et « Vidange » sont toutes deux
/// « Moteur » à 10 000 km) ; les noms réels de production varient (« VIDANGE MOTEUR HUILE + FIL
/// HUILE », « CHANGMENT PNEUS »…).
///
/// <para>Règle : le premier mot significatif du nom, sans accents ni majuscules ni pluriel, en
/// sautant les mots génériques (« changement », « kit »…) et les nombres ; « filtre » garde le mot
/// qui le suit (filtre à air ≠ filtre à essence). Même règle dans l'écran Entretiens
/// (maintenance-template-family.ts), qui demande confirmation avant d'envoyer.</para>
/// </summary>
public static class MaintenanceTemplateFamily
{
    private static readonly HashSet<string> MotsSansSens = new(StringComparer.Ordinal)
    {
        "changement", "changment", "remplacement", "kit", "entretien",
        "de", "du", "des", "d", "la", "le", "les", "l", "a", "et", "en", "au", "aux", "pour"
    };

    /// <summary>Famille du modèle, ou chaîne vide si le nom n'en donne aucune (« 46564546 »).</summary>
    public static string Of(string? name)
    {
        var mots = Mots(name).Where(m => !MotsSansSens.Contains(m) && !m.All(char.IsDigit)).ToList();
        if (mots.Count == 0) return string.Empty;
        return mots[0] == "filtre" && mots.Count > 1 ? $"filtre {mots[1]}" : mots[0];
    }

    public static bool SameFamily(string? a, string? b)
    {
        var fa = Of(a);
        return fa.Length > 0 && fa == Of(b);
    }

    private static IEnumerable<string> Mots(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) yield break;
        var sansAccents = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sansAccents.Append(char.ToLowerInvariant(c));

        var mot = new StringBuilder();
        foreach (var c in sansAccents.ToString() + " ")
        {
            if (char.IsLetterOrDigit(c)) { mot.Append(c); continue; }
            if (mot.Length == 0) continue;
            var m = mot.ToString();
            mot.Clear();
            // Pluriel simple : « pneus », « freins », « filtres », « plaquettes ».
            yield return m.Length > 3 && m.EndsWith('s') ? m[..^1] : m;
        }
    }
}
