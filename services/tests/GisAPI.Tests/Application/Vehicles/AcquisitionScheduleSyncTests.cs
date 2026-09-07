using FluentAssertions;
using GisAPI.Application.Features.AcquisitionPayments;
using GisAPI.Application.Features.Vehicles;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.Vehicles;

/// <summary>
/// L'échéancier PERSISTÉ (acquisition_payments) doit être la projection exacte
/// d'AcquisitionSchedule — les fixtures sont les quatre contrats réels de la
/// société de recette au 04/09/2026 (368, 445, 448, 449) — et les faits saisis
/// (payé / ignoré) doivent survivre à toute correction du contrat.
/// </summary>
public class AcquisitionScheduleSyncTests
{
    private const int CompanyId = 14;
    private static readonly DateTime Now = new(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Jan1 = new(2026, 1, 1);
    private static readonly DateTime Dec31 = new(2026, 12, 31);

    private static Vehicle Leasing(int id, decimal monthly, int months, DateTime start, int payDay,
                                   decimal? deposit = null, DateTime? purchased = null) => new()
    {
        Id = id,
        CompanyId = CompanyId,
        Name = $"Véhicule {id}",
        Plate = $"PL-{id}",
        AcquisitionType = "leasing",
        LeasingMonthlyPayment = monthly,
        LeasingDurationMonths = months,
        LeasingStartDate = start,
        LeasingPaymentDay = payDay,
        PurchasePrice = deposit,
        PurchaseDate = purchased
    };

    // 368 — 524 TFGG 75 : repassé en achat (12 000 le 13/08/2026), garde 1 400 × 36 en base.
    private static Vehicle V368() => new()
    {
        Id = 368, CompanyId = CompanyId, Name = "524 TFGG 75", Plate = "524 TFGG 75",
        AcquisitionType = "purchase",
        PurchasePrice = 12000, PurchaseDate = new DateTime(2026, 8, 13),
        LeasingMonthlyPayment = 1400, LeasingDurationMonths = 36,
        LeasingStartDate = new DateTime(2026, 5, 18), LeasingPaymentDay = 14
    };
    // 445 — 171 TU 629 : apport 10 000 le 02/02/2026 + 1 600 × 36 dès le 03/02/2026 jour 3.
    private static Vehicle V445() => Leasing(445, 1600, 36, new DateTime(2026, 2, 3), 3, 10000, new DateTime(2026, 2, 2));
    // 448 — 524 TDF 75 : apport 7 200 le 12/03/2026 + 320 × 36 dès le 12/03/2026 jour 2.
    private static Vehicle V448() => Leasing(448, 320, 36, new DateTime(2026, 3, 12), 2, 7200, new DateTime(2026, 3, 12));
    // 449 — 225 TU 4836 : apport 3 000 le 02/09/2025 + 720 × 36 dès le 04/09/2025 jour 3.
    private static Vehicle V449() => Leasing(449, 720, 36, new DateTime(2025, 9, 4), 3, 3000, new DateTime(2025, 9, 2));

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static async Task<TestGisDbContext> SeededAsync(params Vehicle[] vehicles)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.AddRange(vehicles);
        await ctx.SaveChangesAsync();
        foreach (var v in vehicles)
            await AcquisitionScheduleSync.SyncAsync(ctx, v, CancellationToken.None);
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static List<AcquisitionPayment> Lines(TestGisDbContext ctx, int vehicleId) =>
        ctx.AcquisitionPayments.Where(p => p.VehicleId == vehicleId)
            .OrderBy(p => p.Kind).ThenBy(p => p.Seq).ToList();

    // ── 1-5 : lignes attendues ────────────────────────────────────────────────

    [Fact]
    public void Contrat_720x36_signe_le_4_jour_3_donne_36_mensualites_a_partir_du_mois_suivant_et_pas_d_apport_sans_prix()
    {
        var v = Leasing(1, 720, 36, new DateTime(2025, 9, 4), 3);

        var lines = AcquisitionScheduleSync.ExpectedLines(v);

        lines.Should().HaveCount(36).And.OnlyContain(l => l.Kind == AcquisitionPayment.Kinds.Mensualite);
        lines.Select(l => l.Seq).Should().Equal(Enumerable.Range(1, 36));
        lines[0].Due.Should().Be(D(2025, 10, 3), "le jour de paiement (3) précède le jour de début (4) → mois suivant");
        lines.Should().OnlyContain(l => l.Amount == 720m);
    }

    [Fact]
    public void Contrat_1600x36_signe_le_3_jour_3_commence_le_jour_meme()
    {
        var lines = AcquisitionScheduleSync.ExpectedLines(Leasing(1, 1600, 36, new DateTime(2026, 2, 3), 3));

        lines.First(l => l.Seq == 1).Due.Should().Be(D(2026, 2, 3));
    }

    [Fact]
    public void Le_jour_de_paiement_est_plafonne_au_28()
    {
        var lines = AcquisitionScheduleSync.ExpectedLines(Leasing(1, 100, 3, new DateTime(2026, 1, 15), 31));

        lines.Select(l => l.Due).Should().Equal(D(2026, 1, 28), D(2026, 2, 28), D(2026, 3, 28));
    }

    [Fact]
    public void Leasing_avec_apport_donne_36_mensualites_et_une_ligne_apport_datee_de_l_achat()
    {
        var lines = AcquisitionScheduleSync.ExpectedLines(V448());

        lines.Where(l => l.Kind == AcquisitionPayment.Kinds.Mensualite).Should().HaveCount(36);
        lines.First(l => l.Kind == AcquisitionPayment.Kinds.Mensualite).Due.Should().Be(D(2026, 4, 2), "jour 2 < 12 → 1re le 02/04");
        var apport = lines.Single(l => l.Kind == AcquisitionPayment.Kinds.Apport);
        apport.Seq.Should().Be(1);
        apport.Due.Should().Be(D(2026, 3, 12));
        apport.Amount.Should().Be(7200m);
        lines.Should().NotContain(l => l.Kind == AcquisitionPayment.Kinds.Achat);
    }

    [Fact]
    public void Achat_comptant_avec_residus_de_contrat_donne_une_seule_ligne_achat()
    {
        var lines = AcquisitionScheduleSync.ExpectedLines(V368());

        var achat = lines.Should().ContainSingle().Subject;
        achat.Kind.Should().Be(AcquisitionPayment.Kinds.Achat);
        achat.Seq.Should().Be(1);
        achat.Due.Should().Be(D(2026, 8, 13));
        achat.Amount.Should().Be(12000m);
    }

    // ── 6-10 : synchronisation ────────────────────────────────────────────────

    [Fact]
    public async Task Deux_synchronisations_consecutives_la_seconde_ne_change_rien()
    {
        using var ctx = TestDbContextFactory.Create();
        var v = V448();
        ctx.Vehicles.Add(v);
        await ctx.SaveChangesAsync();

        (await AcquisitionScheduleSync.SyncAsync(ctx, v, CancellationToken.None)).Should().BeTrue();
        await ctx.SaveChangesAsync();
        var count = ctx.AcquisitionPayments.Count();

        (await AcquisitionScheduleSync.SyncAsync(ctx, v, CancellationToken.None)).Should().BeFalse("rien n'a changé");
        await ctx.SaveChangesAsync();

        ctx.AcquisitionPayments.Count().Should().Be(count).And.Be(37, "36 mensualités + 1 apport");
        ctx.AcquisitionPayments.Should().OnlyContain(p => p.CompanyId == CompanyId && p.Generated && p.Status == "planned");
    }

    [Fact]
    public async Task Changer_la_mensualite_recale_les_planifiees_et_garde_la_payee_telle_quelle()
    {
        var v = V449();
        using var ctx = await SeededAsync(v);
        var paid = ctx.AcquisitionPayments.Single(p => p.VehicleId == 449 && p.Kind == "mensualite" && p.Seq == 5);
        var paidAt = new DateTime(2026, 2, 5, 9, 0, 0, DateTimeKind.Utc);
        paid.Status = AcquisitionPayment.Statuses.Paid;
        paid.PaidAt = paidAt;
        paid.PaidAmount = 720;
        paid.ReceiptUrl = "/uploads/acquisition-receipts/14/quittance.pdf";
        await ctx.SaveChangesAsync();

        v.LeasingMonthlyPayment = 750;
        (await AcquisitionScheduleSync.SyncAsync(ctx, v, CancellationToken.None)).Should().BeTrue();
        await ctx.SaveChangesAsync();

        var lines = Lines(ctx, 449).Where(p => p.Kind == "mensualite").ToList();
        lines.Should().HaveCount(36);
        lines.Where(p => p.Seq != 5).Should().OnlyContain(p => p.Amount == 750m && p.Status == "planned");
        var kept = lines.Single(p => p.Seq == 5);
        kept.Status.Should().Be("paid");
        kept.Amount.Should().Be(720m, "le montant d'un règlement réel ne bouge pas");
        kept.PaidAmount.Should().Be(720m);
        kept.PaidAt.Should().Be(paidAt);
        kept.ReceiptUrl.Should().Be("/uploads/acquisition-receipts/14/quittance.pdf");
    }

    [Fact]
    public async Task Reduire_la_duree_supprime_les_planifiees_en_trop_mais_garde_la_payee_hors_contrat()
    {
        var v = Leasing(7, 720, 36, new DateTime(2025, 9, 4), 3);
        using var ctx = await SeededAsync(v);
        var seq30 = ctx.AcquisitionPayments.Single(p => p.VehicleId == 7 && p.Seq == 30);
        seq30.Status = AcquisitionPayment.Statuses.Paid;
        seq30.PaidAmount = 720;
        await ctx.SaveChangesAsync();

        v.LeasingDurationMonths = 24;
        await AcquisitionScheduleSync.SyncAsync(ctx, v, CancellationToken.None);
        await ctx.SaveChangesAsync();

        var lines = Lines(ctx, 7);
        lines.Should().HaveCount(25, "24 mensualités du contrat + la seq 30 payée");
        lines.Where(p => p.Seq <= 24).Should().HaveCount(24).And.OnlyContain(p => p.Status == "planned");
        lines.Should().NotContain(p => p.Seq >= 25 && p.Seq != 30);
        lines.Single(p => p.Seq == 30).Status.Should().Be("paid");
    }

    [Fact]
    public async Task Basculer_en_achat_comptant_requalifie_l_apport_paye_sans_le_doubler()
    {
        var v = V448();
        using var ctx = await SeededAsync(v);
        var apport = ctx.AcquisitionPayments.Single(p => p.VehicleId == 448 && p.Kind == "apport");
        apport.Status = AcquisitionPayment.Statuses.Paid;
        apport.PaidAmount = 7200;
        apport.ReceiptUrl = "/uploads/acquisition-receipts/14/quittance.pdf";
        await ctx.SaveChangesAsync();
        var apportId = apport.Id;

        // L'écran ne sait pas remettre les champs leasing à null : ils restent en résidu.
        v.AcquisitionType = "purchase";
        await AcquisitionScheduleSync.SyncAsync(ctx, v, CancellationToken.None);
        await ctx.SaveChangesAsync();

        var lines = Lines(ctx, 448);
        lines.Should().NotContain(p => p.Kind == "mensualite", "un achat comptant n'a pas de mensualité, résidus ou pas");
        // apport et achat sont le MÊME emplacement : sans requalification, le prix
        // d'acquisition serait compté deux fois dans le coût total.
        lines.Should().ContainSingle(p => p.Kind == "apport" || p.Kind == "achat");
        var achat = lines.Single(p => p.Kind == "achat");
        achat.Id.Should().Be(apportId, "la ligne existante est requalifiée, pas remplacée");
        achat.Seq.Should().Be(1);
        achat.DueDate.Should().Be(D(2026, 3, 12));
        achat.Amount.Should().Be(7200m);
        achat.Status.Should().Be("paid", "un apport réellement versé reste un règlement");
        achat.PaidAmount.Should().Be(7200m);
        achat.ReceiptUrl.Should().NotBeNull("la quittance suit la ligne");
    }

    [Fact]
    public async Task Basculer_en_leasing_requalifie_l_achat_paye_en_apport()
    {
        var v = V448();
        v.AcquisitionType = "purchase";
        using var ctx = await SeededAsync(v);
        var achat = ctx.AcquisitionPayments.Single(p => p.VehicleId == 448 && p.Kind == "achat");
        achat.Status = AcquisitionPayment.Statuses.Paid;
        achat.PaidAmount = 7200;
        await ctx.SaveChangesAsync();

        v.AcquisitionType = "leasing";
        await AcquisitionScheduleSync.SyncAsync(ctx, v, CancellationToken.None);
        await ctx.SaveChangesAsync();

        var lines = Lines(ctx, 448);
        lines.Should().ContainSingle(p => p.Kind == "apport" || p.Kind == "achat");
        var apport = lines.Single(p => p.Kind == "apport");
        apport.Status.Should().Be("paid");
        apport.PaidAmount.Should().Be(7200m);
        lines.Count(p => p.Kind == "mensualite").Should().Be(36, "le contrat reprend ses mensualités");
    }

    [Fact]
    public async Task Vehicule_deplace_de_societe_recale_ses_echeances_sans_les_dupliquer()
    {
        var v = V448();
        using var ctx = await SeededAsync(v);
        var before = Lines(ctx, 448).Count;
        before.Should().BeGreaterThan(0);

        // Un system_admin déplace le véhicule vers une autre société.
        v.CompanyId = 99;
        var changed = await AcquisitionScheduleSync.SyncAsync(ctx, v, CancellationToken.None);
        await ctx.SaveChangesAsync();

        changed.Should().BeTrue();
        var lines = Lines(ctx, 448);
        lines.Should().HaveCount(before, "les échéances suivent le véhicule, elles ne sont pas ré-insérées");
        lines.Should().OnlyContain(p => p.CompanyId == 99);
    }

    [Fact]
    public async Task Basculer_en_achat_comptant_supprime_un_apport_encore_planifie()
    {
        var v = V448();
        using var ctx = await SeededAsync(v);

        v.AcquisitionType = "purchase";
        await AcquisitionScheduleSync.SyncAsync(ctx, v, CancellationToken.None);
        await ctx.SaveChangesAsync();

        var lines = Lines(ctx, 448);
        lines.Should().ContainSingle().Which.Kind.Should().Be("achat");
    }

    [Fact]
    public async Task Changer_le_jour_de_paiement_recale_les_dates_des_planifiees_pas_celles_des_payees()
    {
        var v = Leasing(10, 720, 36, new DateTime(2025, 9, 4), 3);
        using var ctx = await SeededAsync(v);
        var seq1 = ctx.AcquisitionPayments.Single(p => p.VehicleId == 10 && p.Seq == 1);
        seq1.DueDate.Should().Be(D(2025, 10, 3));
        seq1.Status = AcquisitionPayment.Statuses.Paid;
        await ctx.SaveChangesAsync();

        v.LeasingPaymentDay = 14;
        await AcquisitionScheduleSync.SyncAsync(ctx, v, CancellationToken.None);
        await ctx.SaveChangesAsync();

        var lines = Lines(ctx, 10);
        lines.Single(p => p.Seq == 1).DueDate.Should().Be(D(2025, 10, 3), "la date d'un règlement réel ne bouge pas");
        lines.Single(p => p.Seq == 2).DueDate.Should().Be(D(2025, 10, 14), "jour 14 ≥ 4 → plus de décalage : seq 2 = octobre");
        lines.Single(p => p.Seq == 36).DueDate.Should().Be(D(2028, 8, 14));
    }

    // ── 11 : règle de comptage et parité ─────────────────────────────────────

    [Fact]
    public void Regle_de_comptage_payee_toujours_planifiee_si_echue_ignoree_jamais_montant_regle_prioritaire()
    {
        var today = D(2026, 9, 4);
        var lines = new[]
        {
            new AcquisitionPayment { Kind = "mensualite", Seq = 1, DueDate = D(2026, 11, 3), Amount = 100, Status = "paid" },                       // payée d'avance : compte
            new AcquisitionPayment { Kind = "mensualite", Seq = 2, DueDate = D(2026, 9, 3), Amount = 100, Status = "planned" },                     // échue : compte
            new AcquisitionPayment { Kind = "mensualite", Seq = 3, DueDate = D(2026, 9, 4), Amount = 100, Status = "planned" },                     // le jour même : compte
            new AcquisitionPayment { Kind = "mensualite", Seq = 4, DueDate = D(2026, 9, 5), Amount = 100, Status = "planned" },                     // demain : ne compte pas
            new AcquisitionPayment { Kind = "mensualite", Seq = 5, DueDate = D(2026, 8, 3), Amount = 100, Status = "skipped" },                     // ignorée : jamais
            new AcquisitionPayment { Kind = "mensualite", Seq = 6, DueDate = D(2026, 7, 3), Amount = 100, Status = "paid", PaidAmount = 80 },       // montant réglé prioritaire
        };

        AcquisitionPaymentRules.IsCounted("paid", D(2026, 11, 3), today).Should().BeTrue();
        AcquisitionPaymentRules.IsCounted("planned", D(2026, 9, 4), today).Should().BeTrue();
        AcquisitionPaymentRules.IsCounted("planned", D(2026, 9, 5), today).Should().BeFalse();
        AcquisitionPaymentRules.IsCounted("skipped", D(2026, 8, 3), today).Should().BeFalse();
        AcquisitionPaymentRules.IsOverdue("planned", D(2026, 9, 3), today).Should().BeTrue();
        AcquisitionPaymentRules.IsOverdue("planned", D(2026, 9, 4), today).Should().BeFalse("échue le jour même, pas en retard");
        AcquisitionPaymentRules.IsOverdue("paid", D(2026, 9, 3), today).Should().BeFalse();
        AcquisitionPaymentRules.CountedAmount(100, 80).Should().Be(80);
        AcquisitionPaymentRules.CountedAmount(100, null).Should().Be(100);

        AcquisitionPaymentRules.CountedCost(lines, Jan1, Dec31, Now)
            .Should().Be(100 + 100 + 100 + 80, "seq 1 (payée), 2 et 3 (échues) à 100, seq 6 à 80");
    }

    [Fact]
    public async Task La_somme_SQL_et_la_somme_en_memoire_appliquent_la_meme_regle()
    {
        using var ctx = TestDbContextFactory.Create();
        var today = D(2026, 9, 4);
        ctx.AcquisitionPayments.AddRange(
            new AcquisitionPayment { CompanyId = CompanyId, VehicleId = 1, Kind = "mensualite", Seq = 1, DueDate = D(2026, 11, 3), Amount = 100, Status = "paid" },
            new AcquisitionPayment { CompanyId = CompanyId, VehicleId = 1, Kind = "mensualite", Seq = 2, DueDate = D(2026, 9, 3), Amount = 100, Status = "planned" },
            new AcquisitionPayment { CompanyId = CompanyId, VehicleId = 1, Kind = "mensualite", Seq = 3, DueDate = D(2026, 9, 5), Amount = 100, Status = "planned" },
            new AcquisitionPayment { CompanyId = CompanyId, VehicleId = 1, Kind = "mensualite", Seq = 4, DueDate = D(2026, 8, 3), Amount = 100, Status = "skipped" },
            new AcquisitionPayment { CompanyId = CompanyId, VehicleId = 1, Kind = "mensualite", Seq = 5, DueDate = D(2026, 7, 3), Amount = 100, Status = "paid", PaidAmount = 80 },
            new AcquisitionPayment { CompanyId = CompanyId, VehicleId = 1, Kind = "mensualite", Seq = 6, DueDate = D(2025, 12, 3), Amount = 100, Status = "paid" }); // hors période
        await ctx.SaveChangesAsync();

        var sql = await AcquisitionPaymentRules.CountedCostAsync(ctx.AcquisitionPayments, Jan1, Dec31, Now, CancellationToken.None);
        var memory = AcquisitionPaymentRules.CountedCost(ctx.AcquisitionPayments.ToList(), Jan1, Dec31, Now);

        sql.Should().Be(280m);
        memory.Should().Be(sql);
        _ = today;
    }

    [Fact]
    public async Task Parite_avec_AcquisitionSchedule_sur_les_4_contrats_reels_de_la_societe_14()
    {
        var vehicles = new[] { V368(), V445(), V448(), V449() };
        using var ctx = await SeededAsync(vehicles);

        ctx.AcquisitionPayments.Count().Should().Be(112, "108 mensualités + 4 apports/achat (constat TN du 07/09/2026)");

        // Année 2026 au 04/09 : le poste « Achats véhicule » du tableau de bord.
        var expected2026 = AcquisitionSchedule.Cost(vehicles, Jan1, Dec31, Now);
        expected2026.Should().Be(50400m);
        (await AcquisitionPaymentRules.CountedCostAsync(ctx.AcquisitionPayments, Jan1, Dec31, Now, CancellationToken.None))
            .Should().Be(expected2026);
        AcquisitionPaymentRules.CountedCost(ctx.AcquisitionPayments.ToList(), Jan1, Dec31, Now).Should().Be(expected2026);

        // Véhicule par véhicule (mêmes chiffres que la requête SQL de la cartographie).
        async Task<decimal> Sum(int id, DateTime from, DateTime to) =>
            await AcquisitionPaymentRules.CountedCostAsync(ctx.AcquisitionPayments.Where(p => p.VehicleId == id), from, to, Now, CancellationToken.None);

        (await Sum(368, Jan1, Dec31)).Should().Be(12000m, "achat comptant seul");
        (await Sum(445, Jan1, Dec31)).Should().Be(8 * 1600 + 10000, "8 mensualités (février → septembre) + l'apport");
        (await Sum(448, Jan1, Dec31)).Should().Be(6 * 320 + 7200, "6 mensualités (avril → septembre) + l'apport");
        (await Sum(449, Jan1, Dec31)).Should().Be(9 * 720, "9 mensualités en 2026, l'apport de 2025 est hors période");

        // Tout le contrat de 449 : 12 mensualités échues + l'apport ; l'avenir n'est pas une dépense.
        var whole = new DateTime(2025, 1, 1);
        var end = new DateTime(2028, 12, 31);
        (await Sum(449, whole, end)).Should().Be(12 * 720 + 3000)
            .And.Be(AcquisitionSchedule.Cost(new[] { V449() }, whole, end, Now));

        // Lignes comptées au 04/09 : 4 apports/achat + (8 + 6 + 12) mensualités.
        var today = D(2026, 9, 4);
        ctx.AcquisitionPayments.ToList().Count(p => AcquisitionPaymentRules.IsCounted(p.Status, p.DueDate, today)).Should().Be(30);
    }
}
