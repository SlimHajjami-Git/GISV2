using FluentAssertions;
using GisAPI.Application.Common;
using GisAPI.Application.Common.Helpers;
using GisAPI.Application.Features.Vehicles;
using GisAPI.Application.Features.Vehicles.Commands.CreateVehicle;
using GisAPI.Application.Features.Vehicles.Commands.PatchVehicle;
using GisAPI.Application.Features.Vehicles.Commands.UpdateVehicle;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.Data.Sqlite;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Vehicles;

/// <summary>
/// Fiche véhicule et abonnement — campagne de test Calypso GPA du 13/09/2026 :
/// jour de paiement 31 accepté alors que l'échéancier cale au 28 (DEF-034),
/// limite de véhicules de la formule non contrôlée (DEF-036), matricule en
/// double dans la société (DEF-037), PATCH d'un véhicule hors société en 500
/// (DEF-031), sinistre orphelin après suppression du véhicule (DEF-046) et
/// jours restants du bandeau comptés un de trop (DEF-054).
/// </summary>
public class VehicleAbonnementRecetteGpaTests
{
    private const int CompanyId = 7;
    private const int OtherCompanyId = 5;

    private static CreateVehicleCommandHandler CreateHandler(TestGisDbContext ctx) =>
        new(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object, new Mock<IPublisher>().Object);

    private static UpdateVehicleCommandHandler UpdateHandler(TestGisDbContext ctx) =>
        new(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object);

    private static PatchVehicleCommandHandler PatchHandler(TestGisDbContext ctx) =>
        new(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object);

