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
/// « Exporter mes données » (DEF-002 de la campagne de test GPA) : le classeur ne
/// contenait que les dépenses de type maintenance — l'assurance de 625 € et
/// l'amende de 120 € de la société de test n'étaient dans aucune feuille. Et
/// réimporter ce même classeur recréait en silence entretiens et pleins.
///
/// <para>Tests de bout en bout sur le contrôleur, avec le VRAI GisDbContext (base
/// en mémoire) : l'export écrit le classeur, l'import le relit.</para>
/// </summary>
public class DataPortExportImportTests
{
    private const int CompanyId = 7;
    private static DateTime Utc(int month, int day) => new(2026, month, day, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// GisDbContext sur une base en mémoire. Les colonnes propres à PostgreSQL
    /// (jsonb en Dictionary, tableaux…) n'ont pas d'équivalent en mémoire et ne
    /// concernent pas le port Excel : elles sont retirées du modèle.
    /// </summary>
    private sealed class ContexteEnMemoire : GisDbContext
    {
        public ContexteEnMemoire(DbContextOptions<GisDbContext> options)
            // Un service de société est indispensable : sans lui, les filtres de
            // requête multi-tenant lèvent une NullReferenceException.
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

    private static GisDbContext Contexte() =>
        new ContexteEnMemoire(new DbContextOptionsBuilder<GisDbContext>()
            // On teste le port Excel, pas les contraintes du schéma.
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), b => b.EnableNullChecks(false))
            .Options);

