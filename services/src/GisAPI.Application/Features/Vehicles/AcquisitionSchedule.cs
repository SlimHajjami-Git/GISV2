using GisAPI.Domain.Entities;

namespace GisAPI.Application.Features.Vehicles;

/// <summary>
/// Échéancier d'acquisition d'un véhicule — source unique de vérité, côté
/// serveur, des « achats véhicule » : mensualités de crédit/leasing échues et
/// apport ou achat comptant.
///
/// <para>Recette client du 04/09/2026 : le tableau de bord affichait un
/// « Coût total » hors achats véhicule, parce que les mensualités n'existaient
/// que dans le navigateur (synthétisées par l'écran Dépenses). Ce helper porte
/// EXACTEMENT les mêmes règles que ce générateur — jour de paiement plafonné
/// au 28, première mensualité le mois suivant quand le jour de paiement précède
/// la date de début, échéance comptée dès que sa date est atteinte — pour que
/// les deux écrans donnent le même chiffre. Toute évolution se fait ici ET
/// dans expenses.component.ts, jamais d'un seul côté.</para>
/// </summary>
public static class AcquisitionSchedule
{
    /// <summary>
    /// Dates d'échéance des mensualités d'un contrat de crédit/leasing, dans
    /// l'ordre. Vide si le véhicule n'est pas financé ou si le contrat est
    /// incomplet. Le type d'acquisition fait foi : un véhicule repassé en achat
    /// comptant qui garde des résidus de contrat ne génère rien.
    /// </summary>
    public static IEnumerable<(int Index, DateTime Due)> LeasingDues(Vehicle v)
    {
        if (v.AcquisitionType != "leasing"
            || !(v.LeasingMonthlyPayment > 0)
            || !(v.LeasingDurationMonths > 0)
            || v.LeasingStartDate is null)
            yield break;

        var start = v.LeasingStartDate.Value.Date;
        // Plafond au 28 : évite le débordement des mois courts (février).
        var day = Math.Clamp(v.LeasingPaymentDay ?? 1, 1, 28);
        // Un contrat signé le 18 avec prélèvement le 14 n'a pas d'échéance
        // le 14 du même mois, antérieure au contrat.
        var offset = day < start.Day ? 1 : 0;
        var firstOfMonth = new DateTime(start.Year, start.Month, 1);

        for (var i = 0; i < v.LeasingDurationMonths.Value; i++)
            yield return (i + 1, firstOfMonth.AddMonths(i + offset).AddDays(day - 1));
    }

    /// <summary>
    /// Coût d'acquisition sur la période [from, to] : mensualités dont la date
    /// est atteinte (bornée à aujourd'hui — l'avenir n'est pas une dépense) et
    /// apport ou achat comptant daté dans la période.
    /// </summary>
    public static decimal Cost(IEnumerable<Vehicle> vehicles, DateTime from, DateTime to, DateTime now)
    {
        var fromDate = from.Date;
        var toDate = to.Date;
        var cap = toDate < now.Date ? toDate : now.Date;
        decimal total = 0;

        foreach (var v in vehicles)
        {
            foreach (var (_, due) in LeasingDues(v))
            {
                if (due >= fromDate && due <= cap)
                    total += v.LeasingMonthlyPayment!.Value;
            }

            // Apport (contrat) ou prix d'achat (comptant) : une dépense datée
            // du jour de l'acquisition, quel que soit le mode de financement.
            if (v.PurchasePrice > 0 && v.PurchaseDate is { } purchased)
            {
                var pd = purchased.Date;
                if (pd >= fromDate && pd <= toDate && pd <= now.Date)
                    total += v.PurchasePrice.Value;
            }
        }

        return total;
    }
}
