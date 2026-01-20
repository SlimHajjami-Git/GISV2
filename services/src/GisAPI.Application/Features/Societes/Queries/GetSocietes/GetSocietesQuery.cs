using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Societes.Queries.GetSocietes;

public record GetSocietesQuery(
    string? Search,
    string? Status,
    int Page = 1,
    int PageSize = 20
) : IQuery<SocietesListResponse>;

public record SocietesListResponse(
    List<SocieteDto> Items,
    int TotalCount,
    int Page,
    int PageSize
);

public record SocieteDto(
    int Id,
    string Name,
    string Type,
    string? Description,
    string? Address,
    string? City,
    string Country,
    string? Phone,
    string? Email,
    string? LogoUrl,
    bool IsActive,
    string SubscriptionStatus,
    string BillingCycle,
    DateTime SubscriptionStartedAt,
    DateTime? SubscriptionExpiresAt,
    int? SubscriptionTypeId,
    string? SubscriptionTypeName,
    int UsersCount,
    int VehiclesCount,
    int RolesCount,
    DateTime CreatedAt,
    DateTime UpdatedAt
);
