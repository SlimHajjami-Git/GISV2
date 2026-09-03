using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.VehicleMaintenance.Queries;

public class GetMaintenanceAlertsQueryHandler : IRequestHandler<GetMaintenanceAlertsQuery, List<MaintenanceItemDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public GetMaintenanceAlertsQueryHandler(IGisDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<List<MaintenanceItemDto>> Handle(GetMaintenanceAlertsQuery request, CancellationToken cancellationToken)
    {
        var companyId = _tenant.CompanyId ?? 0;

        // Fuite constatée : le filtre société ne suffisait pas, un employé restreint
        // à quelques véhicules voyait les alertes d'entretien de TOUT le parc.
        // scope == null => administrateur de société : aucun filtre supplémentaire.
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenant, cancellationToken);

        var query = _context.VehicleMaintenanceSchedules
            .Include(s => s.Template)
            .Include(s => s.Vehicle)
                .ThenInclude(v => v!.GpsDevice)
            .Where(s => s.CompanyId == companyId && (s.Status == "overdue" || s.Status == "due"));

        if (scope is not null)
            query = query.Where(s => scope.Contains(s.VehicleId));

        var schedules = await query.ToListAsync(cancellationToken);

        var today = DateTime.UtcNow.Date;

        // Firmware "L": batch fetch latest odometer_km
        var firmwareLDeviceIds = schedules
            .Where(s => s.Vehicle?.GpsDevice != null
                     && !string.IsNullOrEmpty(s.Vehicle.GpsDevice.FirmwareVersion)
                     && s.Vehicle.GpsDevice.FirmwareVersion.StartsWith("L", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Vehicle!.GpsDevice!.Id)
            .Distinct()
            .ToList();

        var odometerMap = new Dictionary<int, long>();
        if (firmwareLDeviceIds.Any())
        {
            var latestOdometers = await _context.GpsPositions
                .Where(p => firmwareLDeviceIds.Contains(p.DeviceId)
                         && p.OdometerKm.HasValue && p.OdometerKm > 0
                         && p.OdometerKm != 1048574)
                .GroupBy(p => p.DeviceId)
                .Select(g => new { DeviceId = g.Key, OdometerKm = g.OrderByDescending(p => p.RecordedAt).First().OdometerKm })
                .ToListAsync(cancellationToken);
            odometerMap = latestOdometers.ToDictionary(x => x.DeviceId, x => x.OdometerKm ?? 0);
        }

        return schedules.Select(s =>
        {
            var vehicleMileage = s.Vehicle?.Mileage ?? 0;
            if (s.Vehicle?.GpsDevice != null && odometerMap.TryGetValue(s.Vehicle.GpsDevice.Id, out var odo) && odo > 0)
                vehicleMileage = (int)odo;

            var kmUntilDue = s.NextDueKm.HasValue && s.Vehicle != null 
                ? s.NextDueKm.Value - vehicleMileage 
                : (int?)null;
            var daysUntilDue = s.NextDueDate.HasValue 
                ? (int)(s.NextDueDate.Value - today).TotalDays 
                : (int?)null;

            return new MaintenanceItemDto(
                s.Id,
                s.TemplateId,
                s.Template?.Name ?? "",
                s.Template?.Category ?? "",
                s.Template?.Priority ?? "medium",
                s.LastDoneDate,
                s.LastDoneKm,
                s.NextDueDate,
                s.NextDueKm,
                s.Status,
                kmUntilDue,
                daysUntilDue
            );
        })
        .OrderBy(i => i.Status == "overdue" ? 0 : 1)
        .ThenBy(i => i.DaysUntilDue ?? int.MaxValue)
        .ToList();
    }
}

public class GetMaintenanceStatsQueryHandler : IRequestHandler<GetMaintenanceStatsQuery, MaintenanceStatsDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public GetMaintenanceStatsQueryHandler(IGisDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<MaintenanceStatsDto> Handle(GetMaintenanceStatsQuery request, CancellationToken cancellationToken)
    {
        var companyId = _tenant.CompanyId ?? 0;

        // Fuite constatée : ces compteurs étaient calculés sur tout le parc de la
        // société, un employé restreint voyait donc des totaux qui ne le concernaient
        // pas. La restriction est appliquée AVANT l'agrégation.
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenant, cancellationToken);

        var query = _context.VehicleMaintenanceSchedules
            .Where(s => !s.IsPaused && s.CompanyId == companyId);

        if (scope is not null)
            query = query.Where(s => scope.Contains(s.VehicleId));

        var schedules = await query.ToListAsync(cancellationToken);

        return new MaintenanceStatsDto(
            schedules.Count,
            schedules.Count(s => s.Status == "overdue"),
            schedules.Count(s => s.Status == "due" || s.Status == "critical"),
            schedules.Count(s => s.Status == "upcoming"),
            schedules.Count(s => s.Status == "ok")
        );
    }
}

