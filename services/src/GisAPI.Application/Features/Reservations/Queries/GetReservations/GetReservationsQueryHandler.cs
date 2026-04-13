using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Reservations.Queries.GetReservations;

public class GetReservationsQueryHandler : IRequestHandler<GetReservationsQuery, List<ReservationDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetReservationsQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<ReservationDto>> Handle(GetReservationsQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");

        var query = _context.Reservations
            .Include(r => r.Vehicle)
            .Include(r => r.RequestedByUser)
            .Include(r => r.AssignedDriver)
            .Where(r => r.CompanyId == companyId)
            .AsQueryable();

        if (request.VehicleId.HasValue)
            query = query.Where(r => r.VehicleId == request.VehicleId.Value);

        if (request.DriverId.HasValue)
            query = query.Where(r => r.AssignedDriverId == request.DriverId.Value || r.RequestedByUserId == request.DriverId.Value);

        if (!string.IsNullOrEmpty(request.Status))
            query = query.Where(r => r.Status == request.Status);

        if (request.From.HasValue)
            query = query.Where(r => r.StartDateTime >= request.From.Value);

        if (request.To.HasValue)
            query = query.Where(r => r.EndDateTime <= request.To.Value);

        var reservations = await query
            .OrderByDescending(r => r.StartDateTime)
            .Take(200)
            .ToListAsync(ct);

        return reservations.Select(r => new ReservationDto(
            r.Id,
            r.VehicleId,
            r.Vehicle?.Name ?? "",
            r.Vehicle?.Plate,
            r.RequestedByUserId,
            r.RequestedByUser?.FullName,
            r.AssignedDriverId,
            r.AssignedDriver?.FullName,
            r.Purpose,
            r.Destination,
            r.StartDateTime,
            r.EndDateTime,
            r.EstimatedKm,
            r.ActualKm,
            r.StartMileage,
            r.EndMileage,
            r.Status,
            r.Notes,
            r.CreatedAt
        )).ToList();
    }
}
