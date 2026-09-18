using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Employees.Commands.CreateEmployee;
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
        // users.id n'est pas un id de chauffeur (vehicles.assigned_driver_id → drivers) :
        // voir CreateEmployeeCommandHandler. Refus avant toute modification.
        if (request.AssignVehicleId.HasValue)
            throw new DomainException(CreateEmployeeCommandHandler.AffectationVehiculeRefusee);

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

        // vehicles.assigned_driver_id n'est pas touché (clé vers drivers, pas users) :
        // la désaffectation retirait le chauffeur dont l'id coïncidait avec celui de l'employé.
        await _context.SaveChangesAsync(ct);
    }
}
