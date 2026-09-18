using FluentAssertions;
using GisAPI.Application.Features.MaintenanceTemplates.Commands;
using GisAPI.Application.Features.VehicleMaintenance.Commands;
using GisAPI.Application.Features.VehicleMaintenance.Queries;
using GisAPI.Application.Services;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Application.MaintenanceTemplates;

/// <summary>
/// Campagne de recette Calypso GPA — fiabilité de l'entretien programmable
/// (DEF-013 à DEF-017).
/// </summary>
public class EntretienProgrammableRecetteTests
{
    // ────────────────────────────────────────────────────────────────────
    //  DEF-013 — un échéancier « critical » reste dans les alertes
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Alerts_KeepCriticalSchedule_AndAgreeWithStats()
    {
        // Constat : /alerts ne gardait que overdue et due. Un échéancier passé
        // « critical » (plus urgent que « due ») disparaissait de la liste,
        // pendant que /stats le comptait toujours dans dueCount.
        using var context = TestDbContextFactory.Create();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "QA", CompanyId = 1, Mileage = 52_600 });
        context.MaintenanceTemplates.AddRange(
            new MaintenanceTemplate { Id = 1, Name = "A", Category = "M", Priority = "medium", IntervalKm = 800, WarningKm = 1000, CriticalKm = 300, CompanyId = 1 },
            new MaintenanceTemplate { Id = 2, Name = "B", Category = "M", Priority = "medium", IntervalKm = 900, WarningKm = 1000, CompanyId = 1 },
            new MaintenanceTemplate { Id = 3, Name = "C", Category = "M", Priority = "medium", IntervalKm = 500, CompanyId = 1 },
            new MaintenanceTemplate { Id = 4, Name = "D", Category = "M", Priority = "medium", IntervalKm = 5000, CompanyId = 1 });
        context.VehicleMaintenanceSchedules.AddRange(
            new VehicleMaintenanceSchedule { Id = 1, VehicleId = 1, TemplateId = 1, NextDueKm = 52_800, Status = "critical", CompanyId = 1 },
            new VehicleMaintenanceSchedule { Id = 2, VehicleId = 1, TemplateId = 2, NextDueKm = 52_900, Status = "due", CompanyId = 1 },
            new VehicleMaintenanceSchedule { Id = 3, VehicleId = 1, TemplateId = 3, NextDueKm = 52_500, Status = "overdue", CompanyId = 1 },
            new VehicleMaintenanceSchedule { Id = 4, VehicleId = 1, TemplateId = 4, NextDueKm = 57_000, Status = "upcoming", CompanyId = 1 });
        await context.SaveChangesAsync();

        var alerts = await new GetMaintenanceAlertsQueryHandler(context, tenant.Object)
            .Handle(new GetMaintenanceAlertsQuery(), CancellationToken.None);
        var stats = await new GetMaintenanceStatsQueryHandler(context, tenant.Object)
            .Handle(new GetMaintenanceStatsQuery(), CancellationToken.None);

        // Ordre d'urgence : en retard, critique, imminent.
        alerts.Select(a => a.Status).Should().Equal("overdue", "critical", "due");
        alerts.Single(a => a.Status == "critical").KmUntilDue.Should().Be(200);

        // Les deux vues parlent du même ensemble d'échéanciers.
        stats.OverdueCount.Should().Be(alerts.Count(a => a.Status == "overdue"));
        stats.DueCount.Should().Be(alerts.Count(a => a.Status is "due" or "critical"));
    }

    [Fact]
    public async Task AlertsAndStats_IgnorePausedSchedules_AndDeactivatedTemplates()
    {
        // Un modèle désactivé (voie proposée quand la suppression est refusée) et
        // un échéancier en pause ne sont plus recalculés : leur statut est figé,
        // ils ne doivent plus alimenter ni les alertes ni les compteurs.
        using var context = TestDbContextFactory.Create();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "QA", CompanyId = 1, Mileage = 60_000 });
        context.MaintenanceTemplates.AddRange(
            new MaintenanceTemplate { Id = 1, Name = "Actif", Category = "M", Priority = "medium", IntervalKm = 10_000, CompanyId = 1 },
            new MaintenanceTemplate { Id = 2, Name = "Désactivé", Category = "M", Priority = "medium", IntervalKm = 10_000, IsActive = false, CompanyId = 1 });
        context.VehicleMaintenanceSchedules.AddRange(
            new VehicleMaintenanceSchedule { Id = 1, VehicleId = 1, TemplateId = 1, NextDueKm = 59_000, Status = "overdue", CompanyId = 1 },
            new VehicleMaintenanceSchedule { Id = 2, VehicleId = 1, TemplateId = 1, NextDueKm = 60_500, Status = "due", IsPaused = true, CompanyId = 1 },
            new VehicleMaintenanceSchedule { Id = 3, VehicleId = 1, TemplateId = 2, NextDueKm = 60_200, Status = "critical", CompanyId = 1 });
        await context.SaveChangesAsync();

        var alerts = await new GetMaintenanceAlertsQueryHandler(context, tenant.Object)
            .Handle(new GetMaintenanceAlertsQuery(), CancellationToken.None);
        var stats = await new GetMaintenanceStatsQueryHandler(context, tenant.Object)
            .Handle(new GetMaintenanceStatsQuery(), CancellationToken.None);

        alerts.Select(a => a.ScheduleId).Should().Equal(1);
        stats.TotalSchedules.Should().Be(1);
        stats.OverdueCount.Should().Be(1);
        stats.DueCount.Should().Be(0);
    }

    // ────────────────────────────────────────────────────────────────────
    //  DEF-014 — « Marquer fait » respecte l'intervalle personnalisé
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MarkDone_UsesScheduleCustomInterval_LikeUpdateIntervals()
    {
        // Reproduction de la fiche : modèle 10 000 km / 6 mois, intervalle
        // personnalisé 5 000 km / 3 mois. « Marquer fait » recalculait avec le
        // modèle (63 200 km, 14/03/2027) alors que le réglage restait en base.
        using var context = TestDbContextFactory.Create();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        var doneDate = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "QA", CompanyId = 1, Mileage = 53_200 });
        context.MaintenanceTemplates.Add(new MaintenanceTemplate
        {
            Id = 1, Name = "Vidange", Category = "Moteur", Priority = "medium",
            IntervalKm = 10_000, IntervalMonths = 6, CompanyId = 1
        });
        context.VehicleMaintenanceSchedules.Add(new VehicleMaintenanceSchedule
        {
            Id = 1, VehicleId = 1, TemplateId = 1, CompanyId = 1,
            LastDoneKm = 53_200, LastDoneDate = doneDate,
            NextDueKm = 63_200, NextDueDate = doneDate.AddMonths(6), Status = "ok"
        });
        await context.SaveChangesAsync();

        await new UpdateScheduleIntervalsCommandHandler(context, TestDbContextFactory.CreateMockTenantService().Object)
            .Handle(new UpdateScheduleIntervalsCommand(1, 5_000, 3, null), CancellationToken.None);
        var afterIntervals = context.VehicleMaintenanceSchedules.Single();
        afterIntervals.NextDueKm.Should().Be(58_200);
        afterIntervals.NextDueDate.Should().Be(new DateTime(2026, 12, 14));

        await new MarkMaintenanceDoneCommandHandler(context, tenant.Object).Handle(
            new MarkMaintenanceDoneCommand(VehicleId: 1, TemplateId: 1, Date: doneDate, Mileage: 53_200,
                Cost: 10m, SupplierId: null, Notes: null),
            CancellationToken.None);

        var schedule = context.VehicleMaintenanceSchedules.Single();
        schedule.NextDueKm.Should().Be(58_200);
        schedule.NextDueDate.Should().Be(new DateTime(2026, 12, 14));
        schedule.CustomIntervalKm.Should().Be(5_000);
        schedule.CustomIntervalMonths.Should().Be(3);
    }

    [Fact]
    public async Task MarkDone_PartialCustomInterval_FallsBackToTemplateForTheOtherAxis()
    {
        // Seul le kilométrage est personnalisé : la périodicité en mois reste
        // celle du modèle (même règle que le recalcul des intervalles).
        using var context = TestDbContextFactory.Create();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        var doneDate = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "QA", CompanyId = 1, Mileage = 40_000 });
        context.MaintenanceTemplates.Add(new MaintenanceTemplate
        {
            Id = 1, Name = "Vidange", Category = "Moteur", Priority = "medium",
            IntervalKm = 10_000, IntervalMonths = 6, CompanyId = 1
        });
        context.VehicleMaintenanceSchedules.Add(new VehicleMaintenanceSchedule
        {
            Id = 1, VehicleId = 1, TemplateId = 1, CompanyId = 1,
            CustomIntervalKm = 7_500, NextDueKm = 47_500, Status = "ok"
        });
        await context.SaveChangesAsync();

        await new MarkMaintenanceDoneCommandHandler(context, tenant.Object).Handle(
            new MarkMaintenanceDoneCommand(VehicleId: 1, TemplateId: 1, Date: doneDate, Mileage: 41_000,
                Cost: 90m, SupplierId: null, Notes: null),
            CancellationToken.None);

        var schedule = context.VehicleMaintenanceSchedules.Single();
        schedule.NextDueKm.Should().Be(48_500);                          // 41 000 + 7 500 (personnalisé)
        schedule.NextDueDate.Should().Be(new DateTime(2027, 3, 14));    // + 6 mois (modèle)
    }

    // ────────────────────────────────────────────────────────────────────
    //  DEF-015 — l'historique renvoie toutes les interventions
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MaintenanceLogs_ReturnEveryIntervention_IdenticalOnesAndBeyondFifty()
    {
        // Deux interventions de même date, même kilométrage et même montant sont
        // deux journaux distincts ; et l'historique n'est plus tronqué à 50 lignes.
        // Le total de la modale (écran et PDF) se calcule sur cette liste.
        using var context = TestDbContextFactory.Create();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "QA", CompanyId = 1, Mileage = 90_000 });
        context.MaintenanceTemplates.Add(new MaintenanceTemplate
        {
            Id = 1, Name = "Vidange", Category = "Moteur", Priority = "medium", IntervalKm = 1_000, CompanyId = 1
        });
        var start = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 58; i++)
        {
            context.MaintenanceLogs.Add(new MaintenanceLog
            {
                VehicleId = 1, TemplateId = 1, CompanyId = 1,
                DoneDate = start.AddDays(i * 10), DoneKm = 30_000 + i * 1_000, ActualCost = 10m
            });
        }
        var sameDay = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);
        context.MaintenanceLogs.AddRange(
            new MaintenanceLog { VehicleId = 1, TemplateId = 1, CompanyId = 1, DoneDate = sameDay, DoneKm = 89_000, ActualCost = 40m },
            new MaintenanceLog { VehicleId = 1, TemplateId = 1, CompanyId = 1, DoneDate = sameDay, DoneKm = 89_000, ActualCost = 40m });
        await context.SaveChangesAsync();

        var logs = await new GetMaintenanceLogsQueryHandler(context, tenant.Object)
            .Handle(new GetMaintenanceLogsQuery(VehicleId: 1, TemplateId: 1), CancellationToken.None);

        logs.Should().HaveCount(60);
        logs.Sum(l => l.ActualCost).Should().Be(58 * 10m + 2 * 40m);
        logs.Take(2).Should().OnlyContain(l => l.DoneDate == sameDay && l.DoneKm == 89_000);
        logs.Select(l => l.Id).Should().OnlyHaveUniqueItems();
    }

    // ────────────────────────────────────────────────────────────────────
    //  DEF-016 — supprimer un modèle ne détruit jamais l'historique
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteTemplate_WithDoneHistory_IsRefusedAndKeepsEverything()
    {
        // Les FK vers maintenance_templates sont en ON DELETE CASCADE : la
        // suppression effaçait échéanciers ET journaux réalisés, alors que les
        // dépenses restaient. Elle est désormais refusée (409) avec la voie à suivre.
        using var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "QA", CompanyId = 1, Mileage = 50_000 });
        context.MaintenanceTemplates.Add(new MaintenanceTemplate
        {
            Id = 1, Name = "QA gabarit", Category = "Moteur", Priority = "medium", IntervalKm = 10_000, CompanyId = 1
        });
        context.VehicleMaintenanceSchedules.Add(new VehicleMaintenanceSchedule { Id = 1, VehicleId = 1, TemplateId = 1, CompanyId = 1 });
        context.VehicleCosts.Add(new VehicleCost { Id = 1, VehicleId = 1, Type = "maintenance", Description = "Entretien: QA gabarit", Amount = 30m, Date = DateTime.UtcNow.Date, CompanyId = 1 });
        context.MaintenanceLogs.Add(new MaintenanceLog
        {
            Id = 1, VehicleId = 1, TemplateId = 1, ScheduleId = 1, CostId = 1, CompanyId = 1,
            DoneDate = DateTime.UtcNow.Date, DoneKm = 50_000, ActualCost = 30m
        });
        await context.SaveChangesAsync();

        var act = () => new DeleteMaintenanceTemplateCommandHandler(context)
            .Handle(new DeleteMaintenanceTemplateCommand(1), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*« QA gabarit » ne peut pas être supprimé : 1 entretien réalisé*Désactivez-le plutôt*");

        context.ChangeTracker.Clear();
        context.MaintenanceTemplates.Should().ContainSingle(t => t.Id == 1);
        context.VehicleMaintenanceSchedules.Should().ContainSingle(s => s.Id == 1);
        context.MaintenanceLogs.Should().ContainSingle(l => l.Id == 1);
        context.VehicleCosts.Should().ContainSingle(c => c.Id == 1);
    }

    [Fact]
    public async Task DeleteTemplate_AlreadyDeactivatedWithHistory_SaysItIsAlreadyDeactivated()
    {
        using var context = TestDbContextFactory.Create();
        context.MaintenanceTemplates.Add(new MaintenanceTemplate
        {
            Id = 1, Name = "Ancien", Category = "Moteur", Priority = "medium", IntervalKm = 10_000,
            IsActive = false, CompanyId = 1
        });
        context.MaintenanceLogs.AddRange(
            new MaintenanceLog { VehicleId = 1, TemplateId = 1, CompanyId = 1, DoneDate = DateTime.UtcNow.Date, DoneKm = 1, ActualCost = 1m },
            new MaintenanceLog { VehicleId = 2, TemplateId = 1, CompanyId = 1, DoneDate = DateTime.UtcNow.Date, DoneKm = 1, ActualCost = 1m });
        await context.SaveChangesAsync();

        var act = () => new DeleteMaintenanceTemplateCommandHandler(context)
            .Handle(new DeleteMaintenanceTemplateCommand(1), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*2 entretiens réalisés y sont rattachés*déjà désactivé*");
        context.MaintenanceTemplates.Should().ContainSingle();
    }

    [Fact]
    public async Task DeleteTemplate_WithoutHistory_StillDeletes()
    {
        // Un modèle qui n'a jamais servi (seulement des échéances à venir) se
        // supprime toujours : il n'y a aucun historique à perdre.
        using var context = TestDbContextFactory.Create();
        context.MaintenanceTemplates.AddRange(
            new MaintenanceTemplate { Id = 1, Name = "Jamais servi", Category = "Moteur", Priority = "medium", IntervalKm = 10_000, CompanyId = 1 },
            new MaintenanceTemplate { Id = 2, Name = "Autre", Category = "Moteur", Priority = "medium", IntervalKm = 10_000, CompanyId = 1 });
        context.VehicleMaintenanceSchedules.Add(new VehicleMaintenanceSchedule { Id = 1, VehicleId = 1, TemplateId = 1, CompanyId = 1 });
        // Un journal d'un AUTRE modèle ne bloque pas la suppression.
        context.MaintenanceLogs.Add(new MaintenanceLog { VehicleId = 1, TemplateId = 2, CompanyId = 1, DoneDate = DateTime.UtcNow.Date, DoneKm = 1, ActualCost = 1m });
        await context.SaveChangesAsync();

        var result = await new DeleteMaintenanceTemplateCommandHandler(context)
            .Handle(new DeleteMaintenanceTemplateCommand(1), CancellationToken.None);

        result.Should().BeTrue();
        context.MaintenanceTemplates.Select(t => t.Id).Should().Equal(2);
    }

    // ────────────────────────────────────────────────────────────────────
    //  DEF-017 — une affectation impossible donne un refus lisible
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Assign_WithDeletedTemplate_IsRefusedWithReadableMessage()
    {
        // Le modèle supprimé pendant que le panneau « Affecter » était ouvert
        // produisait un 500 « An unexpected error occurred » : l'écran ne pouvait
        // rien expliquer. Refus métier (400) avec un message en français.
        using var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "QA", CompanyId = 1, Mileage = 10_000 });
        await context.SaveChangesAsync();

        var scheduler = new MaintenanceSchedulerService(context, NullLogger<MaintenanceSchedulerService>.Instance);
        var act = () => new AssignMaintenanceTemplateCommandHandler(context, scheduler, TestDbContextFactory.CreateMockTenantService().Object)
            .Handle(new AssignMaintenanceTemplateCommand(VehicleId: 1, TemplateId: 404), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Ce modèle d'entretien n'existe plus*");
        context.VehicleMaintenanceSchedules.Should().BeEmpty();
    }
}
