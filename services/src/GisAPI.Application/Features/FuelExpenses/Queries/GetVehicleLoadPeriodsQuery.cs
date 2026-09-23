using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FuelExpenses.Queries;

public record GetVehicleLoadPeriodsQuery(int VehicleId) : IRequest<List<VehicleLoadPeriodDto>>;

public class GetVehicleLoadPeriodsQueryHandler
    : IRequestHandler<GetVehicleLoadPeriodsQuery, List<VehicleLoadPeriodDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetVehicleLoadPeriodsQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<VehicleLoadPeriodDto>> Handle(GetVehicleLoadPeriodsQuery request, CancellationToken ct)
    {
        // Le filtre tenant global scope la SOCIÉTÉ, pas le locataire : chez un loueur
        // il ne cloisonne rien. « /api/consumption-analysis » n'exige par ailleurs
        // AUCUNE case de module — la lecture du tonnage déclaré d'un véhicule loué à
        // un autre client était ouverte à tout compte connecté.
        if (!await VehicleScope.CanAccessVehicleAsync(_context, _tenantService, request.VehicleId, ct))
            return new List<VehicleLoadPeriodDto>();

        return await _context.VehicleLoadPeriods
            .AsNoTracking()
            .Where(lp => lp.VehicleId == request.VehicleId)
            .OrderByDescending(lp => lp.StartTime)
            .Select(lp => new VehicleLoadPeriodDto(
                lp.Id, lp.VehicleId, lp.StartTime, lp.EndTime, lp.TonnageT, lp.Notes))
            .ToListAsync(ct);
    }
}
