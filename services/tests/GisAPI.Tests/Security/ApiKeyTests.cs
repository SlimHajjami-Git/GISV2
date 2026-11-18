using FluentAssertions;
using GisAPI.Application.Common.Security;
using GisAPI.Application.Features.ApiKeys.Commands.CreateApiKey;
using GisAPI.Application.Features.ApiKeys.Commands.ExchangeApiKey;
using GisAPI.Application.Features.ApiKeys.Commands.RevokeApiKey;
using GisAPI.Application.Features.ApiKeys.Queries.GetApiKeys;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Infrastructure.Services;
using GisAPI.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Security;

/// <summary>
/// Clé d'automatisation de l'API (migration 069, plainte client du 18/11/2026) : une tâche planifiée
/// ne peut pas générer un jeton à la main chaque jour. La clé, créée une fois par l'administrateur,
/// ne lit RIEN elle-même : elle s'échange contre un jeton à usage unique, dans les mêmes conditions
/// qu'à la main, relues à chaque échange, et dans un quota. Le chemin HTTP complet est dans
/// ApiKeyServeurTests.
/// </summary>
public class ApiKeyTests
{
    private const string MotDePasse = "MotDePasse1!";

    // ── Format ──

    [Fact]
    public void La_cle_a_son_prefixe_et_n_est_jamais_prise_pour_un_jeton()
    {
        var a = ApiKeyAccess.Generate();
        var b = ApiKeyAccess.Generate();

        a.Should().StartWith("clyk_").And.MatchRegex("^[A-Za-z0-9_-]+$");
        a.Length.Should().BeGreaterThan(40);
        a.Should().NotBe(b);
        ApiKeyAccess.DisplayPrefix(a).Should().HaveLength(12);

        // Une clé présentée comme jeton n'est pas aiguillée vers les jetons, et inversement.
        ApiTokenAccess.LooksLikeToken(a).Should().BeFalse();
        ApiKeyAccess.LooksLikeKey(ApiTokenAccess.Generate()).Should().BeFalse();
    }

    [Fact]
    public void Le_statut_d_une_cle_la_revocation_l_emporte_sur_l_expiration()
    {
        var now = DateTime.UtcNow;
        ApiKeyAccess.Status(new ApiKey { ExpiresAt = now.AddDays(1) }, now).Should().Be("active");
        ApiKeyAccess.Status(new ApiKey { ExpiresAt = now.AddDays(-1) }, now).Should().Be("expired");
        ApiKeyAccess.Status(new ApiKey { ExpiresAt = now.AddDays(-1), RevokedAt = now }, now).Should().Be("revoked");
        new ApiKey { ExpiresAt = now.AddDays(1), RevokedAt = now }.IsActive(now).Should().BeFalse();
    }

    // ── Jeu de données ──

