using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Services;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.AlertEmails;

public class AlertEmailDispatcherTests
{
    private static void SeedAdminUser(TestGisDbContext context, int companyId, string email)
    {
        var role = new Role
        {
            Id = 10 + companyId,
            Name = $"Admin-{companyId}",
            IsCompanyAdmin = true,
            SocieteId = companyId
        };
        context.Roles.Add(role);

        context.Users.Add(new User
        {
            Id = 100 + companyId,
            FirstName = "Admin",
            LastName = $"Company{companyId}",
            Email = email,
            PasswordHash = "x",
            RoleId = role.Id,
            CompanyId = companyId,
            Status = "active"
        });

        context.SaveChanges();
    }

    [Fact]
    public async Task DispatchAsync_SendsToAllConfiguredRecipients()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.AlertEmails.AddRange(
            new AlertEmail { Email = "a@example.com", AlertType = "assurance", CompanyId = 1 },
            new AlertEmail { Email = "b@example.com", AlertType = "assurance", CompanyId = 1 },
            new AlertEmail { Email = "c@example.com", AlertType = "entretien", CompanyId = 1 }  // wrong type
        );
        await context.SaveChangesAsync();

        var emailService = new Mock<IEmailService>();
        var dispatcher = new AlertEmailDispatcher(context, emailService.Object, NullLogger<AlertEmailDispatcher>.Instance);

        // Act
        var count = await dispatcher.DispatchAsync(1, "assurance", "Titre", "Message");

        // Assert: only "assurance" recipients should get the email
        count.Should().Be(2);
        emailService.Verify(
            x => x.SendNotificationEmailAsync("a@example.com", It.IsAny<string>(), "assurance",
                "Titre", "Message", It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        emailService.Verify(
            x => x.SendNotificationEmailAsync("b@example.com", It.IsAny<string>(), "assurance",
                "Titre", "Message", It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        emailService.Verify(
            x => x.SendNotificationEmailAsync("c@example.com", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DispatchAsync_FiltersByCompany()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.AlertEmails.AddRange(
            new AlertEmail { Email = "tenant1@example.com", AlertType = "assurance", CompanyId = 1 },
            new AlertEmail { Email = "tenant2@example.com", AlertType = "assurance", CompanyId = 2 }
        );
        await context.SaveChangesAsync();

        var emailService = new Mock<IEmailService>();
        var dispatcher = new AlertEmailDispatcher(context, emailService.Object, NullLogger<AlertEmailDispatcher>.Instance);

        // Act
        var count = await dispatcher.DispatchAsync(2, "assurance", "T", "M");

        // Assert: only company 2's recipient should be emailed
        count.Should().Be(1);
        emailService.Verify(
            x => x.SendNotificationEmailAsync("tenant2@example.com", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        emailService.Verify(
            x => x.SendNotificationEmailAsync("tenant1@example.com", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DispatchAsync_NoRecipientsConfigured_FallsBackToCompanyAdmin()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        SeedAdminUser(context, companyId: 1, email: "admin@company1.com");

        var emailService = new Mock<IEmailService>();
        var dispatcher = new AlertEmailDispatcher(context, emailService.Object, NullLogger<AlertEmailDispatcher>.Instance);

        // Act
        var count = await dispatcher.DispatchAsync(1, "assurance", "T", "M");

        // Assert: admin fallback was used
        count.Should().Be(1);
        emailService.Verify(
            x => x.SendNotificationEmailAsync("admin@company1.com", It.IsAny<string>(), "assurance",
                "T", "M", It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_NoRecipientsAndNoAdmin_ReturnsZeroWithoutThrowing()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var emailService = new Mock<IEmailService>();
        var dispatcher = new AlertEmailDispatcher(context, emailService.Object, NullLogger<AlertEmailDispatcher>.Instance);

        // Act
        var count = await dispatcher.DispatchAsync(99, "assurance", "T", "M");

        // Assert: nothing sent, nothing thrown
        count.Should().Be(0);
        emailService.Verify(
            x => x.SendNotificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DispatchAsync_SendFailure_IsolatesErrorAndContinues()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.AlertEmails.AddRange(
            new AlertEmail { Email = "good1@example.com", AlertType = "entretien", CompanyId = 1 },
            new AlertEmail { Email = "broken@example.com", AlertType = "entretien", CompanyId = 1 },
            new AlertEmail { Email = "good2@example.com", AlertType = "entretien", CompanyId = 1 }
        );
        await context.SaveChangesAsync();

        var emailService = new Mock<IEmailService>();
        emailService
            .Setup(x => x.SendNotificationEmailAsync("broken@example.com", It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP down for this address"));

        var dispatcher = new AlertEmailDispatcher(context, emailService.Object, NullLogger<AlertEmailDispatcher>.Instance);

        // Act — must NOT throw even though one recipient fails
        var count = await dispatcher.DispatchAsync(1, "entretien", "T", "M");

        // Assert: dispatcher reports 3 recipients attempted
        count.Should().Be(3);
        // And both good addresses were attempted despite broken one
        emailService.Verify(
            x => x.SendNotificationEmailAsync("good1@example.com", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        emailService.Verify(
            x => x.SendNotificationEmailAsync("good2@example.com", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_DeduplicatesIdenticalAddresses()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.AlertEmails.AddRange(
            new AlertEmail { Email = "dup@example.com", AlertType = "visite_technique", CompanyId = 1 },
            new AlertEmail { Email = "DUP@example.com", AlertType = "visite_technique", CompanyId = 1 },
            new AlertEmail { Email = "  dup@example.com ", AlertType = "visite_technique", CompanyId = 1 }
        );
        await context.SaveChangesAsync();

        var emailService = new Mock<IEmailService>();
        var dispatcher = new AlertEmailDispatcher(context, emailService.Object, NullLogger<AlertEmailDispatcher>.Instance);

        // Act
        var count = await dispatcher.DispatchAsync(1, "visite_technique", "T", "M");

        // Assert: exactly one call (dedup is case-insensitive and trims)
        count.Should().Be(1);
        emailService.Verify(
            x => x.SendNotificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendTestAsync_SendsExactlyOneEmailToTheRecipient()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var entry = new AlertEmail
        {
            Id = 42,
            Email = "test@example.com",
            AlertType = "assurance",
            CompanyId = 1
        };
        context.AlertEmails.Add(entry);
        await context.SaveChangesAsync();

        var emailService = new Mock<IEmailService>();
        var dispatcher = new AlertEmailDispatcher(context, emailService.Object, NullLogger<AlertEmailDispatcher>.Instance);

        // Act
        await dispatcher.SendTestAsync(42);

        // Assert
        emailService.Verify(
            x => x.SendNotificationEmailAsync(
                "test@example.com",
                It.IsAny<string>(),
                "assurance",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendTestAsync_UnknownId_Throws()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var emailService = new Mock<IEmailService>();
        var dispatcher = new AlertEmailDispatcher(context, emailService.Object, NullLogger<AlertEmailDispatcher>.Instance);

        // Act
        var act = async () => await dispatcher.SendTestAsync(999);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
