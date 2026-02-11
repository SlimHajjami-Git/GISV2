using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Users.Commands.CreateAdminUser;

public class CreateAdminUserCommandHandler : IRequestHandler<CreateAdminUserCommand, AdminUserDto>
{
    private readonly IGisDbContext _context;
    private readonly IPasswordHasher _passwordHasher;

    public CreateAdminUserCommandHandler(IGisDbContext context, IPasswordHasher passwordHasher)
    {
        _context = context;
        _passwordHasher = passwordHasher;
    }

    public async Task<AdminUserDto> Handle(CreateAdminUserCommand request, CancellationToken ct)
    {
        if (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == request.Email, ct))
            throw new ConflictException("Un utilisateur avec cet email existe déjà");

        var company = await _context.Societes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == request.CompanyId, ct)
            ?? throw new NotFoundException("Société", request.CompanyId);

        if (!company.IsActive)
            throw new DomainException("Impossible de créer un utilisateur pour une société suspendue");

        var role = await _context.Roles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == request.RoleId && r.SocieteId == request.CompanyId, ct)
            ?? throw new DomainException("Rôle non trouvé ou n'appartient pas à cette société");

        // Validate vehicle assignments
        if (request.AssignedVehicleIds is { Length: > 0 })
        {
            var validCount = await _context.Vehicles.IgnoreQueryFilters()
                .CountAsync(v => request.AssignedVehicleIds.Contains(v.Id) && v.CompanyId == request.CompanyId, ct);
            if (validCount != request.AssignedVehicleIds.Length)
                throw new DomainException("Un ou plusieurs véhicules sont invalides ou n'appartiennent pas à cette société");
        }

        var user = new User
        {
            Name = request.Name,
            Email = request.Email,
            Phone = request.Phone,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            RoleId = request.RoleId,
            CompanyId = request.CompanyId,
            Status = "active"
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync(ct);

        // Save vehicle assignments
        if (request.AssignedVehicleIds is { Length: > 0 })
        {
            foreach (var vehicleId in request.AssignedVehicleIds)
            {
                _context.UserVehicles.Add(new UserVehicle
                {
                    UserId = user.Id,
                    VehicleId = vehicleId,
                    AssignedAt = DateTime.UtcNow
                });
            }
            await _context.SaveChangesAsync(ct);
        }

        return new AdminUserDto
        {
            Id = user.Id,
            Name = user.FullName,
            Email = user.Email,
            Phone = user.Phone,
            CompanyId = user.CompanyId,
            CompanyName = company.Name,
            RoleId = user.RoleId,
            RoleName = role.Name,
            AssignedVehicleIds = request.AssignedVehicleIds ?? [],
            Status = user.Status,
            CreatedAt = user.CreatedAt,
            IsOnline = false
        };
    }
}
