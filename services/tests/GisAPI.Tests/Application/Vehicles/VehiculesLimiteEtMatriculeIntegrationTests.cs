using System.Security.Claims;
using ClosedXML.Excel;
using FluentAssertions;
using GisAPI.Application.Features.Admin.Vehicles.Commands.CreateAdminVehicle;
using GisAPI.Application.Features.Admin.Vehicles.Commands.UpdateAdminVehicle;
using GisAPI.Controllers;
using GisAPI.Domain.Entities;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Application.Vehicles;

/// <summary>
/// Tour d'intégration de la campagne de test Calypso GPA : les règles de la fiche
/// véhicule posées pour l'espace client s'appliquent aussi aux autres écrivains.
/// <list type="bullet">
///   <item>DEF-036 — l'import Excel créait des véhicules au-delà de max_vehicles ;
///     la création unitaire les refusait déjà.</item>
///   <item>DEF-037 — l'administration système créait ou renommait un véhicule vers
///     un matricule déjà porté dans la société, ou déplaçait un véhicule vers une
///     société où son matricule existait.</item>
/// </list>
/// </summary>
public class VehiculesLimiteEtMatriculeIntegrationTests
{
    private const int CompanyId = 7;
    private const int OtherCompanyId = 5;

    // ── DEF-036 : import Excel ────────────────────────────────────────────────

    private sealed class ContexteEnMemoire : GisDbContext
    {
        public ContexteEnMemoire(DbContextOptions<GisDbContext> options)
            : base(options, TestDbContextFactory.CreateMockTenantService(CompanyId).Object) { }

        // Colonnes propres à PostgreSQL (jsonb, tableaux…) sans équivalent en mémoire.
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

