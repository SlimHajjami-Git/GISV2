using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Notifications;

/// <summary>
/// Verrouille le cloisonnement par véhicule des notifications.
///
/// <para>Incident Hertz du 15/09/2026 : les producteurs d'alertes faisaient
/// « Notify all active users in the company ». Un opérateur affecté à 2 véhicules
/// sur 279 a reçu 125 excès de vitesse en 18 h, dont <b>100 %</b> portaient sur
/// des véhicules qui ne lui étaient pas affectés. Les trois opérateurs de la
/// société recevaient rigoureusement les mêmes alertes alors qu'ils avaient 2, 36
/// et 187 véhicules affectés — preuve que l'affectation n'était jamais lue.</para>
///
/// <para>Le contrat figé ici :</para>
/// <list type="bullet">
///   <item><description>un utilisateur simple n'est notifié que des véhicules
///     qui lui sont affectés (table UserVehicles) ;</description></item>
///   <item><description>un administrateur est notifié de TOUT le parc, <b>y
///     compris quand il n'a aucune affectation</b> — cas réel chez Hertz, une
///     administratrice a 0 ligne dans UserVehicles et doit tout recevoir ;</description></item>
///   <item><description>sans véhicule identifiable, repli sur les
///     administrateurs seuls (fail-closed), jamais sur toute la société ;</description></item>
///   <item><description>cloisonnement inter-sociétés préservé ;</description></item>
///   <item><description>les comptes inactifs ne sont jamais notifiés.</description></item>
/// </list>
/// </summary>
public class NotificationVehicleScopingTests
{
    private const int CompanyId = 4;          // HERTZ
    private const int OtherCompanyId = 9;
    private const int VehicleAssigned = 91;   // 245 TU 536
    private const int VehicleForeign = 274;   // 262 TU 639 — hors périmètre

    private static void SeedUser(
        TestGisDbContext context,
        int id,
        int companyId,
        bool isAdmin,
        string status = "active",
        bool isSystemRole = false)
    {
        var role = new Role
        {
            Id = 3000 + id,
            Name = isAdmin ? $"Administrateur-{id}" : $"Operateur-{id}",
            IsCompanyAdmin = isAdmin,
            IsSystemRole = isSystemRole,
            SocieteId = companyId
        };
        context.Roles.Add(role);

        context.Users.Add(new User
        {
            Id = id,
            FirstName = $"U{id}",
            LastName = "T",
            Email = $"u{id}@t.com",
            PasswordHash = "x",
            RoleId = role.Id,
            CompanyId = companyId,
            Status = status
        });
    }

    private static void Assign(TestGisDbContext context, int userId, int vehicleId)
    {
        context.UserVehicles.Add(new UserVehicle
        {
            Id = userId * 1000 + vehicleId,
            UserId = userId,
            VehicleId = vehicleId
        });
    }

    private static SpeedAlertNotificationEvent SpeedAlertOn(int? vehicleId) =>
        new(
            CompanyId: CompanyId,
            VehicleId: vehicleId,
            VehicleName: "262 TU 639",
            Plate: "262 TU 639",
            SpeedKph: 116,
            Latitude: 36.8,
            Longitude: 10.18,
            Timestamp: new DateTime(2026, 9, 15, 16, 0, 0, DateTimeKind.Utc),
            SpeedLimitKph: 90);

    private static List<int> NotifiedUserIds(Mock<INotificationService> mock)
    {
        var ids = new List<int>();
        foreach (var inv in mock.Invocations)
        {
            if (inv.Method.Name == nameof(INotificationService.CreateAndSendAsync))
                ids.Add((int)inv.Arguments[1]!);   // userId
        }
        return ids;
    }

    // ── NotificationAudience : la définition unique ──────────────────────────

    [Fact]
    public async Task Audience_ExclutLOperateurNonAffecteAuVehicule()
    {
        using var context = TestDbContextFactory.Create();
        SeedUser(context, 58, CompanyId, isAdmin: false);   // KAP Pharma
        Assign(context, 58, VehicleAssigned);               // affecté ailleurs
        await context.SaveChangesAsync();

        var audience = await NotificationAudience.ForVehicleAsync(context, CompanyId, VehicleForeign);

        audience.Should().NotContain(58,
            "un opérateur affecté à un autre véhicule n'a pas à être notifié de celui-ci");
    }

    [Fact]
    public async Task Audience_IncluitLOperateurAffecteAuVehicule()
    {
        using var context = TestDbContextFactory.Create();
        SeedUser(context, 58, CompanyId, isAdmin: false);
        Assign(context, 58, VehicleAssigned);
        await context.SaveChangesAsync();

        var audience = await NotificationAudience.ForVehicleAsync(context, CompanyId, VehicleAssigned);

        audience.Should().Contain(58);
    }