public class GetVehicleMaintenanceSchedulesQueryHandler : IRequestHandler<GetVehicleMaintenanceSchedulesQuery, List<VehicleScheduleDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly GisAPI.Application.Services.IMaintenanceSchedulerService _scheduler;

    public GetVehicleMaintenanceSchedulesQueryHandler(IGisDbContext context, ICurrentTenantService tenant, GisAPI.Application.Services.IMaintenanceSchedulerService scheduler)
    {
        _context = context;
        _tenant = tenant;
        _scheduler = scheduler;
    }

    public async Task<List<VehicleScheduleDto>> Handle(GetVehicleMaintenanceSchedulesQuery request, CancellationToken cancellationToken)
    {
        var companyId = _tenant.CompanyId ?? 0;

        // Fuite constatée : l'identifiant de véhicule venait de l'URL sans contrôle,
        // un employé restreint pouvait donc lire les échéances de n'importe quel
        // véhicule du parc (scope == null => admin de société, il voit tout).
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenant, cancellationToken);

        var currentKm = await _scheduler.GetCurrentMileageAsync(request.VehicleId, cancellationToken);
        var today = DateTime.UtcNow.Date;

        var query = _context.VehicleMaintenanceSchedules
            .Include(s => s.Template)
            .Where(s => s.VehicleId == request.VehicleId && s.CompanyId == companyId && s.Template!.IsActive);

        if (scope is not null)
            query = query.Where(s => scope.Contains(s.VehicleId));

        var schedules = await query
            .OrderBy(s => s.Status == "overdue" ? 0 : s.Status == "critical" ? 1 : s.Status == "due" ? 2 : 3)
            .ThenBy(s => s.NextDueKm)
            .ToListAsync(cancellationToken);

        return schedules.Select(s => new VehicleScheduleDto(
            s.Id,
            s.TemplateId,
            s.Template?.Name ?? "",
            s.Template?.Category ?? "",
            s.Template?.Priority ?? "medium",
            s.LastDoneDate,
            s.LastDoneKm,
            s.NextDueDate,
            s.NextDueKm,
            s.Status,
            s.IsPaused,
            s.PausedReason,
            s.CustomIntervalKm,
            s.CustomIntervalMonths,
            s.NextDueKm.HasValue ? s.NextDueKm.Value - currentKm : null,
            s.NextDueDate.HasValue ? (int)(s.NextDueDate.Value - today).TotalDays : null,
            s.Template?.EstimatedCost
        )).ToList();
    }
}

public class GetMaintenanceNotificationsQueryHandler : IRequestHandler<GetMaintenanceNotificationsQuery, List<MaintenanceNotificationDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public GetMaintenanceNotificationsQueryHandler(IGisDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<List<MaintenanceNotificationDto>> Handle(GetMaintenanceNotificationsQuery request, CancellationToken cancellationToken)
    {
        // MaintenanceNotification n'a PAS de filtre global de société (contrairement
        // aux échéances) : le filtre par companyId ci-dessous est donc obligatoire.
        var companyId = _tenant.CompanyId ?? 0;

        // Fuite constatée : un employé restreint recevait les notifications
        // d'entretien de tout le parc (scope == null => admin de société, voit tout).
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenant, cancellationToken);

        var query = _context.MaintenanceNotifications
            .Include(n => n.Vehicle)
            .Include(n => n.Template)
            .Where(n => n.CompanyId == companyId);

        if (scope is not null)
            query = query.Where(n => scope.Contains(n.VehicleId));

        if (request.UnacknowledgedOnly)
        {
            query = query.Where(n => n.AcknowledgedAt == null);
        }

        var notifications = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        return notifications.Select(n => new MaintenanceNotificationDto(
            n.Id,
            n.ScheduleId,
            n.VehicleId,
            n.Vehicle?.Name ?? "",
            n.Vehicle?.Plate,
            n.Template?.Name ?? "",
            n.NotificationType,
            n.TriggerReason,
            n.KmRemaining,
            n.DaysRemaining,
            n.CreatedAt,
            n.AcknowledgedAt
        )).ToList();
    }
}

