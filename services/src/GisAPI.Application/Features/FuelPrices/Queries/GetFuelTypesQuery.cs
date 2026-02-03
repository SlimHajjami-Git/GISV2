using MediatR;

namespace GisAPI.Application.Features.FuelPrices.Queries;

public record GetFuelTypesQuery() : IRequest<List<FuelTypeDto>>;
