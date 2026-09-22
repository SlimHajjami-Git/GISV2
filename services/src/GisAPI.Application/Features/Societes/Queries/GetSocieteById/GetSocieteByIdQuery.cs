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
    // ANCIEN quota de scans IA, en nombre de scans (lu seulement si le crédit en jetons
    // n'est pas réglé). Paramètres avec défauts : les handlers Create/Update
    // construisent ce DTO sans ces infos, l'admin UI recharge via GET.
    int? InvoiceScanMonthlyLimit = null,
    // Nombre de scans réussis ce mois-ci (information, ne limite plus rien).
    int InvoiceScanUsedThisMonth = 0,
    // Suspension automatique à l'expiration (grâce 7 j). false = jamais
    // bloquée automatiquement, seule la suspension manuelle coupe.
    bool AutoSuspendEnabled = true,
    // ── Crédit IA mensuel de la société, en jetons (22/09/2026) — toute l'IA ────
    // Réglage brut : null = défaut plateforme (60 000), 0 = désactivé.
    int? InvoiceScanMonthlyTokens = null,
    // Budget EFFECTIF du mois (réglage, ou ancien quota converti, ou défaut).
    int InvoiceScanBudgetTokens = 0,
    // Jetons consommés depuis le 1er du mois, et part du budget (0-100, arrondi bas).
    int InvoiceScanUsedTokens = 0,
    int InvoiceScanPercentUsed = 0,
    // Prochaine recharge (1er du mois suivant, UTC) — null hors lecture GET.
    DateTime? InvoiceScanResetsAt = null,
    // Ventilation du mois par fonction (scans, assistant, rapports IA…), en jetons :
    // le crédit couvre toute l'IA de la société depuis le 22/09/2026. null hors lecture GET.
    IReadOnlyDictionary<string, int>? AiCreditByFeature = null
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



