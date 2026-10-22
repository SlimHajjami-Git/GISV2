using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Users.Commands.UpdateAdminUserRole;

public class UpdateAdminUserRoleCommandHandler : IRequestHandler<UpdateAdminUserRoleCommand>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public UpdateAdminUserRoleCommandHandler(IGisDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task Handle(UpdateAdminUserRoleCommand request, CancellationToken ct)
    {
        // Administrateur restreint : ni un compte à rôle système, ni un rôle système.
        await AdminScreenAccess.EnsureCallerMayManageAsync(_context, _tenant.UserId, request.UserId, request.RoleId, ct);

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
