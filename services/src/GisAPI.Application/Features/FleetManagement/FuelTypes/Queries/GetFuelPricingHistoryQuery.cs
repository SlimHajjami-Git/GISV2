using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.FleetManagement.FuelTypes.Queries;

public record GetFuelPricingHistoryQuery(int FuelTypeId) : IQuery<List<FuelPricingDto>>;

public record FuelPricingDto(
    int Id,
    int FuelTypeId,
    string FuelTypeName,
    decimal PricePerLiter,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    bool IsActive,
    DateTime CreatedAt
);



