using FluentAssertions;
using GisAPI.Application.Features.FuelEntries.Queries;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.FuelEntries;

/// <summary>
/// Tuiles de l'écran Carburant : calculées sur les 100 pleins chargés, elles annonçaient
/// « 100 entrées, 8 004 » pour 439 pleins et 42 383 € en base (campagne de tests GPA du
/// 06/10/2026, erreur n°3). Elles portent désormais sur tous les pleins des filtres, avec le
/// même périmètre que la liste, et le prix moyen est pondéré par les litres.
/// </summary>
public class FuelEntriesSummaryTests
{
    private const int CompanyId = 1;
    private const int RestrictedUserId = 51;
    private static readonly DateTime Jour = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private static ICurrentTenantService Tenant(int userId, params string[] roles)
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.CompanyId).Returns(CompanyId);
        m.Setup(x => x.UserId).Returns(userId);
        m.Setup(x => x.UserRoles).Returns(roles);
        m.Setup(x => x.IsAuthenticated).Returns(true);
        return m.Object;
    }

    private static ICurrentTenantService Admin() => Tenant(1, "company_admin");

    private static FuelEntry Plein(int id, int vehicleId, decimal litres, decimal prix, int jours = 0, int companyId = CompanyId) => new()
    {
        Id = id, CompanyId = companyId, VehicleId = vehicleId, VehiclePlate = $"V{vehicleId}", FuelTypeId = 1,
        Volume = litres, PricePerLiter = prix, TotalAmount = litres * prix, InvoiceDate = Jour.AddDays(jours)
    };

    private static Task<FuelEntriesSummaryDto> TotauxAsync(TestGisDbContext ctx, ICurrentTenantService tenant,
        int? vehicleId = null, DateTime? debut = null, DateTime? fin = null) =>
        new GetFuelEntriesSummaryQueryHandler(ctx, tenant)
            .Handle(new GetFuelEntriesSummaryQuery(null, null, debut, fin, vehicleId), CancellationToken.None);

    [Fact]
    public async Task Les_totaux_portent_sur_tous_les_pleins_et_pas_sur_la_page_chargee()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Clio", Plate = "V1", CompanyId = CompanyId });
        for (var i = 1; i <= 150; i++) ctx.FuelEntries.Add(Plein(i, 1, 40m, 1.80m, jours: i % 30));
        ctx.FuelEntries.Add(Plein(999, 9, 50m, 2m, companyId: 2));
        await ctx.SaveChangesAsync();

        var totaux = await TotauxAsync(ctx, Admin());

        totaux.Count.Should().Be(150, "les 150 pleins de la société, pas les 100 premiers ni celui d'une autre société");
        totaux.TotalVolume.Should().Be(6000m);
        totaux.TotalAmount.Should().Be(10800m);
    }

    [Fact]
    public async Task Le_prix_moyen_est_pondere_par_les_litres()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Clio", Plate = "V1", CompanyId = CompanyId });
        ctx.FuelEntries.AddRange(Plein(1, 1, 60m, 2.00m), Plein(2, 1, 10m, 1.00m));
        await ctx.SaveChangesAsync();

        var totaux = await TotauxAsync(ctx, Admin());

        // 130 € pour 70 L = 1,857 €/L ; la moyenne simple des prix aurait donné 1,50.
        totaux.AveragePricePerLiter.Should().Be(1.857m);
    }

    [Fact]
    public async Task Memes_filtres_et_meme_perimetre_que_la_liste()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "Clio", Plate = "V1", CompanyId = CompanyId },
            new Vehicle { Id = 2, Name = "Kangoo", Plate = "V2", CompanyId = CompanyId });
        ctx.FuelEntries.AddRange(Plein(1, 1, 40m, 2m), Plein(2, 2, 30m, 2m), Plein(3, 1, 20m, 2m, jours: 40));
        ctx.UserVehicles.Add(new UserVehicle { UserId = RestrictedUserId, VehicleId = 1 });
        await ctx.SaveChangesAsync();

        (await TotauxAsync(ctx, Admin(), vehicleId: 2)).Count.Should().Be(1);
        (await TotauxAsync(ctx, Admin(), debut: Jour, fin: Jour.AddDays(10))).Count.Should().Be(2);
        (await TotauxAsync(ctx, Tenant(RestrictedUserId, "user"))).TotalVolume
            .Should().Be(60m, "un utilisateur restreint ne compte que les pleins de ses véhicules");

        var liste = await new GetFuelEntriesQueryHandler(ctx, Tenant(RestrictedUserId, "user"))
            .Handle(new GetFuelEntriesQuery(null, null, null, null), CancellationToken.None);
        liste.TotalCount.Should().Be(2, "la liste et ses totaux partagent le même périmètre");
    }

    [Fact]
    public async Task Sans_plein_le_prix_moyen_est_absent()
    {
        using var ctx = TestDbContextFactory.Create();

        var totaux = await TotauxAsync(ctx, Admin());

        totaux.Count.Should().Be(0);
        totaux.AveragePricePerLiter.Should().BeNull();
    }
}
