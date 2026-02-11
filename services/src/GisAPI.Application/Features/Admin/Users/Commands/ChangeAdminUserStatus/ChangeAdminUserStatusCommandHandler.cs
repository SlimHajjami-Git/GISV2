using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Users.Commands.ChangeAdminUserStatus;

public class ChangeAdminUserStatusCommandHandler : IRequestHandler<ChangeAdminUserStatusCommand>
{
    private readonly IGisDbContext _context;

    public ChangeAdminUserStatusCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task Handle(ChangeAdminUserStatusCommand request, CancellationToken ct)
    {
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
