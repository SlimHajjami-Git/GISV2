using System.Text.Json;
using FluentAssertions;
using GisAPI.Application.Common;
using GisAPI.Domain.Entities;
using GisAPI.Hubs;
using GisAPI.Middleware;
using GisAPI.Services;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Services;

/// <summary>
/// Heures silencieuses, recette du 11/09/2026 : l'interrupteur n'écrivait que dans le
/// localStorage et rien ne le lisait. Ces tests vérifient l'effet RÉEL à l'envoi, sur le
/// vrai <see cref="NotificationService"/> : pendant la plage, la notification est créée et
/// part vers la cloche (SignalR, marquée Silent) mais sans push FCM ; « critical » passe
/// toujours. Et les routes « moi-même » ouvertes sans le droit Utilisateurs.
/// </summary>
public class QuietHoursDeliveryTests
{
    private const int CompanyId = 14;
    private const int UserId = 45;

    private sealed record Harness(NotificationService Service, Mock<IFcmService> Fcm, List<object?[]> SignalR, TestGisDbContext Ctx);

    /// <summary>Plage qui contient « maintenant » (ou non), en heure de la société (Africa/Tunis par défaut).</summary>
    private static (TimeSpan Start, TimeSpan End) Window(bool containsNow)
    {
        var tz = QuietHoursPolicy.ResolveTimeZone(null);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).TimeOfDay;
        TimeSpan Wrap(TimeSpan t) => TimeSpan.FromMinutes(((t.TotalMinutes % 1440) + 1440) % 1440);
        return containsNow
            ? (Wrap(local - TimeSpan.FromHours(1)), Wrap(local + TimeSpan.FromHours(1)))
            : (Wrap(local + TimeSpan.FromHours(2)), Wrap(local + TimeSpan.FromHours(4)));
    }

    private static async Task<Harness> BuildAsync(bool enabled, bool windowContainsNow, string accountType = UserAccountTypes.Staff)
    {
        var ctx = TestDbContextFactory.Create();
        var (start, end) = Window(windowContainsNow);
        ctx.Societes.Add(new Societe { Id = CompanyId, Name = "Belive GPA", SubscriptionStatus = "active", IsActive = true });
        ctx.Users.Add(new User
        {
            Id = UserId, FirstName = "Karim", Email = "recette@example.test", PasswordHash = "x", RoleId = 20,
            CompanyId = CompanyId, Status = "active", AccountType = accountType,
            QuietHoursEnabled = enabled, QuietHoursStart = start, QuietHoursEnd = end,
        });
        await ctx.SaveChangesAsync();

        var sent = new List<object?[]>();
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
             .Callback<string, object?[], CancellationToken>((method, args, _) => { if (method == "NewNotification") sent.Add(args); })
             .Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy.Object);
        var hub = new Mock<IHubContext<GpsHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);

        var fcm = new Mock<IFcmService>();
        var service = new NotificationService(ctx, hub.Object, fcm.Object, NullLogger<NotificationService>.Instance);
        return new Harness(service, fcm, sent, ctx);
    }

    private static bool SilentFlag(object?[] args) =>
        JsonSerializer.SerializeToElement(args[0]).GetProperty("Silent").GetBoolean();

    private static void VerifyFcm(Mock<IFcmService> fcm, Times times) =>
        fcm.Verify(f => f.SendToUserAsync(UserId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<int?>(), It.IsAny<string?>()), times);

    [Fact]
    public async Task Pendant_la_plage_la_notification_va_dans_la_cloche_sans_push_ni_toast()
    {
        var h = await BuildAsync(enabled: true, windowContainsNow: true);

        var n = await h.Service.CreateAndSendAsync(CompanyId, UserId, "document_expiry", "Assurance", "Échéance dans 7 jours", priority: "normal");

        h.Ctx.Notifications.Should().ContainSingle(x => x.Id == n.Id, "la notification reste enregistrée pour la cloche");
        h.SignalR.Should().ContainSingle();
        SilentFlag(h.SignalR[0]).Should().BeTrue("le toast est supprimé à l'écran");
        VerifyFcm(h.Fcm, Times.Never());
    }

    [Fact]
    public async Task Un_accident_critical_passe_malgre_la_plage()
    {
        var h = await BuildAsync(enabled: true, windowContainsNow: true);

        await h.Service.CreateAndSendAsync(CompanyId, UserId, "accident", "Accident", "Choc détecté", priority: "critical");

        SilentFlag(h.SignalR.Single()).Should().BeFalse();
        VerifyFcm(h.Fcm, Times.Once());
    }

    [Fact]
    public async Task Hors_de_la_plage_tout_part_normalement()
    {
        var h = await BuildAsync(enabled: true, windowContainsNow: false);

        await h.Service.CreateAndSendAsync(CompanyId, UserId, "document_expiry", "Assurance", "Échéance", priority: "normal");

        SilentFlag(h.SignalR.Single()).Should().BeFalse();
        VerifyFcm(h.Fcm, Times.Once());
    }

    [Fact]
    public async Task Desactivees_les_heures_silencieuses_ne_changent_rien()
    {
        var h = await BuildAsync(enabled: false, windowContainsNow: true);

        await h.Service.CreateAndSendAsync(CompanyId, UserId, "document_expiry", "Assurance", "Échéance", priority: "normal");

        SilentFlag(h.SignalR.Single()).Should().BeFalse();
        VerifyFcm(h.Fcm, Times.Once());
    }

    // Réponse du client (11/09/2026) : remorquage et panne de démarrage passent la nuit.
    [Theory]
    [InlineData("tow_detected")]
    [InlineData("accident_tow_detected")]
    [InlineData("start_failure")]
    public async Task Remorquage_et_panne_de_demarrage_passent_malgre_la_plage(string type)
    {
        var h = await BuildAsync(enabled: true, windowContainsNow: true);

        await h.Service.CreateAndSendAsync(CompanyId, UserId, type, "Alerte", "Véhicule concerné", priority: "high");

        SilentFlag(h.SignalR.Single()).Should().BeFalse();
        VerifyFcm(h.Fcm, Times.Once());
    }

    [Fact]
    public async Task Une_echeance_de_document_meme_en_priorite_haute_reste_silencieuse()
    {
        var h = await BuildAsync(enabled: true, windowContainsNow: true);

        await h.Service.CreateAndSendAsync(CompanyId, UserId, "document_expiry", "Assurance", "Échéance demain", priority: "high");

        SilentFlag(h.SignalR.Single()).Should().BeTrue("ce n'est pas la priorité qui décide, mais le type");
        VerifyFcm(h.Fcm, Times.Never());
    }

    // ── Compte chauffeur (migration 050) ─────────────────────────────────────────

    [Fact]
    public async Task Un_chauffeur_recoit_sa_tournee_meme_dans_des_heures_silencieuses_heritees()
    {
        // Salarié converti avec une plage 22:00→07:00 : « Nouvelle tournée » (high) envoyée à
        // 06:30 n'était pas poussée, et sans cloche (ligne marquée lue) il ne la voyait jamais.
        var h = await BuildAsync(enabled: true, windowContainsNow: true, accountType: UserAccountTypes.Driver);

        await h.Service.CreateAndSendAsync(CompanyId, UserId, "tour_assigned", "Nouvelle tournée", "Départ 07:00", priority: "high");

        VerifyFcm(h.Fcm, Times.Once());
        SilentFlag(h.SignalR.Single()).Should().BeFalse();
    }

    [Fact]
    public async Task Une_notification_refusee_par_la_base_ne_bloque_pas_l_enregistrement_suivant_du_meme_contexte()
    {
        // Destinataire supprimé (id pendu) : l'INSERT lève une violation de clé étrangère. La
        // notification restait « Added » dans le contexte partagé du scope, et le SaveChanges
        // suivant de l'appelant (clôture de tournée du moniteur) échouait à son tour.
        var h = await BuildAsync(enabled: false, windowContainsNow: false);
        await h.Ctx.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;");

        var envoi = async () => await h.Service.CreateAndSendAsync(CompanyId, 999, "tour_waypoint", "Étape", "Arrivé", priority: "normal");
        await envoi.Should().ThrowAsync<DbUpdateException>();

        h.Ctx.ChangeTracker.Entries<Notification>().Should().BeEmpty("la ligne refusée est abandonnée");
        var societe = await h.Ctx.Societes.SingleAsync(s => s.Id == CompanyId);
        societe.Name = "Belive GPA (suivi)";   // l'écriture suivante de l'appelant
        var enregistrement = async () => await h.Ctx.SaveChangesAsync();
        await enregistrement.Should().NotThrowAsync();
    }

    // ── Routes « moi-même » ouvertes sans le droit Utilisateurs ─────────────────

    [Theory]
    [InlineData("/api/users/me", "GET", true)]
    [InlineData("/api/users/me/quiet-hours", "PUT", true)]
    [InlineData("/api/users/me/password", "PUT", true)]
    [InlineData("/api/users/me", "PUT", false)]              // nom et e-mail : restent réservés à CanUsers
    [InlineData("/api/users/me/quiet-hours", "GET", false)]
    [InlineData("/api/users/me/autre", "PUT", false)]
    [InlineData("/api/users/5", "GET", false)]
    [InlineData("/api/users", "GET", false)]
    [InlineData("/api/users/me/", "GET", false)]
    public void Seules_les_routes_du_compte_de_l_appelant_sont_ouvertes(string path, string method, bool open)
        => PermissionMiddleware.IsSelfServiceUserRoute(path, method).Should().Be(open);
}
