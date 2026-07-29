using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Models;
using GisAPI.Application.Common.Security;
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
        var companyId = _tenantService.CompanyId ?? throw new UnauthorizedAccessException("Company ID not found");

        // Portee vehicules : chaque plein expose le montant, la station, le numero de
        // facture et le NOM DU CHAUFFEUR. Fuite constatee : un employe restreint a ses
        // vehicules voyait les pleins de tout le parc. Seul l'admin societe voit tout.
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, cancellationToken);

        var query = _context.FuelEntries
            .Include(e => e.FuelType)
            .Include(e => e.Driver)
            .Where(e => e.CompanyId == companyId)
            .AsQueryable();

        if (scope is not null)
        {
            // Les saisies non rattachees a un vehicule (VehicleId null) ne restent visibles
            // que si leur plaque correspond a un vehicule autorise — meme regle de
            // rattachement que les rapports carburant (audit / comparatif).
            var scopedPlates = await _context.Vehicles
                .AsNoTracking()
                .Where(v => v.CompanyId == companyId
                            && scope.Contains(v.Id)
                            && v.Plate != null && v.Plate != "")
                .Select(v => v.Plate!)
                .ToListAsync(cancellationToken);

            query = query.Where(e =>
                (e.VehicleId != null && scope.Contains(e.VehicleId.Value))
                || (e.VehicleId == null && scopedPlates.Contains(e.VehiclePlate)));
        }

        if (request.FuelTypeId.HasValue)
            query = query.Where(e => e.FuelTypeId == request.FuelTypeId.Value);

        if (!string.IsNullOrEmpty(request.VehiclePlate))
            query = query.Where(e => e.VehiclePlate.Contains(request.VehiclePlate));

        if (request.StartDate.HasValue)
        {
            var startDate = DateTime.SpecifyKind(request.StartDate.Value, DateTimeKind.Utc);
            query = query.Where(e => e.InvoiceDate >= startDate);
        }

        if (request.EndDate.HasValue)
        {
            var endDate = DateTime.SpecifyKind(request.EndDate.Value.AddDays(1), DateTimeKind.Utc);
            query = query.Where(e => e.InvoiceDate < endDate);
        }

        query = query.OrderByDescending(e => e.InvoiceDate).ThenByDescending(e => e.CreatedAt);

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
