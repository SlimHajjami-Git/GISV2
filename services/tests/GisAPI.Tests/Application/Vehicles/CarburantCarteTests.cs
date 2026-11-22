using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using GisAPI.Application.Common.FuelCalibration;
using GisAPI.Application.Features.Vehicles.Queries.GetVehiclesWithPositions;
using GisAPI.Controllers;
using GisAPI.Services;
using PositionDto = GisAPI.Application.Features.Vehicles.Queries.GetVehiclesWithPositions.PositionDto;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Vehicles;

/// <summary>
/// Carte en direct : le carburant venu du cache Redis doit être converti comme celui de la
/// base (recette du 20/11/2026).
///
/// <para>Ibiza 237 TU 4672 (HERTZ) : boîtier en mode <c>liters</c>, réservoir de 40 L, le
/// boîtier envoie 19 → 19 L, soit 48 %. La requête de la carte convertissait bien, mais le
/// contrôleur remplaçait ensuite ce niveau par la valeur BRUTE du cache Redis, plus récente :
/// la carte affichait 19 % et allumait l'alerte « carburant bas » (seuil 20 %). 482 boîtiers
/// sur 580 sont en mode <c>liters</c> à TN.</para>
/// </summary>
public class CarburantCarteTests
{
    private const int CompanyId = 7;
    private const string Imei = "860141076690818";
    private static readonly DateTime TrameBase = new(2026, 11, 20, 9, 56, 0, DateTimeKind.Utc);

    // ── La règle de conversion ──────────────────────────────────────────────

    [Theory]
    [InlineData(19, "liters", 40, 48)]       // l'Ibiza de la recette
    [InlineData(6, "liters", 40, 15)]
    [InlineData(38, "half_liter", 40, 48)]   // 38 demi-litres = 19 L
    [InlineData(128, "raw_255", null, 50)]
    [InlineData(255, "raw_255", null, 100)]
    [InlineData(70, "percent", null, 70)]
    [InlineData(30, "liters", null, 50)]     // réservoir inconnu : 60 L supposés
    [InlineData(128, null, null, 50)]        // mode absent : raw_255, le défaut du boîtier
    [InlineData(55, "liters", 50, 100)]      // jauge au-delà du réservoir : bornée à 100
    [InlineData(42, "inconnu", null, 42)]    // mode inconnu : valeur laissée telle quelle
    public void ToPercent_ConvertitSelonLeModeDuCapteur(int brut, string? mode, int? reservoir, int attendu)
    {
        FuelLevel.ToPercent(brut, mode, reservoir).Should().Be(attendu);
    }

    [Fact]
    public void ToPercent_ReservoirNul_NeDivisePasParZero()
    {
        FuelLevel.ToPercent(19, "liters", 0).Should().Be(19);
        FuelLevel.ToPercent(38, "half_liter", 0).Should().Be(19);
    }

    // ── Le chemin Redis de /vehicles/with-positions ─────────────────────────

    [Fact]
    public async Task Ibiza_EnLitres_LeCacheRedisNAfficheplusLesLitresCommeUnPourcentage()
    {
        // La base a converti 19 L en 48 % ; Redis porte une trame plus récente, brute.
        var vehicule = Vehicule(fuelRawBase: 19, niveauBase: 48, mode: "liters", reservoir: 40);
        var stats = await Carte(vehicule, Cache(fuelRaw: 19));

        stats.FuelLevel.Should().Be(48, "19 L sur 40 L = 48 %, et non « 19 % » (alerte carburant bas à tort)");
    }

    [Theory]
    [InlineData("liters", 40, 26, 65)]
    [InlineData("half_liter", 40, 52, 65)]
    [InlineData("raw_255", null, 204, 80)]
    [InlineData("percent", null, 65, 65)]
    public async Task CacheRedis_ConvertitCommeLaBase(string mode, int? reservoir, int brutCache, int attendu)
    {
        var vehicule = Vehicule(fuelRawBase: 1, niveauBase: 1, mode: mode, reservoir: reservoir);
        var stats = await Carte(vehicule, Cache(fuelRaw: brutCache));

        stats.FuelLevel.Should().Be(attendu);
    }

