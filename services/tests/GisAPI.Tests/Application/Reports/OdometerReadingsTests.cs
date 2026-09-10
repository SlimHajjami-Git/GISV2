using FluentAssertions;
using GisAPI.Application.Common;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Application.Features.Reports.Queries.GetOperatingCostReport;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.Reports;

/// <summary>
/// D'où vient le kilométrage d'un véhicule, selon son équipement.
///
/// La règle, posée en recette le 10/09/2026, n'est PAS la même selon l'offre :
/// <list type="bullet">
///   <item><b>Calypso GPS</b>, véhicule équipé d'un boîtier branché au bus CAN :
///     le kilométrage vient du BOÎTIER. Les saisies du client ne doivent alors
///     jamais s'y substituer.</item>
///   <item><b>Calypso GPA</b>, ou véhicule sans boîtier, ou boîtier sans CAN :
///     le kilométrage se reconstitue à partir de ce que le client SAISIT — un
///     plein, un entretien, une réparation, une dépense.</item>
/// </list>
///
/// Ces tests verrouillent les deux moitiés de la règle. Sans eux, élargir la
/// source des relevés saisis pourrait, à la faveur d'une refonte, se mettre à
/// écraser le compteur du boîtier — le chiffre le plus fiable des deux.
/// </summary>
public class OdometerReadingsTests
{
    private const int CompanyId = 1;
    private static DateTime Utc(int month, int day) => new(2026, month, day, 10, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Debut = new(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FinExclue = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    // ══════════════ Les quatre sources de relevés saisis ══════════════

    [Fact]
    public async Task Les_quatre_ecrans_de_saisie_alimentent_le_kilometrage()
    {
        using var ctx = TestDbContextFactory.Create();

        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Sans boîtier", CompanyId = CompanyId });
        ctx.FuelEntries.Add(new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(5, 5), Volume = 40, TotalAmount = 60, OdometerKm = 10_000 });
        ctx.MaintenanceLogs.Add(new MaintenanceLog { Id = 1, VehicleId = 1, CompanyId = CompanyId, TemplateId = 1, DoneDate = Utc(5, 20), DoneKm = 10_500, ActualCost = 200 });
        ctx.Repairs.Add(new Repair { Id = 1, SocieteId = CompanyId, VehicleId = 1, Reference = "R1", RepairDate = Utc(6, 5), MileageAtRepair = 11_200, TotalCost = 300, Status = "completed" });
        ctx.VehicleCosts.Add(new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "insurance", Amount = 500, Date = Utc(6, 20), Mileage = 11_900 });
        await ctx.SaveChangesAsync();

        var releves = await OdometerReadings.LoadAsync(ctx, CompanyId, new[] { 1 }, Debut, FinExclue, CancellationToken.None);

        releves[1].Should().HaveCount(4, "chacun des quatre écrans de saisie apporte un relevé");

        var distance = OdometerDistance.Compute(releves[1]);
        distance.DistanceKm.Should().Be(1_900m, "10 000 → 10 500 → 11 200 → 11 900");
        distance.Measurable.Should().BeTrue();
        distance.Reliable.Should().BeTrue();
    }

    [Fact]
    public async Task Ne_lire_que_les_pleins_sous_estime_la_distance()
    {
        using var ctx = TestDbContextFactory.Create();

        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Sans boîtier", CompanyId = CompanyId });
        ctx.FuelEntries.Add(new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(5, 5), Volume = 40, TotalAmount = 60, OdometerKm = 10_000 });
        ctx.Repairs.Add(new Repair { Id = 1, SocieteId = CompanyId, VehicleId = 1, Reference = "R1", RepairDate = Utc(6, 5), MileageAtRepair = 11_200, TotalCost = 300, Status = "completed" });
        await ctx.SaveChangesAsync();

        // Un seul plein : la distance n'est pas mesurable sur les pleins seuls.
        var pleinsSeuls = OdometerDistance.Compute(
            ctx.FuelEntries.Where(f => f.VehicleId == 1).ToList().Select(f => (f.OdometerKm ?? 0L, f.InvoiceDate)));
        pleinsSeuls.Measurable.Should().BeFalse("un relevé isolé ne décrit aucun trajet");

