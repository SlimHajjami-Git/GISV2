using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GisAPI.Application.Features.AccidentClaims.Queries;

public class GetAccidentClaimsQueryHandler : IRequestHandler<GetAccidentClaimsQuery, PaginatedList<AccidentClaimDto>>
{
    private readonly IGisDbContext _context;

    public GetAccidentClaimsQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<PaginatedList<AccidentClaimDto>> Handle(GetAccidentClaimsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.AccidentClaims
            .Include(c => c.Vehicle)
            .Include(c => c.Driver)
            .Include(c => c.ThirdParties)
            .Include(c => c.Documents)
            .AsQueryable();

        // Apply filters
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.ToLower();
            query = query.Where(c =>
                c.ClaimNumber.ToLower().Contains(term) ||
                c.Location.ToLower().Contains(term) ||
                (c.Vehicle != null && c.Vehicle.Name.ToLower().Contains(term)) ||
                (c.Vehicle != null && c.Vehicle.Plate != null && c.Vehicle.Plate.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
            query = query.Where(c => c.Status == request.Status);

        if (!string.IsNullOrWhiteSpace(request.Severity))
            query = query.Where(c => c.Severity == request.Severity);

        if (request.VehicleId.HasValue)
            query = query.Where(c => c.VehicleId == request.VehicleId.Value);

        // Order by date descending
        query = query.OrderByDescending(c => c.AccidentDate).ThenByDescending(c => c.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);

        var claims = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = claims.Select(c => new AccidentClaimDto(
            c.Id,
            c.ClaimNumber,
            c.VehicleId,
            c.Vehicle?.Name ?? "",
            c.Vehicle?.Plate,
            c.DriverId,
            c.Driver?.FullName,
            c.AccidentDate,
            c.AccidentTime.ToString(@"hh\:mm"),
            c.Location,
            c.Latitude,
            c.Longitude,
            c.Description,
            c.Severity,
            c.EstimatedDamage,
            c.ApprovedAmount,
            c.Status,
            c.ThirdPartyInvolved,
            c.PoliceReportNumber,
            c.MileageAtAccident,
            string.IsNullOrEmpty(c.DamagedZones) ? null : JsonSerializer.Deserialize<string[]>(c.DamagedZones),
            c.CreatedAt,
            c.UpdatedAt,
            c.ThirdParties.Select(tp => new AccidentClaimThirdPartyDto(
                tp.Id, tp.Name, tp.Phone, tp.VehiclePlate, tp.VehicleModel, tp.InsuranceCompany, tp.InsuranceNumber
            )).ToList(),
            c.Documents.Select(d => new AccidentClaimDocumentDto(
                d.Id, d.DocumentType, d.FileName, d.FileUrl, d.FileSize, d.MimeType, d.UploadedAt
            )).ToList()
        )).ToList();

        return new PaginatedList<AccidentClaimDto>(items, totalCount, request.Page, request.PageSize);
    }
}



