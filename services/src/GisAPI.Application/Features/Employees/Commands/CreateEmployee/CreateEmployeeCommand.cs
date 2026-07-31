using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Employees.Queries.GetEmployees;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
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
    private readonly ICurrentTenantService _tenantService;

    public CreateEmployeeCommandHandler(
        IGisDbContext context,
        IPasswordHasher passwordHasher,
        ICurrentTenantService tenantService)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _tenantService = tenantService;
    }

    public async Task<EmployeeDto> Handle(CreateEmployeeCommand request, CancellationToken ct)
    {
        // Check email uniqueness
        var exists = await _context.Users.AnyAsync(u => u.Email.ToLower() == request.Email.ToLower(), ct);
        if (exists)
            throw new DomainException("Un utilisateur avec cet email existe déjà");

        // Rôle employé de CETTE société.
        //
        // La requête ne portait aucun filtre de société. Or Role n'a pas de
        // filtre global de tenant (il porte SocieteId, pas CompanyId) : on
        // récupérait donc le premier rôle non-admin de n'importe quelle société.
        // Une base PostgreSQL a en plus un déclencheur qui impose au rôle et à
        // l'utilisateur d'appartenir à la même société : la création échouait
        // alors sur une erreur brute. Une société toute neuve, elle, n'avait
        // aucune chance — c'est le premier mur qu'aurait rencontré un inscrit.
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société introuvable pour l'utilisateur courant.");

        var employeeRoleEntity = await _context.Roles
            .FirstOrDefaultAsync(r => r.SocieteId == companyId
                                   && !r.IsCompanyAdmin && !r.IsSystemRole, ct);

        if (employeeRoleEntity == null)
        {
            // Repli : on crée le rôle plutôt que de bloquer l'exploitant sur une
            // consigne qu'il ne comprendrait pas. Même geste que la création
            // d'utilisateur.
            employeeRoleEntity = new Role
            {
                Name = "Employé",
                Description = "Accès limité — créé automatiquement",
                SocieteId = companyId,
                IsCompanyAdmin = false,
                IsSystemRole = false
            };
            _context.Roles.Add(employeeRoleEntity);
            await _context.SaveChangesAsync(ct);
        }

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
