using FluentAssertions;
using GisAPI.Application.Features.AlertEmails.Commands;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GisAPI.Tests.Application.AlertEmails;

/// <summary>
/// Recette GPA sur la liste de diffusion des échéances.
/// DEF-052 : un type hors des six proposés (« vitesse ») était enregistré et donnait un
/// destinataire qu'aucun envoi ne cible. DEF-053 : deux POST identiques créaient deux lignes
/// pour le même couple (adresse, type). ExceptionHandlingMiddleware traduit
/// <see cref="DomainException"/> en 400 et <see cref="ConflictException"/> en 409.
/// </summary>
public class AlertEmailRecetteTests
{
    private const int CompanyId = 7;
    private const int AutreSociete = 8;

    private static TestGisDbContext Contexte(params AlertEmail[] lignes)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.AlertEmails.AddRange(lignes);
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    private static AlertEmail Ligne(int id, string email, string type, int companyId = CompanyId) =>
        new() { Id = id, CompanyId = companyId, Email = email, AlertType = type };

    private static CreateAlertEmailCommandHandler Creation(TestGisDbContext ctx) =>
        new(ctx, TestDbContextFactory.CreateMockTenantService(companyId: CompanyId).Object);

    // ── DEF-052 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Creation_type_vitesse_refusee_et_rien_n_est_enregistre()
    {
        using var ctx = Contexte();

        var act = () => Creation(ctx).Handle(
            new CreateAlertEmailCommand("qa-j18@belive.test", "vitesse"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Type d'alerte inconnu : vitesse");
        (await ctx.AlertEmails.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Modification_vers_le_type_vitesse_refusee_et_la_ligne_reste_intacte()
    {
        using var ctx = Contexte(Ligne(4, "qa-j18@belive.test", "assurance"));

        var act = () => new UpdateAlertEmailCommandHandler(ctx).Handle(
            new UpdateAlertEmailCommand(4, "qa-j18@belive.test", "vitesse"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Type d'alerte inconnu : vitesse");
        (await ctx.AlertEmails.AsNoTracking().SingleAsync()).AlertType.Should().Be("assurance");
    }

    // ── DEF-053 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Le_second_POST_identique_est_refuse_en_conflit_et_une_seule_ligne_reste()
    {
        using var ctx = Contexte();
        var handler = Creation(ctx);
        await handler.Handle(new CreateAlertEmailCommand("qa-j18@belive.test", "assurance"), CancellationToken.None);
        ctx.ChangeTracker.Clear();

        var act = () => handler.Handle(
            new CreateAlertEmailCommand("qa-j18@belive.test", "assurance"), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("qa-j18@belive.test reçoit déjà les alertes « Assurance ».");
        (await ctx.AlertEmails.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("  QA-J18@Belive.test ", "Assurance")]   // casse et espaces : même destinataire
    [InlineData("qa-j18@belive.test", " ASSURANCE")]
    public async Task Le_doublon_est_reconnu_apres_normalisation(string email, string type)
    {
        using var ctx = Contexte(Ligne(5, "qa-j18@belive.test", "assurance"));

        var act = () => Creation(ctx).Handle(new CreateAlertEmailCommand(email, type), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        (await ctx.AlertEmails.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task La_meme_adresse_reste_acceptee_pour_un_autre_type_ou_dans_une_autre_societe()
    {
        using var ctx = Contexte(
            Ligne(5, "qa-j18@belive.test", "assurance"),
            Ligne(6, "qa-j18@belive.test", "entretien", companyId: AutreSociete));
        var handler = Creation(ctx);

        await handler.Handle(new CreateAlertEmailCommand("qa-j18@belive.test", "entretien"), CancellationToken.None);
        await handler.Handle(new CreateAlertEmailCommand("qa-j18@belive.test", "visite_technique"), CancellationToken.None);

        (await ctx.AlertEmails.CountAsync(a => a.CompanyId == CompanyId)).Should().Be(3);
    }

    [Fact]
    public async Task Modifier_une_ligne_pour_en_faire_un_doublon_est_refuse_et_la_ligne_reste_intacte()
    {
        using var ctx = Contexte(
            Ligne(5, "qa-j18@belive.test", "assurance"),
            Ligne(6, "karim@belive.tn", "assurance"));

        var act = () => new UpdateAlertEmailCommandHandler(ctx).Handle(
            new UpdateAlertEmailCommand(6, "QA-J18@belive.test", "assurance"), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        var ligne = await ctx.AlertEmails.AsNoTracking().SingleAsync(a => a.Id == 6);
        ligne.Email.Should().Be("karim@belive.tn");
    }

    [Fact]
    public async Task Une_ligne_peut_etre_reenregistree_sans_se_prendre_pour_son_propre_doublon()
    {
        // Ligne 7 : seule la casse de l'adresse change ; ligne 5 : doublon ancien réenregistré tel
        // quel. Dans les deux cas le couple est inchangé, le handler ne lance aucun contrôle.
        using var ctx = Contexte(
            Ligne(5, "qa-j18@belive.test", "assurance"),
            Ligne(6, "qa-j18@belive.test", "assurance"),
            Ligne(7, "karim@belive.tn", "entretien"));
        var handler = new UpdateAlertEmailCommandHandler(ctx);

        await handler.Handle(new UpdateAlertEmailCommand(7, "Karim@belive.tn", "entretien"), CancellationToken.None);
        await handler.Handle(new UpdateAlertEmailCommand(5, "qa-j18@belive.test", "assurance"), CancellationToken.None);

        (await ctx.AlertEmails.AsNoTracking().SingleAsync(a => a.Id == 7)).Email.Should().Be("Karim@belive.tn");
    }

    [Fact]
    public async Task Le_controle_d_unicite_ignore_la_ligne_designee_et_elle_seule()
    {
        // Filet du handler de modification : si ses comparaisons C# et SQL divergent, la ligne
        // modifiée ne doit pas se refuser elle-même, sans masquer un vrai doublon.
        using var ctx = Contexte(
            Ligne(5, "qa-j18@belive.test", "assurance"),
            Ligne(6, "karim@belive.tn", "assurance"));

        var sansExclusion = () => AlertEmailInput.EnsureUniqueAsync(
            ctx, CompanyId, "QA-J18@belive.test", "assurance", exceptId: null, CancellationToken.None);
        var ligneElleMeme = () => AlertEmailInput.EnsureUniqueAsync(
            ctx, CompanyId, "QA-J18@belive.test", "assurance", exceptId: 5, CancellationToken.None);
        var autreLigne = () => AlertEmailInput.EnsureUniqueAsync(
            ctx, CompanyId, "QA-J18@belive.test", "assurance", exceptId: 6, CancellationToken.None);

        await sansExclusion.Should().ThrowAsync<ConflictException>();
        await ligneElleMeme.Should().NotThrowAsync();
        await autreLigne.Should().ThrowAsync<ConflictException>();
    }
}
