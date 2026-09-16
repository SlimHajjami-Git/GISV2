using FluentAssertions;
using GisAPI.Application.Features.Suppliers.Commands;
using GisAPI.Application.Features.Suppliers.Queries;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GisAPI.Tests.Application.Suppliers;

/// <summary>
/// Recette GPA, DEF-012 : les services d'un fournisseur (table supplier_services)
/// étaient renvoyés vides en dur par la liste comme par le détail.
/// </summary>
public class SupplierServicesReturnedTests
{
    private const int CompanyId = 7;

    private static TestGisDbContext CreerContexte()
    {
        var context = TestDbContextFactory.Create();
        context.Suppliers.AddRange(
            new Supplier { Id = 11, Name = "Garage Renault Lyon Est", Type = "garage", CompanyId = CompanyId },
            new Supplier { Id = 13, Name = "Bosch Car Service Vénissieux", Type = "garage", CompanyId = CompanyId },
            new Supplier { Id = 15, Name = "Oscaro Pièces Auto", Type = "parts", CompanyId = CompanyId });
        context.SupplierServices.AddRange(
            new SupplierService { SupplierId = 11, ServiceCode = "vidange" },
            new SupplierService { SupplierId = 11, ServiceCode = "diagnostic" },
            new SupplierService { SupplierId = 11, ServiceCode = "mecanique" },
            new SupplierService { SupplierId = 13, ServiceCode = "electricite" },
            new SupplierService { SupplierId = 13, ServiceCode = "climatisation" },
            new SupplierService { SupplierId = 13, ServiceCode = "diagnostic" });
        context.SaveChanges();
        context.ChangeTracker.Clear();
        return context;
    }

    [Fact]
    public async Task GetSuppliers_RenvoieLesServicesDeChaqueFournisseur()
    {
        using var context = CreerContexte();
        var handler = new GetSuppliersQueryHandler(context, TestDbContextFactory.CreateMockTenantService(CompanyId).Object);

        var result = await handler.Handle(new GetSuppliersQuery(PageSize: 500), CancellationToken.None);

        result.Items.Single(s => s.Id == 11).Services.Should().Equal("diagnostic", "mecanique", "vidange");
        result.Items.Single(s => s.Id == 13).Services.Should().Equal("climatisation", "diagnostic", "electricite");
        result.Items.Single(s => s.Id == 15).Services.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSupplierById_RenvoieLesServices()
    {
        using var context = CreerContexte();
        var handler = new GetSupplierByIdQueryHandler(context, TestDbContextFactory.CreateMockTenantService(CompanyId).Object);

        var result = await handler.Handle(new GetSupplierByIdQuery(13), CancellationToken.None);

        result!.Services.Should().Equal("climatisation", "diagnostic", "electricite");
    }

    [Fact]
    public async Task GetGarages_RenvoieLesServices()
    {
        using var context = CreerContexte();
        var handler = new GetGaragesQueryHandler(context, TestDbContextFactory.CreateMockTenantService(CompanyId).Object);

        var result = await handler.Handle(new GetGaragesQuery(), CancellationToken.None);

        result.Items.Select(s => s.Id).Should().BeEquivalentTo(new[] { 11, 13 });
        result.Items.Single(s => s.Id == 11).Services.Should().Equal("diagnostic", "mecanique", "vidange");
    }

    [Fact]
    public async Task UpdateSupplier_AvecServices_RemplaceLaListe_SansServices_LaConserve()
    {
        using var context = CreerContexte();
        var handler = new UpdateSupplierCommandHandler(context);

        await handler.Handle(Modification(11, services: new List<string> { "Pneumatique", "vidange", "xyz" }), CancellationToken.None);
        context.ChangeTracker.Clear();
        (await CodesAsync(context, 11)).Should().Equal("pneumatique", "vidange");

        // La note seule (services absents) ne touche pas à la liste.
        await handler.Handle(Modification(11, services: null, rating: 4), CancellationToken.None);
        context.ChangeTracker.Clear();
        (await CodesAsync(context, 11)).Should().Equal("pneumatique", "vidange");
    }

    private static UpdateSupplierCommand Modification(int id, List<string>? services, decimal? rating = null) =>
        new(id, null, null, null, null, null, null, null, null, null, null, null, null, null, rating, null, null, services);

    private static Task<List<string>> CodesAsync(TestGisDbContext context, int supplierId) =>
        context.SupplierServices.AsNoTracking()
            .Where(ss => ss.SupplierId == supplierId)
            .Select(ss => ss.ServiceCode)
            .OrderBy(c => c)
            .ToListAsync();
}
