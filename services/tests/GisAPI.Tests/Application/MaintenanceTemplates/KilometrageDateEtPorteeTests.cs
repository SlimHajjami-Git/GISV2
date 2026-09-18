using FluentAssertions;
using GisAPI.Application.Features.FuelEntries.Commands;
using GisAPI.Application.Features.Repairs;
using GisAPI.Application.Features.Repairs.Commands;
using GisAPI.Application.Features.Repairs.Handlers;
using GisAPI.Application.Features.VehicleMaintenance.Commands;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.MaintenanceTemplates;

/// <summary>
/// Revue de l'intégration du 18/09/2026.
///
/// <para>1. « Un compteur ne recule pas » comparait tout kilométrage au compteur COURANT,
/// quelle que soit la date saisie. Un entretien fait le 06/07 à 10 000 km et saisi le 11/08,
/// quand le boîtier affichait 18 593, était refusé (22 entretiens HERTZ sur 39 saisis ainsi).
/// Le plancher est désormais daté : compteur courant pour le jour même, relevé connu à la date
/// pour une saisie après coup.</para>
///
/// <para>2. Les écritures rattachées à un véhicule (entretien, réparation, plein) ne filtraient
/// que par société : un employé restreint écrivait sur un véhicule qu'il ne voit pas.</para>
/// </summary>
public class KilometrageDateEtPorteeTests
{
    private const int CompanyId = 7;
    private const int VehiculeId = 1;
    private const int AutreVehiculeId = 2;
    private const int EmployeId = 11;
    private const int ModeleId = 1;
    private const int FuelTypeGazole = 1;

    private static readonly DateTime Aujourdhui = DateTime.UtcNow.Date;

