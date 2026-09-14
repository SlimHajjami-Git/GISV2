using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Dashboard.Queries.GetGpaDashboard;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Application.Features.Reports.Queries.GetOperatingCostReport;
using GisAPI.Application.Features.Vehicles;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Dashboard;

/// <summary>
/// Tableau de bord d'un compte SANS GPS (Calypso GPA), GET /api/dashboard/gpa.
/// Chaque bloc doit retomber sur une définition déjà en service : le rapport
/// « Coût d'exploitation » pour les coûts, l'échéancier persisté pour les
/// acquisitions, les dépenses d'entretien + réparations non annulées pour les
/// interventions, les échéances d'entretien et de documents pour les alertes.
/// Aucun chiffre inventé, aucune alerte GPS, portée véhicules appliquée partout.
/// </summary>
public class GetGpaDashboardQueryHandlerTests
{
    private const int CompanyId = 1;
    private const int OtherCompanyId = 2;
    private const int RestrictedUserId = 42;
    private const int OrphanUserId = 43;

    // « Aujourd'hui » = 11/09/2026 ; période par défaut du 01/01 au 11/09/2026.
    private static readonly DateTime Now = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime From = new(2026, 1, 1);
    private static readonly DateTime To = new(2026, 9, 11);

    private static DateTime Utc(int year, int month, int day) => new(year, month, day, 10, 0, 0, DateTimeKind.Utc);

    private static IDateTimeProvider Clock()
    {
        var m = new Mock<IDateTimeProvider>();
        m.Setup(c => c.UtcNow).Returns(Now);
        m.Setup(c => c.Today).Returns(DateOnly.FromDateTime(Now));
        return m.Object;
    }

    private static ICurrentTenantService Admin() =>
        TestDbContextFactory.CreateMockTenantService(CompanyId, userId: 1).Object;

