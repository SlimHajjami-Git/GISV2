using FluentAssertions;
using GisAPI.Application.Features.Gps.Commands.BroadcastPosition;
using Xunit;

namespace GisAPI.Tests.Application.Geofences;

/// <summary>
/// Le planning jour/horaire des géofences est évalué en heure LOCALE de la
/// société alors que les positions GPS arrivent en UTC. Ces tests couvrent le
/// filtre <see cref="BroadcastPositionCommandHandler.IsScheduleActive"/> :
/// jours (format UI "monday" et legacy "Mon"), bascule de jour via le fuseau,
/// fenêtre horaire simple et fenêtre nocturne (22:00 → 06:00).
/// </summary>
public class GeofenceScheduleTests
{
    // TN/DZ : UTC+1 sans heure d'été — fuseau custom pour ne pas dépendre de tzdata.
    private static readonly TimeZoneInfo Utc1 =
        TimeZoneInfo.CreateCustomTimeZone("UTC+1", TimeSpan.FromHours(1), "UTC+1", "UTC+1");

    private static GeofenceCacheEntry Zone(string[]? days = null, TimeSpan? start = null, TimeSpan? end = null) =>
        new(1, "Zone", "circle", null, 36.8, 10.18, 500, true, true, null, days, start, end);

    // 2026-07-06 = lundi. 10:00 UTC → 11:00 locale (UTC+1).
    private static readonly DateTime MondayMorningUtc = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    // 2026-07-05 = dimanche 23:30 UTC → LUNDI 00:30 locale.
    private static readonly DateTime SundayLateUtc = new(2026, 7, 5, 23, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void No_schedule_means_always_active()
    {
        BroadcastPositionCommandHandler.IsScheduleActive(Zone(), MondayMorningUtc, Utc1).Should().BeTrue();
        BroadcastPositionCommandHandler.IsScheduleActive(Zone(days: Array.Empty<string>()), MondayMorningUtc, Utc1).Should().BeTrue();
    }

    [Fact]
    public void Day_filter_matches_local_day_in_both_ui_and_legacy_formats()
    {
        var mondayOnly = Zone(days: new[] { "monday" });
        BroadcastPositionCommandHandler.IsScheduleActive(mondayOnly, MondayMorningUtc, Utc1).Should().BeTrue();

        var legacyAbbrev = Zone(days: new[] { "Mon" });
        BroadcastPositionCommandHandler.IsScheduleActive(legacyAbbrev, MondayMorningUtc, Utc1).Should().BeTrue();

        var sundayOnly = Zone(days: new[] { "sunday" });
        BroadcastPositionCommandHandler.IsScheduleActive(sundayOnly, MondayMorningUtc, Utc1).Should().BeFalse();
    }

    [Fact]
    public void Day_is_evaluated_in_company_local_time_not_utc()
    {
        // Dimanche 23:30 UTC = lundi 00:30 heure locale → une zone "monday" est active.
        var mondayOnly = Zone(days: new[] { "monday" });
        BroadcastPositionCommandHandler.IsScheduleActive(mondayOnly, SundayLateUtc, Utc1).Should().BeTrue();

        var sundayOnly = Zone(days: new[] { "sunday" });
        BroadcastPositionCommandHandler.IsScheduleActive(sundayOnly, SundayLateUtc, Utc1).Should().BeFalse();
    }

    [Fact]
    public void Time_window_bounds_are_local()
    {
        // Fenêtre 08:00-17:00 locale. 10:00 UTC = 11:00 locale → active.
        var zone = Zone(start: new TimeSpan(8, 0, 0), end: new TimeSpan(17, 0, 0));
        BroadcastPositionCommandHandler.IsScheduleActive(zone, MondayMorningUtc, Utc1).Should().BeTrue();

        // 06:30 UTC = 07:30 locale → avant la fenêtre.
        var early = new DateTime(2026, 7, 6, 6, 30, 0, DateTimeKind.Utc);
        BroadcastPositionCommandHandler.IsScheduleActive(zone, early, Utc1).Should().BeFalse();

        // 16:30 UTC = 17:30 locale → après la fenêtre.
        var late = new DateTime(2026, 7, 6, 16, 30, 0, DateTimeKind.Utc);
        BroadcastPositionCommandHandler.IsScheduleActive(zone, late, Utc1).Should().BeFalse();
    }

    [Fact]
    public void Overnight_window_spans_midnight()
    {
        // 22:00 → 06:00 locale (surveillance de nuit).
        var night = Zone(start: new TimeSpan(22, 0, 0), end: new TimeSpan(6, 0, 0));

        // 22:30 UTC = 23:30 locale → active.
        BroadcastPositionCommandHandler.IsScheduleActive(
            night, new DateTime(2026, 7, 6, 22, 30, 0, DateTimeKind.Utc), Utc1).Should().BeTrue();

        // 02:00 UTC = 03:00 locale → active (après minuit).
        BroadcastPositionCommandHandler.IsScheduleActive(
            night, new DateTime(2026, 7, 6, 2, 0, 0, DateTimeKind.Utc), Utc1).Should().BeTrue();

        // 11:00 UTC = 12:00 locale → inactive (pleine journée).
        BroadcastPositionCommandHandler.IsScheduleActive(
            night, new DateTime(2026, 7, 6, 11, 0, 0, DateTimeKind.Utc), Utc1).Should().BeFalse();
    }

    [Fact]
    public void Days_and_window_combine()
    {
        var zone = Zone(days: new[] { "monday" }, start: new TimeSpan(8, 0, 0), end: new TimeSpan(17, 0, 0));

        // Lundi 11:00 locale → active.
        BroadcastPositionCommandHandler.IsScheduleActive(zone, MondayMorningUtc, Utc1).Should().BeTrue();

        // Mardi 11:00 locale → mauvais jour.
        var tuesday = new DateTime(2026, 7, 7, 10, 0, 0, DateTimeKind.Utc);
        BroadcastPositionCommandHandler.IsScheduleActive(zone, tuesday, Utc1).Should().BeFalse();

        // Lundi 19:00 locale → bon jour, hors fenêtre.
        var evening = new DateTime(2026, 7, 6, 18, 0, 0, DateTimeKind.Utc);
        BroadcastPositionCommandHandler.IsScheduleActive(zone, evening, Utc1).Should().BeFalse();
    }
}
