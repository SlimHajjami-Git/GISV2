using FluentAssertions;
using GisAPI.Application.Features.AccidentEvents.Commands;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Application.AccidentEvents;

/// <summary>
/// Recette Karim du 18/09/2026 : « il faut pas oublier d'ajouter la réparation de
/// l'accident dans RÉPARATIONS ». La phase 5 d'un sinistre créait une dépense
/// <c>vehicle_costs</c> de type « repair » ; l'écran Réparations lit la table
/// <c>repairs</c>, la réparation d'un accident n'y a donc jamais figuré.
///
/// Ces tests fixent le contrat de la phase 5 : une ligne de réparation rattachée au
/// dossier, mise à jour et non dupliquée, jamais comptée deux fois dans les rapports de
/// coûts, et un avertissement explicite quand le dossier n'a plus de véhicule.
///
/// <para>Quand le coût est effacé, la phase ne retire que ce qu'elle a posé elle-même :
/// une réparation ou une dépense enrichie dans son propre écran (pièces, numéro de
/// facture, fournisseur, justificatif scanné) est DÉTACHÉE et l'écran le dit.</para>
/// </summary>
public class AccidentRepairSyncTests
{
    private const int CompanyId = 7;
    private const int AccidentId = 1;
    private const int VehiculeExistant = 49;
    private const int VehiculeSupprime = 50;
    private static readonly DateTime Debut = new(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Fin = new(2026, 9, 14, 17, 0, 0, DateTimeKind.Utc);

    /// <param name="vehiculeDuSinistre">
    /// <c>null</c> = dossier détaché, le cas que la suppression d'un véhicule produit
    /// RÉELLEMENT (vehicle_id passé à NULL après que vehicle_label a été figé).
    /// </param>
    /// <param name="libelleVehicule">Libellé figé par la cascade avant le détachement.</param>
    private static TestGisDbContext Contexte(int? vehiculeDuSinistre, string? libelleVehicule = null)
    {
        var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = VehiculeExistant, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId });
        context.AccidentEvents.Add(new AccidentEvent
        {
            Id = AccidentId,
            CompanyId = CompanyId,
            VehicleId = vehiculeDuSinistre,
            VehicleLabel = libelleVehicule,
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

    private static ICurrentTenantService Tenant() =>
        TestDbContextFactory.CreateMockTenantService(companyId: CompanyId).Object;

    private static RegisterRepairCommandHandler Handler(TestGisDbContext context) =>
        new(context, Tenant(), NullLogger<RegisterRepairCommandHandler>.Instance);

    [Fact]
    public async Task CoutReel_CreeUneLigneDansLEcranReparations()
    {
        using var context = Contexte(VehiculeExistant);

        var result = await Handler(context).Handle(
            new RegisterRepairCommand(AccidentId, Debut, Fin, 1200m), CancellationToken.None);

        result.Synced.Should().BeTrue();
        context.ChangeTracker.Clear();

        var reparation = await context.Repairs.AsNoTracking().SingleAsync();
        reparation.AccidentEventId.Should().Be(AccidentId);
        reparation.VehicleId.Should().Be(VehiculeExistant);
        reparation.SocieteId.Should().Be(CompanyId);
        reparation.TotalCost.Should().Be(1200m);
        reparation.Status.Should().Be("completed");
        reparation.Reference.Should().StartWith("REP-");
        reparation.Description.Should().Contain("ACC-2026-014");
        reparation.Notes.Should().Contain("Garage Central");
        reparation.RepairDate.Should().Be(Fin, "la facture tombe au mois de la fin de réparation");

        // Plus de dépense « repair » : le montant ne doit être porté que par la réparation.
        (await context.VehicleCosts.CountAsync(c => c.Type == "repair")).Should().Be(0);
    }

    [Fact]
    public async Task CoutReelCorrige_MetAJourLaMemeLigne()
    {
        using var context = Contexte(VehiculeExistant);

        await Handler(context).Handle(new RegisterRepairCommand(AccidentId, Debut, Fin, 1200m), CancellationToken.None);
        context.ChangeTracker.Clear();
        await Handler(context).Handle(new RegisterRepairCommand(AccidentId, Debut, Fin, 1500m), CancellationToken.None);
        context.ChangeTracker.Clear();

        var reparation = await context.Repairs.AsNoTracking().SingleAsync();
        reparation.TotalCost.Should().Be(1500m, "une seconde sauvegarde corrige la ligne au lieu d'en empiler une");
        reparation.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ReparationNonTerminee_ResteEnCours()
    {
        using var context = Contexte(VehiculeExistant);

        await Handler(context).Handle(
            new RegisterRepairCommand(AccidentId, Debut, null, 800m), CancellationToken.None);
        context.ChangeTracker.Clear();

        var reparation = await context.Repairs.AsNoTracking().SingleAsync();
        reparation.Status.Should().Be("in_progress");
        reparation.RepairDate.Should().Be(Debut);
    }

    [Fact]
    public async Task CoutEfface_RetireLaLigneDeReparation()
    {
        using var context = Contexte(VehiculeExistant);

        await Handler(context).Handle(new RegisterRepairCommand(AccidentId, Debut, Fin, 1200m), CancellationToken.None);
        context.ChangeTracker.Clear();
        await Handler(context).Handle(new RegisterRepairCommand(AccidentId, Debut, Fin, null), CancellationToken.None);
        context.ChangeTracker.Clear();

        (await context.Repairs.CountAsync()).Should().Be(0, "un coût saisi par erreur doit pouvoir être retiré");
        (await context.AccidentEvents.AsNoTracking().SingleAsync()).ActualRepairCost.Should().BeNull();
    }

    [Fact]
    public async Task MontantZero_RetireLaLigneDeReparation()
    {
        using var context = Contexte(VehiculeExistant);

        await Handler(context).Handle(new RegisterRepairCommand(AccidentId, Debut, Fin, 1200m), CancellationToken.None);
        context.ChangeTracker.Clear();
        await Handler(context).Handle(new RegisterRepairCommand(AccidentId, Debut, Fin, 0m), CancellationToken.None);
        context.ChangeTracker.Clear();

        (await context.Repairs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CoutEfface_ReparationEnrichieDePieces_EstConserveeEtDetachee()
    {
        using var context = Contexte(VehiculeExistant);
        await Handler(context).Handle(new RegisterRepairCommand(AccidentId, Debut, Fin, 1200m), CancellationToken.None);
        context.ChangeTracker.Clear();

        // La ligne vit aussi dans l'écran Réparations : l'utilisateur y a détaillé la
        // facture du garage. Effacer le coût du dossier emportait tout, sans un mot.
        var reparation = await context.Repairs.SingleAsync();
        context.RepairParts.Add(new RepairPart
        {
            RepairId = reparation.Id, PartName = "Pare-chocs avant", Quantity = 1,
            UnitPrice = 500m, Subtotal = 500m,
        });
        reparation.InvoiceNumber = "F-2026-118";
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var result = await Handler(context).Handle(
            new RegisterRepairCommand(AccidentId, Debut, Fin, null), CancellationToken.None);

        result.Warning.Should().Contain("conservée, détachée");
        context.ChangeTracker.Clear();
        var apres = await context.Repairs.AsNoTracking().SingleAsync();
        apres.AccidentEventId.Should().BeNull("le lien au dossier tombe, la saisie reste");
        apres.InvoiceNumber.Should().Be("F-2026-118");
        (await context.RepairParts.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CoutEfface_RetireAussiLAncienneDepenseDuDossier()
    {
        using var context = Contexte(VehiculeExistant);
        // Dossier antérieur à la migration 049, jamais repris à la main.
        context.VehicleCosts.Add(new VehicleCost
        {
            VehicleId = VehiculeExistant, CompanyId = CompanyId, AccidentEventId = AccidentId,
            Type = "repair", Description = "Réparation accident — ACC-2026-014", Amount = 1200m, Date = Fin,
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var result = await Handler(context).Handle(
            new RegisterRepairCommand(AccidentId, Debut, Fin, null), CancellationToken.None);

        result.Warning.Should().BeNull();
        context.ChangeTracker.Clear();
        (await context.VehicleCosts.CountAsync()).Should().Be(0, "le montant vidé quitte aussi Dépenses");
    }

    [Fact]
    public async Task DepenseDuDossierPorteuseDUnJustificatif_EstConserveeEtDetachee()
    {
        using var context = Contexte(VehiculeExistant);
        // Dépense « repair » d'un dossier ancien, reprise dans l'écran Dépenses : montant
        // corrigé, fournisseur, et surtout le justificatif scanné. La supprimer laissait
        // son fichier orphelin sur le disque, sans un mot à l'écran.
        context.VehicleCosts.Add(new VehicleCost
        {
            VehicleId = VehiculeExistant, CompanyId = CompanyId, AccidentEventId = AccidentId,
            Type = "repair", Description = "Réparation accident — ACC-2026-014", Amount = 1200m, Date = Fin,
            ReceiptUrl = "/uploads/costs/7/facture-garage.pdf",
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var result = await Handler(context).Handle(
            new RegisterRepairCommand(AccidentId, Debut, Fin, 1200m), CancellationToken.None);

        result.Synced.Should().BeTrue("la phase et sa réparation s'enregistrent quand même");
        result.Warning.Should().Contain("justificatif");
        context.ChangeTracker.Clear();
        var depense = await context.VehicleCosts.AsNoTracking().SingleAsync();
        depense.ReceiptUrl.Should().Be("/uploads/costs/7/facture-garage.pdf");
        depense.AccidentEventId.Should().BeNull("détachée, pas détruite");
        (await context.Repairs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DepensesDuDossierRetypees_SontTraiteesCommeDesReparations()
    {
        using var context = Contexte(VehiculeExistant);
        // Types corrigés dans l'écran Dépenses. « réparation » et le libellé d'export
        // « Réparation accident » sont rangés en Réparations par les rapports : ignorés
        // par le retrait, ils restaient rattachés au dossier EN PLUS de la ligne de
        // réparation, et le montant comptait deux fois, en silence.
        context.VehicleCosts.Add(new VehicleCost
        {
            VehicleId = VehiculeExistant, CompanyId = CompanyId, AccidentEventId = AccidentId,
            Type = "réparation", Description = "Réparation accident — ACC-2026-014", Amount = 700m, Date = Fin,
        });
        context.VehicleCosts.Add(new VehicleCost
        {
            VehicleId = VehiculeExistant, CompanyId = CompanyId, AccidentEventId = AccidentId,
            Type = "Réparation accident", Description = "Franchise", Amount = 500m, Date = Fin,
            Notes = "Franchise réglée en espèces",
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var result = await Handler(context).Handle(
            new RegisterRepairCommand(AccidentId, Debut, Fin, 1200m), CancellationToken.None);

        result.Synced.Should().BeTrue();
        result.Warning.Should().Contain("détachée du sinistre", "la ligne notée à la main est conservée, pas détruite");
        context.ChangeTracker.Clear();

        var restante = await context.VehicleCosts.AsNoTracking().SingleAsync();
        restante.Notes.Should().Be("Franchise réglée en espèces");
        restante.AccidentEventId.Should().BeNull("détachée, pas détruite");
        (await context.Repairs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RemboursementDAssurance_NEstPasEmporteParLeMenageDeLaPhase5()
    {
        using var context = Contexte(VehiculeExistant);
        // CE QUE CE TEST PROTÈGE : l'argent que l'assureur rend au client. La phase 6 le
        // pose dans Dépenses en ligne « insurance_refund » rattachée au MÊME dossier ; la
        // phase 5, elle, fait le ménage des dépenses du dossier rangées en Réparations.
        // Le remboursement ci-dessous est vierge de toute reprise à la main : s'il était
        // un jour classé en Réparations, il serait donc SUPPRIMÉ — pas même détaché, et
        // sans un mot à l'écran. Il n'échappe au ménage que parce que
        // VehicleCostCategory range « insurance_refund » en Autres, en crédit.
        context.VehicleCosts.Add(new VehicleCost
        {
            VehicleId = VehiculeExistant, CompanyId = CompanyId, AccidentEventId = AccidentId,
            Type = "insurance_refund", Description = "Remboursement assurance — QA-SIN-001",
            Amount = 900m, Date = Fin,
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var result = await Handler(context).Handle(
            new RegisterRepairCommand(AccidentId, Debut, Fin, 1200m), CancellationToken.None);

        result.Synced.Should().BeTrue();
        result.Warning.Should().BeNull("le remboursement n'est ni retiré ni détaché : la phase 5 n'a rien à signaler");
        context.ChangeTracker.Clear();

        var remboursement = await context.VehicleCosts.AsNoTracking().SingleAsync();
        remboursement.Type.Should().Be("insurance_refund");
        remboursement.Amount.Should().Be(900m);
        remboursement.AccidentEventId.Should().Be(AccidentId, "le crédit de l'assureur reste rattaché au sinistre");
        (await context.Repairs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task VehiculeSupprime_DossierDetache_RienNEstEcritEtLAppelantEstAverti()
    {
        // Le cas RÉEL : la cascade fige vehicle_label puis met vehicle_id à NULL.
        using var context = Contexte(null, libelleVehicule: "GA-214-RK");

        var result = await Handler(context).Handle(
            new RegisterRepairCommand(AccidentId, Debut, Fin, 1200m), CancellationToken.None);

        result.Synced.Should().BeFalse();
        result.Reason.Should().Be("vehicle_deleted", "un libellé figé sans identifiant dit « véhicule supprimé »");
        result.Warning.Should().Contain("supprimé");
        context.ChangeTracker.Clear();

        (await context.Repairs.CountAsync()).Should().Be(0);
        (await context.VehicleCosts.CountAsync()).Should().Be(0);
        // Le montant saisi reste : refuser la phase pousserait à l'effacer.
        (await context.AccidentEvents.AsNoTracking().SingleAsync()).ActualRepairCost.Should().Be(1200m);
    }

    [Fact]
    public async Task DossierSansVehicule_LAppelantEstAverti()
    {
        using var context = Contexte(null);

        var result = await Handler(context).Handle(
            new RegisterRepairCommand(AccidentId, Debut, Fin, 1200m), CancellationToken.None);

        result.Synced.Should().BeFalse();
        result.Reason.Should().Be("no_vehicle", "sans libellé figé, rien ne dit qu'un véhicule a été supprimé");
        context.ChangeTracker.Clear();
        (await context.Repairs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task VehicleIdPendant_RienNEstEcritEtLAppelantEstAverti()
    {
        // vehicle_id qui ne désigne plus aucune ligne. La cascade ne produit plus ce cas
        // depuis DEF-046 ; il reste possible sur des données anciennes, et une lecture ne
        // doit pas planter dessus.
        using var context = Contexte(VehiculeSupprime);

        var result = await Handler(context).Handle(
            new RegisterRepairCommand(AccidentId, Debut, Fin, 1200m), CancellationToken.None);

        result.Synced.Should().BeFalse();
        result.Reason.Should().Be("vehicle_deleted");
        context.ChangeTracker.Clear();
        (await context.Repairs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task LAncienneDepenseDuDossier_CedeLaPlaceALaReparation()
    {
        using var context = Contexte(VehiculeExistant);
        // Dossier antérieur à la migration 049 : son coût vit dans Dépenses.
        context.VehicleCosts.Add(new VehicleCost
        {
            VehicleId = VehiculeExistant,
            CompanyId = CompanyId,
            AccidentEventId = AccidentId,
            Type = "repair",
            Description = "Réparation accident — ACC-2026-014",
            Amount = 1200m,
            Date = Fin,
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await Handler(context).Handle(new RegisterRepairCommand(AccidentId, Debut, Fin, 1200m), CancellationToken.None);
        context.ChangeTracker.Clear();

        (await context.VehicleCosts.CountAsync(c => c.Type == "repair")).Should().Be(0);
        (await context.Repairs.CountAsync()).Should().Be(1);

        // Le rapport de coûts additionne repairs ET les dépenses de catégorie
        // Réparation : sans le retrait ci-dessus, 1 200 auraient été comptés deux fois.
        var aggregate = await OperatingCostAggregator.LoadAsync(
            context, Tenant(),
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            null, null, CancellationToken.None);

        aggregate.Vehicles.Single().Total.Repair.Should().Be(1200m);
        aggregate.Vehicles.Single().Total.RepairCount.Should().Be(1);
    }

    [Fact]
    public async Task PiecesAjouteesAlaMain_SurviventAUneSecondeSauvegardeDeLaPhase()
    {
        using var context = Contexte(VehiculeExistant);
        await Handler(context).Handle(new RegisterRepairCommand(AccidentId, Debut, Fin, 1200m), CancellationToken.None);
        context.ChangeTracker.Clear();

        // La réparation figure désormais dans l'écran Réparations, où elle se modifie
        // comme les autres : l'utilisateur y saisit les pièces de la facture du garage.
        var reparation = await context.Repairs.SingleAsync();
        context.RepairParts.Add(new RepairPart
        {
            RepairId = reparation.Id,
            PartName = "Pare-chocs avant",
            Quantity = 1,
            UnitPrice = 500m,
            Subtotal = 500m,
        });
        reparation.PartsCost = 500m;
        reparation.LaborCost = 700m;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await Handler(context).Handle(new RegisterRepairCommand(AccidentId, Debut, Fin, 1200m), CancellationToken.None);
        context.ChangeTracker.Clear();

        var apres = await context.Repairs.AsNoTracking().SingleAsync();
        apres.PartsCost.Should().Be(500m, "les lignes de pièces restent affichées avec leurs sous-totaux");
        apres.LaborCost.Should().Be(700m);
        apres.TotalCost.Should().Be(1200m);
        (apres.LaborCost + apres.PartsCost).Should().Be(apres.TotalCost, "c'est l'addition qu'affiche l'écran Réparations");
        (await context.RepairParts.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task PiecesSuperieuresAuCoutReel_NeCassentPasLAddition()
    {
        using var context = Contexte(VehiculeExistant);
        await Handler(context).Handle(new RegisterRepairCommand(AccidentId, Debut, Fin, 1200m), CancellationToken.None);
        context.ChangeTracker.Clear();

        var reparation = await context.Repairs.SingleAsync();
        context.RepairParts.Add(new RepairPart
        {
            RepairId = reparation.Id,
            PartName = "Moteur complet",
            Quantity = 1,
            UnitPrice = 1500m,
            Subtotal = 1500m,
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var result = await Handler(context).Handle(
            new RegisterRepairCommand(AccidentId, Debut, Fin, 1200m), CancellationToken.None);

        // La fiche affichait des lignes de pièces dont la somme ne correspondait plus au
        // sous-total « pièces » de l'en-tête : seul le journal le disait.
        result.Warning.Should().Contain("main-d'œuvre a été ramenée à 0");
        context.ChangeTracker.Clear();

        var apres = await context.Repairs.AsNoTracking().SingleAsync();
        apres.TotalCost.Should().Be(1200m, "le coût réel facturé du sinistre reste la vérité");
        apres.LaborCost.Should().Be(0m);
        (apres.LaborCost + apres.PartsCost).Should().Be(apres.TotalCost);
    }

    [Fact]
    public async Task MontantHorsCapaciteDeLaColonne_EstRefuseAvantToutEcriture()
    {
        using var context = Contexte(VehiculeExistant);

        var refus = async () => await Handler(context).Handle(
            new RegisterRepairCommand(AccidentId, Debut, Fin, 100_000_000m), CancellationToken.None);

        await refus.Should().ThrowAsync<GisAPI.Domain.Exceptions.DomainException>();
        context.ChangeTracker.Clear();
        (await context.Repairs.CountAsync()).Should().Be(0);
        (await context.AccidentEvents.AsNoTracking().SingleAsync()).ActualRepairCost.Should().BeNull();
    }

    [Fact]
    public async Task SuppressionDuDossier_DetacheLaReparationSansLEffacer()
    {
        using var context = Contexte(VehiculeExistant);
        await Handler(context).Handle(new RegisterRepairCommand(AccidentId, Debut, Fin, 1200m), CancellationToken.None);
        context.ChangeTracker.Clear();

        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId);
        tenant.Setup(t => t.IsSystemAdmin).Returns(true);
        var suppression = new DeleteAccidentEventCommandHandler(
            context, tenant.Object, NullLogger<DeleteAccidentEventCommandHandler>.Instance);

        var result = await suppression.Handle(new DeleteAccidentEventCommand(AccidentId, null), CancellationToken.None);

        result.DetachedRepairs.Should().Be(1);
        context.ChangeTracker.Clear();
        var reparation = await context.Repairs.AsNoTracking().SingleAsync();
        reparation.AccidentEventId.Should().BeNull("l'argent reste dans l'écran Réparations");
        reparation.TotalCost.Should().Be(1200m);
    }
}
