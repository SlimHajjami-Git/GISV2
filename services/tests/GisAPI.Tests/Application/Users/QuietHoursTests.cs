using FluentAssertions;
using GisAPI.Application.Common;
using GisAPI.Application.Features.Users.Commands.UpdateQuietHours;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.Users;

/// <summary>
/// Heures silencieuses (recette client du 11/09/2026) : l'interrupteur n'écrivait que
/// dans le localStorage et aucun envoi ne le lisait. Couvre la règle horaire partagée
/// (<see cref="QuietHoursPolicy"/>) et la validation de la commande d'enregistrement.
/// </summary>
public class QuietHoursTests
{
    // Tunis = UTC+1 toute l'année (pas d'heure d'été) : fuseau fixe pour des tests
    // indépendants de la présence de tzdata sur la machine.
    private static readonly TimeZoneInfo Tunis =
        TimeZoneInfo.CreateCustomTimeZone("Tunis-test", TimeSpan.FromHours(1), "Tunis", "Tunis");

    private static readonly TimeSpan H22 = new(22, 0, 0);
    private static readonly TimeSpan H07 = new(7, 0, 0);

    /// <summary>Instant UTC correspondant à l'heure LOCALE de Tunis donnée, le 11/09/2026.</summary>
    private static DateTime TunisLocal(int hour, int minute) =>
        new DateTime(2026, 9, 11, hour, minute, 0, DateTimeKind.Utc).AddHours(-1);

    // ── Plage nocturne 22:00 → 07:00 (passe minuit) ──

    [Theory]
    [InlineData(23, 30, true)]   // en pleine nuit, avant minuit
    [InlineData(0, 15, true)]    // après minuit
    [InlineData(6, 59, true)]    // dernière minute de la plage
    [InlineData(7, 0, false)]    // fin EXCLUE : 07:00 n'est plus silencieux
    [InlineData(12, 0, false)]   // midi
    [InlineData(22, 0, true)]    // début INCLUS
    [InlineData(21, 59, false)]  // juste avant le début
    public void NightWindow_22_to_07_TunisTime(int hour, int minute, bool expected)
    {
        QuietHoursPolicy.IsQuietNow(true, H22, H07, TunisLocal(hour, minute), Tunis)
            .Should().Be(expected);
    }

    [Fact]
    public void NightWindow_UsesCompanyTimeZone_NotUtc()
    {
        // 06:30 UTC = 07:30 à Tunis : hors plage, alors qu'en UTC on y serait encore.
        var utc = new DateTime(2026, 9, 11, 6, 30, 0, DateTimeKind.Utc);
        QuietHoursPolicy.IsQuietNow(true, H22, H07, utc, Tunis).Should().BeFalse();
        QuietHoursPolicy.IsQuietNow(true, H22, H07, utc, TimeZoneInfo.Utc).Should().BeTrue();
    }

    // ── Plage de jour 12:00 → 14:00 ──

    [Theory]
    [InlineData(11, 59, false)]
    [InlineData(12, 0, true)]
    [InlineData(13, 0, true)]
    [InlineData(14, 0, false)]
    [InlineData(23, 30, false)]
    public void DayWindow_12_to_14(int hour, int minute, bool expected)
    {
        QuietHoursPolicy.IsQuietNow(true, new TimeSpan(12, 0, 0), new TimeSpan(14, 0, 0), TunisLocal(hour, minute), Tunis)
            .Should().Be(expected);
    }

    // ── Cas neutres ──

    [Theory]
    [InlineData("accident", "critical", true)]
    [InlineData("tow_detected", "high", true)]
    [InlineData("accident_tow_detected", "high", true)]
    [InlineData("start_failure", "high", true)]
    [InlineData("START_FAILURE", "normal", true)]     // insensible à la casse
    [InlineData("document_expiry", "high", false)]    // la priorité seule ne suffit pas
    [InlineData("maintenance_due", "high", false)]
    [InlineData("geofence", "normal", false)]
    [InlineData(null, null, false)]
    public void BypassesQuietHours_OnlyCriticalOrAlwaysDeliveredTypes(string? type, string? priority, bool expected)
        => QuietHoursPolicy.BypassesQuietHours(type, priority).Should().Be(expected);

    [Fact]
    public void Disabled_IsNeverQuiet()
    {
        QuietHoursPolicy.IsQuietNow(false, H22, H07, TunisLocal(23, 30), Tunis).Should().BeFalse();
    }

    [Fact]
    public void StartEqualsEnd_IsNeverQuiet()
    {
        QuietHoursPolicy.IsQuietNow(true, H22, H22, TunisLocal(22, 0), Tunis).Should().BeFalse();
        QuietHoursPolicy.IsQuietNow(true, H22, H22, TunisLocal(3, 0), Tunis).Should().BeFalse();
    }

