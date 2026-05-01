using FluentAssertions;
using GisAPI.Application.Features.VehicleMaintenance.Commands;
using GisAPI.Application.Features.VehicleMaintenance.Queries;
using GisAPI.Application.Services;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Application.MaintenanceTemplates;

/// <summary>
/// Couvre toutes les voies de propagation du kilométrage du véhicule
/// dans le sous-système d'entretien programmable, après la simplification
/// Calypso 7 où <c>vehicles.mileage</c> est devenu l'unique source de
/// vérité (le Rust ingest s'occupe de la maintenir, indépendamment du
/// type de tracker).
///
/// <para>Cas couverts :</para>
/// <list type="bullet">
///   <item><c>GetCurrentMileageAsync</c> retourne 0 quand le véhicule
///     n'existe pas / mileage à 0 / mileage non-nul.</item>
///   <item><c>AssignMaintenanceTemplate</c> snape <c>NextDueKm</c> sur
///     le mileage courant (incluant le cas mileage = 0 silent-tracker).</item>
///   <item><c>MarkMaintenanceDone</c> persiste l'historique, met à jour
///     le schedule, gère le crédit gratuit (counter, expiration, multi
///     templates).</item>
///   <item><c>RebaseMaintenanceSchedule</c> re-snape <c>NextDueKm</c> sur
///     le mileage courant et est idempotent.</item>
/// </list>
/// </summary>
public class MaintenanceMileageResolverTests
{
    // ────────────────────────────────────────────────────────────────────
    //  GetCurrentMileageAsync — résolveur du kilométrage courant
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCurrentMileage_VehicleNotFound_Returns0()
    {
        using var context = TestDbContextFactory.Create();
        var scheduler = new MaintenanceSchedulerService(
            context, NullLogger<MaintenanceSchedulerService>.Instance);

        var result = await scheduler.GetCurrentMileageAsync(99999);

        result.Should().Be(0);
    }

