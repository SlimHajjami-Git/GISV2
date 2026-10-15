using FluentAssertions;
using GisAPI.Application.Features.Suppliers.Commands;
using GisAPI.Infrastructure.Persistence.Configurations;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Suppliers;

/// <summary>
/// Code postal du fournisseur — campagne de tests GPA du 06/10/2026, erreur n°11 : la fiche le
/// proposait, l'API le recevait, mais SupplierConfiguration l'ignorait faute de colonne et la
/// saisie disparaissait. Le test tourne sur le VRAI mapping et relit la base, change tracker vidé
/// (TestGisDbContext seul déduit la propriété par convention et aurait passé sur l'ancien code).
/// </summary>
public class SupplierPostalCodePersistenceTests
{
    private const int CompanyId = 7;

    private sealed class ContexteMappingReel : TestGisDbContext
    {
        public ContexteMappingReel(DbContextOptions<TestGisDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfiguration(new SupplierConfiguration());
        }
    }

    [Fact]
    public async Task Le_code_postal_et_le_type_saisis_sont_enregistres()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys = OFF;";
            cmd.ExecuteNonQuery();
        }
        var options = new DbContextOptionsBuilder<TestGisDbContext>().UseSqlite(connection, contextOwnsConnection: true).Options;
        using var ctx = new ContexteMappingReel(options);
        ctx.Database.EnsureCreated();

        var handler = new CreateSupplierCommandHandler(ctx,
            TestDbContextFactory.CreateMockTenantService(CompanyId).Object, Mock.Of<IPublisher>());

        var id = await handler.Handle(new CreateSupplierCommand(
            "QA Assureur", "insurance", "12 rue de la Paix", "Lyon", "69002", null, "0400000000",
            null, null, null, null, null, null, null, null, true, null), CancellationToken.None);

        ctx.ChangeTracker.Clear();
        var relu = await ctx.Suppliers.AsNoTracking().SingleAsync(s => s.Id == id);
        relu.PostalCode.Should().Be("69002");
        relu.Type.Should().Be("insurance");

        // La liste le renvoyait vide en dur : la fiche se rouvrait sans code postal et
        // l'enregistrement suivant l'effaçait.
        var liste = await new GisAPI.Application.Features.Suppliers.Queries.GetSuppliersQueryHandler(
                ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object)
            .Handle(new GisAPI.Application.Features.Suppliers.Queries.GetSuppliersQuery(), CancellationToken.None);
        liste.Items.Single(s => s.Id == id).PostalCode.Should().Be("69002");
    }
}
