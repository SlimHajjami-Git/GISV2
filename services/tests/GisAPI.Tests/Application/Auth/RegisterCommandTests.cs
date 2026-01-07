using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Auth.Commands.Register;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace GisAPI.Tests.Application.Auth;

public class RegisterCommandTests
{
    [Fact]
    public async Task Handle_ValidCommand_ShouldCreateUserAndCompany()
    {
        // Arrange
        var context = TestDbContextFactory.Create();

        var mockPasswordHasher = new Mock<IPasswordHasher>();
        mockPasswordHasher.Setup(x => x.HashPassword(It.IsAny<string>()))
            .Returns("hashed-password");

        var mockJwtService = new Mock<IJwtService>();
        mockJwtService.Setup(x => x.GenerateToken(It.IsAny<User>()))
            .Returns("test-jwt-token");
        mockJwtService.Setup(x => x.GenerateRefreshToken())
            .Returns("test-refresh-token");

        var handler = new RegisterCommandHandler(context, mockPasswordHasher.Object, mockJwtService.Object);
        var command = new RegisterCommand(
            Name: "New User",
            Email: "newuser@test.com",
            Password: "password123",
            CompanyName: "New Company",
            Phone: "+212600000000"
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Token.Should().Be("test-jwt-token");
        result.User.Name.Should().Be("New User");
        result.User.Email.Should().Be("newuser@test.com");
        result.User.CompanyName.Should().Be("New Company");

        // Verify company was created
        var company = await context.Companies.FirstOrDefaultAsync(c => c.Name == "New Company");
        company.Should().NotBeNull();

        // Verify user was created
        var user = await context.Users.FirstOrDefaultAsync(u => u.Email == "newuser@test.com");
        user.Should().NotBeNull();
        user!.Roles.Should().Contain("admin");
    }

    [Fact]
    public async Task Handle_DuplicateEmail_ShouldThrowConflictException()
    {
        // Arrange
        var context = TestDbContextFactory.Create();

        var mockPasswordHasher = new Mock<IPasswordHasher>();
        var mockJwtService = new Mock<IJwtService>();

        var subscription = TestDataBuilder.CreateSubscription();
        var company = TestDataBuilder.CreateCompany(subscriptionId: subscription.Id);
        var existingUser = TestDataBuilder.CreateUser(companyId: company.Id, email: "existing@test.com");

        context.Subscriptions.Add(subscription);
        context.Companies.Add(company);
        context.Users.Add(existingUser);
        await context.SaveChangesAsync();

        var handler = new RegisterCommandHandler(context, mockPasswordHasher.Object, mockJwtService.Object);
        var command = new RegisterCommand(
            Name: "New User",
            Email: "existing@test.com", // Same email
            Password: "password123",
            CompanyName: "Another Company",
            Phone: null
        );

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ShouldCreateDefaultSubscriptionIfNotExists()
    {
        // Arrange
        var context = TestDbContextFactory.Create();

        var mockPasswordHasher = new Mock<IPasswordHasher>();
        mockPasswordHasher.Setup(x => x.HashPassword(It.IsAny<string>()))
            .Returns("hashed");

        var mockJwtService = new Mock<IJwtService>();
        mockJwtService.Setup(x => x.GenerateToken(It.IsAny<User>())).Returns("token");
        mockJwtService.Setup(x => x.GenerateRefreshToken()).Returns("refresh");

        // No subscriptions exist initially
        var subscriptionCount = await context.Subscriptions.CountAsync();
        subscriptionCount.Should().Be(0);

        var handler = new RegisterCommandHandler(context, mockPasswordHasher.Object, mockJwtService.Object);
        var command = new RegisterCommand(
            Name: "First User",
            Email: "first@test.com",
            Password: "password123",
            CompanyName: "First Company",
            Phone: null
        );

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        var subscription = await context.Subscriptions.FirstOrDefaultAsync();
        subscription.Should().NotBeNull();
        subscription!.Type.Should().Be("parc");
    }
}

public class RegisterCommandValidatorTests
{
    private readonly RegisterCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_ShouldPass()
    {
        var command = new RegisterCommand(
            Name: "Test User",
            Email: "test@test.com",
            Password: "password123",
            CompanyName: "Test Company",
            Phone: null
        );
        var result = _validator.Validate(command);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyName_ShouldFail()
    {
        var command = new RegisterCommand("", "test@test.com", "password123", "Company", null);
        var result = _validator.Validate(command);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_InvalidEmail_ShouldFail()
    {
        var command = new RegisterCommand("User", "invalid-email", "password123", "Company", null);
        var result = _validator.Validate(command);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Fact]
    public void Validate_ShortPassword_ShouldFail()
    {
        var command = new RegisterCommand("User", "test@test.com", "123", "Company", null);
        var result = _validator.Validate(command);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Password");
    }

    [Fact]
    public void Validate_EmptyCompanyName_ShouldFail()
    {
        var command = new RegisterCommand("User", "test@test.com", "password123", "", null);
        var result = _validator.Validate(command);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "CompanyName");
    }
}
