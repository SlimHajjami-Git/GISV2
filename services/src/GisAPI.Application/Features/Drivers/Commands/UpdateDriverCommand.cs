using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Drivers.Commands;

public record UpdateDriverCommand(
    int Id,
    // User fields
    string FirstName,
    string LastName,
    string? Phone,
    // Driver fields
    string? PermitNumber,
    string? PermitType,
    DateTime? PermitExpiry,
    string? CIN,
    DateTime? DateOfBirth,
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
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.Id == request.Id && d.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Chauffeur", request.Id);

        if (request.AssignedVehicleId.HasValue)
        {
            var vehicleExists = await _context.Vehicles
                .AnyAsync(v => v.Id == request.AssignedVehicleId.Value && v.CompanyId == companyId, ct);
            if (!vehicleExists)
                throw new DomainException("Véhicule invalide");
        }

        // Update user info
        driver.User.FirstName = request.FirstName;
        driver.User.LastName = request.LastName;
        driver.User.Phone = request.Phone;
        driver.User.UpdatedAt = DateTime.UtcNow;

        // Update driver info
        driver.PermitNumber = request.PermitNumber;
        driver.PermitType = request.PermitType;
        driver.PermitExpiry = request.PermitExpiry.HasValue ? DateTime.SpecifyKind(request.PermitExpiry.Value, DateTimeKind.Utc) : null;
        driver.CIN = request.CIN;
        driver.DateOfBirth = request.DateOfBirth.HasValue ? DateTime.SpecifyKind(request.DateOfBirth.Value, DateTimeKind.Utc) : null;
        driver.AssignedVehicleId = request.AssignedVehicleId;
        if (!string.IsNullOrEmpty(request.Status))
            driver.Status = request.Status;
        driver.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
    }
}
