using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using FluentAssertions.Execution;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Documents.Commands;
using GisAPI.Application.Features.Repairs;
using GisAPI.Application.Features.Repairs.Commands;
using GisAPI.Application.Features.Repairs.Handlers;
using GisAPI.Application.Features.Reports.Queries.GetVehicleCostEvolution;
using GisAPI.Application.Features.VehicleMaintenance.Commands;
using GisAPI.Application.Services;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Middleware;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Middleware;

/// <summary>
/// M9-NOTFOUND (campagne Calypso GPA, 17/09/2026) : NotFoundException n'avait que le gabarit
/// anglais « Entity "X" (id) was not found. ». Quatre lots de recette avaient donc créé
/// chacun une sous-classe dont le seul rôle était de remplacer ce message par un libellé
/// français (réparations, rapports, entretiens, échéances). Le constructeur à message libre
/// les rend inutiles ; ces tests verrouillent le même 404 et le même message qu'avant.
/// </summary>
public class NotFoundMessageLibreTests
{
    private const int CompanyId = 7;
    private const int OtherCompanyId = 5;
    private const int VehiculeEtranger = 20;

    private const string VehiculeIntrouvable = "Véhicule introuvable.";
    private const string EntretienVehiculeIntrouvable =
        "Ce véhicule est introuvable : il a peut-être été supprimé. Rechargez la page puis recommencez.";

    private static readonly DateTime Jour = new(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc);

    private static async Task<(int Status, string? Message)> RenduParLeMiddlewareAsync(Func<Task> action)
    {
        var middleware = new ExceptionHandlingMiddleware(_ => action(), NullLogger<ExceptionHandlingMiddleware>.Instance);
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(http);

        http.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(http.Response.Body);
        return (http.Response.StatusCode, json.RootElement.GetProperty("message").GetString());
    }

    /// <summary>
    /// Sous-classe dont le seul apport est le libellé : aucune propriété publique déclarée
    /// hormis Message (redéfini, comme les quatre d'avant, ou passé au constructeur de base).
    /// Une sous-classe qui porte une donnée de plus reste permise.
    /// </summary>
    private static bool NeSertQuAuLibelle(Type t) =>
        t != typeof(NotFoundException)
        && typeof(NotFoundException).IsAssignableFrom(t)
        && t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .All(p => p.Name == nameof(Exception.Message));

    private sealed class LibelleRedefini : NotFoundException
    {
        public LibelleRedefini() : base("Véhicule", 20) { }
        public override string Message => VehiculeIntrouvable;
    }

    private sealed class LibellePasseALaBase : NotFoundException
    {
        public LibellePasseALaBase() : base(VehiculeIntrouvable) { }
    }

    private sealed class AvecDonneePropre : NotFoundException
    {
        public AvecDonneePropre(int vehicleId) : base(VehiculeIntrouvable) => VehicleId = vehicleId;
        public int VehicleId { get; }
    }

    [Fact]
    public void La_regle_vise_les_sous_classes_de_libelle_et_laisse_celles_qui_portent_une_donnee()
    {
        NeSertQuAuLibelle(typeof(LibelleRedefini)).Should().BeTrue("forme des quatre sous-classes supprimées");
        NeSertQuAuLibelle(typeof(LibellePasseALaBase)).Should().BeTrue();
        NeSertQuAuLibelle(typeof(AvecDonneePropre)).Should().BeFalse();
        NeSertQuAuLibelle(typeof(NotFoundException)).Should().BeFalse();
    }

    [Fact]
    public void Aucune_sous_classe_de_NotFoundException_n_existe_pour_son_seul_libelle()
    {
        var sousClasses = new[]
            {
                typeof(NotFoundException).Assembly,
                typeof(IGisDbContext).Assembly,
                typeof(ExceptionHandlingMiddleware).Assembly
            }
            .SelectMany(a => a.GetTypes())
            .Where(NeSertQuAuLibelle)
            .Select(t => t.FullName);

        sousClasses.Should().BeEmpty(
            "un libellé français se passe au constructeur new NotFoundException(message), sans sous-classe");
    }

