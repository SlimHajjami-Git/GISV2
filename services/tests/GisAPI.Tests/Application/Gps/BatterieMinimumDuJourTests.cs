using System.Text.Json;
using FluentAssertions;
using GisAPI.Application.Common;
using GisAPI.Application.Features.Gps.Commands.BroadcastPosition;
using GisAPI.Application.Features.Vehicles.Queries.GetVehiclesWithPositions;
using GisAPI.Infrastructure.Messaging;
using GisAPI.Services;
using Xunit;

namespace GisAPI.Tests.Application.Gps;

/// <summary>
/// Monitoring — tension batterie des NEMS = minimum du jour de l'octet « Batterie »
/// (34-36), demande de Karim du 25/09/2026.
///
/// <para>Contexte : l'octet 34-36 n'a de sens que sur les firmwares R00C32a et FMS ;
/// les R00C30d y recopient l'octet de cap (0 à 44 brut). Les boîtiers passaient en
/// masse de R00C30d à R00C32a ce jour-là, et l'audit sur 7 jours les laissait masqués
/// plusieurs jours après leur mise à jour.</para>
/// </summary>
public class BatterieMinimumDuJourTests
{
    private static readonly TimeZoneInfo Tunis = QuietHoursPolicy.ResolveTimeZone(null);

    // ── Journée de Tunis ────────────────────────────────────────────────────

