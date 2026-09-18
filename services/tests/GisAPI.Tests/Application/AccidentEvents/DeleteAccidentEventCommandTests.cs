using FluentAssertions;
using GisAPI.Application.Features.AccidentEvents.Commands;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Application.AccidentEvents;

/// <summary>
/// Demande du 18/09/2026 : l'écran Sinistres n'avait aucun bouton « Supprimer » et
/// l'API aucune route de suppression. Règles retenues : l'argent reste (dépenses
/// détachées, pas supprimées), les pièces du dossier partent (documents, tiers et
/// leurs fichiers), la suppression est réservée à un administrateur de société ou
/// au droit Sinistres, et ne franchit jamais la frontière de société.
/// </summary>
public class DeleteAccidentEventCommandTests : IDisposable
{
    private const int Societe = 7;
    private const int AutreSociete = 8;
    private const int SinistreId = 1;
    private const int VehiculeId = 49;
    private const int AdminId = 10;
    private const int ChargeSinistresId = 11;
    private const int SansDroitId = 12;

    private readonly string _uploadsRoot = Path.Combine(
        Path.GetTempPath(), "gisv2-tests-sinistres", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_uploadsRoot)) Directory.Delete(_uploadsRoot, recursive: true); }
        catch (IOException) { }
    }

    /// <summary>Écrit un fichier sous la racine des uploads et renvoie son URL publique.</summary>
    private string CreerFichier(string nom, int accidentId = SinistreId)
    {
        var dossier = Path.Combine(_uploadsRoot, "accident-reports", accidentId.ToString());
        Directory.CreateDirectory(dossier);
        File.WriteAllText(Path.Combine(dossier, nom), "contenu de test");
        return $"/uploads/accident-reports/{accidentId}/{nom}";
    }

    private string CheminDisque(string url) =>
        Path.Combine(_uploadsRoot, url["/uploads/".Length..].Replace('/', Path.DirectorySeparatorChar));

    private static TestGisDbContext CreerContexte(int societeDuSinistre = Societe)
    {
        var context = TestDbContextFactory.Create();

        context.Roles.Add(new Role { Id = 1, Name = "Administrateur", SocieteId = Societe, IsCompanyAdmin = true });
        context.Roles.Add(new Role { Id = 2, Name = "Gestionnaire", SocieteId = Societe });
        context.Users.Add(Admin());
        context.Users.Add(ChargeSinistres());
        context.Users.Add(SansDroit());

        context.Vehicles.Add(TestDataBuilder.CreateVehicle(id: VehiculeId, companyId: Societe));
        context.AccidentEvents.Add(new AccidentEvent
        {
            Id = SinistreId,
            CompanyId = societeDuSinistre,
            VehicleId = VehiculeId,
            DeviceUid = string.Empty,
            IncidentAt = new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc),
            Confidence = 100,
            Origin = "manual",
            Status = "confirmed",
            ReferenceCode = "SIN-2026-0007",
        });
        context.SaveChanges();
        context.ChangeTracker.Clear();
        return context;
    }

    private static User Admin()
    {
        var u = TestDataBuilder.CreateUser(id: AdminId, companyId: Societe, email: "admin@test.tn");
        u.RoleId = 1;
        u.AccessLevel = "admin";
        return u;
    }

    private static User ChargeSinistres()
    {
        var u = TestDataBuilder.CreateUser(id: ChargeSinistresId, companyId: Societe, email: "sinistres@test.tn");
        u.RoleId = 2;
        u.AccessLevel = "user";
        u.CanAccidents = true;
        return u;
    }

    private static User SansDroit()
    {
        var u = TestDataBuilder.CreateUser(id: SansDroitId, companyId: Societe, email: "lecteur@test.tn");
        u.RoleId = 2;
        u.AccessLevel = "user";
        u.CanAccidents = false;
        return u;
    }

    private static ICurrentTenantService Tenant(int userId, int companyId = Societe)
        => TestDbContextFactory.CreateMockTenantService(companyId: companyId, userId: userId).Object;

    private static DeleteAccidentEventCommandHandler Handler(TestGisDbContext context, int userId, int companyId = Societe)
        => new(context, Tenant(userId, companyId), NullLogger<DeleteAccidentEventCommandHandler>.Instance);

    [Fact]
    public async Task Admin_SupprimeLeDossierSesDocumentsSesTiersEtSesFichiers()
    {
        using var context = CreerContexte();
        var pdf = CreerFichier("rapport.pdf");
        var piece = CreerFichier("constat.jpg");

        var ev = await context.AccidentEvents.SingleAsync(e => e.Id == SinistreId);
        ev.PdfReportUrl = pdf;
        context.AccidentEventDocuments.Add(new AccidentEventDocument
        {
            Id = 1,
            AccidentEventId = SinistreId,
            DocumentType = "police_report",
            FileName = "constat.jpg",
            FileUrl = piece,
        });
        context.AccidentEventThirdParties.Add(new AccidentEventThirdParty
        {
            Id = 1,
            AccidentEventId = SinistreId,
            Name = "Tiers QA",
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var resultat = await Handler(context, AdminId).Handle(
            new DeleteAccidentEventCommand(SinistreId, _uploadsRoot), CancellationToken.None);

        context.ChangeTracker.Clear();
        resultat.Reference.Should().Be("SIN-2026-0007");
        resultat.DeletedDocuments.Should().Be(1);
        resultat.DeletedThirdParties.Should().Be(1);
        resultat.DeletedFiles.Should().Be(2);
        (await context.AccidentEvents.CountAsync()).Should().Be(0);
        (await context.AccidentEventDocuments.CountAsync()).Should().Be(0);
        (await context.AccidentEventThirdParties.CountAsync()).Should().Be(0);
        File.Exists(CheminDisque(pdf)).Should().BeFalse();
        File.Exists(CheminDisque(piece)).Should().BeFalse();
        // Le répertoire du dossier ne doit pas rester vide sur le disque.
        Directory.Exists(Path.Combine(_uploadsRoot, "accident-reports", SinistreId.ToString()))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Suppression_RetireLesNotificationsDuDossierEtGardeLesAutres()
    {
        using var context = CreerContexte();
        context.Notifications.Add(Notif(1, "accident_event", SinistreId, Societe));
        context.Notifications.Add(Notif(2, "accident_event", SinistreId, Societe));
        // Autre dossier, autre type, autre société : rien de tout cela ne doit partir.
        context.Notifications.Add(Notif(3, "accident_event", 99, Societe));
        context.Notifications.Add(Notif(4, "maintenance", SinistreId, Societe));
        context.Notifications.Add(Notif(5, "accident_event", SinistreId, AutreSociete));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var resultat = await Handler(context, AdminId).Handle(
            new DeleteAccidentEventCommand(SinistreId, _uploadsRoot), CancellationToken.None);

        context.ChangeTracker.Clear();
        resultat.DeletedNotifications.Should().Be(2);
        var restantes = await context.Notifications.AsNoTracking().OrderBy(n => n.Id).ToListAsync();
        restantes.Select(n => n.Id).Should().Equal(3L, 4L, 5L);
    }

    [Fact]
    public async Task UnFichierHorsDuDossierNEstPasEfface()
    {
        using var context = CreerContexte();
        // Un PDF rangé sous un AUTRE dossier : la suppression ne doit pas y toucher,
        // comme la suppression d'un document unitaire qui filtre sur son préfixe.
        var voisin = CreerFichier("rapport-voisin.pdf", accidentId: 42);
        var ev = await context.AccidentEvents.SingleAsync(e => e.Id == SinistreId);
        ev.PdfReportUrl = voisin;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var resultat = await Handler(context, AdminId).Handle(
            new DeleteAccidentEventCommand(SinistreId, _uploadsRoot), CancellationToken.None);

        resultat.DeletedFiles.Should().Be(0);
        File.Exists(CheminDisque(voisin)).Should().BeTrue();
    }

    private static Notification Notif(long id, string referenceType, int referenceId, int companyId)
        => new()
        {
            Id = id,
            CompanyId = companyId,
            UserId = AdminId,
            Type = "accident_detected",
            Title = "Accident détecté sur votre véhicule",
            Message = "Notification de test",
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            ActionUrl = $"/rapport-accident/{referenceId}",
        };

    [Fact]
    public async Task Suppression_LesDepensesDuDossierSontConserveesEtDetachees()
    {
        using var context = CreerContexte();
        context.VehicleCosts.Add(new VehicleCost
        {
            Id = 1,
            VehicleId = VehiculeId,
            CompanyId = Societe,
            AccidentEventId = SinistreId,
            Type = "repair",
            Description = "Réparation accident — SIN-2026-0007",
            Amount = 1200m,
            Date = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc),
        });
        context.VehicleCosts.Add(new VehicleCost
        {
            Id = 2,
            VehicleId = VehiculeId,
            CompanyId = Societe,
            AccidentEventId = SinistreId,
            Type = "insurance_refund",
            Description = "Remboursement assurance — SIN-2026-0007",
            Amount = 900m,
            Date = new DateTime(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc),
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var resultat = await Handler(context, AdminId).Handle(
            new DeleteAccidentEventCommand(SinistreId, _uploadsRoot), CancellationToken.None);

        context.ChangeTracker.Clear();
        resultat.DetachedCosts.Should().Be(2);
        var couts = await context.VehicleCosts.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
        couts.Should().HaveCount(2);
        couts.Should().OnlyContain(c => c.AccidentEventId == null);
        couts.Sum(c => c.Amount).Should().Be(2100m);
    }

    [Fact]
    public async Task DroitSinistres_SansEtreAdministrateur_PeutSupprimer()
    {
        using var context = CreerContexte();

        await Handler(context, ChargeSinistresId).Handle(
            new DeleteAccidentEventCommand(SinistreId, _uploadsRoot), CancellationToken.None);

        context.ChangeTracker.Clear();
        (await context.AccidentEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SansDroitSinistresNiAdministrateur_RefusEtDossierIntact()
    {
        using var context = CreerContexte();
        var handler = Handler(context, SansDroitId);

        var refus = await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            handler.Handle(new DeleteAccidentEventCommand(SinistreId, _uploadsRoot), CancellationToken.None));

        refus.Message.Should().Contain("Sinistres");
        context.ChangeTracker.Clear();
        (await context.AccidentEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DossierDUneAutreSociete_Introuvable()
    {
        using var context = CreerContexte(societeDuSinistre: AutreSociete);
        var handler = Handler(context, AdminId);

        var absent = await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new DeleteAccidentEventCommand(SinistreId, _uploadsRoot), CancellationToken.None));

        absent.Message.Should().Be("Dossier de sinistre introuvable.");
        context.ChangeTracker.Clear();
        (await context.AccidentEvents.CountAsync()).Should().Be(1);
    }

    // ── Portée véhicules (revue de l'intégration du 18/09/2026) ───────────────────
    // La fiche refusait (404) un dossier hors des véhicules de l'employé, la suppression
    // l'acceptait : un employé restreint avec le droit Sinistres effaçait des dossiers
    // qu'il ne pouvait même pas ouvrir.

    private const int AutreVehiculeId = 50;

    /// <summary>Employé NON administrateur (rôle hors liste admin) avec le droit Sinistres.</summary>
    private static DeleteAccidentEventCommandHandler HandlerRestreint(TestGisDbContext context)
    {
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: Societe, userId: ChargeSinistresId);
        tenant.Setup(t => t.UserRoles).Returns(new[] { "user" });
        return new(context, tenant.Object, NullLogger<DeleteAccidentEventCommandHandler>.Instance);
    }

    private static async Task AffecterAsync(TestGisDbContext context, int vehicleId)
    {
        context.Vehicles.Add(TestDataBuilder.CreateVehicle(id: AutreVehiculeId, companyId: Societe));
        context.UserVehicles.Add(new UserVehicle { UserId = ChargeSinistresId, VehicleId = vehicleId });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task EmployeRestreint_DossierDUnVehiculeNonAffecte_IntrouvableEtIntact()
    {
        using var context = CreerContexte();
        await AffecterAsync(context, AutreVehiculeId); // le sinistre porte sur VehiculeId

        var absent = await Assert.ThrowsAsync<NotFoundException>(() =>
            HandlerRestreint(context).Handle(new DeleteAccidentEventCommand(SinistreId, _uploadsRoot), CancellationToken.None));

        absent.Message.Should().Be("Dossier de sinistre introuvable.");
        context.ChangeTracker.Clear();
        (await context.AccidentEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task EmployeRestreint_DossierSansVehicule_Introuvable()
    {
        using var context = CreerContexte();
        await AffecterAsync(context, VehiculeId);
        var ev = await context.AccidentEvents.SingleAsync(e => e.Id == SinistreId);
        ev.VehicleId = null; // même règle que la fiche : réservé aux administrateurs
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            HandlerRestreint(context).Handle(new DeleteAccidentEventCommand(SinistreId, _uploadsRoot), CancellationToken.None));

        context.ChangeTracker.Clear();
        (await context.AccidentEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task EmployeRestreint_DossierDeSonVehicule_Supprime()
    {
        using var context = CreerContexte();
        await AffecterAsync(context, VehiculeId);

        await HandlerRestreint(context).Handle(new DeleteAccidentEventCommand(SinistreId, _uploadsRoot), CancellationToken.None);

        context.ChangeTracker.Clear();
        (await context.AccidentEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task EmployeRestreint_LaListeNeMontreQueLesDossiersDeSesVehicules()
    {
        using var context = CreerContexte();
        await AffecterAsync(context, AutreVehiculeId);
        context.AccidentEvents.Add(new AccidentEvent
        {
            Id = 2, CompanyId = Societe, VehicleId = AutreVehiculeId, DeviceUid = string.Empty,
            IncidentAt = new DateTime(2026, 9, 11, 8, 0, 0, DateTimeKind.Utc), Confidence = 100,
            Origin = "manual", Status = "confirmed",
        });
        context.AccidentEvents.Add(new AccidentEvent
        {
            Id = 3, CompanyId = Societe, VehicleId = null, DeviceUid = string.Empty,
            IncidentAt = new DateTime(2026, 9, 12, 8, 0, 0, DateTimeKind.Utc), Confidence = 100,
            Origin = "manual", Status = "confirmed",
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: Societe, userId: ChargeSinistresId);
        tenant.Setup(t => t.UserRoles).Returns(new[] { "user" });
        var restreint = await new GisAPI.Application.Features.AccidentEvents.Queries.ListAccidentEventsQueryHandler(context, tenant.Object)
            .Handle(new GisAPI.Application.Features.AccidentEvents.Queries.ListAccidentEventsQuery(), CancellationToken.None);
        var admin = await new GisAPI.Application.Features.AccidentEvents.Queries.ListAccidentEventsQueryHandler(context, Tenant(AdminId))
            .Handle(new GisAPI.Application.Features.AccidentEvents.Queries.ListAccidentEventsQuery(), CancellationToken.None);

        restreint.Items.Select(i => i.Id).Should().Equal(2);
        restreint.TotalCount.Should().Be(1);
        admin.Items.Select(i => i.Id).Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    // ── Redétection : un dossier détecté récent serait recréé par la détection ────

    [Fact]
    public async Task DossierDetecteIlYAMoinsDe30Minutes_Refuse409EtIntact()
    {
        using var context = CreerContexte();
        var ev = await context.AccidentEvents.SingleAsync(e => e.Id == SinistreId);
        ev.Origin = "auto";
        ev.IncidentAt = DateTime.UtcNow.AddMinutes(-12);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var refus = await Assert.ThrowsAsync<ConflictException>(() =>
            Handler(context, AdminId).Handle(new DeleteAccidentEventCommand(SinistreId, _uploadsRoot), CancellationToken.None));

        refus.Message.Should().Contain("Fausse alerte");
        context.ChangeTracker.Clear();
        (await context.AccidentEvents.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("auto", -45, false)]   // hors de la fenêtre de scan : plus de redétection
    [InlineData("auto", -29, true)]
    [InlineData("manual", -5, false)]  // la détection ne recrée jamais un dossier manuel
    public void PeutEtreRedetecte_SuitLaFenetreDeScan(string origin, int minutes, bool attendu)
    {
        var now = new DateTime(2026, 9, 18, 14, 0, 0, DateTimeKind.Utc);
        DeleteAccidentEventCommandHandler.PeutEtreRedetecte(origin, now.AddMinutes(minutes), now).Should().Be(attendu);
    }

    // ── Fichiers : un libellé de zone ne doit jamais faire effacer un fichier ─────

    [Fact]
    public async Task UneZoneEndommageeForgeeNEffaceAucunFichierDUnAutreDossier()
    {
        using var context = CreerContexte();
        var justificatif = Path.Combine(_uploadsRoot, "invoices", "10", "facture.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(justificatif)!);
        File.WriteAllText(justificatif, "justificatif d'une dépense");

        var ev = await context.AccidentEvents.SingleAsync(e => e.Id == SinistreId);
        ev.DamagedZonesJson = $"[\"/uploads/accident-reports/{SinistreId}/../../invoices/10/facture.pdf\"]";
        ev.PdfReportUrl = $"/uploads/accident-reports/{SinistreId}/../../invoices/10/facture.pdf";
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var resultat = await Handler(context, AdminId).Handle(
            new DeleteAccidentEventCommand(SinistreId, _uploadsRoot), CancellationToken.None);

        resultat.DeletedFiles.Should().Be(0);
        File.Exists(justificatif).Should().BeTrue();
    }

    [Fact]
    public async Task SansReferenceCode_LeMessageCiteLeNumeroDuDossier()
    {
        using var context = CreerContexte();
        var ev = await context.AccidentEvents.SingleAsync(e => e.Id == SinistreId);
        ev.ReferenceCode = null;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var resultat = await Handler(context, AdminId).Handle(
            new DeleteAccidentEventCommand(SinistreId, _uploadsRoot), CancellationToken.None);

        resultat.Reference.Should().Be($"#{SinistreId}");
    }
}
