using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.FleetManagement.FuelTypes.Queries;

public record GetFuelTypesQuery() : IQuery<List<FuelTypeDto>>;

public record FuelTypeDto(
    int Id,
    string Code,
    string Name,
    bool IsSystem,
    decimal? CurrentPrice,
    DateTime? PriceEffectiveFrom
);



