using FluentAssertions;
using GisAPI.Application.Features.Vehicles;
using GisAPI.Domain.Entities;
using Xunit;

namespace GisAPI.Tests.Application.Vehicles;

/// <summary>
/// L'échéancier d'acquisition côté serveur doit donner EXACTEMENT ce que
/// l'écran Dépenses synthétise dans le navigateur (expenses.component.ts) :
/// c'est lui qui alimente le « Coût total » du tableau de bord. Les cas sont
/// les quatre contrats réels de la société de recette au 04/09/2026.
/// </summary>
public class AcquisitionScheduleTests
{
    private static readonly DateTime Now = new(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);

    private static Vehicle Leasing(decimal monthly, int months, DateTime start, int payDay,
                                   decimal? deposit = null, DateTime? purchased = null) => new()
    {
        AcquisitionType = "leasing",
        LeasingMonthlyPayment = monthly,
        LeasingDurationMonths = months,
        LeasingStartDate = start,
        LeasingPaymentDay = payDay,
        PurchasePrice = deposit,
        PurchaseDate = purchased
    };

    [Fact]
    public void Contrat_signe_le_4_avec_prelevement_le_3_commence_le_mois_suivant()
    {
        // 225 TU 4836 : 720 × 36 dès le 04/09/2025, jour 3 → 1re échéance le 03/10/2025.
        var v = Leasing(720, 36, new DateTime(2025, 9, 4), 3);

        var dues = AcquisitionSchedule.LeasingDues(v).ToList();

        dues.Should().HaveCount(36);
        dues[0].Due.Should().Be(new DateTime(2025, 10, 3));
        dues.Count(d => d.Due <= Now.Date).Should().Be(12, "12 mensualités sont échues au 04/09/2026");
    }

    [Fact]
    public void Contrat_signe_le_3_avec_prelevement_le_3_commence_le_jour_meme()
    {
        // 171 TU 629 : 1 600 × 36 dès le 03/02/2026, jour 3 → 1re échéance le 03/02/2026.
        var v = Leasing(1600, 36, new DateTime(2026, 2, 3), 3);

        var dues = AcquisitionSchedule.LeasingDues(v).ToList();

        dues[0].Due.Should().Be(new DateTime(2026, 2, 3));
        dues.Count(d => d.Due <= Now.Date).Should().Be(8);
    }

    [Fact]
    public void Le_jour_de_paiement_est_plafonne_au_28()
    {
        var v = Leasing(100, 3, new DateTime(2026, 1, 15), 31);

        var dues = AcquisitionSchedule.LeasingDues(v).Select(d => d.Due).ToList();

        dues.Should().Equal(new DateTime(2026, 1, 28), new DateTime(2026, 2, 28), new DateTime(2026, 3, 28));
    }

    [Fact]
    public void Un_vehicule_achete_comptant_ne_genere_aucune_mensualite_meme_avec_des_residus()
    {
        // 524 TFGG 75 : repassé en achat (12 000 le 13/08/2026) mais garde 1 400 × 36 en base.
        var v = new Vehicle
        {
            AcquisitionType = "purchase",
            PurchasePrice = 12000, PurchaseDate = new DateTime(2026, 8, 13),
            LeasingMonthlyPayment = 1400, LeasingDurationMonths = 36,
            LeasingStartDate = new DateTime(2026, 5, 18), LeasingPaymentDay = 14
        };

        AcquisitionSchedule.LeasingDues(v).Should().BeEmpty();
        AcquisitionSchedule.Cost(new[] { v }, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), Now)
            .Should().Be(12000, "seul le prix d'achat compte");
    }

    [Fact]
    public void Le_cout_de_la_periode_compte_les_mensualites_echues_et_l_apport_date_dedans()
    {
        // 524 TDF 75 : 320 × 36 dès le 12/03/2026 jour 2 (1re le 02/04), apport 7 200 le 12/03/2026.
        var v = Leasing(320, 36, new DateTime(2026, 3, 12), 2, deposit: 7200, purchased: new DateTime(2026, 3, 12));

        var cost = AcquisitionSchedule.Cost(new[] { v }, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), Now);

        cost.Should().Be(6 * 320 + 7200, "6 mensualités échues (avril → septembre) + l'apport");
    }

    [Fact]
    public void Un_apport_hors_periode_n_est_pas_compte()
    {
        // 225 TU 4836 : apport 3 000 daté du 02/09/2025 → hors de l'année 2026.
        var v = Leasing(720, 36, new DateTime(2025, 9, 4), 3, deposit: 3000, purchased: new DateTime(2025, 9, 2));

        var cost = AcquisitionSchedule.Cost(new[] { v }, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), Now);

        cost.Should().Be(9 * 720, "9 mensualités en 2026 (janvier → septembre), pas l'apport de 2025");
    }

    [Fact]
    public void L_avenir_n_est_pas_une_depense()
    {
        var v = Leasing(720, 36, new DateTime(2025, 9, 4), 3);

        var wholeContract = AcquisitionSchedule.Cost(new[] { v }, new DateTime(2025, 1, 1), new DateTime(2028, 12, 31), Now);

        wholeContract.Should().Be(12 * 720, "les 24 mensualités restantes sont postérieures à aujourd'hui");
    }
}
