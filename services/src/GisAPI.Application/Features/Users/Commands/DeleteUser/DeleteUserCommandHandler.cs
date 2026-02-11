using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Users.Commands.DeleteUser;

public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public DeleteUserCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task Handle(DeleteUserCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");
        var currentUserId = _tenantService.UserId
            ?? throw new DomainException("Utilisateur non identifié");

        if (request.Id == currentUserId)
            throw new DomainException("Vous ne pouvez pas supprimer votre propre compte");

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == request.Id && u.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Utilisateur", request.Id);

        // Remove vehicle assignments first
        var assignments = await _context.UserVehicles
            .Where(uv => uv.UserId == request.Id)
            .ToListAsync(ct);
        _context.UserVehicles.RemoveRange(assignments);

        _context.Users.Remove(user);
        await _context.SaveChangesAsync(ct);
    }
}
