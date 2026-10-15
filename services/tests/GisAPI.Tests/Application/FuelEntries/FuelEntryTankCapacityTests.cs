using FluentAssertions;
using GisAPI.Application.Features.FuelEntries.Commands;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using MediatR;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.FuelEntries;

/// <summary>
/// Plein supérieur au réservoir (95 L dans 60 L) enregistré sans un mot — campagne de tests GPA
/// du 06/10/2026, erreur n°14. Réservoir non renseigné (vide ou 0) : rien n'est bloqué
/// (décision de Karim du 15/10/2026). L'import Excel passe par le même handler.
/// </summary>
public class FuelEntryTankCapacityTests
{
    private const int CompanyId = 7;
    private const int Gazole = 1;

    private static async Task<TestGisDbContext> ContexteAsync()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.FuelTypes.Add(new FuelType { Id = Gazole, Code = "diesel", Name = "Gazole" });
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "Clio", Plate = "GE-768-HJ", CompanyId = CompanyId, Mileage = 10_000, FuelTankCapacity = 60 },
            new Vehicle { Id = 2, Name = "Kangoo", Plate = "GE-768-HJ55", CompanyId = CompanyId, Mileage = 10_000, FuelTankCapacity = null },
            new Vehicle { Id = 3, Name = "Partner", Plate = "GG-174-ZR", CompanyId = CompanyId, Mileage = 10_000, FuelTankCapacity = 0 });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static Task<int> PleinAsync(TestGisDbContext ctx, string plate, decimal litres) =>
        new CreateFuelEntryCommandHandler(ctx, TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: 1).Object, Mock.Of<IPublisher>())
            .Handle(new CreateFuelEntryCommand(plate, Gazole, litres, 1.8m, new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc),
                null, null, null, null, null), CancellationToken.None);

    [Fact]
    public async Task Une_quantite_superieure_au_reservoir_est_refusee()
    {
        using var ctx = await ContexteAsync();

        var act = () => PleinAsync(ctx, "GE-768-HJ", 95m);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("La quantité saisie (95 L) dépasse la capacité du réservoir de ce véhicule (60 L)*");
    }

    [Theory]
    [InlineData("GE-768-HJ", 60)]     // plein exact
    [InlineData("GE-768-HJ55", 95)]   // réservoir non renseigné
    [InlineData("GG-174-ZR", 95)]     // réservoir à 0
    public async Task Le_plein_exact_et_un_reservoir_non_renseigne_ne_sont_pas_bloques(string plate, int litres)
    {
        using var ctx = await ContexteAsync();

        (await PleinAsync(ctx, plate, litres)).Should().BeGreaterThan(0);
    }
}
