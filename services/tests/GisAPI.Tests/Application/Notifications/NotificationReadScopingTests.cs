using FluentAssertions;
using GisAPI.Application.Features.Notifications.Queries.GetNotifications;
using GisAPI.Application.Features.Notifications.Queries.GetUnreadCount;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Notifications;

/// <summary>
/// PORTÉE VÉHICULE À LA LECTURE de la cloche — signalement client du 29/09/2026.
///
/// <para>Le 16/09 les PRODUCTEURS ont été cloisonnés (<c>NotificationAudience</c>) : plus
/// une seule ligne n'est adressée hors périmètre, et la production le confirme — le compte
/// de Kap Pharma (utilisateur 58, restreint à 2 véhicules chez HERTZ) n'a rien reçu depuis
/// le 16/09. Mais les lignes écrites AVANT lui restaient adressées : sa cloche affichait
/// encore 128 alertes de véhicules d'autres locataires, et le client a redit « je continue
/// de recevoir les notifications de toute la flotte ». Chez carthage@hertz.tn il y en avait
/// 10 353, dont 1 285 non lues.</para>
///
/// <para>Le correctif filtre à la LECTURE plutôt que d'effacer des lignes : réversible,
/// immédiat pour tous les clients à la fois, et il tient lieu de second verrou si un futur
/// producteur oubliait la règle. Ces tests figent les trois propriétés qui comptent : ce
/// qu'on masque, ce qu'on ne masque PAS, et le fait que la pastille compte exactement ce
/// que la liste affiche.</para>
/// </summary>
public class NotificationReadScopingTests
{
    private const int Societe = 4;              // HERTZ
    private const int Restreint = 58;           // Kap Pharma
    private const int Admin = 11;
    private const int VehiculeSien = 91;        // 245 TU 536
    private const int VehiculeAutrui = 274;     // 262 TU 639

    private static readonly DateTime Jour = new(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc);

