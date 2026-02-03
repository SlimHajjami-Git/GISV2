using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.FuelPrices.Commands;

public record ImportFuelPricesCommand(
    List<FuelPriceImportRow> Rows
) : ICommand<ImportResultDto>;

public record FuelPriceImportRow(
    string FuelTypeCode,
    decimal PricePerLiter,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo
);
