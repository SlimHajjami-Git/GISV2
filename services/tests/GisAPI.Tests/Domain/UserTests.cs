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
        user.FullName.Should().BeEmpty();
        user.Email.Should().BeEmpty();
        user.Status.Should().Be("active");
    }

    [Fact]
    public void User_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        var role = new Role { Id = 1, Name = "Driver", SocieteId = 1 };
        var user = new User
        {
            Id = 1,
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            Phone = "+216 12 345 678",
            PasswordHash = "$2a$11$...",
            CompanyId = 1,
            Status = "active",
            RoleId = 1,
            Role = role
        };

        // Assert
        user.Id.Should().Be(1);
        user.FullName.Should().Be("John Doe");
        user.Email.Should().Be("john@example.com");
        user.Phone.Should().Be("+216 12 345 678");
        user.CompanyId.Should().Be(1);
        user.Status.Should().Be("active");
        user.RoleId.Should().Be(1);
    }

    [Fact]
    public void User_Employee_ShouldHaveRoleId()
    {
        // Arrange
        var employee = new User
        {
            FirstName = "Driver",
            LastName = "One",
            Email = "driver@company.com",
            RoleId = 3
        };

        // Assert
        employee.FullName.Should().Be("Driver One");
        employee.RoleId.Should().Be(3);
    }

    [Fact]
    public void User_Admin_ShouldHaveAdminRole()
    {
        // Arrange
        var adminRole = new Role { Id = 1, Name = "Admin", SocieteId = 1, IsCompanyAdmin = true };
        var admin = new User
        {
            FirstName = "Admin",
            LastName = "User",
            Email = "admin@company.com",
            RoleId = 1,
            Role = adminRole
        };

        // Assert
        admin.IsCompanyAdmin.Should().BeTrue();
        admin.Role.Name.Should().Be("Admin");
    }

    [Fact]
    public void User_ShouldHaveSocieteNavigation()
    {
        // Arrange
        var societe = new Societe { Id = 1, Name = "Test Company" };
        var user = new User
        {
            Id = 1,
            FirstName = "Test",
            LastName = "User",
            CompanyId = 1,
            RoleId = 1,
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
        var role = new Role { Id = 1, Name = "Driver", SocieteId = 1 };
        var user = new User
        {
            Id = 1,
            FirstName = "Test",
            LastName = "User",
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

    [Fact]
    public void User_ShouldTrackLastLogin()
    {
        // Arrange
        var loginTime = DateTime.UtcNow;
        var user = new User
        {
            FirstName = "Test",
            LastName = "User",
            LastLoginAt = loginTime
        };

        // Assert
        user.LastLoginAt.Should().Be(loginTime);
    }

    [Fact]
    public void User_FullName_ShouldCombineFirstAndLastName()
    {
        // Arrange
        var user = new User
        {
            FirstName = "John",
            LastName = "Smith"
        };

        // Assert
        user.FullName.Should().Be("John Smith");
    }

    [Fact]
    public void User_Name_ShouldSplitIntoFirstAndLastName()
    {
        // Arrange
        var user = new User();
        user.Name = "Jane Doe";

        // Assert
        user.FirstName.Should().Be("Jane");
        user.LastName.Should().Be("Doe");
        user.FullName.Should().Be("Jane Doe");
    }
}