    [Fact]
    public void LocalDay_EnJournee_CommenceA23hUtcLaVeille()
    {
        var (debut, fin) = LocalDay.Window(new DateTime(2026, 9, 25, 7, 0, 0, DateTimeKind.Utc), Tunis);

        debut.Should().Be(new DateTime(2026, 9, 24, 23, 0, 0, DateTimeKind.Utc));
        fin.Should().Be(new DateTime(2026, 9, 25, 23, 0, 0, DateTimeKind.Utc));
        debut.Kind.Should().Be(DateTimeKind.Utc);
        fin.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void LocalDay_A23h59UtcLeVingtQuatre_EstDejaLeVingtCinqATunis()
    {
        // 23:59 UTC = 00:59 à Tunis : la nouvelle journée a commencé à 23:00 UTC.
        var (debut, _) = LocalDay.Window(new DateTime(2026, 9, 24, 23, 59, 0, DateTimeKind.Utc), Tunis);
        debut.Should().Be(new DateTime(2026, 9, 24, 23, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void LocalDay_A22h59UtcLeVingtQuatre_EstEncoreLeVingtQuatreATunis()
    {
        var (debut, fin) = LocalDay.Window(new DateTime(2026, 9, 24, 22, 59, 59, DateTimeKind.Utc), Tunis);
        debut.Should().Be(new DateTime(2026, 9, 23, 23, 0, 0, DateTimeKind.Utc));
        fin.Should().Be(new DateTime(2026, 9, 24, 23, 0, 0, DateTimeKind.Utc));
    }

    // ── Ce que le monitoring affiche (BatteryReadout) ───────────────────────

    [Fact]
    public void Nems_AfficheLeMinimumDuJour_EnVoltsEtEnPourcentage()
    {
        // 231 TU 7027, 25/09 : minimum à 69 brut au démarrage, soit 10,78 V.
        var r = BatteryReadout.Compute("gps_type_1", voltageSensorReliable: false,
            deviceBatteryLevel: null, lastBatteryRaw: 80, lastPowerVoltage: 41, dailyMinBatteryRaw: 69);

        r.IsDailyMin.Should().BeTrue();
        r.Volts.Should().Be(10.8, "10,78 V arrondi à 0,1 V ; la dernière trame (12,5 V) n'est PAS affichée");
        r.Percent.Should().Be(0);
        r.LowWarning.Should().BeTrue("10,78 V est sous 11,5 V");
    }

    [Fact]
    public void Nems_NeConsultePlusLAudit_UnBoitierJusteMisAJourSAfficheTout_De_Suite()
    {
        // Verdict encore faux (6 jours de R00C30d dans la fenêtre de l'audit) :
        // la tension s'affiche quand même, c'est la valeur qui est triée.
        var r = BatteryReadout.Compute("gps_type_1", voltageSensorReliable: false,
            deviceBatteryLevel: null, lastBatteryRaw: 82, lastPowerVoltage: null, dailyMinBatteryRaw: 80);

        r.Volts.Should().Be(12.5);
        r.Percent.Should().Be(83);
        r.LowWarning.Should().BeFalse();
    }

    [Fact]
    public void Nems_AucuneValeurSenseeAujourdHui_AfficheNA()
    {
        // R00C30d : la requête a écarté 0-44, il ne reste rien.
        var r = BatteryReadout.Compute("gps_type_1", voltageSensorReliable: true,
            deviceBatteryLevel: 90, lastBatteryRaw: 21, lastPowerVoltage: 41, dailyMinBatteryRaw: null);

        r.IsDailyMin.Should().BeTrue();
        r.Volts.Should().BeNull();
        r.Percent.Should().BeNull("le niveau enregistré sur le boîtier ne doit pas combler le N/A");
        r.LowWarning.Should().BeFalse("pas de valeur, pas d'alerte");
    }

    [Theory]
    [InlineData(73, true)]    // 11,41 V
    [InlineData(74, false)]   // 11,56 V
    public void Nems_IconeAnomalie_SousOnzeVirguleCinqVolts(int minRaw, bool alerte)
    {
        BatteryReadout.Compute("gps_type_1", null, null, null, null, minRaw)
            .LowWarning.Should().Be(alerte);
    }

    [Fact]
    public void Teltonika_RegleInchangee_DerniereTrameSiLAuditLaValidee()
    {
        var r = BatteryReadout.Compute("teltonika", voltageSensorReliable: true,
            deviceBatteryLevel: null, lastBatteryRaw: 0, lastPowerVoltage: 125, dailyMinBatteryRaw: null);

        r.IsDailyMin.Should().BeFalse();
        r.Volts.Should().Be(12.5);
        r.Percent.Should().Be(83);
        r.LowWarning.Should().BeFalse("leur icône reste pilotée par l'alerte de santé");
    }

    [Fact]
    public void Teltonika_AuditNonValide_AfficheNA()
    {
        var r = BatteryReadout.Compute("teltonika", voltageSensorReliable: false,
            deviceBatteryLevel: null, lastBatteryRaw: 0, lastPowerVoltage: 125, dailyMinBatteryRaw: 70);

        r.Volts.Should().BeNull();
        r.Percent.Should().BeNull();
    }

    [Fact]
    public void Teltonika_TensionPlafonneeA14Virgule4_EtNiveauDuBoitierPrioritaire()
    {
        var r = BatteryReadout.Compute("teltonika", true, deviceBatteryLevel: 55,
            lastBatteryRaw: null, lastPowerVoltage: 150, dailyMinBatteryRaw: null);

        r.Volts.Should().Be(14.4);
        r.Percent.Should().Be(55);
    }

    [Fact]
    public void StatsDto_ParDefaut_NEstPasUnMinimumDuJour()
    {
        // Le monitoring admin et tout autre appelant du record gardent l'ancien sens.
        var dto = new VehicleStatsDto(0, 0, null, null, null, null, false, true,
            TimeSpan.Zero, TimeSpan.Zero, null, null, null);

        dto.BatteryIsDailyMin.Should().BeFalse();
        dto.BatteryDayEndUtc.Should().BeNull();
    }

    // ── Temps réel (Broadcast SignalR) ──────────────────────────────────────

    [Fact]
    public void LiveBattery_Nems_CalculeDepuisLOctetBrut_SansAudit()
    {
        var (volts, pct) = BroadcastPositionCommandHandler.LiveBattery(
            "gps_type_1", voltageSensorReliable: false,
            batteryRaw: 74, batteryVoltage: 6.2, batteryPercent: 0);

        volts.Should().Be(11.6, "11,5625 V arrondi ; le batteryVoltage de Redis (6,2) est ignoré");
        pct.Should().Be(31);
    }

    [Theory]
    [InlineData(21)]    // octet de cap R00C30d
    [InlineData(0)]     // pas de mesure : Redis aurait recopié l'ancienne tension
    public void LiveBattery_Nems_ValeurSansSens_NeDiffuseRien(int raw)
    {
        BroadcastPositionCommandHandler.LiveBattery("gps_type_1", true, raw, 12.5, 83)
            .Should().Be(((double?)null, (int?)null));
    }

    [Fact]
    public void LiveBattery_Nems_OctetAbsent_NeDiffuseRien()
    {
        // Voie d'entrée qui ne transmet pas l'octet (ex. GpsController).
        BroadcastPositionCommandHandler.LiveBattery("gps_type_1", true, null, 12.5, 83)
            .Should().Be(((double?)null, (int?)null));
    }

    [Fact]
    public void LiveBattery_Teltonika_Inchange()
    {
        BroadcastPositionCommandHandler.LiveBattery("teltonika", true, 0, 12.7, 94)
            .Should().Be(((double?)12.7, (int?)94));
        BroadcastPositionCommandHandler.LiveBattery("teltonika", false, 0, 12.7, 94)
            .Should().Be(((double?)null, (int?)null));
    }

    // ── L'octet brut arrive par les trois voies du temps réel ───────────────

    [Fact]
    public void Redis_LitBatteryRaw()
    {
        const string json = """{"deviceUid":"860141076673178","latitude":36.8,"longitude":10.1,"recordedAt":"2026-09-25T09:00:00Z","batteryVoltage":12.8,"batteryRaw":82}""";
        JsonSerializer.Deserialize<RedisPositionMessage>(json)!.BatteryRaw.Should().Be(82);
    }

    [Theory]
    [InlineData("""{"device_uid":"x","battery_raw":74}""", 74)]
    [InlineData("""{"device_uid":"x","battery_raw":0}""", 0)]
    [InlineData("""{"device_uid":"x"}""", null)]
    public void Telemetrie_LitBattery_raw(string json, int? attendu)
    {
        JsonSerializer.Deserialize<TelemetryMessage>(json)!.BatteryRaw.Should().Be(attendu);
    }

    [Fact]
    public void RabbitMq_LitBattery_rawEnSnakeCase()
    {
        // Mêmes options que RabbitMqConsumerService.
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };
        const string json = """{"device_uid":"x","recorded_at":"2026-09-25T09:00:00Z","fuel_raw":0,"power_voltage":41,"battery_raw":79}""";

        JsonSerializer.Deserialize<RabbitMqGpsMessage>(json, options)!.BatteryRaw.Should().Be(79);
    }
}
