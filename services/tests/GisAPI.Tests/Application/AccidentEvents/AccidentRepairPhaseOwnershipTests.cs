using FluentAssertions;
using GisAPI.Application.Features.AccidentEvents.Commands;
using GisAPI.Application.Features.Repairs;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Application.AccidentEvents;

/// <summary>
/// Contre-relecture du 18/09/2026 — suite d'<c>AccidentRepairManualEditsTests</c> : le
/// durcissement s'était arrêté aux notes et au statut « annulée ». La phase réécrivait
/// encore sans condition la DESCRIPTION et la DATE de la réparation, que l'écran
/// Réparations laisse pourtant modifier, et ramenait un statut « en attente » choisi à
/// la main vers « en cours » / « terminée ».
///
/// <para>Règle : à la mise à jour, la phase ne reprend que ce qu'elle a elle-même posé.</para>
/// </summary>
public class AccidentRepairPhaseOwnershipTests
{
    private const int CompanyId = 7;
    private const int AccidentId = 1;
    private const int VehiculeExistant = 49;
    private static readonly DateTime Debut = new(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Fin = new(2026, 9, 14, 17, 0, 0, DateTimeKind.Utc);

    private static TestGisDbContext Contexte()
    {
        var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = VehiculeExistant, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId });
        context.AccidentEvents.Add(new AccidentEvent
        {
            Id = AccidentId,
            CompanyId = CompanyId,
            VehicleId = VehiculeExistant,
            DeviceUid = string.Empty,
            IncidentAt = Debut.AddDays(-2),
            Confidence = 100,
            Origin = "manual",
            Status = "confirmed",
            ReferenceCode = "ACC-2026-014",
            MechanicName = "Garage Central",
        });
        context.SaveChanges();
        context.ChangeTracker.Clear();
        return context;
    }

    private static Task Phase5(TestGisDbContext context, decimal montant = 1200m, DateTime? fin = null) =>
        new RegisterRepairCommandHandler(
                context,
                TestDbContextFactory.CreateMockTenantService(companyId: CompanyId).Object,
                NullLogger<RegisterRepairCommandHandler>.Instance)
            .Handle(new RegisterRepairCommand(AccidentId, Debut, fin ?? Fin, montant), CancellationToken.None);

    [Fact]
    public async Task DescriptionPreciseeALaMain_SurvitAUneSecondeSauvegardeDeLaPhase()
    {
        using var context = Contexte();
        await Phase5(context);
        context.ChangeTracker.Clear();

        var reparation = await context.Repairs.SingleAsync();
        reparation.Description = "Remplacement pare-chocs avant + peinture";
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await Phase5(context, 1500m);
        context.ChangeTracker.Clear();

        (await context.Repairs.AsNoTracking().SingleAsync()).Description
            .Should().Be("Remplacement pare-chocs avant + peinture",
                "une description saisie dans Réparations n'appartient pas à la phase");
    }

    [Fact]
    public async Task DateCorrigeeSurLaFacture_SurvitAUneSecondeSauvegardeDeLaPhase()
    {
        var dateFacture = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);
        using var context = Contexte();
        await Phase5(context);
        context.ChangeTracker.Clear();

        var reparation = await context.Repairs.SingleAsync();
        reparation.RepairDate = dateFacture;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await Phase5(context, 1500m);
        context.ChangeTracker.Clear();

        (await context.Repairs.AsNoTracking().SingleAsync()).RepairDate.Date
            .Should().Be(dateFacture.Date,
                "la date portée par la facture prime sur celle déduite de la phase");
    }

    [Fact]
    public async Task StatutEnAttenteChoisiALaMain_NEstPasEcrase()
    {
        using var context = Contexte();
        await Phase5(context);
        context.ChangeTracker.Clear();

        var reparation = await context.Repairs.SingleAsync();
        reparation.Status = RepairInputRules.Pending;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await Phase5(context);
        context.ChangeTracker.Clear();

        (await context.Repairs.AsNoTracking().SingleAsync()).Status
            .Should().Be(RepairInputRules.Pending,
                "« en attente » vient de l'écran Réparations, comme « annulée »");
    }

    [Fact]
    public async Task DescriptionEtDatePoseesParLaPhase_SuiventToujoursLeDossier()
    {
        using var context = Contexte();
        await Phase5(context);
        context.ChangeTracker.Clear();

        var creee = await context.Repairs.AsNoTracking().SingleAsync();
        creee.Description.Should().Be("Réparation accident — ACC-2026-014");
        creee.RepairDate.Date.Should().Be(Fin.Date);

        // Date de fin repoussée dans la phase, rien n'a été touché dans Réparations.
        var nouvelleFin = Fin.AddDays(3);
        await Phase5(context, 1200m, nouvelleFin);
        context.ChangeTracker.Clear();

        (await context.Repairs.AsNoTracking().SingleAsync()).RepairDate.Date
            .Should().Be(nouvelleFin.Date, "tant que l'utilisateur n'y touche pas, la phase reste maîtresse");
    }
}
