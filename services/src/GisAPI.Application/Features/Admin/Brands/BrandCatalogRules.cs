using System.Text.RegularExpressions;

namespace GisAPI.Application.Features.Admin.Brands;

/// <summary>
/// Règles de saisie du référentiel marques / modèles, données GLOBALES partagées par
/// toutes les sociétés. Constat en base TN (14/09/2026) : noms à espaces parasites
/// (« ATECA  », « CUPRA », « Landtrek » précédé d'un espace) et 44 couples de modèles
/// homonymes pour une même marque, dont 34 tous deux actifs (« Hiace » 5 et 62…) — rien n'empêchait un second
/// clic ou une casse différente de créer un doublon. Ces règles sont pures (sans base)
/// pour être testées isolément.
/// </summary>
public static partial class BrandCatalogRules
{
    public const int MaxNameLength = 100;       // brands."Name" / vehicle_models."Name" : varchar(100)
    public const int MaxLogoUrlLength = 500;    // brands."LogoUrl" : varchar(500)

    /// <summary>
    /// Valeurs proposées par l'écran admin ET par le formulaire véhicule (le popup recopie
    /// le type du modèle choisi dans vehicles.type) : un type hors de cette liste n'y
    /// serait plus sélectionnable.
    /// </summary>
    public static readonly IReadOnlySet<string> FormVehicleTypes =
        new HashSet<string>(StringComparer.Ordinal) { "citadine", "suv", "utilitaire", "camion", "other" };

    /// <summary>
    /// Valeurs anglaises héritées du jeu de données initial (van, hatchback, sedan, truck,
    /// pickup : 93 modèles sur 127 en base TN). Acceptées telles quelles pour qu'une
    /// modification qui ne touche pas au type ne soit pas refusée ; les icônes de carte
    /// les reconnaissent déjà (vehicle-icons.ts).
    /// </summary>
    public static readonly IReadOnlySet<string> LegacyVehicleTypes =
        new HashSet<string>(StringComparer.Ordinal) { "van", "hatchback", "sedan", "truck", "pickup" };

    // Libellés de l'écran saisis à la main : ramenés à la valeur stockée.
    private static readonly IReadOnlyDictionary<string, string> VehicleTypeSynonyms =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["autre"] = "other" };

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>Supprime les espaces de bord et réduit les suites d'espaces internes à un seul. Null → chaîne vide.</summary>
    public static string NormalizeName(string? raw) =>
        raw is null ? string.Empty : Whitespace().Replace(raw, " ").Trim();

    /// <summary>
    /// Clé de comparaison des homonymes : nom nettoyé, insensible à la casse. « ATECA  »,
    /// « ateca » et « Ateca » désignent le même modèle.
    /// </summary>
    public static string NameKey(string? name) => NormalizeName(name).ToLowerInvariant();

    public static bool SameName(string? a, string? b) =>
        string.Equals(NameKey(a), NameKey(b), StringComparison.Ordinal);

    /// <summary>
    /// Nom à enregistrer lors d'une modification : le nom stocké est conservé tel quel,
    /// espaces parasites compris, tant que la saisie nettoyée lui est identique (casse
    /// comprise). Pourquoi : les véhicules recopient le nom du modèle en texte
    /// (vehicles.model) et le formulaire véhicule le retrouve par égalité sans trim ; en
    /// base TN, 7 véhicules portent « ATECA  », « CUPRA », « Tiguan » ou « Landtrek »
    /// avec leurs espaces. Nettoyer le nom à chaque enregistrement leur ferait perdre leur
    /// modèle à l'édition. Un vrai renommage (y compris de casse) écrit le nom nettoyé.
    /// </summary>
    public static string NameToStore(string currentName, string normalizedNewName) =>
        string.Equals(NormalizeName(currentName), normalizedNewName, StringComparison.Ordinal)
            ? currentName
            : normalizedNewName;

    /// <summary>Nom nettoyé, ou message d'erreur (vide, trop long).</summary>
    public static bool TryNormalizeName(string? raw, string label, out string name, out string? error)
    {
        name = NormalizeName(raw);
        error = null;
        if (name.Length == 0)
            error = $"Le nom {label} est obligatoire.";
        else if (name.Length > MaxNameLength)
            error = $"Le nom {label} ne peut pas dépasser {MaxNameLength} caractères.";
        return error is null;
    }

    /// <summary>URL de logo nettoyée (vide → null), ou message d'erreur si trop longue.</summary>
    public static bool TryNormalizeLogoUrl(string? raw, out string? logoUrl, out string? error)
    {
        var trimmed = raw?.Trim();
        logoUrl = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        error = logoUrl is { Length: > MaxLogoUrlLength }
            ? $"L'URL du logo ne peut pas dépasser {MaxLogoUrlLength} caractères."
            : null;
        return error is null;
    }

    /// <summary>
    /// Type de véhicule stocké comme les données existantes : minuscules, sans espaces
    /// parasites, vide → null. Un type inconnu est refusé plutôt que stocké, sinon le
    /// formulaire véhicule recevrait une valeur qu'il ne sait pas afficher.
    /// </summary>
    public static bool TryNormalizeVehicleType(string? raw, out string? vehicleType, out string? error)
    {
        vehicleType = null;
        error = null;

        var value = NormalizeName(raw).ToLowerInvariant();
        if (value.Length == 0)
            return true;

        if (VehicleTypeSynonyms.TryGetValue(value, out var canonical))
            value = canonical;

        if (!FormVehicleTypes.Contains(value) && !LegacyVehicleTypes.Contains(value))
        {
            error = $"Type de véhicule inconnu : « {NormalizeName(raw)} ».";
            return false;
        }

        vehicleType = value;
        return true;
    }
}
