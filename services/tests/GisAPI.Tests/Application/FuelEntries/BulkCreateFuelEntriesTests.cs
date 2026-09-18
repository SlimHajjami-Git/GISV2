using FluentAssertions;
using GisAPI.Application.Features.FuelEntries.Commands;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.FuelEntries;

/// <summary>
/// Recette GPA, DEF-010 : dans l'import en masse, une ligne refusée à
/// l'enregistrement restait suivie par le contexte EF partagé et faisait
/// échouer toutes les lignes valides qui la suivaient.
/// </summary>
public class BulkCreateFuelEntriesTests
{
    private const int CompanyId = 7;
    private const int FuelTypeEssence = 26;
    private const string MarqueRefusBase = "refus-base";

    /// <summary>
    /// Contexte qui simule un refus de la base (clé étrangère, taille de colonne…)
    /// pour un plein marqué : l'exception survient au SaveChanges, entités encore
    /// suivies, exactement comme une violation de contrainte PostgreSQL.
    /// </summary>
    private sealed class ContexteAvecRefusBase : TestGisDbContext
    {
        public ContexteAvecRefusBase(DbContextOptions<TestGisDbContext> options) : base(options) { }

        public override Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            if (ChangeTracker.Entries<FuelEntry>().Any(e => e.State == EntityState.Added && e.Entity.Notes == MarqueRefusBase))
                throw new DbUpdateException("An error occurred while saving the entity changes. See the inner exception for details.");
            return base.SaveChangesAsync(ct);
        }
    }

    private static TestGisDbContext CreerContexte()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys = OFF;";
            cmd.ExecuteNonQuery();
        }

        var options = new DbContextOptionsBuilder<TestGisDbContext>().UseSqlite(connection).Options;
        var context = new ContexteAvecRefusBase(options);
        context.Database.EnsureCreated();

        context.FuelTypes.Add(new FuelType { Id = FuelTypeEssence, Code = "essence", Name = "Essence" });
        var vehicle = TestDataBuilder.CreateVehicle(id: 1, companyId: CompanyId);
        vehicle.Plate = "GC-936-LP";
        vehicle.Mileage = 50_000;
        context.Vehicles.Add(vehicle);
        context.SaveChanges();
        context.ChangeTracker.Clear();
        return context;
    }

    private static BulkCreateFuelEntriesCommandHandler CreerHandler(TestGisDbContext context)
    {
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: 1);
        var createHandler = new CreateFuelEntryCommandHandler(context, tenant.Object, Mock.Of<IPublisher>());

        // Chaque ligne passe par le vrai handler de création, comme via MediatR.
        var sender = new Mock<ISender>();
        sender
            .Setup(s => s.Send(It.IsAny<CreateFuelEntryCommand>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<int> r, CancellationToken ct) => createHandler.Handle((CreateFuelEntryCommand)r, ct));

        return new BulkCreateFuelEntriesCommandHandler(sender.Object, context, NullLogger<BulkCreateFuelEntriesCommandHandler>.Instance);
    }

    private static CreateFuelEntryCommand Ligne(int fuelTypeId = FuelTypeEssence, string? notes = null, long? odometerKm = null) =>
        new("GC-936-LP", fuelTypeId, 40m, 2.5m, new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc),
            "Station QA", null, notes, null, odometerKm);

    [Fact]
    public async Task LigneRefuseeParLaBase_LesLignesValidesSuivantesSontEnregistrees()
    {
        using var context = CreerContexte();
        var handler = CreerHandler(context);

        var result = await handler.Handle(new BulkCreateFuelEntriesCommand(new[]
        {
            Ligne(notes: "L1", odometerKm: 51_000),
            Ligne(notes: MarqueRefusBase, odometerKm: 60_000),
            Ligne(notes: "L3", odometerKm: 52_000),
        }), CancellationToken.None);

        result.Total.Should().Be(3);
        result.Success.Should().Be(2);
        result.Failed.Should().Be(1);
        result.Results.Select(r => r.Success).Should().Equal(true, false, true);
        result.Results.Select(r => r.Row).Should().Equal(1, 2, 3);

        var refus = result.Results[1];
        refus.Id.Should().Be(0);
        refus.Error.Should().StartWith("Enregistrement refusé par la base de données");
        refus.Error.Should().NotContain("entity changes");

        var notes = await context.FuelEntries.AsNoTracking().OrderBy(f => f.Id).Select(f => f.Notes).ToListAsync();
        notes.Should().Equal("L1", "L3");

        // Le kilométrage avancé par la ligne refusée (60 000) n'a pas fui dans l'enregistrement suivant.
        var mileage = await context.Vehicles.AsNoTracking().Where(v => v.Id == 1).Select(v => v.Mileage).SingleAsync();
        mileage.Should().Be(52_000);
    }

    [Fact]
    public async Task TypeDeCarburantInconnu_SeuleCetteLigneEstRefuseeAvecUnMotifEnFrancais()
    {
        using var context = CreerContexte();
        var handler = CreerHandler(context);

        var result = await handler.Handle(new BulkCreateFuelEntriesCommand(new[]
        {
            Ligne(notes: "L1"),
            Ligne(fuelTypeId: 99, notes: "L2"),
            Ligne(notes: "L3"),
        }), CancellationToken.None);

        result.Success.Should().Be(2);
        result.Failed.Should().Be(1);
        result.Results[1].Success.Should().BeFalse();
        result.Results[1].Error.Should().StartWith("Type de carburant inconnu (identifiant 99)");
        (await context.FuelEntries.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task LigneVide_RefuseeASonRangSansBloquerLesAutres()
    {
        using var context = CreerContexte();
        var handler = CreerHandler(context);

        var result = await handler.Handle(new BulkCreateFuelEntriesCommand(new CreateFuelEntryCommand?[]
        {
            null,
            Ligne(notes: "L2"),
        }), CancellationToken.None);

        result.Results.Select(r => r.Success).Should().Equal(false, true);
        result.Results[0].Error.Should().StartWith("Ligne vide");
    }

    [Fact]
    public async Task CreationUnitaire_TypeDeCarburantInconnu_RefuseeSansEcriture()
    {
        using var context = CreerContexte();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: 1);
        var handler = new CreateFuelEntryCommandHandler(context, tenant.Object, Mock.Of<IPublisher>());

        var act = () => handler.Handle(Ligne(fuelTypeId: 99, odometerKm: 70_000), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Type de carburant inconnu*");
        (await context.FuelEntries.CountAsync()).Should().Be(0);
        (await context.Vehicles.AsNoTracking().Where(v => v.Id == 1).Select(v => v.Mileage).SingleAsync()).Should().Be(50_000);
    }
}
