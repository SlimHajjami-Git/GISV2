using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Admin.Monitoring;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Admin;

/// <summary>
/// Connexions GPS en attente d'identification : choix du véhicule probable et
/// confirmation par un administrateur. Positions et heures réelles d'EL MOUSSEM
/// (19-21/10/2026).
/// </summary>
public class HeldConnectionsTests
{
    private static readonly DateTime Le20Octobre13h07 = new(2026, 10, 20, 12, 7, 0, DateTimeKind.Utc);

    private static HeldConnectionCandidate Candidat(int id, string plaque, double lat, double lon, DateTime derniere) =>
        new(id, $"86000000{id:D7}", plaque, "SOCIETE TEST", derniere, derniere, lat, lon);

    // ── Choix du véhicule probable ─────────────────────────────────────────────

    [Fact]
    public void Matcher_VehiculePerduEnRoulant_EstPropose()
    {
        // VEHICULE C : dernière position 13h05, première trame rejetée 13h07, 250 m plus loin.
        var premiere = new HeldPosition(36.76208, 10.24707, Le20Octobre13h07, 20);
        var candidats = new[]
        {
            Candidat(1, "VEHICULE C", 36.76014, 10.24882, Le20Octobre13h07.AddMinutes(-2)),
            // VEHICULE D, perdu le même jour près de Kairouan : 130 km, impossible en 2 min.
            Candidat(2, "VEHICULE D", 35.60645, 9.89998, Le20Octobre13h07.AddMinutes(-3)),
        };

        var (suggestions, ambigu) = HeldConnectionMatcher.Rank(premiere, candidats);

        suggestions.Should().ContainSingle();
        suggestions[0].Plate.Should().Be("VEHICULE C");
        suggestions[0].DistanceMeters.Should().BeInRange(200, 300);
        suggestions[0].GapMinutes.Should().Be(2);
        ambigu.Should().BeFalse();
    }

    [Fact]
    public void Matcher_DeuxVehiculesGaresCoteACote_SontSignalesAmbigus()
    {
        // Au dépôt le 20/10 : VEHICULE A et VEHICULE B garés à 6 m l'un de l'autre.
        var premiere = new HeldPosition(36.71222, 10.17513, Le20Octobre13h07, 0);
        var candidats = new[]
        {
            Candidat(1, "VEHICULE A", 36.71222, 10.17513, Le20Octobre13h07.AddMinutes(-40)),
            Candidat(2, "VEHICULE B", 36.71221, 10.17519, Le20Octobre13h07.AddMinutes(-35)),
        };

        var (suggestions, ambigu) = HeldConnectionMatcher.Rank(premiere, candidats);

        suggestions.Should().HaveCount(2);
        ambigu.Should().BeTrue("deux véhicules à quelques mètres : l'administrateur doit trancher en connaissance de cause");
    }

    [Fact]
    public void Matcher_UnCandidatLointainMaisAtteignable_ResteProposeApresLePlusProche()
    {
        var premiere = new HeldPosition(36.70660, 10.17159, Le20Octobre13h07, 0);
        var candidats = new[]
        {
            // 30 km, mais tu depuis 30 min : atteignable à 130 km/h.
            Candidat(1, "LOIN", 36.97, 10.17, Le20Octobre13h07.AddMinutes(-30)),
            // Garé à 2 m : VEHICULE E au dépôt le 21/10.
            Candidat(2, "VEHICULE E", 36.70658, 10.17160, Le20Octobre13h07.AddMinutes(-36)),
        };

        var (suggestions, ambigu) = HeldConnectionMatcher.Rank(premiere, candidats);

        suggestions.Select(s => s.Plate).Should().Equal("VEHICULE E", "LOIN");
        suggestions[0].DistanceMeters.Should().BeLessThan(5);
        ambigu.Should().BeFalse();
    }

    [Fact]
    public void Matcher_AucunCandidatPlausible_RienNEstPropose()
    {
        var premiere = new HeldPosition(36.84774, 10.27419, Le20Octobre13h07, 0);
        var (suggestions, ambigu) = HeldConnectionMatcher.Rank(premiere,
            new[] { Candidat(1, "TROP LOIN", 36.96, 10.91, Le20Octobre13h07.AddMinutes(-5)) });

        suggestions.Should().BeEmpty();
        ambigu.Should().BeFalse();
    }

    // ── Liste ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Liste_IngestionInjoignable_LeDit()
    {
        using var ctx = TestDbContextFactory.Create();
        var ingest = new FakeIngest { Liste = null };

        var dto = await new GetHeldConnectionsQueryHandler(ctx, ingest).Handle(new GetHeldConnectionsQuery(), CancellationToken.None);

        dto.IngestReachable.Should().BeFalse();
        dto.Connections.Should().BeEmpty();
    }

