namespace GisAPI.Domain.Common;

/// <summary>
/// Réglages de l'INSCRIPTION LIBRE, propres à chaque déploiement. Renseignés une
/// seule fois au démarrage depuis la section de configuration <c>Registration</c>
/// (variables d'environnement <c>Registration__*</c>) :
/// <list type="bullet">
///   <item>Algérie / DZ  → <c>Registration__SelfSignupEnabled=true</c></item>
///   <item>Tunisie / TN  → laissé à false : l'endpoint répond 404</item>
/// </list>
///
/// POURQUOI UN INTERRUPTEUR SERVEUR ET PAS SEULEMENT UN DRAPEAU D'ÉCRAN — masquer
/// le bouton côté Angular ne ferme rien : l'endpoint reste appelable directement.
/// Un déploiement qui ne vend pas l'inscription libre doit pouvoir la refuser au
/// niveau de l'API, sans changer d'image.
///
/// Statique mutable plutôt qu'injection, pour la même raison que
/// <see cref="AppCurrency"/> : les entités et les nombreux handlers MediatR le
/// lisent sans prendre de dépendance à IConfiguration.
/// </summary>
public static class AppRegistration
{
    /// <summary>L'API accepte-t-elle les inscriptions publiques ? Fermé par défaut.</summary>
    public static bool SelfSignupEnabled { get; set; } = false;

    /// <summary>
    /// Code du plan attribué à toute inscription. Résolu par Code (colonne à index
    /// unique), jamais « le premier plan actif venu » : sans cela l'inscrit reçoit
    /// un plan arbitraire, potentiellement le plus complet du déploiement.
    /// </summary>
    public static string DefaultPlanCode { get; set; } = "plan-basique";

    /// <summary>
    /// Durée de l'essai, en jours. Une inscription pose TOUJOURS une date
    /// d'expiration : une société sans échéance n'est jamais bloquée, n'apparaît
    /// pas dans la supervision, et rend l'écran de paiement décoratif.
    /// </summary>
    public static int TrialDays { get; set; } = 14;
}
