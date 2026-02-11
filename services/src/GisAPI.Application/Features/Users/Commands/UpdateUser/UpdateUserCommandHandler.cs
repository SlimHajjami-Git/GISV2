using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Users.Commands.UpdateUser;

public class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public UpdateUserCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task Handle(UpdateUserCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");
        var currentUserId = _tenantService.UserId
            ?? throw new DomainException("Utilisateur non identifié");

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == request.Id && u.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Utilisateur", request.Id);

        // Validate role if provided
        if (request.RoleId.HasValue)
        {
            var role = await _context.Roles
                .FirstOrDefaultAsync(r => r.Id == request.RoleId.Value && r.SocieteId == companyId, ct)
                ?? throw new DomainException("Rôle invalide");
            user.RoleId = request.RoleId.Value;
        }

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.Email = request.Email;
        user.Phone = request.Phone;
        if (!string.IsNullOrEmpty(request.Status))
            user.Status = request.Status;
        user.UpdatedAt = DateTime.UtcNow;

        // Update vehicle assignments if provided
        if (request.AssignedVehicleIds != null)
        {
            // Validate vehicles belong to company
            if (request.AssignedVehicleIds.Length > 0)
            {
                var validCount = await _context.Vehicles
                    .CountAsync(v => request.AssignedVehicleIds.Contains(v.Id) && v.CompanyId == companyId, ct);
                if (validCount != request.AssignedVehicleIds.Length)
                    throw new DomainException("Un ou plusieurs véhicules sont invalides");
            }

            // Remove old assignments
            var oldAssignments = await _context.UserVehicles
                .Where(uv => uv.UserId == request.Id)
                .ToListAsync(ct);
            _context.UserVehicles.RemoveRange(oldAssignments);

            // Add new assignments
            foreach (var vehicleId in request.AssignedVehicleIds)
            {
                _context.UserVehicles.Add(new UserVehicle
                {
                    UserId = request.Id,
                    VehicleId = vehicleId,
                    AssignedAt = DateTime.UtcNow,
                    AssignedById = currentUserId
                });
            }
        }

        await _context.SaveChangesAsync(ct);
    }
}
