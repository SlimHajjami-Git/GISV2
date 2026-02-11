using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Users.Queries.GetAdminUsers;

public class GetAdminUsersQueryHandler : IRequestHandler<GetAdminUsersQuery, List<AdminUserDto>>
{
    private readonly IGisDbContext _context;

    public GetAdminUsersQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<List<AdminUserDto>> Handle(GetAdminUsersQuery request, CancellationToken ct)
    {
        var query = _context.Users
            .IgnoreQueryFilters()
            .Include(u => u.Societe)
            .Include(u => u.Role)
            .AsQueryable();

        if (!string.IsNullOrEmpty(request.Search))
        {
            var search = request.Search.ToLower();
            query = query.Where(u =>
                u.FirstName.ToLower().Contains(search) ||
                u.LastName.ToLower().Contains(search) ||
                u.Email.ToLower().Contains(search) ||
                (u.Societe != null && u.Societe.Name.ToLower().Contains(search)));
        }

        if (!string.IsNullOrEmpty(request.Status) && request.Status != "all")
        {
            query = query.Where(u => u.Status == request.Status);
        }

        if (request.CompanyId.HasValue)
        {
            query = query.Where(u => u.CompanyId == request.CompanyId.Value);
        }

        var onlineThreshold = DateTime.UtcNow.AddMinutes(-15);

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
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
            .ToListAsync(ct);

        return users;
    }
}
