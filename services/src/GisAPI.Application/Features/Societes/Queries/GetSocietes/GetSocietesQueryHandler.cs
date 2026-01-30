using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Societes.Queries.GetSocietes;

public class GetSocietesQueryHandler : IRequestHandler<GetSocietesQuery, SocietesListResponse>
{
    private readonly IGisDbContext _context;

    public GetSocietesQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<SocietesListResponse> Handle(GetSocietesQuery request, CancellationToken ct)
    {
        var query = _context.Societes.AsQueryable();

        // Search filter
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.ToLower();
            query = query.Where(s => 
                s.Name.ToLower().Contains(search) ||
                (s.Email != null && s.Email.ToLower().Contains(search)) ||
                (s.Phone != null && s.Phone.Contains(search)));
        }

        // Status filter
        if (!string.IsNullOrWhiteSpace(request.Status) && request.Status != "all")
        {
            if (request.Status == "active")
                query = query.Where(s => s.IsActive && s.SubscriptionStatus == "active");
            else if (request.Status == "suspended")
                query = query.Where(s => !s.IsActive || s.SubscriptionStatus == "suspended");
            else if (request.Status == "expired")
                query = query.Where(s => s.SubscriptionStatus == "expired");
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(s => new SocieteDto(
                s.Id,
                s.Name,
                s.Type,
                s.Description,
                s.Address,
                s.City,
                s.Country,
                s.Phone,
                s.Email,
                s.LogoUrl,
                s.IsActive,
                s.SubscriptionStatus,
                s.BillingCycle,
                s.SubscriptionStartedAt,
                s.SubscriptionExpiresAt,
                s.SubscriptionTypeId,
                s.SubscriptionType != null ? s.SubscriptionType.Name : null,
                s.Users.Count,
                s.Vehicles.Count,
                s.Roles.Count,
                s.CreatedAt,
                s.UpdatedAt
            ))
            .ToListAsync(ct);

        return new SocietesListResponse(items, totalCount, request.Page, request.PageSize);
    }
}



