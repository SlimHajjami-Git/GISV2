using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Application.Services;
using GisAPI.Controllers;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GisAPI.Tests.Services;

/// <summary>
/// Portée véhicules de l'écran Dépenses — testée sur le VRAI
/// <see cref="CostsController"/>, instancié ici avec le contexte SQLite en
/// mémoire (le contrôleur dépend de <c>IGisDbContext</c>, que
/// <see cref="TestGisDbContext"/> implémente).
///
/// Deux constats du 09/09/2026 :
///
/// 1. LECTURE — l'écran ne filtrait que sur la société : un employé restreint à
///    un seul véhicule y lisait les 36 dépenses de tout le parc (donc des
///    matricules qui ne lui sont pas affectés) alors que ses pleins, ses
///    échéances et les rapports n'en montraient que 8.
///
/// 2. ÉCRITURE — le correctif de lecture laissait les mutations charger la
///    ligne sur le seul couple (Id, CompanyId) : le même employé recevait bien
///    un 404 sur GET /api/costs/812 (dépense d'un véhicule hors portée) mais
///    son DELETE /api/costs/812 renvoyait 204 et SUPPRIMAIT la ligne — un IDOR.
///
/// Ces tests appellent les actions du contrôleur (aucune requête recopiée) :
/// retirer le filtre de portée d'une seule d'entre elles les fait échouer.
/// </summary>
public class CostsScreenScopeTests
{
    private const int CompanyId = 1;
    private const int OtherCompanyId = 2;
    private const int AdminUserId = 1;
    private const int RestrictedUserId = 51;
    private const int OrphanUserId = 52;

    private static readonly DateTime Day = new(2026, 6, 10, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodStart = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc);

