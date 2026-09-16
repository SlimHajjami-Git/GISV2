using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Services;
using GisAPI.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GisAPI.Tests.Services;

/// <summary>
/// Recette GPA, DEF-016 : l'écran d'entretien conseille de DÉSACTIVER un gabarit plutôt que de
/// le supprimer. Un échéancier en pause ou d'un gabarit désactivé n'est plus recalculé, son
/// statut reste figé (« overdue » le plus souvent) : le score de santé, qui alimente les
/// véhicules à surveiller du tableau de bord, le comptait encore comme un entretien en retard.
/// </summary>
public class VehicleHealthScoreFrozenSchedulesTests
{
    private const int CompanyId = 7;
    private const int VehiculeFige = 1;
    private const int VehiculeEnRetard = 2;

    private static (TestGisDbContext Context, VehicleHealthScoreService Service) Creer()
    {
        var context = TestDbContextFactory.Create();
        context.Vehicles.AddRange(
            new Vehicle { Id = VehiculeFige, Name = "QA figé", CompanyId = CompanyId, Mileage = 60_000 },
            new Vehicle { Id = VehiculeEnRetard, Name = "QA en retard", CompanyId = CompanyId, Mileage = 60_000 });
        context.MaintenanceTemplates.AddRange(
            new MaintenanceTemplate { Id = 1, Name = "Actif", Category = "M", Priority = "medium", IntervalKm = 10_000, CompanyId = CompanyId },
            new MaintenanceTemplate { Id = 2, Name = "Désactivé", Category = "M", Priority = "medium", IntervalKm = 10_000, IsActive = false, CompanyId = CompanyId });
        context.VehicleMaintenanceSchedules.AddRange(
            // Véhicule 1 : uniquement des statuts figés.
            new VehicleMaintenanceSchedule { Id = 1, VehicleId = VehiculeFige, TemplateId = 1, NextDueKm = 59_000, Status = "overdue", IsPaused = true, CompanyId = CompanyId },
            new VehicleMaintenanceSchedule { Id = 2, VehicleId = VehiculeFige, TemplateId = 2, NextDueKm = 59_500, Status = "critical", CompanyId = CompanyId },
            // Véhicule 2 : un vrai retard, toujours compté.
            new VehicleMaintenanceSchedule { Id = 3, VehicleId = VehiculeEnRetard, TemplateId = 1, NextDueKm = 59_000, Status = "overdue", CompanyId = CompanyId });
        context.SaveChanges();

        var provider = new ServiceCollection()
            .AddSingleton<IGisDbContext>(context)
            .BuildServiceProvider();
        return (context, new VehicleHealthScoreService(provider));
    }

    [Fact]
    public async Task ScoreDeLaFlotte_IgnoreLesEcheanciersEnPauseEtLesGabaritsDesactives()
    {
        var (context, service) = Creer();
        using var _ = context;

        var scores = await service.CalculateAllScoresAsync(CompanyId);

        var fige = scores.Single(s => s.VehicleId == VehiculeFige);
        fige.Factors.Single(f => f.Name == "Entretiens").Score.Should().Be(25);
        fige.Warnings.Should().NotContain(w => w.Contains("entretien"));

        var enRetard = scores.Single(s => s.VehicleId == VehiculeEnRetard);
        enRetard.Factors.Single(f => f.Name == "Entretiens").Score.Should().Be(17);
        enRetard.Warnings.Should().Contain("1 entretien(s) en retard");
    }

    [Fact]
    public async Task ScoreDUnVehicule_IgnoreLesEcheanciersEnPauseEtLesGabaritsDesactives()
    {
        var (context, service) = Creer();
        using var _ = context;

        var fige = await service.CalculateScoreAsync(VehiculeFige, CompanyId);
        fige.Factors.Single(f => f.Name == "Entretiens").Score.Should().Be(25);
        fige.Warnings.Should().NotContain(w => w.Contains("entretien"));

        var enRetard = await service.CalculateScoreAsync(VehiculeEnRetard, CompanyId);
        enRetard.Warnings.Should().Contain("1 entretien(s) en retard");
    }
}
