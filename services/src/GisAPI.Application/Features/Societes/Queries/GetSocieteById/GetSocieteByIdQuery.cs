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
    DateTime UpdatedAt,
    // Quota mensuel de scans de factures IA — null = défaut plateforme (20),
    // 0 = désactivé. Paramètres avec défauts : les handlers Create/Update
    // construisent ce DTO sans ces infos, l'admin UI recharge via GET.
    int? InvoiceScanMonthlyLimit = null,
    int InvoiceScanUsedThisMonth = 0
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



