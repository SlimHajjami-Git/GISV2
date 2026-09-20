using System.Text;
using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.AccidentEvents.Commands;
using GisAPI.Controllers;
using GisAPI.Domain.Entities;
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
/// Contre-relecture du 18/09/2026 — <c>POST /:id/upload-pdf</c> exercé pour de vrai,
/// fichiers écrits sur le disque compris.
///
/// <para>Le rapport de la fiche est REFAIT à chaque phase enregistrée. Sur une
/// déclaration MANUELLE antérieure au correctif, <c>pdf_report_url</c> pouvait porter le
/// document du client : la régénération effaçait son fichier et remplaçait le lien, sans
/// un mot. Trois règles verrouillées ici : ce fichier-là n'est jamais supprimé et devient
/// une pièce jointe avant que le lien ne change ; sur un dossier détecté
/// AUTOMATIQUEMENT, où le champ « PDF expert » n'a jamais existé, l'ancien fichier est un
/// rapport généré et il est remplacé comme tel.</para>
/// </summary>
public class AccidentPdfUploadControllerTests : IDisposable
{
    private const int CompanyId = 7;
    private const int AccidentId = 1;

    /// <summary>
    /// Ancien fichier, sans le préfixe « rapport- » : tout l'existant s'appelle ainsi,
    /// document du client comme rapport généré. Seule l'origine du dossier les sépare.
    /// </summary>
    private const string AncienPdfSansPrefixe = "/uploads/accident-reports/1/8f2b0c6e-0b11-4f0e-9c3a-2b7d5e6f7a81.pdf";

