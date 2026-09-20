using System.Text;
using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.AccidentEvents.Commands;
using GisAPI.Controllers;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Services;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.AccidentEvents;

/// <summary>
/// Revue de la fusion du 20/09/2026 — deux trous ouverts par le lot « la réparation d'un
/// sinistre entre dans Réparations ».
///
/// <para>PORTÉE : la lecture d'un dossier, son PDF et sa suppression répondaient déjà 404
/// hors du périmètre véhicules de l'appelant, mais les PHASES s'y écrivaient encore — et
/// depuis ce lot la phase 5 crée une ligne dans Réparations et retire la dépense en face,
/// sur un véhicule que l'utilisateur ne voit même pas. Le dépôt de fichier, lui,
/// SUPPRIMAIT le PDF précédent du dossier.</para>
///
/// <para>RATTACHEMENT : quand le coût réel est effacé alors que le client a complété la
/// ligne dans l'écran Réparations, celle-ci est conservée et seulement détachée. Ressaisir
/// le coût en créait alors une SECONDE : le même sinistre comptait deux fois.</para>
/// </summary>
public class SinistresPorteeEtRattachementTests : IDisposable
{
    private const int CompanyId = 7;
    private const int AccidentId = 1;
    private const int VehiculeDuSinistre = 49;
    private const int VehiculeDeLEmploye = 50;
    private const int EmployeRestreint = 88;
    private static readonly DateTime Debut = new(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Fin = new(2026, 9, 14, 17, 0, 0, DateTimeKind.Utc);

    private readonly string _racine = Path.Combine(
        Path.GetTempPath(), "gisv2-sinistre-portee-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_racine)) Directory.Delete(_racine, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ── Portée véhicules sur les écritures de phase ────────────────────────────

    [Fact]
    public async Task Phase5_Un_employe_restreint_ne_peut_pas_ecrire_sur_un_sinistre_hors_de_son_perimetre()
    {
        using var context = Contexte();

        var ecrire = async () => await new RegisterRepairCommandHandler(
                context, TenantRestreint(), NullLogger<RegisterRepairCommandHandler>.Instance)
            .Handle(new RegisterRepairCommand(AccidentId, Debut, Fin, 8_500m), CancellationToken.None);

        await ecrire.Should().ThrowAsync<NotFoundException>(
            "le dossier porte un véhicule qui n'est pas affecté à cet utilisateur : il répond déjà 404 en lecture");

        context.ChangeTracker.Clear();
        (await context.Repairs.CountAsync()).Should().Be(0, "aucune réparation n'a été créée");
        (await context.AccidentEvents.AsNoTracking().SingleAsync()).ActualRepairCost
            .Should().BeNull("le dossier n'a pas été modifié non plus");
    }

    [Fact]
    public async Task Phase6_Un_employe_restreint_ne_peut_pas_enregistrer_le_remboursement_d_un_sinistre_hors_perimetre()
    {
        using var context = Contexte();

        var ecrire = async () => await new RegisterClaimCommandHandler(
                context, TenantRestreint(), NullLogger<RegisterClaimCommandHandler>.Instance)
            .Handle(new RegisterClaimCommand(AccidentId, "SIN-99", Debut, 3_200m, "approved", null),
                CancellationToken.None);

        await ecrire.Should().ThrowAsync<NotFoundException>();

        context.ChangeTracker.Clear();
        (await context.VehicleCosts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Le_perimetre_laisse_passer_le_dossier_du_vehicule_affecte()
    {
        using var context = Contexte(vehiculeDuSinistre: VehiculeDeLEmploye);

        var result = await new RegisterRepairCommandHandler(
                context, TenantRestreint(), NullLogger<RegisterRepairCommandHandler>.Instance)
            .Handle(new RegisterRepairCommand(AccidentId, Debut, Fin, 1_200m), CancellationToken.None);

        result.Synced.Should().BeTrue("ce véhicule-là lui est affecté");
        context.ChangeTracker.Clear();
        (await context.Repairs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Un_administrateur_garde_tout_le_parc()
    {
        using var context = Contexte();

        var result = await new RegisterRepairCommandHandler(
                context, TenantAdmin(), NullLogger<RegisterRepairCommandHandler>.Instance)
            .Handle(new RegisterRepairCommand(AccidentId, Debut, Fin, 1_200m), CancellationToken.None);

        result.Synced.Should().BeTrue();
    }

    // ── Rattachement d'une réparation détachée ─────────────────────────────────

    [Fact]
    public async Task Cout_ressaisi_apres_detachement_rattache_la_reparation_au_lieu_d_en_creer_une_seconde()
    {
        using var context = Contexte(vehiculeDuSinistre: VehiculeDuSinistre);
        var handler = new RegisterRepairCommandHandler(
            context, TenantAdmin(), NullLogger<RegisterRepairCommandHandler>.Instance);

        // 1. La phase 5 pose la réparation.
        await handler.Handle(new RegisterRepairCommand(AccidentId, Debut, Fin, 1_200m), CancellationToken.None);
        context.ChangeTracker.Clear();

        // 2. Le client complète la ligne dans l'écran Réparations (n° de facture du garage).
        var ligne = await context.Repairs.SingleAsync();
        ligne.InvoiceNumber = "F-2026-889";
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // 3. Le coût est vidé : la ligne est CONSERVÉE, seulement détachée.
        var detachement = await handler.Handle(
            new RegisterRepairCommand(AccidentId, Debut, Fin, null), CancellationToken.None);
        detachement.Warning.Should().NotBeNullOrEmpty("l'écran doit dire que la ligne a été conservée");
        context.ChangeTracker.Clear();
        (await context.Repairs.SingleAsync()).AccidentEventId.Should().BeNull();

        // 4. Le coût est ressaisi : la MÊME ligne repart, avec sa facture.
        await handler.Handle(new RegisterRepairCommand(AccidentId, Debut, Fin, 1_450m), CancellationToken.None);
        context.ChangeTracker.Clear();

        var reparations = await context.Repairs.AsNoTracking().ToListAsync();
        reparations.Should().HaveCount(1, "une seconde ligne compterait le même sinistre deux fois");
        reparations[0].AccidentEventId.Should().Be(AccidentId);
        reparations[0].InvoiceNumber.Should().Be("F-2026-889", "la saisie du client survit au rattachement");
        reparations[0].TotalCost.Should().Be(1_450m);
    }

    [Fact]
    public async Task Une_reparation_reecrite_par_le_client_n_est_pas_reprise_par_le_sinistre()
    {
        using var context = Contexte(vehiculeDuSinistre: VehiculeDuSinistre);
        var handler = new RegisterRepairCommandHandler(
            context, TenantAdmin(), NullLogger<RegisterRepairCommandHandler>.Instance);

        await handler.Handle(new RegisterRepairCommand(AccidentId, Debut, Fin, 1_200m), CancellationToken.None);
        context.ChangeTracker.Clear();
        var ligne = await context.Repairs.SingleAsync();
        ligne.InvoiceNumber = "F-2026-889";
        // Description REPRISE par le client : la ligne lui appartient, la phase ne la revendique plus.
        ligne.Description = "Remplacement pare-chocs avant + peinture";
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await handler.Handle(new RegisterRepairCommand(AccidentId, Debut, Fin, null), CancellationToken.None);
        context.ChangeTracker.Clear();
        await handler.Handle(new RegisterRepairCommand(AccidentId, Debut, Fin, 1_450m), CancellationToken.None);
        context.ChangeTracker.Clear();

        var reparations = await context.Repairs.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
        reparations.Should().HaveCount(2, "la ligne réécrite appartient à l'écran Réparations, la phase repart d'une neuve");
        reparations[0].AccidentEventId.Should().BeNull();
        reparations[1].AccidentEventId.Should().Be(AccidentId);
    }

    // ── Portée véhicules sur le dépôt de fichier ───────────────────────────────

    [Fact]
    public async Task UploadPdf_Un_employe_restreint_ne_remplace_pas_le_rapport_d_un_dossier_hors_perimetre()
    {
        const string ancienPdf = "/uploads/accident-reports/1/8f2b0c6e-0b11-4f0e-9c3a-2b7d5e6f7a81.pdf";
        var surDisque = EcrireSurDisque(ancienPdf, "rapport du dossier");
        using var ctx = ContexteControleur(ancienPdf);

        var reponse = await Controleur(ctx, TenantRestreint()).UploadPdf(AccidentId, Fichier(), CancellationToken.None);

        reponse.Result.Should().BeOfType<NotFoundResult>(
            "la lecture du même dossier répond 404 : le dépôt ne peut pas être plus ouvert");
        File.Exists(surDisque).Should().BeTrue("le PDF du dossier n'a pas été supprimé");

        ctx.ChangeTracker.Clear();
        (await ctx.AccidentEvents.AsNoTracking().SingleAsync()).PdfReportUrl.Should().Be(ancienPdf);
    }

    // ── Montage ────────────────────────────────────────────────────────────────

    /// <summary>Un employé qui n'a QUE le véhicule 50 affecté (table user_vehicles).</summary>
    private static ICurrentTenantService TenantRestreint()
    {
        var mock = TestDbContextFactory.CreateMockTenantService(CompanyId, userId: EmployeRestreint);
        mock.Setup(t => t.UserRoles).Returns(new[] { "user" });
        return mock.Object;
    }

    private static ICurrentTenantService TenantAdmin() =>
        TestDbContextFactory.CreateMockTenantService(CompanyId).Object;

    private static TestGisDbContext Contexte(int vehiculeDuSinistre = VehiculeDuSinistre)
    {
        var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = VehiculeDuSinistre, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId });
        context.Vehicles.Add(new Vehicle { Id = VehiculeDeLEmploye, Name = "Service 02", Plate = "GA-215-RK", CompanyId = CompanyId });
        context.UserVehicles.Add(new UserVehicle { UserId = EmployeRestreint, VehicleId = VehiculeDeLEmploye });
        context.AccidentEvents.Add(new AccidentEvent
        {
            Id = AccidentId,
            CompanyId = CompanyId,
            VehicleId = vehiculeDuSinistre,
            DeviceUid = string.Empty,
            IncidentAt = Debut.AddDays(-2),
            Confidence = 100,
            Origin = "manual",
            Status = "confirmed",
            ReferenceCode = "ACC-2026-014",
        });
        context.SaveChanges();
        context.ChangeTracker.Clear();
        return context;
    }

    private string EcrireSurDisque(string url, string contenu)
    {
        var chemin = Path.Combine(_racine, url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
        File.WriteAllText(chemin, contenu);
        return chemin;
    }

    private static IFormFile Fichier()
    {
        var octets = Encoding.UTF8.GetBytes("%PDF-1.4 rapport produit par Calypso");
        return new FormFile(new MemoryStream(octets), 0, octets.Length, "file", "accident-report.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf",
        };
    }

    private ContexteEnMemoire ContexteControleur(string? pdfReportUrl)
    {
        var ctx = new ContexteEnMemoire(new DbContextOptionsBuilder<GisDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), b => b.EnableNullChecks(false))
            .Options);
        ctx.Vehicles.Add(new Vehicle { Id = VehiculeDuSinistre, Name = "Service 01", CompanyId = CompanyId });
        ctx.Vehicles.Add(new Vehicle { Id = VehiculeDeLEmploye, Name = "Service 02", CompanyId = CompanyId });
        ctx.UserVehicles.Add(new UserVehicle { Id = 1, UserId = EmployeRestreint, VehicleId = VehiculeDeLEmploye });
        ctx.AccidentEvents.Add(new AccidentEvent
        {
            Id = AccidentId,
            CompanyId = CompanyId,
            VehicleId = VehiculeDuSinistre,
            DeviceUid = string.Empty,
            IncidentAt = Debut.AddDays(-2),
            Confidence = 100,
            Origin = "manual",
            Status = "confirmed",
            ReferenceCode = "MAN-20260910080000",
            PdfReportUrl = pdfReportUrl,
        });
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    private AccidentReportsController Controleur(GisDbContext ctx, ICurrentTenantService tenant)
    {
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<AttachAccidentPdfCommand>(), It.IsAny<CancellationToken>()))
            .Returns((AttachAccidentPdfCommand cmd, CancellationToken ct) =>
                new AttachAccidentPdfCommandHandler(
                        ctx, tenant, NullLogger<AttachAccidentPdfCommandHandler>.Instance)
                    .Handle(cmd, ct));

        return new AccidentReportsController(
            mediator.Object,
            ctx,
            Mock.Of<INotificationService>(),
            tenant,
            Mock.Of<IWebHostEnvironment>(e => e.ContentRootPath == _racine),
            Mock.Of<IManualAccidentEnricher>(),
            NullLogger<AccidentReportsController>.Instance);
    }

    /// <summary>GisDbContext en mémoire, sans les colonnes propres à PostgreSQL (même montage que AccidentPdfUploadControllerTests).</summary>
    private sealed class ContexteEnMemoire : GisDbContext
    {
        public ContexteEnMemoire(DbContextOptions<GisDbContext> options)
            : base(options, TestDbContextFactory.CreateMockTenantService(CompanyId).Object) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            foreach (var entity in modelBuilder.Model.GetEntityTypes().ToList())
                foreach (var property in entity.GetDeclaredProperties().ToList())
                    if (!Simple(property.ClrType) && !property.IsKey() && !property.IsForeignKey())
                        modelBuilder.Entity(entity.ClrType).Ignore(property.Name);
        }

        private static bool Simple(Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime)
                || t == typeof(DateTimeOffset) || t == typeof(TimeSpan) || t == typeof(Guid) || t == typeof(byte[])
                || t == typeof(DateOnly) || t == typeof(TimeOnly);
        }
    }
}
