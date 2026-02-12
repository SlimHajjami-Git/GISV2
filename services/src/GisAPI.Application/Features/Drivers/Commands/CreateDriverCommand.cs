using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Drivers.Commands;

public record CreateDriverCommand(
    int UserId,
    string? PermitNumber,
    string? PermitType,
    DateTime? PermitExpiry,
    string? CIN,
    DateTime? DateOfBirth,
    DateTime? HireDate,
    int? AssignedVehicleId
) : IRequest<DriverDto>;

public class CreateDriverCommandHandler : IRequestHandler<CreateDriverCommand, DriverDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public CreateDriverCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<DriverDto> Handle(CreateDriverCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");

        // Validate user exists and belongs to company
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == request.UserId && u.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Utilisateur", request.UserId);

        // Check if driver record already exists for this user
        if (await _context.Drivers.AnyAsync(d => d.UserId == request.UserId, ct))
            throw new ConflictException("Ce utilisateur est déjà enregistré comme chauffeur");

        // Validate vehicle if provided
        if (request.AssignedVehicleId.HasValue)
        {
            var vehicleExists = await _context.Vehicles
                .AnyAsync(v => v.Id == request.AssignedVehicleId.Value && v.CompanyId == companyId, ct);
            if (!vehicleExists)
                throw new DomainException("Véhicule invalide");
        }

        var driver = new Driver
        {
            CompanyId = companyId,
            UserId = request.UserId,
            PermitNumber = request.PermitNumber,
            PermitType = request.PermitType,
            PermitExpiry = request.PermitExpiry,
            CIN = request.CIN,
            DateOfBirth = request.DateOfBirth,
            HireDate = request.HireDate,
            AssignedVehicleId = request.AssignedVehicleId,
            Status = "active"
        };

        // Mark user as driver
        user.EmployeeRole = "driver";

        _context.Drivers.Add(driver);
        await _context.SaveChangesAsync(ct);

        var vehicle = request.AssignedVehicleId.HasValue
            ? await _context.Vehicles.FindAsync(new object[] { request.AssignedVehicleId.Value }, ct)
            : null;

        return new DriverDto(
            driver.Id, driver.UserId,
            user.FirstName, user.LastName, user.Email, user.Phone,
            driver.PermitNumber, driver.PermitType, driver.PermitExpiry,
            driver.CIN, driver.DateOfBirth, driver.HireDate,
            driver.AssignedVehicleId,
            vehicle?.Name, vehicle?.Plate,
            driver.Status, driver.CreatedAt
        );
    }
}