    [Fact]
    public void MissingBound_IsNeverQuiet()
    {
        QuietHoursPolicy.IsQuietNow(true, null, H07, TunisLocal(3, 0), Tunis).Should().BeFalse();
        QuietHoursPolicy.IsQuietNow(true, H22, null, TunisLocal(23, 0), Tunis).Should().BeFalse();
    }

    [Theory]
    [InlineData("Africa/Tunis")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Pas/UnFuseau")]
    public void ResolveTimeZone_TunisOrFallback_IsUtcPlusOne(string? ianaId)
    {
        // Avec ou sans tzdata : Tunis (UTC+1) ou repli UTC+1 fixe.
        QuietHoursPolicy.ResolveTimeZone(ianaId).BaseUtcOffset.Should().Be(TimeSpan.FromHours(1));
    }

    // ── Lecture « HH:mm » ──

    [Theory]
    [InlineData("22:00", 22, 0)]
    [InlineData("07:05", 7, 5)]
    [InlineData("7:05", 7, 5)]
    [InlineData("22:00:00", 22, 0)]   // valeur relue telle que /users/me la renvoie
    [InlineData(" 23:59 ", 23, 59)]
    public void ParseHourMinute_Valid(string input, int hour, int minute)
    {
        QuietHoursPolicy.ParseHourMinute(input).Should().Be(new TimeSpan(hour, minute, 0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("24:00")]
    [InlineData("12:60")]
    [InlineData("abc")]
    [InlineData("22h00")]
    [InlineData("22:00:30")]
    public void ParseHourMinute_Invalid_ReturnsNull(string? input)
    {
        QuietHoursPolicy.ParseHourMinute(input).Should().BeNull();
    }

    // ── Commande d'enregistrement ──

    private const int CompanyId = 1;
    private const int UserId = 45;

    private static (TestGisDbContext ctx, UpdateQuietHoursCommandHandler handler) Setup(int tenantCompanyId = CompanyId)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Users.Add(new User
        {
            Id = UserId,
            FirstName = "Test",
            LastName = "GPA",
            Email = "gpa@calypso.tn",
            CompanyId = CompanyId,
            PasswordHash = "x",
            Status = "active"
        });
        ctx.SaveChanges();

        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: tenantCompanyId, userId: UserId);
        return (ctx, new UpdateQuietHoursCommandHandler(ctx, tenant.Object));
    }

    [Fact]
    public async Task Enabled_ValidNightWindow_IsPersisted()
    {
        var (ctx, handler) = Setup();

        await handler.Handle(new UpdateQuietHoursCommand(true, "22:00", "07:00"), CancellationToken.None);

        var user = await ctx.Users.FindAsync(UserId);
        user!.QuietHoursEnabled.Should().BeTrue();
        user.QuietHoursStart.Should().Be(H22);
        user.QuietHoursEnd.Should().Be(H07);
    }

    [Fact]
    public async Task Enabled_StartEqualsEnd_IsRefused()
    {
        var (ctx, handler) = Setup();

        var act = () => handler.Handle(new UpdateQuietHoursCommand(true, "22:00", "22:00"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*différentes*");
        (await ctx.Users.FindAsync(UserId))!.QuietHoursEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData("25:00", "07:00")]
    [InlineData("22:00", "7h")]
    public async Task InvalidFormat_IsRefused(string start, string end)
    {
        var (_, handler) = Setup();

        var act = () => handler.Handle(new UpdateQuietHoursCommand(true, start, end), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*HH:mm*");
    }

    [Fact]
    public async Task Enabled_MissingEnd_IsRefused()
    {
        var (_, handler) = Setup();

        var act = () => handler.Handle(new UpdateQuietHoursCommand(true, "22:00", null), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*obligatoires*");
    }

    [Fact]
    public async Task Disabled_WithoutHours_KeepsPreviousHours()
    {
        var (ctx, handler) = Setup();
        await handler.Handle(new UpdateQuietHoursCommand(true, "22:00", "07:00"), CancellationToken.None);

        await handler.Handle(new UpdateQuietHoursCommand(false, null, null), CancellationToken.None);

        var user = await ctx.Users.FindAsync(UserId);
        user!.QuietHoursEnabled.Should().BeFalse();
        user.QuietHoursStart.Should().Be(H22);
        user.QuietHoursEnd.Should().Be(H07);
    }

    [Fact]
    public async Task UserFromOtherCompany_ThrowsNotFound()
    {
        var (_, handler) = Setup(tenantCompanyId: 99);

        var act = () => handler.Handle(new UpdateQuietHoursCommand(true, "22:00", "07:00"), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
