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
/// Recette Karim du 18/09/2026 : « le montant de remboursement de l'assureur n'est pas
/// ajouté dans dépenses ». La phase 6 n'écrivait la dépense que si le montant approuvé
/// était strictement positif, et ne savait rien défaire : un montant corrigé à la baisse
/// laissait l'ancienne ligne, un montant effacé laissait la dépense pour toujours et le
/// véhicule supprimé faisait tout passer à la trappe en silence.
/// </summary>
public class AccidentRefundSyncTests
{
    private const int CompanyId = 7;
    private const int AccidentId = 1;
    private const int VehiculeExistant = 49;
    private const int VehiculeSupprime = 50;
    private static readonly DateTime Depot = new(2026, 9, 16, 9, 0, 0, DateTimeKind.Utc);

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
            IncidentAt = Depot.AddDays(-5),
            Confidence = 100,
            Origin = "manual",
            Status = "confirmed",
            ReferenceCode = "ACC-2026-014",
        });
        context.SaveChanges();
        context.ChangeTracker.Clear();
        return context;
    }

    private static ICurrentTenantService Tenant() =>
        TestDbContextFactory.CreateMockTenantService(companyId: CompanyId).Object;

    private static RegisterClaimCommandHandler Handler(TestGisDbContext context) =>
        new(context, Tenant(), NullLogger<RegisterClaimCommandHandler>.Instance);

    private static RegisterClaimCommand Commande(decimal? montant, string? statut = "approved") =>
        new(AccidentId, "QA-SIN-001", Depot, montant, statut, null);

    [Fact]
    public async Task MontantApprouveCorrige_MetAJourLaDepense()
    {
        using var context = Contexte(VehiculeExistant);

        await Handler(context).Handle(Commande(900m), CancellationToken.None);
        context.ChangeTracker.Clear();
        await Handler(context).Handle(Commande(750m, "partial"), CancellationToken.None);
        context.ChangeTracker.Clear();

        var cout = await context.VehicleCosts.AsNoTracking().SingleAsync();
        cout.Type.Should().Be("insurance_refund");
        cout.Amount.Should().Be(750m);
    }

    [Fact]
    public async Task MontantEfface_SupprimeLaDepenseLiee()
    {
        using var context = Contexte(VehiculeExistant);

        await Handler(context).Handle(Commande(900m), CancellationToken.None);
        context.ChangeTracker.Clear();
        await Handler(context).Handle(Commande(null, "rejected"), CancellationToken.None);
        context.ChangeTracker.Clear();

        (await context.VehicleCosts.CountAsync()).Should().Be(0,
            "un remboursement saisi par erreur doit pouvoir être retiré de Dépenses");
        (await context.AccidentEvents.AsNoTracking().SingleAsync()).ClaimApprovedAmount.Should().BeNull();
    }

    [Fact]
    public async Task MontantRemisAZero_SupprimeLaDepenseLiee()
    {
        using var context = Contexte(VehiculeExistant);

        await Handler(context).Handle(Commande(900m), CancellationToken.None);
        context.ChangeTracker.Clear();
        await Handler(context).Handle(Commande(0m, "rejected"), CancellationToken.None);
        context.ChangeTracker.Clear();

        (await context.VehicleCosts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task MontantNegatif_SupprimeLaDepenseLiee()
    {
        using var context = Contexte(VehiculeExistant);

        await Handler(context).Handle(Commande(900m), CancellationToken.None);
        context.ChangeTracker.Clear();
        await Handler(context).Handle(Commande(-50m, "rejected"), CancellationToken.None);
        context.ChangeTracker.Clear();

        (await context.VehicleCosts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DepenseLibreDuMemeVehicule_NEstJamaisTouchee()
    {
        using var context = Contexte(VehiculeExistant);
        // Dépense saisie à la main dans l'écran Dépenses : aucun lien avec le dossier.
        context.VehicleCosts.Add(new VehicleCost
        {
            VehicleId = VehiculeExistant,
            CompanyId = CompanyId,
            Type = "insurance_refund",
            Description = "Remboursement saisi à la main",
            Amount = 300m,
            Date = Depot,
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await Handler(context).Handle(Commande(null, "rejected"), CancellationToken.None);
        context.ChangeTracker.Clear();

        var restante = await context.VehicleCosts.AsNoTracking().SingleAsync();
        restante.Amount.Should().Be(300m);
        restante.AccidentEventId.Should().BeNull();
    }

    [Fact]
    public async Task VehiculeSupprime_DossierDetache_LAppelantEstAverti()
    {
        // Le cas RÉEL : la cascade fige vehicle_label puis met vehicle_id à NULL.
        using var context = Contexte(null, libelleVehicule: "GA-214-RK");

        var result = await Handler(context).Handle(Commande(900m), CancellationToken.None);

        result.Synced.Should().BeFalse();
        result.Reason.Should().Be("vehicle_deleted", "un libellé figé sans identifiant dit « véhicule supprimé »");
        result.Warning.Should().Contain("supprimé");
        context.ChangeTracker.Clear();
        (await context.VehicleCosts.CountAsync()).Should().Be(0);
        (await context.AccidentEvents.AsNoTracking().SingleAsync()).ClaimApprovedAmount.Should().Be(900m);
    }

    [Fact]
    public async Task DossierSansVehicule_LAppelantEstAverti()
    {
        using var context = Contexte(null);

        var result = await Handler(context).Handle(Commande(900m), CancellationToken.None);

        result.Synced.Should().BeFalse();
        result.Reason.Should().Be("no_vehicle", "sans libellé figé, rien ne dit qu'un véhicule a été supprimé");
        context.ChangeTracker.Clear();
        (await context.VehicleCosts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task VehicleIdPendant_LAppelantEstAverti()
    {
        // vehicle_id qui ne désigne plus aucune ligne : la cascade ne produit plus ce cas
        // depuis DEF-046, il reste possible sur des données anciennes.
        using var context = Contexte(VehiculeSupprime);

        var result = await Handler(context).Handle(Commande(900m), CancellationToken.None);

        result.Synced.Should().BeFalse();
        result.Reason.Should().Be("vehicle_deleted");
        context.ChangeTracker.Clear();
        (await context.VehicleCosts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task VehiculeExistant_AucunAvertissement()
    {
        using var context = Contexte(VehiculeExistant);

        var result = await Handler(context).Handle(Commande(900m), CancellationToken.None);

        result.Synced.Should().BeTrue();
        result.Warning.Should().BeNull();
    }
}
