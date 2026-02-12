using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Users.Commands.CreateUser;

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, UserListDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IPasswordHasher _passwordHasher;

    public CreateUserCommandHandler(
        IGisDbContext context,
        ICurrentTenantService tenantService,
        IPasswordHasher passwordHasher)
    {
        _context = context;
        _tenantService = tenantService;
        _passwordHasher = passwordHasher;
    }

    public async Task<UserListDto> Handle(CreateUserCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");
        var currentUserId = _tenantService.UserId
            ?? throw new DomainException("Utilisateur non identifié");

        // Enforce MaxUsers subscription limit
        var company = await _context.Societes
            .Include(s => s.SubscriptionType)
            .FirstOrDefaultAsync(s => s.Id == companyId, ct);

        if (company?.SubscriptionType != null)
        {
            var currentUsers = await _context.Users.CountAsync(u => u.CompanyId == companyId, ct);
            if (currentUsers >= company.SubscriptionType.MaxUsers)
                throw new DomainException(
                    $"Limite d'utilisateurs atteinte ({company.SubscriptionType.MaxUsers} max pour votre abonnement)");
        }

        // Check email uniqueness
        if (await _context.Users.AnyAsync(u => u.Email == request.Email, ct))
            throw new ConflictException("Cet email est déjà utilisé");

        // Validate role exists and belongs to company
        var role = await _context.Roles
            .FirstOrDefaultAsync(r => r.Id == request.RoleId && r.SocieteId == companyId, ct)
            ?? throw new DomainException("Rôle invalide");

        // Validate vehicle assignments
        if (request.AssignedVehicleIds is { Length: > 0 })
        {
            var validCount = await _context.Vehicles
                .CountAsync(v => request.AssignedVehicleIds.Contains(v.Id) && v.CompanyId == companyId, ct);
            if (validCount != request.AssignedVehicleIds.Length)
                throw new DomainException("Un ou plusieurs véhicules sont invalides");
        }

        var user = new User
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Phone = request.Phone,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            RoleId = request.RoleId,
            CompanyId = companyId,
            Status = "active",
            EmployeeRole = request.EmployeeRole,
            PermitNumber = request.PermitNumber,
            PermitType = request.PermitType,
            PermitExpiry = request.PermitExpiry,
            CIN = request.CIN,
            DateOfBirth = request.DateOfBirth,
            HireDate = request.HireDate
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
                    AssignedAt = DateTime.UtcNow,
                    AssignedById = currentUserId
                });
            }
            await _context.SaveChangesAsync(ct);
        }

        return new UserListDto(
            user.Id,
            user.FullName,
            user.Email,
            user.Phone,
            user.RoleId,
            role.Name,
            role.IsCompanyAdmin,
            user.Status,
            user.CreatedAt,
            user.LastLoginAt,
            request.AssignedVehicleIds
        );
    }
}
