using FluentAssertions;
using GisAPI.Infrastructure.MultiTenancy;

namespace GisAPI.Tests.Infrastructure.Services;

public class CurrentTenantServiceTests
{
    [Fact]
    public void SetTenant_ShouldSetAllProperties()
    {
        // Arrange
        var service = new CurrentTenantService();

        // Act
        service.SetTenant(
            companyId: 5,
            userId: 10,
            email: "user@company.com",
            roles: new[] { "admin", "manager" },
            permissions: new[] { "vehicles", "geofences" }
        );

        // Assert
        service.CompanyId.Should().Be(5);
        service.UserId.Should().Be(10);
        service.UserEmail.Should().Be("user@company.com");
        service.UserRoles.Should().BeEquivalentTo(new[] { "admin", "manager" });
        service.UserPermissions.Should().BeEquivalentTo(new[] { "vehicles", "geofences" });
        service.HasPermission("vehicles").Should().BeTrue();
        service.HasPermission("maintenance").Should().BeFalse();
        service.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void InitialState_ShouldBeUnauthenticated()
    {
        // Arrange & Act
        var service = new CurrentTenantService();

        // Assert
        service.CompanyId.Should().BeNull();
        service.UserId.Should().BeNull();
        service.UserEmail.Should().BeNull();
        service.UserRoles.Should().BeEmpty();
        service.UserPermissions.Should().BeEmpty();
        service.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void SetTenant_CalledMultipleTimes_ShouldOverwrite()
    {
        // Arrange
        var service = new CurrentTenantService();

        // Act
        service.SetTenant(1, 1, "first@test.com", new[] { "user" }, new[] { "vehicles" });
        service.SetTenant(2, 2, "second@test.com", new[] { "admin" }, new[] { "reports" });

        // Assert
        service.CompanyId.Should().Be(2);
        service.UserId.Should().Be(2);
        service.UserEmail.Should().Be("second@test.com");
        service.UserRoles.Should().BeEquivalentTo(new[] { "admin" });
        service.UserPermissions.Should().BeEquivalentTo(new[] { "reports" });
    }
}


