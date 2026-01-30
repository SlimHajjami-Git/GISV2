using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Societes.Queries.GetSocietes;

namespace GisAPI.Application.Features.Societes.Queries.GetSocieteById;

public record GetSocieteByIdQuery(int Id) : IQuery<SocieteDetailDto>;

public record SocieteDetailDto(
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
    string? TaxId,
    string? RC,
    string? IF,
    bool IsActive,
    string SubscriptionStatus,
    string BillingCycle,
    DateTime SubscriptionStartedAt,
    DateTime? SubscriptionExpiresAt,
    DateTime? LastPaymentAt,
    decimal? NextPaymentAmount,
    int? SubscriptionTypeId,
    string? SubscriptionTypeName,
    SocieteSettingsDto? Settings,
    int UsersCount,
    int VehiclesCount,
    int GpsDevicesCount,
    int GeofencesCount,
    int RolesCount,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record SocieteSettingsDto(
    string Currency,
    string Timezone,
    string Language,
    string DateFormat,
    string DistanceUnit,
    string SpeedUnit,
    string VolumeUnit
);



