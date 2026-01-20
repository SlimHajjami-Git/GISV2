using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.VehicleAssignments.Commands.AssignVehicle;

public class AssignVehicleCommandHandler : IRequestHandler<AssignVehicleCommand, VehicleAssignmentDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public AssignVehicleCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<VehicleAssignmentDto> Handle(AssignVehicleCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId 
            ?? throw new DomainException("Société non identifiée");

        // Verify vehicle belongs to company
        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Vehicle", request.VehicleId);

        // Verify user belongs to company
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == request.UserId && u.CompanyId == companyId, ct)
            ?? throw new NotFoundException("User", request.UserId);

        // Check if already assigned
        var existingAssignment = await _context.VehicleAssignments
            .FirstOrDefaultAsync(a => a.VehicleId == request.VehicleId && a.UserId == request.UserId && a.IsActive, ct);

        if (existingAssignment != null)
            throw new ConflictException($"Ce véhicule est déjà attribué à cet utilisateur");

        var assignment = new VehicleAssignment
        {
            VehicleId = request.VehicleId,
            UserId = request.UserId,
            AssignedAt = DateTime.UtcNow,
            IsActive = true,
            AssignedBy = _tenantService.UserEmail,
            Notes = request.Notes
        };

        _context.VehicleAssignments.Add(assignment);
        await _context.SaveChangesAsync(ct);

        return new VehicleAssignmentDto(
            assignment.Id,
            vehicle.Id,
            vehicle.Name,
            user.Id,
            user.Name,
            assignment.AssignedAt,
            assignment.IsActive
        );
    }
}
