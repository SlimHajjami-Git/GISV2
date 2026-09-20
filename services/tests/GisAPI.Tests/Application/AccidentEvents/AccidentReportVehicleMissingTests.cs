using FluentAssertions;
using GisAPI.Application.Features.AccidentEvents.Queries;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.AccidentEvents;

/// <summary>
/// Bandeau « ce dossier n'est plus rattaché à un véhicule » de la fiche de sinistre.
///
/// <para>Il ne pouvait JAMAIS s'allumer dans le cas qu'il vise : la suppression d'un
/// véhicule met <c>accident_events.vehicle_id</c> à NULL (VehicleDeletionHelper), et
/// <c>VehicleExists</c> valait <c>true</c> dès que l'identifiant était absent. Le
/// bandeau n'apparaissait que sur un <c>vehicle_id</c> pendant, que la cascade ne
/// produit plus depuis DEF-046.</para>
/// </summary>
public class AccidentReportVehicleMissingTests
{
    private const int CompanyId = 7;
    private const int AccidentId = 1;
    private const int VehiculeExistant = 49;
    private static readonly DateTime Sinistre = new(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc);

    private static TestGisDbContext Contexte(int? vehiculeDuSinistre, string? libelleVehicule = null)
    {
        var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = VehiculeExistant, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId });
        context.AccidentEvents.Add(new AccidentEvent
        {
            Id = AccidentId,
            CompanyId = CompanyId,
            VehicleId = vehiculeDuSinistre,
            VehicleLabel = libelleVehicule,
            DeviceUid = string.Empty,
            IncidentAt = Sinistre,
            Confidence = 100,
            Origin = "manual",
            Status = "confirmed",
            ReferenceCode = "ACC-2026-014",
        });
        context.SaveChanges();
        context.ChangeTracker.Clear();
        return context;
    }

    private static async Task<AccidentReportDto?> Fiche(TestGisDbContext context) =>
        await new GetAccidentReportQueryHandler(
                context, TestDbContextFactory.CreateMockTenantService(companyId: CompanyId).Object)
            .Handle(new GetAccidentReportQuery(AccidentId), CancellationToken.None);

    [Fact]
    public async Task DossierDetacheParLaSuppressionDuVehicule_AllumeLeBandeau()
    {
        using var context = Contexte(null, libelleVehicule: "GA-214-RK");

        var dto = await Fiche(context);

        dto.Should().NotBeNull();
        dto!.VehicleExists.Should().BeFalse("c'est exactement ce que la cascade laisse derrière elle");
    }

    [Fact]
    public async Task DossierSansVehicule_AllumeLeBandeau()
    {
        using var context = Contexte(null);

        (await Fiche(context))!.VehicleExists.Should().BeFalse(
            "aucun montant ne peut être reporté, que le véhicule ait été supprimé ou jamais saisi");
    }

    [Fact]
    public async Task VehicleIdPendant_AllumeLeBandeau()
    {
        using var context = Contexte(50);

        (await Fiche(context))!.VehicleExists.Should().BeFalse();
    }

    [Fact]
    public async Task VehiculePresent_NAllumeRien()
    {
        using var context = Contexte(VehiculeExistant);

        (await Fiche(context))!.VehicleExists.Should().BeTrue();
    }
}
