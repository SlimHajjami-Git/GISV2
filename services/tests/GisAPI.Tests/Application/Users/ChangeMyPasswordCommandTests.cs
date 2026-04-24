using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Users.Commands.ChangeMyPassword;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Users;

/// <summary>
/// Unit tests for ChangeMyPasswordCommand (self-service password change).
/// Covers: validation, tenant isolation, hash verification, idempotency edge cases.
/// </summary>
public class ChangeMyPasswordCommandTests
{
    private const int CompanyId = 1;
    private const int UserId = 42;
    private const string CurrentHash = "hash-of-current";
    private const string NewHash = "hash-of-new";

    private (TestGisDbContext ctx, Mock<GisAPI.Domain.Interfaces.ICurrentTenantService> tenant, Mock<IPasswordHasher> hasher) Setup()
    {
        var ctx = TestDbContextFactory.Create();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: UserId);
        var hasher = new Mock<IPasswordHasher>();

        ctx.Users.Add(new User
        {
            Id = UserId,
            FirstName = "Test",
            LastName = "User",
            Email = "test@calypso.tn",
            CompanyId = CompanyId,
            PasswordHash = CurrentHash,
            Status = "active"
        });
        ctx.SaveChanges();

        return (ctx, tenant, hasher);
    }

    [Fact]
    public async Task ValidCurrentPassword_UpdatesHash_AndStampUpdatedAt()
    {
        var (ctx, tenant, hasher) = Setup();
        hasher.Setup(h => h.VerifyPassword("oldPass123", CurrentHash)).Returns(true);
        hasher.Setup(h => h.HashPassword("newPass456")).Returns(NewHash);

        var handler = new ChangeMyPasswordCommandHandler(ctx, tenant.Object, hasher.Object);
        var before = DateTime.UtcNow.AddSeconds(-1);

        await handler.Handle(new ChangeMyPasswordCommand("oldPass123", "newPass456"), CancellationToken.None);

        var user = await ctx.Users.FindAsync(UserId);
        user!.PasswordHash.Should().Be(NewHash);
        user.UpdatedAt.Should().BeAfter(before);
    }

    [Fact]
    public async Task InvalidCurrentPassword_ThrowsDomainException()
    {
        var (ctx, tenant, hasher) = Setup();
        hasher.Setup(h => h.VerifyPassword("wrong", CurrentHash)).Returns(false);

        var handler = new ChangeMyPasswordCommandHandler(ctx, tenant.Object, hasher.Object);

        var act = () => handler.Handle(new ChangeMyPasswordCommand("wrong", "newPass456"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*incorrect*");
        // Hash must remain unchanged
        var user = await ctx.Users.FindAsync(UserId);
        user!.PasswordHash.Should().Be(CurrentHash);
    }

    [Fact]
    public async Task NewPasswordTooShort_ThrowsDomainException()
    {
        var (ctx, tenant, hasher) = Setup();
        var handler = new ChangeMyPasswordCommandHandler(ctx, tenant.Object, hasher.Object);

        var act = () => handler.Handle(new ChangeMyPasswordCommand("oldPass123", "abc"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*6 caractères*");
        hasher.Verify(h => h.HashPassword(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EmptyCurrentPassword_ThrowsDomainException()
    {
        var (ctx, tenant, hasher) = Setup();
        var handler = new ChangeMyPasswordCommandHandler(ctx, tenant.Object, hasher.Object);

        var act = () => handler.Handle(new ChangeMyPasswordCommand("", "newPass456"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*obligatoire*");
    }

    [Fact]
    public async Task NewPasswordSameAsOld_ThrowsDomainException()
    {
        var (ctx, tenant, hasher) = Setup();
        var handler = new ChangeMyPasswordCommandHandler(ctx, tenant.Object, hasher.Object);

        var act = () => handler.Handle(new ChangeMyPasswordCommand("samePass", "samePass"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*différent*");
    }

    [Fact]
    public async Task UnauthenticatedUser_ThrowsDomainException()
    {
        var (ctx, _, hasher) = Setup();
        var tenant = new Mock<GisAPI.Domain.Interfaces.ICurrentTenantService>();
        tenant.Setup(x => x.UserId).Returns((int?)null);
        tenant.Setup(x => x.CompanyId).Returns(CompanyId);

        var handler = new ChangeMyPasswordCommandHandler(ctx, tenant.Object, hasher.Object);

        var act = () => handler.Handle(new ChangeMyPasswordCommand("x", "newPass456"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*non identifié*");
    }

    [Fact]
    public async Task UserFromOtherCompany_ThrowsNotFound()
    {
        var (ctx, _, hasher) = Setup();
        // Tenant points to a DIFFERENT company (99), but userId 42 belongs to company 1
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: 99, userId: UserId);
        hasher.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        var handler = new ChangeMyPasswordCommandHandler(ctx, tenant.Object, hasher.Object);

        var act = () => handler.Handle(new ChangeMyPasswordCommand("oldPass123", "newPass456"), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
