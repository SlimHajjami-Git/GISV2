using Microsoft.EntityFrameworkCore;
using GisAPI.Infrastructure.Persistence;

namespace GisAPI.Tests;

public static class TestDbContextFactory
{
    public static GisDbContext Create()
    {
        var options = new DbContextOptionsBuilder<GisDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var context = new GisDbContext(options);
        
        // Ensure database is created
        context.Database.EnsureCreated();
        
        return context;
    }
}


