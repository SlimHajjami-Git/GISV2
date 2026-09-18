using System.Text.Json;
using FluentAssertions;
using GisAPI.Application.Features.MaintenanceTemplates.Commands;
using GisAPI.Application.Features.VehicleMaintenance.Commands;
using GisAPI.Application.Features.VehicleMaintenance.Queries;
using GisAPI.Application.Services;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Middleware;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.MaintenanceTemplates;

/// <summary>
/// Campagne de recette Calypso GPA — refus métier de l'entretien programmable.
///
///   • DEF-031 : gabarit sans intervalle, « marquer fait » et « déclarer des entretiens
///     gratuits » sur un gabarit ou un véhicule introuvable, bornes du crédit gratuit :
///     ArgumentException / InvalidOperationException rendues en 500 « An unexpected error
///     occurred » par ExceptionHandlingMiddleware, message français perdu.
///   • DEF-042 : un échéancier en pause était compté dans « Urgents » par l'écran Entretiens,
///     faute d'indication de pause dans GET /api/vehicle-maintenance.
/// </summary>
public class EntretienRefusMetierTests
{
    private const int CompanyId = 1;
    private const int OtherCompanyId = 2;
    private static readonly DateTime DoneDate = new(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Contexte au jeton d'un administrateur système d'une AUTRE société : SaveChanges
    /// remplit le CompanyId des nouvelles entités avec la société du jeton.
    /// </summary>
    private static TestGisDbContext ContexteAdministrateurSysteme(int companyId)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys = OFF;";
            cmd.ExecuteNonQuery();
        }

        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: companyId);
        tenant.Setup(t => t.IsSystemAdmin).Returns(true);
        var context = new TestGisDbContext(
            new DbContextOptionsBuilder<TestGisDbContext>().UseSqlite(connection).Options, tenant.Object);
        context.Database.EnsureCreated();
        return context;
    }

    private static async Task<TestGisDbContext> SeedAsync(TestGisDbContext? existing = null)
    {
        var context = existing ?? TestDbContextFactory.Create();
        context.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "QA", CompanyId = CompanyId, Mileage = 50_000 },
            new Vehicle { Id = 20, Name = "Étranger", CompanyId = OtherCompanyId, Mileage = 84_438 });
        context.MaintenanceTemplates.AddRange(
            new MaintenanceTemplate { Id = 12, Name = "Vidange", Category = "Moteur", Priority = "medium", IntervalKm = 10_000, CompanyId = CompanyId },
            new MaintenanceTemplate { Id = 30, Name = "Gabarit étranger", Category = "Moteur", Priority = "medium", IntervalKm = 10_000, CompanyId = OtherCompanyId });
        context.VehicleMaintenanceSchedules.Add(new VehicleMaintenanceSchedule
        {
            Id = 47, VehicleId = 1, TemplateId = 12, CompanyId = CompanyId, NextDueKm = 60_000, Status = "ok",
            FreeUsesTotal = 2, FreeUsesRemaining = 1
        });
        await context.SaveChangesAsync();
        return context;
    }

    private static MarkMaintenanceDoneCommand MarkDone(int vehicleId, int templateId) =>
        new(VehicleId: vehicleId, TemplateId: templateId, Date: DoneDate, Mileage: 90_000,
            Cost: 30m, SupplierId: null, Notes: null);

    private static DeclareFreeMaintenancesCommandHandler DeclareFreeHandler(TestGisDbContext context) =>
        new(context, new MaintenanceSchedulerService(context, NullLogger<MaintenanceSchedulerService>.Instance),
            TestDbContextFactory.CreateMockTenantService().Object);

    // ── Gabarit sans intervalle ────────────────────────────────────────────────

    [Fact]
    public async Task Un_gabarit_sans_intervalle_est_un_refus_metier_en_francais()
    {
        using var context = TestDbContextFactory.Create();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId);
        var handler = new CreateMaintenanceTemplateCommandHandler(context, tenant.Object, Mock.Of<IPublisher>());

        var act = () => handler.Handle(
            new CreateMaintenanceTemplateCommand("QA-Sans intervalle", null, "Autre", "medium", null, null, null, true),
            CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Message.Should().Be("Indiquez au moins une périodicité : un intervalle en kilomètres ou en mois.");
        (await context.MaintenanceTemplates.CountAsync()).Should().Be(0);
    }

    // ── Marquer fait ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Marquer_fait_sur_un_gabarit_inexistant_donne_un_404_lisible_sans_ecriture()
    {
        using var context = await SeedAsync();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId);

        var act = () => new MarkMaintenanceDoneCommandHandler(context, tenant.Object)
            .Handle(MarkDone(1, 99_999), CancellationToken.None);

        (await act.Should().ThrowAsync<NotFoundException>())
            .Which.Message.Should().StartWith("Ce modèle d'entretien est introuvable");
        (await context.VehicleCosts.CountAsync()).Should().Be(0);
        (await context.MaintenanceLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Marquer_fait_sur_un_vehicule_inexistant_donne_un_404_lisible()
    {
        using var context = await SeedAsync();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId);

        var act = () => new MarkMaintenanceDoneCommandHandler(context, tenant.Object)
            .Handle(MarkDone(404, 12), CancellationToken.None);

        (await act.Should().ThrowAsync<NotFoundException>())
            .Which.Message.Should().StartWith("Ce véhicule est introuvable");
        (await context.VehicleCosts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Marquer_fait_sur_le_vehicule_ou_le_gabarit_d_une_autre_societe_est_introuvable()
    {
        // Le filtre multi-tenant est contourné pour l'administrateur système (et absent du
        // contexte de test) : seul le filtre société du handler protège les autres sociétés.
        using var context = await SeedAsync();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId);
        var handler = new MarkMaintenanceDoneCommandHandler(context, tenant.Object);

        await FluentActions.Invoking(() => handler.Handle(MarkDone(20, 12), CancellationToken.None))
            .Should().ThrowAsync<NotFoundException>();
        await FluentActions.Invoking(() => handler.Handle(MarkDone(1, 30), CancellationToken.None))
            .Should().ThrowAsync<NotFoundException>();

        (await context.VehicleCosts.CountAsync()).Should().Be(0);
        (await context.Vehicles.AsNoTracking().FirstAsync(v => v.Id == 20)).Mileage.Should().Be(84_438);
    }

    // ── Affectation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Affecter_un_modele_au_vehicule_d_une_autre_societe_est_refuse()
    {
        // Reproduction de la fiche : assign {vehicleId: 20, templateId: 12}. Pour un
        // administrateur système, le filtre multi-tenant ne masque pas le véhicule 20.
        using var context = await SeedAsync();
        var scheduler = new MaintenanceSchedulerService(context, NullLogger<MaintenanceSchedulerService>.Instance);

        var act = () => new AssignMaintenanceTemplateCommandHandler(context, scheduler, TestDbContextFactory.CreateMockTenantService().Object)
            .Handle(new AssignMaintenanceTemplateCommand(VehicleId: 20, TemplateId: 12), CancellationToken.None);

        // Le véhicule existe (dans une autre société) : « n'existe plus » serait faux.
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Ce véhicule n'appartient pas à la société de ce modèle d'entretien*");
        (await context.VehicleMaintenanceSchedules.CountAsync(s => s.VehicleId == 20)).Should().Be(0);
    }

    [Fact]
    public async Task Affecter_un_vehicule_supprime_dit_qu_il_n_existe_plus()
    {
        using var context = await SeedAsync();
        var scheduler = new MaintenanceSchedulerService(context, NullLogger<MaintenanceSchedulerService>.Instance);

        var act = () => new AssignMaintenanceTemplateCommandHandler(context, scheduler, TestDbContextFactory.CreateMockTenantService().Object)
            .Handle(new AssignMaintenanceTemplateCommand(VehicleId: 404, TemplateId: 12), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Ce véhicule n'existe plus*");
    }

    [Fact]
    public async Task L_echeancier_affecte_par_un_administrateur_systeme_appartient_a_la_societe_du_vehicule()
    {
        // Jeton de la société 99 : l'échéancier du véhicule 20 (société 2) recevait CompanyId 99
        // et disparaissait des listes et statistiques de la société 2.
        using var context = await SeedAsync(ContexteAdministrateurSysteme(companyId: 99));
        var scheduler = new MaintenanceSchedulerService(context, NullLogger<MaintenanceSchedulerService>.Instance);

        var id = await new AssignMaintenanceTemplateCommandHandler(context, scheduler, TestDbContextFactory.CreateMockTenantService().Object)
            .Handle(new AssignMaintenanceTemplateCommand(VehicleId: 20, TemplateId: 30), CancellationToken.None);

        (await context.VehicleMaintenanceSchedules.AsNoTracking().FirstAsync(s => s.Id == id))
            .CompanyId.Should().Be(OtherCompanyId);
    }

    [Fact]
    public async Task L_echeancier_cree_en_declarant_des_gratuits_appartient_a_la_societe_du_vehicule()
    {
        using var context = await SeedAsync(ContexteAdministrateurSysteme(companyId: 99));

        var id = await DeclareFreeHandler(context).Handle(
            new DeclareFreeMaintenancesCommand(20, 30, 2, "Concession", null, null), CancellationToken.None);

        var schedule = await context.VehicleMaintenanceSchedules.AsNoTracking().FirstAsync(s => s.Id == id);
        schedule.CompanyId.Should().Be(OtherCompanyId);
        schedule.FreeUsesRemaining.Should().Be(2);
    }

    // ── Entretiens gratuits ────────────────────────────────────────────────────

    [Fact]
    public async Task Declarer_des_entretiens_gratuits_sur_le_vehicule_d_une_autre_societe_est_introuvable()
    {
        using var context = await SeedAsync();

        var act = () => DeclareFreeHandler(context).Handle(
            new DeclareFreeMaintenancesCommand(20, 12, 2, "Concession", null, null), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        (await context.VehicleMaintenanceSchedules.CountAsync(s => s.VehicleId == 20)).Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Declarer_zero_entretien_gratuit_est_un_refus_metier(int count)
    {
        using var context = await SeedAsync();

        var act = () => DeclareFreeHandler(context).Handle(
            new DeclareFreeMaintenancesCommand(1, 12, count, null, null, null), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Message.Should().Be("Le nombre d'entretiens gratuits doit être supérieur à zéro.");
    }

    [Fact]
    public async Task Declarer_des_entretiens_gratuits_sur_un_gabarit_ou_un_vehicule_introuvable_donne_un_404_lisible()
    {
        using var context = await SeedAsync();
        var handler = DeclareFreeHandler(context);

        (await FluentActions.Invoking(() => handler.Handle(
                new DeclareFreeMaintenancesCommand(1, 99_999, 1, null, null, null), CancellationToken.None))
            .Should().ThrowAsync<NotFoundException>())
            .Which.Message.Should().StartWith("Ce modèle d'entretien est introuvable");

        (await FluentActions.Invoking(() => handler.Handle(
                new DeclareFreeMaintenancesCommand(404, 12, 1, null, null, null), CancellationToken.None))
            .Should().ThrowAsync<NotFoundException>())
            .Which.Message.Should().StartWith("Ce véhicule est introuvable");

        (await context.VehicleMaintenanceSchedules.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(2, 3, "Le nombre d'entretiens gratuits restants ne peut pas dépasser le total.")]
    [InlineData(-1, 0, "Les compteurs d'entretiens gratuits ne peuvent pas être négatifs.")]
    public async Task Des_compteurs_gratuits_incoherents_sont_un_refus_metier_sans_ecriture(int total, int remaining, string message)
    {
        using var context = await SeedAsync();

        var act = () => new UpdateFreeMaintenanceCommandHandler(context, TestDbContextFactory.CreateMockTenantService().Object).Handle(
            new UpdateFreeMaintenanceCommand(47, total, remaining, "QA", null, null), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(message);
        var schedule = await context.VehicleMaintenanceSchedules.AsNoTracking().FirstAsync(s => s.Id == 47);
        schedule.FreeUsesTotal.Should().Be(2);
        schedule.FreeUsesRemaining.Should().Be(1);
    }

    // ── Traduction HTTP ────────────────────────────────────────────────────────

    [Fact]
    public async Task Le_middleware_rend_un_404_avec_le_message_francais()
    {
        // Levée réelle du handler (et non une exception fabriquée par le test) : c'est le
        // message que l'écran Entretiens reçoit dans err.error.message.
        using var context = await SeedAsync();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId);
        var middleware = new ExceptionHandlingMiddleware(
            async _ => await new MarkMaintenanceDoneCommandHandler(context, tenant.Object)
                .Handle(MarkDone(1, 99_999), CancellationToken.None),
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(http);

        http.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        http.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(http.Response.Body);
        json.RootElement.GetProperty("message").GetString().Should().Be(
            "Ce modèle d'entretien est introuvable : il a peut-être été supprimé. Rechargez la page puis recommencez.");
    }

    // ── DEF-042 : l'écran sait qu'un échéancier est en pause ───────────────────

    [Fact]
    public async Task Le_planning_des_vehicules_indique_les_echeanciers_en_pause()
    {
        using var context = TestDbContextFactory.Create();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId);
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "QA", CompanyId = CompanyId, Mileage = 60_000 });
        context.MaintenanceTemplates.AddRange(
            new MaintenanceTemplate { Id = 1, Name = "Vidange", Category = "M", Priority = "medium", IntervalKm = 10_000, CompanyId = CompanyId },
            new MaintenanceTemplate { Id = 2, Name = "Freins", Category = "M", Priority = "medium", IntervalKm = 10_000, CompanyId = CompanyId });
        context.VehicleMaintenanceSchedules.AddRange(
            // Statut figé à « overdue » par la pause : l'écran le comptait dans « Urgents ».
            new VehicleMaintenanceSchedule { Id = 53, VehicleId = 1, TemplateId = 1, NextDueKm = 59_000, Status = "overdue", IsPaused = true, CompanyId = CompanyId },
            new VehicleMaintenanceSchedule { Id = 54, VehicleId = 1, TemplateId = 2, NextDueKm = 59_500, Status = "overdue", CompanyId = CompanyId });
        await context.SaveChangesAsync();

        var result = await new GetVehicleMaintenanceQueryHandler(context, tenant.Object)
            .Handle(new GetVehicleMaintenanceQuery(), CancellationToken.None);

        var items = result.Items.Single().MaintenanceItems;
        items.Single(i => i.ScheduleId == 53).IsPaused.Should().BeTrue();
        items.Single(i => i.ScheduleId == 54).IsPaused.Should().BeFalse();
    }
}
