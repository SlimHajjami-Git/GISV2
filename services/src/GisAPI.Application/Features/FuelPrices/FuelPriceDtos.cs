namespace GisAPI.Application.Features.FuelPrices;

public record FuelPriceDto(
    int Id,
    int FuelTypeId,
    string FuelTypeCode,
    string FuelTypeName,
    decimal PricePerLiter,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record FuelTypeDto(
    int Id,
    string Code,
    string Name,
    bool IsSystem
);

public record FuelPriceStatsDto(
    int TotalPrices,
    int ActivePrices,
    Dictionary<string, decimal> CurrentPricesByType,
    Dictionary<string, List<PriceHistoryPoint>> PriceHistory
);

public record PriceHistoryPoint(
    DateTime Date,
    decimal Price
);

public record ImportResultDto(
    int TotalRows,
    int SuccessfulImports,
    int FailedImports,
    List<string> Errors
);
