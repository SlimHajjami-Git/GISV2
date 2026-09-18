namespace GisAPI.Application.Features.Documents;

/// <summary>
/// Échéances de document comptées en jours CALENDAIRES, définition unique de
/// la liste des échéances, des compteurs, des alertes et de la fiche véhicule.
///
/// <para>Constat (recette GPA, DEF-035) : une même échéance « au 20/09 » est
/// stockée à 00:00 (fiche véhicule, renouvellement), 12:00 (jeu de données) ou
/// 23:59:59 (ancienne correction d'échéance), et Npgsql en mode legacy
/// (Program.cs) relit les colonnes timestamptz en heure LOCALE du serveur. Sur
/// un serveur à UTC+1, 23:59:59 UTC devenait le lendemain 00:59:59 : l'écran
/// annonçait 8 jours au lieu de 7, et un document basculait « expiré » un jour
/// trop tard. Le jour d'une échéance est donc celui de l'instant stocké, lu en
/// UTC — l'heure et le fuseau du serveur n'entrent plus dans le compte.</para>
/// </summary>
public static class ExpiryCalendar
{
    public const string Expired = "expired";
    public const string ExpiringSoon = "expiring_soon";
    public const string Ok = "ok";
    public const string Unknown = "unknown";

    /// <summary>Seuil « bientôt » partagé par la liste, les compteurs et la fiche véhicule.</summary>
    public const int ExpiringSoonDays = 30;

    /// <summary>Jour du calendrier (UTC) d'une échéance, quelle que soit l'heure stockée.</summary>
    public static DateTime Day(DateTime stored) =>
        DateTime.SpecifyKind(
            (stored.Kind == DateTimeKind.Local ? stored.ToUniversalTime() : stored).Date,
            DateTimeKind.Utc);

    /// <summary>
    /// Échéance renvoyée par les écrans : le jour compté, à minuit UTC. Une
    /// ligne écrite à 23:59:59 par l'ancienne correction d'échéance
    /// s'affichait au lendemain dans un navigateur à UTC+1 (« 21/09 » pour
    /// « 7 jours » au 20/09) : la date montrée et les jours restants divergeaient.
    /// </summary>
    public static DateTime? Day(DateTime? stored) =>
        stored.HasValue ? Day(stored.Value) : null;

    /// <summary>Jours calendaires restants (négatif = expiré depuis ce nombre de jours).</summary>
    public static int DaysUntil(DateTime stored, DateTime today) =>
        (Day(stored) - today.Date).Days;

    public static string Status(DateTime? stored, DateTime today)
    {
        if (stored is null) return Unknown;
        var days = DaysUntil(stored.Value, today);
        return days < 0 ? Expired : days <= ExpiringSoonDays ? ExpiringSoon : Ok;
    }

    /// <summary>
    /// Rang d'affichage : expirés, puis bientôt, puis à jour, et les échéances
    /// non renseignées en dernier (DEF-055 : elles portent -1 jour et
    /// s'intercalaient avant les échéances à jour).
    /// </summary>
    public static int StatusRank(string status) => status switch
    {
        Expired => 0,
        ExpiringSoon => 1,
        Ok => 2,
        _ => 3
    };

    /// <summary>
    /// Valeur à enregistrer pour une échéance saisie au jour près : minuit UTC,
    /// comme la fiche véhicule et le renouvellement, pour que tous les chemins
    /// d'écriture stockent la même heure.
    /// </summary>
    public static DateTime ToStored(DateTime day) => Day(day);
}
