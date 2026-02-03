using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.FuelPrices.Commands;

public record UpdateFuelPriceCommand(
    int Id,
    int FuelTypeId,
    decimal PricePerLiter,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    bool IsActive
) : ICommand<bool>;