    [Fact]
    public async Task GetCurrentMileage_VehicleWithZeroMileage_Returns0()
    {
        using var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "X", CompanyId = 1, Mileage = 0 });
        await context.SaveChangesAsync();

        var scheduler = new MaintenanceSchedulerService(
            context, NullLogger<MaintenanceSchedulerService>.Instance);

        var result = await scheduler.GetCurrentMileageAsync(1);

        result.Should().Be(0);
    }

    [Fact]
    public async Task GetCurrentMileage_VehicleWithMileage_ReturnsThatMileage()
    {
        // Calypso 7 : vehicles.mileage est l'unique source. Peu importe le
        // type de tracker — c'est le Rust ingest qui le tient à jour
        // (mirror CAN bus pour NEMS L sain, Haversine pour les autres y
        // compris NEMS L à CAN bus muet). Le resolver .NET ne fait pas de
        // cascade.
        using var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "X", CompanyId = 1, Mileage = 73_421 });
        await context.SaveChangesAsync();

        var scheduler = new MaintenanceSchedulerService(
            context, NullLogger<MaintenanceSchedulerService>.Instance);

        var result = await scheduler.GetCurrentMileageAsync(1);

        result.Should().Be(73_421);
    }

    // ────────────────────────────────────────────────────────────────────
    //  AssignMaintenanceTemplate — création d'un schedule
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Assign_OnVehicleWithMileage_SnapsNextDueKmToCurrent()
    {
        using var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "X", CompanyId = 1, Mileage = 50_000 });
        context.MaintenanceTemplates.Add(new MaintenanceTemplate
        {
            Id = 1, Name = "Vidange", Category = "Moteur", Priority = "medium",
            IntervalKm = 10_000, IntervalMonths = 6, CompanyId = 1
        });
        await context.SaveChangesAsync();

        var scheduler = new MaintenanceSchedulerService(
            context, NullLogger<MaintenanceSchedulerService>.Instance);
        var handler = new AssignMaintenanceTemplateCommandHandler(context, scheduler);

        var scheduleId = await handler.Handle(
            new AssignMaintenanceTemplateCommand(1, 1), CancellationToken.None);

        var schedule = await context.VehicleMaintenanceSchedules.FindAsync(scheduleId);
        schedule.Should().NotBeNull();
        schedule!.NextDueKm.Should().Be(60_000); // 50 000 + 10 000
        schedule.Status.Should().Be("upcoming");
    }

    [Fact]
    public async Task Assign_OnVehicleWithZeroMileage_StillAnchorsOnInterval()
    {
        // Cas d'un véhicule fraichement créé sans odomètre + sans manual.
        // Le snapshot prend max(currentMileage = 0, 0) → NextDueKm = intervalKm.
        // Ce qui est correct : on attend de voir 10 000 km parcourus avant
        // la première vidange, peu importe d'où on part.
        using var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "Neuf", CompanyId = 1, Mileage = 0 });
        context.MaintenanceTemplates.Add(new MaintenanceTemplate
        {
            Id = 1, Name = "Vidange", Category = "Moteur", Priority = "medium",
            IntervalKm = 10_000, CompanyId = 1
        });
        await context.SaveChangesAsync();

        var scheduler = new MaintenanceSchedulerService(
            context, NullLogger<MaintenanceSchedulerService>.Instance);
        var handler = new AssignMaintenanceTemplateCommandHandler(context, scheduler);

        var scheduleId = await handler.Handle(
            new AssignMaintenanceTemplateCommand(1, 1), CancellationToken.None);

        var schedule = await context.VehicleMaintenanceSchedules.FindAsync(scheduleId);
        schedule!.NextDueKm.Should().Be(10_000);
    }

    [Fact]
    public async Task Assign_TemplateAlreadyAssigned_ReturnsExistingId_NoDuplicate()
    {
        using var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "X", CompanyId = 1, Mileage = 50_000 });
        context.MaintenanceTemplates.Add(new MaintenanceTemplate
        {
            Id = 1, Name = "V", Category = "M", Priority = "medium",
            IntervalKm = 10_000, CompanyId = 1
        });
        context.VehicleMaintenanceSchedules.Add(new VehicleMaintenanceSchedule
        {
            Id = 99, VehicleId = 1, TemplateId = 1, NextDueKm = 60_000, Status = "upcoming"
        });
        await context.SaveChangesAsync();

        var scheduler = new MaintenanceSchedulerService(
            context, NullLogger<MaintenanceSchedulerService>.Instance);
        var handler = new AssignMaintenanceTemplateCommandHandler(context, scheduler);

        var result = await handler.Handle(
            new AssignMaintenanceTemplateCommand(1, 1), CancellationToken.None);

        result.Should().Be(99);
        context.VehicleMaintenanceSchedules.Count(s => s.VehicleId == 1 && s.TemplateId == 1)
            .Should().Be(1);
    }

    // ────────────────────────────────────────────────────────────────────
    //  MarkMaintenanceDone — exécution + traçabilité
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MarkDone_CreatesLogAndCost_AndAdvancesSchedule()
    {
        using var context = TestDbContextFactory.Create();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "X", CompanyId = 1, Mileage = 50_000 });
        context.MaintenanceTemplates.Add(new MaintenanceTemplate
        {
            Id = 1, Name = "Vidange", Category = "Moteur", Priority = "medium",
            IntervalKm = 10_000, IntervalMonths = 6, CompanyId = 1
        });
        await context.SaveChangesAsync();

        var handler = new MarkMaintenanceDoneCommandHandler(context, tenant.Object);
        var logId = await handler.Handle(new MarkMaintenanceDoneCommand(
            VehicleId: 1, TemplateId: 1,
            Date: DateTime.UtcNow.Date, Mileage: 55_000,
            Cost: 200m, SupplierId: null, Notes: "Huile + filtre"
        ), CancellationToken.None);

        logId.Should().BeGreaterThan(0);
        var log = await context.MaintenanceLogs.FindAsync(logId);
        log!.DoneKm.Should().Be(55_000);
        log.ActualCost.Should().Be(200m);
        log.WasFree.Should().BeFalse();

        var schedule = context.VehicleMaintenanceSchedules.Single(s => s.VehicleId == 1);
        schedule.LastDoneKm.Should().Be(55_000);
        schedule.NextDueKm.Should().Be(65_000); // 55000 + 10000
        schedule.Status.Should().Be("ok");

        // Vehicle.Mileage bumped because 55000 > 50000
        var vehicle = await context.Vehicles.FindAsync(1);
        vehicle!.Mileage.Should().Be(55_000);

        var costs = context.VehicleCosts.Where(c => c.VehicleId == 1).ToList();
        costs.Should().HaveCount(1);
        costs[0].Type.Should().Be("maintenance");
        costs[0].Amount.Should().Be(200m);
        costs[0].Mileage.Should().Be(55_000);
    }

    [Fact]
    public async Task MarkDone_WithMileageLowerThanVehicle_DoesNotDecreaseVehicleMileage()
    {
        // Garde-fou : si le user saisit un km plus petit que vehicle.Mileage
        // (par erreur de frappe), on ne doit jamais reculer le compteur.
        using var context = TestDbContextFactory.Create();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "X", CompanyId = 1, Mileage = 80_000 });
        context.MaintenanceTemplates.Add(new MaintenanceTemplate
        {
            Id = 1, Name = "V", Category = "M", Priority = "medium",
            IntervalKm = 10_000, CompanyId = 1
        });
        await context.SaveChangesAsync();

        var handler = new MarkMaintenanceDoneCommandHandler(context, tenant.Object);
        await handler.Handle(new MarkMaintenanceDoneCommand(
            VehicleId: 1, TemplateId: 1,
            Date: DateTime.UtcNow.Date, Mileage: 50_000, // < vehicle.Mileage = 80000
            Cost: 100m, SupplierId: null, Notes: null
        ), CancellationToken.None);

        var vehicle = await context.Vehicles.FindAsync(1);
        vehicle!.Mileage.Should().Be(80_000); // pas reculé
    }

    [Fact]
    public async Task MarkDone_WithApplyFreeBenefit_ZerosCostAndDecrementsCounter()
    {
        // Cas vidange offerte sous garantie : applyFreeBenefit=true,
        // freeUsesRemaining > 0, freeExpiryDate dans le futur. Coût = 0,
        // counter passe à N-1.
        using var context = TestDbContextFactory.Create();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "X", CompanyId = 1, Mileage = 50_000 });
        context.MaintenanceTemplates.Add(new MaintenanceTemplate
        {
            Id = 1, Name = "V", Category = "M", Priority = "medium",
            IntervalKm = 10_000, CompanyId = 1
        });
        context.VehicleMaintenanceSchedules.Add(new VehicleMaintenanceSchedule
        {
            Id = 1, VehicleId = 1, TemplateId = 1,
            NextDueKm = 60_000, Status = "due",
            FreeUsesRemaining = 3, FreeUsesTotal = 3,
            FreeSource = "Achat neuf",
            FreeExpiryDate = DateTime.UtcNow.AddYears(1).Date
        });
        await context.SaveChangesAsync();

        var handler = new MarkMaintenanceDoneCommandHandler(context, tenant.Object);
        var logId = await handler.Handle(new MarkMaintenanceDoneCommand(
            VehicleId: 1, TemplateId: 1,
            Date: DateTime.UtcNow.Date, Mileage: 60_000,
            Cost: 250m, SupplierId: null, Notes: null,
            ApplyFreeBenefit: true
        ), CancellationToken.None);

        var log = await context.MaintenanceLogs.FindAsync(logId);
        log!.WasFree.Should().BeTrue();
        log.ActualCost.Should().Be(0m); // forcé à 0 même si Cost=250 reçu
        log.Notes.Should().Contain("Gratuit");

        var schedule = context.VehicleMaintenanceSchedules.Single();
        schedule.FreeUsesRemaining.Should().Be(2); // décrémenté
        schedule.FreeUsesTotal.Should().Be(3);     // total inchangé

        // Le VehicleCost est créé même à 0 pour la traçabilité
        var costs = context.VehicleCosts.Where(c => c.VehicleId == 1).ToList();
        costs.Should().HaveCount(1);
        costs[0].Amount.Should().Be(0m);
    }

    [Fact]
    public async Task MarkDone_FreeBenefitExpired_NotApplied_NormalCost()
    {
        // FreeExpiryDate est passée → le bénéfice ne s'applique pas
        // même si l'utilisateur l'a coché. Coût normal, counter intact.
        using var context = TestDbContextFactory.Create();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "X", CompanyId = 1, Mileage = 50_000 });
        context.MaintenanceTemplates.Add(new MaintenanceTemplate
        {
            Id = 1, Name = "V", Category = "M", Priority = "medium",
            IntervalKm = 10_000, CompanyId = 1
        });
        context.VehicleMaintenanceSchedules.Add(new VehicleMaintenanceSchedule
        {
            Id = 1, VehicleId = 1, TemplateId = 1,
            NextDueKm = 60_000, Status = "due",
            FreeUsesRemaining = 3, FreeUsesTotal = 3,
            FreeExpiryDate = DateTime.UtcNow.AddDays(-10).Date // EXPIRÉ
        });
        await context.SaveChangesAsync();

        var handler = new MarkMaintenanceDoneCommandHandler(context, tenant.Object);
        var logId = await handler.Handle(new MarkMaintenanceDoneCommand(
            VehicleId: 1, TemplateId: 1,
            Date: DateTime.UtcNow.Date, Mileage: 60_000,
            Cost: 250m, SupplierId: null, Notes: null,
            ApplyFreeBenefit: true
        ), CancellationToken.None);

        var log = await context.MaintenanceLogs.FindAsync(logId);
        log!.WasFree.Should().BeFalse();
        log.ActualCost.Should().Be(250m);

        var schedule = context.VehicleMaintenanceSchedules.Single();
        schedule.FreeUsesRemaining.Should().Be(3); // intact
    }

    [Fact]
    public async Task MarkDone_OnVehicleWithMileageZero_AcceptsZeroMileage()
    {
        // Cas véhicule sans odomètre + jamais entré manuellement : le user
        // doit pouvoir saisir 0 si c'est la réalité. Vérifie qu'aucune
        // garde "mileage > 0" ne bloque l'opération.
        using var context = TestDbContextFactory.Create();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "X", CompanyId = 1, Mileage = 0 });
        context.MaintenanceTemplates.Add(new MaintenanceTemplate
        {
            Id = 1, Name = "V", Category = "M", Priority = "medium",
            IntervalKm = 10_000, CompanyId = 1
        });
        await context.SaveChangesAsync();

        var handler = new MarkMaintenanceDoneCommandHandler(context, tenant.Object);
        var logId = await handler.Handle(new MarkMaintenanceDoneCommand(
            VehicleId: 1, TemplateId: 1,
            Date: DateTime.UtcNow.Date, Mileage: 0,
            Cost: 0m, SupplierId: null, Notes: null
        ), CancellationToken.None);

        logId.Should().BeGreaterThan(0);
        var log = await context.MaintenanceLogs.FindAsync(logId);
        log!.DoneKm.Should().Be(0);
        var schedule = context.VehicleMaintenanceSchedules.Single();
        schedule.NextDueKm.Should().Be(10_000);
    }

    // ────────────────────────────────────────────────────────────────────
    //  RebaseMaintenanceSchedule — re-snape NextDueKm sur le mileage courant
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rebase_FreshSchedule_RealignsOnCurrentMileage()
    {
        // Schedule créé avec un snapshot stale (NextDueKm = 10000 alors
        // que vehicle.Mileage = 5974). Le rebase doit le re-snapper sur
        // le mileage courant : NextDueKm devient mileage + intervalKm.
        using var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "X", CompanyId = 1, Mileage = 5_974 });
        context.MaintenanceTemplates.Add(new MaintenanceTemplate
        {
            Id = 1, Name = "Vidange", Category = "Moteur", Priority = "medium",
            IntervalKm = 10_000, CompanyId = 1
        });
        context.VehicleMaintenanceSchedules.Add(new VehicleMaintenanceSchedule
        {
            Id = 50, VehicleId = 1, TemplateId = 1,
            NextDueKm = 10_000, // snapshot foireux pré-fix
            Status = "upcoming", LastDoneKm = null,
            NotificationCount = 2, // simule des notifs envoyées
            LastNotificationAt = DateTime.UtcNow.AddHours(-12)
        });
        await context.SaveChangesAsync();

        var scheduler = new MaintenanceSchedulerService(
            context, NullLogger<MaintenanceSchedulerService>.Instance);
        var handler = new RebaseMaintenanceScheduleCommandHandler(context, scheduler);

        var ok = await handler.Handle(
            new RebaseMaintenanceScheduleCommand(50), CancellationToken.None);

        ok.Should().BeTrue();
        var schedule = await context.VehicleMaintenanceSchedules.FindAsync(50);
        schedule!.NextDueKm.Should().Be(15_974); // 5974 + 10000
        // Notification dedup réinitialisée pour pouvoir re-fire si due
        schedule.NotificationCount.Should().Be(0);
        schedule.LastNotificationAt.Should().BeNull();
    }

    [Fact]
    public async Task Rebase_AnchorsOnLastDoneKmWhenItIsHigherThanCurrent()
    {
        // Si le schedule a déjà un last_done_km > vehicle.Mileage (cas
        // anormal mais possible), le rebase doit prendre l'ancre
        // supérieure pour ne pas tirer NextDueKm vers le bas.
        using var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "X", CompanyId = 1, Mileage = 50_000 });
        context.MaintenanceTemplates.Add(new MaintenanceTemplate
        {
            Id = 1, Name = "V", Category = "M", Priority = "medium",
            IntervalKm = 10_000, CompanyId = 1
        });
        context.VehicleMaintenanceSchedules.Add(new VehicleMaintenanceSchedule
        {
            Id = 50, VehicleId = 1, TemplateId = 1,
            NextDueKm = 60_000, LastDoneKm = 70_000, // > vehicle.Mileage
            Status = "ok"
        });
        await context.SaveChangesAsync();

        var scheduler = new MaintenanceSchedulerService(
            context, NullLogger<MaintenanceSchedulerService>.Instance);
        var handler = new RebaseMaintenanceScheduleCommandHandler(context, scheduler);

        await handler.Handle(new RebaseMaintenanceScheduleCommand(50), CancellationToken.None);

        var schedule = await context.VehicleMaintenanceSchedules.FindAsync(50);
        // Math.Max(currentMileage=50000, lastDoneKm=70000) + interval(10000) = 80000
        schedule!.NextDueKm.Should().Be(80_000);
    }

    [Fact]
    public async Task Rebase_Idempotent_TwoCallsSameResult()
    {
        using var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "X", CompanyId = 1, Mileage = 5_974 });
        context.MaintenanceTemplates.Add(new MaintenanceTemplate
        {
            Id = 1, Name = "V", Category = "M", Priority = "medium",
            IntervalKm = 10_000, CompanyId = 1
        });
        context.VehicleMaintenanceSchedules.Add(new VehicleMaintenanceSchedule
        {
            Id = 50, VehicleId = 1, TemplateId = 1,
            NextDueKm = 10_000, Status = "upcoming", LastDoneKm = null
        });
        await context.SaveChangesAsync();

        var scheduler = new MaintenanceSchedulerService(
            context, NullLogger<MaintenanceSchedulerService>.Instance);
        var handler = new RebaseMaintenanceScheduleCommandHandler(context, scheduler);

        await handler.Handle(new RebaseMaintenanceScheduleCommand(50), CancellationToken.None);
        var afterFirst = (await context.VehicleMaintenanceSchedules.FindAsync(50))!.NextDueKm;

        await handler.Handle(new RebaseMaintenanceScheduleCommand(50), CancellationToken.None);
        var afterSecond = (await context.VehicleMaintenanceSchedules.FindAsync(50))!.NextDueKm;

        afterFirst.Should().Be(15_974);
        afterSecond.Should().Be(15_974); // identique
    }

    [Fact]
    public async Task Rebase_NonExistentSchedule_ReturnsFalse()
    {
        using var context = TestDbContextFactory.Create();
        var scheduler = new MaintenanceSchedulerService(
            context, NullLogger<MaintenanceSchedulerService>.Instance);
        var handler = new RebaseMaintenanceScheduleCommandHandler(context, scheduler);

        var ok = await handler.Handle(
            new RebaseMaintenanceScheduleCommand(99999), CancellationToken.None);

        ok.Should().BeFalse();
    }
}
