namespace GisAPI.Application.Common;

/// <summary>
/// Journée calendaire locale (minuit → minuit) exprimée en UTC.
///
/// <para>Les trames sont horodatées en UTC (<c>gps_positions.recorded_at</c>,
/// vérifié en production le 25/09/2026 : identique à <c>created_at</c> et à
/// <c>now()</c>), alors que « la journée » d'un exploitant tunisien commence à
/// minuit heure de Tunis, soit 23:00 UTC la veille. Sert au minimum journalier de
/// la batterie dans le monitoring.</para>
/// </summary>
public static class LocalDay
{
    /// <summary>
    /// Début (inclus) et fin (exclue) de la journée locale qui contient
    /// <paramref name="utcNow"/>, tous deux en <see cref="DateTimeKind.Utc"/>.
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtc) Window(DateTime utcNow, TimeZoneInfo tz)
    {
        var utc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        var localDate = TimeZoneInfo.ConvertTimeFromUtc(utc, tz).Date;
        return (ToUtc(localDate, tz), ToUtc(localDate.AddDays(1), tz));
    }

    private static DateTime ToUtc(DateTime localMidnight, TimeZoneInfo tz)
    {
        var unspecified = DateTime.SpecifyKind(localMidnight, DateTimeKind.Unspecified);
        // Un fuseau qui passe à l'heure d'été À minuit n'a pas de minuit ce jour-là :
        // la journée commence alors à la première heure qui existe.
        while (tz.IsInvalidTime(unspecified)) unspecified = unspecified.AddMinutes(30);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, tz);
    }
}
