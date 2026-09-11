using System.Globalization;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Application.Features.AcquisitionPayments;
using GisAPI.Application.Features.Documents;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Dashboard.Queries.GetGpaDashboard;

/// <summary>
/// Tableau de bord d'un compte SANS GPS (Calypso GPA). Chaque bloc reprend une
/// définition déjà en service, sans en inventer :
/// <list type="bullet">
///   <item><b>costs</b>, <b>top5</b>, <b>monthly</b> et les réparations : <see cref="OperatingCostAggregator"/>,
///     la source du rapport « Coût d'exploitation » (ventilation, remboursement d'assurance
///     soustrait, réparations annulées exclues, acquisitions EXCLUES). Il applique lui-même
///     la société et la portée de l'appelant.</item>
///   <item><b>acquisition</b> / <b>leasingRemaining</b> : <see cref="AcquisitionCostCalculator"/>
///     (échéancier persisté + repli à la volée). Coût complet du parc et part encore à payer,
///     tous deux indépendants de la période. Lecture seule.</item>
///   <item><b>interventions</b> : dépenses d'entretien + réparations non annulées, comme le
///     « Rapport mensuel flotte » ; les maintenance_logs ne sont PAS comptés (chaque log a déjà sa
///     dépense via cost_id : ce serait un double compte).</item>
///   <item><b>upcomingMaintenance</b> / <b>alerts</b> : échéanciers d'entretien et échéances de
///     documents, en direct à la date du jour. Aucune alerte GPS.</item>
/// </list>
/// Tout ce qui est lu hors de l'agrégateur est borné par la société ET par
/// <see cref="VehicleScope"/>. Pas de cache : la réponse dépend de l'appelant.
/// </summary>
public class GetGpaDashboardQueryHandler : IRequestHandler<GetGpaDashboardQuery, GpaDashboardDto>
{
    public const int AlertWindowDays = 30;
    // Documents : 60 jours, comme l'ancienne carte « Échéances à venir » du
    // tableau de bord (une assurance à renouveler dans 45 jours se prépare).
    public const int DocumentWindowDays = 60;
    public const int MaxAlerts = 20;
    public const int TopCount = 5;
    public const int RecentCount = 5;

    public const string KindMaintenance = "maintenance";
    public const string KindDocument = "document";
    public const string SeverityCritical = "critical";
    public const string SeverityWarning = "warning";
    public const string InterventionEntretien = "entretien";
    public const string InterventionReparation = "reparation";

    // Libellés figés plutôt que « MMM » de fr-FR : le rendu ICU d'un conteneur ne
    // doit pas pouvoir changer ce que lit le client.
    private static readonly string[] MonthAbbreviations =
        { "Janv.", "Févr.", "Mars", "Avr.", "Mai", "Juin", "Juil.", "Août", "Sept.", "Oct.", "Nov.", "Déc." };

    // « 1 200 km » avec une espace insécable, indépendamment de la culture du serveur.
    private static readonly NumberFormatInfo KmFormat = new()
    {
        NumberGroupSeparator = "\u00A0",
        NumberGroupSizes = new[] { 3 }
    };

    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly IDateTimeProvider _clock;

    public GetGpaDashboardQueryHandler(IGisDbContext context, ICurrentTenantService tenant, IDateTimeProvider clock)
    {
        _context = context;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<GpaDashboardDto> Handle(GetGpaDashboardQuery request, CancellationToken ct)
    {
        var companyId = _tenant.CompanyId ?? 0;
        var now = _clock.UtcNow;
        var today = now.Date;

        // Période [from 00:00, to + 1 jour[ en UTC, comme les rapports de coûts.
        var startUtc = OperatingCostAggregator.StartUtc(request.From);
        var endExclusiveUtc = OperatingCostAggregator.EndExclusiveUtc(request.To);
        var lastDayUtc = endExclusiveUtc.AddDays(-1);

        // ── Agrégateur : période choisie, puis 12 mois glissants ──────────────
        var period = await OperatingCostAggregator.LoadAsync(
            _context, _tenant, startUtc, endExclusiveUtc, vehicleId: null, departmentId: null, ct);

        // Du 1er du mois d'il y a 11 mois à aujourd'hui inclus, quelle que soit la période.
        var rollingStartUtc = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-11);
        var rollingEndExclusiveUtc = OperatingCostAggregator.EndExclusiveUtc(today);
        var rolling = rollingStartUtc == startUtc && rollingEndExclusiveUtc == endExclusiveUtc
            ? period
            : await OperatingCostAggregator.LoadAsync(
                _context, _tenant, rollingStartUtc, rollingEndExclusiveUtc, vehicleId: null, departmentId: null, ct);

        // ── Périmètre de tout ce qui est calculé hors de l'agrégateur ─────────
        // Même définition que lui : société + véhicules visibles (null = tout le
        // parc, liste vide = rien).
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenant, ct);
        var vehiclesQuery = _context.Vehicles.AsNoTracking().Where(v => v.CompanyId == companyId);
        if (scope is not null)
            vehiclesQuery = vehiclesQuery.Where(v => scope.Contains(v.Id));
        var vehicles = await vehiclesQuery.OrderBy(v => v.Id).ToListAsync(ct);

