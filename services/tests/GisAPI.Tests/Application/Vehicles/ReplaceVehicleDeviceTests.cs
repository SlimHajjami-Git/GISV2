using FluentAssertions;
using GisAPI.Application.Features.Admin.Vehicles.Commands.CreateAdminVehicle;
using GisAPI.Application.Features.Admin.Vehicles.Commands.ReplaceVehicleDevice;
using GisAPI.Application.Features.Admin.Vehicles.Commands.UpdateAdminVehicle;
using GisAPI.Application.Features.Admin.Vehicles.Services;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace GisAPI.Tests.Application.Vehicles;

/// <summary>
/// Boucle « Doublon refusé » constatée en prod le 14/09/2026 sur HTZ 278, 255 et 292.
///
/// La fiche boîtier du véhicule avait été créée avec un IMEI mal recopié (clé de
/// Luhn fausse, jamais une trame) ; le vrai boîtier émettait sous une fiche créée
/// automatiquement par l'ingestion (même MAT, société BELIVE par défaut, aucun
/// véhicule). Ni la modification du véhicule, ni le remplacement de boîtier ne
/// passaient : le remplacement ne savait garder que la fiche DU VÉHICULE.
///
/// Données reproduites à l'identique (ids, IMEI, MAT, SIM, sociétés). Les rejeux du
/// formulaire se font dans un contexte neuf, comme la requête HTTP suivante en prod.
/// </summary>
public class ReplaceVehicleDeviceTests
{
    // HTZ 278 — société 6 BELIVE SAV
    private const int Htz278 = 355;
    private const int WrongDevice278 = 382060;
    private const string WrongImei278 = "860141078677153";
    private const int RealDevice278 = 384940;
    private const string RealImei278 = "860141076677153";
    private const string Mat278 = "NR08G1040";

    // HTZ 255 — société 6 BELIVE SAV
    private const int Htz255 = 328;
    private const int WrongDevice255 = 319546;
    private const string WrongImei255 = "860141076873814";
    private const int RealDevice255 = 340768;
    private const string RealImei255 = "860141076673814";
    private const string Mat255 = "NR08G0935";
    private const string Sim255 = "92005328";

    private const int BeliveSav = 6;
    private const int Belive = 1;
    private const int OtherClient = 7;

    // ── Contrôle IMEI (Luhn) ──

    [Theory]
    [InlineData("860141078677153", false)] // HTZ 278, saisi par erreur
    [InlineData("860141076677153", true)]  // HTZ 278, vrai boîtier
    [InlineData("860141076873814", false)] // HTZ 255, saisi par erreur
    [InlineData("860141076673814", true)]  // HTZ 255, vrai boîtier
    [InlineData("860141075677286", false)] // HTZ 292, saisi par erreur
    [InlineData("860141076677286", true)]  // HTZ 292, vrai boîtier
    [InlineData("8601410766", false)]      // device_uid de 10 caractères
    [InlineData("0123456789abcdef0123456789abcdef", false)] // device_uid de 32 caractères
    [InlineData("86014107667715A", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidImei_ChecksFifteenDigitsAndLuhn(string? imei, bool expected)
    {
        GpsDeviceUniquenessGuard.IsValidImei(imei).Should().Be(expected);
    }

    [Theory]
    // Conservation demandée (valeur du boîtier, saisie faite pour une autre fiche).
    [InlineData("NR08G1040", "nr08g1040 ", true, "NR08G1040")] // casse et espace : orthographe du boîtier gardée
    [InlineData("860141076677153", " 860 141 076 677 153 ", true, "860141076677153")]
    [InlineData("NR08G1040", " NR08G1054 ", true, "NR08G1054")] // vraie modification : saisie sans espaces de bord
    [InlineData(null, " NR08G1054", true, "NR08G1054")]
    [InlineData("HTZ278 ", "HTZ278", true, "HTZ278")] // valeur stockée à espace de bord : jamais celle du boîtier
    // Sans conservation (l'opérateur modifie la fiche de son formulaire) : la saisie gagne.
    [InlineData("htz278", "HTZ278", false, "HTZ278")]
    [InlineData("HTZ 278", " HTZ278 ", false, "HTZ278")]
    public void StoredValueFor_KeepsDeviceSpellingOnlyWhenAsked(string? stored, string input, bool keep, string expected)
    {
        GpsDeviceUniquenessGuard.StoredValueFor(stored, input, keep).Should().Be(expected);
    }

    // ── Garde-fou : variante structurée et fiches ignorées ──

    [Fact]
    public async Task FindConflictDetail_ReturnsStructuredConflict_AndHonoursIgnoredDevices()
    {
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);

        var conflict = await GpsDeviceUniquenessGuard.FindConflictDetailAsync(
            context, WrongDevice278, WrongImei278, Mat278, null);

        conflict.Should().NotBeNull();
        conflict!.DeviceId.Should().Be(RealDevice278);
        conflict.Identifier.Should().Be(GpsDeviceConflict.Mat);
        conflict.DeviceImei.Should().Be(RealImei278);
        conflict.VehicleId.Should().BeNull();
        conflict.Message.Should().Be(
            $"Doublon refusé : le MAT « {Mat278} » est déjà utilisé par le boîtier #{RealDevice278} (boîtier non affecté).");

        var ignored = await GpsDeviceUniquenessGuard.FindConflictAsync(
            context, WrongDevice278, WrongImei278, Mat278, null, ignoreDeviceIds: new[] { RealDevice278 });
        ignored.Should().BeNull();
    }

    // ── La boucle, telle que l'opérateur la vivait ──

    // ── Recette du 21/09/2026 : un doublon DÉJÀ en base ne bloque plus le véhicule ──
    //
    // Jusqu'ici, le formulaire renvoyant toujours l'IMEI, le MAT et la SIM affichés, le
    // contrôle anti-doublons les revérifiait à chaque enregistrement : un MAT partagé en
    // base refusait TOUTE modification du véhicule (couleur, kilométrage, chauffeur…),
    // sans aucune issue quand l'autre fiche avait de l'historique. Sur TN : 261 TU 4113 /
    // 261 TU 4109 (même MAT, deux boîtiers qui émettent), 237 TU 8371 (MAT partagé avec une
    // fiche fantôme), HTZ 316 (IMEI mal saisi). Désormais seuls les identifiants que la
    // saisie CHANGE sur le boîtier actuel sont contrôlés ; le passage à un autre boîtier
    // garde le contrôle complet et le remplacement (cas B plus bas, inchangés).

    [Fact]
    public async Task Recette2109_EditingVehicleWithoutTouchingGps_Succeeds_DespiteMatSharedWithRealDevice()
    {
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);

        // Formulaire renvoyé tel quel : boîtier actuel, IMEI (faux), MAT partagé, kilométrage modifié.
        var result = await new UpdateAdminVehicleCommandHandler(context).Handle(
            FormUpdate(Htz278, gpsDeviceId: WrongDevice278, imei: WrongImei278, mat: Mat278, sim: null, mileage: 12345),
            CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        // Le seul signal d’un IMEI mal saisi reste visible : non bloquant, avec la consigne.
        result.Vehicle!.Warning.Should()
            .StartWith($"Modification enregistrée. Attention : le MAT « {Mat278} » de ce boîtier est aussi porté par le boîtier #{RealDevice278} (boîtier non affecté)")
            .And.Contain($"L'IMEI de ce véhicule ({WrongImei278}) semble mal saisi (chiffre de contrôle invalide)")
            .And.Contain($"remplacez l'IMEI par {RealImei278}");
        using var fresh = Reopen(context);
        (await fresh.Vehicles.AsNoTracking().FirstAsync(v => v.Id == Htz278)).Mileage.Should().Be(12345);
        // Aucune fiche touchée : le doublon historique reste à corriger à part (remplacement).
        (await fresh.Vehicles.AsNoTracking().FirstAsync(v => v.Id == Htz278)).GpsDeviceId.Should().Be(WrongDevice278);
        (await fresh.GpsDevices.CountAsync()).Should().Be(2);
        var real = await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == RealDevice278);
        (real.CompanyId, real.Status).Should().Be((Belive, "unassigned"));
        (await fresh.GpsPositions.CountAsync(p => p.DeviceId == RealDevice278)).Should().Be(352);
    }

