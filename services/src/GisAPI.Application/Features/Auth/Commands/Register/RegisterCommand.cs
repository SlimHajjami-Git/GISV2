using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Auth.Commands.Register;

/// <summary>
/// Inscription libre.
///
/// <para><b>AUCUN champ de plan d'abonnement</b> — le laisser choisir au visiteur
/// serait un libre-service. Le plan de départ vient de la configuration serveur
/// (<c>Registration:DefaultPlanCode</c>) et de nulle part ailleurs.</para>
///
/// <para><b>AccountType</b> — « particulier » ou « societe ». C'est la seule
/// question posée sur ce point : une société est créée dans les deux cas, parce
/// que tout le modèle est cloisonné par société, mais un particulier n'a ni à le
/// savoir ni à inventer un nom d'entreprise. <see cref="CompanyName"/> n'a de sens
/// que pour un professionnel ; pour un particulier il est ignoré.</para>
/// </summary>
public record RegisterCommand(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string? CompanyName,
    string? Phone,
    string AccountType = AccountTypes.Individual
) : ICommand<RegisterResult>;

/// <summary>Valeurs acceptées par <see cref="RegisterCommand.AccountType"/>.</summary>
public static class AccountTypes
{
    public const string Individual = "particulier";
    public const string Company = "societe";

    public static bool IsValid(string? value) =>
        value == Individual || value == Company;
}

/// <summary>
/// Réponse d'inscription. Volontairement SANS jeton de session : le compte naît en
/// attente de confirmation, l'utilisateur ne peut pas encore entrer. Renvoyer un
/// JWT ici viderait la confirmation d'adresse de son sens.
/// </summary>
public record RegisterResult(
    string Email,
    string Message,
    bool EmailSent);
