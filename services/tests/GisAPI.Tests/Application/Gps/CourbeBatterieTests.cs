using FluentAssertions;
using GisAPI.Application.Features.Vehicles.Queries.GetVehicleBatteryHistory;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Gps;

/// <summary>
/// Courbe de tension batterie servie à la fenêtre qui s'ouvre depuis la notification
/// « batterie en fin de vie » (Slim, 29/09/2026).
///
/// <para>Ce que ces cas protègent en priorité : le CLOISONNEMENT. L'incident
/// HERTZ/Kap Pharma du 23/09/2026 — un client voyait les véhicules des autres
/// locataires — rend toute nouvelle lecture par identifiant de véhicule suspecte tant
/// qu'elle n'est pas épinglée ici.</para>
///
/// <para>Les agrégats par tranche viennent de SQL brut, que le provider en mémoire ne
/// sait pas exécuter : ces tests couvrent donc le cloisonnement, les protocoles non
/// supportés et les bornes de période, pas le contenu des tranches.</para>
/// </summary>
public class CourbeBatterieTests
{
    private const int CompanyId = 1;
    private const int AutreSociete = 2;
    private const int AdminId = 10;
    private const int ChauffeurId = 11;

    private static Vehicle SeedVehicule(TestGisDbContext context, int id, int companyId,
        string? protocolType, int deviceId)
    {
        if (protocolType != null)
        {
            context.GpsDevices.Add(new GpsDevice
            {
                Id = deviceId,
                CompanyId = companyId,
                DeviceUid = $"DEV-{deviceId}",
                Status = "active",
                ProtocolType = protocolType,
                BatteryStartMedianRaw = 72
            });
        }

        var vehicle = new Vehicle
        {
            Id = id,
            CompanyId = companyId,
            Name = $"Véhicule {id}",
            Plate = $"TN-{id}",
            Type = "camion",
            Status = "available",
            GpsDeviceId = protocolType != null ? deviceId : null
        };
        context.Vehicles.Add(vehicle);
        return vehicle;
    }

    private static GetVehicleBatteryHistoryQueryHandler Handler(
        TestGisDbContext context, int userId, bool isAdmin)
    {
        var tenant = TestDbContextFactory.CreateMockTenantService(CompanyId, userId);
        tenant.Setup(t => t.UserRoles).Returns(isAdmin ? new[] { "company_admin" } : new[] { "user" });
        return new GetVehicleBatteryHistoryQueryHandler(context, tenant.Object);
    }

    [Fact]
    public async Task UnVehiculeDUneAutreSociete_RendNull_DoncQuatreCentQuatre()
    {
        using var context = TestDbContextFactory.Create();
        SeedVehicule(context, id: 500, companyId: AutreSociete, "gps_type_1", deviceId: 900);
        await context.SaveChangesAsync();

        var result = await Handler(context, AdminId, isAdmin: true)
            .Handle(new GetVehicleBatteryHistoryQuery(500, 7), default);

        result.Should().BeNull("un identifiant d'une autre société ne doit rien révéler, " +
                               "pas même l'existence du véhicule");
    }

    [Fact]
    public async Task UnNonAdminSansAffectation_NAccedePasALaCourbe()
    {
        using var context = TestDbContextFactory.Create();
        SeedVehicule(context, id: 501, companyId: CompanyId, "gps_type_1", deviceId: 901);
        await context.SaveChangesAsync();

        var result = await Handler(context, ChauffeurId, isAdmin: false)
            .Handle(new GetVehicleBatteryHistoryQuery(501, 7), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UnNonAdminAffecte_PasseLeCloisonnement()
    {
        // Boîtier non NEMS à dessein : l'agrégation par tranche est du SQL PostgreSQL
        // (to_timestamp, EXTRACT(epoch)) que SQLite ne sait pas exécuter. Ce qui se
        // vérifie ici est la seule chose qui compte pour ce cas — l'accès est accordé,
        // puisqu'on obtient une réponse au lieu du null des deux tests précédents.
        using var context = TestDbContextFactory.Create();
        SeedVehicule(context, id: 502, companyId: CompanyId, "teltonika", deviceId: 902);
        context.UserVehicles.Add(new UserVehicle { UserId = ChauffeurId, VehicleId = 502 });
        await context.SaveChangesAsync();

        var result = await Handler(context, ChauffeurId, isAdmin: false)
            .Handle(new GetVehicleBatteryHistoryQuery(502, 7), default);

        result.Should().NotBeNull();
        result!.VehicleId.Should().Be(502);
    }

    [Fact]
    public async Task UnVehiculeSansBoitierNems_LeDitFranchement()
    {
        // Teltonika : pas d'octet « Batterie », donc pas de courbe. L'écran doit
        // l'annoncer plutôt que d'afficher un cadre vide qu'on prendrait pour un bug.
        using var context = TestDbContextFactory.Create();
        SeedVehicule(context, id: 503, companyId: CompanyId, "teltonika", deviceId: 903);
        await context.SaveChangesAsync();

        var result = await Handler(context, AdminId, isAdmin: true)
            .Handle(new GetVehicleBatteryHistoryQuery(503, 7), default);

        result.Should().NotBeNull();
        result!.Supported.Should().BeFalse();
        result.Points.Should().BeEmpty();
        result.Starts.Should().BeEmpty();
    }

    [Fact]
    public async Task UnVehiculeSansBoitier_LeDitAussi()
    {
        using var context = TestDbContextFactory.Create();
        SeedVehicule(context, id: 504, companyId: CompanyId, protocolType: null, deviceId: 904);
        await context.SaveChangesAsync();

        var result = await Handler(context, AdminId, isAdmin: true)
            .Handle(new GetVehicleBatteryHistoryQuery(504, 7), default);

        result!.Supported.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, GetVehicleBatteryHistoryQueryHandler.DefaultDays)]    // paramètre absent
    [InlineData(-5, GetVehicleBatteryHistoryQueryHandler.DefaultDays)]   // valeur absurde
    [InlineData(1, 1)]
    [InlineData(400, GetVehicleBatteryHistoryQueryHandler.MaxDays)]      // borné
    public async Task LaPeriodeEstBornee_QuUnParametreDUrlNeFasseBalayerDesMois(int demande, int attendu)
    {
        using var context = TestDbContextFactory.Create();
        SeedVehicule(context, id: 505, companyId: CompanyId, "teltonika", deviceId: 905);
        await context.SaveChangesAsync();

        var result = await Handler(context, AdminId, isAdmin: true)
            .Handle(new GetVehicleBatteryHistoryQuery(505, demande), default);

        result!.Days.Should().Be(attendu);
    }
}