        var vehicleIds = vehicles.Select(v => v.Id).ToList();
        var labels = vehicles.ToDictionary(
            v => v.Id,
            v => (Name: OperatingCostAggregator.VehicleDisplayName(v.Name, v.Brand, v.Model), v.Plate));
        var mileageById = vehicles.ToDictionary(v => v.Id, v => v.Mileage);

        // ── Acquisitions (jamais de génération d'échéancier ici) ──────────────
        // Coût d'achats = coût complet du parc (achats, apports, toutes les
        // mensualités), indépendant de la période — décision de Karim, recette
        // du 11/09/2026 : 3 360 € de mensualités échues ne disaient pas ce que
        // le parc a coûté. Le reste à payer en est la part encore à venir.
        var (acquisitionTotal, purchasedVehicles, financedVehicles) = await AcquisitionCostCalculator.FleetTotalAsync(
            _context, companyId, scope, vehicles, ct);
        var (leasingAmount, leasingContracts, leasingInstallments) = await AcquisitionCostCalculator.LeasingRemainingAsync(
            _context, companyId, scope, vehicles, now, ct);

        // ── Entretiens saisis de la période (dépenses maintenance/entretien) ──
        var maintenanceCosts = await LoadMaintenanceCostsAsync(companyId, vehicleIds, startUtc, endExclusiveUtc, ct);

        // ── Échéances d'entretien et de documents, à la date du jour ──────────
        var schedules = await LoadSchedulesAsync(companyId, vehicleIds, ct);
        var (upcoming, maintenanceAlerts) = BuildMaintenance(schedules, labels, mileageById, today);
        var documentAlerts = BuildDocumentAlerts(vehicles, labels, today);

        var allAlerts = maintenanceAlerts.Concat(documentAlerts).ToList();
        var alertCounts = new GpaAlertCountsDto(allAlerts.Count, allAlerts.Count(a => a.Severity == SeverityCritical));
        var alerts = allAlerts
            .OrderBy(a => a.Severity == SeverityCritical ? 0 : 1)
            .ThenBy(a => a.Date.HasValue ? 0 : 1)
            .ThenBy(a => a.Date)
            .ThenBy(a => a.Plate ?? a.VehicleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Title, StringComparer.Ordinal)
            .Take(MaxAlerts)
            .ToList();

        // ── Coûts de la période (définition du rapport « Coût d'exploitation ») ──
        var costs = Costs(period.Vehicles.Aggregate(CostBucket.Zero, (acc, v) => acc.Plus(v.Total)));

        // ── Interventions : les réparations viennent de l'agrégateur (non annulées) ──
        var interventions = new GpaInterventionsDto(
            Maintenance: maintenanceCosts.Count,
            Repairs: period.Repairs.Count,
            Total: maintenanceCosts.Count + period.Repairs.Count);

        var recentInterventions = await BuildRecentInterventionsAsync(companyId, maintenanceCosts, period.Repairs, labels, ct);

        return new GpaDashboardDto(
            From: startUtc,
            To: lastDayUtc,
            Costs: costs,
            Acquisition: new GpaAcquisitionDto(R(acquisitionTotal), purchasedVehicles, financedVehicles),
            LeasingRemaining: new GpaLeasingRemainingDto(R(leasingAmount), leasingContracts, leasingInstallments),
            Interventions: interventions,
            UpcomingMaintenance: upcoming,
            Alerts: alerts,
            AlertCounts: alertCounts,
            Monthly: BuildMonthly(rolling),
            Top5: BuildTop5(period),
            RecentInterventions: recentInterventions);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Lectures
    // ═════════════════════════════════════════════════════════════════════════

