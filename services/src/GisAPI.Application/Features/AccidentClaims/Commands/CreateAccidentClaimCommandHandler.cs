using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GisAPI.Application.Features.AccidentClaims.Commands;

public class CreateAccidentClaimCommandHandler : IRequestHandler<CreateAccidentClaimCommand, int>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public CreateAccidentClaimCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<int> Handle(CreateAccidentClaimCommand request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        // Generate claim number: SIN-{YEAR}-{SEQUENCE}
        var year = DateTime.UtcNow.Year;
        var lastClaim = await _context.AccidentClaims
            .Where(c => c.ClaimNumber.StartsWith($"SIN-{year}-"))
            .OrderByDescending(c => c.ClaimNumber)
            .FirstOrDefaultAsync(cancellationToken);

        int sequence = 1;
        if (lastClaim != null)
        {
            var parts = lastClaim.ClaimNumber.Split('-');
            if (parts.Length == 3 && int.TryParse(parts[2], out var lastSeq))
                sequence = lastSeq + 1;
        }
        var claimNumber = $"SIN-{year}-{sequence:D4}";

        // Parse time
        if (!TimeSpan.TryParse(request.AccidentTime, out var accidentTime))
            accidentTime = TimeSpan.Zero;

        var claim = new AccidentClaim
        {
            ClaimNumber = claimNumber,
            CompanyId = companyId,
            VehicleId = request.VehicleId,
            DriverId = request.DriverId,
            AccidentDate = request.AccidentDate,
            AccidentTime = accidentTime,
            Location = request.Location,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Description = request.Description,
            Severity = request.Severity,
            EstimatedDamage = request.EstimatedDamage,
            Status = "draft",
            ThirdPartyInvolved = request.ThirdPartyInvolved,
            PoliceReportNumber = request.PoliceReportNumber,
            MileageAtAccident = request.MileageAtAccident,
            DamagedZones = request.DamagedZones != null ? JsonSerializer.Serialize(request.DamagedZones) : null,
            Witnesses = request.Witnesses,
            AdditionalNotes = request.AdditionalNotes
        };

        _context.AccidentClaims.Add(claim);
        await _context.SaveChangesAsync(cancellationToken);

        // Add third party if involved
        if (request.ThirdPartyInvolved && !string.IsNullOrWhiteSpace(request.ThirdPartyName))
        {
            var thirdParty = new AccidentClaimThirdParty
            {
                ClaimId = claim.Id,
                Name = request.ThirdPartyName,
                Phone = request.ThirdPartyPhone,
                VehiclePlate = request.ThirdPartyVehiclePlate,
                VehicleModel = request.ThirdPartyVehicleModel,
                InsuranceCompany = request.ThirdPartyInsurance,
                InsuranceNumber = request.ThirdPartyInsuranceNumber
            };
            _context.AccidentClaimThirdParties.Add(thirdParty);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return claim.Id;
    }
}