public class GetTemplatePartsQueryHandler : IRequestHandler<GetTemplatePartsQuery, List<TemplatePartDto>>
{
    private readonly IGisDbContext _context;

    public GetTemplatePartsQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<List<TemplatePartDto>> Handle(GetTemplatePartsQuery request, CancellationToken cancellationToken)
    {
        // Pas de portée « véhicules accessibles » ici : une pièce est rattachée à un
        // MODÈLE d'entretien, pas à un véhicule. On garde en revanche le rattachement
        // au modèle, lui-même filtré par société, pour qu'un identifiant de modèle
        // deviné dans l'URL ne révèle pas les pièces d'une autre société.
        var parts = await _context.MaintenanceTemplateParts
            .Include(p => p.PreferredSupplier)
            .Where(p => p.TemplateId == request.TemplateId
                     && _context.MaintenanceTemplates.Any(t => t.Id == p.TemplateId))
            .OrderBy(p => p.PartName)
            .ToListAsync(cancellationToken);

        return parts.Select(p => new TemplatePartDto(
            p.Id,
            p.PartName,
            p.PartNumber,
            p.Quantity,
            p.Unit,
            p.EstimatedUnitCost,
            p.IsRequired,
            p.PreferredSupplierId,
            p.PreferredSupplier?.Name
        )).ToList();
    }
}

public class GetMaintenanceLogsQueryHandler : IRequestHandler<GetMaintenanceLogsQuery, List<MaintenanceLogDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public GetMaintenanceLogsQueryHandler(IGisDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<List<MaintenanceLogDto>> Handle(GetMaintenanceLogsQuery request, CancellationToken cancellationToken)
    {
        // MaintenanceLog n'a PAS de filtre global de société : le filtre companyId
        // ci-dessous est obligatoire, l'identifiant de véhicule venant de l'URL.
        var companyId = _tenant.CompanyId ?? 0;

        // Fuite constatée : un employé restreint pouvait lire l'historique
        // d'entretien de n'importe quel véhicule (scope == null => admin, voit tout).
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenant, cancellationToken);

        var query = _context.MaintenanceLogs
            .Include(l => l.Template)
            .Include(l => l.Supplier)
            .Where(l => l.VehicleId == request.VehicleId && l.CompanyId == companyId);

        if (scope is not null)
            query = query.Where(l => scope.Contains(l.VehicleId));

        if (request.TemplateId.HasValue)
            query = query.Where(l => l.TemplateId == request.TemplateId.Value);

        var logs = await query
            .OrderByDescending(l => l.DoneDate)
            .Take(50)
            .ToListAsync(cancellationToken);

        return logs.Select(l => new MaintenanceLogDto(
            l.Id,
            l.VehicleId,
            l.TemplateId,
            l.Template?.Name ?? "",
            l.DoneDate,
            l.DoneKm,
            l.ActualCost,
            l.Supplier?.Name,
            l.Notes
        )).ToList();
    }
}

