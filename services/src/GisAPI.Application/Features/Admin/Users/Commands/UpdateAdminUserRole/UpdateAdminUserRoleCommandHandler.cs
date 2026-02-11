using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Users.Commands.UpdateAdminUserRole;

public class UpdateAdminUserRoleCommandHandler : IRequestHandler<UpdateAdminUserRoleCommand>
{
    private readonly IGisDbContext _context;

    public UpdateAdminUserRoleCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task Handle(UpdateAdminUserRoleCommand request, CancellationToken ct)
    {
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == request.UserId, ct)
            ?? throw new NotFoundException("Utilisateur", request.UserId);

        var role = await _context.Roles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == request.RoleId && r.SocieteId == user.CompanyId, ct)
            ?? throw new DomainException("Rôle invalide ou n'appartient pas à cette société");

        user.RoleId = request.RoleId;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
    }
}
