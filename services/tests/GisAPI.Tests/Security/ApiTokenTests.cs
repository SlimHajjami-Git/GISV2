using FluentAssertions;
using GisAPI.Application.Common.Security;
using GisAPI.Application.Features.ApiTokens.Commands.GenerateApiToken;
using GisAPI.Application.Features.ApiTokens.Queries.GetMyApiTokens;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Infrastructure.Services;
using GisAPI.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Security;

/// <summary>
/// Jetons d'API à usage unique (migration 064) : le jeton ne lit que les arrêts, sur une période
/// bornée, seule son empreinte est conservée, et il n'est généré qu'après ressaisie de l'e-mail et du
/// mot de passe de la session ouverte. La consommation unique et le chemin HTTP sont dans
/// ApiTokenAuthenticationTests et ApiTokenServeurTests.
/// </summary>
public class ApiTokenTests
{
    private const string Debut = "2026-10-01";
    private const string Fin = "2026-10-28";
    private const string MotDePasse = "MotDePasse1!";

    // ── Format du jeton ──

    [Fact]
    public void Le_jeton_a_le_prefixe_et_ne_se_repete_pas()
    {
        var a = ApiTokenAccess.Generate();
        var b = ApiTokenAccess.Generate();

        a.Should().StartWith("cly_");
        a.Length.Should().BeGreaterThan(40);
        a.Should().NotBe(b);
        a.Should().MatchRegex("^[A-Za-z0-9_-]+$");
    }

    [Fact]
    public void L_empreinte_est_stable_et_ne_contient_pas_le_jeton()
    {
        var token = ApiTokenAccess.Generate();

        ApiTokenAccess.Hash(token).Should().Be(ApiTokenAccess.Hash(token));
        ApiTokenAccess.Hash(token).Should().HaveLength(64).And.NotContain(token[4..12]);
        ApiTokenAccess.Hash(token).Should().NotBe(ApiTokenAccess.Hash(token + "x"));
        ApiTokenAccess.DisplayPrefix(token).Should().HaveLength(12).And.Be(token[..12]);
    }

