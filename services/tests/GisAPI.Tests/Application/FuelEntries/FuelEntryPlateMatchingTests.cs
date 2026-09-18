using FluentAssertions;
using GisAPI.Application.Features.FuelEntries;
using GisAPI.Application.Features.FuelEntries.Commands;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.FuelEntries;

/// <summary>
/// Campagne de test GPA, DEF-039 : le rattachement d'un plein par matricule
/// ignorait espaces et casse mais pas les séparateurs. Sur un parc immatriculé
/// « GA-214-RK », la frappe « ga 214 rk » était refusée (« Aucun véhicule ne
/// correspond… ») alors que « ga-214-rk » passait — et l'import Excel, lui,
/// rattachait les deux. Les tests passent par SQLite : la présélection est bien
/// traduite et exécutée par la base, pas évaluée en mémoire.
/// </summary>
public class FuelEntryPlateMatchingTests
{
    private const int CompanyId = 7;
    private const int OtherCompanyId = 8;
    private const int FuelTypeGazole = 1;

    private static async Task<TestGisDbContext> ContexteAsync(params Vehicle[] autres)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.FuelTypes.Add(new FuelType { Id = FuelTypeGazole, Code = "diesel", Name = "Gazole" });
        ctx.Vehicles.AddRange(
            // Id plus petit et mêmes caractères dans le même ordre (« GA-2140-RK ») :
            // retenu par la présélection SQL, il doit être écarté par la clé exacte.
            new Vehicle { Id = 31, Name = "Utilitaire 01", Plate = "GA-2140-RK", CompanyId = CompanyId, Mileage = 10_000 },
            new Vehicle { Id = 32, Name = "Utilitaire 02", Plate = "GA-214-RK", CompanyId = CompanyId, Mileage = 10_000 },
            new Vehicle { Id = 33, Name = "Camion 1", Plate = "123 TU 4567", CompanyId = CompanyId, Mileage = 10_000 },
            // Même matricule dans une autre société : jamais rattaché.
            new Vehicle { Id = 90, Name = "Autre société", Plate = "GB-500-XX", CompanyId = OtherCompanyId });
        ctx.Vehicles.AddRange(autres);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    private static CreateFuelEntryCommandHandler Handler(TestGisDbContext ctx) =>
        new(ctx, TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: 1).Object, Mock.Of<IPublisher>());

    private static CreateFuelEntryCommand Plein(string plate, long? odometerKm = null) =>
        new(plate, FuelTypeGazole, 40m, 1.8m, new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc),
            null, null, null, null, odometerKm);

    private static async Task<FuelEntry> CreerAsync(TestGisDbContext ctx, string plate, long? odometerKm = null)
    {
        var id = await Handler(ctx).Handle(Plein(plate, odometerKm), CancellationToken.None);
        return await ctx.FuelEntries.AsNoTracking().SingleAsync(f => f.Id == id);
    }

    [Theory]
    [InlineData("ga 214 rk")]
    [InlineData("ga-214-rk")]
    [InlineData("GA.214.RK")]
    [InlineData("ga/214/rk")]
    [InlineData("GA214RK")]
    [InlineData(" ga_214 \u2013 rk ")]
    [InlineData("GA,214(RK)")]
    [InlineData("GA\u00A0214\u2011RK")]
    public async Task Separateurs_espaces_et_casse_ignores_dans_la_saisie(string frappe)
    {
        using var ctx = await ContexteAsync();

        var plein = await CreerAsync(ctx, frappe);

        plein.VehicleId.Should().Be(32);
        plein.VehiclePlate.Should().Be("GA-214-RK", "le matricule canonique est enregistré, pas la frappe");
    }

    [Theory]
    [InlineData("123-TU-4567")]
    [InlineData("123tu4567")]
    [InlineData("123.tu.4567")]
    public async Task Separateurs_ignores_aussi_dans_le_matricule_enregistre(string frappe)
    {
        using var ctx = await ContexteAsync();

        (await CreerAsync(ctx, frappe)).VehicleId.Should().Be(33);
    }

    [Fact]
    public async Task Matricule_en_lettres_arabes_rattache_malgre_les_espaces()
    {
        using var ctx = await ContexteAsync(
            new Vehicle { Id = 40, Name = "Fourgon TN", Plate = "123 تونس 4567", CompanyId = CompanyId });

        (await CreerAsync(ctx, "123تونس4567")).VehicleId.Should().Be(40);
    }

    [Fact]
    public async Task Lettre_accentuee_rattachee_meme_si_la_base_ne_met_pas_l_accent_en_majuscule()
    {
        // SQLite, comme PostgreSQL en locale C, ne passe en majuscules que l'ASCII.
        using var ctx = await ContexteAsync(
            new Vehicle { Id = 41, Name = "Fourgon é2", Plate = null, CompanyId = CompanyId });

        (await CreerAsync(ctx, "FOURGON É2")).VehicleId.Should().Be(41);
    }

    [Fact]
    public async Task Nom_du_vehicule_toujours_accepte_a_defaut_de_matricule()
    {
        using var ctx = await ContexteAsync();

        (await CreerAsync(ctx, "utilitaire-02")).VehicleId.Should().Be(32);
    }

    [Fact]
    public async Task Un_matricule_l_emporte_sur_un_nom_identique_d_un_autre_vehicule()
    {
        using var ctx = await ContexteAsync(
            new Vehicle { Id = 5, Name = "GA214RK", Plate = "OLD-005", CompanyId = CompanyId });

        (await CreerAsync(ctx, "GA-214-RK")).VehicleId.Should().Be(32);
    }

    [Fact]
    public async Task Le_releve_compteur_fait_avancer_le_vehicule_rattache()
    {
        using var ctx = await ContexteAsync();

        await CreerAsync(ctx, "ga 214 rk", odometerKm: 12_500);

        (await ctx.Vehicles.AsNoTracking().SingleAsync(v => v.Id == 32)).Mileage.Should().Be(12_500);
    }

    [Theory]
    [InlineData("GB-500-XX")]   // véhicule d'une autre société
    [InlineData("GA-214-RQ")]   // matricule voisin
    [InlineData("GA-21-4RK0")]  // mêmes caractères, pas dans le même ordre
    [InlineData("-")]           // séparateurs seuls : aucune clé
    public async Task Matricule_sans_correspondance_refuse_sans_ecriture(string frappe)
    {
        using var ctx = await ContexteAsync();

        var act = () => Handler(ctx).Handle(Plein(frappe), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Aucun véhicule ne correspond au matricule*");
        (await ctx.FuelEntries.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Import_en_masse_meme_regle_que_la_saisie_unitaire()
    {
        using var ctx = await ContexteAsync();
        var create = Handler(ctx);
        var sender = new Mock<ISender>();
        sender
            .Setup(s => s.Send(It.IsAny<CreateFuelEntryCommand>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<int> r, CancellationToken ct) => create.Handle((CreateFuelEntryCommand)r, ct));
        var bulk = new BulkCreateFuelEntriesCommandHandler(sender.Object, ctx,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BulkCreateFuelEntriesCommandHandler>.Instance);

        var result = await bulk.Handle(new BulkCreateFuelEntriesCommand(new[] { Plein("ga 214 rk"), Plein("123/TU/4567") }),
            CancellationToken.None);

        result.Success.Should().Be(2);
        (await ctx.FuelEntries.AsNoTracking().OrderBy(f => f.Id).Select(f => f.VehicleId).ToListAsync())
            .Should().Equal(32, 33);
    }

    [Fact]
    public void La_preselection_est_executee_par_PostgreSQL_avec_un_motif_parametre()
    {
        // Fournisseur de production, sans connexion : seule la traduction SQL est vérifiée.
        using var ctx = new GisAPI.Infrastructure.Persistence.GisDbContext(
            new DbContextOptionsBuilder<GisAPI.Infrastructure.Persistence.GisDbContext>()
                .UseNpgsql("Host=localhost;Database=traduction_seulement").Options,
            TestDbContextFactory.CreateMockTenantService(CompanyId).Object);

        var sql = ctx.Vehicles
            .Where(v => v.CompanyId == CompanyId)
            .Where(VehiclePlateKey.MayMatch("GA214RK"))
            .ToQueryString();

        sql.Should().Contain("upper(").And.MatchRegex(@"LIKE @\w+");
        // Motif en paramètre, pas en littéral : une requête compilée pour tous les matricules.
        sql.Should().NotContain("LIKE '");
    }

    [Theory]
    [InlineData("ga 214 rk", "GA214RK")]
    [InlineData("GA\u00A0214\u2011RK", "GA214RK")]   // espace et tiret insécables d'un copier-coller
    [InlineData("GA,214(RK)", "GA214RK")]
    [InlineData("GA\r\n214\r\nRK", "GA214RK")]       // cellule Excel sur plusieurs lignes
    [InlineData("123 تونس 4567", "123تونس4567")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Cle_de_matricule_lettres_et_chiffres_seuls(string? plate, string cle)
    {
        VehiclePlateKey.Normalize(plate).Should().Be(cle);
    }
}
