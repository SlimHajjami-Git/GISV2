using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace GisAPI.Tests.Common;

public static class TestDbContextFactory
{
    public static TestGisDbContext Create(int? tenantId = null)
    {
        var serviceProvider = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider();

        var options = new DbContextOptionsBuilder<TestGisDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .UseInternalServiceProvider(serviceProvider)
            .Options;

        var context = new TestGisDbContext(options);
        return context;
    }

    public static TestGisDbContext CreateWithTenant(int companyId, int userId = 1)
    {
        var serviceProvider = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider();

        var options = new DbContextOptionsBuilder<TestGisDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .UseInternalServiceProvider(serviceProvider)
            .Options;

        var tenantService = new Mock<ICurrentTenantService>();
        tenantService.Setup(x => x.CompanyId).Returns(companyId);
        tenantService.Setup(x => x.UserId).Returns(userId);
        tenantService.Setup(x => x.IsAuthenticated).Returns(true);

        return new TestGisDbContext(options, tenantService.Object);
    }

    public static Mock<ICurrentTenantService> CreateMockTenantService(int companyId = 1, int userId = 1)
    {
        var mock = new Mock<ICurrentTenantService>();
        mock.Setup(x => x.CompanyId).Returns(companyId);
        mock.Setup(x => x.UserId).Returns(userId);
        mock.Setup(x => x.UserEmail).Returns("test@test.com");
        mock.Setup(x => x.UserRoles).Returns(new[] { "admin" });
        mock.Setup(x => x.IsAuthenticated).Returns(true);
        return mock;
    }
}


