using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Users.Commands.DeleteAdminUser;

public class DeleteAdminUserCommandHandler : IRequestHandler<DeleteAdminUserCommand>
{
    private readonly IGisDbContext _context;

    public DeleteAdminUserCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task Handle(DeleteAdminUserCommand request, CancellationToken ct)
    {
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == request.Id, ct)
            ?? throw new NotFoundException("Utilisateur", request.Id);

        // Remove vehicle assignments first
        var assignments = await _context.UserVehicles
            .IgnoreQueryFilters()
            .Where(uv => uv.UserId == request.Id)
            .ToListAsync(ct);
        _context.UserVehicles.RemoveRange(assignments);

        _context.Users.Remove(user);
        await _context.SaveChangesAsync(ct);
    }
}
