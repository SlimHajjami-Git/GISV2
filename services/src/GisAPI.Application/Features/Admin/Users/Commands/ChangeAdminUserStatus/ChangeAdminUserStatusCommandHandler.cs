using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Users.Commands.ChangeAdminUserStatus;

public class ChangeAdminUserStatusCommandHandler : IRequestHandler<ChangeAdminUserStatusCommand>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public ChangeAdminUserStatusCommandHandler(IGisDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task Handle(ChangeAdminUserStatusCommand request, CancellationToken ct)
    {
        // Administrateur restreint : jamais suspendre ni réactiver un compte à rôle système.
        await AdminScreenAccess.EnsureCallerMayManageAsync(_context, _tenant.UserId, request.UserId, null, ct);

        var user = await _context.Users
            .IgnoreQueryFilters()
            .Include(u => u.Societe)
            .FirstOrDefaultAsync(u => u.Id == request.UserId, ct)
            ?? throw new NotFoundException("Utilisateur", request.UserId);

        if (request.Status == "active" && user.Societe != null && !user.Societe.IsActive)
            throw new DomainException("Impossible d'activer un utilisateur d'une société suspendue");

        user.Status = request.Status;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
    }
}
