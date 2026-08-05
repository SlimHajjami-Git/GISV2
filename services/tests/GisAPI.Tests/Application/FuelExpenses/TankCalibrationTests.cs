using FluentAssertions;
using GisAPI.Application.Common.FuelCalibration;
using GisAPI.Domain.Entities;
using GisAPI.Infrastructure.Services;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.FuelExpenses;

/// <summary>
/// L'étalonnage points-de-jauge → litres : la réponse au cas Scania 001 (438 L
/// à la pompe affichés « 390 L détectés » parce que la conversion supposait un
/// réservoir de 500 L linéaire). Règle d'or testée partout : sans assez de
/// points cohérents, on garde le nominal et une fourchette large — jamais de
/// fausse précision.
/// </summary>
public class TankCalibrationTests
{
    private static TankCalibrationPoint Pt(decimal liters, int deltaPts) =>
        new(DateTime.UtcNow, liters, deltaPts);

    // ── Fit ──

    [Fact]
    public void Fit_WithEnoughCoherentPoints_LearnsRealCapacity()
    {
        // Cas Scania : fiche 500 L, mais chaque plein confirme ~5,4 L/point.
        var points = new List<TankCalibrationPoint>
        {
            Pt(421m, 78), Pt(437m, 81), Pt(324m, 60), Pt(270m, 50), Pt(216m, 40)
        };

        var result = TankCalibrationResult.Fit(points, nominalCapacity: 500);

        result.IsCalibrated.Should().BeTrue();
        result.LitersPerPoint.Should().BeApproximately(5.4m, 0.05m);
        result.EffectiveTankLiters.Should().BeInRange(535, 545);
        result.PointCount.Should().Be(5);
    }

    [Fact]
    public void Fit_WithOnlyThreePoints_KeepsNominalConversion()
    {
        // Le Scania réel n'a que 3 pleins saisis : pas assez pour faire foi.
        var points = new List<TankCalibrationPoint> { Pt(438m, 78), Pt(423m, 81), Pt(290m, 67) };

        var result = TankCalibrationResult.Fit(points, nominalCapacity: 500);

        result.IsCalibrated.Should().BeFalse();
        result.LitersPerPoint.Should().Be(5.0m); // nominal 500/100 — pas la médiane
        result.EffectiveTankLiters.Should().BeNull();
        result.PointCount.Should().Be(3);
    }

    [Fact]
    public void Fit_RejectsOutlierPair_ThenCalibratesOnTheRest()
    {
        // 4 pleins cohérents + 1 rapprochement raté (facture fractionnée) à 12 L/pt.
        var points = new List<TankCalibrationPoint>
        {
            Pt(421m, 78), Pt(437m, 81), Pt(324m, 60), Pt(270m, 50), Pt(600m, 50)
        };

        var result = TankCalibrationResult.Fit(points, nominalCapacity: 500);

        result.IsCalibrated.Should().BeTrue();
        result.PointCount.Should().Be(4);
        result.LitersPerPoint.Should().BeApproximately(5.4m, 0.1m);
    }

    [Fact]
    public void Fit_HighDispersion_RefusesToCalibrate()
    {
        // Jauge incohérente : 4 points mais des rapports partout (4 à 8 L/pt).
        var points = new List<TankCalibrationPoint>
        {
            Pt(200m, 50), Pt(250m, 50), Pt(325m, 50), Pt(400m, 50)
        };

        var result = TankCalibrationResult.Fit(points, nominalCapacity: 500);

        result.IsCalibrated.Should().BeFalse();
        result.LitersPerPoint.Should().Be(5.0m);
    }

    [Fact]
    public void Fit_IgnoresTinyRisesAndEmptyInvoices()
    {
        // Bond < 15 pts = ballottement ; facture 0 L = saisie sans volume.
        var points = new List<TankCalibrationPoint>
        {
            Pt(50m, 10), Pt(0m, 60), Pt(421m, 78)
        };

        var result = TankCalibrationResult.Fit(points, nominalCapacity: 500);

        result.PointCount.Should().Be(1);
        result.IsCalibrated.Should().BeFalse();
    }

    [Fact]
    public void Fit_RejectsPhysicallyImpossibleRatios()
    {
        // 4 paires à 20 L/pt sur un réservoir nominal 500 L (5 L/pt) : ce ne sont
        // pas des pleins, c'est un mauvais rapprochement systématique.
        var points = Enumerable.Range(0, 4).Select(_ => Pt(1000m, 50)).ToList();

        var result = TankCalibrationResult.Fit(points, nominalCapacity: 500);

        result.IsCalibrated.Should().BeFalse();
        result.PointCount.Should().Be(0);
    }

    // ── Conversion et fourchette ──

    [Fact]
    public void Uncalibrated_RangeCoversTankGeometryUncertainty()
    {
        // Sans étalonnage : ±(1 point + 10 % par point) — la variation de forme
        // mesurée sur le terrain. 78 pts sur 500 L : centre 390, ± ~44 L.
        var calib = TankCalibrationResult.Uncalibrated(500);

        calib.ConvertToLiters(78).Should().Be(390m);
        var (low, high) = calib.RangeFor(78);
        low.Should().BeApproximately(346m, 1m);
        high.Should().BeApproximately(434m, 1m);
    }