    private static DataPortController Controleur(GisDbContext ctx) =>
        new(ctx, NullLogger<DataPortController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("companyId", CompanyId.ToString()) }))
                }
            }
        };

    private static void AjouterVehicules(GisDbContext ctx)
    {
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 37, Name = "Service 04", Plate = "GF-305-WQ", CompanyId = CompanyId, Status = "available", Type = "Berline" },
            new Vehicle { Id = 38, Name = "Terrain 01", Plate = "GG-852-BD", CompanyId = CompanyId, Status = "available", Type = "SUV" },
            new Vehicle { Id = 90, Name = "Autre société", Plate = "ZZ-999-ZZ", CompanyId = 8, Status = "available" });
    }

    private static async Task<GisDbContext> ParcAvecDepensesAsync()
    {
        var ctx = Contexte();
        AjouterVehicules(ctx);
        ctx.VehicleCosts.AddRange(
            new VehicleCost { Id = 1, VehicleId = 38, CompanyId = CompanyId, Type = "maintenance", Amount = 350, Description = "Vidange + filtres", Date = Utc(8, 12) },
            new VehicleCost { Id = 2, VehicleId = 37, CompanyId = CompanyId, Type = "maintenance", Amount = 90, Description = null, Date = Utc(8, 20) },
            new VehicleCost { Id = 219, VehicleId = 38, CompanyId = CompanyId, Type = "insurance", Amount = 625, Description = "Renouvellement Assurance - AXA Flotte Entreprise", Date = Utc(9, 1), ReceiptNumber = "POL-2026-38" },
            new VehicleCost { Id = 220, VehicleId = 37, CompanyId = CompanyId, Type = "amende", Amount = 120, Description = "Radar", Date = Utc(9, 3) },
            new VehicleCost { Id = 221, VehicleId = 37, CompanyId = CompanyId, Type = "fuel", Amount = 72, Liters = 40, Mileage = 55_600, Description = "Jerrican", Date = Utc(9, 5) },
            // Autre société : hors de l'export.
            new VehicleCost { Id = 300, VehicleId = 90, CompanyId = 8, Type = "insurance", Amount = 999, Date = Utc(9, 1) });
        ctx.FuelEntries.AddRange(
            new FuelEntry { Id = 1, VehicleId = 38, CompanyId = CompanyId, VehiclePlate = "GG-852-BD", InvoiceDate = Utc(8, 28), Volume = 72, PricePerLiter = 1.8m, TotalAmount = 129.6m, OdometerKm = 170_668 },
            new FuelEntry { Id = 2, VehicleId = 38, CompanyId = CompanyId, VehiclePlate = "GG-852-BD", InvoiceDate = Utc(9, 6), Volume = 72, PricePerLiter = 1.8m, TotalAmount = 129.6m, OdometerKm = 171_820 });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static async Task<byte[]> ExporterAsync(GisDbContext ctx)
    {
        var fichier = (await Controleur(ctx).Export()).Should().BeOfType<FileContentResult>().Subject;
        return fichier.FileContents;
    }

    private static async Task<DataPortController.ImportSummary> ImporterAsync(GisDbContext ctx, byte[] classeur)
    {
        var file = new FormFile(new MemoryStream(classeur), 0, classeur.Length, "file", "calypso-donnees.xlsx");
        var ok = (await Controleur(ctx).Import(file)).Should().BeOfType<OkObjectResult>().Subject;
        return ok.Value.Should().BeOfType<DataPortController.ImportSummary>().Subject;
    }

    private static List<string[]> Lignes(IXLWorksheet feuille) =>
        feuille.RangeUsed()!.RowsUsed().Skip(1)
            .Select(r => Enumerable.Range(1, feuille.LastColumnUsed()!.ColumnNumber())
                .Select(c => r.Cell(c).GetString()).ToArray())
            .ToList();

    [Fact]
    public async Task L_export_contient_toutes_les_depenses_du_parc()
    {
        using var ctx = await ParcAvecDepensesAsync();

        using var wb = new XLWorkbook(new MemoryStream(await ExporterAsync(ctx)));

        wb.Worksheets.Select(w => w.Name).Should().Equal("Véhicules", "Entretiens", "Réparations", "Carburant", "Dépenses");

        Lignes(wb.Worksheet("Entretiens")).Select(l => l[3]).Should().BeEquivalentTo(new[] { "350", "90" },
            "les entretiens restent dans leur feuille");

        var depenses = Lignes(wb.Worksheet("Dépenses"));
        depenses.Should().HaveCount(3, "assurance, amende et dépense carburant — rien d'une autre société");
        var assurance = depenses.Single(l => l[2] == "Assurance");
        assurance.Should().Equal("GG-852-BD", "01/09/2026", "Assurance", "Renouvellement Assurance - AXA Flotte Entreprise", "625", "", "", "POL-2026-38");
        depenses.Single(l => l[2] == "Amende").Should().Equal("GF-305-WQ", "03/09/2026", "Amende", "Radar", "120", "", "", "");
        depenses.Single(l => l[2] == "Carburant").Should().Equal("GF-305-WQ", "05/09/2026", "Carburant", "Jerrican", "72", "55600", "40", "");
    }

    [Fact]
    public async Task Reimporter_le_classeur_exporte_ne_cree_aucun_doublon()
    {
        using var ctx = await ParcAvecDepensesAsync();
        var classeur = await ExporterAsync(ctx);

        var bilan = await ImporterAsync(ctx, classeur);

        bilan.VehiclesCreated.Should().Be(0);
        bilan.MaintenanceCreated.Should().Be(0, "avant : les 2 entretiens étaient recréés en silence");
        bilan.FuelCreated.Should().Be(0, "avant : les 2 pleins étaient recréés en silence");
        bilan.ExpensesCreated.Should().Be(0);
        bilan.MaintenanceIgnored.Should().Be(2);
        bilan.FuelIgnored.Should().Be(2);
        bilan.ExpensesIgnored.Should().Be(3);
        bilan.Message.Should().Contain("ignorée(s)", "les lignes écartées sont annoncées, pas tues");
        bilan.Notes.Should().Contain(n => n.StartsWith("Dépenses : 3 ligne(s) déjà présente(s)"));
        bilan.Notes.Should().Contain(n => n.StartsWith("Entretiens : 2 ligne(s) déjà présente(s)"));
        bilan.Notes.Should().Contain(n => n.StartsWith("Carburant : 2 plein(s) déjà présent(s)"));

        ctx.ChangeTracker.Clear();
        (await ctx.VehicleCosts.CountAsync(c => c.CompanyId == CompanyId)).Should().Be(5);
        (await ctx.FuelEntries.CountAsync(f => f.CompanyId == CompanyId)).Should().Be(2);
    }

    [Fact]
    public async Task Le_classeur_exporte_restaure_les_depenses_dans_un_parc_vide()
    {
        byte[] classeur;
        using (var source = await ParcAvecDepensesAsync())
            classeur = await ExporterAsync(source);

        using var cible = Contexte();
        AjouterVehicules(cible);
        await cible.SaveChangesAsync();

        var bilan = await ImporterAsync(cible, classeur);

        bilan.MaintenanceCreated.Should().Be(2);
        bilan.FuelCreated.Should().Be(2);
        bilan.ExpensesCreated.Should().Be(3);

        cible.ChangeTracker.Clear();
        var depenses = await cible.VehicleCosts.AsNoTracking()
            .Where(c => c.Type != "maintenance").ToListAsync();
        depenses.Select(c => (c.VehicleId, c.Type, c.Amount)).Should().BeEquivalentTo(new[]
            { (38, "insurance", 625m), (37, "amende", 120m), (37, "fuel", 72m) });
        var assurance = depenses.Single(c => c.Type == "insurance");
        assurance.ReceiptNumber.Should().Be("POL-2026-38");
        assurance.Description.Should().Be("Renouvellement Assurance - AXA Flotte Entreprise");
        var carburant = depenses.Single(c => c.Type == "fuel");
        carburant.Liters.Should().Be(40m);
        carburant.Mileage.Should().Be(55_600);

        // Et un second import du même fichier ne recrée rien, entretien sans intitulé compris.
        var second = await ImporterAsync(cible, classeur);
        (second.MaintenanceCreated + second.FuelCreated + second.ExpensesCreated).Should().Be(0);
    }

    [Fact]
    public async Task Le_modele_d_import_a_une_feuille_Depenses_et_importe_tel_quel_ne_cree_aucune_depense()
    {
        // DEF-029 : ce test importait l'EXEMPLE du modèle et attendait l'assurance de
        // 625 € qu'il créait — le défaut lui-même. Une feuille remplie par le client est
        // couverte par DataPortTemplateExampleTests.
        using var ctx = Contexte();
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Camion 1", Plate = "123 TU 4567", CompanyId = CompanyId, Status = "available" });
        await ctx.SaveChangesAsync();

        var modele = (await Controleur(ctx).Template()).Should().BeOfType<FileContentResult>().Subject.FileContents;
        using (var wb = new XLWorkbook(new MemoryStream(modele)))
            wb.Worksheets.Select(w => w.Name).Should().Contain("Dépenses");

        var bilan = await ImporterAsync(ctx, modele);

        bilan.ExpensesCreated.Should().Be(0, "le modèle vierge ne porte aucune dépense à importer");
        ctx.ChangeTracker.Clear();
        (await ctx.VehicleCosts.AsNoTracking().CountAsync()).Should().Be(0);
    }
}
