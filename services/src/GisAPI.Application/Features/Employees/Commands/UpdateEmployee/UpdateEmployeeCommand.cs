using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Employees.Commands.UpdateEmployee;

public record UpdateEmployeeCommand(
    int Id,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string EmployeeRole,
    string? Status,
    string? PermitNumber,
    string? PermitType,
    DateTime? PermitExpiry,
    string? CIN,
    DateTime? DateOfBirth,
    DateTime? HireDate,
    int? AssignVehicleId
) : IRequest;

public class UpdateEmployeeCommandHandler : IRequestHandler<UpdateEmployeeCommand>
{
    private readonly IGisDbContext _context;

    public UpdateEmployeeCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task Handle(UpdateEmployeeCommand request, CancellationToken ct)
    {
        var user = await _context.Users.FindAsync(new object[] { request.Id }, ct);
        if (user == null)
            throw new DomainException("Employé introuvable");

        // Check email uniqueness (exclude self)
        var emailExists = await _context.Users
            .AnyAsync(u => u.Email.ToLower() == request.Email.ToLower() && u.Id != request.Id, ct);
        if (emailExists)
            throw new DomainException("Un utilisateur avec cet email existe déjà");

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.Email = request.Email;
        user.Phone = request.Phone;
        user.EmployeeRole = request.EmployeeRole;
        user.PermitNumber = request.PermitNumber;
        user.PermitType = request.PermitType;
        user.PermitExpiry = request.PermitExpiry.HasValue ? DateTime.SpecifyKind(request.PermitExpiry.Value, DateTimeKind.Utc) : null;
        user.CIN = request.CIN;
        user.DateOfBirth = request.DateOfBirth.HasValue ? DateTime.SpecifyKind(request.DateOfBirth.Value, DateTimeKind.Utc) : null;
        user.HireDate = request.HireDate.HasValue ? DateTime.SpecifyKind(request.HireDate.Value, DateTimeKind.Utc) : null;

        if (!string.IsNullOrWhiteSpace(request.Status))
            user.Status = request.Status;

        // Handle vehicle assignment
        if (request.AssignVehicleId.HasValue)
        {
            // Unassign from current vehicle
            var currentVehicle = await _context.Vehicles
                .FirstOrDefaultAsync(v => v.AssignedDriverId == user.Id, ct);
            if (currentVehicle != null && currentVehicle.Id != request.AssignVehicleId.Value)
                currentVehicle.AssignedDriverId = null;

            // Assign to new vehicle
            var newVehicle = await _context.Vehicles.FindAsync(new object[] { request.AssignVehicleId.Value }, ct);
            if (newVehicle != null)
                newVehicle.AssignedDriverId = user.Id;
        }
        else
        {
            // Unassign from any vehicle
            var currentVehicle = await _context.Vehicles
                .FirstOrDefaultAsync(v => v.AssignedDriverId == user.Id, ct);
            if (currentVehicle != null)
                currentVehicle.AssignedDriverId = null;
        }

        await _context.SaveChangesAsync(ct);
    }
}
