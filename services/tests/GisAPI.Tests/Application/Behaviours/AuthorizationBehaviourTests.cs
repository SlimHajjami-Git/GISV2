using FluentAssertions;
using GisAPI.Application.Common.Behaviours;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Constants;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Moq;

namespace GisAPI.Tests.Application.Behaviours;

public class AuthorizationBehaviourTests
{
    [Fact]
    public async Task Handle_RequestWithoutPermissions_ShouldPassThrough()
    {
        var tenantService = new Mock<ICurrentTenantService>();
        var behaviour = new AuthorizationBehaviour<NonRestrictedCommand, Unit>(tenantService.Object);
        var nextExecuted = false;

        RequestHandlerDelegate<Unit> next = () =>
        {
            nextExecuted = true;
            return Task.FromResult(Unit.Value);
        };

        await behaviour.Handle(new NonRestrictedCommand(), next, CancellationToken.None);

        nextExecuted.Should().BeTrue();
        tenantService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ShouldThrowForbidden()
    {
        var tenantService = new Mock<ICurrentTenantService>();
        tenantService.SetupGet(x => x.IsAuthenticated).Returns(false);
        var behaviour = new AuthorizationBehaviour<RequiresVehiclesPermissionCommand, Unit>(tenantService.Object);

        Func<Task> act = async () =>
            await behaviour.Handle(new RequiresVehiclesPermissionCommand(), () => Task.FromResult(Unit.Value), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenAccessException>();
    }

    [Fact]
    public async Task Handle_MissingPermission_ShouldThrowForbidden()
    {
        var tenantService = new Mock<ICurrentTenantService>();
        tenantService.SetupGet(x => x.IsAuthenticated).Returns(true);
        tenantService.Setup(x => x.HasPermission(Permissions.Vehicles)).Returns(false);
        var behaviour = new AuthorizationBehaviour<RequiresVehiclesPermissionCommand, Unit>(tenantService.Object);

        Func<Task> act = async () =>
            await behaviour.Handle(new RequiresVehiclesPermissionCommand(), () => Task.FromResult(Unit.Value), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenAccessException>();
    }

    [Fact]
    public async Task Handle_PermissionGranted_ShouldInvokeNext()
    {
        var tenantService = new Mock<ICurrentTenantService>();
        tenantService.SetupGet(x => x.IsAuthenticated).Returns(true);
        tenantService.Setup(x => x.HasPermission(Permissions.Vehicles)).Returns(true);
        var behaviour = new AuthorizationBehaviour<RequiresVehiclesPermissionCommand, Unit>(tenantService.Object);
        var nextExecuted = false;

        RequestHandlerDelegate<Unit> next = () =>
        {
            nextExecuted = true;
            return Task.FromResult(Unit.Value);
        };

        await behaviour.Handle(new RequiresVehiclesPermissionCommand(), next, CancellationToken.None);

        nextExecuted.Should().BeTrue();
    }

    private sealed record NonRestrictedCommand : IRequest<Unit>;

    private sealed record RequiresVehiclesPermissionCommand : IRequest<Unit>, IRequiresPermissions
    {
        public IReadOnlyCollection<string> RequiredPermissions { get; } = new[] { Permissions.Vehicles };
    }
}
