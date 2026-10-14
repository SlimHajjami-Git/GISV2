using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Models;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FuelEntries.Queries;

public class GetFuelEntriesQueryHandler : IRequestHandler<GetFuelEntriesQuery, PaginatedList<FuelEntryDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetFuelEntriesQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<PaginatedList<FuelEntryDto>> Handle(GetFuelEntriesQuery request, CancellationToken cancellationToken)
    {
        // Périmètre (portée véhicules) et filtres partagés avec les totaux de l'écran Carburant.
        var query = (await FuelEntriesFilter.AppliquerAsync(
                _context, _tenantService, request.FuelTypeId, request.VehiclePlate,
                request.StartDate, request.EndDate, request.VehicleId, cancellationToken))
            .Include(e => e.FuelType)
            .Include(e => e.Driver)
            .OrderByDescending(e => e.InvoiceDate).ThenByDescending(e => e.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(e => new FuelEntryDto(
                e.Id,
                e.VehicleId,
                e.VehiclePlate,
                e.FuelTypeId,
                e.FuelType != null ? e.FuelType.Code : "",
                e.FuelType != null ? e.FuelType.Name : "",
                e.Volume,
                e.PricePerLiter,
                e.TotalAmount,
                e.InvoiceDate,
                e.StationName,
                e.InvoiceNumber,
                e.Notes,
                e.DriverId,
                e.Driver != null ? e.Driver.FirstName + " " + e.Driver.LastName : null,
                e.OdometerKm,
                e.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        return new PaginatedList<FuelEntryDto>(items, totalCount, request.Page, request.PageSize);
    }
}
