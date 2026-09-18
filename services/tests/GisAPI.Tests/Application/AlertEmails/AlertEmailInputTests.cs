using FluentAssertions;
using GisAPI.Application.Features.AlertEmails.Commands;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.AlertEmails;

/// <summary>
/// Liste de diffusion des échéances (assurance, taxe, visite technique, entretien).
/// Rien n'était vérifié côté serveur : une adresse vide ou malformée était enregistrée avec un
/// 200 et un id, puis AlertEmailDispatcher la remettait telle quelle au serveur SMTP — l'alerte
/// partait dans le vide sans que l'utilisateur en soit averti (recette du 16/09/2026).
/// Un <see cref="DomainException"/> se traduit en HTTP 400 par ExceptionHandlingMiddleware.
/// </summary>
public class AlertEmailInputTests
{
    private const int CompanyId = 7;

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("QA-lot6-sans-arobase")]
    [InlineData("qa-j18(at)belive.test")]
    [InlineData("QA-lot6 pas un email")]
    [InlineData("qa@belive")]              // domaine sans point : intraitable par le SMTP
    [InlineData("qa@belive.")]
    [InlineData("Karim <karim@belive.tn>")] // forme d'en-tête, pas une adresse de liste
    public void Une_adresse_inutilisable_est_refusee(string? email)
    {
        var act = () => AlertEmailInput.NormalizeEmail(email);

        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("karim@belive.tn", "karim@belive.tn")]
    [InlineData("  karim.hajjami@belive.com.tn  ", "karim.hajjami@belive.com.tn")]
    public void Une_adresse_valide_est_conservee_sans_les_espaces(string saisi, string attendu)
        => AlertEmailInput.NormalizeEmail(saisi).Should().Be(attendu);

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("assurances")]
    [InlineData("n'importe quoi")]
    public void Un_type_d_alerte_qu_aucun_service_n_emet_est_refuse(string? alertType)
    {
        var act = () => AlertEmailInput.NormalizeAlertType(alertType);

        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("assurance")]
    [InlineData("taxe_circulation")]
    [InlineData("visite_technique")]
    [InlineData("entretien")]
    [InlineData("permis")]
    [InlineData("accident")]
    public void Les_types_emis_par_les_services_d_alerte_sont_acceptes(string alertType)
        => AlertEmailInput.NormalizeAlertType(alertType).Should().Be(alertType);

    [Fact]
    public void Le_type_est_ramene_en_minuscules_comme_le_lisent_les_services()
        => AlertEmailInput.NormalizeAlertType(" Assurance ").Should().Be("assurance");

    [Fact]
    public async Task Aucune_ligne_n_est_enregistree_quand_l_adresse_est_vide()
    {
        using var ctx = TestDbContextFactory.Create();
        var tenant = new Mock<ICurrentTenantService>();
        tenant.Setup(x => x.CompanyId).Returns(CompanyId);
        var handler = new CreateAlertEmailCommandHandler(ctx, tenant.Object);

        var act = () => handler.Handle(new CreateAlertEmailCommand("", "assurance"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        (await ctx.AlertEmails.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Une_adresse_valide_est_enregistree_pour_la_societe_de_l_appelant()
    {
        using var ctx = TestDbContextFactory.Create();
        var tenant = new Mock<ICurrentTenantService>();
        tenant.Setup(x => x.CompanyId).Returns(CompanyId);
        var handler = new CreateAlertEmailCommandHandler(ctx, tenant.Object);

        var id = await handler.Handle(
            new CreateAlertEmailCommand(" karim@belive.tn ", "Visite_Technique"), CancellationToken.None);

        var ligne = await ctx.AlertEmails.AsNoTracking().FirstAsync(a => a.Id == id);
        ligne.Email.Should().Be("karim@belive.tn");
        ligne.AlertType.Should().Be("visite_technique");
        ligne.CompanyId.Should().Be(CompanyId);
    }

    /// <summary>Ligne déjà en base, destinataire valide : le point de départ d'une modification.</summary>
    private static async Task<TestGisDbContext> AvecUneLigneValideAsync()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.AlertEmails.Add(new AlertEmail
        {
            Id = 9,
            CompanyId = CompanyId,
            Email = "karim@belive.tn",
            AlertType = "assurance"
        });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    [Theory]
    [InlineData("QA-lot6 pas un email", "assurance")]
    [InlineData("", "assurance")]
    [InlineData("karim@belive.tn", "assurances")]
    public async Task La_modification_applique_les_memes_controles_que_la_creation(string email, string alertType)
    {
        // La modification refuse ce que la création refuse, et avant de toucher la ligne : une
        // saisie rejetée ne remplace pas le destinataire valide déjà enregistré.
        await using var ctx = await AvecUneLigneValideAsync();
        var handler = new UpdateAlertEmailCommandHandler(ctx);

        var act = () => handler.Handle(new UpdateAlertEmailCommand(9, email, alertType), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        var ligne = await ctx.AlertEmails.AsNoTracking().FirstAsync(a => a.Id == 9);
        ligne.Email.Should().Be("karim@belive.tn", "la ligne existante ne doit pas être abîmée");
        ligne.AlertType.Should().Be("assurance");
    }

    [Fact]
    public async Task La_modification_normalise_l_adresse_et_le_type_comme_la_creation()
    {
        await using var ctx = await AvecUneLigneValideAsync();
        var handler = new UpdateAlertEmailCommandHandler(ctx);

        await handler.Handle(
            new UpdateAlertEmailCommand(9, "  contact@belive.com.tn  ", "Visite_Technique"), CancellationToken.None);

        var ligne = await ctx.AlertEmails.AsNoTracking().FirstAsync(a => a.Id == 9);
        ligne.Email.Should().Be("contact@belive.com.tn");
        ligne.AlertType.Should().Be("visite_technique");
    }
}
