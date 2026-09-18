using FluentAssertions;
using GisAPI.Application.Features.AccidentEvents.Commands;
using GisAPI.Controllers;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Application.AccidentEvents;

/// <summary>
/// Contre-relecture du 18/09/2026 : le « PDF expert (facultatif) » joint à une
/// déclaration manuelle partait sur <c>POST /:id/upload-pdf</c>, donc dans
/// <c>accident_events.pdf_report_url</c> — le MÊME champ que le rapport produit par
/// l'application, désormais refait à chaque phase enregistrée. Le document de
/// l'expert était remplacé sans un mot et son fichier effacé du disque.
///
/// <para>Ce fichier verrouille la RÈGLE : deux champs distincts, et un fichier que
/// l'application n'a pas produit n'est jamais reconnu comme un rapport remplaçable.
/// La preuve du correctif lui-même est ailleurs, là où le choix se fait :
/// <c>AccidentPdfUploadControllerTests</c> pour le serveur (fichiers écrits sur le
/// disque) et <c>accident-reports-list.component.spec.ts</c> pour la route choisie par
/// l'écran de déclaration.</para>
/// </summary>
public class AccidentClientPdfTests
{
    private const int CompanyId = 7;
    private const int AccidentId = 1;
    private const int VehiculeId = 49;
    private static readonly DateTime Sinistre = new(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc);

    private static TestGisDbContext Contexte()
    {
        var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = VehiculeId, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId });
        context.AccidentEvents.Add(new AccidentEvent
        {
            Id = AccidentId,
            CompanyId = CompanyId,
            VehicleId = VehiculeId,
            DeviceUid = string.Empty,
            IncidentAt = Sinistre,
            Confidence = 100,
            Origin = "manual",
            Status = "confirmed",
            ReferenceCode = "MAN-20260910080000",
        });
        context.SaveChanges();
        context.ChangeTracker.Clear();
        return context;
    }

    private static ICurrentTenantService Tenant() =>
        TestDbContextFactory.CreateMockTenantService(companyId: CompanyId).Object;

    /// <summary>
    /// Pièce jointe et rapport généré sont deux enregistrements distincts : joindre l'un
    /// n'écrase pas l'autre. Garde-fou de la règle, pas preuve du correctif.
    /// </summary>
    [Fact]
    public async Task PieceJointeEtRapportGenere_SontDeuxEnregistrementsDistincts()
    {
        using var context = Contexte();

        // 1. Le fichier du client est rangé en pièce jointe du dossier (route /documents).
        var docId = await new AddAccidentDocumentCommandHandler(
                context, Tenant(), NullLogger<AddAccidentDocumentCommandHandler>.Instance)
            .Handle(new AddAccidentDocumentCommand(
                AccidentEventId: AccidentId,
                DocumentType: "expert_report",
                FileName: "rapport-expert-assurance.pdf",
                FileUrl: "/uploads/accident-reports/1/8f2b0c6e-expert.pdf",
                FileSize: 240_000,
                MimeType: "application/pdf"), CancellationToken.None);
        context.ChangeTracker.Clear();

        // 2. La fiche produit ensuite son rapport et l'enregistre dans pdf_report_url.
        await new AttachAccidentPdfCommandHandler(
                context, Tenant(), NullLogger<AttachAccidentPdfCommandHandler>.Instance)
            .Handle(new AttachAccidentPdfCommand(AccidentId, "/uploads/accident-reports/1/rapport-3c1f.pdf"),
                CancellationToken.None);
        context.ChangeTracker.Clear();

        var document = await context.AccidentEventDocuments.AsNoTracking().SingleAsync(d => d.Id == docId);
        document.FileUrl.Should().Be("/uploads/accident-reports/1/8f2b0c6e-expert.pdf",
            "le document de l'expert reste une pièce jointe du dossier");
        document.DocumentType.Should().Be("expert_report");

        var dossier = await context.AccidentEvents.AsNoTracking().SingleAsync();
        dossier.PdfReportUrl.Should().Be("/uploads/accident-reports/1/rapport-3c1f.pdf");
        dossier.PdfReportUrl.Should().NotBe(document.FileUrl,
            "les deux documents ne partagent plus le même champ");
        AccidentReportsController.EstRapportGenere(dossier.PdfReportUrl).Should().BeTrue();
    }

    [Theory]
    // Rapports produits par l'application : remplaçables, donc effaçables.
    [InlineData("/uploads/accident-reports/1/rapport-3c1f8d20-0b11-4f0e-9c3a-2b7d5e6f7a81.pdf", true)]
    [InlineData("/uploads/accident-reports/1/RAPPORT-3c1f8d20.pdf", true)]
    // Fichiers antérieurs au correctif, sous un simple GUID : le nom ne dit pas d'où ils
    // viennent. C'est l'origine du dossier qui tranche (AccidentPdfUploadControllerTests).
    [InlineData("/uploads/accident-reports/1/3c1f8d20-0b11-4f0e-9c3a-2b7d5e6f7a81.pdf", false)]
    [InlineData("/uploads/accident-reports/1/expertise-assurance.pdf", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SeulUnRapportProduitParLApplication_EstRemplacable(string? fileUrl, bool attendu)
    {
        AccidentReportsController.EstRapportGenere(fileUrl).Should().Be(attendu);
    }
}
