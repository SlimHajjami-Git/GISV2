using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.FuelPrices.Commands;

public record CreateFuelPriceCommand(
    int FuelTypeId,
    decimal PricePerLiter,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo
) : ICommand<int>;
