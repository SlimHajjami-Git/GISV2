using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Services;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Alerts;

/// <summary>
/// The client scenario: one colleague opts OUT of "entretien" emails while the
/// other keeps them. The per-user User.Alert* flags must drive recipients, with
/// a legacy admin fallback so a company that configured nothing keeps receiving.
/// </summary>
public class AlertEmailDispatcherTests
{
    private const int CompanyId = 1;

    private static (AlertEmailDispatcher Dispatcher, List<string> Sent) Build(TestGisDbContext ctx)
    {
        var sent = new List<string>();
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendNotificationEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback((string to, string _, string _, string _, string _, string? _, CancellationToken _) => sent.Add(to))
            .Returns(Task.CompletedTask);

        return (new AlertEmailDispatcher(ctx, email.Object, NullLogger<AlertEmailDispatcher>.Instance), sent);
    }

    private static Role AdminRole(int id = 1) => new()
    {
        Id = id, Name = "Administrateur", SocieteId = CompanyId, IsCompanyAdmin = true
    };

    private static User AdminUser(int id, string email, bool entretien, int roleId = 1) => new()
    {
        Id = id, CompanyId = CompanyId, Email = email, FirstName = "U", LastName = id.ToString(),
        Status = "active", RoleId = roleId, AlertEntretien = entretien
    };

    [Fact]
    public async Task Colleague_who_opted_in_receives_entretien_the_one_who_opted_out_does_not()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Roles.Add(AdminRole());
        ctx.Users.Add(AdminUser(1, "a@corp.tn", entretien: false)); // opted OUT
        ctx.Users.Add(AdminUser(2, "b@corp.tn", entretien: true));  // opted IN
        await ctx.SaveChangesAsync();

        var (dispatcher, sent) = Build(ctx);
        await dispatcher.DispatchAsync(CompanyId, "entretien", "Entretien dû", "…");

        sent.Should().BeEquivalentTo(new[] { "b@corp.tn" },
            "once someone opts in, the per-user list is authoritative — A is excluded, B kept");
    }

    [Fact]
    public async Task No_per_user_config_falls_back_to_all_admins()
    {
        // Nobody ticked entretien → legacy behaviour: every active admin receives,
        // so a company that never configured anything doesn't silently go dark.
        using var ctx = TestDbContextFactory.Create();
        ctx.Roles.Add(AdminRole());
        ctx.Users.Add(AdminUser(1, "a@corp.tn", entretien: false));
        ctx.Users.Add(AdminUser(2, "b@corp.tn", entretien: false));
        await ctx.SaveChangesAsync();

        var (dispatcher, sent) = Build(ctx);
        await dispatcher.DispatchAsync(CompanyId, "entretien", "Entretien dû", "…");

        sent.Should().BeEquivalentTo(new[] { "a@corp.tn", "b@corp.tn" });
    }

    [Fact]
    public async Task External_alert_emails_are_unioned_with_opted_in_users()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Roles.Add(AdminRole());
        ctx.Users.Add(AdminUser(2, "b@corp.tn", entretien: true));
        ctx.AlertEmails.Add(new AlertEmail { CompanyId = CompanyId, Email = "manager@ext.com", AlertType = "entretien" });
        await ctx.SaveChangesAsync();

        var (dispatcher, sent) = Build(ctx);
        await dispatcher.DispatchAsync(CompanyId, "entretien", "Entretien dû", "…");

        sent.Should().BeEquivalentTo(new[] { "b@corp.tn", "manager@ext.com" });
    }

    [Fact]
    public async Task Opting_out_of_entretien_does_not_affect_other_alert_types()
    {
        // A opts out of entretien but stays subscribed to assurance — the flags
        // are independent per type.
        using var ctx = TestDbContextFactory.Create();
        ctx.Roles.Add(AdminRole());
        var a = AdminUser(1, "a@corp.tn", entretien: false);
        a.AlertAssurance = true;
        ctx.Users.Add(a);
        await ctx.SaveChangesAsync();

        var (dispatcher, sent) = Build(ctx);
        await dispatcher.DispatchAsync(CompanyId, "assurance", "Assurance expire", "…");

        sent.Should().BeEquivalentTo(new[] { "a@corp.tn" }, "A opted in to assurance");
    }

    [Fact]
    public async Task Explicitly_saved_all_unchecked_preferences_silence_the_alert_no_admin_fallback()
    {
        // THE client bug: the admin unchecked every box and saved — yet the old
        // fallback re-subscribed all admins because "nobody opted in" looked
        // identical to "never configured". AlertPrefsConfigured disambiguates.
        using var ctx = TestDbContextFactory.Create();
        ctx.Roles.Add(AdminRole());
        var a = AdminUser(1, "a@corp.tn", entretien: false);
        a.AlertPrefsConfigured = true;   // preferences explicitly saved (all unchecked)
        ctx.Users.Add(a);
        ctx.Users.Add(AdminUser(2, "b@corp.tn", entretien: false));
        await ctx.SaveChangesAsync();

        var (dispatcher, sent) = Build(ctx);
        await dispatcher.DispatchAsync(CompanyId, "entretien", "Entretien dû", "…");

        sent.Should().BeEmpty("someone explicitly saved preferences with nobody opted in — that's a deliberate opt-out, not a missing config");
    }

    [Fact]
    public async Task Never_configured_company_still_gets_the_admin_fallback()
    {
        // Safety net preserved: no AlertPrefsConfigured anywhere → legacy fallback,
        // so companies that never opened the alert settings keep receiving.
        using var ctx = TestDbContextFactory.Create();
        ctx.Roles.Add(AdminRole());
        ctx.Users.Add(AdminUser(1, "a@corp.tn", entretien: false));
        await ctx.SaveChangesAsync();

        var (dispatcher, sent) = Build(ctx);
        await dispatcher.DispatchAsync(CompanyId, "entretien", "Entretien dû", "…");

        sent.Should().BeEquivalentTo(new[] { "a@corp.tn" });
    }

    [Fact]
    public async Task Accident_alerts_keep_the_admin_fallback_even_when_preferences_are_configured()
    {
        // "accident" has no per-user checkbox — an explicit opt-out of the four
        // document types must NOT silence accident notifications.
        using var ctx = TestDbContextFactory.Create();
        ctx.Roles.Add(AdminRole());
        var a = AdminUser(1, "a@corp.tn", entretien: false);
        a.AlertPrefsConfigured = true;
        ctx.Users.Add(a);
        await ctx.SaveChangesAsync();

        var (dispatcher, sent) = Build(ctx);
        await dispatcher.DispatchAsync(CompanyId, "accident", "Accident détecté", "…");

        sent.Should().BeEquivalentTo(new[] { "a@corp.tn" }, "accidents must never go unnoticed");
    }
}