    [Fact]
    public async Task Recette2109_TwoAssignedVehiclesSharingAMat_BothStayEditable()
    {
        // 261 TU 4113 / 261 TU 4109 (HERTZ) : deux boîtiers qui émettent, un MAT saisi deux fois.
        using var context = TestDbContextFactory.Create();
        await SeedCompaniesAsync(context);
        context.GpsDevices.Add(Device(202124, "860141076675512", "NR08G0844", "99108859", BeliveSav, "assigned"));
        context.GpsDevices.Add(Device(202129, "860141076676742", "NR08G0844", "99112866", BeliveSav, "assigned"));
        context.Vehicles.Add(Vehicle(195, "261 TU 4113", 202124));
        context.Vehicles.Add(Vehicle(197, "261 TU 4109", 202129));
        AddPositions(context, 202124, 20);
        AddPositions(context, 202129, 20);
        await context.SaveChangesAsync();

        foreach (var (vehicule, boitier, imei, sim) in new[]
                 { (195, 202124, "860141076675512", "99108859"), (197, 202129, "860141076676742", "99112866") })
        {
            var result = await new UpdateAdminVehicleCommandHandler(context).Handle(
                FormUpdate(vehicule, gpsDeviceId: boitier, imei: imei, mat: "NR08G0844", sim: sim, mileage: 50_000),
                CancellationToken.None);
            result.Success.Should().BeTrue($"le véhicule {vehicule} doit rester modifiable : {result.Error}");
        }

        using var fresh = Reopen(context);
        (await fresh.Vehicles.AsNoTracking().Where(v => v.Mileage == 50_000).CountAsync()).Should().Be(2);
        (await fresh.GpsDevices.CountAsync()).Should().Be(2, "aucune fiche créée ni supprimée");
    }

    [Fact]
    public async Task Recette2109_ChangingOnlyTheSim_ChecksTheSim_NotTheUnchangedSharedMat()
    {
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);

