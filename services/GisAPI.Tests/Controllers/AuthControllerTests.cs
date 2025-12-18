using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using GisAPI.Controllers;
using GisAPI.Data;
using GisAPI.Models;
using GisAPI.DTOs;

namespace GisAPI.Tests.Controllers;

public class AuthControllerTests
{
    private GisDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GisDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new GisDbContext(options);
    }

    private IConfiguration CreateConfiguration()
    {
        var config = new Dictionary<string, string?>
        {
            { "Jwt:Key", "TestSecretKeyForJWTTokenGeneration123!" },
            { "Jwt:Issuer", "GisAPI" },
            { "Jwt:Audience", "GisAPI" }
        };
        return new ConfigurationBuilder()
            .AddInMemoryCollection(config)
            .Build();
    }

    [Fact]
    public async Task Register_CreatesUserAndCompany()
    {
        // Arrange
        using var context = CreateContext();
        var config = CreateConfiguration();
        var controller = new AuthController(context, config);
        
        var request = new RegisterRequest(
            Name: "Test User",
            Email: "test@example.com",
            Password: "Test123!",
            CompanyName: "Test Company",
            Phone: "+212600000000"
        );

        // Act
        var result = await controller.Register(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AuthResponse>(okResult.Value);
        Assert.NotEmpty(response.Token);
        Assert.Equal("Test User", response.User.Name);
        Assert.Equal("test@example.com", response.User.Email);
        Assert.Equal("Test Company", response.User.CompanyName);
    }

    [Fact]
    public async Task Register_ReturnsBadRequest_WhenEmailExists()
    {
        // Arrange
        using var context = CreateContext();
        context.Users.Add(new User
        {
            Id = 1,
            Name = "Existing User",
            Email = "test@example.com",
            PasswordHash = "hash",
            CompanyId = 1,
            Status = "active",
            Roles = new[] { "admin" },
            Permissions = new[] { "all" }
        });
        await context.SaveChangesAsync();
        
        var config = CreateConfiguration();
        var controller = new AuthController(context, config);
        
        var request = new RegisterRequest(
            Name: "New User",
            Email: "test@example.com",
            Password: "Test123!",
            CompanyName: "New Company",
            Phone: null
        );

        // Act
        var result = await controller.Register(request);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Login_ReturnsToken_WhenCredentialsValid()
    {
        // Arrange
        using var context = CreateContext();
        var hashedPassword = BCrypt.Net.BCrypt.HashPassword("Test123!");
        context.Companies.Add(new Company
        {
            Id = 1,
            Name = "Test Company",
            Type = "transport",
            SubscriptionId = 1
        });
        context.Users.Add(new User
        {
            Id = 1,
            Name = "Test User",
            Email = "test@example.com",
            PasswordHash = hashedPassword,
            CompanyId = 1,
            Status = "active",
            Roles = new[] { "admin" },
            Permissions = new[] { "all" }
        });
        await context.SaveChangesAsync();
        
        var config = CreateConfiguration();
        var controller = new AuthController(context, config);
        
        var request = new LoginRequest("test@example.com", "Test123!");

        // Act
        var result = await controller.Login(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AuthResponse>(okResult.Value);
        Assert.NotEmpty(response.Token);
        Assert.Equal("Test User", response.User.Name);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_WhenPasswordWrong()
    {
        // Arrange
        using var context = CreateContext();
        var hashedPassword = BCrypt.Net.BCrypt.HashPassword("CorrectPassword");
        context.Users.Add(new User
        {
            Id = 1,
            Name = "Test User",
            Email = "test@example.com",
            PasswordHash = hashedPassword,
            CompanyId = 1,
            Status = "active",
            Roles = new[] { "admin" },
            Permissions = new[] { "all" }
        });
        await context.SaveChangesAsync();
        
        var config = CreateConfiguration();
        var controller = new AuthController(context, config);
        
        var request = new LoginRequest("test@example.com", "WrongPassword");

        // Act
        var result = await controller.Login(request);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_WhenUserNotFound()
    {
        // Arrange
        using var context = CreateContext();
        var config = CreateConfiguration();
        var controller = new AuthController(context, config);
        
        var request = new LoginRequest("nonexistent@example.com", "Test123!");

        // Act
        var result = await controller.Login(request);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_WhenUserInactive()
    {
        // Arrange
        using var context = CreateContext();
        var hashedPassword = BCrypt.Net.BCrypt.HashPassword("Test123!");
        context.Users.Add(new User
        {
            Id = 1,
            Name = "Inactive User",
            Email = "inactive@example.com",
            PasswordHash = hashedPassword,
            CompanyId = 1,
            Status = "inactive", // Inactive user
            Roles = new[] { "admin" },
            Permissions = new[] { "all" }
        });
        await context.SaveChangesAsync();
        
        var config = CreateConfiguration();
        var controller = new AuthController(context, config);
        
        var request = new LoginRequest("inactive@example.com", "Test123!");

        // Act
        var result = await controller.Login(request);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }
}
