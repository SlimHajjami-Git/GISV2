using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.AlertEmails.Commands;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.AlertEmails;

public class TestAlertEmailCommandTests
{
    [Fact]
    public async Task Handle_ExistingEmailInTenant_CallsDispatcherSendTest()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.AlertEmails.Add(new AlertEmail
        {
            Id = 5,
            Email = "me@example.com",
            AlertType = "assurance",
            CompanyId = 1
        });
        await context.SaveChangesAsync();

        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        var dispatcher = new Mock<IAlertEmailDispatcher>();
        var handler = new TestAlertEmailCommandHandler(context, tenant.Object, dispatcher.Object);

        // Act
        await handler.Handle(new TestAlertEmailCommand(5), CancellationToken.None);

        // Assert
        dispatcher.Verify(x => x.SendTestAsync(5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_EmailInDifferentTenant_ThrowsNotFound()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.AlertEmails.Add(new AlertEmail
        {
            Id = 7,
            Email = "other@example.com",
            AlertType = "entretien",
            CompanyId = 2   // belongs to company 2
        });
        await context.SaveChangesAsync();

        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: 1);  // caller is company 1
        var dispatcher = new Mock<IAlertEmailDispatcher>();
        var handler = new TestAlertEmailCommandHandler(context, tenant.Object, dispatcher.Object);

        // Act
        var act = async () => await handler.Handle(new TestAlertEmailCommand(7), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
        dispatcher.Verify(
            x => x.SendTestAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownId_ThrowsNotFound()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        var dispatcher = new Mock<IAlertEmailDispatcher>();
        var handler = new TestAlertEmailCommandHandler(context, tenant.Object, dispatcher.Object);

        // Act
        var act = async () => await handler.Handle(new TestAlertEmailCommand(999), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
        dispatcher.Verify(
            x => x.SendTestAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NoTenantContext_ThrowsDomainException()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var tenant = new Mock<ICurrentTenantService>();
        tenant.Setup(x => x.CompanyId).Returns((int?)null);
        var dispatcher = new Mock<IAlertEmailDispatcher>();
        var handler = new TestAlertEmailCommandHandler(context, tenant.Object, dispatcher.Object);

        // Act
        var act = async () => await handler.Handle(new TestAlertEmailCommand(1), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<DomainException>();
    }
}