public class GetAllMaintenanceLogsQueryHandler : IRequestHandler<GetAllMaintenanceLogsQuery, List<MaintenanceLogReportDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public GetAllMaintenanceLogsQueryHandler(IGisDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<List<MaintenanceLogReportDto>> Handle(GetAllMaintenanceLogsQuery request, CancellationToken ct)
    {
        var companyId = _tenant.CompanyId ?? 0;

        // Fuite constatée : ce rapport listait l'entretien de TOUT le parc alors qu'un
        // employé restreint ne doit voir que ses véhicules affectés
        // (scope == null => administrateur de société : aucun filtre supplémentaire).
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenant, ct);

        // ── Entretiens RÉALISÉS = les DÉPENSES d'entretien ──────────────────
        //
        // La source est VehicleCost (type maintenance), pas MaintenanceLog, pour
        // deux raisons constatées en production le 03/09/2026 :
        //
        //  1. `maintenance_logs.company_id` vaut 0 sur la TOTALITÉ de la table
        //     (47 lignes, 6 mois, toutes sociétés) : MaintenanceLog n'implémente
        //     pas ITenantEntity, donc le stamping automatique de SaveChangesAsync
        //     ne le renseigne jamais. Le filtre `l.CompanyId == companyId` ne
        //     matchait donc AUCUNE ligne et le rapport sortait vide pour tous les
        //     clients — seuls les entretiens PLANIFIÉS s'affichaient, avec un coût
        //     « — » et un kilométrage qui est en réalité celui de la prochaine
        //     échéance (next_due_km).
        //  2. Un MaintenanceLog n'existe que pour les entretiens passés par
        //     « marquer fait ». Ceux saisis depuis l'écran Dépenses et ceux venus
        //     de l'import Excel n'en ont pas : 23 dépenses (3 892 €) restaient
        //     invisibles même en corrigeant le point 1.
        //
        // La dépense est la source de vérité unique du coût d'entretien (décision
        // du commit 4540744, qui a aligné tableau de bord / entretiens / dépenses).
        // Ce rapport s'y aligne à son tour. Le log éventuellement lié n'est plus
        // utilisé que pour enrichir la ligne (modèle d'entretien, kilométrage).
        var costsQuery = _context.VehicleCosts
            .Include(c => c.Vehicle)
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId
                     && (c.Type == "maintenance" || c.Type == "entretien"));

        if (scope is not null)
            costsQuery = costsQuery.Where(c => scope.Contains(c.VehicleId));

        if (request.VehicleId.HasValue)
            costsQuery = costsQuery.Where(c => c.VehicleId == request.VehicleId.Value);

        if (request.StartDate.HasValue)
            costsQuery = costsQuery.Where(c => c.Date >= request.StartDate.Value);

        if (request.EndDate.HasValue)
            costsQuery = costsQuery.Where(c => c.Date <= request.EndDate.Value);

        var costs = await costsQuery
            .OrderByDescending(c => c.Date)
            .ToListAsync(ct);

        // Logs liés à ces dépenses : ils portent le modèle d'entretien et le
        // relevé kilométrique du jour de l'intervention. Jointure par cost_id,
        // sans filtre de société (la colonne n'est pas fiable, cf. plus haut) —
        // le périmètre est déjà borné par les dépenses retenues.
        var costIds = costs.Select(c => c.Id).ToList();
        var linkedLogs = costIds.Count == 0
            ? new List<GisAPI.Domain.Entities.MaintenanceLog>()
            : await _context.MaintenanceLogs
                .Include(l => l.Template)
                .Include(l => l.Supplier)
                .AsNoTracking()
                .Where(l => l.CostId != null && costIds.Contains(l.CostId.Value))
                .ToListAsync(ct);
        var logByCostId = linkedLogs
            .GroupBy(l => l.CostId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var result = costs.Select(c =>
        {
            logByCostId.TryGetValue(c.Id, out var log);
            // Le libellé d'une dépense issue de « marquer fait » est préfixé
            // « Entretien: » — inutile de le répéter dans une colonne Type.
            var label = (c.Description ?? string.Empty).StartsWith("Entretien: ")
                ? c.Description!.Substring("Entretien: ".Length)
                : c.Description;
            return new MaintenanceLogReportDto(
                c.Id,
                c.VehicleId,
                c.Vehicle?.Name ?? $"Véhicule {c.VehicleId}",
                c.Vehicle?.Plate,
                log?.TemplateId ?? 0,
                log?.Template?.Name ?? (string.IsNullOrWhiteSpace(label) ? "Général" : label!),
                log?.Template?.Category,
                c.Date,
                c.Mileage ?? log?.DoneKm ?? 0,
                c.Amount,
                log?.LaborCost,
                log?.PartsCost,
                log?.Supplier?.Name,
                log?.Notes,
                "completed"
            );
        }).ToList();

        // Also surface PLANNED/scheduled maintenances (entretiens créés/assignés mais pas encore
        // "marqués faits"). They live in VehicleMaintenanceSchedule (tenant auto-filtré) et étaient
        // invisibles car le rapport ne lisait que les MaintenanceLogs terminés — d'où le rapport
        // vide pour les utilisateurs qui n'avaient fait que créer/assigner des entretiens.
        var schedQuery = _context.VehicleMaintenanceSchedules
            .Include(s => s.Vehicle)
            .Include(s => s.Template)
            .AsNoTracking()
            .Where(s => !s.IsPaused && s.CompanyId == companyId);

        // Même portée que les logs terminés : sinon les entretiens PLANIFIÉS
        // rouvraient la fuite sur tout le parc.
        if (scope is not null)
            schedQuery = schedQuery.Where(s => scope.Contains(s.VehicleId));

        if (request.VehicleId.HasValue)
            schedQuery = schedQuery.Where(s => s.VehicleId == request.VehicleId.Value);

        var schedules = await schedQuery
            .OrderBy(s => s.NextDueDate)
            .ToListAsync(ct);

        result.AddRange(schedules.Select(s => new MaintenanceLogReportDto(
            -s.Id, // id négatif : une ligne "planifiée" ne collisionne jamais avec un log terminé
            s.VehicleId,
            s.Vehicle?.Name ?? $"Véhicule {s.VehicleId}",
            s.Vehicle?.Plate,
            s.TemplateId,
            s.Template?.Name ?? "Général",
            s.Template?.Category,
            s.NextDueDate ?? s.LastDoneDate ?? DateTime.UtcNow,
            s.NextDueKm ?? 0,
            0m, // pas de cout sur un entretien non effectue (l'estime ne doit pas compter comme une depense)
            null,
            null,
            null,
            s.Notes,
            s.Status // statut réel : upcoming / due / overdue / critical / ok
        )));

        return result;
    }
}

