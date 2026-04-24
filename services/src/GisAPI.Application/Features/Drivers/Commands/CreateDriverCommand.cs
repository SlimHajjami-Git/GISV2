using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Drivers.Commands;

/// <summary>
/// Creates a driver record. Drivers are standalone — they don't log in,
/// don't have credentials, and don't consume a user seat.
/// </summary>
public record CreateDriverCommand(
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
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
    private readonly IPublisher _publisher;

    public CreateDriverCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, IPublisher publisher)
    {
        _context = context;
        _tenantService = tenantService;
        _publisher = publisher;
    }

    public async Task<DriverDto> Handle(CreateDriverCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");

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
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Phone = request.Phone,
            PermitNumber = request.PermitNumber,
            PermitType = request.PermitType,
            PermitExpiry = request.PermitExpiry.HasValue ? DateTime.SpecifyKind(request.PermitExpiry.Value, DateTimeKind.Utc) : null,
            CIN = request.CIN,
            DateOfBirth = request.DateOfBirth.HasValue ? DateTime.SpecifyKind(request.DateOfBirth.Value, DateTimeKind.Utc) : null,
            HireDate = request.HireDate.HasValue ? DateTime.SpecifyKind(request.HireDate.Value, DateTimeKind.Utc) : null,
            AssignedVehicleId = request.AssignedVehicleId,
            Status = "active"
        };
        _context.Drivers.Add(driver);
        await _context.SaveChangesAsync(ct);

        // Notify company admins about the new driver
        var actorId = _tenantService.UserId ?? 0;
        var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct);
        if (actor != null)
        {
            await _publisher.Publish(new AdminActionNotificationEvent(
                companyId, actorId, actor.FullName,
                "driver_created", $"{request.FirstName} {request.LastName}", driver.Id, "driver"
            ), ct);
        }

        var vehicle = request.AssignedVehicleId.HasValue
            ? await _context.Vehicles.FindAsync(new object[] { request.AssignedVehicleId.Value }, ct)
            : null;

        return new DriverDto(
            driver.Id,
            driver.FirstName, driver.LastName, driver.Email, driver.Phone,
            driver.PermitNumber, driver.PermitType, driver.PermitExpiry,
            driver.CIN, driver.DateOfBirth, driver.HireDate,
            driver.AssignedVehicleId,
            vehicle?.Name, vehicle?.Plate,
            driver.Status, driver.CreatedAt
        );
    }
}