    private sealed record MaintenanceCostRow(int Id, int VehicleId, DateTime Date, string? Description, decimal Amount, int? Mileage);

    private sealed record ScheduleRow(int VehicleId, string Status, DateTime? NextDueDate, int? NextDueKm, string TemplateName);

    private sealed record LogRow(int Id, int CostId, int TemplateId, int DoneKm, int? SupplierId);

    /// <summary>Même normalisation que l'agrégateur (trim + minuscules).</summary>
    private static bool IsMaintenanceType(string? type) =>
        (type ?? string.Empty).Trim().ToLowerInvariant() is "maintenance" or "entretien";

    private async Task<List<MaintenanceCostRow>> LoadMaintenanceCostsAsync(
        int companyId, List<int> vehicleIds, DateTime startUtc, DateTime endExclusiveUtc, CancellationToken ct)
    {
        if (vehicleIds.Count == 0) return new List<MaintenanceCostRow>();

        // Pré-filtre SQL large (sur-ensemble), puis la règle exacte en mémoire :
        // le type est comparé exactement comme l'agrégateur le ventile, sinon le
        // nombre d'entretiens et leur montant ne porteraient pas sur les mêmes lignes.
        var rows = await _context.VehicleCosts.AsNoTracking()
            .Where(c => c.CompanyId == companyId
                     && vehicleIds.Contains(c.VehicleId)
                     && c.Date >= startUtc
                     && c.Date < endExclusiveUtc
                     && (c.Type.ToLower().Contains("maintenance") || c.Type.ToLower().Contains("entretien")))
            .Select(c => new { c.Id, c.VehicleId, c.Type, c.Date, c.Description, c.Amount, c.Mileage })
            .ToListAsync(ct);

        return rows
            .Where(c => IsMaintenanceType(c.Type))
            .Select(c => new MaintenanceCostRow(c.Id, c.VehicleId, c.Date, c.Description, c.Amount, c.Mileage))
            .ToList();
    }

