using MediatR;

namespace GisAPI.Application.Features.FuelPrices.Queries;

public record GetCurrentFuelPricesQuery() : IRequest<List<FuelPriceDto>>;
