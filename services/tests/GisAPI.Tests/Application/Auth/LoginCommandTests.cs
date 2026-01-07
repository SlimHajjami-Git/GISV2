using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Auth.Commands.Login;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using Moq;

namespace GisAPI.Tests.Application.Auth;

public class LoginCommandTests
{
    [Fact]
    public async Task Handle_ValidCredentials_ShouldReturnTokenAndUser()
    {
        // Arrange
        var context = TestDbContextFactory.Create();
        
        var mockPasswordHasher = new Mock<IPasswordHasher>();
        mockPasswordHasher.Setup(x => x.VerifyPassword("password123", It.IsAny<string>()))
            .Returns(true);

        var mockJwtService = new Mock<IJwtService>();
        mockJwtService.Setup(x => x.GenerateToken(It.IsAny<User>()))
            .Returns("test-jwt-token");
        mockJwtService.Setup(x => x.GenerateRefreshToken())
            .Returns("test-refresh-token");

        var subscription = TestDataBuilder.CreateSubscription();
        var company = TestDataBuilder.CreateCompany(subscriptionId: subscription.Id);
        var user = TestDataBuilder.CreateUser(companyId: company.Id, email: "login@test.com");

        context.Subscriptions.Add(subscription);
        context.Companies.Add(company);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var handler = new LoginCommandHandler(context, mockPasswordHasher.Object, mockJwtService.Object);
        var command = new LoginCommand("login@test.com", "password123");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Token.Should().Be("test-jwt-token");
        result.RefreshToken.Should().Be("test-refresh-token");
        result.User.Email.Should().Be("login@test.com");
        result.User.CompanyId.Should().Be(company.Id);
    }

    [Fact]
    public async Task Handle_UserNotFound_ShouldThrowNotFoundException()
    {
        // Arrange
        var context = TestDbContextFactory.Create();
        var mockPasswordHasher = new Mock<IPasswordHasher>();
        var mockJwtService = new Mock<IJwtService>();

        var handler = new LoginCommandHandler(context, mockPasswordHasher.Object, mockJwtService.Object);
        var command = new LoginCommand("nonexistent@test.com", "password123");

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_InvalidPassword_ShouldThrowDomainException()
    {
        // Arrange
        var context = TestDbContextFactory.Create();

        var mockPasswordHasher = new Mock<IPasswordHasher>();
        mockPasswordHasher.Setup(x => x.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(false);

        var mockJwtService = new Mock<IJwtService>();

        var subscription = TestDataBuilder.CreateSubscription();
        var company = TestDataBuilder.CreateCompany(subscriptionId: subscription.Id);
        var user = TestDataBuilder.CreateUser(companyId: company.Id, email: "wrongpass@test.com");

        context.Subscriptions.Add(subscription);
        context.Companies.Add(company);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var handler = new LoginCommandHandler(context, mockPasswordHasher.Object, mockJwtService.Object);
        var command = new LoginCommand("wrongpass@test.com", "wrongpassword");

        // Act & Assert
        await Assert.ThrowsAsync<DomainException>(() =>
            handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_CaseInsensitiveEmail_ShouldFindUser()
    {
        // Arrange
        var context = TestDbContextFactory.Create();

        var mockPasswordHasher = new Mock<IPasswordHasher>();
        mockPasswordHasher.Setup(x => x.VerifyPassword("password123", It.IsAny<string>()))
            .Returns(true);

        var mockJwtService = new Mock<IJwtService>();
        mockJwtService.Setup(x => x.GenerateToken(It.IsAny<User>()))
            .Returns("token");
        mockJwtService.Setup(x => x.GenerateRefreshToken())
            .Returns("refresh");

        var subscription = TestDataBuilder.CreateSubscription();
        var company = TestDataBuilder.CreateCompany(subscriptionId: subscription.Id);
        var user = TestDataBuilder.CreateUser(companyId: company.Id, email: "case@test.com");

        context.Subscriptions.Add(subscription);
        context.Companies.Add(company);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var handler = new LoginCommandHandler(context, mockPasswordHasher.Object, mockJwtService.Object);
        var command = new LoginCommand("CASE@TEST.COM", "password123"); // Uppercase

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.User.Email.Should().Be("case@test.com");
    }
}

public class LoginCommandValidatorTests
{
    private readonly LoginCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_ShouldPass()
    {
        var command = new LoginCommand("valid@email.com", "password123");
        var result = _validator.Validate(command);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyEmail_ShouldFail()
    {
        var command = new LoginCommand("", "password123");
        var result = _validator.Validate(command);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Fact]
    public void Validate_InvalidEmailFormat_ShouldFail()
    {
        var command = new LoginCommand("not-an-email", "password123");
        var result = _validator.Validate(command);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Fact]
    public void Validate_EmptyPassword_ShouldFail()
    {
        var command = new LoginCommand("valid@email.com", "");
        var result = _validator.Validate(command);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Password");
    }
}
