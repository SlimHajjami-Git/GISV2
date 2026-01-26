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
            .Include(v => v.GpsDevice)
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
            var vehicleSchedules = schedules.Where(s => s.VehicleId == vehicle.Id).ToList();
            if (vehicleSchedules.Count == 0 && request.Status != null) continue;

            var items = vehicleSchedules.Select(s =>
            {
                var kmUntilDue = s.NextDueKm.HasValue ? s.NextDueKm.Value - vehicle.Mileage : (int?)null;
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
                    daysUntilDue
                );
            }).OrderBy(i => i.Status == "overdue" ? 0 : i.Status == "due" ? 1 : i.Status == "upcoming" ? 2 : 3)
              .ThenBy(i => i.DaysUntilDue ?? int.MaxValue)
              .ToList();

            results.Add(new VehicleMaintenanceStatusDto(
                vehicle.Id,
                vehicle.Name,
                vehicle.Plate,
                vehicle.Mileage,
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