    private static ICurrentTenantService Tenant(int userId, params string[] roles)
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.UserId).Returns(userId);
        m.Setup(x => x.CompanyId).Returns(Societe);
        m.Setup(x => x.UserRoles).Returns(roles);
        m.Setup(x => x.IsAuthenticated).Returns(true);
        m.Setup(x => x.IsSystemAdmin).Returns(roles.Contains("system_admin"));
        return m.Object;
    }

    /// <summary>L'opérateur restreint : aucun rôle d'administration.</summary>
    private static ICurrentTenantService Locataire() => Tenant(Restreint, "Operateur");

    private static ICurrentTenantService AdminSociete() => Tenant(Admin, "company_admin");

    private static void Notif(
        TestGisDbContext ctx, long id, int userId, string? referenceType, int? referenceId,
        bool lue = false)
    {
        ctx.Notifications.Add(new Notification
        {
            Id = id,
            UserId = userId,
            CompanyId = Societe,
            Type = "speed_alert",
            Title = $"Alerte {id}",
            Message = "…",
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            IsRead = lue,
            CreatedAt = Jour.AddMinutes(id)
        });
    }

    /// <summary>
    /// Le jeu de données du signalement, en miniature : l'opérateur n'a QU'UN véhicule
    /// affecté, et sa cloche contient aussi des lignes d'un véhicule d'autrui, une ligne
    /// sans véhicule, et une alerte de géofence.
    /// </summary>
    private static TestGisDbContext Parc()
    {
        var ctx = TestDbContextFactory.Create(Societe);

        ctx.UserVehicles.Add(new UserVehicle { Id = 1, UserId = Restreint, VehicleId = VehiculeSien });

        Notif(ctx, 1, Restreint, "vehicle", VehiculeSien);
        Notif(ctx, 2, Restreint, "vehicle", VehiculeAutrui);
        Notif(ctx, 3, Restreint, "vehicle", VehiculeAutrui, lue: true);
        Notif(ctx, 4, Restreint, "user", Restreint);
        Notif(ctx, 5, Restreint, "geofence", 21);
        Notif(ctx, 6, Restreint, null, null);

        // Adressées à quelqu'un d'autre : elles ne doivent jamais apparaître, et ce
        // filtre-là existait déjà.
        Notif(ctx, 7, Admin, "vehicle", VehiculeAutrui);

        ctx.SaveChanges();
        return ctx;
    }

    private static async Task<NotificationPageDto> Liste(TestGisDbContext ctx, ICurrentTenantService t) =>
        await new GetNotificationsQueryHandler(ctx, t)
            .Handle(new GetNotificationsQuery(PageSize: 50), CancellationToken.None);

    private static async Task<int> Pastille(TestGisDbContext ctx, ICurrentTenantService t) =>
        (await new GetUnreadCountQueryHandler(ctx, t)
            .Handle(new GetUnreadCountQuery(), CancellationToken.None)).Count;

    [Fact]
    public async Task Un_operateur_restreint_ne_voit_plus_les_alertes_des_vehicules_d_autrui()
    {
        using var ctx = Parc();

        var page = await Liste(ctx, Locataire());

        page.Items.Select(i => i.Id).Should().BeEquivalentTo(new long[] { 1, 4, 5, 6 },
            "les lignes 2 et 3 désignent un véhicule hors de son périmètre ; c'est exactement "
            + "ce que Kap Pharma voyait encore, 128 fois, après le cloisonnement des producteurs");
        page.TotalCount.Should().Be(4);
    }

    [Fact]
    public async Task Les_notifications_SANS_vehicule_restent_visibles()
    {
        using var ctx = Parc();

        var page = await Liste(ctx, Locataire());

        page.Items.Select(i => i.Id).Should().Contain(new long[] { 4, 6 },
            "une notification de compte ou sans référence ne relève pas de la portée véhicule : "
            + "la masquer priverait l'utilisateur de messages qui le concernent lui");
        page.Items.Select(i => i.Id).Should().Contain(5L,
            "limite assumée : l'alerte de géofence porte son véhicule dans le jsonb Metadata, "
            + "non filtrable en SQL de façon fiable — elle reste visible, et c'est documenté");
    }

    [Fact]
    public async Task La_pastille_compte_exactement_ce_que_la_liste_affiche()
    {
        using var ctx = Parc();
        var locataire = Locataire();

        var page = await Liste(ctx, locataire);
        var pastille = await Pastille(ctx, locataire);

        var nonLuesAffichees = page.Items.Count(i => !i.IsRead);
        pastille.Should().Be(nonLuesAffichees,
            "une pastille qui compte des notifications que la liste n'affiche pas ne retombe "
            + "jamais à zéro : le client cherche indéfiniment des messages invisibles");
        // Les quatre lignes visibles (1 son véhicule, 4 sans véhicule, 5 géofence,
        // 6 sans référence) sont toutes non lues ; les deux masquées ne comptent plus.
        pastille.Should().Be(4);
        page.UnreadCount.Should().Be(pastille, "les deux chemins doivent dire la même chose");
    }

    [Fact]
    public async Task Un_administrateur_de_societe_continue_de_tout_voir()
    {
        using var ctx = Parc();

        // L'administrateur 11 n'a AUCUNE ligne dans user_vehicles : c'est le cas piège.
        // Portée nulle = aucun filtre, surtout pas « il ne voit rien ».
        var page = await Liste(ctx, AdminSociete());

        page.Items.Select(i => i.Id).Should().BeEquivalentTo(new long[] { 7 },
            "il voit toutes SES notifications, sans filtre de périmètre — mais toujours pas "
            + "celles adressées à un autre utilisateur");
    }

    [Fact]
    public async Task Le_filtre_ne_remplace_pas_le_cloisonnement_par_destinataire()
    {
        using var ctx = Parc();

        var page = await Liste(ctx, Locataire());

        page.Items.Select(i => i.Id).Should().NotContain(7L,
            "la ligne 7 est adressée à l'administrateur : le filtre par UserId reste la "
            + "première barrière, la portée véhicule n'est que la seconde");
    }
}