    [Fact]
    public void Calibrated_RangeIsTighterThanNominal()
    {
        var points = new List<TankCalibrationPoint>
        {
            Pt(421m, 78), Pt(437m, 81), Pt(324m, 60), Pt(270m, 50), Pt(216m, 40)
        };
        var calib = TankCalibrationResult.Fit(points, 500);

        var (low, high) = calib.RangeFor(78);
        // Le plein Scania de 438 L doit tomber DANS la fourchette étalonnée :
        // c'est ce qui met fin à l'accusation implicite du chauffeur.
        low.Should().BeLessThan(438m);
        high.Should().BeGreaterThan(421m);
        (high - low).Should().BeLessThan(434m - 346m); // plus étroite que le nominal
    }
}

/// <summary>
/// Le flux réel : détection des montées de jauge (avec points bruts), fusion des
/// paliers d'un même plein, et apprentissage depuis les factures en base.
/// </summary>
public class TankCalibrationFlowTests
{
    private const int CompanyId = 1;

    private static (TestGisDbContext ctx, Vehicle vehicle) Setup(int tankCapacity = 100)
    {
        var ctx = TestDbContextFactory.Create();
        var device = new GpsDevice { Id = 1, DeviceUid = "TEST-DEV", CompanyId = CompanyId, FuelSensorMode = "percent" };
        var vehicle = new Vehicle
        {
            Id = 1, Name = "Test", Plate = "1 TU 1", CompanyId = CompanyId,
            GpsDeviceId = 1, GpsDevice = device, FuelTankCapacity = tankCapacity
        };
        ctx.GpsDevices.Add(device);
        ctx.Vehicles.Add(vehicle);
        ctx.SaveChanges();
        return (ctx, vehicle);
    }

    private static void AddPos(TestGisDbContext ctx, DateTime t, int fuel, double speed = 0)
    {
        ctx.GpsPositions.Add(new GpsPosition
        {
            DeviceId = 1, RecordedAt = t, FuelRaw = fuel,
            Latitude = 36.8, Longitude = 10.1, SpeedKph = speed, IsValid = true, IsRealTime = true
        });
    }

    [Fact]
    public async Task Audit_MergedRefill_CarriesGaugePointsSpan()
    {
        var (ctx, vehicle) = Setup(tankCapacity: 100);
        var t0 = DateTime.UtcNow.AddDays(-1);
        // Plein en deux paliers à 2 min d'écart : 20→50 puis 50→80 = UN plein 20→80.
        AddPos(ctx, t0, 20);
        AddPos(ctx, t0.AddMinutes(10), 20);
        AddPos(ctx, t0.AddMinutes(20), 50);
        AddPos(ctx, t0.AddMinutes(22), 80);
        AddPos(ctx, t0.AddMinutes(60), 79);
        AddPos(ctx, t0.AddMinutes(120), 78);
        await ctx.SaveChangesAsync();

        var service = new FuelCalculationService(ctx);
        var audit = await service.GetFuelLevelAuditAsync(vehicle, t0.AddHours(-1), t0.AddHours(3));

        audit.HasSensor.Should().BeTrue();
        audit.DetectedRefills.Should().HaveCount(1);
        var refill = audit.DetectedRefills[0];
        refill.FromPercent.Should().Be(20);
        refill.ToPercent.Should().Be(80);
        refill.DeltaPoints.Should().Be(60);
        refill.Liters.Should().Be(60m); // conversion nominale : 60 pts × 1 L/pt
    }

    [Fact]
    public async Task Calibration_LearnsFromFourBilledFills()
    {
        // Réservoir déclaré 100 L, mais 1,2 L réel par point : chaque plein de
        // 60 L ne fait monter la jauge que de 50 points.
        var (ctx, vehicle) = Setup(tankCapacity: 100);
        var baseDate = DateTime.UtcNow.AddDays(-60);

        for (int i = 0; i < 4; i++)
        {
            var day = baseDate.AddDays(i * 10);
            AddPos(ctx, day, 20);
            AddPos(ctx, day.AddMinutes(15), 20);
            AddPos(ctx, day.AddMinutes(30), 70);
            AddPos(ctx, day.AddMinutes(45), 70);
            AddPos(ctx, day.AddMinutes(90), 69);
            ctx.FuelEntries.Add(new FuelEntry
            {
                VehicleId = 1, CompanyId = CompanyId, VehiclePlate = "1 TU 1",
                InvoiceDate = day.AddMinutes(35), Volume = 60m, TotalAmount = 150m, PricePerLiter = 2.5m
            });
        }
        await ctx.SaveChangesAsync();

        var service = new FuelCalculationService(ctx);
        var calib = await service.GetTankCalibrationAsync(vehicle);

        calib.IsCalibrated.Should().BeTrue();
        calib.PointCount.Should().Be(4);
        calib.LitersPerPoint.Should().Be(1.2m);
        calib.EffectiveTankLiters.Should().Be(120);
    }

    [Fact]
    public async Task Calibration_WithoutSensorData_StaysNominal()
    {
        var (ctx, vehicle) = Setup(tankCapacity: 500);
        ctx.FuelEntries.Add(new FuelEntry
        {
            VehicleId = 1, CompanyId = CompanyId, VehiclePlate = "1 TU 1",
            InvoiceDate = DateTime.UtcNow.AddDays(-5), Volume = 400m, TotalAmount = 800m
        });
        await ctx.SaveChangesAsync();

        var service = new FuelCalculationService(ctx);
        var calib = await service.GetTankCalibrationAsync(vehicle);

        calib.IsCalibrated.Should().BeFalse();
        calib.LitersPerPoint.Should().Be(5.0m);
    }
}
