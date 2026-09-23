using System.Data;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Repairs;
using GisAPI.Controllers;
using GisAPI.Domain.Common;
using GisAPI.Domain.Entities;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Services;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Repairs;

/// <summary>
/// Liste M9 de la recette GPA (17/09/2026) : une réparation au statut « cancelled » n'est
/// pas un coût. Le coût d'exploitation, le rapport mensuel, l'écran Réparations et
/// /api/repairs/stats l'excluaient déjà ; quatre lecteurs la comptaient encore — synthèse
/// des coûts du tableau de bord GPS, comparaison et rapport de flotte de l'assistant IA,
/// fréquence des réparations du score de santé. Pour 295 de réparations réelles et 1 380
/// d'interventions annulées, ils annonçaient 1 675.
/// </summary>
public class ReparationsAnnuleesHorsCoutsTests
{
    private const int CompanyId = 7;
    private const int AdminUserId = 1;

    /// <summary>
    /// Une terminée (245) et une en attente (50) comptent ; trois annulées (80, 300, 1 000)
    /// aux casses et espaces des données anciennes ne comptent pas.
    /// </summary>
    private static Repair[] Reparations(DateTime date, int vehicleId = 1) =>
    [
        new() { SocieteId = CompanyId, VehicleId = vehicleId, Reference = $"REP-{vehicleId}-1", Description = "Plaquettes avant", RepairDate = date, LaborCost = 45m, PartsCost = 200m, TotalCost = 245m, Status = "completed" },
        new() { SocieteId = CompanyId, VehicleId = vehicleId, Reference = $"REP-{vehicleId}-2", Description = "Courroie", RepairDate = date.AddHours(-1), LaborCost = 50m, TotalCost = 50m, Status = "pending" },
        new() { SocieteId = CompanyId, VehicleId = vehicleId, Reference = $"REP-{vehicleId}-3", Description = "Devis refusé", RepairDate = date.AddHours(-2), LaborCost = 80m, TotalCost = 80m, Status = "cancelled" },
        new() { SocieteId = CompanyId, VehicleId = vehicleId, Reference = $"REP-{vehicleId}-4", Description = "Doublon de saisie", RepairDate = date.AddHours(-3), PartsCost = 300m, TotalCost = 300m, Status = "Cancelled" },
        new() { SocieteId = CompanyId, VehicleId = vehicleId, Reference = $"REP-{vehicleId}-5", Description = "Boîte annulée", RepairDate = date.AddHours(-4), PartsCost = 1_000m, TotalCost = 1_000m, Status = " CANCELLED " },
    ];

