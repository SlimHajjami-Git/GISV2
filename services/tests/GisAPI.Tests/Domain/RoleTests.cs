using FluentAssertions;
using GisAPI.Domain.Entities;

namespace GisAPI.Tests.Domain;

public class RoleTests
{
    [Fact]
    public void Role_ShouldInitializeWithDefaultValues()
    {
        // Act
        var role = new Role();

        // Assert
        role.Id.Should().Be(0);
        role.Name.Should().BeEmpty();
        role.Description.Should().BeNull();
        role.SocieteId.Should().Be(0);
        role.IsCompanyAdmin.Should().BeFalse();
        role.Users.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Role_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        var role = new Role
        {
            Id = 1,
            Name = "Admin",
            Description = "Administrator role with full access",
            SocieteId = 1,
            IsCompanyAdmin = true
        };

        // Assert
        role.Id.Should().Be(1);
        role.Name.Should().Be("Admin");
        role.Description.Should().Be("Administrator role with full access");
        role.SocieteId.Should().Be(1);
        role.IsCompanyAdmin.Should().BeTrue();
    }

    [Fact]
    public void Role_ShouldSupportPermissionsDictionary()
    {
        // Arrange
        var role = new Role
        {
            Name = "Manager",
            SocieteId = 1,
            Permissions = new Dictionary<string, object>
            {
                { "dashboard", true },
                { "vehicles", new Dictionary<string, object> { { "view", true }, { "edit", true } } },
                { "reports", new Dictionary<string, object> { { "view", true }, { "export", false } } }
            }
        };

        // Assert
        role.Permissions.Should().NotBeNull();
        role.Permissions!.ContainsKey("dashboard").Should().BeTrue();
        role.Permissions["dashboard"].Should().Be(true);
        role.Permissions.ContainsKey("vehicles").Should().BeTrue();
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    [InlineData("Driver")]
    [InlineData("Custom Role")]
    public void Role_ShouldAcceptValidNames(string roleName)
    {
        // Arrange & Act
        var role = new Role { Name = roleName, SocieteId = 1 };

        // Assert
        role.Name.Should().Be(roleName);
    }

    [Fact]
    public void Role_CompanyAdmin_ShouldHaveIsCompanyAdminTrue()
    {
        // Arrange
        var adminRole = new Role
        {
            Name = "Company Admin",
            SocieteId = 1,
            IsCompanyAdmin = true
        };

        // Assert
        adminRole.IsCompanyAdmin.Should().BeTrue();
        adminRole.SocieteId.Should().Be(1);
    }

    [Fact]
    public void Role_Employee_ShouldHaveIsCompanyAdminFalse()
    {
        // Arrange
        var employeeRole = new Role
        {
            Name = "Fleet Manager",
            SocieteId = 1,
            IsCompanyAdmin = false
        };

        // Assert
        employeeRole.IsCompanyAdmin.Should().BeFalse();
        employeeRole.SocieteId.Should().Be(1);
    }
}


