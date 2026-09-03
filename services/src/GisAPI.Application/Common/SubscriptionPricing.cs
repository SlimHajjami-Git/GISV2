using GisAPI.Domain.Entities;

namespace GisAPI.Application.Common;

/// <summary>
/// Source unique de vérité du MONTANT dû par une société — utilisée par l'écran
/// client (/subscriptions/current), le bandeau facturation sys_admin et le
/// digest de supervision, pour que tous montrent EXACTEMENT le même chiffre.
///
/// Deux modèles cohabitent (recette client 01/09/2026) :
///  - plan FORFAITAIRE : la société porte la valeur qui fait foi
///    (NextPaymentAmount, figée au dernier règlement) ;
///  - plan tarifé PAR VÉHICULE (PricePerVehicle, offre GPA — 3 €/véhicule/mois
///    en annuel) : une valeur figée ment dès que le parc ou le tarif bouge, le
///    montant est donc recalculé À CHAQUE LECTURE : prix du cycle × parc réel
///    (au moins 1 véhicule — un parc vide n'affiche pas « 0,00 », qui
///    ressemblerait à un bug, mais le tarif d'entrée).
/// </summary>
public static class SubscriptionPricing
{
    /// <summary>Prix du cycle de facturation demandé, tel que porté par le plan
    /// (par véhicule si le plan est PricePerVehicle). Cycle inconnu → annuel,
    /// comme RenewSubscriptionCommandHandler.</summary>
    public static decimal CyclePrice(SubscriptionType plan, string? billingCycle) =>
        (billingCycle ?? "yearly").ToLowerInvariant() switch
        {
            "monthly" => plan.MonthlyPrice,
            "quarterly" => plan.QuarterlyPrice,
            "semiannual" => plan.SemiannualPrice,
            _ => plan.YearlyPrice
        };

    /// <summary>
    /// Montant dû par la société. <paramref name="vehicleCount"/> : parc réel de
    /// LA société (l'appelant le fournit — depuis un contexte admin, penser à
    /// IgnoreQueryFilters, le filtre de tenant ne pointe pas sur elle).
    /// </summary>
    public static decimal? AmountDue(Societe societe, int vehicleCount)
    {
        var plan = societe.SubscriptionType;
        if (plan == null || !plan.PricePerVehicle)
            return societe.NextPaymentAmount;

        return CyclePrice(plan, societe.BillingCycle) * Math.Max(1, vehicleCount);
    }
}
