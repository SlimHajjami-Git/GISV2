using FluentAssertions;
using GisAPI.Application.Features.AccidentEvents.Commands;
using GisAPI.Application.Features.Repairs.Handlers;
using GisAPI.Application.Features.Repairs.Queries;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Application.AccidentEvents;

/// <summary>
/// Lien de la réparation vers son dossier de sinistre, dans les DEUX sens.
///
/// <para>Avant la migration 049, la phase 5 écrivait une <c>vehicle_costs</c> porteuse
/// d'<c>accident_event_id</c> : l'écran Dépenses posait dessus le badge « Accident #N »
/// avec un lien vers la fiche, et verrouillait le bouton Supprimer. Depuis que la ligne
/// vient de <c>repairs</c>, aucun DTO de réparation n'exposait la colonne — badge, lien
/// et verrou tombaient d'un coup, et le seul indice restant était la description, que
/// l'utilisateur a précisément le droit de réécrire.</para>
/// </summary>
public class AccidentRepairLinkExposedTests
{
    private const int CompanyId = 7;
    private const int AccidentId = 1;
    private const int VehiculeId = 49;
    private static readonly DateTime Debut = new(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Fin = new(2026, 9, 14, 17, 0, 0, DateTimeKind.Utc);

    private static ICurrentTenantService Tenant() =>
        TestDbContextFactory.CreateMockTenantService(companyId: CompanyId).Object;

    private static async Task<TestGisDbContext> ContexteAvecReparationDeSinistre()
    {
        var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = VehiculeId, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId });
        context.AccidentEvents.Add(new AccidentEvent
        {
            Id = AccidentId,
            CompanyId = CompanyId,
            VehicleId = VehiculeId,
            DeviceUid = string.Empty,
            IncidentAt = Debut.AddDays(-2),
            Confidence = 100,
            Origin = "manual",
            Status = "confirmed",
            ReferenceCode = "ACC-2026-014",
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await new RegisterRepairCommandHandler(context, Tenant(), NullLogger<RegisterRepairCommandHandler>.Instance)
            .Handle(new RegisterRepairCommand(AccidentId, Debut, Fin, 1200m), CancellationToken.None);
        context.ChangeTracker.Clear();
        return context;
    }

    [Fact]
    public async Task LaListeDesReparations_CiteLeDossierDeSinistre()
    {
        using var context = await ContexteAvecReparationDeSinistre();

        var liste = await new GetRepairsQueryHandler(context, Tenant())
            .Handle(new GetRepairsQuery(), CancellationToken.None);

        liste.Items.Should().ContainSingle()
            .Which.AccidentEventId.Should().Be(AccidentId, "l'écran Dépenses en tire le badge et le verrou de suppression");
    }

    [Fact]
    public async Task LaFicheDUneReparation_CiteLeDossierDeSinistre()
    {
        using var context = await ContexteAvecReparationDeSinistre();
        var id = await context.Repairs.AsNoTracking().Select(r => r.Id).SingleAsync();

        var fiche = await new GetRepairByIdQueryHandler(context, Tenant())
            .Handle(new GetRepairByIdQuery(id), CancellationToken.None);

        fiche!.AccidentEventId.Should().Be(AccidentId);
    }

    [Fact]
    public async Task UneReparationOrdinaire_NaAucunDossier()
    {
        using var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = VehiculeId, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId });
        context.Repairs.Add(new Repair
        {
            SocieteId = CompanyId, VehicleId = VehiculeId, Reference = "REP-202609-0009",
            RepairDate = Fin, TotalCost = 300m, Status = "completed",
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var liste = await new GetRepairsQueryHandler(context, Tenant())
            .Handle(new GetRepairsQuery(), CancellationToken.None);

        liste.Items.Should().ContainSingle().Which.AccidentEventId.Should().BeNull();
    }

    [Fact]
    public async Task LaReparationDUnSinistre_NEstPasSupprimableDepuisLEcranReparations()
    {
        // Le verrou de l'écran Dépenses ne protégeait rien : la MÊME ligne partait en un
        // clic depuis Réparations, où rien ne dit qu'elle appartient à un dossier. Le
        // dossier gardait alors son coût réel, perdait sa fiche de réparation, et le
        // montant quittait les coûts sans un mot.
        using var context = await ContexteAvecReparationDeSinistre();
        var id = await context.Repairs.AsNoTracking().Select(r => r.Id).SingleAsync();

        var refus = async () => await new DeleteRepairCommandHandler(context, Tenant())
            .Handle(new GisAPI.Application.Features.Repairs.Commands.DeleteRepairCommand(id), CancellationToken.None);

        (await refus.Should().ThrowAsync<GisAPI.Domain.Exceptions.DomainException>())
            .Which.Message.Should().Contain("ACC-2026-014", "le message doit nommer le dossier à ouvrir");
        context.ChangeTracker.Clear();
        (await context.Repairs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UneReparationDetacheeDuDossier_RedevientSupprimable()
    {
        // Contrepartie du verrou : la phase 5 (coût vidé) et la suppression du dossier
        // détachent la ligne — elle doit alors se supprimer comme n'importe quelle autre,
        // sinon le verrou serait définitif.
        using var context = await ContexteAvecReparationDeSinistre();
        var reparation = await context.Repairs.SingleAsync();
        reparation.AccidentEventId = null;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var supprime = await new DeleteRepairCommandHandler(context, Tenant())
            .Handle(new GisAPI.Application.Features.Repairs.Commands.DeleteRepairCommand(reparation.Id), CancellationToken.None);

        supprime.Should().BeTrue();
        context.ChangeTracker.Clear();
        (await context.Repairs.CountAsync()).Should().Be(0);
    }
}
