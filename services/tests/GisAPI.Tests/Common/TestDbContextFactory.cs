using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace GisAPI.Tests.Common;

public static class TestDbContextFactory
{
    public static TestGisDbContext Create(int? tenantId = null)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        // Disable FK enforcement — tests verify business logic, not DB constraints
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys = OFF;";
            cmd.ExecuteNonQuery();
        }

        var options = new DbContextOptionsBuilder<TestGisDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new TestGisDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public static TestGisDbContext CreateWithTenant(int companyId, int userId = 1)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<TestGisDbContext>()
            .UseSqlite(connection)
            .Options;

        var tenantService = new Mock<ICurrentTenantService>();
        tenantService.Setup(x => x.CompanyId).Returns(companyId);
        tenantService.Setup(x => x.UserId).Returns(userId);
        tenantService.Setup(x => x.IsAuthenticated).Returns(true);

        var context = new TestGisDbContext(options, tenantService.Object);
        context.Database.EnsureCreated();
        return context;
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
