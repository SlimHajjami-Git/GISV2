using MediatR;

namespace GisAPI.Application.Features.Admin.Companies.Commands.UpdateCompany;

public record UpdateCompanyCommand(
    int Id,
    string? Name,
    string? Email,
    string? Phone,
    string? Type,
    int? SubscriptionId
) : IRequest<AdminCompanyDto?>;
