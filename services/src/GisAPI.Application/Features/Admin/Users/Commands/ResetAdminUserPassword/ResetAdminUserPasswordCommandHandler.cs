using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Users.Commands.ResetAdminUserPassword;

public class ResetAdminUserPasswordCommandHandler : IRequestHandler<ResetAdminUserPasswordCommand>
{
    private readonly IGisDbContext _context;
    private readonly IPasswordHasher _passwordHasher;

    public ResetAdminUserPasswordCommandHandler(IGisDbContext context, IPasswordHasher passwordHasher)
    {
        _context = context;
        _passwordHasher = passwordHasher;
    }

    public async Task Handle(ResetAdminUserPasswordCommand request, CancellationToken ct)
    {
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == request.UserId, ct)
            ?? throw new NotFoundException("Utilisateur", request.UserId);

        user.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
    }
}
