using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FuelRecords.Queries.GetFuelRecords;

public class GetFuelRecordsQueryHandler : IRequestHandler<GetFuelRecordsQuery, FuelRecordsResultDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetFuelRecordsQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<FuelRecordsResultDto> Handle(GetFuelRecordsQuery request, CancellationToken ct)
    {
        var query = _context.FuelRecords
            .AsNoTracking()
            .Include(f => f.Vehicle)
            .Include(f => f.Driver)
            .AsQueryable();

        // Apply filters
        if (request.VehicleId.HasValue)
        {
            query = query.Where(f => f.VehicleId == request.VehicleId.Value);
        }

        if (request.StartDate.HasValue)
        {
            query = query.Where(f => f.RecordedAt >= request.StartDate.Value);
        }

        if (request.EndDate.HasValue)
        {
            query = query.Where(f => f.RecordedAt <= request.EndDate.Value);
        }

        if (!string.IsNullOrEmpty(request.EventType))
        {
            query = query.Where(f => f.EventType == request.EventType);
        }

        if (request.AnomaliesOnly == true)
        {
            query = query.Where(f => f.IsAnomaly);
        }

        // Get total count
        var totalCount = await query.CountAsync(ct);

        // Calculate summary
        var summary = await CalculateSummaryAsync(query, ct);

        // Apply pagination and ordering
        var records = await query
            .OrderByDescending(f => f.RecordedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(f => new FuelRecordDto(
                f.Id,
                f.VehicleId,
                f.Vehicle != null ? f.Vehicle.Name : null,
                f.Vehicle != null ? f.Vehicle.Plate : null,
                f.DriverId,
                f.Driver != null ? f.Driver.FullName : null,
                f.RecordedAt,
                f.FuelPercent,
                f.FuelLiters,
                f.FuelChange,
                f.EventType,
                f.OdometerKm,
                f.SpeedKph,
                f.Rpm,
                f.IgnitionOn,
                f.Latitude,
                f.Longitude,
                f.IsAnomaly,
                f.AnomalyReason,
                f.RefuelAmount,
                f.RefuelCost,
                f.RefuelStation
            ))
            .ToListAsync(ct);

        return new FuelRecordsResultDto(records, totalCount, request.Page, request.PageSize, summary);
    }

    private async Task<FuelSummaryDto> CalculateSummaryAsync(IQueryable<FuelRecord> query, CancellationToken ct)
    {
        var stats = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalRecords = g.Count(),
                RefuelCount = g.Count(f => f.EventType == FuelEventTypes.Refuel),
                AnomalyCount = g.Count(f => f.IsAnomaly),
                TotalRefuelLiters = g.Where(f => f.EventType == FuelEventTypes.Refuel)
                    .Sum(f => f.RefuelAmount),
                TotalRefuelCost = g.Where(f => f.EventType == FuelEventTypes.Refuel)
                    .Sum(f => f.RefuelCost),
                AvgConsumption = g.Where(f => f.ConsumptionRateLPer100Km.HasValue)
                    .Average(f => (double?)f.ConsumptionRateLPer100Km)
            })
            .FirstOrDefaultAsync(ct);

        if (stats == null)
        {
            return new FuelSummaryDto(0, 0, 0, null, null, null);
        }

        return new FuelSummaryDto(
            stats.TotalRecords,
            stats.RefuelCount,
            stats.AnomalyCount,
            stats.TotalRefuelLiters,
            stats.TotalRefuelCost,
            stats.AvgConsumption
        );
    }
}



