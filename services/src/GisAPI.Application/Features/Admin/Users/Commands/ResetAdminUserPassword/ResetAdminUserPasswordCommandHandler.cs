using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Users.Commands.ResetAdminUserPassword;

public class ResetAdminUserPasswordCommandHandler : IRequestHandler<ResetAdminUserPasswordCommand>
{
    private readonly IGisDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentTenantService _tenant;

    public ResetAdminUserPasswordCommandHandler(IGisDbContext context, IPasswordHasher passwordHasher, ICurrentTenantService tenant)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _tenant = tenant;
    }

    public async Task Handle(ResetAdminUserPasswordCommand request, CancellationToken ct)
    {
        // Administrateur restreint : jamais le mot de passe d'un compte à rôle système.
        await AdminScreenAccess.EnsureCallerMayManageAsync(_context, _tenant.UserId, request.UserId, null, ct);

        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == request.UserId, ct)
            ?? throw new NotFoundException("Utilisateur", request.UserId);

        user.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
    }
}
