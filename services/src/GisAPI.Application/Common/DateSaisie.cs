namespace GisAPI.Application.Common;

/// <summary>
/// Date d'une dépense ou d'un plein : un fait passé, jamais dans le futur. Une dépense et un
/// plein datés de juin 2027 étaient enregistrés sans un mot et comptaient dans les totaux de
/// l'année suivante (campagne de tests GPA du 06/10/2026, erreur n°13).
///
/// Jour calendaire, avec UN jour de tolérance : une saisie faite peu après minuit dans un
/// fuseau en avance sur UTC (Tunisie, France) porte déjà la date du lendemain en UTC.
/// </summary>
public static class DateSaisie
{
    /// <summary>
    /// Motif de refus si <paramref name="date"/> est après demain (UTC), null sinon.
    /// <paramref name="quoi"/> complète la phrase : « de la dépense », « du plein ».
    /// </summary>
    public static string? ErreurDateFuture(DateTime date, string quoi, DateTime? maintenantUtc = null)
    {
        var aujourdhui = (maintenantUtc ?? DateTime.UtcNow).Date;
        var jour = (date.Kind == DateTimeKind.Local ? date.ToUniversalTime() : date).Date;
        return jour > aujourdhui.AddDays(1)
            ? $"La date {quoi} ne peut pas être dans le futur : au plus tard aujourd'hui ({aujourdhui:dd/MM/yyyy})."
            : null;
    }
}
