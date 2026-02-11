using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Companies.Commands.UpdateCompany;

public class UpdateCompanyCommandHandler : IRequestHandler<UpdateCompanyCommand, AdminCompanyDto?>
{
    private readonly IGisDbContext _context;

    public UpdateCompanyCommandHandler(IGisDbContext context) => _context = context;

    public async Task<AdminCompanyDto?> Handle(UpdateCompanyCommand request, CancellationToken ct)
    {
        var company = await _context.Societes
            .Include(c => c.SubscriptionType)
            .Include(c => c.Vehicles)
            .Include(c => c.Users)
            .FirstOrDefaultAsync(c => c.Id == request.Id, ct);

        if (company == null) return null;

        if (!string.IsNullOrEmpty(request.Name)) company.Name = request.Name;
        if (request.Email != null) company.Email = request.Email;
        if (request.Phone != null) company.Phone = request.Phone;
        if (request.Type != null) company.Type = request.Type;
        if (request.SubscriptionId.HasValue) company.SubscriptionTypeId = request.SubscriptionId.Value;

        company.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return new AdminCompanyDto
        {
            Id = company.Id,
            Name = company.Name,
            Email = company.Email,
            Phone = company.Phone,
            Type = company.Type,
            SubscriptionId = company.SubscriptionTypeId,
            SubscriptionName = company.SubscriptionType?.Name,
            MaxVehicles = company.SubscriptionType?.MaxVehicles ?? 0,
            CurrentVehicles = company.Vehicles?.Count ?? 0,
            CurrentUsers = company.Users?.Count ?? 0,
            Status = company.IsActive ? "active" : "suspended",
            CreatedAt = company.CreatedAt,
            LastActivity = company.UpdatedAt
        };
    }
}
