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
/// Modèle d'import Excel et règles de l'import, campagne de test GPA :
/// <list type="bullet">
///   <item>DEF-029 — chaque feuille du modèle portait une ligne d'exemple importable
///     telle quelle : remplir le modèle sans l'effacer créait un véhicule fictif
///     « 123 TU 4567 », un entretien, une réparation, un plein et une assurance ;</item>
///   <item>DEF-039 — l'import et la saisie d'un plein ne rattachaient pas un matricule
///     selon la même règle ;</item>
///   <item>DEF-050 — un montant négatif jouait comme un crédit silencieux.</item>
/// </list>
/// Tests de bout en bout sur le contrôleur, avec le vrai GisDbContext en mémoire.
/// </summary>
public class DataPortTemplateExampleTests
{
    private const int CompanyId = 7;

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

    private static GisDbContext Contexte() =>
        new ContexteEnMemoire(new DbContextOptionsBuilder<GisDbContext>()
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

    /// <summary>Parc où le véhicule de l'exemple EXISTE : le pire cas, tout serait rattaché.</summary>
    private static async Task<GisDbContext> ParcAvecVehiculeDeLExempleAsync()
    {
        var ctx = Contexte();
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "Camion 1", Plate = "123 TU 4567", CompanyId = CompanyId, Status = "available", Mileage = 100_000 },
            new Vehicle { Id = 32, Name = "Utilitaire 02", Plate = "GA-214-RK", CompanyId = CompanyId, Status = "available" });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static async Task<DataPortController.ImportSummary> ImporterAsync(GisDbContext ctx, byte[] classeur)
    {
        var file = new FormFile(new MemoryStream(classeur), 0, classeur.Length, "file", "modele.xlsx");
        var ok = (await Controleur(ctx).Import(file)).Should().BeOfType<OkObjectResult>().Subject;
        return ok.Value.Should().BeOfType<DataPortController.ImportSummary>().Subject;
    }

    private static int Crees(DataPortController.ImportSummary b) =>
        b.VehiclesCreated + b.VehiclesUpdated + b.MaintenanceCreated + b.RepairsCreated + b.FuelCreated + b.ExpensesCreated;

    private static async Task AucuneDonneeCreeeAsync(GisDbContext ctx, int vehicules = 2)
    {
        ctx.ChangeTracker.Clear();
        (await ctx.Vehicles.CountAsync()).Should().Be(vehicules);
        (await ctx.VehicleCosts.CountAsync()).Should().Be(0);
        (await ctx.Repairs.CountAsync()).Should().Be(0);
        (await ctx.FuelEntries.CountAsync()).Should().Be(0);
        (await ctx.Vehicles.AsNoTracking().SingleAsync(v => v.Id == 1)).Mileage.Should().Be(100_000);
    }

    /// <summary>Le modèle tel qu'il était servi avant le correctif : exemple tunisien en ligne 2 de chaque feuille.</summary>
    private static byte[] AncienModele() => ModeleAvecExemplesEnLigne2(
        new object[] { "123 TU 4567", "Camion 1", "Renault", "Master", 2021, "camion", "diesel", 145000, 80 },
        new object[] { "123 TU 4567", "15/08/2026", "Vidange + filtres", 350 },
        new object[] { "123 TU 4567", "18/08/2026", "Plaquettes de frein AV", "Freinage", 145100, 80, 120, 200, "Terminée" },
        new object[] { "123 TU 4567", "20/08/2026", 45, 2.2, 99, 145200 },
        new object[] { "123 TU 4567", "10/08/2026", "Assurance", "Assurance flotte 2026", 625, 145000 });

    /// <summary>Un client français qui a recopié l'exemple de l'aide en ligne 2 de chaque feuille.</summary>
    private static byte[] ModeleFrancaisRempliAvecSesExemples() => ModeleAvecExemplesEnLigne2(
        new object[] { "AB-123-CD", "Camion 1", "Renault", "Master", 2021, "camion", "diesel", 145000, 80 },
        new object[] { "AB-123-CD", "15/08/2026", "Vidange + filtres", 180 },
        new object[] { "AB-123-CD", "18/08/2026", "Plaquettes de frein AV", "Freinage", 145100, 80, 120, 200, "Terminée" },
        new object[] { "AB-123-CD", "20/08/2026", 45, 1.75, 78.75, 145200 },
        new object[] { "AB-123-CD", "10/08/2026", "Assurance", "Assurance flotte 2026", 900, 145000 });

