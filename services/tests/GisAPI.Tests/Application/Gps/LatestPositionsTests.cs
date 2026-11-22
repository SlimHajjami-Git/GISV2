using FluentAssertions;
using GisAPI.Application.Common;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.Gps;

/// <summary>
/// Dernière trame par boîtier (application mobile « horriblement lente », 22/11/2026) :
/// la requête de production est un LATERAL (une lecture d'index par boîtier) ; ces tests
/// fixent la RÈGLE, que le repli hors PostgreSQL applique à l'identique.
/// </summary>
public class LatestPositionsTests
{
    private static readonly DateTime T0 = new(2026, 11, 22, 8, 0, 0, DateTimeKind.Utc);

    private static GpsPosition Trame(long id, int device, DateTime at, long? odometre = null) => new()
    {
        Id = id, DeviceId = device, RecordedAt = at, Latitude = 36.8, Longitude = 10.18,
        SpeedKph = 40, IgnitionOn = true, OdometerKm = odometre, CreatedAt = at
    };

    [Fact]
    public async Task La_derniere_trame_est_la_plus_recente_pas_le_plus_grand_id()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.GpsPositions.AddRange(
            Trame(1, 10, T0),
            Trame(2, 10, T0.AddMinutes(5)),
            // Trame stockée par le boîtier et renvoyée après coup : id plus grand, heure plus ancienne.
            Trame(3, 10, T0.AddMinutes(-30)),
            Trame(4, 20, T0.AddMinutes(1)));
        await ctx.SaveChangesAsync();

        var dernieres = await LatestPositions.LoadAsync(ctx, new[] { 10, 20, 30 }, CancellationToken.None);

        dernieres.Keys.Should().BeEquivalentTo(new[] { 10, 20 }, "un boîtier sans trame n'apparaît pas");
        dernieres[10].Id.Should().Be(2);
        dernieres[20].Id.Should().Be(4);
    }

    [Fact]
    public async Task Aucun_boitier_aucune_requete()
    {
        using var ctx = TestDbContextFactory.Create();

        (await LatestPositions.LoadAsync(ctx, Array.Empty<int>(), CancellationToken.None)).Should().BeEmpty();
    }
}