    private static async Task<GisDbContext> ParcAsync(int maxVehicles, bool prixParVehicule)
    {
        var ctx = new ContexteEnMemoire(new DbContextOptionsBuilder<GisDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), b => b.EnableNullChecks(false))
            .Options);
        ctx.SubscriptionTypes.Add(new SubscriptionType
        {
            Id = 10, Name = "Plan Basique", Code = "plan-basique", MaxVehicles = maxVehicles, PricePerVehicle = prixParVehicule
        });
        ctx.Societes.Add(new Societe { Id = CompanyId, Name = "Belive GPA", SubscriptionTypeId = 10 });
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId, Status = "available", Mileage = 1_000 },
            new Vehicle { Id = 2, Name = "Service 02", Plate = "GB-100-AA", CompanyId = CompanyId, Status = "available" },
            // Une autre société ne consomme pas la limite.
            new Vehicle { Id = 90, Name = "Autre", Plate = "ZZ-999-ZZ", CompanyId = OtherCompanyId, Status = "available" });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static byte[] Classeur()
    {
        using var wb = new XLWorkbook();
        var vehicules = wb.Worksheets.Add("Véhicules");
        var entetes = new[] { "Matricule", "Nom", "Marque", "Modèle", "Année", "Type", "Carburant", "Kilométrage", "Capacité réservoir (L)" };
        for (var i = 0; i < entetes.Length; i++) vehicules.Cell(1, i + 1).Value = entetes[i];
        vehicules.Cell(2, 1).Value = "QA-IMP-1";
        vehicules.Cell(3, 1).Value = "ga 214 rk";       // déjà présent : kilométrage seul, hors limite
        vehicules.Cell(3, 8).Value = 2_000;
        vehicules.Cell(4, 1).Value = "QA-IMP-2";
        vehicules.Cell(5, 1).Value = "QA-IMP-3";

        var entretiens = wb.Worksheets.Add("Entretiens");
        var colonnes = new[] { "Matricule", "Date (JJ/MM/AAAA)", "Intitulé", "Coût" };
        for (var i = 0; i < colonnes.Length; i++) entretiens.Cell(1, i + 1).Value = colonnes[i];
        entretiens.Cell(2, 1).Value = "GB-100-AA";
        entretiens.Cell(2, 2).Value = "12/09/2026";
        entretiens.Cell(2, 3).Value = "Vidange";
        entretiens.Cell(2, 4).Value = 90;
        entretiens.Cell(3, 1).Value = "qa imp 3";      // véhicule écarté par la limite, s'il l'est
        entretiens.Cell(3, 2).Value = "13/09/2026";
        entretiens.Cell(3, 3).Value = "Vidange";
        entretiens.Cell(3, 4).Value = 60;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static async Task<DataPortController.ImportSummary> ImporterAsync(GisDbContext ctx)
    {
        var classeur = Classeur();
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
        var file = new FormFile(new MemoryStream(classeur), 0, classeur.Length, "file", "parc.xlsx");
        var ok = (await controleur.Import(file)).Should().BeOfType<OkObjectResult>().Subject;
        return ok.Value.Should().BeOfType<DataPortController.ImportSummary>().Subject;
    }

    [Fact]
    public async Task L_import_ne_cree_pas_de_vehicule_au_dela_de_la_limite_et_importe_le_reste_du_classeur()
    {
        using var ctx = await ParcAsync(maxVehicles: 3, prixParVehicule: false);

        var bilan = await ImporterAsync(ctx);

        bilan.VehiclesCreated.Should().Be(1);
        bilan.VehiclesUpdated.Should().Be(1);
        bilan.VehiclesIgnored.Should().Be(2);
        bilan.Notes.Should().Contain(new[]
        {
            "Véhicule « QA-IMP-2 » non créé : limite de l'abonnement atteinte (3 au maximum).",
            "Véhicule « QA-IMP-3 » non créé : limite de l'abonnement atteinte (3 au maximum)."
        });
        bilan.MaintenanceCreated.Should().Be(1);

        // L'entretien du véhicule écarté dit pourquoi : le matricule figure bien au classeur.
        bilan.MaintenanceIgnored.Should().Be(1);
        bilan.Notes.Should().Contain("Entretien ignoré : véhicule « qa imp 3 » non créé (limite de l'abonnement).");
        bilan.Notes.Should().NotContain(n => n.Contains("introuvable"));

        ctx.ChangeTracker.Clear();
        (await ctx.Vehicles.IgnoreQueryFilters().Where(v => v.CompanyId == CompanyId).Select(v => v.Plate).ToListAsync())
            .Should().BeEquivalentTo("GA-214-RK", "GB-100-AA", "QA-IMP-1");
        (await ctx.Vehicles.SingleAsync(v => v.Id == 1)).Mileage.Should().Be(2_000);
    }

    [Fact]
    public async Task Une_formule_facturee_au_vehicule_importe_tous_les_vehicules()
    {
        using var ctx = await ParcAsync(maxVehicles: 3, prixParVehicule: true);

        var bilan = await ImporterAsync(ctx);

        bilan.VehiclesCreated.Should().Be(3);
        bilan.MaintenanceCreated.Should().Be(2);
        bilan.Notes.Should().NotContain(n => n.Contains("limite de l'abonnement"));
    }

    // ── DEF-037 : administration système ─────────────────────────────────────

    private static async Task<TestGisDbContext> AdminAsync(params Vehicle[] vehicles)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.SubscriptionTypes.Add(TestDataBuilder.CreateSubscriptionType(1));
        ctx.Societes.AddRange(TestDataBuilder.CreateSociete(CompanyId), TestDataBuilder.CreateSociete(OtherCompanyId));
        ctx.Vehicles.AddRange(vehicles);
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static Vehicle Veh(int id, string? plate, int companyId = CompanyId) => new()
    {
        Id = id, CompanyId = companyId, Name = $"Véhicule {id}", Type = "camion", Plate = plate, Status = "available"
    };

    private static CreateAdminVehicleCommand Creer(string plate, int companyId = CompanyId) => new(
        Name: "QA Admin", Type: "camion", Brand: null, Model: null, Plate: plate, Year: null, Color: null,
        Status: "available", HasGps: false, Mileage: 0, FuelType: "diesel", FuelTankCapacity: null,
        CompanyId: companyId, GpsDeviceId: null, GpsImei: null, GpsMat: null, GpsBrand: null, GpsModel: null,
        GpsFirmwareVersion: null, GpsFuelSensorMode: null, GpsSimNumber: null, GpsSimOperator: null,
        GpsInstallationDate: null);

    private static UpdateAdminVehicleCommand Modifier(int id, string? plate, int? companyId, string? color = null) => new(
        Id: id, Name: null, Type: null, Brand: null, Model: null, Plate: plate, Year: null, Color: color,
        Status: null, HasGps: null, Mileage: null, FuelType: null, FuelTankCapacity: null, CompanyId: companyId,
        GpsDeviceId: null, GpsImei: null, GpsMat: null, GpsBrand: null, GpsModel: null, GpsFirmwareVersion: null,
        GpsFuelSensorMode: null, GpsSimNumber: null, GpsSimOperator: null, GpsInstallationDate: null);

    [Fact]
    public async Task L_administration_ne_cree_pas_un_second_vehicule_de_meme_matricule_dans_la_societe()
    {
        using var ctx = await AdminAsync(Veh(40, "GA-214-RK"));
        var handler = new CreateAdminVehicleCommandHandler(ctx);

        var refus = await handler.Handle(Creer("ga 214.rk"), CancellationToken.None);

        refus.Success.Should().BeFalse();
        refus.Error.Should().Be("Doublon refusé : le matricule GA-214-RK est déjà porté par le véhicule « Véhicule 40 » (#40) de cette société.");
        ctx.Vehicles.Count().Should().Be(1);

        // Le même matricule dans une autre société reste libre.
        (await handler.Handle(Creer("GA-214-RK", OtherCompanyId), CancellationToken.None)).Success.Should().BeTrue();
    }

    [Fact]
    public async Task L_administration_refuse_un_renommage_ou_un_changement_de_societe_vers_un_matricule_pris()
    {
        using var ctx = await AdminAsync(Veh(40, "GA-214-RK"), Veh(41, "QA-041"), Veh(50, "QA-041", OtherCompanyId));
        var handler = new UpdateAdminVehicleCommandHandler(ctx);

        var renommage = await handler.Handle(Modifier(41, "GA-214-RK", CompanyId), CancellationToken.None);
        renommage.Success.Should().BeFalse();
        renommage.Error.Should().StartWith("Doublon refusé : le matricule GA-214-RK");

        // Matricule inchangé, mais la société d'arrivée a déjà un « QA-041 ».
        ctx.ChangeTracker.Clear();
        var transfert = await handler.Handle(Modifier(50, null, CompanyId), CancellationToken.None);
        transfert.Success.Should().BeFalse();
        transfert.Error.Should().Contain("« Véhicule 41 » (#41)");

        ctx.ChangeTracker.Clear();
        ctx.Vehicles.Single(v => v.Id == 41).Plate.Should().Be("QA-041");
        ctx.Vehicles.Single(v => v.Id == 50).CompanyId.Should().Be(OtherCompanyId);
    }

    [Fact]
    public async Task Un_doublon_deja_en_base_reste_modifiable_depuis_l_administration()
    {
        using var ctx = await AdminAsync(Veh(40, "GA-214-RK"), Veh(45, "GA-214-RK"));
        var handler = new UpdateAdminVehicleCommandHandler(ctx);

        // L'écran renvoie le matricule et la société du véhicule à chaque enregistrement.
        var result = await handler.Handle(Modifier(45, "GA-214-RK", CompanyId, color: "Bleu"), CancellationToken.None);

        result.Success.Should().BeTrue();
        ctx.Vehicles.Single(v => v.Id == 45).Color.Should().Be("Bleu");
    }
}
