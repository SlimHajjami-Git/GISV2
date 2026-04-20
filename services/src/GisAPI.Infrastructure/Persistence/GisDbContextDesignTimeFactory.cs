using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GisAPI.Infrastructure.Persistence;

/// <summary>
/// Used by `dotnet ef migrations add` and other EF design-time tools.
///
/// GisDbContext has multiple constructors (one with just options, one with
/// options + tenant service + publisher), which makes EF's default
/// design-time context activator ambiguous. This factory picks the
/// options-only constructor explicitly.
///
/// The connection string is irrelevant for migration scaffolding — EF only
/// needs the model metadata, not a working DB — but Npgsql still wants a
/// syntactically valid one, so we pass a harmless placeholder.
/// </summary>
public class GisDbContextDesignTimeFactory : IDesignTimeDbContextFactory<GisDbContext>
{
    public GisDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GisDbContext>()
            .UseNpgsql("Host=localhost;Database=gis_v2_designtime;Username=postgres;Password=postgres")
            .Options;

        return new GisDbContext(options);
    }
}
