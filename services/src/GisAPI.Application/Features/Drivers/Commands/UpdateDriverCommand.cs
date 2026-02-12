using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Drivers.Commands;

public record UpdateDriverCommand(
    int Id,
    string? PermitNumber,
    string? PermitType,
    DateTime? PermitExpiry,
    string? CIN,
    DateTime? DateOfBirth,
    DateTime? HireDate,
    int? AssignedVehicleId,
    string? Status
) : IRequest;

public class UpdateDriverCommandHandler : IRequestHandler<UpdateDriverCommand>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public UpdateDriverCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task Handle(UpdateDriverCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");

        var driver = await _context.Drivers
            .FirstOrDefaultAsync(d => d.Id == request.Id && d.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Chauffeur", request.Id);

        if (request.AssignedVehicleId.HasValue)
        {
            var vehicleExists = await _context.Vehicles
                .AnyAsync(v => v.Id == request.AssignedVehicleId.Value && v.CompanyId == companyId, ct);
            if (!vehicleExists)
                throw new DomainException("Véhicule invalide");
        }

        driver.PermitNumber = request.PermitNumber;
        driver.PermitType = request.PermitType;
        driver.PermitExpiry = request.PermitExpiry;
        driver.CIN = request.CIN;
        driver.DateOfBirth = request.DateOfBirth;
        driver.HireDate = request.HireDate;
        driver.AssignedVehicleId = request.AssignedVehicleId;
        if (!string.IsNullOrEmpty(request.Status))
            driver.Status = request.Status;
        driver.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
    }
}
