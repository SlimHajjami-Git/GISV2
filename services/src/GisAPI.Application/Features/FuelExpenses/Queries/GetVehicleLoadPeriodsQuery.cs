using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FuelExpenses.Queries;

public record GetVehicleLoadPeriodsQuery(int VehicleId) : IRequest<List<VehicleLoadPeriodDto>>;

public class GetVehicleLoadPeriodsQueryHandler
    : IRequestHandler<GetVehicleLoadPeriodsQuery, List<VehicleLoadPeriodDto>>
{
    private readonly IGisDbContext _context;

    public GetVehicleLoadPeriodsQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<List<VehicleLoadPeriodDto>> Handle(GetVehicleLoadPeriodsQuery request, CancellationToken ct)
    {
        // Filtre tenant global sur VehicleLoadPeriods : déjà scopé société.
        return await _context.VehicleLoadPeriods
            .AsNoTracking()
            .Where(lp => lp.VehicleId == request.VehicleId)
            .OrderByDescending(lp => lp.StartTime)
            .Select(lp => new VehicleLoadPeriodDto(
                lp.Id, lp.VehicleId, lp.StartTime, lp.EndTime, lp.TonnageT, lp.Notes))
            .ToListAsync(ct);
    }
}
