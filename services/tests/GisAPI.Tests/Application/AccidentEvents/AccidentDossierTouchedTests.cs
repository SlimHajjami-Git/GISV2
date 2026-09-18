using FluentAssertions;
using GisAPI.Application.Features.AccidentEvents.Commands;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Application.AccidentEvents;

/// <summary>
/// Le PDF du rapport de sinistre liste les tiers et les pièces jointes, et l'écran
/// signale « PDF antérieur à la dernière modification » en comparant l'horodatage du
/// fichier à <c>accident_events.updated_at</c>.
///
/// L'AJOUT d'un tiers ou d'une pièce posait bien cette date, pas leur SUPPRESSION :
/// un tiers retiré par erreur restait nommé dans le document remis à l'assureur, sans
/// que rien ne l'indique. Ces tests verrouillent la date de modification du dossier.
/// </summary>
public class AccidentDossierTouchedTests
{
    private const int CompanyId = 7;
    private const int AccidentId = 1;
    private static readonly DateTime Ancienne = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    private static TestGisDbContext Contexte()
    {
        var context = TestDbContextFactory.Create();
        context.AccidentEvents.Add(new AccidentEvent
        {
            Id = AccidentId,
            CompanyId = CompanyId,
            VehicleId = 49,
            DeviceUid = string.Empty,
            IncidentAt = Ancienne,
            Confidence = 100,
            Origin = "manual",
            Status = "confirmed",
            ReferenceCode = "ACC-2026-014",
            UpdatedAt = Ancienne,
        });
        context.AccidentEventThirdParties.Add(new AccidentEventThirdParty
        {
            Id = 11,
            AccidentEventId = AccidentId,
            Name = "Mehdi Ben Salah",
            VehiclePlate = "123 TU 4567",
        });
        context.AccidentEventDocuments.Add(new AccidentEventDocument
        {
            Id = 21,
            AccidentEventId = AccidentId,
            DocumentType = "photo",
            FileName = "degats-avant.jpg",
            FileUrl = "/uploads/accidents/1/degats-avant.jpg",
            UploadedAt = Ancienne,
        });
        context.SaveChanges();
        context.ChangeTracker.Clear();
        return context;
    }

    private static ICurrentTenantService Tenant() =>
        TestDbContextFactory.CreateMockTenantService(companyId: CompanyId).Object;

    [Fact]
    public async Task SuppressionDunTiers_MarqueLeDossierCommeModifie()
    {
        using var context = Contexte();
        var handler = new DeleteThirdPartyCommandHandler(
            context, Tenant(), NullLogger<DeleteThirdPartyCommandHandler>.Instance);

        await handler.Handle(new DeleteThirdPartyCommand(AccidentId, 11), CancellationToken.None);
        context.ChangeTracker.Clear();

        var ev = await context.AccidentEvents.AsNoTracking().SingleAsync();
        ev.UpdatedAt.Should().BeAfter(Ancienne,
            "sans cette date, le PDF déjà produit garderait le tiers retiré sans aucun signal");
        (await context.AccidentEventThirdParties.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SuppressionDunePieceJointe_MarqueLeDossierCommeModifie()
    {
        using var context = Contexte();
        var handler = new DeleteAccidentDocumentCommandHandler(
            context, Tenant(), NullLogger<DeleteAccidentDocumentCommandHandler>.Instance);

        await handler.Handle(new DeleteAccidentDocumentCommand(AccidentId, 21), CancellationToken.None);
        context.ChangeTracker.Clear();

        var ev = await context.AccidentEvents.AsNoTracking().SingleAsync();
        ev.UpdatedAt.Should().BeAfter(Ancienne);
        (await context.AccidentEventDocuments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SuppressionInexistante_NeTouchePasLaDateDuDossier()
    {
        using var context = Contexte();
        var handler = new DeleteThirdPartyCommandHandler(
            context, Tenant(), NullLogger<DeleteThirdPartyCommandHandler>.Instance);

        // Idempotent : un second appel ne doit pas faire clignoter « PDF antérieur ».
        await handler.Handle(new DeleteThirdPartyCommand(AccidentId, 999), CancellationToken.None);
        context.ChangeTracker.Clear();

        var ev = await context.AccidentEvents.AsNoTracking().SingleAsync();
        ev.UpdatedAt.Should().Be(Ancienne);
    }
}
