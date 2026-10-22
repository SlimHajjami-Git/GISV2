using System.Globalization;
using GisAPI.Application.Features.Auth.Commands.Login;
using GisAPI.Domain.Entities;

namespace GisAPI.Application.Common;

/// <summary>
/// Rappels d'échéance envoyés au CLIENT, par e-mail et dans la cloche (préparation du
/// paiement en ligne, 19/10/2026). Jusque-là seul le résumé quotidien des
/// administrateurs de la plateforme existait : le client découvrait la fin de son
/// abonnement quand l'accès se bloquait.
///
/// <para>Seulement les sociétés en LIBRE-SERVICE (offre GPA) : les clients installés par
/// Belive sont facturés par Belive. Trois étapes, une fois chacune par échéance :</para>
/// <list type="bullet">
///   <item><description>rappel : 7 jours avant l'échéance — 2 jours avant la fin d'un
///     essai gratuit, qui ne dure que 7 jours (« 7 jours avant » tomberait le jour de
///     l'inscription) ;</description></item>
///   <item><description>échéance : le jour même, avec la date de suspension ;</description></item>
///   <item><description>suspension : à la fin des 7 jours de grâce.</description></item>
/// </list>
/// <para>Chaque étape part le jour où elle tombe, ou le lendemain si le serveur était
/// arrêté, JAMAIS en rattrapage : une mise en service n'envoie pas de rappels périmés.
/// Les jours sont comptés comme <see cref="SubscriptionPolicy"/> (jour entamé = jour
/// restant), donc comme l'écran Abonnement et le bandeau.</para>
/// </summary>
public static class SubscriptionReminders
{
    public const string Rappel = "rappel";
    public const string Echeance = "echeance";
    public const string Suspension = "suspension";

    /// <summary>Type de la notification : préfixe + étape. Sert aussi à n'envoyer chaque étape qu'une fois.</summary>
    public const string NotificationTypePrefix = "subscription_reminder_";

    /// <summary>Type d'e-mail : libellé « Abonnement » et bouton « Renouveler mon abonnement » (EmailService).</summary>
    public const string EmailType = "subscription_reminder";

    public const string ActionUrl = "/subscription";

    /// <summary>Jours avant l'échéance du rappel (abonnement, essai gratuit).</summary>
    public const int RappelDays = 7;
    public const int RappelTrialDays = 2;

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public record Reminder(string Step, string Title, string Message, string Priority, DateTime ExpiresAtUtc);

    /// <summary>
    /// Essai gratuit : offre en libre-service, rien n'a jamais été réglé, et l'échéance a
    /// la taille d'un essai (31 jours au plus depuis le départ) — sans cette borne, un
    /// client GPA installé à la main avec un an d'échéance (règlements hors application)
    /// serait étiqueté « essai » à vie. Même règle que l'écran Abonnement.
    /// </summary>
    public static bool IsTrial(Societe s) =>
        LoginCommandHandler.IsSelfServiceSubscription(s)
        && s.LastPaymentAt == null
        && s.SubscriptionExpiresAt.HasValue
        && (s.SubscriptionExpiresAt.Value - s.SubscriptionStartedAt).TotalDays <= 31;

    public static Reminder? Due(Societe s, DateTime nowUtc) => Due(s, nowUtc, TimeZoneInfo.Local);

    /// <summary>L'étape à envoyer aujourd'hui pour cette société, ou null.</summary>
    public static Reminder? Due(Societe s, DateTime nowUtc, TimeZoneInfo readZone)
    {
        if (!LoginCommandHandler.IsSelfServiceSubscription(s)) return null;

        var state = SubscriptionPolicy.Evaluate(s, nowUtc, readZone);
        // Suspension ou résiliation par Belive : rien à rappeler au client.
        if (state.Reason is "suspended" or "cancelled") return null;
        if (state.ExpiresAt is not DateTime expires || state.DaysRemaining is not int days) return null;

        var trial = IsTrial(s);
        var zone = QuietHoursPolicy.ResolveTimeZone(s.Settings?.Timezone);
        string Date(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, zone).ToString("d MMMM yyyy", Fr);
        var what = trial ? "Votre essai gratuit" : "Votre abonnement";
        var renew = trial ? "choisir une formule" : "le renouveler";

        var rappel = trial ? RappelTrialDays : RappelDays;
        if (state.Reason == "expiring" && (days == rappel || days == rappel - 1))
            return new Reminder(Rappel,
                $"{what} se termine le {Date(expires)}",
                trial
                    ? "Pour continuer sans interruption et garder vos données, choisissez une formule depuis l'écran Abonnement."
                    : "Pour continuer sans interruption, renouvelez-le avant cette date depuis l'écran Abonnement.",
                "normal", expires);

        // Échue depuis moins de deux jours (DaysRemaining vaut 0 puis -1).
        if (state.Reason is "grace" or "expired" && (days == 0 || days == -1))
            return new Reminder(Echeance,
                trial ? "Votre essai gratuit est terminé" : "Votre abonnement est arrivé à échéance",
                s.AutoSuspendEnabled
                    ? $"Il vous reste {SubscriptionPolicy.GraceDays} jours pour {renew}. Passé ce délai, l'accès de "
                      + $"votre société sera suspendu le {Date(expires.AddDays(SubscriptionPolicy.GraceDays))}. "
                      + "Rendez-vous sur l'écran Abonnement."
                    : $"Pour continuer à utiliser l'application, pensez à {renew} depuis l'écran Abonnement.",
                "high", expires);

        // Grâce écoulée : bloquée depuis moins de deux jours (DaysRemaining -7 puis -8).
        if (state.IsBlocked && state.Reason == "expired"
            && (days == -SubscriptionPolicy.GraceDays || days == -SubscriptionPolicy.GraceDays - 1))
            return new Reminder(Suspension,
                "L'accès de votre société est suspendu",
                $"{what} a pris fin le {Date(expires)} et le délai de {SubscriptionPolicy.GraceDays} jours est écoulé. "
                + (trial ? "Choisissez une formule" : "Renouvelez votre abonnement")
                + " depuis l'écran Abonnement : l'accès sera rétabli dès la validation de votre règlement.",
                "high", expires);

        return null;
    }
}
