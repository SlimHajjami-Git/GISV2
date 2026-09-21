using System.Security.Claims;
using FluentAssertions;
using GisAPI.Domain.Entities;
using GisAPI.Hubs;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Middleware;

/// <summary>
/// Hub GPS de la flotte (positions de tous les véhicules de la société) : jamais ouvert à un
/// compte chauffeur. Le claim « acct » ne suffit pas — un salarié converti garde jusqu'à
/// 24 h un jeton émis avant sa conversion, sans ce claim ; la ligne users tranche, lue une
/// fois à la connexion.
/// </summary>
public class GpsHubDriverTests
{
    private const int CompanyId = 7;

    private sealed record Connexion(bool Aborted, List<string> Groups);

    private static async Task<Connexion> ConnectAsync(string accountTypeEnBase, bool claimChauffeur)
    {
        await using var ctx = TestDbContextFactory.Create();
        var compte = TestDataBuilder.CreateUser(id: 61, companyId: CompanyId, email: "c@test.com");
        compte.AccountType = accountTypeEnBase;
        ctx.Users.Add(compte);
        await ctx.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "61"),
            new("companyId", CompanyId.ToString()),
        };
        if (claimChauffeur) claims.Add(new Claim(JwtClaims.AccountType, UserAccountTypes.Driver));

        var aborted = false;
        var context = new Mock<HubCallerContext>();
        context.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")));
        context.SetupGet(c => c.ConnectionId).Returns("cnx-1");
        context.Setup(c => c.Abort()).Callback(() => aborted = true);

        var groups = new List<string>();
        var groupManager = new Mock<IGroupManager>();
        groupManager.Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, group, _) => groups.Add(group))
            .Returns(Task.CompletedTask);

        var hub = new GpsHub(NullLogger<GpsHub>.Instance, ctx) { Context = context.Object, Groups = groupManager.Object };
        await hub.OnConnectedAsync();
        return new Connexion(aborted, groups);
    }

    [Fact]
    public async Task Un_jeton_emis_avant_le_passage_en_chauffeur_est_refuse_d_apres_la_base()
    {
        var cnx = await ConnectAsync(UserAccountTypes.Driver, claimChauffeur: false);

        cnx.Aborted.Should().BeTrue();
        cnx.Groups.Should().BeEmpty("ni le groupe de la société (positions de la flotte), ni le sien");
    }

    [Fact]
    public async Task Un_jeton_chauffeur_est_refuse()
    {
        var cnx = await ConnectAsync(UserAccountTypes.Driver, claimChauffeur: true);

        cnx.Aborted.Should().BeTrue();
        cnx.Groups.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_salarie_rejoint_ses_groupes()
    {
        var cnx = await ConnectAsync(UserAccountTypes.Staff, claimChauffeur: false);

        cnx.Aborted.Should().BeFalse();
        cnx.Groups.Should().Equal($"company_{CompanyId}", "user_61");
    }
}
