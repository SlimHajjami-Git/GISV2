using FluentAssertions;
using GisAPI.Application.Common;
using GisAPI.Domain.Entities;
using Xunit;

namespace GisAPI.Tests.Application.Subscriptions;

/// <summary>
/// SubscriptionPolicy est la source unique de vérité du blocage/bannière :
/// avertissement J-30, urgence J-7, grâce de 7 jours après expiration, puis
/// blocage ; suspension manuelle prioritaire sur les dates.
/// </summary>
public class SubscriptionPolicyTests
{
    private static readonly DateTime Now = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

    private static Societe Make(DateTime? expires, string status = "active", bool isActive = true) => new()
    {
        Name = "Test",
        SubscriptionExpiresAt = expires,
        SubscriptionStatus = status,
        IsActive = isActive,
    };

    [Fact]
    public void Far_from_expiry_is_none()
    {
        var s = SubscriptionPolicy.Evaluate(Make(Now.AddDays(120)), Now);
        s.Level.Should().Be("none");
        s.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public void No_expiry_date_is_none()
    {
        SubscriptionPolicy.Evaluate(Make(null), Now).Level.Should().Be("none");
    }

    [Fact]
    public void Within_30_days_is_warning_within_7_is_danger()
    {
        SubscriptionPolicy.Evaluate(Make(Now.AddDays(20)), Now).Level.Should().Be("warning");
        SubscriptionPolicy.Evaluate(Make(Now.AddDays(5)), Now).Should().Match<SubscriptionPolicy.State>(
            x => x.Level == "danger" && x.Reason == "expiring" && !x.IsBlocked);
    }

    [Fact]
    public void Expired_within_grace_allows_access_with_danger_banner()
    {
        var s = SubscriptionPolicy.Evaluate(Make(Now.AddDays(-3)), Now);
        s.Level.Should().Be("danger");
        s.Reason.Should().Be("grace");
        s.IsBlocked.Should().BeFalse();
        s.GraceDaysLeft.Should().Be(4);                 // 7 - 3
    }

    [Fact]
    public void Expired_beyond_grace_is_blocked()
    {
        var s = SubscriptionPolicy.Evaluate(Make(Now.AddDays(-10)), Now);
        s.IsBlocked.Should().BeTrue();
        s.Reason.Should().Be("expired");
        s.GraceDaysLeft.Should().Be(0);
    }

    [Fact]
    public void Manual_suspension_blocks_regardless_of_dates()
    {
        SubscriptionPolicy.Evaluate(Make(Now.AddDays(200), "suspended"), Now)
            .Should().Match<SubscriptionPolicy.State>(x => x.IsBlocked && x.Reason == "suspended");
        SubscriptionPolicy.Evaluate(Make(Now.AddDays(200), "cancelled"), Now)
            .Should().Match<SubscriptionPolicy.State>(x => x.IsBlocked && x.Reason == "cancelled");
    }

    [Fact]
    public void IsActive_false_blocks_as_manual_suspension()
    {
        SubscriptionPolicy.Evaluate(Make(Now.AddDays(200), "active", isActive: false), Now)
            .Should().Match<SubscriptionPolicy.State>(x => x.IsBlocked && x.Reason == "suspended");
    }

    [Fact]
    public void Auto_suspend_disabled_keeps_access_after_grace_with_permanent_red_banner()
    {
        var s = Make(Now.AddDays(-30));
        s.AutoSuspendEnabled = false;
        var state = SubscriptionPolicy.Evaluate(s, Now);
        state.IsBlocked.Should().BeFalse();               // jamais bloquée automatiquement
        state.Level.Should().Be("danger");                // bannière rouge permanente
        state.Reason.Should().Be("expired");
    }

    [Fact]
    public void Manual_suspension_still_blocks_when_auto_suspend_disabled()
    {
        var s = Make(Now.AddDays(-30), "suspended");
        s.AutoSuspendEnabled = false;
        SubscriptionPolicy.Evaluate(s, Now).IsBlocked.Should().BeTrue();
    }

    [Fact]
    public void Legacy_expired_rows_with_isactive_false_follow_grace_not_suspension()
    {
        // L'ancien middleware posait IsActive=false à l'expiration : ces lignes
        // historiques doivent suivre le circuit expiration/grâce, pas la
        // suspension manuelle.
        var inGrace = SubscriptionPolicy.Evaluate(Make(Now.AddDays(-2), "expired", isActive: false), Now);
        inGrace.Reason.Should().Be("grace");
        inGrace.IsBlocked.Should().BeFalse();

        var beyond = SubscriptionPolicy.Evaluate(Make(Now.AddDays(-30), "expired", isActive: false), Now);
        beyond.Reason.Should().Be("expired");
        beyond.IsBlocked.Should().BeTrue();
    }
}
