using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.FleetManagement.FuelTypes.Commands;

public record SetFuelPriceCommand(
    int FuelTypeId,
    decimal PricePerLiter,
    DateTime? EffectiveFrom = null
) : ICommand<int>;



