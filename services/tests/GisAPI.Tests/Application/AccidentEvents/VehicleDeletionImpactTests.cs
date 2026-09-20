using FluentAssertions;
using GisAPI.Application.Features.AccidentEvents.Queries;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.AccidentEvents;

/// <summary>
/// Avant de supprimer un véhicule, l'écran d'administration annonçait « toutes les
/// données associées seront perdues » sans dire lesquelles. Or la règle réelle
/// (<c>VehicleDeletionHelper</c>) n'est pas uniforme : les dossiers de SINISTRE
/// survivent, détachés ; les réparations et les dépenses, dont la colonne
/// <c>vehicle_id</c> est NON NULL, partent avec le véhicule.
///
/// Ces compteurs alimentent la fenêtre de confirmation.
/// </summary>
public class VehicleDeletionImpactTests
{
    private const int CompanyId = 7;
    private const int AutreSociete = 8;
    private const int VehiculeId = 49;
    private static readonly DateTime Jour = new(2026, 9, 14, 17, 0, 0, DateTimeKind.Utc);

    private static TestGisDbContext Contexte()
    {
        var context = TestDbContextFactory.Create();
        context.Vehicles.AddRange(
            new Vehicle { Id = VehiculeId, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId },
            new Vehicle { Id = 50, Name = "Service 02", Plate = "GA-215-RK", CompanyId = CompanyId });
        context.AccidentEvents.AddRange(
            new AccidentEvent
            {
                Id = 1, CompanyId = CompanyId, VehicleId = VehiculeId, DeviceUid = string.Empty,
                IncidentAt = Jour, Confidence = 100, Origin = "manual", Status = "confirmed",
            },
            new AccidentEvent
            {
                Id = 2, CompanyId = CompanyId, VehicleId = VehiculeId, DeviceUid = string.Empty,
                IncidentAt = Jour, Confidence = 100, Origin = "auto", Status = "pending",
            },
            // Dossier d'un AUTRE véhicule : il ne doit pas gonfler le compte.
            new AccidentEvent
            {
                Id = 3, CompanyId = CompanyId, VehicleId = 50, DeviceUid = string.Empty,
                IncidentAt = Jour, Confidence = 100, Origin = "manual", Status = "confirmed",
            });
        context.Repairs.Add(new Repair
        {
            SocieteId = CompanyId, VehicleId = VehiculeId, Reference = "REP-202609-0001",
            RepairDate = Jour, TotalCost = 1200m, Status = "completed",
        });
        context.VehicleCosts.AddRange(
            new VehicleCost { VehicleId = VehiculeId, CompanyId = CompanyId, Type = "fuel", Amount = 120m, Date = Jour, Description = "Plein" },
            new VehicleCost { VehicleId = VehiculeId, CompanyId = CompanyId, Type = "insurance_refund", Amount = 900m, Date = Jour, Description = "Remboursement" });
        context.SaveChanges();
        context.ChangeTracker.Clear();
        return context;
    }

    private static Task<VehicleDeletionImpactDto?> Impact(TestGisDbContext context, int companyId, int vehicleId = VehiculeId, bool systemAdmin = false)
    {
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: companyId);
        tenant.Setup(t => t.IsSystemAdmin).Returns(systemAdmin);
        return new GetVehicleDeletionImpactQueryHandler(context, tenant.Object)
            .Handle(new GetVehicleDeletionImpactQuery(vehicleId), CancellationToken.None);
    }

    [Fact]
    public async Task LesDossiersDeSinistreEtLesLignesSupprimees_SontComptesSeparement()
    {
        using var context = Contexte();

        var impact = await Impact(context, CompanyId);

        impact.Should().NotBeNull();
        impact!.Accidents.Should().Be(2, "les dossiers du véhicule survivent, détachés");
        impact.Repairs.Should().Be(1, "les réparations partent avec le véhicule");
        impact.Costs.Should().Be(2, "les dépenses aussi, remboursement compris");
    }

    [Fact]
    public async Task UnVehiculeSansHistorique_NAnnonceRien()
    {
        using var context = Contexte();

        var impact = await Impact(context, CompanyId, vehicleId: 50);

        impact!.Accidents.Should().Be(1);
        impact.Repairs.Should().Be(0);
        impact.Costs.Should().Be(0);
    }

    [Fact]
    public async Task UnVehiculeDUneAutreSociete_EstIntrouvable()
    {
        using var context = Contexte();

        (await Impact(context, AutreSociete)).Should().BeNull();
    }

    [Fact]
    public async Task LAdministrateurSysteme_VoitLeVehiculeDeNImporteQuelleSociete()
    {
        using var context = Contexte();

        // L'écran d'administration travaille sur toutes les sociétés : son jeton ne
        // porte pas la société du véhicule.
        var impact = await Impact(context, AutreSociete, systemAdmin: true);

        impact.Should().NotBeNull();
        impact!.Accidents.Should().Be(2);
        impact.Repairs.Should().Be(1);
    }

    [Fact]
    public async Task UnVehiculeInexistant_EstIntrouvable()
    {
        using var context = Contexte();

        (await Impact(context, CompanyId, vehicleId: 9999)).Should().BeNull();
    }
}
