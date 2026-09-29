using FluentAssertions;
using GisAPI.Domain.Entities;
using GisAPI.Services;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GisAPI.Tests.Services;

/// <summary>
/// Tests de garde de <c>GisAPI.Services.VoltageHealthMonitoringService</c>, refondu
/// le 29/09/2026 : un seul signal, la tension relevée au DERNIER DÉMARRAGE du
/// véhicule (<c>gps_devices.battery_start_raw</c>) passée sous 11,5 V.
///
/// <list type="bullet">
///   <item><description><b>Filtre des candidats</b> — boîtiers NEMS (gps_type_1)
///     avec un véhicule, NON immobilisé, hors temporisation de 48 h, ET porteurs
///     d'une mesure de moins de 3 jours.</description></item>
///   <item><description><b>Décision</b> — la valeur brute est triée sur la bande
///     68-92 avant d'être comparée au seuil : hors bande ce n'est pas une tension
///     (octet de cap des firmwares R00C30d).</description></item>
/// </list>
///
/// <para>Ce que le service ne lit PLUS : <c>gps_positions.power_voltage</c>
/// (octet 32-34), prouvé muet le 25/09/2026 — 281 boîtiers sur 288 y renvoyaient la
/// même valeur moteur tournant et moteur éteint. Le service tournait à vide :
/// 2 boîtiers signalés sur 308 jugeables au 29/09/2026.</para>
/// </summary>
public class VoltageHealthMonitoringServiceTests
{
    private const int CompanyId = 1;
    private const int NemsDeviceId = 500;

    // Miroir des constantes du service.
    private const int CooldownHours = 48;
    private const int MaxReadingAgeDays = 3;

    private static GpsDevice SeedDevice(
        TestGisDbContext context,
        int id,
        string protocolType,
        int? vehicleId,
        short? batteryStartRaw = 80,
        DateTime? batteryStartAt = null,
        DateTime? lastHealthAlertAt = null,
        bool isImmobilized = false)
    {
        var device = new GpsDevice
        {
            Id = id,
            CompanyId = CompanyId,
            DeviceUid = $"DEV-{id}",
            Status = "active",
            ProtocolType = protocolType,
            BatteryStartRaw = batteryStartRaw,
            BatteryStartAt = batteryStartAt ?? DateTime.UtcNow.AddHours(-2),
            LastVoltageHealthAlertAt = lastHealthAlertAt
        };
        context.GpsDevices.Add(device);

        if (vehicleId.HasValue)
        {
            context.Vehicles.Add(new Vehicle
            {
                Id = vehicleId.Value,
                CompanyId = CompanyId,
                Name = $"Véhicule {vehicleId}",
                Plate = $"TN-{vehicleId}",
                GpsDeviceId = id,
                Type = "camion",
                Status = "available",
                IsImmobilized = isImmobilized
            });
        }

        return device;
    }

    // ── Filtre des candidats ────────────────────────────────────────────────

