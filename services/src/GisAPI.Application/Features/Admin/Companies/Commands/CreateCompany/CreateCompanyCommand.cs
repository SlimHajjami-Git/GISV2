using MediatR;

namespace GisAPI.Application.Features.Admin.Companies.Commands.CreateCompany;

public record CreateCompanyCommand(
    string Name,
    string? Email,
    string? Phone,
    string? Type,
    int SubscriptionId,
    string? BillingCycle,
    string? AdminName,
    string? AdminEmail,
    string? AdminPassword
) : IRequest<AdminCompanyDto>;
