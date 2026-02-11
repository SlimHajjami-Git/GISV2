using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Companies.Queries.GetCompanyById;

public class GetCompanyByIdQueryHandler : IRequestHandler<GetCompanyByIdQuery, AdminCompanyDto?>
{
    private readonly IGisDbContext _context;

    public GetCompanyByIdQueryHandler(IGisDbContext context) => _context = context;

    public async Task<AdminCompanyDto?> Handle(GetCompanyByIdQuery request, CancellationToken ct)
    {
        return await _context.Societes
            .Include(c => c.SubscriptionType)
            .Include(c => c.Vehicles)
            .Include(c => c.Users)
            .Where(c => c.Id == request.Id)
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
            .FirstOrDefaultAsync(ct);
    }
}
