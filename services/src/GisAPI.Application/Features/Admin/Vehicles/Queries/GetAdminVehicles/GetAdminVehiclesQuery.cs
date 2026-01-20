using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Vehicles.Queries.GetAdminVehicles;

public record GetAdminVehiclesQuery(string? Search, int? CompanyId, string? Status) : IRequest<List<AdminVehicleDto>>;

public class GetAdminVehiclesQueryHandler : IRequestHandler<GetAdminVehiclesQuery, List<AdminVehicleDto>>
{
    private readonly IGisDbContext _context;

    public GetAdminVehiclesQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<List<AdminVehicleDto>> Handle(GetAdminVehiclesQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Vehicles
            .Include(v => v.Societe)
            .Include(v => v.GpsDevice)
            .Include(v => v.AssignedDriver)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrEmpty(request.Search))
        {
            query = query.Where(v =>
                v.Name.Contains(request.Search) ||
                (v.Plate != null && v.Plate.Contains(request.Search)));
        }

        if (request.CompanyId.HasValue)
        {
            query = query.Where(v => v.CompanyId == request.CompanyId.Value);
        }

        if (!string.IsNullOrEmpty(request.Status) && request.Status != "all")
        {
            query = query.Where(v => v.Status == request.Status);
        }

        var vehicles = await query
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(cancellationToken);

        return vehicles.Select(v => new AdminVehicleDto
        {
            Id = v.Id,
            Name = v.Name,
            Type = v.Type,
            Brand = v.Brand,
            Model = v.Model,
            Plate = v.Plate,
            Year = v.Year,
            Color = v.Color,
            Status = v.Status,
            HasGps = v.HasGps,
            Mileage = v.Mileage,
            CompanyId = v.CompanyId,
            CompanyName = v.Societe?.Name,
            GpsDeviceId = v.GpsDeviceId,
            GpsImei = v.GpsDevice?.DeviceUid,
            GpsMat = v.GpsDevice?.Mat,
            GpsModel = v.GpsDevice?.Model,
            GpsFirmwareVersion = v.GpsDevice?.FirmwareVersion,
            AssignedDriverId = v.AssignedDriverId,
            AssignedDriverName = v.AssignedDriver?.Name,
            CreatedAt = v.CreatedAt,
            UpdatedAt = v.UpdatedAt
        }).ToList();
    }
}
