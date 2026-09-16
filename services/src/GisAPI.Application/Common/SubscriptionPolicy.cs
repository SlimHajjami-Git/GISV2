using GisAPI.Domain.Entities;

namespace GisAPI.Application.Common;

/// <summary>
/// Source unique de vérité de l'état d'abonnement d'une société — utilisée par le
/// middleware de blocage, le login/refresh, l'endpoint bannière et la supervision
/// sys_admin, pour que tous appliquent EXACTEMENT les mêmes règles :
///  - avertissement à J-30 (bannière ambre, admins société),
///  - urgence à J-7 (bannière rouge, tous les utilisateurs),
///  - expiration → période de GRÂCE de 7 jours (accès maintenu, bannière rouge),
///  - au-delà de la grâce, ou suspension manuelle par le sys_admin → accès bloqué.
/// </summary>
public static class SubscriptionPolicy
{
    public const int GraceDays = 7;
    public const int WarnDays = 30;
    public const int UrgentDays = 7;

    /// <summary>
    /// Level: none | warning | danger | blocked.
    /// Reason (si blocked): suspended | cancelled | expired.
    /// DaysRemaining: jours avant expiration (négatif = expiré depuis N jours).
    /// GraceDaysLeft: jours de grâce restants quand la date est dépassée.
    /// </summary>
    public record State(string Level, string Reason, DateTime? ExpiresAt, int? DaysRemaining, int? GraceDaysLeft)
    {
        public bool IsBlocked => Level == "blocked";
    }

    public static State Evaluate(Societe s, DateTime nowUtc) => Evaluate(s, nowUtc, TimeZoneInfo.Local);

    /// <summary>
    /// <paramref name="readZone"/> : fuseau dans lequel sont exprimées les dates
    /// Kind=Local (celui du serveur en production). Paramétrable pour que les tests
    /// reproduisent un poste hors UTC sur n'importe quelle machine.
    /// </summary>
    public static State Evaluate(Societe s, DateTime nowUtc, TimeZoneInfo readZone)
    {
        nowUtc = ToUtc(nowUtc, readZone);
        var expiresAt = s.SubscriptionExpiresAt is DateTime e ? ToUtc(e, readZone) : (DateTime?)null;

        // Suspension/annulation manuelle par le sys_admin — prioritaire sur les dates.
        if (s.SubscriptionStatus is "suspended" or "cancelled")
            return new State("blocked", s.SubscriptionStatus, expiresAt, DaysRemaining(expiresAt, nowUtc), null);

        // IsActive=false sans statut suspendu = désactivation manuelle (ceinture-bretelles).
        // Exception : les lignes historiques où l'ancien middleware posait IsActive=false
        // à l'expiration (statut "expired") suivent le circuit expiration/grâce ci-dessous.
        if (!s.IsActive && s.SubscriptionStatus != "expired")
            return new State("blocked", "suspended", expiresAt, DaysRemaining(expiresAt, nowUtc), null);

        if (expiresAt is not DateTime expires)
            return new State("none", "active", null, null, null);

        var days = DaysRemaining(expires, nowUtc)!.Value;
        if (expires <= nowUtc)
        {
            var graceLeft = GraceDays + days; // days est négatif après expiration
            if (graceLeft > 0)
                return new State("danger", "grace", expires, days, graceLeft);
            // Suspension automatique désactivée pour cette société (choix du
            // sys_admin) : expirée mais tolérée — bannière rouge permanente,
            // accès maintenu, marquée impayée côté supervision.
            return s.AutoSuspendEnabled
                ? new State("blocked", "expired", expires, days, 0)
                : new State("danger", "expired", expires, days, 0);
        }
        if (days <= UrgentDays) return new State("danger", "expiring", expires, days, null);
        if (days <= WarnDays) return new State("warning", "expiring", expires, days, null);
        return new State("none", "active", expires, days, null);
    }

    // Jour entamé = jour restant (arrondi supérieur), comme l'écran Abonnement.
    private static int? DaysRemaining(DateTime? expiresUtc, DateTime nowUtc) =>
        expiresUtc is DateTime e ? (int)Math.Ceiling((e - nowUtc).TotalDays) : null;

    // Npgsql en mode « legacy timestamp » (Program.cs) lit un timestamptz en heure
    // LOCALE du serveur. Soustraire UtcNow à cette heure locale ajoutait le décalage
    // du fuseau : 363 jours affichés au lieu de 362 sur un poste à UTC+1 (DEF-054),
    // et l'expiration comme la fin de grâce basculaient une heure trop tard.
    private static DateTime ToUtc(DateTime d, TimeZoneInfo readZone)
    {
        if (d.Kind != DateTimeKind.Local) return d;
        // Fuseau du serveur : ToUniversalTime garde l'indication d'heure ambiguë
        // posée par la lecture (changement d'heure), qu'un calcul par décalage perdrait.
        if (readZone.Equals(TimeZoneInfo.Local)) return d.ToUniversalTime();
        var wall = DateTime.SpecifyKind(d, DateTimeKind.Unspecified);
        return DateTime.SpecifyKind(wall - readZone.GetUtcOffset(wall), DateTimeKind.Utc);
    }
}
