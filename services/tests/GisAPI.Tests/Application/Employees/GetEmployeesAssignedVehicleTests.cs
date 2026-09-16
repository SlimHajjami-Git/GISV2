using FluentAssertions;
using GisAPI.Application.Features.Employees.Queries.GetEmployees;
using GisAPI.Domain.Entities;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GisAPI.Tests.Application.Employees;

/// <summary>
/// Côté lecture de la fiche employé : le « véhicule affecté » était déduit de
/// vehicles.assigned_driver_id == users.id, alors que cette colonne pointe vers
/// drivers(id). L'employé n° 12 héritait du véhicule du chauffeur n° 12.
/// </summary>
public class GetEmployeesAssignedVehicleTests
{
    private const int CompanyId = 7;
    private const int EmployeId = 12;
    private const int VehiculeDuChauffeur = 40;
    private const int VehiculeVisible = 41;

    /// <summary>
    /// Vrai GisDbContext en mémoire : le contexte de test SQLite ignore la navigation
    /// User.UserVehicles, que la requête inclut. Les colonnes propres à PostgreSQL
    /// (jsonb, tableaux…) n'ont pas d'équivalent en mémoire et sont retirées du modèle.
    /// </summary>
    private sealed class ContexteEnMemoire : GisDbContext
    {
        public ContexteEnMemoire(DbContextOptions<GisDbContext> options)
            : base(options, TestDbContextFactory.CreateMockTenantService(CompanyId).Object) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            foreach (var entity in modelBuilder.Model.GetEntityTypes().ToList())
                foreach (var property in entity.GetDeclaredProperties().ToList())
                    if (!Simple(property.ClrType) && !property.IsKey() && !property.IsForeignKey())
                        modelBuilder.Entity(entity.ClrType).Ignore(property.Name);
        }

        private static bool Simple(Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime)
                || t == typeof(DateTimeOffset) || t == typeof(TimeSpan) || t == typeof(Guid) || t == typeof(byte[])
                || t == typeof(DateOnly) || t == typeof(TimeOnly);
        }
    }

    private static GisDbContext CreerContexte()
    {
        var context = new ContexteEnMemoire(new DbContextOptionsBuilder<GisDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), b => b.EnableNullChecks(false))
            .Options);
        context.Roles.Add(new Role { Id = 1, Name = "Employé", SocieteId = CompanyId });

        // Chauffeur n° 12 (table drivers) au volant du véhicule 40.
        context.Drivers.Add(new Driver { Id = EmployeId, FirstName = "Karim", LastName = "Chauffeur", CompanyId = CompanyId, AssignedVehicleId = VehiculeDuChauffeur });
        var vehiculeChauffeur = TestDataBuilder.CreateVehicle(id: VehiculeDuChauffeur, companyId: CompanyId, name: "Utilitaire 40");
        vehiculeChauffeur.AssignedDriverId = EmployeId;
        context.Vehicles.Add(vehiculeChauffeur);
        context.Vehicles.Add(TestDataBuilder.CreateVehicle(id: VehiculeVisible, companyId: CompanyId, name: "Utilitaire 41"));

        // Employé n° 12 (table users), même numéro, qui voit le véhicule 40 et le véhicule 41.
        var employe = TestDataBuilder.CreateUser(id: EmployeId, companyId: CompanyId, email: "employe@test.com");
        employe.EmployeeRole = "supervisor";
        context.Users.Add(employe);
        context.UserVehicles.AddRange(
            new UserVehicle { Id = 1, UserId = EmployeId, VehicleId = VehiculeDuChauffeur },
            new UserVehicle { Id = 2, UserId = EmployeId, VehicleId = VehiculeVisible });

        context.SaveChanges();
        context.ChangeTracker.Clear();
        return context;
    }

    [Fact]
    public async Task LEmployeNHeritePasDuVehiculeDuChauffeurDeMemeNumero()
    {
        using var context = CreerContexte();
        var handler = new GetEmployeesQueryHandler(context, TestDbContextFactory.CreateMockTenantService(companyId: CompanyId).Object);

        var employes = await handler.Handle(new GetEmployeesQuery(), CancellationToken.None);

        var employe = employes.Should().ContainSingle().Subject;
        employe.Id.Should().Be(EmployeId);
        employe.AssignedVehicleId.Should().BeNull();
        employe.AssignedVehicleName.Should().BeNull();
        employe.AssignedVehiclePlate.Should().BeNull();
        // Les véhicules visibles par l'employé (user_vehicles) restent exposés.
        employe.AssignedVehicleIds.Should().BeEquivalentTo(new[] { VehiculeDuChauffeur, VehiculeVisible });
    }
}
