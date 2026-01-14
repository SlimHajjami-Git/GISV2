using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Models;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Vehicles.Queries.GetVehicles;

public class GetVehiclesQueryHandler : IRequestHandler<GetVehiclesQuery, PaginatedList<VehicleDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetVehiclesQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<PaginatedList<VehicleDto>> Handle(GetVehiclesQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? 0;

        var query = _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId)
            .Include(v => v.AssignedDriver)
            .Include(v => v.AssignedSupervisor)
            .Include(v => v.GpsDevice)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.ToLower();
            query = query.Where(v =>
                v.Name.ToLower().Contains(term) ||
                (v.Plate != null && v.Plate.ToLower().Contains(term)) ||
                (v.Brand != null && v.Brand.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            query = query.Where(v => v.Status == request.Status);
        }

        var projectedQuery = query
            .OrderBy(v => v.Name)
            .Select(v => new VehicleDto(
                v.Id,
                v.Name,
                v.Type,
                v.Brand,
                v.Model,
                v.Plate,
                v.Year,
                v.Color,
                v.Status,
                v.HasGps,
                v.Mileage,
                v.AssignedDriverId,
                v.AssignedDriver != null ? v.AssignedDriver.Name : null,
                v.AssignedSupervisorId,
                v.AssignedSupervisor != null ? v.AssignedSupervisor.Name : null,
                v.GpsDevice != null ? new GpsDeviceDto(
                    v.GpsDevice.Id,
                    v.GpsDevice.DeviceUid,
                    v.GpsDevice.Label,
                    v.GpsDevice.Status,
                    v.GpsDevice.LastCommunication,
                    v.GpsDevice.BatteryLevel,
                    v.GpsDevice.SignalStrength,
                    v.GpsDevice.Model,
                    v.GpsDevice.FirmwareVersion
                ) : null,
                v.CreatedAt
            ));

        return await projectedQuery.ToPaginatedListAsync(request.Page, request.PageSize, ct);
    }
}
