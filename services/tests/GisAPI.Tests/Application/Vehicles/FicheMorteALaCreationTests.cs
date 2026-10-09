using FluentAssertions;
using GisAPI.Application.Features.Admin.Vehicles.Commands.CreateAdminVehicle;
using GisAPI.Application.Features.Admin.Vehicles.Commands.ReleaseEmptyGpsDevice;
using GisAPI.Application.Features.Admin.Vehicles.Commands.UpdateAdminVehicle;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Application.Vehicles;

/// <summary>
/// Recette du 09/10/2026 (Belive SAV, HTZ 344 / HTZ 345) : les IMEI de deux boîtiers avaient été
/// permutés à la saisie du lot du 01/10. Après suppression de HTZ 344, sa fiche boîtier (IMEI à
/// 14 chiffres, jamais connectée) survivait « non affectée » avec le MAT NR08G1115 et la SIM ;
/// le vrai boîtier d'IMEI …632, connecté le 08/10, avait reçu de l'ingestion sa propre fiche avec
/// le MAT qu'il annonce, NR08G1116. La re-création de HTZ 344 (IMEI …632 + MAT NR08G1115 + SIM)
/// était refusée « Doublon refusé : le MAT … boîtier #567111 (boîtier non affecté) », sans issue.
///
/// Deux règles en sortent : un MAT saisi qui contredit celui d'une fiche qui a communiqué se
/// SIGNALE au lieu de s'appliquer en silence ; et une fiche strictement vide qui bloque un MAT
/// ou une SIM se NOMME et se propose à la libération.
/// </summary>
public class FicheMorteALaCreationTests
{
    private const int Belive = 1;              // société par défaut de l'ingestion
    private const int BeliveSav = 6;
    private const int FicheMorte = 567111;     // ex-HTZ 344 : IMEI tronqué, jamais connectée
    private const int FicheIngestion = 577031; // créée par l'ingestion à la première trame du boîtier …632
    private const string ImeiTronque = "86014076687624";
    private const string ImeiReel = "860141076687632";
    private const string MatSaisi = "NR08G1115";
    private const string MatAnnonce = "NR08G1116";
    private const string Sim = "93094727";
    private const int Hertz = 4;               // un autre client

    // ── Contradiction entre le MAT saisi et celui de la fiche qui a communiqué ──

