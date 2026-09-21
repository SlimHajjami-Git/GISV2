using FluentAssertions;
using GisAPI.Domain.Entities;
using GisAPI.Services;
using Xunit;

namespace GisAPI.Tests.Application.Tours;

/// <summary>
/// Règles communes de <see cref="TourPlanning"/> (18/09/2026) : estimations
/// par étape, décalage d'un départ en retard, état des étapes. Le démarrage
/// automatique de TourMonitoringService passe par <see cref="TourPlanning.Start"/> :
/// ces tests couvrent donc aussi ce chemin.
/// </summary>
public class TourPlanningTests
{
    private static readonly DateTime Start = new(2030, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    private static TourPlanning.StopInput Stop(double lat, double lon, int pause = 0) => new(lat, lon, pause);

    // Trois points alignés sur un méridien : le 2e tronçon fait deux fois le 1er.
    private static readonly List<TourPlanning.StopInput> Aligned = new()
    {
        Stop(36.0, 10.0), Stop(36.1, 10.0, pause: 10), Stop(36.3, 10.0)
    };

    // ------------------------------------------------------------ estimations

    [Fact]
    public void Route_with_one_leg_per_stop_gives_cumulative_arrivals_with_pauses()
    {
        var estimates = TourPlanning.EstimateFromRoute(Start, Aligned, new List<double> { 600, 900 }, 1500);

        estimates.Select(e => e.EstimatedArrivalTime).Should().Equal(
            Start, Start.AddMinutes(10), Start.AddMinutes(10 + 10 + 15));
        estimates.Select(e => e.LegMinutes).Should().Equal(0, 10, 15);
    }

    [Fact]
    public void Through_route_with_a_single_leg_is_split_by_distance_and_keeps_every_pause()
    {
        // Les étapes intermédiaires partent en « through » : un seul tronçon
        // global. Avant, l'arrêt prenait toute la durée et les pauses suivantes
        // étaient oubliées (destination à 60 min au lieu de 70).
        var estimates = TourPlanning.EstimateFromRoute(Start, Aligned, new List<double> { 3600 }, 3600);

        estimates[1].EstimatedArrivalTime.Should().BeCloseTo(Start.AddMinutes(20), TimeSpan.FromSeconds(30));
        estimates[2].EstimatedArrivalTime.Should().BeCloseTo(Start.AddMinutes(60 + 10), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Without_route_every_stop_gets_a_straight_line_estimate_with_pauses()
    {
        // Avant le lot 0 : toutes les étapes à l'heure de départ (faux « temps
        // dépassé »). Première version du lot 0 : aucune heure, donc aucune
        // échéance — une étape manquée bloquait la destination pour toujours.
        var estimates = TourPlanning.EstimateFromRoute(Start, Aligned, legTimesSeconds: null, totalTimeSeconds: 0);

        var leg1 = TourPlanning.FallbackLegSeconds(Aligned[0], Aligned[1]);
        var leg2 = TourPlanning.FallbackLegSeconds(Aligned[1], Aligned[2]);
        estimates.Select(e => e.EstimatedArrivalTime).Should().Equal(
            Start, Start.AddSeconds(leg1), Start.AddSeconds(leg1 + leg2).AddMinutes(10));

        // 0,1° de latitude ≈ 11,1 km, × 1,3 à 50 km/h ≈ 17,3 min.
        TimeSpan.FromSeconds(leg1).TotalMinutes.Should().BeApproximately(17.3, 0.2);
        estimates.Select(e => e.LegMinutes).Should().Equal(0, 18, 35);
    }

    [Fact]
    public void A_route_without_duration_falls_back_instead_of_putting_every_stop_at_the_start()
    {
        var estimates = TourPlanning.EstimateFromRoute(Start, Aligned, new List<double>(), totalTimeSeconds: 0);

        estimates[2].EstimatedArrivalTime.Should().Be(
            TourPlanning.EstimateFromRoute(Start, Aligned, null, 0)[2].EstimatedArrivalTime);
        estimates[2].EstimatedArrivalTime.Should().BeAfter(Start.AddMinutes(10));
    }

    [Fact]
    public void Previous_estimates_follow_new_start_and_new_pauses_when_routing_is_down()
    {
        var previous = new List<TourWaypoint>
        {
            new() { SequenceOrder = 0, Type = "origin", Latitude = 36.0, Longitude = 10.0, EstimatedArrivalTime = Start },
            new() { SequenceOrder = 1, Type = "waypoint", Latitude = 36.1, Longitude = 10.0, PlannedPauseMinutes = 10,
                    EstimatedLegMinutes = 12, EstimatedArrivalTime = Start.AddMinutes(12) },
            new() { SequenceOrder = 2, Type = "destination", Latitude = 36.3, Longitude = 10.0,
                    EstimatedLegMinutes = 25, EstimatedArrivalTime = Start.AddMinutes(47) }
        };
        var stops = new List<TourPlanning.StopInput> { Stop(36.0, 10.0), Stop(36.1, 10.0, pause: 20), Stop(36.3, 10.0) };

        var estimates = TourPlanning.EstimateFromPrevious(Start.AddHours(2), stops, previous);

        estimates.Select(e => e.EstimatedArrivalTime).Should().Equal(
            Start.AddHours(2), Start.AddHours(2).AddMinutes(12), Start.AddHours(2).AddMinutes(12 + 20 + 25));
        estimates.Select(e => e.LegMinutes).Should().Equal(0, 12, 25);
    }

    [Fact]
    public void Previous_estimates_of_a_round_trip_are_matched_by_leg_not_by_place()
    {
        // Aller-retour : destination aux coordonnées de l'origine.
        var previous = new List<TourWaypoint>
        {
            new() { SequenceOrder = 0, Type = "origin", Latitude = 36.0, Longitude = 10.0, EstimatedArrivalTime = Start },
            new() { SequenceOrder = 1, Type = "waypoint", Latitude = 36.1, Longitude = 10.0,
                    EstimatedLegMinutes = 15, EstimatedArrivalTime = Start.AddMinutes(15) },
            new() { SequenceOrder = 2, Type = "destination", Latitude = 36.0, Longitude = 10.0,
                    EstimatedLegMinutes = 16, EstimatedArrivalTime = Start.AddMinutes(31) }
        };
        var stops = new List<TourPlanning.StopInput> { Stop(36.0, 10.0), Stop(36.1, 10.0), Stop(36.0, 10.0) };

        var estimates = TourPlanning.EstimateFromPrevious(Start, stops, previous);

        estimates.Select(e => e.EstimatedArrivalTime).Should().Equal(Start, Start.AddMinutes(15), Start.AddMinutes(31));
    }

    [Fact]
    public void A_stop_inserted_during_an_outage_pushes_the_next_stops_by_its_detour_and_its_pause()
    {
        // Relecture du 18/09 : A → C (60 min) devient A → B (pause 30) → C. La
        // première version redonnait à C son ancienne heure (A + 60) : détour
        // et pause de B ignorés, faux « temps dépassé » sur C.
        var a = Stop(36.0, 10.0);
        var b = Stop(36.0, 10.5, pause: 30);   // à l'écart de la ligne A–C
        var c = Stop(36.3, 10.0);
        var previous = new List<TourWaypoint>
        {
            new() { SequenceOrder = 0, Type = "origin", Latitude = a.Latitude, Longitude = a.Longitude, EstimatedArrivalTime = Start },
            new() { SequenceOrder = 1, Type = "destination", Latitude = c.Latitude, Longitude = c.Longitude,
                    EstimatedLegMinutes = 60, EstimatedArrivalTime = Start.AddMinutes(60) }
        };

        var estimates = TourPlanning.EstimateFromPrevious(Start, new List<TourPlanning.StopInput> { a, b, c }, previous);

        var toB = TourPlanning.FallbackLegSeconds(a, b);
        var toC = TourPlanning.FallbackLegSeconds(b, c);
        estimates[1].EstimatedArrivalTime.Should().Be(Start.AddSeconds(toB));
        estimates[2].EstimatedArrivalTime.Should().Be(Start.AddSeconds(toB + toC).AddMinutes(30));
        estimates[2].EstimatedArrivalTime.Should().BeAfter(Start.AddMinutes(60 + 30),
            "le détour par B et sa pause s'ajoutent au trajet");
    }

    [Fact]
    public void Unchanged_legs_keep_their_duration_when_a_stop_is_appended()
    {
        var previous = new List<TourWaypoint>
        {
            new() { SequenceOrder = 0, Type = "origin", Latitude = 36.0, Longitude = 10.0, EstimatedArrivalTime = Start },
            new() { SequenceOrder = 1, Type = "destination", Latitude = 36.1, Longitude = 10.0,
                    EstimatedLegMinutes = 12, EstimatedArrivalTime = Start.AddMinutes(12) }
        };
        var stops = new List<TourPlanning.StopInput> { Stop(36.0, 10.0), Stop(36.1, 10.0, pause: 5), Stop(36.3, 10.0) };

        var estimates = TourPlanning.EstimateFromPrevious(Start, stops, previous);

        estimates[1].EstimatedArrivalTime.Should().Be(Start.AddMinutes(12));
        estimates[2].EstimatedArrivalTime.Should().BeCloseTo(
            Start.AddMinutes(12 + 5).AddSeconds(TourPlanning.FallbackLegSeconds(stops[1], stops[2])), TimeSpan.FromMilliseconds(1));
    }

    // ------------------------------------------------------------ suivi

    [Fact]
    public void Vehicle_must_still_belong_to_the_company_of_the_tour()
    {
        TourPlanning.VehicleBelongsToTourCompany(new Tour { CompanyId = 1, Vehicle = new Vehicle { CompanyId = 1 } })
            .Should().BeTrue();
        TourPlanning.VehicleBelongsToTourCompany(new Tour { CompanyId = 1, Vehicle = new Vehicle { CompanyId = 2 } })
            .Should().BeFalse("véhicule transféré à une autre société : sa position n'est plus lue");
        TourPlanning.VehicleBelongsToTourCompany(new Tour { CompanyId = 1, Vehicle = null })
            .Should().BeFalse();
    }

    [Fact]
    public void A_pending_stop_is_overdue_only_after_its_deadline_and_then_no_longer_blocks_the_destination()
    {
        var stop = new TourWaypoint { SequenceOrder = 1, WaypointStatus = "pending", EstimatedArrivalTime = Start, DeadlineMarginMinutes = 30 };
        var destination = new TourWaypoint { SequenceOrder = 2, Type = "destination", WaypointStatus = "pending" };
        var waypoints = new[] { stop, destination };

        TourPlanning.IsOverdue(stop, Start.AddMinutes(30)).Should().BeFalse("l'échéance pile n'est pas dépassée");
        TourPlanning.IsOverdue(stop, Start.AddMinutes(31)).Should().BeTrue();
        TourPlanning.HasPendingStopBefore(waypoints, destination).Should().BeTrue();

        stop.WaypointStatus = "temps_depasse";
        TourPlanning.IsOverdue(stop, Start.AddMinutes(90)).Should().BeFalse("déjà signalée, pas de seconde alerte");
        TourPlanning.HasPendingStopBefore(waypoints, destination).Should().BeFalse();

        TourPlanning.IsOverdue(new TourWaypoint { WaypointStatus = "pending" }, Start.AddYears(1))
            .Should().BeFalse("sans heure prévue, pas d'échéance");
    }

    // ------------------------------------------------------------ démarrage

    private static Tour PlannedTour(DateTime scheduled) => new()
    {
        Status = "planned",
        ScheduledStartTime = scheduled,
        Waypoints = new List<TourWaypoint>
        {
            new() { SequenceOrder = 0, Type = "origin", EstimatedArrivalTime = scheduled },
            new() { SequenceOrder = 1, Type = "waypoint", EstimatedArrivalTime = scheduled.AddMinutes(40) },
            new() { SequenceOrder = 2, Type = "destination", EstimatedArrivalTime = scheduled.AddMinutes(70) }
        }
    };

    [Fact]
    public void Start_after_the_threshold_shifts_estimates_and_marks_the_origin_coherently()
    {
        var tour = PlannedTour(Start);
        var now = Start.AddMinutes(25);

        var shift = TourPlanning.Start(tour, now);

        shift.Should().Be(TimeSpan.FromMinutes(25));
        tour.Status.Should().Be("in_progress");
        tour.ActualStartTime.Should().Be(now);
        var wps = tour.Waypoints.OrderBy(w => w.SequenceOrder).ToList();
        wps.Select(w => w.EstimatedArrivalTime).Should().Equal(now, Start.AddMinutes(65), Start.AddMinutes(95));
        wps[0].IsCompleted.Should().BeTrue();
        wps[0].WaypointStatus.Should().Be("completed");
        wps[0].ActualArrivalTime.Should().Be(now);
    }

    [Fact]
    public void Start_within_the_threshold_does_not_shift()
    {
        var tour = PlannedTour(Start);

        TourPlanning.Start(tour, Start.AddMinutes(2)).Should().Be(TimeSpan.Zero, "2 min pile n'est pas un retard");
        tour.Waypoints.Single(w => w.SequenceOrder == 1).EstimatedArrivalTime.Should().Be(Start.AddMinutes(40));
    }

    [Fact]
    public void Shift_leaves_reached_stops_untouched()
    {
        var reached = new TourWaypoint { IsCompleted = true, WaypointStatus = "completed", EstimatedArrivalTime = Start };
        var pending = new TourWaypoint { EstimatedArrivalTime = Start.AddMinutes(30) };

        TourPlanning.ShiftPendingEstimates(new[] { reached, pending }, TimeSpan.FromMinutes(10)).Should().BeTrue();

        reached.EstimatedArrivalTime.Should().Be(Start);
        pending.EstimatedArrivalTime.Should().Be(Start.AddMinutes(40));
    }

    // ------------------------------------------------------------ clôture

    [Fact]
    public void Closing_a_tour_reaches_the_destination_skips_pending_stops_and_keeps_overdue_ones()
    {
        var tour = new Tour
        {
            Waypoints = new List<TourWaypoint>
            {
                // Donnée antérieure au 18/09 : cochée mais restée « pending ».
                new() { SequenceOrder = 0, Type = "origin", IsCompleted = true, WaypointStatus = "pending" },
                new() { SequenceOrder = 1, Type = "waypoint", WaypointStatus = "pending" },
                new() { SequenceOrder = 2, Type = "waypoint", WaypointStatus = "temps_depasse" },
                new() { SequenceOrder = 3, Type = "destination", WaypointStatus = "pending" }
            }
        };
        var now = Start.AddHours(3);

        TourPlanning.CloseWaypointsOnCompletion(tour, now);

        var wps = tour.Waypoints.OrderBy(w => w.SequenceOrder).ToList();
        wps.Select(w => w.WaypointStatus).Should().Equal("completed", "skipped", "temps_depasse", "completed");
        wps.Select(w => w.IsCompleted).Should().Equal(true, false, false, true);
        wps[3].ActualArrivalTime.Should().Be(now);
    }

    [Fact]
    public void Closing_keeps_the_detected_arrival_of_an_already_reached_destination()
    {
        var detected = Start.AddMinutes(50);
        var tour = new Tour
        {
            Waypoints = new List<TourWaypoint>
            {
                new() { SequenceOrder = 0, Type = "origin", IsCompleted = true, WaypointStatus = "completed" },
                new() { SequenceOrder = 1, Type = "destination", IsCompleted = true, WaypointStatus = "completed", ActualArrivalTime = detected }
            }
        };

        TourPlanning.CloseWaypointsOnCompletion(tour, Start.AddHours(3));

        tour.Waypoints.Last().ActualArrivalTime.Should().Be(detected);
    }
}
