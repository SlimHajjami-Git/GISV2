using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Admin.DeviceCommands;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Admin;

/// <summary>
/// Écran admin « Commandes boîtiers » (16/09/2026). Deux contrats verrouillés :
///
/// <list type="bullet">
///   <item><description><b>Jamais de STOP par cet écran</b> — règle absolue du projet
///     (coupure moteur uniquement via le circuit d'immobilisation approuvé). Refusé
///     avant qu'une seule ligne device_commands ne soit écrite.</description></item>
///   <item><description><b>Cloisonnement société</b> — un identifiant de boîtier
///     d'une autre société passé dans la sélection est ignoré, jamais servi.</description></item>
/// </list>
/// Plus le tri NEMS (AJ+ n'existe pas sur Teltonika/Noron) et le suivi de statut
/// après push (sent / pending), copie de la synchro des limites de vitesse.
/// </summary>
public class AdminDeviceCommandTests
{
    private const int Hertz = 4;
    private const int Other = 9;

    // ── Garde-fou ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("AJ+STOP#9999")]
    [InlineData("aj+stop#1311")]
    [InlineData("AJ+CONFN=stop,1")]
    [InlineData("  AJ+STOP#9999\r\n")]
    public void Safety_RefuseToujoursLeStop(string texte)
    {
        var check = DeviceCommandSafety.Check(texte);
        check.Ok.Should().BeFalse();
        check.Reason.Should().Contain("STOP");
    }

    [Fact]
    public void Safety_RefuseCeQuiNestPasDuAjPlus()
    {
        DeviceCommandSafety.Check("CONFN=101,3,2,377,0,0,#9999").Ok.Should().BeFalse();
        DeviceCommandSafety.Check("*HQ,imei,V1").Ok.Should().BeFalse();
    }

    [Fact]
    public void Safety_RefuseVideMultiligneEtTropLong()
    {
        DeviceCommandSafety.Check("").Ok.Should().BeFalse();
        DeviceCommandSafety.Check("   \n").Ok.Should().BeFalse();
        DeviceCommandSafety.Check("AJ+GO#9999\nAJ+GO#9999").Ok.Should().BeFalse();
        DeviceCommandSafety.Check("AJ+" + new string('X', 97)).Ok.Should().BeFalse("100 caractères + \\n dépasse VARCHAR(100)");
        DeviceCommandSafety.Check("AJ+" + new string('X', 96)).Ok.Should().BeTrue();
        DeviceCommandSafety.Check("AJ+CONFN=101,3,2,377,0,0,#9999é").Ok.Should().BeFalse("non ASCII");
    }

    [Fact]
    public void Safety_NormaliseAvecUnSeulRetourALaLigne()
    {
        var check = DeviceCommandSafety.Check("  AJ+CONFN=101,3,2,377,0,0,#9999\r\n");
        check.Ok.Should().BeTrue();
        check.Normalized.Should().Be("AJ+CONFN=101,3,2,377,0,0,#9999\n");
    }