    [Fact]
    public async Task Un_MAT_qui_contredit_celui_de_la_fiche_qui_a_communique_est_refuse_et_nomme()
    {
        using var context = TestDbContextFactory.Create();
        await SeedSocietesAsync(context);
        context.GpsDevices.Add(FicheQuiCommunique());
        AddPosition(context, FicheIngestion);
        await context.SaveChangesAsync();

        var result = await Creer(context, imei: ImeiReel, mat: MatSaisi, sim: Sim);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("a déjà communiqué").And.Contain(MatAnnonce).And.Contain(MatSaisi);
        result.ReleaseSuggestedDeviceId.Should().BeNull();
        (await context.Vehicles.CountAsync()).Should().Be(0);

        using var fresh = Reopen(context);
        var fiche = await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == FicheIngestion);
        fiche.Mat.Should().Be(MatAnnonce, "la saisie ne remplace pas le MAT d'un boîtier qui émet");
        fiche.CompanyId.Should().Be(Belive, "rien n'est transféré sur un refus");
    }

    [Fact]
    public async Task Le_MAT_de_la_fiche_a_la_casse_pres_n_est_pas_une_contradiction()
    {
        using var context = TestDbContextFactory.Create();
        await SeedSocietesAsync(context);
        context.GpsDevices.Add(FicheQuiCommunique());
        AddPosition(context, FicheIngestion);
        await context.SaveChangesAsync();

        var result = await Creer(context, imei: ImeiReel, mat: " nr08g1116 ", sim: Sim);

        result.Success.Should().BeTrue(result.Error);
        using var fresh = Reopen(context);
        var fiche = await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == FicheIngestion);
        fiche.Mat.Should().Be(MatAnnonce, "l'orthographe du boîtier est gardée");
        fiche.CompanyId.Should().Be(BeliveSav);
        fiche.Status.Should().Be("assigned");
    }

    // ── La fiche PROPRE du véhicule reste corrigeable (cas jumeau HTZ 345) ──

    [Fact]
    public async Task Corriger_le_MAT_du_boitier_du_vehicule_reste_possible_apres_communication()
    {
        // HTZ 345 : fiche saisie avec le MAT NR08G1116 alors que le boîtier …624 annonce NR08G1115 ;
        // le boîtier « a communiqué » (positions résolues par IMEI). L'écran n'édite le MAT qu'en
        // mode « nouvel appareil » : la saisie arrive avec l'IMEI du boîtier actuel et sans id.
        using var context = TestDbContextFactory.Create();
        await SeedSocietesAsync(context);
        var fiche = Fiche(567112, "860141076687624", MatAnnonce, "93095368", BeliveSav, "assigned");
        fiche.LastCommunication = DateTime.UtcNow;
        context.GpsDevices.Add(fiche);
        context.Vehicles.Add(Vehicule(524, "HTZ 345", 567112));
        AddPosition(context, 567112);
        await context.SaveChangesAsync();

        var result = await new UpdateAdminVehicleCommandHandler(context).Handle(Modifier(524,
            gpsDeviceId: null, imei: "860141076687624", mat: MatSaisi, sim: "93095368"), CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        using var fresh = Reopen(context);
        (await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == 567112)).Mat.Should().Be(MatSaisi);
        (await fresh.Vehicles.AsNoTracking().FirstAsync(v => v.Id == 524)).GpsDeviceId.Should().Be(567112);
    }

    [Fact]
    public async Task Rattacher_une_autre_fiche_par_IMEI_avec_un_MAT_contraire_est_refuse_a_la_modification()
    {
        using var context = TestDbContextFactory.Create();
        await SeedSocietesAsync(context);
        context.GpsDevices.Add(Fiche(567111, ImeiTronque, MatSaisi, Sim, BeliveSav, "assigned"));
        context.Vehicles.Add(Vehicule(523, "HTZ 344", 567111));
        context.GpsDevices.Add(FicheQuiCommunique());
        AddPosition(context, FicheIngestion);
        await context.SaveChangesAsync();

        var result = await new UpdateAdminVehicleCommandHandler(context).Handle(Modifier(523,
            gpsDeviceId: null, imei: ImeiReel, mat: MatSaisi, sim: Sim), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("a déjà communiqué").And.Contain(MatAnnonce);
        result.ReplaceSuggested.Should().BeFalse();
        using var fresh = Reopen(context);
        (await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == FicheIngestion)).Mat.Should().Be(MatAnnonce);
        (await fresh.Vehicles.AsNoTracking().FirstAsync(v => v.Id == 523)).GpsDeviceId.Should().Be(567111);
    }

    // ── Fiche morte qui bloque la SIM (ou le MAT) ──

    [Fact]
    public async Task Une_fiche_morte_qui_bloque_le_MAT_est_proposee_a_la_liberation()
    {
        using var context = TestDbContextFactory.Create();
        await SeedSocietesAsync(context);
        context.GpsDevices.Add(FicheMorteDeHtz344());
        await context.SaveChangesAsync();

        // Boîtier neuf (IMEI valide, jamais vu) dont l'étiquette porte le MAT de la fiche morte.
        var result = await Creer(context, imei: "860141076687640", mat: MatSaisi, sim: null);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain($"le MAT « {MatSaisi} »").And.Contain("libérer");
        result.ReleaseSuggestedDeviceId.Should().Be(FicheMorte);
    }

    [Fact]
    public async Task Une_fiche_morte_a_IMEI_valide_est_proposee_aussi_sans_parler_de_forme()
    {
        using var context = TestDbContextFactory.Create();
        await SeedSocietesAsync(context);
        context.GpsDevices.Add(Fiche(FicheMorte, "860141076687624", MatSaisi, Sim, BeliveSav, "unassigned"));
        await context.SaveChangesAsync();

        var result = await Creer(context, imei: "860141076687640", mat: MatSaisi, sim: null);

        result.Success.Should().BeFalse();
        result.ReleaseSuggestedDeviceId.Should().Be(FicheMorte);
        result.Error.Should().NotContain("n'a pas la forme");
    }

    [Fact]
    public async Task Une_fiche_morte_d_une_autre_societe_est_nommee_mais_pas_proposee()
    {
        using var context = TestDbContextFactory.Create();
        await SeedSocietesAsync(context);
        context.GpsDevices.Add(Fiche(FicheMorte, ImeiTronque, MatSaisi, Sim, Hertz, "unassigned"));
        await context.SaveChangesAsync();

        var result = await Creer(context, imei: "860141076687640", mat: MatSaisi, sim: null);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("HERTZ").And.Contain("appartient à la société").And.NotContain("Vous pouvez la libérer");
        result.ReleaseSuggestedDeviceId.Should().BeNull();
    }

    [Fact]
    public async Task Un_doublon_d_IMEI_n_est_jamais_propose_a_la_liberation()
    {
        // Fiche pré-enregistrée (jamais connectée) ; l'IMEI est retapé avec des espaces internes :
        // le résolveur ne la retrouve pas à l'exact, le garde la voit en normalisé. C'est l'IMEI
        // à corriger, pas une fiche à supprimer.
        using var context = TestDbContextFactory.Create();
        await SeedSocietesAsync(context);
        context.GpsDevices.Add(Fiche(FicheMorte, ImeiReel, null, null, BeliveSav, "unassigned"));
        await context.SaveChangesAsync();

        var result = await Creer(context, imei: "860 141 076 687 632", mat: null, sim: null);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("IMEI");
        result.ReleaseSuggestedDeviceId.Should().BeNull();
        (await context.GpsDevices.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Une_fiche_morte_qui_bloque_la_SIM_est_nommee_et_proposee_a_la_liberation()
    {
        using var context = TestDbContextFactory.Create();
        await SeedSocietesAsync(context);
        context.GpsDevices.Add(FicheMorteDeHtz344());
        context.GpsDevices.Add(FicheQuiCommunique());
        AddPosition(context, FicheIngestion);
        await context.SaveChangesAsync();

        // MAT laissé vide (la fiche de l'ingestion garde NR08G1116) : seule la SIM bloque.
        var result = await Creer(context, imei: ImeiReel, mat: null, sim: Sim);

        result.Success.Should().BeFalse();
        result.Error.Should().StartWith("Doublon refusé")
            .And.Contain($"#{FicheMorte}")
            .And.Contain("n'a jamais communiqué")
            .And.Contain("n'a pas la forme d'un IMEI valide")
            .And.Contain("libérer");
        result.ReleaseSuggestedDeviceId.Should().Be(FicheMorte);
        (await context.Vehicles.CountAsync()).Should().Be(0);

        using var fresh = Reopen(context);
        (await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == FicheIngestion)).CompanyId
            .Should().Be(Belive, "rien n'est persisté sur un refus");
    }

    [Fact]
    public async Task Une_fiche_en_conflit_qui_a_des_donnees_n_est_pas_proposee_a_la_liberation()
    {
        using var context = TestDbContextFactory.Create();
        await SeedSocietesAsync(context);
        var fiche = Fiche(10, "IMEI-AVEC-HISTORIQUE", "MAT-H", Sim, BeliveSav, "unassigned");
        context.GpsDevices.Add(fiche);
        AddPosition(context, 10);
        await context.SaveChangesAsync();

        var result = await Creer(context, imei: "IMEI-NEUF", mat: null, sim: Sim);

        result.Success.Should().BeFalse();
        result.ReleaseSuggestedDeviceId.Should().BeNull();
        result.Error.Should().NotContain("libérer");
    }

    [Fact]
    public async Task Une_fiche_en_conflit_qui_a_communique_n_est_pas_proposee_a_la_liberation()
    {
        using var context = TestDbContextFactory.Create();
        await SeedSocietesAsync(context);
        var fiche = Fiche(10, "IMEI-VU", "MAT-V", Sim, BeliveSav, "unassigned");
        fiche.LastCommunication = DateTime.UtcNow;
        context.GpsDevices.Add(fiche);
        await context.SaveChangesAsync();

        var result = await Creer(context, imei: "IMEI-NEUF", mat: null, sim: Sim);

        result.Success.Should().BeFalse();
        result.ReleaseSuggestedDeviceId.Should().BeNull();
    }

    [Fact]
    public async Task Une_fiche_en_conflit_rattachee_a_un_vehicule_n_est_pas_proposee_a_la_liberation()
    {
        using var context = TestDbContextFactory.Create();
        await SeedSocietesAsync(context);
        context.GpsDevices.Add(Fiche(10, "IMEI-RATTACHE", "MAT-R", Sim, BeliveSav, "assigned"));
        context.Vehicles.Add(Vehicule(50, "HTZ 345", 10));
        await context.SaveChangesAsync();

        var result = await Creer(context, imei: "IMEI-NEUF", mat: null, sim: Sim);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("HTZ 345");
        result.ReleaseSuggestedDeviceId.Should().BeNull();
    }

    // ── Libération d'une fiche vide ──

    [Fact]
    public async Task Liberer_une_fiche_morte_la_supprime()
    {
        using var context = TestDbContextFactory.Create();
        await SeedSocietesAsync(context);
        context.GpsDevices.Add(FicheMorteDeHtz344());
        await context.SaveChangesAsync();

        var result = await Liberer(context, FicheMorte);

        result.Success.Should().BeTrue(result.Message);
        result.Message.Should().Contain($"#{FicheMorte}").And.Contain(ImeiTronque);
        (await context.GpsDevices.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Liberer_refuse_une_fiche_d_une_autre_societe_que_celle_du_vehicule()
    {
        using var context = TestDbContextFactory.Create();
        await SeedSocietesAsync(context);
        context.GpsDevices.Add(Fiche(FicheMorte, ImeiTronque, MatSaisi, Sim, Hertz, "unassigned"));
        await context.SaveChangesAsync();

        var result = await Liberer(context, FicheMorte, pourSociete: BeliveSav);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("autre société");
        (await context.GpsDevices.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Liberer_accepte_une_fiche_de_la_societe_par_defaut_de_l_ingestion()
    {
        using var context = TestDbContextFactory.Create();
        await SeedSocietesAsync(context);
        context.GpsDevices.Add(Fiche(FicheMorte, ImeiTronque, MatSaisi, Sim, Belive, "unassigned"));
        await context.SaveChangesAsync();

        var result = await Liberer(context, FicheMorte, pourSociete: BeliveSav);

        result.Success.Should().BeTrue(result.Message);
        (await context.GpsDevices.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Liberer_refuse_une_fiche_qui_a_communique()
    {
        using var context = TestDbContextFactory.Create();
        await SeedSocietesAsync(context);
        var fiche = FicheMorteDeHtz344();
        fiche.LastCommunication = DateTime.UtcNow;
        context.GpsDevices.Add(fiche);
        await context.SaveChangesAsync();

        var result = await Liberer(context, FicheMorte);

        result.Success.Should().BeFalse();
        result.NotFound.Should().BeFalse();
        result.Message.Should().Contain("a déjà communiqué");
        (await context.GpsDevices.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Liberer_refuse_une_fiche_avec_positions()
    {
        using var context = TestDbContextFactory.Create();
        await SeedSocietesAsync(context);
        context.GpsDevices.Add(FicheMorteDeHtz344());
        AddPosition(context, FicheMorte);
        await context.SaveChangesAsync();

        var result = await Liberer(context, FicheMorte);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("position");
        (await context.GpsDevices.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Liberer_refuse_une_fiche_rattachee_a_un_vehicule()
    {
        using var context = TestDbContextFactory.Create();
        await SeedSocietesAsync(context);
        context.GpsDevices.Add(FicheMorteDeHtz344());
        context.Vehicles.Add(Vehicule(50, "HTZ 344", FicheMorte));
        await context.SaveChangesAsync();

        var result = await Liberer(context, FicheMorte);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("véhicule");
        (await context.GpsDevices.CountAsync()).Should().Be(1);
        (await context.Vehicles.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Liberer_une_fiche_inconnue_rend_introuvable()
    {
        using var context = TestDbContextFactory.Create();
        await SeedSocietesAsync(context);

        var result = await Liberer(context, 424242);

        result.Success.Should().BeFalse();
        result.NotFound.Should().BeTrue();
    }

    // ── Le déroulé complet de la recette ──

    [Fact]
    public async Task Apres_liberation_la_creation_reutilise_la_fiche_de_l_ingestion()
    {
        using var context = TestDbContextFactory.Create();
        await SeedSocietesAsync(context);
        context.GpsDevices.Add(FicheMorteDeHtz344());
        context.GpsDevices.Add(FicheQuiCommunique());
        AddPosition(context, FicheIngestion);
        await context.SaveChangesAsync();

        var liberation = await Liberer(context, FicheMorte, pourSociete: BeliveSav);
        liberation.Success.Should().BeTrue(liberation.Message);

        var result = await Creer(context, imei: ImeiReel, mat: null, sim: Sim);

        result.Success.Should().BeTrue(result.Error);
        using var fresh = Reopen(context);
        (await fresh.GpsDevices.CountAsync()).Should().Be(1, "la fiche de l'ingestion est réutilisée, rien n'est créé");
        var fiche = await fresh.GpsDevices.AsNoTracking().FirstAsync();
        fiche.Id.Should().Be(FicheIngestion);
        fiche.CompanyId.Should().Be(BeliveSav);
        fiche.Status.Should().Be("assigned");
        fiche.Mat.Should().Be(MatAnnonce);
        fiche.SimNumber.Should().Be(Sim);
        (await fresh.Vehicles.AsNoTracking().FirstAsync()).GpsDeviceId.Should().Be(FicheIngestion);
    }

    // ── Aides ──

    private static Task<ReleaseEmptyGpsDeviceResult> Liberer(TestGisDbContext context, int deviceId, int? pourSociete = null) =>
        new ReleaseEmptyGpsDeviceCommandHandler(context, NullLogger<ReleaseEmptyGpsDeviceCommandHandler>.Instance)
            .Handle(new ReleaseEmptyGpsDeviceCommand(deviceId, pourSociete), CancellationToken.None);

    private static UpdateAdminVehicleCommand Modifier(int vehicleId, int? gpsDeviceId, string? imei, string? mat, string? sim) => new(
        vehicleId, null, null, null, null, null, null, null, null, true, null, null, null, null,
        GpsDeviceId: gpsDeviceId, GpsImei: imei, GpsMat: mat,
        GpsBrand: null, GpsModel: null, GpsFirmwareVersion: null, GpsFuelSensorMode: null,
        GpsSimNumber: sim, GpsSimOperator: null, GpsInstallationDate: null);

    private static Task<CreateAdminVehicleResult> Creer(TestGisDbContext context, string imei, string? mat, string? sim) =>
        new CreateAdminVehicleCommandHandler(context).Handle(new CreateAdminVehicleCommand(
            Name: "HTZ 344", Type: "voiture", Brand: null, Model: null, Plate: "HTZ 344", Year: 2024, Color: null,
            Status: "available", HasGps: true, Mileage: 0, FuelType: "diesel", FuelTankCapacity: null,
            CompanyId: BeliveSav, GpsDeviceId: null, GpsImei: imei, GpsMat: mat,
            GpsBrand: "NEMS", GpsModel: "L", GpsFirmwareVersion: null, GpsFuelSensorMode: "liters",
            GpsSimNumber: sim, GpsSimOperator: "tunisie_telecom", GpsInstallationDate: null),
            CancellationToken.None);

    /// <summary>Ancienne fiche de HTZ 344 : IMEI à 14 chiffres, jamais connectée, détachée.</summary>
    private static GpsDevice FicheMorteDeHtz344() =>
        Fiche(FicheMorte, ImeiTronque, MatSaisi, Sim, BeliveSav, "unassigned");

    /// <summary>Fiche créée par l'ingestion à la première trame du boîtier …632 (MAT annoncé NR08G1116).</summary>
    private static GpsDevice FicheQuiCommunique()
    {
        var fiche = Fiche(FicheIngestion, ImeiReel, MatAnnonce, null, Belive, "unassigned");
        fiche.LastCommunication = new DateTime(2026, 10, 8, 10, 26, 17, DateTimeKind.Utc);
        return fiche;
    }

    private static GpsDevice Fiche(int id, string imei, string? mat, string? sim, int companyId, string status) => new()
    {
        Id = id,
        DeviceUid = imei,
        Mat = mat,
        SimNumber = sim,
        CompanyId = companyId,
        Status = status,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static Vehicle Vehicule(int id, string plate, int deviceId) => new()
    {
        Id = id,
        Name = plate,
        Plate = plate,
        Type = "voiture",
        Status = "available",
        CompanyId = BeliveSav,
        GpsDeviceId = deviceId,
        HasGps = true,
        Mileage = 0,
        FuelType = "diesel"
    };

    private static void AddPosition(TestGisDbContext context, int deviceId) =>
        context.GpsPositions.Add(new GpsPosition
        {
            DeviceId = deviceId,
            RecordedAt = new DateTime(2026, 10, 8, 10, 25, 53, DateTimeKind.Utc),
            Latitude = 36.8,
            Longitude = 10.18,
            IsValid = true
        });

    private static async Task SeedSocietesAsync(TestGisDbContext context)
    {
        context.SubscriptionTypes.Add(TestDataBuilder.CreateSubscriptionType());
        context.Societes.Add(TestDataBuilder.CreateSociete(id: Belive, subscriptionTypeId: 1));
        context.Societes.Add(TestDataBuilder.CreateSociete(id: BeliveSav, subscriptionTypeId: 1));
        var hertz = TestDataBuilder.CreateSociete(id: Hertz, subscriptionTypeId: 1);
        hertz.Name = "HERTZ";
        context.Societes.Add(hertz);
        await context.SaveChangesAsync();
    }

    private static TestGisDbContext Reopen(TestGisDbContext context)
    {
        var options = new DbContextOptionsBuilder<TestGisDbContext>()
            .UseSqlite(context.Database.GetDbConnection())
            .Options;
        return new TestGisDbContext(options);
    }
}
