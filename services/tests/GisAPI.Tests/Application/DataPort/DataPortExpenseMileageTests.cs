using System.Security.Claims;
using ClosedXML.Excel;
using FluentAssertions;
using GisAPI.Controllers;
using GisAPI.Domain.Entities;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Application.DataPort;

/// <summary>
/// Feuille « Dépenses » de l'import Excel (DEF-002) : le relevé compteur d'une
/// dépense importée faisait avancer le kilométrage du véhicule, alors que la même
/// dépense saisie à l'écran (CostsController) ne le touche pas. L'import doit se
/// comporter comme l'écran.
/// </summary>
public class DataPortExpenseMileageTests
{
    private const int CompanyId = 7;
    private const int KilometrageFiche = 120_000;

    /// <summary>
    /// GisDbContext en mémoire ; les colonnes propres à PostgreSQL (jsonb, tableaux…)
    /// n'ont pas d'équivalent et ne concernent pas le port Excel.
    /// </summary>
    private sealed class ContexteEnMemoire : GisDbContext
    {
        public ContexteEnMemoire(DbContextOptions<GisDbContext> options)
            : base(options, TestDbContextFactory.CreateMockTenantService(CompanyId).Object) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            foreach (var entity in modelBuilder.Model.GetEntityTypes().ToList())
                foreach (var property in entity.GetDeclaredProperties().ToList())
                    if (!Simple(property.ClrType) && !property.IsKey() && !property.IsForeignKey())
                        modelBuilder.Entity(entity.ClrType).Ignore(property.Name);
        }

        private static bool Simple(Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime)
                || t == typeof(DateTimeOffset) || t == typeof(TimeSpan) || t == typeof(Guid) || t == typeof(byte[])
                || t == typeof(DateOnly) || t == typeof(TimeOnly);
        }
    }

    private static byte[] ClasseurDepenses(int kilometrage)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Dépenses");
        var entetes = new[] { "Matricule", "Date (JJ/MM/AAAA)", "Type", "Description", "Montant", "Kilométrage", "Volume (L)", "N° pièce" };
        for (var i = 0; i < entetes.Length; i++) ws.Cell(1, i + 1).Value = entetes[i];
        ws.Cell(2, 1).Value = "GG-852-BD";
        ws.Cell(2, 2).Value = "01/09/2026";
        ws.Cell(2, 3).Value = "Assurance";
        ws.Cell(2, 4).Value = "Assurance flotte 2026";
        ws.Cell(2, 5).Value = 625;
        ws.Cell(2, 6).Value = kilometrage;
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    [Fact]
    public async Task Le_releve_d_une_depense_importee_ne_fait_pas_avancer_la_fiche_vehicule()
    {
        using var ctx = new ContexteEnMemoire(new DbContextOptionsBuilder<GisDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), b => b.EnableNullChecks(false))
            .Options);
        ctx.Vehicles.Add(new Vehicle { Id = 38, Name = "Terrain 01", Plate = "GG-852-BD", CompanyId = CompanyId, Status = "available", Mileage = KilometrageFiche });
        await ctx.SaveChangesAsync();

        var controleur = new DataPortController(ctx, NullLogger<DataPortController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("companyId", CompanyId.ToString()) }))
                }
            }
        };
        var classeur = ClasseurDepenses(kilometrage: 150_000);
        var file = new FormFile(new MemoryStream(classeur), 0, classeur.Length, "file", "depenses.xlsx");

        var ok = (await controleur.Import(file)).Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<DataPortController.ImportSummary>().Which.ExpensesCreated.Should().Be(1);

        ctx.ChangeTracker.Clear();
        // Le relevé reste sur la dépense (série compteur des rapports)…
        (await ctx.VehicleCosts.AsNoTracking().SingleAsync()).Mileage.Should().Be(150_000);
        // … mais la fiche véhicule n'est pas modifiée, comme pour une saisie à l'écran.
        (await ctx.Vehicles.AsNoTracking().SingleAsync(v => v.Id == 38)).Mileage.Should().Be(KilometrageFiche);
    }
}
