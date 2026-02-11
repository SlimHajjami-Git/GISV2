using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Users.Queries.GetAdminUserById;

public class GetAdminUserByIdQueryHandler : IRequestHandler<GetAdminUserByIdQuery, AdminUserDto>
{
    private readonly IGisDbContext _context;

    public GetAdminUserByIdQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<AdminUserDto> Handle(GetAdminUserByIdQuery request, CancellationToken ct)
    {
        var onlineThreshold = DateTime.UtcNow.AddMinutes(-15);

        var user = await _context.Users
            .IgnoreQueryFilters()
            .Include(u => u.Societe)
            .Include(u => u.Role)
            .Where(u => u.Id == request.Id)
            .Select(u => new AdminUserDto
            {
                Id = u.Id,
                Name = u.FullName,
                Email = u.Email,
                Phone = u.Phone,
                CompanyId = u.CompanyId,
                CompanyName = u.Societe != null ? u.Societe.Name : null,
                RoleId = u.RoleId,
                RoleName = u.Role != null ? u.Role.Name : null,
                Status = u.Status,
                LastLoginAt = u.LastLoginAt,
                CreatedAt = u.CreatedAt,
                IsOnline = u.LastLoginAt.HasValue && u.LastLoginAt.Value > onlineThreshold
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Utilisateur", request.Id);

        return user;
    }
}
