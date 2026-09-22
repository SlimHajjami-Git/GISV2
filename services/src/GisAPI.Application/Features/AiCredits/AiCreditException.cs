using System.Globalization;
using GisAPI.Domain.Exceptions;

namespace GisAPI.Application.Features.AiCredits;

/// <summary>
/// Refus d'un appel payant à l'IA faute de crédit, levé par
/// <see cref="AiCredit.EnsureAvailableAsync"/> AVANT l'appel — depuis un contrôleur comme
/// depuis une requête MediatR (explication de consommation). ExceptionHandlingMiddleware le
/// rend tel quel : <c>{ code, message, credit }</c> en 403 (IA désactivée) ou 429 (crédit
/// du mois épuisé), et l'écran affiche le message du serveur et redessine sa barre.
///
/// <para>Dérivée de <see cref="DomainException"/> : un chemin qui ne la connaîtrait pas la
/// rendrait au pire en 400 avec son message, jamais en 500 « unexpected error ».</para>
/// </summary>
public sealed class AiCreditException : DomainException
{
    public const string DisabledCode = "AI_CREDIT_DISABLED";
    public const string ExhaustedCode = "AI_CREDIT_EXHAUSTED";

    public const string DisabledMessage = "Les fonctions d'IA ne sont pas activées pour votre société.";

    /// <summary>Code lu par l'écran (AI_CREDIT_DISABLED / AI_CREDIT_EXHAUSTED).</summary>
    public string Code { get; }

    /// <summary>Statut HTTP : 403 (désactivée) ou 429 (épuisé).</summary>
    public int StatusCode { get; }

    /// <summary>Crédit du moment, joint au refus pour que la barre passe à 100 % et que les
    /// boutons se grisent sur une page ouverte avant l'épuisement.</summary>
    public AiCreditStatus Credit { get; }

    private AiCreditException(string code, int statusCode, string message, AiCreditStatus credit)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
        Credit = credit;
    }

    public static AiCreditException Disabled(AiCreditStatus credit) =>
        new(DisabledCode, 403, DisabledMessage, credit);

    public static AiCreditException Exhausted(AiCreditStatus credit) =>
        new(ExhaustedCode, 429, ExhaustedMessage(credit.ResetsAt), credit);

    /// <summary>
    /// « Crédit IA du mois épuisé (100 %). Il se recharge le 01/10/2026 ; votre administrateur
    /// peut l'augmenter. » Date écrite en culture invariante : « / » dans un format .NET est
    /// le séparateur de la culture du serveur, pas forcément une barre oblique.
    /// </summary>
    public static string ExhaustedMessage(DateTime resetsAt) =>
        "Crédit IA du mois épuisé (100 %). Il se recharge le "
        + resetsAt.ToString("dd'/'MM'/'yyyy", CultureInfo.InvariantCulture)
        + " ; votre administrateur peut l'augmenter.";
}
