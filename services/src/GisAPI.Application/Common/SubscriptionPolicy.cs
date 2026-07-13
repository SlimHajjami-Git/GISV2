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

    public static State Evaluate(Societe s, DateTime nowUtc)
    {
        // Suspension/annulation manuelle par le sys_admin — prioritaire sur les dates.
        if (s.SubscriptionStatus is "suspended" or "cancelled")
            return new State("blocked", s.SubscriptionStatus, s.SubscriptionExpiresAt, DaysRemaining(s, nowUtc), null);

        // IsActive=false sans statut suspendu = désactivation manuelle (ceinture-bretelles).
        // Exception : les lignes historiques où l'ancien middleware posait IsActive=false
        // à l'expiration (statut "expired") suivent le circuit expiration/grâce ci-dessous.
        if (!s.IsActive && s.SubscriptionStatus != "expired")
            return new State("blocked", "suspended", s.SubscriptionExpiresAt, DaysRemaining(s, nowUtc), null);

        if (s.SubscriptionExpiresAt is not DateTime expires)
            return new State("none", "active", null, null, null);

        var days = DaysRemaining(s, nowUtc)!.Value;
        if (expires <= nowUtc)
        {
            var graceLeft = GraceDays + days; // days est négatif après expiration
            return graceLeft > 0
                ? new State("danger", "grace", expires, days, graceLeft)
                : new State("blocked", "expired", expires, days, 0);
        }
        if (days <= UrgentDays) return new State("danger", "expiring", expires, days, null);
        if (days <= WarnDays) return new State("warning", "expiring", expires, days, null);
        return new State("none", "active", expires, days, null);
    }

    private static int? DaysRemaining(Societe s, DateTime nowUtc) =>
        s.SubscriptionExpiresAt is DateTime e ? (int)Math.Ceiling((e - nowUtc).TotalDays) : null;
}