        // Nouvelle SIM, libre : l'enregistrement passe, le MAT partagé (inchangé) n'est pas rejugé.
        var ok = await new UpdateAdminVehicleCommandHandler(context).Handle(
            FormUpdate(Htz278, gpsDeviceId: WrongDevice278, imei: WrongImei278, mat: Mat278, sim: "92111222", mileage: 1000),
            CancellationToken.None);
        ok.Success.Should().BeTrue(ok.Error);
        using (var fresh = Reopen(context))
            (await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == WrongDevice278)).SimNumber.Should().Be("92111222");

        // SIM d'un AUTRE boîtier : toujours refusée — le contrôle porte sur ce qui change.
        context.GpsDevices.Add(Device(777001, "860141076600001", "NR08G7001", "92999888", BeliveSav, "unassigned"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var refus = await new UpdateAdminVehicleCommandHandler(context).Handle(
            FormUpdate(Htz278, gpsDeviceId: WrongDevice278, imei: WrongImei278, mat: Mat278, sim: "92999888", mileage: 1000),
            CancellationToken.None);
        refus.Success.Should().BeFalse();
        refus.Error.Should().StartWith("Doublon refusé : le numéro SIM « 92999888 » est déjà utilisé par le boîtier #777001");
    }

    [Fact]
    public async Task Recette2109_ChangingTheMatToAnotherDevicesMat_IsStillRefused()
    {
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);
        context.GpsDevices.Add(Device(777002, "860141076600002", "NR08G7002", null, BeliveSav, "assigned"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var result = await new UpdateAdminVehicleCommandHandler(context).Handle(
            FormUpdate(Htz278, gpsDeviceId: WrongDevice278, imei: WrongImei278, mat: "NR08G7002", sim: null, mileage: 12345),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().StartWith("Doublon refusé : le MAT « NR08G7002 » est déjà utilisé par le boîtier #777002");
        using var fresh = Reopen(context);
        (await fresh.Vehicles.AsNoTracking().FirstAsync(v => v.Id == Htz278)).Mileage.Should().Be(1000, "rien n'est enregistré");
    }

    [Fact]
    public async Task Recette2109_SimAlreadySharedWithAnotherVehicle_DoesNotBlockAnUnrelatedEdit()
    {
        // Doublon historique de SIM entre deux véhicules (cas 92002732, HTZ 159 / 262 TU 8165).
        using var context = TestDbContextFactory.Create();
        await SeedCompaniesAsync(context);
        context.GpsDevices.Add(Device(WrongDevice278, WrongImei278, Mat278, "92002732", BeliveSav, "assigned"));
        context.GpsDevices.Add(Device(RealDevice278, "860141076677286", "NR08G9999", "92002732", BeliveSav, "assigned"));
        context.Vehicles.Add(Vehicle(Htz278, "HTZ 278", WrongDevice278));
        context.Vehicles.Add(Vehicle(999, "262 TU 8165", RealDevice278));
        AddPositions(context, RealDevice278, 10);
        await context.SaveChangesAsync();

        var result = await new UpdateAdminVehicleCommandHandler(context).Handle(
            FormUpdate(Htz278, gpsDeviceId: WrongDevice278, imei: WrongImei278, mat: Mat278, sim: "92002732", mileage: 12345),
            CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        result.Vehicle!.Warning.Should().Contain("(véhicule 262 TU 8165)")
            .And.NotContain("semble mal saisi", "conseiller son IMEI mènerait à voler le boîtier d'un autre véhicule");
        using var fresh = Reopen(context);
        (await fresh.Vehicles.AsNoTracking().FirstAsync(v => v.Id == 999)).GpsDeviceId.Should().Be(RealDevice278,
            "l'autre véhicule garde son boîtier");
    }

    [Fact]
    public async Task Recette2109_ValidImei_WarnsAboutTheInheritedDuplicate_WithoutTheTypoHint()
    {
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);
        var current = await context.GpsDevices.FirstAsync(d => d.Id == WrongDevice278);
        current.DeviceUid = "860141076677286";   // IMEI valide : vrai doublon de MAT, pas une faute de recopie
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var result = await new UpdateAdminVehicleCommandHandler(context).Handle(
            FormUpdate(Htz278, gpsDeviceId: WrongDevice278, imei: "860141076677286", mat: Mat278, sim: null, mileage: 12345),
            CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        result.Vehicle!.Warning.Should().Contain("doublon déjà présent en base").And.NotContain("semble mal saisi");
    }

    [Fact]
    public async Task Recette2109_CurrentDeviceFoundByImei_NewMatOfAnotherDevice_IsStillRefused()
    {
        // Relecture du 21/09 : sans gpsDeviceId, le résolveur retrouve le boîtier actuel par son
        // IMEI et réécrit aussitôt son MAT. Relevé APRÈS, le nouveau MAT passait pour inchangé
        // et créait un doublon avec un autre boîtier. La « valeur avant » est relevée en base.
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);
        context.GpsDevices.Add(Device(777003, "860141076600003", "NR08G7003", null, BeliveSav, "assigned"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var result = await new UpdateAdminVehicleCommandHandler(context).Handle(
            FormUpdate(Htz278, gpsDeviceId: null, imei: WrongImei278, mat: "NR08G7003", sim: null, mileage: 12345),
            CancellationToken.None);

        result.Success.Should().BeFalse("le MAT NR08G7003 appartient au boîtier #777003");
        result.Error.Should().StartWith("Doublon refusé : le MAT « NR08G7003 » est déjà utilisé par le boîtier #777003");
        using var fresh = Reopen(context);
        (await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == WrongDevice278)).Mat.Should().Be(Mat278);
    }

    [Fact]
    public async Task CaseB_CorrectingImei_MessageSaysExactlyWhatTheReplacementWillDo()
    {
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);

        // L'opérateur passe en « nouvel appareil » et saisit le bon IMEI.
        var result = await new UpdateAdminVehicleCommandHandler(context).Handle(
            FormUpdate(Htz278, gpsDeviceId: null, imei: RealImei278, mat: Mat278, sim: null, mileage: 12345),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ReplaceSuggested.Should().BeTrue();
        result.Error.Should().StartWith(
            $"Doublon refusé : le MAT « {Mat278} » est aussi porté par le boîtier actuel de ce véhicule (#{WrongDevice278}, IMEI {WrongImei278}).");
        result.Error.Should()
            .Contain($"Si vous confirmez le remplacement, « HTZ 278 » sera rattaché au boîtier #{RealDevice278} (IMEI {RealImei278}), " +
                     $"qui porte l'historique (352 position(s)), et sa fiche actuelle #{WrongDevice278} (IMEI {WrongImei278}), strictement vide, sera supprimée.")
            .And.Contain("Le boîtier passera de la société « BELIVE » à « BELIVE SAV ».")
            .And.NotContain("(véhicule HTZ 278)");

        var vehicle = await context.Vehicles.AsNoTracking().FirstAsync(v => v.Id == Htz278);
        vehicle.GpsDeviceId.Should().Be(WrongDevice278);
    }

    [Fact]
    public async Task CaseB_FormStillCarriesOldDeviceId_ConflictOnImei_ReplacementProposed()
    {
        // Popup passé en « nouvel appareil » sans vider gpsDeviceId (liste d'appareils vide).
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);

        var result = await new UpdateAdminVehicleCommandHandler(context).Handle(
            FormUpdate(Htz278, gpsDeviceId: WrongDevice278, imei: RealImei278, mat: Mat278, sim: null, mileage: 12345),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ReplaceSuggested.Should().BeTrue();
        result.Error.Should().StartWith($"Doublon refusé : l'IMEI « {RealImei278} » est déjà utilisé par le boîtier #{RealDevice278}")
            .And.Contain($"sera rattaché au boîtier #{RealDevice278}");
        (await context.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == WrongDevice278)).DeviceUid.Should().Be(WrongImei278);
    }

    [Fact]
    public async Task SecondTypo_UnknownImei_NoReplacementProposed_MessageSaysWhy()
    {
        // IMEI retapé avec une autre faute : la fiche qui émet porte le même MAT et de
        // l'historique, un remplacement échouerait — il n'est pas proposé.
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);

        var result = await new UpdateAdminVehicleCommandHandler(context).Handle(
            FormUpdate(Htz278, gpsDeviceId: null, imei: "860141076677154", mat: Mat278, sim: null, mileage: 12345),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ReplaceSuggested.Should().BeFalse();
        result.Error.Should().StartWith($"Doublon refusé : le MAT « {Mat278} » est aussi porté par le boîtier actuel de ce véhicule (#{WrongDevice278}")
            .And.Contain($"Remplacement impossible : le boîtier #{RealDevice278} porte déjà cet identifiant et contient des données (352 position(s))")
            .And.NotContain("Si vous confirmez");
    }

    [Fact]
    public async Task OldLoop_ReplacingWithTheUnchangedWrongImei_StillRefusesAndDeletesNothing()
    {
        // L'écran ne propose plus ce remplacement, mais le serveur doit rester sûr s'il
        // est appelé : la fiche qui émet n'est jamais supprimée.
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);

        var result = await new ReplaceVehicleDeviceCommandHandler(context).Handle(
            new ReplaceVehicleDeviceCommand(Htz278, WrongImei278, NewMat: Mat278), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain($"#{RealDevice278}").And.Contain("contient des données");
        (await context.GpsDevices.CountAsync()).Should().Be(2);
    }

    // ── Rattachement ──

    [Fact]
    public async Task Attach_Htz278_RattachesVehicleToEmittingDevice_DeletesEmptyOne_ThenFormReplaySucceeds()
    {
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);

        var replace = await new ReplaceVehicleDeviceCommandHandler(context).Handle(
            new ReplaceVehicleDeviceCommand(Htz278, RealImei278, NewMat: Mat278), CancellationToken.None);

        replace.Success.Should().BeTrue(replace.Message);
        replace.Mode.Should().Be(ReplaceVehicleDeviceCommandHandler.ModeAttach);
        replace.DeviceId.Should().Be(RealDevice278);
        replace.ReleasedDeviceId.Should().Be(WrongDevice278);
        replace.Message.Should()
            .StartWith($"Boîtier corrigé : « HTZ 278 » est rattaché au boîtier #{RealDevice278} (IMEI {RealImei278}), " +
                       "dont les positions suivent désormais le véhicule (352 position(s)).")
            .And.Contain($"La fiche #{WrongDevice278} (IMEI {WrongImei278}, jamais utilisée) a été supprimée.")
            .And.Contain("de la société « BELIVE » vers « BELIVE SAV »")
            .And.NotContain("Seules les positions", "la fiche conservée ne porte que des positions");

        using (var fresh = Reopen(context))
        {
            var vehicle = await fresh.Vehicles.AsNoTracking().FirstAsync(v => v.Id == Htz278);
            vehicle.GpsDeviceId.Should().Be(RealDevice278);
            vehicle.HasGps.Should().BeTrue();

            (await fresh.GpsDevices.AnyAsync(d => d.Id == WrongDevice278)).Should().BeFalse();
            var real = await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == RealDevice278);
            real.CompanyId.Should().Be(BeliveSav);
            real.Status.Should().Be("assigned");
            real.DeviceUid.Should().Be(RealImei278);
            real.Mat.Should().Be(Mat278);
            (await fresh.GpsPositions.CountAsync(p => p.DeviceId == RealDevice278)).Should().Be(352);
        }

        // Rejeu du formulaire par l'écran, nouvelle requête : il désigne la fiche retenue
        // (res.deviceId), jamais la fiche supprimée restée dans le formulaire.
        using (var replayContext = Reopen(context))
        {
            var replay = await new UpdateAdminVehicleCommandHandler(replayContext).Handle(
                FormUpdate(Htz278, gpsDeviceId: replace.DeviceId, imei: RealImei278, mat: Mat278, sim: null, mileage: 12345),
                CancellationToken.None);

            replay.Success.Should().BeTrue(replay.Error);
            replay.Vehicle!.GpsDeviceId.Should().Be(RealDevice278);
        }

        using var check = Reopen(context);
        (await check.Vehicles.AsNoTracking().FirstAsync(v => v.Id == Htz278)).Mileage.Should().Be(12345);
    }

    [Fact]
    public async Task Attach_Htz255_TakesSimFromEmptyDevice_ThenFormReplayWithSimSucceeds()
    {
        using var context = TestDbContextFactory.Create();
        await SeedCompaniesAsync(context);
        context.GpsDevices.Add(Device(WrongDevice255, WrongImei255, Mat255, Sim255, BeliveSav, "assigned"));
        context.GpsDevices.Add(Device(RealDevice255, RealImei255, Mat255, null, Belive, "unassigned"));
        context.Vehicles.Add(Vehicle(Htz255, "HTZ 255", WrongDevice255));
        AddPositions(context, RealDevice255, 782);
        await context.SaveChangesAsync();

        // Boîte de dialogue sans SIM saisie : la SIM doit venir de la fiche supprimée.
        var replace = await new ReplaceVehicleDeviceCommandHandler(context).Handle(
            new ReplaceVehicleDeviceCommand(Htz255, RealImei255, NewMat: Mat255), CancellationToken.None);

        replace.Success.Should().BeTrue(replace.Message);
        replace.Mode.Should().Be(ReplaceVehicleDeviceCommandHandler.ModeAttach);
        replace.Message.Should().Contain("782 position(s)");

        using (var fresh = Reopen(context))
        {
            var real = await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == RealDevice255);
            real.SimNumber.Should().Be(Sim255);
            real.CompanyId.Should().Be(BeliveSav);
            (await fresh.GpsDevices.AnyAsync(d => d.Id == WrongDevice255)).Should().BeFalse();
            (await fresh.Vehicles.AsNoTracking().FirstAsync(v => v.Id == Htz255)).GpsDeviceId.Should().Be(RealDevice255);
        }

        // Rejeu sans gpsDeviceId (mode « nouvel appareil ») dans un contexte neuf.
        using var replayContext = Reopen(context);
        var replay = await new UpdateAdminVehicleCommandHandler(replayContext).Handle(
            FormUpdate(Htz255, gpsDeviceId: null, imei: RealImei255, mat: Mat255, sim: Sim255, mileage: 54321),
            CancellationToken.None);

        replay.Success.Should().BeTrue(replay.Error);
        replay.Vehicle!.GpsSimNumber.Should().Be(Sim255);
        replay.Vehicle.GpsDeviceId.Should().Be(RealDevice255);
    }

    [Fact]
    public async Task Attach_WithForeignKeysEnforced_UpdatesVehicleBeforeDeletingItsDevice()
    {
        // Les autres tests tournent sans contrôle des clés étrangères : celui-ci vérifie
        // que l'ordre des écritures (véhicule déplacé AVANT la suppression de sa fiche)
        // tient avec des contraintes réelles.
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;");

        await using (var probe = await context.Database.BeginTransactionAsync())
        {
            var deleteReferenced = () => context.Database.ExecuteSqlRaw("DELETE FROM GpsDevices WHERE Id = 382060");
            deleteReferenced.Should().Throw<Exception>("les clés étrangères doivent être réellement contrôlées");
            await probe.RollbackAsync();
        }

        var replace = await new ReplaceVehicleDeviceCommandHandler(context).Handle(
            new ReplaceVehicleDeviceCommand(Htz278, RealImei278, NewMat: Mat278), CancellationToken.None);

        replace.Success.Should().BeTrue(replace.Message);
        using var fresh = Reopen(context);
        (await fresh.Vehicles.AsNoTracking().FirstAsync(v => v.Id == Htz278)).GpsDeviceId.Should().Be(RealDevice278);
        (await fresh.GpsDevices.AnyAsync(d => d.Id == WrongDevice278)).Should().BeFalse();
        (await fresh.GpsPositions.CountAsync(p => p.DeviceId == RealDevice278)).Should().Be(352);
    }

    [Fact]
    public async Task Attach_KeepsOperatorFuelSensorMode_AndMatSpellingAnnouncedByDevice()
    {
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);
        // TN : fiches des véhicules en « liters », fiches créées par l'ingestion en « raw_255 ».
        (await context.GpsDevices.FirstAsync(d => d.Id == WrongDevice278)).FuelSensorMode = "liters";
        await context.SaveChangesAsync();

        var replace = await new ReplaceVehicleDeviceCommandHandler(context).Handle(
            new ReplaceVehicleDeviceCommand(Htz278, RealImei278, NewMat: "nr08g1040 "), CancellationToken.None);

        replace.Success.Should().BeTrue(replace.Message);
        using var fresh = Reopen(context);
        var real = await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == RealDevice278);
        real.FuelSensorMode.Should().Be("liters");
        real.Mat.Should().Be(Mat278, "l'ingestion compare le MAT à l'identique");
    }

    [Fact]
    public async Task Attach_FuelSensorModeFromRequest_Wins()
    {
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);
        (await context.GpsDevices.FirstAsync(d => d.Id == WrongDevice278)).FuelSensorMode = "liters";
        await context.SaveChangesAsync();

        var replace = await new ReplaceVehicleDeviceCommandHandler(context).Handle(
            new ReplaceVehicleDeviceCommand(Htz278, RealImei278, NewMat: Mat278, NewFuelSensorMode: "percent"),
            CancellationToken.None);

        replace.Success.Should().BeTrue(replace.Message);
        using var fresh = Reopen(context);
        (await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == RealDevice278)).FuelSensorMode.Should().Be("percent");
    }

    [Fact]
    public async Task ScreenReplay_AfterAttachWithLowercaseMat_KeepsIdentifiersAndFuelModeAppliedByAttach()
    {
        // L'écran envoie au remplacement les valeurs saisies pour l'ANCIENNE fiche (MAT en
        // minuscules, SIM avec espaces, mode « liters »), puis rejoue la mise à jour sur la
        // fiche retenue SANS ces champs (replayAfterReplacement) : le rejeu ne doit pas
        // réécrire l'orthographe du MAT annoncée par le boîtier.
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);
        (await new ReplaceVehicleDeviceCommandHandler(context).Handle(
            new ReplaceVehicleDeviceCommand(Htz278, RealImei278 + " ", NewSimNumber: " 92005328 ", NewMat: "nr08g1040",
                NewFuelSensorMode: "liters"), CancellationToken.None))
            .Success.Should().BeTrue();

        using (var replayContext = Reopen(context))
        {
            var replay = await new UpdateAdminVehicleCommandHandler(replayContext).Handle(
                ScreenReplay(Htz278, RealDevice278, mileage: 12345), CancellationToken.None);
            replay.Success.Should().BeTrue(replay.Error);
        }

        using var fresh = Reopen(context);
        var real = await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == RealDevice278);
        real.DeviceUid.Should().Be(RealImei278, "l'ingestion retrouve le boîtier par son IMEI exact");
        real.Mat.Should().Be(Mat278, "l'ingestion compare le MAT à l'identique");
        real.SimNumber.Should().Be("92005328");
        real.FuelSensorMode.Should().Be("liters");
        (await fresh.Vehicles.AsNoTracking().FirstAsync(v => v.Id == Htz278)).Mileage.Should().Be(12345);
        (await fresh.GpsDevices.CountAsync()).Should().Be(1, "aucune nouvelle fiche n'est créée");
    }

    // ── Orthographe des identifiants (contre-relecture du 14/09/2026) ──

    [Theory]
    [InlineData("htz278")]
    [InlineData("HTZ 278")]
    public async Task Update_SameDevice_OperatorCorrectsMatCaseOrSpaces_CorrectionIsSaved(string storedMat)
    {
        // Fiche saisie à la main avec un MAT mal orthographié : le boîtier NEMS envoie
        // « HTZ278 », l'ingestion (WHERE mat = $1) ne le retrouve pas. La correction de
        // l'opérateur sur la fiche de son formulaire doit s'enregistrer, même si le boîtier
        // a déjà communiqué par son IMEI.
        using var context = TestDbContextFactory.Create();
        await SeedCompaniesAsync(context);
        var device = Device(10, "860141076677286", storedMat, null, BeliveSav, "assigned");
        device.LastCommunication = DateTime.UtcNow.AddDays(-3);
        context.GpsDevices.Add(device);
        context.Vehicles.Add(Vehicle(139, "HTZ 139", 10));
        await context.SaveChangesAsync();

        var result = await new UpdateAdminVehicleCommandHandler(context).Handle(
            FormUpdate(139, gpsDeviceId: 10, imei: "860141076677286", mat: "HTZ278", sim: null, mileage: 1000),
            CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        using var fresh = Reopen(context);
        (await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == 10)).Mat.Should().Be("HTZ278");
    }

    [Fact]
    public async Task Update_SameDevice_ImeiTypedWithSpaces_CommunicatingDeviceKeepsExactImei_NeverCommunicatedTakesInput()
    {
        using var context = TestDbContextFactory.Create();
        await SeedCompaniesAsync(context);
        var emitting = Device(10, "860141076677286", "NR08G0001", null, BeliveSav, "assigned");
        emitting.LastCommunication = DateTime.UtcNow;
        context.GpsDevices.Add(emitting);
        context.GpsDevices.Add(Device(20, "86014107667 7153", "NR08G0002", null, BeliveSav, "assigned"));
        context.Vehicles.Add(Vehicle(139, "HTZ 139", 10));
        context.Vehicles.Add(Vehicle(140, "HTZ 140", 20));
        await context.SaveChangesAsync();

        (await new UpdateAdminVehicleCommandHandler(context).Handle(
            FormUpdate(139, gpsDeviceId: 10, imei: "860 141 076 677 286", mat: "NR08G0001", sim: null, mileage: 1000),
            CancellationToken.None)).Success.Should().BeTrue();
        using (var replay = Reopen(context))
            (await new UpdateAdminVehicleCommandHandler(replay).Handle(
                FormUpdate(140, gpsDeviceId: 20, imei: "860141076677153", mat: "NR08G0002", sim: null, mileage: 1000),
                CancellationToken.None)).Success.Should().BeTrue();

        using var fresh = Reopen(context);
        (await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == 10)).DeviceUid
            .Should().Be("860141076677286", "l'ingestion retrouve ce boîtier par son IMEI exact");
        (await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == 20)).DeviceUid
            .Should().Be("860141076677153", "une fiche qui n'a jamais communiqué prend la correction");
    }

    [Fact]
    public async Task Create_WithImeiOfCommunicatingIngestDevice_KeepsMatSpellingAnnouncedByDevice()
    {
        // Création d'un véhicule avec l'IMEI d'une fiche créée par l'ingestion (MAT
        // « NR08G1040 ») et le MAT tapé en minuscules : la ligne qui réécrivait la saisie
        // brute après le Resolver faisait rejeter les trames du boîtier.
        using var context = TestDbContextFactory.Create();
        await SeedCompaniesAsync(context);
        var ingest = Device(RealDevice278, RealImei278, Mat278, null, Belive, "unassigned");
        ingest.LastCommunication = DateTime.UtcNow;
        context.GpsDevices.Add(ingest);
        await context.SaveChangesAsync();

        var result = await new CreateAdminVehicleCommandHandler(context).Handle(
            new CreateAdminVehicleCommand(
                Name: "HTZ 400", Type: "voiture", Brand: null, Model: null, Plate: "HTZ 400", Year: 2024, Color: null,
                Status: "available", HasGps: true, Mileage: 0, FuelType: "diesel", FuelTankCapacity: null,
                CompanyId: BeliveSav, GpsDeviceId: null, GpsImei: RealImei278, GpsMat: "nr08g1040 ",
                GpsBrand: "NORON", GpsModel: "NR024", GpsFirmwareVersion: null, GpsFuelSensorMode: "liters",
                GpsSimNumber: " 92005328 ", GpsSimOperator: null, GpsInstallationDate: null),
            CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        using var fresh = Reopen(context);
        var device = await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == RealDevice278);
        device.Mat.Should().Be(Mat278);
        device.SimNumber.Should().Be("92005328");
        device.CompanyId.Should().Be(BeliveSav);
    }

    // ── Appareil choisi dans la liste (contre-relecture du 14/09/2026) ──

    [Fact]
    public async Task Update_DeviceChosenFromListInIngestDefaultCompany_ProposesAttach_NothingTransferredOrDetached()
    {
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);

        // Liste « appareil existant » : #384940 choisi, le formulaire recopie son IMEI et son MAT.
        var result = await new UpdateAdminVehicleCommandHandler(context).Handle(
            FormUpdate(Htz278, gpsDeviceId: RealDevice278, imei: RealImei278, mat: Mat278, sim: null, mileage: 12345),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ReplaceSuggested.Should().BeTrue();
        result.Error.Should()
            .StartWith("Cet appareil GPS appartient à une autre société.")
            .And.Contain($"Si vous confirmez le remplacement, « HTZ 278 » sera rattaché au boîtier #{RealDevice278}")
            .And.Contain("Le boîtier passera de la société « BELIVE » à « BELIVE SAV ».");
        await AssertNothingChangedAsync(context);
        (await Reopen(context).Vehicles.AsNoTracking().FirstAsync(v => v.Id == Htz278)).Mileage.Should().Be(1000);
    }

    [Fact]
    public async Task Update_DeviceChosenFromList_AutoLinkedToAnotherClientsVehicle_RefusedWithoutDetaching()
    {
        // Auto-rattachement Rust : fiche de la société 1, statut « unassigned », liée au
        // véhicule d'un autre client. Vue depuis une session BELIVE, elle paraît disponible.
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);
        var other = TestDataBuilder.CreateSociete(id: OtherClient, subscriptionTypeId: 1);
        other.Name = "AUTRE CLIENT";
        context.Societes.Add(other);
        context.GpsDevices.Add(Device(777, "860141070000777", "NR08G0777", null, Belive, "unassigned"));
        var otherVehicle = Vehicle(999, "999 TU 9999", 777);
        otherVehicle.CompanyId = OtherClient;
        context.Vehicles.Add(otherVehicle);
        AddPositions(context, 777, 5);
        await context.SaveChangesAsync();

        var result = await new UpdateAdminVehicleCommandHandler(context).Handle(
            FormUpdate(Htz278, gpsDeviceId: 777, imei: "860141070000777", mat: null, sim: null, mileage: 12345),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ReplaceSuggested.Should().BeFalse();
        result.Error.Should().StartWith("Cet appareil GPS appartient à une autre société.")
            .And.Contain("déjà rattaché au véhicule 999 TU 9999");
        using var fresh = Reopen(context);
        var stolen = await fresh.Vehicles.AsNoTracking().FirstAsync(v => v.Id == 999);
        stolen.GpsDeviceId.Should().Be(777);
        stolen.HasGps.Should().BeTrue();
        (await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == 777)).CompanyId.Should().Be(Belive);
        await AssertNothingChangedAsync(context);
    }

    [Fact]
    public async Task Update_DeviceChosenFromList_OfAnotherClient_PlainRefusal()
    {
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);
        var other = TestDataBuilder.CreateSociete(id: OtherClient, subscriptionTypeId: 1);
        other.Name = "AUTRE CLIENT";
        context.Societes.Add(other);
        (await context.GpsDevices.FirstAsync(d => d.Id == RealDevice278)).CompanyId = OtherClient;
        await context.SaveChangesAsync();

        var result = await new UpdateAdminVehicleCommandHandler(context).Handle(
            FormUpdate(Htz278, gpsDeviceId: RealDevice278, imei: RealImei278, mat: Mat278, sim: null, mileage: 12345),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ReplaceSuggested.Should().BeFalse();
        result.Error.Should().Be("Cet appareil GPS appartient à une autre société.");
    }

    // ── Messages ──

    [Fact]
    public async Task Attach_EmittingDeviceHasAlerts_MessagesSayOnlyPositionsFollowTheVehicle()
    {
        // Arrêts, relevés carburant et alertes enregistrés sous la fiche de l'ingestion
        // portent vehicle_id = 0 et la société 1 : le rattachement ne les réaffecte pas.
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);
        AddReferencingRow(context, "gps_alerts", RealDevice278, 0);
        AddReferencingRow(context, "vehicle_stops", RealDevice278, 0);
        await context.SaveChangesAsync();

        var update = await new UpdateAdminVehicleCommandHandler(context).Handle(
            FormUpdate(Htz278, gpsDeviceId: null, imei: RealImei278, mat: Mat278, sim: null, mileage: 12345),
            CancellationToken.None);
        update.ReplaceSuggested.Should().BeTrue();
        update.Error.Should().Contain(
            "Seules les positions suivront le véhicule : les autres données déjà enregistrées sous ce boîtier " +
            "(1 alerte(s), 1 arrêt(s)) ne sont pas réaffectées et gardent le véhicule et la société enregistrés à l'époque.");

        using var replaceContext = Reopen(context);
        var replace = await new ReplaceVehicleDeviceCommandHandler(replaceContext).Handle(
            new ReplaceVehicleDeviceCommand(Htz278, RealImei278, NewMat: Mat278), CancellationToken.None);
        replace.Success.Should().BeTrue(replace.Message);
        replace.Message.Should()
            .Contain("dont les positions suivent désormais le véhicule (352 position(s)).")
            .And.Contain("Seules les positions suivent le véhicule : les autres données déjà enregistrées sous ce boîtier " +
                         "(1 alerte(s), 1 arrêt(s)) ne sont pas réaffectées")
            .And.NotContain("historique est conservé");
    }

    [Fact]
    public async Task Attach_ThirdEmptyDeviceSharingTheSim_IsReleasedToo()
    {
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);
        context.GpsDevices.Add(Device(500000, "PRE-ENREGISTRE", null, Sim255, Belive, "unassigned"));
        await context.SaveChangesAsync();

        var replace = await new ReplaceVehicleDeviceCommandHandler(context).Handle(
            new ReplaceVehicleDeviceCommand(Htz278, RealImei278, NewSimNumber: Sim255, NewMat: Mat278), CancellationToken.None);

        replace.Success.Should().BeTrue(replace.Message);
        replace.Message.Should().Contain("#500000");
        using var fresh = Reopen(context);
        (await fresh.GpsDevices.AnyAsync(d => d.Id == 500000)).Should().BeFalse();
        (await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == RealDevice278)).SimNumber.Should().Be(Sim255);
    }

    [Fact]
    public async Task Attach_EmittingDeviceAlreadyInVehicleCompany_NoTransferMentioned()
    {
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);
        (await context.GpsDevices.FirstAsync(d => d.Id == RealDevice278)).CompanyId = BeliveSav;
        await context.SaveChangesAsync();

        var replace = await new ReplaceVehicleDeviceCommandHandler(context).Handle(
            new ReplaceVehicleDeviceCommand(Htz278, RealImei278, NewMat: Mat278), CancellationToken.None);

        replace.Success.Should().BeTrue(replace.Message);
        replace.Message.Should().NotContain("transféré");
    }

    // ── Refus du rattachement ──

    [Fact]
    public async Task Attach_Refused_WhenVehiclesDeviceHasAPosition()
    {
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);
        AddPositions(context, WrongDevice278, 1);
        await context.SaveChangesAsync();

        var result = await new ReplaceVehicleDeviceCommandHandler(context).Handle(
            new ReplaceVehicleDeviceCommand(Htz278, RealImei278, NewMat: Mat278), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should()
            .Contain("les deux fiches contiennent des données")
            .And.Contain($"#{WrongDevice278}").And.Contain("1 position(s)")
            .And.Contain($"#{RealDevice278}").And.Contain("352 position(s)")
            .And.Contain("fusion de deux historiques n'est pas prise en charge");
        await AssertNothingChangedAsync(context);
    }

    [Theory]
    [InlineData("gps_alerts")]
    [InlineData("device_commands")]
    [InlineData("device_events")]
    [InlineData("tow_events")]
    [InlineData("fuel_records")]
    [InlineData("vehicle_stops")]
    [InlineData("accident_events")]
    [InlineData("geofence_events")]
    [InlineData("poi_visits")]
    public async Task Attach_Refused_WhenVehiclesDeviceHasAnyReferencingRow(string table)
    {
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);
        AddReferencingRow(context, table, WrongDevice278, Htz278);
        await context.SaveChangesAsync();

        var result = await new ReplaceVehicleDeviceCommandHandler(context).Handle(
            new ReplaceVehicleDeviceCommand(Htz278, RealImei278, NewMat: Mat278), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("les deux fiches contiennent des données");
        await AssertNothingChangedAsync(context);
    }

    [Fact]
    public async Task Attach_Refused_WhenEmittingDeviceBelongsToAnotherVehicle()
    {
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);
        context.Vehicles.Add(Vehicle(999, "HTZ 999", RealDevice278));
        await context.SaveChangesAsync();

        var result = await new ReplaceVehicleDeviceCommandHandler(context).Handle(
            new ReplaceVehicleDeviceCommand(Htz278, RealImei278, NewMat: Mat278), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain($"#{RealDevice278}").And.Contain("déjà rattaché au véhicule HTZ 999");
        await AssertNothingChangedAsync(context);
        (await context.Vehicles.AsNoTracking().FirstAsync(v => v.Id == 999)).GpsDeviceId.Should().Be(RealDevice278);
    }

    [Fact]
    public async Task Attach_BothDevicesHaveData_AndEmittingDeviceLinkedElsewhere_SaysBothHaveDataFirst()
    {
        // Remplacement ordinaire (fiche du véhicule pleine) : inviter à détacher l'autre
        // véhicule mènerait ensuite au refus « les deux fiches » — on le dit d'emblée.
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);
        AddPositions(context, WrongDevice278, 4);
        context.Vehicles.Add(Vehicle(999, "HTZ 200", RealDevice278));
        await context.SaveChangesAsync();

        var result = await new ReplaceVehicleDeviceCommandHandler(context).Handle(
            new ReplaceVehicleDeviceCommand(Htz278, RealImei278, NewMat: Mat278), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should()
            .StartWith("Remplacement impossible : les deux fiches contiennent des données.")
            .And.Contain("4 position(s)")
            .And.Contain("et est rattaché au véhicule HTZ 200")
            .And.NotContain("Détachez");
        await AssertNothingChangedAsync(context);
    }

    [Fact]
    public async Task Attach_Refused_WhenEmittingDeviceBelongsToAnotherClient()
    {
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);
        var other = TestDataBuilder.CreateSociete(id: OtherClient, subscriptionTypeId: 1);
        other.Name = "AUTRE CLIENT";
        context.Societes.Add(other);
        (await context.GpsDevices.FirstAsync(d => d.Id == RealDevice278)).CompanyId = OtherClient;
        await context.SaveChangesAsync();

        // L'écran ne propose pas le remplacement…
        var update = await new UpdateAdminVehicleCommandHandler(context).Handle(
            FormUpdate(Htz278, gpsDeviceId: null, imei: RealImei278, mat: Mat278, sim: null, mileage: 12345),
            CancellationToken.None);
        update.Success.Should().BeFalse();
        update.ReplaceSuggested.Should().BeFalse();
        update.Error.Should().Contain("appartient à la société « AUTRE CLIENT »").And.NotContain("Si vous confirmez");

        // … et le serveur le refuse s'il est appelé.
        using var replaceContext = Reopen(context);
        var result = await new ReplaceVehicleDeviceCommandHandler(replaceContext).Handle(
            new ReplaceVehicleDeviceCommand(Htz278, RealImei278, NewMat: Mat278), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should()
            .StartWith($"Rattachement refusé : le boîtier #{RealDevice278} (IMEI {RealImei278}) appartient à la société « AUTRE CLIENT »")
            .And.Contain("« BELIVE SAV »");
        using var fresh = Reopen(context);
        (await fresh.Vehicles.AsNoTracking().FirstAsync(v => v.Id == Htz278)).GpsDeviceId.Should().Be(WrongDevice278);
        (await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == RealDevice278)).CompanyId.Should().Be(OtherClient);
        (await fresh.GpsDevices.AnyAsync(d => d.Id == WrongDevice278)).Should().BeTrue();
    }

    [Fact]
    public async Task Attach_Refused_WhenThirdDeviceSharingTheSimHasData()
    {
        using var context = TestDbContextFactory.Create();
        await SeedHtz278Async(context);
        context.GpsDevices.Add(Device(500000, "AUTRE-BOITIER", null, Sim255, Belive, "unassigned"));
        AddPositions(context, 500000, 3);
        await context.SaveChangesAsync();

        var result = await new ReplaceVehicleDeviceCommandHandler(context).Handle(
            new ReplaceVehicleDeviceCommand(Htz278, RealImei278, NewSimNumber: Sim255, NewMat: Mat278), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("#500000").And.Contain("contient des données").And.Contain("3 position(s)");
        await AssertNothingChangedAsync(context);
        (await context.GpsDevices.AnyAsync(d => d.Id == 500000)).Should().BeTrue();
    }

    // ── Mode renommage (remplacement de boîtier sur le terrain) ──

    [Fact]
    public async Task Rename_VehicleDeviceHasHistory_EmptyOccupantDeleted_ImeiRenamedInPlace()
    {
        using var context = TestDbContextFactory.Create();
        await SeedCompaniesAsync(context);
        context.GpsDevices.Add(Device(10, "OLD-IMEI", "MAT-OLD", "11111111", BeliveSav, "assigned"));
        context.GpsDevices.Add(Device(20, "NEW-IMEI", null, null, Belive, "unassigned"));
        context.Vehicles.Add(Vehicle(139, "HTZ 139", 10));
        AddPositions(context, 10, 25);
        await context.SaveChangesAsync();

        var result = await new ReplaceVehicleDeviceCommandHandler(context).Handle(
            new ReplaceVehicleDeviceCommand(139, "NEW-IMEI", NewSimNumber: "22222222"), CancellationToken.None);

        result.Success.Should().BeTrue(result.Message);
        result.Mode.Should().Be(ReplaceVehicleDeviceCommandHandler.ModeRename);
        result.Message.Should().Be("Boîtier remplacé : IMEI OLD-IMEI → NEW-IMEI. Fiche vide #20 libérée. L'historique du véhicule est conservé.");
        result.DeviceId.Should().Be(10);
        result.ReleasedDeviceId.Should().Be(20);

        using var fresh = Reopen(context);
        var device = await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == 10);
        device.DeviceUid.Should().Be("NEW-IMEI");
        device.SimNumber.Should().Be("22222222");
        device.CompanyId.Should().Be(BeliveSav);
        (await fresh.GpsDevices.AnyAsync(d => d.Id == 20)).Should().BeFalse();
        (await fresh.Vehicles.AsNoTracking().FirstAsync(v => v.Id == 139)).GpsDeviceId.Should().Be(10);
        (await fresh.GpsPositions.CountAsync(p => p.DeviceId == 10)).Should().Be(25);
    }

    [Fact]
    public async Task Rename_EmptyVehicleDevice_EmptyOccupant_StaysRename()
    {
        // Aucune des deux fiches n'a d'historique : pas de rattachement, comportement historique.
        using var context = TestDbContextFactory.Create();
        await SeedCompaniesAsync(context);
        context.GpsDevices.Add(Device(10, "OLD-IMEI", null, null, BeliveSav, "assigned"));
        context.GpsDevices.Add(Device(20, "NEW-IMEI", null, null, Belive, "unassigned"));
        context.Vehicles.Add(Vehicle(139, "HTZ 139", 10));
        await context.SaveChangesAsync();

        var result = await new ReplaceVehicleDeviceCommandHandler(context).Handle(
            new ReplaceVehicleDeviceCommand(139, "NEW-IMEI"), CancellationToken.None);

        result.Success.Should().BeTrue(result.Message);
        result.Mode.Should().Be(ReplaceVehicleDeviceCommandHandler.ModeRename);
        using var fresh = Reopen(context);
        (await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == 10)).DeviceUid.Should().Be("NEW-IMEI");
        (await fresh.GpsDevices.AnyAsync(d => d.Id == 20)).Should().BeFalse();
    }

    [Theory]
    [InlineData("gps_positions")]
    [InlineData("gps_alerts")]
    [InlineData("device_commands")]
    [InlineData("device_events")]
    [InlineData("tow_events")]
    [InlineData("fuel_records")]
    [InlineData("vehicle_stops")]
    [InlineData("accident_events")]
    [InlineData("geofence_events")]
    [InlineData("poi_visits")]
    public async Task Rename_Refused_WhenOccupantHasAnyReferencingRow(string table)
    {
        // L'occupant bloque par la SIM seulement (IMEI différent de celui demandé) : on
        // est bien en mode renommage, et une seule ligne dans n'importe quelle table
        // suffit à interdire sa suppression.
        using var context = TestDbContextFactory.Create();
        await SeedCompaniesAsync(context);
        context.GpsDevices.Add(Device(10, "OLD-IMEI", null, "11111111", BeliveSav, "assigned"));
        context.GpsDevices.Add(Device(20, "RESERVE-20", null, "22222222", Belive, "unassigned"));
        context.Vehicles.Add(Vehicle(139, "HTZ 139", 10));
        AddPositions(context, 10, 5);
        AddReferencingRow(context, table, 20, 139);
        await context.SaveChangesAsync();

        var result = await new ReplaceVehicleDeviceCommandHandler(context).Handle(
            new ReplaceVehicleDeviceCommand(139, "NEW-IMEI", NewSimNumber: "22222222"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should()
            .StartWith("Remplacement impossible : le boîtier #20 porte déjà cet identifiant et contient des données")
            .And.NotContain("les deux fiches");
        using var fresh = Reopen(context);
        (await fresh.GpsDevices.CountAsync()).Should().Be(2);
        (await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == 10)).DeviceUid.Should().Be("OLD-IMEI");
    }

    [Fact]
    public async Task SimReserve_ImeiUnchanged_EmptyDeviceHoldsTheSim_ReplacementProposedThenSucceeds()
    {
        // Issue rouverte : changer la SIM sans changer l'IMEI, quand la nouvelle SIM est
        // portée par une fiche de réserve vide. La confirmation doit rester proposée.
        using var context = TestDbContextFactory.Create();
        await SeedCompaniesAsync(context);
        context.GpsDevices.Add(Device(10, "OLD-IMEI", "MAT-OLD", "11111111", BeliveSav, "assigned"));
        context.GpsDevices.Add(Device(30, "RESERVE-30", null, "22222222", Belive, "unassigned"));
        context.Vehicles.Add(Vehicle(139, "HTZ 139", 10));
        AddPositions(context, 10, 25);
        await context.SaveChangesAsync();

        var update = await new UpdateAdminVehicleCommandHandler(context).Handle(
            FormUpdate(139, gpsDeviceId: 10, imei: "OLD-IMEI", mat: "MAT-OLD", sim: "22222222", mileage: 2000),
            CancellationToken.None);

        update.Success.Should().BeFalse();
        update.ReplaceSuggested.Should().BeTrue();
        update.Error.Should()
            .StartWith("Doublon refusé : le numéro SIM « 22222222 » est déjà utilisé par le boîtier #30 (boîtier non affecté).")
            .And.Contain("Si vous confirmez le remplacement, le boîtier actuel #10 garde son IMEI et son historique.")
            .And.Contain("supprimée(s) pour libérer l'identifiant : #30");

        using (var replaceContext = Reopen(context))
        {
            var replace = await new ReplaceVehicleDeviceCommandHandler(replaceContext).Handle(
                new ReplaceVehicleDeviceCommand(139, "OLD-IMEI", NewSimNumber: "22222222", NewMat: "MAT-OLD"),
                CancellationToken.None);
            replace.Success.Should().BeTrue(replace.Message);
            replace.Mode.Should().Be(ReplaceVehicleDeviceCommandHandler.ModeRename);
            replace.DeviceId.Should().Be(10);
            // IMEI inchangé : aucun boîtier n'a été remplacé, le message ne doit pas le prétendre.
            replace.Message.Should().Be(
                "Identifiants mis à jour sur le boîtier #10 (IMEI inchangé : OLD-IMEI). Fiche vide #30 supprimée. " +
                "L'historique du véhicule est conservé.");
        }

        using (var replayContext = Reopen(context))
        {
            var replay = await new UpdateAdminVehicleCommandHandler(replayContext).Handle(
                FormUpdate(139, gpsDeviceId: 10, imei: "OLD-IMEI", mat: "MAT-OLD", sim: "22222222", mileage: 2000),
                CancellationToken.None);
            replay.Success.Should().BeTrue(replay.Error);
        }

        using var fresh = Reopen(context);
        (await fresh.GpsDevices.AnyAsync(d => d.Id == 30)).Should().BeFalse();
        var device = await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == 10);
        device.SimNumber.Should().Be("22222222");
        (await fresh.GpsPositions.CountAsync(p => p.DeviceId == 10)).Should().Be(25);
    }

    [Fact]
    public async Task FieldReplacement_UnknownImei_SimAndMatKept_MessageAnnouncesRenameNotAttach()
    {
        // Remplacement terrain (HTZ 139) : nouvel IMEI inconnu, MAT et SIM préremplis. Le
        // doublon est porté par le boîtier actuel ; le message ne doit pas promettre un
        // rattachement ni une suppression : c'est un renommage qui garde l'historique.
        using var context = TestDbContextFactory.Create();
        await SeedCompaniesAsync(context);
        context.GpsDevices.Add(Device(10, "OLD-IMEI", "MAT-OLD", "11111111", BeliveSav, "assigned"));
        context.Vehicles.Add(Vehicle(139, "HTZ 139", 10));
        AddPositions(context, 10, 25);
        await context.SaveChangesAsync();

        var update = await new UpdateAdminVehicleCommandHandler(context).Handle(
            FormUpdate(139, gpsDeviceId: null, imei: "NEW-IMEI", mat: "MAT-OLD", sim: "11111111", mileage: 2000),
            CancellationToken.None);

        update.Success.Should().BeFalse();
        update.ReplaceSuggested.Should().BeTrue();
        update.Error.Should()
            .StartWith("Doublon refusé : le MAT « MAT-OLD » est aussi porté par le boîtier actuel de ce véhicule (#10, IMEI OLD-IMEI).")
            .And.Contain("Si vous confirmez le remplacement, le boîtier actuel #10 prendra l'IMEI NEW-IMEI (au lieu de OLD-IMEI) et gardera tout son historique.")
            .And.NotContain("rattaché")
            .And.NotContain("supprimée");
    }

    // ── Réponse perdue pendant le COMMIT (contre-relecture du 14/09/2026) ──

    [Fact]
    public async Task Attach_TransientErrorAfterCommitIsApplied_RetryReportsTheAttach_NotARename()
    {
        // La base valide le COMMIT mais la réponse se perd : la stratégie de retry rejoue le
        // délégué. Sans détection de l'état atteint, le plan refait tombait en renommage sans
        // rien à supprimer et répondait « IMEI X → X », taisant la suppression de #382060.
        using var seedContext = TestDbContextFactory.Create();
        await SeedHtz278Async(seedContext);

        var lostCommit = new LoseFirstCommitResponseInterceptor();
        var options = new DbContextOptionsBuilder<TestGisDbContext>()
            .UseSqlite(seedContext.Database.GetDbConnection(),
                o => o.ExecutionStrategy(deps => new RetryOnTimeoutStrategy(deps)))
            .AddInterceptors(lostCommit)
            .Options;
        using var context = new TestGisDbContext(options);

        var result = await new ReplaceVehicleDeviceCommandHandler(context).Handle(
            new ReplaceVehicleDeviceCommand(Htz278, RealImei278, NewMat: Mat278), CancellationToken.None);

        lostCommit.Thrown.Should().BeTrue("le scénario doit réellement perdre la réponse du COMMIT");
        result.Success.Should().BeTrue(result.Message);
        result.Mode.Should().Be(ReplaceVehicleDeviceCommandHandler.ModeAttach);
        result.DeviceId.Should().Be(RealDevice278);
        result.ReleasedDeviceId.Should().Be(WrongDevice278);
        result.Message.Should()
            .StartWith($"Boîtier corrigé : « HTZ 278 » est rattaché au boîtier #{RealDevice278}")
            .And.Contain($"La fiche #{WrongDevice278} (IMEI {WrongImei278}, jamais utilisée) a été supprimée.")
            .And.NotContain("→");

        using var fresh = Reopen(seedContext);
        (await fresh.Vehicles.AsNoTracking().FirstAsync(v => v.Id == Htz278)).GpsDeviceId.Should().Be(RealDevice278);
        (await fresh.GpsDevices.AnyAsync(d => d.Id == WrongDevice278)).Should().BeFalse();
    }

    [Fact]
    public async Task Rename_TransientErrorAfterCommitIsApplied_RetryReportsTheRealRename()
    {
        using var seedContext = TestDbContextFactory.Create();
        await SeedCompaniesAsync(seedContext);
        seedContext.GpsDevices.Add(Device(10, "OLD-IMEI", "MAT-OLD", "11111111", BeliveSav, "assigned"));
        seedContext.GpsDevices.Add(Device(20, "NEW-IMEI", null, null, Belive, "unassigned"));
        seedContext.Vehicles.Add(Vehicle(139, "HTZ 139", 10));
        AddPositions(seedContext, 10, 25);
        await seedContext.SaveChangesAsync();

        var lostCommit = new LoseFirstCommitResponseInterceptor();
        var options = new DbContextOptionsBuilder<TestGisDbContext>()
            .UseSqlite(seedContext.Database.GetDbConnection(),
                o => o.ExecutionStrategy(deps => new RetryOnTimeoutStrategy(deps)))
            .AddInterceptors(lostCommit)
            .Options;
        using var context = new TestGisDbContext(options);

        var result = await new ReplaceVehicleDeviceCommandHandler(context).Handle(
            new ReplaceVehicleDeviceCommand(139, "NEW-IMEI", NewSimNumber: "22222222"), CancellationToken.None);

        lostCommit.Thrown.Should().BeTrue();
        result.Success.Should().BeTrue(result.Message);
        result.Message.Should().Be("Boîtier remplacé : IMEI OLD-IMEI → NEW-IMEI. Fiche vide #20 libérée. L'historique du véhicule est conservé.");
        result.ReleasedDeviceId.Should().Be(20);
    }

    [Fact]
    public async Task Attach_TransientErrorBeforeCommit_RetryExecutesNormally()
    {
        // Erreur transitoire AVANT toute écriture (sur le verrou / la première lecture sous
        // transaction) : la détection ne doit pas croire l'opération faite.
        using var seedContext = TestDbContextFactory.Create();
        await SeedHtz278Async(seedContext);

        var failOnce = new FailFirstTransactionStartInterceptor();
        var options = new DbContextOptionsBuilder<TestGisDbContext>()
            .UseSqlite(seedContext.Database.GetDbConnection(),
                o => o.ExecutionStrategy(deps => new RetryOnTimeoutStrategy(deps)))
            .AddInterceptors(failOnce)
            .Options;
        using var context = new TestGisDbContext(options);

        var result = await new ReplaceVehicleDeviceCommandHandler(context).Handle(
            new ReplaceVehicleDeviceCommand(Htz278, RealImei278, NewMat: Mat278), CancellationToken.None);

        failOnce.Thrown.Should().BeTrue();
        result.Success.Should().BeTrue(result.Message);
        result.Mode.Should().Be(ReplaceVehicleDeviceCommandHandler.ModeAttach);
        using var fresh = Reopen(seedContext);
        (await fresh.Vehicles.AsNoTracking().FirstAsync(v => v.Id == Htz278)).GpsDeviceId.Should().Be(RealDevice278);
        (await fresh.GpsDevices.AnyAsync(d => d.Id == WrongDevice278)).Should().BeFalse();
    }

    /// <summary>Stratégie de retry minimale : rejoue sur TimeoutException, sans délai.</summary>
    private sealed class RetryOnTimeoutStrategy : ExecutionStrategy
    {
        public RetryOnTimeoutStrategy(ExecutionStrategyDependencies dependencies)
            : base(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.Zero) { }

        protected override bool ShouldRetryOn(Exception exception) => exception is TimeoutException;
        protected override TimeSpan? GetNextDelay(Exception lastException) =>
            ExceptionsEncountered.Count <= MaxRetryCount ? TimeSpan.Zero : null;
    }

    /// <summary>Le premier COMMIT est réellement effectué, puis sa « réponse » est perdue.</summary>
    private sealed class LoseFirstCommitResponseInterceptor : DbTransactionInterceptor
    {
        public bool Thrown { get; private set; }

        public override Task TransactionCommittedAsync(
            System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Thrown) return Task.CompletedTask;
            Thrown = true;
            throw new TimeoutException("Réponse du COMMIT perdue (simulée).");
        }
    }

    /// <summary>La première transaction échoue dès son ouverture (rien n'est écrit).</summary>
    private sealed class FailFirstTransactionStartInterceptor : DbTransactionInterceptor
    {
        public bool Thrown { get; private set; }

        public override ValueTask<System.Data.Common.DbTransaction> TransactionStartedAsync(
            System.Data.Common.DbConnection connection, TransactionEndEventData eventData,
            System.Data.Common.DbTransaction result, CancellationToken cancellationToken = default)
        {
            if (Thrown) return ValueTask.FromResult(result);
            Thrown = true;
            result.Dispose();
            throw new TimeoutException("Coupure simulée à l'ouverture de la transaction.");
        }
    }

    // ── Helpers ──

    /// <summary>
    /// Mise à jour rejouée par l'écran après un remplacement (replayAfterReplacement) :
    /// fiche retenue, sans les identifiants et réglages déjà appliqués par le remplacement.
    /// </summary>
    private static UpdateAdminVehicleCommand ScreenReplay(int vehicleId, int retainedDeviceId, int mileage) =>
        FormUpdate(vehicleId, gpsDeviceId: retainedDeviceId, imei: null, mat: null, sim: null, mileage: mileage) with
        {
            GpsFuelSensorMode = null
        };

    private static UpdateAdminVehicleCommand FormUpdate(
        int vehicleId, int? gpsDeviceId, string? imei, string? mat, string? sim, int mileage)
    {
        var plate = vehicleId switch
        {
            Htz278 => "HTZ 278",
            Htz255 => "HTZ 255",
            _ => $"HTZ {vehicleId}"
        };
        return new(
            Id: vehicleId,
            Name: plate,
            Type: "voiture", Brand: null, Model: null,
            Plate: plate,
            Year: 2024, Color: null, Status: "available",
            HasGps: true, Mileage: mileage,
            FuelType: "diesel", FuelTankCapacity: null,
            CompanyId: BeliveSav,
            GpsDeviceId: gpsDeviceId,
            GpsImei: imei,
            GpsMat: mat,
            GpsBrand: "NORON", GpsModel: "NR024", GpsFirmwareVersion: null, GpsFuelSensorMode: "raw_255",
            GpsSimNumber: sim, GpsSimOperator: null, GpsInstallationDate: null);
    }

    private static async Task SeedCompaniesAsync(TestGisDbContext context)
    {
        context.SubscriptionTypes.Add(TestDataBuilder.CreateSubscriptionType());
        var belive = TestDataBuilder.CreateSociete(id: Belive, subscriptionTypeId: 1);
        belive.Name = "BELIVE";
        var sav = TestDataBuilder.CreateSociete(id: BeliveSav, subscriptionTypeId: 1);
        sav.Name = "BELIVE SAV";
        context.Societes.AddRange(belive, sav);
        await context.SaveChangesAsync();
    }

    private static async Task SeedHtz278Async(TestGisDbContext context)
    {
        await SeedCompaniesAsync(context);
        context.GpsDevices.Add(Device(WrongDevice278, WrongImei278, Mat278, null, BeliveSav, "assigned"));
        context.GpsDevices.Add(Device(RealDevice278, RealImei278, Mat278, null, Belive, "unassigned"));
        context.Vehicles.Add(Vehicle(Htz278, "HTZ 278", WrongDevice278));
        AddPositions(context, RealDevice278, 352);
        await context.SaveChangesAsync();
    }

    private static async Task AssertNothingChangedAsync(TestGisDbContext context)
    {
        using var fresh = Reopen(context);
        (await fresh.Vehicles.AsNoTracking().FirstAsync(v => v.Id == Htz278)).GpsDeviceId.Should().Be(WrongDevice278);
        (await fresh.GpsDevices.AnyAsync(d => d.Id == WrongDevice278)).Should().BeTrue();
        var real = await fresh.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == RealDevice278);
        real.CompanyId.Should().Be(Belive);
        (await fresh.GpsPositions.CountAsync(p => p.DeviceId == RealDevice278)).Should().Be(352);
    }

    /// <summary>Second contexte sur la même base : relit ce qui a VRAIMENT été enregistré.</summary>
    private static TestGisDbContext Reopen(TestGisDbContext context)
    {
        var options = new DbContextOptionsBuilder<TestGisDbContext>()
            .UseSqlite(context.Database.GetDbConnection())
            .Options;
        return new TestGisDbContext(options);
    }

    private static GpsDevice Device(int id, string imei, string? mat, string? sim, int companyId, string status) => new()
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

    private static Vehicle Vehicle(int id, string plate, int deviceId) => new()
    {
        Id = id,
        Name = plate,
        Plate = plate,
        Type = "voiture",
        Status = "available",
        CompanyId = BeliveSav,
        GpsDeviceId = deviceId,
        HasGps = true,
        Mileage = 1000,
        FuelType = "diesel"
    };

    private static void AddPositions(TestGisDbContext context, int deviceId, int count)
    {
        var start = new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < count; i++)
            context.GpsPositions.Add(new GpsPosition
            {
                DeviceId = deviceId,
                RecordedAt = start.AddMinutes(i * 5),
                Latitude = 36.8,
                Longitude = 10.18,
                IsValid = true
            });
    }

    private static void AddReferencingRow(TestGisDbContext context, string table, int deviceId, int vehicleId)
    {
        var now = DateTime.UtcNow;
        switch (table)
        {
            case "gps_positions":
                AddPositions(context, deviceId, 1);
                break;
            case "gps_alerts":
                context.GpsAlerts.Add(new GpsAlert { DeviceId = deviceId, CompanyId = BeliveSav, Type = "overspeed" });
                break;
            case "device_commands":
                context.DeviceCommands.Add(new DeviceCommand
                {
                    DeviceId = deviceId, VehicleId = vehicleId, UserId = 1, CompanyId = BeliveSav,
                    CommandType = "GO", CommandText = "AJ+GO#9999\n", Status = "sent"
                });
                break;
            case "device_events":
                context.DeviceEvents.Add(new DeviceEvent { DeviceId = deviceId, CompanyId = BeliveSav, EventType = "restart" });
                break;
            case "tow_events":
                context.TowEvents.Add(new TowEvent
                {
                    DeviceId = deviceId, VehicleId = vehicleId, CompanyId = BeliveSav,
                    StartedAt = now, LastSeenAt = now, Status = "ended"
                });
                break;
            case "fuel_records":
                context.FuelRecords.Add(new FuelRecord { DeviceId = deviceId, VehicleId = vehicleId, CompanyId = BeliveSav });
                break;
            case "vehicle_stops":
                context.VehicleStops.Add(new VehicleStop { DeviceId = deviceId, VehicleId = vehicleId, CompanyId = BeliveSav });
                break;
            case "accident_events":
                context.AccidentEvents.Add(new AccidentEvent { GpsDeviceId = deviceId, VehicleId = vehicleId, CompanyId = BeliveSav });
                break;
            case "geofence_events":
                context.GeofenceEvents.Add(new GeofenceEvent
                {
                    DeviceId = deviceId, VehicleId = vehicleId, GeofenceId = 1, CompanyId = BeliveSav, Type = "enter"
                });
                break;
            case "poi_visits":
                context.PoiVisits.Add(new PoiVisit
                {
                    PoiId = 1, VehicleId = vehicleId, DeviceId = deviceId, CompanyId = BeliveSav, ArrivalAt = now
                });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(table), table, null);
        }
    }
}
