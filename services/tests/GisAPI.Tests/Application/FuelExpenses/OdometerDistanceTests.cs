using FluentAssertions;
using GisAPI.Application.Common;
using Xunit;

namespace GisAPI.Tests.Application.FuelExpenses;

/// <summary>
/// Distance d'après les relevés compteur (helper partagé entre « Carburant réel »
/// et les rapports de coûts). Séries tirées de la recette client du 04/09/2026
/// (société 14 : relevé isolé aberrant, deux imports incompatibles).
/// </summary>
public class OdometerDistanceTests
{
    private static DateTime D(int day, int month = 8, int year = 2026) =>
        new(year, month, day, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Isolated_aberrant_reading_is_ignored_but_the_interval_around_it_is_kept()
    {
        // 46 845 → 145 200 (faute de frappe) → 47 455 : les voisins sont cohérents
        // entre eux (610 km), le relevé du milieu s'écarte des deux.
        var result = OdometerDistance.Compute(new[]
        {
            (46_845L, D(10)),
            (145_200L, D(20)),
            (47_455L, D(28)),
        });

        result.IgnoredReadings.Should().Be(1);
        result.KeptReadings.Should().Be(2);
        result.Breaks.Should().Be(0);
        result.DistanceKm.Should().Be(610m);
        result.Measurable.Should().BeTrue();
        result.Reliable.Should().BeFalse("un relevé a dû être écarté");
        result.MonthlyKm.Should().ContainKey((2026, 8)).WhoseValue.Should().Be(610m);
    }

    [Fact]
    public void Incompatible_series_produce_a_break_and_the_two_segments_are_summed()
    {
        // Import de février (97 450 → 98 300) puis import de juin (42 380 → … → 48 005,
        // par pleins successifs ≤ 3 000 km) : 850 + 5 625 = 6 475 km, une rupture
        // (98 300 → 42 380), rien d'ignoré.
        var result = OdometerDistance.Compute(new[]
        {
            (97_450L, D(3, 2)),
            (98_300L, D(20, 2)),
            (42_380L, D(5, 6)),
            (45_000L, D(15, 6)),
            (47_500L, D(25, 6)),
            (48_005L, D(30, 6)),
        });

        result.DistanceKm.Should().Be(6_475m);
        result.Breaks.Should().Be(1);
        result.IgnoredReadings.Should().Be(0);
        result.KeptReadings.Should().Be(6);
        result.Reliable.Should().BeFalse("la série comporte une rupture");
        result.Measurable.Should().BeTrue();
        result.MonthlyKm[(2026, 2)].Should().Be(850m);
        result.MonthlyKm[(2026, 6)].Should().Be(5_625m);
    }

    [Fact]
    public void Empty_series_is_not_measurable()
    {
        var result = OdometerDistance.Compute(Array.Empty<(long, DateTime)>());

        result.DistanceKm.Should().Be(0m);
        result.KeptReadings.Should().Be(0);
        result.Measurable.Should().BeFalse();
        result.Reliable.Should().BeFalse();
        result.MonthlyKm.Should().BeEmpty();
    }

    [Fact]
    public void Single_reading_is_not_measurable()
    {
        var result = OdometerDistance.Compute(new[] { (50_000L, D(1)) });

        result.KeptReadings.Should().Be(1);
        result.DistanceKm.Should().Be(0m);
        result.Measurable.Should().BeFalse();
        result.Reliable.Should().BeFalse("il faut au moins deux relevés");
    }

    [Fact]
    public void Readings_at_or_below_zero_are_dropped_and_order_follows_dates_not_input()
    {
        var result = OdometerDistance.Compute(new[]
        {
            (1_500L, D(15)),
            (0L, D(2)),
            (-5L, D(3)),
            (1_000L, D(5)),
        });

        result.KeptReadings.Should().Be(2);
        result.DistanceKm.Should().Be(500m);
        result.Reliable.Should().BeTrue();
    }

    [Fact]
    public void Each_coherent_gap_is_attributed_to_the_month_of_the_downstream_reading()
    {
        var result = OdometerDistance.Compute(new[]
        {
            (1_000L, D(31, 1)),
            (1_500L, D(2, 2)),
            (2_000L, D(20, 2)),
            (2_600L, D(1, 3)),
        });

        result.DistanceKm.Should().Be(1_600m);
        result.MonthlyKm.Should().NotContainKey((2026, 1));
        result.MonthlyKm[(2026, 2)].Should().Be(1_000m);
        result.MonthlyKm[(2026, 3)].Should().Be(600m);
        result.Reliable.Should().BeTrue();
    }

    [Fact]
    public void Gap_exactly_at_the_ceiling_is_accepted_one_km_more_is_a_break()
    {
        var accepted = OdometerDistance.Compute(new[] { (10_000L, D(1)), (13_000L, D(20)) });
        accepted.DistanceKm.Should().Be(OdometerDistance.MaxSegmentKm);
        accepted.Breaks.Should().Be(0);

        var broken = OdometerDistance.Compute(new[] { (10_000L, D(1)), (13_001L, D(20)) });
        broken.DistanceKm.Should().Be(0m);
        broken.Breaks.Should().Be(1);
        broken.Measurable.Should().BeFalse();
    }

    [Fact]
    public void Two_coherent_neighbours_do_not_get_a_reading_ignored_when_it_fits_between_them()
    {
        // 40 686 → 42 380 (1 694 km ≤ 3 000) : accepté, pas d'aberration.
        var result = OdometerDistance.Compute(new[]
        {
            (39_771L, D(1, 6)),
            (40_686L, D(10, 6)),
            (42_380L, D(25, 6)),
            (43_100L, D(2, 7)),
        });

        result.IgnoredReadings.Should().Be(0);
        result.Breaks.Should().Be(0);
        result.DistanceKm.Should().Be(3_329m);
        result.Reliable.Should().BeTrue();
    }
}