    /// <summary>Racine de contenu propre au test : les fichiers y sont réellement écrits.</summary>
    private readonly string _racine = Path.Combine(
        Path.GetTempPath(), "gisv2-sinistre-pdf-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_racine)) Directory.Delete(_racine, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task DeclarationManuelle_ConserveLePdfFourniParLeClient_EtLeRangeEnPieceJointe()
    {
        var fichierClient = EcrireSurDisque(AncienPdfSansPrefixe, "PDF remis par l'expert");
        using var ctx = Contexte(AncienPdfSansPrefixe, origine: "manual");

        var reponse = await Controleur(ctx).UploadPdf(AccidentId, Fichier(), CancellationToken.None);

        reponse.Result.Should().BeOfType<OkObjectResult>();
        File.Exists(fichierClient).Should().BeTrue(
            "le document remis par l'assuré n'est jamais effacé par une régénération");

        ctx.ChangeTracker.Clear();
        var piece = await ctx.AccidentEventDocuments.AsNoTracking().SingleAsync();
        piece.FileUrl.Should().Be(AncienPdfSansPrefixe, "le fichier reste atteignable depuis la fiche");
        piece.DocumentType.Should().Be("other",
            "rien ne prouve que ce fichier soit une expertise, et le libellé part dans le PDF de l'assureur");

        var dossier = await ctx.AccidentEvents.AsNoTracking().SingleAsync();
        dossier.PdfReportUrl.Should().NotBe(AncienPdfSansPrefixe);
        AccidentReportsController.EstRapportGenere(dossier.PdfReportUrl).Should().BeTrue();
        File.Exists(SurDisque(dossier.PdfReportUrl!)).Should().BeTrue("le rapport généré est bien écrit");
    }

    [Fact]
    public async Task AccidentDetecte_AncienPdfTraiteCommeUnRapportGenere()
    {
        // La modale de décision envoie un rapport jsPDF sur upload-pdf à la confirmation
        // de chaque accident détecté : le fichier s'appelle « {guid}.pdf » comme un
        // document du client. Sur un dossier « auto », le champ « PDF expert » n'a jamais
        // existé — le prendre pour une pièce du client donnait à CHAQUE dossier ancien
        // une fausse pièce jointe et gardait son fichier à jamais.
        var ancien = EcrireSurDisque(AncienPdfSansPrefixe, "rapport produit à la confirmation");
        using var ctx = Contexte(AncienPdfSansPrefixe, origine: "auto");

        await Controleur(ctx).UploadPdf(AccidentId, Fichier(), CancellationToken.None);

        File.Exists(ancien).Should().BeFalse("le rapport remplacé quitte le disque");
        ctx.ChangeTracker.Clear();
        (await ctx.AccidentEventDocuments.AsNoTracking().CountAsync()).Should().Be(0,
            "un dossier détecté automatiquement n'a jamais porté de document du client");
    }

    [Fact]
    public async Task UnRapportGenereNEstJamaisRangeEnPieceJointe()
    {
        EcrireSurDisque(AncienPdfSansPrefixe, "PDF remis par l'expert");
        using var ctx = Contexte(AncienPdfSansPrefixe, origine: "manual");
        var controleur = Controleur(ctx);

        await controleur.UploadPdf(AccidentId, Fichier(), CancellationToken.None);
        await controleur.UploadPdf(AccidentId, Fichier(), CancellationToken.None);

        ctx.ChangeTracker.Clear();
        (await ctx.AccidentEventDocuments.AsNoTracking().CountAsync()).Should().Be(1,
            "au second appel le dossier porte un « rapport- », qui n'est pas une pièce du client");
    }

    [Fact]
    public async Task PieceJointeDejaPresente_NEstPasDupliquee()
    {
        // Garde d'idempotence proprement dite : une ligne porte DÉJÀ ce fichier. Le cas
        // arrive dès qu'une pièce jointe a été créée puis le lien remis à l'ancienne URL.
        EcrireSurDisque(AncienPdfSansPrefixe, "PDF remis par l'expert");
        using var ctx = Contexte(AncienPdfSansPrefixe, origine: "manual");
        ctx.AccidentEventDocuments.Add(new AccidentEventDocument
        {
            AccidentEventId = AccidentId,
            DocumentType = "other",
            FileName = "8f2b0c6e-0b11-4f0e-9c3a-2b7d5e6f7a81.pdf",
            FileUrl = AncienPdfSansPrefixe,
            UploadedAt = new DateTime(2026, 9, 17, 9, 0, 0, DateTimeKind.Utc),
        });
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();

        await Controleur(ctx).UploadPdf(AccidentId, Fichier(), CancellationToken.None);

        ctx.ChangeTracker.Clear();
        (await ctx.AccidentEventDocuments.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task FichierDisparu_NEstPasRangeEnPieceJointe()
    {
        // Aucune écriture sur disque : le fichier a été purgé (racine uploads nettoyée,
        // société remise à zéro, restauration partielle). La pièce jointe n'aurait qu'un
        // lien mort, recopié tel quel dans le tableau du PDF remis à l'assureur.
        using var ctx = Contexte(AncienPdfSansPrefixe, origine: "manual");

        await Controleur(ctx).UploadPdf(AccidentId, Fichier(), CancellationToken.None);

        ctx.ChangeTracker.Clear();
        (await ctx.AccidentEventDocuments.AsNoTracking().CountAsync()).Should().Be(0,
            "une pièce jointe sans fichier vaut moins que pas de pièce jointe du tout");
    }

    [Fact]
    public async Task RegenererLeRapport_RemplaceEtEffaceLePrecedentRapportGenere()
    {
        const string ancienRapport = "/uploads/accident-reports/1/rapport-3c1f8d20.pdf";
        var fichier = EcrireSurDisque(ancienRapport, "rapport précédent");
        using var ctx = Contexte(ancienRapport, origine: "manual");

        await Controleur(ctx).UploadPdf(AccidentId, Fichier(), CancellationToken.None);

        File.Exists(fichier).Should().BeFalse("un rapport produit par l'application reste remplaçable");
        ctx.ChangeTracker.Clear();
        (await ctx.AccidentEventDocuments.AsNoTracking().CountAsync()).Should().Be(0,
            "un rapport généré n'a rien à faire dans les pièces jointes");
    }

    // ── Montage ──────────────────────────────────────────────────────────────

    private string SurDisque(string url) =>
        Path.Combine(_racine, url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    private string EcrireSurDisque(string url, string contenu)
    {
        var chemin = SurDisque(url);
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

    private ContexteEnMemoire Contexte(string? pdfReportUrl, string origine)
    {
        var ctx = new ContexteEnMemoire(new DbContextOptionsBuilder<GisDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), b => b.EnableNullChecks(false))
            .Options);
        ctx.AccidentEvents.Add(new AccidentEvent
        {
            Id = AccidentId,
            CompanyId = CompanyId,
            DeviceUid = string.Empty,
            IncidentAt = new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc),
            Confidence = 100,
            Origin = origine,
            Status = "confirmed",
            ReferenceCode = "MAN-20260910080000",
            PdfReportUrl = pdfReportUrl,
        });
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    private AccidentReportsController Controleur(GisDbContext ctx)
    {
        var tenant = TestDbContextFactory.CreateMockTenantService(CompanyId).Object;
        var mediator = new Mock<IMediator>();
        // Le vrai handler : c'est lui qui remplace le lien, l'étape même où le document
        // du client était perdu.
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

    /// <summary>GisDbContext en mémoire, sans les colonnes propres à PostgreSQL (même montage que ReparationsAnnuleesHorsCoutsTests).</summary>
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
