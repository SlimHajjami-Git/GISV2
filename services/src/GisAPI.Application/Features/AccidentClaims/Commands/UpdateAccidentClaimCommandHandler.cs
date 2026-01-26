using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GisAPI.Application.Features.AccidentClaims.Commands;

public class UpdateAccidentClaimCommandHandler : IRequestHandler<UpdateAccidentClaimCommand, bool>
{
    private readonly IGisDbContext _context;

    public UpdateAccidentClaimCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(UpdateAccidentClaimCommand request, CancellationToken cancellationToken)
    {
        var claim = await _context.AccidentClaims
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

        if (claim == null) return false;

        // Only drafts can be modified
        if (claim.Status != "draft")
            throw new InvalidOperationException("Only draft claims can be modified");

        // Update fields
        if (request.VehicleId.HasValue) claim.VehicleId = request.VehicleId.Value;
        if (request.DriverId.HasValue) claim.DriverId = request.DriverId;
        if (request.AccidentDate.HasValue) claim.AccidentDate = request.AccidentDate.Value;
        if (!string.IsNullOrEmpty(request.AccidentTime) && TimeSpan.TryParse(request.AccidentTime, out var time))
            claim.AccidentTime = time;
        if (request.Location != null) claim.Location = request.Location;
        if (request.Latitude.HasValue) claim.Latitude = request.Latitude;
        if (request.Longitude.HasValue) claim.Longitude = request.Longitude;
        if (request.Description != null) claim.Description = request.Description;
        if (request.Severity != null) claim.Severity = request.Severity;
        if (request.EstimatedDamage.HasValue) claim.EstimatedDamage = request.EstimatedDamage.Value;
        if (request.DamagedZones != null) claim.DamagedZones = JsonSerializer.Serialize(request.DamagedZones);
        if (request.ThirdPartyInvolved.HasValue) claim.ThirdPartyInvolved = request.ThirdPartyInvolved.Value;
        if (request.PoliceReportNumber != null) claim.PoliceReportNumber = request.PoliceReportNumber;
        if (request.MileageAtAccident.HasValue) claim.MileageAtAccident = request.MileageAtAccident;
        if (request.Witnesses != null) claim.Witnesses = request.Witnesses;
        if (request.AdditionalNotes != null) claim.AdditionalNotes = request.AdditionalNotes;

        claim.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