    [Fact]
    public async Task Un_message_libre_est_rendu_tel_quel_en_404_sans_changer_le_gabarit_anglais()
    {
        var libre = await RenduParLeMiddlewareAsync(() => throw new NotFoundException(VehiculeIntrouvable));
        var gabarit = await RenduParLeMiddlewareAsync(() => throw new NotFoundException("Vehicle", 4));

        libre.Should().Be((StatusCodes.Status404NotFound, VehiculeIntrouvable));
        // D'autres écrans et tests lisent encore ce format : il ne bouge pas.
        gabarit.Should().Be((StatusCodes.Status404NotFound, "Entity \"Vehicle\" (4) was not found."));
    }

    [Fact]
    public async Task Les_anciennes_levees_des_sous_classes_rendent_le_meme_404_francais()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "QA", Plate = "111 TU 1", CompanyId = CompanyId, Mileage = 50_000 },
            new Vehicle { Id = VehiculeEtranger, Name = "Etranger", Plate = "999 TU 9", CompanyId = OtherCompanyId, Mileage = 84_438 });
        ctx.MaintenanceTemplates.Add(new MaintenanceTemplate
        {
            Id = 12, Name = "Vidange", Category = "Moteur", Priority = "medium", IntervalKm = 10_000, CompanyId = CompanyId
        });
        await ctx.SaveChangesAsync();

        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId).Object;
        var aucunePiece = new List<CreateRepairPartRequest>();
        var reparation = await new CreateRepairCommandHandler(ctx, tenant, Mock.Of<IPublisher>()).Handle(
            new CreateRepairCommand(1, null, "QA réparation", Jour, 0, 50m, null, null, aucunePiece, "autre"),
            CancellationToken.None);

        var cas = new (string Chemin, Func<Task> Action, string Message)[]
        {
            ("POST /api/repairs", () => new CreateRepairCommandHandler(ctx, tenant, Mock.Of<IPublisher>()).Handle(
                new CreateRepairCommand(VehiculeEtranger, null, "QA", Jour, 0, 50m, null, null, aucunePiece, "autre"),
                CancellationToken.None), VehiculeIntrouvable),
            ("PUT /api/repairs/{id}", () => new UpdateRepairCommandHandler(ctx, tenant).Handle(
                new UpdateRepairCommand(reparation, VehiculeEtranger, null, "QA", Jour, 0, 50m, "completed", null, null, aucunePiece, "autre"),
                CancellationToken.None), VehiculeIntrouvable),
            ("renouvellement de document", () => new RenewDocumentCommandHandler(ctx, tenant, NullLogger<RenewDocumentCommandHandler>.Instance).Handle(
                new RenewDocumentCommand(VehiculeEtranger, "insurance", 480m, Jour, Jour.AddYears(1), null, null, null, null),
                CancellationToken.None), VehiculeIntrouvable),
            ("correction d'échéance", () => new UpdateDocumentExpiryCommandHandler(ctx, tenant).Handle(
                new UpdateDocumentExpiryCommand(VehiculeEtranger, "insurance", Jour.AddYears(1)),
                CancellationToken.None), VehiculeIntrouvable),
            ("GET /api/reports/costs/evolution/{id}", () => new GetVehicleCostEvolutionQueryHandler(ctx, tenant).Handle(
                new GetVehicleCostEvolutionQuery(VehiculeEtranger, new DateTime(2026, 9, 1), new DateTime(2026, 9, 30)),
                CancellationToken.None), VehiculeIntrouvable),
            ("marquer un entretien fait", () => new MarkMaintenanceDoneCommandHandler(ctx, tenant).Handle(
                new MarkMaintenanceDoneCommand(VehiculeEtranger, 12, Jour, 90_000, 30m, null, null),
                CancellationToken.None), EntretienVehiculeIntrouvable),
            ("déclarer des entretiens gratuits", () => new DeclareFreeMaintenancesCommandHandler(
                    ctx, new MaintenanceSchedulerService(ctx, NullLogger<MaintenanceSchedulerService>.Instance))
                .Handle(new DeclareFreeMaintenancesCommand(VehiculeEtranger, 12, 1, null, null, null), CancellationToken.None),
                EntretienVehiculeIntrouvable),
        };

        using var scope = new AssertionScope();
        foreach (var (chemin, action, message) in cas)
        {
            var rendu = await RenduParLeMiddlewareAsync(action);
            rendu.Should().Be((StatusCodes.Status404NotFound, message), chemin);
        }
    }
}