public class GetCurrentVehicleMileageQueryHandler : IRequestHandler<GetCurrentVehicleMileageQuery, VehicleMileageDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly GisAPI.Application.Services.IMaintenanceSchedulerService _scheduler;

    public GetCurrentVehicleMileageQueryHandler(IGisDbContext context, ICurrentTenantService tenant, GisAPI.Application.Services.IMaintenanceSchedulerService scheduler)
    {
        _context = context;
        _tenant = tenant;
        _scheduler = scheduler;
    }

    public async Task<VehicleMileageDto> Handle(GetCurrentVehicleMileageQuery request, CancellationToken cancellationToken)
    {
        var companyId = _tenant.CompanyId ?? 0;

        // Fuite constatée : le kilométrage était renvoyé pour n'importe quel
        // identifiant de véhicule passé dans l'URL, y compris ceux non affectés à
        // l'utilisateur (scope == null => administrateur de société : voit tout).
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenant, cancellationToken);

        var vehicleQuery = _context.Vehicles
            .Include(v => v.GpsDevice)
            .Where(v => v.Id == request.VehicleId && v.CompanyId == companyId);

        if (scope is not null)
            vehicleQuery = vehicleQuery.Where(v => scope.Contains(v.Id));

        var vehicle = await vehicleQuery.FirstOrDefaultAsync(cancellationToken);

        if (vehicle == null)
            return new VehicleMileageDto(request.VehicleId, 0, "unknown", null);

        var currentKm = await _scheduler.GetCurrentMileageAsync(request.VehicleId, cancellationToken);
        
        DateTime? lastGpsUpdate = null;
        string source = "manual";

        if (vehicle.GpsDeviceId.HasValue)
        {
            var lastPosition = await _context.GpsPositions
                .Where(p => p.DeviceId == vehicle.GpsDeviceId.Value && p.OdometerKm.HasValue && p.OdometerKm > 0 && p.OdometerKm != 1048574)
                .OrderByDescending(p => p.RecordedAt)
                .FirstOrDefaultAsync(cancellationToken);

            var isFirmwareL = vehicle.GpsDevice != null
                && !string.IsNullOrEmpty(vehicle.GpsDevice.FirmwareVersion)
                && vehicle.GpsDevice.FirmwareVersion.StartsWith("L", StringComparison.OrdinalIgnoreCase);

            if (lastPosition != null && (isFirmwareL || lastPosition.OdometerKm > vehicle.Mileage))
            {
                source = "gps";
                lastGpsUpdate = lastPosition.RecordedAt;
            }
        }

        return new VehicleMileageDto(request.VehicleId, currentKm, source, lastGpsUpdate);
    }
}