    private static TestGisDbContext Contexte(int mileage = 80_000)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = VehiculeId, Name = "V1", Plate = "AB-123-CD", CompanyId = CompanyId, Mileage = mileage });
        ctx.Vehicles.Add(new Vehicle { Id = AutreVehiculeId, Name = "V2", Plate = "EF-456-GH", CompanyId = CompanyId, Mileage = 10_000 });
        ctx.MaintenanceTemplates.Add(new MaintenanceTemplate
        {
            Id = ModeleId, Name = "Vidange", Category = "Moteur", Priority = "medium",
            IntervalKm = 10_000, IntervalMonths = 12, CompanyId = CompanyId
        });
        ctx.FuelTypes.Add(new FuelType { Id = FuelTypeGazole, Code = "diesel", Name = "Gazole" });
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    private static ICurrentTenantService Admin() =>
        TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: 1).Object;

    /// <summary>Employé NON administrateur affecté au seul véhicule 1.</summary>
    private static ICurrentTenantService EmployeRestreint(TestGisDbContext ctx)
    {
        ctx.UserVehicles.Add(new UserVehicle { UserId = EmployeId, VehicleId = VehiculeId });
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: EmployeId);
        tenant.Setup(t => t.UserRoles).Returns(new[] { "user" });
        return tenant.Object;
    }

    private static MarkMaintenanceDoneCommand Entretien(DateTime date, int km, int vehiculeId = VehiculeId) =>
        new(VehicleId: vehiculeId, TemplateId: ModeleId, Date: date, Mileage: km, Cost: 120m, SupplierId: null, Notes: null);

    // ── Plancher daté : entretiens ──────────────────────────────────────────────

    [Fact]
    public async Task Entretien_saisi_apres_coup_avec_le_kilometrage_de_la_facture_est_accepte()
    {
        using var ctx = Contexte(mileage: 18_593); // compteur courant (boîtier)

        var logId = await new MarkMaintenanceDoneCommandHandler(ctx, Admin())
            .Handle(Entretien(Aujourdhui.AddDays(-36), 10_000), CancellationToken.None);

        var log = await ctx.MaintenanceLogs.FindAsync(logId);
        log!.DoneKm.Should().Be(10_000);
        (await ctx.Vehicles.FindAsync(VehiculeId))!.Mileage.Should().Be(18_593, "le compteur ne recule jamais");
    }

    [Fact]
    public async Task Entretien_saisi_apres_coup_sous_un_releve_anterieur_est_refuse()
    {
        using var ctx = Contexte(mileage: 80_000);
        // Plein du 01 du mois dernier à 60 000 km : un entretien du 10 ne peut pas être à 6 000.
        ctx.FuelEntries.Add(new FuelEntry
        {
            CompanyId = CompanyId, VehicleId = VehiculeId, FuelTypeId = FuelTypeGazole, Volume = 40, TotalAmount = 70,
            InvoiceDate = Aujourdhui.AddDays(-40), OdometerKm = 60_000
        });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var act = () => new MarkMaintenanceDoneCommandHandler(ctx, Admin())
            .Handle(Entretien(Aujourdhui.AddDays(-30), 6_000), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*inférieur à un relevé déjà enregistré*60*000 km*Un compteur ne recule pas*");
        ctx.MaintenanceLogs.Should().BeEmpty();
        ctx.VehicleCosts.Should().BeEmpty();
    }

    [Fact]
    public async Task Entretien_du_jour_reste_compare_au_compteur_courant()
    {
        using var ctx = Contexte(mileage: 80_000);

        var act = () => new MarkMaintenanceDoneCommandHandler(ctx, Admin())
            .Handle(Entretien(Aujourdhui, 8_000), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*inférieur au kilométrage actuel du véhicule*");
    }

    [Fact]
    public async Task Entretien_anterieur_au_dernier_connu_ne_recule_pas_l_echeance()
    {
        using var ctx = Contexte(mileage: 90_000);
        ctx.VehicleMaintenanceSchedules.Add(new VehicleMaintenanceSchedule
        {
            Id = 1, VehicleId = VehiculeId, TemplateId = ModeleId, CompanyId = CompanyId,
            LastDoneDate = Aujourdhui.AddDays(-10), LastDoneKm = 88_000,
            NextDueKm = 98_000, NextDueDate = Aujourdhui.AddDays(-10).AddMonths(12), Status = "ok"
        });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var logId = await new MarkMaintenanceDoneCommandHandler(ctx, Admin())
            .Handle(Entretien(Aujourdhui.AddDays(-200), 70_000), CancellationToken.None);

        ctx.ChangeTracker.Clear();
        (await ctx.MaintenanceLogs.FindAsync(logId))!.DoneKm.Should().Be(70_000, "l'historique garde la saisie");
        var echeance = await ctx.VehicleMaintenanceSchedules.AsNoTracking().SingleAsync();
        echeance.LastDoneKm.Should().Be(88_000);
        echeance.NextDueKm.Should().Be(98_000, "recaler sur un passage plus ancien avancerait l'alerte à tort");
    }

    // ── Plancher daté : pleins (saisie et import en masse) ───────────────────────

    private static CreateFuelEntryCommand Plein(DateTime date, long km, string plaque = "AB-123-CD") =>
        new(plaque, FuelTypeGazole, 40m, 1.8m, date, null, null, null, null, km);

    [Fact]
    public async Task Plein_ancien_importe_apres_coup_est_accepte()
    {
        using var ctx = Contexte(mileage: 80_000);

        var id = await new CreateFuelEntryCommandHandler(ctx, Admin(), Mock.Of<IPublisher>())
            .Handle(Plein(Aujourdhui.AddDays(-60), 71_000), CancellationToken.None);

        id.Should().BeGreaterThan(0);
        (await ctx.Vehicles.FindAsync(VehiculeId))!.Mileage.Should().Be(80_000);
    }

    [Fact]
    public async Task Plein_du_jour_sous_le_compteur_courant_reste_refuse()
    {
        using var ctx = Contexte(mileage: 80_000);

        var act = () => new CreateFuelEntryCommandHandler(ctx, Admin(), Mock.Of<IPublisher>())
            .Handle(Plein(Aujourdhui, 71_000), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*Un compteur ne recule pas*");
    }

    // ── Portée véhicules sur les écritures ───────────────────────────────────────

    [Fact]
    public async Task Employe_restreint_n_enregistre_pas_d_entretien_sur_un_vehicule_hors_portee()
    {
        using var ctx = Contexte();
        var tenant = EmployeRestreint(ctx);

        var act = () => new MarkMaintenanceDoneCommandHandler(ctx, tenant)
            .Handle(Entretien(Aujourdhui, 10_000, vehiculeId: AutreVehiculeId), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        ctx.VehicleCosts.Should().BeEmpty();
        ctx.MaintenanceLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task Employe_restreint_enregistre_un_entretien_sur_son_vehicule()
    {
        using var ctx = Contexte();
        var tenant = EmployeRestreint(ctx);

        var logId = await new MarkMaintenanceDoneCommandHandler(ctx, tenant)
            .Handle(Entretien(Aujourdhui, 80_000), CancellationToken.None);

        logId.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Employe_restreint_ne_rattache_pas_un_plein_a_un_vehicule_hors_portee()
    {
        using var ctx = Contexte();
        var tenant = EmployeRestreint(ctx);

        var act = () => new CreateFuelEntryCommandHandler(ctx, tenant, Mock.Of<IPublisher>())
            .Handle(Plein(Aujourdhui, 10_500, plaque: "EF-456-GH"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Aucun véhicule ne correspond*");
        ctx.FuelEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task Employe_restreint_ne_cree_ni_ne_modifie_ni_ne_supprime_une_reparation_hors_portee()
    {
        using var ctx = Contexte();
        ctx.Repairs.Add(new Repair
        {
            Id = 1, SocieteId = CompanyId, VehicleId = AutreVehiculeId, Reference = "REP-1", Description = "Plaquettes",
            RepairDate = Aujourdhui, LaborCost = 100m, TotalCost = 100m, Status = "pending"
        });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        var tenant = EmployeRestreint(ctx);

        var creation = () => new CreateRepairCommandHandler(ctx, tenant, Mock.Of<IPublisher>()).Handle(
            new CreateRepairCommand(AutreVehiculeId, null, "Forgée", Aujourdhui, null, 50m, null, null, new List<CreateRepairPartRequest>()),
            CancellationToken.None);
        await creation.Should().ThrowAsync<NotFoundException>().WithMessage("Véhicule introuvable.");

        (await new UpdateRepairStatusCommandHandler(ctx, tenant).Handle(
            new UpdateRepairStatusCommand(1, "cancelled"), CancellationToken.None)).Should().BeFalse();
        (await new DeleteRepairCommandHandler(ctx, tenant).Handle(
            new DeleteRepairCommand(1), CancellationToken.None)).Should().BeFalse();

        ctx.ChangeTracker.Clear();
        var reparation = await ctx.Repairs.AsNoTracking().SingleAsync();
        reparation.Status.Should().Be("pending");
        reparation.VehicleId.Should().Be(AutreVehiculeId);
    }
}
