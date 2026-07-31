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
        user.EmailVerificationToken.Should().BeNull("un jeton consommé ne doit plus servir");
        user.EmailVerificationExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task Un_lien_reclique_est_accueilli_sans_erreur()
    {
        // Cas très fréquent : l'utilisateur reclique, ou sa messagerie préouvre les
        // liens. Lui montrer « lien invalide » serait alarmant et faux.
        var (handler, ctx) = Setup(status: "active");

        var result = await handler.Handle(new ConfirmEmailCommand(Token), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.AlreadyConfirmed.Should().BeTrue();
        (await ctx.Users.SingleAsync()).EmailVerificationToken.Should().BeNull();
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