    private static byte[] ModeleAvecExemplesEnLigne2(object[] vehicule, object[] entretien, object[] reparation, object[] plein, object[] depense)
    {
        using var wb = new XLWorkbook();
        void Feuille(string nom, string[] entetes, params object[] exemple)
        {
            var ws = wb.Worksheets.Add(nom);
            for (var i = 0; i < entetes.Length; i++) ws.Cell(1, i + 1).Value = entetes[i];
            for (var i = 0; i < exemple.Length; i++)
                ws.Cell(2, i + 1).Value = exemple[i] switch
                {
                    string s => s, int n => n, double d => d, _ => throw new ArgumentException("type d'exemple inattendu")
                };
            ws.Row(2).Style.Font.Italic = true;
        }

        Feuille("Véhicules", new[] { "Matricule", "Nom", "Marque", "Modèle", "Année", "Type", "Carburant", "Kilométrage", "Capacité réservoir (L)" }, vehicule);
        Feuille("Entretiens", new[] { "Matricule", "Date (JJ/MM/AAAA)", "Intitulé", "Coût" }, entretien);
        Feuille("Réparations", new[] { "Matricule", "Date (JJ/MM/AAAA)", "Description", "Type", "Kilométrage", "Main d'œuvre", "Pièces", "Total", "Statut", "Fournisseur", "N° facture", "Référence" }, reparation);
        Feuille("Carburant", new[] { "Matricule", "Date (JJ/MM/AAAA)", "Volume (L)", "Prix/L", "Montant total", "Kilométrage" }, plein);
        Feuille("Dépenses", new[] { "Matricule", "Date (JJ/MM/AAAA)", "Type", "Description", "Montant", "Kilométrage", "Volume (L)", "N° pièce" }, depense);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ──────────── Exemples selon le pays du client (Karim, 23/09/2026) ────────────
    // « Le fichier modèle contient des immatriculations tunisiennes alors qu'il est
    // destiné à l'export, essentiellement la France. » Même règle que les sinistres :
    // un compte en euros voit une plaque française et des montants en euros, tout
    // autre compte garde l'exemple tunisien. À l'import, les deux jeux sont écartés.

    private const string PlaqueFrancaise = "AB-123-CD";
    private const string PlaqueTunisienne = "123 TU 4567";

    private static readonly string[] Feuilles = { "Véhicules", "Entretiens", "Réparations", "Carburant", "Dépenses" };

    /// <summary>Le parc de <see cref="ParcAvecVehiculeDeLExempleAsync"/>, plus le véhicule de l'exemple français, dans une société en euros.</summary>
    private static async Task<GisDbContext> ParcFrancaisEnEurosAsync()
    {
        var ctx = await ParcAvecVehiculeDeLExempleAsync();
        ctx.Vehicles.Add(new Vehicle { Id = 3, Name = "Camion 1", Plate = PlaqueFrancaise, CompanyId = CompanyId, Status = "available", Mileage = 100_000 });
        ctx.Societes.Add(new Societe
        {
            Id = CompanyId, Name = "Belive GPA", SubscriptionStatus = "active", IsActive = true,
            Settings = new SocieteSettings { Currency = "EUR" }
        });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static async Task<IXLWorksheet> AideDuModeleAsync(GisDbContext ctx)
    {
        var modele = (await Controleur(ctx).Template()).Should().BeOfType<FileContentResult>().Subject.FileContents;
        var wb = new XLWorkbook(new MemoryStream(modele));
        foreach (var nom in Feuilles)
            wb.Worksheet(nom).LastRowUsed()!.RowNumber().Should().Be(1, $"la feuille {nom} ne porte que ses en-têtes");
        return wb.Worksheet(DataPortController.HelpSheetName);
    }

    [Fact]
    public async Task Un_compte_en_euros_recoit_un_modele_a_plaque_francaise_et_montants_en_euros()
    {
        using var ctx = await ParcFrancaisEnEurosAsync();

        var aide = await AideDuModeleAsync(ctx);

        aide.CellsUsed().Count(c => c.GetString() == PlaqueFrancaise).Should().Be(5, "un exemple par feuille, en français");
        aide.CellsUsed().Any(c => c.GetString() == PlaqueTunisienne).Should().BeFalse("plus de plaque tunisienne pour un client en euros");
        aide.CellsUsed().Any(c => c.DataType == XLDataType.Number && c.GetDouble() == 1.75).Should().BeTrue("prix du litre en euros");
    }

    [Fact]
    public async Task Un_compte_hors_euro_garde_l_exemple_tunisien()
    {
        using var ctx = await ParcAvecVehiculeDeLExempleAsync();
        ctx.Societes.Add(new Societe
        {
            Id = CompanyId, Name = "Belive GPS", SubscriptionStatus = "active", IsActive = true,
            Settings = new SocieteSettings { Currency = "TND" }
        });
        await ctx.SaveChangesAsync();

        var aide = await AideDuModeleAsync(ctx);

        aide.CellsUsed().Count(c => c.GetString() == PlaqueTunisienne).Should().Be(5);
        aide.CellsUsed().Any(c => c.GetString() == PlaqueFrancaise).Should().BeFalse();
    }

    [Fact]
    public async Task Sans_societe_connue_le_modele_reste_tunisien_comme_avant()
    {
        // Devise du déploiement (AppCurrency.Default, "TND" hors configuration) :
        // le comportement d'avant le 23/09/2026 est conservé tel quel.
        using var ctx = await ParcAvecVehiculeDeLExempleAsync();
        var aide = await AideDuModeleAsync(ctx);
        aide.CellsUsed().Count(c => c.GetString() == PlaqueTunisienne).Should().Be(5);
    }

    [Fact]
    public async Task Chez_un_client_francais_les_exemples_francais_ET_tunisiens_recopies_ne_creent_rien()
    {
        // Pire cas : les deux véhicules d'exemple existent dans le parc, tout serait rattaché.
        using var ctx = await ParcFrancaisEnEurosAsync();

        var bilan = await ImporterAsync(ctx, ModeleFrancaisRempliAvecSesExemples());
        Crees(bilan).Should().Be(0, "les cinq lignes recopiées de l'aide française sont des exemples");
        bilan.IgnoredLines.Should().Be(5);
        bilan.Notes.Should().Contain("Véhicules : ligne d'exemple du modèle ignorée.");
        await AucuneDonneeCreeeAsync(ctx, vehicules: 3);

        // Modèle téléchargé avant le 23/09/2026, exemple tunisien en ligne 2 : écarté aussi.
        var ancien = await ImporterAsync(ctx, AncienModele());
        Crees(ancien).Should().Be(0);
        ancien.IgnoredLines.Should().Be(5);
        await AucuneDonneeCreeeAsync(ctx, vehicules: 3);
    }

    [Fact]
    public async Task Une_vraie_ligne_francaise_qui_ne_reprend_qu_une_partie_de_l_exemple_est_importee()
    {
        using var ctx = await ParcFrancaisEnEurosAsync();
        using var wb = new XLWorkbook(new MemoryStream(ModeleFrancaisRempliAvecSesExemples()));
        // Même plaque et même date que l'exemple, mais un autre entretien : c'est une saisie.
        wb.Worksheet("Entretiens").Cell(3, 1).Value = PlaqueFrancaise;
        wb.Worksheet("Entretiens").Cell(3, 2).Value = "15/08/2026";
        wb.Worksheet("Entretiens").Cell(3, 3).Value = "Contrôle technique";
        wb.Worksheet("Entretiens").Cell(3, 4).Value = 95;
        using var ms = new MemoryStream();
        wb.SaveAs(ms);

        var bilan = await ImporterAsync(ctx, ms.ToArray());

        bilan.MaintenanceCreated.Should().Be(1);
        bilan.MaintenanceIgnored.Should().Be(1, "la ligne d'exemple intacte reste écartée");
    }

    // ─────────────────────────────── DEF-029 ───────────────────────────────

    [Fact]
    public async Task Le_modele_n_a_que_des_en_tetes_dans_ses_feuilles_de_donnees_et_ses_exemples_dans_l_aide()
    {
        using var ctx = Contexte();

        var modele = (await Controleur(ctx).Template()).Should().BeOfType<FileContentResult>().Subject.FileContents;

        using var wb = new XLWorkbook(new MemoryStream(modele));
        wb.Worksheets.Select(w => w.Name).Should().Equal(
            DataPortController.HelpSheetName, "Véhicules", "Entretiens", "Réparations", "Carburant", "Dépenses");
        foreach (var nom in new[] { "Véhicules", "Entretiens", "Réparations", "Carburant", "Dépenses" })
            wb.Worksheet(nom).LastRowUsed()!.RowNumber().Should().Be(1, $"la feuille {nom} ne porte que ses en-têtes");

        var aide = wb.Worksheet(DataPortController.HelpSheetName);
        aide.CellsUsed().Count(c => c.GetString() == "123 TU 4567").Should().Be(5, "un exemple par feuille, dans l'aide");
    }

    [Fact]
    public async Task Importer_le_modele_tel_quel_ne_cree_rien()
    {
        using var ctx = await ParcAvecVehiculeDeLExempleAsync();
        var modele = (await Controleur(ctx).Template()).Should().BeOfType<FileContentResult>().Subject.FileContents;

        var bilan = await ImporterAsync(ctx, modele);

        Crees(bilan).Should().Be(0);
        await AucuneDonneeCreeeAsync(ctx);
    }

    [Fact]
    public async Task Un_ancien_modele_avec_ses_lignes_d_exemple_ne_cree_rien_et_le_dit()
    {
        using var ctx = await ParcAvecVehiculeDeLExempleAsync();

        var bilan = await ImporterAsync(ctx, AncienModele());

        Crees(bilan).Should().Be(0, "avant : 1 entretien, 1 réparation, 1 plein, 1 assurance et un kilométrage porté à 145 200 km");
        bilan.VehiclesIgnored.Should().Be(1);
        bilan.MaintenanceIgnored.Should().Be(1);
        bilan.RepairsIgnored.Should().Be(1);
        bilan.FuelIgnored.Should().Be(1);
        bilan.ExpensesIgnored.Should().Be(1);
        bilan.Notes.Should().Contain(new[]
        {
            "Véhicules : ligne d'exemple du modèle ignorée.",
            "Entretiens : ligne d'exemple du modèle ignorée.",
            "Réparations : ligne d'exemple du modèle ignorée.",
            "Carburant : ligne d'exemple du modèle ignorée.",
            "Dépenses : ligne d'exemple du modèle ignorée."
        });
        await AucuneDonneeCreeeAsync(ctx);
    }

    [Fact]
    public async Task Une_vraie_ligne_qui_ne_reprend_qu_une_partie_de_l_exemple_est_importee()
    {
        using var ctx = await ParcAvecVehiculeDeLExempleAsync();
        using var wb = new XLWorkbook(new MemoryStream(AncienModele()));
        // Le client a remplacé le montant de l'exemple : c'est une vraie saisie.
        wb.Worksheet("Dépenses").Cell(2, 5).Value = 640;
        wb.Worksheet("Entretiens").Cell(3, 1).Value = "123 TU 4567";
        wb.Worksheet("Entretiens").Cell(3, 2).Value = "15/08/2026";
        wb.Worksheet("Entretiens").Cell(3, 3).Value = "Vidange + filtres";
        wb.Worksheet("Entretiens").Cell(3, 4).Value = 360;
        using var ms = new MemoryStream();
        wb.SaveAs(ms);

        var bilan = await ImporterAsync(ctx, ms.ToArray());

        bilan.ExpensesCreated.Should().Be(1);
        bilan.MaintenanceCreated.Should().Be(1);
        bilan.MaintenanceIgnored.Should().Be(1, "la ligne d'exemple intacte reste écartée");
    }

    [Fact]
    public async Task La_feuille_Depenses_du_modele_remplie_par_le_client_est_importee()
    {
        // Reprend la couverture de l'ancien test « feuille Dépenses importable », qui
        // importait l'exemple lui-même : type ramené à son code, montant et compteur lus.
        using var ctx = await ParcAvecVehiculeDeLExempleAsync();
        var modele = (await Controleur(ctx).Template()).Should().BeOfType<FileContentResult>().Subject.FileContents;
        using var wb = new XLWorkbook(new MemoryStream(modele));
        var depenses = wb.Worksheet("Dépenses");
        depenses.Cell(2, 1).Value = "123 TU 4567"; depenses.Cell(2, 2).Value = "10/08/2026";
        depenses.Cell(2, 3).Value = "Assurance"; depenses.Cell(2, 4).Value = "Assurance flotte 2026";
        depenses.Cell(2, 5).Value = 640; depenses.Cell(2, 6).Value = 145000;
        using var ms = new MemoryStream();
        wb.SaveAs(ms);

        var bilan = await ImporterAsync(ctx, ms.ToArray());

        bilan.ExpensesCreated.Should().Be(1);
        ctx.ChangeTracker.Clear();
        var assurance = await ctx.VehicleCosts.AsNoTracking().SingleAsync(c => c.Type == "insurance");
        assurance.VehicleId.Should().Be(1);
        assurance.Amount.Should().Be(640m);
        assurance.Mileage.Should().Be(145_000);
    }

    // ─────────────────────────────── DEF-039 / DEF-050 ───────────────────────────────

    private static byte[] Classeur(Action<XLWorkbook> remplir)
    {
        using var wb = new XLWorkbook();
        remplir(wb);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Non-régression : l'import rattachait déjà ces frappes ; c'est la saisie d'un plein
    /// qui les refusait (FuelEntryPlateMatchingTests). Les mêmes frappes, virgule et
    /// parenthèses comprises, doivent être rattachées des deux côtés par la clé commune.
    /// </summary>
    [Fact]
    public async Task Import_rattache_le_matricule_selon_la_regle_de_la_saisie_d_un_plein()
    {
        using var ctx = await ParcAvecVehiculeDeLExempleAsync();
        var classeur = Classeur(wb =>
        {
            var ws = wb.Worksheets.Add("Dépenses");
            ws.Cell(1, 1).Value = "Matricule";
            ws.Cell(2, 1).Value = "ga 214 rk"; ws.Cell(2, 2).Value = "01/09/2026"; ws.Cell(2, 3).Value = "Amende"; ws.Cell(2, 5).Value = 120;
            ws.Cell(3, 1).Value = "123-TU-4567"; ws.Cell(3, 2).Value = "02/09/2026"; ws.Cell(3, 3).Value = "Péage"; ws.Cell(3, 5).Value = 8;
            ws.Cell(4, 1).Value = "GA,214(RK)"; ws.Cell(4, 2).Value = "03/09/2026"; ws.Cell(4, 3).Value = "Stationnement"; ws.Cell(4, 5).Value = 4;
        });

        var bilan = await ImporterAsync(ctx, classeur);

        bilan.ExpensesCreated.Should().Be(3);
        ctx.ChangeTracker.Clear();
        (await ctx.VehicleCosts.OrderBy(c => c.Date).Select(c => c.VehicleId).ToListAsync()).Should().Equal(32, 1, 32);
    }

    [Fact]
    public async Task Import_montant_negatif_ecarte_avec_une_note()
    {
        using var ctx = await ParcAvecVehiculeDeLExempleAsync();
        var classeur = Classeur(wb =>
        {
            var es = wb.Worksheets.Add("Dépenses");
            es.Cell(1, 1).Value = "Matricule";
            es.Cell(2, 1).Value = "GA-214-RK"; es.Cell(2, 2).Value = "01/09/2026"; es.Cell(2, 3).Value = "Assurance"; es.Cell(2, 5).Value = -50;
            var ms = wb.Worksheets.Add("Entretiens");
            ms.Cell(1, 1).Value = "Matricule";
            ms.Cell(2, 1).Value = "GA-214-RK"; ms.Cell(2, 2).Value = "01/09/2026"; ms.Cell(2, 3).Value = "Vidange"; ms.Cell(2, 4).Value = -350;
            // Entretien gratuit (0) : l'export l'écrit, l'import doit le relire.
            ms.Cell(3, 1).Value = "GA-214-RK"; ms.Cell(3, 2).Value = "02/09/2026"; ms.Cell(3, 3).Value = "Vidange offerte"; ms.Cell(3, 4).Value = 0;
        });

        var bilan = await ImporterAsync(ctx, classeur);

        bilan.ExpensesCreated.Should().Be(0);
        bilan.ExpensesIgnored.Should().Be(1);
        bilan.MaintenanceCreated.Should().Be(1);
        bilan.MaintenanceIgnored.Should().Be(1);
        bilan.Notes.Should().Contain(n => n.Contains("montant négatif"));
        ctx.ChangeTracker.Clear();
        (await ctx.VehicleCosts.AnyAsync(c => c.Amount < 0)).Should().BeFalse();
    }
}