    [Theory]
    [InlineData("cly_abc", true)]
    [InlineData("eyJhbGciOi", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Seul_un_jeton_prefixe_est_pris_pour_un_jeton_d_api(string? value, bool attendu) =>
        ApiTokenAccess.LooksLikeToken(value).Should().Be(attendu);

    // ── Ce qu'un jeton peut appeler ──

    [Theory]
    [InlineData("/api/reports/stops/12")]
    [InlineData("/api/reports/stops")]
    [InlineData("/api/vehiclestops")]
    [InlineData("/api/vehiclestops/vehicle/12")]
    [InlineData("/API/VehicleStops")]
    public void La_lecture_des_arrets_est_ouverte(string path) =>
        ApiTokenAccess.Evaluate("GET", path, Debut, Fin, null).Should().BeNull();

    [Theory]
    [InlineData("/api/vehicles")]
    [InlineData("/api/vehicles/with-positions")]
    [InlineData("/api/reports/trips/3")]
    [InlineData("/api/api-tokens")]
    [InlineData("/api/auth/login")]
    [InlineData("/api/users/me")]
    [InlineData("/api/hubs/gps/negotiate")]
    [InlineData("/uploads/facture.pdf")]
    // Frontière de segment : un préfixe qui ressemble ne suffit pas.
    [InlineData("/api/vehiclestopsx")]
    [InlineData("/api/reports/stopsx/1")]
    [InlineData("/api/reports/stops-export")]
    public void Tout_le_reste_est_refuse(string path)
    {
        var refus = ApiTokenAccess.Evaluate("GET", path, Debut, Fin, null);

        refus.Should().NotBeNull();
        refus!.Value.StatusCode.Should().Be(403);
        refus.Value.Message.Should().Be(ApiTokenAccess.RouteMessage);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    public void Un_jeton_ne_modifie_jamais_rien(string method)
    {
        var refus = ApiTokenAccess.Evaluate(method, "/api/vehiclestops", Debut, Fin, null);

        refus.Should().NotBeNull();
        refus!.Value.StatusCode.Should().Be(403);
        refus.Value.Message.Should().Be(ApiTokenAccess.ReadOnlyMessage);
    }

    // ── Période et taille de page ──

    [Theory]
    [InlineData(null, Fin)]
    [InlineData(Debut, null)]
    [InlineData("", "")]
    [InlineData("pas-une-date", Fin)]
    public void Les_deux_dates_sont_obligatoires(string? debut, string? fin)
    {
        var refus = ApiTokenAccess.Evaluate("GET", "/api/reports/stops", debut, fin, null);

        refus.Should().NotBeNull();
        refus!.Value.StatusCode.Should().Be(400);
        refus.Value.Message.Should().Be(ApiTokenAccess.PeriodRequiredMessage);
    }

    [Fact]
    public void Trente_et_un_jours_passent_trente_deux_non()
    {
        ApiTokenAccess.Evaluate("GET", "/api/reports/stops", "2026-10-01", "2026-10-31", null).Should().BeNull();

        var refus = ApiTokenAccess.Evaluate("GET", "/api/reports/stops", "2026-10-01", "2026-11-01", null);
        refus.Should().NotBeNull();
        refus!.Value.StatusCode.Should().Be(400);
        refus.Value.Message.Should().Be(ApiTokenAccess.PeriodTooLongMessage);
    }

    [Fact]
    public void Une_periode_inversee_est_laissee_au_controleur()
    {
        // Le contrôleur la refuse avec son propre message ; le jeton ne doit pas la déguiser en « trop longue ».
        ApiTokenAccess.Evaluate("GET", "/api/reports/stops", "2026-10-28", "2026-01-01", null).Should().BeNull();
    }

    [Fact]
    public void La_taille_de_page_est_bornee()
    {
        ApiTokenAccess.Evaluate("GET", "/api/vehiclestops", Debut, Fin, "1000").Should().BeNull();

        var refus = ApiTokenAccess.Evaluate("GET", "/api/vehiclestops", Debut, Fin, "1001");
        refus.Should().NotBeNull();
        refus!.Value.Message.Should().Be(ApiTokenAccess.PageSizeMessage);

        ApiTokenAccess.Evaluate("GET", "/api/vehiclestops", Debut, Fin, "abc").Should().NotBeNull();
    }

    // ── Le compte qui porte le jeton ──

    // Règle de Karim (01/11/2026, migration 065) : l'administrateur de la société, et
    // seulement si Belive a activé l'accès API pour cette société.
    [Fact]
    public void L_administrateur_d_une_societe_activee_peut_porter_un_jeton() =>
        ApiTokenAccess.AccountRefusal(new User { Status = "active", Role = new Role { IsCompanyAdmin = true } }, true)
            .Should().BeNull();

    [Fact]
    public void Un_utilisateur_qui_n_est_pas_administrateur_ne_porte_pas_de_jeton() =>
        ApiTokenAccess.AccountRefusal(new User { Status = "active", Role = new Role { IsCompanyAdmin = false } }, true)
            .Should().Be(ApiTokenAccess.AdminOnlyMessage);

    [Fact]
    public void Sans_activation_par_Belive_meme_l_administrateur_ne_porte_pas_de_jeton() =>
        ApiTokenAccess.AccountRefusal(new User { Status = "active", Role = new Role { IsCompanyAdmin = true } }, false)
            .Should().Be(ApiTokenAccess.NotEnabledMessage);

    [Fact]
    public void Compte_inactif_chauffeur_ou_systeme_ne_portent_pas_de_jeton()
    {
        ApiTokenAccess.AccountRefusal(new User { Status = "suspended", Role = new Role() }, true)
            .Should().Be(ApiTokenAccess.InactiveMessage);
        ApiTokenAccess.AccountRefusal(new User { Status = "active", AccountType = UserAccountTypes.Driver, Role = new Role() }, true)
            .Should().Be(ApiTokenAccess.DriverMessage);
        ApiTokenAccess.AccountRefusal(new User { Status = "active", Role = new Role { IsSystemRole = true } }, true)
            .Should().Be(ApiTokenAccess.SystemRoleMessage);
    }

    [Fact]
    public void Un_jeton_utilise_ou_expire_n_est_plus_utilisable()
    {
        var now = DateTime.UtcNow;
        new ApiToken { ExpiresAt = now.AddMinutes(5) }.IsUsable(now).Should().BeTrue();
        new ApiToken { ExpiresAt = now.AddMinutes(5), UsedAt = now }.IsUsable(now).Should().BeFalse();
        new ApiToken { ExpiresAt = now.AddMinutes(-1) }.IsUsable(now).Should().BeFalse();
    }

    // ── Génération ──

    private static async Task<TestGisDbContext> SeedAsync()
    {
        var ctx = TestDbContextFactory.Create();
        { var societe1 = TestDataBuilder.CreateSociete(1, null); societe1.ApiAccessEnabled = true; ctx.Societes.Add(societe1); } // accès API activé par Belive (migration 065)
        { var societe2 = TestDataBuilder.CreateSociete(2, null); societe2.ApiAccessEnabled = true; ctx.Societes.Add(societe2); } // accès API activé par Belive (migration 065)
        ctx.Roles.Add(new Role { Id = 1, Name = "Gestionnaire", SocieteId = 1, IsCompanyAdmin = true });
        ctx.Roles.Add(new Role { Id = 2, Name = "Administrateur", SocieteId = 1, IsSystemRole = true });
        ctx.Roles.Add(new Role { Id = 3, Name = "Gestionnaire", SocieteId = 2, IsCompanyAdmin = true });

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
            Make(10, 1, "client@exemple.tn", 1),
            Make(11, 1, "chauffeur@exemple.tn", 1, u => u.AccountType = UserAccountTypes.Driver),
            Make(12, 1, "systeme@exemple.tn", 2),
            Make(13, 1, "inactif@exemple.tn", 1, u => u.Status = "suspended"),
            Make(20, 2, "autre@autre.tn", 3));
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static GenerateApiTokenCommandHandler Handler(TestGisDbContext ctx, int companyId, int userId) =>
        new(ctx, TestDbContextFactory.CreateMockTenantService(companyId, userId).Object,
            new PasswordHasher(), NullLogger<GenerateApiTokenCommandHandler>.Instance);

    [Fact]
    public async Task La_generation_stocke_l_empreinte_jamais_le_jeton()
    {
        await using var ctx = await SeedAsync();

        var created = await Handler(ctx, 1, 10).Handle(
            new GenerateApiTokenCommand("client@exemple.tn", MotDePasse, "203.0.113.7"), CancellationToken.None);

        created.Token.Should().StartWith("cly_");
        created.ExpiresInSeconds.Should().Be(ApiTokenAccess.LifetimeMinutes * 60);
        created.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(ApiTokenAccess.LifetimeMinutes), TimeSpan.FromMinutes(1));

        var row = ctx.ApiTokens.Single();
        row.CompanyId.Should().Be(1);
        row.UserId.Should().Be(10);
        row.UsedAt.Should().BeNull();
        row.CreatedIp.Should().Be("203.0.113.7");
        row.TokenHash.Should().Be(ApiTokenAccess.Hash(created.Token));
        row.TokenHash.Should().NotContain(created.Token);
        row.TokenPrefix.Should().Be(created.Token[..12]);
    }

    [Fact]
    public async Task La_generation_laisse_une_trace_d_audit_sans_le_jeton()
    {
        await using var ctx = await SeedAsync();

        var created = await Handler(ctx, 1, 10).Handle(
            new GenerateApiTokenCommand("client@exemple.tn", MotDePasse), CancellationToken.None);

        var audit = ctx.AuditLogs.Single(a => a.Action == "api_token_generated");
        audit.UserId.Should().Be(10);
        audit.CompanyId.Should().Be(1);
        new[] { audit.Action, audit.EntityType, audit.EntityName, audit.Description, audit.IpAddress }
            .Should().OnlyContain(champ => champ == null || !champ.Contains(created.Token));
        audit.EntityName.Should().Be(created.TokenPrefix);   // le préfixe seul, jamais le jeton entier
    }

    [Fact]
    public async Task Le_mail_est_compare_sans_tenir_compte_de_la_casse_ni_des_espaces()
    {
        await using var ctx = await SeedAsync();

        var created = await Handler(ctx, 1, 10).Handle(
            new GenerateApiTokenCommand("  CLIENT@Exemple.TN ", MotDePasse), CancellationToken.None);

        created.Token.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("client@exemple.tn", "mauvais-mot-de-passe")]
    [InlineData("autre@autre.tn", MotDePasse)]          // le mot de passe est bon, mais l'e-mail n'est pas celui de la session
    [InlineData("client@exemple.tn", "")]
    [InlineData("", MotDePasse)]
    public async Task La_generation_refuse_un_couple_qui_n_est_pas_celui_de_la_session(string email, string password)
    {
        await using var ctx = await SeedAsync();

        var act = () => Handler(ctx, 1, 10).Handle(new GenerateApiTokenCommand(email, password), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        ctx.ApiTokens.Should().BeEmpty();
    }

    [Fact]
    public async Task Le_refus_ne_dit_pas_lequel_des_deux_est_faux()
    {
        await using var ctx = await SeedAsync();

        var mauvaisMdp = await FluentActions.Awaiting(() => Handler(ctx, 1, 10).Handle(
            new GenerateApiTokenCommand("client@exemple.tn", "faux"), CancellationToken.None)).Should().ThrowAsync<DomainException>();
        var mauvaisMail = await FluentActions.Awaiting(() => Handler(ctx, 1, 10).Handle(
            new GenerateApiTokenCommand("autre@autre.tn", MotDePasse), CancellationToken.None)).Should().ThrowAsync<DomainException>();

        mauvaisMdp.Which.Message.Should().Be(GenerateApiTokenCommandHandler.WrongCredentialsMessage);
        mauvaisMail.Which.Message.Should().Be(GenerateApiTokenCommandHandler.WrongCredentialsMessage);
    }

    [Theory]
    [InlineData(11, "chauffeur@exemple.tn")]
    [InlineData(12, "systeme@exemple.tn")]
    [InlineData(13, "inactif@exemple.tn")]
    public async Task La_generation_refuse_un_compte_qui_ne_peut_pas_porter_de_jeton(int userId, string email)
    {
        await using var ctx = await SeedAsync();

        var act = () => Handler(ctx, 1, userId).Handle(new GenerateApiTokenCommand(email, MotDePasse), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        ctx.ApiTokens.Should().BeEmpty();
    }

    [Fact]
    public async Task Le_nombre_de_jetons_en_attente_est_plafonne_puis_se_libere()
    {
        await using var ctx = await SeedAsync();
        var handler = Handler(ctx, 1, 10);
        var cmd = new GenerateApiTokenCommand("client@exemple.tn", MotDePasse);

        for (var i = 0; i < ApiTokenAccess.MaxPendingPerUser; i++)
            await handler.Handle(cmd, CancellationToken.None);

        var act = () => handler.Handle(cmd, CancellationToken.None);
        (await act.Should().ThrowAsync<DomainException>()).Which.Message
            .Should().Be(GenerateApiTokenCommandHandler.TooManyPendingMessage);

        // Un jeton consommé libère une place.
        ctx.ApiTokens.OrderBy(t => t.Id).First().UsedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync();
        await handler.Handle(cmd, CancellationToken.None);
    }

    // ── Liste ──

    [Fact]
    public async Task La_liste_est_celle_de_l_appelant_seul_et_sans_secret()
    {
        await using var ctx = await SeedAsync();

        var created = await Handler(ctx, 1, 10).Handle(
            new GenerateApiTokenCommand("client@exemple.tn", MotDePasse), CancellationToken.None);
        await Handler(ctx, 2, 20).Handle(
            new GenerateApiTokenCommand("autre@autre.tn", MotDePasse), CancellationToken.None);
        // Un administrateur de société ne voit pas non plus les jetons d'un collègue.
        ctx.ApiTokens.Add(new ApiToken
        {
            CompanyId = 1, UserId = 99, TokenPrefix = "cly_collegue1", TokenHash = new string('a', 64),
            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        });
        await ctx.SaveChangesAsync();

        var tenant = TestDbContextFactory.CreateMockTenantService(1, 10).Object;
        var liste = await new GetMyApiTokensQueryHandler(ctx, tenant).Handle(new GetMyApiTokensQuery(), CancellationToken.None);

        liste.Should().ContainSingle();
        liste[0].Status.Should().Be("pending");
        liste[0].TokenPrefix.Should().Be(created.Token[..12]);
        System.Text.Json.JsonSerializer.Serialize(liste).Should().NotContain(created.Token);
    }

    [Fact]
    public async Task La_liste_distingue_en_attente_utilise_et_expire()
    {
        await using var ctx = await SeedAsync();
        var now = DateTime.UtcNow;
        ctx.ApiTokens.AddRange(
            new ApiToken { CompanyId = 1, UserId = 10, TokenPrefix = "cly_attente1", TokenHash = new string('1', 64), ExpiresAt = now.AddMinutes(5), CreatedAt = now.AddMinutes(-1) },
            new ApiToken { CompanyId = 1, UserId = 10, TokenPrefix = "cly_utilise01", TokenHash = new string('2', 64), ExpiresAt = now.AddMinutes(5), UsedAt = now, CreatedAt = now.AddMinutes(-2) },
            new ApiToken { CompanyId = 1, UserId = 10, TokenPrefix = "cly_expire001", TokenHash = new string('3', 64), ExpiresAt = now.AddMinutes(-1), CreatedAt = now.AddMinutes(-20) });
        await ctx.SaveChangesAsync();

        var tenant = TestDbContextFactory.CreateMockTenantService(1, 10).Object;
        var liste = await new GetMyApiTokensQueryHandler(ctx, tenant).Handle(new GetMyApiTokensQuery(), CancellationToken.None);

        liste.Select(t => t.Status).Should().Equal("pending", "used", "expired");
    }

    // ── Règle de Karim (01/11/2026, migration 065) : administrateur, société activée ──

    [Fact]
    public async Task Un_utilisateur_qui_n_est_pas_administrateur_ne_genere_pas_de_jeton()
    {
        await using var ctx = await SeedAsync();
        ctx.Roles.Add(new Role { Id = 4, Name = "Opérateur", SocieteId = 1, IsCompanyAdmin = false });
        var employe = TestDataBuilder.CreateUser(id: 14, companyId: 1, email: "employe@exemple.tn");
        employe.RoleId = 4;
        employe.PasswordHash = new PasswordHasher().HashPassword(MotDePasse);
        ctx.Users.Add(employe);
        await ctx.SaveChangesAsync();

        var act = () => Handler(ctx, 1, 14).Handle(
            new GenerateApiTokenCommand("employe@exemple.tn", MotDePasse), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).WithMessage(ApiTokenAccess.AdminOnlyMessage);
        ctx.ApiTokens.Should().BeEmpty();
    }

    [Fact]
    public async Task Sans_activation_par_Belive_l_administrateur_ne_genere_pas_de_jeton()
    {
        await using var ctx = await SeedAsync();
        (await ctx.Societes.FindAsync(2))!.ApiAccessEnabled = false;
        await ctx.SaveChangesAsync();

        var act = () => Handler(ctx, 2, 20).Handle(
            new GenerateApiTokenCommand("autre@autre.tn", MotDePasse), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).WithMessage(ApiTokenAccess.NotEnabledMessage);
        ctx.ApiTokens.Should().BeEmpty();
    }

    [Fact]
    public async Task Le_profil_sait_si_l_acces_API_est_ouvert_a_ce_compte()
    {
        await using var ctx = await SeedAsync();
        (await ctx.Societes.FindAsync(2))!.ApiAccessEnabled = false;
        await ctx.SaveChangesAsync();
        Task<ApiAccessDto> Acces(int companyId, int userId) =>
            new GetMyApiAccessQueryHandler(ctx, TestDbContextFactory.CreateMockTenantService(companyId, userId).Object)
                .Handle(new GetMyApiAccessQuery(), CancellationToken.None);

        (await Acces(1, 10)).Should().Be(new ApiAccessDto(true, null));
        (await Acces(2, 20)).Should().Be(new ApiAccessDto(false, ApiTokenAccess.NotEnabledMessage));
        (await Acces(1, 12)).Should().Be(new ApiAccessDto(false, ApiTokenAccess.SystemRoleMessage));
    }
}
