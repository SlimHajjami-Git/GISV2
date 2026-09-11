using System.Globalization;

namespace GisAPI.Application.Common;

/// <summary>
/// Heures silencieuses d'un utilisateur (recette client du 11/09/2026) : l'interrupteur
/// de Paramètres > Notifications n'écrivait que dans le localStorage et aucun envoi ne
/// le lisait. Règle unique partagée par la commande d'enregistrement (validation) et par
/// NotificationService (décision push / toast).
///
/// Les heures sont LOCALES à la société (Africa/Tunis, Africa/Algiers…) alors que
/// l'instant d'envoi est en UTC. La plage peut passer minuit (22:00 → 07:00).
/// Début inclus, fin EXCLUE : avec 22:00 → 07:00, 06:59 est silencieux, 07:00 ne l'est plus.
///
/// Logique de fenêtre et résolution du fuseau recopiées de
/// BroadcastPositionCommandHandler (IsScheduleActive / ResolveTimeZone), laissé intact.
/// </summary>
public static class QuietHoursPolicy
{
    /// <summary>
    /// Types livrés MÊME pendant la plage (réponse du client, recette du 11/09/2026) :
    /// un véhicule remorqué ou qui refuse de démarrer n'attend pas le matin. Les accidents
    /// passent déjà par leur priorité « critical ». Une échéance de document ou un entretien
    /// dû, même en priorité « high », attendent : ce n'est pas la priorité qui décide.
    /// </summary>
    public static readonly IReadOnlySet<string> AlwaysDeliveredTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tow_detected", "accident_tow_detected", "start_failure" };

    /// <summary>La notification ignore-t-elle les heures silencieuses ?</summary>
    public static bool BypassesQuietHours(string? type, string? priority) =>
        string.Equals(priority, "critical", StringComparison.OrdinalIgnoreCase)
        || (type != null && AlwaysDeliveredTypes.Contains(type));

    /// <summary>
    /// L'utilisateur est-il dans sa plage silencieuse à cet instant ?
    /// Désactivé, plage incomplète ou début == fin → jamais silencieux.
    /// </summary>
    public static bool IsQuietNow(bool enabled, TimeSpan? start, TimeSpan? end, DateTime utcNow, TimeZoneInfo tz)
    {
        if (!enabled || !start.HasValue || !end.HasValue) return false;
        var s = start.Value;
        var e = end.Value;
        if (s == e) return false;

        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), tz);
        var t = local.TimeOfDay;

        return s < e
            ? t >= s && t < e                // plage de jour : 12:00 → 14:00
            : t >= s || t < e;               // plage nocturne : de s à minuit ET de minuit à e
    }

    /// <summary>
    /// Fuseau de la société : id IANA de Societe.Settings.Timezone, repli Africa/Tunis ;
    /// tzdata absent du conteneur ou id inconnu → UTC+1 fixe (TN et DZ, toutes deux
    /// sans heure d'été).
    /// </summary>
    public static TimeZoneInfo ResolveTimeZone(string? ianaId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(
                string.IsNullOrWhiteSpace(ianaId) ? "Africa/Tunis" : ianaId);
        }
        catch
        {
            return TimeZoneInfo.CreateCustomTimeZone("UTC+1", TimeSpan.FromHours(1), "UTC+1", "UTC+1");
        }
    }

    /// <summary>
    /// Lit une heure « HH:mm » (00:00 à 23:59). Tolère « H:mm » et un suffixe
    /// « :ss » à zéro (valeur relue telle que /users/me la renvoie). Null si invalide.
    /// </summary>
    public static TimeSpan? ParseHourMinute(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (v.Length == 8 && v.EndsWith(":00", StringComparison.Ordinal)) v = v[..5];

        if (!TimeSpan.TryParseExact(v, new[] { @"hh\:mm", @"h\:mm" }, CultureInfo.InvariantCulture, out var ts))
            return null;
        if (ts < TimeSpan.Zero || ts >= TimeSpan.FromDays(1)) return null;
        return ts;
    }
}
