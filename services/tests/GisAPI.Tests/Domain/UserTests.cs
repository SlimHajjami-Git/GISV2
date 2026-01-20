using FluentAssertions;
using GisAPI.Domain.Entities;

namespace GisAPI.Tests.Domain;

public class UserTests
{
    [Fact]
    public void User_ShouldInitializeWithDefaultValues()
    {
        // Act
        var user = new User();

        // Assert
        user.Id.Should().Be(0);
        user.Name.Should().BeEmpty();
        user.Email.Should().BeEmpty();
        user.Status.Should().Be("active");
        user.UserType.Should().Be("user");
        user.IsCompanyAdmin.Should().BeFalse();
        user.Roles.Should().BeEmpty();
        user.Permissions.Should().BeEmpty();
        user.AssignedVehicleIds.Should().BeEmpty();
    }

    [Fact]
    public void User_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            Name = "John Doe",
            Email = "john@example.com",
            Phone = "+216 12 345 678",
            PasswordHash = "$2a$11$...",
            CompanyId = 1,
            Status = "active",
            UserType = "employee",
            IsCompanyAdmin = false,
            Roles = new[] { "driver" },
            Permissions = new[] { "dashboard", "monitoring" }
        };

        // Assert
        user.Id.Should().Be(1);
        user.Name.Should().Be("John Doe");
        user.Email.Should().Be("john@example.com");
        user.Phone.Should().Be("+216 12 345 678");
        user.CompanyId.Should().Be(1);
        user.Status.Should().Be("active");
        user.UserType.Should().Be("employee");
        user.IsCompanyAdmin.Should().BeFalse();
        user.Roles.Should().Contain("driver");
        user.Permissions.Should().Contain("dashboard");
    }

    [Fact]
    public void User_Employee_ShouldHaveEmployeeFields()
    {
        // Arrange
        var employee = new User
        {
            Name = "Driver One",
            Email = "driver@company.com",
            UserType = "employee",
            HireDate = new DateTime(2024, 1, 15),
            LicenseNumber = "DL123456",
            LicenseExpiry = new DateTime(2028, 1, 15),
            RoleId = 3
        };

        // Assert
        employee.UserType.Should().Be("employee");
        employee.HireDate.Should().Be(new DateTime(2024, 1, 15));
        employee.LicenseNumber.Should().Be("DL123456");
        employee.LicenseExpiry.Should().Be(new DateTime(2028, 1, 15));
        employee.RoleId.Should().Be(3);
    }

    [Fact]
    public void User_Admin_ShouldHaveAdminRole()
    {
        // Arrange
        var admin = new User
        {
            Name = "Admin User",
            Email = "admin@company.com",
            IsCompanyAdmin = true,
            Roles = new[] { "admin" },
            Permissions = new[] { "all" }
        };

        // Assert
        admin.IsCompanyAdmin.Should().BeTrue();
        admin.Roles.Should().Contain("admin");
        admin.Permissions.Should().Contain("all");
    }

    [Fact]
    public void User_ShouldHaveSocieteNavigation()
    {
        // Arrange
        var societe = new Societe { Id = 1, Name = "Test Company" };
        var user = new User
        {
            Id = 1,
            Name = "Test User",
            CompanyId = 1,
            Societe = societe
        };

        // Assert
        user.Societe.Should().NotBeNull();
        user.Societe!.Id.Should().Be(1);
        user.Societe.Name.Should().Be("Test Company");
    }

    [Fact]
    public void User_ShouldHaveRoleNavigation()
    {
        // Arrange
        var role = new Role { Id = 1, Name = "Driver", RoleType = "employee" };
        var user = new User
        {
            Id = 1,
            Name = "Test User",
            RoleId = 1,
            Role = role
        };

        // Assert
        user.Role.Should().NotBeNull();
        user.Role!.Id.Should().Be(1);
        user.Role.Name.Should().Be("Driver");
    }

    [Theory]
    [InlineData("active")]
    [InlineData("inactive")]
    [InlineData("suspended")]
    public void User_ShouldAcceptValidStatuses(string status)
    {
        // Arrange & Act
        var user = new User { Status = status };

        // Assert
        user.Status.Should().Be(status);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("employee")]
    [InlineData("admin")]
    public void User_ShouldAcceptValidUserTypes(string userType)
    {
        // Arrange & Act
        var user = new User { UserType = userType };

        // Assert
        user.UserType.Should().Be(userType);
    }

    [Fact]
    public void User_ShouldTrackLastLogin()
    {
        // Arrange
        var loginTime = DateTime.UtcNow;
        var user = new User
        {
            Name = "Test User",
            LastLoginAt = loginTime
        };

        // Assert
        user.LastLoginAt.Should().Be(loginTime);
    }
}
