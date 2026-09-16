using FluentAssertions;
using GisAPI.Application.Features.Repairs.Handlers;
using GisAPI.Application.Features.Repairs.Queries;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Repairs;

/// <summary>
/// Compteurs de l'écran Réparations (GET /api/repairs/stats), recette GPA du 16/09/2026.
///
/// Depuis DEF-043, « Annulée » se choisit dans le panneau de modification. Or les
/// statistiques comptaient une réparation annulée dans le coût total et la moyenne (les
/// rapports l'excluent), et aucun compteur ne la reprenait : en attente + terminées ne
/// recoupait plus le total, le symptôme même de la fiche.
/// </summary>
public class RepairStatsAnnuleesTests
{
    private const int CompanyId = 7;
    private static readonly DateTime Day = new(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc);

    private static ICurrentTenantService Admin()
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.CompanyId).Returns(CompanyId);
        m.Setup(x => x.UserId).Returns(1);
        m.Setup(x => x.UserRoles).Returns(new[] { "company_admin" });
        m.Setup(x => x.IsAuthenticated).Returns(true);
        return m.Object;
    }

    private static Repair Repair(int id, string status, decimal labor, decimal parts) => new()
    {
        Id = id, SocieteId = CompanyId, VehicleId = 1, Reference = $"REP-{id}", RepairDate = Day,
        LaborCost = labor, PartsCost = parts, TotalCost = labor + parts, Status = status
    };

    [Fact]
    public async Task Une_reparation_annulee_est_comptee_a_part_et_exclue_des_montants()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Opel", Plate = "111 TU 1", CompanyId = CompanyId });
        ctx.Repairs.AddRange(
            Repair(1, "completed", 40m, 60m),
            Repair(2, "pending", 100m, 0m),
            Repair(3, "cancelled", 200m, 300m),
            // Casse ancienne : les rapports la comparent déjà sans casse.
            Repair(4, "Cancelled", 1_000m, 0m));
        await ctx.SaveChangesAsync();

        var stats = await new GetRepairStatsQueryHandler(ctx, Admin())
            .Handle(new GetRepairStatsQuery(), CancellationToken.None);

        stats.TotalRepairs.Should().Be(4);
        stats.CancelledRepairs.Should().Be(2);
        (stats.PendingRepairs + stats.CompletedRepairs + stats.CancelledRepairs)
            .Should().Be(stats.TotalRepairs, "les compteurs de l'écran doivent se recouper");
        stats.TotalCost.Should().Be(200m, "une réparation annulée ne coûte rien, comme dans les rapports");
        stats.AverageCost.Should().Be(100m);
        stats.TotalLaborCost.Should().Be(140m);
        stats.TotalPartsCost.Should().Be(60m);
    }

    [Fact]
    public async Task Sans_reparation_annulee_les_montants_sont_inchanges()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Opel", Plate = "111 TU 1", CompanyId = CompanyId });
        ctx.Repairs.AddRange(Repair(1, "completed", 40m, 60m), Repair(2, "in_progress", 30m, 50m));
        await ctx.SaveChangesAsync();

        var stats = await new GetRepairStatsQueryHandler(ctx, Admin())
            .Handle(new GetRepairStatsQuery(), CancellationToken.None);

        stats.CancelledRepairs.Should().Be(0);
        stats.PendingRepairs.Should().Be(1);
        stats.TotalCost.Should().Be(180m);
        stats.AverageCost.Should().Be(90m);
    }
}
