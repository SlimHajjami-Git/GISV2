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
        role.RoleType.Should().Be("employee");
        role.SocieteId.Should().BeNull();
        role.IsSystem.Should().BeFalse();
        role.IsDefault.Should().BeFalse();
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
            RoleType = "company_admin",
            SocieteId = 1,
            IsSystem = true,
            IsDefault = false
        };

        // Assert
        role.Id.Should().Be(1);
        role.Name.Should().Be("Admin");
        role.Description.Should().Be("Administrator role with full access");
        role.RoleType.Should().Be("company_admin");
        role.SocieteId.Should().Be(1);
        role.IsSystem.Should().BeTrue();
        role.IsDefault.Should().BeFalse();
    }

    [Fact]
    public void Role_ShouldSupportPermissionsDictionary()
    {
        // Arrange
        var role = new Role
        {
            Name = "Manager",
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
    [InlineData("system_admin")]
    [InlineData("company_admin")]
    [InlineData("employee")]
    [InlineData("custom")]
    public void Role_ShouldAcceptValidRoleTypes(string roleType)
    {
        // Arrange & Act
        var role = new Role { RoleType = roleType };

        // Assert
        role.RoleType.Should().Be(roleType);
    }

    [Fact]
    public void Role_SystemRole_ShouldNotHaveSocieteId()
    {
        // Arrange
        var systemRole = new Role
        {
            Name = "Super Admin",
            RoleType = "system_admin",
            IsSystem = true,
            SocieteId = null
        };

        // Assert
        systemRole.IsSystem.Should().BeTrue();
        systemRole.SocieteId.Should().BeNull();
    }

    [Fact]
    public void Role_CompanyRole_ShouldHaveSocieteId()
    {
        // Arrange
        var companyRole = new Role
        {
            Name = "Fleet Manager",
            RoleType = "employee",
            IsSystem = false,
            SocieteId = 1
        };

        // Assert
        companyRole.IsSystem.Should().BeFalse();
        companyRole.SocieteId.Should().Be(1);
    }
}