    [Fact]
    public async Task CacheRedis_SansCarburant_GardeLeNiveauDeLaBase()
    {
        // Trame Redis sans carburant (0) : on retombe sur la dernière valeur de la base,
        // convertie elle aussi — et non sur le brut de la base.
        var vehicule = Vehicule(fuelRawBase: 19, niveauBase: 48, mode: "liters", reservoir: 40);
        var stats = await Carte(vehicule, Cache(fuelRaw: 0));

        stats.FuelLevel.Should().Be(48);
    }

    [Fact]
    public void ReglageCarburant_NeSortJamaisDansLeJsonDeLaCarte()
    {
        var vehicule = Vehicule(fuelRawBase: 19, niveauBase: 48, mode: "liters", reservoir: 40);

        var json = JsonSerializer.Serialize(vehicule, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().NotContain("fuelSensorMode").And.NotContain("fuelTankCapacity");
        json.Should().Contain("\"fuelLevel\":48");
    }

    // ── Outils ──────────────────────────────────────────────────────────────

    private static VehicleWithPositionDto Vehicule(int fuelRawBase, int niveauBase, string mode, int? reservoir) =>
        new(
            Id: 42, Name: "237 TU 4672", Type: "car", Brand: "Seat", Model: "Ibiza", Plate: "237 TU 4672",
            Status: "available", HasGps: true, DeviceUid: Imei, GpsDeviceId: 127630,
            LastCommunication: TrameBase, IsOnline: true,
            LastPosition: new PositionDto(1, 36.8, 10.18, 40, 90, true, TrameBase, fuelRawBase,
                null, null, null, null, null),
            Stats: new VehicleStatsDto(40, 80, niveauBase, null, null, null, true, false,
                TimeSpan.Zero, TimeSpan.Zero, null, TrameBase, null),
            Mileage: 0, HasBatteryHealthAlert: false, IsImmobilized: false,
            ImmobilizationReason: null, ImmobilizationStartedAt: null,
            FuelSensorMode: mode, FuelTankCapacity: reservoir);

    /// <summary>Position Redis plus récente que celle de la base (c'est le cas d'un véhicule en ligne).</summary>
    private static VehiclePositionCache Cache(int fuelRaw) => new()
    {
        DeviceUid = Imei, CompanyId = CompanyId, Latitude = 36.81, Longitude = 10.19,
        SpeedKph = 50, IgnitionOn = true, IsValid = true, FuelRaw = fuelRaw,
        RecordedAt = TrameBase.AddMinutes(1), CachedAt = TrameBase.AddMinutes(1)
    };

    private static async Task<VehicleStatsDto> Carte(VehicleWithPositionDto vehicule, VehiclePositionCache cache)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetVehiclesWithPositionsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VehicleWithPositionDto> { vehicule });

        var redis = new Mock<IRedisCacheService>();
        redis.Setup(r => r.GetAllPositionsForCompanyAsync(CompanyId))
            .ReturnsAsync(new List<VehiclePositionCache> { cache });

        var controleur = new VehiclesController(mediator.Object, redis.Object,
            new MemoryCache(new MemoryCacheOptions()), NullLogger<VehiclesController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("companyId", CompanyId.ToString()),
                        new Claim(ClaimTypes.NameIdentifier, "3")
                    }, "test"))
                }
            }
        };

        var resultat = await controleur.GetVehiclesWithPositions();

        var liste = (List<VehicleWithPositionDto>)((OkObjectResult)resultat.Result!).Value!;
        liste.Should().ContainSingle();
        liste[0].LastPosition!.RecordedAt.Should().Be(cache.RecordedAt, "la trame Redis, plus récente, doit avoir été prise");
        return liste[0].Stats!;
    }
}
