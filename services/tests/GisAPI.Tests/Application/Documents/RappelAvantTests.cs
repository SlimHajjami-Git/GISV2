using FluentAssertions;
using GisAPI.Application.Features.Documents;
using GisAPI.Application.Features.Documents.Commands;
using GisAPI.Application.Features.Documents.Queries;
using GisAPI.Application.Features.Drivers.Commands;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using GisAPI.Services;
using GisAPI.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Documents;

/// <summary>
/// « Rappel avant (jours) » — campagne de tests GPA du 06/10/2026, erreur n°4, et test de Karim
/// du 14/10/2026 : assurance renouvelée au 28/11 avec un rappel de 60 jours, restée « En règle »
/// et rappel relu à 30. La valeur n'était ni envoyée ni enregistrée (renouvellement, permis
/// chauffeur), et le seuil « bientôt » des écrans et des alertes était fixé à 30 jours.
/// </summary>
public class RappelAvantTests
{
    private const int CompanyId = 7;
    private static readonly DateTime Aujourdhui = DateTime.UtcNow.Date;

    private static ICurrentTenantService Admin()
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.CompanyId).Returns(CompanyId);
        m.Setup(x => x.UserId).Returns(1);
        m.Setup(x => x.UserRoles).Returns(new[] { "company_admin" });
        m.Setup(x => x.IsAuthenticated).Returns(true);
        return m.Object;
    }

    private static async Task<TestGisDbContext> AvecVehiculeAsync(Action<Vehicle>? reglage = null)
    {
        var ctx = TestDbContextFactory.Create();
        var v = new Vehicle { Id = 32, CompanyId = CompanyId, Name = "Service 01", Plate = "GA-214-RK", Status = "available" };
        reglage?.Invoke(v);
        ctx.Vehicles.Add(v);
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static RenewDocumentCommand Renouvellement(string type, int? rappel) => new(
        VehicleId: 32, DocumentType: type, Amount: 0m,
        PaymentDate: Aujourdhui, NewExpiryDate: Aujourdhui.AddDays(45),
        DocumentNumber: null, Provider: null, Notes: null, DocumentUrl: null,
        ReminderDays: rappel);

    private static RenewDocumentCommandHandler Handler(TestGisDbContext ctx) =>
        new(ctx, Admin(), NullLogger<RenewDocumentCommandHandler>.Instance);

    [Fact]
    public void Une_echeance_passe_bientot_au_rappel_choisi()
    {
        var echeance = Aujourdhui.AddDays(45);

        ExpiryCalendar.Status(echeance, Aujourdhui, 60).Should().Be(ExpiryCalendar.ExpiringSoon);
        ExpiryCalendar.Status(echeance, Aujourdhui, 30).Should().Be(ExpiryCalendar.Ok);
        ExpiryCalendar.Status(echeance, Aujourdhui).Should().Be(ExpiryCalendar.Ok, "30 jours par défaut, comme avant");
        ExpiryCalendar.Status(Aujourdhui.AddDays(20), Aujourdhui, 0)
            .Should().Be(ExpiryCalendar.ExpiringSoon, "un réglage hors bornes retombe sur 30 jours");
    }

    [Fact]
    public async Task Le_renouvellement_enregistre_le_rappel_de_l_echeance()
    {
        using var ctx = await AvecVehiculeAsync();

        await Handler(ctx).Handle(Renouvellement("insurance", 60), CancellationToken.None);
        await Handler(ctx).Handle(Renouvellement("registration", 90), CancellationToken.None);

        var v = ctx.Vehicles.Single();
        v.InsuranceReminderDays.Should().Be(60);
        v.RegistrationReminderDays.Should().Be(90);
        v.TaxReminderDays.Should().Be(30, "les autres échéances gardent leur réglage");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public async Task Un_rappel_hors_bornes_est_refuse_avec_un_message(int rappel)
    {
        using var ctx = await AvecVehiculeAsync();

        var act = () => Handler(ctx).Handle(Renouvellement("insurance", rappel), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*entre 1 et 365 jours*");
    }

    [Fact]
    public async Task L_ecran_echeances_suit_le_rappel_et_le_renvoie()
    {
        using var ctx = await AvecVehiculeAsync(v =>
        {
            v.InsuranceExpiry = Aujourdhui.AddDays(45);
            v.InsuranceReminderDays = 60;
            v.TaxExpiry = Aujourdhui.AddDays(45);
        });

        var lignes = await new GetExpiriesQueryHandler(ctx, Admin())
            .Handle(new GetExpiriesQuery(), CancellationToken.None);

        var assurance = lignes.Items.Single(e => e.DocumentType == "insurance");
        assurance.Status.Should().Be(ExpiryCalendar.ExpiringSoon);
        assurance.ReminderDays.Should().Be(60);
        lignes.Items.Single(e => e.DocumentType == "tax").Status
            .Should().Be(ExpiryCalendar.Ok, "la vignette garde son rappel de 30 jours");
    }

    [Fact]
    public void L_alerte_part_au_rappel_choisi()
    {
        var echeance = Aujourdhui.AddDays(45);

        PredictiveAlertService.DocumentExpiryAlert("Assurance", "Service 01", "GA-214-RK", echeance, Aujourdhui, 60)
            .Should().NotBeNull();
        PredictiveAlertService.DocumentExpiryAlert("Assurance", "Service 01", "GA-214-RK", echeance, Aujourdhui)
            .Should().BeNull("sans réglage, l'alerte part à 30 jours comme avant");
        PredictiveAlertService.DriverPermitExpiryAlert("QA Un", echeance, Aujourdhui, 60).Should().NotBeNull();
    }

    [Fact]
    public async Task La_fiche_chauffeur_enregistre_le_rappel_du_permis()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Drivers.Add(new Driver { Id = 5, CompanyId = CompanyId, FirstName = "Mehdi", LastName = "QA", Status = "active" });
        await ctx.SaveChangesAsync();

        await new UpdateDriverCommandHandler(ctx, Admin()).Handle(new UpdateDriverCommand(
            5, "Mehdi", "QA", null, null, null, null, Aujourdhui.AddDays(45), null, null, null, null, null,
            PermitReminderDays: 60), CancellationToken.None);

        ctx.Drivers.Single().PermitReminderDays.Should().Be(60);
    }
}
