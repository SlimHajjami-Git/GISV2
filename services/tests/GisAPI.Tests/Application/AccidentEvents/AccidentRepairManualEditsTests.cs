using FluentAssertions;
using GisAPI.Application.Features.AccidentEvents.Commands;
using GisAPI.Application.Features.Repairs;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Application.AccidentEvents;

/// <summary>
/// La réparation née de la phase 5 vit dans l'écran Réparations, où elle se modifie
/// comme les autres. La phase la réécrivait pourtant EN ENTIER à chaque
/// enregistrement : les notes du garagiste saisies à la main étaient effacées, et une
/// réparation annulée repassait en « Terminée » — son montant revenait dans les coûts.
///
/// Même famille que les pièces (parts_cost remis à zéro), sur les champs oubliés :
/// à la CRÉATION la phase renseigne notes et statut ; à la MISE À JOUR elle ne
/// remplace que ce qu'elle a elle-même posé.
/// </summary>
public class AccidentRepairManualEditsTests
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

    private static RegisterRepairCommandHandler Handler(TestGisDbContext context) =>
        new(context,
            TestDbContextFactory.CreateMockTenantService(companyId: CompanyId).Object,
            NullLogger<RegisterRepairCommandHandler>.Instance);

    private static Task Phase5(TestGisDbContext context, decimal montant = 1200m) =>
        Handler(context).Handle(new RegisterRepairCommand(AccidentId, Debut, Fin, montant), CancellationToken.None);

    [Fact]
    public async Task ALaCreation_LaPhaseRenseigneLeGarageEtLeStatut()
    {
        using var context = Contexte();

        await Phase5(context);
        context.ChangeTracker.Clear();

        var reparation = await context.Repairs.AsNoTracking().SingleAsync();
        reparation.Notes.Should().Be("Garage : Garage Central");
        reparation.Status.Should().Be(RepairInputRules.Completed);
    }

    [Fact]
    public async Task NoteSaisieALaMain_SurvitAUneSecondeSauvegardeDeLaPhase()
    {
        using var context = Contexte();
        await Phase5(context);
        context.ChangeTracker.Clear();

        // L'utilisateur complète la fiche depuis l'écran Réparations.
        var reparation = await context.Repairs.SingleAsync();
        reparation.Notes = "Pièces d'occasion, garantie 6 mois. Immobilisé 4 jours.";
        reparation.SupplierId = 12;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await Phase5(context, 1500m);
        context.ChangeTracker.Clear();

        var apres = await context.Repairs.AsNoTracking().SingleAsync();
        apres.Notes.Should().Be("Pièces d'occasion, garantie 6 mois. Immobilisé 4 jours.",
            "une note saisie dans Réparations n'appartient pas à la phase");
        apres.SupplierId.Should().Be(12, "la phase n'écrit jamais supplier_id");
        apres.TotalCost.Should().Be(1500m, "le montant de la phase, lui, reste la vérité");
    }

    [Fact]
    public async Task NotePoseeParLaPhase_SuitLeGarageDuDossier()
    {
        using var context = Contexte();
        await Phase5(context);
        context.ChangeTracker.Clear();

        // Phase 4 corrigée : le véhicule est finalement passé chez un autre garage.
        var dossier = await context.AccidentEvents.SingleAsync();
        dossier.MechanicName = "Carrosserie du Lac";
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await Phase5(context);
        context.ChangeTracker.Clear();

        (await context.Repairs.AsNoTracking().SingleAsync()).Notes.Should().Be("Garage : Carrosserie du Lac");
    }

    [Fact]
    public async Task ReparationAnnuleeALaMain_NEstPasRessuscitee()
    {
        using var context = Contexte();
        await Phase5(context);
        context.ChangeTracker.Clear();

        var reparation = await context.Repairs.SingleAsync();
        reparation.Status = RepairInputRules.Cancelled;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await Phase5(context);
        context.ChangeTracker.Clear();

        (await context.Repairs.AsNoTracking().SingleAsync()).Status
            .Should().Be(RepairInputRules.Cancelled,
                "une réparation annulée est exclue des rapports : la phase ne doit pas y ramener son montant");
    }

    [Fact]
    public async Task StatutAncienEnMajuscules_EstReconnuCommeAnnule()
    {
        using var context = Contexte();
        await Phase5(context);
        context.ChangeTracker.Clear();

        // Données anciennes : « Cancelled » existe en base (import Excel).
        var reparation = await context.Repairs.SingleAsync();
        reparation.Status = "Cancelled";
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await Phase5(context);
        context.ChangeTracker.Clear();

        (await context.Repairs.AsNoTracking().SingleAsync()).Status.Should().Be("Cancelled");
    }

    // ── Relecture du 22/09/2026 : le statut annulé conservé se tait ─────────────
    // « Annulée » est de nouveau proposée dans l'écran Réparations. Le coût réel saisi
    // ensuite en phase 5 était écrit sur la ligne annulée — donc compté nulle part —
    // alors que le dossier l'affiche, sans un mot à l'écran.

    [Fact]
    public async Task CoutReelSurUneReparationAnnulee_EstSignaleALEcran()
    {
        using var context = Contexte();
        await Phase5(context);
        context.ChangeTracker.Clear();

        var reparation = await context.Repairs.SingleAsync();
        reparation.Status = RepairInputRules.Cancelled;
        await context.SaveChangesAsync();
        var reference = reparation.Reference;
        context.ChangeTracker.Clear();

        var result = await Handler(context).Handle(
            new RegisterRepairCommand(AccidentId, Debut, Fin, 2000m), CancellationToken.None);
        context.ChangeTracker.Clear();

        var apres = await context.Repairs.AsNoTracking().SingleAsync();
        apres.Status.Should().Be(RepairInputRules.Cancelled, "le statut choisi à l'écran reste le sien");
        apres.TotalCost.Should().Be(2000m);
        result.Synced.Should().BeTrue();
        result.Warning.Should().Contain(reference).And.Contain("annulée")
            .And.Contain("aucun coût", "l'utilisateur doit savoir que ce montant n'est compté nulle part");
    }

    [Fact]
    public async Task ReparationAnnulee_LaDepenseDuDossierAnterieurNEstPasRetiree()
    {
        using var context = Contexte();
        await Phase5(context);
        context.ChangeTracker.Clear();

        var reparation = await context.Repairs.SingleAsync();
        reparation.Status = RepairInputRules.Cancelled;
        // Dépense « repair » d'un dossier antérieur à la migration 049, encore liée.
        context.VehicleCosts.Add(new VehicleCost
        {
            VehicleId = VehiculeExistant, CompanyId = CompanyId, AccidentEventId = AccidentId,
            Type = "repair", Description = "Réparation accident — ACC-2026-014", Amount = 1200m, Date = Fin,
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await Phase5(context, 2000m);
        context.ChangeTracker.Clear();

        (await context.VehicleCosts.AsNoTracking().CountAsync(c => c.AccidentEventId == AccidentId))
            .Should().Be(1, "la réparation annulée ne compte nulle part : retirer la dépense effaçait le montant des coûts");
    }

    [Fact]
    public async Task ReparationNonAnnulee_AucunAvertissementDeStatut()
    {
        using var context = Contexte();
        await Phase5(context);
        context.ChangeTracker.Clear();

        var result = await Handler(context).Handle(
            new RegisterRepairCommand(AccidentId, Debut, Fin, 1500m), CancellationToken.None);

        result.Warning.Should().BeNull();
    }

    [Fact]
    public async Task ReparationNonAnnulee_SuitToujoursLaDateDeFinDeLaPhase()
    {
        using var context = Contexte();
        await Phase5(context);
        context.ChangeTracker.Clear();

        // Date de fin effacée dans la phase : le véhicule est reparti à l'atelier.
        await Handler(context).Handle(
            new RegisterRepairCommand(AccidentId, Debut, null, 1200m), CancellationToken.None);
        context.ChangeTracker.Clear();

        (await context.Repairs.AsNoTracking().SingleAsync()).Status.Should().Be(RepairInputRules.InProgress);
    }
}