    [Fact]
    public async Task Liste_RendLesConnexionsEnAttente()
    {
        using var ctx = TestDbContextFactory.Create();
        var ingest = new FakeIngest
        {
            Liste = new List<HeldConnection>
            {
                new("203.0.113.10:2618", "gps_type_1", true, Le20Octobre13h07, Le20Octobre13h07.AddHours(2), 73, 0,
                    new HeldPosition(36.73930, 10.25339, Le20Octobre13h07, 40), null)
            }
        };

        var dto = await new GetHeldConnectionsQueryHandler(ctx, ingest).Handle(new GetHeldConnectionsQuery(), CancellationToken.None);

        dto.IngestReachable.Should().BeTrue();
        dto.Connections.Should().ContainSingle();
        dto.Connections[0].Peer.Should().Be("203.0.113.10:2618");
        dto.Connections[0].Frames.Should().Be(73);
    }

    // ── Confirmation ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Confirmation_TransmetLImeiDuBoitierEtLAuteur()
    {
        using var ctx = TestDbContextFactory.Create();
        Seed(ctx, 7, "VEHICULE A");
        await ctx.SaveChangesAsync();
        var ingest = new FakeIngest { Reponse = new HeldConnectionAttribution(HeldAttributionOutcome.Done, 73, true, null) };

        var result = await Handler(ctx, ingest).Handle(
            new AttributeHeldConnectionCommand(" 203.0.113.10:2618 ", 7), CancellationToken.None);

        result.Ok.Should().BeTrue();
        result.Message.Should().Contain("VEHICULE A").And.Contain("73 trame(s)");
        ingest.Appels.Should().ContainSingle().Which.Should().Be(("203.0.113.10:2618", "860000000000007", "admin@exemple.test"));
    }

    [Fact]
    public async Task Confirmation_BoitierInconnu_NAppellePasLIngestion()
    {
        using var ctx = TestDbContextFactory.Create();
        var ingest = new FakeIngest();

        var result = await Handler(ctx, ingest).Handle(
            new AttributeHeldConnectionCommand("203.0.113.10:2618", 404), CancellationToken.None);

        result.Ok.Should().BeFalse();
        ingest.Appels.Should().BeEmpty();
    }

    [Fact]
    public async Task Confirmation_LeBoitierSEstAnnonceAutrement_EstRefusee()
    {
        using var ctx = TestDbContextFactory.Create();
        Seed(ctx, 7, "VEHICULE A");
        await ctx.SaveChangesAsync();
        var ingest = new FakeIngest
        {
            Reponse = new HeldConnectionAttribution(HeldAttributionOutcome.AlreadyIdentified, 0, true, "860000000000209")
        };

        var result = await Handler(ctx, ingest).Handle(
            new AttributeHeldConnectionCommand("203.0.113.11:3071", 7), CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Message.Should().Contain("860000000000209");
    }

    private static AttributeHeldConnectionCommandHandler Handler(TestGisDbContext ctx, FakeIngest ingest)
    {
        var tenant = new Mock<ICurrentTenantService>();
        tenant.SetupGet(t => t.UserEmail).Returns("admin@exemple.test");
        tenant.SetupGet(t => t.UserId).Returns(1);
        return new AttributeHeldConnectionCommandHandler(ctx, ingest, tenant.Object,
            NullLogger<AttributeHeldConnectionCommandHandler>.Instance);
    }

    private static void Seed(TestGisDbContext ctx, int id, string plate)
    {
        ctx.GpsDevices.Add(new GpsDevice
        {
            Id = id,
            DeviceUid = $"86000000{id:D7}",
            Mat = $"NR08G{id:D4}",
            Label = plate,
            Status = "active",
            CompanyId = 20,
            ProtocolType = "gps_type_1",
            Brand = "NEMS",
            Model = "S",
        });
        ctx.Vehicles.Add(new Vehicle
        {
            Id = id,
            Name = plate,
            Plate = plate,
            Type = "camion",
            Status = "available",
            CompanyId = 20,
            HasGps = true,
            GpsDeviceId = id
        });
    }

    private sealed class FakeIngest : IGpsIngestConnexions
    {
        public IReadOnlyList<HeldConnection>? Liste { get; set; } = new List<HeldConnection>();
        public HeldConnectionAttribution Reponse { get; set; } = new(HeldAttributionOutcome.Done, 0, true, null);
        public List<(string Peer, string Imei, string Par)> Appels { get; } = new();

        public Task<IReadOnlyList<HeldConnection>?> ListAsync(CancellationToken ct = default) => Task.FromResult(Liste);

        public Task<HeldConnectionAttribution> AttributeAsync(string peer, string imei, string by, CancellationToken ct = default)
        {
            Appels.Add((peer, imei, by));
            return Task.FromResult(Reponse);
        }
    }
}