    // ── Handler ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handler_StopRefuseAvantToutEcritureEnBase()
    {
        using var ctx = TestDbContextFactory.Create();
        Seed(ctx, 1, Hertz, "245 TU 536");
        await ctx.SaveChangesAsync();
        var pusher = new FakePusher();

        var result = await Handler(ctx, pusher).Handle(
            new SendAdminDeviceCommandCommand(Hertz, "AJ+STOP#9999", null, AllFleet: true), CancellationToken.None);

        result.Accepted.Should().BeFalse();
        (await ctx.DeviceCommands.CountAsync()).Should().Be(0);
        pusher.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Handler_SelectionVideRefusee()
    {
        using var ctx = TestDbContextFactory.Create();
        Seed(ctx, 1, Hertz, "245 TU 536");
        await ctx.SaveChangesAsync();

        var result = await Handler(ctx, new FakePusher()).Handle(
            new SendAdminDeviceCommandCommand(Hertz, "AJ+GO#9999", new List<int>(), AllFleet: false), CancellationToken.None);

        result.Accepted.Should().BeFalse();
        (await ctx.DeviceCommands.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Handler_SousEnsemble_NeTouchePasLesAutres()
    {
        using var ctx = TestDbContextFactory.Create();
        Seed(ctx, 1, Hertz, "245 TU 536");
        Seed(ctx, 2, Hertz, "233 TU 5102");
        Seed(ctx, 3, Hertz, "234 TU 4624");
        await ctx.SaveChangesAsync();
        var pusher = new FakePusher();

        var result = await Handler(ctx, pusher).Handle(
            new SendAdminDeviceCommandCommand(Hertz, "AJ+GO#9999", new List<int> { 1, 3 }, AllFleet: false), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        result.Targeted.Should().Be(2);
        pusher.Calls.Select(c => c.DeviceId).Should().BeEquivalentTo(new[] { 1, 3 });
        (await ctx.DeviceCommands.Select(c => c.DeviceId).ToListAsync()).Should().BeEquivalentTo(new[] { 1, 3 });
    }

    [Fact]
    public async Task Handler_ToutLeParc_CibleTousLesNems_EtSauteTeltonika()
    {
        using var ctx = TestDbContextFactory.Create();
        Seed(ctx, 1, Hertz, "245 TU 536");
        Seed(ctx, 2, Hertz, "233 TU 5102");
        Seed(ctx, 3, Hertz, "FMB130", protocol: "teltonika", brand: "Teltonika");
        await ctx.SaveChangesAsync();
        var pusher = new FakePusher();

        var result = await Handler(ctx, pusher).Handle(
            new SendAdminDeviceCommandCommand(Hertz, "AJ+CONFN=101,3,2,377,0,0,#9999", null, AllFleet: true), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        result.Targeted.Should().Be(3);
        result.SkippedNonNems.Should().Be(1);
        result.PushedLive.Should().Be(2);
        pusher.Calls.Select(c => c.DeviceId).Should().BeEquivalentTo(new[] { 1, 2 });
        result.Details.Single(d => d.DeviceId == 3).Outcome.Should().Be("skipped_non_nems");

        var rows = await ctx.DeviceCommands.ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.CommandType == "ADMIN" && r.Source == "admin" && r.CompanyId == Hertz
                                       && r.CommandText == "AJ+CONFN=101,3,2,377,0,0,#9999\n");
    }

    [Fact]
    public async Task Handler_IgnoreUnBoitierDuneAutreSociete()
    {
        using var ctx = TestDbContextFactory.Create();
        Seed(ctx, 1, Hertz, "245 TU 536");
        Seed(ctx, 50, Other, "999 TU 1");
        await ctx.SaveChangesAsync();
        var pusher = new FakePusher();

        // L'écran (ou un appel forgé) passe un id étranger : silencieusement écarté.
        var result = await Handler(ctx, pusher).Handle(
            new SendAdminDeviceCommandCommand(Hertz, "AJ+GO#9999", new List<int> { 1, 50 }, AllFleet: false), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        result.Targeted.Should().Be(1);
        pusher.Calls.Select(c => c.DeviceId).Should().BeEquivalentTo(new[] { 1 });
        (await ctx.DeviceCommands.AnyAsync(c => c.DeviceId == 50)).Should().BeFalse();

        // Et « tout le parc » de HERTZ ne déborde pas non plus.
        var all = await Handler(ctx, pusher).Handle(
            new SendAdminDeviceCommandCommand(Hertz, "AJ+GO#9999", null, AllFleet: true), CancellationToken.None);
        all.Targeted.Should().Be(1);
    }

    [Fact]
    public async Task Handler_PushReussi_MarqueSent_HorsLigne_RestePending()
    {
        using var ctx = TestDbContextFactory.Create();
        Seed(ctx, 1, Hertz, "245 TU 536");
        Seed(ctx, 2, Hertz, "233 TU 5102");
        await ctx.SaveChangesAsync();
        var pusher = new FakePusher { OutcomeFor = { [2] = RustPushOutcome.DeviceNotConnected } };

        var result = await Handler(ctx, pusher).Handle(
            new SendAdminDeviceCommandCommand(Hertz, "AJ+GO#9999", null, AllFleet: true), CancellationToken.None);

        result.PushedLive.Should().Be(1);
        result.Offline.Should().Be(1);
        var byDevice = await ctx.DeviceCommands.ToDictionaryAsync(c => c.DeviceId);
        byDevice[1].Status.Should().Be("sent");
        byDevice[1].SentAt.Should().NotBeNull();
        byDevice[1].Attempts.Should().Be(1);
        byDevice[2].Status.Should().Be("pending");
        byDevice[2].SentAt.Should().BeNull();
        result.Details.Single(d => d.DeviceId == 2).Outcome.Should().Be("offline");
        byDevice[1].UserId.Should().Be(7, "l'auteur est tracé");
    }

    // ── Outillage ─────────────────────────────────────────────────────────────

    private static SendAdminDeviceCommandCommandHandler Handler(TestGisDbContext ctx, IRustCommandPusher pusher)
    {
        var tenant = new Mock<ICurrentTenantService>();
        tenant.Setup(t => t.UserId).Returns((int?)7);
        return new SendAdminDeviceCommandCommandHandler(
            ctx, tenant.Object, pusher, NullLogger<SendAdminDeviceCommandCommandHandler>.Instance);
    }

    private static void Seed(TestGisDbContext ctx, int id, int companyId, string plate,
        string protocol = "gps_type_1", string brand = "NEMS")
    {
        ctx.GpsDevices.Add(new GpsDevice
        {
            Id = id,
            DeviceUid = $"8601410766{id:D5}",
            Mat = $"NR08G{id:D4}",
            Label = plate,
            Status = "active",
            CompanyId = companyId,
            ProtocolType = protocol,
            Brand = brand,
            Model = "L",
            CommandGo = "AJ+GO#9999\n",
            CommandStop = "AJ+STOP#9999\n",
            LastCommunication = DateTime.UtcNow
        });
        ctx.Vehicles.Add(new Vehicle
        {
            Id = id,
            Name = plate,
            Plate = plate,
            Type = "camion",
            Status = "available",
            CompanyId = companyId,
            HasGps = true,
            GpsDeviceId = id
        });
    }

    private sealed class FakePusher : IRustCommandPusher
    {
        public List<(int DeviceId, string Command)> Calls { get; } = new();
        public Dictionary<int, RustPushOutcome> OutcomeFor { get; } = new();

        public Task<RustPushResult> PushAsync(int deviceId, string command, CancellationToken ct = default)
        {
            Calls.Add((deviceId, command));
            var outcome = OutcomeFor.TryGetValue(deviceId, out var o) ? o : RustPushOutcome.Pushed;
            return Task.FromResult(new RustPushResult(outcome, outcome.ToString()));
        }
    }
}
