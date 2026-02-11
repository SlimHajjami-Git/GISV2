using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Companies.Queries.GetCompanies;

public class GetCompaniesQueryHandler : IRequestHandler<GetCompaniesQuery, List<AdminCompanyDto>>
{
    private readonly IGisDbContext _context;

    public GetCompaniesQueryHandler(IGisDbContext context) => _context = context;

    public async Task<List<AdminCompanyDto>> Handle(GetCompaniesQuery request, CancellationToken ct)
    {
        var query = _context.Societes
            .Include(c => c.SubscriptionType)
            .Include(c => c.Vehicles)
            .Include(c => c.Users)
            .AsQueryable();

        if (!string.IsNullOrEmpty(request.Search))
            query = query.Where(c => c.Name.Contains(request.Search) || (c.Email != null && c.Email.Contains(request.Search)));

        if (!string.IsNullOrEmpty(request.Status) && request.Status != "all")
        {
            query = request.Status switch
            {
                "active" => query.Where(c => c.IsActive),
                "suspended" => query.Where(c => !c.IsActive),
                _ => query
            };
        }

        return await query
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new AdminCompanyDto
            {
                Id = c.Id,
                Name = c.Name,
                Email = c.Email,
                Phone = c.Phone,
                Type = c.Type,
                SubscriptionId = c.SubscriptionTypeId,
                SubscriptionName = c.SubscriptionType != null ? c.SubscriptionType.Name : null,
                MaxVehicles = c.SubscriptionType != null ? c.SubscriptionType.MaxVehicles : 0,
                CurrentVehicles = c.Vehicles != null ? c.Vehicles.Count : 0,
                CurrentUsers = c.Users != null ? c.Users.Count : 0,
                Status = c.IsActive ? "active" : "suspended",
                CreatedAt = c.CreatedAt,
                LastActivity = c.UpdatedAt
            })
            .ToListAsync(ct);
    }
}
