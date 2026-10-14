using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Models;

namespace GisAPI.Application.Features.Documents.Queries;

public record GetExpiriesQuery(
    string? DocumentType = null,
    string? Status = null,
    int? VehicleId = null,
    int Page = 1,
    int PageSize = 100
) : IQuery<PaginatedList<VehicleExpiryDto>>;

public record VehicleExpiryDto(
    int VehicleId,
    string VehicleName,
    string? VehiclePlate,
    string DocumentType,
    DateTime? ExpiryDate,
    string Status,
    int DaysUntilExpiry,
    DateTime? LastRenewalDate,
    decimal? LastRenewalCost,
    string? DocumentNumber,
    // « Rappel avant (jours) » de l'échéance (migration 059) : seuil de « bientôt » et
    // valeur proposée par la fenêtre de renouvellement.
    int ReminderDays = ExpiryCalendar.ExpiringSoonDays
);

public record ExpiryStatsDto(
    int ExpiredCount,
    int ExpiringSoonCount,
    int OkCount,
    int TotalCount
);



