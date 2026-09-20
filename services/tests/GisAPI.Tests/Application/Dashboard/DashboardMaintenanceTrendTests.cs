using FluentAssertions;
using GisAPI.Application.Features.AccidentEvents.Commands;
using GisAPI.Application.Features.Dashboard.Queries.GetDashboardCharts;
using GisAPI.Application.Features.Repairs;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Application.Dashboard;

/// <summary>
/// Courbe « Tendance entretien » du tableau de bord des clients équipés GPS
/// (GET /api/dashboard/charts). Elle se construisait sur les seules dépenses
/// <c>vehicle_costs</c> de type « maintenance » ou « repair » et ne lisait jamais la
/// table <c>repairs</c> : depuis que la phase 5 d'un sinistre y écrit sa réparation —
/// et retire la dépense correspondante — le montant disparaissait du mois, sans un mot.
///
/// La courbe suit désormais la définition des rapports de coûts : réparations non
/// annulées de l'écran Réparations, plus les dépenses de catégorie Entretien ou
/// Réparation, synonymes compris.
/// </summary>
public class DashboardMaintenanceTrendTests
{
    private const int CompanyId = 7;
    private const int AccidentId = 1;
    private const int VehiculeId = 49;
    private static readonly DateTime Debut = new(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Fin = new(2026, 9, 14, 17, 0, 0, DateTimeKind.Utc);

    private static TestGisDbContext Contexte()
    {
        var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle
        {
            Id = VehiculeId, Name = "Service 01", Plate = "GA-214-RK",
            CompanyId = CompanyId, GpsDeviceId = 900,
        });
        context.AccidentEvents.Add(new AccidentEvent
        {
            Id = AccidentId,
            CompanyId = CompanyId,
            VehicleId = VehiculeId,
            DeviceUid = string.Empty,
            IncidentAt = Debut.AddDays(-2),
            Confidence = 100,
            Origin = "manual",
            Status = "confirmed",
            ReferenceCode = "ACC-2026-014",
            MechanicName = "Garage Central",
        });
        context.SaveChanges();
        context.ChangeTracker.Clear();
        return context;
    }

    private static async Task<DashboardChartsDto> Courbes(TestGisDbContext context)
    {
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId).Object;
        return await new GetDashboardChartsQueryHandler(context, tenant)
            .Handle(new GetDashboardChartsQuery(Year: 2026, Month: 9), CancellationToken.None);
    }

    private static double TotalEntretien(DashboardChartsDto charts) =>
        charts.MaintenanceTrend.Series.Sum(s => s.Values.Sum());

    [Fact]
    public async Task LaReparationDUnSinistre_FigureDansLaCourbeDuMois()
    {
        using var context = Contexte();

        // Phase 5 : la réparation est écrite dans repairs et la dépense « repair »
        // du dossier est retirée — c'est exactement ce qui vidait la courbe.
        await new RegisterRepairCommandHandler(
                context,
                TestDbContextFactory.CreateMockTenantService(companyId: CompanyId).Object,
                NullLogger<RegisterRepairCommandHandler>.Instance)
            .Handle(new RegisterRepairCommand(AccidentId, Debut, Fin, 1200m), CancellationToken.None);
        context.ChangeTracker.Clear();

        var charts = await Courbes(context);

        TotalEntretien(charts).Should().Be(1200d, "la réparation du sinistre vit maintenant dans repairs");
        charts.MaintenanceTrend.Series.Should().ContainSingle()
            .Which.VehicleId.Should().Be(VehiculeId);
    }

    [Fact]
    public async Task UneReparationAnnulee_NeComptePas()
    {
        using var context = Contexte();
        context.Repairs.Add(new Repair
        {
            SocieteId = CompanyId, VehicleId = VehiculeId, Reference = "REP-202609-0002",
            RepairDate = Fin, TotalCost = 800m, Status = RepairInputRules.Cancelled,
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var charts = await Courbes(context);

        TotalEntretien(charts).Should().Be(0d, "les rapports de coûts écartent les réparations annulées");
    }

    [Fact]
    public async Task DepensesDEntretien_ComptentToujours_SynonymesCompris()
    {
        using var context = Contexte();
        context.VehicleCosts.AddRange(
            new VehicleCost { VehicleId = VehiculeId, CompanyId = CompanyId, Type = "maintenance", Amount = 100m, Date = Fin, Description = "Vidange" },
            // Type ancien, écrit avant la liste blanche des catégories : la courbe le
            // manquait, son test d'égalité ne connaissait que « maintenance »/« repair ».
            new VehicleCost { VehicleId = VehiculeId, CompanyId = CompanyId, Type = "réparation", Amount = 50m, Date = Fin, Description = "Pare-brise" },
            new VehicleCost { VehicleId = VehiculeId, CompanyId = CompanyId, Type = "fuel", Amount = 300m, Date = Fin, Description = "Plein" });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var charts = await Courbes(context);

        TotalEntretien(charts).Should().Be(150d, "le carburant n'est pas de l'entretien");
    }

    [Fact]
    public async Task UneReparationDUneAutreSociete_NEntrePasDansLaCourbe()
    {
        using var context = Contexte();
        context.Repairs.Add(new Repair
        {
            SocieteId = CompanyId + 1, VehicleId = VehiculeId, Reference = "REP-202609-0003",
            RepairDate = Fin, TotalCost = 999m, Status = RepairInputRules.Completed,
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var charts = await Courbes(context);

        TotalEntretien(charts).Should().Be(0d, "repairs n'a pas de filtre de requête global");
    }
}
