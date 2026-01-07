using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using GisAPI.Domain.Entities;
using GisAPI.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace GisAPI.Tests.Infrastructure.Services;

public class JwtServiceTests
{
    private readonly JwtService _jwtService;
    private readonly IConfiguration _configuration;

    public JwtServiceTests()
    {
        var configValues = new Dictionary<string, string?>
        {
            {"Jwt:Key", "TestSecretKeyForJWTTokenGeneration12345678901234567890!"},
            {"Jwt:Issuer", "TestIssuer"},
            {"Jwt:Audience", "TestAudience"},
            {"Jwt:ExpiryMinutes", "60"}
        };

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        _jwtService = new JwtService(_configuration);
    }

    [Fact]
    public void GenerateToken_ShouldReturnValidJwtToken()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            Name = "Test User",
            Email = "test@test.com",
            CompanyId = 1,
            Roles = new[] { "admin" },
            Permissions = new[] { "dashboard", "vehicles" }
        };

        // Act
        var token = _jwtService.GenerateToken(user);

        // Assert
        token.Should().NotBeNullOrEmpty();
        
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);
        
        jwtToken.Should().NotBeNull();
        jwtToken.Issuer.Should().Be("TestIssuer");
        jwtToken.Audiences.Should().Contain("TestAudience");
    }

    [Fact]
    public void GenerateToken_ShouldContainUserClaims()
    {
        // Arrange
        var user = new User
        {
            Id = 42,
            Name = "John Doe",
            Email = "john@test.com",
            CompanyId = 5,
            Roles = new[] { "admin", "manager" },
            Permissions = new[] { "dashboard", "vehicles", "employees" }
        };

        // Act
        var token = _jwtService.GenerateToken(user);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        jwtToken.Subject.Should().Be("42");
        jwtToken.Claims.Should().Contain(c => c.Type == "email" && c.Value == "john@test.com");
        jwtToken.Claims.Should().Contain(c => c.Type == "name" && c.Value == "John Doe");
        jwtToken.Claims.Should().Contain(c => c.Type == "companyId" && c.Value == "5");
    }

    [Fact]
    public void GenerateToken_ShouldContainRoles()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            Name = "Test",
            Email = "test@test.com",
            CompanyId = 1,
            Roles = new[] { "admin", "manager" },
            Permissions = Array.Empty<string>()
        };

        // Act
        var token = _jwtService.GenerateToken(user);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        var roleClaims = jwtToken.Claims
            .Where(c => c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role")
            .Select(c => c.Value)
            .ToList();

        roleClaims.Should().Contain("admin");
        roleClaims.Should().Contain("manager");
    }

    [Fact]
    public void GenerateToken_ShouldSetExpiry()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            Name = "Test",
            Email = "test@test.com",
            CompanyId = 1,
            Roles = Array.Empty<string>(),
            Permissions = Array.Empty<string>()
        };

        // Act
        var token = _jwtService.GenerateToken(user);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        jwtToken.ValidTo.Should().BeAfter(DateTime.UtcNow);
        jwtToken.ValidTo.Should().BeBefore(DateTime.UtcNow.AddMinutes(65)); // 60 + small buffer
    }

    [Fact]
    public void GenerateRefreshToken_ShouldReturnNonEmptyString()
    {
        // Act
        var refreshToken = _jwtService.GenerateRefreshToken();

        // Assert
        refreshToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateRefreshToken_ShouldReturnUniqueTokens()
    {
        // Act
        var token1 = _jwtService.GenerateRefreshToken();
        var token2 = _jwtService.GenerateRefreshToken();

        // Assert
        token1.Should().NotBe(token2);
    }

    [Fact]
    public void GenerateRefreshToken_ShouldReturnBase64String()
    {
        // Act
        var refreshToken = _jwtService.GenerateRefreshToken();

        // Assert
        var isBase64 = () => Convert.FromBase64String(refreshToken);
        isBase64.Should().NotThrow();
    }
}
