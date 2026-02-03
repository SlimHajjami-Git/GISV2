using GisAPI.Application.Common.Models;
using MediatR;

namespace GisAPI.Application.Features.FuelPrices.Queries;

public record GetFuelPricesQuery(
    int? FuelTypeId = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 50
) : IRequest<PaginatedList<FuelPriceDto>>;