    private static async Task<TestGisDbContext> SeedAsync()
    {
        var ctx = TestDbContextFactory.Create();
        { var s = TestDataBuilder.CreateSociete(1, null); s.ApiAccessEnabled = true; ctx.Societes.Add(s); }
        { var s = TestDataBuilder.CreateSociete(2, null); s.ApiAccessEnabled = true; ctx.Societes.Add(s); }
        ctx.Roles.Add(new Role { Id = 1, Name = "Gestionnaire", SocieteId = 1, IsCompanyAdmin = true });
        ctx.Roles.Add(new Role { Id = 2, Name = "Administrateur", SocieteId = 1, IsSystemRole = true });
        ctx.Roles.Add(new Role { Id = 3, Name = "Gestionnaire", SocieteId = 2, IsCompanyAdmin = true });
        ctx.Roles.Add(new Role { Id = 4, Name = "Opérateur", SocieteId = 1, IsCompanyAdmin = false });

        var hash = new PasswordHasher().HashPassword(MotDePasse);
        User Make(int id, int company, string email, int roleId, Action<User>? tweak = null)
        {
            var u = TestDataBuilder.CreateUser(id: id, companyId: company, email: email);
            u.RoleId = roleId;
            u.PasswordHash = hash;
            tweak?.Invoke(u);
            return u;
        }
        ctx.Users.AddRange(
            Make(10, 1, "client@exemple.tn", 1, u => { u.FirstName = "Sami"; u.LastName = "Ben Ali"; }),
            Make(11, 1, "chauffeur@exemple.tn", 1, u => u.AccountType = UserAccountTypes.Driver),
            Make(12, 1, "systeme@exemple.tn", 2),
            Make(13, 1, "inactif@exemple.tn", 1, u => u.Status = "suspended"),
            Make(14, 1, "employe@exemple.tn", 4),
            Make(15, 1, "second@exemple.tn", 1),
            Make(20, 2, "autre@autre.tn", 3));
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static CreateApiKeyCommandHandler Create(TestGisDbContext ctx, int companyId, int userId) =>
        new(ctx, TestDbContextFactory.CreateMockTenantService(companyId, userId).Object,
            new PasswordHasher(), NullLogger<CreateApiKeyCommandHandler>.Instance);

    private static ExchangeApiKeyCommandHandler Exchange(TestGisDbContext ctx) =>
        new(ctx, NullLogger<ExchangeApiKeyCommandHandler>.Instance);

    private static RevokeApiKeyCommandHandler Revoke(TestGisDbContext ctx, int companyId, int userId) =>
        new(ctx, TestDbContextFactory.CreateMockTenantService(companyId, userId).Object);

    private static GetApiKeysQueryHandler List(TestGisDbContext ctx, int companyId, int userId) =>
        new(ctx, TestDbContextFactory.CreateMockTenantService(companyId, userId).Object);

    private static Task<ApiKeyCreatedDto> NouvelleCleAsync(TestGisDbContext ctx, string? label = "Serveur de paie") =>
        Create(ctx, 1, 10).Handle(new CreateApiKeyCommand(label, "client@exemple.tn", MotDePasse, "198.51.100.4"), CancellationToken.None);

    // ── Création ──

    [Fact]
    public async Task La_creation_stocke_l_empreinte_jamais_la_cle_et_laisse_une_trace()
    {
        await using var ctx = await SeedAsync();

        var created = await NouvelleCleAsync(ctx);

        created.Key.Should().StartWith("clyk_");
        created.Label.Should().Be("Serveur de paie");
        created.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(ApiKeyAccess.LifetimeDays), TimeSpan.FromMinutes(1));

        var row = ctx.ApiKeys.Single();
        row.CompanyId.Should().Be(1);
        row.UserId.Should().Be(10);
        row.CreatedIp.Should().Be("198.51.100.4");
        row.KeyHash.Should().Be(ApiKeyAccess.Hash(created.Key)).And.NotContain(created.Key);
        row.KeyPrefix.Should().Be(created.Key[..12]);

        var audit = ctx.AuditLogs.Single(a => a.Action == "api_key_created");
        audit.UserId.Should().Be(10);
        new[] { audit.EntityName, audit.Description, audit.IpAddress }
            .Should().OnlyContain(champ => champ == null || !champ.Contains(created.Key));
    }

