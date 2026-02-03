using GisAPI.Application.Common.Models;
using MediatR;

namespace GisAPI.Application.Features.FuelEntries.Queries;

public record GetFuelEntriesQuery(
    int? FuelTypeId,
    string? VehiclePlate,
    DateTime? StartDate,
    DateTime? EndDate,
    int Page = 1,
    int PageSize = 50
) : IRequest<PaginatedList<FuelEntryDto>>;
