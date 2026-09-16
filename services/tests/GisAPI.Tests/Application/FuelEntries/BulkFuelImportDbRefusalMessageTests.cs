using FluentAssertions;
using GisAPI.Application.Features.FuelEntries.Commands;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.FuelEntries;

/// <summary>
/// Recette GPA, DEF-010 : le motif d'un refus de la base citait le véhicule, alors
/// que le véhicule est retrouvé en base par son matricule et ne peut pas violer sa
/// clé étrangère. Ici une VRAIE violation (chauffeur inexistant, clés étrangères
/// SQLite activées) doit donner un motif qui désigne les vraies causes possibles.
/// </summary>
public class BulkFuelImportDbRefusalMessageTests
{
    private const int CompanyId = 7;
    private const int FuelTypeEssence = 26;

    private static TestGisDbContext CreerContexteAvecClesEtrangeres()
    {
        var context = TestDbContextFactory.Create();
        context.Societes.Add(TestDataBuilder.CreateSociete(id: CompanyId, subscriptionTypeId: null));
        context.FuelTypes.Add(new FuelType { Id = FuelTypeEssence, Code = "essence", Name = "Essence" });
        var vehicle = TestDataBuilder.CreateVehicle(id: 1, companyId: CompanyId);
        vehicle.Plate = "GC-936-LP";
        context.Vehicles.Add(vehicle);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        // Jeu de données en place : les contraintes s'appliquent désormais aux pleins insérés.
        context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
        return context;
    }

    private static BulkCreateFuelEntriesCommandHandler CreerHandler(TestGisDbContext context)
    {
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: 1);
        var createHandler = new CreateFuelEntryCommandHandler(context, tenant.Object, Mock.Of<IPublisher>());
        var sender = new Mock<ISender>();
        sender
            .Setup(s => s.Send(It.IsAny<CreateFuelEntryCommand>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<int> r, CancellationToken ct) => createHandler.Handle((CreateFuelEntryCommand)r, ct));
        return new BulkCreateFuelEntriesCommandHandler(sender.Object, context, NullLogger<BulkCreateFuelEntriesCommandHandler>.Instance);
    }

    private static CreateFuelEntryCommand Ligne(string notes, int? driverId = null) =>
        new("GC-936-LP", FuelTypeEssence, 40m, 2.5m, new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc),
            "Station QA", null, notes, driverId, null);

    [Fact]
    public async Task ChauffeurInexistant_MotifSansVehicule_EtLignesSuivantesEnregistrees()
    {
        using var context = CreerContexteAvecClesEtrangeres();

        var result = await CreerHandler(context).Handle(new BulkCreateFuelEntriesCommand(new[]
        {
            Ligne("L1"),
            Ligne("L2", driverId: 999),
            Ligne("L3"),
        }), CancellationToken.None);

        result.Results.Select(r => r.Success).Should().Equal(true, false, true);

        var motif = result.Results[1].Error;
        motif.Should().StartWith("Enregistrement refusé par la base de données");
        motif.Should().Contain("chauffeur");
        motif.Should().NotContain("véhicule");

        var notes = await context.FuelEntries.AsNoTracking().OrderBy(f => f.Id).Select(f => f.Notes).ToListAsync();
        notes.Should().Equal("L1", "L3");
    }
}