    [Theory]
    [InlineData("client@exemple.tn", "mauvais")]
    [InlineData("autre@autre.tn", MotDePasse)]   // bon mot de passe, mais pas l'e-mail de la session
    [InlineData("", MotDePasse)]
    [InlineData("client@exemple.tn", "")]
    public async Task La_creation_exige_l_email_et_le_mot_de_passe_de_la_session(string email, string password)
    {
        await using var ctx = await SeedAsync();

        var act = () => Create(ctx, 1, 10).Handle(new CreateApiKeyCommand("x", email, password), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        ctx.ApiKeys.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, 14, "employe@exemple.tn", ApiTokenAccess.AdminOnlyMessage)]
    [InlineData(1, 11, "chauffeur@exemple.tn", ApiTokenAccess.DriverMessage)]
    [InlineData(1, 12, "systeme@exemple.tn", ApiTokenAccess.SystemRoleMessage)]
    [InlineData(1, 13, "inactif@exemple.tn", ApiTokenAccess.InactiveMessage)]
    public async Task La_creation_est_reservee_a_l_administrateur_actif(int company, int userId, string email, string message)
    {
        await using var ctx = await SeedAsync();

        var act = () => Create(ctx, company, userId).Handle(new CreateApiKeyCommand("x", email, MotDePasse), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).WithMessage(message);
        ctx.ApiKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task Sans_activation_par_Belive_pas_de_cle()
    {
        await using var ctx = await SeedAsync();
        (await ctx.Societes.FindAsync(2))!.ApiAccessEnabled = false;
        await ctx.SaveChangesAsync();

        var act = () => Create(ctx, 2, 20).Handle(new CreateApiKeyCommand("x", "autre@autre.tn", MotDePasse), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).WithMessage(ApiTokenAccess.NotEnabledMessage);
        ctx.ApiKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task Deux_cles_actives_au_plus_par_societe_une_revocation_libere_la_place()
    {
        await using var ctx = await SeedAsync();
        var premiere = await NouvelleCleAsync(ctx, "A");
        await NouvelleCleAsync(ctx, "B");

        // Le plafond est celui de la SOCIÉTÉ : un second administrateur ne le contourne pas.
        var act = () => Create(ctx, 1, 15).Handle(new CreateApiKeyCommand("C", "second@exemple.tn", MotDePasse), CancellationToken.None);
        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(ApiKeyAccess.TooManyActiveMessage);

        // L'autre société n'est pas concernée.
        await Create(ctx, 2, 20).Handle(new CreateApiKeyCommand("Z", "autre@autre.tn", MotDePasse), CancellationToken.None);

        await Revoke(ctx, 1, 10).Handle(new RevokeApiKeyCommand(premiere.Id), CancellationToken.None);
        await Create(ctx, 1, 15).Handle(new CreateApiKeyCommand("C", "second@exemple.tn", MotDePasse), CancellationToken.None);
    }

    [Fact]
    public async Task Le_nom_est_facultatif_et_borne()
    {
        await using var ctx = await SeedAsync();

        (await NouvelleCleAsync(ctx, "   ")).Label.Should().Be(ApiKeyAccess.DefaultLabel);

        var act = () => NouvelleCleAsync(ctx, new string('x', ApiKeyAccess.LabelMaxLength + 1));
        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(ApiKeyAccess.LabelTooLongMessage);
    }

    // ── Échange ──

    [Fact]
    public async Task L_echange_rend_un_jeton_a_usage_unique_aux_droits_du_createur()
    {
        await using var ctx = await SeedAsync();
        var cle = await NouvelleCleAsync(ctx);

        var result = await Exchange(ctx).Handle(new ExchangeApiKeyCommand(" " + cle.Key + " ", "203.0.113.9"), CancellationToken.None);

        result.StatusCode.Should().Be(200);
        result.Token!.Token.Should().StartWith("cly_");
        result.Token.ExpiresInSeconds.Should().Be(ApiTokenAccess.LifetimeMinutes * 60);

        var jeton = ctx.ApiTokens.Single();
        jeton.ApiKeyId.Should().Be(cle.Id);
        jeton.UserId.Should().Be(10);
        jeton.CompanyId.Should().Be(1);
        jeton.CreatedIp.Should().Be("203.0.113.9");
        jeton.TokenHash.Should().Be(ApiTokenAccess.Hash(result.Token.Token));
        jeton.IsUsable(DateTime.UtcNow).Should().BeTrue();

        var row = ctx.ApiKeys.Single();
        row.LastUsedIp.Should().Be("203.0.113.9");
        row.LastUsedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task La_cle_d_une_autre_societe_ne_donne_que_les_droits_de_son_createur()
    {
        await using var ctx = await SeedAsync();
        var cle = await Create(ctx, 2, 20).Handle(new CreateApiKeyCommand("Z", "autre@autre.tn", MotDePasse), CancellationToken.None);

        var result = await Exchange(ctx).Handle(new ExchangeApiKeyCommand(cle.Key), CancellationToken.None);

        result.StatusCode.Should().Be(200);
        var jeton = ctx.ApiTokens.Single();
        jeton.CompanyId.Should().Be(2);
        jeton.UserId.Should().Be(20);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pas-une-cle")]
    [InlineData("clyk_inconnue0000000000000000000000000000000")]
    public async Task Une_cle_absente_mal_formee_ou_inconnue_c_est_401(string? presentee)
    {
        await using var ctx = await SeedAsync();
        await NouvelleCleAsync(ctx);

        var result = await Exchange(ctx).Handle(new ExchangeApiKeyCommand(presentee), CancellationToken.None);

        result.StatusCode.Should().Be(401);
        result.Message.Should().Be(ApiKeyAccess.InvalidKeyMessage);
        result.Token.Should().BeNull();
        ctx.ApiTokens.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_jeton_ne_s_echange_pas_contre_un_autre_jeton()
    {
        await using var ctx = await SeedAsync();
        var cle = await NouvelleCleAsync(ctx);
        var premier = await Exchange(ctx).Handle(new ExchangeApiKeyCommand(cle.Key), CancellationToken.None);

        var result = await Exchange(ctx).Handle(new ExchangeApiKeyCommand(premier.Token!.Token), CancellationToken.None);

        result.StatusCode.Should().Be(401);
        ctx.ApiTokens.Should().ContainSingle();
    }

    [Fact]
    public async Task Une_cle_revoquee_ou_expiree_c_est_401_avec_sa_cause()
    {
        await using var ctx = await SeedAsync();
        var revoquee = await NouvelleCleAsync(ctx, "A");
        var expiree = await NouvelleCleAsync(ctx, "B");
        await Revoke(ctx, 1, 10).Handle(new RevokeApiKeyCommand(revoquee.Id), CancellationToken.None);
        ctx.ApiKeys.Single(k => k.Id == expiree.Id).ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await ctx.SaveChangesAsync();

        var r1 = await Exchange(ctx).Handle(new ExchangeApiKeyCommand(revoquee.Key), CancellationToken.None);
        var r2 = await Exchange(ctx).Handle(new ExchangeApiKeyCommand(expiree.Key), CancellationToken.None);

        (r1.StatusCode, r1.Message).Should().Be((401, ApiKeyAccess.RevokedMessage));
        (r2.StatusCode, r2.Message).Should().Be((401, ApiKeyAccess.ExpiredMessage));
        ctx.ApiTokens.Should().BeEmpty();
    }

    [Fact]
    public async Task La_cle_tombe_d_elle_meme_si_son_createur_ne_peut_plus_utiliser_l_API()
    {
        await using var ctx = await SeedAsync();
        var cle = await NouvelleCleAsync(ctx);
        var createur = ctx.Users.Single(u => u.Id == 10);
        var societe = (await ctx.Societes.FindAsync(1))!;

        async Task<(int, string?)> Echanger()
        {
            var r = await Exchange(ctx).Handle(new ExchangeApiKeyCommand(cle.Key), CancellationToken.None);
            return (r.StatusCode, r.Message);
        }

        createur.RoleId = 4;   // n'est plus administrateur
        await ctx.SaveChangesAsync();
        (await Echanger()).Should().Be((403, ApiKeyAccess.OwnerNotAdminMessage));

        createur.RoleId = 1;
        createur.Status = "suspended";
        await ctx.SaveChangesAsync();
        (await Echanger()).Should().Be((403, ApiKeyAccess.OwnerInactiveMessage));

        createur.Status = "active";
        societe.ApiAccessEnabled = false;   // Belive coupe l'accès API de la société
        await ctx.SaveChangesAsync();
        (await Echanger()).Should().Be((403, ApiTokenAccess.NotEnabledMessage));

        ctx.ApiTokens.Should().BeEmpty();

        societe.ApiAccessEnabled = true;
        await ctx.SaveChangesAsync();
        (await Echanger()).Item1.Should().Be(200);
    }

    [Fact]
    public async Task Le_quota_compte_les_echanges_des_24_dernieres_heures_par_cle()
    {
        await using var ctx = await SeedAsync();
        var cle = await NouvelleCleAsync(ctx);
        var now = DateTime.UtcNow;
        ApiToken Ancien(int i) => new()
        {
            CompanyId = 1, UserId = 10, ApiKeyId = cle.Id, TokenPrefix = "cly_ancien",
            TokenHash = i.ToString("D64"), ExpiresAt = now, UsedAt = now
        };

        // 99 échanges récents (jetons déjà utilisés) et 5 de la veille.
        ctx.ApiTokens.AddRange(Enumerable.Range(0, 104).Select(Ancien));
        await ctx.SaveChangesAsync();
        foreach (var t in ctx.ApiTokens.OrderBy(t => t.Id).Take(5))
            t.CreatedAt = now.AddHours(-25);
        await ctx.SaveChangesAsync();

        (await Exchange(ctx).Handle(new ExchangeApiKeyCommand(cle.Key), CancellationToken.None)).StatusCode.Should().Be(200);

        var refus = await Exchange(ctx).Handle(new ExchangeApiKeyCommand(cle.Key), CancellationToken.None);
        refus.StatusCode.Should().Be(429);
        refus.Message.Should().Be(ApiKeyAccess.QuotaMessage);
    }

    [Fact]
    public async Task Une_tache_qui_echange_sans_appeler_bute_sur_les_jetons_en_attente()
    {
        await using var ctx = await SeedAsync();
        var cle = await NouvelleCleAsync(ctx);

        for (var i = 0; i < ApiTokenAccess.MaxPendingPerUser; i++)
            (await Exchange(ctx).Handle(new ExchangeApiKeyCommand(cle.Key), CancellationToken.None)).StatusCode.Should().Be(200);

        var refus = await Exchange(ctx).Handle(new ExchangeApiKeyCommand(cle.Key), CancellationToken.None);
        (refus.StatusCode, refus.Message).Should().Be((429, ApiKeyAccess.TooManyPendingMessage));
    }

    // ── Révocation ──

    [Fact]
    public async Task Un_autre_administrateur_de_la_societe_revoque_la_cle_et_rejouer_est_sans_effet()
    {
        await using var ctx = await SeedAsync();
        var cle = await NouvelleCleAsync(ctx);

        await Revoke(ctx, 1, 15).Handle(new RevokeApiKeyCommand(cle.Id, "198.51.100.8"), CancellationToken.None);
        var row = ctx.ApiKeys.Single();
        var revokedAt = row.RevokedAt;
        revokedAt.Should().NotBeNull();
        row.RevokedByUserId.Should().Be(15);
        ctx.AuditLogs.Single(a => a.Action == "api_key_revoked").EntityId.Should().Be(cle.Id);

        await Revoke(ctx, 1, 10).Handle(new RevokeApiKeyCommand(cle.Id), CancellationToken.None);
        ctx.ApiKeys.Single().RevokedAt.Should().Be(revokedAt);
        ctx.AuditLogs.Count(a => a.Action == "api_key_revoked").Should().Be(1);
    }

    [Fact]
    public async Task Une_autre_societe_ne_voit_ni_ne_revoque_la_cle()
    {
        await using var ctx = await SeedAsync();
        var cle = await NouvelleCleAsync(ctx);

        var act = () => Revoke(ctx, 2, 20).Handle(new RevokeApiKeyCommand(cle.Id), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
        ctx.ApiKeys.Single().RevokedAt.Should().BeNull();

        (await List(ctx, 2, 20).Handle(new GetApiKeysQuery(), CancellationToken.None)).Keys.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_non_administrateur_ne_gere_pas_les_cles()
    {
        await using var ctx = await SeedAsync();
        var cle = await NouvelleCleAsync(ctx);

        await FluentActions.Awaiting(() => Revoke(ctx, 1, 14).Handle(new RevokeApiKeyCommand(cle.Id), CancellationToken.None))
            .Should().ThrowAsync<ForbiddenAccessException>();
        await FluentActions.Awaiting(() => List(ctx, 1, 14).Handle(new GetApiKeysQuery(), CancellationToken.None))
            .Should().ThrowAsync<ForbiddenAccessException>();
    }

    [Fact]
    public async Task Une_societe_coupee_par_Belive_peut_encore_revoquer_ses_cles()
    {
        await using var ctx = await SeedAsync();
        var cle = await NouvelleCleAsync(ctx);
        (await ctx.Societes.FindAsync(1))!.ApiAccessEnabled = false;
        await ctx.SaveChangesAsync();

        await Revoke(ctx, 1, 10).Handle(new RevokeApiKeyCommand(cle.Id), CancellationToken.None);

        ctx.ApiKeys.Single().RevokedAt.Should().NotBeNull();
    }

    // ── Liste ──

    [Fact]
    public async Task La_liste_montre_les_cles_de_la_societe_sans_secret_avec_leur_activite()
    {
        await using var ctx = await SeedAsync();
        var active = await NouvelleCleAsync(ctx, "Active");
        var revoquee = await NouvelleCleAsync(ctx, "Révoquée");
        await Create(ctx, 2, 20).Handle(new CreateApiKeyCommand("Autre société", "autre@autre.tn", MotDePasse), CancellationToken.None);
        await Exchange(ctx).Handle(new ExchangeApiKeyCommand(active.Key, "203.0.113.9"), CancellationToken.None);
        await Exchange(ctx).Handle(new ExchangeApiKeyCommand(active.Key, "203.0.113.9"), CancellationToken.None);
        await Revoke(ctx, 1, 10).Handle(new RevokeApiKeyCommand(revoquee.Id), CancellationToken.None);

        // Un second administrateur voit les clés de son collègue : ce sont des objets de la société.
        var liste = await List(ctx, 1, 15).Handle(new GetApiKeysQuery(), CancellationToken.None);

        liste.MaxActive.Should().Be(ApiKeyAccess.MaxActivePerCompany);
        liste.MaxExchangesPer24Hours.Should().Be(ApiKeyAccess.MaxExchangesPer24Hours);
        liste.Keys.Select(k => k.Label).Should().BeEquivalentTo("Active", "Révoquée");

        var a = liste.Keys.Single(k => k.Label == "Active");
        a.Status.Should().Be("active");
        a.Exchanges24Hours.Should().Be(2);
        a.LastUsedIp.Should().Be("203.0.113.9");
        a.CreatedBy.Should().Be("Sami Ben Ali");
        a.KeyPrefix.Should().Be(active.Key[..12]);
        liste.Keys.Single(k => k.Label == "Révoquée").Status.Should().Be("revoked");

        var json = System.Text.Json.JsonSerializer.Serialize(liste);
        json.Should().NotContain(active.Key).And.NotContain(revoquee.Key);
    }
}