    private static ICurrentTenantService Restricted(int userId)
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.CompanyId).Returns(CompanyId);
        m.Setup(x => x.UserId).Returns(userId);
        m.Setup(x => x.UserRoles).Returns(new[] { "user" });
        m.Setup(x => x.IsAuthenticated).Returns(true);
        return m.Object;
    }

    private static Task<GpaDashboardDto> RunAsync(TestGisDbContext ctx, ICurrentTenantService tenant,
        DateTime? from = null, DateTime? to = null) =>
        new GetGpaDashboardQueryHandler(ctx, tenant, Clock())
            .Handle(new GetGpaDashboardQuery(from ?? From, to ?? To), CancellationToken.None);

    private static Task<OperatingCostReportDto> OperatingReportAsync(TestGisDbContext ctx, ICurrentTenantService tenant,
        DateTime from, DateTime to) =>
        new GetOperatingCostReportQueryHandler(ctx, tenant)
            .Handle(new GetOperatingCostReportQuery(from, to), CancellationToken.None);

    // V3 : leasing 500 × 24 dès le 10/01/2026 (jour 10) + apport 5 000 le 10/01/2026.
    private static Vehicle Leasing3() => new()
    {
        Id = 3, CompanyId = CompanyId, Name = "Leasing 01", Plate = "GJ-473-KS",
        AcquisitionType = "leasing",
        LeasingMonthlyPayment = 500, LeasingDurationMonths = 24,
        LeasingStartDate = new DateTime(2026, 1, 10), LeasingPaymentDay = 10,
        PurchasePrice = 5_000, PurchaseDate = new DateTime(2026, 1, 10),
        TechnicalInspectionExpiry = new DateTime(2026, 11, 10),   // J+60 : borne documents incluse
        RegistrationExpiry = new DateTime(2026, 10, 11)           // J+30
    };

    /// <summary>
    /// Société 1, quatre véhicules sans boîtier (V1, V2 achetés ; V3 en leasing
    /// avec échéancier persisté ; V4 en leasing JAMAIS synchronisé), plus un
    /// véhicule d'une autre société qui ne doit apparaître nulle part.
    /// </summary>
    private static async Task<TestGisDbContext> SeedAsync()
    {
        var ctx = TestDbContextFactory.Create();

        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, CompanyId = CompanyId, Name = "Service 01", Plate = "GA-214-RK", Mileage = 61_200,
                          InsuranceExpiry = new DateTime(2026, 9, 25),
                          RegistrationExpiry = new DateTime(2026, 11, 11) },   // J+61 : hors fenêtre
            new Vehicle { Id = 2, CompanyId = CompanyId, Name = "Logistique 01", Plate = "GH-619-XC", Mileage = 208_400,
                          TaxExpiry = new DateTime(2026, 8, 1) },
            Leasing3(),
            // V4 : leasing 300 × 12 dès le 01/06/2026 (jour 5), sans aucune ligne en base.
            new Vehicle { Id = 4, CompanyId = CompanyId, Name = "Leasing 02", Plate = "GK-100-AA",
                          AcquisitionType = "leasing", LeasingMonthlyPayment = 300, LeasingDurationMonths = 12,
                          LeasingStartDate = new DateTime(2026, 6, 1), LeasingPaymentDay = 5 },
            new Vehicle { Id = 9, CompanyId = OtherCompanyId, Name = "Etranger", Plate = "999 TU 9",
                          InsuranceExpiry = new DateTime(2026, 8, 15),
                          AcquisitionType = "leasing", LeasingMonthlyPayment = 999, LeasingDurationMonths = 12,
                          LeasingStartDate = new DateTime(2026, 1, 1), LeasingPaymentDay = 1 });

        ctx.FuelEntries.AddRange(
            new FuelEntry { Id = 1, CompanyId = CompanyId, VehicleId = 1, InvoiceDate = Utc(2026, 3, 5), Volume = 50, TotalAmount = 100, OdometerKm = 60_000 },
            new FuelEntry { Id = 2, CompanyId = CompanyId, VehicleId = 2, InvoiceDate = Utc(2026, 8, 10), Volume = 100, TotalAmount = 200, OdometerKm = 208_300 },
            // Hors période, mais dans les 12 mois glissants (décembre 2025).
            new FuelEntry { Id = 3, CompanyId = CompanyId, VehicleId = 1, InvoiceDate = Utc(2025, 12, 15), Volume = 25, TotalAmount = 50 },
            // Avant les 12 mois glissants (qui commencent le 01/10/2025).
            new FuelEntry { Id = 5, CompanyId = CompanyId, VehicleId = 1, InvoiceDate = Utc(2025, 9, 20), Volume = 35, TotalAmount = 70 },
            new FuelEntry { Id = 4, CompanyId = OtherCompanyId, VehicleId = 9, InvoiceDate = Utc(2026, 3, 1), Volume = 10, TotalAmount = 999 });

        ctx.VehicleCosts.AddRange(
            // Entretien passé par « marquer fait » : une dépense ET un log qui la référence.
            new VehicleCost { Id = 11, CompanyId = CompanyId, VehicleId = 1, Type = "maintenance", Amount = 120,
                              Date = Utc(2026, 6, 10), Description = "Entretien: Vidange", Mileage = 61_000 },
            // Entretien saisi depuis l'écran Dépenses (type à casse/espaces variables, sans log).
            new VehicleCost { Id = 12, CompanyId = CompanyId, VehicleId = 2, Type = "Entretien ", Amount = 80,
                              Date = Utc(2026, 7, 1) },
            new VehicleCost { Id = 13, CompanyId = CompanyId, VehicleId = 1, Type = "insurance", Amount = 600, Date = Utc(2026, 2, 1) },
            new VehicleCost { Id = 14, CompanyId = CompanyId, VehicleId = 1, Type = "insurance_refund", Amount = 100, Date = Utc(2026, 2, 20) },
            new VehicleCost { Id = 15, CompanyId = CompanyId, VehicleId = 2, Type = "fuel", Amount = 40, Date = Utc(2026, 4, 1) },
            // Facture de sinistre : Réparations côté coûts, mais PAS une intervention.
            new VehicleCost { Id = 16, CompanyId = CompanyId, VehicleId = 2, Type = "repair", Amount = 300, Date = Utc(2026, 5, 1) },
            new VehicleCost { Id = 17, CompanyId = OtherCompanyId, VehicleId = 9, Type = "maintenance", Amount = 999, Date = Utc(2026, 6, 1) });

        ctx.MaintenanceLogs.AddRange(
            new MaintenanceLog { Id = 1, VehicleId = 1, TemplateId = 1, CostId = 11, DoneDate = Utc(2026, 6, 10),
                                 DoneKm = 61_050, ActualCost = 120, SupplierId = 5 },
            // Log sans dépense : les maintenance_logs ne sont jamais comptés.
            new MaintenanceLog { Id = 2, VehicleId = 2, TemplateId = 2, CostId = null, DoneDate = Utc(2026, 5, 5),
                                 DoneKm = 0, ActualCost = 55 });

        ctx.Repairs.AddRange(
            new Repair { Id = 1, SocieteId = CompanyId, VehicleId = 2, Reference = "REP-1", Description = "Nettoyage FAP forcé",
                         RepairType = "mecanique", SupplierId = 5, MileageAtRepair = 208_152,
                         RepairDate = Utc(2026, 8, 3), TotalCost = 468, Status = "completed" },
            new Repair { Id = 2, SocieteId = CompanyId, VehicleId = 1, Reference = "REP-2", Description = "Annulée",
                         RepairDate = Utc(2026, 9, 1), TotalCost = 250, Status = "Cancelled" },
            // Type non saisi (il serait « déduit » électrique) et kilométrage 0 = inconnu.
            new Repair { Id = 3, SocieteId = CompanyId, VehicleId = 3, Reference = "REP-3", Description = "Remplacement ampoule",
                         MileageAtRepair = 0, RepairDate = Utc(2026, 4, 15), TotalCost = 150, Status = "completed" },
            new Repair { Id = 9, SocieteId = OtherCompanyId, VehicleId = 9, Reference = "REP-9",
                         RepairDate = Utc(2026, 5, 1), TotalCost = 999, Status = "completed" });

        ctx.Suppliers.Add(new Supplier { Id = 5, CompanyId = CompanyId, Name = "Garage Central" });

        ctx.MaintenanceTemplates.AddRange(
            new MaintenanceTemplate { Id = 1, CompanyId = CompanyId, Name = "Vidange", Category = "Moteur", IsActive = true },
            new MaintenanceTemplate { Id = 2, CompanyId = CompanyId, Name = "Courroie", Category = "Moteur", IsActive = true },
            new MaintenanceTemplate { Id = 3, CompanyId = CompanyId, Name = "Pneus", Category = "Pneus", IsActive = false },
            new MaintenanceTemplate { Id = 4, CompanyId = CompanyId, Name = "Freins", Category = "Freinage", IsActive = true });

        ctx.VehicleMaintenanceSchedules.AddRange(
            // En retard AU KM (61 200 > 60 000), sans échéance par date.
            new VehicleMaintenanceSchedule { Id = 1, CompanyId = CompanyId, VehicleId = 1, TemplateId = 1, Status = "overdue", NextDueKm = 60_000 },
            // Statut périmé (« upcoming ») mais échéance du 01/09 dépassée : en retard.
            new VehicleMaintenanceSchedule { Id = 2, CompanyId = CompanyId, VehicleId = 2, TemplateId = 2, Status = "upcoming", NextDueDate = new DateTime(2026, 9, 1) },
            // Sous 30 jours par date.
            new VehicleMaintenanceSchedule { Id = 3, CompanyId = CompanyId, VehicleId = 2, TemplateId = 4, Status = "upcoming", NextDueDate = new DateTime(2026, 9, 25) },
            // Gabarit inactif, puis échéance en pause : ignorés.
            new VehicleMaintenanceSchedule { Id = 4, CompanyId = CompanyId, VehicleId = 1, TemplateId = 3, Status = "overdue", NextDueDate = new DateTime(2026, 1, 1) },
            new VehicleMaintenanceSchedule { Id = 5, CompanyId = CompanyId, VehicleId = 3, TemplateId = 1, Status = "overdue", IsPaused = true, NextDueDate = new DateTime(2026, 2, 1) },
            // Lointaine : rien.
            new VehicleMaintenanceSchedule { Id = 6, CompanyId = CompanyId, VehicleId = 2, TemplateId = 1, Status = "ok", NextDueDate = new DateTime(2026, 12, 1) },
            // Autre société.
            new VehicleMaintenanceSchedule { Id = 7, CompanyId = OtherCompanyId, VehicleId = 9, TemplateId = 1, Status = "overdue", NextDueDate = new DateTime(2026, 3, 1) });

        // Une alerte GPS existe : elle ne doit jamais remonter ici.
        ctx.GpsAlerts.Add(new GpsAlert { Id = 1, CompanyId = CompanyId, VehicleId = 1, Type = "overspeed", Severity = "high",
                                         Message = "Excès de vitesse", Timestamp = Utc(2026, 9, 10) });

        ctx.UserVehicles.Add(new UserVehicle { UserId = RestrictedUserId, VehicleId = 2 });

        // Échéancier PERSISTÉ de V3, avec des faits saisis par l'exploitant.
        foreach (var e in AcquisitionScheduleSync.ExpectedLines(Leasing3()))
        {
            var line = new AcquisitionPayment
            {
                CompanyId = CompanyId, VehicleId = 3, Kind = e.Kind, Seq = e.Seq, DueDate = e.Due,
                Amount = e.Amount, Status = AcquisitionPayment.Statuses.Planned
            };
            switch (e.Kind, e.Seq)
            {
                case ("apport", 1): line.Status = "paid"; line.PaidAmount = 5_000; line.PaidAt = Utc(2026, 1, 10); break;
                case ("mensualite", 3): line.Status = "skipped"; break;                                                // 10/03 ignorée
                case ("mensualite", 4): line.Status = "paid"; line.PaidAmount = 480; line.PaidAt = Utc(2026, 4, 12); break; // réglée 480
                case ("mensualite", 12): line.Status = "paid"; line.PaidAmount = 500; line.PaidAt = Utc(2026, 9, 1); break; // 10/12 payée d'avance
                case ("mensualite", 13): line.Status = "skipped"; break;                                               // 10/01/2027 ignorée
            }
            ctx.AcquisitionPayments.Add(line);
        }

        await ctx.SaveChangesAsync();
        return ctx;
    }

    // ── Coûts ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Les_couts_recoupent_le_rapport_Cout_d_exploitation_sur_la_meme_plage()
    {
        using var ctx = await SeedAsync();

        foreach (var (from, to, expectedTotal) in new[]
                 {
                     (From, To, 1_958m),
                     // Bornes incluses : le remboursement du 20/02 et l'entretien du 10/06 comptent.
                     (new DateTime(2026, 2, 20), new DateTime(2026, 6, 10), 610m)
                 })
        {
            var dashboard = await RunAsync(ctx, Admin(), from, to);
            var report = await OperatingReportAsync(ctx, Admin(), from, to);

            dashboard.Costs.Total.Should().Be(expectedTotal);
            dashboard.Costs.Total.Should().Be(report.TotalCost, "même définition que le rapport « Coût d'exploitation »");
            dashboard.Costs.Fuel.Should().Be(report.TotalFuelCost);
            dashboard.Costs.Maintenance.Should().Be(report.TotalMaintenanceCost);
            dashboard.Costs.Repair.Should().Be(report.TotalRepairCost);
            dashboard.Costs.Other.Should().Be(report.TotalOtherCost);
            (dashboard.Costs.Fuel + dashboard.Costs.Maintenance + dashboard.Costs.Repair + dashboard.Costs.Other)
                .Should().Be(dashboard.Costs.Total);
            dashboard.From.Should().Be(DateTime.SpecifyKind(from, DateTimeKind.Utc));
            dashboard.To.Should().Be(DateTime.SpecifyKind(to, DateTimeKind.Utc), "le jour de fin est inclus");
        }

        var full = await RunAsync(ctx, Admin());
        full.Costs.Should().Be(new GpaCostsDto(Fuel: 340m, Maintenance: 200m, Repair: 918m, Other: 500m, Total: 1_958m),
            "carburant 100 + 200 + 40 ; entretien 120 + 80 ; réparations 468 + 150 + facture 300 (l'annulée exclue) ; " +
            "assurance 600 − remboursement 100 ; rien de la société 2");
    }

    // ── Acquisitions ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Le_cout_d_achats_est_le_cout_complet_du_parc_independant_de_la_periode_et_hors_des_couts()
    {
        using var ctx = await SeedAsync();
        var linesBefore = ctx.AcquisitionPayments.Count();

        var dashboard = await RunAsync(ctx, Admin());

        // V3 (lignes) : apport payé 5 000 + 24 mensualités de 500, sauf mars et
        // janvier 2027 ignorées (22) et avril réglée 480 : 21 × 500 + 480 = 10 980.
        // Les mensualités à venir comptent aussi : c'est le coût du contrat.
        // V4 (aucune ligne) : repli à la volée, 12 × 300 = 3 600, sans apport daté.
        // V1 et V2 n'ont ni prix ni contrat.
        dashboard.Acquisition.Should().Be(new GpaAcquisitionDto(Total: 15_980m + 3_600m, PurchasedVehicles: 0, FinancedVehicles: 2));

        // Indépendant de la période choisie.
        var otherPeriod = await RunAsync(ctx, Admin(), new DateTime(2025, 1, 1), new DateTime(2025, 3, 31));
        otherPeriod.Acquisition.Should().Be(dashboard.Acquisition);

        var report = await OperatingReportAsync(ctx, Admin(), From, To);
        dashboard.Costs.Total.Should().Be(report.TotalCost).And.Be(1_958m,
            "les achats véhicule ne sont pas des dépenses d'exploitation : acquisition est à part");

        ctx.AcquisitionPayments.Count().Should().Be(linesBefore, "le tableau de bord ne génère jamais d'échéancier");
        ctx.AcquisitionPayments.Any(p => p.VehicleId == 4).Should().BeFalse();
    }

    [Fact]
    public async Task Le_cout_d_achats_compte_un_achat_comptant_persiste_et_un_achat_comptant_en_repli()
    {
        using var ctx = await SeedAsync();

        // V1 : achat comptant persisté (ligne « achat »), V2 : prix daté sans aucune ligne (repli).
        ctx.AcquisitionPayments.Add(new AcquisitionPayment
        {
            CompanyId = CompanyId, VehicleId = 1, Kind = AcquisitionPayment.Kinds.Achat, Seq = 1,
            DueDate = new DateOnly(2022, 3, 15), Amount = 16_500, Status = AcquisitionPayment.Statuses.Planned
        });
        var v2 = await ctx.Vehicles.FindAsync(2);
        v2!.AcquisitionType = "purchase"; v2.PurchasePrice = 26_700; v2.PurchaseDate = new DateTime(2019, 11, 15);
        await ctx.SaveChangesAsync();

        var dashboard = await RunAsync(ctx, Admin());

        dashboard.Acquisition.Should().Be(new GpaAcquisitionDto(
            Total: 16_500m + 26_700m + 15_980m + 3_600m, PurchasedVehicles: 2, FinancedVehicles: 2),
            "un achat de 2019 compte : le coût du parc ne dépend pas de la période");
    }

    [Fact]
    public async Task Le_reste_a_payer_ne_compte_que_les_mensualites_planifiees_futures()
    {
        using var ctx = await SeedAsync();

        var dashboard = await RunAsync(ctx, Admin());

        // V3 : 15 mensualités après le 11/09 (10/10/2026 → 10/12/2027), moins celle
        // payée d'avance et celle ignorée = 13 × 500. L'apport n'est pas une mensualité.
        // V4 (repli) : 05/10/2026 → 05/05/2027 = 8 × 300. La société 2 est exclue.
        dashboard.LeasingRemaining.Should().Be(new GpaLeasingRemainingDto(Amount: 6_500m + 2_400m, Contracts: 2, Installments: 21));

        // Indépendant de la période choisie.
        var otherPeriod = await RunAsync(ctx, Admin(), new DateTime(2025, 1, 1), new DateTime(2025, 3, 31));
        otherPeriod.LeasingRemaining.Should().Be(dashboard.LeasingRemaining);
    }

    // ── Interventions ────────────────────────────────────────────────────────

    [Fact]
    public async Task Les_interventions_excluent_les_reparations_annulees_et_ne_doublent_pas_un_entretien()
    {
        using var ctx = await SeedAsync();

        var dashboard = await RunAsync(ctx, Admin());

        dashboard.Interventions.Should().Be(new GpaInterventionsDto(Maintenance: 2, Repairs: 2, Total: 4),
            "entretiens = 2 dépenses (celle qui a un log n'est comptée qu'une fois, le log orphelin jamais) ; " +
            "réparations = 2 (l'annulée et la facture de sinistre n'en sont pas)");
    }

    // ── Douze mois glissants ────────────────────────────────────────────────

    [Fact]
    public async Task Les_douze_mois_retombent_sur_l_agregateur_et_le_mois_courant_est_partiel()
    {
        using var ctx = await SeedAsync();

        var dashboard = await RunAsync(ctx, Admin());

        dashboard.Monthly.Should().HaveCount(12);
        dashboard.Monthly.Select(m => (m.Year, m.Month)).First().Should().Be((2025, 10));
        dashboard.Monthly.Select(m => (m.Year, m.Month)).Last().Should().Be((2026, 9));
        dashboard.Monthly.Select(m => m.Label).Should().Equal(
            "Oct. 2025", "Nov. 2025", "Déc. 2025", "Janv. 2026", "Févr. 2026", "Mars 2026",
            "Avr. 2026", "Mai 2026", "Juin 2026", "Juil. 2026", "Août 2026", "Sept. 2026");

        var rolling = await OperatingCostAggregator.LoadAsync(ctx, Admin(),
            new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc),
            null, null, CancellationToken.None);
        var expected = rolling.Vehicles.Sum(v => v.Total.Total);

        dashboard.Monthly.Sum(m => m.Total).Should().Be(expected).And.Be(2_008m,
            "la période (1 958) + le plein de décembre 2025 (50) ; celui de septembre 2025 est hors fenêtre");
        dashboard.Monthly.Single(m => m.Month == 12).Fuel.Should().Be(50m);
        dashboard.Monthly.Single(m => m is { Year: 2026, Month: 2 }).Other.Should().Be(500m, "assurance 600 − remboursement 100");

        dashboard.Monthly.Where(m => m.IsPartial).Should().ContainSingle()
            .Which.Should().Match<GpaMonthDto>(m => m.Year == 2026 && m.Month == 9);

        // Indépendant de la période choisie.
        var otherPeriod = await RunAsync(ctx, Admin(), new DateTime(2026, 8, 1), new DateTime(2026, 8, 31));
        otherPeriod.Monthly.Should().BeEquivalentTo(dashboard.Monthly, o => o.WithStrictOrdering());
    }

    // ── Classement et limites ───────────────────────────────────────────────

    [Fact]
    public async Task Le_top5_classe_entretien_plus_reparations_sur_la_periode()
    {
        using var ctx = await SeedAsync();

        var dashboard = await RunAsync(ctx, Admin());

        dashboard.Top5.Should().Equal(
            new GpaTopVehicleDto(2, "GH-619-XC", "Logistique 01", Maintenance: 80m, Repair: 768m, Total: 848m),
            new GpaTopVehicleDto(3, "GJ-473-KS", "Leasing 01", Maintenance: 0m, Repair: 150m, Total: 150m),
            new GpaTopVehicleDto(1, "GA-214-RK", "Service 01", Maintenance: 120m, Repair: 0m, Total: 120m));
    }

    [Fact]
    public async Task Le_top5_et_les_alertes_sont_tries_et_bornes()
    {
        using var ctx = TestDbContextFactory.Create();
        var repairCosts = new Dictionary<int, (string Plate, decimal Cost)>
        {
            [1] = ("AA-1", 100), [2] = ("AA-2", 700), [3] = ("ZZ-3", 300), [4] = ("BB-4", 300),
            [5] = ("AA-5", 500), [6] = ("AA-6", 600), [7] = ("AA-7", 50)
        };
        foreach (var (id, (plate, cost)) in repairCosts)
        {
            ctx.Vehicles.Add(new Vehicle
            {
                Id = id, CompanyId = CompanyId, Name = $"V{id}", Plate = plate,
                TaxExpiry = new DateTime(2026, 7, id),                       // expirée
                InsuranceExpiry = new DateTime(2026, 8, id),                 // expirée
                TechnicalInspectionExpiry = new DateTime(2026, 9, 20 + id)   // sous 30 jours
            });
            ctx.Repairs.Add(new Repair
            {
                Id = id, SocieteId = CompanyId, VehicleId = id, Reference = $"REP-{id}",
                RepairDate = Utc(2026, 6, id), TotalCost = cost, Status = "completed"
            });
        }
        await ctx.SaveChangesAsync();

        var dashboard = await RunAsync(ctx, Admin());

        dashboard.Top5.Select(t => t.VehicleId).Should().Equal(new[] { 2, 6, 5, 4, 3 },
            "total décroissant, puis plaque à égalité (BB-4 avant ZZ-3) ; 5 au plus");

        dashboard.Alerts.Should().HaveCount(GetGpaDashboardQueryHandler.MaxAlerts, "21 échéances, 20 au plus");
        dashboard.AlertCounts.Should().Be(new GpaAlertCountsDto(Total: 21, Critical: 14), "les comptes sont pris avant la coupe");
        dashboard.Alerts.Take(14).Should().OnlyContain(a => a.Severity == "critical");
        dashboard.Alerts.Skip(14).Should().OnlyContain(a => a.Severity == "warning");
        dashboard.Alerts.Take(14).Select(a => a.Date).Should().BeInAscendingOrder();
        dashboard.Alerts.Skip(14).Select(a => a.Date).Should().BeInAscendingOrder();
        dashboard.Alerts.Last().Date.Should().Be(new DateTime(2026, 9, 26), "la 21e (27/09) est coupée");
    }

    // ── Interventions récentes ──────────────────────────────────────────────

    [Fact]
    public async Task Les_interventions_recentes_melent_entretiens_et_reparations_avec_leur_fournisseur()
    {
        using var ctx = await SeedAsync();

        var dashboard = await RunAsync(ctx, Admin());

        dashboard.RecentInterventions.Should().Equal(
            new GpaInterventionDto(Utc(2026, 8, 3), "GH-619-XC", "Logistique 01", "reparation", "Réparation · Mécanique",
                "Nettoyage FAP forcé", "Garage Central", 208_152, 468m),
            new GpaInterventionDto(Utc(2026, 7, 1), "GH-619-XC", "Logistique 01", "entretien", "Entretien",
                null, null, null, 80m),
            // Préfixe « Entretien: » retiré ; kilométrage et fournisseur du log lié par cost_id.
            new GpaInterventionDto(Utc(2026, 6, 10), "GA-214-RK", "Service 01", "entretien", "Entretien",
                "Vidange", "Garage Central", 61_050, 120m),
            // Type non saisi : « Réparation » seul, pas le type déduit ; 0 km = inconnu.
            new GpaInterventionDto(Utc(2026, 4, 15), "GJ-473-KS", "Leasing 01", "reparation", "Réparation",
                "Remplacement ampoule", null, null, 150m));
    }

    [Fact]
    public async Task Sans_description_un_entretien_prend_le_nom_du_gabarit_et_le_kilometrage_de_la_depense()
    {
        using var ctx = await SeedAsync();
        ctx.VehicleCosts.Add(new VehicleCost { Id = 20, CompanyId = CompanyId, VehicleId = 2, Type = "maintenance",
                                               Amount = 90, Date = Utc(2026, 9, 5), Description = "Entretien", Mileage = 208_390 });
        ctx.MaintenanceLogs.Add(new MaintenanceLog { Id = 20, VehicleId = 2, TemplateId = 4, CostId = 20,
                                                     DoneDate = Utc(2026, 9, 5), DoneKm = 0, ActualCost = 90 });
        await ctx.SaveChangesAsync();

        var dashboard = await RunAsync(ctx, Admin());

        var latest = dashboard.RecentInterventions.First();
        latest.Date.Should().Be(Utc(2026, 9, 5));
        latest.Description.Should().Be("Freins", "« Entretien » seul n'apprend rien : nom du gabarit du log");
        latest.MileageKm.Should().Be(208_390, "done_km = 0 est inconnu : relevé saisi sur la dépense");
        latest.Supplier.Should().BeNull();
        dashboard.RecentInterventions.Should().HaveCount(GetGpaDashboardQueryHandler.RecentCount);
    }

    // ── Échéances et alertes ────────────────────────────────────────────────

    [Fact]
    public async Task Les_alertes_signalent_les_retards_en_critical_et_les_echeances_proches_en_warning_sans_GPS()
    {
        using var ctx = await SeedAsync();

        var dashboard = await RunAsync(ctx, Admin());

        dashboard.UpcomingMaintenance.Should().Be(new GpaUpcomingMaintenanceDto(Next30Days: 1, Overdue: 2),
            "Freins le 25/09 ; Vidange au km et Courroie par date en retard ; pause et gabarit inactif ignorés");

        dashboard.Alerts.Select(a => (a.Severity, a.Title, a.Plate)).Should().Equal(
            ("critical", "Vignette expirée", "GH-619-XC"),
            ("critical", "Courroie en retard", "GH-619-XC"),
            ("critical", "Vidange en retard", "GA-214-RK"),
            ("warning", "Assurance à renouveler", "GA-214-RK"),
            ("warning", "Freins à prévoir", "GH-619-XC"),
            ("warning", "Carte grise à renouveler", "GJ-473-KS"),
            ("warning", "Visite technique à renouveler", "GJ-473-KS"));

        dashboard.Alerts.Select(a => a.Kind).Should().OnlyContain(k => k == "maintenance" || k == "document",
            "aucune alerte GPS (l'excès de vitesse en base ne remonte pas)");
        dashboard.AlertCounts.Should().Be(new GpaAlertCountsDto(Total: 7, Critical: 3));

        var vignette = dashboard.Alerts[0];
        vignette.Kind.Should().Be("document");
        vignette.Detail.Should().Be("expirée le 01/08/2026 (il y a 41 j)");
        vignette.Date.Should().Be(new DateTime(2026, 8, 1));
        vignette.DaysLeft.Should().Be(-41);
        vignette.VehicleName.Should().Be("Logistique 01");

        var courroie = dashboard.Alerts[1];
        courroie.Kind.Should().Be("maintenance");
        courroie.Detail.Should().Be("échéance du 01/09/2026 dépassée");
        courroie.DaysLeft.Should().Be(-10);

        var vidange = dashboard.Alerts[2];
        vidange.Detail.Should().Be("en retard de 1\u00A0200 km");
        vidange.Date.Should().BeNull("échéance au kilomètre seulement");
        vidange.DaysLeft.Should().BeNull();

        dashboard.Alerts[3].Detail.Should().Be("expire le 25/09/2026 (14 j)");
        dashboard.Alerts[3].DaysLeft.Should().Be(14);
        dashboard.Alerts[4].Detail.Should().Be("échéance le 25/09/2026 (14 j)");
        dashboard.Alerts[5].Detail.Should().Be("expire le 11/10/2026 (30 j)");
        dashboard.Alerts[6].Detail.Should().Be("expire le 10/11/2026 (60 j)", "J+60 est inclus pour les documents, comme l'ancienne carte Échéances");
        dashboard.Alerts.Should().NotContain(a => a.Plate == "GA-214-RK" && a.Title == "Carte grise à renouveler",
            "J+61 est hors fenêtre");
    }

    // ── Portée véhicules ────────────────────────────────────────────────────

    [Fact]
    public async Task Un_utilisateur_restreint_ne_voit_que_ses_vehicules_partout()
    {
        using var ctx = await SeedAsync();
        var tenant = Restricted(RestrictedUserId);

        var dashboard = await RunAsync(ctx, tenant);
        var report = await OperatingReportAsync(ctx, tenant, From, To);

        dashboard.Costs.Should().Be(new GpaCostsDto(Fuel: 240m, Maintenance: 80m, Repair: 768m, Other: 0m, Total: 1_088m));
        dashboard.Costs.Total.Should().Be(report.TotalCost);
        dashboard.Acquisition.Should().Be(new GpaAcquisitionDto(0m, 0, 0), "les leasings V3/V4 ne lui sont pas affectés");
        dashboard.LeasingRemaining.Should().Be(new GpaLeasingRemainingDto(0m, 0, 0));
        dashboard.Interventions.Should().Be(new GpaInterventionsDto(1, 1, 2));
        dashboard.UpcomingMaintenance.Should().Be(new GpaUpcomingMaintenanceDto(Next30Days: 1, Overdue: 1));

        dashboard.Alerts.Select(a => a.Title).Should().Equal("Vignette expirée", "Courroie en retard", "Freins à prévoir");
        dashboard.Alerts.Should().OnlyContain(a => a.Plate == "GH-619-XC");
        dashboard.Top5.Select(t => t.VehicleId).Should().Equal(2);
        dashboard.RecentInterventions.Should().OnlyContain(i => i.Plate == "GH-619-XC").And.HaveCount(2);
        dashboard.Monthly.Sum(m => m.Total).Should().Be(1_088m);
    }

    [Fact]
    public async Task Un_utilisateur_sans_affectation_ne_voit_rien()
    {
        using var ctx = await SeedAsync();

        var dashboard = await RunAsync(ctx, Restricted(OrphanUserId));

        dashboard.Costs.Total.Should().Be(0m);
        dashboard.Acquisition.Should().Be(new GpaAcquisitionDto(0m, 0, 0));
        dashboard.LeasingRemaining.Should().Be(new GpaLeasingRemainingDto(0m, 0, 0));
        dashboard.Interventions.Total.Should().Be(0);
        dashboard.UpcomingMaintenance.Should().Be(new GpaUpcomingMaintenanceDto(0, 0));
        dashboard.Alerts.Should().BeEmpty();
        dashboard.Top5.Should().BeEmpty();
        dashboard.RecentInterventions.Should().BeEmpty();
        dashboard.Monthly.Should().HaveCount(12).And.OnlyContain(m => m.Total == 0m);
    }

    // ── Action du contrôleur ────────────────────────────────────────────────

    [Fact]
    public async Task L_action_prend_l_annee_en_cours_par_defaut_tronque_au_jour_et_refuse_une_plage_inversee()
    {
        GetGpaDashboardQuery? sent = null;
        var mediator = new Mock<MediatR.IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<GetGpaDashboardQuery>(), It.IsAny<CancellationToken>()))
            .Callback<MediatR.IRequest<GpaDashboardDto>, CancellationToken>((q, _) => sent = (GetGpaDashboardQuery)q)
            .ReturnsAsync((GpaDashboardDto)null!);

        // Seul le médiateur sert à cette action : les autres dépendances restent nulles.
        var controller = new global::GisAPI.Controllers.DashboardController(null!, mediator.Object, null!, null!, null!, null!, null!)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
            }
        };

        var today = DateTime.UtcNow.Date;
        (await controller.GetGpaDashboard()).Result.Should().BeOfType<Microsoft.AspNetCore.Mvc.OkObjectResult>();
        sent.Should().Be(new GetGpaDashboardQuery(new DateTime(today.Year, 1, 1), today));

        await controller.GetGpaDashboard(new DateTime(2026, 3, 5, 14, 30, 0), new DateTime(2026, 3, 20, 9, 0, 0));
        sent.Should().Be(new GetGpaDashboardQuery(new DateTime(2026, 3, 5), new DateTime(2026, 3, 20)));

        sent = null;
        (await controller.GetGpaDashboard(new DateTime(2026, 5, 1), new DateTime(2026, 4, 1)))
            .Result.Should().BeOfType<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>();
        sent.Should().BeNull("une plage inversée n'est pas calculée");
    }

    // ── Contrat JSON ────────────────────────────────────────────────────────

    [Fact]
    public void Le_contrat_JSON_expose_exactement_les_noms_attendus_par_le_front()
    {
        var dto = new GpaDashboardDto(
            From: Now, To: Now,
            Costs: new GpaCostsDto(1, 2, 3, 4, 10),
            Acquisition: new GpaAcquisitionDto(5, 1, 1),
            LeasingRemaining: new GpaLeasingRemainingDto(6, 1, 2),
            Interventions: new GpaInterventionsDto(1, 2, 3),
            UpcomingMaintenance: new GpaUpcomingMaintenanceDto(1, 2),
            Alerts: new() { new GpaAlertDto("document", "warning", "t", "d", null, "v", null, null) },
            AlertCounts: new GpaAlertCountsDto(1, 0),
            Monthly: new() { new GpaMonthDto(2026, 9, "Sept. 2026", 1, 2, 3, 4, 10, true) },
            Top5: new() { new GpaTopVehicleDto(1, null, "v", 1, 2, 3) },
            RecentInterventions: new() { new GpaInterventionDto(Now, null, "v", "entretien", "Entretien", null, null, null, 1) });

        // Même politique de nommage que Program.cs (AddJsonOptions).
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        };
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(dto, options));
        static IEnumerable<string> Names(System.Text.Json.JsonElement e) => e.EnumerateObject().Select(p => p.Name);
        var root = doc.RootElement;

        Names(root).Should().BeEquivalentTo("from", "to", "costs", "acquisition", "leasingRemaining", "interventions",
            "upcomingMaintenance", "alerts", "alertCounts", "monthly", "top5", "recentInterventions");
        Names(root.GetProperty("alertCounts")).Should().BeEquivalentTo("total", "critical");
        Names(root.GetProperty("costs")).Should().BeEquivalentTo("fuel", "maintenance", "repair", "other", "total");
        Names(root.GetProperty("acquisition")).Should().BeEquivalentTo("total", "purchasedVehicles", "financedVehicles");
        Names(root.GetProperty("leasingRemaining")).Should().BeEquivalentTo("amount", "contracts", "installments");
        Names(root.GetProperty("interventions")).Should().BeEquivalentTo("maintenance", "repairs", "total");
        Names(root.GetProperty("upcomingMaintenance")).Should().BeEquivalentTo("next30Days", "overdue");
        Names(root.GetProperty("alerts")[0]).Should().BeEquivalentTo(
            "kind", "severity", "title", "detail", "plate", "vehicleName", "date", "daysLeft");
        Names(root.GetProperty("monthly")[0]).Should().BeEquivalentTo(
            "year", "month", "label", "fuel", "maintenance", "repair", "other", "total", "isPartial");
        Names(root.GetProperty("top5")[0]).Should().BeEquivalentTo(
            "vehicleId", "plate", "vehicleName", "maintenance", "repair", "total");
        Names(root.GetProperty("recentInterventions")[0]).Should().BeEquivalentTo(
            "date", "plate", "vehicleName", "kind", "typeLabel", "description", "supplier", "mileageKm", "cost");
    }

    // ── Libellés ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("mecanique", "Réparation · Mécanique")]
    [InlineData("Électrique", "Réparation · Électrique")]
    [InlineData("freinage", "Réparation · Freinage")]
    [InlineData("autre", "Réparation")]
    [InlineData(null, "Réparation")]
    [InlineData("  ", "Réparation")]
    public void Le_libelle_d_une_reparation_ne_montre_que_le_type_saisi(string? repairType, string expected) =>
        GetGpaDashboardQueryHandler.RepairTypeLabel(repairType).Should().Be(expected);

    [Theory]
    [InlineData(2025, 10, "Oct. 2025")]
    [InlineData(2026, 1, "Janv. 2026")]
    [InlineData(2026, 2, "Févr. 2026")]
    [InlineData(2026, 6, "Juin 2026")]
    [InlineData(2026, 8, "Août 2026")]
    [InlineData(2026, 9, "Sept. 2026")]
    [InlineData(2026, 12, "Déc. 2026")]
    public void Le_libelle_de_mois_est_l_abreviation_francaise(int year, int month, string expected) =>
        GetGpaDashboardQueryHandler.MonthLabel(year, month).Should().Be(expected);
}