    private static ICurrentTenantService Tenant(int userId, params string[] roles)
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.CompanyId).Returns(CompanyId);
        m.Setup(x => x.UserId).Returns(userId);
        m.Setup(x => x.UserRoles).Returns(roles);
        m.Setup(x => x.IsAuthenticated).Returns(true);
        return m.Object;
    }

    private static ICurrentTenantService Admin() => Tenant(AdminUserId, "company_admin");
    private static ICurrentTenantService Restricted() => Tenant(RestrictedUserId, "user");
    private static ICurrentTenantService Orphan() => Tenant(OrphanUserId, "user");

    /// <summary>Le vrai contrôleur, avec les claims JWT qu'il lit lui-même
    /// (companyId + identifiant utilisateur) alignés sur le tenant.</summary>
    private static CostsController Controller(TestGisDbContext ctx, ICurrentTenantService tenant)
    {
        var publisher = new Mock<IPublisher>();
        publisher
            .Setup(p => p.Publish(It.IsAny<AdminActionNotificationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var controller = new CostsController(
            ctx,
            tenant,
            publisher.Object,
            new Mock<IInvoiceExtractionService>().Object,
            new Mock<IWebHostEnvironment>().Object,
            new Mock<ILogger<CostsController>>().Object);

        var identity = new ClaimsIdentity(new[]
        {
            new Claim("companyId", (tenant.CompanyId ?? 0).ToString()),
            new Claim(ClaimTypes.NameIdentifier, (tenant.UserId ?? 0).ToString())
        }, "test");

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };

        return controller;
    }

    private static async Task SeedAsync(TestGisDbContext ctx)
    {
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "Opel", Plate = "111 TU 1", CompanyId = CompanyId },
            new Vehicle { Id = 2, Name = "Camion", Plate = "222 TU 2", CompanyId = CompanyId },
            new Vehicle { Id = 9, Name = "Etranger", Plate = "999 TU 9", CompanyId = OtherCompanyId });

        ctx.VehicleCosts.AddRange(
            new VehicleCost { Id = 1, CompanyId = CompanyId, VehicleId = 1, Type = "maintenance", Amount = 120, Date = Day },
            new VehicleCost { Id = 2, CompanyId = CompanyId, VehicleId = 1, Type = "fuel", Amount = 60, Liters = 40, Date = Day },
            new VehicleCost { Id = 3, CompanyId = CompanyId, VehicleId = 2, Type = "insurance", Amount = 830, Date = Day },
            new VehicleCost { Id = 4, CompanyId = CompanyId, VehicleId = 2, Type = "fuel", Amount = 90, Liters = 60, Date = Day },
            new VehicleCost { Id = 5, CompanyId = OtherCompanyId, VehicleId = 9, Type = "tax", Amount = 77, Date = Day });

        ctx.UserVehicles.Add(new UserVehicle { UserId = RestrictedUserId, VehicleId = 1 });

        await ctx.SaveChangesAsync();
    }

    // ---------------------------------------------------------------- outils

    private static JsonElement Payload(ActionResult result) =>
        JsonSerializer.SerializeToElement(result.Should().BeOfType<OkObjectResult>().Subject.Value);

    /// GET /api/costs → identifiants réellement renvoyés par le contrôleur.
    private static async Task<List<int>> ListedIdsAsync(TestGisDbContext ctx, ICurrentTenantService tenant)
    {
        var result = await Controller(ctx, tenant).GetCosts();
        return Payload(result)
            .EnumerateArray()
            .Select(e => e.GetProperty("Id").GetInt32())
            .OrderBy(id => id)
            .ToList();
    }

    /// GET /api/costs/summary → total et litres réellement renvoyés.
    private static async Task<(decimal Total, decimal Liters)> SummaryAsync(TestGisDbContext ctx, ICurrentTenantService tenant)
    {
        var result = await Controller(ctx, tenant).GetCostSummary(PeriodStart, PeriodEnd);
        var payload = Payload(result);
        return (payload.GetProperty("TotalAmount").GetDecimal(),
                payload.GetProperty("TotalFuelLiters").GetDecimal());
    }

    private static Task<VehicleCost?> ReloadAsync(TestGisDbContext ctx, int id) =>
        ctx.VehicleCosts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);

    // ------------------------------------------------------------- lecture

    [Fact]
    public async Task Company_admin_sees_every_cost_of_the_company()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        (await ListedIdsAsync(ctx, Admin()))
            .Should().Equal(new[] { 1, 2, 3, 4 }, "un admin voit tout le parc (et jamais l'autre société)");

        var summary = await SummaryAsync(ctx, Admin());
        summary.Total.Should().Be(1_100m);
        summary.Liters.Should().Be(100m);
    }

    [Fact]
    public async Task Restricted_user_only_sees_costs_of_the_vehicles_assigned_to_them()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        (await ListedIdsAsync(ctx, Restricted()))
            .Should().Equal(new[] { 1, 2 }, "seul le véhicule 1 lui est affecté");

        var summary = await SummaryAsync(ctx, Restricted());
        summary.Total.Should().Be(180m, "le total en tête d'écran porte sur les mêmes lignes que la liste");
        summary.Liters.Should().Be(40m);
    }

    [Fact]
    public async Task User_without_any_assignment_sees_nothing()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        (await ListedIdsAsync(ctx, Orphan()))
            .Should().BeEmpty("liste d'affectations vide = résultat vide, jamais l'absence de filtre");

        (await SummaryAsync(ctx, Orphan())).Total.Should().Be(0m);
    }

    [Fact]
    public async Task Detail_of_a_cost_outside_the_scope_is_not_readable()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var controller = Controller(ctx, Restricted());

        (await controller.GetCost(3)).Result
            .Should().BeOfType<NotFoundResult>("la dépense 3 porte sur le véhicule 2, hors portée");

        (await Controller(ctx, Restricted()).GetCost(1)).Result
            .Should().BeOfType<OkObjectResult>("la dépense 1 porte sur son véhicule");
    }

    // ------------------------------------------------------------- écriture

    [Fact]
    public async Task Restricted_user_cannot_delete_a_cost_outside_their_scope()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        // Le défaut d'origine : 204 + ligne supprimée alors que le GET du même
        // identifiant renvoyait 404.
        var result = await Controller(ctx, Restricted()).DeleteCost(3);

        result.Should().BeOfType<NotFoundResult>();
        (await ReloadAsync(ctx, 3)).Should().NotBeNull("la dépense d'un véhicule hors portée doit survivre");
    }

    [Fact]
    public async Task Restricted_user_can_still_delete_a_cost_of_their_own_vehicle()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var result = await Controller(ctx, Restricted()).DeleteCost(1);

        result.Should().BeOfType<NoContentResult>("le cloisonnement ne doit pas bloquer ce qui est légitime");
        (await ReloadAsync(ctx, 1)).Should().BeNull();
    }

    [Fact]
    public async Task Company_admin_can_delete_any_cost_of_the_company()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        (await Controller(ctx, Admin()).DeleteCost(3)).Should().BeOfType<NoContentResult>();
        (await ReloadAsync(ctx, 3)).Should().BeNull();
    }

    [Fact]
    public async Task Cost_of_another_company_is_never_deletable()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        (await Controller(ctx, Admin()).DeleteCost(5)).Should().BeOfType<NotFoundResult>();
        (await ReloadAsync(ctx, 5)).Should().NotBeNull();
    }

    [Fact]
    public async Task Restricted_user_cannot_update_a_cost_outside_their_scope()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var payload = new VehicleCost
        {
            Type = "insurance", Description = "détournée", Amount = 1m, Date = Day
        };

        var result = await Controller(ctx, Restricted()).UpdateCost(3, payload);

        result.Should().BeOfType<NotFoundResult>();
        var untouched = await ReloadAsync(ctx, 3);
        untouched!.Amount.Should().Be(830m, "la ligne hors portée ne doit pas avoir bougé");
        untouched.Description.Should().BeNull();
    }

    [Fact]
    public async Task Restricted_user_can_still_update_a_cost_of_their_own_vehicle()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var payload = new VehicleCost
        {
            Type = "maintenance", Description = "vidange", Amount = 150m, Date = Day
        };

        (await Controller(ctx, Restricted()).UpdateCost(1, payload))
            .Should().BeOfType<NoContentResult>();

        (await ReloadAsync(ctx, 1))!.Amount.Should().Be(150m);
    }

    [Fact]
    public async Task Restricted_user_cannot_create_a_cost_on_a_vehicle_outside_their_scope()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var created = await Controller(ctx, Restricted()).CreateCost(new VehicleCost
        {
            VehicleId = 2, Type = "maintenance", Amount = 500m, Date = Day
        });

        created.Result.Should().BeOfType<NotFoundObjectResult>(
            "imputer une dépense à un véhicule qu'on ne voit pas est une écriture hors portée");
        (await ctx.VehicleCosts.AsNoTracking().CountAsync()).Should().Be(5, "rien ne doit être enregistré");
    }

    [Fact]
    public async Task Nobody_can_create_a_cost_on_a_vehicle_of_another_company()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var created = await Controller(ctx, Admin()).CreateCost(new VehicleCost
        {
            VehicleId = 9, Type = "tax", Amount = 42m, Date = Day
        });

        created.Result.Should().BeOfType<NotFoundObjectResult>();
        (await ctx.VehicleCosts.AsNoTracking().CountAsync()).Should().Be(5);
    }

    [Fact]
    public async Task Restricted_user_can_create_a_cost_on_their_own_vehicle()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var created = await Controller(ctx, Restricted()).CreateCost(new VehicleCost
        {
            VehicleId = 1, Type = "maintenance", Amount = 75m, Date = Day
        });

        created.Result.Should().BeOfType<CreatedAtActionResult>();

        var saved = await ctx.VehicleCosts.AsNoTracking()
            .Where(c => c.VehicleId == 1 && c.Amount == 75m)
            .SingleOrDefaultAsync();
        saved.Should().NotBeNull();
        saved!.CompanyId.Should().Be(CompanyId);
        saved.CreatedByUserId.Should().Be(RestrictedUserId);
    }
}
