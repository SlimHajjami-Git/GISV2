using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Employees.Queries.GetEmployees;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Employees.Commands.CreateEmployee;

public record CreateEmployeeCommand(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string EmployeeRole,
    string? PermitNumber,
    string? PermitType,
    DateTime? PermitExpiry,
    string? CIN,
    DateTime? DateOfBirth,
    DateTime? HireDate,
    int? AssignVehicleId
) : IRequest<EmployeeDto>;

public class CreateEmployeeCommandHandler : IRequestHandler<CreateEmployeeCommand, EmployeeDto>
{
    private readonly IGisDbContext _context;
    private readonly IPasswordHasher _passwordHasher;

    public CreateEmployeeCommandHandler(IGisDbContext context, IPasswordHasher passwordHasher)
    {
        _context = context;
        _passwordHasher = passwordHasher;
    }

    public async Task<EmployeeDto> Handle(CreateEmployeeCommand request, CancellationToken ct)
    {
        // Check email uniqueness
        var exists = await _context.Users.AnyAsync(u => u.Email.ToLower() == request.Email.ToLower(), ct);
        if (exists)
            throw new DomainException("Un utilisateur avec cet email existe déjà");

        // Get default employee role (non-admin)
        var employeeRoleEntity = await _context.Roles
            .FirstOrDefaultAsync(r => r.IsCompanyAdmin == false && r.IsSystemRole == false, ct);

        if (employeeRoleEntity == null)
            throw new DomainException("Aucun rôle employé trouvé. Veuillez créer un rôle non-admin d'abord.");

        var user = new User
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Phone = request.Phone,
            PasswordHash = _passwordHasher.HashPassword("Changeme@2026"),
            EmployeeRole = request.EmployeeRole,
            PermitNumber = request.PermitNumber,
            PermitType = request.PermitType,
            PermitExpiry = request.PermitExpiry.HasValue ? DateTime.SpecifyKind(request.PermitExpiry.Value, DateTimeKind.Utc) : null,
            CIN = request.CIN,
            DateOfBirth = request.DateOfBirth.HasValue ? DateTime.SpecifyKind(request.DateOfBirth.Value, DateTimeKind.Utc) : null,
            HireDate = request.HireDate.HasValue ? DateTime.SpecifyKind(request.HireDate.Value, DateTimeKind.Utc) : null,
            RoleId = employeeRoleEntity.Id,
            Status = "active"
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync(ct);

        // Assign to vehicle if requested
        string? vehicleName = null;
        string? vehiclePlate = null;
        if (request.AssignVehicleId.HasValue)
        {
            var vehicle = await _context.Vehicles.FindAsync(new object[] { request.AssignVehicleId.Value }, ct);
            if (vehicle != null)
            {
                vehicle.AssignedDriverId = user.Id;
                vehicleName = vehicle.Name;
                vehiclePlate = vehicle.Plate;
                await _context.SaveChangesAsync(ct);
            }
        }

        return new EmployeeDto(
            user.Id,
            user.FirstName,
            user.LastName,
            user.FullName,
            user.Email,
            user.Phone,
            user.EmployeeRole,
            user.Status,
            user.PermitNumber,
            user.PermitType,
            user.PermitExpiry,
            user.CIN,
            user.DateOfBirth,
            user.HireDate,
            request.AssignVehicleId,
            vehicleName,
            vehiclePlate,
            null
        );
    }
}
