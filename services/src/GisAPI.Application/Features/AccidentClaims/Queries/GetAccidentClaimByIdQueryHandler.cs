using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GisAPI.Application.Features.AccidentClaims.Queries;

public class GetAccidentClaimByIdQueryHandler : IRequestHandler<GetAccidentClaimByIdQuery, AccidentClaimDto?>
{
    private readonly IGisDbContext _context;

    public GetAccidentClaimByIdQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<AccidentClaimDto?> Handle(GetAccidentClaimByIdQuery request, CancellationToken cancellationToken)
    {
        var claim = await _context.AccidentClaims
            .Include(c => c.Vehicle)
            .Include(c => c.Driver)
            .Include(c => c.ThirdParties)
            .Include(c => c.Documents)
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

        if (claim == null) return null;

        return new AccidentClaimDto(
            claim.Id,
            claim.ClaimNumber,
            claim.VehicleId,
            claim.Vehicle?.Name ?? "",
            claim.Vehicle?.Plate,
            claim.DriverId,
            claim.Driver?.Name,
            claim.AccidentDate,
            claim.AccidentTime.ToString(@"hh\:mm"),
            claim.Location,
            claim.Latitude,
            claim.Longitude,
            claim.Description,
            claim.Severity,
            claim.EstimatedDamage,
            claim.ApprovedAmount,
            claim.Status,
            claim.ThirdPartyInvolved,
            claim.PoliceReportNumber,
            claim.MileageAtAccident,
            string.IsNullOrEmpty(claim.DamagedZones) ? null : JsonSerializer.Deserialize<string[]>(claim.DamagedZones),
            claim.CreatedAt,
            claim.UpdatedAt,
            claim.ThirdParties.Select(tp => new AccidentClaimThirdPartyDto(
                tp.Id, tp.Name, tp.Phone, tp.VehiclePlate, tp.VehicleModel, tp.InsuranceCompany, tp.InsuranceNumber
            )).ToList(),
            claim.Documents.Select(d => new AccidentClaimDocumentDto(
                d.Id, d.DocumentType, d.FileName, d.FileUrl, d.FileSize, d.MimeType, d.UploadedAt
            )).ToList()
        );
    }
}
