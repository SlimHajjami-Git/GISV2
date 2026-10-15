namespace GisAPI.Application.Features.Vehicles;

/// <summary>
/// Champs de la fiche véhicule que l'écran exige ou borne, contrôlés aussi par l'API : elle
/// acceptait une fiche sans plaque, sans carburant ou avec l'année 1800 (campagne de tests GPA
/// du 06/10/2026, erreur n°15). Mêmes règles pour la création, la modification et la
/// modification partielle.
/// </summary>
public static class VehicleInputRules
{
    public const int AnneeMin = 1900;

    /// <summary>Année du modèle de l'an prochain au plus (les modèles sortent en avance).</summary>
    public static int AnneeMax => DateTime.UtcNow.Year + 1;

    public static bool AnneeValide(int? annee) => annee is null || (annee >= AnneeMin && annee <= AnneeMax);

    public static string MessageAnnee => $"L'année doit être comprise entre {AnneeMin} et {AnneeMax}.";

    public const string MessagePlaque = "La plaque d'immatriculation est obligatoire.";
    public const string MessagePlaqueLongue = "La plaque d'immatriculation ne doit pas dépasser 20 caractères.";
    public const string MessageCarburant = "Le type de carburant est obligatoire.";
}
