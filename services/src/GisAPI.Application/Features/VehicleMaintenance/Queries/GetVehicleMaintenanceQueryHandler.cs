using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.VehicleMaintenance.Queries;

public class GetVehicleMaintenanceQueryHandler : IRequestHandler<GetVehicleMaintenanceQuery, PaginatedList<VehicleMaintenanceStatusDto>>
{
    private readonly IGisDbContext _context;

    public GetVehicleMaintenanceQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<PaginatedList<VehicleMaintenanceStatusDto>> Handle(GetVehicleMaintenanceQuery request, CancellationToken cancellationToken)
    {
        var vehiclesQuery = _context.Vehicles.AsQueryable();

        if (request.VehicleId.HasValue)
            vehiclesQuery = vehiclesQuery.Where(v => v.Id == request.VehicleId.Value);

        var vehicles = await vehiclesQuery
            .ToListAsync(cancellationToken);

        var schedules = await _context.VehicleMaintenanceSchedules
            .Include(s => s.Template)
            .Where(s => vehicles.Select(v => v.Id).Contains(s.VehicleId))
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.Status))
            schedules = schedules.Where(s => s.Status == request.Status).ToList();

        var today = DateTime.UtcNow.Date;
        var results = new List<VehicleMaintenanceStatusDto>();

        foreach (var vehicle in vehicles)
        {
            // Calypso 7 — vehicles.mileage est l'unique source. Le Rust ingest
            // la tient à jour pour tous les types de tracker :
            //   - NEMS L (CAN bus actif)  : mirror de l'odomètre du véhicule
            //   - NEMS L (CAN bus muet)   : Haversine fallback (db.rs:199)
            //   - NEMS S / Noron          : Haversine systématique
            // Voir MaintenanceSchedulerService.GetCurrentMileageAsync pour
            // la justification complète de cette simplification.
            int currentMileage = vehicle.Mileage;

            var vehicleSchedules = schedules.Where(s => s.VehicleId == vehicle.Id).ToList();
            if (vehicleSchedules.Count == 0 && request.Status != null) continue;

            var items = vehicleSchedules.Select(s =>
            {
                var kmUntilDue = s.NextDueKm.HasValue ? s.NextDueKm.Value - currentMileage : (int?)null;
                var daysUntilDue = s.NextDueDate.HasValue ? (int)(s.NextDueDate.Value - today).TotalDays : (int?)null;

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
                    daysUntilDue,
                    s.FreeUsesTotal,
                    s.FreeUsesRemaining,
                    s.FreeSource,
                    s.FreeExpiryDate,
                    s.FreeNotes
                );
            }).OrderBy(i => i.Status == "overdue" ? 0 : i.Status == "due" ? 1 : i.Status == "upcoming" ? 2 : 3)
              .ThenBy(i => i.DaysUntilDue ?? int.MaxValue)
              .ToList();

            results.Add(new VehicleMaintenanceStatusDto(
                vehicle.Id,
                vehicle.Name,
                vehicle.Plate,
                currentMileage,
                items
            ));
        }

        var totalCount = results.Count;
        var paginatedItems = results
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return new PaginatedList<VehicleMaintenanceStatusDto>(paginatedItems, totalCount, request.Page, request.PageSize);
    }
}



