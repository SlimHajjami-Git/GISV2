using GisAPI.Domain.Exceptions;

namespace GisAPI.Application.Features.Reports.Common;

/// <summary>
/// Contrôles des paramètres communs aux rapports : mois demandé et période.
/// </summary>
public static class ReportRequestRules
{
    /// <summary>Même libellé que le tableau de bord GPA (DashboardController).</summary>
    public const string InvertedPeriodMessage = "La date de début doit précéder la date de fin.";

    public const int MinYear = 2000;
    public const int MaxYear = 2100;

    /// <summary>
    /// Refuse un mois hors 1..12 ou une année aberrante, en 400.
    ///
    /// <para>Constat de la recette du 16/09/2026 : month=13, 0 ou -1 et year=0
    /// faisaient lever <c>new DateTime(...)</c> dans les handlers mensuels, soit un
    /// 500 anonyme dans les journaux. Les listes déroulantes ne l'envoient pas, mais
    /// l'API est ouverte à tout intégrateur authentifié. Les bornes d'année écartent
    /// aussi les débordements des comparaisons (mois et année précédents).</para>
    /// </summary>
    public static void EnsureValidMonth(int year, int month)
    {
        if (month is < 1 or > 12)
            throw new DomainException("Mois invalide : il doit être compris entre 1 et 12.");
        if (year is < MinYear or > MaxYear)
            throw new DomainException($"Année invalide : elle doit être comprise entre {MinYear} et {MaxYear}.");
    }

    /// <summary>
    /// Période inversée (début après fin, au jour près). Une réponse vide serait
    /// indiscernable d'une période réellement sans dépense : le client concluait
    /// « aucune dépense » au lieu de corriger ses dates.
    /// </summary>
    public static bool IsInverted(DateTime start, DateTime end) => start.Date > end.Date;
}