        // Avec la réparation, deux relevés : la distance apparaît.
        var toutesSources = OdometerDistance.Compute(
            await OdometerReadings.LoadAsync(ctx, CompanyId, new[] { 1 }, Debut, FinExclue, CancellationToken.None)
                .ContinueWith(t => t.Result[1]));
        toutesSources.DistanceKm.Should().Be(1_200m);
    }

    // ══════════════ Ce qui doit être écarté ══════════════

    [Fact]
    public async Task Les_reparations_annulees_les_relevés_vides_et_les_autres_societes_sont_ecartes()
    {
        using var ctx = TestDbContextFactory.Create();

        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "Suivi", CompanyId = CompanyId },
            new Vehicle { Id = 2, Name = "Autre société", CompanyId = 2 });

        ctx.FuelEntries.AddRange(
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(5, 5), Volume = 40, TotalAmount = 60, OdometerKm = 10_000 },
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(5, 6), Volume = 40, TotalAmount = 60, OdometerKm = null },
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(5, 7), Volume = 40, TotalAmount = 60, OdometerKm = 0 },
            new FuelEntry { VehicleId = 2, CompanyId = 2, InvoiceDate = Utc(5, 8), Volume = 40, TotalAmount = 60, OdometerKm = 99_000 });

        ctx.Repairs.AddRange(
            new Repair { Id = 1, SocieteId = CompanyId, VehicleId = 1, Reference = "R1", RepairDate = Utc(5, 10), MileageAtRepair = 10_400, TotalCost = 300, Status = "completed" },
            new Repair { Id = 2, SocieteId = CompanyId, VehicleId = 1, Reference = "R2", RepairDate = Utc(5, 11), MileageAtRepair = 90_000, TotalCost = 0, Status = "cancelled" });

        await ctx.SaveChangesAsync();

        var releves = await OdometerReadings.LoadAsync(ctx, CompanyId, new[] { 1 }, Debut, FinExclue, CancellationToken.None);

        releves[1].Should().HaveCount(2, "relevé nul, relevé à zéro et réparation annulée sont écartés");
        releves[2].Should().BeEmpty("une autre société n'entre jamais dans le calcul");
        OdometerDistance.Compute(releves[1]).DistanceKm.Should().Be(400m);
    }

    [Fact]
    public async Task La_borne_de_fin_est_exclue_et_ne_perd_pas_le_dernier_jour()
    {
        using var ctx = TestDbContextFactory.Create();

        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Suivi", CompanyId = CompanyId });
        ctx.FuelEntries.AddRange(
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(5, 1), Volume = 40, TotalAmount = 60, OdometerKm = 10_000 },
            // Dernier jour de la période, à midi : DOIT être compté.
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc), Volume = 40, TotalAmount = 60, OdometerKm = 10_800 },
            // Premier jour hors période : ne doit PAS être compté.
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(7, 1), Volume = 40, TotalAmount = 60, OdometerKm = 12_000 });
        await ctx.SaveChangesAsync();

        var releves = await OdometerReadings.LoadAsync(ctx, CompanyId, new[] { 1 }, Debut, FinExclue, CancellationToken.None);

        releves[1].Should().HaveCount(2);
        OdometerDistance.Compute(releves[1]).DistanceKm.Should().Be(800m);
    }

    [Fact]
    public async Task Aucun_vehicule_demande_ne_declenche_aucune_requete()
    {
        using var ctx = TestDbContextFactory.Create();
        var releves = await OdometerReadings.LoadAsync(ctx, CompanyId, Array.Empty<int>(), Debut, FinExclue, CancellationToken.None);
        releves.Should().BeEmpty();
    }

    // ══════════════ La priorité : boîtier d'abord ══════════════

    [Fact]
    public async Task Un_vehicule_equipe_garde_le_kilometrage_du_boitier_malgre_ses_saisies()
    {
        using var ctx = TestDbContextFactory.Create();

        ctx.GpsDevices.Add(new GpsDevice { Id = 1, DeviceUid = "DEV-1", CompanyId = CompanyId });
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Camion équipé", CompanyId = CompanyId, GpsDeviceId = 1 });

        // Le boîtier remonte 500 km de trajets terminés.
        ctx.Trips.AddRange(
            new Trip { Id = 1, VehicleId = 1, CompanyId = CompanyId, Status = "completed", StartTime = Utc(5, 4), EndTime = Utc(5, 4), DistanceKm = 300 },
            new Trip { Id = 2, VehicleId = 1, CompanyId = CompanyId, Status = "completed", StartTime = Utc(5, 6), EndTime = Utc(5, 6), DistanceKm = 200 });

        // Le client saisit AUSSI des relevés, qui donneraient 4 000 km.
        ctx.FuelEntries.AddRange(
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(5, 3), Volume = 80, TotalAmount = 120, OdometerKm = 20_000 },
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(6, 3), Volume = 80, TotalAmount = 120, OdometerKm = 22_000 });
        ctx.Repairs.Add(new Repair { Id = 1, SocieteId = CompanyId, VehicleId = 1, Reference = "R1", RepairDate = Utc(6, 20), MileageAtRepair = 24_000, TotalCost = 300, Status = "completed" });

        await ctx.SaveChangesAsync();

        var rapport = await new GetOperatingCostReportQueryHandler(ctx, TestDbContextFactory.CreateMockTenantService().Object)
            .Handle(new GetOperatingCostReportQuery(new DateTime(2026, 5, 1), new DateTime(2026, 6, 30)), CancellationToken.None);

        var camion = rapport.Vehicles.Single(v => v.VehicleId == 1);
        camion.DistanceSource.Should().Be(OperatingCostAggregator.SourceGps,
            "un véhicule équipé tire son kilométrage du boîtier, jamais des saisies");
        camion.DistanceKm.Should().Be(500m,
            "les 4 000 km reconstitués des saisies ne doivent PAS écraser les 500 km du boîtier");
    }

    [Fact]
    public async Task Un_vehicule_sans_boitier_tire_son_kilometrage_des_saisies()
    {
        using var ctx = TestDbContextFactory.Create();

        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Utilitaire GPA", CompanyId = CompanyId });
        ctx.FuelEntries.Add(new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(5, 3), Volume = 50, TotalAmount = 75, OdometerKm = 30_000 });
        ctx.MaintenanceLogs.Add(new MaintenanceLog { Id = 1, VehicleId = 1, CompanyId = CompanyId, TemplateId = 1, DoneDate = Utc(6, 3), DoneKm = 31_600, ActualCost = 250 });
        await ctx.SaveChangesAsync();

        var rapport = await new GetOperatingCostReportQueryHandler(ctx, TestDbContextFactory.CreateMockTenantService().Object)
            .Handle(new GetOperatingCostReportQuery(new DateTime(2026, 5, 1), new DateTime(2026, 6, 30)), CancellationToken.None);

        var utilitaire = rapport.Vehicles.Single(v => v.VehicleId == 1);
        utilitaire.DistanceSource.Should().Be(OperatingCostAggregator.SourceOdometer);
        utilitaire.DistanceKm.Should().Be(1_600m, "le relevé de l'entretien compte autant que celui du plein");
    }
}
