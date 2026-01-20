using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Roles.Commands.DeleteRole;

public class DeleteRoleCommandHandler : IRequestHandler<DeleteRoleCommand>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public DeleteRoleCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task Handle(DeleteRoleCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId 
            ?? throw new DomainException("Société non identifiée");

        var role = await _context.Roles
            .Include(r => r.Users)
            .FirstOrDefaultAsync(r => r.Id == request.Id && r.SocieteId == companyId, ct)
            ?? throw new NotFoundException("Role", request.Id);

        if (role.IsSystem)
            throw new DomainException("Impossible de supprimer un rôle système");

        if (role.Users.Any())
            throw new DomainException($"Impossible de supprimer ce rôle car {role.Users.Count} utilisateur(s) y sont affectés");

        _context.Roles.Remove(role);
        await _context.SaveChangesAsync(ct);
    }
}
