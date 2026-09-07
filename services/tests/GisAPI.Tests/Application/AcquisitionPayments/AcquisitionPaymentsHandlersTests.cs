using FluentAssertions;
using GisAPI.Application.Features.AcquisitionPayments;
using GisAPI.Application.Features.Vehicles.Commands.PatchVehicle;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.AcquisitionPayments;

/// <summary>
/// Endpoints des échéances d'acquisition : lecture (portée véhicules,
/// génération paresseuse, filtres), changement de statut (règles paid /
/// planned / skipped, cloisonnement société) et déclencheur PATCH véhicule.
/// </summary>
public class AcquisitionPaymentsHandlersTests
{
    private const int CompanyId = 14;
    private const int OtherCompanyId = 2;
    private static readonly DateTime Now = new(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);

    private static IDateTimeProvider Clock()
    {
        var m = new Mock<IDateTimeProvider>();
        m.Setup(c => c.UtcNow).Returns(Now);
        m.Setup(c => c.Today).Returns(DateOnly.FromDateTime(Now));
        return m.Object;
    }

    private static ICurrentTenantService Admin(int companyId = CompanyId) =>
        TestDbContextFactory.CreateMockTenantService(companyId, userId: 1).Object;

    private static ICurrentTenantService RestrictedUser(int userId)
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.CompanyId).Returns(CompanyId);
        m.Setup(x => x.UserId).Returns(userId);
        m.Setup(x => x.UserRoles).Returns(new[] { "user" });
        m.Setup(x => x.IsAuthenticated).Returns(true);
        return m.Object;
    }

    // 448 — 524 TDF 75 : apport 7 200 le 12/03/2026 + 320 × 36 dès le 12/03/2026 jour 2 (1re le 02/04).
    private static Vehicle V448(int companyId = CompanyId) => new()
    {
        Id = 448, CompanyId = companyId, Name = "524 TDF 75", Plate = "524 TDF 75",
        AcquisitionType = "leasing",
        LeasingMonthlyPayment = 320, LeasingDurationMonths = 36,
        LeasingStartDate = new DateTime(2026, 3, 12), LeasingPaymentDay = 2,
        PurchasePrice = 7200, PurchaseDate = new DateTime(2026, 3, 12)
    };

    // 445 — 171 TU 629 : apport 10 000 le 02/02/2026 + 1 600 × 36 dès le 03/02/2026 jour 3.
    private static Vehicle V445() => new()
    {
        Id = 445, CompanyId = CompanyId, Name = "171 TU 629", Plate = "171 TU 629",
        AcquisitionType = "leasing",
        LeasingMonthlyPayment = 1600, LeasingDurationMonths = 36,
        LeasingStartDate = new DateTime(2026, 2, 3), LeasingPaymentDay = 3,
        PurchasePrice = 10000, PurchaseDate = new DateTime(2026, 2, 2)
    };

    private static Vehicle Plain(int id, int companyId = CompanyId) => new()
    {
        Id = id, CompanyId = companyId, Name = $"Sans contrat {id}", Plate = $"SC-{id}", AcquisitionType = "purchase"
    };

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static Task<List<AcquisitionPaymentDto>> GetAsync(TestGisDbContext ctx, ICurrentTenantService tenant,
        int? vehicleId = null, DateTime? start = null, DateTime? end = null, bool includeFuture = false) =>
        new GetAcquisitionPaymentsQueryHandler(ctx, tenant, Clock(), NullLogger<GetAcquisitionPaymentsQueryHandler>.Instance)
            .Handle(new GetAcquisitionPaymentsQuery(vehicleId, start, end, includeFuture), CancellationToken.None);

    private static Task<AcquisitionPaymentDto> PutAsync(TestGisDbContext ctx, ICurrentTenantService tenant,
        UpdateAcquisitionPaymentCommand cmd) =>
        new UpdateAcquisitionPaymentHandler(ctx, tenant, Clock()).Handle(cmd, CancellationToken.None);

    // ── GET : génération paresseuse, portée, filtres ─────────────────────────

    [Fact]
    public async Task Le_premier_GET_genere_l_echeancier_d_un_vehicule_a_contrat_et_ne_renvoie_que_les_lignes_echues()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.AddRange(V448(), Plain(9));
        await ctx.SaveChangesAsync();
        ctx.AcquisitionPayments.Should().BeEmpty();

        var result = await GetAsync(ctx, Admin());

        ctx.AcquisitionPayments.Count().Should().Be(37, "36 mensualités + 1 apport générés au premier GET");
        ctx.AcquisitionPayments.Should().OnlyContain(p => p.VehicleId == 448 && p.CompanyId == CompanyId);

        result.Should().HaveCount(7, "l'apport + 6 mensualités échues (02/04 → 02/09) ; les 30 à venir ne sont pas listées");
        result.Should().OnlyContain(r => r.Counted);
        result.Select(r => r.DueDate).Should().BeInDescendingOrder();
        result[0].DueDate.Should().Be(D(2026, 9, 2));
        result[0].Kind.Should().Be("mensualite");
        result[0].Seq.Should().Be(6);
        result[0].Total.Should().Be(36);
        result[0].Overdue.Should().BeTrue("échue le 02/09, on est le 04/09");
        result[0].VehiclePlate.Should().Be("524 TDF 75");
        result[0].VehicleName.Should().Be("524 TDF 75");
        var apport = result.Single(r => r.Kind == "apport");
        apport.Total.Should().Be(1);
        apport.DueDate.Should().Be(D(2026, 3, 12));
        apport.Amount.Should().Be(7200m);
        apport.Status.Should().Be("planned");
        apport.Generated.Should().BeTrue();

        // Un second GET ne regénère rien.
        await GetAsync(ctx, Admin());
        ctx.AcquisitionPayments.Count().Should().Be(37);
    }

    [Fact]
    public async Task IncludeFuture_renvoie_tout_l_echeancier_avec_les_drapeaux_counted_et_overdue()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(V448());
        await ctx.SaveChangesAsync();

        var result = await GetAsync(ctx, Admin(), includeFuture: true);

        result.Should().HaveCount(37);
        result.Count(r => r.Counted).Should().Be(7);
        result.Count(r => r.Overdue).Should().Be(7, "les 6 mensualités échues et l'apport, tous planifiés, sont antérieurs au 04/09");
        result.Where(r => !r.Counted).Should().OnlyContain(r => r.DueDate > D(2026, 9, 4) && r.Status == "planned");
        result.Select(r => (r.DueDate, r.Seq)).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task Un_utilisateur_restreint_sans_vehicule_affecte_ne_voit_rien_et_ne_declenche_aucune_generation()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(V448());
        await ctx.SaveChangesAsync();

        var result = await GetAsync(ctx, RestrictedUser(userId: 42));

        result.Should().BeEmpty();
        ctx.AcquisitionPayments.Should().BeEmpty("rien n'est généré pour un appelant qui ne voit aucun véhicule");
    }

    [Fact]
    public async Task Un_utilisateur_restreint_ne_voit_et_ne_genere_que_ses_vehicules()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.AddRange(V448(), V445());
        ctx.UserVehicles.Add(new UserVehicle { UserId = 42, VehicleId = 448 });
        await ctx.SaveChangesAsync();

        var result = await GetAsync(ctx, RestrictedUser(userId: 42), includeFuture: true);

        result.Should().HaveCount(37).And.OnlyContain(r => r.VehicleId == 448);
        ctx.AcquisitionPayments.Should().OnlyContain(p => p.VehicleId == 448, "le véhicule 445, invisible, n'est pas synchronisé");
    }

    [Fact]
    public async Task Les_filtres_vehicleId_et_periode_bornent_la_date_d_echeance()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.AddRange(V448(), V445());
        await ctx.SaveChangesAsync();

        var all = await GetAsync(ctx, Admin());
        all.Select(r => r.VehicleId).Distinct().Should().BeEquivalentTo(new[] { 448, 445 });

        var only448 = await GetAsync(ctx, Admin(), vehicleId: 448);
        only448.Should().HaveCount(7).And.OnlyContain(r => r.VehicleId == 448);

        var mayJune = await GetAsync(ctx, Admin(), vehicleId: 448, start: new DateTime(2026, 5, 1), end: new DateTime(2026, 6, 30));
        mayJune.Select(r => r.DueDate).Should().BeEquivalentTo(new[] { D(2026, 6, 2), D(2026, 5, 2) });

        var futureSlice = await GetAsync(ctx, Admin(), vehicleId: 448, start: new DateTime(2027, 1, 1), end: new DateTime(2027, 3, 31), includeFuture: true);
        futureSlice.Select(r => r.DueDate).Should().BeEquivalentTo(new[] { D(2027, 3, 2), D(2027, 2, 2), D(2027, 1, 2) });
    }

    [Fact]
    public async Task Une_ligne_payee_d_avance_est_listee_meme_sans_includeFuture()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(V448());
        await ctx.SaveChangesAsync();
        await GetAsync(ctx, Admin());
        var future = ctx.AcquisitionPayments.Single(p => p.Kind == "mensualite" && p.Seq == 12);
        future.Status = "paid";
        future.PaidAt = Now;
        future.PaidAmount = 320;
        await ctx.SaveChangesAsync();

        var result = await GetAsync(ctx, Admin());

        result.Should().Contain(r => r.Seq == 12 && r.Status == "paid" && r.Counted && !r.Overdue);
        result.Should().HaveCount(8);
    }

    // ── PUT : règles de statut et cloisonnement ──────────────────────────────

    private static async Task<(TestGisDbContext Ctx, AcquisitionPayment Line)> SeedOneLineAsync(int companyId = CompanyId)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(V448(companyId));
        await ctx.SaveChangesAsync();
        await GetAsync(ctx, Admin(companyId), includeFuture: true);
        var line = ctx.AcquisitionPayments.Single(p => p.Kind == "mensualite" && p.Seq == 3);
        return (ctx, line);
    }

    [Fact]
    public async Task Marquer_payee_sans_date_ni_montant_pose_maintenant_et_le_montant_prevu()
    {
        var (ctx, line) = await SeedOneLineAsync();
        using var _ = ctx;

        var dto = await PutAsync(ctx, Admin(), new UpdateAcquisitionPaymentCommand(line.Id, "paid"));

        dto.Status.Should().Be("paid");
        dto.PaidAt.Should().Be(Now);
        dto.PaidAmount.Should().Be(320m);
        dto.Counted.Should().BeTrue();
        dto.Overdue.Should().BeFalse();
        dto.Total.Should().Be(36);
        ctx.AcquisitionPayments.Single(p => p.Id == line.Id).PaidAt.Should().Be(Now);
    }

    [Fact]
    public async Task Marquer_payee_avec_date_montant_et_note_les_conserve()
    {
        var (ctx, line) = await SeedOneLineAsync();
        using var _ = ctx;
        var paidAt = new DateTime(2026, 6, 3, 8, 30, 0, DateTimeKind.Utc);

        var dto = await PutAsync(ctx, Admin(), new UpdateAcquisitionPaymentCommand(line.Id, "paid", paidAt, 300, "  Virement BIAT  "));

        dto.PaidAt.Should().Be(paidAt);
        dto.PaidAmount.Should().Be(300m);
        dto.Note.Should().Be("Virement BIAT");
        dto.Amount.Should().Be(320m, "le montant prévu ne bouge pas");
    }

    [Fact]
    public async Task Revenir_a_planifiee_efface_la_date_et_le_montant_regles_mais_garde_la_quittance()
    {
        var (ctx, line) = await SeedOneLineAsync();
        using var _ = ctx;
        line.ReceiptUrl = "/uploads/acquisition-receipts/14/q.pdf";
        await ctx.SaveChangesAsync();
        await PutAsync(ctx, Admin(), new UpdateAcquisitionPaymentCommand(line.Id, "paid", null, 300, "note"));

        var dto = await PutAsync(ctx, Admin(), new UpdateAcquisitionPaymentCommand(line.Id, "planned"));

        dto.Status.Should().Be("planned");
        dto.PaidAt.Should().BeNull();
        dto.PaidAmount.Should().BeNull();
        dto.ReceiptUrl.Should().Be("/uploads/acquisition-receipts/14/q.pdf");
        dto.Note.Should().Be("note", "la note n'est modifiée que si elle est fournie");
    }

    [Fact]
    public async Task Ignorer_garde_la_date_et_le_montant_regles_et_la_ligne_ne_compte_plus()
    {
        var (ctx, line) = await SeedOneLineAsync();
        using var _ = ctx;
        await PutAsync(ctx, Admin(), new UpdateAcquisitionPaymentCommand(line.Id, "paid", null, 300, "note"));

        var dto = await PutAsync(ctx, Admin(), new UpdateAcquisitionPaymentCommand(line.Id, "skipped"));

        dto.Status.Should().Be("skipped");
        dto.PaidAt.Should().Be(Now);
        dto.PaidAmount.Should().Be(300m);
        dto.Note.Should().Be("note");
        dto.Counted.Should().BeFalse();
        dto.Overdue.Should().BeFalse();
    }

    [Fact]
    public async Task Une_ligne_d_une_autre_societe_est_introuvable()
    {
        var (ctx, line) = await SeedOneLineAsync(OtherCompanyId);
        using var _ = ctx;

        var act = () => PutAsync(ctx, Admin(CompanyId), new UpdateAcquisitionPaymentCommand(line.Id, "paid"));

        await act.Should().ThrowAsync<NotFoundException>();
        ctx.AcquisitionPayments.Single(p => p.Id == line.Id).Status.Should().Be("planned");
    }

    [Fact]
    public async Task Un_utilisateur_restreint_ne_peut_pas_modifier_une_echeance_d_un_vehicule_hors_portee()
    {
        var (ctx, line) = await SeedOneLineAsync();
        using var _ = ctx;

        var act = () => PutAsync(ctx, RestrictedUser(userId: 42), new UpdateAcquisitionPaymentCommand(line.Id, "paid"));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public void Le_validateur_refuse_un_statut_inconnu_un_montant_negatif_et_une_note_trop_longue()
    {
        var validator = new UpdateAcquisitionPaymentValidator();

        validator.Validate(new UpdateAcquisitionPaymentCommand(1, "paid")).IsValid.Should().BeTrue();
        validator.Validate(new UpdateAcquisitionPaymentCommand(1, "planned")).IsValid.Should().BeTrue();
        validator.Validate(new UpdateAcquisitionPaymentCommand(1, "skipped", null, 0, new string('x', 500))).IsValid.Should().BeTrue();

        validator.Validate(new UpdateAcquisitionPaymentCommand(1, "payee")).IsValid.Should().BeFalse();
        validator.Validate(new UpdateAcquisitionPaymentCommand(1, "paid", null, -1)).IsValid.Should().BeFalse();
        validator.Validate(new UpdateAcquisitionPaymentCommand(1, "paid", null, null, new string('x', 501))).IsValid.Should().BeFalse();
    }

    // ── Quittance ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Joindre_une_quittance_remplace_l_ancienne_et_renvoie_l_url_precedente()
    {
        var (ctx, line) = await SeedOneLineAsync();
        using var _ = ctx;
        var handler = new SetAcquisitionPaymentReceiptHandler(ctx, Admin());

        var first = await handler.Handle(new SetAcquisitionPaymentReceiptCommand(line.Id, "/uploads/acquisition-receipts/14/a.pdf"), CancellationToken.None);
        var second = await handler.Handle(new SetAcquisitionPaymentReceiptCommand(line.Id, "/uploads/acquisition-receipts/14/b.jpg"), CancellationToken.None);

        first.Should().BeNull();
        second.Should().Be("/uploads/acquisition-receipts/14/a.pdf");
        ctx.AcquisitionPayments.Single(p => p.Id == line.Id).ReceiptUrl.Should().Be("/uploads/acquisition-receipts/14/b.jpg");

        var act = () => handler.Handle(new SetAcquisitionPaymentReceiptCommand(9999, "/x"), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── Déclencheur : PATCH véhicule ─────────────────────────────────────────

    private static PatchVehicleCommand Patch(int id, decimal? monthly = null, int? mileage = null) => new(
        Id: id, SpeedLimit: null, DepartmentId: null, FuelType: null,
        Brand: null, Model: null, Plate: null, Year: null, Color: null, Mileage: mileage, FuelTankCapacity: null,
        AcquisitionType: null, PurchasePrice: null, LeasingMonthlyPayment: monthly, LeasingDurationMonths: null,
        LeasingStartDate: null, LeasingPaymentDay: null, RegistrationDate: null, PurchaseDate: null);

    [Fact]
    public async Task Modifier_le_contrat_par_PATCH_recale_l_echeancier_mais_pas_une_edition_de_kilometrage()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(V448());
        await ctx.SaveChangesAsync();
        var handler = new PatchVehicleCommandHandler(ctx, Admin());

        // Kilométrage seul : le contrat n'a pas bougé → aucun échéancier généré.
        await handler.Handle(Patch(448, mileage: 12_345), CancellationToken.None);
        ctx.AcquisitionPayments.Should().BeEmpty("une édition hors contrat ne synchronise pas");

        // Mensualité modifiée → l'échéancier est (re)généré dans la même transaction.
        await handler.Handle(Patch(448, monthly: 350), CancellationToken.None);
        ctx.AcquisitionPayments.Count().Should().Be(37);
        ctx.AcquisitionPayments.Where(p => p.Kind == "mensualite").Should().OnlyContain(p => p.Amount == 350m);

        // Une ligne payée survit à une nouvelle correction.
        var seq2 = ctx.AcquisitionPayments.Single(p => p.Kind == "mensualite" && p.Seq == 2);
        seq2.Status = "paid";
        seq2.PaidAmount = 350;
        await ctx.SaveChangesAsync();
        await handler.Handle(Patch(448, monthly: 360), CancellationToken.None);
        ctx.AcquisitionPayments.Single(p => p.Kind == "mensualite" && p.Seq == 2).Amount.Should().Be(350m);
        ctx.AcquisitionPayments.Single(p => p.Kind == "mensualite" && p.Seq == 3).Amount.Should().Be(360m);
    }
}
