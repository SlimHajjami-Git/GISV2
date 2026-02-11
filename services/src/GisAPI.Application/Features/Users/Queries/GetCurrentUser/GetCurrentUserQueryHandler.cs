using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Users.Queries.GetCurrentUser;

public class GetCurrentUserQueryHandler : IRequestHandler<GetCurrentUserQuery, UserListDto>
{
    private readonly IGisDbContext _context;

    public GetCurrentUserQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<UserListDto> Handle(GetCurrentUserQuery request, CancellationToken ct)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.UserVehicles)
            .Where(u => u.Id == request.UserId)
            .Select(u => new UserListDto(
                u.Id,
                u.FullName,
                u.Email,
                u.Phone,
                u.RoleId,
                u.Role != null ? u.Role.Name : null,
                u.Role != null && u.Role.IsCompanyAdmin,
                u.Status,
                u.CreatedAt,
                u.LastLoginAt,
                u.UserVehicles.Select(uv => uv.VehicleId).ToArray()
            ))
            .FirstOrDefaultAsync(ct);

        if (user == null)
            throw new NotFoundException("Utilisateur", request.UserId);

        return user;
    }
}
