using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Drivers.Commands;

public record CreateDriverCommand(
    // User fields
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string Password,
    // Driver fields
    string? PermitNumber,
    string? PermitType,
    DateTime? PermitExpiry,
    string? CIN,
    DateTime? DateOfBirth,
    int? AssignedVehicleId
) : IRequest<DriverDto>;

public class CreateDriverCommandHandler : IRequestHandler<CreateDriverCommand, DriverDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPublisher _publisher;

    public CreateDriverCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, IPasswordHasher passwordHasher, IPublisher publisher)
    {
        _context = context;
        _tenantService = tenantService;
        _passwordHasher = passwordHasher;
        _publisher = publisher;
    }

    public async Task<DriverDto> Handle(CreateDriverCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");

        // Check email uniqueness
        if (await _context.Users.AnyAsync(u => u.Email == request.Email, ct))
            throw new ConflictException("Un utilisateur avec cet email existe déjà");

        // Find or create a "Chauffeur" role for this company
        var driverRole = await _context.Roles
            .FirstOrDefaultAsync(r => r.SocieteId == companyId && r.Name == "Chauffeur", ct);

        if (driverRole == null)
        {
            driverRole = new Role
            {
                Name = "Chauffeur",
                Description = "Rôle chauffeur (créé automatiquement)",
                SocieteId = companyId,
                IsCompanyAdmin = false,
                IsSystemRole = false,
                Permissions = new Dictionary<string, object>()
            };
            _context.Roles.Add(driverRole);
            await _context.SaveChangesAsync(ct);
        }

        // Validate vehicle if provided
        if (request.AssignedVehicleId.HasValue)
        {
            var vehicleExists = await _context.Vehicles
                .AnyAsync(v => v.Id == request.AssignedVehicleId.Value && v.CompanyId == companyId, ct);
            if (!vehicleExists)
                throw new DomainException("Véhicule invalide");
        }

        // Create User account
        var user = new User
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Phone = request.Phone,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            RoleId = driverRole.Id,
            CompanyId = companyId,
            Status = "active",
            EmployeeRole = "driver"
        };
        _context.Users.Add(user);

        // Create Driver record (single SaveChanges = atomic)
        var driver = new Driver
        {
            CompanyId = companyId,
            User = user,
            PermitNumber = request.PermitNumber,
            PermitType = request.PermitType,
            PermitExpiry = request.PermitExpiry,
            CIN = request.CIN,
            DateOfBirth = request.DateOfBirth,
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
            _ = _publisher.Publish(new AdminActionNotificationEvent(
                companyId, actorId, actor.FullName,
                "driver_created", $"{request.FirstName} {request.LastName}", driver.Id, "driver"
            ), ct);
        }

        var vehicle = request.AssignedVehicleId.HasValue
            ? await _context.Vehicles.FindAsync(new object[] { request.AssignedVehicleId.Value }, ct)
            : null;

        return new DriverDto(
            driver.Id, driver.UserId,
            user.FirstName, user.LastName, user.Email, user.Phone,
            driver.PermitNumber, driver.PermitType, driver.PermitExpiry,
            driver.CIN, driver.DateOfBirth,
            driver.AssignedVehicleId,
            vehicle?.Name, vehicle?.Plate,
            driver.Status, driver.CreatedAt
        );
    }
}