    private async Task<List<ScheduleRow>> LoadSchedulesAsync(int companyId, List<int> vehicleIds, CancellationToken ct)
    {
        if (vehicleIds.Count == 0) return new List<ScheduleRow>();

        // Même périmètre que les écrans d'entretien : non en pause, gabarit actif.
        return await _context.VehicleMaintenanceSchedules.AsNoTracking()
            .Where(s => s.CompanyId == companyId
                     && !s.IsPaused
                     && vehicleIds.Contains(s.VehicleId)
                     && s.Template!.IsActive)
            .Select(s => new ScheduleRow(s.VehicleId, s.Status, s.NextDueDate, s.NextDueKm, s.Template!.Name))
            .ToListAsync(ct);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Échéances et alertes
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// En retard = statut « overdue » posé par MaintenanceSchedulerService, OU
    /// échéance dépassée à l'instant (kilométrage du véhicule au-delà de
    /// next_due_km, ou next_due_date passée) — les deux critères du planificateur,
    /// réévalués ici pour ne pas dépendre de son dernier passage.
    /// À prévoir sous 30 jours = échéance PAR DATE entre aujourd'hui et J+30 (hors
    /// retard). Une alerte « à prévoir » est aussi levée pour un statut due/critical
    /// (seuils km ou jours du gabarit), sans entrer dans le compteur par date.
    /// </summary>
    private static (GpaUpcomingMaintenanceDto Upcoming, List<GpaAlertDto> Alerts) BuildMaintenance(
        List<ScheduleRow> schedules,
        IReadOnlyDictionary<int, (string Name, string? Plate)> labels,
        IReadOnlyDictionary<int, int> mileageById,
        DateTime today)
    {
        var windowEnd = today.AddDays(AlertWindowDays);
        var next30 = 0;
        var overdue = 0;
        var alerts = new List<GpaAlertDto>();

        foreach (var s in schedules)
        {
            var mileage = mileageById.GetValueOrDefault(s.VehicleId);
            var (name, plate) = LabelOf(labels, s.VehicleId);
            var template = string.IsNullOrWhiteSpace(s.TemplateName) ? "Entretien" : s.TemplateName.Trim();

            DateTime? due = s.NextDueDate?.Date;
            int? daysLeft = due.HasValue ? (due.Value - today).Days : null;
            int? kmOver = s.NextDueKm is int dueKm && mileage > dueKm ? mileage - dueKm : null;
            var dateOver = due.HasValue && due.Value < today;

            // Le titre nomme l'entretien (« Vidange en retard ») : une liste de douze
            // « Entretien en retard » identiques ne disait pas lequel. Le détail
            // garde le chiffre utile, lisible sans ouvrir l'info-bulle.
            if (StatusIs(s.Status, "overdue") || kmOver.HasValue || dateOver)
            {
                overdue++;
                var parts = new List<string>();
                if (kmOver.HasValue) parts.Add($"en retard de {Km(kmOver.Value)} km");
                if (dateOver) parts.Add($"échéance du {Day(due!.Value)} dépassée");
                if (parts.Count == 0) parts.Add("signalé en retard");   // statut posé par le planificateur

                alerts.Add(new GpaAlertDto(KindMaintenance, SeverityCritical, $"{template} en retard",
                    string.Join(" · ", parts), plate, name, AsUtcDay(due), daysLeft));
                continue;
            }

            var inWindow = due.HasValue && due.Value >= today && due.Value <= windowEnd;
            if (inWindow) next30++;

            if (inWindow || StatusIs(s.Status, "due") || StatusIs(s.Status, "critical"))
            {
                var parts = new List<string>();
                if (due.HasValue) parts.Add($"échéance le {Day(due.Value)} ({daysLeft} j)");
                // Kilométrage 0 = jamais renseigné : « dans 60 000 km » ne dirait rien.
                if (s.NextDueKm is int nextKm && mileage > 0) parts.Add($"dans {Km(Math.Max(0, nextKm - mileage))} km");

                alerts.Add(new GpaAlertDto(KindMaintenance, SeverityWarning, $"{template} à prévoir",
                    string.Join(" · ", parts), plate, name, AsUtcDay(due), daysLeft));
            }
        }

        return (new GpaUpcomingMaintenanceDto(next30, overdue), alerts);
    }

    /// <summary>
    /// Échéances de documents du véhicule (<see cref="VehicleDocumentExpiries"/>,
    /// la liste de GetExpiryAlertsQueryHandler) : expirée → critical, sous 60 jours
    /// (bornes incluses, même horizon que l'ancienne carte du tableau de bord) → warning.
    /// </summary>
    private static List<GpaAlertDto> BuildDocumentAlerts(
        IEnumerable<GisAPI.Domain.Entities.Vehicle> vehicles,
        IReadOnlyDictionary<int, (string Name, string? Plate)> labels,
        DateTime today)
    {
        var alerts = new List<GpaAlertDto>();

        foreach (var vehicle in vehicles)
        {
            var (name, plate) = LabelOf(labels, vehicle.Id);
            foreach (var (type, expiry) in VehicleDocumentExpiries.Of(vehicle))
            {
                if (expiry is null) continue;

                var day = expiry.Value.Date;
                var days = (day - today).Days;
                if (days > DocumentWindowDays) continue;

                var label = VehicleDocumentExpiries.Label(type);
                alerts.Add(days < 0
                    ? new GpaAlertDto(KindDocument, SeverityCritical, $"{label} expirée",
                        $"expirée le {Day(day)} (il y a {-days} j)", plate, name, AsUtcDay(day), days)
                    : new GpaAlertDto(KindDocument, SeverityWarning, $"{label} à renouveler",
                        $"expire le {Day(day)} ({days} j)", plate, name, AsUtcDay(day), days));
            }
        }

        return alerts;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Mois, classement, interventions récentes
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Un point par mois de la plage, mois vides compris. Le mois en cours est
    /// INCOMPLET (la plage s'arrête avant sa fin), comme dans « Évolution
    /// mensuelle » : le front ne doit pas le lire comme une baisse.
    /// </summary>
    private static List<GpaMonthDto> BuildMonthly(OperatingCostData rolling) =>
        rolling.MonthsInRange()
            .Select(m =>
            {
                var bucket = rolling.Vehicles.Aggregate(CostBucket.Zero,
                    (acc, v) => acc.Plus(v.Months.GetValueOrDefault(m, CostBucket.Zero)));
                var monthEndExclusive = new DateTime(m.Year, m.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
                var c = Costs(bucket);

                return new GpaMonthDto(
                    Year: m.Year,
                    Month: m.Month,
                    Label: MonthLabel(m.Year, m.Month),
                    Fuel: c.Fuel,
                    Maintenance: c.Maintenance,
                    Repair: c.Repair,
                    Other: c.Other,
                    Total: c.Total,
                    IsPartial: rolling.EndExclusiveUtc < monthEndExclusive);
            })
            .ToList();

    /// <summary>Les véhicules qui coûtent le plus en entretien + réparations sur la période.</summary>
    private static List<GpaTopVehicleDto> BuildTop5(OperatingCostData period) =>
        period.Vehicles
            .Select(v => (Vehicle: v, Total: v.Total.Maintenance + v.Total.Repair))
            .Where(x => x.Total > 0)
            .OrderByDescending(x => x.Total)
            .ThenBy(x => string.IsNullOrWhiteSpace(x.Vehicle.Plate) ? 1 : 0)
            .ThenBy(x => x.Vehicle.Plate, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Vehicle.VehicleId)
            .Take(TopCount)
            .Select(x =>
            {
                var maintenance = R(x.Vehicle.Total.Maintenance);
                var repair = R(x.Vehicle.Total.Repair);
                return new GpaTopVehicleDto(
                    x.Vehicle.VehicleId, x.Vehicle.Plate, x.Vehicle.VehicleName,
                    maintenance, repair, maintenance + repair);
            })
            .ToList();

    /// <summary>
    /// Entretiens saisis et réparations non annulées de la période, les plus
    /// récents d'abord. Un entretien est enrichi par le maintenance_log qui
    /// référence sa dépense (cost_id) : gabarit, kilométrage, fournisseur.
    /// </summary>
    private async Task<List<GpaInterventionDto>> BuildRecentInterventionsAsync(
        int companyId,
        List<MaintenanceCostRow> maintenanceCosts,
        IReadOnlyList<RepairRow> repairs,
        IReadOnlyDictionary<int, (string Name, string? Plate)> labels,
        CancellationToken ct)
    {
        // Les N plus récents de chaque source suffisent à former les N plus récents de l'union.
        var recentCosts = maintenanceCosts
            .OrderByDescending(c => c.Date).ThenByDescending(c => c.Id)
            .Take(RecentCount)
            .ToList();
        var recentRepairs = repairs
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.Id)
            .Take(RecentCount)
            .ToList();

        // Logs liés par cost_id, SANS filtre société : MaintenanceLog n'est pas une
        // TenantEntity et son company_id n'est pas garanti (0 sur d'anciennes lignes).
        // Le périmètre est déjà borné par les dépenses retenues, elles-mêmes scopées.
        var costIds = recentCosts.Select(c => c.Id).ToList();
        var logs = costIds.Count == 0
            ? new List<LogRow>()
            : await _context.MaintenanceLogs.AsNoTracking()
                .Where(l => l.CostId != null && costIds.Contains(l.CostId.Value))
                .Select(l => new LogRow(l.Id, l.CostId!.Value, l.TemplateId, l.DoneKm, l.SupplierId))
                .ToListAsync(ct);
        var logByCost = logs
            .GroupBy(l => l.CostId)
            .ToDictionary(g => g.Key, g => g.OrderBy(l => l.Id).First());

        var templateIds = logByCost.Values.Select(l => l.TemplateId).Distinct().ToList();
        var templateNames = templateIds.Count == 0
            ? new Dictionary<int, string>()
            : await _context.MaintenanceTemplates.AsNoTracking()
                .Where(t => t.CompanyId == companyId && templateIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Name })
                .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        // Fournisseurs (TenantEntity) : filtre société explicite, en une requête.
        var supplierIds = logByCost.Values.Where(l => l.SupplierId.HasValue).Select(l => l.SupplierId!.Value)
            .Concat(recentRepairs.Where(r => r.SupplierId.HasValue).Select(r => r.SupplierId!.Value))
            .Distinct()
            .ToList();
        var supplierNames = supplierIds.Count == 0
            ? new Dictionary<int, string>()
            : await _context.Suppliers.AsNoTracking()
                .Where(s => s.CompanyId == companyId && supplierIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Name })
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        string? SupplierOf(int? id) =>
            id.HasValue && supplierNames.TryGetValue(id.Value, out var supplier) ? NullIfBlank(supplier) : null;

        var items = new List<(DateTime Date, int Id, GpaInterventionDto Dto)>();

        foreach (var c in recentCosts)
        {
            logByCost.TryGetValue(c.Id, out var log);
            var template = log is not null && templateNames.TryGetValue(log.TemplateId, out var t) ? NullIfBlank(t) : null;

            // done_km = 0 : relevé inconnu. À défaut, le kilométrage saisi sur la dépense.
            int? km = log is { DoneKm: > 0 } ? log.DoneKm
                    : c.Mileage is > 0 ? c.Mileage
                    : null;

            var (name, plate) = LabelOf(labels, c.VehicleId);
            items.Add((c.Date, c.Id, new GpaInterventionDto(
                Date: c.Date,
                Plate: plate,
                VehicleName: name,
                Kind: InterventionEntretien,
                TypeLabel: "Entretien",
                Description: MaintenanceDescription(c.Description) ?? template,
                Supplier: SupplierOf(log?.SupplierId),
                MileageKm: km,
                Cost: R(c.Amount))));
        }

        foreach (var r in recentRepairs)
        {
            var (name, plate) = LabelOf(labels, r.VehicleId);
            items.Add((r.Date, r.Id, new GpaInterventionDto(
                Date: r.Date,
                Plate: plate,
                VehicleName: name,
                Kind: InterventionReparation,
                TypeLabel: RepairTypeLabel(r.RepairType),
                Description: NullIfBlank(r.Description),
                Supplier: SupplierOf(r.SupplierId),
                MileageKm: r.MileageAtRepair is > 0 ? r.MileageAtRepair : null,
                Cost: R(r.TotalCost))));
        }

        return items
            .OrderByDescending(i => i.Date)
            .ThenByDescending(i => i.Id)
            .Take(RecentCount)
            .Select(i => i.Dto)
            .ToList();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Mise en forme
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>« Oct. 2025 ».</summary>
    public static string MonthLabel(int year, int month) => $"{MonthAbbreviations[month - 1]} {year}";

    /// <summary>
    /// « Réparation · Mécanique » quand le type d'intervention a été SAISI
    /// (colonne repair_type) ; « Réparation » sinon. Le type déduit de la
    /// description par <see cref="RepairTypeClassifier"/> n'est pas affiché ici :
    /// ce serait présenter une supposition comme une donnée.
    /// </summary>
    public static string RepairTypeLabel(string? repairType)
    {
        if (string.IsNullOrWhiteSpace(repairType)) return "Réparation";

        var (type, _) = RepairTypeClassifier.Classify(repairType, description: null);
        return type == RepairTypeClassifier.Autre
            ? "Réparation"
            : $"Réparation · {RepairTypeClassifier.Label(type)}";
    }

    /// <summary>
    /// Description d'une dépense d'entretien sans le préfixe « Entretien: »
    /// qu'y ajoute « marquer fait » (la ligne dit déjà « Entretien »). Une
    /// description réduite à « Entretien » n'apprend rien : null, et le nom du
    /// gabarit prend le relais.
    /// </summary>
    private static string? MaintenanceDescription(string? description)
    {
        var text = description?.Trim();
        if (string.IsNullOrEmpty(text)) return null;

        const string prefix = "Entretien:";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            text = text[prefix.Length..].Trim();

        return text.Length == 0 || string.Equals(text, "Entretien", StringComparison.OrdinalIgnoreCase)
            ? null
            : text;
    }

    private static (string Name, string? Plate) LabelOf(
        IReadOnlyDictionary<int, (string Name, string? Plate)> labels, int vehicleId) =>
        labels.TryGetValue(vehicleId, out var label) ? label : ($"Véhicule #{vehicleId}", null);

    private static bool StatusIs(string? status, string expected) =>
        string.Equals(status?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal R(decimal value) => Math.Round(value, 2);

    /// <summary>
    /// Parts arrondies au centime et total = somme des parts AFFICHÉES : la ligne
    /// TOTAL de l'écran retombe toujours sur ses lignes (en TND à 3 décimales,
    /// arrondir le total à part pouvait donner ±0,01 d'écart).
    /// </summary>
    private static GpaCostsDto Costs(CostBucket b)
    {
        var fuel = R(b.Fuel);
        var maintenance = R(b.Maintenance);
        var repair = R(b.Repair);
        var other = R(b.Other);
        return new GpaCostsDto(fuel, maintenance, repair, other, fuel + maintenance + repair + other);
    }

    private static string Day(DateTime day) => day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string Km(int km) => km.ToString("#,0", KmFormat);

    private static DateTime? AsUtcDay(DateTime? day) =>
        day.HasValue ? DateTime.SpecifyKind(day.Value.Date, DateTimeKind.Utc) : null;
}
