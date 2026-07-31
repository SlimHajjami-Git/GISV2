using FluentAssertions;
using GisAPI.Application.Features.Auth.Commands.ConfirmEmail;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Application.Auth;

/// <summary>
/// Confirmation d'adresse email : le passage de « pending » à « active ».
///
/// C'est la seule barrière entre « quelqu'un a tapé une adresse » et « quelqu'un
/// possède cette adresse ». Les cas d'erreur comptent donc autant que le cas
/// nominal — et le lien recliqué, qui arrive tout le temps, ne doit pas ressembler
/// à une panne.
/// </summary>
public class ConfirmEmailCommandTests
{
    private const string Token = "jeton-de-confirmation";

    private static (ConfirmEmailCommandHandler handler, TestGisDbContext ctx) Setup(
        string status = "pending", DateTime? expiry = null, string? token = Token)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Users.Add(new User
        {
            Id = 7,
            FirstName = "Sonia",
            LastName = "Ben Salah",
            Email = "sonia@exemple.tn",
            CompanyId = 1,
            PasswordHash = "hashed",
            Status = status,
            EmailVerificationToken = token,
            EmailVerificationExpiresAt = expiry ?? DateTime.UtcNow.AddHours(24)
        });
        ctx.SaveChanges();

        return (new ConfirmEmailCommandHandler(ctx, NullLogger<ConfirmEmailCommandHandler>.Instance), ctx);
    }

    [Fact]
    public async Task Un_jeton_valide_active_le_compte_et_consomme_le_jeton()
    {
        var (handler, ctx) = Setup();

        var result = await handler.Handle(new ConfirmEmailCommand(Token), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.AlreadyConfirmed.Should().BeFalse();

        var user = await ctx.Users.SingleAsync();
        user.Status.Should().Be("active");
        user.EmailVerificationExpiresAt.Should().BeNull("le jeton ne doit plus rien activer");
    }

    [Fact]
    public async Task Le_meme_lien_reclique_repond_deja_confirme_et_non_une_erreur()
    {
        // C'est le cas le PLUS fréquent : l'utilisateur reclique, ou l'antivirus de
        // sa messagerie préouvre l'URL avant lui. Effacer le jeton à la première
        // confirmation rendait ce chemin inatteignable et affichait « lien
        // invalide » — c'est exactement ce qu'un essai réel a montré.
        var (handler, _) = Setup();

        await handler.Handle(new ConfirmEmailCommand(Token), CancellationToken.None);
        var second = await handler.Handle(new ConfirmEmailCommand(Token), CancellationToken.None);

        second.Success.Should().BeTrue();
        second.AlreadyConfirmed.Should().BeTrue();
    }

    [Fact]
    public async Task Un_jeton_expire_est_refuse_et_le_compte_reste_en_attente()
    {
        var (handler, ctx) = Setup(expiry: DateTime.UtcNow.AddHours(-1));

        var act = () => handler.Handle(new ConfirmEmailCommand(Token), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        (await ctx.Users.SingleAsync()).Status.Should().Be("pending");
    }

    [Fact]
    public async Task Un_jeton_inconnu_est_refuse()
    {
        var (handler, ctx) = Setup();

        var act = () => handler.Handle(new ConfirmEmailCommand("jeton-invente"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        (await ctx.Users.SingleAsync()).Status.Should().Be("pending");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Un_jeton_vide_est_refuse(string token)
    {
        var (handler, _) = Setup();

        var act = () => handler.Handle(new ConfirmEmailCommand(token), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }
}
