using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Employees.Commands.CreateEmployee;
using GisAPI.Application.Features.Employees.Commands.DeleteEmployee;
using GisAPI.Application.Features.Employees.Commands.UpdateEmployee;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Employees;

/// <summary>
/// La fiche employé écrivait users.id dans vehicles.assigned_driver_id, clé
/// étrangère vers drivers(id) : erreur 23503 en base, ou véhicule retiré / confié
/// au chauffeur dont l'id coïncidait avec celui de l'employé.
/// </summary>
public class EmployeeVehicleAssignmentTests
{
    private const int CompanyId = 7;
    private const int EmployeId = 12;
    private const int VehiculeId = 40;

    // Chauffeur n° 12 (table drivers) au volant du véhicule 40 : même numéro que l'employé n° 12 (table users).
    private static TestGisDbContext CreerContexte(bool avecEmploye = true)
    {
        var context = TestDbContextFactory.Create();
        context.Drivers.Add(new Driver { Id = EmployeId, FirstName = "Karim", LastName = "Chauffeur", CompanyId = CompanyId, AssignedVehicleId = VehiculeId });
        var vehicle = TestDataBuilder.CreateVehicle(id: VehiculeId, companyId: CompanyId);
        vehicle.AssignedDriverId = EmployeId;
        context.Vehicles.Add(vehicle);
        if (avecEmploye)
        {
            var user = TestDataBuilder.CreateUser(id: EmployeId, companyId: CompanyId, email: "employe@test.com");
            user.EmployeeRole = "supervisor";
            context.Users.Add(user);
        }
        context.SaveChanges();
        context.ChangeTracker.Clear();
        return context;
    }

    private static Task<int?> ChauffeurDuVehiculeAsync(TestGisDbContext context) =>
        context.Vehicles.AsNoTracking().Where(v => v.Id == VehiculeId).Select(v => v.AssignedDriverId).SingleAsync();

    private static CreateEmployeeCommandHandler CreerHandlerCreation(TestGisDbContext context)
    {
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.HashPassword(It.IsAny<string>())).Returns("hash");
        return new CreateEmployeeCommandHandler(context, hasher.Object,
            TestDbContextFactory.CreateMockTenantService(companyId: CompanyId).Object);
    }

    private static CreateEmployeeCommand Creation(int? assignVehicleId) =>
        new("Nouvel", "Employé", "nouvel@test.com", null, "driver", null, null, null, null, null, null, assignVehicleId);

    private static UpdateEmployeeCommand Modification(int? assignVehicleId) =>
        new(EmployeId, "Prénom modifié", "User", "employe@test.com", null, "supervisor", null,
            null, null, null, null, null, null, assignVehicleId);

    [Fact]
    public async Task Creation_AvecAffectationDeVehicule_RefuseeAvantTouteEcriture()
    {
        using var context = CreerContexte(avecEmploye: false);

        var act = () => CreerHandlerCreation(context).Handle(Creation(VehiculeId), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Un employé n'est pas une fiche chauffeur*");
        (await context.Users.CountAsync()).Should().Be(0);
        (await ChauffeurDuVehiculeAsync(context)).Should().Be(EmployeId);
    }

    [Fact]
    public async Task Creation_SansAffectation_NeTouchePasAuxVehicules()
    {
        using var context = CreerContexte(avecEmploye: false);

        var dto = await CreerHandlerCreation(context).Handle(Creation(null), CancellationToken.None);

        dto.AssignedVehicleId.Should().BeNull();
        (await context.Users.CountAsync()).Should().Be(1);
        (await ChauffeurDuVehiculeAsync(context)).Should().Be(EmployeId);
    }

    [Fact]
    public async Task Modification_NeRetirePasLeChauffeurDontLIdCoincideAvecLEmploye()
    {
        using var context = CreerContexte();

        await new UpdateEmployeeCommandHandler(context).Handle(Modification(null), CancellationToken.None);

        context.ChangeTracker.Clear();
        (await context.Users.AsNoTracking().SingleAsync(u => u.Id == EmployeId)).FirstName.Should().Be("Prénom modifié");
        (await ChauffeurDuVehiculeAsync(context)).Should().Be(EmployeId);
    }

    [Fact]
    public async Task Modification_AvecAffectationDeVehicule_RefuseeSansRienModifier()
    {
        using var context = CreerContexte();

        var act = () => new UpdateEmployeeCommandHandler(context).Handle(Modification(VehiculeId), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Un employé n'est pas une fiche chauffeur*");
        context.ChangeTracker.Clear();
        (await context.Users.AsNoTracking().SingleAsync(u => u.Id == EmployeId)).FirstName.Should().Be("Test");
        (await ChauffeurDuVehiculeAsync(context)).Should().Be(EmployeId);
    }

    [Fact]
    public async Task Suppression_NeRetirePasLeChauffeurDontLIdCoincideAvecLEmploye()
    {
        using var context = CreerContexte();

        await new DeleteEmployeeCommandHandler(context).Handle(new DeleteEmployeeCommand(EmployeId), CancellationToken.None);

        context.ChangeTracker.Clear();
        (await context.Users.CountAsync()).Should().Be(0);
        (await ChauffeurDuVehiculeAsync(context)).Should().Be(EmployeId);
    }
}
