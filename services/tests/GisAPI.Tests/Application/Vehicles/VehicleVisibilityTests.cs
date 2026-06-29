using FluentAssertions;
using GisAPI.Application.Features.Users.Commands.UpdateUser;
using GisAPI.Application.Features.Vehicles.Queries.GetVehicles;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Vehicles;

/// <summary>
/// End-to-end visibility scenario (the one that regressed twice):
///   - A NON-admin user sees ONLY the vehicles explicitly assigned to them.
///   - A vehicle added AFTER assignment stays invisible until it too is assigned.
///   - A company admin is NEVER restricted by per-user assignments (sees all).
///
/// Visibility is decided by GetVehiclesQueryHandler using the tenant's roles
/// (isAdmin) + the user_vehicles rows. Assignments go through the real
/// UpdateUserCommandHandler, exactly like the operator clicking "assign" in the UI.
/// </summary>
public class VehicleVisibilityTests
{
    private const int CompanyId = 1;
    private const int AdminUserId = 1;
    private const int OperatorUserId = 10;

    private static ICurrentTenantService AdminTenant() =>
        Tenant(CompanyId, AdminUserId, "company_admin");

    /// Restricted operator — NO admin role, so isAdmin == false.
    private static ICurrentTenantService OperatorTenant() =>
        Tenant(CompanyId, OperatorUserId, "operator");

    private static ICurrentTenantService Tenant(int companyId, int userId, params string[] roles)
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.CompanyId).Returns(companyId);
        m.Setup(x => x.UserId).Returns(userId);
        m.Setup(x => x.UserRoles).Returns(roles);
        m.Setup(x => x.IsAuthenticated).Returns(true);
        return m.Object;
    }

    private static Vehicle Veh(int id, string name) => new Vehicle
    {
        Id = id,
        Name = name,
        Type = "camion",
        Plate = $"PLATE-{id}",
        Status = "available",
        Mileage = 0,
        CompanyId = CompanyId
    };

    private static async Task SeedAsync(TestGisDbContext ctx)
    {
        ctx.SubscriptionTypes.Add(TestDataBuilder.CreateSubscriptionType());
        ctx.Societes.Add(TestDataBuilder.CreateSociete(id: CompanyId));
        ctx.Users.Add(TestDataBuilder.CreateUser(id: OperatorUserId, companyId: CompanyId, email: "operator@test.com"));
        ctx.Vehicles.Add(Veh(1, "Vehicle 1"));
        ctx.Vehicles.Add(Veh(2, "Vehicle 2"));
        await ctx.SaveChangesAsync();
    }

    /// Assign exactly `vehicleIds` to the operator, via the real command (admin acting).
    private static async Task AssignAsync(TestGisDbContext ctx, params int[] vehicleIds)
    {
        var handler = new UpdateUserCommandHandler(ctx, AdminTenant());
        await handler.Handle(new UpdateUserCommand(
            Id: OperatorUserId,
            FirstName: "Op", LastName: "Erator", Email: "operator@test.com",
            Phone: null, RoleId: 1, Status: "active",
            AssignedVehicleIds: vehicleIds), CancellationToken.None);
    }

    private static async Task<List<int>> VisibleIdsAsync(TestGisDbContext ctx, ICurrentTenantService tenant)
    {
        var handler = new GetVehiclesQueryHandler(ctx, tenant);
        var result = await handler.Handle(new GetVehiclesQuery(PageSize: 500), CancellationToken.None);
        return result.Items.Select(v => v.Id).OrderBy(i => i).ToList();
    }

    [Fact]
    public async Task Operator_sees_only_assigned_vehicles_and_not_a_newly_added_one_until_assigned()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        // 1) Assign vehicle 1 → the operator sees ONLY vehicle 1.
        await AssignAsync(ctx, 1);
        (await VisibleIdsAsync(ctx, OperatorTenant()))
            .Should().Equal(new[] { 1 }, "the operator is restricted to their assigned vehicle (not #2)");

        // 2) A NEW vehicle (3) is added but NOT assigned → it must stay invisible.
        ctx.Vehicles.Add(Veh(3, "Vehicle 3 (new)"));
        await ctx.SaveChangesAsync();
        (await VisibleIdsAsync(ctx, OperatorTenant()))
            .Should().Equal(new[] { 1 }, "an unassigned, newly-added vehicle must NOT become visible automatically");

        // 3) Now assign the new vehicle (3) too → the operator sees [1, 3] (never #2).
        await AssignAsync(ctx, 1, 3);
        (await VisibleIdsAsync(ctx, OperatorTenant()))
            .Should().Equal(new[] { 1, 3 }, "after assignment the new vehicle is visible; the never-assigned #2 stays hidden");
    }

    [Fact]
    public async Task Company_admin_is_never_restricted_by_assignments()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);
        ctx.Vehicles.Add(Veh(3, "Vehicle 3"));
        await ctx.SaveChangesAsync();
        // The admin has ZERO user_vehicles rows, yet must see the whole fleet.
        (await VisibleIdsAsync(ctx, AdminTenant()))
            .Should().Equal(new[] { 1, 2, 3 }, "an admin/CEO sees all company vehicles regardless of assignments");
    }

    [Fact]
    public async Task Assigning_with_a_stale_or_foreign_vehicle_id_assigns_only_the_valid_ones()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx); // company 1 has vehicles 1 and 2

        // Assign [1, 999] — 999 doesn't exist / isn't in the company. This must NOT throw
        // "Un ou plusieurs véhicules sont invalides"; only the valid #1 is assigned.
        await AssignAsync(ctx, 1, 999);

        (await VisibleIdsAsync(ctx, OperatorTenant()))
            .Should().Equal(new[] { 1 }, "the stale/foreign id is dropped, the valid one is assigned (no error)");
    }
}
