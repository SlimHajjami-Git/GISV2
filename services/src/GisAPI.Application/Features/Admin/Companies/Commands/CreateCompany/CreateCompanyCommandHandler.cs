using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Admin.Companies.Commands.CreateCompany;

public class CreateCompanyCommandHandler : IRequestHandler<CreateCompanyCommand, AdminCompanyDto>
{
    private readonly IGisDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<CreateCompanyCommandHandler> _logger;

    public CreateCompanyCommandHandler(IGisDbContext context, IPasswordHasher passwordHasher, ILogger<CreateCompanyCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<AdminCompanyDto> Handle(CreateCompanyCommand request, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(request.Email) && await _context.Societes.AnyAsync(c => c.Email == request.Email, ct))
            throw new GisAPI.Domain.Exceptions.DomainException("Une société avec cet email existe déjà");

        var subscriptionType = await _context.SubscriptionTypes
            .FirstOrDefaultAsync(s => s.Id == request.SubscriptionId, ct);

        var billingCycle = request.BillingCycle ?? "yearly";
        var startDate = DateTime.UtcNow;
        var durationDays = billingCycle switch
        {
            "monthly" => subscriptionType?.MonthlyDurationDays ?? 30,
            "quarterly" => subscriptionType?.QuarterlyDurationDays ?? 90,
            "yearly" => subscriptionType?.YearlyDurationDays ?? 365,
            _ => 365
        };
        var expiresAt = startDate.AddDays(durationDays);
        var price = billingCycle switch
        {
            "monthly" => subscriptionType?.MonthlyPrice ?? 0,
            "quarterly" => subscriptionType?.QuarterlyPrice ?? 0,
            "yearly" => subscriptionType?.YearlyPrice ?? 0,
            _ => subscriptionType?.YearlyPrice ?? 0
        };

        var company = new Societe
        {
            Name = request.Name,
            Email = request.Email,
            Phone = request.Phone,
            Type = request.Type ?? "transport",
            SubscriptionTypeId = request.SubscriptionId > 0 ? request.SubscriptionId : null,
            IsActive = true,
            SubscriptionStartedAt = startDate,
            SubscriptionExpiresAt = expiresAt,
            BillingCycle = billingCycle,
            SubscriptionStatus = "active",
            NextPaymentAmount = price,
            Settings = new SocieteSettings
            {
                Currency = GisAPI.Domain.Common.AppCurrency.Default,
                Timezone = "Africa/Tunis",
                Language = "fr"
            }
        };

        _context.Societes.Add(company);
        await _context.SaveChangesAsync(ct);

        var adminUserCreated = false;

        if (!string.IsNullOrEmpty(request.AdminEmail) && !string.IsNullOrEmpty(request.AdminPassword))
        {
            var adminRole = new Role
            {
                Name = "Administrateur",
                Description = "Administrateur de la société",
                SocieteId = company.Id,
                IsCompanyAdmin = true,
                IsSystemRole = false,
                Permissions = new Dictionary<string, object>
                {
                    { "dashboard", true }, { "monitoring", true }, { "vehicles", true },
                    { "employees", true }, { "maintenance", true }, { "costs", true },
                    { "reports", true }, { "geofences", true }, { "settings", true },
                    { "users", true }, { "suppliers", true }, { "documents", true },
                    { "accidents", true }, { "fleet_management", true }
                }
            };
            _context.Roles.Add(adminRole);
            await _context.SaveChangesAsync(ct);

            var adminUser = new User
            {
                FirstName = request.AdminName?.Split(' ').FirstOrDefault() ?? "Admin",
                LastName = request.AdminName?.Split(' ').Skip(1).FirstOrDefault() ?? company.Name,
                Email = request.AdminEmail,
                Phone = request.Phone,
                PasswordHash = _passwordHasher.HashPassword(request.AdminPassword),
                CompanyId = company.Id,
                RoleId = adminRole.Id,
                Status = "active"
            };
            _context.Users.Add(adminUser);
            await _context.SaveChangesAsync(ct);
            adminUserCreated = true;
        }

        _logger.LogInformation("Company created: {CompanyName} (ID: {CompanyId})", company.Name, company.Id);

        return new AdminCompanyDto
        {
            Id = company.Id,
            Name = company.Name,
            Email = company.Email,
            Phone = company.Phone,
            Type = company.Type,
            SubscriptionId = company.SubscriptionTypeId,
            SubscriptionName = subscriptionType?.Name,
            MaxVehicles = subscriptionType?.MaxVehicles ?? 0,
            CurrentVehicles = 0,
            CurrentUsers = adminUserCreated ? 1 : 0,
            Status = "active",
            CreatedAt = company.CreatedAt,
            SubscriptionStatus = company.SubscriptionStatus,
            SubscriptionStartedAt = company.SubscriptionStartedAt,
            SubscriptionExpiresAt = company.SubscriptionExpiresAt,
            BillingCycle = company.BillingCycle
        };
    }
}
