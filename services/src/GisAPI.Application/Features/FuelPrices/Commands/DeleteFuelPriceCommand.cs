using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.FuelPrices.Commands;

public record DeleteFuelPriceCommand(int Id) : ICommand<bool>;
