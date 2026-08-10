using GisAPI.Domain.Entities;
using GisAPI.Infrastructure.Services;
using GisAPI.Tests.Common;

namespace GisAPI.Tests.Application.FuelExpenses;

/// <summary>
/// Tests du calcul de consommation par segments de X km.
/// Scénarios tirés de l'enquête carburant réelle (Scania 284, août 2026) :
/// consommation nominale, plein en route, falaise capteur à l'arrêt,
/// jauge figée.
/// </summary>
public class ConsumptionSegmentsTests
{
    private static Vehicle Truck() => new()
    {
        Id = 284,
        Name = "Scania Test",
        Type = "camion",
        FuelTankCapacity = 540,   // 1 pt = 5,4 L (conversion nominale, pas de factures seedées)
        GpsDeviceId = 1,
        FuelType = "diesel",
        CompanyId = 1
    };

    private static void Seed(TestGisDbContext db, DateTime t, int fuel, long odo, double speed = 60)
    {
        db.GpsPositions.Add(new GpsPosition
        {
            DeviceId = 1,
            RecordedAt = t,
            Latitude = 36.8,
            Longitude = 10.2,
            SpeedKph = speed,
            IgnitionOn = speed > 0,
            FuelRaw = fuel,
            OdometerKm = odo,
            IsValid = true,
            IsRealTime = true,
            CreatedAt = t
        });
    }

    private static readonly DateTime T0 = new(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task NominalDecline_SplitsIntoExactSegments_WithMinMax()
    {
        using var db = TestDbContextFactory.Create();
        // 600 km à 1 km/trame, 1 pt de jauge tous les 12 km → 45 L/100 en moyenne
        for (int i = 0; i < 600; i++)
            Seed(db, T0.AddMinutes(i), 95 - i / 12, 100000 + i);
        await db.SaveChangesAsync();

        var service = new FuelCalculationService(db);
        var report = await service.GetConsumptionSegmentsAsync(Truck(), T0.AddHours(-1), T0.AddDays(1), 100);

        Assert.NotNull(report);
        Assert.True(report!.HasSensor);
        // 600 trames consécutives = 599 km d'odomètre → 5 segments pleins + 1 partiel de 99 km
        Assert.Equal(6, report.Segments.Count);
        Assert.All(report.Segments, s => Assert.True(s.IsReliable));
        Assert.All(report.Segments.Take(5), s => Assert.Equal(100, s.DistanceKm, 0));
        Assert.Equal(99, report.Segments[^1].DistanceKm, 0);
        Assert.NotNull(report.Summary.AvgLPer100Km);
        Assert.InRange(report.Summary.AvgLPer100Km!.Value, 43m, 47m);
        Assert.NotNull(report.Summary.MinLPer100Km);
        Assert.NotNull(report.Summary.MaxLPer100Km);
        Assert.True(report.Summary.MinLPer100Km <= report.Summary.MaxLPer100Km);
        Assert.Equal(599, report.Summary.TotalKm, 0);
        Assert.Equal(0, report.Summary.ExcludedSegments);
    }

    [Fact]
    public async Task RefuelJump_IsNotCountedAsConsumption()
    {
        using var db = TestDbContextFactory.Create();
        // 100 km en baisse 30→20, plein (+60 pts), puis 100 km en baisse 80→70
        for (int i = 0; i < 100; i++)
            Seed(db, T0.AddMinutes(i), 30 - i / 10, 100000 + i);
        for (int i = 100; i < 200; i++)
            Seed(db, T0.AddMinutes(i), 80 - (i - 100) / 10, 100000 + i);
        await db.SaveChangesAsync();

        var service = new FuelCalculationService(db);
        var report = await service.GetConsumptionSegmentsAsync(Truck(), T0.AddHours(-1), T0.AddDays(1), 100);

        Assert.NotNull(report);
        Assert.Equal(2, report!.Segments.Count);
        // Chaque segment ≈ 10 pts × 5,4 L = 54 L/100 — le saut de +60 pts ne doit PAS apparaître
        Assert.All(report.Segments, s => Assert.InRange(s.LPer100Km, 45m, 60m));
        Assert.All(report.Segments, s => Assert.True(s.IsReliable));
    }

    [Fact]
    public async Task SensorCliffWhileParked_MarksSegmentUnreliable()
    {
        using var db = TestDbContextFactory.Create();
        // Réplique du cas Scania 04/08 : baisse normale, puis chute de 30 pts
        // en UNE trame à l'arrêt au milieu du 2e segment.
        int fuel = 60;
        for (int i = 0; i < 300; i++)
        {
            if (i > 0 && i % 12 == 0) fuel -= 1;
            if (i == 150)
            {
                fuel -= 30;                                    // falaise
                Seed(db, T0.AddMinutes(i), fuel, 100000 + 149, speed: 0);
                continue;
            }
            Seed(db, T0.AddMinutes(i), fuel, 100000 + (i > 150 ? i - 1 : i));
        }
        await db.SaveChangesAsync();

        var service = new FuelCalculationService(db);
        var report = await service.GetConsumptionSegmentsAsync(Truck(), T0.AddHours(-1), T0.AddDays(1), 100);

        Assert.NotNull(report);
        var unreliable = report!.Segments.Where(s => !s.IsReliable).ToList();
        Assert.Single(unreliable);
        Assert.Equal("chute capteur suspecte", unreliable[0].ExclusionReason);
        // Les min/max ne doivent venir QUE des segments fiables
        Assert.True(report.Summary.MaxLPer100Km < 60m);
        Assert.Equal(1, report.Summary.ExcludedSegments);
    }

    [Fact]
    public async Task StuckGauge_IsExcludedFromStats()
    {
        using var db = TestDbContextFactory.Create();
        // ~159 km parcourus, jauge strictement immobile (flotteur coincé)
        for (int i = 0; i < 160; i++)
            Seed(db, T0.AddMinutes(i), 20, 100000 + i);
        await db.SaveChangesAsync();

        var service = new FuelCalculationService(db);
        var report = await service.GetConsumptionSegmentsAsync(Truck(), T0.AddHours(-1), T0.AddDays(1), 100);

        Assert.NotNull(report);
        // 1 segment plein (100 km) + 1 partiel (59 km ≥ moitié de X)
        Assert.Equal(2, report!.Segments.Count);
        Assert.All(report.Segments, s =>
        {
            Assert.False(s.IsReliable);
            Assert.Equal("jauge figee (aucune baisse)", s.ExclusionReason);
        });
        Assert.Equal(0, report.Summary.ReliableSegments);
        Assert.Null(report.Summary.AvgLPer100Km);
    }

    [Fact]
    public async Task NoFuelData_ReturnsHasSensorFalse()
    {
        using var db = TestDbContextFactory.Create();
        var service = new FuelCalculationService(db);

        var report = await service.GetConsumptionSegmentsAsync(Truck(), T0, T0.AddDays(1), 100);

        Assert.NotNull(report);
        Assert.False(report!.HasSensor);
        Assert.Empty(report.Segments);
    }
}