    private static ClaimsPrincipal Admin() => new(new ClaimsIdentity(new[]
    {
        new Claim("companyId", CompanyId.ToString()),
        new Claim(ClaimTypes.NameIdentifier, AdminUserId.ToString()),
        new Claim(ClaimTypes.Role, "company_admin")
    }, "test"));

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value);

    // ─────────────────────────────── GET /api/dashboard/cost-summary ───────────────────────────────

    /// <summary>GisDbContext en mémoire, sans les colonnes propres à PostgreSQL (même montage que CreditsDeduitsPartoutTests).</summary>
    private sealed class ContexteEnMemoire : GisDbContext
    {
        public ContexteEnMemoire(DbContextOptions<GisDbContext> options)
            : base(options, TestDbContextFactory.CreateMockTenantService(CompanyId).Object) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            foreach (var entity in modelBuilder.Model.GetEntityTypes().ToList())
                foreach (var property in entity.GetDeclaredProperties().ToList())
                    if (!Simple(property.ClrType) && !property.IsKey() && !property.IsForeignKey())
                        modelBuilder.Entity(entity.ClrType).Ignore(property.Name);
        }

        private static bool Simple(Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime)
                || t == typeof(DateTimeOffset) || t == typeof(TimeSpan) || t == typeof(Guid) || t == typeof(byte[])
                || t == typeof(DateOnly) || t == typeof(TimeOnly);
        }
    }

    [Fact]
    public async Task La_synthese_des_couts_du_tableau_de_bord_exclut_les_reparations_annulees()
    {
        using var ctx = new ContexteEnMemoire(new DbContextOptionsBuilder<GisDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), b => b.EnableNullChecks(false))
            .Options);
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId, Status = "available" });
        ctx.Repairs.AddRange(Reparations(new DateTime(DateTime.UtcNow.Year, 1, 2, 10, 0, 0, DateTimeKind.Utc)));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var ok = (await Tableau(ctx).GetCostSummary("year")).Should().BeOfType<OkObjectResult>().Subject;

        var synthese = Json(ok.Value);
        synthese.GetProperty("RepairCost").GetDecimal().Should().Be(295m, "245 terminée + 50 en attente, les 1 380 annulés exclus");
        synthese.GetProperty("TotalCost").GetDecimal().Should().Be(295m);
    }

    private static DashboardController Tableau(GisDbContext ctx) =>
        new(ctx, Mock.Of<IMediator>(), new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<IVehicleHealthScoreService>(), null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Admin() } }
        };

    /// <summary>Pas de serveur PostgreSQL en test : l'ouverture de connexion est neutralisée.</summary>
    private sealed class SansConnexion : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            => InterceptionResult.Suppress();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(InterceptionResult.Suppress());
    }

    /// <summary>
    /// Note le SQL de chaque requête sans l'exécuter. Résultat simulé : une ligne à 0 pour une
    /// somme simple, aucune ligne pour un regroupement (VehicleCostCategory.SignedTotalAsync).
    /// </summary>
    private sealed class SqlNote : DbCommandInterceptor
    {
        public List<string> Commandes { get; } = new();

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
            => InterceptionResult<DbDataReader>.SuppressWithResult(Noter(command));

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(Noter(command)));

        private DbDataReader Noter(DbCommand command)
        {
            Commandes.Add(command.CommandText);
            var table = new DataTable();
            if (!command.CommandText.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase))
            {
                table.Columns.Add("somme", typeof(decimal));
                table.Rows.Add(0m);
            }
            return table.CreateDataReader();
        }
    }

    [Fact]
    public async Task La_synthese_des_couts_ecarte_les_annulees_dans_le_SQL_PostgreSQL()
    {
        // Le test en mémoire prouve l'exclusion mais n'exerce aucune traduction SQL : ici la
        // requête RÉELLE du contrôleur passe par le fournisseur de production. Un filtre
        // réécrit sous une forme non traduisible lèverait, un filtre retiré disparaîtrait du SQL.
        var sql = new SqlNote();
        await using var ctx = new GisDbContext(
            new DbContextOptionsBuilder<GisDbContext>()
                .UseNpgsql("Host=localhost;Database=traduction_seulement")
                .AddInterceptors(new SansConnexion(), sql)
                .Options,
            TestDbContextFactory.CreateMockTenantService(CompanyId).Object);

        (await Tableau(ctx).GetCostSummary("year")).Should().BeOfType<OkObjectResult>();

        var reparations = sql.Commandes.Should().ContainSingle(c => c.Contains("FROM repairs")).Subject;
        reparations.Should().Contain("sum(", "la somme reste calculée en base")
            .And.Contain("lower(btrim(").And.Contain("'cancelled'");
    }

    // ─────────────────────────────── assistant IA ───────────────────────────────

    private static async Task<TestGisDbContext> ParcAsync(DateTime date)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId, Status = "available" },
            new Vehicle { Id = 2, Name = "Service 02", Plate = "GA-215-RK", CompanyId = CompanyId, Status = "available" });
        ctx.Repairs.AddRange(Reparations(date));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    private static AiChatController Assistant(TestGisDbContext ctx, Mock<ILlmService> llm)
    {
        var sante = new Mock<IVehicleHealthScoreService>();
        sante.Setup(s => s.CalculateAllScoresAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<VehicleHealthResult>());
        sante.Setup(s => s.CalculateScoreAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new VehicleHealthResult { VehicleId = 1, Score = 80, Level = "good" });

        // Crédit IA de la société (22/09/2026) : sans société connue, l'appel serait refusé.
        AiCreditTestData.EnsureSocieteAvecCredit(ctx, CompanyId);
        return new AiChatController(ctx, llm.Object, sante.Object, NullLogger<AiChatController>.Instance,
            TestDbContextFactory.CreateMockTenantService(CompanyId).Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Admin() } }
        };
    }

    /// <summary>LLM simulé qui mémorise le prompt système, quelle que soit la surcharge appelée.</summary>
    private static Mock<ILlmService> Llm(Action<string> prompt)
    {
        var llm = new Mock<ILlmService>();
        llm.Setup(l => l.ChatAsync(It.IsAny<string>(), It.IsAny<List<LlmMessage>>(), It.IsAny<CancellationToken>()))
           .Callback<string, List<LlmMessage>, CancellationToken>((system, _, _) => prompt(system))
           .ReturnsAsync(new LlmResponse("analyse", 900));
        llm.Setup(l => l.ChatAsync(It.IsAny<string>(), It.IsAny<List<LlmMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
           .Callback<string, List<LlmMessage>, int, CancellationToken>((system, _, _, _) => prompt(system))
           .ReturnsAsync(new LlmResponse("analyse", 1200));
        return llm;
    }

    [Fact]
    public async Task Le_rapport_de_flotte_IA_ne_compte_pas_les_reparations_annulees()
    {
        using var ctx = await ParcAsync(DateTime.UtcNow.AddDays(-2));
        string prompt = "";

        var ok = (await Assistant(ctx, Llm(p => prompt = p)).GenerateFleetReport(new FleetReportRequest("month", null)))
            .Should().BeOfType<OkObjectResult>().Subject;

        var vehicule = Json(ok.Value).GetProperty("vehicleDetails").EnumerateArray()
            .Single(v => v.GetProperty("id").GetInt32() == 1);
        vehicule.GetProperty("repairCount").GetInt32().Should().Be(2, "terminée et en attente comptent, les trois annulées non");
        vehicule.GetProperty("repairCost").GetDecimal().Should().Be(295m);
        vehicule.GetProperty("topRepairs").EnumerateArray().Select(r => r.GetProperty("description").GetString())
            .Should().BeEquivalentTo("Plaquettes avant", "Courroie");

        prompt.Should().Contain("| Réparations: 2")
            .And.Contain($"Réparations: 2 ({295m:N0} {AppCurrency.Default})")
            .And.NotContain("Devis refusé").And.NotContain("Boîte annulée");
    }

    [Fact]
    public async Task Le_diagnostic_IA_garde_la_reparation_annulee_mais_la_signale_hors_couts()
    {
        using var ctx = await ParcAsync(DateTime.UtcNow.AddDays(-3));
        string prompt = "";

        (await Assistant(ctx, Llm(p => prompt = p)).GenerateReport(1)).Should().BeOfType<OkObjectResult>();

        var lignes = prompt.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        lignes.Single(l => l.Contains("Plaquettes avant")).Should().EndWith("| Statut: completed");
        lignes.Single(l => l.Contains("Courroie")).Should().EndWith("| Statut: pending");
        foreach (var annulee in new[] { "Devis refusé", "Doublon de saisie", "Boîte annulée" })
            lignes.Single(l => l.Contains(annulee)).Should().EndWith("| Statut: annulée, non comptée dans les coûts");
    }

    [Fact]
    public async Task La_comparaison_IA_n_additionne_pas_les_reparations_annulees()
    {
        using var ctx = await ParcAsync(DateTime.UtcNow.AddDays(-3));
        string prompt = "";

        (await Assistant(ctx, Llm(p => prompt = p)).CompareVehicles(new CompareVehiclesRequest(new List<int> { 1, 2 }, null)))
            .Should().BeOfType<OkObjectResult>();

        prompt.Should().Contain($"Réparations récentes: 2 | Coût total: {295m:N0} {AppCurrency.Default}")
            .And.NotContain($"Coût total: {1_675m:N0}");
    }

    // ─────────────────────────────── score de santé ───────────────────────────────

    private static (TestGisDbContext Context, VehicleHealthScoreService Service) Sante()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId, Mileage = 60_000 });
        ctx.Repairs.AddRange(Reparations(DateTime.UtcNow.AddDays(-10)));
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();

        var provider = new ServiceCollection()
            .AddSingleton<IGisDbContext>(ctx)
            .BuildServiceProvider();
        return (ctx, new VehicleHealthScoreService(provider));
    }

    private static void DeuxReparationsComptees(VehicleHealthResult score)
    {
        // Cinq lignes brutes donnaient 4/20 et « 5 réparations en 6 mois (fréquence élevée) ».
        var reparations = score.Factors.Single(f => f.Name == "Réparations");
        reparations.Detail.Should().Be("2 en 6 mois");
        reparations.Score.Should().Be(15);
        score.Warnings.Should().NotContain(w => w.Contains("réparations en 6 mois"));
    }

    [Fact]
    public async Task Le_score_de_sante_de_la_flotte_ne_compte_pas_les_reparations_annulees()
    {
        var (ctx, service) = Sante();
        using var _ = ctx;

        var scores = await service.CalculateAllScoresAsync(CompanyId);

        DeuxReparationsComptees(scores.Single(s => s.VehicleId == 1));
    }

    [Fact]
    public async Task Le_score_de_sante_d_un_vehicule_ne_compte_pas_les_reparations_annulees()
    {
        var (ctx, service) = Sante();
        using var _ = ctx;

        DeuxReparationsComptees(await service.CalculateScoreAsync(1, CompanyId));
    }
}
