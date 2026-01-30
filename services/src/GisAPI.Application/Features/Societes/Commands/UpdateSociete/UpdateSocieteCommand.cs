using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Societes.Queries.GetSocieteById;
using GisAPI.Domain.Entities;

namespace GisAPI.Application.Features.Societes.Commands.UpdateSociete;

public record UpdateSocieteCommand(
    int Id,
    string? Name,
    string? Type,
    string? Description,
    string? Address,
    string? City,
    string? Country,
    string? Phone,
    string? Email,
    string? LogoUrl,
    string? TaxId,
    string? RC,
    string? IF,
    bool? IsActive,
    string? SubscriptionStatus,
    string? BillingCycle,
    int? SubscriptionTypeId,
    SocieteSettings? Settings
) : ICommand<SocieteDetailDto>;