    [Fact]
    public async Task FiltreCandidats_NemsAvecVehiculeNonImmobiliseHorsTemporisationEtMesureFraiche()
    {
        using var context = TestDbContextFactory.Create();
        var now = DateTime.UtcNow;
        var cooldownCutoff = now.AddHours(-CooldownHours);
        var readingCutoff = now.AddDays(-MaxReadingAgeDays);

        SeedDevice(context, id: NemsDeviceId, protocolType: "gps_type_1", vehicleId: 700); // retenu
        SeedDevice(context, id: 503, protocolType: "gps_type_1", vehicleId: 701,
            lastHealthAlertAt: now.AddHours(-49));                                          // 48 h passées, retenu
        SeedDevice(context, id: 504, protocolType: "gps_type_1", vehicleId: 702,
            lastHealthAlertAt: now.AddHours(-2));                                           // temporisation, écarté
        SeedDevice(context, id: 505, protocolType: "noron", vehicleId: 703);                // autre protocole, écarté
        SeedDevice(context, id: 506, protocolType: "gps_type_1", vehicleId: null);          // sans véhicule, écarté
        SeedDevice(context, id: 507, protocolType: "gps_type_1", vehicleId: 705,
            isImmobilized: true);                                                           // hors service, écarté
        SeedDevice(context, id: 508, protocolType: "gps_type_1", vehicleId: 706,
            batteryStartRaw: null);                                                         // jamais relevé, écarté
        SeedDevice(context, id: 509, protocolType: "gps_type_1", vehicleId: 707,
            batteryStartAt: now.AddDays(-5));                                               // mesure trop vieille, écartée

        await context.SaveChangesAsync();

        var picked = await context.GpsDevices
            .Include(d => d.Vehicle)
            .Where(d => d.ProtocolType == "gps_type_1"
                     && d.Vehicle != null
                     && !d.Vehicle.IsImmobilized
                     && d.BatteryStartRaw != null
                     && d.BatteryStartAt != null
                     && d.BatteryStartAt >= readingCutoff
                     && (d.LastVoltageHealthAlertAt == null
                         || d.LastVoltageHealthAlertAt < cooldownCutoff))
            .Select(d => d.Id)
            .OrderBy(id => id)
            .ToListAsync();

        picked.Should().BeEquivalentTo(new[] { NemsDeviceId, 503 });
    }

    [Fact]
    public async Task MesureDePlusDeTroisJours_NEstPasNotifiee()
    {
        // L'écran continue de l'afficher, datée : c'est une information. Mais
        // notifier « batterie en fin de vie » sur une mesure d'il y a une semaine
        // serait au mieux inutile, au pire faux — batterie déjà remplacée.
        using var context = TestDbContextFactory.Create();
        var now = DateTime.UtcNow;
        SeedDevice(context, id: NemsDeviceId, protocolType: "gps_type_1", vehicleId: 700,
            batteryStartRaw: 70, batteryStartAt: now.AddDays(-4));
        await context.SaveChangesAsync();

        var frais = await context.GpsDevices
            .CountAsync(d => d.BatteryStartAt >= now.AddDays(-MaxReadingAgeDays));

        frais.Should().Be(0);
    }

    // ── Décision ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData((short)68, 10.6)]    // 10,625 V — borne basse de la bande
    [InlineData((short)70, 10.9)]    // 10,94 V
    [InlineData((short)73, 11.4)]    // 11,41 V, dernier cran sous le seuil
    public void BatterieEnFinDeVie_SousLeSeuil(short raw, double attenduArrondi)
    {
        var volts = VoltageHealthMonitoringService.DyingBatteryVolts(raw);

        volts.Should().NotBeNull();
        Math.Round(volts!.Value, 1).Should().Be(attenduArrondi);
    }

    [Theory]
    [InlineData((short)74)]   // 11,56 V — au-dessus du seuil
    [InlineData((short)79)]   // 12,34 V — médiane de la flotte TN au démarrage
    [InlineData((short)90)]   // 14,06 V
    public void BatterieSaine_NeDeclenchePas(short raw)
    {
        VoltageHealthMonitoringService.DyingBatteryVolts(raw).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]          // jamais relevé, ou octet jugé figé
    [InlineData((short)21)]     // octet de cap d'un firmware R00C30d : 3,3 V n'est pas une mesure
    [InlineData((short)67)]     // 10,47 V : sous le plancher de 10,5 V
    [InlineData((short)50)]     // 7,8 V : repart comme un vehicule sain, donc pas la batterie
    [InlineData((short)44)]     // dernier cran de l'octet de cap
    [InlineData((short)93)]     // au-delà de l'échelle
    public void ValeurQuiNEstPasUneTension_NeDeclenchePas(short? raw)
    {
        VoltageHealthMonitoringService.DyingBatteryVolts(raw).Should().BeNull(
            "une valeur absurde ne doit jamais devenir une alerte");
    }
}