    private static CreateVehicleCommand Create(string plate, int? paymentDay = null) => new(
        Name: $"QA {plate}", Type: "Berline",
        Brand: null, Model: null, Plate: plate, Year: null, Color: null,
        AcquisitionType: paymentDay.HasValue ? "leasing" : null,
        LeasingMonthlyPayment: paymentDay.HasValue ? 250m : null,
        LeasingDurationMonths: paymentDay.HasValue ? 12 : null,
        LeasingStartDate: paymentDay.HasValue ? new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc) : null,
        LeasingPaymentDay: paymentDay);

    private static UpdateVehicleCommand Update(Vehicle v, string? plate, int? paymentDay) => new(
        Id: v.Id, Name: v.Name, Type: v.Type, Brand: null, Model: null, Plate: plate,
        Year: null, Color: "Bleu", Status: "available", Mileage: 0, FuelType: null, FuelTankCapacity: null,
        AssignedDriverId: null, AssignedSupervisorId: null,
        AcquisitionType: null, PurchasePrice: null, PurchaseDate: null, LeasingMonthlyPayment: null,
        LeasingDurationMonths: null, LeasingStartDate: null, LeasingPaymentDay: paymentDay, RegistrationDate: null,
        InsuranceStartDate: null, InsuranceExpiry: null, InsuranceReminderDays: null,
        TaxStartDate: null, TaxExpiry: null, TaxReminderDays: null,
        TechnicalInspectionStartDate: null, TechnicalInspectionExpiry: null, TechnicalInspectionReminderDays: null);

    private static PatchVehicleCommand Patch(int id, string? plate = null, int? paymentDay = null) => new(
        Id: id, SpeedLimit: null, DepartmentId: null, FuelType: null,
        Brand: null, Model: null, Plate: plate, Year: null, Color: null,
        Mileage: null, FuelTankCapacity: null,
        AcquisitionType: null, PurchasePrice: null, LeasingMonthlyPayment: null,
        LeasingDurationMonths: null, LeasingStartDate: null, LeasingPaymentDay: paymentDay,
        RegistrationDate: null, PurchaseDate: null);

    private static Vehicle Veh(int id, string? plate, int companyId = CompanyId, int? paymentDay = null) => new()
    {
        Id = id,
        CompanyId = companyId,
        Name = $"Véhicule {id}",
        Type = "Berline",
        Plate = plate,
        Status = "available",
        LeasingPaymentDay = paymentDay
    };

    private static async Task<TestGisDbContext> AvecAsync(params Vehicle[] vehicles)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.AddRange(vehicles);
        await ctx.SaveChangesAsync();
        return ctx;
    }

    // ── DEF-034 : jour de paiement borné à 1..28 ─────────────────────────────

    [Fact]
    public async Task La_creation_refuse_un_jour_de_paiement_31()
    {
        using var ctx = TestDbContextFactory.Create();

        var act = () => CreateHandler(ctx).Handle(Create("QA-034-A", paymentDay: 31), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*Jour de paiement invalide (31)*1 et 28*");
        ctx.Vehicles.Should().BeEmpty();
    }

    [Fact]
    public async Task Le_patch_et_le_put_refusent_un_jour_hors_1_a_28()
    {
        using var ctx = await AvecAsync(Veh(46, "QA-046", paymentDay: 12));

        var patch = () => PatchHandler(ctx).Handle(Patch(46, paymentDay: 31), CancellationToken.None);
        await patch.Should().ThrowAsync<DomainException>().WithMessage("*Jour de paiement invalide*");

        var put = () => UpdateHandler(ctx).Handle(Update(ctx.Vehicles.Single(), "QA-046", 0), CancellationToken.None);
        await put.Should().ThrowAsync<DomainException>().WithMessage("*Jour de paiement invalide (0)*");

        ctx.ChangeTracker.Clear();
        ctx.Vehicles.Single().LeasingPaymentDay.Should().Be(12);
    }

    [Fact]
    public async Task Un_jour_valide_passe_et_une_valeur_ancienne_renvoyee_telle_quelle_ne_bloque_pas_la_fiche()
    {
        using var ctx = await AvecAsync(Veh(46, "QA-046", paymentDay: 31), Veh(47, "QA-047"));

        await PatchHandler(ctx).Handle(Patch(47, paymentDay: 28), CancellationToken.None);
        await UpdateHandler(ctx).Handle(Update(ctx.Vehicles.Single(v => v.Id == 46), "QA-046", 31), CancellationToken.None);

        ctx.Vehicles.Single(v => v.Id == 47).LeasingPaymentDay.Should().Be(28);
        ctx.Vehicles.Single(v => v.Id == 46).Color.Should().Be("Bleu");
    }

    // ── DEF-036 : limite de véhicules de la formule ──────────────────────────

    private static async Task<TestGisDbContext> SocieteAvecFormuleAsync(int maxVehicles, int vehicles, bool prixParVehicule = false)
    {
        var ctx = TestDbContextFactory.Create();
        var plan = TestDataBuilder.CreateSubscriptionType(10);
        plan.MaxVehicles = maxVehicles;
        plan.PricePerVehicle = prixParVehicule;
        ctx.SubscriptionTypes.Add(plan);
        ctx.Societes.Add(TestDataBuilder.CreateSociete(CompanyId, plan.Id));
        for (var i = 1; i <= vehicles; i++)
            ctx.Vehicles.Add(Veh(100 + i, $"LIM-{i}"));
        // Les véhicules d'une autre société ne comptent pas.
        ctx.Vehicles.Add(Veh(900, "AUTRE-1", OtherCompanyId));
        await ctx.SaveChangesAsync();
        return ctx;
    }

    [Fact]
    public async Task Le_vehicule_au_dela_de_la_limite_de_la_formule_est_refuse()
    {
        using var ctx = await SocieteAvecFormuleAsync(maxVehicles: 15, vehicles: 14);

        await CreateHandler(ctx).Handle(Create("QA-LIM-1"), CancellationToken.None);   // 15e : accepté
        var act = () => CreateHandler(ctx).Handle(Create("QA-LIM-2"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Limite de véhicules de votre abonnement atteinte (15 au maximum).");
        ctx.Vehicles.Count(v => v.CompanyId == CompanyId).Should().Be(15);
    }

    [Fact]
    public async Task Une_formule_sans_limite_parametree_ne_bloque_pas_la_creation()
    {
        using var ctx = await SocieteAvecFormuleAsync(maxVehicles: 0, vehicles: 3);

        var id = await CreateHandler(ctx).Handle(Create("QA-LIM-0"), CancellationToken.None);

        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Une_formule_facturee_au_vehicule_n_a_pas_de_limite_de_vehicules()
    {
        // Plan-basique de l'offre GPA européenne : max_vehicles = 15 en base, mais
        // facturé au véhicule et présenté « Véhicules illimités » à l'écran Abonnement.
        using var ctx = await SocieteAvecFormuleAsync(maxVehicles: 15, vehicles: 15, prixParVehicule: true);

        var id = await CreateHandler(ctx).Handle(Create("QA-LIM-16"), CancellationToken.None);

        id.Should().BeGreaterThan(0);
        ctx.Vehicles.Count(v => v.CompanyId == CompanyId).Should().Be(16);
        (await VehicleWriteRules.GetVehicleQuotaAsync(ctx, CompanyId, CancellationToken.None)).Should().BeNull();
    }

    // ── DEF-037 : matricule unique dans la société ───────────────────────────

    [Fact]
    public async Task Un_matricule_deja_utilise_dans_la_societe_est_refuse_sans_tenir_compte_de_la_casse_ni_des_separateurs()
    {
        using var ctx = await AvecAsync(Veh(40, "GA-214-RK"), Veh(900, "XX-100-ZZ", OtherCompanyId));

        var act = () => CreateHandler(ctx).Handle(Create(" ga 214.rk "), CancellationToken.None);

        // Matricule enregistré cité, pas le nom du véhicule en conflit.
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("Le matricule GA-214-RK est déjà utilisé dans votre société.");

        // Même clé que le rattachement d'un plein : tiret insécable d'un copier-coller.
        var copieColle = () => CreateHandler(ctx).Handle(Create("GA‑214‑RK"), CancellationToken.None);
        await copieColle.Should().ThrowAsync<ConflictException>();

        // Le même matricule dans une AUTRE société reste libre.
        var id = await CreateHandler(ctx).Handle(Create("XX/100/ZZ"), CancellationToken.None);
        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Le_put_et_le_patch_refusent_de_renommer_vers_un_matricule_existant()
    {
        using var ctx = await AvecAsync(Veh(40, "GA-214-RK"), Veh(41, "QA-041"));

        var put = () => UpdateHandler(ctx).Handle(Update(ctx.Vehicles.Single(v => v.Id == 41), "ga214rk", null), CancellationToken.None);
        await put.Should().ThrowAsync<ConflictException>();

        ctx.ChangeTracker.Clear();
        var patch = () => PatchHandler(ctx).Handle(Patch(41, plate: "GA-214-RK"), CancellationToken.None);
        await patch.Should().ThrowAsync<ConflictException>();

        ctx.ChangeTracker.Clear();
        ctx.Vehicles.Single(v => v.Id == 41).Plate.Should().Be("QA-041");
    }

    [Fact]
    public async Task Un_doublon_deja_en_base_reste_modifiable_tant_que_son_matricule_ne_change_pas()
    {
        using var ctx = await AvecAsync(Veh(40, "GA-214-RK"), Veh(45, "GA-214-RK"));

        await UpdateHandler(ctx).Handle(Update(ctx.Vehicles.Single(v => v.Id == 45), "GA-214-RK", null), CancellationToken.None);
        await PatchHandler(ctx).Handle(Patch(45, plate: "ga-214-rk"), CancellationToken.None);

        ctx.Vehicles.Single(v => v.Id == 45).Color.Should().Be("Bleu");
    }

    // ── DEF-031 : PATCH d'un véhicule d'une autre société ────────────────────

    [Fact]
    public async Task Le_patch_d_un_vehicule_d_une_autre_societe_repond_404_et_non_500()
    {
        using var ctx = await AvecAsync(Veh(20, "AUTRE-20", OtherCompanyId));

        var act = () => PatchHandler(ctx).Handle(Patch(20), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── DEF-046 : le dossier de sinistre survit à la suppression du véhicule ─

    [Fact]
    public void Les_dossiers_de_sinistre_sont_detaches_avant_toute_suppression_et_jamais_supprimes()
    {
        var statements = VehicleDeletionHelper.BuildStatements(new[]
        {
            "NO|vehicle_costs.vehicle_id",
            "YES|drivers.assigned_vehicle_id",
        });

        statements.Take(2).Should().Equal(VehicleDeletionHelper.AccidentFilePreservationSql);
        statements.Should().NotContain(s => s.Contains("DELETE") && s.Contains("accident_events"));
        statements.Should().Contain("DELETE FROM \"vehicle_costs\" WHERE \"vehicle_id\" = {0}");
        statements.Should().Contain("UPDATE \"drivers\" SET \"assigned_vehicle_id\" = NULL WHERE \"assigned_vehicle_id\" = {0}");
        statements.Last().Should().Be("DELETE FROM \"vehicles\" WHERE id = {0}");
    }

    [Fact]
    public async Task Le_sinistre_garde_un_libelle_et_ne_pointe_plus_vers_le_vehicule_supprime()
    {
        await using var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();
        await ExecAsync(conn, @"
            CREATE TABLE vehicles (id INTEGER PRIMARY KEY, name TEXT, plate_number TEXT);
            CREATE TABLE accident_events (id INTEGER PRIMARY KEY, vehicle_id INTEGER NULL, vehicle_label TEXT NULL);
            INSERT INTO vehicles VALUES (50, 'Camion 50', 'GA-214-RK'), (51, 'Autre', 'QA-051'), (52, 'Fourgon', '  ');
            INSERT INTO accident_events VALUES (1, 50, NULL), (2, 50, 'Libellé saisi'), (3, 51, NULL), (4, 52, '');");

        foreach (var vehicleId in new[] { 50, 52 })
            foreach (var sql in VehicleDeletionHelper.AccidentFilePreservationSql)
                await ExecAsync(conn, sql.Replace("{0}", "@p0"), vehicleId);

        var rows = new List<(long Id, long? VehicleId, string? Label)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, vehicle_id, vehicle_label FROM accident_events ORDER BY id";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                rows.Add((reader.GetInt64(0),
                    reader.IsDBNull(1) ? null : reader.GetInt64(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        rows.Should().Equal(
            (1L, (long?)null, "GA-214-RK"),
            (2L, (long?)null, "Libellé saisi"),
            (3L, (long?)51, (string?)null),
            (4L, (long?)null, "Fourgon"));
    }

    private static async Task ExecAsync(SqliteConnection conn, string sql, int? p0 = null)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (p0.HasValue) cmd.Parameters.AddWithValue("@p0", p0.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── DEF-054 : jours restants du bandeau d'abonnement ─────────────────────

    // Fuseaux fixes : les tests restent discriminants sur une machine à TZ=UTC,
    // où une date Kind=Local ne porte aucun décalage.
    private static readonly TimeZoneInfo PosteRecette =   // UTC+1 sans heure d'été
        TimeZoneInfo.CreateCustomTimeZone("QA UTC+1", TimeSpan.FromHours(1), "QA UTC+1", "QA UTC+1");
    private static readonly TimeZoneInfo FuseauOuest =
        TimeZoneInfo.CreateCustomTimeZone("QA UTC-5", TimeSpan.FromHours(-5), "QA UTC-5", "QA UTC-5");

    // Npgsql (mode legacy) rend un timestamptz en heure du serveur, Kind=Local.
    private static Societe EcheanceLueDans(DateTime expiresUtc, TimeZoneInfo zone) => new()
    {
        Name = "Belive GPA",
        SubscriptionExpiresAt = DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeFromUtc(expiresUtc, zone), DateTimeKind.Local)
    };

    [Fact]
    public void Les_jours_restants_ne_dependent_pas_du_fuseau_de_lecture_de_l_echeance()
    {
        // Deux instants proches d'un jour entier : l'un fait dériver un fuseau à
        // l'est d'UTC (le poste de recette), l'autre un fuseau à l'ouest.
        var expiresUtc = new DateTime(2027, 9, 10, 10, 13, 49, DateTimeKind.Utc);

        var recette = new DateTime(2026, 9, 13, 10, 35, 0, DateTimeKind.Utc);   // 361,99 j restants
        var state = SubscriptionPolicy.Evaluate(EcheanceLueDans(expiresUtc, PosteRecette), recette, PosteRecette);
        state.DaysRemaining.Should().Be(362);
        state.ExpiresAt.Should().Be(expiresUtc);
        state.ExpiresAt!.Value.Kind.Should().Be(DateTimeKind.Utc);

        var justeAvantUnJourPlein = expiresUtc.AddDays(-362).AddMinutes(-1);    // 362,0007 j restants
        SubscriptionPolicy.Evaluate(EcheanceLueDans(expiresUtc, FuseauOuest), justeAvantUnJourPlein, FuseauOuest)
            .DaysRemaining.Should().Be(363);

        // Chemin de production : fuseau du poste qui exécute le test.
        var lueEnLocal = new Societe { Name = "Belive GPA", SubscriptionExpiresAt = expiresUtc.ToLocalTime() };
        SubscriptionPolicy.Evaluate(lueEnLocal, recette).DaysRemaining.Should().Be(362);
    }

    [Fact]
    public void L_expiration_bascule_a_l_instant_reel_quel_que_soit_le_fuseau_de_lecture()
    {
        var expiresUtc = new DateTime(2026, 9, 8, 11, 56, 50, DateTimeKind.Utc);
        var societe = EcheanceLueDans(expiresUtc, PosteRecette);

        SubscriptionPolicy.Evaluate(societe, expiresUtc.AddMinutes(-1), PosteRecette).Reason.Should().Be("expiring");
        SubscriptionPolicy.Evaluate(societe, expiresUtc.AddMinutes(1), PosteRecette).Reason.Should().Be("grace");
    }
}
