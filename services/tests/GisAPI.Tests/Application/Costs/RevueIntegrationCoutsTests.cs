using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Application.Services;
using GisAPI.Controllers;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Services;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Costs;

/// <summary>
/// Revue de l'intégration du 18/09/2026 (apport de Karim) — les coûts :
/// <list type="bullet">
///   <item>une dépense à 0 écrite par « marquer fait » (entretien gratuit) ne se modifiait
///     plus : PUT refusait tout montant ≤ 0 ;</item>
///   <item>modifiée en place, une dépense divergeait de son journal d'entretien ;</item>
///   <item>un crédit ancien saisi en négatif n'était pas déduit pareil partout ;</item>
///   <item>le rapport IA flotte ne lisait que vehicle_costs, sans portée véhicule ;</item>
///   <item>la synthèse /api/dashboard/cost-summary comptait l'entretien deux fois.</item>
/// </list>
/// </summary>
public class RevueIntegrationCoutsTests
{
    private const int CompanyId = 7;
    private const int AdminUserId = 1;
    private const int EmployeId = 11;
    private static readonly DateTime Jour = DateTime.UtcNow.AddDays(-2);

    private static ClaimsPrincipal Principal(int userId, string role) => new(new ClaimsIdentity(new[]
    {
        new Claim("companyId", CompanyId.ToString()),
        new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
        new Claim(ClaimTypes.Role, role)
    }, "test"));

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value);

    private static CostsController Couts(IGisDbContext ctx)
    {
        var tenant = new Mock<ICurrentTenantService>();
        tenant.Setup(x => x.CompanyId).Returns(CompanyId);
        tenant.Setup(x => x.UserId).Returns(AdminUserId);
        tenant.Setup(x => x.UserRoles).Returns(new[] { "company_admin" });
        tenant.Setup(x => x.IsAuthenticated).Returns(true);
        var publisher = new Mock<IPublisher>();
        publisher.Setup(p => p.Publish(It.IsAny<AdminActionNotificationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new CostsController(ctx, tenant.Object, publisher.Object,
            Mock.Of<IInvoiceExtractionService>(), Mock.Of<IWebHostEnvironment>(), Mock.Of<ILogger<CostsController>>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = Principal(AdminUserId, "company_admin") }
            }
        };
    }

    private static async Task<TestGisDbContext> ParcAsync()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId, Status = "available" });
        ctx.Vehicles.Add(new Vehicle { Id = 2, Name = "Service 02", Plate = "GA-215-RK", CompanyId = CompanyId, Status = "available" });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    private static VehicleCost Copie(VehicleCost c) => new()
    {
        Id = c.Id, VehicleId = c.VehicleId, CompanyId = c.CompanyId, Type = c.Type, Description = c.Description,
        Amount = c.Amount, Date = c.Date, Mileage = c.Mileage
    };

    // ── PUT /api/costs/{id} ─────────────────────────────────────────────────────

    [Fact]
    public async Task Une_depense_d_entretien_gratuit_a_zero_reste_modifiable()
    {
        using var ctx = await ParcAsync();
        ctx.VehicleCosts.Add(new VehicleCost
        {
            Id = 23, VehicleId = 1, CompanyId = CompanyId, Type = "maintenance",
            Description = "Entretien: Plaquette de freins (gratuit — Achat neuf)", Amount = 0m, Date = Jour
        });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var corps = Copie(await ctx.VehicleCosts.AsNoTracking().SingleAsync());
        corps.Description = "Plaquettes avant (gratuit — Achat neuf)";
        (await Couts(ctx).UpdateCost(23, corps)).Should().BeOfType<NoContentResult>();

        var negatif = Copie(corps);
        negatif.Amount = -5m;
        (await Couts(ctx).UpdateCost(23, negatif)).Should().BeOfType<BadRequestObjectResult>();

        ctx.ChangeTracker.Clear();
        var ligne = await ctx.VehicleCosts.AsNoTracking().SingleAsync();
        (ligne.Description, ligne.Amount).Should().Be(("Plaquettes avant (gratuit — Achat neuf)", 0m));
    }

    [Fact]
    public async Task Passer_une_depense_a_zero_reste_refuse()
    {
        using var ctx = await ParcAsync();
        ctx.VehicleCosts.Add(new VehicleCost { Id = 1, VehicleId = 1, CompanyId = CompanyId, Type = "insurance", Amount = 600m, Date = Jour });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var corps = Copie(await ctx.VehicleCosts.AsNoTracking().SingleAsync());
        corps.Amount = 0m;

        (await Couts(ctx).UpdateCost(1, corps)).Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Corriger_la_depense_d_un_entretien_met_a_jour_son_journal()
    {
        using var ctx = await ParcAsync();
        ctx.VehicleCosts.Add(new VehicleCost
        {
            Id = 5, VehicleId = 1, CompanyId = CompanyId, Type = "maintenance",
            Description = "Entretien: Vidange", Amount = 350m, Date = Jour, Mileage = 45_000
        });
        ctx.MaintenanceLogs.Add(new MaintenanceLog
        {
            Id = 9, CompanyId = CompanyId, VehicleId = 1, TemplateId = 1, CostId = 5,
            DoneDate = Jour, DoneKm = 45_000, ActualCost = 350m
        });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var corps = Copie(await ctx.VehicleCosts.AsNoTracking().SingleAsync());
        corps.Amount = 380m;
        corps.Date = Jour.AddDays(-1);
        corps.Mileage = 45_100;
        (await Couts(ctx).UpdateCost(5, corps)).Should().BeOfType<NoContentResult>();

        ctx.ChangeTracker.Clear();
        var journal = await ctx.MaintenanceLogs.AsNoTracking().SingleAsync();
        (journal.ActualCost, journal.DoneDate, journal.DoneKm).Should().Be((380m, Jour.AddDays(-1), 45_100));
    }

    // ── Crédits : un signe, ligne à ligne, partout ──────────────────────────────

    [Theory]
    [InlineData("credit_note", 150, -120, -270)]
    [InlineData("insurance_refund", 100, 0, -100)]
    [InlineData("fuel", 50, -10, 40)]
    public void SignedFromParts_deduit_chaque_credit_en_valeur_absolue(string type, int positif, int negatif, int attendu)
        => VehicleCostCategory.SignedFromParts(type, positif, negatif).Should().Be(attendu);

    [Fact]
    public async Task Le_tableau_de_bord_et_le_resume_des_couts_deduisent_chaque_avoir_en_valeur_absolue()
    {
        using var ctx = await ParcAsync();
        ctx.VehicleCosts.AddRange(
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "insurance", Amount = 1_000m, Date = Jour },
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "credit_note", Amount = 150m, Date = Jour },
            // Avoir ancien saisi en négatif, avant le refus des montants négatifs (DEF-050).
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "credit_note", Amount = -120m, Date = Jour });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var (_, _, reparations, autres) = await DashboardService.PeriodCostsAsync(
            ctx, CompanyId, null, Jour.AddDays(-1), DateTime.UtcNow, CancellationToken.None);
        // Règle du 18/09/2026 : dans un tableau de bord (quatre cases, pas de ligne de
        // crédit) l'avoir se déduit des RÉPARATIONS et les autres postes restent bruts.
        // Ce qui est vérifié ici reste le signe : chaque avoir compte en valeur absolue,
        // y compris celui saisi en négatif avant DEF-050 — en signant la somme brute du
        // type (150 − 120 = 30) l'avoir n'aurait pesé que 30 au lieu de 270.
        autres.Should().Be(1_000m, "les postes ne portent plus le crédit");
        reparations.Should().Be(-270m, "−150 − 120, et non −30");
        (autres + reparations).Should().Be(730m, "le total, lui, ne change pas");

        var ok = (await Couts(ctx).GetCostSummary(Jour.AddDays(-1), DateTime.UtcNow)).Should().BeOfType<OkObjectResult>().Subject;
        Json(ok.Value).GetProperty("TotalAmount").GetDecimal().Should().Be(730m);
    }

    // ── Rapport IA flotte : mêmes sources et même portée que le tableau de bord ─

    private static AiChatController Assistant(TestGisDbContext ctx, ClaimsPrincipal user, Action<string>? prompt = null)
    {
        var sante = new Mock<IVehicleHealthScoreService>();
        sante.Setup(s => s.CalculateAllScoresAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<VehicleHealthResult>
             {
                 new() { VehicleId = 1, Score = 80, Level = "good" },
                 new() { VehicleId = 2, Score = 40, Level = "poor" },
             });
        var llm = new Mock<ILlmService>();
        llm.Setup(l => l.ChatAsync(It.IsAny<string>(), It.IsAny<List<LlmMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
           .Callback<string, List<LlmMessage>, int, CancellationToken>((system, _, _, _) => prompt?.Invoke(system))
           .ReturnsAsync(new LlmResponse("analyse", 1200));

        return new AiChatController(ctx, llm.Object, sante.Object, NullLogger<AiChatController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };
    }

    private static async Task<TestGisDbContext> ParcAvecPleinsEtReparationsAsync()
    {
        var ctx = await ParcAsync();
        ctx.FuelTypes.Add(new FuelType { Id = 1, Code = "diesel", Name = "Gazole" });
        ctx.FuelEntries.AddRange(
            new FuelEntry { CompanyId = CompanyId, VehicleId = 1, FuelTypeId = 1, Volume = 40, TotalAmount = 300m, InvoiceDate = Jour },
            new FuelEntry { CompanyId = CompanyId, VehicleId = 2, FuelTypeId = 1, Volume = 40, TotalAmount = 500m, InvoiceDate = Jour });
        ctx.Repairs.AddRange(
            new Repair { SocieteId = CompanyId, VehicleId = 1, Reference = "REP-1", Description = "Plaquettes", RepairDate = Jour, TotalCost = 145m, Status = "completed" },
            new Repair { SocieteId = CompanyId, VehicleId = 1, Reference = "REP-2", Description = "Annulée", RepairDate = Jour, TotalCost = 999m, Status = " Cancelled " });
        ctx.VehicleCosts.Add(new VehicleCost { VehicleId = 2, CompanyId = CompanyId, Type = "insurance", Amount = 200m, Date = Jour });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    [Fact]
    public async Task Le_rapport_IA_compte_les_pleins_saisis_et_les_reparations_comme_le_tableau_de_bord()
    {
        using var ctx = await ParcAvecPleinsEtReparationsAsync();

        var ok = (await Assistant(ctx, Principal(AdminUserId, "company_admin"))
                .GenerateFleetReport(new FleetReportRequest("month", null)))
            .Should().BeOfType<OkObjectResult>().Subject;

        var rapport = Json(ok.Value);
        rapport.GetProperty("fleetSummary").GetProperty("totalCosts").GetDecimal().Should().Be(1_145m, "800 de pleins + 145 de réparation + 200 d'assurance");
        var r = rapport.GetProperty("charts").GetProperty("costBreakdown");
        (r.GetProperty("fuel").GetDecimal(), r.GetProperty("repairs").GetDecimal(), r.GetProperty("other").GetDecimal())
            .Should().Be((800m, 145m, 200m));
        var parVehicule = rapport.GetProperty("vehicleDetails").EnumerateArray()
            .ToDictionary(v => v.GetProperty("id").GetInt32(), v => v.GetProperty("totalCosts").GetDecimal());
        parVehicule.Should().BeEquivalentTo(new Dictionary<int, decimal> { [1] = 445m, [2] = 700m });
    }

    [Fact]
    public async Task Le_rapport_IA_d_un_employe_restreint_ne_porte_que_sur_ses_vehicules()
    {
        using var ctx = await ParcAvecPleinsEtReparationsAsync();
        ctx.UserVehicles.Add(new UserVehicle { UserId = EmployeId, VehicleId = 1 });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        string prompt = "";

        var ok = (await Assistant(ctx, Principal(EmployeId, "user"), p => prompt = p)
                .GenerateFleetReport(new FleetReportRequest("month", null)))
            .Should().BeOfType<OkObjectResult>().Subject;

        var rapport = Json(ok.Value);
        rapport.GetProperty("fleetSummary").GetProperty("totalVehicles").GetInt32().Should().Be(1);
        rapport.GetProperty("fleetSummary").GetProperty("totalCosts").GetDecimal().Should().Be(445m);
        rapport.GetProperty("vehicleDetails").EnumerateArray().Select(v => v.GetProperty("id").GetInt32())
            .Should().Equal(1);
        prompt.Should().NotContain("GA-215-RK");
    }

    // ── /api/dashboard/cost-summary : un entretien compté une fois ──────────────

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
    public async Task La_synthese_des_couts_du_tableau_de_bord_ne_compte_pas_l_entretien_deux_fois()
    {
        using var ctx = new ContexteEnMemoire(new DbContextOptionsBuilder<GisDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), b => b.EnableNullChecks(false))
            .Options);
        var date = new DateTime(DateTime.UtcNow.Year, 1, 2, 10, 0, 0, DateTimeKind.Utc);
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId, Status = "available" });
        // « Marquer fait » : UNE dépense et son journal, reliés par CostId.
        ctx.VehicleCosts.Add(new VehicleCost { Id = 5, VehicleId = 1, CompanyId = CompanyId, Type = "maintenance", Amount = 350m, Date = date });
        ctx.MaintenanceLogs.Add(new MaintenanceLog { Id = 9, CompanyId = CompanyId, VehicleId = 1, TemplateId = 1, CostId = 5, DoneDate = date, DoneKm = 45_000, ActualCost = 350m });
        // Une réparation saisie en dépense « repair » : poste Réparations, pas Autres.
        ctx.VehicleCosts.Add(new VehicleCost { Id = 6, VehicleId = 1, CompanyId = CompanyId, Type = "repair", Amount = 200m, Date = date });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var tableau = new DashboardController(ctx, Mock.Of<IMediator>(), new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<IVehicleHealthScoreService>(), null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(AdminUserId, "company_admin") } }
        };

        var ok = (await tableau.GetCostSummary("year")).Should().BeOfType<OkObjectResult>().Subject;

        var synthese = Json(ok.Value);
        synthese.GetProperty("MaintenanceCost").GetDecimal().Should().Be(350m, "journal et dépense décrivent le même entretien");
        synthese.GetProperty("RepairCost").GetDecimal().Should().Be(200m);
        synthese.GetProperty("OtherCost").GetDecimal().Should().Be(0m);
        synthese.GetProperty("TotalCost").GetDecimal().Should().Be(550m);
    }
}