    [Fact]
    public async Task Audience_IncluitLAdminMemeSansAucuneAffectation()
    {
        using var context = TestDbContextFactory.Create();
        SeedUser(context, 11, CompanyId, isAdmin: true);    // Khouloud : 0 affectation
        await context.SaveChangesAsync();

        var audience = await NotificationAudience.ForVehicleAsync(context, CompanyId, VehicleForeign);

        audience.Should().Contain(11,
            "un administrateur voit tout le parc, l'absence d'affectation ne doit pas le priver d'alertes");
    }

    [Fact]
    public async Task Audience_IncluitLeRoleSysteme()
    {
        using var context = TestDbContextFactory.Create();
        SeedUser(context, 1, CompanyId, isAdmin: false, isSystemRole: true);
        await context.SaveChangesAsync();

        var audience = await NotificationAudience.ForVehicleAsync(context, CompanyId, VehicleForeign);

        audience.Should().Contain(1);
    }

    [Fact]
    public async Task Audience_ExclutLesComptesInactifs()
    {
        using var context = TestDbContextFactory.Create();
        SeedUser(context, 12, CompanyId, isAdmin: true, status: "inactive");
        SeedUser(context, 13, CompanyId, isAdmin: false, status: "inactive");
        Assign(context, 13, VehicleForeign);
        await context.SaveChangesAsync();

        var audience = await NotificationAudience.ForVehicleAsync(context, CompanyId, VehicleForeign);

        audience.Should().BeEmpty();
    }

    [Fact]
    public async Task Audience_NeFranchitPasLaFrontiereDeSociete()
    {
        using var context = TestDbContextFactory.Create();
        SeedUser(context, 70, OtherCompanyId, isAdmin: true);
        SeedUser(context, 71, OtherCompanyId, isAdmin: false);
        Assign(context, 71, VehicleForeign);               // affectation croisée aberrante
        await context.SaveChangesAsync();

        var audience = await NotificationAudience.ForVehicleAsync(context, CompanyId, VehicleForeign);

        audience.Should().BeEmpty("aucun compte d'une autre société ne doit être notifié");
    }

    [Fact]
    public async Task Audience_SansVehicule_SeReplieSurLesAdmins()
    {
        using var context = TestDbContextFactory.Create();
        SeedUser(context, 11, CompanyId, isAdmin: true);
        SeedUser(context, 58, CompanyId, isAdmin: false);
        Assign(context, 58, VehicleAssigned);
        await context.SaveChangesAsync();

        var audience = await NotificationAudience.ForVehicleAsync(context, CompanyId, null);

        audience.Should().BeEquivalentTo(new[] { 11 },
            "sans véhicule on ne sait pas cloisonner : on notifie moins, pas plus");
    }

    // ── Le handler qui a produit les 125 fausses alertes ─────────────────────

    [Fact]
    public async Task ExcesDeVitesse_NeNotifiePasLOperateurHorsPerimetre()
    {
        using var context = TestDbContextFactory.Create();
        SeedUser(context, 11, CompanyId, isAdmin: true);    // admin, 0 affectation
        SeedUser(context, 58, CompanyId, isAdmin: false);   // KAP Pharma, 2 véhicules
        Assign(context, 58, VehicleAssigned);
        SeedUser(context, 37, CompanyId, isAdmin: false);   // opérateur affecté au véhicule visé
        Assign(context, 37, VehicleForeign);
        await context.SaveChangesAsync();

        var notif = new Mock<INotificationService>();
        var handler = new SpeedAlertNotificationHandler(
            notif.Object, context, NullLogger<SpeedAlertNotificationHandler>.Instance);

        await handler.Handle(SpeedAlertOn(VehicleForeign), CancellationToken.None);

        var notified = NotifiedUserIds(notif);
        notified.Should().BeEquivalentTo(new[] { 11, 37 });
        notified.Should().NotContain(58,
            "c'est exactement la fuite constatée chez Hertz : 125 alertes sur des véhicules tiers");
    }

    [Fact]
    public async Task ExcesDeVitesse_SansDestinataire_NEcritRien()
    {
        using var context = TestDbContextFactory.Create();
        SeedUser(context, 58, CompanyId, isAdmin: false);
        Assign(context, 58, VehicleAssigned);
        await context.SaveChangesAsync();

        var notif = new Mock<INotificationService>();
        var handler = new SpeedAlertNotificationHandler(
            notif.Object, context, NullLogger<SpeedAlertNotificationHandler>.Instance);

        await handler.Handle(SpeedAlertOn(VehicleForeign), CancellationToken.None);

        NotifiedUserIds(notif).Should().BeEmpty(
            "liste vide = personne à prévenir, jamais un repli sur toute la société");
    }
}
