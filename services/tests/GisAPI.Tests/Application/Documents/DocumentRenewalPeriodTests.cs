using FluentAssertions;
using GisAPI.Application.Features.Documents.Commands;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Documents;

/// <summary>
/// Renouvellement de document — campagne de test Calypso GPA.
///
/// <para>DEF-032 : le renouvellement recalait l'échéance mais laissait la date
/// de début de la période précédente (insurance_start_date 2025-09-30 pour une
/// assurance renouvelée jusqu'au 2027-09-13).</para>
///
/// <para>DEF-031 : type de document inconnu, véhicule hors société et société
/// absente levaient ArgumentException / InvalidOperationException, rendues en
/// 500 « An unexpected error occurred » par ExceptionHandlingMiddleware.</para>
/// </summary>
public class DocumentRenewalPeriodTests
{
    private const int CompanyId = 7;
    private const int OtherCompanyId = 5;

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);
    private static DateTime Day(int y, int m, int d) => new(y, m, d);

    private static RenewDocumentCommandHandler Handler(TestGisDbContext ctx, int? companyId = CompanyId)
    {
        var tenant = new Mock<ICurrentTenantService>();
        tenant.Setup(t => t.CompanyId).Returns(companyId);
        tenant.Setup(t => t.UserId).Returns(1);
        tenant.Setup(t => t.UserRoles).Returns(new[] { "admin" });
        tenant.Setup(t => t.IsAuthenticated).Returns(true);
        return new RenewDocumentCommandHandler(ctx, tenant.Object, NullLogger<RenewDocumentCommandHandler>.Instance);
    }

    private static RenewDocumentCommand Renew(int vehicleId, string type, DateTime paymentDate, DateTime newExpiry) =>
        new(vehicleId, type, 480.5m, paymentDate, newExpiry, null, null, null, null);

    private static async Task<TestGisDbContext> WithVehicleAsync(Vehicle vehicle)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(vehicle);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    // ── DEF-032 : début de période recalé ───────────────────────────────────

    [Fact]
    public async Task Renouvellement_anticipe_la_periode_part_de_la_date_de_paiement()
    {
        // Reproduction de la fiche : assurance 30/09/2025 → 30/09/2026, payée le
        // 13/09/2026 jusqu'au 13/09/2027 (même durée d'un an).
        using var ctx = await WithVehicleAsync(new Vehicle
        {
            Id = 44, CompanyId = CompanyId, Name = "QA-44",
            InsuranceStartDate = Day(2025, 9, 30),
            InsuranceExpiry = Utc(2026, 9, 30)
        });

        await Handler(ctx).Handle(Renew(44, "insurance", Day(2026, 9, 13), Day(2027, 9, 13)), CancellationToken.None);

        ctx.ChangeTracker.Clear();
        var v = await ctx.Vehicles.AsNoTracking().SingleAsync(x => x.Id == 44);
        v.InsuranceExpiry!.Value.Date.Should().Be(Day(2027, 9, 13));
        v.InsuranceStartDate.Should().Be(Day(2026, 9, 13),
            "nouvelle échéance moins la durée renouvelée (un an) = date de paiement");
    }

    [Fact]
    public async Task Renouvellement_dans_la_continuite_la_periode_part_de_l_ancienne_echeance()
    {
        using var ctx = await WithVehicleAsync(new Vehicle
        {
            Id = 44, CompanyId = CompanyId, Name = "QA-44",
            InsuranceStartDate = Day(2025, 9, 30),
            InsuranceExpiry = Utc(2026, 9, 30)
        });

        await Handler(ctx).Handle(Renew(44, "insurance", Day(2026, 9, 13), Day(2027, 9, 30)), CancellationToken.None);

        ctx.ChangeTracker.Clear();
        (await ctx.Vehicles.AsNoTracking().SingleAsync()).InsuranceStartDate.Should().Be(Day(2026, 9, 30));
    }

    [Theory]
    [InlineData("tax")]
    [InlineData("technical_inspection")]
    public async Task Vignette_et_visite_technique_recalent_aussi_leur_debut(string type)
    {
        using var ctx = await WithVehicleAsync(new Vehicle
        {
            Id = 44, CompanyId = CompanyId, Name = "QA-44",
            TaxStartDate = Day(2025, 9, 30), TaxExpiry = Utc(2026, 9, 30),
            TechnicalInspectionStartDate = Day(2025, 9, 30), TechnicalInspectionExpiry = Utc(2026, 9, 30)
        });

        await Handler(ctx).Handle(Renew(44, type, Day(2026, 9, 13), Day(2027, 9, 13)), CancellationToken.None);

        ctx.ChangeTracker.Clear();
        var v = await ctx.Vehicles.AsNoTracking().SingleAsync();
        var (start, other) = type == "tax"
            ? (v.TaxStartDate, v.TechnicalInspectionStartDate)
            : (v.TechnicalInspectionStartDate, v.TaxStartDate);
        start.Should().Be(Day(2026, 9, 13));
        other.Should().Be(Day(2025, 9, 30), "seul le document renouvelé change de période");
    }

    [Fact]
    public async Task La_carte_grise_ne_touche_pas_a_la_date_de_mise_en_circulation()
    {
        using var ctx = await WithVehicleAsync(new Vehicle
        {
            Id = 44, CompanyId = CompanyId, Name = "QA-44",
            RegistrationDate = Day(2020, 2, 20), RegistrationExpiry = Utc(2026, 9, 1)
        });

        await Handler(ctx).Handle(Renew(44, "registration", Day(2026, 9, 13), Day(2036, 9, 13)), CancellationToken.None);

        ctx.ChangeTracker.Clear();
        (await ctx.Vehicles.AsNoTracking().SingleAsync()).RegistrationDate.Should().Be(Day(2020, 2, 20));
    }

    [Fact]
    public void Sans_duree_precedente_le_debut_est_le_plus_tardif_de_l_ancienne_echeance_et_du_paiement()
    {
        RenewDocumentCommandHandler.RenewedPeriodStart(null, Utc(2026, 9, 30), Utc(2026, 9, 13), Utc(2027, 9, 13))
            .Should().Be(Day(2026, 9, 30));
        RenewDocumentCommandHandler.RenewedPeriodStart(null, Utc(2026, 1, 1), Utc(2026, 3, 1), Utc(2027, 3, 1))
            .Should().Be(Day(2026, 3, 1), "document échu : la période ne part pas avant le paiement");
        RenewDocumentCommandHandler.RenewedPeriodStart(null, null, Utc(2026, 9, 13), Utc(2027, 9, 13))
            .Should().Be(Day(2026, 9, 13), "aucune échéance connue : date de paiement");
    }

    [Fact]
    public void Une_periode_precedente_incoherente_ne_sert_pas_de_duree()
    {
        // Véhicule 38 des données de recette : début jamais recalé (2025-09-28)
        // pour une échéance déjà renouvelée au 2027-09-11, soit « 24 mois ».
        RenewDocumentCommandHandler.RenewedPeriodStart(
                new DateTime(2025, 9, 28, 12, 0, 0), Utc(2027, 9, 11), Utc(2027, 9, 1), Utc(2028, 9, 11))
            .Should().Be(Day(2027, 9, 11));
    }

    [Fact]
    public void Une_duree_au_jour_pres_est_arrondie_au_mois()
    {
        // Période « du 30/09/2025 au 29/09/2026 inclus » = un an.
        RenewDocumentCommandHandler.RenewedPeriodStart(
                Day(2025, 9, 30), Utc(2026, 9, 29), Utc(2026, 9, 10), Utc(2027, 9, 16))
            .Should().Be(Day(2026, 9, 16));
    }

    [Fact]
    public void Une_echeance_anterieure_au_paiement_ne_fabrique_pas_de_periode_inversee()
    {
        RenewDocumentCommandHandler.RenewedPeriodStart(
                Day(2025, 9, 30), Utc(2026, 9, 30), Utc(2026, 10, 15), Utc(2026, 10, 1))
            .Should().Be(Day(2025, 10, 1), "même durée d'un an avant la nouvelle échéance");
        RenewDocumentCommandHandler.RenewedPeriodStart(
                null, Utc(2026, 9, 30), Utc(2026, 10, 15), Utc(2026, 10, 1))
            .Should().BeNull("durée inconnue : l'ancien début (vide) est conservé");
    }

    // ── DEF-031 : refus métier en 400 / 404, pas en 500 ────────────────────

    [Fact]
    public async Task Un_type_de_document_inconnu_est_un_refus_metier_en_francais()
    {
        using var ctx = await WithVehicleAsync(new Vehicle { Id = 44, CompanyId = CompanyId, Name = "QA-44" });

        var act = () => Handler(ctx).Handle(Renew(44, "permis", Day(2026, 9, 13), Day(2027, 9, 13)), CancellationToken.None);

        (await act.Should().ThrowExactlyAsync<DomainException>())
            .Which.Message.Should().Contain("« permis » invalide").And.Contain("fiche du chauffeur");
        ctx.ChangeTracker.Clear();
        ctx.VehicleCosts.Should().BeEmpty();
    }

    [Fact]
    public async Task Une_faute_de_frappe_sur_le_type_ne_renvoie_pas_vers_la_fiche_du_chauffeur()
    {
        using var ctx = await WithVehicleAsync(new Vehicle { Id = 44, CompanyId = CompanyId, Name = "QA-44" });

        var act = () => Handler(ctx).Handle(Renew(44, "asurance", Day(2026, 9, 13), Day(2027, 9, 13)), CancellationToken.None);

        (await act.Should().ThrowExactlyAsync<DomainException>())
            .Which.Message.Should().Be(
                "Type de document « asurance » invalide (assurance, visite technique, vignette, " +
                "carte grise ou autorisation de transport).");
    }

    [Fact]
    public async Task Un_vehicule_d_une_autre_societe_est_introuvable_et_n_est_pas_renouvele()
    {
        using var ctx = await WithVehicleAsync(new Vehicle
        {
            Id = 20, CompanyId = OtherCompanyId, Name = "Autre société", InsuranceExpiry = Utc(2026, 9, 30)
        });

        var act = () => Handler(ctx).Handle(Renew(20, "insurance", Day(2026, 9, 13), Day(2027, 9, 13)), CancellationToken.None);

        // 404 au message français : le gabarit de NotFoundException est anglais.
        (await act.Should().ThrowAsync<NotFoundException>())
            .Which.Message.Should().Be("Véhicule introuvable.");
        ctx.ChangeTracker.Clear();
        ctx.VehicleCosts.Should().BeEmpty();
        (await ctx.Vehicles.AsNoTracking().SingleAsync()).InsuranceExpiry.Should().Be(Utc(2026, 9, 30));
    }

    [Fact]
    public async Task La_correction_d_echeance_d_un_vehicule_d_une_autre_societe_est_un_404_en_francais()
    {
        using var ctx = await WithVehicleAsync(new Vehicle
        {
            Id = 20, CompanyId = OtherCompanyId, Name = "Autre société", InsuranceExpiry = Utc(2026, 9, 30)
        });
        var tenant = new Mock<ICurrentTenantService>();
        tenant.Setup(t => t.CompanyId).Returns(CompanyId);

        var act = () => new UpdateDocumentExpiryCommandHandler(ctx, tenant.Object)
            .Handle(new UpdateDocumentExpiryCommand(20, "insurance", Day(2027, 9, 13)), CancellationToken.None);

        (await act.Should().ThrowAsync<NotFoundException>())
            .Which.Message.Should().Be("Véhicule introuvable.");
        ctx.ChangeTracker.Clear();
        (await ctx.Vehicles.AsNoTracking().SingleAsync()).InsuranceExpiry.Should().Be(Utc(2026, 9, 30));
    }

    [Fact]
    public async Task Sans_societe_le_renouvellement_est_refuse_en_refus_metier()
    {
        using var ctx = await WithVehicleAsync(new Vehicle { Id = 44, CompanyId = CompanyId, Name = "QA-44" });

        var act = () => Handler(ctx, companyId: null)
            .Handle(Renew(44, "insurance", Day(2026, 9, 13), Day(2027, 9, 13)), CancellationToken.None);

        await act.Should().ThrowExactlyAsync<DomainException>();
    }
}
